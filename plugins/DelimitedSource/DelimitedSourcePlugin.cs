using FlowEngine.Abstractions.Plugins;
using Microsoft.Extensions.Logging;
using System.Collections.Immutable;
using System.Diagnostics;

namespace DelimitedSource;

/// <summary>
/// Main plugin class for DelimitedSource implementing the five-component architecture.
/// Provides lifecycle management and metadata for the delimited file source plugin.
/// </summary>
public sealed class DelimitedSourcePlugin : IPlugin
{
    private readonly ILogger<DelimitedSourcePlugin> _logger;
    private DelimitedSourceConfiguration? _configuration;
    private DelimitedSourceValidator? _validator;
    private DelimitedSourceProcessor? _processor;
    private DelimitedSourceService? _service;
    private PluginState _state = PluginState.Created;
    private bool _isDisposed;

    /// <summary>
    /// Gets the unique plugin identifier.
    /// </summary>
    public string Id => "delimited-source";

    /// <summary>
    /// Gets the plugin name.
    /// </summary>
    public string Name => "DelimitedSource";

    /// <summary>
    /// Gets the plugin version.
    /// </summary>
    public string Version => "3.0.0";

    /// <summary>
    /// Gets the plugin type.
    /// </summary>
    public string Type => "Source";

    /// <summary>
    /// Gets the plugin description.
    /// </summary>
    public string Description => "High-performance delimited file source plugin with ArrayRow optimization. Supports CSV, TSV, and custom delimited formats with configurable encoding, batching, and parallel processing.";

    /// <summary>
    /// Gets the plugin author.
    /// </summary>
    public string Author => "FlowEngine Team";

    /// <summary>
    /// Gets whether the plugin supports hot-swapping.
    /// </summary>
    public bool SupportsHotSwapping => true;

    /// <summary>
    /// Gets the current plugin status.
    /// </summary>
    public PluginState Status => _state;

    /// <summary>
    /// Gets the plugin configuration.
    /// </summary>
    public IPluginConfiguration? Configuration => _configuration;

    /// <summary>
    /// Initializes a new instance of the DelimitedSourcePlugin class.
    /// </summary>
    /// <param name="logger">Logger instance</param>
    public DelimitedSourcePlugin(ILogger<DelimitedSourcePlugin> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
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
            _logger.LogInformation("Initializing DelimitedSource plugin");

            if (_isDisposed)
                throw new ObjectDisposedException(nameof(DelimitedSourcePlugin));

            if (configuration is not DelimitedSourceConfiguration delimitedConfig)
            {
                return new PluginInitializationResult
                {
                    Success = false,
                    Message = $"Invalid configuration type. Expected {nameof(DelimitedSourceConfiguration)}, got {configuration?.GetType().Name ?? "null"}",
                    InitializationTime = stopwatch.Elapsed
                };
            }

            _state = PluginState.Initializing;
            _configuration = delimitedConfig;

            // Create validator
            _validator = new DelimitedSourceValidator(_logger as ILogger<DelimitedSourceValidator> ?? 
                throw new InvalidOperationException("Logger not available for validator"));

            // Validate configuration
            var validationResult = await _validator.ValidateAsync(_configuration, cancellationToken);
            if (!validationResult.IsValid)
            {
                _state = PluginState.Failed;
                var errorMessages = string.Join("; ", validationResult.Errors.Select(e => e.Message));
                return new PluginInitializationResult
                {
                    Success = false,
                    Message = $"Configuration validation failed: {errorMessages}",
                    InitializationTime = stopwatch.Elapsed
                };
            }

            // Create service
            _service = new DelimitedSourceService(_configuration, 
                _logger as ILogger<DelimitedSourceService> ?? 
                throw new InvalidOperationException("Logger not available for service"));

            // Initialize service
            await _service.InitializeAsync(cancellationToken);
            await _service.StartAsync(cancellationToken);

            // Create processor 
            _processor = new DelimitedSourceProcessor(_service, _configuration,
                _logger as ILogger<DelimitedSourceProcessor> ?? 
                throw new InvalidOperationException("Logger not available for processor"));

            _state = PluginState.Initialized;
            stopwatch.Stop();

            _logger.LogInformation("DelimitedSource plugin initialized successfully in {ElapsedMs}ms", stopwatch.ElapsedMilliseconds);

            return new PluginInitializationResult
            {
                Success = true,
                Message = "DelimitedSource plugin initialized successfully",
                InitializationTime = stopwatch.Elapsed
            };
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogError(ex, "Failed to initialize DelimitedSource plugin after {ElapsedMs}ms", stopwatch.ElapsedMilliseconds);
            _state = PluginState.Failed;

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
        _logger.LogInformation("Starting DelimitedSource plugin");

        if (_state != PluginState.Initialized)
        {
            throw new InvalidOperationException($"Plugin must be initialized before starting. Current status: {_state}");
        }

        try
        {
            _state = PluginState.Starting;
            if (_service != null)
            {
                await _service.StartAsync(cancellationToken);
            }
            _state = PluginState.Running;
            
            _logger.LogInformation("DelimitedSource plugin started successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start DelimitedSource plugin");
            _state = PluginState.Failed;
            throw;
        }
    }

    /// <summary>
    /// Stops the plugin asynchronously.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Task representing the stop operation</returns>
    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Stopping DelimitedSource plugin");

        try
        {
            _state = PluginState.Stopping;
            if (_service != null)
            {
                await _service.StopAsync(cancellationToken);
            }
            _state = PluginState.Stopped;
            
            _logger.LogInformation("DelimitedSource plugin stopped successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to stop DelimitedSource plugin");
            _state = PluginState.Failed;
            throw;
        }
    }

    /// <summary>
    /// Checks the health of the plugin asynchronously.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Current plugin health status</returns>
    public async Task<ServiceHealth> CheckHealthAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            if (_isDisposed)
            {
                return new ServiceHealth
                {
                    Status = HealthStatus.Unhealthy,
                    Message = "Plugin is disposed",
                    CheckedAt = DateTimeOffset.UtcNow
                };
            }

            if (_configuration == null)
            {
                return new ServiceHealth
                {
                    Status = HealthStatus.Unhealthy,
                    Message = "Plugin is not initialized",
                    CheckedAt = DateTimeOffset.UtcNow
                };
            }

            // Check file accessibility
            if (!File.Exists(_configuration.FilePath))
            {
                return new ServiceHealth
                {
                    Status = HealthStatus.Unhealthy,
                    Message = $"Source file not accessible: {_configuration.FilePath}",
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
                Message = "DelimitedSource plugin is healthy and ready",
                CheckedAt = DateTimeOffset.UtcNow,
                Details = new Dictionary<string, object>
                {
                    ["PluginId"] = Id,
                    ["Version"] = Version,
                    ["State"] = _state.ToString(),
                    ["FilePath"] = _configuration.FilePath,
                    ["ConfigurationValid"] = _configuration != null
                }.ToImmutableDictionary()
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Health check failed for DelimitedSource plugin");
            
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
    /// Hot-swaps the plugin configuration.
    /// </summary>
    /// <param name="newConfiguration">New configuration</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Hot-swap result</returns>
    public async Task<HotSwapResult> HotSwapAsync(IPluginConfiguration newConfiguration, CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();

        try
        {
            _logger.LogInformation("Attempting hot-swap for DelimitedSource plugin");

            if (!SupportsHotSwapping)
            {
                return new HotSwapResult
                {
                    Success = false,
                    Message = "Plugin does not support hot-swapping",
                    SwapTime = stopwatch.Elapsed
                };
            }

            if (newConfiguration is not DelimitedSourceConfiguration newConfig)
            {
                return new HotSwapResult
                {
                    Success = false,
                    Message = $"Invalid configuration type. Expected {nameof(DelimitedSourceConfiguration)}",
                    SwapTime = stopwatch.Elapsed
                };
            }

            // Validate new configuration
            if (_validator != null)
            {
                var validationResult = await _validator.ValidateAsync(newConfig, cancellationToken);
                if (!validationResult.IsValid)
                {
                    var errorMessages = string.Join("; ", validationResult.Errors.Select(e => e.Message));
                    return new HotSwapResult
                    {
                        Success = false,
                        Message = $"Configuration validation failed: {errorMessages}",
                        SwapTime = stopwatch.Elapsed
                    };
                }
            }

            // Perform hot-swap in service
            if (_service != null)
            {
                await _service.HotSwapConfigurationAsync(newConfig, cancellationToken);
            }

            _configuration = newConfig;
            stopwatch.Stop();

            _logger.LogInformation("Hot-swap completed successfully for DelimitedSource plugin in {ElapsedMs}ms", stopwatch.ElapsedMilliseconds);

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
            _logger.LogError(ex, "Hot-swap failed for DelimitedSource plugin after {ElapsedMs}ms", stopwatch.ElapsedMilliseconds);

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
    public IPluginProcessor GetProcessor()
    {
        if (_processor == null)
            throw new InvalidOperationException("Plugin must be initialized before getting processor");

        return _processor;
    }

    /// <summary>
    /// Gets the plugin service for business logic operations.
    /// </summary>
    /// <returns>Plugin service instance</returns>
    public IPluginService GetService()
    {
        if (_service == null)
            throw new InvalidOperationException("Plugin must be initialized before getting service");

        return _service;
    }

    /// <summary>
    /// Gets the plugin validator for configuration validation.
    /// </summary>
    /// <returns>Plugin validator instance</returns>
    public IPluginValidator GetValidator()
    {
        return _validator ?? new DelimitedSourceValidator(_logger as ILogger<DelimitedSourceValidator> ?? 
            throw new InvalidOperationException("Logger not available for validator"));
    }

    /// <summary>
    /// Disposes the plugin and releases resources asynchronously.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (_isDisposed)
            return;

        _logger.LogInformation("Disposing DelimitedSource plugin");

        try
        {
            if (_state == PluginState.Running)
            {
                await StopAsync();
            }

            _service?.Dispose();
            _processor?.Dispose();

            _isDisposed = true;
            _state = PluginState.Disposed;
            _logger.LogInformation("DelimitedSource plugin disposed successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error occurred while disposing DelimitedSource plugin");
        }
    }

    /// <summary>
    /// Disposes the plugin and releases resources.
    /// </summary>
    public void Dispose()
    {
        DisposeAsync().AsTask().GetAwaiter().GetResult();
    }
}