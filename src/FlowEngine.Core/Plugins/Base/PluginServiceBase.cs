using FlowEngine.Abstractions.Data;
using Microsoft.Extensions.Logging;

namespace FlowEngine.Core.Plugins.Base;

/// <summary>
/// Base class for all plugin services in the five-component architecture.
/// Provides core business logic implementation with ArrayRow optimization.
/// </summary>
/// <remarks>
/// This base class implements the Service component of the five-component pattern:
/// Configuration → Validator → Plugin → Processor → Service
/// 
/// Key Responsibilities:
/// - Implement core business logic for data processing
/// - Provide ArrayRow-optimized operations for maximum performance
/// - Handle plugin-specific data transformations
/// - Maintain processing state and error handling
/// - Support configuration updates and hot-swapping
/// 
/// Performance Requirements:
/// - Achieve O(1) field access through pre-calculated indexes
/// - Support >200K rows/sec throughput for typical operations
/// - Memory usage must be bounded and predictable
/// - Error handling must not significantly impact performance
/// 
/// The Service component contains the actual business logic that processes
/// data according to the plugin's purpose (source, transform, sink).
/// </remarks>
/// <typeparam name="TConfiguration">Type of configuration this service uses</typeparam>
public abstract class PluginServiceBase<TConfiguration> : IAsyncDisposable
    where TConfiguration : PluginConfigurationBase
{
    /// <summary>
    /// Logger for service operations and diagnostics.
    /// </summary>
    protected readonly ILogger Logger;

    /// <summary>
    /// Plugin configuration instance.
    /// </summary>
    protected TConfiguration Configuration { get; private set; }

    /// <summary>
    /// Current service state.
    /// </summary>
    public ServiceState State { get; private set; } = ServiceState.Created;

    /// <summary>
    /// Gets whether this service can process the specified schema.
    /// </summary>
    /// <param name="schema">Schema to check compatibility</param>
    /// <returns>True if service can process the schema</returns>
    public abstract bool CanProcess(ISchema schema);

    /// <summary>
    /// Gets the current service metrics.
    /// </summary>
    /// <returns>Current service performance metrics</returns>
    public abstract ServiceMetrics GetMetrics();

    /// <summary>
    /// Processes a single data chunk with ArrayRow optimization.
    /// </summary>
    /// <param name="chunk">Data chunk to process</param>
    /// <param name="cancellationToken">Cancellation token for processing</param>
    /// <returns>Processed data chunk</returns>
    /// <remarks>
    /// This method implements the core processing logic for the plugin.
    /// It should leverage pre-calculated field indexes for optimal performance.
    /// </remarks>
    public abstract Task<DataChunk> ProcessChunkAsync(DataChunk chunk, CancellationToken cancellationToken = default);

    private readonly object _stateLock = new();
    private bool _disposed;

    // Performance tracking
    private long _totalProcessed;
    private long _errorCount;
    private readonly List<double> _processingTimes = new();
    private readonly object _metricsLock = new();
    private DateTimeOffset _lastProcessedAt = DateTimeOffset.UtcNow;

    /// <summary>
    /// Initializes a new instance of the PluginServiceBase class.
    /// </summary>
    /// <param name="configuration">Plugin configuration</param>
    /// <param name="logger">Logger for service operations</param>
    protected PluginServiceBase(TConfiguration configuration, ILogger logger)
    {
        Configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        Logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Initializes the service with resource allocation and preparation.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token for initialization</param>
    /// <returns>Task representing the initialization operation</returns>
    /// <exception cref="ServiceInitializationException">Service initialization failed</exception>
    /// <remarks>
    /// Initialization process:
    /// 1. Validate configuration compatibility
    /// 2. Allocate required resources
    /// 3. Prepare processing optimizations
    /// 4. Initialize service-specific components
    /// 5. Transition to Initialized state
    /// </remarks>
    public virtual async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        
        lock (_stateLock)
        {
            if (State != ServiceState.Created)
            {
                throw new InvalidOperationException($"Service cannot be initialized from state {State}");
            }
            
            State = ServiceState.Initializing;
        }

        try
        {
            Logger.LogInformation("Initializing service for plugin {PluginId}", Configuration.PluginId);

            var stopwatch = System.Diagnostics.Stopwatch.StartNew();

            // 1. Validate schema compatibility
            await ValidateSchemaCompatibilityAsync(cancellationToken);

            // 2. Initialize ArrayRow optimization
            await InitializeArrayRowOptimizationAsync(cancellationToken);

            // 3. Allocate resources
            await AllocateResourcesAsync(cancellationToken);

            // 4. Initialize service-specific components
            await InitializeServiceSpecificAsync(cancellationToken);

            stopwatch.Stop();

            lock (_stateLock)
            {
                State = ServiceState.Initialized;
            }

            Logger.LogInformation("Service initialized successfully for plugin {PluginId} in {Duration}ms", 
                Configuration.PluginId, stopwatch.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            lock (_stateLock)
            {
                State = ServiceState.Failed;
            }

            Logger.LogError(ex, "Failed to initialize service for plugin {PluginId}", Configuration.PluginId);
            throw new ServiceInitializationException($"Service initialization failed: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Starts the service and begins processing operations.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token for start operation</param>
    /// <returns>Task representing the start operation</returns>
    /// <exception cref="InvalidOperationException">Service not in correct state for starting</exception>
    /// <remarks>
    /// Start process:
    /// 1. Verify initialized state
    /// 2. Start background operations
    /// 3. Enable processing capabilities
    /// 4. Transition to Running state
    /// 
    /// After starting, the service is ready to process data chunks.
    /// </remarks>
    public virtual async Task StartAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        
        lock (_stateLock)
        {
            if (State != ServiceState.Initialized && State != ServiceState.Stopped)
            {
                throw new InvalidOperationException($"Service cannot be started from state {State}");
            }
            
            State = ServiceState.Starting;
        }

        try
        {
            Logger.LogInformation("Starting service for plugin {PluginId}", Configuration.PluginId);

            var stopwatch = System.Diagnostics.Stopwatch.StartNew();

            // Start service-specific operations
            await StartServiceSpecificAsync(cancellationToken);

            stopwatch.Stop();

            lock (_stateLock)
            {
                State = ServiceState.Running;
            }

            Logger.LogInformation("Service started successfully for plugin {PluginId} in {Duration}ms", 
                Configuration.PluginId, stopwatch.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            lock (_stateLock)
            {
                State = ServiceState.Failed;
            }

            Logger.LogError(ex, "Failed to start service for plugin {PluginId}", Configuration.PluginId);
            throw;
        }
    }

    /// <summary>
    /// Stops the service and suspends processing operations.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token for stop operation</param>
    /// <returns>Task representing the stop operation</returns>
    /// <remarks>
    /// Stop process:
    /// 1. Complete pending operations
    /// 2. Stop background operations
    /// 3. Preserve state for restart
    /// 4. Transition to Stopped state
    /// </remarks>
    public virtual async Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed || State == ServiceState.Stopped || State == ServiceState.Failed)
            return;

        lock (_stateLock)
        {
            State = ServiceState.Stopping;
        }

        try
        {
            Logger.LogInformation("Stopping service for plugin {PluginId}", Configuration.PluginId);

            var stopwatch = System.Diagnostics.Stopwatch.StartNew();

            // Stop service-specific operations
            await StopServiceSpecificAsync(cancellationToken);

            stopwatch.Stop();

            lock (_stateLock)
            {
                State = ServiceState.Stopped;
            }

            Logger.LogInformation("Service stopped successfully for plugin {PluginId} in {Duration}ms", 
                Configuration.PluginId, stopwatch.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            lock (_stateLock)
            {
                State = ServiceState.Failed;
            }

            Logger.LogError(ex, "Failed to stop service for plugin {PluginId}", Configuration.PluginId);
            throw;
        }
    }

    /// <summary>
    /// Updates the service configuration with hot-swapping support.
    /// </summary>
    /// <param name="newConfiguration">New configuration to apply</param>
    /// <param name="cancellationToken">Cancellation token for update operation</param>
    /// <returns>Task representing the configuration update operation</returns>
    /// <exception cref="NotSupportedException">Service does not support hot-swapping</exception>
    /// <remarks>
    /// Configuration update process:
    /// 1. Validate new configuration
    /// 2. Prepare for configuration change
    /// 3. Apply new configuration atomically
    /// 4. Update ArrayRow optimizations
    /// 5. Verify successful update
    /// </remarks>
    public virtual async Task UpdateConfigurationAsync(
        TConfiguration newConfiguration, 
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(newConfiguration);

        if (!Configuration.SupportsHotSwapping)
        {
            throw new NotSupportedException("Service does not support hot-swapping");
        }

        Logger.LogInformation("Updating service configuration for plugin {PluginId}", Configuration.PluginId);

        try
        {
            // Validate schema compatibility
            if (!newConfiguration.IsCompatibleWith(Configuration.InputSchema))
            {
                throw new ArgumentException("New configuration is not compatible with current schema");
            }

            var oldConfiguration = Configuration;
            Configuration = newConfiguration;

            // Update ArrayRow optimization if schema changed
            if (!oldConfiguration.InputSchema.Equals(newConfiguration.InputSchema) ||
                !oldConfiguration.OutputSchema.Equals(newConfiguration.OutputSchema))
            {
                await InitializeArrayRowOptimizationAsync(cancellationToken);
            }

            // Apply service-specific configuration changes
            await UpdateServiceSpecificConfigurationAsync(oldConfiguration, newConfiguration, cancellationToken);

            Logger.LogInformation("Service configuration updated successfully for plugin {PluginId}", Configuration.PluginId);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to update service configuration for plugin {PluginId}", Configuration.PluginId);
            throw;
        }
    }

    /// <summary>
    /// Processes a single ArrayRow with optimized field access.
    /// </summary>
    /// <param name="row">IArrayRow to process</param>
    /// <param name="cancellationToken">Cancellation token for processing</param>
    /// <returns>Processed IArrayRow</returns>
    /// <remarks>
    /// This method should be overridden by derived classes to implement
    /// specific processing logic using ArrayRow optimization patterns.
    /// 
    /// Performance: Use Configuration.GetInputFieldIndex() and Configuration.GetOutputFieldIndex()
    /// for O(1) field access.
    /// </remarks>
    protected virtual async Task<IArrayRow> ProcessRowAsync(IArrayRow row, CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask;
        
        // Base implementation returns the row unchanged
        // Override in derived classes for specific processing logic
        return row;
    }

    /// <summary>
    /// Validates that the service can handle the configured schemas.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token for validation</param>
    /// <returns>Task representing the validation operation</returns>
    private async Task ValidateSchemaCompatibilityAsync(CancellationToken cancellationToken)
    {
        if (!CanProcess(Configuration.InputSchema))
        {
            throw new ServiceInitializationException("Service cannot process the configured input schema");
        }

        // Additional schema validation can be added here
        await Task.CompletedTask;
    }

    /// <summary>
    /// Initializes ArrayRow optimization with pre-calculated field indexes.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token for initialization</param>
    /// <returns>Task representing the optimization initialization</returns>
    private async Task InitializeArrayRowOptimizationAsync(CancellationToken cancellationToken)
    {
        // Verify that field indexes are properly calculated
        var inputIndexes = Configuration.InputFieldIndexes;
        var outputIndexes = Configuration.OutputFieldIndexes;

        if (inputIndexes.IsEmpty && Configuration.InputSchema.ColumnCount > 0)
        {
            Logger.LogWarning("Input field indexes are not pre-calculated for plugin {PluginId}", Configuration.PluginId);
        }

        if (outputIndexes.IsEmpty && Configuration.OutputSchema.ColumnCount > 0)
        {
            Logger.LogWarning("Output field indexes are not pre-calculated for plugin {PluginId}", Configuration.PluginId);
        }

        Logger.LogDebug("ArrayRow optimization initialized for plugin {PluginId} with {InputFields} input fields and {OutputFields} output fields",
            Configuration.PluginId, inputIndexes.Count, outputIndexes.Count);

        await Task.CompletedTask;
    }

    /// <summary>
    /// Allocates resources required for service operation.
    /// Override this method to implement custom resource allocation.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token for allocation</param>
    /// <returns>Task representing the resource allocation</returns>
    protected virtual async Task AllocateResourcesAsync(CancellationToken cancellationToken)
    {
        await Task.CompletedTask; // Base implementation does nothing
    }

    /// <summary>
    /// Performs service-specific initialization logic.
    /// Override this method to implement custom initialization requirements.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token for initialization</param>
    /// <returns>Task representing the service-specific initialization</returns>
    protected virtual async Task InitializeServiceSpecificAsync(CancellationToken cancellationToken)
    {
        await Task.CompletedTask; // Base implementation does nothing
    }

    /// <summary>
    /// Performs service-specific start logic.
    /// Override this method to implement custom start requirements.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token for start operation</param>
    /// <returns>Task representing the service-specific start</returns>
    protected virtual async Task StartServiceSpecificAsync(CancellationToken cancellationToken)
    {
        await Task.CompletedTask; // Base implementation does nothing
    }

    /// <summary>
    /// Performs service-specific stop logic.
    /// Override this method to implement custom stop requirements.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token for stop operation</param>
    /// <returns>Task representing the service-specific stop</returns>
    protected virtual async Task StopServiceSpecificAsync(CancellationToken cancellationToken)
    {
        await Task.CompletedTask; // Base implementation does nothing
    }

    /// <summary>
    /// Performs service-specific configuration update logic.
    /// Override this method to implement custom configuration update requirements.
    /// </summary>
    /// <param name="oldConfiguration">Previous configuration</param>
    /// <param name="newConfiguration">New configuration</param>
    /// <param name="cancellationToken">Cancellation token for update operation</param>
    /// <returns>Task representing the service-specific configuration update</returns>
    protected virtual async Task UpdateServiceSpecificConfigurationAsync(
        TConfiguration oldConfiguration, 
        TConfiguration newConfiguration, 
        CancellationToken cancellationToken)
    {
        await Task.CompletedTask; // Base implementation does nothing
    }

    /// <summary>
    /// Updates performance metrics after processing an item.
    /// </summary>
    /// <param name="processingTimeMs">Processing time in milliseconds</param>
    /// <param name="itemCount">Number of items processed</param>
    /// <param name="hasError">Whether an error occurred</param>
    protected void UpdateMetrics(double processingTimeMs, int itemCount, bool hasError = false)
    {
        lock (_metricsLock)
        {
            _totalProcessed += itemCount;
            _lastProcessedAt = DateTimeOffset.UtcNow;

            if (hasError)
            {
                _errorCount++;
            }

            _processingTimes.Add(processingTimeMs);

            // Keep only recent processing times for average calculation
            if (_processingTimes.Count > 1000)
            {
                _processingTimes.RemoveRange(0, _processingTimes.Count - 1000);
            }
        }
    }

    /// <summary>
    /// Gets the average processing time from recent operations.
    /// </summary>
    /// <returns>Average processing time in milliseconds</returns>
    protected double GetAverageProcessingTime()
    {
        lock (_metricsLock)
        {
            return _processingTimes.Count > 0 ? _processingTimes.Average() : 0.0;
        }
    }

    /// <summary>
    /// Throws an exception if the service has been disposed.
    /// </summary>
    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(GetType().Name);
        }
    }

    /// <summary>
    /// Disposes the service and all its resources.
    /// </summary>
    /// <returns>Task representing the disposal operation</returns>
    public virtual async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;

        Logger.LogInformation("Disposing service for plugin {PluginId}", Configuration.PluginId);

        try
        {
            // Stop the service if running
            if (State == ServiceState.Running)
            {
                await StopAsync();
            }

            // Release resources
            await ReleaseResourcesAsync();
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error disposing service for plugin {PluginId}", Configuration.PluginId);
        }
        finally
        {
            _disposed = true;
            
            lock (_stateLock)
            {
                State = ServiceState.Disposed;
            }
        }
    }

    /// <summary>
    /// Releases resources allocated by the service.
    /// Override this method to implement custom resource cleanup.
    /// </summary>
    /// <returns>Task representing the resource release</returns>
    protected virtual async Task ReleaseResourcesAsync()
    {
        await Task.CompletedTask; // Base implementation does nothing
    }
}

/// <summary>
/// Service lifecycle states.
/// </summary>
public enum ServiceState
{
    /// <summary>
    /// Service has been created but not initialized.
    /// </summary>
    Created,
    
    /// <summary>
    /// Service is being initialized.
    /// </summary>
    Initializing,
    
    /// <summary>
    /// Service has been initialized and is ready to start.
    /// </summary>
    Initialized,
    
    /// <summary>
    /// Service is being started.
    /// </summary>
    Starting,
    
    /// <summary>
    /// Service is running and processing data.
    /// </summary>
    Running,
    
    /// <summary>
    /// Service is being stopped.
    /// </summary>
    Stopping,
    
    /// <summary>
    /// Service has been stopped and can be restarted.
    /// </summary>
    Stopped,
    
    /// <summary>
    /// Service has failed and requires intervention.
    /// </summary>
    Failed,
    
    /// <summary>
    /// Service has been disposed and cannot be used.
    /// </summary>
    Disposed
}

/// <summary>
/// Service performance metrics.
/// </summary>
public sealed record ServiceMetrics
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
    
    /// <summary>
    /// Gets the error rate as a percentage.
    /// </summary>
    public double ErrorRate => TotalProcessed > 0 ? (double)ErrorCount / TotalProcessed * 100 : 0;
}

/// <summary>
/// Exception thrown when service initialization fails.
/// </summary>
public sealed class ServiceInitializationException : Exception
{
    public ServiceInitializationException(string message) : base(message) { }
    public ServiceInitializationException(string message, Exception innerException) : base(message, innerException) { }
}