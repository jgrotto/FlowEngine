using FlowEngine.Abstractions.Data;
using FlowEngine.Abstractions.Plugins;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace JavaScriptTransform;

/// <summary>
/// Processor component for JavaScriptTransform plugin implementing data flow orchestration.
/// Manages chunked processing with performance optimization and error handling.
/// </summary>
public sealed class JavaScriptTransformProcessor : IPluginProcessor
{
    private readonly JavaScriptTransformService _service;
    private readonly JavaScriptTransformConfiguration _configuration;
    private readonly ILogger<JavaScriptTransformProcessor> _logger;

    // Performance tracking
    private long _totalProcessedRows;
    private long _totalProcessingTimeMs;
    private readonly object _metricsLock = new();

    /// <summary>
    /// Initializes a new instance of the JavaScriptTransformProcessor class.
    /// </summary>
    /// <param name="service">Core JavaScript transform service</param>
    /// <param name="configuration">Plugin configuration</param>
    /// <param name="logger">Logger instance</param>
    public JavaScriptTransformProcessor(
        JavaScriptTransformService service,
        JavaScriptTransformConfiguration configuration,
        ILogger<JavaScriptTransformProcessor> logger)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Processes a single data chunk through JavaScript transformation.
    /// </summary>
    /// <param name="chunk">Input data chunk to process</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Processed data chunk</returns>
    public async Task<IChunk> ProcessChunkAsync(IChunk chunk, CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        
        _logger.LogDebug("Processing chunk with {RowCount} rows through JavaScript transform", chunk.RowCount);

        try
        {
            // Process all rows in the chunk using the service
            var processedRows = new List<IArrayRow>();
            
            await foreach (var processedRow in _service.ProcessRowsAsync(chunk.Rows, cancellationToken))
            {
                processedRows.Add(processedRow);
                cancellationToken.ThrowIfCancellationRequested();
            }

            // Create output chunk with processed rows
            var outputSchema = _configuration.OutputSchema ?? chunk.Schema;
            var outputChunk = new MemoryChunk(outputSchema, processedRows);

            stopwatch.Stop();
            
            // Update performance metrics
            lock (_metricsLock)
            {
                _totalProcessedRows += chunk.RowCount;
                _totalProcessingTimeMs += stopwatch.ElapsedMilliseconds;
            }

            _logger.LogDebug(
                "Completed JavaScript transform processing of {RowCount} rows in {ElapsedMs}ms", 
                chunk.RowCount, 
                stopwatch.ElapsedMilliseconds);

            return outputChunk;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogError(ex, 
                "Failed to process chunk with {RowCount} rows through JavaScript transform after {ElapsedMs}ms", 
                chunk.RowCount, 
                stopwatch.ElapsedMilliseconds);

            if (!_configuration.ContinueOnError)
                throw;

            // Return empty chunk on error when ContinueOnError is enabled
            var emptyChunk = new MemoryChunk(_configuration.OutputSchema ?? chunk.Schema, Array.Empty<IArrayRow>());
            return emptyChunk;
        }
    }

    /// <summary>
    /// Processes an entire dataset through JavaScript transformation with memory-bounded streaming.
    /// </summary>
    /// <param name="input">Input dataset to process</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Processed dataset</returns>
    public async Task<IDataset> ProcessAsync(IDataset input, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Starting JavaScript transform processing of dataset with {RowCount} rows", 
            input.RowCount?.ToString() ?? "unknown");

        var stopwatch = Stopwatch.StartNew();
        var processedChunks = new List<IChunk>();
        var totalProcessedRows = 0L;

        try
        {
            // Configure chunking for optimal performance
            var chunkingOptions = new ChunkingOptions
            {
                ChunkSize = _configuration.BatchSize,
                MaxMemoryUsage = _configuration.MaxMemoryBytes
            };

            // Process each chunk
            await foreach (var chunk in input.GetChunksAsync(chunkingOptions, cancellationToken))
            {
                var processedChunk = await ProcessChunkAsync(chunk, cancellationToken);
                processedChunks.Add(processedChunk);
                totalProcessedRows += processedChunk.RowCount;

                _logger.LogDebug("Processed chunk {ChunkNumber}, total rows: {TotalRows}", 
                    processedChunks.Count, totalProcessedRows);

                cancellationToken.ThrowIfCancellationRequested();
            }

            stopwatch.Stop();

            _logger.LogInformation(
                "Completed JavaScript transform processing of {TotalRows} rows in {ElapsedMs}ms (avg: {AvgPerRow:F2}ms/row)",
                totalProcessedRows,
                stopwatch.ElapsedMilliseconds,
                totalProcessedRows > 0 ? (double)stopwatch.ElapsedMilliseconds / totalProcessedRows : 0);

            // Create output dataset
            var outputSchema = _configuration.OutputSchema ?? input.Schema;
            return new MemoryDataset(outputSchema, processedChunks);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogError(ex, 
                "Failed to process dataset through JavaScript transform after {ElapsedMs}ms. Processed {ProcessedRows} rows.",
                stopwatch.ElapsedMilliseconds, 
                totalProcessedRows);
            throw;
        }
    }

    /// <summary>
    /// Gets current processing metrics.
    /// </summary>
    /// <returns>Processing performance metrics</returns>
    public ProcessingMetrics GetMetrics()
    {
        lock (_metricsLock)
        {
            var averageTimePerRow = _totalProcessedRows > 0 
                ? (double)_totalProcessingTimeMs / _totalProcessedRows 
                : 0;

            return new ProcessingMetrics
            {
                TotalProcessedRows = _totalProcessedRows,
                TotalProcessingTime = TimeSpan.FromMilliseconds(_totalProcessingTimeMs),
                AverageTimePerRow = TimeSpan.FromMilliseconds(averageTimePerRow),
                ProcessingRate = _totalProcessingTimeMs > 0 
                    ? _totalProcessedRows / (_totalProcessingTimeMs / 1000.0) 
                    : 0,
                LastUpdated = DateTimeOffset.UtcNow
            };
        }
    }

    /// <summary>
    /// Resets processing metrics.
    /// </summary>
    public void ResetMetrics()
    {
        lock (_metricsLock)
        {
            _totalProcessedRows = 0;
            _totalProcessingTimeMs = 0;
        }

        _logger.LogDebug("JavaScript transform processor metrics reset");
    }

    /// <summary>
    /// Disposes the processor and releases resources.
    /// </summary>
    public void Dispose()
    {
        _logger.LogInformation("JavaScript transform processor disposed");
    }
}

/// <summary>
/// Processing performance metrics.
/// </summary>
public sealed record ProcessingMetrics
{
    /// <summary>
    /// Gets the total number of rows processed.
    /// </summary>
    public required long TotalProcessedRows { get; init; }

    /// <summary>
    /// Gets the total processing time.
    /// </summary>
    public required TimeSpan TotalProcessingTime { get; init; }

    /// <summary>
    /// Gets the average processing time per row.
    /// </summary>
    public required TimeSpan AverageTimePerRow { get; init; }

    /// <summary>
    /// Gets the processing rate in rows per second.
    /// </summary>
    public required double ProcessingRate { get; init; }

    /// <summary>
    /// Gets when the metrics were last updated.
    /// </summary>
    public required DateTimeOffset LastUpdated { get; init; }
}

/// <summary>
/// Memory-based implementation of IChunk for processed data.
/// </summary>
internal sealed class MemoryChunk : IChunk
{
    public ISchema Schema { get; }
    public IAsyncEnumerable<IArrayRow> Rows { get; }
    public long RowCount { get; }

    public MemoryChunk(ISchema schema, IEnumerable<IArrayRow> rows)
    {
        Schema = schema ?? throw new ArgumentNullException(nameof(schema));
        var rowArray = rows?.ToArray() ?? throw new ArgumentNullException(nameof(rows));
        RowCount = rowArray.Length;
        Rows = rowArray.ToAsyncEnumerable();
    }

    public async ValueTask DisposeAsync()
    {
        await Task.CompletedTask;
    }

    public void Dispose()
    {
        // No resources to dispose for memory-based chunk
    }
}

/// <summary>
/// Memory-based implementation of IDataset for processed data.
/// </summary>
internal sealed class MemoryDataset : IDataset
{
    public ISchema Schema { get; }
    public long? RowCount { get; }

    private readonly IList<IChunk> _chunks;

    public MemoryDataset(ISchema schema, IEnumerable<IChunk> chunks)
    {
        Schema = schema ?? throw new ArgumentNullException(nameof(schema));
        _chunks = chunks?.ToList() ?? throw new ArgumentNullException(nameof(chunks));
        RowCount = _chunks.Sum(c => c.RowCount);
    }

    public async IAsyncEnumerable<IChunk> GetChunksAsync(
        ChunkingOptions? options = null, 
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        foreach (var chunk in _chunks)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return chunk;
            await Task.Yield();
        }
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var chunk in _chunks)
        {
            if (chunk is IAsyncDisposable asyncDisposable)
            {
                await asyncDisposable.DisposeAsync();
            }
            else if (chunk is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }
    }

    public void Dispose()
    {
        foreach (var chunk in _chunks)
        {
            if (chunk is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }
    }
}

/// <summary>
/// Extension methods for async enumerable support.
/// </summary>
internal static class AsyncEnumerableExtensions
{
    public static async IAsyncEnumerable<T> ToAsyncEnumerable<T>(this IEnumerable<T> source)
    {
        foreach (var item in source)
        {
            yield return item;
            await Task.Yield();
        }
    }
}