namespace FlowEngine.Abstractions.Execution;

/// <summary>
/// Result of pipeline execution.
/// </summary>
public sealed record PipelineExecutionResult
{
    /// <summary>
    /// Gets whether the pipeline execution was successful.
    /// </summary>
    public required bool IsSuccess { get; init; }

    /// <summary>
    /// Gets the total execution time.
    /// </summary>
    public required TimeSpan ExecutionTime { get; init; }

    /// <summary>
    /// Gets the total number of rows processed.
    /// </summary>
    public required long TotalRowsProcessed { get; init; }

    /// <summary>
    /// Gets the total number of chunks processed.
    /// </summary>
    public required long TotalChunksProcessed { get; init; }

    /// <summary>
    /// Gets any errors that occurred during execution.
    /// </summary>
    public IReadOnlyList<PipelineError> Errors { get; init; } = Array.Empty<PipelineError>();

    /// <summary>
    /// Gets performance metrics for each plugin.
    /// </summary>
    public IReadOnlyDictionary<string, PluginExecutionMetrics> PluginMetrics { get; init; } = new Dictionary<string, PluginExecutionMetrics>();

    /// <summary>
    /// Gets the final pipeline status.
    /// </summary>
    public required PipelineExecutionStatus FinalStatus { get; init; }

    /// <summary>
    /// Gets additional execution context and diagnostics.
    /// </summary>
    public IReadOnlyDictionary<string, object>? ExecutionContext { get; init; }
}