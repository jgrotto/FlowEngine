using System.Collections.Immutable;
using FlowEngine.Abstractions.Data;

namespace FlowEngine.Abstractions.Plugins;

/// <summary>
/// Result of plugin initialization operation.
/// </summary>
public sealed record PluginInitializationResult
{
    /// <summary>
    /// Gets whether the initialization was successful.
    /// </summary>
    public required bool Success { get; init; }

    /// <summary>
    /// Gets the initialization message.
    /// </summary>
    public required string Message { get; init; }

    /// <summary>
    /// Gets the time taken for initialization.
    /// </summary>
    public required TimeSpan InitializationTime { get; init; }

    /// <summary>
    /// Gets any errors that occurred during initialization.
    /// </summary>
    public ImmutableArray<string> Errors { get; init; } = ImmutableArray<string>.Empty;

    /// <summary>
    /// Gets any warnings that occurred during initialization.
    /// </summary>
    public ImmutableArray<string> Warnings { get; init; } = ImmutableArray<string>.Empty;
}

/// <summary>
/// Result of plugin hot-swap operation.
/// </summary>
public sealed record HotSwapResult
{
    /// <summary>
    /// Gets whether the hot-swap was successful.
    /// </summary>
    public required bool Success { get; init; }

    /// <summary>
    /// Gets the hot-swap message.
    /// </summary>
    public required string Message { get; init; }

    /// <summary>
    /// Gets the time taken for hot-swap.
    /// </summary>
    public TimeSpan SwapTime { get; init; }

    /// <summary>
    /// Gets the previous configuration that was replaced.
    /// </summary>
    public IPluginConfiguration? PreviousConfiguration { get; init; }

    /// <summary>
    /// Gets any rollback information if the swap failed.
    /// </summary>
    public string? RollbackInfo { get; init; }
}

/// <summary>
/// Result of data processing operation.
/// </summary>
public sealed record ProcessResult
{
    /// <summary>
    /// Gets whether the processing was successful.
    /// </summary>
    public required bool Success { get; init; }

    /// <summary>
    /// Gets the number of rows processed.
    /// </summary>
    public required long ProcessedRows { get; init; }

    /// <summary>
    /// Gets the number of errors encountered.
    /// </summary>
    public required long ErrorCount { get; init; }

    /// <summary>
    /// Gets the processing time.
    /// </summary>
    public required TimeSpan ProcessingTime { get; init; }

    /// <summary>
    /// Gets the processing rate in rows per second.
    /// </summary>
    public required double ProcessingRate { get; init; }

    /// <summary>
    /// Gets the output dataset if applicable.
    /// </summary>
    public IDataset? Output { get; init; }

    /// <summary>
    /// Gets the processing message.
    /// </summary>
    public string? Message { get; init; }

    /// <summary>
    /// Gets any processing errors.
    /// </summary>
    public ImmutableArray<ProcessingError> Errors { get; init; } = ImmutableArray<ProcessingError>.Empty;
}

/// <summary>
/// Represents a processing error with context.
/// </summary>
public sealed record ProcessingError
{
    /// <summary>
    /// Gets the row index where the error occurred.
    /// </summary>
    public required long RowIndex { get; init; }

    /// <summary>
    /// Gets the error message.
    /// </summary>
    public required string Message { get; init; }

    /// <summary>
    /// Gets the error severity.
    /// </summary>
    public required ErrorSeverity Severity { get; init; }

    /// <summary>
    /// Gets the error code.
    /// </summary>
    public string? ErrorCode { get; init; }

    /// <summary>
    /// Gets additional error context.
    /// </summary>
    public ImmutableDictionary<string, object> Context { get; init; } = ImmutableDictionary<string, object>.Empty;
}

/// <summary>
/// Error severity levels.
/// </summary>
public enum ErrorSeverity
{
    /// <summary>
    /// Information message.
    /// </summary>
    Info,

    /// <summary>
    /// Warning that doesn't prevent processing.
    /// </summary>
    Warning,

    /// <summary>
    /// Error that affects processing but allows continuation.
    /// </summary>
    Error,

    /// <summary>
    /// Critical error that stops processing.
    /// </summary>
    Critical
}

/// <summary>
/// Service performance metrics.
/// </summary>
public sealed record ServiceMetrics
{
    /// <summary>
    /// Gets the total number of rows processed.
    /// </summary>
    public required long ProcessedRows { get; init; }

    /// <summary>
    /// Gets the total number of errors encountered.
    /// </summary>
    public required long ErrorCount { get; init; }

    /// <summary>
    /// Gets the processing rate in rows per second.
    /// </summary>
    public required double ProcessingRate { get; init; }

    /// <summary>
    /// Gets the average processing time.
    /// </summary>
    public required TimeSpan AverageProcessingTime { get; init; }

    /// <summary>
    /// Gets the current memory usage in bytes.
    /// </summary>
    public required long MemoryUsage { get; init; }

    /// <summary>
    /// Gets the timestamp of last update.
    /// </summary>
    public required DateTimeOffset LastUpdated { get; init; }
}

// ServiceHealth and HealthStatus definitions moved to IPluginService.cs to avoid duplicates