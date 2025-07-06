# FlowEngine Plugin Developer Guide

**Version**: 1.0.0  
**Target Framework**: .NET 8.0  
**Last Updated**: July 6, 2025

---

## 🎯 Overview

FlowEngine is a high-performance data processing pipeline framework designed for 200K+ rows/sec throughput. This guide covers everything you need to know about developing plugins for the FlowEngine ecosystem.

### What You'll Learn

- [Plugin Architecture & Types](#plugin-architecture--types)
- [Development Workflow](#development-workflow)
- [CLI Development Tools](#cli-development-tools)
- [Plugin Configuration & Validation](#plugin-configuration--validation)
- [Performance Optimization](#performance-optimization)
- [Testing & Debugging](#testing--debugging)
- [Deployment & Publishing](#deployment--publishing)

---

## 🏗️ Plugin Architecture & Types

### Core Concepts

FlowEngine plugins are .NET assemblies that implement specific interfaces to participate in data processing pipelines. All plugins share a common base contract through the `IPlugin` interface and extend it for specific behaviors.

```csharp
// Base plugin interface - all plugins implement this
public interface IPlugin
{
    string Id { get; }
    string Name { get; }
    string Version { get; }
    string Description { get; }
    string Category { get; }
    
    Task InitializeAsync(IPluginConfiguration configuration);
    ISchema GetOutputSchema();
    Task<PluginHealthStatus> CheckHealthAsync();
}
```

### Plugin Types

FlowEngine supports three primary plugin types, each optimized for specific pipeline roles:

#### 1. Source Plugins (`ISourcePlugin`)

**Purpose**: Generate or read data into the pipeline  
**Examples**: File readers, database sources, web APIs, message queues  
**Performance Target**: 200K+ rows/sec sustained throughput

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

**Key Design Principles**:
- Use `IAsyncEnumerable<IChunk>` for memory-efficient streaming
- Implement chunking to maintain bounded memory usage
- Support cancellation for graceful shutdown
- Consider seekability for fault tolerance

#### 2. Transform Plugins (`ITransformPlugin`)

**Purpose**: Process and modify data flowing through the pipeline  
**Examples**: Data cleaning, aggregation, enrichment, format conversion  
**Performance Target**: <50ns per field access using ArrayRow optimization

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

**Transform Modes**:
- `OneToOne`: Each input chunk produces exactly one output chunk (most efficient)
- `OneToMany`: Input chunks may produce multiple outputs (expansion scenarios)
- `ManyToOne`: Multiple inputs produce one output (aggregation scenarios)
- `ManyToMany`: Complex relationships (windowing, complex joins)
- `Filter`: Produces subset of input chunks (filtering, sampling)

#### 3. Sink Plugins (`ISinkPlugin`)

**Purpose**: Consume and write data from the pipeline  
**Examples**: File writers, database sinks, web APIs, monitoring systems  
**Performance Target**: Handle backpressure appropriately for sustained throughput

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

**Write Modes**:
- `Streaming`: Immediate write (lowest latency)
- `Buffered`: Batched writes (highest throughput)
- `Transactional`: ACID compliance (consistency)
- `AppendOnly`: Optimized for append scenarios

---

## 🚀 Development Workflow

### 1. Project Setup

Use the FlowEngine CLI to create a new plugin project:

```bash
# Create a new source plugin
flowengine plugin create MyDataReader --template source

# Create a transform plugin
flowengine plugin create MyDataTransformer --template transform

# Create a sink plugin  
flowengine plugin create MyDataWriter --template destination
```

This generates a complete project structure:

```
MyDataReader/
├── MyDataReader.csproj     # Project file with FlowEngine dependencies
├── MyDataReader.cs         # Main plugin implementation
├── plugin.json            # Plugin manifest for discovery
└── README.md              # Development instructions
```

### 2. Plugin Manifest (plugin.json)

Every plugin must include a `plugin.json` manifest for discovery and validation:

```json
{
  "name": "MyDataReader",
  "version": "1.0.0",
  "description": "Reads data from custom source",
  "category": "source",
  "author": "Your Name",
  "dependencies": [
    {
      "name": "FlowEngine.Abstractions",
      "version": ">=1.0.0"
    }
  ],
  "configuration": {
    "schema": {
      "type": "object",
      "properties": {
        "connectionString": {
          "type": "string",
          "description": "Connection string for data source"
        },
        "batchSize": {
          "type": "integer",
          "default": 1000,
          "minimum": 1,
          "maximum": 10000,
          "description": "Number of rows to process in each batch"
        }
      },
      "required": ["connectionString"]
    }
  },
  "performance": {
    "targetThroughput": "50000 rows/sec",
    "memoryUsage": "low"
  }
}
```

### 3. Implementation Template

Here's a complete source plugin implementation:

```csharp
using FlowEngine.Abstractions.Data;
using FlowEngine.Abstractions.Plugins;

namespace MyDataReader;

/// <summary>
/// Source plugin that reads data from a custom source.
/// </summary>
public sealed class MyDataReader : PluginBase, ISourcePlugin
{
    private ISchema? _outputSchema;
    private string? _connectionString;
    private int _batchSize;

    /// <summary>
    /// Initializes a new instance of MyDataReader.
    /// </summary>
    public MyDataReader()
    {
        Id = "mydatareader";
        Name = "MyDataReader";
        Version = "1.0.0";
        Description = "Reads data from custom source";
        Category = "source";
    }

    /// <inheritdoc />
    public override async Task InitializeAsync(IPluginConfiguration configuration)
    {
        await base.InitializeAsync(configuration);
        
        // Get configuration values with validation
        _connectionString = configuration.GetProperty<string>("connectionString");
        _batchSize = configuration.TryGetProperty<int>("batchSize", out var batchSize) ? batchSize : 1000;
        
        // Define output schema
        _outputSchema = CreateOutputSchema();
        
        // Validate connection
        await ValidateConnectionAsync();
    }

    /// <inheritdoc />
    public override ISchema GetOutputSchema()
    {
        return _outputSchema ?? throw new InvalidOperationException("Plugin not initialized");
    }

    /// <inheritdoc />
    public long? EstimatedRowCount => null; // Unknown count for this source

    /// <inheritdoc />
    public bool IsSeekable => false; // This source doesn't support seeking

    /// <inheritdoc />
    public SourcePosition? CurrentPosition => null;

    /// <inheritdoc />
    public Task SeekAsync(SourcePosition position, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("This source does not support seeking");
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<IChunk> ProduceAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var schema = GetOutputSchema();
        var rowsProduced = 0;
        
        await foreach (var batch in ReadDataBatchesAsync(cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            
            var rows = new List<IArrayRow>();
            foreach (var record in batch)
            {
                var values = ExtractValuesFromRecord(record);
                var row = ArrayRowFactory.CreateRow(schema, values);
                rows.Add(row);
            }
            
            if (rows.Count > 0)
            {
                var chunk = ChunkFactory.CreateChunk(rows);
                yield return chunk;
                rowsProduced += rows.Count;
            }
        }
        
        Logger?.LogInformation("Produced {RowCount} rows", rowsProduced);
    }

    private static ISchema CreateOutputSchema()
    {
        var columns = new[]
        {
            new ColumnDefinition { Name = "Id", DataType = typeof(long), IsNullable = false },
            new ColumnDefinition { Name = "Name", DataType = typeof(string), IsNullable = false },
            new ColumnDefinition { Name = "Timestamp", DataType = typeof(DateTime), IsNullable = false },
            new ColumnDefinition { Name = "Value", DataType = typeof(decimal), IsNullable = true }
        };
        
        return Schema.GetOrCreate(columns);
    }

    private async Task ValidateConnectionAsync()
    {
        // TODO: Implement connection validation logic
        await Task.CompletedTask;
    }

    private async IAsyncEnumerable<IEnumerable<object>> ReadDataBatchesAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        // TODO: Implement actual data reading logic
        // This is a placeholder that generates sample data
        
        for (int batch = 0; batch < 10; batch++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            
            var batchData = Enumerable.Range(batch * _batchSize, _batchSize)
                .Select(i => new object[] { i, $"Record {i}", DateTime.UtcNow, (decimal)(i * 1.5) });
            
            yield return batchData;
            
            await Task.Delay(100, cancellationToken); // Simulate processing time
        }
    }

    private static object?[] ExtractValuesFromRecord(object record)
    {
        // TODO: Implement record value extraction logic
        return (object?[])record;
    }
}
```

---

## 🛠️ CLI Development Tools

FlowEngine provides comprehensive CLI tools for plugin development, testing, and performance validation.

### Plugin Creation

```bash
# Create different plugin types
flowengine plugin create MySource --template source
flowengine plugin create MyTransform --template transform  
flowengine plugin create MySink --template destination

# Advanced options
flowengine plugin create MyPlugin ./custom/path --template transform --verbose
```

### Plugin Validation

```bash
# Validate plugin structure and configuration
flowengine plugin validate ./MyPlugin

# Verbose validation with detailed output
flowengine plugin validate ./MyPlugin --verbose

# Validate specific aspects
flowengine plugin validate ./MyPlugin --check-manifest --check-dependencies
```

The validator checks:
- ✅ Plugin manifest syntax and schema compliance
- ✅ Assembly structure and plugin discovery
- ✅ Dependency version compatibility
- ✅ Configuration schema validation
- ✅ Interface implementation completeness

### Plugin Testing

```bash
# Run functional tests with sample data
flowengine plugin test ./MyPlugin

# Control test iterations
flowengine plugin test ./MyPlugin --count 100

# Verbose testing with detailed output
flowengine plugin test ./MyPlugin --verbose
```

Test scenarios include:
- **Initialization**: Plugin configuration and setup
- **Data Processing**: Functional operation with sample data
- **Error Handling**: Response to invalid inputs and edge cases
- **Resource Cleanup**: Proper disposal and resource management

### Performance Benchmarking

```bash
# Run comprehensive performance benchmarks
flowengine plugin benchmark ./MyPlugin

# Quick benchmark for development
flowengine plugin benchmark ./MyPlugin --quick

# Verbose benchmarking with memory analysis
flowengine plugin benchmark ./MyPlugin --verbose
```

Benchmark metrics:
- **Throughput**: Rows processed per second
- **Latency**: Average processing time per iteration
- **Memory Usage**: Allocation patterns and efficiency
- **Resource Utilization**: CPU and I/O characteristics

Performance targets:
- 🚀 **Excellent**: 200K+ rows/sec (exceeds target)
- 👍 **Good**: 100K-200K rows/sec 
- ⚡ **Moderate**: 50K-100K rows/sec
- 🐌 **Needs Optimization**: <50K rows/sec

---

## ⚙️ Plugin Configuration & Validation

### Configuration System

FlowEngine uses a strongly-typed configuration system with JSON Schema validation:

```csharp
public interface IPluginConfiguration
{
    string PluginId { get; }
    string Name { get; }
    string Version { get; }
    string PluginType { get; }
    
    ISchema? InputSchema { get; }
    ISchema? OutputSchema { get; }
    
    // Performance optimization - pre-calculated field indexes
    ImmutableDictionary<string, int> InputFieldIndexes { get; }
    ImmutableDictionary<string, int> OutputFieldIndexes { get; }
    
    ImmutableDictionary<string, object> Properties { get; }
    
    T GetProperty<T>(string key);
    bool TryGetProperty<T>(string key, out T? value);
    bool IsCompatibleWith(ISchema inputSchema);
}
```

### JSON Schema Validation

Configure validation schemas in `plugin.json`:

```json
{
  "configuration": {
    "schema": {
      "type": "object",
      "properties": {
        "connectionString": {
          "type": "string",
          "pattern": "^[^;]+;[^;]+$",
          "description": "Database connection string",
          "examples": ["Server=localhost;Database=test"]
        },
        "batchSize": {
          "type": "integer",
          "minimum": 1,
          "maximum": 10000,
          "default": 1000,
          "description": "Batch size for processing"
        },
        "enableRetries": {
          "type": "boolean",
          "default": true,
          "description": "Enable automatic retry logic"
        },
        "timeout": {
          "type": "number",
          "minimum": 0.1,
          "maximum": 300.0,
          "default": 30.0,
          "description": "Timeout in seconds"
        },
        "tags": {
          "type": "array",
          "items": { "type": "string" },
          "uniqueItems": true,
          "description": "Processing tags"
        },
        "metadata": {
          "type": "object",
          "additionalProperties": { "type": "string" },
          "description": "Custom metadata"
        }
      },
      "required": ["connectionString"],
      "additionalProperties": false
    }
  }
}
```

### Configuration Access Patterns

```csharp
public override async Task InitializeAsync(IPluginConfiguration configuration)
{
    await base.InitializeAsync(configuration);
    
    // Required properties (throws if missing)
    var connectionString = configuration.GetProperty<string>("connectionString");
    
    // Optional properties with defaults
    var batchSize = configuration.TryGetProperty<int>("batchSize", out var size) ? size : 1000;
    var enableRetries = configuration.TryGetProperty<bool>("enableRetries", out var retries) ? retries : true;
    
    // Complex property types
    var tags = configuration.TryGetProperty<string[]>("tags", out var tagArray) ? tagArray : Array.Empty<string>();
    var metadata = configuration.TryGetProperty<Dictionary<string, string>>("metadata", out var meta) ? meta : new();
    
    // Validate business rules
    if (batchSize <= 0 || batchSize > 10000)
        throw new PluginConfigurationException("batchSize must be between 1 and 10000");
}
```

---

## 🏎️ Performance Optimization

### ArrayRow Optimization

FlowEngine uses ArrayRow for high-performance field access. Optimize using pre-calculated indexes:

```csharp
public class OptimizedTransformPlugin : PluginBase, ITransformPlugin
{
    private int _idIndex;
    private int _nameIndex;
    private int _valueIndex;
    
    public override async Task InitializeAsync(IPluginConfiguration configuration)
    {
        await base.InitializeAsync(configuration);
        
        // Cache field indexes for O(1) access
        _idIndex = configuration.GetInputFieldIndex("Id");
        _nameIndex = configuration.GetInputFieldIndex("Name");
        _valueIndex = configuration.GetInputFieldIndex("Value");
    }
    
    public async IAsyncEnumerable<IChunk> TransformAsync(
        IAsyncEnumerable<IChunk> input,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var chunk in input)
        {
            var transformedRows = new List<IArrayRow>();
            
            // Use Span<T> for high-performance iteration
            var rows = chunk.Rows;
            for (int i = 0; i < rows.Length; i++)
            {
                var row = rows[i];
                
                // Direct index access - <20ns per field
                var id = row.GetFieldValue<long>(_idIndex);
                var name = row.GetFieldValue<string>(_nameIndex);
                var value = row.GetFieldValue<decimal?>(_valueIndex);
                
                // Apply transformation
                var transformedValue = value * 1.1m; // 10% increase
                
                var transformedRow = ArrayRowFactory.CreateRow(
                    OutputSchema, 
                    new object?[] { id, name.ToUpper(), transformedValue });
                
                transformedRows.Add(transformedRow);
            }
            
            yield return ChunkFactory.CreateChunk(transformedRows);
        }
    }
}
```

### Memory Management

```csharp
public async IAsyncEnumerable<IChunk> ProduceAsync(
    [EnumeratorCancellation] CancellationToken cancellationToken = default)
{
    var schema = GetOutputSchema();
    
    await foreach (var batch in ReadDataBatchesAsync(cancellationToken))
    {
        cancellationToken.ThrowIfCancellationRequested();
        
        // Use object pooling for frequently allocated objects
        var rows = _rowPool.Get();
        try
        {
            foreach (var record in batch)
            {
                var row = ArrayRowFactory.CreateRow(schema, ExtractValues(record));
                rows.Add(row);
                
                // Bounded memory usage - yield chunks at optimal size
                if (rows.Count >= _batchSize)
                {
                    yield return ChunkFactory.CreateChunk(rows.ToArray());
                    rows.Clear();
                }
            }
            
            // Yield remaining rows
            if (rows.Count > 0)
            {
                yield return ChunkFactory.CreateChunk(rows.ToArray());
            }
        }
        finally
        {
            rows.Clear();
            _rowPool.Return(rows);
        }
    }
}
```

### Async Best Practices

```csharp
// ✅ Correct: Use ConfigureAwait(false) in libraries
await SomeAsyncOperation().ConfigureAwait(false);

// ✅ Correct: Use IAsyncEnumerable for streaming
public async IAsyncEnumerable<IChunk> ProduceAsync(
    [EnumeratorCancellation] CancellationToken cancellationToken = default)

// ✅ Correct: Respect cancellation tokens
cancellationToken.ThrowIfCancellationRequested();

// ✅ Correct: Use ValueTask for potentially synchronous operations
public ValueTask<bool> CanProcessAsync(IChunk chunk)

// ❌ Avoid: Blocking async operations
await SomeAsyncOperation().Result; // Deadlock risk

// ❌ Avoid: Fire-and-forget without proper handling
_ = SomeAsyncOperation(); // Unhandled exceptions
```

---

## 🧪 Testing & Debugging

### Unit Testing Framework

```csharp
using FlowEngine.Abstractions.Data;
using FlowEngine.Abstractions.Plugins;
using FlowEngine.Core;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

public class MyDataReaderTests
{
    private readonly IServiceProvider _serviceProvider;
    
    public MyDataReaderTests()
    {
        var services = new ServiceCollection();
        services.AddFlowEngine();
        services.AddLogging();
        _serviceProvider = services.BuildServiceProvider();
    }
    
    [Fact]
    public async Task ProduceAsync_ShouldGenerateExpectedData()
    {
        // Arrange
        var plugin = new MyDataReader();
        var config = CreateTestConfiguration();
        await plugin.InitializeAsync(config);
        
        // Act
        var chunks = new List<IChunk>();
        await foreach (var chunk in plugin.ProduceAsync(CancellationToken.None))
        {
            chunks.Add(chunk);
            if (chunks.Count >= 3) break; // Limit for testing
        }
        
        // Assert
        Assert.NotEmpty(chunks);
        Assert.All(chunks, chunk => 
        {
            Assert.True(chunk.RowCount > 0);
            Assert.Equal(plugin.GetOutputSchema(), chunk.Schema);
        });
    }
    
    [Fact]
    public async Task InitializeAsync_WithInvalidConfig_ShouldThrow()
    {
        // Arrange
        var plugin = new MyDataReader();
        var config = CreateInvalidConfiguration();
        
        // Act & Assert
        await Assert.ThrowsAsync<PluginConfigurationException>(
            () => plugin.InitializeAsync(config));
    }
    
    private IPluginConfiguration CreateTestConfiguration()
    {
        // Implementation using test doubles
    }
}
```

### Integration Testing

```csharp
[Fact]
public async Task EndToEnd_PluginInPipeline_ShouldProcessSuccessfully()
{
    // Arrange
    var services = new ServiceCollection();
    services.AddFlowEngine();
    services.AddLogging();
    
    await using var serviceProvider = services.BuildServiceProvider();
    var coordinator = serviceProvider.GetRequiredService<FlowEngineCoordinator>();
    
    // Register plugin
    await coordinator.RegisterPluginAsync(typeof(MyDataReader));
    
    // Create pipeline configuration
    var pipelineConfig = CreateTestPipelineConfiguration();
    
    // Act
    var result = await coordinator.PipelineExecutor.ExecuteAsync(pipelineConfig);
    
    // Assert
    Assert.True(result.IsSuccess);
    Assert.True(result.TotalRowsProcessed > 0);
    Assert.True(result.ExecutionTime.TotalSeconds < 10); // Performance assertion
}
```

### Performance Testing

```csharp
[Fact]
public async Task ProduceAsync_PerformanceBenchmark_ShouldMeetTargets()
{
    // Arrange
    var plugin = new MyDataReader();
    var config = CreatePerformanceTestConfiguration();
    await plugin.InitializeAsync(config);
    
    var stopwatch = Stopwatch.StartNew();
    var totalRows = 0;
    
    // Act
    await foreach (var chunk in plugin.ProduceAsync(CancellationToken.None))
    {
        totalRows += chunk.RowCount;
        if (totalRows >= 100000) break; // Test with 100K rows
    }
    
    stopwatch.Stop();
    
    // Assert
    var throughput = totalRows / stopwatch.Elapsed.TotalSeconds;
    Assert.True(throughput >= 50000, $"Throughput {throughput:N0} rows/sec below 50K threshold");
    
    // Memory assertion
    var memoryBefore = GC.GetTotalMemory(true);
    // ... repeat operation ...
    var memoryAfter = GC.GetTotalMemory(true);
    var memoryIncrease = memoryAfter - memoryBefore;
    
    Assert.True(memoryIncrease < 50_000_000, $"Memory increase {memoryIncrease:N0} bytes exceeds 50MB limit");
}
```

### Debugging Tips

1. **Enable Detailed Logging**:
```csharp
public override async Task InitializeAsync(IPluginConfiguration configuration)
{
    await base.InitializeAsync(configuration);
    Logger?.LogDebug("Initializing {PluginName} with config: {@Config}", Name, configuration);
}
```

2. **Use Activity for Tracing**:
```csharp
private static readonly ActivitySource ActivitySource = new("MyDataReader");

public async IAsyncEnumerable<IChunk> ProduceAsync(CancellationToken cancellationToken = default)
{
    using var activity = ActivitySource.StartActivity("ProduceData");
    activity?.SetTag("plugin.name", Name);
    
    // Implementation
}
```

3. **Monitor Performance Counters**:
```csharp
private readonly Counter<long> _rowsProduced = 
    Meter.CreateCounter<long>("flowengine.plugin.rows_produced");

// In ProduceAsync
_rowsProduced.Add(chunk.RowCount);
```

---

## 📦 Deployment & Publishing

### Building for Production

```bash
# Build optimized release
dotnet build --configuration Release

# Create NuGet package
dotnet pack --configuration Release --output ./packages

# Run final validation
flowengine plugin validate ./bin/Release/net8.0/MyPlugin.dll --production
```

### Plugin Deployment

1. **Local Development**:
```bash
# Copy to local plugins directory
cp -r ./MyPlugin ./FlowEngine/plugins/
```

2. **Docker Deployment**:
```dockerfile
FROM mcr.microsoft.com/dotnet/runtime:8.0
COPY ./plugins /app/plugins
COPY ./FlowEngine.Cli /app
WORKDIR /app
ENTRYPOINT ["./FlowEngine.Cli"]
```

3. **NuGet Distribution**:
```xml
<PackageReference Include="MyCompany.FlowEngine.MyPlugin" Version="1.0.0" />
```

### Production Checklist

- [ ] All tests pass (unit, integration, performance)
- [ ] Plugin manifest is complete and validated
- [ ] Configuration schema is documented
- [ ] Performance targets are met
- [ ] Error handling is comprehensive
- [ ] Logging is appropriate (not verbose in production)
- [ ] Dependencies are minimal and compatible
- [ ] Security review completed (if handling sensitive data)
- [ ] Documentation is complete

---

## 📚 References

- [FlowEngine Core Documentation](./Core-Architecture.md)
- [Pipeline Configuration Guide](./Pipeline-Configuration.md)
- [Performance Optimization Guide](./Performance-Optimization.md)
- [JSON Schema Validation Reference](./JSON-Schema-Validation.md)
- [API Reference](./API-Reference.md)

---

## 🤝 Support & Community

- **Issues**: [GitHub Issues](https://github.com/flowengine/flowengine/issues)
- **Discussions**: [GitHub Discussions](https://github.com/flowengine/flowengine/discussions)
- **Documentation**: [FlowEngine Docs](https://docs.flowengine.dev)

---

**Happy Plugin Development!** 🚀