using FlowEngine.Abstractions.Configuration;
using FlowEngine.Abstractions.Plugins;

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

/// <summary>
/// Result of pipeline validation.
/// </summary>
public sealed record PipelineValidationResult
{
    /// <summary>
    /// Gets whether the pipeline is valid.
    /// </summary>
    public required bool IsValid { get; init; }

    /// <summary>
    /// Gets validation error messages.
    /// </summary>
    public IReadOnlyList<string> Errors { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Gets validation warning messages.
    /// </summary>
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Gets the execution order determined by dependency analysis.
    /// </summary>
    public IReadOnlyList<string> ExecutionOrder { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Gets schema flow analysis showing how schemas transform through the pipeline.
    /// </summary>
    public IReadOnlyList<SchemaFlowStep> SchemaFlow { get; init; } = Array.Empty<SchemaFlowStep>();

    /// <summary>
    /// Creates a successful validation result.
    /// </summary>
    /// <param name="executionOrder">Determined execution order</param>
    /// <param name="schemaFlow">Schema transformation flow</param>
    /// <param name="warnings">Optional warnings</param>
    public static PipelineValidationResult Success(IReadOnlyList<string> executionOrder, IReadOnlyList<SchemaFlowStep> schemaFlow, params string[] warnings) =>
        new() { IsValid = true, ExecutionOrder = executionOrder, SchemaFlow = schemaFlow, Warnings = warnings };

    /// <summary>
    /// Creates a failed validation result.
    /// </summary>
    /// <param name="errors">Error messages</param>
    public static PipelineValidationResult Failure(params string[] errors) =>
        new() { IsValid = false, Errors = errors };
}

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

/// <summary>
/// Performance metrics for pipeline execution.
/// </summary>
public sealed record PipelineExecutionMetrics
{
    /// <summary>
    /// Gets the total execution time.
    /// </summary>
    public TimeSpan TotalExecutionTime { get; init; }

    /// <summary>
    /// Gets the total number of rows processed.
    /// </summary>
    public long TotalRowsProcessed { get; init; }

    /// <summary>
    /// Gets the total number of chunks processed.
    /// </summary>
    public long TotalChunksProcessed { get; init; }

    /// <summary>
    /// Gets the average throughput in rows per second.
    /// </summary>
    public double RowsPerSecond => TotalExecutionTime.TotalSeconds > 0 ? TotalRowsProcessed / TotalExecutionTime.TotalSeconds : 0;

    /// <summary>
    /// Gets the peak memory usage during execution.
    /// </summary>
    public long PeakMemoryUsage { get; init; }

    /// <summary>
    /// Gets the number of errors encountered.
    /// </summary>
    public int ErrorCount { get; init; }

    /// <summary>
    /// Gets performance metrics for individual plugins.
    /// </summary>
    public IReadOnlyDictionary<string, PluginExecutionMetrics> PluginMetrics { get; init; } = new Dictionary<string, PluginExecutionMetrics>();

    /// <summary>
    /// Gets metrics for channel performance.
    /// </summary>
    public IReadOnlyDictionary<string, ChannelExecutionMetrics> ChannelMetrics { get; init; } = new Dictionary<string, ChannelExecutionMetrics>();
}

/// <summary>
/// Performance metrics for individual plugin execution.
/// </summary>
public sealed record PluginExecutionMetrics
{
    /// <summary>
    /// Gets the plugin name.
    /// </summary>
    public required string PluginName { get; init; }

    /// <summary>
    /// Gets the total execution time for this plugin.
    /// </summary>
    public TimeSpan ExecutionTime { get; init; }

    /// <summary>
    /// Gets the number of chunks processed by this plugin.
    /// </summary>
    public long ChunksProcessed { get; init; }

    /// <summary>
    /// Gets the number of rows processed by this plugin.
    /// </summary>
    public long RowsProcessed { get; init; }

    /// <summary>
    /// Gets the memory usage for this plugin.
    /// </summary>
    public long MemoryUsage { get; init; }

    /// <summary>
    /// Gets the number of errors in this plugin.
    /// </summary>
    public int ErrorCount { get; init; }

    /// <summary>
    /// Gets the average processing time per chunk.
    /// </summary>
    public TimeSpan AverageChunkTime => ChunksProcessed > 0 ? TimeSpan.FromTicks(ExecutionTime.Ticks / ChunksProcessed) : TimeSpan.Zero;
}

/// <summary>
/// Performance metrics for channel execution.
/// </summary>
public sealed record ChannelExecutionMetrics
{
    /// <summary>
    /// Gets the channel identifier.
    /// </summary>
    public required string ChannelId { get; init; }

    /// <summary>
    /// Gets the total number of items passed through this channel.
    /// </summary>
    public long TotalItems { get; init; }

    /// <summary>
    /// Gets the number of items dropped due to backpressure.
    /// </summary>
    public long DroppedItems { get; init; }

    /// <summary>
    /// Gets the average latency through this channel.
    /// </summary>
    public TimeSpan AverageLatency { get; init; }

    /// <summary>
    /// Gets the peak buffer utilization percentage.
    /// </summary>
    public double PeakBufferUtilization { get; init; }
}

/// <summary>
/// Information about a schema transformation step in the pipeline.
/// </summary>
public sealed record SchemaFlowStep
{
    /// <summary>
    /// Gets the plugin name that performs this transformation.
    /// </summary>
    public required string PluginName { get; init; }

    /// <summary>
    /// Gets the input schema for this step.
    /// </summary>
    public ISchema? InputSchema { get; init; }

    /// <summary>
    /// Gets the output schema for this step.
    /// </summary>
    public ISchema? OutputSchema { get; init; }

    /// <summary>
    /// Gets the schema transformation description.
    /// </summary>
    public string? Description { get; init; }
}

/// <summary>
/// Information about a pipeline error.
/// </summary>
public sealed record PipelineError
{
    /// <summary>
    /// Gets the plugin name where the error occurred.
    /// </summary>
    public required string PluginName { get; init; }

    /// <summary>
    /// Gets the error message.
    /// </summary>
    public required string Message { get; init; }

    /// <summary>
    /// Gets the exception that caused the error.
    /// </summary>
    public Exception? Exception { get; init; }

    /// <summary>
    /// Gets the timestamp when the error occurred.
    /// </summary>
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Gets the chunk being processed when the error occurred.
    /// </summary>
    public IChunk? FailedChunk { get; init; }

    /// <summary>
    /// Gets whether this error is recoverable.
    /// </summary>
    public bool IsRecoverable { get; init; }
}

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

/// <summary>
/// Event arguments for plugin chunk processing.
/// </summary>
public sealed class PluginChunkProcessedEventArgs : EventArgs
{
    /// <summary>
    /// Initializes plugin chunk processed event arguments.
    /// </summary>
    /// <param name="pluginName">Name of the plugin</param>
    /// <param name="chunk">Processed chunk</param>
    /// <param name="processingTime">Time taken to process the chunk</param>
    public PluginChunkProcessedEventArgs(string pluginName, IChunk chunk, TimeSpan processingTime)
    {
        PluginName = pluginName ?? throw new ArgumentNullException(nameof(pluginName));
        Chunk = chunk ?? throw new ArgumentNullException(nameof(chunk));
        ProcessingTime = processingTime;
        Timestamp = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Gets the name of the plugin that processed the chunk.
    /// </summary>
    public string PluginName { get; }

    /// <summary>
    /// Gets the processed chunk.
    /// </summary>
    public IChunk Chunk { get; }

    /// <summary>
    /// Gets the time taken to process the chunk.
    /// </summary>
    public TimeSpan ProcessingTime { get; }

    /// <summary>
    /// Gets the timestamp when processing completed.
    /// </summary>
    public DateTimeOffset Timestamp { get; }
}

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