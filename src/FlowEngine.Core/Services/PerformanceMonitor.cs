using FlowEngine.Abstractions.Services;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Diagnostics;

namespace FlowEngine.Core.Services;

/// <summary>
/// Provides performance monitoring and telemetry services for FlowEngine components.
/// </summary>
public sealed class PerformanceMonitor : IPerformanceMonitor, IDisposable
{
    private readonly ILogger<PerformanceMonitor> _logger;
    private readonly ConcurrentDictionary<string, ComponentStatistics> _statistics = new();
    private readonly object _lock = new();
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the PerformanceMonitor.
    /// </summary>
    /// <param name="logger">Logger for performance monitoring operations</param>
    public PerformanceMonitor(ILogger<PerformanceMonitor> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _logger.LogInformation("PerformanceMonitor initialized");
    }

    /// <inheritdoc />
    public bool IsEnabled => !_disposed;

    /// <inheritdoc />
    public IDisposable StartOperation(string operationName, IDictionary<string, string>? tags = null)
    {
        ThrowIfDisposed();
        return new OperationTimer(this, operationName, tags);
    }

    /// <inheritdoc />
    public void RecordMetric(string metricName, double value, IDictionary<string, string>? tags = null)
    {
        ThrowIfDisposed();

        var componentName = ExtractComponentName(metricName, tags);
        var stats = GetOrCreateStatistics(componentName);

        lock (stats.Lock)
        {
            stats.RecordMetric(metricName, value);
        }

        _logger.LogDebug("Recorded metric {MetricName} = {Value} for component {Component}",
            metricName, value, componentName);
    }

    /// <inheritdoc />
    public void RecordCounter(string counterName, long increment = 1, IDictionary<string, string>? tags = null)
    {
        ThrowIfDisposed();

        var componentName = ExtractComponentName(counterName, tags);
        var stats = GetOrCreateStatistics(componentName);

        lock (stats.Lock)
        {
            stats.RecordCounter(counterName, increment);
        }

        _logger.LogDebug("Recorded counter {CounterName} += {Increment} for component {Component}",
            counterName, increment, componentName);
    }

    /// <inheritdoc />
    public void RecordProcessingTime(string operationName, TimeSpan elapsed, IDictionary<string, string>? tags = null)
    {
        ThrowIfDisposed();

        var componentName = ExtractComponentName(operationName, tags);
        var stats = GetOrCreateStatistics(componentName);

        lock (stats.Lock)
        {
            stats.RecordProcessingTime(elapsed);
        }

        _logger.LogDebug("Recorded processing time {Elapsed}ms for operation {Operation} in component {Component}",
            elapsed.TotalMilliseconds, operationName, componentName);
    }

    /// <inheritdoc />
    public void RecordChunkProcessed(int chunkSize, TimeSpan processingTime, string componentName)
    {
        ThrowIfDisposed();

        var stats = GetOrCreateStatistics(componentName);

        lock (stats.Lock)
        {
            stats.RecordChunkProcessed(chunkSize, processingTime);
        }

        _logger.LogDebug("Recorded chunk processing: {ChunkSize} rows in {ProcessingTime}ms for component {Component}",
            chunkSize, processingTime.TotalMilliseconds, componentName);
    }

    /// <inheritdoc />
    public void RecordMemoryUsage(string componentName, long memoryUsage)
    {
        ThrowIfDisposed();

        var stats = GetOrCreateStatistics(componentName);

        lock (stats.Lock)
        {
            stats.RecordMemoryUsage(memoryUsage);
        }

        _logger.LogDebug("Recorded memory usage: {MemoryUsage} bytes for component {Component}",
            memoryUsage, componentName);
    }

    /// <inheritdoc />
    public PerformanceStatistics? GetStatistics(string componentName)
    {
        ThrowIfDisposed();

        if (!_statistics.TryGetValue(componentName, out var stats))
        {
            return null;
        }

        lock (stats.Lock)
        {
            return stats.ToPerformanceStatistics();
        }
    }

    /// <inheritdoc />
    public IReadOnlyDictionary<string, PerformanceStatistics> GetAllStatistics()
    {
        ThrowIfDisposed();

        var result = new Dictionary<string, PerformanceStatistics>();

        foreach (var kvp in _statistics)
        {
            lock (kvp.Value.Lock)
            {
                result[kvp.Key] = kvp.Value.ToPerformanceStatistics();
            }
        }

        return result;
    }

    /// <inheritdoc />
    public void ResetStatistics(string componentName)
    {
        ThrowIfDisposed();

        if (_statistics.TryGetValue(componentName, out var stats))
        {
            lock (stats.Lock)
            {
                stats.Reset();
            }

            _logger.LogInformation("Reset statistics for component {Component}", componentName);
        }
    }

    private ComponentStatistics GetOrCreateStatistics(string componentName)
    {
        return _statistics.GetOrAdd(componentName, name => new ComponentStatistics(name));
    }

    private static string ExtractComponentName(string operationName, IDictionary<string, string>? tags)
    {
        // Try to extract component name from tags first
        if (tags?.TryGetValue("component", out var component) == true)
        {
            return component;
        }

        // Extract from operation name (e.g., "PluginName.OperationName" -> "PluginName")
        var dotIndex = operationName.IndexOf('.');
        return dotIndex > 0 ? operationName[..dotIndex] : operationName;
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(PerformanceMonitor));
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (!_disposed)
        {
            _logger.LogInformation("Disposing PerformanceMonitor, tracked {ComponentCount} components",
                _statistics.Count);
            _disposed = true;
        }
    }

    /// <summary>
    /// Timer for measuring operation duration.
    /// </summary>
    private sealed class OperationTimer : IDisposable
    {
        private readonly PerformanceMonitor _monitor;
        private readonly string _operationName;
        private readonly IDictionary<string, string>? _tags;
        private readonly Stopwatch _stopwatch;

        public OperationTimer(PerformanceMonitor monitor, string operationName, IDictionary<string, string>? tags)
        {
            _monitor = monitor;
            _operationName = operationName;
            _tags = tags;
            _stopwatch = Stopwatch.StartNew();
        }

        public void Dispose()
        {
            _stopwatch.Stop();
            _monitor.RecordProcessingTime(_operationName, _stopwatch.Elapsed, _tags);
        }
    }

    /// <summary>
    /// Internal statistics tracking for a component.
    /// </summary>
    private sealed class ComponentStatistics
    {
        public string ComponentName { get; }
        public object Lock { get; } = new();

        private long _totalOperations;
        private TimeSpan _totalProcessingTime;
        private TimeSpan _minProcessingTime = TimeSpan.MaxValue;
        private TimeSpan _maxProcessingTime = TimeSpan.MinValue;
        private long _totalRowsProcessed;
        private long _currentMemoryUsage;
        private long _peakMemoryUsage;
        private DateTimeOffset _lastUpdated;

        public ComponentStatistics(string componentName)
        {
            ComponentName = componentName;
            _lastUpdated = DateTimeOffset.UtcNow;
        }

        public void RecordMetric(string metricName, double value)
        {
            // Custom metric handling can be added here
            _lastUpdated = DateTimeOffset.UtcNow;
        }

        public void RecordCounter(string counterName, long increment)
        {
            _totalOperations += increment;
            _lastUpdated = DateTimeOffset.UtcNow;
        }

        public void RecordProcessingTime(TimeSpan elapsed)
        {
            _totalOperations++;
            _totalProcessingTime += elapsed;

            if (elapsed < _minProcessingTime)
            {
                _minProcessingTime = elapsed;
            }

            if (elapsed > _maxProcessingTime)
            {
                _maxProcessingTime = elapsed;
            }

            _lastUpdated = DateTimeOffset.UtcNow;
        }

        public void RecordChunkProcessed(int chunkSize, TimeSpan processingTime)
        {
            _totalRowsProcessed += chunkSize;
            RecordProcessingTime(processingTime);
        }

        public void RecordMemoryUsage(long memoryUsage)
        {
            _currentMemoryUsage = memoryUsage;

            if (memoryUsage > _peakMemoryUsage)
            {
                _peakMemoryUsage = memoryUsage;
            }

            _lastUpdated = DateTimeOffset.UtcNow;
        }

        public void Reset()
        {
            _totalOperations = 0;
            _totalProcessingTime = TimeSpan.Zero;
            _minProcessingTime = TimeSpan.MaxValue;
            _maxProcessingTime = TimeSpan.MinValue;
            _totalRowsProcessed = 0;
            _currentMemoryUsage = 0;
            _peakMemoryUsage = 0;
            _lastUpdated = DateTimeOffset.UtcNow;
        }

        public PerformanceStatistics ToPerformanceStatistics()
        {
            var averageTime = _totalOperations > 0
                ? TimeSpan.FromTicks(_totalProcessingTime.Ticks / _totalOperations)
                : TimeSpan.Zero;

            var averageRowsPerSecond = _totalProcessingTime.TotalSeconds > 0
                ? _totalRowsProcessed / _totalProcessingTime.TotalSeconds
                : 0.0;

            return new PerformanceStatistics
            {
                ComponentName = ComponentName,
                TotalOperations = _totalOperations,
                TotalProcessingTime = _totalProcessingTime,
                AverageProcessingTime = averageTime,
                MinProcessingTime = _minProcessingTime == TimeSpan.MaxValue ? TimeSpan.Zero : _minProcessingTime,
                MaxProcessingTime = _maxProcessingTime == TimeSpan.MinValue ? TimeSpan.Zero : _maxProcessingTime,
                TotalRowsProcessed = _totalRowsProcessed,
                AverageRowsPerSecond = averageRowsPerSecond,
                CurrentMemoryUsage = _currentMemoryUsage,
                PeakMemoryUsage = _peakMemoryUsage,
                LastUpdated = _lastUpdated
            };
        }
    }
}
