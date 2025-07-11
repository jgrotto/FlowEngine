using FlowEngine.Abstractions;
using FlowEngine.Abstractions.Plugins;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Diagnostics;

namespace FlowEngine.Core.Monitoring;

/// <summary>
/// Monitors pipeline performance across all plugins and channels.
/// Provides real-time metrics, bottleneck detection, and performance analytics.
/// </summary>
public sealed class PipelinePerformanceMonitor : IDisposable
{
    private readonly ChannelTelemetry _channelTelemetry;
    private readonly ConcurrentDictionary<string, PluginPerformanceMetrics> _pluginMetrics = new();
    private readonly Timer _analysisTimer;
    private readonly object _analysisLock = new();
    private volatile bool _disposed;

    // Performance thresholds for alerting
    private readonly TimeSpan _highLatencyThreshold = TimeSpan.FromSeconds(5);
    private readonly double _lowThroughputThreshold = 1000; // rows/second
    private readonly double _highMemoryThreshold = 500_000_000; // 500MB

    /// <summary>
    /// Initializes a new pipeline performance monitor.
    /// </summary>
    /// <param name="channelTelemetry">Channel telemetry instance to use for metrics</param>
    public PipelinePerformanceMonitor(ChannelTelemetry channelTelemetry)
    {
        _channelTelemetry = channelTelemetry ?? throw new ArgumentNullException(nameof(channelTelemetry));
        _analysisTimer = new Timer(PerformAnalysis, null, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));
    }

    /// <summary>
    /// Event raised when performance issues are detected.
    /// </summary>
    public event EventHandler<PerformanceAlertEventArgs>? PerformanceAlert;

    /// <summary>
    /// Gets comprehensive pipeline performance report.
    /// </summary>
    public PipelinePerformanceReport GetPerformanceReport()
    {
        ThrowIfDisposed();

        var channelMetrics = _channelTelemetry.GetAllMetrics();
        var pluginMetrics = _pluginMetrics.ToImmutableDictionary();

        return new PipelinePerformanceReport
        {
            Timestamp = DateTimeOffset.UtcNow,
            ChannelMetrics = channelMetrics,
            PluginMetrics = pluginMetrics,
            OverallThroughput = CalculateOverallThroughput(channelMetrics),
            BottleneckAnalysis = IdentifyBottlenecks(channelMetrics, pluginMetrics),
            ResourceUsage = GetResourceUsage(),
            Recommendations = GenerateRecommendations(channelMetrics, pluginMetrics)
        };
    }

    /// <summary>
    /// Records plugin execution metrics.
    /// </summary>
    public void RecordPluginExecution(string pluginName, TimeSpan duration, long rowsProcessed, long memoryUsed)
    {
        if (_disposed)
        {
            return;
        }

        var metrics = _pluginMetrics.GetOrAdd(pluginName, _ => new PluginPerformanceMetrics { PluginName = pluginName });

        lock (_analysisLock)
        {
            metrics.TotalExecutions++;
            metrics.TotalProcessingTime += duration;
            metrics.TotalRowsProcessed += rowsProcessed;
            metrics.LastExecutionTime = DateTimeOffset.UtcNow;

            if (duration > metrics.MaxExecutionTime)
            {
                metrics.MaxExecutionTime = duration;
            }

            if (memoryUsed > metrics.PeakMemoryUsage)
            {
                metrics.PeakMemoryUsage = memoryUsed;
            }

            metrics.ExecutionDurations.Add(duration);
            metrics.CurrentMemoryUsage = memoryUsed;

            // Calculate throughput
            if (duration.TotalSeconds > 0)
            {
                var throughput = rowsProcessed / duration.TotalSeconds;
                if (throughput > metrics.PeakThroughput)
                {
                    metrics.PeakThroughput = throughput;
                }
            }
        }
    }

    /// <summary>
    /// Records plugin error for monitoring.
    /// </summary>
    public void RecordPluginError(string pluginName, Exception exception)
    {
        if (_disposed)
        {
            return;
        }

        var metrics = _pluginMetrics.GetOrAdd(pluginName, _ => new PluginPerformanceMetrics { PluginName = pluginName });

        lock (_analysisLock)
        {
            metrics.ErrorCount++;
            metrics.LastError = exception.Message;
            metrics.LastErrorTime = DateTimeOffset.UtcNow;
        }
    }

    /// <summary>
    /// Gets real-time plugin metrics.
    /// </summary>
    public PluginPerformanceMetrics? GetPluginMetrics(string pluginName)
    {
        ThrowIfDisposed();
        return _pluginMetrics.TryGetValue(pluginName, out var metrics) ? metrics : null;
    }

    private void PerformAnalysis(object? state)
    {
        if (_disposed)
        {
            return;
        }

        try
        {
            var alerts = new List<PerformanceAlert>();
            var channelMetrics = _channelTelemetry.GetAllMetrics();

            // Analyze channel performance
            foreach (var (channelId, metrics) in channelMetrics)
            {
                // Check for high latency
                if (metrics.AverageWriteLatency > _highLatencyThreshold)
                {
                    alerts.Add(new PerformanceAlert
                    {
                        Severity = AlertSeverity.Warning,
                        Component = $"Channel:{channelId}",
                        Message = $"High write latency detected: {metrics.AverageWriteLatency.TotalMilliseconds:F1}ms",
                        Metric = "WriteLatency",
                        Value = metrics.AverageWriteLatency.TotalMilliseconds
                    });
                }

                // Check for low throughput
                if (metrics.CurrentThroughputRowsPerSecond < _lowThroughputThreshold && metrics.CurrentThroughputRowsPerSecond > 0)
                {
                    alerts.Add(new PerformanceAlert
                    {
                        Severity = AlertSeverity.Warning,
                        Component = $"Channel:{channelId}",
                        Message = $"Low throughput detected: {metrics.CurrentThroughputRowsPerSecond:F0} rows/sec",
                        Metric = "Throughput",
                        Value = metrics.CurrentThroughputRowsPerSecond
                    });
                }

                // Check for backpressure
                if (metrics.BackpressureEvents > 0)
                {
                    var backpressureRate = metrics.BackpressureEvents / Math.Max(1.0, (DateTimeOffset.UtcNow - metrics.CreatedAt).TotalMinutes);
                    if (backpressureRate > 5) // More than 5 events per minute
                    {
                        alerts.Add(new PerformanceAlert
                        {
                            Severity = AlertSeverity.Critical,
                            Component = $"Channel:{channelId}",
                            Message = $"Frequent backpressure events: {backpressureRate:F1}/min",
                            Metric = "BackpressureRate",
                            Value = backpressureRate
                        });
                    }
                }
            }

            // Analyze plugin performance
            foreach (var (pluginName, metrics) in _pluginMetrics)
            {
                lock (_analysisLock)
                {
                    // Check for high memory usage
                    if (metrics.CurrentMemoryUsage > _highMemoryThreshold)
                    {
                        alerts.Add(new PerformanceAlert
                        {
                            Severity = AlertSeverity.Warning,
                            Component = $"Plugin:{pluginName}",
                            Message = $"High memory usage: {metrics.CurrentMemoryUsage / 1_000_000:F1}MB",
                            Metric = "MemoryUsage",
                            Value = metrics.CurrentMemoryUsage
                        });
                    }

                    // Check error rates
                    if (metrics.ErrorCount > 0 && metrics.TotalExecutions > 0)
                    {
                        var errorRate = (double)metrics.ErrorCount / metrics.TotalExecutions * 100;
                        if (errorRate > 5) // More than 5% error rate
                        {
                            alerts.Add(new PerformanceAlert
                            {
                                Severity = AlertSeverity.Critical,
                                Component = $"Plugin:{pluginName}",
                                Message = $"High error rate: {errorRate:F1}%",
                                Metric = "ErrorRate",
                                Value = errorRate
                            });
                        }
                    }

                    // Update average execution time
                    if (metrics.ExecutionDurations.Count > 0)
                    {
                        metrics.AverageExecutionTime = TimeSpan.FromTicks((long)metrics.ExecutionDurations.Average(d => d.Ticks));
                    }
                }
            }

            // Raise alerts if any found
            if (alerts.Count > 0)
            {
                PerformanceAlert?.Invoke(this, new PerformanceAlertEventArgs(alerts));
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Performance analysis error: {ex}");
        }
    }

    private double CalculateOverallThroughput(IReadOnlyDictionary<string, ChannelMetrics> channelMetrics)
    {
        return channelMetrics.Values.Sum(m => m.CurrentThroughputRowsPerSecond);
    }

    private BottleneckAnalysis IdentifyBottlenecks(IReadOnlyDictionary<string, ChannelMetrics> channelMetrics, IReadOnlyDictionary<string, PluginPerformanceMetrics> pluginMetrics)
    {
        var bottlenecks = new List<string>();

        // Find slowest channel
        var slowestChannel = channelMetrics
            .Where(kvp => kvp.Value.CurrentThroughputRowsPerSecond > 0)
            .OrderBy(kvp => kvp.Value.CurrentThroughputRowsPerSecond)
            .FirstOrDefault();

        if (slowestChannel.Key != null)
        {
            bottlenecks.Add($"Channel '{slowestChannel.Key}' has lowest throughput: {slowestChannel.Value.CurrentThroughputRowsPerSecond:F0} rows/sec");
        }

        // Find slowest plugin
        var slowestPlugin = pluginMetrics.Values
            .Where(m => m.AverageExecutionTime > TimeSpan.Zero)
            .OrderByDescending(m => m.AverageExecutionTime)
            .FirstOrDefault();

        if (slowestPlugin != null)
        {
            bottlenecks.Add($"Plugin '{slowestPlugin.PluginName}' has highest average execution time: {slowestPlugin.AverageExecutionTime.TotalMilliseconds:F1}ms");
        }

        return new BottleneckAnalysis
        {
            IdentifiedBottlenecks = bottlenecks,
            SlowestComponent = slowestChannel.Key ?? slowestPlugin?.PluginName ?? "Unknown",
            RecommendedActions = GenerateBottleneckRecommendations(slowestChannel.Value, slowestPlugin)
        };
    }

    private ResourceUsage GetResourceUsage()
    {
        var process = Process.GetCurrentProcess();
        return new ResourceUsage
        {
            MemoryUsageMB = process.WorkingSet64 / 1_000_000.0,
            CpuTimeTotal = process.TotalProcessorTime,
            ThreadCount = process.Threads.Count,
            HandleCount = process.HandleCount
        };
    }

    private List<string> GenerateRecommendations(IReadOnlyDictionary<string, ChannelMetrics> channelMetrics, IReadOnlyDictionary<string, PluginPerformanceMetrics> pluginMetrics)
    {
        var recommendations = new List<string>();

        // Check for over-utilized channels
        foreach (var (channelId, metrics) in channelMetrics)
        {
            if (metrics.PeakCapacityUtilization > 90)
            {
                recommendations.Add($"Consider increasing buffer size for channel '{channelId}' (current utilization: {metrics.PeakCapacityUtilization:F1}%)");
            }

            if (metrics.BackpressureEvents > 100)
            {
                recommendations.Add($"Channel '{channelId}' experiencing frequent backpressure - consider optimizing downstream processing");
            }
        }

        // Check for memory-intensive plugins
        foreach (var (pluginName, metrics) in pluginMetrics)
        {
            if (metrics.PeakMemoryUsage > 100_000_000) // 100MB
            {
                recommendations.Add($"Plugin '{pluginName}' has high memory usage - consider implementing streaming or chunked processing");
            }

            if (metrics.ErrorCount > 0)
            {
                recommendations.Add($"Plugin '{pluginName}' has {metrics.ErrorCount} errors - review error handling and data validation");
            }
        }

        return recommendations;
    }

    private static List<string> GenerateBottleneckRecommendations(ChannelMetrics? channelMetrics, PluginPerformanceMetrics? pluginMetrics)
    {
        var recommendations = new List<string>();

        if (channelMetrics != null)
        {
            recommendations.Add("Increase channel buffer size");
            recommendations.Add("Optimize downstream plugin performance");
            recommendations.Add("Consider parallel processing");
        }

        if (pluginMetrics != null)
        {
            recommendations.Add("Profile plugin execution for optimization opportunities");
            recommendations.Add("Consider algorithmic improvements");
            recommendations.Add("Implement caching where appropriate");
        }

        return recommendations;
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(PipelinePerformanceMonitor));
        }
    }

    /// <summary>
    /// Disposes the performance monitor and releases resources.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _analysisTimer?.Dispose();
    }
}

/// <summary>
/// Performance metrics for a specific plugin.
/// </summary>
public sealed class PluginPerformanceMetrics
{
    /// <summary>Name of the plugin.</summary>
    public required string PluginName { get; init; }

    /// <summary>Total number of executions.</summary>
    public long TotalExecutions { get; set; }
    /// <summary>Total processing time across all executions.</summary>
    public TimeSpan TotalProcessingTime { get; set; }
    /// <summary>Average execution time.</summary>
    public TimeSpan AverageExecutionTime { get; set; }
    /// <summary>Maximum execution time observed.</summary>
    public TimeSpan MaxExecutionTime { get; set; }
    /// <summary>Total number of rows processed.</summary>
    public long TotalRowsProcessed { get; set; }
    /// <summary>Peak throughput in rows per second.</summary>
    public double PeakThroughput { get; set; }

    /// <summary>Current memory usage in bytes.</summary>
    public long CurrentMemoryUsage { get; set; }
    /// <summary>Peak memory usage in bytes.</summary>
    public long PeakMemoryUsage { get; set; }

    /// <summary>Total number of errors encountered.</summary>
    public long ErrorCount { get; set; }
    /// <summary>Last error message.</summary>
    public string? LastError { get; set; }
    /// <summary>Timestamp of the last error.</summary>
    public DateTimeOffset? LastErrorTime { get; set; }
    /// <summary>Timestamp of the last execution.</summary>
    public DateTimeOffset? LastExecutionTime { get; set; }

    internal List<TimeSpan> ExecutionDurations { get; } = new();
}

/// <summary>
/// Comprehensive pipeline performance report.
/// </summary>
public sealed class PipelinePerformanceReport
{
    /// <summary>Report generation timestamp.</summary>
    public required DateTimeOffset Timestamp { get; init; }
    /// <summary>Channel performance metrics by channel ID.</summary>
    public required IReadOnlyDictionary<string, ChannelMetrics> ChannelMetrics { get; init; }
    /// <summary>Plugin performance metrics by plugin name.</summary>
    public required IReadOnlyDictionary<string, PluginPerformanceMetrics> PluginMetrics { get; init; }
    /// <summary>Overall pipeline throughput in rows per second.</summary>
    public required double OverallThroughput { get; init; }
    /// <summary>Analysis of performance bottlenecks.</summary>
    public required BottleneckAnalysis BottleneckAnalysis { get; init; }
    /// <summary>System resource usage information.</summary>
    public required ResourceUsage ResourceUsage { get; init; }
    /// <summary>Performance optimization recommendations.</summary>
    public required List<string> Recommendations { get; init; }
}

/// <summary>
/// Analysis of pipeline bottlenecks.
/// </summary>
public sealed class BottleneckAnalysis
{
    /// <summary>List of identified performance bottlenecks.</summary>
    public required List<string> IdentifiedBottlenecks { get; init; }
    /// <summary>Name of the slowest component in the pipeline.</summary>
    public required string SlowestComponent { get; init; }
    /// <summary>Recommended actions to address bottlenecks.</summary>
    public required List<string> RecommendedActions { get; init; }
}

/// <summary>
/// System resource usage metrics.
/// </summary>
public sealed class ResourceUsage
{
    /// <summary>Current memory usage in megabytes.</summary>
    public required double MemoryUsageMB { get; init; }
    /// <summary>Total CPU time consumed.</summary>
    public required TimeSpan CpuTimeTotal { get; init; }
    /// <summary>Current thread count.</summary>
    public required int ThreadCount { get; init; }
    /// <summary>Current handle count.</summary>
    public required int HandleCount { get; init; }
}

/// <summary>
/// Performance alert information.
/// </summary>
public sealed class PerformanceAlert
{
    /// <summary>Alert severity level.</summary>
    public required AlertSeverity Severity { get; init; }
    /// <summary>Component that triggered the alert.</summary>
    public required string Component { get; init; }
    /// <summary>Alert message describing the issue.</summary>
    public required string Message { get; init; }
    /// <summary>Metric name that triggered the alert.</summary>
    public required string Metric { get; init; }
    /// <summary>Metric value that triggered the alert.</summary>
    public required double Value { get; init; }
    /// <summary>Timestamp when the alert was generated.</summary>
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// Event args for performance alerts.
/// </summary>
public sealed class PerformanceAlertEventArgs : EventArgs
{
    /// <summary>Initializes new performance alert event args.</summary>
    /// <param name="alerts">List of performance alerts</param>
    public PerformanceAlertEventArgs(List<PerformanceAlert> alerts)
    {
        Alerts = alerts ?? throw new ArgumentNullException(nameof(alerts));
    }

    /// <summary>List of performance alerts that were raised.</summary>
    public List<PerformanceAlert> Alerts { get; }
}

/// <summary>
/// Severity levels for performance alerts.
/// </summary>
public enum AlertSeverity
{
    /// <summary>Informational alert.</summary>
    Info,
    /// <summary>Warning level alert.</summary>
    Warning,
    /// <summary>Critical level alert requiring immediate attention.</summary>
    Critical
}
