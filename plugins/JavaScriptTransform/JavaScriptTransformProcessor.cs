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

    // State and performance tracking
    private ProcessorState _state = ProcessorState.Created;
    private long _totalProcessedRows;
    private long _totalProcessingTimeMs;
    private long _totalChunksProcessed;
    private long _errorCount;
    private readonly object _metricsLock = new();
    private bool _disposed;

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
    /// Gets the current processor state.
    /// </summary>
    public ProcessorState State => _state;

    /// <summary>
    /// Gets whether this processor is compatible with the specified service.
    /// </summary>
    public bool IsServiceCompatible(IPluginService service)
    {
        return service is JavaScriptTransformService;
    }

    /// <summary>
    /// Initializes the processor with channel setup and validation.
    /// </summary>
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Initializing JavaScriptTransformProcessor");
        _state = ProcessorState.Initializing;
        
        // Initialize the service
        await _service.InitializeAsync(cancellationToken);
        
        _state = ProcessorState.Initialized;
        _logger.LogDebug("JavaScriptTransformProcessor initialized successfully");
    }

    /// <summary>
    /// Starts the processor and begins data processing operations.
    /// </summary>
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_state != ProcessorState.Initialized)
            throw new InvalidOperationException($"Processor must be initialized before starting. Current state: {_state}");

        _logger.LogDebug("Starting JavaScriptTransformProcessor");
        _state = ProcessorState.Starting;
        
        await _service.StartAsync(cancellationToken);
        
        _state = ProcessorState.Running;
        _logger.LogDebug("JavaScriptTransformProcessor started successfully");
    }

    /// <summary>
    /// Stops the processor and suspends processing operations.
    /// </summary>
    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (_state != ProcessorState.Running)
            return;

        _logger.LogDebug("Stopping JavaScriptTransformProcessor");
        _state = ProcessorState.Stopping;
        
        await _service.StopAsync(cancellationToken);
        
        _state = ProcessorState.Stopped;
        _logger.LogDebug("JavaScriptTransformProcessor stopped successfully");
    }

    /// <summary>
    /// Updates the processor configuration with hot-swapping support.
    /// </summary>
    public async Task UpdateConfigurationAsync(IPluginConfiguration newConfiguration, CancellationToken cancellationToken = default)
    {
        if (newConfiguration is not JavaScriptTransformConfiguration jsConfig)
            throw new ArgumentException("Invalid configuration type for JavaScriptTransformProcessor");

        _logger.LogDebug("Updating JavaScriptTransformProcessor configuration");
        
        // Delegate to service for actual configuration update
        await _service.UpdateConfigurationAsync(jsConfig, cancellationToken);
        
        _logger.LogDebug("JavaScriptTransformProcessor configuration updated successfully");
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
                _totalChunksProcessed++;
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
    /// Gets current processor performance metrics and statistics.
    /// </summary>
    /// <returns>Current processor performance metrics</returns>
    public ProcessorMetrics GetMetrics()
    {
        lock (_metricsLock)
        {
            var averageTimePerRow = _totalProcessedRows > 0 
                ? (double)_totalProcessingTimeMs / _totalProcessedRows 
                : 0;

            var throughput = _totalProcessingTimeMs > 0 
                ? _totalProcessedRows / (_totalProcessingTimeMs / 1000.0) 
                : 0;

            var averageChunkSize = _totalChunksProcessed > 0
                ? (double)_totalProcessedRows / _totalChunksProcessed
                : 0;

            return new ProcessorMetrics
            {
                TotalChunksProcessed = _totalChunksProcessed,
                TotalRowsProcessed = _totalProcessedRows,
                ErrorCount = _errorCount,
                AverageProcessingTimeMs = averageTimePerRow,
                MemoryUsageBytes = GC.GetTotalMemory(false),
                ThroughputPerSecond = throughput,
                LastProcessedAt = DateTimeOffset.UtcNow,
                BackpressureLevel = 0, // Not implemented in this example
                AverageChunkSize = averageChunkSize
            };
        }
    }

    /// <summary>
    /// Performs a health check on the processor and its data flow.
    /// </summary>
    public async Task<ProcessorHealth> CheckHealthAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var serviceHealth = await _service.CheckHealthAsync(cancellationToken);
            var isHealthy = _state == ProcessorState.Running && serviceHealth.Status == HealthStatus.Healthy;

            return new ProcessorHealth
            {
                IsHealthy = isHealthy,
                State = _state,
                FlowStatus = isHealthy ? DataFlowStatus.Normal : DataFlowStatus.Failed,
                Checks = new List<HealthCheck>
                {
                    new HealthCheck("ProcessorState", _state == ProcessorState.Running, $"State: {_state}"),
                    new HealthCheck("ServiceHealth", serviceHealth.Status == HealthStatus.Healthy, serviceHealth.Message)
                },
                LastChecked = DateTimeOffset.UtcNow,
                PerformanceScore = isHealthy ? 100 : 0
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Health check failed for JavaScriptTransformProcessor");
            return new ProcessorHealth
            {
                IsHealthy = false,
                State = _state,
                FlowStatus = DataFlowStatus.Failed,
                Checks = new List<HealthCheck>
                {
                    new HealthCheck("HealthCheck", false, $"Health check failed: {ex.Message}")
                },
                LastChecked = DateTimeOffset.UtcNow,
                PerformanceScore = 0
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
            _totalChunksProcessed = 0;
            _errorCount = 0;
        }

        _logger.LogDebug("JavaScript transform processor metrics reset");
    }

    /// <summary>
    /// Disposes the processor and releases resources asynchronously.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;

        try
        {
            if (_state == ProcessorState.Running)
                await StopAsync();

            _service?.Dispose();
            _disposed = true;
            _state = ProcessorState.Disposed;
            
            _logger.LogInformation("JavaScript transform processor disposed");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during JavaScriptTransformProcessor disposal");
        }
    }

    /// <summary>
    /// Disposes the processor and releases resources.
    /// </summary>
    public void Dispose()
    {
        DisposeAsync().AsTask().GetAwaiter().GetResult();
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
    private readonly IArrayRow[] _rowArray;
    private bool _disposed;

    public ISchema Schema { get; }
    public int RowCount { get; }  // Fixed: changed from long to int
    public ReadOnlySpan<IArrayRow> Rows => _rowArray.AsSpan();  // Fixed: changed return type
    public IReadOnlyDictionary<string, object>? Metadata { get; private set; }
    public long ApproximateMemorySize { get; }
    public bool IsDisposed => _disposed;

    // Indexer for direct row access
    public IArrayRow this[int index] => _rowArray[index];

    public MemoryChunk(ISchema schema, IEnumerable<IArrayRow> rows, IReadOnlyDictionary<string, object>? metadata = null)
    {
        Schema = schema ?? throw new ArgumentNullException(nameof(schema));
        _rowArray = rows?.ToArray() ?? throw new ArgumentNullException(nameof(rows));
        RowCount = _rowArray.Length;
        Metadata = metadata;
        
        // Approximate memory calculation
        ApproximateMemorySize = _rowArray.Length * 1024; // Rough estimate
    }

    // WithRows method for creating modified chunks
    public IChunk WithRows(IEnumerable<IArrayRow> newRows)
    {
        return new MemoryChunk(Schema, newRows, Metadata);
    }

    // WithMetadata method for creating chunks with different metadata
    public IChunk WithMetadata(IReadOnlyDictionary<string, object>? metadata)
    {
        return new MemoryChunk(Schema, _rowArray, metadata);
    }

    // GetRows method for retrieving rows synchronously
    public IEnumerable<IArrayRow> GetRows()
    {
        return _rowArray;
    }

    public async ValueTask DisposeAsync()
    {
        if (!_disposed)
        {
            _disposed = true;
        }
        await Task.CompletedTask;
    }

    public void Dispose()
    {
        DisposeAsync().AsTask().GetAwaiter().GetResult();
    }
}

/// <summary>
/// Memory-based implementation of IDataset for processed data.
/// </summary>
internal sealed class MemoryDataset : IDataset
{
    private readonly IList<IChunk> _chunks;
    private bool _disposed;

    public ISchema Schema { get; }
    public long? RowCount { get; }
    public int? ChunkCount => _chunks.Count;
    public bool IsDisposed => _disposed;
    public IReadOnlyDictionary<string, object>? Metadata { get; private set; }

    public MemoryDataset(ISchema schema, IEnumerable<IChunk> chunks, IReadOnlyDictionary<string, object>? metadata = null)
    {
        Schema = schema ?? throw new ArgumentNullException(nameof(schema));
        _chunks = chunks?.ToList() ?? throw new ArgumentNullException(nameof(chunks));
        RowCount = _chunks.Sum(c => c.RowCount);
        Metadata = metadata;
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

    // TransformSchemaAsync method for schema transformations
    public async Task<IDataset> TransformSchemaAsync(ISchema newSchema, Func<IArrayRow, IArrayRow> transform, CancellationToken cancellationToken = default)
    {
        var transformedChunks = new List<IChunk>();
        
        await foreach (var chunk in GetChunksAsync(null, cancellationToken))
        {
            var transformedRows = chunk.GetRows().Select(transform);
            var transformedChunk = new MemoryChunk(newSchema, transformedRows, chunk.Metadata);
            transformedChunks.Add(transformedChunk);
        }
        
        return new MemoryDataset(newSchema, transformedChunks, Metadata);
    }

    // FilterAsync method for row filtering
    public async Task<IDataset> FilterAsync(Func<IArrayRow, bool> predicate, CancellationToken cancellationToken = default)
    {
        var filteredChunks = new List<IChunk>();
        
        await foreach (var chunk in GetChunksAsync(null, cancellationToken))
        {
            var filteredRows = chunk.GetRows().Where(predicate);
            var filteredChunk = new MemoryChunk(Schema, filteredRows, chunk.Metadata);
            filteredChunks.Add(filteredChunk);
        }
        
        return new MemoryDataset(Schema, filteredChunks, Metadata);
    }

    // MaterializeAsync method for materializing data
    public async Task<IList<IArrayRow>> MaterializeAsync(long maxRows = long.MaxValue, CancellationToken cancellationToken = default)
    {
        var materializedRows = new List<IArrayRow>();
        long totalRows = 0;
        
        await foreach (var chunk in GetChunksAsync(null, cancellationToken))
        {
            foreach (var row in chunk.GetRows())
            {
                if (totalRows >= maxRows)
                    break;
                    
                materializedRows.Add(row);
                totalRows++;
            }
            
            if (totalRows >= maxRows)
                break;
        }
        
        return materializedRows;
    }

    // WithMetadata method for creating dataset with different metadata
    public IDataset WithMetadata(IReadOnlyDictionary<string, object>? metadata)
    {
        return new MemoryDataset(Schema, _chunks, metadata);
    }

    public async ValueTask DisposeAsync()
    {
        if (!_disposed)
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
            _disposed = true;
        }
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            foreach (var chunk in _chunks)
            {
                if (chunk is IDisposable disposable)
                {
                    disposable.Dispose();
                }
            }
            _disposed = true;
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