using FlowEngine.Abstractions.Configuration;

namespace FlowEngine.Abstractions.Execution;

/// <summary>
/// Interface for executing data processing pipelines with DAG-based dependency management.
/// Coordinates plugin execution, data flow, and error handling across the pipeline.
/// </summary>
public interface IPipelineExecutor : IAsyncDisposable
{
    /// <summary>
    /// Executes a pipeline based on the provided configuration.
    /// </summary>
    /// <param name="configuration">Pipeline configuration defining plugins and connections</param>
    /// <param name="cancellationToken">Cancellation token for stopping execution</param>
    /// <returns>Pipeline execution result</returns>
    /// <exception cref="PipelineExecutionException">Thrown when pipeline execution fails</exception>
    Task<PipelineExecutionResult> ExecuteAsync(IPipelineConfiguration configuration, CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates a pipeline configuration without executing it.
    /// Checks for dependency cycles, schema compatibility, and resource requirements.
    /// </summary>
    /// <param name="configuration">Pipeline configuration to validate</param>
    /// <returns>Validation result with any errors or warnings</returns>
    Task<PipelineValidationResult> ValidatePipelineAsync(IPipelineConfiguration configuration);

    /// <summary>
    /// Gets the current status of pipeline execution.
    /// </summary>
    PipelineExecutionStatus Status { get; }

    /// <summary>
    /// Gets performance metrics for the current or last pipeline execution.
    /// </summary>
    PipelineExecutionMetrics Metrics { get; }

    /// <summary>
    /// Pauses pipeline execution if currently running.
    /// </summary>
    /// <returns>Task that completes when pipeline is paused</returns>
    Task PauseAsync();

    /// <summary>
    /// Resumes a paused pipeline execution.
    /// </summary>
    /// <returns>Task that completes when pipeline is resumed</returns>
    Task ResumeAsync();

    /// <summary>
    /// Stops pipeline execution gracefully.
    /// </summary>
    /// <param name="timeout">Maximum time to wait for graceful shutdown</param>
    /// <returns>Task that completes when pipeline is stopped</returns>
    Task StopAsync(TimeSpan? timeout = null);

    /// <summary>
    /// Event raised when pipeline execution status changes.
    /// </summary>
    event EventHandler<PipelineStatusChangedEventArgs>? StatusChanged;

    /// <summary>
    /// Event raised when a plugin completes processing a chunk.
    /// </summary>
    event EventHandler<PluginChunkProcessedEventArgs>? PluginChunkProcessed;

    /// <summary>
    /// Event raised when a pipeline error occurs.
    /// </summary>
    event EventHandler<PipelineErrorEventArgs>? PipelineError;

    /// <summary>
    /// Event raised when pipeline execution completes.
    /// </summary>
    event EventHandler<PipelineCompletedEventArgs>? PipelineCompleted;
}