namespace FlowEngine.Abstractions.Execution;

/// <summary>
/// Event arguments for pipeline completion.
/// </summary>
public sealed class PipelineCompletedEventArgs : EventArgs
{
    /// <summary>
    /// Initializes pipeline completed event arguments.
    /// </summary>
    /// <param name="result">The execution result</param>
    public PipelineCompletedEventArgs(PipelineExecutionResult result)
    {
        Result = result ?? throw new ArgumentNullException(nameof(result));
    }

    /// <summary>
    /// Gets the pipeline execution result.
    /// </summary>
    public PipelineExecutionResult Result { get; }
}