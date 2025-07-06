using FlowEngine.Abstractions.Data;

namespace FlowEngine.Abstractions.Execution;

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