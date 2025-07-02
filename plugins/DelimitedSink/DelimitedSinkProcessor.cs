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
    private ProcessorState _state = ProcessorState.Created;
    private bool _isInitialized;
    private bool _disposed;

    // Performance metrics
    private long _requestCount;
    private long _errorCount;
    private long _totalProcessingTime;
    private long _totalChunksProcessed;
    private long _totalRowsProcessed;
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
    /// Gets the current processor state.
    /// </summary>
    public ProcessorState State => _state;

    /// <summary>
    /// Gets whether this processor is compatible with the specified service.
    /// </summary>
    public bool IsServiceCompatible(IPluginService service)
    {
        return service is DelimitedSinkService;
    }

    /// <summary>
    /// Initializes the processor with channel setup and validation.
    /// </summary>
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Initializing DelimitedSinkProcessor");
        _state = ProcessorState.Initializing;
        
        // Initialize the service
        await _service.InitializeAsync(cancellationToken);
        
        _state = ProcessorState.Initialized;
        _logger.LogDebug("DelimitedSinkProcessor initialized successfully");
    }

    /// <summary>
    /// Starts the processor and begins data processing operations.
    /// </summary>
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_state != ProcessorState.Initialized)
            throw new InvalidOperationException($"Processor must be initialized before starting. Current state: {_state}");

        _logger.LogDebug("Starting DelimitedSinkProcessor");
        _state = ProcessorState.Starting;
        
        await _service.StartAsync(cancellationToken);
        
        _state = ProcessorState.Running;
        _logger.LogDebug("DelimitedSinkProcessor started successfully");
    }

    /// <summary>
    /// Stops the processor and suspends processing operations.
    /// </summary>
    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (_state != ProcessorState.Running)
            return;

        _logger.LogDebug("Stopping DelimitedSinkProcessor");
        _state = ProcessorState.Stopping;
        
        await _service.StopAsync(cancellationToken);
        
        _state = ProcessorState.Stopped;
        _logger.LogDebug("DelimitedSinkProcessor stopped successfully");
    }

    /// <summary>
    /// Processes a single data chunk using the delimited sink service.
    /// </summary>
    public async Task<IChunk> ProcessChunkAsync(IChunk chunk, CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        
        _logger.LogDebug("Processing chunk with {RowCount} rows through delimited sink", chunk.RowCount);

        try
        {
            // Delegate chunk processing to the service
            await _service.ProcessChunkAsync(chunk, cancellationToken);
            
            stopwatch.Stop();
            
            // Update performance metrics
            lock (_metricsLock)
            {
                _totalRowsProcessed += chunk.RowCount;
                _totalProcessingTime += stopwatch.ElapsedMilliseconds;
                _totalChunksProcessed++;
                _requestCount++;
                _lastRequestTime = DateTime.UtcNow;
            }

            _logger.LogDebug(
                "Completed delimited sink processing of {RowCount} rows in {ElapsedMs}ms", 
                chunk.RowCount, 
                stopwatch.ElapsedMilliseconds);

            // For sink plugins, we typically return the same chunk as it's the end of the pipeline
            return chunk;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            
            lock (_metricsLock)
            {
                _errorCount++;
            }
            
            _logger.LogError(ex, 
                "Error processing chunk with {RowCount} rows in delimited sink after {ElapsedMs}ms", 
                chunk.RowCount, 
                stopwatch.ElapsedMilliseconds);
            
            throw;
        }
    }

    /// <summary>
    /// Updates the processor configuration with hot-swapping support.
    /// </summary>
    public async Task UpdateConfigurationAsync(IPluginConfiguration newConfiguration, CancellationToken cancellationToken = default)
    {
        if (newConfiguration is not DelimitedSinkConfiguration sinkConfig)
            throw new ArgumentException("Invalid configuration type for DelimitedSinkProcessor");

        _logger.LogDebug("Updating DelimitedSinkProcessor configuration");
        
        // Update internal configuration reference
        _configuration = sinkConfig;
        
        // Delegate to service for actual configuration update if supported
        // Note: This assumes the service has an update method - implement as needed
        
        _logger.LogDebug("DelimitedSinkProcessor configuration updated successfully");
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
            _logger.LogError(ex, "Health check failed for DelimitedSinkProcessor");
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
    /// Gets current processor performance metrics and statistics.
    /// </summary>
    /// <returns>Current processor performance metrics</returns>
    public ProcessorMetrics GetMetrics()
    {
        lock (_metricsLock)
        {
            var averageTimePerRow = _totalRowsProcessed > 0 
                ? (double)_totalProcessingTime / _totalRowsProcessed 
                : 0;

            var throughput = _totalProcessingTime > 0 
                ? _totalRowsProcessed / (_totalProcessingTime / 1000.0) 
                : 0;

            var averageChunkSize = _totalChunksProcessed > 0
                ? (double)_totalRowsProcessed / _totalChunksProcessed
                : 0;

            return new ProcessorMetrics
            {
                TotalChunksProcessed = _totalChunksProcessed,
                TotalRowsProcessed = _totalRowsProcessed,
                ErrorCount = _errorCount,
                AverageProcessingTimeMs = averageTimePerRow,
                MemoryUsageBytes = GC.GetTotalMemory(false),
                ThroughputPerSecond = throughput,
                LastProcessedAt = _lastRequestTime?.ToUniversalTime() ?? DateTimeOffset.MinValue,
                BackpressureLevel = 0, // Not implemented for sink processors
                AverageChunkSize = averageChunkSize
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