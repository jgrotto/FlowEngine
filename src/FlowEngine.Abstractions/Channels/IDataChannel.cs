namespace FlowEngine.Abstractions.Channels;

/// <summary>
/// Interface for high-performance data channels that connect plugins in a pipeline.
/// Channels provide buffering, backpressure, and flow control between data producers and consumers.
/// </summary>
/// <typeparam name="T">Type of data flowing through the channel</typeparam>
public interface IDataChannel<T> : IAsyncDisposable
{
    /// <summary>
    /// Gets the unique identifier for this channel.
    /// </summary>
    string Id { get; }

    /// <summary>
    /// Gets the configuration for this channel.
    /// </summary>
    Configuration.IChannelConfiguration Configuration { get; }

    /// <summary>
    /// Gets the current status of this channel.
    /// </summary>
    ChannelStatus Status { get; }

    /// <summary>
    /// Gets performance metrics for this channel.
    /// </summary>
    IChannelMetrics Metrics { get; }

    /// <summary>
    /// Writes data to the channel asynchronously.
    /// May block or drop data based on channel configuration when full.
    /// </summary>
    /// <param name="item">Item to write to the channel</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if the item was written successfully, false if dropped or rejected</returns>
    /// <exception cref="ChannelException">Thrown when writing fails due to channel errors</exception>
    ValueTask<bool> WriteAsync(T item, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads all available items from the channel asynchronously.
    /// Continues until the channel is completed or cancelled.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Async enumerable of items from the channel</returns>
    IAsyncEnumerable<T> ReadAllAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks the channel as complete for writing.
    /// No more items can be written after this call, but existing items can still be read.
    /// </summary>
    /// <param name="exception">Optional exception indicating abnormal completion</param>
    void Complete(Exception? exception = null);

    /// <summary>
    /// Waits for the channel to be completed by the writer.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Task that completes when the channel is finished</returns>
    Task WaitForCompletionAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Event raised when the channel status changes.
    /// </summary>
    event EventHandler<ChannelStatusChangedEventArgs>? StatusChanged;
}


/// <summary>
/// Performance metrics for channel monitoring.
/// </summary>
public interface IChannelMetrics
{
    /// <summary>
    /// Gets the total number of items written to this channel.
    /// </summary>
    long TotalItemsWritten { get; }

    /// <summary>
    /// Gets the total number of items read from this channel.
    /// </summary>
    long TotalItemsRead { get; }

    /// <summary>
    /// Gets the number of items currently in the channel buffer.
    /// </summary>
    int CurrentBufferCount { get; }

    /// <summary>
    /// Gets the current buffer utilization as a percentage (0-100).
    /// </summary>
    double BufferUtilization { get; }

    /// <summary>
    /// Gets the number of items that were dropped due to channel overflow.
    /// </summary>
    long DroppedItems { get; }

    /// <summary>
    /// Gets the number of write operations that were blocked due to backpressure.
    /// </summary>
    long BlockedWrites { get; }

    /// <summary>
    /// Gets the average time items spend in the channel buffer.
    /// </summary>
    TimeSpan AverageLatency { get; }

    /// <summary>
    /// Gets the current throughput in items per second.
    /// </summary>
    double ThroughputPerSecond { get; }

    /// <summary>
    /// Gets the timestamp when these metrics were last updated.
    /// </summary>
    DateTimeOffset LastUpdated { get; }
}


/// <summary>
/// Current status of a data channel.
/// </summary>
public enum ChannelStatus
{
    /// <summary>
    /// Channel is active and accepting writes.
    /// </summary>
    Active,

    /// <summary>
    /// Channel is under backpressure (buffer utilization above threshold).
    /// </summary>
    Backpressure,

    /// <summary>
    /// Channel has been completed normally (no more writes accepted).
    /// </summary>
    Completed,

    /// <summary>
    /// Channel has been completed with an error.
    /// </summary>
    Faulted,

    /// <summary>
    /// Channel has been disposed and is no longer usable.
    /// </summary>
    Disposed
}

/// <summary>
/// Event arguments for channel status changes.
/// </summary>
public sealed class ChannelStatusChangedEventArgs : EventArgs
{
    /// <summary>
    /// Initializes a new instance of the event arguments.
    /// </summary>
    /// <param name="oldStatus">Previous channel status</param>
    /// <param name="newStatus">New channel status</param>
    /// <param name="exception">Optional exception for faulted status</param>
    public ChannelStatusChangedEventArgs(ChannelStatus oldStatus, ChannelStatus newStatus, Exception? exception = null)
    {
        OldStatus = oldStatus;
        NewStatus = newStatus;
        Exception = exception;
        Timestamp = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Gets the previous channel status.
    /// </summary>
    public ChannelStatus OldStatus { get; }

    /// <summary>
    /// Gets the new channel status.
    /// </summary>
    public ChannelStatus NewStatus { get; }

    /// <summary>
    /// Gets the exception associated with the status change (if any).
    /// </summary>
    public Exception? Exception { get; }

    /// <summary>
    /// Gets the timestamp when the status change occurred.
    /// </summary>
    public DateTimeOffset Timestamp { get; }
}

/// <summary>
/// Exception thrown when channel operations fail.
/// </summary>
public sealed class ChannelException : Exception
{
    /// <summary>
    /// Initializes a new channel exception.
    /// </summary>
    /// <param name="message">Error message</param>
    public ChannelException(string message) : base(message)
    {
    }

    /// <summary>
    /// Initializes a new channel exception with inner exception.
    /// </summary>
    /// <param name="message">Error message</param>
    /// <param name="innerException">Inner exception</param>
    public ChannelException(string message, Exception innerException) : base(message, innerException)
    {
    }

    /// <summary>
    /// Gets or sets the channel ID associated with this exception.
    /// </summary>
    public string? ChannelId { get; init; }

    /// <summary>
    /// Gets or sets the channel status when the exception occurred.
    /// </summary>
    public ChannelStatus? ChannelStatus { get; init; }
}
