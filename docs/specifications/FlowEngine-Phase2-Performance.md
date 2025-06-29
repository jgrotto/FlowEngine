# FlowEngine Phase 2 Performance Tuning Guide

**Reading Time**: 5 minutes  
**Goal**: Optimize Phase 2 pipelines for maximum throughput  

---

## Key Performance Metrics

### Throughput Targets
- **Development**: 1M+ rows/sec
- **Production**: 5M+ rows/sec  
- **Enterprise**: 10M+ rows/sec

### Latency Targets
- **Streaming**: <10ms per chunk
- **Batch**: <100ms per chunk
- **Real-time**: <1ms per chunk

## Quick Optimization Checklist

### ✅ Channel Configuration
```yaml
# High throughput
channels:
  default:
    bufferSize: 1000          # Larger buffers for batch
    backpressureThreshold: 90 # Allow higher utilization
    fullMode: "DropOldest"    # Prevent blocking

# Low latency  
channels:
  default:
    bufferSize: 10            # Small buffers for responsiveness
    backpressureThreshold: 50 # Early backpressure
    fullMode: "Wait"          # Preserve all data
```

### ✅ Plugin Resource Limits
```yaml
isolation:
  defaultMemoryLimitMB: 200   # Higher for complex transforms
  defaultTimeoutSeconds: 60   # Longer for database operations
  enableSpillover: true       # Handle memory pressure
```

### ✅ Schema Optimization
```yaml
schema:
  fields:
    - name: "id"
      type: "int32"           # Use smallest sufficient types
    - name: "timestamp"
      type: "int64"           # Unix timestamps vs DateTime
    - name: "data"
      type: "string"          # Avoid nested objects
```

## Performance Monitoring

### Built-in Metrics
```csharp
// Access metrics through dependency injection
public class MyPlugin : ITransformPlugin
{
    private readonly IMeterFactory _meterFactory;
    private readonly Counter<long> _rowsProcessed;
    private readonly Histogram<double> _processingTime;
    
    public MyPlugin(IMeterFactory meterFactory)
    {
        _meterFactory = meterFactory;
        var meter = _meterFactory.Create("FlowEngine.MyPlugin");
        _rowsProcessed = meter.CreateCounter<long>("rows_processed");
        _processingTime = meter.CreateHistogram<double>("processing_time_ms");
    }
}
```

### Key Metrics to Monitor
- **Rows per second**: Primary throughput metric
- **Memory usage per plugin**: Isolation effectiveness
- **Channel buffer utilization**: Backpressure indicators
- **GC pressure**: Memory allocation patterns

## Common Performance Issues

### Issue: Plugin Memory Leaks
**Symptoms**: Increasing memory usage over time  
**Diagnosis**: Monitor plugin-specific memory metrics  
**Solution**: 
```csharp
// Implement proper disposal
public async Task DisposeAsync()
{
    _largeDataStructure?.Clear();
    await _databaseConnection.DisposeAsync();
    GC.Collect(); // Force cleanup in plugin context
}
```

### Issue: Channel Bottlenecks
**Symptoms**: Low throughput despite CPU availability  
**Diagnosis**: Check buffer utilization metrics  
**Solution**: Increase buffer sizes or implement async processing
```yaml
channels:
  highThroughput:
    bufferSize: 2000          # Double default size
    allowOverflow: true       # Prevent blocking
```

### Issue: Schema Transformation Overhead
**Symptoms**: Performance drops with schema evolution  
**Diagnosis**: Profile transformation execution time  
**Solution**: Cache transformed schemas and pre-compute mappings
```csharp
private static readonly ConcurrentDictionary<(SchemaVersion, SchemaVersion), 
    Func<ArrayRow, ArrayRow>> TransformCache = new();
```

### Issue: Plugin Loading Latency
**Symptoms**: Slow pipeline startup  
**Diagnosis**: Monitor AssemblyLoadContext creation time  
**Solution**: Implement plugin preloading or lazy loading
```yaml
isolation:
  preloadPlugins: true      # Load plugins at startup
  enableLazyLoading: false  # Trade startup time for runtime performance
```

## Optimization Strategies

### 1. Batch Size Tuning
```yaml
# Small batches: Lower latency, higher overhead
plugins:
  - name: "source"
    config:
      chunkSize: 100

# Large batches: Higher latency, better throughput  
plugins:
  - name: "source"
    config:
      chunkSize: 5000
```

### 2. Memory Pool Configuration
```yaml
settings:
  memoryPools:
    arrayRowPool:
      initialSize: 1000       # Pre-allocate for hot paths
      maxSize: 10000         # Prevent unbounded growth
    chunkPool:
      initialSize: 100
      maxSize: 1000
```

### 3. Parallel Processing
```csharp
// Enable parallel transforms where safe
public async IAsyncEnumerable<Chunk> TransformAsync(
    IAsyncEnumerable<Chunk> input,
    CancellationToken cancellationToken)
{
    await foreach (var chunk in input)
    {
        // Process rows in parallel within chunk
        var tasks = chunk.Rows.Select(row => Task.Run(() => TransformRow(row)));
        var transformedRows = await Task.WhenAll(tasks);
        
        yield return new Chunk(transformedRows, _outputSchema);
    }
}
```

### 4. Schema Caching
```csharp
// Cache frequently used schemas
private static readonly ConcurrentDictionary<string, ISchema> SchemaCache = new();

public ISchema GetSchema(string name, string version)
{
    var key = $"{name}:{version}";
    return SchemaCache.GetOrAdd(key, _ => LoadSchemaFromRegistry(name, version));
}
```

## Environment-Specific Tuning

### Development Environment
```yaml
settings:
  channels:
    default:
      bufferSize: 50          # Small buffers for quick feedback
  isolation:
    defaultMemoryLimitMB: 100 # Conservative limits
    enableDebugLogging: true  # Detailed diagnostics
```

### Production Environment
```yaml
settings:
  channels:
    default:
      bufferSize: 1000        # Large buffers for throughput
  isolation:
    defaultMemoryLimitMB: 500 # Higher limits
    enableDebugLogging: false # Minimal logging overhead
    enableTelemetry: true     # Production monitoring
```

### Cloud/Container Environment
```yaml
settings:
  channels:
    default:
      enableMemoryPressureAdaptation: true  # Auto-adjust to container limits
  isolation:
    dynamicMemoryLimits: true              # Scale with available resources
    spilloverPath: "/tmp/flowengine"       # Fast local storage
```

## Benchmarking Your Pipeline

### Basic Performance Test
```csharp
[Benchmark]
public async Task ProcessLargeDataset()
{
    var pipeline = await PipelineBuilder.FromYamlAsync(_pipelineConfig);
    
    using var timer = new Timer();
    timer.Start();
    
    await pipeline.ExecuteAsync();
    
    timer.Stop();
    var rowsPerSecond = _totalRows / timer.Elapsed.TotalSeconds;
    
    Console.WriteLine($"Throughput: {rowsPerSecond:N0} rows/sec");
}
```

### Memory Profiling
```csharp
[Test]
public async Task MemoryUsageTest()
{
    var initialMemory = GC.GetTotalMemory(true);
    
    await RunPipeline();
    
    var finalMemory = GC.GetTotalMemory(true);
    var memoryIncrease = finalMemory - initialMemory;
    
    Assert.That(memoryIncrease, Is.LessThan(100 * 1024 * 1024)); // < 100MB
}
```

## Troubleshooting Performance

### Diagnostic Commands
```bash
# Monitor pipeline performance
dotnet run -- --pipeline config.yml --monitor --interval 5s

# Profile memory usage
dotnet run -- --pipeline config.yml --profile-memory

# Enable detailed metrics
export FLOWENGINE_ENABLE_DETAILED_METRICS=true
```

### Performance Regression Detection
```yaml
pipeline:
  performance:
    baselineFile: "baseline-performance.json"
    failOnRegression: true
    maxRegressionPercent: 10  # Fail if >10% slower
```

---

**Advanced Topics**: See [Memory Management Guide](memory-guide.md) and [Distributed Processing](distributed-guide.md)