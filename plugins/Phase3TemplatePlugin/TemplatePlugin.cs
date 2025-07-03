using FlowEngine.Abstractions.Plugins;
using Microsoft.Extensions.Logging;
using System.Collections.Immutable;

namespace Phase3TemplatePlugin;

/// <summary>
/// COMPONENT 3: Plugin
/// 
/// Demonstrates proper Phase 3 plugin lifecycle management with:
/// - Plugin metadata and versioning
/// - Lifecycle state management (Created → Initialized → Running → Stopped → Disposed)
/// - Hot-swapping configuration support
/// - Health monitoring and metrics
/// - Component factory methods
/// </summary>
public sealed class TemplatePlugin : IPlugin
{
    private readonly ILogger<TemplatePlugin> _logger;
    private TemplatePluginConfiguration? _configuration;
    private PluginState _state = PluginState.Created;
    private bool _disposed;
    private readonly object _stateLock = new();

    /// <summary>
    /// Constructor demonstrating proper dependency injection
    /// </summary>
    /// <param name="logger">Logger injected from Core framework</param>
    public TemplatePlugin(ILogger<TemplatePlugin> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    // === IPlugin Core Properties ===

    /// <inheritdoc />
    public string Id => "phase3-template-plugin";

    /// <inheritdoc />
    public string Name => "Phase 3 Template Plugin";

    /// <inheritdoc />
    public string Version => "3.0.0";

    /// <inheritdoc />
    public string Type => "Source";

    /// <inheritdoc />
    public string Description => "Reference implementation demonstrating correct Phase 3 five-component plugin architecture";

    /// <inheritdoc />
    public string Author => "FlowEngine Team";

    /// <inheritdoc />
    public bool SupportsHotSwapping => true;

    /// <inheritdoc />
    public PluginState Status 
    { 
        get 
        { 
            lock (_stateLock) 
            { 
                return _state; 
            } 
        } 
    }

    /// <inheritdoc />
    public IPluginConfiguration? Configuration => _configuration;

    /// <inheritdoc />
    public ImmutableArray<string> SupportedCapabilities => ImmutableArray.Create(
        "DataGeneration",
        "ConfigurableOutput",
        "BatchProcessing",
        "ArrayRowOptimization",
        "PerformanceMonitoring"
    );

    /// <inheritdoc />
    public ImmutableArray<string> Dependencies => ImmutableArray<string>.Empty;

    // === Lifecycle Management ===

    /// <inheritdoc />
    public async Task<PluginInitializationResult> InitializeAsync(
        IPluginConfiguration configuration,
        CancellationToken cancellationToken = default)
    {
        var startTime = DateTimeOffset.UtcNow;
        _logger.LogInformation("Initializing Template Plugin v{Version}", Version);

        lock (_stateLock)
        {
            if (_state != PluginState.Created)
            {
                return new PluginInitializationResult
                {
                    Success = false,
                    Message = $"Plugin cannot be initialized from state {_state}",
                    InitializationTime = TimeSpan.Zero,
                    Errors = ImmutableArray.Create($"Invalid state transition from {_state} to Initialized")
                };
            }
        }

        if (configuration is not TemplatePluginConfiguration templateConfig)
        {
            return new PluginInitializationResult
            {
                Success = false,
                Message = $"Expected TemplatePluginConfiguration, got {configuration?.GetType().Name ?? "null"}",
                InitializationTime = TimeSpan.Zero,
                Errors = ImmutableArray.Create("Invalid configuration type")
            };
        }

        try
        {
            // Validate configuration using validator
            var validator = new TemplatePluginValidator();
            var validationResult = await validator.ValidateAsync(templateConfig, cancellationToken);

            if (!validationResult.IsValid)
            {
                var errors = validationResult.Errors.Select(e => e.Message).ToArray();
                return new PluginInitializationResult
                {
                    Success = false,
                    Message = "Configuration validation failed",
                    InitializationTime = DateTimeOffset.UtcNow - startTime,
                    Errors = ImmutableArray.Create(errors)
                };
            }

            // Store configuration and update state
            _configuration = templateConfig;
            
            lock (_stateLock)
            {
                _state = PluginState.Initialized;
            }

            _logger.LogInformation(
                "Template Plugin initialized successfully. RowCount: {RowCount}, BatchSize: {BatchSize}, DataType: {DataType}",
                templateConfig.RowCount,
                templateConfig.BatchSize,
                templateConfig.DataType);

            return new PluginInitializationResult
            {
                Success = true,
                Message = "Plugin initialized successfully",
                InitializationTime = DateTimeOffset.UtcNow - startTime
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize Template Plugin");
            
            lock (_stateLock)
            {
                _state = PluginState.Failed;
            }

            return new PluginInitializationResult
            {
                Success = false,
                Message = $"Initialization failed: {ex.Message}",
                InitializationTime = DateTimeOffset.UtcNow - startTime,
                Errors = ImmutableArray.Create(ex.Message)
            };
        }
    }

    /// <inheritdoc />
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Starting Template Plugin");

        lock (_stateLock)
        {
            if (_state != PluginState.Initialized)
                throw new InvalidOperationException($"Plugin must be initialized before starting. Current state: {_state}");

            _state = PluginState.Running;
        }

        _logger.LogInformation("Template Plugin started successfully");
        await Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Stopping Template Plugin");

        lock (_stateLock)
        {
            if (_state == PluginState.Running)
                _state = PluginState.Stopped;
        }

        _logger.LogInformation("Template Plugin stopped successfully");
        await Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task<ServiceHealth> CheckHealthAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed)
        {
            return new ServiceHealth
            {
                IsHealthy = false,
                Status = HealthStatus.Unhealthy,
                Message = "Plugin has been disposed",
                CheckedAt = DateTimeOffset.UtcNow
            };
        }

        var currentState = Status;
        var isHealthy = currentState == PluginState.Running || currentState == PluginState.Initialized;
        
        return new ServiceHealth
        {
            IsHealthy = isHealthy,
            Status = isHealthy ? HealthStatus.Healthy : HealthStatus.Degraded,
            Message = $"Plugin is in {currentState} state",
            CheckedAt = DateTimeOffset.UtcNow
        };
    }

    /// <inheritdoc />
    public ServiceMetrics GetMetrics()
    {
        return new ServiceMetrics
        {
            ProcessedRows = 0, // Would be tracked by service during actual processing
            ErrorCount = 0,
            ProcessingRate = 0.0,
            AverageProcessingTime = TimeSpan.Zero,
            MemoryUsage = GC.GetTotalMemory(false),
            LastUpdated = DateTimeOffset.UtcNow
        };
    }

    /// <inheritdoc />
    public async Task<HotSwapResult> HotSwapAsync(
        IPluginConfiguration newConfiguration,
        CancellationToken cancellationToken = default)
    {
        var startTime = DateTimeOffset.UtcNow;
        _logger.LogInformation("Performing hot-swap for Template Plugin");

        if (newConfiguration is not TemplatePluginConfiguration newTemplateConfig)
        {
            return new HotSwapResult
            {
                Success = false,
                Message = $"Expected TemplatePluginConfiguration, got {newConfiguration?.GetType().Name ?? "null"}",
                SwapTime = TimeSpan.Zero
            };
        }

        try
        {
            // Validate new configuration
            var validator = new TemplatePluginValidator();
            var validationResult = await validator.ValidateAsync(newTemplateConfig, cancellationToken);

            if (!validationResult.IsValid)
            {
                return new HotSwapResult
                {
                    Success = false,
                    Message = "Hot-swap validation failed",
                    SwapTime = DateTimeOffset.UtcNow - startTime
                };
            }

            // Update configuration atomically
            var oldConfig = _configuration;
            _configuration = newTemplateConfig;

            _logger.LogInformation(
                "Hot-swap completed successfully. RowCount: {OldRowCount} → {NewRowCount}, BatchSize: {OldBatchSize} → {NewBatchSize}",
                oldConfig?.RowCount ?? 0,
                newTemplateConfig.RowCount,
                oldConfig?.BatchSize ?? 0,
                newTemplateConfig.BatchSize);

            return new HotSwapResult
            {
                Success = true,
                Message = "Configuration updated successfully",
                SwapTime = DateTimeOffset.UtcNow - startTime
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Hot-swap failed for Template Plugin");
            
            return new HotSwapResult
            {
                Success = false,
                Message = $"Hot-swap failed: {ex.Message}",
                SwapTime = DateTimeOffset.UtcNow - startTime
            };
        }
    }

    // === Component Factory Methods ===

    /// <inheritdoc />
    public IPluginProcessor GetProcessor()
    {
        return new TemplatePluginProcessor(this, _logger);
    }

    /// <inheritdoc />
    public IPluginService GetService()
    {
        return new TemplatePluginService(this, _logger);
    }

    /// <inheritdoc />
    public IPluginValidator<IPluginConfiguration> GetValidator()
    {
        return new TemplatePluginValidator();
    }

    // === IDisposable Implementation ===

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;

        _logger.LogInformation("Disposing Template Plugin");

        // Stop plugin if running
        if (Status == PluginState.Running)
            await StopAsync();

        lock (_stateLock)
        {
            _disposed = true;
            _state = PluginState.Disposed;
        }

        _logger.LogInformation("Template Plugin disposed successfully");
    }

    /// <inheritdoc />
    public void Dispose()
    {
        DisposeAsync().AsTask().GetAwaiter().GetResult();
    }
}