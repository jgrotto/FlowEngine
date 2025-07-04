using FlowEngine.Abstractions.Data;
using System.Collections.Immutable;

namespace FlowEngine.Abstractions.Plugins;

/// <summary>
/// Orchestrates data processing between the FlowEngine framework and plugin business logic.
/// Manages chunking, error handling, and performance monitoring for plugin execution.
/// </summary>
public interface IPluginProcessor : IDisposable
{
    /// <summary>
    /// Gets the unique identifier for this processor instance.
    /// </summary>
    string Id { get; }

    /// <summary>
    /// Gets the current state of this processor.
    /// </summary>
    PluginState State { get; }

    /// <summary>
    /// Gets the configuration used by this processor.
    /// </summary>
    IPluginConfiguration Configuration { get; }

    /// <summary>
    /// Gets performance metrics for this processor.
    /// </summary>
    ProcessorMetrics Metrics { get; }

    /// <summary>
    /// Initializes the processor with configuration and dependencies.
    /// </summary>
    /// <param name="configuration">Plugin configuration</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Task representing the initialization operation</returns>
    Task InitializeAsync(IPluginConfiguration configuration, CancellationToken cancellationToken = default);

    /// <summary>
    /// Processes a chunk of data using the plugin's business logic.
    /// Implements chunking strategy and error handling.
    /// </summary>
    /// <param name="input">Input data chunk</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Result of the processing operation</returns>
    Task<ProcessResult> ProcessAsync(IChunk input, CancellationToken cancellationToken = default);

    /// <summary>
    /// Processes a dataset by breaking it into chunks and managing the flow.
    /// </summary>
    /// <param name="input">Input dataset</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Processed dataset</returns>
    Task<IDataset> ProcessDatasetAsync(IDataset input, CancellationToken cancellationToken = default);

    /// <summary>
    /// Starts the processor for streaming operations.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Task representing the start operation</returns>
    Task StartAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Stops the processor gracefully.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Task representing the stop operation</returns>
    Task StopAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Performs a health check on the processor.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Collection of health check results</returns>
    Task<IEnumerable<HealthCheck>> HealthCheckAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Event raised when the processor state changes.
    /// </summary>
    event EventHandler<PluginStateChangedEventArgs>? StateChanged;
}

/// <summary>
/// Event arguments for processor state changes.
/// </summary>
public sealed class ProcessorStateChangedEventArgs : EventArgs
{
    /// <summary>
    /// Gets the previous state of the processor.
    /// </summary>
    public required PluginState PreviousState { get; init; }

    /// <summary>
    /// Gets the current state of the processor.
    /// </summary>
    public required PluginState CurrentState { get; init; }

    /// <summary>
    /// Gets the reason for the state change.
    /// </summary>
    public string? Reason { get; init; }

    /// <summary>
    /// Gets the time when the state change occurred.
    /// </summary>
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
}

/// <summary>
/// Performance metrics for plugin processors.
/// </summary>
public sealed record ProcessorMetrics
{
    /// <summary>
    /// Gets the total number of chunks processed.
    /// </summary>
    public long TotalChunks { get; init; }

    /// <summary>
    /// Gets the total number of rows processed.
    /// </summary>
    public long TotalRows { get; init; }

    /// <summary>
    /// Gets the total processing time.
    /// </summary>
    public TimeSpan TotalProcessingTime { get; init; }

    /// <summary>
    /// Gets the current memory usage in bytes.
    /// </summary>
    public long CurrentMemoryUsage { get; init; }

    /// <summary>
    /// Gets the peak memory usage in bytes.
    /// </summary>
    public long PeakMemoryUsage { get; init; }

    /// <summary>
    /// Gets the number of errors encountered.
    /// </summary>
    public long ErrorCount { get; init; }

    /// <summary>
    /// Gets the number of warnings encountered.
    /// </summary>
    public long WarningCount { get; init; }

    /// <summary>
    /// Gets the timestamp when these metrics were captured.
    /// </summary>
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Calculates the average processing time per chunk.
    /// </summary>
    public TimeSpan AverageChunkProcessingTime => 
        TotalChunks > 0 ? TimeSpan.FromTicks(TotalProcessingTime.Ticks / TotalChunks) : TimeSpan.Zero;

    /// <summary>
    /// Calculates the throughput in rows per second.
    /// </summary>
    public double RowsPerSecond => 
        TotalProcessingTime.TotalSeconds > 0 ? TotalRows / TotalProcessingTime.TotalSeconds : 0.0;
}

/// <summary>
/// Result of a data processing operation.
/// </summary>
public sealed record ProcessResult
{
    /// <summary>
    /// Gets whether the processing was successful.
    /// </summary>
    public required bool Success { get; init; }

    /// <summary>
    /// Gets the processed data chunk.
    /// </summary>
    public IChunk? OutputChunk { get; init; }

    /// <summary>
    /// Gets the number of rows processed.
    /// </summary>
    public int ProcessedRows { get; init; }

    /// <summary>
    /// Gets the time taken for processing.
    /// </summary>
    public TimeSpan ProcessingTime { get; init; }

    /// <summary>
    /// Gets any errors that occurred during processing.
    /// </summary>
    public ImmutableArray<string> Errors { get; init; } = ImmutableArray<string>.Empty;

    /// <summary>
    /// Gets any warnings that occurred during processing.
    /// </summary>
    public ImmutableArray<string> Warnings { get; init; } = ImmutableArray<string>.Empty;

    /// <summary>
    /// Gets additional metadata about the processing result.
    /// </summary>
    public ImmutableDictionary<string, object>? Metadata { get; init; }

    /// <summary>
    /// Creates a successful processing result.
    /// </summary>
    /// <param name="outputChunk">The processed data chunk</param>
    /// <param name="processingTime">Time taken for processing</param>
    /// <returns>A successful ProcessResult</returns>
    public static ProcessResult CreateSuccess(IChunk outputChunk, TimeSpan processingTime) =>
        new() { Success = true, OutputChunk = outputChunk, ProcessedRows = outputChunk.RowCount, ProcessingTime = processingTime };

    /// <summary>
    /// Creates a failed processing result.
    /// </summary>
    /// <param name="errors">Error messages</param>
    /// <returns>A failed ProcessResult</returns>
    public static ProcessResult CreateFailure(params string[] errors) =>
        new() { Success = false, Errors = errors.ToImmutableArray() };
}