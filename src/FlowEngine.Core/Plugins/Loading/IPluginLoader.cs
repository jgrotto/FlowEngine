using FlowEngine.Abstractions.Plugins;

namespace FlowEngine.Core.Plugins.Loading;

/// <summary>
/// Provides dynamic loading and management of plugin assemblies.
/// Supports hot-swapping, isolation, and lifecycle management for standalone plugin libraries.
/// </summary>
public interface IPluginLoader : IDisposable
{
    /// <summary>
    /// Loads a plugin from the specified assembly path.
    /// </summary>
    /// <param name="assemblyPath">Path to the plugin assembly</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Loaded plugin descriptor</returns>
    Task<PluginDescriptor> LoadPluginAsync(string assemblyPath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Loads a plugin from the specified assembly path with configuration.
    /// </summary>
    /// <param name="assemblyPath">Path to the plugin assembly</param>
    /// <param name="configuration">Plugin configuration</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Loaded and configured plugin instance</returns>
    Task<IPlugin> LoadPluginWithConfigurationAsync(string assemblyPath, IPluginConfiguration configuration, CancellationToken cancellationToken = default);

    /// <summary>
    /// Unloads a previously loaded plugin.
    /// </summary>
    /// <param name="pluginId">Plugin identifier</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if plugin was successfully unloaded</returns>
    Task<bool> UnloadPluginAsync(string pluginId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all currently loaded plugins.
    /// </summary>
    /// <returns>Collection of loaded plugin descriptors</returns>
    IReadOnlyCollection<PluginDescriptor> GetLoadedPlugins();

    /// <summary>
    /// Gets a loaded plugin by identifier.
    /// </summary>
    /// <param name="pluginId">Plugin identifier</param>
    /// <returns>Plugin descriptor if found, null otherwise</returns>
    PluginDescriptor? GetPlugin(string pluginId);

    /// <summary>
    /// Validates that a plugin assembly is compatible with the current engine version.
    /// </summary>
    /// <param name="assemblyPath">Path to the plugin assembly</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Validation result</returns>
    Task<PluginValidationResult> ValidatePluginAsync(string assemblyPath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Hot-swaps a plugin with a new version.
    /// </summary>
    /// <param name="pluginId">Plugin identifier to replace</param>
    /// <param name="newAssemblyPath">Path to the new plugin assembly</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Hot-swap result</returns>
    Task<HotSwapResult> HotSwapPluginAsync(string pluginId, string newAssemblyPath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Event raised when a plugin is loaded.
    /// </summary>
    event EventHandler<PluginLoadedEventArgs> PluginLoaded;

    /// <summary>
    /// Event raised when a plugin is unloaded.
    /// </summary>
    event EventHandler<PluginUnloadedEventArgs> PluginUnloaded;

    /// <summary>
    /// Event raised when a plugin load fails.
    /// </summary>
    event EventHandler<PluginLoadFailedEventArgs> PluginLoadFailed;
}

/// <summary>
/// Describes a loaded plugin with metadata and lifecycle information.
/// </summary>
public sealed record PluginDescriptor
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
    /// Gets the plugin version.
    /// </summary>
    public required string Version { get; init; }

    /// <summary>
    /// Gets the plugin type (Source, Transform, Sink).
    /// </summary>
    public required string Type { get; init; }

    /// <summary>
    /// Gets the path to the plugin assembly.
    /// </summary>
    public required string AssemblyPath { get; init; }

    /// <summary>
    /// Gets the plugin assembly load context.
    /// </summary>
    public required PluginAssemblyLoadContext LoadContext { get; init; }

    /// <summary>
    /// Gets the plugin instance factory.
    /// </summary>
    public required Func<IPluginConfiguration, IPlugin> PluginFactory { get; init; }

    /// <summary>
    /// Gets the timestamp when the plugin was loaded.
    /// </summary>
    public required DateTimeOffset LoadedAt { get; init; }

    /// <summary>
    /// Gets the current plugin status.
    /// </summary>
    public PluginLoadStatus Status { get; init; } = PluginLoadStatus.Loaded;

    /// <summary>
    /// Gets additional plugin metadata.
    /// </summary>
    public IReadOnlyDictionary<string, object> Metadata { get; init; } = new Dictionary<string, object>();
}

/// <summary>
/// Plugin load status enumeration.
/// </summary>
public enum PluginLoadStatus
{
    /// <summary>
    /// Plugin is being loaded.
    /// </summary>
    Loading,

    /// <summary>
    /// Plugin has been loaded successfully.
    /// </summary>
    Loaded,

    /// <summary>
    /// Plugin failed to load.
    /// </summary>
    Failed,

    /// <summary>
    /// Plugin is being unloaded.
    /// </summary>
    Unloading,

    /// <summary>
    /// Plugin has been unloaded.
    /// </summary>
    Unloaded
}

/// <summary>
/// Event arguments for plugin loaded event.
/// </summary>
public sealed class PluginLoadedEventArgs : EventArgs
{
    /// <summary>
    /// Gets the loaded plugin descriptor.
    /// </summary>
    public required PluginDescriptor Plugin { get; init; }

    /// <summary>
    /// Gets the time taken to load the plugin.
    /// </summary>
    public required TimeSpan LoadTime { get; init; }
}

/// <summary>
/// Event arguments for plugin unloaded event.
/// </summary>
public sealed class PluginUnloadedEventArgs : EventArgs
{
    /// <summary>
    /// Gets the unloaded plugin descriptor.
    /// </summary>
    public required PluginDescriptor Plugin { get; init; }

    /// <summary>
    /// Gets the time taken to unload the plugin.
    /// </summary>
    public required TimeSpan UnloadTime { get; init; }
}

/// <summary>
/// Event arguments for plugin load failed event.
/// </summary>
public sealed class PluginLoadFailedEventArgs : EventArgs
{
    /// <summary>
    /// Gets the assembly path that failed to load.
    /// </summary>
    public required string AssemblyPath { get; init; }

    /// <summary>
    /// Gets the exception that caused the load failure.
    /// </summary>
    public required Exception Exception { get; init; }

    /// <summary>
    /// Gets the attempted plugin information if available.
    /// </summary>
    public PluginDescriptor? PartialPlugin { get; init; }
}