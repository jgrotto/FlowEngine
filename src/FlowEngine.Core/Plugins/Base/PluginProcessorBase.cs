using FlowEngine.Abstractions.Data;
using Microsoft.Extensions.Logging;
using System.Threading.Channels;

namespace FlowEngine.Core.Plugins.Base;

/// <summary>
/// Base class for all plugin processors in the five-component architecture.
/// Provides data flow orchestration, chunked processing, and performance optimization.
/// </summary>
/// <remarks>
/// This base class implements the Processor component of the five-component pattern:
/// Configuration → Validator → Plugin → Processor → Service
/// 
/// Key Responsibilities:
/// - Orchestrate data flow between input and output ports
/// - Manage chunked processing for memory efficiency
/// - Coordinate with Service component for actual data processing
/// - Handle backpressure and flow control
/// - Provide performance monitoring and metrics
/// 
/// Performance Requirements:
/// - Support >200K rows/sec throughput for ArrayRow processing
/// - Memory usage must remain bounded regardless of dataset size
/// - Chunking must prevent memory pressure while maintaining performance
/// - Error handling must not impact overall pipeline throughput
/// 
/// The Processor acts as the data flow coordinator, managing the movement
/// of data through the plugin while delegating actual processing to the Service.
/// </remarks>
/// <typeparam name="TConfiguration">Type of configuration this processor uses</typeparam>
/// <typeparam name="TService">Type of service this processor coordinates with</typeparam>
public abstract class PluginProcessorBase<TConfiguration, TService> : IAsyncDisposable
    where TConfiguration : PluginConfigurationBase
    where TService : PluginServiceBase<TConfiguration>
{
    /// <summary>
    /// Logger for processor operations and diagnostics.
    /// </summary>
    protected readonly ILogger Logger;

    /// <summary>
    /// Plugin configuration instance.
    /// </summary>
    protected TConfiguration Configuration { get; private set; }

    /// <summary>
    /// Service component for actual data processing.
    /// </summary>
    protected TService Service { get; private set; }

    /// <summary>
    /// Current processor state.
    /// </summary>
    public ProcessorState State { get; private set; } = ProcessorState.Created;

    /// <summary>
    /// Input channel for receiving data chunks.
    /// </summary>
    protected Channel<DataChunk>? InputChannel { get; private set; }

    /// <summary>
    /// Output channel for sending processed data chunks.
    /// </summary>
    protected Channel<DataChunk>? OutputChannel { get; private set; }

    /// <summary>
    /// Gets the current processing metrics.
    /// </summary>
    public abstract ProcessorMetrics GetMetrics();

    /// <summary>
    /// Gets whether this processor can handle the current service.
    /// </summary>
    /// <param name="service">Service to check compatibility</param>
    /// <returns>True if processor is compatible with the service</returns>
    public abstract bool IsServiceCompatible(TService service);

    private readonly object _stateLock = new();
    private readonly CancellationTokenSource _processingCancellation = new();
    private Task? _processingTask;
    private bool _disposed;

    // Performance tracking
    private long _totalProcessed;
    private long _errorCount;
    private readonly object _metricsLock = new();
    private DateTimeOffset _lastProcessedAt = DateTimeOffset.UtcNow;

    /// <summary>
    /// Initializes a new instance of the PluginProcessorBase class.
    /// </summary>
    /// <param name="configuration">Plugin configuration</param>
    /// <param name="service">Service component for data processing</param>
    /// <param name="logger">Logger for processor operations</param>
    protected PluginProcessorBase(
        TConfiguration configuration,
        TService service,
        ILogger logger)
    {
        Configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        Service = service ?? throw new ArgumentNullException(nameof(service));
        Logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Initializes the processor with channel setup and validation.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token for initialization</param>
    /// <returns>Task representing the initialization operation</returns>
    /// <exception cref="ProcessorInitializationException">Processor initialization failed</exception>
    /// <remarks>
    /// Initialization process:
    /// 1. Validate service compatibility
    /// 2. Set up input and output channels
    /// 3. Configure processing parameters
    /// 4. Prepare for data flow
    /// 5. Transition to Initialized state
    /// </remarks>
    public virtual async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        
        lock (_stateLock)
        {
            if (State != ProcessorState.Created)
            {
                throw new InvalidOperationException($"Processor cannot be initialized from state {State}");
            }
            
            State = ProcessorState.Initializing;
        }

        try
        {
            Logger.LogInformation("Initializing processor for plugin {PluginId}", Configuration.PluginId);

            // 1. Validate service compatibility
            if (!IsServiceCompatible(Service))
            {
                throw new ProcessorInitializationException("Service is not compatible with this processor");
            }

            // 2. Set up data flow channels
            await SetupChannelsAsync(cancellationToken);

            // 3. Configure processing parameters
            await ConfigureProcessingAsync(cancellationToken);

            // 4. Initialize processor-specific components
            await InitializeProcessorSpecificAsync(cancellationToken);

            lock (_stateLock)
            {
                State = ProcessorState.Initialized;
            }

            Logger.LogInformation("Processor initialized successfully for plugin {PluginId}", Configuration.PluginId);
        }
        catch (Exception ex)
        {
            lock (_stateLock)
            {
                State = ProcessorState.Failed;
            }

            Logger.LogError(ex, "Failed to initialize processor for plugin {PluginId}", Configuration.PluginId);
            throw new ProcessorInitializationException($"Processor initialization failed: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Starts the processor and begins data processing operations.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token for start operation</param>
    /// <returns>Task representing the start operation</returns>
    /// <exception cref="InvalidOperationException">Processor not in correct state for starting</exception>
    /// <remarks>
    /// Start process:
    /// 1. Verify initialized state
    /// 2. Start processing task
    /// 3. Begin monitoring data flow
    /// 4. Transition to Running state
    /// 
    /// The processor will continuously process data chunks until stopped.
    /// </remarks>
    public virtual async Task StartAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        
        lock (_stateLock)
        {
            if (State != ProcessorState.Initialized && State != ProcessorState.Stopped)
            {
                throw new InvalidOperationException($"Processor cannot be started from state {State}");
            }
            
            State = ProcessorState.Starting;
        }

        try
        {
            Logger.LogInformation("Starting processor for plugin {PluginId}", Configuration.PluginId);

            // Start the main processing loop
            _processingTask = ProcessDataAsync(_processingCancellation.Token);

            // Start processor-specific operations
            await StartProcessorSpecificAsync(cancellationToken);

            lock (_stateLock)
            {
                State = ProcessorState.Running;
            }

            Logger.LogInformation("Processor started successfully for plugin {PluginId}", Configuration.PluginId);
        }
        catch (Exception ex)
        {
            lock (_stateLock)
            {
                State = ProcessorState.Failed;
            }

            Logger.LogError(ex, "Failed to start processor for plugin {PluginId}", Configuration.PluginId);
            throw;
        }
    }

    /// <summary>
    /// Stops the processor and suspends processing operations.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token for stop operation</param>
    /// <returns>Task representing the stop operation</returns>
    /// <remarks>
    /// Stop process:
    /// 1. Signal processing cancellation
    /// 2. Complete pending chunks
    /// 3. Stop processing task
    /// 4. Clean up resources
    /// 5. Transition to Stopped state
    /// </remarks>
    public virtual async Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed || State == ProcessorState.Stopped || State == ProcessorState.Failed)
            return;

        lock (_stateLock)
        {
            State = ProcessorState.Stopping;
        }

        try
        {
            Logger.LogInformation("Stopping processor for plugin {PluginId}", Configuration.PluginId);

            // Cancel processing operations
            _processingCancellation.Cancel();

            // Wait for processing task to complete
            if (_processingTask != null)
            {
                try
                {
                    await _processingTask.WaitAsync(cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    // Expected when cancelling
                }
            }

            // Stop processor-specific operations
            await StopProcessorSpecificAsync(cancellationToken);

            lock (_stateLock)
            {
                State = ProcessorState.Stopped;
            }

            Logger.LogInformation("Processor stopped successfully for plugin {PluginId}", Configuration.PluginId);
        }
        catch (Exception ex)
        {
            lock (_stateLock)
            {
                State = ProcessorState.Failed;
            }

            Logger.LogError(ex, "Failed to stop processor for plugin {PluginId}", Configuration.PluginId);
            throw;
        }
    }

    /// <summary>
    /// Updates the processor configuration with hot-swapping support.
    /// </summary>
    /// <param name="newConfiguration">New configuration to apply</param>
    /// <param name="cancellationToken">Cancellation token for update operation</param>
    /// <returns>Task representing the configuration update operation</returns>
    /// <exception cref="NotSupportedException">Processor does not support hot-swapping</exception>
    /// <remarks>
    /// Configuration update process:
    /// 1. Validate new configuration compatibility
    /// 2. Prepare for configuration change
    /// 3. Apply new configuration
    /// 4. Verify successful update
    /// </remarks>
    public virtual async Task UpdateConfigurationAsync(
        TConfiguration newConfiguration, 
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(newConfiguration);

        if (!Configuration.SupportsHotSwapping)
        {
            throw new NotSupportedException("Processor does not support hot-swapping");
        }

        Logger.LogInformation("Updating processor configuration for plugin {PluginId}", Configuration.PluginId);

        try
        {
            // Validate configuration compatibility
            if (!newConfiguration.IsCompatibleWith(Configuration.InputSchema))
            {
                throw new ArgumentException("New configuration is not compatible with current schema");
            }

            var oldConfiguration = Configuration;
            Configuration = newConfiguration;

            // Apply configuration changes
            await UpdateProcessorSpecificConfigurationAsync(oldConfiguration, newConfiguration, cancellationToken);

            Logger.LogInformation("Processor configuration updated successfully for plugin {PluginId}", Configuration.PluginId);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to update processor configuration for plugin {PluginId}", Configuration.PluginId);
            throw;
        }
    }

    /// <summary>
    /// Processes a single data chunk using the service component.
    /// </summary>
    /// <param name="chunk">Data chunk to process</param>
    /// <param name="cancellationToken">Cancellation token for processing</param>
    /// <returns>Processed data chunk</returns>
    /// <remarks>
    /// This method coordinates with the Service component to process data
    /// while maintaining performance metrics and error handling.
    /// </remarks>
    public virtual async Task<DataChunk> ProcessChunkAsync(DataChunk chunk, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(chunk);
        ThrowIfDisposed();

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            // Delegate actual processing to the service
            var processedChunk = await Service.ProcessChunkAsync(chunk, cancellationToken);

            // Update metrics
            lock (_metricsLock)
            {
                _totalProcessed += chunk.RowCount;
                _lastProcessedAt = DateTimeOffset.UtcNow;
            }

            Logger.LogTrace("Processed chunk with {RowCount} rows in {Duration}ms", 
                chunk.RowCount, stopwatch.ElapsedMilliseconds);

            return processedChunk;
        }
        catch (Exception ex)
        {
            lock (_metricsLock)
            {
                _errorCount++;
            }

            Logger.LogError(ex, "Error processing chunk with {RowCount} rows", chunk.RowCount);
            throw;
        }
        finally
        {
            stopwatch.Stop();
        }
    }

    /// <summary>
    /// Sets up input and output channels for data flow.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token for setup</param>
    /// <returns>Task representing the channel setup</returns>
    protected virtual async Task SetupChannelsAsync(CancellationToken cancellationToken)
    {
        // Configure channel options for optimal performance
        var channelOptions = new BoundedChannelOptions(1000) // Reasonable buffer size
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false,
            AllowSynchronousContinuations = false
        };

        InputChannel = Channel.CreateBounded<DataChunk>(channelOptions);
        OutputChannel = Channel.CreateBounded<DataChunk>(channelOptions);

        Logger.LogDebug("Channels configured for plugin {PluginId}", Configuration.PluginId);
        await Task.CompletedTask;
    }

    /// <summary>
    /// Configures processing parameters based on configuration.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token for configuration</param>
    /// <returns>Task representing the processing configuration</returns>
    protected virtual async Task ConfigureProcessingAsync(CancellationToken cancellationToken)
    {
        // Base configuration - override in derived classes for specific needs
        Logger.LogDebug("Processing parameters configured for plugin {PluginId}", Configuration.PluginId);
        await Task.CompletedTask;
    }

    /// <summary>
    /// Main processing loop that handles data flow between channels.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token for processing</param>
    /// <returns>Task representing the processing loop</returns>
    private async Task ProcessDataAsync(CancellationToken cancellationToken)
    {
        if (InputChannel == null || OutputChannel == null)
        {
            throw new InvalidOperationException("Channels not properly initialized");
        }

        var reader = InputChannel.Reader;
        var writer = OutputChannel.Writer;

        try
        {
            await foreach (var chunk in reader.ReadAllAsync(cancellationToken))
            {
                try
                {
                    var processedChunk = await ProcessChunkAsync(chunk, cancellationToken);
                    await writer.WriteAsync(processedChunk, cancellationToken);
                }
                catch (Exception ex)
                {
                    Logger.LogError(ex, "Error in processing loop for plugin {PluginId}", Configuration.PluginId);
                    
                    // Continue processing other chunks unless cancellation is requested
                    if (cancellationToken.IsCancellationRequested)
                        break;
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            Logger.LogInformation("Processing loop cancelled for plugin {PluginId}", Configuration.PluginId);
        }
        finally
        {
            writer.TryComplete();
        }
    }

    /// <summary>
    /// Performs processor-specific initialization logic.
    /// Override this method to implement custom initialization requirements.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token for initialization</param>
    /// <returns>Task representing the processor-specific initialization</returns>
    protected virtual async Task InitializeProcessorSpecificAsync(CancellationToken cancellationToken)
    {
        await Task.CompletedTask; // Base implementation does nothing
    }

    /// <summary>
    /// Performs processor-specific start logic.
    /// Override this method to implement custom start requirements.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token for start operation</param>
    /// <returns>Task representing the processor-specific start</returns>
    protected virtual async Task StartProcessorSpecificAsync(CancellationToken cancellationToken)
    {
        await Task.CompletedTask; // Base implementation does nothing
    }

    /// <summary>
    /// Performs processor-specific stop logic.
    /// Override this method to implement custom stop requirements.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token for stop operation</param>
    /// <returns>Task representing the processor-specific stop</returns>
    protected virtual async Task StopProcessorSpecificAsync(CancellationToken cancellationToken)
    {
        await Task.CompletedTask; // Base implementation does nothing
    }

    /// <summary>
    /// Performs processor-specific configuration update logic.
    /// Override this method to implement custom configuration update requirements.
    /// </summary>
    /// <param name="oldConfiguration">Previous configuration</param>
    /// <param name="newConfiguration">New configuration</param>
    /// <param name="cancellationToken">Cancellation token for update operation</param>
    /// <returns>Task representing the processor-specific configuration update</returns>
    protected virtual async Task UpdateProcessorSpecificConfigurationAsync(
        TConfiguration oldConfiguration, 
        TConfiguration newConfiguration, 
        CancellationToken cancellationToken)
    {
        await Task.CompletedTask; // Base implementation does nothing
    }

    /// <summary>
    /// Throws an exception if the processor has been disposed.
    /// </summary>
    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(GetType().Name);
        }
    }

    /// <summary>
    /// Disposes the processor and all its resources.
    /// </summary>
    /// <returns>Task representing the disposal operation</returns>
    public virtual async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;

        Logger.LogInformation("Disposing processor for plugin {PluginId}", Configuration.PluginId);

        try
        {
            // Stop processing if running
            if (State == ProcessorState.Running)
            {
                await StopAsync();
            }

            // Cancel any ongoing operations
            _processingCancellation.Cancel();

            // Wait for processing task to complete
            if (_processingTask != null)
            {
                try
                {
                    await _processingTask;
                }
                catch (OperationCanceledException)
                {
                    // Expected
                }
            }

            _processingCancellation.Dispose();
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error disposing processor for plugin {PluginId}", Configuration.PluginId);
        }
        finally
        {
            _disposed = true;
            
            lock (_stateLock)
            {
                State = ProcessorState.Disposed;
            }
        }
    }
}

/// <summary>
/// Processor lifecycle states.
/// </summary>
public enum ProcessorState
{
    /// <summary>
    /// Processor has been created but not initialized.
    /// </summary>
    Created,
    
    /// <summary>
    /// Processor is being initialized.
    /// </summary>
    Initializing,
    
    /// <summary>
    /// Processor has been initialized and is ready to start.
    /// </summary>
    Initialized,
    
    /// <summary>
    /// Processor is being started.
    /// </summary>
    Starting,
    
    /// <summary>
    /// Processor is running and processing data.
    /// </summary>
    Running,
    
    /// <summary>
    /// Processor is being stopped.
    /// </summary>
    Stopping,
    
    /// <summary>
    /// Processor has been stopped and can be restarted.
    /// </summary>
    Stopped,
    
    /// <summary>
    /// Processor has failed and requires intervention.
    /// </summary>
    Failed,
    
    /// <summary>
    /// Processor has been disposed and cannot be used.
    /// </summary>
    Disposed
}

/// <summary>
/// Processor performance metrics.
/// </summary>
public sealed record ProcessorMetrics
{
    /// <summary>
    /// Gets the total number of items processed.
    /// </summary>
    public required long TotalProcessed { get; init; }
    
    /// <summary>
    /// Gets the total number of errors encountered.
    /// </summary>
    public required long ErrorCount { get; init; }
    
    /// <summary>
    /// Gets the average processing time in milliseconds.
    /// </summary>
    public required double AverageProcessingTimeMs { get; init; }
    
    /// <summary>
    /// Gets the current memory usage in bytes.
    /// </summary>
    public required long MemoryUsageBytes { get; init; }
    
    /// <summary>
    /// Gets the processing throughput in items per second.
    /// </summary>
    public required double ThroughputPerSecond { get; init; }
    
    /// <summary>
    /// Gets the timestamp of last processed item.
    /// </summary>
    public required DateTimeOffset LastProcessedAt { get; init; }
}

/// <summary>
/// Represents a chunk of data for processing.
/// </summary>
public sealed class DataChunk : IDisposable
{
    /// <summary>
    /// Gets the schema for this data chunk.
    /// </summary>
    public required ISchema Schema { get; init; }
    
    /// <summary>
    /// Gets the data rows in this chunk.
    /// </summary>
    public required IArrayRow[] Rows { get; init; }
    
    /// <summary>
    /// Gets the number of rows in this chunk.
    /// </summary>
    public int RowCount => Rows.Length;
    
    /// <summary>
    /// Gets metadata associated with this chunk.
    /// </summary>
    public Dictionary<string, object>? Metadata { get; init; }
    
    /// <summary>
    /// Gets the chunk creation timestamp.
    /// </summary>
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

    private bool _disposed;

    /// <summary>
    /// Disposes the chunk and releases resources.
    /// </summary>
    public void Dispose()
    {
        if (!_disposed)
        {
            // Release any pooled objects or resources
            _disposed = true;
        }
    }
}

/// <summary>
/// Exception thrown when processor initialization fails.
/// </summary>
public sealed class ProcessorInitializationException : Exception
{
    public ProcessorInitializationException(string message) : base(message) { }
    public ProcessorInitializationException(string message, Exception innerException) : base(message, innerException) { }
}