using FlowEngine.Abstractions.Data;
using FlowEngine.Abstractions.Plugins;
using Microsoft.Extensions.Logging;
using System.Collections.Immutable;

namespace DelimitedSource;

// Note: Using types from FlowEngine.Abstractions.Plugins namespace

/// <summary>
/// Internal metrics tracking for the service.
/// </summary>
internal sealed record InternalServiceMetrics
{
    public long ProcessedRows { get; init; }
    public long ErrorCount { get; init; }
    public double ProcessingRate { get; init; }
    public TimeSpan AverageProcessingTime { get; init; }
    public long MemoryUsage { get; init; }
    public DateTimeOffset LastUpdated { get; init; }
}

/// <summary>
/// Core business logic service for DelimitedSource plugin with simplified implementation.
/// Implements high-performance file parsing for standalone plugin architecture.
/// </summary>
public sealed class DelimitedSourceService : IPluginService, IDisposable
{
    private readonly ILogger<DelimitedSourceService> _logger;
    private DelimitedSourceConfiguration _configuration;
    private readonly object _configurationLock = new();
    private InternalServiceMetrics _metrics = new();
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the DelimitedSourceService class.
    /// </summary>
    /// <param name="configuration">Plugin configuration</param>
    /// <param name="logger">Logger instance</param>
    public DelimitedSourceService(
        DelimitedSourceConfiguration configuration,
        ILogger<DelimitedSourceService> logger)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Initializes the service asynchronously.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Task representing the initialization operation</returns>
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Initializing DelimitedSourceService for file: {FilePath}", _configuration.FilePath);

        try
        {
            // Validate file exists and is accessible
            if (!File.Exists(_configuration.FilePath))
            {
                throw new FileNotFoundException($"Source file not found: {_configuration.FilePath}");
            }

            // Test file accessibility
            using var testStream = new FileStream(_configuration.FilePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            
            _logger.LogDebug("DelimitedSourceService initialized successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize DelimitedSourceService");
            throw;
        }

        await Task.CompletedTask;
    }

    /// <summary>
    /// Starts the service asynchronously.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Task representing the start operation</returns>
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Starting DelimitedSourceService");
        
        // Service is ready to process data
        _metrics = new InternalServiceMetrics
        {
            ProcessedRows = 0,
            ErrorCount = 0,
            ProcessingRate = 0,
            AverageProcessingTime = TimeSpan.Zero,
            MemoryUsage = GC.GetTotalMemory(false),
            LastUpdated = DateTimeOffset.UtcNow
        };

        await Task.CompletedTask;
    }

    /// <summary>
    /// Stops the service asynchronously.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Task representing the stop operation</returns>
    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Stopping DelimitedSourceService");
        await Task.CompletedTask;
    }

    /// <summary>
    /// Processes the entire file asynchronously, yielding chunks of data.
    /// This is a placeholder implementation for the standalone plugin.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Async enumerable of data chunks</returns>
    public async IAsyncEnumerable<IChunk> ProcessFileAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(DelimitedSourceService));
        }

        _logger.LogDebug("Starting file processing for: {FilePath}", _configuration.FilePath);

        var totalRows = 0L;
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            using var reader = new StreamReader(_configuration.FilePath);

            // Skip header if configured
            if (_configuration.HasHeader)
            {
                var headerLine = await reader.ReadLineAsync();
                _logger.LogTrace("Skipped header line: {Header}", headerLine?.Substring(0, Math.Min(100, headerLine.Length ?? 0)));
            }

            var batch = new List<string>();
            string? line;

            while ((line = await reader.ReadLineAsync()) != null)
            {
                cancellationToken.ThrowIfCancellationRequested();

                // Skip empty lines if configured
                if (_configuration.SkipEmptyLines && string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                batch.Add(line);

                // Process batch when it reaches the configured size
                if (batch.Count >= _configuration.BatchSize)
                {
                    var chunk = CreateSimpleChunk(batch, totalRows);
                    totalRows += batch.Count;
                    
                    UpdateMetrics(totalRows, stopwatch.Elapsed);
                    
                    yield return chunk;
                    
                    batch.Clear();
                }
            }

            // Process remaining lines in final batch
            if (batch.Count > 0)
            {
                var chunk = CreateSimpleChunk(batch, totalRows);
                totalRows += batch.Count;
                
                UpdateMetrics(totalRows, stopwatch.Elapsed);
                
                yield return chunk;
            }

            stopwatch.Stop();

            _logger.LogInformation(
                "File processing completed: {TotalRows} rows in {ElapsedMs}ms ({Rate:F0} rows/sec)",
                totalRows, stopwatch.ElapsedMilliseconds, totalRows / stopwatch.Elapsed.TotalSeconds);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during file processing");
            throw;
        }
    }

    /// <summary>
    /// Processes a single row asynchronously.
    /// For source plugins, this method is not typically used.
    /// </summary>
    /// <param name="row">Input row data</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Processed row</returns>
    public async Task<IArrayRow> ProcessRowAsync(IArrayRow row, CancellationToken cancellationToken = default)
    {
        _logger.LogWarning("ProcessRowAsync called on source plugin. Source plugins generate rows.");
        return await Task.FromResult(row);
    }

    /// <summary>
    /// Checks the health of the service.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Service health status</returns>
    public async Task<ServiceHealth> CheckHealthAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            // Check file accessibility
            if (!File.Exists(_configuration.FilePath))
            {
                return new ServiceHealth
                {
                    Status = HealthStatus.Unhealthy,
                    Message = $"Source file not found: {_configuration.FilePath}",
                    CheckedAt = DateTimeOffset.UtcNow
                };
            }

            // Test file read access
            using var testStream = new FileStream(_configuration.FilePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            
            return new ServiceHealth
            {
                Status = HealthStatus.Healthy,
                Message = "Service is healthy and file is accessible",
                CheckedAt = DateTimeOffset.UtcNow,
                Details = new Dictionary<string, object>
                {
                    ["FilePath"] = _configuration.FilePath,
                    ["FileSize"] = new FileInfo(_configuration.FilePath).Length,
                    ["BatchSize"] = _configuration.BatchSize,
                    ["ProcessedRows"] = _metrics.ProcessedRows
                }.ToImmutableDictionary()
            };
        }
        catch (Exception ex)
        {
            return new ServiceHealth
            {
                Status = HealthStatus.Unhealthy,
                Message = $"Health check failed: {ex.Message}",
                CheckedAt = DateTimeOffset.UtcNow
            };
        }
    }

    /// <summary>
    /// Gets current service metrics.
    /// </summary>
    /// <returns>Service performance metrics</returns>
    public ServiceMetrics GetMetrics()
    {
        return new ServiceMetrics
        {
            ProcessedRows = _metrics.ProcessedRows,
            ErrorCount = _metrics.ErrorCount,
            ProcessingRate = _metrics.ProcessingRate,
            AverageProcessingTime = _metrics.AverageProcessingTime,
            MemoryUsage = _metrics.MemoryUsage,
            LastUpdated = _metrics.LastUpdated
        };
    }

    /// <summary>
    /// Hot-swaps the service configuration.
    /// </summary>
    /// <param name="newConfiguration">New configuration</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Task representing the hot-swap operation</returns>
    public async Task HotSwapConfigurationAsync(DelimitedSourceConfiguration newConfiguration, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Performing hot-swap configuration update");

        lock (_configurationLock)
        {
            _configuration = newConfiguration ?? throw new ArgumentNullException(nameof(newConfiguration));
        }

        await InitializeAsync(cancellationToken);

        _logger.LogInformation("Hot-swap configuration update completed");
    }

    /// <summary>
    /// Creates a simple chunk implementation for demonstration purposes.
    /// </summary>
    /// <param name="lines">Lines to include in chunk</param>
    /// <param name="baseRowIndex">Base row index for chunk</param>
    /// <returns>Simple chunk implementation</returns>
    private IChunk CreateSimpleChunk(List<string> lines, long baseRowIndex)
    {
        // This is a placeholder implementation
        // In a real implementation, this would create proper IChunk objects
        // with parsed IArrayRow instances following the schema
        
        return new SimpleChunk(lines, baseRowIndex);
    }

    /// <summary>
    /// Updates service metrics with current processing statistics.
    /// </summary>
    /// <param name="totalRows">Total rows processed</param>
    /// <param name="elapsedTime">Time elapsed since processing started</param>
    private void UpdateMetrics(long totalRows, TimeSpan elapsedTime)
    {
        _metrics = _metrics with
        {
            ProcessedRows = totalRows,
            ProcessingRate = elapsedTime.TotalSeconds > 0 ? totalRows / elapsedTime.TotalSeconds : 0,
            AverageProcessingTime = elapsedTime,
            MemoryUsage = GC.GetTotalMemory(false),
            LastUpdated = DateTimeOffset.UtcNow
        };
    }

    /// <summary>
    /// Disposes the service and releases resources.
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
    }

    /// <summary>
    /// Simple chunk implementation for demonstration.
    /// This would be replaced with a proper implementation in production.
    /// </summary>
    private sealed class SimpleChunk : IChunk
    {
        private readonly List<string> _lines;
        private readonly long _baseRowIndex;
        private bool _disposed;

        public SimpleChunk(List<string> lines, long baseRowIndex)
        {
            _lines = lines ?? throw new ArgumentNullException(nameof(lines));
            _baseRowIndex = baseRowIndex;
        }

        public ISchema Schema => throw new NotImplementedException("Schema not implemented in simple chunk");
        public int RowCount => _lines.Count;  // Fixed: changed from long to int
        public ReadOnlySpan<IArrayRow> Rows => throw new NotImplementedException("Rows not implemented in simple chunk");
        public IReadOnlyDictionary<string, object>? Metadata => null;
        public long ApproximateMemorySize => _lines.Sum(l => l.Length * sizeof(char));
        public bool IsDisposed => _disposed;

        // Indexer for direct row access
        public IArrayRow this[int index] => throw new NotImplementedException("Direct row access not implemented in simple chunk");

        // WithRows method for creating modified chunks
        public IChunk WithRows(IEnumerable<IArrayRow> newRows)
        {
            throw new NotImplementedException("WithRows not implemented in simple chunk");
        }

        // WithMetadata method for creating chunks with different metadata
        public IChunk WithMetadata(IReadOnlyDictionary<string, object>? metadata)
        {
            throw new NotImplementedException("WithMetadata not implemented in simple chunk");
        }

        // GetRows method for retrieving rows synchronously
        public IEnumerable<IArrayRow> GetRows()
        {
            throw new NotImplementedException("GetRows not implemented in simple chunk");
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
}