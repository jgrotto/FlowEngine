using FlowEngine.Abstractions.Data;
using Microsoft.Extensions.Logging;
using System.Collections.Immutable;

namespace FlowEngine.Core.Plugins.Base;

/// <summary>
/// Base class for all plugins in the five-component architecture.
/// Provides lifecycle management, configuration binding, and plugin orchestration.
/// </summary>
/// <remarks>
/// This base class implements the Plugin component of the five-component pattern:
/// Configuration → Validator → Plugin → Processor → Service
/// 
/// Key Responsibilities:
/// - Plugin lifecycle management (Initialize, Start, Stop, Dispose)
/// - Configuration and validation coordination
/// - Processor and Service component orchestration
/// - State management and error handling
/// - Hot-swapping support and compatibility checking
/// 
/// Performance Requirements:
/// - Plugin initialization must complete in less than 100ms for typical configurations
/// - State transitions must be thread-safe and atomic
/// - Resource cleanup must be comprehensive and efficient
/// 
/// The Plugin component acts as the central coordinator that binds all other
/// components together and manages the overall plugin lifecycle.
/// </remarks>
/// <typeparam name="TConfiguration">Type of configuration this plugin uses</typeparam>
/// <typeparam name="TValidator">Type of validator this plugin uses</typeparam>
/// <typeparam name="TProcessor">Type of processor this plugin uses</typeparam>
/// <typeparam name="TService">Type of service this plugin uses</typeparam>
public abstract class PluginBase<TConfiguration, TValidator, TProcessor, TService> : IAsyncDisposable
    where TConfiguration : PluginConfigurationBase
    where TValidator : PluginValidatorBase<TConfiguration>
    where TProcessor : PluginProcessorBase<TConfiguration, TService>
    where TService : PluginServiceBase<TConfiguration>
{
    /// <summary>
    /// Logger for plugin lifecycle and operations.
    /// </summary>
    protected readonly ILogger Logger;

    /// <summary>
    /// Plugin configuration instance.
    /// </summary>
    protected TConfiguration Configuration { get; private set; }

    /// <summary>
    /// Plugin validator instance.
    /// </summary>
    protected TValidator Validator { get; private set; }

    /// <summary>
    /// Plugin processor instance.
    /// </summary>
    protected TProcessor Processor { get; private set; }

    /// <summary>
    /// Plugin service instance.
    /// </summary>
    protected TService Service { get; private set; }

    /// <summary>
    /// Current plugin state.
    /// </summary>
    public PluginState State { get; private set; } = PluginState.Created;

    /// <summary>
    /// Plugin metadata and information.
    /// </summary>
    public abstract PluginMetadata Metadata { get; }

    /// <summary>
    /// Gets whether this plugin supports hot-swapping.
    /// </summary>
    public virtual bool SupportsHotSwapping => Configuration?.SupportsHotSwapping ?? false;

    /// <summary>
    /// Gets the current plugin health status.
    /// </summary>
    public abstract PluginHealth Health { get; }

    private readonly object _stateLock = new();
    private readonly CancellationTokenSource _lifecycleCancellation = new();
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the PluginBase class.
    /// </summary>
    /// <param name="configuration">Plugin configuration</param>
    /// <param name="validator">Plugin validator</param>
    /// <param name="processor">Plugin processor</param>
    /// <param name="service">Plugin service</param>
    /// <param name="logger">Logger for plugin operations</param>
    protected PluginBase(
        TConfiguration configuration,
        TValidator validator,
        TProcessor processor,
        TService service,
        ILogger logger)
    {
        Configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        Validator = validator ?? throw new ArgumentNullException(nameof(validator));
        Processor = processor ?? throw new ArgumentNullException(nameof(processor));
        Service = service ?? throw new ArgumentNullException(nameof(service));
        Logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Initializes the plugin with validation and component setup.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token for initialization</param>
    /// <returns>Task representing the initialization operation</returns>
    /// <exception cref="PluginInitializationException">Plugin initialization failed</exception>
    /// <remarks>
    /// Initialization process:
    /// 1. Validate configuration
    /// 2. Initialize service component
    /// 3. Initialize processor component
    /// 4. Verify component integration
    /// 5. Transition to Initialized state
    /// 
    /// This method is called once per plugin lifetime and must complete successfully
    /// before the plugin can be started.
    /// </remarks>
    public virtual async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        
        lock (_stateLock)
        {
            if (State != PluginState.Created)
            {
                throw new InvalidOperationException($"Plugin cannot be initialized from state {State}");
            }
            
            State = PluginState.Initializing;
        }

        try
        {
            Logger.LogInformation("Initializing {PluginType} plugin {PluginId}", 
                Metadata.Type, Metadata.Id);

            var stopwatch = System.Diagnostics.Stopwatch.StartNew();

            // 1. Validate configuration
            var validationResult = await Validator.ValidateAsync(Configuration);
            if (!validationResult.IsValid)
            {
                var errorMessages = string.Join(", ", validationResult.Errors.Select(e => e.Message));
                throw new PluginInitializationException(
                    $"Configuration validation failed: {errorMessages}");
            }

            // 2. Initialize service component
            await Service.InitializeAsync(cancellationToken);
            Logger.LogDebug("Service component initialized for plugin {PluginId}", Metadata.Id);

            // 3. Initialize processor component
            await Processor.InitializeAsync(cancellationToken);
            Logger.LogDebug("Processor component initialized for plugin {PluginId}", Metadata.Id);

            // 4. Verify component integration
            await ValidateComponentIntegrationAsync(cancellationToken);

            // 5. Plugin-specific initialization
            await InitializePluginSpecificAsync(cancellationToken);

            stopwatch.Stop();

            lock (_stateLock)
            {
                State = PluginState.Initialized;
            }

            Logger.LogInformation("Plugin {PluginId} initialized successfully in {Duration}ms", 
                Metadata.Id, stopwatch.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            lock (_stateLock)
            {
                State = PluginState.Failed;
            }

            Logger.LogError(ex, "Failed to initialize plugin {PluginId}", Metadata.Id);
            throw new PluginInitializationException($"Plugin initialization failed: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Starts the plugin and begins processing operations.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token for start operation</param>
    /// <returns>Task representing the start operation</returns>
    /// <exception cref="InvalidOperationException">Plugin not in correct state for starting</exception>
    /// <remarks>
    /// Start process:
    /// 1. Verify initialized state
    /// 2. Start service component
    /// 3. Start processor component
    /// 4. Begin plugin operations
    /// 5. Transition to Running state
    /// 
    /// After starting, the plugin is ready to process data and respond to requests.
    /// </remarks>
    public virtual async Task StartAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        
        lock (_stateLock)
        {
            if (State != PluginState.Initialized && State != PluginState.Stopped)
            {
                throw new InvalidOperationException($"Plugin cannot be started from state {State}");
            }
            
            State = PluginState.Starting;
        }

        try
        {
            Logger.LogInformation("Starting {PluginType} plugin {PluginId}", 
                Metadata.Type, Metadata.Id);

            var stopwatch = System.Diagnostics.Stopwatch.StartNew();

            // 1. Start service component
            await Service.StartAsync(cancellationToken);
            Logger.LogDebug("Service component started for plugin {PluginId}", Metadata.Id);

            // 2. Start processor component
            await Processor.StartAsync(cancellationToken);
            Logger.LogDebug("Processor component started for plugin {PluginId}", Metadata.Id);

            // 3. Plugin-specific start operations
            await StartPluginSpecificAsync(cancellationToken);

            stopwatch.Stop();

            lock (_stateLock)
            {
                State = PluginState.Running;
            }

            Logger.LogInformation("Plugin {PluginId} started successfully in {Duration}ms", 
                Metadata.Id, stopwatch.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            lock (_stateLock)
            {
                State = PluginState.Failed;
            }

            Logger.LogError(ex, "Failed to start plugin {PluginId}", Metadata.Id);
            throw;
        }
    }

    /// <summary>
    /// Stops the plugin and suspends processing operations.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token for stop operation</param>
    /// <returns>Task representing the stop operation</returns>
    /// <remarks>
    /// Stop process:
    /// 1. Suspend new operations
    /// 2. Complete pending operations
    /// 3. Stop processor component
    /// 4. Stop service component
    /// 5. Transition to Stopped state
    /// 
    /// After stopping, the plugin can be restarted or disposed.
    /// </remarks>
    public virtual async Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed || State == PluginState.Stopped || State == PluginState.Failed)
            return;

        lock (_stateLock)
        {
            if (State != PluginState.Running)
            {
                Logger.LogWarning("Attempting to stop plugin {PluginId} from state {State}", 
                    Metadata.Id, State);
            }
            
            State = PluginState.Stopping;
        }

        try
        {
            Logger.LogInformation("Stopping {PluginType} plugin {PluginId}", 
                Metadata.Type, Metadata.Id);

            var stopwatch = System.Diagnostics.Stopwatch.StartNew();

            // 1. Plugin-specific stop operations
            await StopPluginSpecificAsync(cancellationToken);

            // 2. Stop processor component
            await Processor.StopAsync(cancellationToken);
            Logger.LogDebug("Processor component stopped for plugin {PluginId}", Metadata.Id);

            // 3. Stop service component
            await Service.StopAsync(cancellationToken);
            Logger.LogDebug("Service component stopped for plugin {PluginId}", Metadata.Id);

            stopwatch.Stop();

            lock (_stateLock)
            {
                State = PluginState.Stopped;
            }

            Logger.LogInformation("Plugin {PluginId} stopped successfully in {Duration}ms", 
                Metadata.Id, stopwatch.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            lock (_stateLock)
            {
                State = PluginState.Failed;
            }

            Logger.LogError(ex, "Failed to stop plugin {PluginId}", Metadata.Id);
            throw;
        }
    }

    /// <summary>
    /// Updates the plugin configuration with hot-swapping support.
    /// </summary>
    /// <param name="newConfiguration">New configuration to apply</param>
    /// <param name="cancellationToken">Cancellation token for update operation</param>
    /// <returns>Task representing the configuration update operation</returns>
    /// <exception cref="NotSupportedException">Plugin does not support hot-swapping</exception>
    /// <exception cref="InvalidOperationException">Configuration update not allowed in current state</exception>
    /// <remarks>
    /// Configuration update process:
    /// 1. Validate new configuration
    /// 2. Check compatibility with current state
    /// 3. Apply configuration to components
    /// 4. Verify successful update
    /// 
    /// Hot-swapping allows configuration changes without stopping the plugin.
    /// </remarks>
    public virtual async Task UpdateConfigurationAsync(
        TConfiguration newConfiguration, 
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(newConfiguration);

        if (!SupportsHotSwapping)
        {
            throw new NotSupportedException("Plugin does not support hot-swapping");
        }

        lock (_stateLock)
        {
            if (State != PluginState.Running && State != PluginState.Initialized)
            {
                throw new InvalidOperationException($"Configuration cannot be updated in state {State}");
            }
        }

        Logger.LogInformation("Updating configuration for plugin {PluginId}", Metadata.Id);

        try
        {
            // 1. Validate new configuration
            var validationResult = await Validator.ValidateAsync(newConfiguration);
            if (!validationResult.IsValid)
            {
                var errorMessages = string.Join(", ", validationResult.Errors.Select(e => e.Message));
                throw new ArgumentException($"New configuration is invalid: {errorMessages}");
            }

            // 2. Check schema compatibility
            if (!newConfiguration.IsCompatibleWith(Configuration.InputSchema))
            {
                throw new ArgumentException("New configuration is not compatible with current schema");
            }

            // 3. Update components
            await Service.UpdateConfigurationAsync(newConfiguration, cancellationToken);
            await Processor.UpdateConfigurationAsync(newConfiguration, cancellationToken);

            // 4. Apply new configuration
            var oldConfiguration = Configuration;
            Configuration = newConfiguration;

            // 5. Plugin-specific configuration update
            await UpdatePluginSpecificConfigurationAsync(oldConfiguration, newConfiguration, cancellationToken);

            Logger.LogInformation("Configuration updated successfully for plugin {PluginId}", Metadata.Id);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to update configuration for plugin {PluginId}", Metadata.Id);
            throw;
        }
    }

    /// <summary>
    /// Validates compatibility with another plugin for pipeline connections.
    /// </summary>
    /// <param name="sourcePlugin">Source plugin to check compatibility</param>
    /// <returns>Schema compatibility result</returns>
    /// <remarks>
    /// Compatibility checking ensures that data can flow correctly between plugins
    /// without type errors or schema mismatches.
    /// </remarks>
    public virtual async Task<SchemaCompatibilityResult> ValidateCompatibilityAsync(PluginBase<TConfiguration, TValidator, TProcessor, TService> sourcePlugin)
    {
        ArgumentNullException.ThrowIfNull(sourcePlugin);

        return await Validator.ValidateCompatibilityAsync(Configuration, sourcePlugin.Configuration.OutputSchema);
    }

    /// <summary>
    /// Gets plugin performance metrics and statistics.
    /// </summary>
    /// <returns>Current plugin performance metrics</returns>
    /// <remarks>
    /// Metrics include processing throughput, error rates, resource usage,
    /// and component-specific performance indicators.
    /// </remarks>
    public virtual PluginMetrics GetMetrics()
    {
        var serviceMetrics = Service.GetMetrics();
        var processorMetrics = Processor.GetMetrics();

        return new PluginMetrics
        {
            PluginId = Metadata.Id,
            State = State,
            TotalProcessed = serviceMetrics.TotalProcessed + processorMetrics.TotalProcessed,
            ErrorCount = serviceMetrics.ErrorCount + processorMetrics.ErrorCount,
            AverageProcessingTimeMs = (serviceMetrics.AverageProcessingTimeMs + processorMetrics.AverageProcessingTimeMs) / 2,
            MemoryUsageBytes = serviceMetrics.MemoryUsageBytes + processorMetrics.MemoryUsageBytes,
            LastActivityAt = DateTimeOffset.UtcNow
        };
    }

    /// <summary>
    /// Performs plugin-specific initialization logic.
    /// Override this method to implement custom initialization requirements.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token for initialization</param>
    /// <returns>Task representing the plugin-specific initialization</returns>
    protected virtual async Task InitializePluginSpecificAsync(CancellationToken cancellationToken)
    {
        await Task.CompletedTask; // Base implementation does nothing
    }

    /// <summary>
    /// Performs plugin-specific start logic.
    /// Override this method to implement custom start requirements.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token for start operation</param>
    /// <returns>Task representing the plugin-specific start</returns>
    protected virtual async Task StartPluginSpecificAsync(CancellationToken cancellationToken)
    {
        await Task.CompletedTask; // Base implementation does nothing
    }

    /// <summary>
    /// Performs plugin-specific stop logic.
    /// Override this method to implement custom stop requirements.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token for stop operation</param>
    /// <returns>Task representing the plugin-specific stop</returns>
    protected virtual async Task StopPluginSpecificAsync(CancellationToken cancellationToken)
    {
        await Task.CompletedTask; // Base implementation does nothing
    }

    /// <summary>
    /// Performs plugin-specific configuration update logic.
    /// Override this method to implement custom configuration update requirements.
    /// </summary>
    /// <param name="oldConfiguration">Previous configuration</param>
    /// <param name="newConfiguration">New configuration</param>
    /// <param name="cancellationToken">Cancellation token for update operation</param>
    /// <returns>Task representing the plugin-specific configuration update</returns>
    protected virtual async Task UpdatePluginSpecificConfigurationAsync(
        TConfiguration oldConfiguration, 
        TConfiguration newConfiguration, 
        CancellationToken cancellationToken)
    {
        await Task.CompletedTask; // Base implementation does nothing
    }

    /// <summary>
    /// Validates that all components are properly integrated and functional.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token for validation</param>
    /// <returns>Task representing the integration validation</returns>
    private async Task ValidateComponentIntegrationAsync(CancellationToken cancellationToken)
    {
        // Verify service can handle configuration
        if (!Service.CanProcess(Configuration.InputSchema))
        {
            throw new PluginInitializationException("Service component cannot process the configured input schema");
        }

        // Verify processor can coordinate with service
        if (!Processor.IsServiceCompatible(Service))
        {
            throw new PluginInitializationException("Processor component is not compatible with the service component");
        }

        // Additional integration checks can be added here
        await Task.CompletedTask;
    }

    /// <summary>
    /// Throws an exception if the plugin has been disposed.
    /// </summary>
    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(GetType().Name);
        }
    }

    /// <summary>
    /// Disposes the plugin and all its components.
    /// </summary>
    /// <returns>Task representing the disposal operation</returns>
    public virtual async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;

        Logger.LogInformation("Disposing plugin {PluginId}", Metadata.Id);

        try
        {
            // Cancel any ongoing operations
            _lifecycleCancellation.Cancel();

            // Stop the plugin if it's running
            if (State == PluginState.Running)
            {
                await StopAsync();
            }

            // Dispose components
            if (Processor is IAsyncDisposable asyncProcessor)
                await asyncProcessor.DisposeAsync();
            
            if (Service is IAsyncDisposable asyncService)
                await asyncService.DisposeAsync();

            _lifecycleCancellation.Dispose();
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error disposing plugin {PluginId}", Metadata.Id);
        }
        finally
        {
            _disposed = true;
            
            lock (_stateLock)
            {
                State = PluginState.Disposed;
            }
        }
    }
}

/// <summary>
/// Plugin lifecycle states.
/// </summary>
public enum PluginState
{
    /// <summary>
    /// Plugin has been created but not initialized.
    /// </summary>
    Created,
    
    /// <summary>
    /// Plugin is being initialized.
    /// </summary>
    Initializing,
    
    /// <summary>
    /// Plugin has been initialized and is ready to start.
    /// </summary>
    Initialized,
    
    /// <summary>
    /// Plugin is being started.
    /// </summary>
    Starting,
    
    /// <summary>
    /// Plugin is running and processing data.
    /// </summary>
    Running,
    
    /// <summary>
    /// Plugin is being stopped.
    /// </summary>
    Stopping,
    
    /// <summary>
    /// Plugin has been stopped and can be restarted.
    /// </summary>
    Stopped,
    
    /// <summary>
    /// Plugin has failed and requires intervention.
    /// </summary>
    Failed,
    
    /// <summary>
    /// Plugin has been disposed and cannot be used.
    /// </summary>
    Disposed
}

/// <summary>
/// Plugin metadata and information.
/// </summary>
public sealed record PluginMetadata
{
    /// <summary>
    /// Gets the unique plugin identifier.
    /// </summary>
    public required string Id { get; init; }
    
    /// <summary>
    /// Gets the plugin name.
    /// </summary>
    public required string Name { get; init; }
    
    /// <summary>
    /// Gets the plugin type (Source, Transform, Sink).
    /// </summary>
    public required string Type { get; init; }
    
    /// <summary>
    /// Gets the plugin version.
    /// </summary>
    public required string Version { get; init; }
    
    /// <summary>
    /// Gets the plugin description.
    /// </summary>
    public string? Description { get; init; }
    
    /// <summary>
    /// Gets the plugin author.
    /// </summary>
    public string? Author { get; init; }
    
    /// <summary>
    /// Gets additional plugin tags.
    /// </summary>
    public ImmutableArray<string> Tags { get; init; } = ImmutableArray<string>.Empty;
}

/// <summary>
/// Plugin health status information.
/// </summary>
public sealed record PluginHealth
{
    /// <summary>
    /// Gets whether the plugin is healthy.
    /// </summary>
    public required bool IsHealthy { get; init; }
    
    /// <summary>
    /// Gets the plugin state.
    /// </summary>
    public required PluginState State { get; init; }
    
    /// <summary>
    /// Gets health check details.
    /// </summary>
    public required ImmutableArray<HealthCheck> Checks { get; init; }
    
    /// <summary>
    /// Gets the last health check time.
    /// </summary>
    public required DateTimeOffset LastChecked { get; init; }
}

/// <summary>
/// Individual health check result.
/// </summary>
public sealed record HealthCheck
{
    /// <summary>
    /// Gets the check name.
    /// </summary>
    public required string Name { get; init; }
    
    /// <summary>
    /// Gets whether the check passed.
    /// </summary>
    public required bool Passed { get; init; }
    
    /// <summary>
    /// Gets check details or error message.
    /// </summary>
    public string? Details { get; init; }
}

/// <summary>
/// Plugin performance metrics.
/// </summary>
public sealed record PluginMetrics
{
    /// <summary>
    /// Gets the plugin identifier.
    /// </summary>
    public required string PluginId { get; init; }
    
    /// <summary>
    /// Gets the current plugin state.
    /// </summary>
    public required PluginState State { get; init; }
    
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
    /// Gets the timestamp of last activity.
    /// </summary>
    public required DateTimeOffset LastActivityAt { get; init; }
}

/// <summary>
/// Exception thrown when plugin initialization fails.
/// </summary>
public sealed class PluginInitializationException : Exception
{
    public PluginInitializationException(string message) : base(message) { }
    public PluginInitializationException(string message, Exception innerException) : base(message, innerException) { }
}