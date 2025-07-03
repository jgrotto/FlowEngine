# Phase 3 Core/Abstractions Bridge Requirements

**Date**: July 3, 2025  
**Status**: Critical Integration Gaps Identified  
**Source**: Template Plugin Build Analysis  

## Executive Summary

The Phase 3 Template Plugin has successfully revealed the **exact integration gaps** between FlowEngine.Abstractions and the types that external plugins need. This document defines the required bridge components to enable Abstractions-only external plugins.

---

## Critical Finding

The template plugin **validates our architectural approach** - external plugins CAN work with Abstractions-only dependencies. We just need to:

1. **Add missing types to Abstractions**
2. **Create bridge services in Core**
3. **Implement factory patterns for Core types**

## Missing Types in Abstractions

### Data Types (FlowEngine.Abstractions.Data namespace missing)
```csharp
// Required in Abstractions:
namespace FlowEngine.Abstractions.Data
{
    public interface ISchema { /* ... */ }
    public interface IArrayRow { /* ... */ }
    public interface IChunk { /* ... */ }
    public interface IDataset { /* ... */ }
    public interface IColumnDefinition { /* ... */ }
}
```

### Plugin Types (FlowEngine.Abstractions.Plugins namespace incomplete)
```csharp
// Missing from Abstractions:
public interface IPluginConfiguration { /* ... */ }
public interface IPluginProcessor { /* ... */ }
public interface IPluginService { /* ... */ }
public interface IPluginValidator<T> { /* ... */ }

public enum PluginState { Created, Initialized, Running, Stopped, Failed, Disposed }
public enum ProcessorState { Created, Initialized, Running, Stopped, Failed, Disposed }

// Result types
public class PluginInitializationResult { /* ... */ }
public class ValidationResult { /* ... */ }
public class SchemaCompatibilityResult { /* ... */ }
public class ArrayRowOptimizationResult { /* ... */ }
public class HotSwapResult { /* ... */ }

// Metrics types
public class ServiceMetrics { /* ... */ }
public class ProcessorMetrics { /* ... */ }
public class ServiceHealth { /* ... */ }
public class ProcessorHealth { /* ... */ }
```

### Validation Types
```csharp
// Missing validation infrastructure:
public class ValidationError { /* ... */ }
public class ValidationWarning { /* ... */ }
public class CompatibilityIssue { /* ... */ }
public class OptimizationIssue { /* ... */ }
public enum ValidationSeverity { Info, Warning, Error }
public enum HealthStatus { Healthy, Degraded, Unhealthy }
```

## Interface Misalignment Issues

### Current IPlugin Interface vs Plugin Needs
```csharp
// Current IPlugin in Abstractions (from Phase 2):
public interface IPlugin
{
    string Name { get; }
    string Version { get; }
    ISchema? OutputSchema { get; }         // ❌ References missing ISchema
    IReadOnlyDictionary<string, object>? Metadata { get; }
    
    Task InitializeAsync(IConfiguration config, CancellationToken cancellationToken = default);
    PluginValidationResult ValidateInputSchema(ISchema? inputSchema);  // ❌ Wrong return type
    PluginMetrics GetMetrics();            // ❌ Wrong return type
}

// Required IPlugin for Phase 3 external plugins:
public interface IPlugin
{
    // Identity
    string Id { get; }
    string Name { get; }
    string Version { get; }
    string Type { get; }
    string Description { get; }
    string Author { get; }
    
    // State Management
    PluginState Status { get; }
    bool SupportsHotSwapping { get; }
    IPluginConfiguration? Configuration { get; }
    ImmutableArray<string> SupportedCapabilities { get; }
    ImmutableArray<string> Dependencies { get; }
    
    // Lifecycle
    Task<PluginInitializationResult> InitializeAsync(IPluginConfiguration configuration, CancellationToken cancellationToken = default);
    Task StartAsync(CancellationToken cancellationToken = default);
    Task StopAsync(CancellationToken cancellationToken = default);
    Task<ServiceHealth> CheckHealthAsync(CancellationToken cancellationToken = default);
    ServiceMetrics GetMetrics();
    Task<HotSwapResult> HotSwapAsync(IPluginConfiguration newConfiguration, CancellationToken cancellationToken = default);
    
    // Component Factory
    IPluginProcessor GetProcessor();
    IPluginService GetService();
    IPluginValidator<IPluginConfiguration> GetValidator();
}
```

## Required Bridge Services

### 1. Factory Interfaces (in Abstractions)
```csharp
namespace FlowEngine.Abstractions.Services
{
    public interface ISchemaFactory
    {
        ISchema Create(IEnumerable<IColumnDefinition> columns);
        ISchema CreateWithIndexes(IEnumerable<IColumnDefinition> columns);
    }
    
    public interface IArrayRowFactory
    {
        IArrayRow Create(ISchema schema, object?[] values);
        IArrayRow CreateOptimized(ISchema schema, object?[] values);
    }
    
    public interface IChunkFactory
    {
        IChunk Create(ISchema schema, IEnumerable<IArrayRow> rows, IReadOnlyDictionary<string, object>? metadata = null);
    }
    
    public interface IDatasetFactory
    {
        IDataset Create(ISchema schema, IEnumerable<IChunk> chunks);
    }
}
```

### 2. Core Service Interfaces (in Abstractions)
```csharp
namespace FlowEngine.Abstractions.Services
{
    public interface IScriptEngineService
    {
        Task<object?> ExecuteAsync(string script, IReadOnlyDictionary<string, object?> parameters, CancellationToken cancellationToken = default);
        Task<T?> ExecuteAsync<T>(string script, IReadOnlyDictionary<string, object?> parameters, CancellationToken cancellationToken = default);
        bool ValidateScript(string script, out string[] errors);
    }
    
    public interface IPerformanceCounters
    {
        void RecordProcessingTime(TimeSpan processingTime);
        void RecordThroughput(long rowCount, TimeSpan duration);
        void RecordMemoryUsage(long memoryBytes);
        PerformanceMetrics GetMetrics();
    }
    
    public interface IDataTypeService
    {
        bool IsValidType(Type type);
        T? ConvertValue<T>(object? value);
        bool TryConvertValue<T>(object? value, out T? result);
    }
}
```

## Implementation Strategy

### Phase 1: Add Missing Types to Abstractions (1-2 Days)

**Objective**: Add all missing interfaces and types to make template plugin compile

**Tasks**:
1. **Create FlowEngine.Abstractions.Data namespace**
   - Add ISchema, IArrayRow, IChunk, IDataset interfaces
   - Add ColumnDefinition and related types

2. **Complete FlowEngine.Abstractions.Plugins namespace**
   - Add missing interfaces (IPluginConfiguration, IPluginProcessor, etc.)
   - Add all enum types (PluginState, ProcessorState, etc.)
   - Add all result types (PluginInitializationResult, etc.)

3. **Add FlowEngine.Abstractions.Services namespace**
   - Add factory interfaces for Core type creation
   - Add Core service interfaces for DI

4. **Update existing IPlugin interface**
   - Replace with Phase 3 compatible interface
   - Maintain backward compatibility where possible

### Phase 2: Implement Core Bridges (2-3 Days)

**Objective**: Create Core implementations of factory and service interfaces

**Tasks**:
1. **Implement Factory Services in Core**
   ```csharp
   // In FlowEngine.Core
   public class SchemaFactory : ISchemaFactory
   {
       public ISchema Create(IEnumerable<IColumnDefinition> columns)
       {
           // Bridge to existing Core Schema.Create()
           return Schema.Create(columns.ToArray());
       }
   }
   ```

2. **Register Bridge Services in Core DI**
   ```csharp
   services.AddSingleton<ISchemaFactory, SchemaFactory>();
   services.AddSingleton<IArrayRowFactory, ArrayRowFactory>();
   services.AddSingleton<IChunkFactory, ChunkFactory>();
   services.AddSingleton<IDatasetFactory, DatasetFactory>();
   ```

3. **Update Plugin Loading to Inject Bridge Services**
   ```csharp
   // External plugins get factory services injected
   var pluginServices = externalPluginServiceProvider;
   pluginServices.AddSingleton(coreServiceProvider.GetService<ISchemaFactory>());
   pluginServices.AddSingleton(coreServiceProvider.GetService<IArrayRowFactory>());
   ```

### Phase 3: Test Template Plugin Integration (1 Day)

**Objective**: Verify template plugin works with Core through bridges

**Tasks**:
1. Update template plugin to use factory services
2. Test plugin loading and execution in Core
3. Validate performance targets (200K+ rows/sec)
4. Verify ArrayRow optimization works correctly

## Expected Outcomes

### After Phase 1 (Add Types to Abstractions)
```bash
# Template plugin should compile successfully
cd plugins/Phase3TemplatePlugin
dotnet build --configuration Release
# Success: 0 errors

# Verify no Core dependencies
dotnet list package --include-transitive
# Should show ONLY FlowEngine.Abstractions
```

### After Phase 2 (Implement Core Bridges)
```bash
# Core should build with bridge implementations
dotnet build src/FlowEngine.Core --configuration Release
# Success: 0 errors

# Integration test
dotnet test --filter "ExternalPluginIntegrationTests"
# All tests pass
```

### After Phase 3 (Test Template Plugin)
```csharp
[Test]
public async Task TemplatePlugin_LoadsAndExecutesCorrectly()
{
    // Load external plugin from assembly
    var plugin = await pluginManager.LoadExternalPluginAsync("Phase3TemplatePlugin.dll");
    
    // Initialize and start
    await plugin.InitializeAsync(config);
    await plugin.StartAsync();
    
    // Process data
    var processor = plugin.GetProcessor();
    var result = await processor.ProcessAsync(testDataset);
    
    // Verify results
    Assert.IsNotNull(result);
    Assert.IsTrue(result.RowCount > 0);
    
    // Verify performance
    var metrics = processor.GetMetrics();
    Assert.IsTrue(metrics.ThroughputPerSecond >= 200_000);
}
```

## Bridge Architecture Diagram

```
┌─────────────────────────────────────────────────────────────┐
│                    External Plugin                          │
│  ┌─────────────┐ ┌─────────────┐ ┌─────────────┐          │
│  │Configuration│ │ Validator   │ │   Plugin    │          │
│  └─────────────┘ └─────────────┘ └─────────────┘          │
│  ┌─────────────┐ ┌─────────────┐                          │
│  │ Processor   │ │  Service    │                          │
│  └─────────────┘ └─────────────┘                          │
└─────────────────────┬───────────────────────────────────────┘
                      │ ONLY FlowEngine.Abstractions
                      │
┌─────────────────────▼───────────────────────────────────────┐
│              FlowEngine.Abstractions                        │
│  ┌─────────────────────────────────────────────────────────┐ │
│  │ Data: ISchema, IArrayRow, IChunk, IDataset            │ │
│  │ Plugins: IPlugin, IPluginProcessor, IPluginService   │ │  
│  │ Services: ISchemaFactory, IArrayRowFactory            │ │
│  └─────────────────────────────────────────────────────────┘ │
└─────────────────────┬───────────────────────────────────────┘
                      │ Bridge Layer (DI Injection)
                      │
┌─────────────────────▼───────────────────────────────────────┐
│                FlowEngine.Core                              │
│  ┌─────────────────────────────────────────────────────────┐ │
│  │ Bridge Implementations:                                │ │
│  │ • SchemaFactory → Core.Schema.Create()                │ │
│  │ • ArrayRowFactory → Core.ArrayRow constructor         │ │
│  │ • ChunkFactory → Core.Chunk constructor               │ │
│  │ • ScriptEngineService → Core script execution         │ │
│  └─────────────────────────────────────────────────────────┘ │
└─────────────────────────────────────────────────────────────┘
```

## Success Criteria

1. ✅ **Template Plugin Compiles**: 0 errors with Abstractions-only
2. ✅ **Core Bridges Work**: Factory services create Core types correctly
3. ✅ **External Plugin Loads**: Plugin manager can load and execute template plugin
4. ✅ **Performance Target**: Template plugin achieves 200K+ rows/sec
5. ✅ **DI Integration**: Core services properly injected into external plugins
6. ✅ **Hot-Swapping**: Configuration updates work without restarts

This bridge architecture enables **true external plugin isolation** while maintaining **full Core functionality** access through controlled interfaces.