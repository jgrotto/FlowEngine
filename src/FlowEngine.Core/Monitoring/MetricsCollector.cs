using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace FlowEngine.Core.Monitoring;

/// <summary>
/// Collects and aggregates performance metrics for monitoring and alerting.
/// Provides OpenTelemetry-compatible metrics for production observability.
/// </summary>
public sealed class MetricsCollector : IDisposable
{
    private static readonly Meter Meter = new("FlowEngine", "1.0.0");

    private readonly ILogger<MetricsCollector> _logger;
    private readonly ConcurrentDictionary<string, MetricTracker> _trackers = new();
    private readonly Timer _aggregationTimer;
    private readonly object _lock = new();
    private bool _disposed;

    // Core metrics instruments
    private readonly Counter<long> _operationCounter;
    private readonly Counter<long> _errorCounter;
    private readonly Histogram<double> _operationDuration;
    private readonly Histogram<long> _memoryUsage;
    private readonly Histogram<long> _dataProcessingThroughput;
    private readonly UpDownCounter<int> _activeOperations;
    private readonly ObservableGauge<double>? _systemCpuUsage;
    private readonly ObservableGauge<long>? _systemMemoryUsage;

    /// <summary>
    /// Gets the current metrics summary.
    /// </summary>
    public MetricsSummary CurrentSummary { get; private set; } = new();

    /// <summary>
    /// Event raised when metrics are aggregated.
    /// </summary>
    public event EventHandler<MetricsAggregatedEventArgs>? MetricsAggregated;

    public MetricsCollector(ILogger<MetricsCollector> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        // Initialize metric instruments
        _operationCounter = Meter.CreateCounter<long>("flowengine_operations_total", "operations", "Total number of operations");
        _errorCounter = Meter.CreateCounter<long>("flowengine_errors_total", "errors", "Total number of errors");
        _operationDuration = Meter.CreateHistogram<double>("flowengine_operation_duration_ms", "milliseconds", "Operation duration");
        _memoryUsage = Meter.CreateHistogram<long>("flowengine_memory_usage_bytes", "bytes", "Memory usage");
        _dataProcessingThroughput = Meter.CreateHistogram<long>("flowengine_data_throughput_rows_per_second", "rows/second", "Data processing throughput");
        _activeOperations = Meter.CreateUpDownCounter<int>("flowengine_active_operations", "operations", "Number of active operations");

        // TODO: Fix CreateObservableGauge method signature
        // _systemCpuUsage = Meter.CreateObservableGauge<double>("flowengine_system_cpu_usage_percent", "percent", "System CPU usage percentage");
        // _systemMemoryUsage = Meter.CreateObservableGauge<long>("flowengine_system_memory_usage_bytes", "bytes", "System memory usage");

        // Start aggregation timer
        _aggregationTimer = new Timer(AggregateMetrics, null, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(30));

        _logger.LogInformation("Metrics collector initialized with OpenTelemetry instruments");
    }

    /// <summary>
    /// Records the start of an operation.
    /// </summary>
    public OperationMetrics StartOperation(string operationName, Dictionary<string, object>? tags = null)
    {
        var tagList = CreateTagList(tags);
        tagList.Add(new KeyValuePair<string, object?>("operation", operationName));

        _operationCounter.Add(1);
        _activeOperations.Add(1);

        var tracker = GetOrCreateTracker(operationName);
        tracker.RecordOperationStart();

        return new OperationMetrics(this, operationName, tagList.ToArray(), Stopwatch.StartNew());
    }

    /// <summary>
    /// Records the completion of an operation.
    /// </summary>
    public void CompleteOperation(string operationName, TimeSpan duration, bool success = true, Dictionary<string, object>? tags = null)
    {
        var tagList = CreateTagList(tags);
        tagList.Add(new KeyValuePair<string, object?>("operation", operationName));
        tagList.Add(new KeyValuePair<string, object?>("success", success));

        _operationDuration.Record(duration.TotalMilliseconds);
        _activeOperations.Add(-1, new KeyValuePair<string, object?>("operation", operationName));

        if (!success)
        {
            _errorCounter.Add(1);
        }

        var tracker = GetOrCreateTracker(operationName);
        tracker.RecordOperationComplete(duration, success);

        _logger.LogDebug("Operation {Operation} completed in {Duration}ms. Success: {Success}",
            operationName, duration.TotalMilliseconds, success);
    }

    /// <summary>
    /// Records an error occurrence.
    /// </summary>
    public void RecordError(string operationName, string errorType, Exception? exception = null, Dictionary<string, object>? tags = null)
    {
        var tagList = CreateTagList(tags);
        tagList.Add(new KeyValuePair<string, object?>("operation", operationName));
        tagList.Add(new KeyValuePair<string, object?>("error_type", errorType));

        if (exception != null)
        {
            tagList.Add(new KeyValuePair<string, object?>("exception_type", exception.GetType().Name));
        }

        _errorCounter.Add(1);

        var tracker = GetOrCreateTracker(operationName);
        tracker.RecordError(errorType);

        _logger.LogDebug("Error recorded for operation {Operation}: {ErrorType}", operationName, errorType);
    }

    /// <summary>
    /// Records memory usage.
    /// </summary>
    public void RecordMemoryUsage(long bytes, string context, Dictionary<string, object>? tags = null)
    {
        var tagList = CreateTagList(tags);
        tagList.Add(new KeyValuePair<string, object?>("context", context));

        _memoryUsage.Record(bytes);

        _logger.LogDebug("Memory usage recorded: {Bytes} bytes for context {Context}", bytes, context);
    }

    /// <summary>
    /// Records data processing throughput.
    /// </summary>
    public void RecordDataThroughput(long rowsPerSecond, string operation, Dictionary<string, object>? tags = null)
    {
        var tagList = CreateTagList(tags);
        tagList.Add(new KeyValuePair<string, object?>("operation", operation));

        _dataProcessingThroughput.Record(rowsPerSecond);

        var tracker = GetOrCreateTracker($"throughput_{operation}");
        tracker.RecordThroughput(rowsPerSecond);

        _logger.LogDebug("Data throughput recorded: {RowsPerSecond} rows/second for operation {Operation}",
            rowsPerSecond, operation);
    }

    /// <summary>
    /// Records a custom metric value.
    /// </summary>
    public void RecordCustomMetric(string metricName, double value, Dictionary<string, object>? tags = null)
    {
        var tagList = CreateTagList(tags);
        tagList.Add(new KeyValuePair<string, object?>("metric_name", metricName));

        // For custom metrics, we'll use the operation duration histogram as a generic recorder
        // In a production system, you'd want dedicated instruments for custom metrics
        _operationDuration.Record(value);

        var tracker = GetOrCreateTracker($"custom_{metricName}");
        tracker.RecordCustomValue(value);

        _logger.LogDebug("Custom metric recorded: {MetricName} = {Value}", metricName, value);
    }

    /// <summary>
    /// Gets metrics for a specific operation.
    /// </summary>
    public OperationMetricsSummary? GetOperationMetrics(string operationName)
    {
        if (_trackers.TryGetValue(operationName, out var tracker))
        {
            return tracker.GetSummary();
        }
        return null;
    }

    /// <summary>
    /// Gets all operation metrics.
    /// </summary>
    public IReadOnlyDictionary<string, OperationMetricsSummary> GetAllOperationMetrics()
    {
        return _trackers.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.GetSummary());
    }

    /// <summary>
    /// Resets all metrics counters and trackers.
    /// </summary>
    public void Reset()
    {
        lock (_lock)
        {
            _trackers.Clear();
            CurrentSummary = new MetricsSummary();
        }

        _logger.LogInformation("Metrics collector reset");
    }

    /// <summary>
    /// Exports metrics in Prometheus format for monitoring systems.
    /// </summary>
    public string ExportPrometheusMetrics()
    {
        var metrics = new List<string>();
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        foreach (var kvp in _trackers)
        {
            var operationName = SanitizeMetricName(kvp.Key);
            var summary = kvp.Value.GetSummary();

            // Operation counts
            metrics.Add($"flowengine_operation_total{{operation=\"{operationName}\"}} {summary.TotalOperations} {timestamp}");
            metrics.Add($"flowengine_operation_errors{{operation=\"{operationName}\"}} {summary.ErrorCount} {timestamp}");

            // Timing metrics
            metrics.Add($"flowengine_operation_duration_avg{{operation=\"{operationName}\"}} {summary.AverageDuration.TotalMilliseconds} {timestamp}");
            metrics.Add($"flowengine_operation_duration_min{{operation=\"{operationName}\"}} {summary.MinDuration.TotalMilliseconds} {timestamp}");
            metrics.Add($"flowengine_operation_duration_max{{operation=\"{operationName}\"}} {summary.MaxDuration.TotalMilliseconds} {timestamp}");

            // Throughput if available
            if (summary.AverageThroughput > 0)
            {
                metrics.Add($"flowengine_throughput_avg{{operation=\"{operationName}\"}} {summary.AverageThroughput} {timestamp}");
            }
        }

        // System metrics
        metrics.Add($"flowengine_system_memory_bytes {GetMemoryUsage()} {timestamp}");
        metrics.Add($"flowengine_system_cpu_percent {GetCpuUsage()} {timestamp}");

        return string.Join("\n", metrics);
    }

    private MetricTracker GetOrCreateTracker(string operationName)
    {
        return _trackers.GetOrAdd(operationName, _ => new MetricTracker(operationName));
    }

    private List<KeyValuePair<string, object?>> CreateTagList(Dictionary<string, object>? tags)
    {
        var tagList = new List<KeyValuePair<string, object?>>
        {
            new("machine", Environment.MachineName),
            new("process_id", Environment.ProcessId)
        };

        if (tags != null)
        {
            foreach (var tag in tags)
            {
                tagList.Add(new KeyValuePair<string, object?>(tag.Key, tag.Value));
            }
        }

        return tagList;
    }

    private double GetCpuUsage()
    {
        try
        {
            // Simplified CPU usage calculation
            // In production, would use performance counters or system APIs
            var process = Process.GetCurrentProcess();
            var threadCount = process.Threads.Count;
            var processorCount = Environment.ProcessorCount;

            // Rough heuristic - actual CPU measurement would require performance counters
            return Math.Min((threadCount / (double)processorCount) * 10, 100);
        }
        catch
        {
            return 0;
        }
    }

    private long GetMemoryUsage()
    {
        return GC.GetTotalMemory(false);
    }

    private string SanitizeMetricName(string name)
    {
        return name.Replace(" ", "_").Replace("-", "_").ToLowerInvariant();
    }

    private void AggregateMetrics(object? state)
    {
        try
        {
            var summary = new MetricsSummary
            {
                Timestamp = DateTimeOffset.UtcNow,
                TotalOperations = _trackers.Values.Sum(t => t.GetSummary().TotalOperations),
                TotalErrors = _trackers.Values.Sum(t => t.GetSummary().ErrorCount),
                AverageOperationDuration = TimeSpan.FromMilliseconds(
                    _trackers.Values.Where(t => t.GetSummary().TotalOperations > 0)
                             .Select(t => t.GetSummary().AverageDuration.TotalMilliseconds)
                             .DefaultIfEmpty(0).Average()),
                MemoryUsage = GetMemoryUsage(),
                CpuUsage = GetCpuUsage(),
                ActiveOperations = _trackers.Values.Sum(t => t.ActiveOperations)
            };

            CurrentSummary = summary;

            // Raise aggregation event
            try
            {
                MetricsAggregated?.Invoke(this, new MetricsAggregatedEventArgs(summary));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error in metrics aggregated event handler");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during metrics aggregation");
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        try
        {
            _aggregationTimer?.Dispose();
            Meter?.Dispose();
            _logger.LogInformation("Metrics collector disposed");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error during metrics collector disposal");
        }
        finally
        {
            _disposed = true;
        }
    }
}

/// <summary>
/// Tracks metrics for a specific operation.
/// </summary>
internal sealed class MetricTracker
{
    private readonly string _operationName;
    private readonly object _lock = new();
    private long _totalOperations;
    private long _errorCount;
    private long _activeOperations;
    private readonly List<TimeSpan> _durations = new();
    private readonly List<double> _throughputValues = new();
    private readonly List<double> _customValues = new();
    private readonly Dictionary<string, long> _errorTypes = new();

    public string OperationName => _operationName;
    public long ActiveOperations => _activeOperations;

    public MetricTracker(string operationName)
    {
        _operationName = operationName;
    }

    public void RecordOperationStart()
    {
        lock (_lock)
        {
            Interlocked.Increment(ref _activeOperations);
        }
    }

    public void RecordOperationComplete(TimeSpan duration, bool success)
    {
        lock (_lock)
        {
            _totalOperations++;
            _durations.Add(duration);
            Interlocked.Decrement(ref _activeOperations);

            if (!success)
            {
                _errorCount++;
            }

            // Keep only recent durations to prevent memory growth
            if (_durations.Count > 1000)
            {
                _durations.RemoveRange(0, _durations.Count - 1000);
            }
        }
    }

    public void RecordError(string errorType)
    {
        lock (_lock)
        {
            _errorCount++;
            _errorTypes[errorType] = _errorTypes.GetValueOrDefault(errorType, 0) + 1;
        }
    }

    public void RecordThroughput(long rowsPerSecond)
    {
        lock (_lock)
        {
            _throughputValues.Add(rowsPerSecond);

            // Keep only recent values
            if (_throughputValues.Count > 100)
            {
                _throughputValues.RemoveRange(0, _throughputValues.Count - 100);
            }
        }
    }

    public void RecordCustomValue(double value)
    {
        lock (_lock)
        {
            _customValues.Add(value);

            // Keep only recent values
            if (_customValues.Count > 100)
            {
                _customValues.RemoveRange(0, _customValues.Count - 100);
            }
        }
    }

    public OperationMetricsSummary GetSummary()
    {
        lock (_lock)
        {
            return new OperationMetricsSummary
            {
                OperationName = _operationName,
                TotalOperations = _totalOperations,
                ErrorCount = _errorCount,
                ActiveOperations = _activeOperations,
                AverageDuration = _durations.Any() ? TimeSpan.FromMilliseconds(_durations.Average(d => d.TotalMilliseconds)) : TimeSpan.Zero,
                MinDuration = _durations.Any() ? _durations.Min() : TimeSpan.Zero,
                MaxDuration = _durations.Any() ? _durations.Max() : TimeSpan.Zero,
                AverageThroughput = _throughputValues.Any() ? _throughputValues.Average() : 0,
                ErrorTypes = _errorTypes.ToDictionary(kvp => kvp.Key, kvp => kvp.Value),
                LastUpdate = DateTimeOffset.UtcNow
            };
        }
    }
}

/// <summary>
/// Represents an active operation measurement.
/// </summary>
public sealed class OperationMetrics : IDisposable
{
    private readonly MetricsCollector _collector;
    private readonly string _operationName;
    private readonly KeyValuePair<string, object?>[] _tags;
    private readonly Stopwatch _stopwatch;
    private readonly Dictionary<string, object> _additionalData = new();
    private bool _completed;
    private bool _disposed;

    internal OperationMetrics(MetricsCollector collector, string operationName, KeyValuePair<string, object?>[] tags, Stopwatch stopwatch)
    {
        _collector = collector;
        _operationName = operationName;
        _tags = tags;
        _stopwatch = stopwatch;
    }

    /// <summary>
    /// Adds additional data to be included with the metrics.
    /// </summary>
    public void AddData(string key, object value)
    {
        _additionalData[key] = value;
    }

    /// <summary>
    /// Records an error for this operation.
    /// </summary>
    public void RecordError(string errorType, Exception? exception = null)
    {
        _collector.RecordError(_operationName, errorType, exception, _additionalData);
    }

    /// <summary>
    /// Completes the operation measurement with success status.
    /// </summary>
    public void Complete(bool success = true)
    {
        if (_completed)
        {
            return;
        }

        _stopwatch.Stop();
        _collector.CompleteOperation(_operationName, _stopwatch.Elapsed, success, _additionalData);
        _completed = true;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        if (!_completed)
        {
            Complete(true);
        }

        _disposed = true;
    }
}

/// <summary>
/// Summary of metrics for a specific operation.
/// </summary>
public sealed class OperationMetricsSummary
{
    public string OperationName { get; set; } = string.Empty;
    public long TotalOperations { get; set; }
    public long ErrorCount { get; set; }
    public long ActiveOperations { get; set; }
    public TimeSpan AverageDuration { get; set; }
    public TimeSpan MinDuration { get; set; }
    public TimeSpan MaxDuration { get; set; }
    public double AverageThroughput { get; set; }
    public Dictionary<string, long> ErrorTypes { get; set; } = new();
    public DateTimeOffset LastUpdate { get; set; }

    public double SuccessRate => TotalOperations > 0 ? (TotalOperations - ErrorCount) / (double)TotalOperations : 0;
    public double ErrorRate => TotalOperations > 0 ? ErrorCount / (double)TotalOperations : 0;
}

/// <summary>
/// Overall system metrics summary.
/// </summary>
public sealed class MetricsSummary
{
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
    public long TotalOperations { get; set; }
    public long TotalErrors { get; set; }
    public TimeSpan AverageOperationDuration { get; set; }
    public long MemoryUsage { get; set; }
    public double CpuUsage { get; set; }
    public long ActiveOperations { get; set; }

    public double OverallSuccessRate => TotalOperations > 0 ? (TotalOperations - TotalErrors) / (double)TotalOperations : 0;
}

/// <summary>
/// Event arguments for metrics aggregation events.
/// </summary>
public sealed class MetricsAggregatedEventArgs : EventArgs
{
    public MetricsSummary Summary { get; }

    public MetricsAggregatedEventArgs(MetricsSummary summary)
    {
        Summary = summary;
    }
}
