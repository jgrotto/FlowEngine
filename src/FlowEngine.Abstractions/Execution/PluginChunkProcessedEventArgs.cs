using FlowEngine.Abstractions.Data;

namespace FlowEngine.Abstractions.Execution;

/// <summary>
/// Event arguments for plugin chunk processing.
/// </summary>
public sealed class PluginChunkProcessedEventArgs : EventArgs
{
    /// <summary>
    /// Initializes plugin chunk processed event arguments.
    /// </summary>
    /// <param name="pluginName">Name of the plugin</param>
    /// <param name="chunk">Processed chunk</param>
    /// <param name="processingTime">Time taken to process the chunk</param>
    public PluginChunkProcessedEventArgs(string pluginName, IChunk chunk, TimeSpan processingTime)
    {
        PluginName = pluginName ?? throw new ArgumentNullException(nameof(pluginName));
        Chunk = chunk ?? throw new ArgumentNullException(nameof(chunk));
        ProcessingTime = processingTime;
        Timestamp = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Gets the name of the plugin that processed the chunk.
    /// </summary>
    public string PluginName { get; }

    /// <summary>
    /// Gets the processed chunk.
    /// </summary>
    public IChunk Chunk { get; }

    /// <summary>
    /// Gets the time taken to process the chunk.
    /// </summary>
    public TimeSpan ProcessingTime { get; }

    /// <summary>
    /// Gets the timestamp when processing completed.
    /// </summary>
    public DateTimeOffset Timestamp { get; }
}