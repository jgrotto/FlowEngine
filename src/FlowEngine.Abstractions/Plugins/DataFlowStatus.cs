namespace FlowEngine.Abstractions.Plugins;

/// <summary>
/// Status of data flow through the system.
/// </summary>
public enum DataFlowStatus
{
    /// <summary>
    /// Data is flowing normally.
    /// </summary>
    Normal,

    /// <summary>
    /// Data flow is slower than expected.
    /// </summary>
    Slow,

    /// <summary>
    /// Data flow has been paused.
    /// </summary>
    Paused,

    /// <summary>
    /// Data flow has stopped due to an error.
    /// </summary>
    Failed,

    /// <summary>
    /// Data flow has been cancelled.
    /// </summary>
    Cancelled
}