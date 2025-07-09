# Plugin Development Guide

**Create High-Performance Plugins for FlowEngine**

This guide covers everything you need to know about developing plugins for FlowEngine, from basic concepts to advanced optimization techniques.

## 🎯 Overview

FlowEngine uses a **thin plugin calling Core services** architecture that provides:

- **High Performance**: 200K+ rows/sec throughput capability
- **Clean Architecture**: Plugins stay simple, Core services handle complexity
- **Type Safety**: Schema-aware data processing
- **Extensibility**: Easy to add new data sources and transformations

### Architecture Pattern

```
┌─────────────────────────────────────────────────────────────────────┐
│                        FlowEngine.Core                              │
│                                                                     │
│  ┌─────────────────────┐  ┌─────────────────────────────────────┐  │
│  │ Business Services   │  │ Infrastructure Services             │  │
│  │                     │  │                                     │  │
│  │ • ScriptEngine      │  │ • ArrayRowFactory                   │  │
│  │ • ContextService    │  │ • SchemaFactory                     │  │
│  │ • ValidationService │  │ • ChunkFactory                      │  │
│  │ • EnrichmentService │  │ • MemoryManager                     │  │
│  └─────────────────────┘  └─────────────────────────────────────┘  │
└─────────────────────────────────────────────────────────────────────┘
           ▲                                 ▲
           │                                 │
   ┌───────────────────────────────────────────────────────────────────┐
   │                    Thin Plugins                                   │
   │                                                                   │
   │  ┌─────────────────┐  ┌─────────────────┐  ┌─────────────────┐  │
   │  │ DelimitedSource │  │ JavaScript      │  │ DelimitedSink   │  │
   │  │                 │  │ Transform       │  │                 │  │
   │  │ • Configuration │  │ • Configuration │  │ • Configuration │  │
   │  │ • Orchestration │  │ • Orchestration │  │ • Orchestration │  │
   │  │ • Data Flow     │  │ • Data Flow     │  │ • Data Flow     │  │
   │  └─────────────────┘  └─────────────────┘  └─────────────────┘  │
   └───────────────────────────────────────────────────────────────────┘
```

## 🏗️ Plugin Types

### 1. Source Plugins (ISourcePlugin)

**Purpose**: Generate or read data into the pipeline

```csharp
public interface ISourcePlugin : IPlugin
{
    IAsyncEnumerable<IChunk> ProduceAsync(CancellationToken cancellationToken = default);
    long? EstimatedRowCount { get; }
    bool IsSeekable { get; }
    SourcePosition? CurrentPosition { get; }
    Task SeekAsync(SourcePosition position, CancellationToken cancellationToken = default);
}
```

**Example Implementation**:
```csharp
public class MySourcePlugin : PluginBase, ISourcePlugin
{
    private readonly IDatasetFactory _datasetFactory;
    private readonly IArrayRowFactory _arrayRowFactory;
    private readonly IChunkFactory _chunkFactory;
    
    public MySourcePlugin(
        IDatasetFactory datasetFactory,
        IArrayRowFactory arrayRowFactory,
        IChunkFactory chunkFactory,
        ILogger<MySourcePlugin> logger)
        : base(logger)
    {
        _datasetFactory = datasetFactory;
        _arrayRowFactory = arrayRowFactory;
        _chunkFactory = chunkFactory;
    }
    
    public async IAsyncEnumerable<IChunk> ProduceAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var batch in ReadDataBatchesAsync(cancellationToken))
        {
            var rows = new List<IArrayRow>();
            
            foreach (var record in batch)
            {
                var row = _arrayRowFactory.CreateRow(OutputSchema, record);
                rows.Add(row);
            }
            
            yield return _chunkFactory.CreateChunk(OutputSchema, rows);
        }
    }
}
```

### 2. Transform Plugins (ITransformPlugin)

**Purpose**: Process and modify data flowing through the pipeline

```csharp
public interface ITransformPlugin : IPlugin
{
    ISchema InputSchema { get; }
    IAsyncEnumerable<IChunk> TransformAsync(
        IAsyncEnumerable<IChunk> input,
        CancellationToken cancellationToken = default);
    bool IsStateful { get; }
    TransformMode Mode { get; }
    IAsyncEnumerable<IChunk> FlushAsync(CancellationToken cancellationToken = default);
}
```

**Example Implementation**:
```csharp
public class MyTransformPlugin : PluginBase, ITransformPlugin
{
    private readonly IBusinessService _businessService;
    private readonly IChunkFactory _chunkFactory;
    
    public MyTransformPlugin(
        IBusinessService businessService,
        IChunkFactory chunkFactory,
        ILogger<MyTransformPlugin> logger)
        : base(logger)
    {
        _businessService = businessService;
        _chunkFactory = chunkFactory;
    }
    
    public async IAsyncEnumerable<IChunk> TransformAsync(
        IAsyncEnumerable<IChunk> input,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var chunk in input.WithCancellation(cancellationToken))
        {
            var transformedRows = new List<IArrayRow>();
            
            foreach (var row in chunk.Rows)
            {
                // Use Core service for complex business logic
                var result = await _businessService.ProcessRowAsync(row);
                transformedRows.Add(result);
            }
            
            yield return _chunkFactory.CreateChunk(OutputSchema, transformedRows);
        }
    }
}
```

### 3. Sink Plugins (ISinkPlugin)

**Purpose**: Consume and write data from the pipeline

```csharp
public interface ISinkPlugin : IPlugin
{
    ISchema InputSchema { get; }
    Task ConsumeAsync(
        IAsyncEnumerable<IChunk> input,
        CancellationToken cancellationToken = default);
    bool SupportsTransactions { get; }
    WriteMode Mode { get; }
    Task FlushAsync(CancellationToken cancellationToken = default);
}
```

**Example Implementation**:
```csharp
public class MySinkPlugin : PluginBase, ISinkPlugin
{
    private readonly IOutputService _outputService;
    
    public MySinkPlugin(
        IOutputService outputService,
        ILogger<MySinkPlugin> logger)
        : base(logger)
    {
        _outputService = outputService;
    }
    
    public async Task ConsumeAsync(
        IAsyncEnumerable<IChunk> input,
        CancellationToken cancellationToken = default)
    {
        await foreach (var chunk in input.WithCancellation(cancellationToken))
        {
            // Use Core service for output processing
            await _outputService.WriteChunkAsync(chunk, cancellationToken);
        }
    }
}
```

## 🚀 Getting Started

### 1. Create Plugin Project

```bash
# Create new plugin project
dotnet new classlib -n MyPlugin -o plugins/MyPlugin/
cd plugins/MyPlugin/

# Add FlowEngine dependencies
dotnet add package FlowEngine.Abstractions
dotnet add package Microsoft.Extensions.DependencyInjection.Abstractions
dotnet add package Microsoft.Extensions.Logging.Abstractions
```

### 2. Create Plugin Manifest

Create `plugin.json` in your plugin directory:

```json
{
  "name": "MyPlugin",
  "version": "1.0.0",
  "description": "My custom plugin",
  "author": "Your Name",
  "pluginType": "Transform",
  "assembly": "MyPlugin.dll",
  "entryPoint": "MyPlugin.MyTransformPlugin",
  "configuration": {
    "schema": {
      "type": "object",
      "properties": {
        "setting1": {
          "type": "string",
          "description": "Example setting"
        }
      },
      "required": ["setting1"]
    }
  }
}
```

### 3. Implement Plugin Base

```csharp
using FlowEngine.Abstractions.Data;
using FlowEngine.Abstractions.Plugins;
using Microsoft.Extensions.Logging;

namespace MyPlugin;

public class MyTransformPlugin : PluginBase, ITransformPlugin
{
    private readonly ILogger<MyTransformPlugin> _logger;
    private MyPluginConfiguration? _configuration;
    
    public MyTransformPlugin(ILogger<MyTransformPlugin> logger)
        : base(logger)
    {
        _logger = logger;
    }
    
    // Plugin metadata
    public override string Name => "MyTransform";
    public override string Version => "1.0.0";
    public override string Description => "Custom transform plugin";
    public override string Author => "Your Name";
    public override string PluginType => "Transform";
    
    // Transform-specific properties
    public ISchema InputSchema => _inputSchema ?? throw new InvalidOperationException("Plugin not initialized");
    public override ISchema? OutputSchema => _outputSchema;
    public bool IsStateful => false;
    public TransformMode Mode => TransformMode.OneToOne;
    
    // Plugin lifecycle
    protected override async Task<PluginInitializationResult> InitializeInternalAsync(
        IPluginConfiguration configuration, 
        CancellationToken cancellationToken)
    {
        _configuration = (MyPluginConfiguration)configuration;
        
        // Initialize plugin-specific logic
        await InitializePluginAsync();
        
        return PluginInitializationResult.Success();
    }
    
    protected override async Task StartInternalAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Starting {PluginName}", Name);
        await Task.CompletedTask;
    }
    
    protected override async Task StopInternalAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Stopping {PluginName}", Name);
        await Task.CompletedTask;
    }
    
    // Core functionality
    public async IAsyncEnumerable<IChunk> TransformAsync(
        IAsyncEnumerable<IChunk> input,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var chunk in input.WithCancellation(cancellationToken))
        {
            var transformedChunk = await TransformChunkAsync(chunk, cancellationToken);
            yield return transformedChunk;
        }
    }
    
    public async IAsyncEnumerable<IChunk> FlushAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        // Stateless plugin - nothing to flush
        yield break;
    }
    
    // Private implementation
    private async Task<IChunk> TransformChunkAsync(IChunk chunk, CancellationToken cancellationToken)
    {
        // Your transformation logic here
        return chunk;
    }
}
```

## ⚙️ Configuration System

### Configuration Class

```csharp
public class MyPluginConfiguration : IPluginConfiguration
{
    public string Setting1 { get; set; } = string.Empty;
    public int BatchSize { get; set; } = 1000;
    public bool EnableFeature { get; set; } = true;
    
    public bool IsValid()
    {
        return !string.IsNullOrEmpty(Setting1) && BatchSize > 0;
    }
    
    public IEnumerable<string> GetValidationErrors()
    {
        var errors = new List<string>();
        
        if (string.IsNullOrEmpty(Setting1))
            errors.Add("Setting1 is required");
            
        if (BatchSize <= 0)
            errors.Add("BatchSize must be greater than 0");
            
        return errors;
    }
}
```

### Configuration Usage

```yaml
# In pipeline configuration
- id: transform
  type: my-transform
  config:
    setting1: "example value"
    batchSize: 5000
    enableFeature: true
```

## 🔧 Using Core Services

### Dependency Injection

```csharp
public class MyTransformPlugin : PluginBase, ITransformPlugin
{
    private readonly IArrayRowFactory _arrayRowFactory;
    private readonly ISchemaFactory _schemaFactory;
    private readonly IChunkFactory _chunkFactory;
    private readonly IMyBusinessService _businessService;
    
    public MyTransformPlugin(
        IArrayRowFactory arrayRowFactory,
        ISchemaFactory schemaFactory,
        IChunkFactory chunkFactory,
        IMyBusinessService businessService,
        ILogger<MyTransformPlugin> logger)
        : base(logger)
    {
        _arrayRowFactory = arrayRowFactory;
        _schemaFactory = schemaFactory;
        _chunkFactory = chunkFactory;
        _businessService = businessService;
    }
}
```

### Core Service Registration

```csharp
// In your plugin assembly
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddMyPlugin(this IServiceCollection services)
    {
        services.AddTransient<IMyBusinessService, MyBusinessService>();
        services.AddTransient<MyTransformPlugin>();
        
        return services;
    }
}
```

### Using Core Services

```csharp
public async IAsyncEnumerable<IChunk> TransformAsync(
    IAsyncEnumerable<IChunk> input,
    [EnumeratorCancellation] CancellationToken cancellationToken = default)
{
    await foreach (var chunk in input.WithCancellation(cancellationToken))
    {
        var transformedRows = new List<IArrayRow>();
        
        foreach (var row in chunk.Rows)
        {
            // Use Core service for complex logic
            var result = await _businessService.ProcessAsync(row);
            
            // Use Core factory to create new row
            var newRow = _arrayRowFactory.CreateRow(OutputSchema, result);
            transformedRows.Add(newRow);
        }
        
        // Use Core factory to create chunk
        var newChunk = _chunkFactory.CreateChunk(OutputSchema, transformedRows);
        yield return newChunk;
    }
}
```

## 🚀 Performance Optimization

### ArrayRow Optimization

```csharp
public class HighPerformanceTransformPlugin : PluginBase, ITransformPlugin
{
    private int _idIndex;
    private int _nameIndex;
    private int _valueIndex;
    
    protected override async Task<PluginInitializationResult> InitializeInternalAsync(
        IPluginConfiguration configuration, 
        CancellationToken cancellationToken)
    {
        // Cache field indexes for O(1) access
        _idIndex = InputSchema.GetFieldIndex("id");
        _nameIndex = InputSchema.GetFieldIndex("name");
        _valueIndex = InputSchema.GetFieldIndex("value");
        
        return PluginInitializationResult.Success();
    }
    
    public async IAsyncEnumerable<IChunk> TransformAsync(
        IAsyncEnumerable<IChunk> input,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var chunk in input.WithCancellation(cancellationToken))
        {
            var rows = chunk.Rows;
            var transformedRows = new ArrayRow[rows.Length];
            
            // Use array indexing for maximum performance
            for (int i = 0; i < rows.Length; i++)
            {
                var row = rows[i];
                
                // Direct field access using cached indexes
                var id = row[_idIndex];
                var name = row[_nameIndex];
                var value = row[_valueIndex];
                
                // Create transformed row
                var transformedData = new object[] { id, name, ProcessValue(value) };
                transformedRows[i] = new ArrayRow(OutputSchema, transformedData);
            }
            
            yield return new Chunk(OutputSchema, transformedRows);
        }
    }
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private object ProcessValue(object value)
    {
        // Inline simple transformations for performance
        return value;
    }
}
```

### Memory Management

```csharp
public class MemoryEfficientPlugin : PluginBase, ISourcePlugin
{
    private readonly ObjectPool<List<IArrayRow>> _listPool;
    
    public MemoryEfficientPlugin(
        ObjectPool<List<IArrayRow>> listPool,
        ILogger<MemoryEfficientPlugin> logger)
        : base(logger)
    {
        _listPool = listPool;
    }
    
    public async IAsyncEnumerable<IChunk> ProduceAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var batch in ReadDataAsync(cancellationToken))
        {
            var rows = _listPool.Get();
            try
            {
                foreach (var record in batch)
                {
                    var row = CreateRowFromRecord(record);
                    rows.Add(row);
                    
                    // Yield chunk when reaching optimal size
                    if (rows.Count >= ChunkSize)
                    {
                        yield return _chunkFactory.CreateChunk(OutputSchema, rows);
                        rows.Clear();
                    }
                }
                
                // Yield remaining rows
                if (rows.Count > 0)
                {
                    yield return _chunkFactory.CreateChunk(OutputSchema, rows);
                }
            }
            finally
            {
                rows.Clear();
                _listPool.Return(rows);
            }
        }
    }
}
```

## 🧪 Testing

### Unit Testing

```csharp
public class MyTransformPluginTests : IDisposable
{
    private readonly ServiceProvider _serviceProvider;
    
    public MyTransformPluginTests()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddFlowEngineCore();
        services.AddMyPlugin();
        
        _serviceProvider = services.BuildServiceProvider();
    }
    
    [Fact]
    public async Task TransformAsync_ShouldProcessData()
    {
        // Arrange
        var plugin = _serviceProvider.GetRequiredService<MyTransformPlugin>();
        var config = CreateTestConfiguration();
        
        await plugin.InitializeAsync(config);
        await plugin.StartAsync();
        
        var inputChunk = CreateTestChunk();
        var inputChunks = new[] { inputChunk }.ToAsyncEnumerable();
        
        // Act
        var results = new List<IChunk>();
        await foreach (var chunk in plugin.TransformAsync(inputChunks))
        {
            results.Add(chunk);
        }
        
        // Assert
        Assert.Single(results);
        var outputChunk = results[0];
        Assert.Equal(inputChunk.RowCount, outputChunk.RowCount);
        Assert.Equal(plugin.OutputSchema, outputChunk.Schema);
        
        await plugin.StopAsync();
    }
    
    [Fact]
    public async Task TransformAsync_WithInvalidData_ShouldHandle()
    {
        // Test error handling scenarios
    }
    
    [Fact]
    public async Task TransformAsync_Performance_ShouldMeetTargets()
    {
        // Performance testing
        const int rowCount = 50000;
        var plugin = _serviceProvider.GetRequiredService<MyTransformPlugin>();
        
        var stopwatch = Stopwatch.StartNew();
        var totalRows = 0;
        
        await foreach (var chunk in plugin.TransformAsync(CreateLargeTestData(rowCount)))
        {
            totalRows += chunk.RowCount;
        }
        
        stopwatch.Stop();
        
        var throughput = totalRows / stopwatch.Elapsed.TotalSeconds;
        Assert.True(throughput >= 10000, $"Throughput {throughput:F0} rows/sec below minimum");
    }
    
    public void Dispose()
    {
        _serviceProvider?.Dispose();
    }
}
```

### Integration Testing

```csharp
[Fact]
public async Task EndToEndPipeline_ShouldProcessSuccessfully()
{
    // Arrange
    var services = new ServiceCollection();
    services.AddFlowEngineCore();
    services.AddMyPlugin();
    
    var pipelineConfig = new PipelineConfiguration
    {
        Steps = new List<PipelineStep>
        {
            new() { Id = "source", Type = "test-source", Config = new Dictionary<string, object>() },
            new() { Id = "transform", Type = "my-transform", Config = new Dictionary<string, object>() },
            new() { Id = "sink", Type = "test-sink", Config = new Dictionary<string, object>() }
        },
        Connections = new List<PipelineConnection>
        {
            new() { From = "source", To = "transform" },
            new() { From = "transform", To = "sink" }
        }
    };
    
    // Act
    var result = await ExecutePipelineAsync(pipelineConfig);
    
    // Assert
    Assert.True(result.Success);
    Assert.True(result.RowsProcessed > 0);
}
```

## 🎯 Best Practices

### 1. Plugin Design

- **Keep plugins thin**: Delegate complex logic to Core services
- **Use dependency injection**: Leverage FlowEngine's DI container
- **Follow async patterns**: Use IAsyncEnumerable for streaming
- **Handle cancellation**: Respect cancellation tokens
- **Implement health checks**: Monitor plugin health

### 2. Performance

- **Cache field indexes**: Pre-calculate field access patterns
- **Use object pooling**: Reduce memory allocation
- **Batch processing**: Process data in optimal chunk sizes
- **Minimize allocations**: Reuse objects where possible
- **Profile regularly**: Use performance testing

### 3. Error Handling

- **Validate configuration**: Check settings at initialization
- **Handle data errors**: Gracefully handle invalid data
- **Log appropriately**: Use structured logging
- **Provide context**: Include useful error information
- **Fail fast**: Don't continue with invalid state

### 4. Testing

- **Unit test thoroughly**: Test individual components
- **Integration test**: Test plugin in pipeline
- **Performance test**: Validate throughput targets
- **Error scenario testing**: Test failure cases
- **Use test doubles**: Mock external dependencies

## 📚 Advanced Topics

### Custom Core Services

```csharp
// Define service interface
public interface IMyBusinessService
{
    Task<object[]> ProcessRowAsync(IArrayRow row);
}

// Implement service
public class MyBusinessService : IMyBusinessService
{
    private readonly ILogger<MyBusinessService> _logger;
    
    public MyBusinessService(ILogger<MyBusinessService> logger)
    {
        _logger = logger;
    }
    
    public async Task<object[]> ProcessRowAsync(IArrayRow row)
    {
        // Complex business logic here
        await Task.Delay(1); // Simulate async work
        return new object[] { /* transformed data */ };
    }
}

// Register service
services.AddSingleton<IMyBusinessService, MyBusinessService>();
```

### Plugin Lifecycle Management

```csharp
public class AdvancedPlugin : PluginBase, ITransformPlugin
{
    protected override async Task<PluginInitializationResult> InitializeInternalAsync(
        IPluginConfiguration configuration, 
        CancellationToken cancellationToken)
    {
        try
        {
            // Initialize plugin resources
            await InitializeResourcesAsync();
            
            // Validate configuration
            ValidateConfiguration();
            
            // Set up monitoring
            SetupMonitoring();
            
            return PluginInitializationResult.Success();
        }
        catch (Exception ex)
        {
            return PluginInitializationResult.Failure(ex.Message);
        }
    }
    
    protected override async Task StartInternalAsync(CancellationToken cancellationToken)
    {
        // Start background services
        await StartBackgroundServicesAsync();
        
        // Log startup
        _logger.LogInformation("Plugin {Name} started successfully", Name);
    }
    
    protected override async Task StopInternalAsync(CancellationToken cancellationToken)
    {
        // Stop background services
        await StopBackgroundServicesAsync();
        
        // Clean up resources
        await CleanupResourcesAsync();
        
        _logger.LogInformation("Plugin {Name} stopped", Name);
    }
    
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            // Dispose managed resources
            DisposeResources();
        }
        
        base.Dispose(disposing);
    }
}
```

### Health Monitoring

```csharp
protected override async Task<IEnumerable<HealthCheck>> PerformAdditionalHealthChecksAsync(
    CancellationToken cancellationToken)
{
    var healthChecks = new List<HealthCheck>();
    
    // Check performance
    if (_performanceMetrics.ThroughputRpm < MinimumThroughput)
    {
        healthChecks.Add(new HealthCheck
        {
            Name = "Performance",
            Passed = false,
            Details = $"Throughput {_performanceMetrics.ThroughputRpm:F0} below minimum {MinimumThroughput}",
            Severity = HealthCheckSeverity.Warning
        });
    }
    
    // Check memory usage
    var memoryUsage = GC.GetTotalMemory(false);
    if (memoryUsage > MaxMemoryUsage)
    {
        healthChecks.Add(new HealthCheck
        {
            Name = "Memory",
            Passed = false,
            Details = $"Memory usage {memoryUsage / 1024 / 1024:F1}MB exceeds limit",
            Severity = HealthCheckSeverity.Critical
        });
    }
    
    // Check external dependencies
    if (await CheckExternalDependencyAsync(cancellationToken))
    {
        healthChecks.Add(new HealthCheck
        {
            Name = "External Dependency",
            Passed = true,
            Details = "All dependencies healthy",
            Severity = HealthCheckSeverity.Info
        });
    }
    
    return healthChecks;
}
```

---

This guide provides a comprehensive foundation for developing high-performance plugins for FlowEngine. The thin plugin architecture ensures that your plugins remain simple and focused while leveraging the powerful Core services for complex operations.