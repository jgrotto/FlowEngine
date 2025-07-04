using System.Diagnostics;

namespace FlowEngine.Abstractions.Services;

/// <summary>
/// Provides performance monitoring and telemetry services for FlowEngine components.
/// Enables plugins to track performance metrics and identify bottlenecks.
/// </summary>
public interface IPerformanceMonitor
{
    /// <summary>
    /// Starts a new performance measurement operation.
    /// </summary>
    /// <param name="operationName">The name of the operation being measured</param>
    /// <param name="tags">Optional tags to associate with the measurement</param>
    /// <returns>A disposable operation handle</returns>
    IDisposable StartOperation(string operationName, IDictionary<string, string>? tags = null);

    /// <summary>
    /// Records a metric value.
    /// </summary>
    /// <param name="metricName">The name of the metric</param>
    /// <param name="value">The metric value</param>
    /// <param name="tags">Optional tags to associate with the metric</param>
    void RecordMetric(string metricName, double value, IDictionary<string, string>? tags = null);

    /// <summary>
    /// Records a counter increment.
    /// </summary>
    /// <param name="counterName">The name of the counter</param>
    /// <param name="increment">The increment value (default: 1)</param>
    /// <param name="tags">Optional tags to associate with the counter</param>
    void RecordCounter(string counterName, long increment = 1, IDictionary<string, string>? tags = null);

    /// <summary>
    /// Records a processing time measurement.
    /// </summary>
    /// <param name="operationName">The name of the operation</param>
    /// <param name="elapsed">The elapsed time</param>
    /// <param name="tags">Optional tags to associate with the measurement</param>
    void RecordProcessingTime(string operationName, TimeSpan elapsed, IDictionary<string, string>? tags = null);

    /// <summary>
    /// Records the processing of a data chunk.
    /// </summary>
    /// <param name="chunkSize">The size of the chunk processed</param>
    /// <param name="processingTime">The time taken to process the chunk</param>
    /// <param name="componentName">The name of the component that processed the chunk</param>
    void RecordChunkProcessed(int chunkSize, TimeSpan processingTime, string componentName);

    /// <summary>
    /// Records memory usage for a component.
    /// </summary>
    /// <param name="componentName">The name of the component</param>
    /// <param name="memoryUsage">The current memory usage in bytes</param>
    void RecordMemoryUsage(string componentName, long memoryUsage);

    /// <summary>
    /// Gets the current performance statistics for a component.
    /// </summary>
    /// <param name="componentName">The name of the component</param>
    /// <returns>Performance statistics, or null if not available</returns>
    PerformanceStatistics? GetStatistics(string componentName);

    /// <summary>
    /// Gets performance statistics for all monitored components.
    /// </summary>
    /// <returns>A dictionary of component names to their performance statistics</returns>
    IReadOnlyDictionary<string, PerformanceStatistics> GetAllStatistics();

    /// <summary>
    /// Resets performance statistics for a component.
    /// </summary>
    /// <param name="componentName">The name of the component</param>
    void ResetStatistics(string componentName);

    /// <summary>
    /// Checks if performance monitoring is enabled.
    /// </summary>
    bool IsEnabled { get; }
}

/// <summary>
/// Represents performance statistics for a component.
/// </summary>
public sealed class PerformanceStatistics
{
    /// <summary>
    /// Gets the component name.
    /// </summary>
    public string ComponentName { get; init; } = string.Empty;

    /// <summary>
    /// Gets the total number of operations performed.
    /// </summary>
    public long TotalOperations { get; init; }

    /// <summary>
    /// Gets the total processing time.
    /// </summary>
    public TimeSpan TotalProcessingTime { get; init; }

    /// <summary>
    /// Gets the average processing time per operation.
    /// </summary>
    public TimeSpan AverageProcessingTime { get; init; }

    /// <summary>
    /// Gets the minimum processing time observed.
    /// </summary>
    public TimeSpan MinProcessingTime { get; init; }

    /// <summary>
    /// Gets the maximum processing time observed.
    /// </summary>
    public TimeSpan MaxProcessingTime { get; init; }

    /// <summary>
    /// Gets the total number of rows processed.
    /// </summary>
    public long TotalRowsProcessed { get; init; }

    /// <summary>
    /// Gets the average rows per second.
    /// </summary>
    public double AverageRowsPerSecond { get; init; }

    /// <summary>
    /// Gets the current memory usage in bytes.
    /// </summary>
    public long CurrentMemoryUsage { get; init; }

    /// <summary>
    /// Gets the peak memory usage in bytes.
    /// </summary>
    public long PeakMemoryUsage { get; init; }

    /// <summary>
    /// Gets the timestamp when statistics were last updated.
    /// </summary>
    public DateTimeOffset LastUpdated { get; init; }
}