namespace FlowEngine.Abstractions.Execution;

/// <summary>
/// Event arguments for pipeline status changes.
/// </summary>
public sealed class PipelineStatusChangedEventArgs : EventArgs
{
    /// <summary>
    /// Initializes pipeline status changed event arguments.
    /// </summary>
    /// <param name="oldStatus">Previous status</param>
    /// <param name="newStatus">New status</param>
    /// <param name="message">Optional status change message</param>
    public PipelineStatusChangedEventArgs(PipelineExecutionStatus oldStatus, PipelineExecutionStatus newStatus, string? message = null)
    {
        OldStatus = oldStatus;
        NewStatus = newStatus;
        Message = message;
        Timestamp = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Gets the previous pipeline status.
    /// </summary>
    public PipelineExecutionStatus OldStatus { get; }

    /// <summary>
    /// Gets the new pipeline status.
    /// </summary>
    public PipelineExecutionStatus NewStatus { get; }

    /// <summary>
    /// Gets an optional message describing the status change.
    /// </summary>
    public string? Message { get; }

    /// <summary>
    /// Gets the timestamp when the status change occurred.
    /// </summary>
    public DateTimeOffset Timestamp { get; }
}
