using System.Reflection;

namespace FlowEngine.Abstractions.Plugins;

/// <summary>
/// Interface for loading and managing plugin assemblies with isolation.
/// Provides secure loading, unloading, and lifecycle management of plugins.
/// </summary>
public interface IPluginLoader : IDisposable
{
    /// <summary>
    /// Loads a plugin from the specified assembly path with isolation.
    /// </summary>
    /// <typeparam name="T">Type of plugin interface to load</typeparam>
    /// <param name="assemblyPath">Path to the plugin assembly</param>
    /// <param name="typeName">Full type name of the plugin class</param>
    /// <param name="isolationLevel">Level of isolation for the plugin</param>
    /// <returns>Loaded plugin instance</returns>
    /// <exception cref="PluginLoadException">Thrown when plugin loading fails</exception>
    Task<T> LoadPluginAsync<T>(string assemblyPath, string typeName, PluginIsolationLevel isolationLevel = PluginIsolationLevel.Isolated) where T : class, IPlugin;

    /// <summary>
    /// Unloads a plugin and its associated context.
    /// </summary>
    /// <param name="plugin">Plugin instance to unload</param>
    /// <returns>Task that completes when unloading is finished</returns>
    Task UnloadPluginAsync(IPlugin plugin);

    /// <summary>
    /// Gets information about all loaded plugins.
    /// </summary>
    /// <returns>Collection of loaded plugin information</returns>
    IReadOnlyCollection<PluginInfo> GetLoadedPlugins();

    /// <summary>
    /// Event raised when a plugin is successfully loaded.
    /// </summary>
    event EventHandler<PluginLoadedEventArgs>? PluginLoaded;

    /// <summary>
    /// Event raised when a plugin is unloaded.
    /// </summary>
    event EventHandler<PluginUnloadedEventArgs>? PluginUnloaded;

    /// <summary>
    /// Event raised when a plugin load operation fails.
    /// </summary>
    event EventHandler<PluginLoadFailedEventArgs>? PluginLoadFailed;
}


/// <summary>
/// Information about a loaded plugin.
/// </summary>
public sealed record PluginInfo
{
    /// <summary>
    /// Gets the unique identifier for this plugin instance.
    /// </summary>
    public required string Id { get; init; }

    /// <summary>
    /// Gets the plugin type name.
    /// </summary>
    public required string TypeName { get; init; }

    /// <summary>
    /// Gets the assembly path where the plugin was loaded from.
    /// </summary>
    public required string AssemblyPath { get; init; }

    /// <summary>
    /// Gets the isolation level used for this plugin.
    /// </summary>
    public required PluginIsolationLevel IsolationLevel { get; init; }

    /// <summary>
    /// Gets the timestamp when the plugin was loaded.
    /// </summary>
    public required DateTimeOffset LoadedAt { get; init; }

    /// <summary>
    /// Gets the current status of the plugin.
    /// </summary>
    public required PluginStatus Status { get; init; }

    /// <summary>
    /// Gets the assembly load context name (if applicable).
    /// </summary>
    public string? ContextName { get; init; }

    /// <summary>
    /// Gets plugin metadata if available.
    /// </summary>
    public IReadOnlyDictionary<string, object>? Metadata { get; init; }
}

/// <summary>
/// Event arguments for plugin loaded events.
/// </summary>
public sealed class PluginLoadedEventArgs : EventArgs
{
    /// <summary>
    /// Initializes plugin loaded event arguments.
    /// </summary>
    /// <param name="pluginInfo">Information about the loaded plugin</param>
    /// <param name="plugin">The loaded plugin instance</param>
    public PluginLoadedEventArgs(PluginInfo pluginInfo, IPlugin plugin)
    {
        PluginInfo = pluginInfo ?? throw new ArgumentNullException(nameof(pluginInfo));
        Plugin = plugin ?? throw new ArgumentNullException(nameof(plugin));
    }

    /// <summary>
    /// Gets information about the loaded plugin.
    /// </summary>
    public PluginInfo PluginInfo { get; }

    /// <summary>
    /// Gets the loaded plugin instance.
    /// </summary>
    public IPlugin Plugin { get; }
}

/// <summary>
/// Event arguments for plugin unloaded events.
/// </summary>
public sealed class PluginUnloadedEventArgs : EventArgs
{
    /// <summary>
    /// Initializes plugin unloaded event arguments.
    /// </summary>
    /// <param name="pluginInfo">Information about the unloaded plugin</param>
    public PluginUnloadedEventArgs(PluginInfo pluginInfo)
    {
        PluginInfo = pluginInfo ?? throw new ArgumentNullException(nameof(pluginInfo));
    }

    /// <summary>
    /// Gets information about the unloaded plugin.
    /// </summary>
    public PluginInfo PluginInfo { get; }
}

/// <summary>
/// Event arguments for plugin load failed events.
/// </summary>
public sealed class PluginLoadFailedEventArgs : EventArgs
{
    /// <summary>
    /// Initializes plugin load failed event arguments.
    /// </summary>
    /// <param name="assemblyPath">Path to the assembly that failed to load</param>
    /// <param name="typeName">Type name that failed to load</param>
    /// <param name="exception">Exception that occurred during loading</param>
    public PluginLoadFailedEventArgs(string assemblyPath, string typeName, Exception exception)
    {
        AssemblyPath = assemblyPath ?? throw new ArgumentNullException(nameof(assemblyPath));
        TypeName = typeName ?? throw new ArgumentNullException(nameof(typeName));
        Exception = exception ?? throw new ArgumentNullException(nameof(exception));
    }

    /// <summary>
    /// Gets the assembly path that failed to load.
    /// </summary>
    public string AssemblyPath { get; }

    /// <summary>
    /// Gets the type name that failed to load.
    /// </summary>
    public string TypeName { get; }

    /// <summary>
    /// Gets the exception that occurred during loading.
    /// </summary>
    public Exception Exception { get; }
}

/// <summary>
/// Current status of a plugin instance.
/// </summary>
public enum PluginStatus
{
    /// <summary>
    /// Plugin is not loaded.
    /// </summary>
    NotLoaded,

    /// <summary>
    /// Plugin is loading or initializing.
    /// </summary>
    Loading,

    /// <summary>
    /// Plugin is loaded and ready for processing.
    /// </summary>
    Loaded,

    /// <summary>
    /// Plugin is running or processing data.
    /// </summary>
    Running,

    /// <summary>
    /// Plugin has been paused.
    /// </summary>
    Paused,

    /// <summary>
    /// Plugin encountered an error.
    /// </summary>
    Faulted,

    /// <summary>
    /// Plugin is stopping.
    /// </summary>
    Stopping,

    /// <summary>
    /// Plugin has been stopped.
    /// </summary>
    Stopped,

    /// <summary>
    /// Plugin is unloading.
    /// </summary>
    Unloading,

    /// <summary>
    /// Plugin has been unloaded and disposed.
    /// </summary>
    Unloaded
}