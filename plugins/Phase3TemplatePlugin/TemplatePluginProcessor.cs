using FlowEngine.Abstractions.Data;
using FlowEngine.Abstractions.Plugins;
using Microsoft.Extensions.Logging;

namespace Phase3TemplatePlugin;

/// <summary>
/// COMPONENT 4: Processor
/// 
/// Demonstrates proper Phase 3 processor orchestration with:
/// - Data flow orchestration between framework and business logic
/// - Chunk-based processing with ArrayRow optimization
/// - Error handling and recovery
/// - Performance monitoring and metrics
/// - Integration with Core framework through Abstractions
/// </summary>
public sealed class TemplatePluginProcessor : IPluginProcessor
{
    private readonly TemplatePlugin _plugin;
    private readonly ILogger _logger;
    private readonly TemplatePluginService _service;
    private ProcessorState _state = ProcessorState.Created;
    private long _totalChunksProcessed;
    private long _totalRowsProcessed;
    private long _errorCount;
    private DateTime _lastProcessedAt;
    private readonly object _metricsLock = new();

    /// <summary>
    /// Constructor demonstrating proper component composition
    /// </summary>
    public TemplatePluginProcessor(TemplatePlugin plugin, ILogger logger)
    {
        _plugin = plugin ?? throw new ArgumentNullException(nameof(plugin));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _service = new TemplatePluginService(plugin, logger);
    }

    /// <inheritdoc />
    public ProcessorState State => _state;

    /// <inheritdoc />
    public bool IsServiceCompatible(IPluginService service)
    {
        return service is TemplatePluginService;
    }

    // === Lifecycle Management ===

    /// <inheritdoc />
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Initializing Template Plugin Processor");
        
        if (_state != ProcessorState.Created)
            throw new InvalidOperationException($"Processor cannot be initialized from state {_state}");

        // Initialize the service component
        await _service.InitializeAsync(cancellationToken);
        
        _state = ProcessorState.Initialized;
        _logger.LogDebug("Template Plugin Processor initialized successfully");
    }

    /// <inheritdoc />
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Starting Template Plugin Processor");

        if (_state != ProcessorState.Initialized)
            throw new InvalidOperationException($"Processor must be initialized before starting. Current state: {_state}");

        // Start the service component
        await _service.StartAsync(cancellationToken);
        
        _state = ProcessorState.Running;
        _logger.LogDebug("Template Plugin Processor started successfully");
    }

    /// <inheritdoc />
    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Stopping Template Plugin Processor");

        if (_state == ProcessorState.Running)
        {
            await _service.StopAsync(cancellationToken);
            _state = ProcessorState.Stopped;
        }

        _logger.LogDebug("Template Plugin Processor stopped successfully");
    }

    /// <inheritdoc />
    public async Task UpdateConfigurationAsync(IPluginConfiguration newConfiguration, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Updating Template Plugin Processor configuration");

        if (newConfiguration is not TemplatePluginConfiguration)
        {
            throw new ArgumentException("Configuration must be TemplatePluginConfiguration", nameof(newConfiguration));
        }

        // Delegate to plugin's hot-swap functionality
        var result = await _plugin.HotSwapAsync(newConfiguration, cancellationToken);
        
        if (!result.Success)
        {
            throw new InvalidOperationException($"Configuration update failed: {result.Message}");
        }

        _logger.LogInformation("Template Plugin Processor configuration updated successfully");
    }

    // === Data Processing Orchestration ===

    /// <inheritdoc />
    public async Task<IChunk> ProcessChunkAsync(IChunk chunk, CancellationToken cancellationToken = default)
    {
        if (_state != ProcessorState.Running)
            throw new InvalidOperationException($"Processor must be running to process chunks. Current state: {_state}");

        var startTime = DateTime.UtcNow;
        
        try
        {
            _logger.LogDebug("Processing chunk with {RowCount} rows", chunk.RowCount);

            // Orchestrate the processing through the service
            var processedRows = new List<IArrayRow>();

            foreach (var row in chunk.GetRows())
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    // Delegate actual processing to service component
                    var processedRow = await _service.ProcessRowAsync(row, cancellationToken);
                    processedRows.Add(processedRow);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing row in chunk");
                    
                    lock (_metricsLock)
                    {
                        _errorCount++;
                    }

                    // Depending on error handling strategy, might continue or rethrow
                    // For template, we'll continue processing other rows
                    continue;
                }
            }

            // Create result chunk with processed rows
            // TODO: Need Core bridge to create Chunk - this demonstrates the integration gap
            // var resultChunk = new Chunk(chunk.Schema, processedRows, chunk.Metadata);

            // For now, return original chunk as placeholder
            var resultChunk = chunk;

            // Update metrics
            var processingTime = DateTime.UtcNow - startTime;
            lock (_metricsLock)
            {
                _totalChunksProcessed++;
                _totalRowsProcessed += processedRows.Count;
                _lastProcessedAt = DateTime.UtcNow;
            }

            _logger.LogDebug(
                "Chunk processed successfully. {RowCount} rows in {ProcessingTime}ms",
                processedRows.Count,
                processingTime.TotalMilliseconds);

            return resultChunk;
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Chunk processing cancelled");
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process chunk");
            
            lock (_metricsLock)
            {
                _errorCount++;
            }
            
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<IDataset> ProcessAsync(IDataset input, CancellationToken cancellationToken = default)
    {
        if (_state != ProcessorState.Running)
            throw new InvalidOperationException($"Processor must be running to process datasets. Current state: {_state}");

        _logger.LogInformation("Processing dataset with {ChunkCount} chunks", input.ChunkCount);

        var processedChunks = new List<IChunk>();
        var startTime = DateTime.UtcNow;

        try
        {
            // Process each chunk in the dataset
            await foreach (var chunk in input.GetChunksAsync(cancellationToken: cancellationToken))
            {
                cancellationToken.ThrowIfCancellationRequested();

                var processedChunk = await ProcessChunkAsync(chunk, cancellationToken);
                processedChunks.Add(processedChunk);
            }

            // TODO: Need Core bridge to create Dataset - this demonstrates the integration gap
            // var resultDataset = new Dataset(input.Schema, processedChunks);

            // For now, return original dataset as placeholder
            var resultDataset = input;

            var processingTime = DateTime.UtcNow - startTime;
            _logger.LogInformation(
                "Dataset processed successfully. {ChunkCount} chunks, {RowCount} total rows in {ProcessingTime}ms",
                processedChunks.Count,
                _totalRowsProcessed,
                processingTime.TotalMilliseconds);

            return resultDataset;
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Dataset processing cancelled");
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process dataset");
            throw;
        }
    }

    // === Monitoring and Metrics ===

    /// <inheritdoc />
    public ProcessorMetrics GetMetrics()
    {
        lock (_metricsLock)
        {
            var currentTime = DateTime.UtcNow;
            var throughput = _totalRowsProcessed > 0 && _lastProcessedAt != default 
                ? _totalRowsProcessed / (currentTime - _lastProcessedAt).TotalSeconds 
                : 0.0;

            return new ProcessorMetrics
            {
                TotalChunksProcessed = _totalChunksProcessed,
                TotalRowsProcessed = _totalRowsProcessed,
                ErrorCount = _errorCount,
                AverageProcessingTimeMs = 0.0, // Would calculate from timing data
                MemoryUsageBytes = GC.GetTotalMemory(false),
                ThroughputPerSecond = throughput,
                LastProcessedAt = _lastProcessedAt != default ? _lastProcessedAt : (DateTimeOffset?)null,
                BackpressureLevel = 0, // Would calculate based on queue depths
                AverageChunkSize = _totalChunksProcessed > 0 
                    ? (double)_totalRowsProcessed / _totalChunksProcessed 
                    : 0.0
            };
        }
    }

    /// <inheritdoc />
    public async Task<ProcessorHealth> CheckHealthAsync(CancellationToken cancellationToken = default)
    {
        var serviceHealth = await _service.CheckHealthAsync(cancellationToken);
        
        // Determine flow status based on service health and processor state
        var flowStatus = _state == ProcessorState.Running && serviceHealth.IsHealthy 
            ? DataFlowStatus.Normal 
            : DataFlowStatus.Failed;

        return new ProcessorHealth
        {
            IsHealthy = serviceHealth.IsHealthy && _state == ProcessorState.Running,
            State = _state,
            FlowStatus = flowStatus,
            Checks = new List<HealthCheck>
            {
                new HealthCheck
                {
                    Name = "ServiceHealth",
                    Passed = serviceHealth.IsHealthy,
                    Details = serviceHealth.Message,
                    ExecutionTime = TimeSpan.FromMilliseconds(1),
                    Severity = serviceHealth.IsHealthy ? HealthCheckSeverity.Info : HealthCheckSeverity.Error
                },
                new HealthCheck
                {
                    Name = "ProcessorState",
                    Passed = _state == ProcessorState.Running,
                    Details = $"Processor is in {_state} state",
                    ExecutionTime = TimeSpan.FromMilliseconds(1),
                    Severity = _state == ProcessorState.Running ? HealthCheckSeverity.Info : HealthCheckSeverity.Warning
                }
            },
            LastChecked = DateTimeOffset.UtcNow,
            PerformanceScore = serviceHealth.IsHealthy && _state == ProcessorState.Running ? 100 : 0
        };
    }

    /// <inheritdoc />
    public void ResetMetrics()
    {
        _logger.LogDebug("Resetting Template Plugin Processor metrics");
        
        lock (_metricsLock)
        {
            _totalChunksProcessed = 0;
            _totalRowsProcessed = 0;
            _errorCount = 0;
            _lastProcessedAt = default;
        }
    }

    // === IDisposable Implementation ===

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        _logger.LogDebug("Disposing Template Plugin Processor");

        if (_state == ProcessorState.Running)
            await StopAsync();

        await _service.DisposeAsync();
        _state = ProcessorState.Disposed;
        
        _logger.LogDebug("Template Plugin Processor disposed successfully");
    }

    /// <inheritdoc />
    public void Dispose()
    {
        DisposeAsync().AsTask().GetAwaiter().GetResult();
    }
}