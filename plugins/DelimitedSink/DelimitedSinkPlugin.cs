using FlowEngine.Abstractions.Data;
using FlowEngine.Abstractions.Plugins;
using FlowEngine.Abstractions.Schema;
using Microsoft.Extensions.Logging;
using System.Collections.Immutable;
using System.Diagnostics;

namespace DelimitedSink;

/// <summary>
/// DelimitedSink plugin implementing the five-component architecture pattern.
/// Provides high-performance writing of delimited files with ArrayRow optimization.
/// </summary>
public sealed class DelimitedSinkPlugin : IPlugin
{
    private readonly ILogger<DelimitedSinkPlugin> _logger;
    private readonly DelimitedSinkValidator _validator;
    private readonly DelimitedSinkProcessor _processor;
    private readonly DelimitedSinkService _service;

    private DelimitedSinkConfiguration? _configuration;
    private PluginState _state = PluginState.Created;
    private bool _isInitialized;
    private bool _disposed;

    /// <summary>
    /// Gets the unique plugin identifier.
    /// </summary>
    public string Id => "delimited-sink";
    
    /// <summary>
    /// Gets the unique plugin identifier (legacy property).
    /// </summary>
    public string PluginId => Id;

    /// <summary>
    /// Gets the plugin name.
    /// </summary>
    public string Name => "DelimitedSink";

    /// <summary>
    /// Gets the plugin version.
    /// </summary>
    public string Version => "3.0.0";

    /// <summary>
    /// Gets the plugin type.
    /// </summary>
    public string Type => "Sink";
    
    /// <summary>
    /// Gets the plugin type (legacy property).
    /// </summary>
    public string PluginType => Type;

    /// <summary>
    /// Gets the plugin description.
    /// </summary>
    public string Description => "High-performance delimited file sink plugin with ArrayRow optimization";

    /// <summary>
    /// Gets the plugin author.
    /// </summary>
    public string Author => "FlowEngine";

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
    /// Gets supported plugin capabilities.
    /// </summary>
    public ImmutableArray<string> SupportedCapabilities => ImmutableArray.Create(
        "DelimitedOutput",
        "ArrayRowOptimization",
        "HighPerformance",
        "HotSwapping",
        "ParallelProcessing",
        "FileRotation",
        "Compression",
        "SchemaValidation"
    );

    /// <summary>
    /// Gets plugin dependencies.
    /// </summary>
    public ImmutableArray<string> Dependencies => ImmutableArray<string>.Empty;

    /// <summary>
    /// Initializes a new instance of the DelimitedSinkPlugin class.
    /// </summary>
    /// <param name="logger">Logger instance</param>
    /// <param name="jsonSchemaValidator">JSON schema validator (optional)</param>
    public DelimitedSinkPlugin(
        ILogger<DelimitedSinkPlugin> logger,
        IJsonSchemaValidator? jsonSchemaValidator = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        
        // Create validator with injected dependencies
        _validator = new DelimitedSinkValidator(
            logger as ILogger<DelimitedSinkValidator> ?? 
            throw new InvalidOperationException("Logger not available for validator"),
            jsonSchemaValidator);

        // Create service with logger
        _service = new DelimitedSinkService(
            logger as ILogger<DelimitedSinkService> ?? 
            throw new InvalidOperationException("Logger not available for service"));

        // Create processor with dependencies
        _processor = new DelimitedSinkProcessor(
            _service,
            logger as ILogger<DelimitedSinkProcessor> ?? 
            throw new InvalidOperationException("Logger not available for processor"));
    }

    /// <summary>
    /// Initializes the plugin with the specified configuration.
    /// </summary>
    /// <param name="configuration">Plugin configuration</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Initialization result with detailed information</returns>
    public async Task<PluginInitializationResult> InitializeAsync(IPluginConfiguration configuration, CancellationToken cancellationToken = default)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(DelimitedSinkPlugin));

        _logger.LogInformation("Initializing DelimitedSink plugin");

        try
        {
            if (configuration is not DelimitedSinkConfiguration sinkConfig)
            {
                return PluginInitializationResult.Failure(
                    "INVALID_CONFIGURATION_TYPE",
                    $"Expected DelimitedSinkConfiguration, got {configuration?.GetType().Name ?? "null"}",
                    TimeSpan.Zero);
            }

            // Validate configuration
            var validationResult = await _validator.ValidateAsync(sinkConfig, cancellationToken);
            if (!validationResult.IsValid)
            {
                var errorMessages = validationResult.Errors
                    .Select(e => $"{e.Code}: {e.Message}")
                    .ToArray();

                return PluginInitializationResult.Failure(
                    "CONFIGURATION_VALIDATION_FAILED",
                    $"Configuration validation failed: {string.Join("; ", errorMessages)}",
                    validationResult.ValidationTime,
                    validationResult.Errors,
                    validationResult.Warnings);
            }

            // Initialize service with configuration
            await _service.InitializeAsync(sinkConfig, cancellationToken);

            // Initialize processor with configuration
            await _processor.InitializeAsync(sinkConfig, cancellationToken);

            _configuration = sinkConfig;
            _isInitialized = true;

            _logger.LogInformation("DelimitedSink plugin initialized successfully");

            return PluginInitializationResult.Success(
                validationResult.ValidationTime,
                validationResult.Warnings,
                ImmutableDictionary<string, object>.Empty
                    .Add("InputSchema", sinkConfig.InputSchema?.ToString() ?? "Not specified")
                    .Add("FilePath", sinkConfig.FilePath)
                    .Add("BatchSize", sinkConfig.BatchSize)
                    .Add("EnableParallelProcessing", sinkConfig.EnableParallelProcessing)
                    .Add("MaxDegreeOfParallelism", sinkConfig.MaxDegreeOfParallelism));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize DelimitedSink plugin");
            return PluginInitializationResult.Failure(
                "INITIALIZATION_EXCEPTION",
                $"Plugin initialization failed: {ex.Message}",
                TimeSpan.Zero);
        }
    }

    /// <summary>
    /// Starts the plugin asynchronously.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Task representing the start operation</returns>
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Starting DelimitedSink plugin");

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
            
            if (_processor != null)
            {
                await _processor.StartAsync(cancellationToken);
            }
            
            _state = PluginState.Running;
            
            _logger.LogInformation("DelimitedSink plugin started successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start DelimitedSink plugin");
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
        _logger.LogInformation("Stopping DelimitedSink plugin");

        try
        {
            _state = PluginState.Stopping;
            
            if (_processor != null)
            {
                await _processor.StopAsync(cancellationToken);
            }
            
            if (_service != null)
            {
                await _service.StopAsync(cancellationToken);
            }
            
            _state = PluginState.Stopped;
            
            _logger.LogInformation("DelimitedSink plugin stopped successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to stop DelimitedSink plugin");
            _state = PluginState.Failed;
            throw;
        }
    }

    /// <summary>
    /// Checks the health of the plugin.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Health status</returns>
    public async Task<ServiceHealth> CheckHealthAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed)
        {
            return ServiceHealth.CreateUnhealthy(
                "PLUGIN_DISPOSED",
                "Plugin has been disposed",
                ImmutableDictionary<string, object>.Empty);
        }

        if (!_isInitialized)
        {
            return ServiceHealth.CreateUnhealthy(
                "PLUGIN_NOT_INITIALIZED",
                "Plugin has not been initialized",
                ImmutableDictionary<string, object>.Empty);
        }

        try
        {
            // Check service health
            var serviceHealth = await _service.CheckHealthAsync(cancellationToken);
            if (serviceHealth.Status != HealthStatus.Healthy)
            {
                return serviceHealth;
            }

            // Check processor health
            var processorHealth = await _processor.CheckHealthAsync(cancellationToken);
            if (processorHealth.Status != HealthStatus.Healthy)
            {
                return processorHealth;
            }

            // Check file system health
            var fileSystemHealth = await CheckFileSystemHealthAsync(cancellationToken);
            if (fileSystemHealth.Status != HealthStatus.Healthy)
            {
                return fileSystemHealth;
            }

            return ServiceHealth.CreateHealthy(
                "Plugin is healthy and ready to process data",
                ImmutableDictionary<string, object>.Empty
                    .Add("IsInitialized", _isInitialized)
                    .Add("Configuration", _configuration?.PluginId ?? "None")
                    .Add("LastHealthCheck", DateTimeOffset.UtcNow));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Health check failed");
            return ServiceHealth.CreateUnhealthy(
                "HEALTH_CHECK_EXCEPTION",
                $"Health check failed with exception: {ex.Message}",
                ImmutableDictionary<string, object>.Empty);
        }
    }

    /// <summary>
    /// Gets current plugin metrics.
    /// </summary>
    /// <returns>Plugin metrics</returns>
    public ServiceMetrics GetMetrics()
    {
        if (_disposed)
        {
            return ServiceMetrics.Empty;
        }

        var serviceMetrics = _service.GetMetrics();
        var processorMetrics = _processor.GetMetrics();

        return new ServiceMetrics
        {
            RequestCount = processorMetrics.RequestCount,
            ErrorCount = processorMetrics.ErrorCount + serviceMetrics.ErrorCount,
            AverageResponseTime = processorMetrics.AverageResponseTime,
            LastRequestTime = processorMetrics.LastRequestTime,
            Metadata = ImmutableDictionary<string, object>.Empty
                .Add("ServiceMetrics", serviceMetrics)
                .Add("ProcessorMetrics", processorMetrics)
                .Add("PluginType", PluginType)
                .Add("Version", Version)
        };
    }

    /// <summary>
    /// Performs hot-swapping of configuration.
    /// </summary>
    /// <param name="newConfiguration">New configuration</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Hot-swap result</returns>
    public async Task<HotSwapResult> HotSwapAsync(IPluginConfiguration newConfiguration, CancellationToken cancellationToken = default)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(DelimitedSinkPlugin));

        _logger.LogInformation("Performing hot-swap for DelimitedSink plugin");

        try
        {
            if (newConfiguration is not DelimitedSinkConfiguration newSinkConfig)
            {
                return HotSwapResult.Failure(
                    "INVALID_CONFIGURATION_TYPE",
                    $"Expected DelimitedSinkConfiguration, got {newConfiguration?.GetType().Name ?? "null"}",
                    TimeSpan.Zero);
            }

            // Validate new configuration
            var validationResult = await _validator.ValidateAsync(newSinkConfig, cancellationToken);
            if (!validationResult.IsValid)
            {
                var errorMessages = validationResult.Errors
                    .Select(e => $"{e.Code}: {e.Message}")
                    .ToArray();

                return HotSwapResult.Failure(
                    "CONFIGURATION_VALIDATION_FAILED",
                    $"New configuration validation failed: {string.Join("; ", errorMessages)}",
                    validationResult.ValidationTime);
            }

            // Perform hot-swap on service
            var serviceSwapResult = await _service.HotSwapAsync(newSinkConfig, cancellationToken);
            if (!serviceSwapResult.IsSuccessful)
            {
                return serviceSwapResult;
            }

            // Perform hot-swap on processor
            var processorSwapResult = await _processor.HotSwapAsync(newSinkConfig, cancellationToken);
            if (!processorSwapResult.IsSuccessful)
            {
                // Rollback service if processor fails
                await _service.HotSwapAsync(_configuration!, cancellationToken);
                return processorSwapResult;
            }

            _configuration = newSinkConfig;

            _logger.LogInformation("DelimitedSink plugin hot-swap completed successfully");

            return HotSwapResult.Success(
                validationResult.ValidationTime,
                validationResult.Warnings,
                ImmutableDictionary<string, object>.Empty
                    .Add("PreviousFilePath", _configuration?.FilePath ?? "Unknown")
                    .Add("NewFilePath", newSinkConfig.FilePath)
                    .Add("ConfigurationChanged", true));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Hot-swap failed");
            return HotSwapResult.Failure(
                "HOT_SWAP_EXCEPTION",
                $"Hot-swap failed with exception: {ex.Message}",
                TimeSpan.Zero);
        }
    }

    /// <summary>
    /// Gets the plugin processor.
    /// </summary>
    /// <returns>Plugin processor</returns>
    public IPluginProcessor GetProcessor()
    {
        return _processor;
    }

    /// <summary>
    /// Gets the plugin service.
    /// </summary>
    /// <returns>Plugin service</returns>
    public IPluginService GetService()
    {
        return _service;
    }

    /// <summary>
    /// Gets the plugin validator.
    /// </summary>
    /// <returns>Plugin validator</returns>
    public IPluginValidator<IPluginConfiguration> GetValidator()
    {
        return _validator;
    }

    /// <summary>
    /// Checks file system health for write operations.
    /// </summary>
    private async Task<ServiceHealth> CheckFileSystemHealthAsync(CancellationToken cancellationToken)
    {
        try
        {
            if (_configuration == null)
            {
                return ServiceHealth.CreateDegraded(
                    "NO_CONFIGURATION",
                    "No configuration available for file system check",
                    ImmutableDictionary<string, object>.Empty);
            }

            var directoryPath = Path.GetDirectoryName(_configuration.FilePath) ?? Directory.GetCurrentDirectory();

            // Check if directory exists and is writable
            if (!Directory.Exists(directoryPath))
            {
                return ServiceHealth.CreateDegraded(
                    "OUTPUT_DIRECTORY_NOT_EXISTS",
                    $"Output directory does not exist: {directoryPath}",
                    ImmutableDictionary<string, object>.Empty
                        .Add("DirectoryPath", directoryPath));
            }

            // Test write access
            var testFile = Path.Combine(directoryPath, $"flowengine_health_{Guid.NewGuid():N}.tmp");
            try
            {
                await File.WriteAllTextAsync(testFile, "health_check", cancellationToken);
                File.Delete(testFile);
            }
            catch (UnauthorizedAccessException)
            {
                return ServiceHealth.CreateUnhealthy(
                    "NO_WRITE_PERMISSION",
                    $"No write permission for directory: {directoryPath}",
                    ImmutableDictionary<string, object>.Empty
                        .Add("DirectoryPath", directoryPath));
            }

            // Check disk space
            var drive = new DriveInfo(directoryPath);
            var availableSpace = drive.AvailableFreeSpace;
            var availableSpaceMB = availableSpace / (1024 * 1024);

            if (availableSpaceMB < 100) // Less than 100MB
            {
                return ServiceHealth.CreateDegraded(
                    "LOW_DISK_SPACE",
                    $"Low disk space: {availableSpaceMB}MB available",
                    ImmutableDictionary<string, object>.Empty
                        .Add("AvailableSpaceMB", availableSpaceMB)
                        .Add("DriveName", drive.Name));
            }

            return ServiceHealth.CreateHealthy(
                "File system is healthy and accessible",
                ImmutableDictionary<string, object>.Empty
                    .Add("DirectoryPath", directoryPath)
                    .Add("AvailableSpaceMB", availableSpaceMB)
                    .Add("DriveName", drive.Name));
        }
        catch (Exception ex)
        {
            return ServiceHealth.CreateUnhealthy(
                "FILE_SYSTEM_CHECK_FAILED",
                $"File system health check failed: {ex.Message}",
                ImmutableDictionary<string, object>.Empty);
        }
    }

    /// <summary>
    /// Disposes the plugin asynchronously.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;

        _logger.LogInformation("Disposing DelimitedSink plugin");

        try
        {
            if (_processor != null)
                await _processor.DisposeAsync();

            if (_service != null)
                await _service.DisposeAsync();

            _disposed = true;
            _logger.LogInformation("DelimitedSink plugin disposed successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during plugin disposal");
            throw;
        }
    }

    /// <summary>
    /// Disposes the plugin synchronously.
    /// </summary>
    public void Dispose()
    {
        DisposeAsync().AsTask().GetAwaiter().GetResult();
    }
}