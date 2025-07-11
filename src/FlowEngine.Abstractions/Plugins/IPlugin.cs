using FlowEngine.Abstractions.Data;
using System.Collections.Immutable;

namespace FlowEngine.Abstractions.Plugins;

/// <summary>
/// Phase 3 plugin interface implementing the five-component architecture.
/// Provides lifecycle management, configuration, and metadata for plugin components.
/// </summary>
public interface IPlugin : IDisposable
{
    /// <summary>
    /// Gets the unique identifier for this plugin.
    /// </summary>
    string Id { get; }

    /// <summary>
    /// Gets the display name of this plugin.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Gets the version of this plugin.
    /// </summary>
    string Version { get; }

    /// <summary>
    /// Gets the description of this plugin.
    /// </summary>
    string Description { get; }

    /// <summary>
    /// Gets the author of this plugin.
    /// </summary>
    string Author { get; }

    /// <summary>
    /// Gets the type of this plugin (Source, Transform, Sink).
    /// </summary>
    string PluginType { get; }

    /// <summary>
    /// Gets the current state of this plugin.
    /// </summary>
    PluginState State { get; }

    /// <summary>
    /// Gets the configuration for this plugin.
    /// </summary>
    IPluginConfiguration Configuration { get; }

    /// <summary>
    /// Gets additional metadata about this plugin.
    /// </summary>
    ImmutableDictionary<string, object>? Metadata { get; }

    /// <summary>
    /// Gets whether this plugin supports hot-swapping.
    /// </summary>
    bool SupportsHotSwapping { get; }

    /// <summary>
    /// Gets the output schema produced by this plugin.
    /// For sink plugins, this may be null as they don't produce output.
    /// </summary>
    ISchema? OutputSchema { get; }

    /// <summary>
    /// Initializes the plugin with the specified configuration.
    /// </summary>
    /// <param name="configuration">The configuration to use for initialization</param>
    /// <param name="cancellationToken">Token for cancelling the operation</param>
    /// <returns>Result of the initialization operation</returns>
    Task<PluginInitializationResult> InitializeAsync(IPluginConfiguration configuration, CancellationToken cancellationToken = default);

    /// <summary>
    /// Starts the plugin.
    /// </summary>
    /// <param name="cancellationToken">Token for cancelling the operation</param>
    /// <returns>Task representing the start operation</returns>
    Task StartAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Stops the plugin.
    /// </summary>
    /// <param name="cancellationToken">Token for cancelling the operation</param>
    /// <returns>Task representing the stop operation</returns>
    Task StopAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Performs a hot-swap of the plugin configuration.
    /// </summary>
    /// <param name="newConfiguration">The new configuration to apply</param>
    /// <param name="cancellationToken">Token for cancelling the operation</param>
    /// <returns>Result of the hot-swap operation</returns>
    Task<HotSwapResult> HotSwapAsync(IPluginConfiguration newConfiguration, CancellationToken cancellationToken = default);

    /// <summary>
    /// Performs a health check on the plugin.
    /// </summary>
    /// <param name="cancellationToken">Token for cancelling the operation</param>
    /// <returns>Collection of health check results</returns>
    Task<IEnumerable<HealthCheck>> HealthCheckAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates that this plugin can process the given input schema.
    /// Called during pipeline validation before execution starts.
    /// </summary>
    /// <param name="inputSchema">The schema of data this plugin will receive</param>
    /// <returns>Validation result indicating compatibility</returns>
    ValidationResult ValidateInputSchema(ISchema? inputSchema);

    /// <summary>
    /// Event raised when the plugin state changes.
    /// </summary>
    event EventHandler<PluginStateChangedEventArgs>? StateChanged;
}
