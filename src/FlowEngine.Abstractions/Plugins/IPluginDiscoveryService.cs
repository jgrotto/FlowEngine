namespace FlowEngine.Abstractions.Plugins;

/// <summary>
/// Service for discovering plugins in the standard ./plugins/ directory.
/// Simplified interface focused on single-directory scanning for predictable deployment.
/// </summary>
public interface IPluginDiscoveryService
{
    /// <summary>
    /// Discovers plugins in the standard ./plugins/ directory.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Collection of discovered plugin information</returns>
    Task<IReadOnlyList<DiscoveredPlugin>> DiscoverPluginsAsync(CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Discovers plugins in a specific directory (for testing scenarios).
    /// </summary>
    /// <param name="pluginDirectory">Directory to scan for plugins</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Collection of discovered plugin information</returns>
    Task<IReadOnlyList<DiscoveredPlugin>> DiscoverPluginsAsync(string pluginDirectory, CancellationToken cancellationToken = default);
}

/// <summary>
/// Represents a discovered plugin with essential information.
/// </summary>
public sealed record DiscoveredPlugin
{
    /// <summary>
    /// Gets the plugin name.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Gets the path to the plugin assembly.
    /// </summary>
    public required string AssemblyPath { get; init; }

    /// <summary>
    /// Gets the plugin type information.
    /// </summary>
    public PluginTypeInfo? TypeInfo { get; init; }

    /// <summary>
    /// Gets the plugin version information.
    /// </summary>
    public Version? Version { get; init; }
}

