# FlowEngine High-Level Project Overview v0.4
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

### 3. **Immutable Data Model with Chunking**
- **Immutable Row structure** prevents data corruption when multiple steps consume the same data.
- **Chunked processing** ensures data flows through the pipeline without memory bottlenecks.
- Each consuming step gets its own **independent enumerator** over the dataset.
- **Configurable chunk sizes** balance memory usage with processing efficiency.
- **Memory pooling** with automatic disposal for optimal resource utilization.

### 4. **Smart Materialization for Complex Operations**
- **Streaming-first approach** with automatic detection of operations requiring full dataset access.
- **Intelligent spillover** to temporary files when memory thresholds are exceeded.
- **Operations requiring materialization**: sorting, aggregations, deduplication, joins, statistical analysis.
- **Memory-adaptive strategy**: small datasets stay in memory, large datasets spill to disk seamlessly.

### 5. **Jobs and YAML Configuration**
- Jobs are described using **YAML files** with comprehensive validation.
- Each job is a composition of named steps, wired by port connections.
- **Variable substitution** supports environment-specific deployments.
- **Hierarchical correlation IDs** provide complete execution traceability.
- **DAG cycle detection** prevents invalid job configurations.
- Branching and fan-in/fan-out is fully supported.

---

## 🧩 Plugin Architecture

FlowEngine uses a **plugin-first architecture** where all functionality is delivered through plugins that implement standardized interfaces with complete assembly isolation.

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
- **Three-layer validation**: JSON Schema → Plugin validation → Runtime checks
- **Core engine** validates job structure, connections, and plugin availability.
- **Individual plugins** are responsible for validating their own step configurations.
- **External JSON schema files** provide primary validation with fallback to plugin internal validation.
- **Rich error reporting** with plugin-specific expertise applied.

---

## 💻 JavaScript Transform Plugin (Cornerstone Feature)

The JavaScript Transform plugin is a **cornerstone component** that enables powerful, accessible data transformations with abstracted engine support.

### JavaScript Engine Abstraction
FlowEngine abstracts JavaScript execution through the `IScriptEngine` interface, allowing for engine flexibility:

**V1 Implementation**: Jint (pure .NET, excellent compatibility, no native dependencies)  
**Future Options**: ClearScript V8, Microsoft JavaScript engines, or other high-performance alternatives

### Execution Model
- **Optional lifecycle methods**: `initialize()`, `process()` (required), `cleanup()`.
- **Cached compilation**: Scripts compiled once at job startup for optimal performance.
- **Engine pooling**: Reuse JavaScript engines across row processing for efficiency.
- **Synchronous execution**: V1 focuses on sync operations for architectural simplicity.
- **Async-ready interfaces**: Designed for future async compatibility without breaking changes.

### Context API
The JavaScript runtime receives a rich **context object** with execution state and fluent APIs:
```javascript
function process() {
    // Access current data
    context.current.email = context.clean.email(context.current.email);
    
    // Validation with structured logging
    if (!context.validate.email(context.current.email)) {
        context.log.warning("Invalid email for customer {customerId}: {email}", 
            context.current.customerId, context.current.email);
        context.route.to('validation-errors');
        return false;
    }
    
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

---

## 📊 Debug Taps and Observability

FlowEngine provides comprehensive observability through **port-level debug taps** integrated with structured logging and distributed tracing.

### Debug Tap Architecture
- **Port-level debugging**: Every output port supports optional debug taps.
- **Configurable sampling**: Adjustable sampling rates to balance insight with performance.
- **Field masking**: Configurable data masking for sensitive information.
- **Logging integration**: Debug data flows through the standard .NET logging infrastructure.
- **Zero overhead when disabled**: No performance impact when debug taps are not active.

### Correlation and Tracing
**Hierarchical correlation** provides complete execution traceability:
- **Job Execution ID**: Traces all activity for a single job run
- **Step Execution ID**: Isolates problems in specific transforms  
- **Chunk ID**: Understands processing patterns in large datasets
- **Row Index**: Enables precise row-level troubleshooting

### Structured Log Output
All debug data is output as structured JSON with complete context, enabling powerful querying and analysis in log aggregation systems.

---

## 🛡️ Resource Governance and Safety

FlowEngine implements **multi-layered resource management** to ensure production stability and prevent resource exhaustion.

### Resource Management Layers
- **Process Level**: Memory pressure detection with configurable thresholds using `GC.GetTotalMemory()`
- **Plugin Level**: CPU time limits via `CancellationToken`, memory quotas via monitoring
- **Step Level**: Row processing timeouts, buffer size limits, connection quotas

### Memory Management Strategy
- **Spillover triggers**: Automatic disk spillover at configurable heap usage thresholds
- **Chunk recycling**: `ArrayPool<T>` for efficient memory reuse with proper disposal
- **Temporary file management**: LZ4 compression with automatic cleanup

### Comprehensive Error Handling
- **Dead letter queue**: File-based implementation with complete error context preservation
- **Multi-level error policies**: Job, step, and row-level error handling with retry policies
- **Backpressure management**: Channel-based flow control with configurable buffering
- **Graceful degradation**: Circuit breaker patterns for external dependencies

---

## 🏗️ Architecture

### Core Engine Foundation
The FlowEngine core provides a robust foundation with:
- **DAG execution** with topological sorting and cycle detection
- **Port system** for type-safe inter-step communication
- **Connection management** with automatic wiring and validation
- **Memory management** with intelligent spillover and pooling
- **Error propagation** with rich context and correlation tracking

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

### V1 Core Features
- ✅ **Flat record processing**: Delimited and fixed-width file formats with schema inference.
- ✅ **JavaScript transforms** with engine abstraction, rich context API, and templates.
- ✅ **Smart materialization**: Memory-first with disk spillover for sorting/aggregation operations.
- ✅ **Plugin architecture** with discovery, loading, validation, and resource governance.
- ✅ **Immutable datasets** with chunked processing and memory pooling for efficiency.
- ✅ **Debug taps**: Port-level debugging integrated with structured logging and field masking.
- ✅ **Comprehensive observability**: Correlation IDs, distributed tracing, and configurable sampling.
- ✅ **Resource governance**: Multi-layered resource management with spillover and safety limits.
- ✅ **Production-ready error handling**: Dead letter queues, retry policies, and graceful degradation.
- ✅ **Cross-platform support**: Native Linux compatibility with .NET 8.

### V1 Plugin Set
- **DelimitedSource/Sink**: CSV, TSV, pipe-delimited files with debug support and schema inference.
- **FixedWidthSource/Sink**: Mainframe-style fixed-width files with position mapping.
- **JavaScriptTransform**: Business logic, validation, routing, cleansing with multiple engine support.

### Future Enhancements (V2+)
- 🔮 **Async JavaScript functions** for external API calls and database lookups.
- 🔮 **SQL Source/Sink plugins** for database integration with connection pooling.
- 🔮 **Template-based output sinks** for reporting and document generation.
- 🔮 **File watching functionality** for real-time processing and event-driven workflows.
- 🔮 **Nested data formats** (JSON, XML) for complex data structures.
- 🔮 **Web-based management UI** with real-time debug tap visualization and job monitoring.
- 🔮 **Distributed processing** capabilities with horizontal scaling.
- 🔮 **Data lineage tracking** for compliance and auditing requirements.
- 🔮 **Schema evolution** management with migration support.

---

## 📦 .NET Implementation Details

### Core Technologies
- **Target Framework**: .NET 8.0 (cross-platform compatibility)
- **Streaming APIs**: `IAsyncEnumerable<T>`, `FileStream`, `StreamReader/Writer` for high-performance I/O.
- **JavaScript Engine**: **Jint** (V1), abstracted via `IScriptEngine` for future alternatives.
- **Plugin Loading**: `AssemblyLoadContext` for isolation with resource monitoring.
- **Configuration**: **YamlDotNet** for YAML parsing with comprehensive validation.
- **Logging**: `Microsoft.Extensions.Logging` with structured output and correlation support.
- **Memory Management**: `ArrayPool<T>` and `MemoryPool<T>` for efficient resource utilization.

### Recommended Open Source Packages
- **NJsonSchema**: JSON schema validation for plugin configurations.
- **CsvHelper**: High-performance CSV processing with streaming support.
- **McMaster.NETCore.Plugins**: Plugin architecture with assembly isolation.
- **BenchmarkDotNet**: Performance testing and optimization validation.
- **FluentValidation**: Configuration validation with rich error messages.
- **Jint**: JavaScript execution engine (pure .NET, no native dependencies).
- **Serilog**: Structured logging with multiple output providers and correlation.

### Performance Characteristics
- **Memory-bounded processing** regardless of input file sizes.
- **Configurable chunk sizes** (1K-10K rows) for memory vs. performance tuning.
- **Script compilation caching** for optimal JavaScript execution performance.
- **Immutable data structures** with efficient copying/cloning via memory pooling.
- **Debug tap sampling** prevents performance degradation during troubleshooting.
- **Spillover compression** using LZ4 for optimal disk utilization.

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
  
  errorPolicy:
    stopOnError: false
    maxConsecutiveErrors: 10
    maxErrorRate: 0.05
    deadLetterQueue: "failed-records"
  
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
- **Plugin-centric**: All functionality delivered through discoverable, isolated plugins with comprehensive resource governance.
- **JavaScript-powered**: Rich transformation capabilities accessible to both developers and business users with engine abstraction for future flexibility.
- **Observability-driven**: Comprehensive debug capabilities integrated with structured logging, correlation tracking, and field masking for security.
- **Immutable data**: Prevents corruption and enables safe branching/fan-out scenarios with efficient memory management.
- **Smart materialization**: Seamless spillover to disk for operations requiring full dataset access with adaptive memory strategies.
- **Validation-heavy**: Comprehensive validation at multiple levels prevents runtime surprises and ensures data integrity.
- **Performance-focused**: Optimized for high-throughput flat file processing with memory-adaptive strategies and pooling.
- **Production-ready**: Resource governance, comprehensive error handling, dead letter queues, and operational visibility built-in from the start.
- **Future-compatible**: Async-ready interfaces, abstracted dependencies, and extensible plugin architecture enable evolution without breaking changes.

---

## 🎯 Summary

FlowEngine "Osprey" v0.4 represents a production-ready, enterprise-grade architecture for stream-oriented data processing. The combination of plugin extensibility, JavaScript-powered transforms with engine abstraction, intelligent materialization, comprehensive debug capabilities, robust resource governance, and production-grade error handling creates a powerful yet accessible platform for ETL workflows, data validation pipelines, and file processing automation.

The focus on observability through debug taps, structured logging with correlation IDs, field masking for security, multi-layered resource management, and comprehensive error handling with dead letter queues provides the operational visibility and safety required for production deployments. The async-ready interface design, abstracted JavaScript engine support, and complete plugin isolation ensure the platform can evolve toward more advanced scenarios while maintaining backward compatibility and system stability.