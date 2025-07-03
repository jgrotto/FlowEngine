using FlowEngine.Abstractions.Data;
using FlowEngine.Abstractions.Plugins;
using Microsoft.Extensions.Logging;

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
    private readonly TemplatePlugin _plugin;
    private readonly ILogger _logger;
    
    // TODO: These would be injected from Core when bridge is implemented
    // private readonly IScriptEngineService _scriptEngine;
    // private readonly IDataTypeService _dataTypeService;
    // private readonly IPerformanceCounters _performanceCounters;

    private TemplatePluginConfiguration? _config;
    private readonly Random _random = new();
    private long _rowsProcessed;
    private readonly object _metricsLock = new();

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
    /// Constructor demonstrating dependency injection pattern
    /// </summary>
    /// <param name="plugin">Parent plugin for configuration access</param>
    /// <param name="logger">Logger injected from Core framework</param>
    public TemplatePluginService(TemplatePlugin plugin, ILogger logger)
    {
        _plugin = plugin ?? throw new ArgumentNullException(nameof(plugin));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        
        // TODO: When Core bridge is implemented, inject additional services:
        // _scriptEngine = scriptEngine ?? throw new ArgumentNullException(nameof(scriptEngine));
        // _dataTypeService = dataTypeService ?? throw new ArgumentNullException(nameof(dataTypeService));
        // _performanceCounters = performanceCounters ?? throw new ArgumentNullException(nameof(performanceCounters));
    }

    // === Lifecycle Management ===

    /// <inheritdoc />
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Initializing Template Plugin Service");

        _config = _plugin.Configuration as TemplatePluginConfiguration;
        if (_config == null)
        {
            throw new InvalidOperationException("Plugin must be initialized with TemplatePluginConfiguration");
        }

        _logger.LogDebug(
            "Template Plugin Service initialized. DataType: {DataType}, RowCount: {RowCount}",
            _config.DataType,
            _config.RowCount);

        await Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Starting Template Plugin Service");

        if (_config == null)
            throw new InvalidOperationException("Service must be initialized before starting");

        // TODO: When Core bridge is available, start any background services
        // await _performanceCounters.StartAsync(cancellationToken);

        _logger.LogDebug("Template Plugin Service started successfully");
        await Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Stopping Template Plugin Service");

        // TODO: When Core bridge is available, stop background services
        // await _performanceCounters.StopAsync(cancellationToken);

        _logger.LogDebug("Template Plugin Service stopped successfully");
        await Task.CompletedTask;
    }

    // === Core Business Logic with ArrayRow Optimization ===

    /// <inheritdoc />
    public async Task<IArrayRow> ProcessRowAsync(IArrayRow row, CancellationToken cancellationToken = default)
    {
        if (_config == null)
            throw new InvalidOperationException("Service not initialized");

        var startTime = DateTime.UtcNow;

        try
        {
            // PHASE 3 CRITICAL PATTERN: Pre-calculate field indexes for O(1) access
            var outputSchema = _config.OutputSchema;
            if (outputSchema == null)
            {
                throw new InvalidOperationException("Output schema not configured");
            }

            // ✅ CORRECT: Get field indexes once, use for all rows
            var idIndex = _config.GetOutputFieldIndex("Id");
            var nameIndex = _config.GetOutputFieldIndex("Name");
            var valueIndex = _config.GetOutputFieldIndex("Value");
            var timestampIndex = _config.GetOutputFieldIndex("Timestamp");

            // Generate data based on configured type
            var (name, value) = GenerateDataByType(_config.DataType);

            // ✅ CORRECT: Create output values using pre-calculated indexes
            var outputValues = new object?[outputSchema.Columns.Length];
            outputValues[idIndex] = GenerateId();
            outputValues[nameIndex] = name;
            outputValues[valueIndex] = value;
            outputValues[timestampIndex] = DateTime.UtcNow;

            // TODO: Need Core bridge to create ArrayRow - this is a key integration point
            // var outputRow = new ArrayRow(outputSchema, outputValues);
            
            // For now, return input row as placeholder
            var outputRow = row;

            // Update metrics
            lock (_metricsLock)
            {
                _rowsProcessed++;
            }

            // TODO: When Core bridge is available, record performance metrics
            // var processingTime = DateTime.UtcNow - startTime;
            // _performanceCounters.RecordProcessingTime(processingTime);

            _logger.LogTrace("Row processed in {ProcessingTime}μs", (DateTime.UtcNow - startTime).TotalMicroseconds);

            return outputRow;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing row");
            throw;
        }

        await Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task<ServiceHealth> CheckHealthAsync(CancellationToken cancellationToken = default)
    {
        var isHealthy = _config != null;
        var status = isHealthy ? HealthStatus.Healthy : HealthStatus.Unhealthy;
        var message = isHealthy ? "Service is operational" : "Service not properly initialized";

        return new ServiceHealth
        {
            IsHealthy = isHealthy,
            Status = status,
            Message = message,
            CheckedAt = DateTimeOffset.UtcNow
        };
    }

    // === Business Logic Methods ===

    /// <summary>
    /// Generates data based on the configured data type.
    /// Demonstrates type-specific business logic.
    /// </summary>
    private (string name, decimal value) GenerateDataByType(string dataType)
    {
        return dataType.ToLowerInvariant() switch
        {
            "customer" => GenerateCustomerData(),
            "product" => GenerateProductData(),
            "order" => GenerateOrderData(),
            "generic" => GenerateGenericData(),
            _ => throw new ArgumentException($"Unsupported data type: {dataType}")
        };
    }

    private (string name, decimal value) GenerateCustomerData()
    {
        var name = SampleNames[_random.Next(SampleNames.Length)];
        var salary = (decimal)(_random.NextDouble() * 100000 + 30000); // $30K-$130K
        return (name, Math.Round(salary, 2));
    }

    private (string name, decimal value) GenerateProductData()
    {
        var category = SampleCategories[_random.Next(SampleCategories.Length)];
        var price = (decimal)(_random.NextDouble() * 500 + 10); // $10-$510
        return (category, Math.Round(price, 2));
    }

    private (string name, decimal value) GenerateOrderData()
    {
        var orderType = _random.Next(2) == 0 ? "Online" : "InStore";
        var amount = (decimal)(_random.NextDouble() * 1000 + 50); // $50-$1050
        return (orderType, Math.Round(amount, 2));
    }

    private (string name, decimal value) GenerateGenericData()
    {
        var name = $"Item_{_random.Next(1000, 9999)}";
        var value = (decimal)(_random.NextDouble() * 1000);
        return (name, Math.Round(value, 2));
    }

    private int GenerateId()
    {
        // In real implementation, this might use a distributed ID generator service
        return _random.Next(10000, 99999);
    }

    // === Performance Optimization Helpers ===

    /// <summary>
    /// Demonstrates batch processing optimization for high-throughput scenarios.
    /// Target: Process 200K+ rows per second.
    /// </summary>
    public async Task<IEnumerable<IArrayRow>> ProcessRowBatchAsync(
        IEnumerable<IArrayRow> rows, 
        CancellationToken cancellationToken = default)
    {
        if (_config == null)
            throw new InvalidOperationException("Service not initialized");

        var outputSchema = _config.OutputSchema;
        if (outputSchema == null)
            throw new InvalidOperationException("Output schema not configured");

        // ✅ PERFORMANCE OPTIMIZATION: Pre-calculate indexes once for entire batch
        var idIndex = _config.GetOutputFieldIndex("Id");
        var nameIndex = _config.GetOutputFieldIndex("Name");
        var valueIndex = _config.GetOutputFieldIndex("Value");
        var timestampIndex = _config.GetOutputFieldIndex("Timestamp");

        var processedRows = new List<IArrayRow>();
        var batchStartTime = DateTime.UtcNow;

        foreach (var row in rows)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // ✅ PERFORMANCE: No field lookups in the processing loop
            var (name, value) = GenerateDataByType(_config.DataType);
            
            var outputValues = new object?[outputSchema.Columns.Length];
            outputValues[idIndex] = GenerateId();        // O(1) access
            outputValues[nameIndex] = name;              // O(1) access
            outputValues[valueIndex] = value;            // O(1) access
            outputValues[timestampIndex] = DateTime.UtcNow; // O(1) access

            // TODO: Need Core bridge to create ArrayRow
            // var outputRow = new ArrayRow(outputSchema, outputValues);
            var outputRow = row; // Placeholder

            processedRows.Add(outputRow);
        }

        var batchTime = DateTime.UtcNow - batchStartTime;
        var throughput = processedRows.Count / batchTime.TotalSeconds;

        _logger.LogDebug(
            "Batch processed: {RowCount} rows in {BatchTime}ms, throughput: {Throughput:F0} rows/sec",
            processedRows.Count,
            batchTime.TotalMilliseconds,
            throughput);

        // Performance target validation
        if (throughput < 200_000) // 200K rows/sec target
        {
            _logger.LogWarning(
                "Performance below target: {Throughput:F0} rows/sec (target: 200K+ rows/sec)",
                throughput);
        }

        lock (_metricsLock)
        {
            _rowsProcessed += processedRows.Count;
        }

        return processedRows;
    }

    /// <summary>
    /// Gets current service metrics including performance data
    /// </summary>
    public ServiceMetrics GetServiceMetrics()
    {
        lock (_metricsLock)
        {
            return new ServiceMetrics
            {
                ProcessedRows = _rowsProcessed,
                ErrorCount = 0, // Would track actual errors
                ProcessingRate = 0.0, // Would calculate based on timing data
                AverageProcessingTime = TimeSpan.Zero, // Would calculate from samples
                MemoryUsage = GC.GetTotalMemory(false),
                LastUpdated = DateTimeOffset.UtcNow
            };
        }
    }

    // === IDisposable Implementation ===

    /// <inheritdoc />
    public void Dispose()
    {
        _logger.LogDebug("Disposing Template Plugin Service");

        // TODO: When Core bridge is available, dispose injected services
        // _performanceCounters?.Dispose();

        _logger.LogDebug("Template Plugin Service disposed successfully");
    }
}