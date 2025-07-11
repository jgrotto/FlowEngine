using FlowEngine.Abstractions.Data;

namespace FlowEngine.Abstractions.Services;

/// <summary>
/// Provides telemetry and monitoring services for data channels.
/// Enables plugins to monitor channel performance and optimize data flow.
/// </summary>
public interface IChannelTelemetry
{
    /// <summary>
    /// Records that data was written to a channel.
    /// </summary>
    /// <param name="channelName">The name of the channel</param>
    /// <param name="chunkSize">The size of the chunk written</param>
    /// <param name="queueDepth">The current queue depth</param>
    void RecordWrite(string channelName, int chunkSize, int queueDepth);

    /// <summary>
    /// Records that data was read from a channel.
    /// </summary>
    /// <param name="channelName">The name of the channel</param>
    /// <param name="chunkSize">The size of the chunk read</param>
    /// <param name="waitTime">The time spent waiting for data</param>
    void RecordRead(string channelName, int chunkSize, TimeSpan waitTime);

    /// <summary>
    /// Records a backpressure event on a channel.
    /// </summary>
    /// <param name="channelName">The name of the channel</param>
    /// <param name="queueDepth">The queue depth when backpressure occurred</param>
    /// <param name="waitTime">The time spent waiting due to backpressure</param>
    void RecordBackpressure(string channelName, int queueDepth, TimeSpan waitTime);

    /// <summary>
    /// Records a channel timeout event.
    /// </summary>
    /// <param name="channelName">The name of the channel</param>
    /// <param name="operation">The operation that timed out (Read/Write)</param>
    /// <param name="timeout">The configured timeout duration</param>
    void RecordTimeout(string channelName, string operation, TimeSpan timeout);

    /// <summary>
    /// Records channel buffer utilization.
    /// </summary>
    /// <param name="channelName">The name of the channel</param>
    /// <param name="bufferSize">The total buffer size</param>
    /// <param name="usedSize">The currently used buffer size</param>
    void RecordBufferUtilization(string channelName, int bufferSize, int usedSize);

    /// <summary>
    /// Gets the current telemetry data for a channel.
    /// </summary>
    /// <param name="channelName">The name of the channel</param>
    /// <returns>Channel telemetry data, or null if not available</returns>
    ChannelTelemetryData? GetChannelTelemetry(string channelName);

    /// <summary>
    /// Gets telemetry data for all monitored channels.
    /// </summary>
    /// <returns>A dictionary of channel names to their telemetry data</returns>
    IReadOnlyDictionary<string, ChannelTelemetryData> GetAllChannelTelemetry();

    /// <summary>
    /// Resets telemetry data for a channel.
    /// </summary>
    /// <param name="channelName">The name of the channel</param>
    void ResetChannelTelemetry(string channelName);

    /// <summary>
    /// Checks if a channel is experiencing performance issues.
    /// </summary>
    /// <param name="channelName">The name of the channel</param>
    /// <returns>True if the channel has performance issues, false otherwise</returns>
    bool HasPerformanceIssues(string channelName);

    /// <summary>
    /// Gets performance recommendations for a channel.
    /// </summary>
    /// <param name="channelName">The name of the channel</param>
    /// <returns>A list of performance recommendations</returns>
    IReadOnlyList<string> GetPerformanceRecommendations(string channelName);
}

/// <summary>
/// Represents telemetry data for a data channel.
/// </summary>
public sealed class ChannelTelemetryData
{
    /// <summary>
    /// Gets the channel name.
    /// </summary>
    public string ChannelName { get; init; } = string.Empty;

    /// <summary>
    /// Gets the total number of chunks written.
    /// </summary>
    public long TotalChunksWritten { get; init; }

    /// <summary>
    /// Gets the total number of chunks read.
    /// </summary>
    public long TotalChunksRead { get; init; }

    /// <summary>
    /// Gets the total number of rows written.
    /// </summary>
    public long TotalRowsWritten { get; init; }

    /// <summary>
    /// Gets the total number of rows read.
    /// </summary>
    public long TotalRowsRead { get; init; }

    /// <summary>
    /// Gets the average chunk size for writes.
    /// </summary>
    public double AverageWriteChunkSize { get; init; }

    /// <summary>
    /// Gets the average chunk size for reads.
    /// </summary>
    public double AverageReadChunkSize { get; init; }

    /// <summary>
    /// Gets the total time spent waiting for reads.
    /// </summary>
    public TimeSpan TotalReadWaitTime { get; init; }

    /// <summary>
    /// Gets the average time spent waiting for reads.
    /// </summary>
    public TimeSpan AverageReadWaitTime { get; init; }

    /// <summary>
    /// Gets the total number of backpressure events.
    /// </summary>
    public long TotalBackpressureEvents { get; init; }

    /// <summary>
    /// Gets the total time spent in backpressure.
    /// </summary>
    public TimeSpan TotalBackpressureTime { get; init; }

    /// <summary>
    /// Gets the number of timeout events.
    /// </summary>
    public long TimeoutEvents { get; init; }

    /// <summary>
    /// Gets the current queue depth.
    /// </summary>
    public int CurrentQueueDepth { get; init; }

    /// <summary>
    /// Gets the maximum queue depth observed.
    /// </summary>
    public int MaxQueueDepth { get; init; }

    /// <summary>
    /// Gets the current buffer utilization percentage.
    /// </summary>
    public double CurrentBufferUtilization { get; init; }

    /// <summary>
    /// Gets the average buffer utilization percentage.
    /// </summary>
    public double AverageBufferUtilization { get; init; }

    /// <summary>
    /// Gets the current throughput in rows per second.
    /// </summary>
    public double CurrentThroughput { get; init; }

    /// <summary>
    /// Gets the average throughput in rows per second.
    /// </summary>
    public double AverageThroughput { get; init; }

    /// <summary>
    /// Gets the timestamp when telemetry was last updated.
    /// </summary>
    public DateTimeOffset LastUpdated { get; init; }
}
