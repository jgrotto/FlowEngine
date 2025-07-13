# FlowEngine: High-Level Project Overview v0.2
## Codename: "Osprey"

## 🧭 Purpose

FlowEngine is a modular, stream-oriented data processing engine written in C#. It is designed to support configurable workflows using YAML files that define jobs consisting of source, transform, and sink steps.

The engine supports streaming-first processing with intelligent chunking for memory efficiency and performance. It is built for extensibility, separation of concerns, and developer usability, with a particular focus on flat record processing and JavaScript-based transformations.

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

### 4. **Jobs and YAML Configuration**
- Jobs are described using **YAML files** with strong validation.
- Each job is a composition of named steps, wired by port connections.
- **Variable substitution** supports environment-specific deployments.
- Branching and fan-in/fan-out is fully supported.

### 5. **Materialization Operations**
- **Automatic detection** of transforms requiring full dataset access (sorting, aggregation).
- **Memory-first with disk spillover** for large dataset operations.
- **JavaScript materialization APIs** for complex operations like sorting and grouping.
- **Temporary file management** with automatic cleanup and compression support.

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

### Plugin Validation Strategy
- **Core engine** validates job structure, connections, and plugin availability.
- **Individual plugins** are responsible for validating their own step configurations.
- **External JSON schema files** provide primary validation with fallback to plugin internal validation.
- **Rich error reporting** with plugin-specific expertise applied.

---

## 💻 JavaScript Transform Plugin (Cornerstone Feature)

The JavaScript Transform plugin is a **cornerstone component** that enables powerful, accessible data transformations.

### Execution Model
- **Optional lifecycle methods**: `initialize()`, `process()` (required), `cleanup()`.
- **Cached compilation**: Scripts compiled once at job startup for optimal performance.
- **Engine pooling**: Reuse JavaScript engines across row processing for efficiency.
- **Synchronous execution**: V1 focuses on sync operations for architectural simplicity.

### Context API
The JavaScript runtime receives a rich **context object** rather than direct row manipulation:
```javascript
function process() {
    // Access current data
    context.current.email = context.clean.email(context.current.email);
    
    // Validation
    if (!context.validate.email(context.current.email)) {
        context.route.to('validation-errors');
        return false;
    }
    
    // State management
    context.step.processedCount = (context.step.processedCount || 0) + 1;
    
    // Success routing
    context.route.to('clean-data');
    return true; // Success/failure indicator
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
- **Logging**: Structured logging with step context.

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
1. **Job Loading**: YAML parsing, variable substitution, validation.
2. **Job Preparation**: Plugin loading, script compilation, step processor creation.
3. **DAG Execution**: Topological execution order, data flow through ports.
4. **Chunked Processing**: Streaming data in configurable chunks.
5. **Error Handling**: Multi-level error handling with plugin-specific logic.

---

## 🖥️ Command Line Interface

FlowEngine provides a **slim CLI** that delegates core functionality to the engine while providing excellent developer experience.

### Primary Commands
```bash
# Execute a job
flowengine run job.yaml --variable ENV=prod --log-level Debug

# Validate without execution
flowengine validate job.yaml --verbose --check-files

# Display job information
flowengine info job.yaml

# List available plugins
flowengine plugins list

# File watching (future)
flowengine watch job.yaml --watch-path /data/incoming --pattern "*.csv"
```

### CLI Features
- **Variable substitution**: Environment variables and command-line overrides.
- **Rich output**: Color-coded results, progress tables, structured error reporting.
- **Flexible logging**: Console, file, or structured JSON output.
- **Validation mode**: Comprehensive validation without job execution.

---

## 🎯 V1 Scope and Focus

### V1 Includes
- ✅ **Flat record processing**: Delimited and fixed-width file formats.
- ✅ **Synchronous JavaScript transforms** with rich context API and templates.
- ✅ **Smart materialization**: Memory-first with disk spillover for sorting/aggregation operations.
- ✅ **Plugin architecture** with discovery, loading, and validation.
- ✅ **Immutable datasets** with chunked processing for memory efficiency.
- ✅ **Comprehensive CLI** with validation, execution, and monitoring.
- ✅ **Robust error handling** at job, step, and row levels.
- ✅ **Template system** for JavaScript transforms including materialization operations.

### V1 Plugin Set
- **DelimitedSource/Sink**: CSV, TSV, pipe-delimited files.
- **FixedWidthSource/Sink**: Mainframe-style fixed-width files.
- **JavaScriptTransform**: Business logic, validation, routing, cleansing.

### Future Enhancements (V2+)
- 🔮 **Async JavaScript functions** for external API calls and database lookups.
- 🔮 **SQL Source/Sink plugins** for database integration.
- 🔮 **Template-based output sinks** for reporting and document generation:
  - **Reporting Sink**: Generate HTML/Excel reports from data using templates
  - **PDF Composition Sink**: Create formatted PDF documents with charts and tables
  - **Email Notification Sink**: Send templated emails with processed data summaries
  - **Dashboard Data Sink**: Push processed data to visualization platforms
- 🔮 **File watching functionality** for real-time processing.
- 🔮 **Nested data formats** (JSON, XML) for complex data structures.
- 🔮 **Web-based management UI** for job monitoring and management.
- 🔮 **Distributed processing** capabilities.

---

## 🛡️ Error Handling Strategy

### Multi-Level Error Handling
- **Job Level**: YAML validation, plugin availability, connection validation.
- **Step Level**: Plugin-specific validation, resource availability, execution policies.
- **Row Level**: Data validation, transform failures, routing decisions.

### Error Policies
```yaml
job:
  errorPolicy:
    stopOnError: false
    maxConsecutiveErrors: 10
    maxErrorRate: 0.05  # 5% threshold
    deadLetterQueue: "failed-records"

steps:
  - id: validate-data
    errorPolicy:
      retryPolicy:
        maxAttempts: 3
        delay: 1s
```

### Plugin-Owned Validation
- **External JSON schemas** provide primary validation for plugin configurations.
- **Fallback to internal validation** ensures robustness.
- **Rich error reporting** with plugin-specific expertise.

---

## 📦 .NET Implementation Details

### Core Technologies
- **Target Framework**: .NET 8.0
- **Streaming APIs**: `IAsyncEnumerable<T>`, `FileStream`, `StreamReader/Writer`.
- **JavaScript Engine**: **Jint** (pure .NET, good performance, no native dependencies).
- **Plugin Loading**: `AssemblyLoadContext` for isolation.
- **Configuration**: **YamlDotNet** for YAML parsing.

### Recommended Open Source Packages
- **NJsonSchema**: JSON schema validation.
- **CsvHelper**: High-performance CSV processing.
- **McMaster.NETCore.Plugins**: Plugin architecture with assembly isolation.
- **BenchmarkDotNet**: Performance testing and optimization.
- **FluentValidation**: Configuration validation.
- **Polly**: Resilience patterns (future).

### Performance Characteristics
- **Memory-bounded processing** regardless of file sizes.
- **Configurable chunk sizes** (1K-10K rows) for memory vs. performance tuning.
- **Script compilation caching** for optimal JavaScript execution.
- **Immutable data structures** with efficient copying/cloning.

---

## 📁 Job Configuration Example

```yaml
job:
  name: "customer-data-processing"
  
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
    - from: categorize-customers.output
      to: sort-by-value.input
    - from: sort-by-value.output
      to: write-vip-customers.input
```

---

## ✅ Design Philosophy

- **Streaming-first**: The engine favors pipelined data flow with intelligent chunking and automatic materialization when needed.
- **Plugin-centric**: All functionality delivered through discoverable, isolated plugins.
- **JavaScript-powered**: Rich transformation capabilities accessible to both developers and business users.
- **Immutable data**: Prevents corruption and enables safe branching/fan-out scenarios.
- **Smart materialization**: Seamless spillover to disk for operations requiring full dataset access.
- **Validation-heavy**: Comprehensive validation at job load time prevents runtime surprises.
- **Performance-focused**: Optimized for high-throughput flat file processing with memory-adaptive strategies.
- **Developer-friendly**: Rich CLI, clear error messages, comprehensive logging.

---

## 🎯 Summary

FlowEngine "Osprey" v0.2 represents a mature, production-ready architecture for stream-oriented data processing. The combination of plugin extensibility, JavaScript-powered transforms, intelligent materialization, and robust CLI tooling creates a powerful yet accessible platform for ETL workflows, data validation pipelines, and file processing automation.

The focus on flat record processing, synchronous operations, smart materialization for complex operations, and comprehensive validation provides a solid foundation that can evolve toward more advanced scenarios like async operations, nested data formats, and distributed processing in future versions.