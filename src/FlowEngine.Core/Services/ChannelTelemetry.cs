using FlowEngine.Abstractions.Services;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace FlowEngine.Core.Services;

/// <summary>
/// Provides telemetry and monitoring services for data channels.
/// </summary>
public sealed class ChannelTelemetry : IChannelTelemetry, IDisposable
{
    private readonly ILogger<ChannelTelemetry> _logger;
    private readonly ConcurrentDictionary<string, ChannelStatistics> _channelStats = new();
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the ChannelTelemetry.
    /// </summary>
    /// <param name="logger">Logger for channel telemetry operations</param>
    public ChannelTelemetry(ILogger<ChannelTelemetry> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _logger.LogInformation("ChannelTelemetry initialized");
    }

    /// <inheritdoc />
    public void RecordWrite(string channelName, int chunkSize, int queueDepth)
    {
        ThrowIfDisposed();
        
        var stats = GetOrCreateChannelStats(channelName);
        
        lock (stats.Lock)
        {
            stats.RecordWrite(chunkSize, queueDepth);
        }
        
        _logger.LogDebug("Recorded write to channel {Channel}: {ChunkSize} rows, queue depth {QueueDepth}", 
            channelName, chunkSize, queueDepth);
    }

    /// <inheritdoc />
    public void RecordRead(string channelName, int chunkSize, TimeSpan waitTime)
    {
        ThrowIfDisposed();
        
        var stats = GetOrCreateChannelStats(channelName);
        
        lock (stats.Lock)
        {
            stats.RecordRead(chunkSize, waitTime);
        }
        
        _logger.LogDebug("Recorded read from channel {Channel}: {ChunkSize} rows, wait time {WaitTime}ms", 
            channelName, chunkSize, waitTime.TotalMilliseconds);
    }

    /// <inheritdoc />
    public void RecordBackpressure(string channelName, int queueDepth, TimeSpan waitTime)
    {
        ThrowIfDisposed();
        
        var stats = GetOrCreateChannelStats(channelName);
        
        lock (stats.Lock)
        {
            stats.RecordBackpressure(queueDepth, waitTime);
        }
        
        _logger.LogWarning("Recorded backpressure on channel {Channel}: queue depth {QueueDepth}, wait time {WaitTime}ms", 
            channelName, queueDepth, waitTime.TotalMilliseconds);
    }

    /// <inheritdoc />
    public void RecordTimeout(string channelName, string operation, TimeSpan timeout)
    {
        ThrowIfDisposed();
        
        var stats = GetOrCreateChannelStats(channelName);
        
        lock (stats.Lock)
        {
            stats.RecordTimeout(operation);
        }
        
        _logger.LogError("Recorded timeout on channel {Channel}: {Operation} operation, timeout {Timeout}ms", 
            channelName, operation, timeout.TotalMilliseconds);
    }

    /// <inheritdoc />
    public void RecordBufferUtilization(string channelName, int bufferSize, int usedSize)
    {
        ThrowIfDisposed();
        
        var stats = GetOrCreateChannelStats(channelName);
        
        lock (stats.Lock)
        {
            stats.RecordBufferUtilization(bufferSize, usedSize);
        }
        
        var utilization = bufferSize > 0 ? (double)usedSize / bufferSize * 100 : 0;
        _logger.LogDebug("Recorded buffer utilization for channel {Channel}: {Utilization:F1}% ({Used}/{Total})", 
            channelName, utilization, usedSize, bufferSize);
    }

    /// <inheritdoc />
    public ChannelTelemetryData? GetChannelTelemetry(string channelName)
    {
        ThrowIfDisposed();
        
        if (!_channelStats.TryGetValue(channelName, out var stats))
            return null;
            
        lock (stats.Lock)
        {
            return stats.ToTelemetryData();
        }
    }

    /// <inheritdoc />
    public IReadOnlyDictionary<string, ChannelTelemetryData> GetAllChannelTelemetry()
    {
        ThrowIfDisposed();
        
        var result = new Dictionary<string, ChannelTelemetryData>();
        
        foreach (var kvp in _channelStats)
        {
            lock (kvp.Value.Lock)
            {
                result[kvp.Key] = kvp.Value.ToTelemetryData();
            }
        }
        
        return result;
    }

    /// <inheritdoc />
    public void ResetChannelTelemetry(string channelName)
    {
        ThrowIfDisposed();
        
        if (_channelStats.TryGetValue(channelName, out var stats))
        {
            lock (stats.Lock)
            {
                stats.Reset();
            }
            
            _logger.LogInformation("Reset telemetry for channel {Channel}", channelName);
        }
    }

    /// <inheritdoc />
    public bool HasPerformanceIssues(string channelName)
    {
        ThrowIfDisposed();
        
        var telemetry = GetChannelTelemetry(channelName);
        if (telemetry == null) return false;
        
        // Consider performance issues if:
        // 1. High backpressure events (>10% of operations)
        // 2. High buffer utilization (>90%)
        // 3. Frequent timeouts
        // 4. Low throughput with high wait times
        
        var totalOps = telemetry.TotalChunksRead + telemetry.TotalChunksWritten;
        var backpressureRatio = totalOps > 0 ? (double)telemetry.TotalBackpressureEvents / totalOps : 0;
        
        return backpressureRatio > 0.1 ||
               telemetry.CurrentBufferUtilization > 90 ||
               telemetry.TimeoutEvents > 0 ||
               (telemetry.AverageReadWaitTime.TotalMilliseconds > 100 && telemetry.CurrentThroughput < 1000);
    }

    /// <inheritdoc />
    public IReadOnlyList<string> GetPerformanceRecommendations(string channelName)
    {
        ThrowIfDisposed();
        
        var telemetry = GetChannelTelemetry(channelName);
        if (telemetry == null) return Array.Empty<string>();
        
        var recommendations = new List<string>();
        
        // High buffer utilization
        if (telemetry.CurrentBufferUtilization > 90)
        {
            recommendations.Add("Consider increasing buffer size to reduce backpressure");
        }
        
        // High backpressure
        var totalOps = telemetry.TotalChunksRead + telemetry.TotalChunksWritten;
        var backpressureRatio = totalOps > 0 ? (double)telemetry.TotalBackpressureEvents / totalOps : 0;
        if (backpressureRatio > 0.1)
        {
            recommendations.Add("High backpressure detected - consider optimizing consumer performance");
        }
        
        // Frequent timeouts
        if (telemetry.TimeoutEvents > 0)
        {
            recommendations.Add("Timeout events detected - consider increasing timeout duration or optimizing processing");
        }
        
        // Imbalanced read/write rates
        var writeRate = telemetry.TotalChunksWritten;
        var readRate = telemetry.TotalChunksRead;
        if (writeRate > 0 && readRate > 0)
        {
            var ratio = (double)writeRate / readRate;
            if (ratio > 2)
            {
                recommendations.Add("Producer is significantly faster than consumer - consider optimizing consumer or adding more consumers");
            }
            else if (ratio < 0.5)
            {
                recommendations.Add("Consumer is significantly faster than producer - consider optimizing producer or reducing consumer count");
            }
        }
        
        // Low throughput with high wait times
        if (telemetry.AverageReadWaitTime.TotalMilliseconds > 100 && telemetry.CurrentThroughput < 1000)
        {
            recommendations.Add("Low throughput with high wait times - consider optimizing data flow or increasing parallelism");
        }
        
        return recommendations;
    }

    private ChannelStatistics GetOrCreateChannelStats(string channelName)
    {
        return _channelStats.GetOrAdd(channelName, name => new ChannelStatistics(name));
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(ChannelTelemetry));
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (!_disposed)
        {
            _logger.LogInformation("Disposing ChannelTelemetry, tracked {ChannelCount} channels", 
                _channelStats.Count);
            _disposed = true;
        }
    }

    /// <summary>
    /// Internal statistics tracking for a channel.
    /// </summary>
    private sealed class ChannelStatistics
    {
        public string ChannelName { get; }
        public object Lock { get; } = new();
        
        private long _totalChunksWritten;
        private long _totalChunksRead;
        private long _totalRowsWritten;
        private long _totalRowsRead;
        private long _totalWriteSize;
        private long _totalReadSize;
        private TimeSpan _totalReadWaitTime;
        private long _totalBackpressureEvents;
        private TimeSpan _totalBackpressureTime;
        private long _timeoutEvents;
        private int _currentQueueDepth;
        private int _maxQueueDepth;
        private int _currentBufferSize;
        private int _currentUsedSize;
        private double _bufferUtilizationSum;
        private long _bufferUtilizationSamples;
        private DateTimeOffset _lastUpdated;
        private DateTimeOffset _startTime;

        public ChannelStatistics(string channelName)
        {
            ChannelName = channelName;
            _startTime = DateTimeOffset.UtcNow;
            _lastUpdated = _startTime;
        }

        public void RecordWrite(int chunkSize, int queueDepth)
        {
            _totalChunksWritten++;
            _totalRowsWritten += chunkSize;
            _totalWriteSize += chunkSize;
            _currentQueueDepth = queueDepth;
            
            if (queueDepth > _maxQueueDepth)
                _maxQueueDepth = queueDepth;
                
            _lastUpdated = DateTimeOffset.UtcNow;
        }

        public void RecordRead(int chunkSize, TimeSpan waitTime)
        {
            _totalChunksRead++;
            _totalRowsRead += chunkSize;
            _totalReadSize += chunkSize;
            _totalReadWaitTime += waitTime;
            _lastUpdated = DateTimeOffset.UtcNow;
        }

        public void RecordBackpressure(int queueDepth, TimeSpan waitTime)
        {
            _totalBackpressureEvents++;
            _totalBackpressureTime += waitTime;
            _currentQueueDepth = queueDepth;
            _lastUpdated = DateTimeOffset.UtcNow;
        }

        public void RecordTimeout(string operation)
        {
            _timeoutEvents++;
            _lastUpdated = DateTimeOffset.UtcNow;
        }

        public void RecordBufferUtilization(int bufferSize, int usedSize)
        {
            _currentBufferSize = bufferSize;
            _currentUsedSize = usedSize;
            
            if (bufferSize > 0)
            {
                var utilization = (double)usedSize / bufferSize * 100;
                _bufferUtilizationSum += utilization;
                _bufferUtilizationSamples++;
            }
            
            _lastUpdated = DateTimeOffset.UtcNow;
        }

        public void Reset()
        {
            _totalChunksWritten = 0;
            _totalChunksRead = 0;
            _totalRowsWritten = 0;
            _totalRowsRead = 0;
            _totalWriteSize = 0;
            _totalReadSize = 0;
            _totalReadWaitTime = TimeSpan.Zero;
            _totalBackpressureEvents = 0;
            _totalBackpressureTime = TimeSpan.Zero;
            _timeoutEvents = 0;
            _currentQueueDepth = 0;
            _maxQueueDepth = 0;
            _bufferUtilizationSum = 0;
            _bufferUtilizationSamples = 0;
            _startTime = DateTimeOffset.UtcNow;
            _lastUpdated = _startTime;
        }

        public ChannelTelemetryData ToTelemetryData()
        {
            var averageWriteChunkSize = _totalChunksWritten > 0 ? (double)_totalWriteSize / _totalChunksWritten : 0;
            var averageReadChunkSize = _totalChunksRead > 0 ? (double)_totalReadSize / _totalChunksRead : 0;
            var averageReadWaitTime = _totalChunksRead > 0 
                ? TimeSpan.FromTicks(_totalReadWaitTime.Ticks / _totalChunksRead)
                : TimeSpan.Zero;
            var averageBufferUtilization = _bufferUtilizationSamples > 0 ? _bufferUtilizationSum / _bufferUtilizationSamples : 0;
            var currentBufferUtilization = _currentBufferSize > 0 ? (double)_currentUsedSize / _currentBufferSize * 100 : 0;
            
            var elapsedTime = _lastUpdated - _startTime;
            var currentThroughput = elapsedTime.TotalSeconds > 0 ? _totalRowsRead / elapsedTime.TotalSeconds : 0;
            var averageThroughput = currentThroughput; // For now, same as current

            return new ChannelTelemetryData
            {
                ChannelName = ChannelName,
                TotalChunksWritten = _totalChunksWritten,
                TotalChunksRead = _totalChunksRead,
                TotalRowsWritten = _totalRowsWritten,
                TotalRowsRead = _totalRowsRead,
                AverageWriteChunkSize = averageWriteChunkSize,
                AverageReadChunkSize = averageReadChunkSize,
                TotalReadWaitTime = _totalReadWaitTime,
                AverageReadWaitTime = averageReadWaitTime,
                TotalBackpressureEvents = _totalBackpressureEvents,
                TotalBackpressureTime = _totalBackpressureTime,
                TimeoutEvents = _timeoutEvents,
                CurrentQueueDepth = _currentQueueDepth,
                MaxQueueDepth = _maxQueueDepth,
                CurrentBufferUtilization = currentBufferUtilization,
                AverageBufferUtilization = averageBufferUtilization,
                CurrentThroughput = currentThroughput,
                AverageThroughput = averageThroughput,
                LastUpdated = _lastUpdated
            };
        }
    }
}