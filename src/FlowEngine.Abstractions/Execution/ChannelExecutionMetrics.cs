namespace FlowEngine.Abstractions.Execution;

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