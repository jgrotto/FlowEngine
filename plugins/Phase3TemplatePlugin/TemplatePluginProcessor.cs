using FlowEngine.Abstractions.Data;
using FlowEngine.Abstractions.Plugins;
using Microsoft.Extensions.Logging;

namespace Phase3TemplatePlugin;

/// <summary>
/// COMPONENT 4: Processor
/// 
/// Demonstrates proper Phase 3 processor orchestration using PluginProcessorBase:
/// - Data flow orchestration between framework and business logic
/// - Chunk-based processing with ArrayRow optimization
/// - Error handling and recovery
/// - Performance monitoring and metrics (handled by base class)
/// - Integration with Core framework through Abstractions
/// </summary>
public sealed class TemplatePluginProcessor : PluginProcessorBase
{
    private TemplatePluginConfiguration? _configuration;
    private readonly TemplatePluginService _service;

    /// <summary>
    /// Constructor demonstrating proper component composition with base class
    /// </summary>
    public TemplatePluginProcessor(ILogger<TemplatePluginProcessor> logger) : base(logger)
    {
        _service = new TemplatePluginService();
    }

    /// <summary>
    /// Parameterless constructor for factory pattern
    /// </summary>
    public TemplatePluginProcessor() : base(Microsoft.Extensions.Logging.Abstractions.NullLogger<TemplatePluginProcessor>.Instance)
    {
        _service = new TemplatePluginService();
    }

    /// <inheritdoc />
    public override IPluginConfiguration Configuration 
    { 
        get => _configuration ?? throw new InvalidOperationException("Processor not initialized");
        protected set => _configuration = (TemplatePluginConfiguration)value;
    }

    /// <summary>
    /// Checks if the service is compatible with this processor
    /// </summary>
    public bool IsServiceCompatible(IPluginService service)
    {
        return service is TemplatePluginService;
    }

    // === Base Class Overrides ===

    /// <inheritdoc />
    protected override async Task InitializeInternalAsync(IPluginConfiguration configuration, CancellationToken cancellationToken)
    {
        Logger.LogDebug("Initializing Template Plugin Processor");
        
        _configuration = configuration as TemplatePluginConfiguration ?? throw new ArgumentException("Expected TemplatePluginConfiguration");
        
        // Initialize the service component
        await _service.InitializeAsync(configuration, cancellationToken);
        
        Logger.LogDebug("Template Plugin Processor initialized successfully");
    }

    /// <inheritdoc />
    protected override async Task StartInternalAsync(CancellationToken cancellationToken)
    {
        Logger.LogDebug("Starting Template Plugin Processor");

        // Start the service component
        await _service.StartAsync(cancellationToken);
        
        Logger.LogDebug("Template Plugin Processor started successfully");
    }

    /// <inheritdoc />
    protected override async Task StopInternalAsync(CancellationToken cancellationToken)
    {
        Logger.LogDebug("Stopping Template Plugin Processor");

        await _service.StopAsync(cancellationToken);

        Logger.LogDebug("Template Plugin Processor stopped successfully");
    }

    // Configuration updates handled by base class through hot-swap

    // === Data Processing Orchestration ===

    /// <inheritdoc />
    protected override async Task<ProcessResult> ProcessInternalAsync(IChunk input, CancellationToken cancellationToken)
    {
        try
        {
            Logger.LogDebug("Processing chunk with {RowCount} rows", input.RowCount);

            // Delegate processing to service component
            var processedChunk = await _service.ProcessChunkAsync(input, cancellationToken);

            Logger.LogDebug("Chunk processed successfully. {RowCount} rows", processedChunk.RowCount);

            return ProcessResult.CreateSuccess(processedChunk, TimeSpan.Zero);
        }
        catch (OperationCanceledException)
        {
            Logger.LogInformation("Chunk processing cancelled");
            throw;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to process chunk");
            return ProcessResult.CreateFailure(ex.Message);
        }
    }

    /// <inheritdoc />
    protected override async Task<IDataset> ProcessDatasetInternalAsync(IDataset input, CancellationToken cancellationToken)
    {
        Logger.LogInformation("Processing dataset with streaming approach");
        
        try
        {
            // Use the service to process the entire dataset
            // For a real implementation, this would stream through chunks
            // For template purposes, we'll return the input as-is
            Logger.LogInformation("Dataset processing completed");
            return input;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to process dataset");
            throw;
        }
    }

    // === Monitoring and Metrics (handled by base class) ===
    // Base class provides:
    // - GetMetrics() with automatic performance tracking
    // - HealthCheckAsync() with processor state monitoring
    // - Automatic error counting and throughput calculation

    // Disposal handled by PluginProcessorBase
}