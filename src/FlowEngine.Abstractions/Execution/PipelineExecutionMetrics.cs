namespace FlowEngine.Abstractions.Execution;

/// <summary>
/// Performance metrics for pipeline execution.
/// </summary>
public sealed record PipelineExecutionMetrics
{
    /// <summary>
    /// Gets the total execution time.
    /// </summary>
    public TimeSpan TotalExecutionTime { get; init; }

    /// <summary>
    /// Gets the total number of rows processed.
    /// </summary>
    public long TotalRowsProcessed { get; init; }

    /// <summary>
    /// Gets the total number of chunks processed.
    /// </summary>
    public long TotalChunksProcessed { get; init; }

    /// <summary>
    /// Gets the average throughput in rows per second.
    /// </summary>
    public double RowsPerSecond => TotalExecutionTime.TotalSeconds > 0 ? TotalRowsProcessed / TotalExecutionTime.TotalSeconds : 0;

    /// <summary>
    /// Gets the peak memory usage during execution.
    /// </summary>
    public long PeakMemoryUsage { get; init; }

    /// <summary>
    /// Gets the number of errors encountered.
    /// </summary>
    public int ErrorCount { get; init; }

    /// <summary>
    /// Gets performance metrics for individual plugins.
    /// </summary>
    public IReadOnlyDictionary<string, PluginExecutionMetrics> PluginMetrics { get; init; } = new Dictionary<string, PluginExecutionMetrics>();

    /// <summary>
    /// Gets metrics for channel performance.
    /// </summary>
    public IReadOnlyDictionary<string, ChannelExecutionMetrics> ChannelMetrics { get; init; } = new Dictionary<string, ChannelExecutionMetrics>();
}
