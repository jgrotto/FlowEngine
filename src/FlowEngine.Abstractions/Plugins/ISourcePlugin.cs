namespace FlowEngine.Abstractions.Plugins;

/// <summary>
/// Interface for source plugins that produce data into the pipeline.
/// Source plugins are the entry points for data processing pipelines and don't have input connections.
/// Examples: file readers, database sources, web APIs, message queues.
/// </summary>
public interface ISourcePlugin : IPlugin
{
    /// <summary>
    /// Produces chunks of data asynchronously into the pipeline.
    /// This method is called by the pipeline executor and should yield chunks as they become available.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token to stop data production</param>
    /// <returns>Async enumerable of data chunks with the plugin's output schema</returns>
    /// <exception cref="PluginExecutionException">Thrown when data production fails</exception>
    IAsyncEnumerable<IChunk> ProduceAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets an estimate of the total number of rows this source will produce.
    /// Returns null if the count is unknown or infinite (e.g., live data streams).
    /// Used for progress reporting and resource planning.
    /// </summary>
    long? EstimatedRowCount { get; }

    /// <summary>
    /// Gets whether this source supports seeking to a specific position.
    /// Seekable sources can be restarted from a checkpoint for fault tolerance.
    /// </summary>
    bool IsSeekable { get; }

    /// <summary>
    /// Gets the current position in the data source for checkpointing.
    /// Returns null if the source doesn't support seeking or position tracking.
    /// </summary>
    SourcePosition? CurrentPosition { get; }

    /// <summary>
    /// Seeks to a specific position in the data source.
    /// Only supported if IsSeekable returns true.
    /// </summary>
    /// <param name="position">Position to seek to</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Task representing the seek operation</returns>
    /// <exception cref="NotSupportedException">Thrown if seeking is not supported</exception>
    /// <exception cref="PluginExecutionException">Thrown if seeking fails</exception>
    Task SeekAsync(SourcePosition position, CancellationToken cancellationToken = default);
}

/// <summary>
/// Represents a position in a data source for seeking and checkpointing.
/// The format is source-specific and opaque to the pipeline engine.
/// </summary>
public sealed record SourcePosition
{
    /// <summary>
    /// Gets the source-specific position data.
    /// This could be a file offset, database cursor, timestamp, etc.
    /// </summary>
    public required object Value { get; init; }

    /// <summary>
    /// Gets the timestamp when this position was captured.
    /// Used for ordering and validation of position data.
    /// </summary>
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Gets optional metadata about this position for debugging.
    /// </summary>
    public IReadOnlyDictionary<string, object>? Metadata { get; init; }

    /// <summary>
    /// Creates a position from a simple value.
    /// </summary>
    /// <param name="value">Position value</param>
    /// <returns>Source position</returns>
    public static SourcePosition FromValue(object value) => new() { Value = value };

    /// <summary>
    /// Creates a position with metadata.
    /// </summary>
    /// <param name="value">Position value</param>
    /// <param name="metadata">Position metadata</param>
    /// <returns>Source position</returns>
    public static SourcePosition FromValueWithMetadata(object value, IReadOnlyDictionary<string, object> metadata) => 
        new() { Value = value, Metadata = metadata };
}