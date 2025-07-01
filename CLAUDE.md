# CLAUDE.md - FlowEngine Phase 3 Development Instructions

**Version**: 3.0  
**Phase**: Phase 3 - Plugin Ecosystem & Enterprise Features  
**Last Updated**: June 30, 2025  
**Purpose**: AI Assistant instructions for FlowEngine Phase 3 development

---

## Role and Context

You are an expert C#/.NET developer assisting with FlowEngine Phase 3 implementation. Phase 3 focuses on building a robust plugin ecosystem using the **five-component architecture pattern** while maintaining the exceptional performance characteristics (14.3M rows/sec capability) achieved in Phases 1 and 2.

### Current Project State
- **Phase 1**: ✅ Complete - ArrayRow architecture delivering 3.25x performance improvement
- **Phase 2**: ✅ Complete - Plugin system, DAG execution, monitoring all operational
- **Phase 3**: 🚧 Active - Plugin ecosystem expansion and enterprise features

---

## Phase 3 Architecture Overview

### Five-Component Plugin Pattern (MANDATORY)
Every plugin MUST implement these five components:
```
1. Configuration → Schema-mapped YAML configuration with embedded field definitions
2. Validator     → Configuration and schema validation with detailed errors
3. Plugin        → Lifecycle management and metadata provider
4. Processor     → Orchestration between framework and business logic
5. Service       → Core business logic with ArrayRow optimization
```

### Critical ArrayRow Requirements
- **Explicit Field Indexes**: All fields must use sequential indexing (0, 1, 2, ...)
- **Pre-calculated Access**: Use `GetFieldIndex()` once, then direct array access
- **No String Lookups**: Never use field names in processing loops
- **Schema Validation**: Validate sequential indexing before processing

---

## Development Guidelines

### Code Generation Rules

1. **Always Use Five Components**
   - Even simple plugins need all five components
   - Use provided templates as starting point
   - Maintain clear separation of concerns

2. **ArrayRow Performance Patterns**
   ```csharp
   // ✅ CORRECT - Pre-calculate indexes
   var nameIndex = schema.GetFieldIndex("name");
   var ageIndex = schema.GetFieldIndex("age");
   
   foreach (var row in chunk.Rows)
   {
       var name = row.GetString(nameIndex);  // O(1) access
       var age = row.GetInt32(ageIndex);     // O(1) access
   }
   
   // ❌ WRONG - String lookups in loop
   foreach (var row in chunk.Rows)
   {
       var name = row.GetValue("name");  // O(n) lookup
       var age = row.GetValue("age");    // O(n) lookup
   }
   ```

3. **Schema Definition Requirements**
   ```yaml
   # Every plugin configuration MUST define explicit schemas
   configuration:
     schema:
       fields:
         - name: "id"
           type: "int32"
           index: 0        # REQUIRED: Sequential indexing
         - name: "name"
           type: "string"
           index: 1        # REQUIRED: Must be index + 1
   ```

### Response Patterns

1. **Component Implementation Queries**
   - Always provide all five components
   - Include extensive comments in templates
   - Show ArrayRow optimization patterns
   - Add performance considerations

2. **Configuration Examples**
   - Always include schema with explicit indexes
   - Show both simple and complex scenarios
   - Include validation rules
   - Demonstrate schema evolution

3. **Performance Optimization**
   - Emphasize pre-calculated field indexes
   - Show memory pooling patterns
   - Include chunking strategies
   - Demonstrate batch processing

4. **Error Handling**
   - Validation at configuration time
   - Graceful degradation patterns
   - Detailed error context
   - Recovery strategies

---

## CLI-First Development

All operations must be available through CLI:
```bash
# Plugin operations
flowengine plugin create --name MyPlugin --type Transform
flowengine plugin validate --path ./MyPlugin
flowengine plugin test --path ./MyPlugin --data sample.csv
flowengine plugin benchmark --path ./MyPlugin --rows 1000000

# Schema operations
flowengine schema infer --input data.csv --output schema.yaml
flowengine schema validate --schema schema.yaml --data test.csv
flowengine schema migrate --from v1.yaml --to v2.yaml

# Pipeline operations
flowengine pipeline validate --config pipeline.yaml
flowengine pipeline run --config pipeline.yaml --monitor
flowengine pipeline export --config pipeline.yaml --format docker
```

---

## Phase 3 Specific Patterns

### Plugin Categories
1. **Source Plugins**: DelimitedSource, JsonSource, DatabaseSource
2. **Transform Plugins**: DataEnrichment, JavaScriptTransform, SchemaMapper
3. **Sink Plugins**: DatabaseSink, ApiSink, FileSink
4. **Utility Plugins**: Validator, Router, ErrorHandler

### Enterprise Features
1. **Hot-Swapping**: Zero-downtime plugin updates
2. **Schema Evolution**: Backward-compatible migrations
3. **Script Engine**: Core-managed, not plugin-managed
4. **Monitoring**: Real-time metrics and health checks

---

## Script Engine Architecture

### Core Service Pattern (IMPORTANT)
JavaScript/Script engines are managed by **core services**, NOT individual plugins:

```csharp
// ✅ CORRECT - Plugin uses injected service
public class JavaScriptTransformService
{
    private readonly IScriptEngineService _scriptEngine; // From core
    
    public JavaScriptTransformService(IScriptEngineService scriptEngine)
    {
        _scriptEngine = scriptEngine;
    }
}

// ❌ WRONG - Plugin manages its own engine
public class JavaScriptTransformService
{
    private readonly ObjectPool<ScriptEngine> _enginePool; // Don't do this
}
```

### Rationale for Core Management
- **Resource Efficiency**: Single engine pool for entire application
- **Script Caching**: Compiled scripts shared across plugins
- **Security**: Centralized script validation and sandboxing
- **Performance**: Unified monitoring and optimization
- **Configuration**: Single point of engine configuration

---

## Testing Patterns

### Unit Testing Plugins
```csharp
// Test each component independently
[TestClass]
public class MyPluginValidatorTests
{
    [TestMethod]
    public void Validate_WithInvalidSchema_ReturnsErrors()
    {
        // Arrange
        var validator = new MyPluginValidator();
        var config = new MyPluginConfiguration { /* invalid */ };
        
        // Act
        var result = validator.ValidateConfiguration(config);
        
        // Assert
        Assert.IsFalse(result.IsValid);
        Assert.Contains("Sequential indexing required", result.Errors);
    }
}
```

### Integration Testing
```csharp
// Test complete plugin flow
[TestMethod]
public async Task Plugin_ProcessesData_MaintainsPerformance()
{
    // Arrange
    var plugin = CreatePlugin();
    var testData = GenerateTestData(10000);
    var stopwatch = Stopwatch.StartNew();
    
    // Act
    var result = await plugin.ProcessAsync(testData);
    stopwatch.Stop();
    
    // Assert
    Assert.IsTrue(result.Success);
    Assert.IsTrue(stopwatch.ElapsedMilliseconds < 50); // <50ms for 10K rows
    Assert.AreEqual(10000, result.ProcessedRows);
}
```

### Performance Benchmarking
```csharp
[Benchmark]
public async Task BenchmarkArrayRowAccess()
{
    var schema = CreateSchema();
    var nameIndex = schema.GetFieldIndex("name"); // Pre-calculated
    
    foreach (var row in _testChunk.Rows)
    {
        var name = row.GetString(nameIndex); // O(1) access
        // Process...
    }
}
```

---

## Common Anti-Patterns to Avoid

### 1. **String-Based Field Access in Loops**
```csharp
// ❌ NEVER DO THIS
foreach (var row in chunk.Rows)
{
    var value = row["fieldName"]; // String lookup each time
}
```

### 2. **Creating Services in Processor**
```csharp
// ❌ WRONG - Creates new service each time
public async Task<ProcessResult> ProcessAsync(IDataset input)
{
    var service = new MyPluginService(); // Don't create here
}

// ✅ CORRECT - Inject service
public MyPluginProcessor(MyPluginService service)
{
    _service = service; // Injected
}
```

### 3. **Synchronous I/O in Async Methods**
```csharp
// ❌ BLOCKS THREAD
public async Task ProcessAsync()
{
    var data = File.ReadAllText(path); // Sync I/O
}

// ✅ CORRECT
public async Task ProcessAsync()
{
    var data = await File.ReadAllTextAsync(path); // Async I/O
}
```

### 4. **Ignoring Cancellation Tokens**
```csharp
// ❌ Can't be cancelled
public async Task LongRunningOperation()
{
    await Task.Delay(5000);
}

// ✅ CORRECT - Respects cancellation
public async Task LongRunningOperation(CancellationToken cancellationToken)
{
    await Task.Delay(5000, cancellationToken);
}
```

---

## Documentation Standards

### Code Comments
```csharp
/// <summary>
/// Processes input data using ArrayRow optimization for O(1) field access.
/// </summary>
/// <param name="input">Input dataset with pre-validated schema</param>
/// <param name="cancellationToken">Cancellation token for cooperative cancellation</param>
/// <returns>ProcessResult with success/failure status and processed row count</returns>
/// <remarks>
/// Performance: Processes 200K+ rows/second on standard hardware.
/// Memory: Uses bounded memory regardless of input size.
/// </remarks>
public async Task<ProcessResult> ProcessAsync(
    IDataset input, 
    CancellationToken cancellationToken)
```

### YAML Configuration Comments
```yaml
# Plugin configuration with embedded schema definitions
configuration:
  # REQUIRED: Explicit schema for ArrayRow optimization
  schema:
    fields:
      - name: "customerId"
        type: "int32"
        index: 0          # MUST be sequential: 0, 1, 2...
        required: true
        description: "Unique customer identifier"
```

---

## Phase 3 Success Criteria

When generating code or reviewing implementations, ensure:

1. ✅ **Five Components**: All plugins have Configuration, Validator, Plugin, Processor, Service
2. ✅ **ArrayRow Optimization**: Pre-calculated indexes, no string lookups in loops
3. ✅ **CLI Integration**: All operations available through command-line
4. ✅ **Performance**: Maintains 200K+ rows/sec throughput
5. ✅ **Error Handling**: Comprehensive validation and graceful degradation
6. ✅ **Documentation**: XML comments and usage examples
7. ✅ **Testing**: Unit tests for each component, integration tests for flow
8. ✅ **Monitoring**: Metrics and health checks implemented

---

## Quick Reference

### Create New Plugin
```bash
flowengine plugin create --name MyPlugin --type Transform
```

### Validate Plugin
```bash
flowengine plugin validate --path ./MyPlugin --comprehensive
```

### Test Performance
```bash
flowengine plugin benchmark --path ./MyPlugin --rows 1000000
```

### Common Issues
- **Performance degradation**: Check for string-based field access
- **Memory leaks**: Ensure proper disposal patterns
- **Schema errors**: Validate sequential field indexing
- **Service failures**: Check dependency injection configuration

---

**Remember**: FlowEngine Phase 3 is about building a robust plugin ecosystem while maintaining the exceptional performance achieved in earlier phases. Every decision should support both goals.