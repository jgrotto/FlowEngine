using FlowEngine.Abstractions.Data;
using FlowEngine.Abstractions.Plugins;
using Microsoft.Extensions.Logging;
using System.Collections.Immutable;

namespace JavaScriptTransform;

/// <summary>
/// Minimal working JavaScript transform plugin implementing core interfaces directly.
/// This serves as our foundation for the proper five-component implementation.
/// </summary>
public sealed class MinimalJavaScriptTransformConfiguration : IPluginConfiguration
{
    public required string Script { get; init; }
    public int TimeoutMs { get; init; } = 5000;
    public bool ContinueOnError { get; init; } = false;
    
    // Required by IPluginConfiguration
    public required string PluginId { get; init; }
    public required string PluginType { get; init; }
    public required string Version { get; init; }
    public bool SupportsHotSwapping { get; init; } = true;
    public IReadOnlyDictionary<string, object>? Parameters { get; init; }
}

/// <summary>
/// Minimal working JavaScript transform plugin.
/// </summary>
public sealed class MinimalJavaScriptTransformPlugin : IPlugin
{
    private readonly ILogger<MinimalJavaScriptTransformPlugin> _logger;
    private MinimalJavaScriptTransformConfiguration? _configuration;
    private PluginState _state = PluginState.Created;
    private bool _disposed;

    public MinimalJavaScriptTransformPlugin(ILogger<MinimalJavaScriptTransformPlugin> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    // IPlugin implementation
    public string Id => "minimal-javascript-transform";
    public string Name => "Minimal JavaScript Transform";
    public string Version => "3.0.0";
    public string Type => "Transform";
    public string Description => "Minimal JavaScript transformation plugin";
    public string Author => "FlowEngine Team";
    public bool SupportsHotSwapping => true;
    public PluginState Status => _state;
    public IPluginConfiguration? Configuration => _configuration;
    public ImmutableArray<string> SupportedCapabilities => ImmutableArray.Create("JavaScriptExecution");
    public ImmutableArray<string> Dependencies => ImmutableArray<string>.Empty;

    public async Task<PluginInitializationResult> InitializeAsync(
        IPluginConfiguration configuration, 
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Initializing minimal JavaScript transform plugin");

        if (configuration is not MinimalJavaScriptTransformConfiguration jsConfig)
        {
            return PluginInitializationResult.Failure(
                "INVALID_CONFIGURATION_TYPE",
                $"Expected MinimalJavaScriptTransformConfiguration, got {configuration?.GetType().Name ?? "null"}",
                TimeSpan.Zero);
        }

        if (string.IsNullOrWhiteSpace(jsConfig.Script))
        {
            return PluginInitializationResult.Failure(
                "INVALID_SCRIPT",
                "Script cannot be null or empty",
                TimeSpan.Zero);
        }

        _configuration = jsConfig;
        _state = PluginState.Initialized;

        _logger.LogInformation("Minimal JavaScript transform plugin initialized successfully");

        return PluginInitializationResult.Success(
            TimeSpan.FromMilliseconds(1),
            ImmutableArray<ValidationWarning>.Empty,
            ImmutableDictionary<string, object>.Empty
                .Add("ScriptLength", jsConfig.Script.Length)
                .Add("TimeoutMs", jsConfig.TimeoutMs));
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Starting minimal JavaScript transform plugin");
        
        if (_state != PluginState.Initialized)
            throw new InvalidOperationException($"Plugin must be initialized before starting. Current status: {_state}");

        _state = PluginState.Running;
        _logger.LogInformation("Minimal JavaScript transform plugin started successfully");
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Stopping minimal JavaScript transform plugin");
        _state = PluginState.Stopped;
        _logger.LogInformation("Minimal JavaScript transform plugin stopped successfully");
    }

    public async Task<ServiceHealth> CheckHealthAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed)
        {
            return ServiceHealth.CreateUnhealthy(
                "PLUGIN_DISPOSED",
                "Plugin has been disposed",
                ImmutableDictionary<string, object>.Empty);
        }

        return ServiceHealth.CreateHealthy(
            "Plugin is healthy and ready",
            ImmutableDictionary<string, object>.Empty
                .Add("State", _state.ToString())
                .Add("LastHealthCheck", DateTimeOffset.UtcNow));
    }

    public ServiceMetrics GetMetrics()
    {
        return new ServiceMetrics
        {
            RequestCount = 0,
            ErrorCount = 0,
            AverageResponseTime = TimeSpan.Zero,
            LastRequestTime = null,
            Metadata = ImmutableDictionary<string, object>.Empty
                .Add("PluginType", Type)
                .Add("Version", Version)
        };
    }

    public async Task<HotSwapResult> HotSwapAsync(
        IPluginConfiguration newConfiguration, 
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Performing hot-swap for minimal JavaScript transform plugin");

        if (newConfiguration is not MinimalJavaScriptTransformConfiguration newJsConfig)
        {
            return HotSwapResult.Failure(
                "INVALID_CONFIGURATION_TYPE",
                $"Expected MinimalJavaScriptTransformConfiguration, got {newConfiguration?.GetType().Name ?? "null"}",
                TimeSpan.Zero);
        }

        _configuration = newJsConfig;
        _logger.LogInformation("Hot-swap completed successfully");

        return HotSwapResult.Success(
            TimeSpan.FromMilliseconds(1),
            ImmutableArray<ValidationWarning>.Empty,
            ImmutableDictionary<string, object>.Empty
                .Add("ConfigurationChanged", true));
    }

    // Placeholder implementations for now
    public IPluginProcessor GetProcessor() => throw new NotImplementedException("Processor not implemented yet");
    public IPluginService GetService() => throw new NotImplementedException("Service not implemented yet");
    public IPluginValidator<IPluginConfiguration> GetValidator() => throw new NotImplementedException("Validator not implemented yet");

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;

        _logger.LogInformation("Disposing minimal JavaScript transform plugin");
        
        if (_state == PluginState.Running)
            await StopAsync();

        _disposed = true;
        _state = PluginState.Disposed;
        
        _logger.LogInformation("Minimal JavaScript transform plugin disposed successfully");
    }

    public void Dispose() => DisposeAsync().AsTask().GetAwaiter().GetResult();
}