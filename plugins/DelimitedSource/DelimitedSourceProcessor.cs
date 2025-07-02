using FlowEngine.Abstractions.Data;
using FlowEngine.Abstractions.Plugins;
using Microsoft.Extensions.Logging;

namespace DelimitedSource;

/// <summary>
/// Minimal processor for DelimitedSource plugin implementing data flow orchestration.
/// Coordinates between framework and business logic while maintaining performance optimization.
/// </summary>
public sealed class DelimitedSourceProcessor : IPluginProcessor
{
    private readonly DelimitedSourceService _service;
    private readonly DelimitedSourceConfiguration _configuration;
    private readonly ILogger<DelimitedSourceProcessor> _logger;
    private ProcessorState _state = ProcessorState.Created;

    /// <summary>
    /// Gets the current processor state.
    /// </summary>
    public ProcessorState State => _state;

    /// <summary>
    /// Initializes a new instance of the DelimitedSourceProcessor class.
    /// </summary>
    /// <param name="service">Core business logic service</param>
    /// <param name="configuration">Plugin configuration</param>
    /// <param name="logger">Logger instance</param>
    public DelimitedSourceProcessor(
        DelimitedSourceService service,
        DelimitedSourceConfiguration configuration,
        ILogger<DelimitedSourceProcessor> logger)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Gets whether this processor is compatible with the specified service.
    /// </summary>
    /// <param name="service">Service to check compatibility</param>
    /// <returns>True if processor is compatible with the service</returns>
    public bool IsServiceCompatible(IPluginService service)
    {
        return service is DelimitedSourceService;
    }

    /// <summary>
    /// Initializes the processor with channel setup and validation.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token for initialization</param>
    /// <returns>Task representing the initialization operation</returns>
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Initializing DelimitedSourceProcessor");
        
        _state = ProcessorState.Initializing;
        
        try
        {
            await _service.InitializeAsync(cancellationToken);
            _state = ProcessorState.Initialized;
            
            _logger.LogDebug("DelimitedSourceProcessor initialized successfully");
        }
        catch (Exception ex)
        {
            _state = ProcessorState.Failed;
            _logger.LogError(ex, "Failed to initialize DelimitedSourceProcessor");
            throw;
        }
    }

    /// <summary>
    /// Starts the processor and begins data processing operations.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token for start operation</param>
    /// <returns>Task representing the start operation</returns>
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_state != ProcessorState.Initialized)
        {
            throw new InvalidOperationException($"Processor must be initialized before starting. Current state: {_state}");
        }

        _logger.LogDebug("Starting DelimitedSourceProcessor");
        
        _state = ProcessorState.Starting;
        
        try
        {
            await _service.StartAsync(cancellationToken);
            _state = ProcessorState.Running;
            
            _logger.LogDebug("DelimitedSourceProcessor started successfully");
        }
        catch (Exception ex)
        {
            _state = ProcessorState.Failed;
            _logger.LogError(ex, "Failed to start DelimitedSourceProcessor");
            throw;
        }
    }

    /// <summary>
    /// Stops the processor and suspends processing operations.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token for stop operation</param>
    /// <returns>Task representing the stop operation</returns>
    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Stopping DelimitedSourceProcessor");
        
        _state = ProcessorState.Stopping;
        
        try
        {
            await _service.StopAsync(cancellationToken);
            _state = ProcessorState.Stopped;
            
            _logger.LogDebug("DelimitedSourceProcessor stopped successfully");
        }
        catch (Exception ex)
        {
            _state = ProcessorState.Failed;
            _logger.LogError(ex, "Failed to stop DelimitedSourceProcessor");
            throw;
        }
    }

    /// <summary>
    /// Processes a single data chunk using the coordinated service component.
    /// </summary>
    /// <param name="chunk">Data chunk to process</param>
    /// <param name="cancellationToken">Cancellation token for processing</param>
    /// <returns>Processed data chunk</returns>
    public async Task<IChunk> ProcessChunkAsync(IChunk chunk, CancellationToken cancellationToken = default)
    {
        // Source plugins generate chunks rather than process existing ones
        _logger.LogWarning("ProcessChunkAsync called on source plugin. Source plugins generate data chunks.");
        
        return await Task.FromResult(chunk);
    }

    /// <summary>
    /// Updates the processor configuration with hot-swapping support.
    /// </summary>
    /// <param name="newConfiguration">New configuration to apply</param>
    /// <param name="cancellationToken">Cancellation token for update operation</param>
    /// <returns>Task representing the configuration update operation</returns>
    public async Task UpdateConfigurationAsync(IPluginConfiguration newConfiguration, CancellationToken cancellationToken = default)
    {
        if (newConfiguration is not DelimitedSourceConfiguration newConfig)
        {
            throw new ArgumentException("Invalid configuration type for DelimitedSource processor", nameof(newConfiguration));
        }

        _logger.LogInformation("Updating DelimitedSourceProcessor configuration");
        
        await _service.HotSwapConfigurationAsync(newConfig, cancellationToken);
        
        _logger.LogInformation("DelimitedSourceProcessor configuration updated successfully");
    }

    /// <summary>
    /// Gets current processor performance metrics and statistics.
    /// </summary>
    /// <returns>Current processor performance metrics</returns>
    public ProcessorMetrics GetMetrics()
    {
        var serviceMetrics = _service.GetMetrics();
        
        return new ProcessorMetrics
        {
            TotalChunksProcessed = 0, // Source plugins generate chunks
            TotalRowsProcessed = serviceMetrics.ProcessedRows,
            ErrorCount = serviceMetrics.ErrorCount,
            AverageProcessingTimeMs = serviceMetrics.AverageProcessingTime.TotalMilliseconds,
            MemoryUsageBytes = serviceMetrics.MemoryUsage,
            ThroughputPerSecond = serviceMetrics.ProcessingRate,
            LastProcessedAt = serviceMetrics.LastUpdated,
            BackpressureLevel = 0, // Source plugins don't experience backpressure
            AverageChunkSize = 5000 // Default chunk size
        };
    }

    /// <summary>
    /// Performs a health check on the processor and its data flow.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token for health check timeout</param>
    /// <returns>Processor health check result</returns>
    public async Task<ProcessorHealth> CheckHealthAsync(CancellationToken cancellationToken = default)
    {
        var serviceHealth = await _service.CheckHealthAsync(cancellationToken);
        
        var checks = new List<HealthCheck>
        {
            new HealthCheck
            {
                Name = "ProcessorState",
                Passed = _state == ProcessorState.Running || _state == ProcessorState.Initialized,
                Details = $"Current state: {_state}",
                ExecutionTime = TimeSpan.FromMilliseconds(1),
                Severity = _state == ProcessorState.Failed ? HealthCheckSeverity.Critical : HealthCheckSeverity.Info
            }
        };

        if (serviceHealth.Details != null)
        {
            checks.Add(new HealthCheck
            {
                Name = "ServiceHealth",
                Passed = serviceHealth.Status == HealthStatus.Healthy,
                Details = serviceHealth.Message,
                ExecutionTime = TimeSpan.FromMilliseconds(1),
                Severity = serviceHealth.Status == HealthStatus.Healthy ? HealthCheckSeverity.Info : HealthCheckSeverity.Error
            });
        }

        return new ProcessorHealth
        {
            IsHealthy = serviceHealth.Status == HealthStatus.Healthy && _state != ProcessorState.Failed,
            State = _state,
            FlowStatus = serviceHealth.Status == HealthStatus.Healthy ? DataFlowStatus.Normal : DataFlowStatus.Failed,
            Checks = checks,
            LastChecked = DateTimeOffset.UtcNow,
            PerformanceScore = serviceHealth.Status == HealthStatus.Healthy ? 100 : 0
        };
    }

    /// <summary>
    /// Disposes the processor and releases resources.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        try
        {
            if (_state == ProcessorState.Running)
            {
                await StopAsync();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during DelimitedSourceProcessor disposal");
        }
        finally
        {
            _state = ProcessorState.Disposed;
            _service?.Dispose();
        }
    }
}