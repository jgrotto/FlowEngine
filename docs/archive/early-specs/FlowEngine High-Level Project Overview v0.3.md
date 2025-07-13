# FlowEngine: High-Level Project Overview v0.3
## Codename: "Osprey"

## 🧭 Purpose

FlowEngine is a modular, stream-oriented data processing engine written in C#. It is designed to support configurable workflows using YAML files that define jobs consisting of source, transform, and sink steps.

The engine supports streaming-first processing with intelligent chunking for memory efficiency and performance. It is built for extensibility, separation of concerns, and developer usability, with a particular focus on flat record processing, JavaScript-based transformations, and comprehensive observability through integrated debug capabilities.

---

## ⚙️ Core Concepts

### 1. **Steps**
Each job consists of a directed acyclic graph (DAG) of _steps_. There are three types of steps:
- **Sources**: Entry points that read data from external systems (files, databases).
- **Transforms**: Operate on or combine data, can have multiple inputs/outputs.
- **Sinks**: Output processors that write data to targets.

### 2. **Ports and Data Flow**
- Each step exposes one or more **ports** as interfaces for connecting to other steps.
- **Ports wrap immutable datasets** that contain the actual data records.
- Ports provide **flow control** capabilities including buffering, backpressure, and routing.
- **Debug taps** can be enabled on any output port for real-time data inspection and logging.
- **Unconnected output ports** act as "bit buckets" (valid scenario).
- **Unconnected input ports** generate warnings as potential "dead branches".

### 3. **Immutable Datasets with Chunking**
- **Datasets are immutable** to prevent data corruption when multiple steps consume the same data.
- **Chunked processing** ensures data flows through the pipeline without memory bottlenecks.
- Each consuming step gets its own **independent enumerator** over the dataset.
- **Configurable chunk sizes** balance memory usage with processing efficiency.

### 4. **Smart Materialization for Complex Operations**
- **Streaming-first approach** with automatic detection of operations requiring full dataset access.
- **Intelligent spillover** to temporary files when memory thresholds are exceeded.
- **Operations requiring materialization**: sorting, aggregations, deduplication, joins, statistical analysis.
- **Memory-adaptive strategy**: small datasets stay in memory, large datasets spill to disk seamlessly.

### 5. **Jobs and YAML Configuration**
- Jobs are described using **YAML files** with strong validation.
- Each job is a composition of named steps, wired by port connections.
- **Variable substitution** supports environment-specific deployments.
- **Hierarchical correlation IDs** provide complete execution traceability.
- Branching and fan-in/fan-out is fully supported.

---

## 🧩 Plugin Architecture

FlowEngine uses a **plugin-first architecture** where all functionality is delivered through plugins that implement standardized interfaces.

### Plugin Components
- **Plugin Class**: Entry point implementing `IPlugin` interface.
- **Configuration Class**: Strongly typed config bound from YAML.
- **Validation Class**: Plugin-owned validation with external JSON schema support.
- **Processor Class**: Handles step execution and lifecycle management.
- **Service Class**: Performs the core processing logic.
- **JSON Schema**: **External schema file** with fallback to internal validation.

### Plugin Discovery and Loading
- **Automatic discovery** from configured plugin directories.
- **Assembly isolation** using `AssemblyLoadContext` for plugin independence.
- **Metadata extraction** from plugin manifests and assemblies.
- **Version compatibility** checking and dependency resolution.
- **Resource governance** with configurable limits per plugin.

### Plugin Validation Strategy
- **Core engine** validates job structure, connections, and plugin availability.
- **Individual plugins** are responsible for validating their own step configurations.
- **External JSON schema files** provide primary validation with fallback to plugin internal validation.
- **Rich error reporting** with plugin-specific expertise applied.

---

## 💻 JavaScript Transform Plugin (Cornerstone Feature)

The JavaScript Transform plugin is a **cornerstone component** that enables powerful, accessible data transformations with abstracted engine support.

### JavaScript Engine Abstraction
FlowEngine abstracts JavaScript execution through the `IScriptEngine` interface, allowing for engine flexibility:

```csharp
public interface IScriptEngine : IDisposable
{
    Task<ICompiledScript> CompileAsync(string script, ScriptOptions options);
    void SetGlobalObject(string name, object value);
    T GetGlobalValue<T>(string name);
}

public interface IScriptEngineFactory
{
    string EngineName { get; }
    IScriptEngine CreateEngine(ScriptEngineOptions options);
    bool SupportsFeature(ScriptFeature feature);
}
```

**V1 Implementation**: Jint (pure .NET, excellent compatibility, no native dependencies)
**Future Options**: ClearScript V8, Microsoft JavaScript engines, or other high-performance alternatives

### Execution Model
- **Optional lifecycle methods**: `initialize()`, `process()` (required), `cleanup()`.
- **Cached compilation**: Scripts compiled once at job startup for optimal performance.
- **Engine pooling**: Reuse JavaScript engines across row processing for efficiency.
- **Synchronous execution**: V1 focuses on sync operations for architectural simplicity.
- **Async-ready interfaces**: Designed for future async compatibility without breaking changes.

### Context API
The JavaScript runtime receives a rich **context object** rather than direct row manipulation:
```javascript
function process() {
    // Access current data
    context.current.email = context.clean.email(context.current.email);
    
    // Validation with logging
    if (!context.validate.email(context.current.email)) {
        context.log.warning("Invalid email for customer {customerId}: {email}", 
            context.current.customerId, context.current.email);
        context.route.to('validation-errors');
        return false;
    }
    
    // State management
    context.step.processedCount = (context.step.processedCount || 0) + 1;
    
    // Execution context access
    context.log.debug("Processing in job {jobId}, step {stepId}", 
        context.execution.jobId, context.execution.stepId);
    
    // Success routing
    context.route.to('clean-data');
    return true;
}
```

### Template System
- **Template-based transforms** for non-developer users.
- **Parameterized templates** with strong typing and validation.
- **Progressive complexity**: Basic field mapping → stateful processing → advanced logic.
- **Built-in templates** for common patterns (validation, cleansing, routing).

### Fluent APIs
Rich APIs available through the context:
- **Data Cleaning**: `context.clean.email()`, `context.clean.phone()`, etc.
- **Validation**: `context.validate.email()`, `context.validate.required()`, etc.
- **Routing**: `context.route.to()`, conditional routing, multi-port output.
- **Materialization**: `context.materialize.createBuffer()`, sort operations, aggregations.
- **Logging**: Structured logging with correlation IDs and step context.

---

## 📊 Debug Taps and Observability

FlowEngine provides comprehensive observability through **port-level debug taps** integrated with structured logging.

### Debug Tap Architecture
- **Port-level debugging**: Every output port supports optional debug taps.
- **Configurable sampling**: Adjustable sampling rates to balance insight with performance.
- **Logging integration**: Debug data flows through the standard .NET logging infrastructure.
- **Zero overhead when disabled**: No performance impact when debug taps are not active.

### Debug Configuration
```yaml
job:
  logging:
    level: Information
    providers:
      - type: Console
        level: Information
      - type: File
        level: Debug
        path: "logs/flowengine-{Date}.log"
        rollingInterval: Day
      - type: Structured
        level: Debug
        path: "logs/debug-{Date}.jsonl"
        
  debug:
    enabled: true
    sampleRate: 0.05  # 5% global sampling
    logRowData: true
    fieldMasking:
      enabled: true
      rules:
        - name: "ssn"
          pattern: "\\b\\d{3}-\\d{2}-\\d{4}\\b"
          replacement: "***-**-****"
        - name: "email"
          pattern: "\\b[\\w.-]+@[\\w.-]+\\.[A-Za-z]{2,}\\b"
          replacement: "***@***.***"
      fieldNames: ["ssn", "password", "token"]
    
steps:
  - id: validate-customers
    debug:
      sampleRate: 1.0     # Override: 100% for this critical step
      includeSchema: true
      ports: ["validation-errors"]  # Only debug the error port
```

### Correlation IDs
**Hierarchical correlation** provides complete execution traceability:
- **Job Execution ID**: Traces all activity for a single job run
- **Step Execution ID**: Isolates problems in specific transforms  
- **Chunk ID**: Understands processing patterns in large datasets
- **Row Index**: Enables precise row-level troubleshooting

### Structured Log Output
```json
{
  "timestamp": "2025-06-27T10:30:45.123Z",
  "level": "Debug",
  "category": "FlowEngine.Port.Debug",
  "message": "Port data sample",
  "jobExecutionId": "job-cust-proc-20250627-103045-abc123",
  "stepExecutionId": "step-validate-20250627-103046-def456",
  "chunkId": "chunk-003-ghi789",
  "rowIndex": 1247,
  "stepId": "validate-customers",
  "portName": "validation-errors",
  "rowData": {
    "customerId": "CUST-12345",
    "email": "***@***.***",
    "validationError": "Invalid email format"
  }
}
```

---

## 🛡️ Resource Governance and Safety

FlowEngine implements **multi-layered resource management** to ensure production stability and prevent resource exhaustion.

### Resource Management Layers
- **Process Level**: Memory pressure detection with configurable thresholds using `GC.GetTotalMemory()`
- **Plugin Level**: CPU time limits via `CancellationToken`, memory quotas via monitoring
- **Step Level**: Row processing timeouts, buffer size limits, connection quotas

### Memory Management Strategy
- **Spillover triggers**: Automatic disk spillover at 80% heap usage (configurable)
- **Chunk recycling**: `ArrayPool<T>` for efficient memory reuse
- **Temporary file management**: LZ4 compression with automatic cleanup

### Error Handling and Resilience
```yaml
job:
  errorPolicy:
    stopOnError: false
    maxConsecutiveErrors: 10
    maxErrorRate: 0.05  # 5% threshold
    deadLetterQueue: "failed-records"
    
  resourceLimits:
    maxMemoryMB: 2048
    processingTimeoutMinutes: 30
    maxTempFilesGB: 10

steps:
  - id: validate-data
    errorPolicy:
      retryPolicy:
        maxAttempts: 3
        delay: 1s
        backoffMultiplier: 2.0
    resourceLimits:
      maxRowProcessingTimeMs: 100
      maxBufferSizeMB: 256
```

---

## 🏗️ Architecture

### Projects Structure
```plaintext
FlowEngine/
├── Abstractions/         # Interfaces, ports, models, plugin contracts
├── Core/                 # DAG executor, plugin loader, job validation
├── Cli/                  # Command-line interface and job orchestration
├── Plugins/
│   ├── Delimited/        # CSV, TSV, delimited file processing
│   ├── FixedWidth/       # Fixed-width file processing
│   ├── SqlSource/        # Database connectivity (future)
│   └── JavaScriptTransform/ # JavaScript-based transformations
└── Templates/            # Built-in JavaScript templates
```

### Data Processing Flow
1. **Job Loading**: YAML parsing, variable substitution, correlation ID generation, validation.
2. **Job Preparation**: Plugin loading, script compilation, resource allocation, step processor creation.
3. **DAG Execution**: Topological execution order, data flow through ports with debug taps.
4. **Chunked Processing**: Streaming data in configurable chunks with memory monitoring.
5. **Error Handling**: Multi-level error handling with plugin-specific logic and correlation.

---

## 🖥️ Command Line Interface

FlowEngine provides a **comprehensive CLI** that delegates core functionality to the engine while providing excellent developer experience.

### Primary Commands
```bash
# Execute a job with debug enabled
flowengine run job.yaml --variable ENV=prod --log-level Debug --debug

# Validate without execution
flowengine validate job.yaml --verbose --check-files

# Display job information
flowengine info job.yaml --show-debug-ports

# List available plugins and script engines
flowengine plugins list --show-capabilities

# Monitor running job (future)
flowengine monitor job-execution-id --follow
```

### CLI Features
- **Variable substitution**: Environment variables and command-line overrides.
- **Rich output**: Color-coded results, progress tables, structured error reporting.
- **Flexible logging**: Console, file, or structured JSON output with correlation IDs.
- **Validation mode**: Comprehensive validation without job execution.
- **Debug integration**: Real-time debug tap monitoring and port data inspection.

---

## 🎯 V1 Scope and Focus

### V1 Includes
- ✅ **Flat record processing**: Delimited and fixed-width file formats.
- ✅ **JavaScript transforms** with engine abstraction, rich context API, and templates.
- ✅ **Smart materialization**: Memory-first with disk spillover for sorting/aggregation operations.
- ✅ **Plugin architecture** with discovery, loading, validation, and resource governance.
- ✅ **Immutable datasets** with chunked processing for memory efficiency.
- ✅ **Debug taps**: Port-level debugging integrated with structured logging.
- ✅ **Comprehensive observability**: Correlation IDs, field masking, and configurable sampling.
- ✅ **Resource governance**: Multi-layered resource management and safety limits.
- ✅ **Async-ready interfaces**: Future compatibility without breaking changes.
- ✅ **Cross-platform support**: Native Linux compatibility (implicit benefit).

### V1 Plugin Set
- **DelimitedSource/Sink**: CSV, TSV, pipe-delimited files with debug support.
- **FixedWidthSource/Sink**: Mainframe-style fixed-width files.
- **JavaScriptTransform**: Business logic, validation, routing, cleansing with multiple engine support.

### Future Enhancements (V2+)
- 🔮 **Async JavaScript functions** for external API calls and database lookups.
- 🔮 **SQL Source/Sink plugins** for database integration.
- 🔮 **Template-based output sinks** for reporting and document generation.
- 🔮 **File watching functionality** for real-time processing.
- 🔮 **Nested data formats** (JSON, XML) for complex data structures.
- 🔮 **Web-based management UI** with real-time debug tap visualization.
- 🔮 **Distributed processing** capabilities.
- 🔮 **Data lineage tracking** for compliance and auditing.
- 🔮 **Schema evolution** management with migration support.

---

## 📦 .NET Implementation Details

### Core Technologies
- **Target Framework**: .NET 8.0 (cross-platform compatibility)
- **Streaming APIs**: `IAsyncEnumerable<T>`, `FileStream`, `StreamReader/Writer`.
- **JavaScript Engine**: **Jint** (V1), abstracted via `IScriptEngine` for future alternatives.
- **Plugin Loading**: `AssemblyLoadContext` for isolation with resource monitoring.
- **Configuration**: **YamlDotNet** for YAML parsing.
- **Logging**: `Microsoft.Extensions.Logging` with structured output support.

### Recommended Open Source Packages
- **NJsonSchema**: JSON schema validation for plugin configurations.
- **CsvHelper**: High-performance CSV processing with streaming support.
- **McMaster.NETCore.Plugins**: Plugin architecture with assembly isolation.
- **BenchmarkDotNet**: Performance testing and optimization.
- **FluentValidation**: Configuration validation with rich error messages.
- **Jint**: JavaScript execution engine (pure .NET).
- **Serilog**: Structured logging with multiple output providers.

### Performance Characteristics
- **Memory-bounded processing** regardless of file sizes.
- **Configurable chunk sizes** (1K-10K rows) for memory vs. performance tuning.
- **Script compilation caching** for optimal JavaScript execution.
- **Immutable data structures** with efficient copying/cloning.
- **Debug tap sampling** prevents performance degradation during troubleshooting.

---

## 📁 Job Configuration Example

```yaml
job:
  name: "customer-data-processing"
  
  logging:
    level: Information
    providers:
      - type: File
        path: "logs/customer-processing-{Date}.log"
        level: Debug
  
  debug:
    enabled: true
    sampleRate: 0.1
    fieldMasking:
      enabled: true
      fieldNames: ["ssn", "creditCard"]
  
  resourceLimits:
    maxMemoryMB: 1024
    processingTimeoutMinutes: 15
  
  steps:
    - id: read-customers
      type: DelimitedSource
      config:
        path: "${INPUT_PATH}/customers.csv"
        delimiter: ","
        hasHeader: true
        chunkSize: 2000
    
    - id: validate-and-clean
      type: JavaScriptTransform
      debug:
        sampleRate: 1.0  # Debug all validation decisions
        ports: ["validation-errors"]
      config:
        template: "customer-validation"
        parameters:
          requiredFields: ["customerId", "email", "firstName"]
          emailDomain: "${COMPANY_DOMAIN}"
    
    - id: categorize-customers
      type: JavaScriptTransform
      config:
        script: |
          function process() {
            const value = parseFloat(context.current.lifetimeValue) || 0;
            
            context.log.debug("Categorizing customer {customerId} with lifetime value {value}", 
                context.current.customerId, value);
            
            if (value > 10000) {
              context.current.tier = 'vip';
              context.route.to('vip-output');
            } else {
              context.current.tier = 'standard';
              context.route.to('standard-output');
            }
            
            return true;
          }
    
    - id: write-vip-customers
      type: DelimitedSink
      config:
        path: "${OUTPUT_PATH}/vip-customers.csv"
        writeHeader: true
    
    - id: write-standard-customers
      type: DelimitedSink
      config:
        path: "${OUTPUT_PATH}/standard-customers.csv"
        writeHeader: true
  
  connections:
    - from: read-customers.output
      to: validate-and-clean.input
    - from: validate-and-clean.output
      to: categorize-customers.input
    - from: categorize-customers.vip-output
      to: write-vip-customers.input
    - from: categorize-customers.standard-output
      to: write-standard-customers.input
```

---

## ✅ Design Philosophy

- **Streaming-first**: The engine favors pipelined data flow with intelligent chunking and automatic materialization when needed.
- **Plugin-centric**: All functionality delivered through discoverable, isolated plugins with resource governance.
- **JavaScript-powered**: Rich transformation capabilities accessible to both developers and business users with engine abstraction.
- **Observability-driven**: Comprehensive debug capabilities integrated with structured logging and correlation tracking.
- **Immutable data**: Prevents corruption and enables safe branching/fan-out scenarios.
- **Smart materialization**: Seamless spillover to disk for operations requiring full dataset access.
- **Validation-heavy**: Comprehensive validation at job load time prevents runtime surprises.
- **Performance-focused**: Optimized for high-throughput flat file processing with memory-adaptive strategies.
- **Production-ready**: Resource governance, error handling, and operational visibility built-in from the start.
- **Future-compatible**: Async-ready interfaces and abstracted dependencies enable evolution without breaking changes.

---

## 🎯 Summary

FlowEngine "Osprey" v0.3 represents a production-ready, enterprise-grade architecture for stream-oriented data processing. The combination of plugin extensibility, JavaScript-powered transforms with engine abstraction, intelligent materialization, comprehensive debug capabilities, and robust resource governance creates a powerful yet accessible platform for ETL workflows, data validation pipelines, and file processing automation.

The focus on observability through debug taps, structured logging with correlation IDs, field masking for security, and multi-layered resource management provides the operational visibility and safety required for production deployments. The async-ready interface design and abstracted JavaScript engine support ensure the platform can evolve toward more advanced scenarios while maintaining backward compatibility.