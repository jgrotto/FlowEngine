using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Collections.Immutable;

namespace FlowEngine.Core.Services.Scripting;

/// <summary>
/// Metrics collector for script engine service performance and usage tracking.
/// Provides comprehensive monitoring of compilation, execution, caching, and pooling metrics.
/// </summary>
/// <remarks>
/// This class collects detailed metrics about script engine operations for:
/// - Performance monitoring and optimization
/// - Resource usage tracking and capacity planning  
/// - Error analysis and troubleshooting
/// - Service health assessment and alerting
/// 
/// All metrics are thread-safe and designed for high-frequency collection
/// with minimal performance impact on script execution.
/// </remarks>
public sealed class ScriptEngineMetrics
{
    private readonly IMetricsCollector? _metricsCollector;
    private readonly ConcurrentDictionary<string, long> _counters;
    private readonly ConcurrentDictionary<string, double> _gauges;
    private readonly ConcurrentQueue<string> _recentErrors;
    private readonly object _lockObject = new();
    
    private long _totalCompilations;
    private long _totalExecutions;
    private long _cacheHits;
    private long _cacheMisses;
    private long _compilationErrors;
    private long _executionErrors;
    private double _totalCompilationTime;
    private double _totalExecutionTime;

    /// <summary>
    /// Initializes a new instance of the ScriptEngineMetrics class.
    /// </summary>
    /// <param name="metricsCollector">Optional external metrics collector for integration</param>
    public ScriptEngineMetrics(IMetricsCollector? metricsCollector = null)
    {
        _metricsCollector = metricsCollector;
        _counters = new ConcurrentDictionary<string, long>();
        _gauges = new ConcurrentDictionary<string, double>();
        _recentErrors = new ConcurrentQueue<string>();
    }

    /// <summary>
    /// Records a script compilation event with timing and engine type.
    /// </summary>
    /// <param name="durationMs">Compilation duration in milliseconds</param>
    /// <param name="scriptSizeBytes">Size of the compiled script in bytes</param>
    /// <param name="engineType">Type of script engine used</param>
    public void RecordCompilation(long durationMs, int scriptSizeBytes, ScriptEngineType engineType)
    {
        Interlocked.Increment(ref _totalCompilations);
        
        lock (_lockObject)
        {
            _totalCompilationTime += durationMs;
        }
        
        _counters.AddOrUpdate($"compilations_{engineType}", 1, (_, existing) => existing + 1);
        _gauges.AddOrUpdate($"compilation_time_{engineType}", durationMs, (_, existing) => (existing + durationMs) / 2);
        
        _metricsCollector?.RecordHistogram("script_compilation_duration_ms", durationMs, 
            ("engine_type", engineType.ToString()));
        _metricsCollector?.RecordHistogram("script_size_bytes", scriptSizeBytes,
            ("engine_type", engineType.ToString()));
        _metricsCollector?.IncrementCounter("script_compilations_total",
            ("engine_type", engineType.ToString()));
    }

    /// <summary>
    /// Records a script execution event with timing and success status.
    /// </summary>
    /// <param name="durationMs">Execution duration in milliseconds</param>
    /// <param name="success">Whether the execution completed successfully</param>
    /// <param name="engineType">Type of script engine used</param>
    /// <param name="error">Error message if execution failed</param>
    public void RecordExecution(long durationMs, bool success, ScriptEngineType engineType, string? error = null)
    {
        Interlocked.Increment(ref _totalExecutions);
        
        lock (_lockObject)
        {
            _totalExecutionTime += durationMs;
        }
        
        if (success)
        {
            _counters.AddOrUpdate($"executions_success_{engineType}", 1, (_, existing) => existing + 1);
        }
        else
        {
            Interlocked.Increment(ref _executionErrors);
            _counters.AddOrUpdate($"executions_error_{engineType}", 1, (_, existing) => existing + 1);
            
            if (!string.IsNullOrEmpty(error))
            {
                RecordError($"Execution failed: {error}");
            }
        }
        
        _gauges.AddOrUpdate($"execution_time_{engineType}", durationMs, (_, existing) => (existing + durationMs) / 2);
        
        _metricsCollector?.RecordHistogram("script_execution_duration_ms", durationMs,
            ("engine_type", engineType.ToString()),
            ("success", success.ToString()));
        _metricsCollector?.IncrementCounter("script_executions_total",
            ("engine_type", engineType.ToString()),
            ("success", success.ToString()));
    }

    /// <summary>
    /// Records a cache hit event.
    /// </summary>
    public void RecordCacheHit()
    {
        Interlocked.Increment(ref _cacheHits);
        _metricsCollector?.IncrementCounter("script_cache_hits_total");
    }

    /// <summary>
    /// Records a cache miss event.
    /// </summary>
    public void RecordCacheMiss()
    {
        Interlocked.Increment(ref _cacheMisses);
        _metricsCollector?.IncrementCounter("script_cache_misses_total");
    }

    /// <summary>
    /// Records a cache eviction event.
    /// </summary>
    /// <param name="reason">Reason for the eviction</param>
    public void RecordCacheEviction(string reason)
    {
        _counters.AddOrUpdate($"cache_evictions_{reason}", 1, (_, existing) => existing + 1);
        _metricsCollector?.IncrementCounter("script_cache_evictions_total", ("reason", reason));
    }

    /// <summary>
    /// Records an engine acquisition from the pool.
    /// </summary>
    /// <param name="engineType">Type of engine acquired</param>
    public void RecordEngineAcquisition(ScriptEngineType engineType)
    {
        _counters.AddOrUpdate($"engine_acquisitions_{engineType}", 1, (_, existing) => existing + 1);
        _metricsCollector?.IncrementCounter("engine_acquisitions_total",
            ("engine_type", engineType.ToString()));
    }

    /// <summary>
    /// Records an engine return to the pool.
    /// </summary>
    /// <param name="engineType">Type of engine returned</param>
    public void RecordEngineReturn(ScriptEngineType engineType)
    {
        _counters.AddOrUpdate($"engine_returns_{engineType}", 1, (_, existing) => existing + 1);
        _metricsCollector?.IncrementCounter("engine_returns_total",
            ("engine_type", engineType.ToString()));
    }

    /// <summary>
    /// Records a service health check result.
    /// </summary>
    /// <param name="isHealthy">Whether the service is healthy</param>
    public void RecordHealthCheck(bool isHealthy)
    {
        _counters.AddOrUpdate($"health_checks_{(isHealthy ? "healthy" : "unhealthy")}", 1, 
            (_, existing) => existing + 1);
        _metricsCollector?.RecordGauge("script_engine_health", isHealthy ? 1 : 0);
    }

    /// <summary>
    /// Records an error for troubleshooting purposes.
    /// </summary>
    /// <param name="error">Error message or description</param>
    public void RecordError(string error)
    {
        if (string.IsNullOrWhiteSpace(error))
            return;
            
        _recentErrors.Enqueue($"{DateTimeOffset.UtcNow:yyyy-MM-dd HH:mm:ss} - {error}");
        
        // Keep only the last 100 errors
        while (_recentErrors.Count > 100)
        {
            _recentErrors.TryDequeue(out _);
        }
        
        _metricsCollector?.IncrementCounter("script_engine_errors_total");
    }

    /// <summary>
    /// Gets the total number of engine creations for the specified type.
    /// </summary>
    /// <param name="engineType">Engine type to query</param>
    /// <returns>Total number of engines created</returns>
    public long GetEngineCreationCount(ScriptEngineType engineType)
    {
        return _counters.GetValueOrDefault($"engine_creations_{engineType}", 0);
    }

    /// <summary>
    /// Gets the total number of engine destructions for the specified type.
    /// </summary>
    /// <param name="engineType">Engine type to query</param>
    /// <returns>Total number of engines destroyed</returns>
    public long GetEngineDestructionCount(ScriptEngineType engineType)
    {
        return _counters.GetValueOrDefault($"engine_destructions_{engineType}", 0);
    }

    /// <summary>
    /// Gets the total number of cache hits.
    /// </summary>
    /// <returns>Total cache hit count</returns>
    public long GetCacheHitCount()
    {
        return _cacheHits;
    }

    /// <summary>
    /// Gets the total number of cache misses.
    /// </summary>
    /// <returns>Total cache miss count</returns>
    public long GetCacheMissCount()
    {
        return _cacheMisses;
    }

    /// <summary>
    /// Gets comprehensive performance metrics.
    /// </summary>
    /// <returns>Performance metrics summary</returns>
    public PerformanceMetrics GetPerformanceMetrics()
    {
        lock (_lockObject)
        {
            var avgCompilationTime = _totalCompilations > 0 
                ? _totalCompilationTime / _totalCompilations 
                : 0;
                
            var avgExecutionTime = _totalExecutions > 0 
                ? _totalExecutionTime / _totalExecutions 
                : 0;

            return new PerformanceMetrics
            {
                AverageCompilationTimeMs = avgCompilationTime,
                AverageExecutionTimeMs = avgExecutionTime,
                TotalCompilations = _totalCompilations,
                TotalExecutions = _totalExecutions,
                CompilationErrors = _compilationErrors,
                ExecutionErrors = _executionErrors
            };
        }
    }

    /// <summary>
    /// Gets recent error messages for troubleshooting.
    /// </summary>
    /// <returns>Array of recent error messages</returns>
    public ImmutableArray<string> GetRecentErrors()
    {
        return _recentErrors.ToImmutableArray();
    }

    /// <summary>
    /// Resets all metrics to initial values.
    /// </summary>
    /// <remarks>
    /// This method is primarily for testing purposes and should be used
    /// carefully in production environments.
    /// </remarks>
    public void Reset()
    {
        _counters.Clear();
        _gauges.Clear();
        
        while (_recentErrors.TryDequeue(out _))
        {
            // Clear the queue
        }
        
        Interlocked.Exchange(ref _totalCompilations, 0);
        Interlocked.Exchange(ref _totalExecutions, 0);
        Interlocked.Exchange(ref _cacheHits, 0);
        Interlocked.Exchange(ref _cacheMisses, 0);
        Interlocked.Exchange(ref _compilationErrors, 0);
        Interlocked.Exchange(ref _executionErrors, 0);
        
        lock (_lockObject)
        {
            _totalCompilationTime = 0;
            _totalExecutionTime = 0;
        }
    }
}

/// <summary>
/// Interface for external metrics collection systems.
/// Allows integration with monitoring systems like Prometheus, Application Insights, etc.
/// </summary>
public interface IMetricsCollector
{
    /// <summary>
    /// Records a histogram value with optional tags.
    /// </summary>
    /// <param name="name">Metric name</param>
    /// <param name="value">Metric value</param>
    /// <param name="tags">Optional tags for metric categorization</param>
    void RecordHistogram(string name, double value, params (string Key, string Value)[] tags);

    /// <summary>
    /// Increments a counter with optional tags.
    /// </summary>
    /// <param name="name">Counter name</param>
    /// <param name="tags">Optional tags for counter categorization</param>
    void IncrementCounter(string name, params (string Key, string Value)[] tags);

    /// <summary>
    /// Records a gauge value with optional tags.
    /// </summary>
    /// <param name="name">Gauge name</param>
    /// <param name="value">Gauge value</param>
    /// <param name="tags">Optional tags for gauge categorization</param>
    void RecordGauge(string name, double value, params (string Key, string Value)[] tags);
}