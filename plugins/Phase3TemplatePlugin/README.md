# Phase 3 Template Plugin

**Reference Implementation of Correct FlowEngine Phase 3 Plugin Architecture**

This plugin serves as the **definitive template** for all Phase 3 external plugins, demonstrating the correct implementation of the five-component architecture with Abstractions-only dependencies.

## Architecture Overview

This template implements the **mandatory five-component pattern**:

```
1. Configuration → TemplatePluginConfiguration.cs
2. Validator     → TemplatePluginValidator.cs  
3. Plugin        → TemplatePlugin.cs
4. Processor     → TemplatePluginProcessor.cs
5. Service       → TemplatePluginService.cs
```

## Key Design Principles

### ✅ **Abstractions-Only Dependencies**
- **ONLY** references `FlowEngine.Abstractions`
- **NO** references to `FlowEngine.Core` 
- Demonstrates proper external plugin isolation

### ✅ **Five-Component Separation**
- **Configuration**: Schema-mapped YAML with embedded field definitions
- **Validator**: Comprehensive validation with detailed error reporting
- **Plugin**: Lifecycle management and metadata
- **Processor**: Orchestration between framework and business logic
- **Service**: Core business logic with dependency injection

### ✅ **ArrayRow Optimization**
- Pre-calculated field indexes for O(1) access
- Sequential field indexing (0, 1, 2, ...)
- Performance target: 200K+ rows/sec

### ✅ **Dependency Injection Ready**
```csharp
// Service receives Core services via DI (when bridge is implemented)
public TemplatePluginService(
    TemplatePlugin plugin,
    ILogger logger,
    IScriptEngineService scriptEngine,     // From Core
    IDataTypeService dataTypeService)      // From Core
```

## Integration Gaps Identified

This template reveals the **Core/Abstractions bridge requirements**:

### Missing Types in Abstractions
```csharp
// TODO: Need Core bridge for these operations
// var schema = Schema.Create(columns);           // Schema creation
// var row = new ArrayRow(schema, values);       // ArrayRow creation  
// var chunk = new Chunk(schema, rows);          // Chunk creation
// var dataset = new Dataset(schema, chunks);    // Dataset creation
```

### Required Bridge Services
1. **ISchemaFactory** - Create schemas from column definitions
2. **IArrayRowFactory** - Create ArrayRow instances 
3. **IChunkFactory** - Create data chunks
4. **IDatasetFactory** - Create datasets
5. **IPerformanceCounters** - Performance monitoring
6. **IScriptEngineService** - Script execution (for other plugins)

## Usage as Template

### For Plugin Developers
1. **Copy this entire plugin structure**
2. **Rename all classes** (replace "Template" with your plugin name)
3. **Implement your business logic** in the Service component
4. **Configure your schema** in the Configuration component
5. **Add your validation rules** in the Validator component

### For dotnet new Template
```bash
# Future: This will become the official plugin template
dotnet new flowengine-plugin -n MyAwesomePlugin
```

## Build and Test

```bash
# Build plugin (should succeed with only Abstractions)
cd plugins/Phase3TemplatePlugin
dotnet build --configuration Release

# Verify no Core dependencies
dotnet list package --include-transitive
# Should show ONLY FlowEngine.Abstractions

# Run validation tests
dotnet test
```

## Performance Characteristics

- **Target Throughput**: 200K+ rows/sec
- **Memory Usage**: Bounded, independent of dataset size
- **Field Access**: O(1) using pre-calculated indexes
- **Batch Processing**: Optimized for high-throughput scenarios

## Configuration Example

```yaml
plugins:
  - id: "phase3-template-plugin"
    name: "Phase 3 Template Plugin"
    type: "Source"
    configuration:
      rowCount: 10000
      batchSize: 1000
      dataType: "Customer"
      schema:
        fields:
          - name: "Id"
            type: "int32"
            index: 0
            required: true
          - name: "Name"
            type: "string"
            index: 1
            required: true
          - name: "Value"
            type: "decimal"
            index: 2
            required: true
          - name: "Timestamp"
            type: "datetime"
            index: 3
            required: true
```

## Testing Strategy

```csharp
[Test]
public void Configuration_ValidatesCorrectly()
{
    var config = new TemplatePluginConfiguration 
    { 
        RowCount = 1000, 
        BatchSize = 100 
    };
    var validator = new TemplatePluginValidator();
    var result = validator.Validate(config);
    
    Assert.IsTrue(result.IsValid);
}

[Test]
public void Plugin_FollowsLifecycleProperly()
{
    var plugin = new TemplatePlugin(logger);
    
    // Test lifecycle: Created → Initialized → Running → Stopped → Disposed
    Assert.AreEqual(PluginState.Created, plugin.Status);
    
    await plugin.InitializeAsync(config);
    Assert.AreEqual(PluginState.Initialized, plugin.Status);
    
    await plugin.StartAsync();
    Assert.AreEqual(PluginState.Running, plugin.Status);
}

[Test] 
public void Service_AchievesPerformanceTarget()
{
    var service = new TemplatePluginService(plugin, logger);
    var testData = GenerateTestRows(200_000);
    
    var stopwatch = Stopwatch.StartNew();
    var results = await service.ProcessRowBatchAsync(testData);
    stopwatch.Stop();
    
    var throughput = results.Count() / stopwatch.Elapsed.TotalSeconds;
    Assert.IsTrue(throughput >= 200_000, $"Throughput {throughput:F0} below target");
}
```

## Next Steps

1. **Implement Core Bridge** - Create adapter services for Abstractions/Core integration
2. **Test Integration** - Verify plugin loads and executes in Core framework
3. **Create Template Generator** - Convert to `dotnet new` template
4. **Performance Validation** - Benchmark against 200K rows/sec target
5. **Documentation** - Create plugin development guide using this template

This template demonstrates that **Phase 3 external plugins CAN work with Abstractions-only** - we just need to build the bridge layer to connect them to Core functionality.