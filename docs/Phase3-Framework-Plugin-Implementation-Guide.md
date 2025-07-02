# FlowEngine Phase 3 Framework Plugin Implementation Guide

**Version**: 1.0  
**Created**: January 2, 2025  
**Purpose**: Definitive guide for implementing plugins using FlowEngine framework architecture  
**Status**: MANDATORY REFERENCE - All plugin implementations must follow this guide

---

## 🎯 **Executive Summary**

This document defines the **correct** approach for implementing FlowEngine Phase 3 plugins using the framework's built-in base classes, core services, and architectural patterns. This replaces any previous implementation approaches that bypassed the framework architecture.

### **Critical Success Factors**
- ✅ Use framework base classes (PluginServiceBase, PluginProcessorBase, etc.)
- ✅ Leverage core services (IScriptEngineService, dependency injection)
- ✅ Use framework data structures (DataChunk, not custom implementations)
- ✅ Maintain 200K+ rows/sec performance through framework optimizations
- ✅ Follow five-component architecture with proper inheritance

---

## 🏗️ **Framework Architecture Overview**

### **Core Framework Components**
```
FlowEngine.Core/
├── Plugins/Base/                    # MUST inherit from these
│   ├── PluginConfigurationBase.cs  # Configuration base class
│   ├── PluginValidatorBase.cs      # Validator base class  
│   ├── PluginBase.cs               # Plugin base class
│   ├── PluginProcessorBase.cs      # Processor base class
│   └── PluginServiceBase.cs        # Service base class
├── Data/
│   ├── Chunk.cs                    # USE THIS, not custom implementations
│   └── ArrayRow.cs                 # Framework-optimized ArrayRow
├── Services/Scripting/
│   └── IScriptEngineService.cs     # Core-managed script engines
└── Extensions/
    └── ServiceCollectionExtensions.cs # DI registration patterns
```

### **Data Flow Architecture**
```
Input Data → [Framework Channel] → Processor → Service → [Framework Channel] → Output Data
                     ↑                ↑           ↑              ↑
               [Backpressure]  [Orchestration] [Business] [Performance]
               [Monitoring]    [Lifecycle]     [Logic]    [Metrics]
```

---

## 📐 **Five-Component Implementation Pattern**

### **1. Configuration Component**
```csharp
// ✅ CORRECT IMPLEMENTATION
public sealed class JavaScriptTransformConfiguration : PluginConfigurationBase
{
    /// <summary>
    /// JavaScript code to execute for each row transformation.
    /// </summary>
    [JsonPropertyName("script")]
    [JsonSchemaRequired]
    public required string Script { get; init; }

    /// <summary>
    /// Gets the script execution timeout in milliseconds.
    /// </summary>
    [JsonPropertyName("timeoutMs")]
    public int TimeoutMs { get; init; } = 5000;

    /// <summary>
    /// Gets whether to continue processing on script errors.
    /// </summary>
    [JsonPropertyName("continueOnError")]
    public bool ContinueOnError { get; init; } = false;

    // Framework automatically provides:
    // - InputSchema, OutputSchema 
    // - InputFieldIndexes, OutputFieldIndexes (for ArrayRow optimization)
    // - PluginId, BatchSize, SupportsHotSwapping
    // - Validation methods: IsCompatibleWith(), GetFieldIndex()
}
```

### **2. Validator Component**
```csharp
// ✅ CORRECT IMPLEMENTATION
public sealed class JavaScriptTransformValidator : PluginValidatorBase<JavaScriptTransformConfiguration>
{
    private readonly IScriptEngineService _scriptEngine;

    public JavaScriptTransformValidator(
        ILogger<JavaScriptTransformValidator> logger,
        IJsonSchemaValidator? jsonSchemaValidator = null,
        IScriptEngineService? scriptEngine = null)
        : base(logger, jsonSchemaValidator)
    {
        _scriptEngine = scriptEngine ?? throw new ArgumentNullException(nameof(scriptEngine));
    }

    protected override async Task<ValidationResult> ValidateConfigurationSpecificAsync(
        JavaScriptTransformConfiguration configuration, 
        CancellationToken cancellationToken = default)
    {
        var errors = new List<ValidationError>();
        var warnings = new List<ValidationWarning>();

        // Validate script syntax and security using framework service
        try
        {
            var validationResult = await _scriptEngine.ValidateAsync(
                configuration.Script,
                new ValidationOptions
                {
                    DetectInfiniteLoops = true,
                    DetectMemoryLeaks = true,
                    MaxComplexity = 100
                },
                cancellationToken);

            if (!validationResult.IsValid)
            {
                foreach (var violation in validationResult.Violations)
                {
                    errors.Add(new ValidationError(
                        $"SCRIPT_{violation.Severity.ToString().ToUpper()}",
                        violation.Description,
                        $"Script validation failed: {violation.Remediation}"));
                }
            }
        }
        catch (Exception ex)
        {
            errors.Add(new ValidationError(
                "SCRIPT_VALIDATION_FAILED", 
                "Script validation failed",
                $"Unable to validate script: {ex.Message}"));
        }

        return ValidationResult.Create(errors, warnings);
    }
}
```

### **3. Service Component**
```csharp
// ✅ CORRECT IMPLEMENTATION - Inherits from framework base
public sealed class JavaScriptTransformService : PluginServiceBase<JavaScriptTransformConfiguration>
{
    private readonly IScriptEngineService _scriptEngine;
    private CompiledScript? _compiledScript;
    private readonly object _compilationLock = new();

    public JavaScriptTransformService(
        JavaScriptTransformConfiguration configuration,
        IScriptEngineService scriptEngine,
        ILogger<JavaScriptTransformService> logger)
        : base(configuration, logger)
    {
        _scriptEngine = scriptEngine ?? throw new ArgumentNullException(nameof(scriptEngine));
    }

    public override bool CanProcess(ISchema schema)
    {
        return Configuration.IsCompatibleWith(schema);
    }

    public override ServiceMetrics GetMetrics()
    {
        return new ServiceMetrics
        {
            TotalProcessed = _totalProcessed,
            ErrorCount = _errorCount,
            AverageProcessingTimeMs = GetAverageProcessingTime(),
            MemoryUsageBytes = GC.GetTotalMemory(false),
            ThroughputPerSecond = CalculateThroughput(),
            LastProcessedAt = _lastProcessedAt
        };
    }

    // ✅ FRAMEWORK REQUIRES: Use DataChunk, not custom types
    public override async Task<DataChunk> ProcessChunkAsync(DataChunk chunk, CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        
        try
        {
            // Ensure script is compiled (framework caching handles efficiency)
            await EnsureScriptCompiledAsync(cancellationToken);

            var processedRows = new List<IArrayRow>();

            // ✅ USE FRAMEWORK OPTIMIZATION: Pre-calculated field indexes
            var inputIndexes = Configuration.InputFieldIndexes;
            
            foreach (var row in chunk.Rows)
            {
                var processedRow = await ProcessSingleRowAsync(row, inputIndexes, cancellationToken);
                processedRows.Add(processedRow);
            }

            // ✅ RETURN FRAMEWORK DATA STRUCTURE
            return new DataChunk
            {
                Schema = Configuration.OutputSchema ?? Configuration.InputSchema,
                Rows = processedRows.ToArray(),
                Metadata = chunk.Metadata
            };
        }
        finally
        {
            stopwatch.Stop();
            UpdateMetrics(stopwatch.ElapsedMilliseconds, chunk.RowCount);
        }
    }

    private async Task EnsureScriptCompiledAsync(CancellationToken cancellationToken)
    {
        if (_compiledScript != null) return;

        lock (_compilationLock)
        {
            if (_compiledScript != null) return;

            // ✅ USE FRAMEWORK SERVICE: Centralized compilation with caching
            _compiledScript = await _scriptEngine.CompileAsync(
                Configuration.Script,
                new ScriptCompilationOptions
                {
                    EngineType = ScriptEngineType.Auto,
                    OptimizationLevel = OptimizationLevel.Balanced,
                    StrictMode = true
                },
                cancellationToken);
        }
    }

    private async Task<IArrayRow> ProcessSingleRowAsync(
        IArrayRow inputRow, 
        ImmutableDictionary<string, int> fieldIndexes,
        CancellationToken cancellationToken)
    {
        // ✅ FRAMEWORK SCRIPT EXECUTION
        using var execution = await _scriptEngine.CreateExecutionAsync(
            _compiledScript!,
            new ExecutionOptions
            {
                Timeout = TimeSpan.FromMilliseconds(Configuration.TimeoutMs),
                EnableSandbox = true
            },
            cancellationToken);

        // ✅ ARRAYROW OPTIMIZATION: O(1) field access using pre-calculated indexes
        foreach (var (fieldName, index) in fieldIndexes)
        {
            var value = inputRow.GetValue(index); // O(1) access
            execution.SetGlobal(fieldName, value);
        }

        // Execute transformation
        var result = await execution.ExecuteAsync<object>(cancellationToken: cancellationToken);

        // Create output row (framework handles ArrayRow creation)
        return CreateOutputRow(result, inputRow);
    }
}
```

### **4. Processor Component**
```csharp
// ✅ CORRECT IMPLEMENTATION - Framework handles data flow
public sealed class JavaScriptTransformProcessor : PluginProcessorBase<JavaScriptTransformConfiguration, JavaScriptTransformService>
{
    public JavaScriptTransformProcessor(
        JavaScriptTransformConfiguration configuration,
        JavaScriptTransformService service,
        ILogger<JavaScriptTransformProcessor> logger)
        : base(configuration, service, logger)
    {
    }

    public override ProcessorMetrics GetMetrics()
    {
        lock (_metricsLock)
        {
            return new ProcessorMetrics
            {
                TotalProcessed = _totalProcessed,
                ErrorCount = _errorCount,
                AverageProcessingTimeMs = GetAverageProcessingTime(),
                MemoryUsageBytes = GC.GetTotalMemory(false),
                ThroughputPerSecond = CalculateThroughput(),
                LastProcessedAt = _lastProcessedAt
            };
        }
    }

    public override bool IsServiceCompatible(JavaScriptTransformService service)
    {
        return service != null && service.CanProcess(Configuration.InputSchema);
    }

    // ✅ FRAMEWORK HANDLES: channels, backpressure, lifecycle, error handling
    // We only override specific behavior if needed
}
```

### **5. Plugin Component**
```csharp
// ✅ CORRECT IMPLEMENTATION - Framework integration
public sealed class JavaScriptTransformPlugin : PluginBase<JavaScriptTransformConfiguration>
{
    public override string Id => "javascript-transform";
    public override string Name => "JavaScript Transform";
    public override string Version => "3.0.0";
    public override string Type => "Transform";
    public override string Description => "High-performance JavaScript transformation plugin";

    private readonly JavaScriptTransformService _service;
    private readonly JavaScriptTransformProcessor _processor;
    private readonly JavaScriptTransformValidator _validator;

    public JavaScriptTransformPlugin(
        JavaScriptTransformService service,
        JavaScriptTransformProcessor processor,
        JavaScriptTransformValidator validator,
        ILogger<JavaScriptTransformPlugin> logger)
        : base(logger)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
        _processor = processor ?? throw new ArgumentNullException(nameof(processor));
        _validator = validator ?? throw new ArgumentNullException(nameof(validator));
    }

    public override IPluginProcessor GetProcessor() => _processor;
    public override IPluginService GetService() => _service;
    public override IPluginValidator<IPluginConfiguration> GetValidator() => _validator;

    // ✅ FRAMEWORK HANDLES: lifecycle, hot-swapping, health checks, metrics
}
```

---

## 🔧 **Dependency Injection Setup**

### **Framework Service Registration**
```csharp
// ✅ CORRECT DI REGISTRATION in Startup/Program.cs
public static void ConfigureServices(IServiceCollection services)
{
    // Framework core services (provided by FlowEngine.Core)
    services.AddFlowEngineCore(); // Extension method registers all core services
    
    // Plugin-specific services
    services.AddScoped<JavaScriptTransformConfiguration>();
    services.AddScoped<JavaScriptTransformValidator>();
    services.AddScoped<JavaScriptTransformService>();
    services.AddScoped<JavaScriptTransformProcessor>();
    services.AddScoped<JavaScriptTransformPlugin>();
    
    // Register as plugin
    services.AddTransient<IPlugin, JavaScriptTransformPlugin>();
}
```

---

## 🚀 **Performance Requirements**

### **Framework Optimization Features**
- **ArrayRow Access**: Use `Configuration.InputFieldIndexes` for O(1) field access
- **Chunking**: Framework handles optimal chunk sizes and memory management
- **Caching**: Script compilation cached by framework
- **Pooling**: Engine pooling managed by framework
- **Metrics**: Built-in performance tracking and monitoring

### **Performance Targets**
- **Throughput**: >200K rows/sec for typical transformations
- **Memory**: Bounded usage regardless of dataset size
- **Latency**: <5ms per chunk for 1000-row chunks
- **CPU**: <80% utilization under normal load

---

## ✅ **Implementation Checklist**

### **Before Starting**
- [ ] Study framework base classes in `FlowEngine.Core.Plugins.Base`
- [ ] Review core services in `FlowEngine.Core.Services`
- [ ] Understand framework data structures (`DataChunk`, `ArrayRow`)
- [ ] Plan dependency injection registration

### **During Implementation**
- [ ] Inherit from framework base classes (never implement interfaces directly)
- [ ] Use framework data structures (never create custom `IChunk`/`IDataset` implementations)
- [ ] Leverage core services (never create custom script engines, etc.)
- [ ] Use pre-calculated field indexes for ArrayRow optimization
- [ ] Follow framework lifecycle patterns

### **After Implementation**
- [ ] Unit test each component independently
- [ ] Integration test with framework pipeline executor
- [ ] Performance benchmark against targets
- [ ] Memory usage validation with profiler
- [ ] Error handling and resilience testing

---

## 🔍 **Common Anti-Patterns to Avoid**

### **❌ WRONG - Direct Interface Implementation**
```csharp
public class MyService : IPluginService { ... } // DON'T DO THIS
```

### **✅ CORRECT - Framework Base Class Inheritance**
```csharp
public class MyService : PluginServiceBase<MyConfiguration> { ... } // DO THIS
```

### **❌ WRONG - Custom Data Structures**
```csharp
internal class MemoryChunk : IChunk { ... } // DON'T DO THIS
```

### **✅ CORRECT - Framework Data Structures**
```csharp
return new DataChunk { Schema = schema, Rows = rows }; // DO THIS
```

### **❌ WRONG - Custom Service Creation**
```csharp
private readonly ObjectPool<ScriptEngine> _engines; // DON'T DO THIS
```

### **✅ CORRECT - Framework Service Injection**
```csharp
public MyService(IScriptEngineService scriptEngine) { ... } // DO THIS
```

---

## 📚 **References**

- [FlowEngine Core Architecture](./FlowEngine-Core-Architecture.md)
- [Plugin Development Patterns](./Phase3-Plugin-Development-Guide.md)
- [Performance Optimization Guide](./FlowEngine-Performance-Guide.md)
- [Framework Base Classes API](../src/FlowEngine.Core/Plugins/Base/)

---

**⚠️ IMPORTANT**: This guide is MANDATORY for all Phase 3 plugin implementations. Any plugin that bypasses framework architecture will be rejected and require complete reimplementation.