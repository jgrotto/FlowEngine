using FlowEngine.Abstractions.Data;
using FlowEngine.Abstractions.Plugins;
using Microsoft.Extensions.Logging;
using System.Collections.Immutable;
using System.Diagnostics;

namespace DelimitedSink;

/// <summary>
/// Processor for DelimitedSink plugin implementing high-performance data writing with ArrayRow optimization.
/// Orchestrates between the framework and the DelimitedSinkService for efficient file writing operations.
/// </summary>
public sealed class DelimitedSinkProcessor : IPluginProcessor
{
    private readonly DelimitedSinkService _service;
    private readonly ILogger<DelimitedSinkProcessor> _logger;
    private readonly object _metricsLock = new();

    private DelimitedSinkConfiguration? _configuration;
    private bool _isInitialized;
    private bool _disposed;

    // Performance metrics
    private long _requestCount;
    private long _errorCount;
    private long _totalProcessingTime;
    private DateTime? _lastRequestTime;

    /// <summary>
    /// Initializes a new instance of the DelimitedSinkProcessor class.
    /// </summary>
    /// <param name="service">Delimited sink service</param>
    /// <param name="logger">Logger instance</param>
    public DelimitedSinkProcessor(
        DelimitedSinkService service,
        ILogger<DelimitedSinkProcessor> logger)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Initializes the processor with the specified configuration.
    /// </summary>
    /// <param name="configuration">Sink configuration</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Completion task</returns>
    public async Task InitializeAsync(DelimitedSinkConfiguration configuration, CancellationToken cancellationToken = default)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(DelimitedSinkProcessor));

        _logger.LogDebug("Initializing DelimitedSinkProcessor");

        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));

        // Initialize underlying service
        await _service.InitializeAsync(configuration, cancellationToken);

        _isInitialized = true;
        _logger.LogDebug("DelimitedSinkProcessor initialized successfully");
    }

    /// <summary>
    /// Processes a dataset by writing it to the configured delimited file.
    /// </summary>
    /// <param name="input">Input dataset to write</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Processing result with statistics</returns>
    public async Task<ProcessResult> ProcessAsync(IDataset input, CancellationToken cancellationToken = default)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(DelimitedSinkProcessor));

        if (!_isInitialized)
            throw new InvalidOperationException("Processor must be initialized before processing");

        if (_configuration == null)
            throw new InvalidOperationException("Configuration is required for processing");

        var stopwatch = Stopwatch.StartNew();
        
        lock (_metricsLock)
        {
            _requestCount++;
            _lastRequestTime = DateTime.UtcNow;
        }

        _logger.LogDebug("Starting DelimitedSink processing for dataset with {ChunkCount} chunks", input.ChunkCount);

        try
        {
            long totalRowsProcessed = 0;
            long totalRowsFailed = 0;
            var processingErrors = new List<string>();

            // Process each chunk in the dataset
            await foreach (var chunk in input.GetChunksAsync(cancellationToken))
            {
                try
                {
                    var result = await _service.WriteChunkAsync(chunk, cancellationToken);
                    
                    totalRowsProcessed += result.RowsProcessed;
                    totalRowsFailed += result.RowsFailed;

                    if (result.Errors.Any())
                    {
                        processingErrors.AddRange(result.Errors);
                    }

                    // Check error threshold
                    if (_configuration.MaxErrorThreshold > 0 && 
                        totalRowsFailed >= _configuration.MaxErrorThreshold)
                    {
                        if (!_configuration.ContinueOnError)
                        {
                            _logger.LogError("Error threshold exceeded: {ErrorCount} >= {Threshold}", 
                                totalRowsFailed, _configuration.MaxErrorThreshold);
                            
                            lock (_metricsLock)
                            {
                                _errorCount++;
                            }

                            return ProcessResult.Failure(
                                $"Error threshold exceeded: {totalRowsFailed} errors",
                                totalRowsProcessed,
                                stopwatch.Elapsed,
                                processingErrors);
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to process chunk");
                    
                    if (!_configuration.ContinueOnError)
                    {
                        lock (_metricsLock)
                        {
                            _errorCount++;
                        }

                        return ProcessResult.Failure(
                            $"Chunk processing failed: {ex.Message}",
                            totalRowsProcessed,
                            stopwatch.Elapsed,
                            new[] { ex.Message });
                    }
                    
                    processingErrors.Add($"Chunk processing error: {ex.Message}");
                    totalRowsFailed += chunk.RowCount;
                }
            }

            // Finalize writing (flush buffers, close files, etc.)
            await _service.FinalizeAsync(cancellationToken);

            stopwatch.Stop();

            // Update performance metrics
            lock (_metricsLock)
            {
                _totalProcessingTime += stopwatch.ElapsedMilliseconds;
            }

            var throughput = totalRowsProcessed > 0 ? totalRowsProcessed / stopwatch.Elapsed.TotalSeconds : 0;

            _logger.LogInformation("DelimitedSink processing completed: {RowsProcessed} rows in {ElapsedMs}ms ({Throughput:F0} rows/sec)",
                totalRowsProcessed, stopwatch.ElapsedMilliseconds, throughput);

            var metadata = ImmutableDictionary<string, object>.Empty
                .Add("TotalRowsProcessed", totalRowsProcessed)
                .Add("TotalRowsFailed", totalRowsFailed)
                .Add("ThroughputRowsPerSecond", throughput)
                .Add("ProcessingTimeMs", stopwatch.ElapsedMilliseconds)
                .Add("FilePath", _configuration.FilePath)
                .Add("ChunksProcessed", input.ChunkCount);

            return ProcessResult.Success(
                totalRowsProcessed,
                stopwatch.Elapsed,
                processingErrors.Any() ? processingErrors : null,
                metadata);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            
            lock (_metricsLock)
            {
                _errorCount++;
            }

            _logger.LogError(ex, "DelimitedSink processing failed after {ElapsedMs}ms", stopwatch.ElapsedMilliseconds);

            return ProcessResult.Failure(
                $"Processing failed: {ex.Message}",
                0,
                stopwatch.Elapsed,
                new[] { ex.Message });
        }
    }

    /// <summary>
    /// Checks the health of the processor.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Health status</returns>
    public async Task<ServiceHealth> CheckHealthAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed)
        {
            return ServiceHealth.CreateUnhealthy(
                "PROCESSOR_DISPOSED",
                "Processor has been disposed",
                ImmutableDictionary<string, object>.Empty);
        }

        if (!_isInitialized)
        {
            return ServiceHealth.CreateUnhealthy(
                "PROCESSOR_NOT_INITIALIZED",
                "Processor has not been initialized",
                ImmutableDictionary<string, object>.Empty);
        }

        try
        {
            // Check underlying service health
            var serviceHealth = await _service.CheckHealthAsync(cancellationToken);
            if (serviceHealth.Status != HealthStatus.Healthy)
            {
                return ServiceHealth.CreateDegraded(
                    "SERVICE_UNHEALTHY",
                    $"Underlying service is unhealthy: {serviceHealth.Description}",
                    ImmutableDictionary<string, object>.Empty
                        .Add("ServiceHealth", serviceHealth));
            }

            lock (_metricsLock)
            {
                var errorRate = _requestCount > 0 ? (double)_errorCount / _requestCount : 0;
                
                if (errorRate > 0.1) // More than 10% error rate
                {
                    return ServiceHealth.CreateDegraded(
                        "HIGH_ERROR_RATE",
                        $"High error rate detected: {errorRate:P1}",
                        ImmutableDictionary<string, object>.Empty
                            .Add("ErrorRate", errorRate)
                            .Add("RequestCount", _requestCount)
                            .Add("ErrorCount", _errorCount));
                }
            }

            return ServiceHealth.CreateHealthy(
                "Processor is healthy and ready to process data",
                ImmutableDictionary<string, object>.Empty
                    .Add("IsInitialized", _isInitialized)
                    .Add("RequestCount", _requestCount)
                    .Add("ErrorCount", _errorCount)
                    .Add("LastRequestTime", _lastRequestTime?.ToString("O") ?? "Never"));
        }
        catch (Exception ex)
        {
            return ServiceHealth.CreateUnhealthy(
                "HEALTH_CHECK_EXCEPTION",
                $"Health check failed: {ex.Message}",
                ImmutableDictionary<string, object>.Empty);
        }
    }

    /// <summary>
    /// Gets current processor metrics.
    /// </summary>
    /// <returns>Service metrics</returns>
    public ServiceMetrics GetMetrics()
    {
        if (_disposed)
            return ServiceMetrics.Empty;

        lock (_metricsLock)
        {
            var averageResponseTime = _requestCount > 0 
                ? TimeSpan.FromMilliseconds(_totalProcessingTime / _requestCount)
                : TimeSpan.Zero;

            return new ServiceMetrics
            {
                RequestCount = _requestCount,
                ErrorCount = _errorCount,
                AverageResponseTime = averageResponseTime,
                LastRequestTime = _lastRequestTime,
                Metadata = ImmutableDictionary<string, object>.Empty
                    .Add("IsInitialized", _isInitialized)
                    .Add("TotalProcessingTimeMs", _totalProcessingTime)
                    .Add("ErrorRate", _requestCount > 0 ? (double)_errorCount / _requestCount : 0)
            };
        }
    }

    /// <summary>
    /// Performs hot-swapping of configuration.
    /// </summary>
    /// <param name="newConfiguration">New configuration</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Hot-swap result</returns>
    public async Task<HotSwapResult> HotSwapAsync(DelimitedSinkConfiguration newConfiguration, CancellationToken cancellationToken = default)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(DelimitedSinkProcessor));

        _logger.LogDebug("Performing hot-swap for DelimitedSinkProcessor");

        try
        {
            // Hot-swap the underlying service first
            var serviceResult = await _service.HotSwapAsync(newConfiguration, cancellationToken);
            if (!serviceResult.IsSuccessful)
            {
                return serviceResult;
            }

            _configuration = newConfiguration;

            _logger.LogDebug("DelimitedSinkProcessor hot-swap completed successfully");

            return HotSwapResult.Success(
                TimeSpan.FromMilliseconds(1),
                ImmutableArray<ValidationWarning>.Empty,
                ImmutableDictionary<string, object>.Empty
                    .Add("ProcessorHotSwapped", true)
                    .Add("NewConfiguration", newConfiguration.PluginId));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Processor hot-swap failed");
            return HotSwapResult.Failure(
                "PROCESSOR_HOT_SWAP_FAILED",
                $"Processor hot-swap failed: {ex.Message}",
                TimeSpan.Zero);
        }
    }

    /// <summary>
    /// Validates that the processor can handle the specified input schema.
    /// </summary>
    /// <param name="inputSchema">Input schema to validate</param>
    /// <returns>True if the schema is compatible</returns>
    public bool CanProcessSchema(ISchema inputSchema)
    {
        if (_configuration == null)
            return false;

        return _configuration.IsCompatibleWith(inputSchema);
    }

    /// <summary>
    /// Gets the expected output schema for this processor.
    /// Sink processors typically don't produce output data.
    /// </summary>
    /// <returns>Output schema or null for sink processors</returns>
    public ISchema? GetOutputSchema()
    {
        return _configuration?.OutputSchema; // Typically null for sink processors
    }

    /// <summary>
    /// Estimates the processing time for a dataset.
    /// </summary>
    /// <param name="inputDataset">Input dataset</param>
    /// <returns>Estimated processing time</returns>
    public TimeSpan EstimateProcessingTime(IDataset inputDataset)
    {
        if (_configuration == null)
            return TimeSpan.Zero;

        lock (_metricsLock)
        {
            if (_requestCount == 0)
            {
                // No historical data, estimate based on configuration
                var estimatedRowsPerSecond = _configuration.BatchSize * _configuration.MaxDegreeOfParallelism * 10; // Conservative estimate
                var totalRows = inputDataset.ChunkCount * _configuration.BatchSize; // Rough estimate
                return TimeSpan.FromSeconds(totalRows / (double)estimatedRowsPerSecond);
            }

            // Use historical average
            var averageRowsPerSecond = _requestCount * 1000.0 / _totalProcessingTime; // rows per second
            var estimatedTotalRows = inputDataset.ChunkCount * _configuration.BatchSize;
            return TimeSpan.FromSeconds(estimatedTotalRows / averageRowsPerSecond);
        }
    }

    /// <summary>
    /// Disposes the processor asynchronously.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;

        _logger.LogDebug("Disposing DelimitedSinkProcessor");

        try
        {
            if (_service != null)
            {
                await _service.DisposeAsync();
            }

            _disposed = true;
            _logger.LogDebug("DelimitedSinkProcessor disposed successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during processor disposal");
            throw;
        }
    }

    /// <summary>
    /// Disposes the processor synchronously.
    /// </summary>
    public void Dispose()
    {
        DisposeAsync().AsTask().GetAwaiter().GetResult();
    }
}