using FlowEngine.Abstractions.Data;
using FlowEngine.Abstractions.Plugins;
using FlowEngine.Abstractions.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Collections.Immutable;
using System.Diagnostics;

namespace JavaScriptTransform;

/// <summary>
/// Main plugin class for JavaScriptTransform implementing the five-component architecture.
/// Provides lifecycle management, dependency injection, and plugin metadata.
/// </summary>
public sealed class JavaScriptTransformPlugin : IPlugin
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<JavaScriptTransformPlugin> _logger;
    private JavaScriptTransformConfiguration? _configuration;
    private JavaScriptTransformValidator? _validator;
    private JavaScriptTransformService? _service;
    private JavaScriptTransformProcessor? _processor;
    private bool _isInitialized;
    private bool _isDisposed;

    /// <summary>
    /// Gets the unique plugin identifier.
    /// </summary>
    public string Id => "javascript-transform";

    /// <summary>
    /// Gets the plugin name.
    /// </summary>
    public string Name => "JavaScript Transform";

    /// <summary>
    /// Gets the plugin version.
    /// </summary>
    public string Version => "3.0.0";

    /// <summary>
    /// Gets the plugin description.
    /// </summary>
    public string Description => "High-performance JavaScript transformation plugin with ArrayRow optimization and script engine integration";

    /// <summary>
    /// Gets the plugin author.
    /// </summary>
    public string Author => "FlowEngine";

    /// <summary>
    /// Gets the plugin type.
    /// </summary>
    public string Type => "Transform";

    /// <summary>
    /// Gets whether the plugin supports hot-swapping.
    /// </summary>
    public bool SupportsHotSwapping => true;

    /// <summary>
    /// Gets whether the plugin is currently initialized.
    /// </summary>
    public bool IsInitialized => _isInitialized && !_isDisposed;

    /// <summary>
    /// Gets the current plugin status.
    /// </summary>
    public PluginState Status 
    { 
        get 
        {
            if (_isDisposed) return PluginState.Disposed;
            if (!_isInitialized) return PluginState.Created;
            return PluginState.Running;
        } 
    }

    /// <summary>
    /// Gets the plugin configuration.
    /// </summary>
    public IPluginConfiguration? Configuration => _configuration;

    /// <summary>
    /// Gets additional plugin metadata.
    /// </summary>
    public IReadOnlyDictionary<string, object> Metadata { get; }

    /// <summary>
    /// Initializes a new instance of the JavaScriptTransformPlugin class.
    /// </summary>
    /// <param name="serviceProvider">Service provider for dependency injection</param>
    /// <param name="logger">Logger instance</param>
    public JavaScriptTransformPlugin(IServiceProvider serviceProvider, ILogger<JavaScriptTransformPlugin> logger)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        Metadata = new Dictionary<string, object>
        {
            ["SupportedEngineTypes"] = new[] { "Jint", "V8" },
            ["PerformanceProfile"] = "High",
            ["MemoryUsage"] = "Moderate",
            ["CpuIntensive"] = true,
            ["ThreadSafe"] = true,
            ["SchemaEvolution"] = "Supported",
            ["ArrayRowOptimized"] = true,
            ["FiveComponentArchitecture"] = true
        };
    }

    /// <summary>
    /// Initializes the plugin with the specified configuration.
    /// </summary>
    /// <param name="configuration">Plugin configuration</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Initialization result</returns>
    public async Task<PluginInitializationResult> InitializeAsync(IPluginConfiguration configuration, CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        
        try
        {
            _logger.LogInformation("Initializing JavaScript transform plugin");

            if (_isDisposed)
                throw new ObjectDisposedException(nameof(JavaScriptTransformPlugin));

            if (configuration is not JavaScriptTransformConfiguration jsConfig)
            {
                return new PluginInitializationResult
                {
                    Success = false,
                    Message = $"Invalid configuration type. Expected {nameof(JavaScriptTransformConfiguration)}, got {configuration?.GetType().Name ?? "null"}",
                    InitializationTime = stopwatch.Elapsed
                };
            }

            // Validate configuration first
            _validator = new JavaScriptTransformValidator(_serviceProvider.GetRequiredService<ILogger<JavaScriptTransformValidator>>());
            var validationResult = await _validator.ValidateAsync(jsConfig, cancellationToken);
            
            if (!validationResult.IsValid)
            {
                var errorMessages = string.Join("; ", validationResult.Errors.Select(e => e.Message));
                return new PluginInitializationResult
                {
                    Success = false,
                    Message = $"Configuration validation failed: {errorMessages}",
                    InitializationTime = stopwatch.Elapsed
                };
            }

            _configuration = jsConfig;

            // Initialize service with dependencies
            var scriptEngineService = _serviceProvider.GetRequiredService<IScriptEngineService>();
            var arrayRowFactory = _serviceProvider.GetRequiredService<IArrayRowFactory>();
            var serviceLogger = _serviceProvider.GetRequiredService<ILogger<JavaScriptTransformService>>();

            _service = new JavaScriptTransformService(scriptEngineService, arrayRowFactory, _configuration, serviceLogger);
            await _service.InitializeAsync(cancellationToken);
            await _service.StartAsync(cancellationToken);

            // Initialize processor
            var processorLogger = _serviceProvider.GetRequiredService<ILogger<JavaScriptTransformProcessor>>();
            _processor = new JavaScriptTransformProcessor(_service, _configuration, processorLogger);

            _isInitialized = true;
            stopwatch.Stop();

            _logger.LogInformation("JavaScript transform plugin initialized successfully in {ElapsedMs}ms", stopwatch.ElapsedMilliseconds);

            return new PluginInitializationResult
            {
                Success = true,
                Message = "JavaScript transform plugin initialized successfully",
                InitializationTime = stopwatch.Elapsed
            };
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogError(ex, "Failed to initialize JavaScript transform plugin after {ElapsedMs}ms", stopwatch.ElapsedMilliseconds);

            return new PluginInitializationResult
            {
                Success = false,
                Message = $"Initialization failed: {ex.Message}",
                InitializationTime = stopwatch.Elapsed
            };
        }
    }

    /// <summary>
    /// Starts the plugin asynchronously.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Task representing the start operation</returns>
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (!_isInitialized)
            throw new InvalidOperationException("Plugin must be initialized before starting");

        if (_isDisposed)
            throw new ObjectDisposedException(nameof(JavaScriptTransformPlugin));

        _logger.LogInformation("Starting JavaScript transform plugin");

        if (_service != null)
        {
            await _service.StartAsync(cancellationToken);
        }

        _logger.LogInformation("JavaScript transform plugin started successfully");
    }

    /// <summary>
    /// Stops the plugin asynchronously.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Task representing the stop operation</returns>
    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (!_isInitialized)
            return;

        _logger.LogInformation("Stopping JavaScript transform plugin");

        if (_service != null)
        {
            await _service.StopAsync(cancellationToken);
        }

        _logger.LogInformation("JavaScript transform plugin stopped successfully");
    }

    /// <summary>
    /// Performs hot-swap of the plugin configuration.
    /// </summary>
    /// <param name="newConfiguration">New configuration</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Hot-swap result</returns>
    public async Task<HotSwapResult> HotSwapAsync(IPluginConfiguration newConfiguration, CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();

        try
        {
            _logger.LogInformation("Performing hot-swap for JavaScript transform plugin");

            if (!_isInitialized)
                throw new InvalidOperationException("Plugin must be initialized before hot-swapping");

            if (_isDisposed)
                throw new ObjectDisposedException(nameof(JavaScriptTransformPlugin));

            if (newConfiguration is not JavaScriptTransformConfiguration jsConfig)
            {
                return new HotSwapResult
                {
                    Success = false,
                    Message = $"Invalid configuration type for hot-swap. Expected {nameof(JavaScriptTransformConfiguration)}",
                    SwapTime = stopwatch.Elapsed
                };
            }

            // Validate new configuration
            if (_validator != null)
            {
                var validationResult = await _validator.ValidateAsync(jsConfig, cancellationToken);
                if (!validationResult.IsValid)
                {
                    var errorMessages = string.Join("; ", validationResult.Errors.Select(e => e.Message));
                    return new HotSwapResult
                    {
                        Success = false,
                        Message = $"New configuration validation failed: {errorMessages}",
                        SwapTime = stopwatch.Elapsed
                    };
                }
            }

            // Perform hot-swap on service
            if (_service != null)
            {
                await _service.HotSwapConfigurationAsync(jsConfig, cancellationToken);
            }

            _configuration = jsConfig;
            stopwatch.Stop();

            _logger.LogInformation("Hot-swap completed successfully for JavaScript transform plugin in {ElapsedMs}ms", stopwatch.ElapsedMilliseconds);

            return new HotSwapResult
            {
                Success = true,
                Message = "Hot-swap completed successfully",
                SwapTime = stopwatch.Elapsed
            };
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogError(ex, "Hot-swap failed for JavaScript transform plugin after {ElapsedMs}ms", stopwatch.ElapsedMilliseconds);

            return new HotSwapResult
            {
                Success = false,
                Message = $"Hot-swap failed: {ex.Message}",
                SwapTime = stopwatch.Elapsed
            };
        }
    }

    /// <summary>
    /// Gets the plugin processor for data processing operations.
    /// </summary>
    /// <returns>Plugin processor instance</returns>
    /// <exception cref="InvalidOperationException">Plugin not initialized</exception>
    public IPluginProcessor GetProcessor()
    {
        if (!_isInitialized)
            throw new InvalidOperationException("Plugin must be initialized before getting processor");

        if (_isDisposed)
            throw new ObjectDisposedException(nameof(JavaScriptTransformPlugin));

        return _processor ?? throw new InvalidOperationException("Processor not available");
    }

    /// <summary>
    /// Gets the plugin service for business logic operations.
    /// </summary>
    /// <returns>Plugin service instance</returns>
    /// <exception cref="InvalidOperationException">Plugin not initialized</exception>
    public IPluginService GetService()
    {
        if (!_isInitialized)
            throw new InvalidOperationException("Plugin must be initialized before getting service");

        if (_isDisposed)
            throw new ObjectDisposedException(nameof(JavaScriptTransformPlugin));

        return _service ?? throw new InvalidOperationException("Service not available");
    }

    /// <summary>
    /// Gets the plugin validator for configuration validation.
    /// </summary>
    /// <returns>Plugin validator instance</returns>
    public IPluginValidator GetValidator()
    {
        return _validator ?? new JavaScriptTransformValidator(_serviceProvider.GetRequiredService<ILogger<JavaScriptTransformValidator>>());
    }

    /// <summary>
    /// Checks the health of the plugin.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Plugin health status</returns>
    public async Task<ServiceHealth> CheckHealthAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            if (!_isInitialized)
            {
                return new ServiceHealth
                {
                    Status = HealthStatus.Unhealthy,
                    Message = "Plugin is not initialized",
                    CheckedAt = DateTimeOffset.UtcNow
                };
            }

            if (_isDisposed)
            {
                return new ServiceHealth
                {
                    Status = HealthStatus.Unhealthy,
                    Message = "Plugin is disposed",
                    CheckedAt = DateTimeOffset.UtcNow
                };
            }

            // Check service health
            if (_service != null)
            {
                var serviceHealth = await _service.CheckHealthAsync(cancellationToken);
                if (serviceHealth.Status != HealthStatus.Healthy)
                {
                    return new ServiceHealth
                    {
                        Status = serviceHealth.Status,
                        Message = $"Service health check failed: {serviceHealth.Message}",
                        CheckedAt = DateTimeOffset.UtcNow,
                        Details = serviceHealth.Details
                    };
                }
            }

            return new ServiceHealth
            {
                Status = HealthStatus.Healthy,
                Message = "JavaScript transform plugin is healthy and ready",
                CheckedAt = DateTimeOffset.UtcNow,
                Details = new Dictionary<string, object>
                {
                    ["PluginId"] = Id,
                    ["Version"] = Version,
                    ["IsInitialized"] = _isInitialized,
                    ["SupportsHotSwapping"] = SupportsHotSwapping,
                    ["ConfigurationValid"] = _configuration != null
                }.ToImmutableDictionary()
            };
        }
        catch (Exception ex)
        {
            return new ServiceHealth
            {
                Status = HealthStatus.Unhealthy,
                Message = $"Health check failed: {ex.Message}",
                CheckedAt = DateTimeOffset.UtcNow
            };
        }
    }

    /// <summary>
    /// Gets current plugin metrics.
    /// </summary>
    /// <returns>Plugin performance metrics</returns>
    public ServiceMetrics GetMetrics()
    {
        if (_service != null)
        {
            return _service.GetMetrics();
        }

        return new ServiceMetrics
        {
            ProcessedRows = 0,
            ErrorCount = 0,
            ProcessingRate = 0,
            AverageProcessingTime = TimeSpan.Zero,
            MemoryUsage = GC.GetTotalMemory(false),
            LastUpdated = DateTimeOffset.UtcNow
        };
    }

    /// <summary>
    /// Disposes the plugin and releases all resources.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (_isDisposed)
            return;

        _logger.LogInformation("Disposing JavaScript transform plugin");

        try
        {
            if (_isInitialized)
            {
                await StopAsync();
            }

            _service?.Dispose();
            _processor?.Dispose();

            _isDisposed = true;
            _logger.LogInformation("JavaScript transform plugin disposed successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error occurred while disposing JavaScript transform plugin");
        }
    }

    /// <summary>
    /// Disposes the plugin and releases all resources.
    /// </summary>
    public void Dispose()
    {
        DisposeAsync().AsTask().GetAwaiter().GetResult();
    }
}

/// <summary>
/// Factory for creating JavaScriptTransformPlugin instances.
/// Used by the plugin loading system for dependency injection.
/// </summary>
public sealed class JavaScriptTransformPluginFactory : IPluginFactory
{
    private readonly IServiceProvider _serviceProvider;

    /// <summary>
    /// Initializes a new instance of the JavaScriptTransformPluginFactory class.
    /// </summary>
    /// <param name="serviceProvider">Service provider for dependency injection</param>
    public JavaScriptTransformPluginFactory(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
    }

    /// <summary>
    /// Creates a new plugin instance.
    /// </summary>
    /// <returns>Plugin instance</returns>
    public IPlugin CreatePlugin()
    {
        var logger = _serviceProvider.GetRequiredService<ILogger<JavaScriptTransformPlugin>>();
        return new JavaScriptTransformPlugin(_serviceProvider, logger);
    }

    /// <summary>
    /// Gets the plugin type supported by this factory.
    /// </summary>
    public Type PluginType => typeof(JavaScriptTransformPlugin);

    /// <summary>
    /// Gets the plugin identifier.
    /// </summary>
    public string PluginId => "javascript-transform";
}