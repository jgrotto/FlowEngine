using FlowEngine.Abstractions.Data;
using FlowEngine.Abstractions.Plugins;
using FlowEngine.Abstractions.Factories;
using FlowEngine.Abstractions.Services;
using Microsoft.Extensions.Logging;
using System.Threading;

namespace Phase3TemplatePlugin;

/// <summary>
/// COMPONENT 5: Service
/// 
/// Demonstrates proper Phase 3 service implementation with:
/// - Core business logic with ArrayRow optimization
/// - Dependency injection of Core services (when available)
/// - O(1) field access using pre-calculated indexes
/// - Performance-optimized data processing (>200K rows/sec target)
/// - Proper error handling and resource management
/// </summary>
public sealed class TemplatePluginService : IPluginService
{
    private TemplatePluginConfiguration? _config;
    private readonly Random _random = new();
    private long _rowsProcessed;
    private readonly object _metricsLock = new();
    private bool _disposed;
    
    // Injected Core services for real data processing (optional for backward compatibility)
    private readonly IArrayRowFactory? _arrayRowFactory;
    private readonly ISchemaFactory? _schemaFactory;
    private readonly IChunkFactory? _chunkFactory;
    private readonly IDataTypeService? _dataTypeService;
    private readonly IMemoryManager? _memoryManager;
    private readonly IPerformanceMonitor? _performanceMonitor;
    private readonly IChannelTelemetry? _channelTelemetry;
    private readonly ILogger<TemplatePluginService> _logger;
    
    // Pre-calculated field indexes for O(1) access
    private int _idIndex = -1;
    private int _nameIndex = -1;
    private int _categoryIndex = -1;
    private int _priceIndex = -1;
    private int _timestampIndex = -1;

    // Sample data for generation (in real implementation, this might come from injected services)
    private static readonly string[] SampleNames = 
    {
        "Alice", "Bob", "Charlie", "Diana", "Edward", "Fiona", "George", "Hannah",
        "Ivan", "Julia", "Kevin", "Luna", "Marcus", "Nina", "Oliver", "Petra"
    };

    private static readonly string[] SampleCategories = 
    {
        "Electronics", "Books", "Clothing", "Home", "Sports", "Toys", "Automotive", "Health"
    };

    /// <summary>
    /// Constructor with full service injection for real data processing
    /// </summary>
    public TemplatePluginService(
        IArrayRowFactory? arrayRowFactory = null,
        ISchemaFactory? schemaFactory = null,
        IChunkFactory? chunkFactory = null,
        IDataTypeService? dataTypeService = null,
        IMemoryManager? memoryManager = null,
        IPerformanceMonitor? performanceMonitor = null,
        IChannelTelemetry? channelTelemetry = null,
        ILogger<TemplatePluginService>? logger = null)
    {
        _arrayRowFactory = arrayRowFactory;
        _schemaFactory = schemaFactory;
        _chunkFactory = chunkFactory;
        _dataTypeService = dataTypeService;
        _memoryManager = memoryManager;
        _performanceMonitor = performanceMonitor;
        _channelTelemetry = channelTelemetry;
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<TemplatePluginService>.Instance;
    }

    // === IPluginService Implementation ===

    /// <inheritdoc />
    public string Id { get; } = Guid.NewGuid().ToString();

    /// <inheritdoc />
    public IPluginConfiguration Configuration => _config ?? throw new InvalidOperationException("Service not initialized");

    /// <inheritdoc />
    public ServiceMetrics Metrics => new ServiceMetrics
    {
        TotalChunks = 0,
        TotalRows = _rowsProcessed,
        TotalProcessingTime = TimeSpan.Zero,
        CurrentMemoryUsage = GC.GetTotalMemory(false),
        PeakMemoryUsage = GC.GetTotalMemory(false),
        ErrorCount = 0,
        WarningCount = 0,
        Timestamp = DateTimeOffset.UtcNow
    };

    /// <inheritdoc />
    public async Task InitializeAsync(IPluginConfiguration configuration, CancellationToken cancellationToken = default)
    {
        if (configuration is not TemplatePluginConfiguration templateConfig)
        {
            throw new ArgumentException("Expected TemplatePluginConfiguration", nameof(configuration));
        }

        _config = templateConfig;
        
        // Pre-calculate field indexes for O(1) access performance
        if (_config.OutputSchema != null)
        {
            _idIndex = _config.OutputSchema.GetIndex("Id");
            _nameIndex = _config.OutputSchema.GetIndex("Name");
            _categoryIndex = _config.OutputSchema.GetIndex("Category");
            _priceIndex = _config.OutputSchema.GetIndex("Price");
            _timestampIndex = _config.OutputSchema.GetIndex("Timestamp");
            
            _logger.LogInformation("TemplatePluginService initialized with schema. Field indexes: Id={IdIndex}, Name={NameIndex}, Category={CategoryIndex}, Price={PriceIndex}, Timestamp={TimestampIndex}",
                _idIndex, _nameIndex, _categoryIndex, _priceIndex, _timestampIndex);
        }
        
        await Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_config == null)
            throw new InvalidOperationException("Service must be initialized before starting");

        await Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask;
    }

    /// <inheritdoc />
    public SchemaCompatibilityResult ValidateInputSchema(ISchema inputSchema)
    {
        // For a source plugin, input schema validation is not applicable
        return SchemaCompatibilityResult.Success();
    }

    /// <inheritdoc />
    public ISchema GetOutputSchema(ISchema inputSchema)
    {
        return _config?.OutputSchema ?? throw new InvalidOperationException("Service not initialized");
    }

    /// <inheritdoc />
    public async Task<IEnumerable<HealthCheck>> HealthCheckAsync(CancellationToken cancellationToken = default)
    {
        return new[]
        {
            new HealthCheck
            {
                Name = "Service State",
                Passed = _config != null,
                Details = _config != null ? "Service initialized" : "Service not initialized",
                ExecutionTime = TimeSpan.Zero,
                Severity = HealthCheckSeverity.Info
            }
        };
    }

    // === Core Business Logic with ArrayRow Optimization ===

    /// <inheritdoc />
    public async Task<IChunk> ProcessChunkAsync(IChunk input, CancellationToken cancellationToken = default)
    {
        if (_config == null)
            throw new InvalidOperationException("Service not initialized");
            
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        
        try
        {
            // For source plugins, generate new data instead of processing input
            var outputRows = new List<IArrayRow>();
            var batchSize = Math.Min(_config.BatchSize, _config.RowCount);
            
            for (int i = 0; i < batchSize; i++)
            {
                var row = await GenerateDataRowAsync(cancellationToken);
                outputRows.Add(row);
                
                // Check memory pressure and yield if needed (if memory manager available)
                if (i % 100 == 0 && _memoryManager?.IsUnderMemoryPressure() == true)
                {
                    _logger.LogWarning("Memory pressure detected, yielding control");
                    await Task.Yield();
                }
            }
            
            var chunk = _chunkFactory?.CreateChunk(_config.OutputSchema, outputRows) ?? CreateBasicChunk(_config.OutputSchema, outputRows);
            
            stopwatch.Stop();
            _performanceMonitor?.RecordChunkProcessed(outputRows.Count, stopwatch.Elapsed, "TemplatePluginService");
            
            lock (_metricsLock)
            {
                _rowsProcessed += outputRows.Count;
            }
            
            return chunk;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogError(ex, "Error processing chunk in TemplatePluginService");
            _performanceMonitor?.RecordCounter("ProcessChunk.Errors", 1);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<IArrayRow> ProcessRowAsync(IArrayRow input, CancellationToken cancellationToken = default)
    {
        if (_config == null)
            throw new InvalidOperationException("Service not initialized");

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        
        try
        {
            // For source plugins, generate new data instead of processing input
            var row = await GenerateDataRowAsync(cancellationToken);
            
            stopwatch.Stop();
            _performanceMonitor?.RecordProcessingTime("ProcessRow", stopwatch.Elapsed);
            
            lock (_metricsLock)
            {
                _rowsProcessed++;
            }

            return row;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogError(ex, "Error processing row in TemplatePluginService");
            _performanceMonitor?.RecordCounter("ProcessRow.Errors", 1);
            throw;
        }
    }

    // === Real Data Generation Using Core Factories ===
    
    /// <summary>
    /// Generates a single data row using injected Core factories with O(1) field access
    /// </summary>
    private async Task<IArrayRow> GenerateDataRowAsync(CancellationToken cancellationToken = default)
    {
        if (_config?.OutputSchema == null)
            throw new InvalidOperationException("Output schema not configured");
            
        // Create array for field values using pre-calculated indexes
        var fieldCount = _config.OutputSchema.ColumnCount;
        var values = new object?[fieldCount];
        
        // Generate data based on configured type
        switch (_config.DataType)
        {
            case "TestData":
                GenerateTestData(values);
                break;
            case "Random":
                GenerateRandomData(values);
                break;
            case "Sequential":
                GenerateSequentialData(values);
                break;
            default:
                throw new InvalidOperationException($"Unsupported data type: {_config.DataType}");
        }
        
        // Use injected factory to create ArrayRow with proper validation (if available)
        var result = _arrayRowFactory?.CreateRow(_config.OutputSchema, values) ?? CreateBasicRow(_config.OutputSchema, values);
        
        await Task.CompletedTask;
        return result;
    }
    
    /// <summary>
    /// Generates test data with realistic business values
    /// </summary>
    private void GenerateTestData(object?[] values)
    {
        var currentRowId = Interlocked.Read(ref _rowsProcessed) + 1;
        
        if (_idIndex >= 0) values[_idIndex] = (int)currentRowId;
        if (_nameIndex >= 0) values[_nameIndex] = SampleNames[_random.Next(SampleNames.Length)];
        if (_categoryIndex >= 0) values[_categoryIndex] = SampleCategories[_random.Next(SampleCategories.Length)];
        if (_priceIndex >= 0) values[_priceIndex] = Math.Round(_random.NextDouble() * 999.99 + 0.01, 2);
        if (_timestampIndex >= 0) values[_timestampIndex] = DateTimeOffset.UtcNow;
    }
    
    /// <summary>
    /// Generates random data for stress testing
    /// </summary>
    private void GenerateRandomData(object?[] values)
    {
        var currentRowId = Interlocked.Read(ref _rowsProcessed) + 1;
        
        if (_idIndex >= 0) values[_idIndex] = _random.Next(1, 1_000_000);
        if (_nameIndex >= 0) values[_nameIndex] = $"Random{_random.Next(10000, 99999)}";
        if (_categoryIndex >= 0) values[_categoryIndex] = $"Category{_random.Next(1, 10)}";
        if (_priceIndex >= 0) values[_priceIndex] = Math.Round(_random.NextDouble() * 9999.99, 2);
        if (_timestampIndex >= 0) values[_timestampIndex] = DateTimeOffset.UtcNow.AddMinutes(_random.Next(-1440, 1440));
    }
    
    /// <summary>
    /// Generates sequential data for predictable testing
    /// </summary>
    private void GenerateSequentialData(object?[] values)
    {
        var currentRowId = Interlocked.Read(ref _rowsProcessed) + 1;
        
        if (_idIndex >= 0) values[_idIndex] = (int)currentRowId;
        if (_nameIndex >= 0) values[_nameIndex] = $"Item{currentRowId:D6}";
        if (_categoryIndex >= 0) values[_categoryIndex] = $"Category{(currentRowId % 5) + 1}";
        if (_priceIndex >= 0) values[_priceIndex] = Math.Round((double)currentRowId * 1.5, 2);
        if (_timestampIndex >= 0) values[_timestampIndex] = DateTimeOffset.UtcNow.AddSeconds(currentRowId);
    }
    
    // === Basic Factory Methods (Fallback when DI not available) ===
    
    /// <summary>
    /// Creates a basic ArrayRow when factory is not available
    /// </summary>
    private static IArrayRow CreateBasicRow(ISchema schema, object?[] values)
    {
        // Use reflection to create ArrayRow from Core assembly
        var coreAssembly = System.Reflection.Assembly.LoadFrom("FlowEngine.Core.dll");
        var arrayRowType = coreAssembly.GetType("FlowEngine.Core.Data.ArrayRow");
        if (arrayRowType == null)
            throw new InvalidOperationException("Unable to find ArrayRow type in Core assembly");
            
        var constructor = arrayRowType.GetConstructor(new[] { typeof(ISchema), typeof(object[]) });
        if (constructor == null)
            throw new InvalidOperationException("Unable to find ArrayRow constructor");
            
        return (IArrayRow)constructor.Invoke(new object[] { schema, values });
    }
    
    /// <summary>
    /// Creates a basic Chunk when factory is not available
    /// </summary>
    private static IChunk CreateBasicChunk(ISchema schema, IEnumerable<IArrayRow> rows)
    {
        // Use reflection to create Chunk from Core assembly
        var coreAssembly = System.Reflection.Assembly.LoadFrom("FlowEngine.Core.dll");
        var chunkType = coreAssembly.GetType("FlowEngine.Core.Data.Chunk");
        if (chunkType == null)
            throw new InvalidOperationException("Unable to find Chunk type in Core assembly");
            
        var constructor = chunkType.GetConstructor(new[] { typeof(ISchema), typeof(IArrayRow[]) });
        if (constructor == null)
            throw new InvalidOperationException("Unable to find Chunk constructor");
            
        return (IChunk)constructor.Invoke(new object[] { schema, rows.ToArray() });
    }
    
    /// <summary>
    /// Disposes the service resources
    /// </summary>
    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
        }
    }

}