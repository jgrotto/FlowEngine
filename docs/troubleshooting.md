# FlowEngine Troubleshooting Guide

**Diagnose and Resolve Common Issues**

This guide provides solutions for common problems encountered when developing and running FlowEngine pipelines.

## 🔍 Quick Diagnostics

### Check System Health

```bash
# Check FlowEngine status
flowengine status

# Validate configuration
flowengine config validate pipeline.yaml

# Test pipeline without execution
flowengine pipeline test pipeline.yaml --dry-run

# Check plugin health
flowengine plugin health
```

### Common Diagnostic Commands

```bash
# Check memory usage
dotnet.exe run --project src/FlowEngine.Cli/ pipeline.yaml --monitor-memory

# Enable verbose logging
dotnet.exe run --project src/FlowEngine.Cli/ pipeline.yaml --verbose

# Performance profiling
dotnet.exe run --project src/FlowEngine.Cli/ pipeline.yaml --profile

# Check dependencies
dotnet.exe run --project src/FlowEngine.Cli/ --check-dependencies
```

## 🚨 Common Issues and Solutions

### 1. Performance Issues

#### **Issue**: Low Throughput (< 10K rows/sec)

**Symptoms**:
```
warn: Performance below target: 7521 rows/sec < 120000 rows/sec
info: Processing 20000 rows in 2660ms
```

**Diagnosis**:
```bash
# Check performance metrics
dotnet.exe test tests/DelimitedPlugins.Tests/ --filter "Performance" --logger "console;verbosity=detailed"

# Monitor memory usage
dotnet.exe run --project src/FlowEngine.Cli/ pipeline.yaml --monitor-memory
```

**Solutions**:

1. **Increase Chunk Size**:
```yaml
settings:
  chunkSize: 10000              # Increase from default 5000
```

2. **Optimize JavaScript**:
```javascript
// ❌ Inefficient
function process(context) {
  const current = context.input.current();
  // Complex regex operations
  const result = current.field.replace(/complex-regex/g, 'replacement');
  return true;
}

// ✅ Efficient
function process(context) {
  const current = context.input.current();
  // Simple string operations
  const result = current.field.toLowerCase();
  return true;
}
```

3. **Increase Memory Allocation**:
```yaml
settings:
  maxMemoryMB: 8192             # Increase from default 2048
```

4. **Enable Parallel Processing**:
```yaml
settings:
  parallelism: 8                # Match CPU cores
```

#### **Issue**: Memory Pressure

**Symptoms**:
```
warn: Memory usage near limit: 3072.5MB
error: OutOfMemoryException during processing
```

**Solutions**:

1. **Reduce Chunk Size**:
```yaml
settings:
  chunkSize: 2000               # Reduce from default 5000
```

2. **Enable Memory Monitoring**:
```yaml
settings:
  enableMemoryMonitoring: true
  memoryWarningThreshold: 75    # Warn at 75% usage
```

3. **Optimize Object Creation**:
```csharp
// ❌ Creates many objects
var results = inputData.Select(x => new ProcessedData { ... }).ToList();

// ✅ Reuse objects
var results = new List<ProcessedData>(inputData.Count);
foreach (var item in inputData) {
    results.Add(ProcessExistingObject(item));
}
```

### 2. Configuration Issues

#### **Issue**: Plugin Not Found

**Symptoms**:
```
error: Plugin 'javascript-transform' not found
error: Failed to load plugin assembly
```

**Solutions**:

1. **Check Plugin Registration**:
```csharp
// In ServiceCollectionExtensions.cs
services.AddSingleton<IScriptEngineService, JintScriptEngineService>();
services.AddSingleton<IJavaScriptContextService, JavaScriptContextService>();
```

2. **Verify Plugin Assembly**:
```bash
# Check if plugin DLL exists
ls -la plugins/JavaScriptTransform/bin/Release/net8.0/

# Add to solution if missing
dotnet sln FlowEngine.sln add plugins/JavaScriptTransform/JavaScriptTransform.csproj
```

3. **Check Dependencies**:
```xml
<!-- In test project -->
<ItemGroup>
  <ProjectReference Include="../../plugins/JavaScriptTransform/JavaScriptTransform.csproj" />
</ItemGroup>
```

#### **Issue**: Configuration Validation Errors

**Symptoms**:
```
error: Configuration validation failed: Script must contain a 'function process(context)' function
error: Output schema validation failed: Field 'timestamp' type mismatch
```

**Solutions**:

1. **Fix Script Function**:
```javascript
// ❌ Missing function
const data = context.input.current();

// ✅ Proper function
function process(context) {
  const data = context.input.current();
  context.output.setField('result', data.field);
  return true;
}
```

2. **Fix Schema Types**:
```yaml
# ❌ Wrong type
outputSchema:
  fields:
    - name: timestamp
      type: integer        # JavaScript Date.now() returns long

# ✅ Correct type
outputSchema:
  fields:
    - name: timestamp
      type: long           # Matches JavaScript Date.now()
```

### 3. JavaScript Transform Issues

#### **Issue**: Context API Errors

**Symptoms**:
```
error: JavaScript execution failed: context.input.current is not a function
error: context.output.setField is not a function
```

**Solutions**:

1. **Check Context Usage**:
```javascript
// ❌ Incorrect usage
function process(context) {
  const current = context.input.current;     // Missing ()
  context.output.setField('result');         // Missing value
  return true;
}

// ✅ Correct usage
function process(context) {
  const current = context.input.current();   // Function call
  context.output.setField('result', 'value'); // With value
  return true;
}
```

2. **Validate Field Names**:
```javascript
// ❌ Field doesn't exist in schema
context.output.setField('nonexistent_field', 'value');

// ✅ Check schema first
const current = context.input.current();
if (current.hasOwnProperty('field_name')) {
  context.output.setField('output_field', current.field_name);
}
```

#### **Issue**: Type Conversion Errors

**Symptoms**:
```
error: Value for column 'amount' at index 3 is not compatible with type Decimal
error: Unable to convert 'undefined' to required type
```

**Solutions**:

1. **Handle Undefined Values**:
```javascript
// ❌ May cause errors
const amount = current.amount;
context.output.setField('amount', amount);

// ✅ Safe handling
const amount = current.amount || 0;
context.output.setField('amount', parseFloat(amount) || 0);
```

2. **Type Validation**:
```javascript
function process(context) {
  const current = context.input.current();
  
  // Validate and convert types
  const amount = parseFloat(current.amount) || 0;
  const timestamp = Date.now();
  
  context.output.setField('amount', amount);
  context.output.setField('timestamp', timestamp);
  
  return true;
}
```

### 4. Build and Dependency Issues

#### **Issue**: Style Warnings as Errors

**Symptoms**:
```
error IDE0011: Add braces to 'if' statement
error: Build failed due to warnings treated as errors
```

**Solutions**:

1. **Disable Warnings as Errors**:
```xml
<PropertyGroup>
  <TreatWarningsAsErrors>false</TreatWarningsAsErrors>
</PropertyGroup>
```

2. **Fix Style Issues**:
```csharp
// ❌ Missing braces
if (condition)
    DoSomething();

// ✅ With braces
if (condition)
{
    DoSomething();
}
```

#### **Issue**: Dependency Injection Failures

**Symptoms**:
```
error: Unable to resolve service for type 'FlowEngine.Core.Factories.ArrayRowFactory'
error: No service for type 'IScriptEngineService' has been registered
```

**Solutions**:

1. **Register Services**:
```csharp
// In ServiceCollectionExtensions.cs
services.TryAddSingleton<IArrayRowFactory, ArrayRowFactory>();
services.TryAddSingleton<IScriptEngineService, JintScriptEngineService>();
```

2. **Use Interfaces**:
```csharp
// ❌ Direct class dependency
public JavaScriptContextService(ArrayRowFactory factory) 

// ✅ Interface dependency
public JavaScriptContextService(IArrayRowFactory factory)
```

### 5. Data Processing Issues

#### **Issue**: Schema Compatibility Errors

**Symptoms**:
```
error: Row at index 0 has incompatible schema
error: Input schema does not match expected schema
```

**Solutions**:

1. **Check Schema Definitions**:
```csharp
// Ensure input and output schemas match plugin expectations
var inputSchema = Schema.GetOrCreate(new[]
{
    new ColumnDefinition { Name = "customer_id", DataType = typeof(int), Index = 0 },
    new ColumnDefinition { Name = "name", DataType = typeof(string), Index = 1 }
});
```

2. **Validate Schema Compatibility**:
```csharp
var validationResult = plugin.ValidateInputSchema(inputSchema);
if (!validationResult.IsValid)
{
    throw new InvalidOperationException($"Schema validation failed: {validationResult.ErrorMessage}");
}
```

#### **Issue**: Null Reference Errors

**Symptoms**:
```
error: System.NullReferenceException: Object reference not set to an instance of an object
error: Value cannot be null. (Parameter 'collection')
```

**Solutions**:

1. **Add Null Checks**:
```csharp
// ❌ No null check
var metadata = new Dictionary<string, object>(chunk.Metadata);

// ✅ With null check
var metadata = chunk.Metadata != null 
    ? new Dictionary<string, object>(chunk.Metadata)
    : new Dictionary<string, object>();
```

2. **Initialize Objects**:
```csharp
// ❌ Uninitialized
private List<IArrayRow> _rows;

// ✅ Initialized
private List<IArrayRow> _rows = new List<IArrayRow>();
```

## 🔧 Advanced Troubleshooting

### Enable Debug Logging

```yaml
# In pipeline configuration
settings:
  logLevel: "Debug"
  enableVerboseLogging: true
  
# Or via environment variable
export FLOWENGINE_LOG_LEVEL=Debug
```

### Memory Profiling

```csharp
// Add memory diagnostics
[MemoryDiagnoser]
public class PerformanceTests
{
    [Benchmark]
    public void ProcessData()
    {
        // Your code here
    }
}
```

### Performance Profiling

```bash
# Run with performance profiling
dotnet run --project src/FlowEngine.Cli/ pipeline.yaml --profile

# Use BenchmarkDotNet
dotnet run --project benchmarks/FlowEngine.Benchmarks/ --configuration Release
```

### Network Diagnostics

```bash
# Check network connectivity
ping external-service.com

# Check DNS resolution
nslookup external-service.com

# Test HTTP endpoints
curl -v http://external-service.com/api/health
```

## 🚀 Performance Optimization

### Identify Bottlenecks

```csharp
// Add performance counters
private readonly Counter<long> _rowsProcessed = 
    Meter.CreateCounter<long>("flowengine.rows_processed");

private readonly Histogram<double> _processingTime =
    Meter.CreateHistogram<double>("flowengine.processing_time");

// In processing loop
using var activity = ActivitySource.StartActivity("ProcessChunk");
var stopwatch = Stopwatch.StartNew();

// Process data
ProcessChunk(chunk);

stopwatch.Stop();
_processingTime.Record(stopwatch.Elapsed.TotalMilliseconds);
_rowsProcessed.Add(chunk.RowCount);
```

### Optimize Memory Usage

```csharp
// Use object pooling
private readonly ObjectPool<List<IArrayRow>> _listPool;

public async Task ProcessAsync()
{
    var list = _listPool.Get();
    try
    {
        // Use list
        ProcessData(list);
    }
    finally
    {
        list.Clear();
        _listPool.Return(list);
    }
}
```

### Optimize I/O Operations

```csharp
// Use async I/O
public async Task WriteDataAsync(IChunk chunk)
{
    using var fileStream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 
        bufferSize: 81920, useAsync: true);
    
    await WriteChunkAsync(fileStream, chunk);
}

// Batch operations
public async Task WriteBatchAsync(IEnumerable<IChunk> chunks)
{
    var buffer = new List<IChunk>(1000);
    
    foreach (var chunk in chunks)
    {
        buffer.Add(chunk);
        
        if (buffer.Count >= 1000)
        {
            await WriteBatchToFileAsync(buffer);
            buffer.Clear();
        }
    }
    
    if (buffer.Count > 0)
    {
        await WriteBatchToFileAsync(buffer);
    }
}
```

## 📊 Monitoring and Alerts

### Health Checks

```csharp
public class PluginHealthCheck : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, 
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Check plugin health
            var isHealthy = await CheckPluginHealthAsync();
            
            return isHealthy 
                ? HealthCheckResult.Healthy("Plugin is healthy")
                : HealthCheckResult.Unhealthy("Plugin is unhealthy");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy($"Health check failed: {ex.Message}");
        }
    }
}
```

### Custom Metrics

```csharp
// Define custom metrics
public class CustomMetrics
{
    private readonly Counter<long> _errorsCounter;
    private readonly Histogram<double> _latencyHistogram;
    private readonly Gauge<int> _activeConnections;
    
    public CustomMetrics(IMeterFactory meterFactory)
    {
        var meter = meterFactory.Create("FlowEngine.CustomMetrics");
        
        _errorsCounter = meter.CreateCounter<long>("errors_total");
        _latencyHistogram = meter.CreateHistogram<double>("request_duration_seconds");
        _activeConnections = meter.CreateGauge<int>("active_connections");
    }
    
    public void RecordError(string errorType)
    {
        _errorsCounter.Add(1, new KeyValuePair<string, object>("type", errorType));
    }
    
    public void RecordLatency(double seconds)
    {
        _latencyHistogram.Record(seconds);
    }
    
    public void SetActiveConnections(int count)
    {
        _activeConnections.Record(count);
    }
}
```

## 🔍 Debugging Techniques

### Breakpoint Debugging

```csharp
// Add conditional breakpoints
public async Task ProcessAsync(IChunk chunk)
{
    foreach (var row in chunk.Rows)
    {
        // Conditional breakpoint: row[0] == 12345
        var result = ProcessRow(row);
        
        if (result == null)
        {
            System.Diagnostics.Debugger.Break(); // Debug on null result
        }
    }
}
```

### Logging Best Practices

```csharp
// Structured logging
_logger.LogInformation("Processing chunk {ChunkId} with {RowCount} rows", 
    chunk.Id, chunk.RowCount);

// Log with context
using var scope = _logger.BeginScope(new Dictionary<string, object>
{
    ["ChunkId"] = chunk.Id,
    ["PluginName"] = Name,
    ["Operation"] = "Transform"
});

_logger.LogDebug("Starting transformation");
```

### Trace Debugging

```csharp
// Add activity tracing
private static readonly ActivitySource ActivitySource = new("FlowEngine.MyPlugin");

public async Task ProcessAsync()
{
    using var activity = ActivitySource.StartActivity("ProcessData");
    activity?.SetTag("plugin.name", Name);
    activity?.SetTag("data.rows", chunk.RowCount);
    
    // Process data
    await ProcessDataAsync();
    
    activity?.SetTag("result.status", "success");
}
```

## 🛠️ Development Tools

### Visual Studio Debugging

```json
// launch.json for VS Code
{
    "version": "0.2.0",
    "configurations": [
        {
            "name": "Debug FlowEngine",
            "type": "coreclr",
            "request": "launch",
            "program": "${workspaceFolder}/src/FlowEngine.Cli/bin/Debug/net8.0/FlowEngine.Cli.dll",
            "args": ["pipeline.yaml"],
            "cwd": "${workspaceFolder}",
            "console": "integratedTerminal",
            "stopAtEntry": false,
            "env": {
                "FLOWENGINE_LOG_LEVEL": "Debug"
            }
        }
    ]
}
```

### Performance Profiling Tools

```bash
# Use dotnet-trace
dotnet tool install --global dotnet-trace
dotnet trace collect --process-id <PID> --providers Microsoft-Extensions-Logging

# Use dotnet-counters
dotnet tool install --global dotnet-counters
dotnet counters monitor --process-id <PID> --counters System.Runtime

# Use dotnet-dump
dotnet tool install --global dotnet-dump
dotnet dump collect --process-id <PID>
```

## 📚 Resources

### Documentation

- [Architecture Guide](./architecture.md)
- [Plugin Development Guide](./plugin-development.md)
- [Performance Guide](./performance.md)
- [Configuration Reference](./configuration.md)

### Community Support

- **GitHub Issues**: Report bugs and request features
- **GitHub Discussions**: Ask questions and share knowledge
- **Stack Overflow**: Tag questions with `flowengine`

### Diagnostic Tools

- **FlowEngine CLI**: Built-in diagnostic commands
- **.NET Diagnostic Tools**: dotnet-trace, dotnet-counters, dotnet-dump
- **Performance Profilers**: BenchmarkDotNet, PerfView, dotMemory

---

This troubleshooting guide covers the most common issues encountered with FlowEngine. If you encounter issues not covered here, please check the GitHub repository or create an issue with detailed information about your problem.