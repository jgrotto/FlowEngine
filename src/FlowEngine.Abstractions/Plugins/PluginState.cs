namespace FlowEngine.Abstractions.Plugins;

/// <summary>
/// Represents the current state of a plugin in its lifecycle.
/// </summary>
public enum PluginState
{
    /// <summary>
    /// Plugin has been created but not initialized.
    /// </summary>
    Created,

    /// <summary>
    /// Plugin has been initialized with configuration.
    /// </summary>
    Initialized,

    /// <summary>
    /// Plugin is running and processing data.
    /// </summary>
    Running,

    /// <summary>
    /// Plugin has been stopped but can be restarted.
    /// </summary>
    Stopped,

    /// <summary>
    /// Plugin encountered an error and cannot process data.
    /// </summary>
    Failed,

    /// <summary>
    /// Plugin has been disposed and cannot be used.
    /// </summary>
    Disposed
}
