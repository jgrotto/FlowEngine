namespace FlowEngine.Abstractions.Execution;

/// <summary>
/// Performance metrics for individual plugin execution.
/// </summary>
public sealed record PluginExecutionMetrics
{
    /// <summary>
    /// Gets the plugin name.
    /// </summary>
    public required string PluginName { get; init; }

    /// <summary>
    /// Gets the total execution time for this plugin.
    /// </summary>
    public TimeSpan ExecutionTime { get; init; }

    /// <summary>
    /// Gets the number of chunks processed by this plugin.
    /// </summary>
    public long ChunksProcessed { get; init; }

    /// <summary>
    /// Gets the number of rows processed by this plugin.
    /// </summary>
    public long RowsProcessed { get; init; }

    /// <summary>
    /// Gets the memory usage for this plugin.
    /// </summary>
    public long MemoryUsage { get; init; }

    /// <summary>
    /// Gets the number of errors in this plugin.
    /// </summary>
    public int ErrorCount { get; init; }

    /// <summary>
    /// Gets the average processing time per chunk.
    /// </summary>
    public TimeSpan AverageChunkTime => ChunksProcessed > 0 ? TimeSpan.FromTicks(ExecutionTime.Ticks / ChunksProcessed) : TimeSpan.Zero;
}