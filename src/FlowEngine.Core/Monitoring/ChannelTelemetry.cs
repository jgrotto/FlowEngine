using FlowEngine.Abstractions;
using FlowEngine.Abstractions.Data;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Diagnostics;

namespace FlowEngine.Core.Monitoring;

/// <summary>
/// Provides telemetry and performance monitoring for data channels.
/// Tracks throughput, latency, backpressure events, and memory usage.
/// </summary>
public sealed class ChannelTelemetry : IDisposable
{
    private readonly ConcurrentDictionary<string, ChannelMetrics> _channelMetrics = new();
    private readonly Timer _metricsUpdateTimer;
    private readonly object _metricsLock = new();
    private volatile bool _disposed;

    /// <summary>
    /// Initializes a new channel telemetry monitor.
    /// </summary>
    public ChannelTelemetry()
    {
        _metricsUpdateTimer = new Timer(UpdateMetrics, null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
    }

    /// <summary>
    /// Gets current metrics for all channels.
    /// </summary>
    public IReadOnlyDictionary<string, ChannelMetrics> GetAllMetrics()
    {
        ThrowIfDisposed();
        return _channelMetrics.ToImmutableDictionary();
    }

    /// <summary>
    /// Gets metrics for a specific channel.
    /// </summary>
    public ChannelMetrics? GetChannelMetrics(string channelId)
    {
        ThrowIfDisposed();
        return _channelMetrics.TryGetValue(channelId, out var metrics) ? metrics : null;
    }

    /// <summary>
    /// Records a chunk write operation.
    /// </summary>
    public void RecordWrite(string channelId, IChunk chunk, TimeSpan duration)
    {
        if (_disposed) return;

        var metrics = _channelMetrics.GetOrAdd(channelId, _ => new ChannelMetrics { ChannelId = channelId });
        
        lock (_metricsLock)
        {
            metrics.TotalChunksWritten++;
            metrics.TotalRowsWritten += chunk.RowCount;
            metrics.TotalBytesWritten += EstimateChunkSize(chunk);
            metrics.WriteLatencies.Add(duration);
            
            if (duration > metrics.MaxWriteLatency)
                metrics.MaxWriteLatency = duration;
                
            metrics.LastWriteTime = DateTimeOffset.UtcNow;
        }
    }

    /// <summary>
    /// Records a chunk read operation.
    /// </summary>
    public void RecordRead(string channelId, IChunk chunk, TimeSpan duration)
    {
        if (_disposed) return;

        var metrics = _channelMetrics.GetOrAdd(channelId, _ => new ChannelMetrics { ChannelId = channelId });
        
        lock (_metricsLock)
        {
            metrics.TotalChunksRead++;
            metrics.TotalRowsRead += chunk.RowCount;
            metrics.TotalBytesRead += EstimateChunkSize(chunk);
            metrics.ReadLatencies.Add(duration);
            
            if (duration > metrics.MaxReadLatency)
                metrics.MaxReadLatency = duration;
                
            metrics.LastReadTime = DateTimeOffset.UtcNow;
        }
    }

    /// <summary>
    /// Records a backpressure event when channel is full.
    /// </summary>
    public void RecordBackpressure(string channelId, TimeSpan waitTime)
    {
        if (_disposed) return;

        var metrics = _channelMetrics.GetOrAdd(channelId, _ => new ChannelMetrics { ChannelId = channelId });
        
        lock (_metricsLock)
        {
            metrics.BackpressureEvents++;
            metrics.TotalBackpressureTime += waitTime;
            
            if (waitTime > metrics.MaxBackpressureWait)
                metrics.MaxBackpressureWait = waitTime;
        }
    }

    /// <summary>
    /// Updates channel capacity metrics.
    /// </summary>
    public void UpdateCapacity(string channelId, int currentSize, int maxCapacity)
    {
        if (_disposed) return;

        var metrics = _channelMetrics.GetOrAdd(channelId, _ => new ChannelMetrics { ChannelId = channelId });
        
        lock (_metricsLock)
        {
            metrics.CurrentCapacity = currentSize;
            metrics.MaxCapacity = maxCapacity;
            
            var utilizationPercent = maxCapacity > 0 ? (double)currentSize / maxCapacity * 100 : 0;
            if (utilizationPercent > metrics.PeakCapacityUtilization)
                metrics.PeakCapacityUtilization = utilizationPercent;
        }
    }

    /// <summary>
    /// Records when a channel is completed.
    /// </summary>
    public void RecordCompletion(string channelId)
    {
        if (_disposed) return;

        var metrics = _channelMetrics.GetOrAdd(channelId, _ => new ChannelMetrics { ChannelId = channelId });
        
        lock (_metricsLock)
        {
            metrics.CompletedAt = DateTimeOffset.UtcNow;
            metrics.IsCompleted = true;
        }
    }

    private void UpdateMetrics(object? state)
    {
        if (_disposed) return;

        var now = DateTimeOffset.UtcNow;
        
        foreach (var kvp in _channelMetrics)
        {
            var metrics = kvp.Value;
            
            lock (_metricsLock)
            {
                // Calculate throughput (rows/second over last interval)
                var timeSinceLastUpdate = now - metrics.LastMetricsUpdate;
                if (timeSinceLastUpdate.TotalSeconds >= 1.0)
                {
                    var rowsSinceLastUpdate = metrics.TotalRowsWritten - metrics.PreviousRowCount;
                    metrics.CurrentThroughputRowsPerSecond = rowsSinceLastUpdate / timeSinceLastUpdate.TotalSeconds;
                    
                    if (metrics.CurrentThroughputRowsPerSecond > metrics.PeakThroughputRowsPerSecond)
                        metrics.PeakThroughputRowsPerSecond = metrics.CurrentThroughputRowsPerSecond;
                    
                    metrics.PreviousRowCount = metrics.TotalRowsWritten;
                    metrics.LastMetricsUpdate = now;
                }

                // Calculate average latencies
                if (metrics.WriteLatencies.Count > 0)
                {
                    metrics.AverageWriteLatency = TimeSpan.FromTicks((long)metrics.WriteLatencies.Average(l => l.Ticks));
                }
                
                if (metrics.ReadLatencies.Count > 0)
                {
                    metrics.AverageReadLatency = TimeSpan.FromTicks((long)metrics.ReadLatencies.Average(l => l.Ticks));
                }
            }
        }
    }

    private static long EstimateChunkSize(IChunk chunk)
    {
        // Rough estimation: 8 bytes per object reference + estimated string lengths
        const int FixedObjectOverhead = 8;
        var stringEstimate = 0L;
        
        // Sample first few rows to estimate string content
        var sampleSize = Math.Min(10, chunk.RowCount);
        for (int i = 0; i < sampleSize; i++)
        {
            var row = chunk.Rows[i];
            for (int j = 0; j < row.ColumnCount; j++)
            {
                if (row[j] is string str)
                    stringEstimate += str.Length * 2; // UTF-16 encoding
                else
                    stringEstimate += FixedObjectOverhead;
            }
        }
        
        // Extrapolate to full chunk
        return stringEstimate * chunk.RowCount / Math.Max(1, sampleSize);
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(ChannelTelemetry));
    }

    /// <summary>
    /// Disposes the channel telemetry monitor and releases resources.
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;
        
        _disposed = true;
        _metricsUpdateTimer?.Dispose();
    }
}

/// <summary>
/// Performance metrics for a data channel.
/// </summary>
public sealed class ChannelMetrics
{
    /// <summary>Unique identifier for the channel.</summary>
    public required string ChannelId { get; init; }
    
    /// <summary>Total number of chunks written to the channel.</summary>
    public long TotalChunksWritten { get; set; }
    /// <summary>Total number of rows written to the channel.</summary>
    public long TotalRowsWritten { get; set; }
    /// <summary>Total bytes written to the channel.</summary>
    public long TotalBytesWritten { get; set; }
    /// <summary>Maximum write latency observed.</summary>
    public TimeSpan MaxWriteLatency { get; set; }
    /// <summary>Average write latency.</summary>
    public TimeSpan AverageWriteLatency { get; set; }
    /// <summary>Timestamp of the last write operation.</summary>
    public DateTimeOffset LastWriteTime { get; set; }
    
    /// <summary>Total number of chunks read from the channel.</summary>
    public long TotalChunksRead { get; set; }
    /// <summary>Total number of rows read from the channel.</summary>
    public long TotalRowsRead { get; set; }
    /// <summary>Total bytes read from the channel.</summary>
    public long TotalBytesRead { get; set; }
    /// <summary>Maximum read latency observed.</summary>
    public TimeSpan MaxReadLatency { get; set; }
    /// <summary>Average read latency.</summary>
    public TimeSpan AverageReadLatency { get; set; }
    /// <summary>Timestamp of the last read operation.</summary>
    public DateTimeOffset LastReadTime { get; set; }
    
    /// <summary>Current throughput in rows per second.</summary>
    public double CurrentThroughputRowsPerSecond { get; set; }
    /// <summary>Peak throughput observed in rows per second.</summary>
    public double PeakThroughputRowsPerSecond { get; set; }
    
    /// <summary>Current channel capacity utilization.</summary>
    public int CurrentCapacity { get; set; }
    /// <summary>Maximum channel capacity.</summary>
    public int MaxCapacity { get; set; }
    /// <summary>Peak capacity utilization percentage.</summary>
    public double PeakCapacityUtilization { get; set; }
    
    /// <summary>Number of backpressure events.</summary>
    public long BackpressureEvents { get; set; }
    /// <summary>Total time spent waiting due to backpressure.</summary>
    public TimeSpan TotalBackpressureTime { get; set; }
    /// <summary>Maximum wait time due to backpressure.</summary>
    public TimeSpan MaxBackpressureWait { get; set; }
    
    /// <summary>Channel creation timestamp.</summary>
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    /// <summary>Channel completion timestamp.</summary>
    public DateTimeOffset? CompletedAt { get; set; }
    /// <summary>Whether the channel has been completed.</summary>
    public bool IsCompleted { get; set; }
    
    // Internal tracking
    internal List<TimeSpan> WriteLatencies { get; } = new();
    internal List<TimeSpan> ReadLatencies { get; } = new();
    internal long PreviousRowCount { get; set; }
    internal DateTimeOffset LastMetricsUpdate { get; set; } = DateTimeOffset.UtcNow;
}