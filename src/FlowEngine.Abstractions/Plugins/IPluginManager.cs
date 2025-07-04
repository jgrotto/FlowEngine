using FlowEngine.Abstractions.Configuration;

namespace FlowEngine.Abstractions.Plugins;

/// <summary>
/// High-level plugin management interface that coordinates plugin loading, configuration, and lifecycle.
/// Provides the main entry point for plugin operations in the FlowEngine pipeline.
/// </summary>
public interface IPluginManager : IDisposable
{
    /// <summary>
    /// Loads and configures a plugin based on its definition.
    /// </summary>
    /// <param name="definition">Plugin definition from YAML</param>
    /// <returns>Loaded and configured plugin instance</returns>
    /// <exception cref="PluginLoadException">Thrown when plugin loading or configuration fails</exception>
    Task<IPlugin> LoadPluginAsync(IPluginDefinition definition);

    /// <summary>
    /// Unloads a plugin and releases its resources.
    /// </summary>
    /// <param name="plugin">Plugin to unload</param>
    /// <returns>Task that completes when plugin is unloaded</returns>
    Task UnloadPluginAsync(IPlugin plugin);

    /// <summary>
    /// Gets a loaded plugin by its name.
    /// </summary>
    /// <param name="name">Plugin name from configuration</param>
    /// <returns>Plugin instance if found, null otherwise</returns>
    IPlugin? GetPlugin(string name);

    /// <summary>
    /// Gets all loaded plugins of the specified type.
    /// </summary>
    /// <typeparam name="T">Plugin interface type</typeparam>
    /// <returns>Collection of plugins matching the type</returns>
    IReadOnlyCollection<T> GetPlugins<T>() where T : class, IPlugin;

    /// <summary>
    /// Gets all loaded plugins.
    /// </summary>
    /// <returns>Collection of all loaded plugins</returns>
    IReadOnlyCollection<IPlugin> GetAllPlugins();

    /// <summary>
    /// Gets plugin information for monitoring and diagnostics.
    /// </summary>
    /// <returns>Collection of plugin information</returns>
    IReadOnlyCollection<PluginInfo> GetPluginInfo();

    /// <summary>
    /// Validates that a plugin definition can be loaded.
    /// </summary>
    /// <param name="definition">Plugin definition to validate</param>
    /// <returns>Validation result</returns>
    Task<ValidationResult> ValidatePluginAsync(IPluginDefinition definition);

    /// <summary>
    /// Reloads a plugin with new definition.
    /// </summary>
    /// <param name="name">Name of plugin to reload</param>
    /// <param name="definition">New plugin definition</param>
    /// <returns>Reloaded plugin instance</returns>
    Task<IPlugin> ReloadPluginAsync(string name, IPluginDefinition definition);

    /// <summary>
    /// Event raised when a plugin is loaded.
    /// </summary>
    event EventHandler<PluginLoadedEventArgs>? PluginLoaded;

    /// <summary>
    /// Event raised when a plugin is unloaded.
    /// </summary>
    event EventHandler<PluginUnloadedEventArgs>? PluginUnloaded;

    /// <summary>
    /// Event raised when a plugin operation fails.
    /// </summary>
    event EventHandler<PluginErrorEventArgs>? PluginError;
}


/// <summary>
/// Event arguments for plugin error events.
/// </summary>
public sealed class PluginErrorEventArgs : EventArgs
{
    /// <summary>
    /// Initializes plugin error event arguments.
    /// </summary>
    /// <param name="pluginName">Name of the plugin that encountered an error</param>
    /// <param name="exception">The exception that occurred</param>
    /// <param name="operation">The operation that was being performed</param>
    public PluginErrorEventArgs(string pluginName, Exception exception, string operation)
    {
        PluginName = pluginName ?? throw new ArgumentNullException(nameof(pluginName));
        Exception = exception ?? throw new ArgumentNullException(nameof(exception));
        Operation = operation ?? throw new ArgumentNullException(nameof(operation));
        Timestamp = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Gets the name of the plugin that encountered an error.
    /// </summary>
    public string PluginName { get; }

    /// <summary>
    /// Gets the exception that occurred.
    /// </summary>
    public Exception Exception { get; }

    /// <summary>
    /// Gets the operation that was being performed when the error occurred.
    /// </summary>
    public string Operation { get; }

    /// <summary>
    /// Gets the timestamp when the error occurred.
    /// </summary>
    public DateTimeOffset Timestamp { get; }
}

