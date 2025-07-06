namespace FlowEngine.Abstractions.Execution;

/// <summary>
/// Current status of pipeline execution.
/// </summary>
public enum PipelineExecutionStatus
{
    /// <summary>
    /// Pipeline is not running.
    /// </summary>
    Idle,

    /// <summary>
    /// Pipeline is validating configuration and initializing.
    /// </summary>
    Initializing,

    /// <summary>
    /// Pipeline is running and processing data.
    /// </summary>
    Running,

    /// <summary>
    /// Pipeline execution is paused.
    /// </summary>
    Paused,

    /// <summary>
    /// Pipeline is stopping gracefully.
    /// </summary>
    Stopping,

    /// <summary>
    /// Pipeline execution completed successfully.
    /// </summary>
    Completed,

    /// <summary>
    /// Pipeline execution failed with errors.
    /// </summary>
    Failed,

    /// <summary>
    /// Pipeline execution was cancelled.
    /// </summary>
    Cancelled
}