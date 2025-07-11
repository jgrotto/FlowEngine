namespace FlowEngine.Abstractions.Execution;

/// <summary>
/// Event arguments for pipeline errors.
/// </summary>
public sealed class PipelineErrorEventArgs : EventArgs
{
    /// <summary>
    /// Initializes pipeline error event arguments.
    /// </summary>
    /// <param name="error">The pipeline error</param>
    public PipelineErrorEventArgs(PipelineError error)
    {
        Error = error ?? throw new ArgumentNullException(nameof(error));
    }

    /// <summary>
    /// Gets the pipeline error.
    /// </summary>
    public PipelineError Error { get; }
}
