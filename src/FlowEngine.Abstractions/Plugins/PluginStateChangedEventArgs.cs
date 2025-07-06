namespace FlowEngine.Abstractions.Plugins;

/// <summary>
/// Event arguments for plugin state changes.
/// </summary>
public sealed class PluginStateChangedEventArgs : EventArgs
{
    /// <summary>
    /// Gets the previous state of the plugin.
    /// </summary>
    public required PluginState PreviousState { get; init; }

    /// <summary>
    /// Gets the current state of the plugin.
    /// </summary>
    public required PluginState CurrentState { get; init; }

    /// <summary>
    /// Gets the reason for the state change.
    /// </summary>
    public string? Reason { get; init; }

    /// <summary>
    /// Gets the time when the state change occurred.
    /// </summary>
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
}