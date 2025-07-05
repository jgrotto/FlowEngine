using FlowEngine.Abstractions.Data;
using FlowEngine.Abstractions.Plugins;
using FlowEngine.Abstractions.Factories;
using FlowEngine.Abstractions.Services;
using Microsoft.Extensions.Logging;
using System.Collections.Immutable;

namespace Phase3TemplatePlugin;

/// <summary>
/// COMPONENT 3: Plugin
/// 
/// Demonstrates proper Phase 3 plugin lifecycle management using PluginBase:
/// - Plugin metadata and versioning
/// - Lifecycle state management (Created → Initialized → Running → Stopped → Disposed)
/// - Hot-swapping configuration support
/// - Health monitoring and metrics
/// - Simplified implementation using base class
/// </summary>
public sealed class TemplatePlugin : PluginBase
{
    private TemplatePluginConfiguration? _configuration;
    private readonly ISchemaFactory? _schemaFactory;
    private readonly IArrayRowFactory? _arrayRowFactory;
    private readonly IChunkFactory? _chunkFactory;
    private readonly IDatasetFactory? _datasetFactory;
    private readonly IDataTypeService? _dataTypeService;
    private readonly IMemoryManager? _memoryManager;
    private readonly IPerformanceMonitor? _performanceMonitor;
    private readonly IChannelTelemetry? _channelTelemetry;

    /// <summary>
    /// Constructor demonstrating comprehensive service injection for advanced data processing
    /// </summary>
    /// <param name="schemaFactory">Factory for creating schemas</param>
    /// <param name="arrayRowFactory">Factory for creating array rows</param>
    /// <param name="chunkFactory">Factory for creating chunks</param>
    /// <param name="datasetFactory">Factory for creating datasets</param>
    /// <param name="dataTypeService">Service for data type operations</param>
    /// <param name="memoryManager">Service for memory management and pooling</param>
    /// <param name="performanceMonitor">Service for performance monitoring</param>
    /// <param name="channelTelemetry">Service for channel telemetry</param>
    /// <param name="logger">Logger injected from Core framework</param>
    public TemplatePlugin(
        ISchemaFactory schemaFactory,
        IArrayRowFactory arrayRowFactory,
        IChunkFactory chunkFactory,
        IDatasetFactory datasetFactory,
        IDataTypeService dataTypeService,
        IMemoryManager memoryManager,
        IPerformanceMonitor performanceMonitor,
        IChannelTelemetry channelTelemetry,
        ILogger<TemplatePlugin> logger) : base(logger)
    {
        _schemaFactory = schemaFactory ?? throw new ArgumentNullException(nameof(schemaFactory));
        _arrayRowFactory = arrayRowFactory ?? throw new ArgumentNullException(nameof(arrayRowFactory));
        _chunkFactory = chunkFactory ?? throw new ArgumentNullException(nameof(chunkFactory));
        _datasetFactory = datasetFactory ?? throw new ArgumentNullException(nameof(datasetFactory));
        _dataTypeService = dataTypeService ?? throw new ArgumentNullException(nameof(dataTypeService));
        _memoryManager = memoryManager ?? throw new ArgumentNullException(nameof(memoryManager));
        _performanceMonitor = performanceMonitor ?? throw new ArgumentNullException(nameof(performanceMonitor));
        _channelTelemetry = channelTelemetry ?? throw new ArgumentNullException(nameof(channelTelemetry));
    }

    /// <summary>
    /// Fallback constructor for backward compatibility (uses logger only)
    /// </summary>
    /// <param name="logger">Logger injected from Core framework</param>
    public TemplatePlugin(ILogger<TemplatePlugin> logger) : base(logger)
    {
        // This constructor is kept for backward compatibility but will log a warning
        Logger.LogWarning("TemplatePlugin created without Core services - advanced capabilities disabled");
    }

    // === Plugin Metadata (PluginBase Overrides) ===

    /// <inheritdoc />
    public override string Name => "Phase 3 Template Plugin";

    /// <inheritdoc />
    public override string Version => "3.0.0";

    /// <inheritdoc />
    public override string Description => "Reference implementation demonstrating correct Phase 3 five-component plugin architecture";

    /// <inheritdoc />
    public override string Author => "FlowEngine Team";

    /// <inheritdoc />
    public override string PluginType => "Source";

    /// <inheritdoc />
    public override bool SupportsHotSwapping => true;

    /// <inheritdoc />
    public override IPluginConfiguration Configuration 
    { 
        get => _configuration ?? throw new InvalidOperationException("Plugin not initialized");
        protected set => _configuration = (TemplatePluginConfiguration)value;
    }

    /// <inheritdoc />
    public override ISchema? OutputSchema
    {
        get
        {
            // Return the output schema defined in configuration, or null if not initialized
            return _configuration?.OutputSchema;
        }
    }

    // Additional plugin capabilities metadata
    public ImmutableArray<string> SupportedCapabilities => ImmutableArray.Create(
        "DataGeneration",
        "ConfigurableOutput",
        "BatchProcessing",
        "ArrayRowOptimization",
        "PerformanceMonitoring"
    );

    // === Base Class Overrides ===

    /// <inheritdoc />
    protected override async Task<PluginInitializationResult> InitializeInternalAsync(
        IPluginConfiguration configuration,
        CancellationToken cancellationToken)
    {
        Logger.LogInformation("Initializing Template Plugin v{Version}", Version);

        if (configuration is not TemplatePluginConfiguration templateConfig)
        {
            return new PluginInitializationResult
            {
                Success = false,
                Message = $"Expected TemplatePluginConfiguration, got {configuration?.GetType().Name ?? "null"}",
                InitializationTime = TimeSpan.Zero,
                Errors = ImmutableArray.Create("Invalid configuration type")
            };
        }

        try
        {
            // Validate configuration using validator
            var validator = new TemplatePluginValidator();
            var validationResult = validator.ValidateConfiguration(templateConfig);

            if (!validationResult.IsValid)
            {
                var errors = validationResult.Errors.Select(e => e.Message).ToArray();
                return new PluginInitializationResult
                {
                    Success = false,
                    Message = "Configuration validation failed",
                    InitializationTime = TimeSpan.Zero,
                    Errors = ImmutableArray.Create(errors)
                };
            }

            // Store configuration
            _configuration = templateConfig;

            Logger.LogInformation(
                "Template Plugin initialized successfully. RowCount: {RowCount}, BatchSize: {BatchSize}, DataType: {DataType}",
                templateConfig.RowCount,
                templateConfig.BatchSize,
                templateConfig.DataType);

            return new PluginInitializationResult
            {
                Success = true,
                Message = "Plugin initialized successfully",
                InitializationTime = TimeSpan.Zero
            };
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to initialize Template Plugin");

            return new PluginInitializationResult
            {
                Success = false,
                Message = $"Initialization failed: {ex.Message}",
                InitializationTime = TimeSpan.Zero,
                Errors = ImmutableArray.Create(ex.Message)
            };
        }
    }

    /// <inheritdoc />
    protected override async Task StartInternalAsync(CancellationToken cancellationToken)
    {
        Logger.LogInformation("Starting Template Plugin");
        
        // If we have factory services, demonstrate real data processing
        if (_schemaFactory != null && _arrayRowFactory != null && _chunkFactory != null && _datasetFactory != null)
        {
            var hasMonitoringServices = _memoryManager != null && _performanceMonitor != null && _channelTelemetry != null;
            Logger.LogInformation("🎯 Core services available - running data processing demonstration (monitoring: {HasMonitoring})", hasMonitoringServices);
            
            try
            {
                var dataset = await DemonstrateRealDataProcessingAsync();
                Logger.LogInformation("✅ Data processing demonstration completed - dataset created with {RowCount} rows", dataset.RowCount);
        Logger.LogInformation("📋 Configuration values used: RowCount={ConfigRowCount}, BatchSize={ConfigBatchSize}, DataType={ConfigDataType}", 
            _configuration?.RowCount ?? 0, _configuration?.BatchSize ?? 0, _configuration?.DataType ?? "not set");
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "❌ Data processing demonstration failed");
            }
        }
        else
        {
            Logger.LogWarning("⚠️ Core services not available - skipping data processing demonstration");
        }
        
        Logger.LogInformation("Template Plugin started successfully");
    }

    /// <inheritdoc />
    protected override async Task StopInternalAsync(CancellationToken cancellationToken)
    {
        Logger.LogInformation("Stopping Template Plugin");
        // Plugin-specific stop logic here
        Logger.LogInformation("Template Plugin stopped successfully");
        await Task.CompletedTask;
    }

    /// <summary>
    /// Demonstrates real data processing using Core factory services.
    /// This method showcases how external plugins can create and process Core data types.
    /// </summary>
    /// <returns>A dataset with processed sample data</returns>
    public async Task<IDataset> DemonstrateRealDataProcessingAsync()
    {
        if (_schemaFactory == null || _arrayRowFactory == null || _chunkFactory == null || _datasetFactory == null)
        {
            throw new InvalidOperationException("Plugin not initialized with factory services - real data processing not available");
        }

        Logger.LogInformation("🚀 Starting REAL DATA PROCESSING demonstration using Core services");

        // Start performance monitoring
        using var operation = _performanceMonitor?.StartOperation("TemplatePlugin.DataProcessing", 
            new Dictionary<string, string> { { "component", "TemplatePlugin" } });

        try
        {
            // === STEP 1: Create a Schema using SchemaFactory ===
            var columnDefinitions = new[]
            {
                new ColumnDefinition { Name = "Id", DataType = typeof(int), IsNullable = false, Index = 0 },
                new ColumnDefinition { Name = "Name", DataType = typeof(string), IsNullable = false, Index = 1 },
                new ColumnDefinition { Name = "Score", DataType = typeof(double), IsNullable = true, Index = 2 },
                new ColumnDefinition { Name = "CreatedAt", DataType = typeof(DateTime), IsNullable = false, Index = 3 }
            };

            var schema = _schemaFactory.CreateSchema(columnDefinitions);
            Logger.LogInformation("✅ Created schema with {ColumnCount} columns: {Schema}", schema.ColumnCount, schema.Signature);
            
            // Record schema creation performance
            _performanceMonitor?.RecordCounter("TemplatePlugin.SchemasCreated", 1, 
                new Dictionary<string, string> { { "component", "TemplatePlugin" } });

            // === STEP 2: Create ArrayRows using ArrayRowFactory with Memory Management ===
            var rows = new List<IArrayRow>();
            
            // Check memory pressure before creating large datasets
            if (_memoryManager?.IsUnderMemoryPressure() == true)
            {
                Logger.LogWarning("⚠️ System under memory pressure (level: {PressureLevel}), proceeding with caution", 
                    _memoryManager.PressureLevel);
            }
            
            var sampleData = new object[][]
            {
                new object[] { 1, "Alice Johnson", 95.5, DateTime.UtcNow.AddDays(-10) },
                new object[] { 2, "Bob Smith", 87.2, DateTime.UtcNow.AddDays(-9) },
                new object[] { 3, "Carol Williams", null, DateTime.UtcNow.AddDays(-8) }, // Null score
                new object[] { 4, "David Brown", 92.1, DateTime.UtcNow.AddDays(-7) },
                new object[] { 5, "Eve Davis", 89.7, DateTime.UtcNow.AddDays(-6) }
            };

            foreach (var rowData in sampleData)
            {
                var row = _arrayRowFactory.CreateRow(schema, rowData);
                rows.Add(row);
                Logger.LogDebug("Created row: Id={Id}, Name={Name}, Score={Score}", 
                    row[0], row[1], row[2]?.ToString() ?? "NULL");
            }

            Logger.LogInformation("✅ Created {RowCount} ArrayRows with real data", rows.Count);
            _performanceMonitor?.RecordCounter("TemplatePlugin.RowsCreated", rows.Count, 
                new Dictionary<string, string> { { "component", "TemplatePlugin" } });

            // === STEP 3: Create a Chunk using ChunkFactory ===
            var chunk = _chunkFactory.CreateChunk(rows.ToArray());
            Logger.LogInformation("✅ Created chunk with {RowCount} rows, memory size ~{MemorySize} bytes", 
                chunk.RowCount, chunk.ApproximateMemorySize);
            
            // Record chunk creation and memory usage
            _performanceMonitor?.RecordCounter("TemplatePlugin.ChunksCreated", 1, 
                new Dictionary<string, string> { { "component", "TemplatePlugin" } });
            _performanceMonitor?.RecordMemoryUsage("TemplatePlugin", chunk.ApproximateMemorySize);

            // === STEP 4: Demonstrate data processing (filtering and transformation) ===
            var filteredRows = new List<IArrayRow>();
            foreach (var row in chunk.GetRows())
            {
                // Filter: Only include rows with non-null scores
                if (row[2] != null)
                {
                    // Transform: Add bonus points to score
                    var originalScore = (double)row[2];
                    var bonusScore = originalScore + 5.0;
                    var transformedRow = row.With("Score", bonusScore);
                    filteredRows.Add(transformedRow);
                }
            }

            Logger.LogInformation("✅ Processed data: filtered {FilteredCount} rows from {TotalCount}, added bonus points", 
                filteredRows.Count, chunk.RowCount);
            
            // Record data processing metrics
            _performanceMonitor?.RecordCounter("TemplatePlugin.RowsProcessed", filteredRows.Count, 
                new Dictionary<string, string> { { "component", "TemplatePlugin" } });

            // === STEP 5: Create processed Chunk and Dataset ===
            var processedChunk = _chunkFactory.CreateChunk(filteredRows.ToArray());
            var dataset = _datasetFactory.CreateDataset(new[] { processedChunk });

            Logger.LogInformation("✅ Created final dataset with {ChunkCount} chunk(s), {RowCount} rows", 
                dataset.ChunkCount, dataset.RowCount);

            // === STEP 6: Demonstrate streaming processing ===
            Logger.LogInformation("🔄 Demonstrating streaming data access:");
            await foreach (var streamChunk in dataset.GetChunksAsync())
            {
                Logger.LogInformation("Processing chunk with {RowCount} rows", streamChunk.RowCount);
                foreach (var row in streamChunk.GetRows())
                {
                    Logger.LogDebug("  Row: {Id} | {Name} | {Score} | {Date}", 
                        row[0], row[1], row[2], row[3]);
                }
            }

            Logger.LogInformation("🎉 REAL DATA PROCESSING demonstration completed successfully!");
            Logger.LogInformation("📊 Summary: Created schema → {RowCount} rows → chunk → filtered/transformed → final dataset", 
                dataset.RowCount);

            // === STEP 7: Display performance statistics if monitoring is available ===
            if (_performanceMonitor != null)
            {
                var stats = _performanceMonitor.GetStatistics("TemplatePlugin");
                if (stats != null)
                {
                    Logger.LogInformation("📈 Performance Statistics: {Operations} operations, {AvgTime}ms avg, {TotalRows} rows processed", 
                        stats.TotalOperations, stats.AverageProcessingTime.TotalMilliseconds, stats.TotalRowsProcessed);
                }
            }

            return dataset;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "❌ Real data processing demonstration failed");
            throw;
        }
    }

    // Health checks and metrics handled by base class

    /// <inheritdoc />
    protected override async Task<HotSwapResult> HotSwapInternalAsync(
        IPluginConfiguration newConfiguration,
        CancellationToken cancellationToken)
    {
        Logger.LogInformation("Performing hot-swap for Template Plugin");

        if (newConfiguration is not TemplatePluginConfiguration newTemplateConfig)
        {
            return new HotSwapResult
            {
                Success = false,
                Message = $"Expected TemplatePluginConfiguration, got {newConfiguration?.GetType().Name ?? "null"}",
                SwapTime = TimeSpan.Zero
            };
        }

        try
        {
            // Validate new configuration
            var validator = new TemplatePluginValidator();
            var validationResult = validator.ValidateConfiguration(newTemplateConfig);

            if (!validationResult.IsValid)
            {
                return new HotSwapResult
                {
                    Success = false,
                    Message = "Hot-swap validation failed",
                    SwapTime = TimeSpan.Zero,
                    Errors = validationResult.Errors.Select(e => e.Message).ToImmutableArray()
                };
            }

            // Update configuration atomically
            var oldConfig = _configuration;
            _configuration = newTemplateConfig;

            Logger.LogInformation(
                "Hot-swap completed successfully. RowCount: {OldRowCount} → {NewRowCount}, BatchSize: {OldBatchSize} → {NewBatchSize}",
                oldConfig?.RowCount ?? 0,
                newTemplateConfig.RowCount,
                oldConfig?.BatchSize ?? 0,
                newTemplateConfig.BatchSize);

            return new HotSwapResult
            {
                Success = true,
                Message = "Configuration updated successfully",
                SwapTime = TimeSpan.Zero
            };
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Hot-swap failed for Template Plugin");
            
            return new HotSwapResult
            {
                Success = false,
                Message = $"Hot-swap failed: {ex.Message}",
                SwapTime = TimeSpan.Zero,
                Errors = ImmutableArray.Create(ex.Message)
            };
        }
    }

    // === Component Factory Methods (for framework integration) ===

    /// <summary>
    /// Creates the processor component for this plugin
    /// </summary>
    public IPluginProcessor GetProcessor()
    {
        return new TemplatePluginProcessor();
    }

    /// <summary>
    /// Creates the service component for this plugin
    /// </summary>
    public IPluginService GetService()
    {
        return new TemplatePluginService(); // Constructor now uses optional DI parameters
    }

    /// <summary>
    /// Creates the validator component for this plugin
    /// </summary>
    public IPluginValidator GetValidator()
    {
        return new TemplatePluginValidator();
    }

    // Disposal handled by PluginBase
}