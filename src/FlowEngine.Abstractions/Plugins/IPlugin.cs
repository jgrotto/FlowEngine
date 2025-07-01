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
public interface IPlugin : IAsyncDisposable
{
    /// <summary>
    /// Gets the unique name of this plugin instance.
    /// Must match the name specified in the pipeline configuration.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Gets the output schema produced by this plugin.
    /// For sink plugins, this may be null as they don't produce output.
    /// </summary>
    ISchema? OutputSchema { get; }

    /// <summary>
    /// Gets the plugin version for compatibility checking.
    /// Should follow semantic versioning (e.g., "1.0.0").
    /// </summary>
    string Version { get; }

    /// <summary>
    /// Gets optional metadata about this plugin for diagnostics and monitoring.
    /// </summary>
    IReadOnlyDictionary<string, object>? Metadata { get; }

    /// <summary>
    /// Initializes the plugin with configuration and validates settings.
    /// Called once during pipeline startup before any data processing.
    /// </summary>
    /// <param name="config">Plugin-specific configuration from YAML</param>
    /// <param name="cancellationToken">Cancellation token for initialization timeout</param>
    /// <returns>Task representing the initialization operation</returns>
    /// <exception cref="PluginInitializationException">Thrown when initialization fails</exception>
    Task InitializeAsync(IConfiguration config, CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates that this plugin can process the given input schema.
    /// Called during pipeline validation before execution starts.
    /// </summary>
    /// <param name="inputSchema">The schema of data this plugin will receive</param>
    /// <returns>Validation result indicating compatibility</returns>
    PluginValidationResult ValidateInputSchema(ISchema? inputSchema);

    /// <summary>
    /// Gets the current plugin state.
    /// </summary>
    PluginState State { get; }

    /// <summary>
    /// Gets whether this plugin supports hot-swapping configuration updates.
    /// </summary>
    bool SupportsHotSwapping { get; }

    /// <summary>
    /// Gets the current plugin health status.
    /// </summary>
    PluginHealth Health { get; }

    /// <summary>
    /// Starts the plugin and begins processing operations.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token for start operation</param>
    /// <returns>Task representing the start operation</returns>
    /// <exception cref="InvalidOperationException">Plugin not in correct state for starting</exception>
    /// <remarks>
    /// Start process:
    /// 1. Verify initialized state
    /// 2. Start all components in dependency order
    /// 3. Begin monitoring and health checks
    /// 4. Enable processing capabilities
    /// 5. Transition to Running state
    /// 
    /// After starting, the plugin should be ready to process data and respond to requests.
    /// </remarks>
    Task StartAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Stops the plugin and suspends processing operations.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token for stop operation</param>
    /// <returns>Task representing the stop operation</returns>
    /// <remarks>
    /// Stop process:
    /// 1. Suspend new operations
    /// 2. Complete pending operations gracefully
    /// 3. Stop all components in reverse dependency order
    /// 4. Preserve state for potential restart
    /// 5. Transition to Stopped state
    /// 
    /// After stopping, the plugin can be restarted or disposed.
    /// </remarks>
    Task StopAsync(CancellationToken cancellationToken = default);

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
    /// 3. Apply configuration to all components atomically
    /// 4. Verify successful update
    /// 5. Update monitoring and metrics
    /// 
    /// Hot-swapping allows configuration changes without stopping the plugin,
    /// enabling zero-downtime updates in production environments.
    /// </remarks>
    Task UpdateConfigurationAsync(IPluginConfiguration newConfiguration, CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates compatibility with another plugin for pipeline connections.
    /// </summary>
    /// <param name="sourcePlugin">Source plugin to check compatibility</param>
    /// <returns>Schema compatibility result</returns>
    /// <remarks>
    /// Compatibility checking ensures that data can flow correctly between plugins
    /// without type errors, schema mismatches, or performance degradation.
    /// </remarks>
    Task<SchemaCompatibilityResult> ValidateCompatibilityAsync(IPlugin sourcePlugin);

    /// <summary>
    /// Gets current plugin performance metrics and statistics.
    /// </summary>
    /// <returns>Current plugin performance metrics</returns>
    /// <remarks>
    /// Metrics include:
    /// - Processing throughput and latency
    /// - Error rates and types
    /// - Resource usage (memory, CPU)
    /// - Component-specific performance indicators
    /// - Health and availability metrics
    /// </remarks>
    PluginMetrics GetMetrics();

    /// <summary>
    /// Performs a health check on the plugin and all its components.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token for health check timeout</param>
    /// <returns>Comprehensive health check result</returns>
    /// <remarks>
    /// Health checking verifies:
    /// - Plugin state and component status
    /// - Resource availability and limits
    /// - Performance within acceptable ranges
    /// - Error rates below thresholds
    /// - Component integration integrity
    /// </remarks>
    Task<PluginHealth> CheckHealthAsync(CancellationToken cancellationToken = default);
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

