# FlowEngine Plugin API Specification - Development Summary
**Purpose**: Guide for completing the Plugin API Specification in future sessions  
**Dependencies**: Core Engine Specification v1.0 (Complete)  
**Estimated Effort**: 5-7 days  
**Created**: June 27, 2025

---

## 📋 Context from Previous Sessions

### What's Been Completed
1. **Core Engine Specification** - Fully defined with:
   - Data model (Row, Dataset, Chunk)
   - Port system (IInputPort, IOutputPort, Connection)
   - DAG execution (Validator, Executor, Topological Sort)
   - Memory management (IMemoryManager, spillover)
   - Error handling (ErrorHandler, DeadLetterQueue)

2. **Critical Additions** - Implementation details for:
   - Concrete port implementations
   - DAG cycle detection algorithm
   - Dead letter queue implementation
   - Memory allocation patterns
   - Connection wiring mechanism

### Key Interfaces Plugins Must Interact With
- `IInputPort` / `IOutputPort` - For data flow
- `IDataset` - For data representation
- `IMemoryManager` - For resource allocation
- `StepExecutionContext` - For execution state
- `ErrorHandler` - For error management

---

## 🎯 Plugin API Specification Objectives

### Primary Goals
1. **Define plugin discovery and loading mechanism**
2. **Establish plugin lifecycle management**
3. **Create resource governance framework**
4. **Design configuration binding system**
5. **Implement validation architecture**
6. **Enable plugin isolation and security**

### Non-Goals for V1
- Hot-reloading of plugins
- Remote plugin loading
- Plugin marketplace/distribution
- Cross-plugin communication
- Plugin versioning conflicts resolution

---

## 📐 Key Design Decisions to Make

### 1. Plugin Discovery Strategy
**Options to Consider:**
- Directory-based scanning
- Explicit registration in configuration
- Assembly attribute marking
- Manifest files

**Recommendation**: Combine directory scanning with manifest files for security/metadata

### 2. Assembly Isolation Level
**Options to Consider:**
- Shared AppDomain (simpler, less isolation)
- AssemblyLoadContext per plugin (better isolation)
- Process isolation (maximum isolation, complex)

**Recommendation**: AssemblyLoadContext for V1, prepare interfaces for process isolation in V2

### 3. Configuration Binding
**Options to Consider:**
- Direct YAML deserialization to classes
- Dynamic configuration with validation
- Schema-first with code generation

**Recommendation**: Strongly-typed classes with JSON Schema validation

### 4. Resource Governance
**Options to Consider:**
- Trust-based (no enforcement)
- Monitoring with alerts
- Hard limits with enforcement

**Recommendation**: Monitoring in V1 with hooks for enforcement in V2

---

## 🔧 Required Interfaces and Components

### 1. Core Plugin Interfaces
```csharp
public interface IPlugin
{
    PluginMetadata Metadata { get; }
    Type[] GetStepProcessorTypes();
    void Initialize(IServiceProvider services);
}

public interface IStepProcessorFactory
{
    IStepProcessor CreateProcessor(
        StepDefinition definition,
        IStepConfiguration configuration);
}

public interface IStepConfiguration
{
    T GetConfiguration<T>() where T : class;
    bool Validate(out IReadOnlyList<ValidationError> errors);
}
```

### 2. Plugin Metadata Structure
```csharp
public sealed class PluginMetadata
{
    public string Name { get; init; }
    public Version Version { get; init; }
    public string Author { get; init; }
    public string Description { get; init; }
    public IReadOnlyList<StepTypeMetadata> ProvidedSteps { get; init; }
    public IReadOnlyList<PluginDependency> Dependencies { get; init; }
}
```

### 3. Resource Governance
```csharp
public interface IResourceGovernor
{
    ResourceQuota GetQuota(string pluginName);
    ResourceUsage GetCurrentUsage(string pluginName);
    void EnforceQuota(string pluginName, ResourceRequest request);
}
```

---

## 📦 Plugin Loading Architecture

### Component Hierarchy
```
PluginLoader
├── PluginDiscovery
│   ├── DirectoryScanner
│   ├── ManifestReader
│   └── AssemblyInspector
├── PluginIsolation
│   ├── PluginLoadContext : AssemblyLoadContext
│   ├── DependencyResolver
│   └── TypeLoader
├── PluginRegistry
│   ├── MetadataCache
│   ├── ProcessorFactoryMap
│   └── ConfigurationSchemaMap
└── PluginLifecycle
    ├── Initializer
    ├── HealthChecker
    └── Disposer
```

### Loading Sequence
1. **Discovery Phase**
   - Scan plugin directories
   - Read manifest files
   - Validate plugin signatures

2. **Loading Phase**
   - Create AssemblyLoadContext
   - Load plugin assembly
   - Resolve dependencies

3. **Registration Phase**
   - Extract metadata
   - Register step processors
   - Cache configuration schemas

4. **Initialization Phase**
   - Call plugin Initialize
   - Verify capabilities
   - Set up monitoring

---

## 🔒 Security Considerations

### Trust Boundaries
1. **Plugin Code Execution**
   - Assume plugins are untrusted
   - Limit API surface exposure
   - Monitor resource usage

2. **Configuration Access**
   - Plugins only see their configuration
   - No access to global settings
   - Validated before plugin sees it

3. **Data Access**
   - Plugins process data through ports only
   - No direct file system access in V1
   - Memory allocation through IMemoryManager

### Security Controls
- Assembly strong naming
- Digital signatures for plugins
- Capability-based permissions
- Resource quotas
- Audit logging

---

## 📋 Validation Architecture

### Three-Layer Validation
1. **Schema Validation** (JSON Schema)
   - Structure validation
   - Type checking
   - Required fields

2. **Plugin Validation** (Plugin code)
   - Business rules
   - Cross-field validation
   - External resource checks

3. **Runtime Validation** (During execution)
   - Resource availability
   - Permission checks
   - Data compatibility

### Validation Flow
```
YAML Config → JSON Schema → Plugin Validator → Runtime Checks
                   ↓              ↓                  ↓
              Schema Errors  Domain Errors   Runtime Errors
```

---

## 🧪 Testing Requirements

### Plugin Test Harness
Need to provide:
- Mock implementations of core interfaces
- Test data generators
- Performance profiling hooks
- Error injection capabilities

### Standard Test Scenarios
1. **Happy Path** - Normal data flow
2. **Error Handling** - Various failure modes
3. **Resource Limits** - Memory/CPU constraints
4. **Cancellation** - Graceful shutdown
5. **Configuration** - Invalid configs

---

## 📝 Implementation Checklist

### Phase 1: Core Interfaces
- [ ] Define IPlugin and IStepProcessor interfaces
- [ ] Create plugin metadata structures
- [ ] Design configuration binding interfaces
- [ ] Establish validation contracts

### Phase 2: Plugin Loading
- [ ] Implement AssemblyLoadContext isolation
- [ ] Create plugin discovery mechanism
- [ ] Build dependency resolution
- [ ] Implement plugin registry

### Phase 3: Configuration & Validation
- [ ] Design configuration binding system
- [ ] Implement JSON Schema validation
- [ ] Create validation error reporting
- [ ] Build configuration inheritance

### Phase 4: Resource Management
- [ ] Define resource quota structure
- [ ] Implement usage monitoring
- [ ] Create enforcement hooks
- [ ] Add diagnostic APIs

### Phase 5: Security & Isolation
- [ ] Implement assembly verification
- [ ] Create permission system
- [ ] Add audit logging
- [ ] Build security boundaries

---

## 🎨 Example Plugin Structure

### Minimal Plugin Implementation
```csharp
[Plugin("DelimitedFile", "1.0.0")]
public class DelimitedFilePlugin : IPlugin
{
    public PluginMetadata Metadata { get; }
    
    public Type[] GetStepProcessorTypes()
    {
        return new[]
        {
            typeof(DelimitedSourceProcessor),
            typeof(DelimitedSinkProcessor)
        };
    }
    
    public void Initialize(IServiceProvider services)
    {
        // Plugin initialization
    }
}

public class DelimitedSourceProcessor : StepProcessorBase
{
    private readonly DelimitedSourceConfig _config;
    
    public override async Task ExecuteAsync(StepExecutionContext context)
    {
        var outputPort = GetOutputPort("output");
        // Implementation
    }
}
```

---

## 🚀 Next Session Starting Points

### Option 1: Start with Interfaces
Begin by defining all public interfaces in detail, focusing on:
- IPlugin lifecycle
- IStepProcessor execution model
- Configuration contracts
- Validation interfaces

### Option 2: Start with Loading Mechanism
Begin with the plugin loading infrastructure:
- AssemblyLoadContext implementation
- Directory scanning
- Manifest parsing
- Dependency resolution

### Option 3: Start with Configuration
Begin with configuration and validation:
- YAML to strongly-typed binding
- JSON Schema integration
- Validation pipeline
- Error reporting

### Recommended Approach
Start with **Option 1 (Interfaces)** as it drives all other decisions and ensures a clean API surface.

---

## 📚 References and Decisions Made

### From Core Engine Spec
- Plugins interact through IStepProcessor interface
- Data flows through ports only
- Memory allocation via IMemoryManager
- Error handling through ErrorHandler

### From Project Overview v0.3
- Plugin-first architecture
- JavaScript engine abstraction for transforms
- Assembly isolation using AssemblyLoadContext
- JSON Schema validation with fallback

### Key Design Principles
1. **Isolation** - Plugins can't affect each other
2. **Safety** - Resource governance prevents abuse
3. **Simplicity** - Easy to write basic plugins
4. **Extensibility** - Complex plugins are possible
5. **Diagnostics** - Rich debugging and monitoring

---

## ❓ Open Questions for Next Session

1. **Plugin Dependencies**
   - How to handle shared dependencies between plugins?
   - Version conflict resolution strategy?

2. **JavaScript Integration**
   - Should JavaScript transform be a plugin or core feature?
   - How to abstract script engine for plugins?

3. **Performance**
   - Acceptable overhead for plugin isolation?
   - Caching strategies for plugin metadata?

4. **Future Features**
   - How to prepare for hot-reloading?
   - Remote plugin loading considerations?

---

## 📎 Appendix: Code Snippets to Consider

### Plugin Manifest Schema
```json
{
  "$schema": "http://json-schema.org/draft-07/schema#",
  "type": "object",
  "required": ["name", "version", "steps"],
  "properties": {
    "name": { "type": "string" },
    "version": { "type": "string", "pattern": "^\\d+\\.\\d+\\.\\d+$" },
    "author": { "type": "string" },
    "description": { "type": "string" },
    "steps": {
      "type": "array",
      "items": {
        "type": "object",
        "required": ["type", "processor"],
        "properties": {
          "type": { "type": "string" },
          "processor": { "type": "string" },
          "configSchema": { "type": "string" }
        }
      }
    }
  }
}
```

### Assembly Load Context
```csharp
public class PluginLoadContext : AssemblyLoadContext
{
    private readonly AssemblyDependencyResolver _resolver;
    
    public PluginLoadContext(string pluginPath) : base(isCollectible: true)
    {
        _resolver = new AssemblyDependencyResolver(pluginPath);
    }
    
    protected override Assembly? Load(AssemblyName assemblyName)
    {
        string? assemblyPath = _resolver.ResolveAssemblyToPath(assemblyName);
        return assemblyPath != null ? LoadFromAssemblyPath(assemblyPath) : null;
    }
}
```

---

This summary provides everything needed to continue developing the Plugin API Specification in the next session.