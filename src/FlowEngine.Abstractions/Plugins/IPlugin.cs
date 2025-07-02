using Microsoft.Extensions.Configuration;
using FlowEngine.Abstractions.Data;
using System.Collections.Immutable;

namespace FlowEngine.Abstractions.Plugins;

/// <summary>
/// Represents a plugin component that coordinates configuration, validation, processing, and services.
/// Provides lifecycle management and plugin orchestration capabilities.
/// </summary>
/// <remarks>
/// The plugin interface defines the central coordination component in the five-component architecture:
/// Configuration → Validator → Plugin → Processor → Service
/// 
/// Key responsibilities:
/// - Plugin lifecycle management (Initialize, Start, Stop, Dispose)
/// - Component coordination and orchestration
/// - State management and monitoring
/// - Hot-swapping support for configuration updates
/// - Error handling and recovery
/// 
/// Plugins should maintain high availability and performance while providing
/// comprehensive monitoring and management capabilities.
/// </remarks>
public interface IPlugin : IAsyncDisposable, IDisposable
{
    /// <summary>
    /// Gets the unique plugin identifier.
    /// </summary>
    string Id { get; }

    /// <summary>
    /// Gets the unique name of this plugin instance.
    /// Must match the name specified in the pipeline configuration.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Gets the plugin version for compatibility checking.
    /// Should follow semantic versioning (e.g., "1.0.0").
    /// </summary>
    string Version { get; }

    /// <summary>
    /// Gets the plugin type (Source, Transform, Sink).
    /// </summary>
    string Type { get; }

    /// <summary>
    /// Gets the plugin description.
    /// </summary>
    string Description { get; }

    /// <summary>
    /// Gets the plugin author.
    /// </summary>
    string Author { get; }

    /// <summary>
    /// Gets whether this plugin supports hot-swapping configuration updates.
    /// </summary>
    bool SupportsHotSwapping { get; }

    /// <summary>
    /// Gets the current plugin status.
    /// </summary>
    PluginState Status { get; }

    /// <summary>
    /// Gets the plugin configuration.
    /// </summary>
    IPluginConfiguration Configuration { get; }

    /// <summary>
    /// Initializes the plugin with the specified configuration.
    /// </summary>
    /// <param name="configuration">Plugin configuration</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Initialization result</returns>
    Task<PluginInitializationResult> InitializeAsync(IPluginConfiguration configuration, CancellationToken cancellationToken = default);

    /// <summary>
    /// Starts the plugin asynchronously.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Task representing the start operation</returns>
    Task StartAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Stops the plugin asynchronously.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Task representing the stop operation</returns>
    Task StopAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks the health of the plugin asynchronously.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Current plugin health status</returns>
    Task<ServiceHealth> CheckHealthAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets current plugin metrics.
    /// </summary>
    /// <returns>Plugin performance metrics</returns>
    ServiceMetrics GetMetrics();

    /// <summary>
    /// Updates the plugin configuration with hot-swapping support.
    /// </summary>
    /// <param name="newConfiguration">New configuration to apply</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Hot-swap result</returns>
    Task<HotSwapResult> HotSwapAsync(IPluginConfiguration newConfiguration, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the plugin processor for data processing operations.
    /// </summary>
    /// <returns>Plugin processor instance</returns>
    IPluginProcessor GetProcessor();

    /// <summary>
    /// Gets the plugin service for business logic operations.
    /// </summary>
    /// <returns>Plugin service instance</returns>
    IPluginService GetService();

    /// <summary>
    /// Gets the plugin validator for configuration validation.
    /// </summary>
    /// <returns>Plugin validator instance</returns>
    IPluginValidator<IPluginConfiguration> GetValidator();
}

/// <summary>
/// Result of plugin input schema validation.
/// </summary>
public sealed record PluginValidationResult
{
    /// <summary>
    /// Gets whether the validation passed.
    /// </summary>
    public bool IsValid { get; init; }

    /// <summary>
    /// Gets validation error messages if validation failed.
    /// </summary>
    public IReadOnlyList<string> Errors { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Gets optional warnings that don't prevent execution but may affect performance.
    /// </summary>
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Gets the plugin type that was validated (for plugin manager use).
    /// </summary>
    public Type? PluginType { get; init; }

    /// <summary>
    /// Creates a successful validation result.
    /// </summary>
    public static PluginValidationResult Success() => new() { IsValid = true };

    /// <summary>
    /// Creates a successful validation result with plugin type.
    /// </summary>
    /// <param name="pluginType">The validated plugin type</param>
    /// <param name="warnings">Optional warning messages</param>
    public static PluginValidationResult Success(Type? pluginType, params string[] warnings) =>
        new() { IsValid = true, PluginType = pluginType, Warnings = warnings };

    /// <summary>
    /// Creates a successful validation result with warnings.
    /// </summary>
    /// <param name="warnings">Warning messages</param>
    public static PluginValidationResult SuccessWithWarnings(params string[] warnings) => 
        new() { IsValid = true, Warnings = warnings };

    /// <summary>
    /// Creates a failed validation result.
    /// </summary>
    /// <param name="errors">Error messages</param>
    public static PluginValidationResult Failure(params string[] errors) => 
        new() { IsValid = false, Errors = errors };
}