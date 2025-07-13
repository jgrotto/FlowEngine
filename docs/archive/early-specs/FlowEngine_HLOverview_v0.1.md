# FlowEngine: High-Level Project Overview

## 🧭 Purpose

FlowEngine is a modular, stream-oriented data processing engine written in C#. It is designed to support configurable workflows using YAML files that define jobs consisting of source, transform, and sink steps.

The engine supports both streaming-first processing with intelligent fallback to materialization (e.g., for sorting or aggregation). It is built for extensibility, separation of concerns, and developer usability.

---

## ⚙️ Core Concepts

### 1. **Steps**
Each job consists of a directed acyclic graph (DAG) of _steps_. There are three types of steps:
- **Sources**: Entry points that read data from external systems.
- **Transforms**: Operate on or combine data, can have multiple inputs/outputs.
- **Sinks**: Output processors that write data to targets.

### 2. **Ports**
- Each step exposes one or more **ports** as interfaces for connecting to other steps.
- Ports are either **input** or **output**, and are used to define the data flow.
- Steps do not know what they are connected to — they are connected by configuration via ports.

### 3. **Jobs and YAML Configuration**
- Jobs are described using YAML files.
- Each job is a composition of named steps, wired by port connections.
- Branching and fan-in/fan-out is supported.

---

## 🧩 Plugin Architecture

Plugins extend functionality and follow a standard structure:

### Plugin Components
- `Plugin Class`: Entry point for engine to instantiate plugin.
- `Configuration Class`: Strongly typed config bound from YAML.
- `Validation Class`: Performs internal + schema-based validation.
- `Processor Class`: Wires inputs/outputs and manages the lifecycle.
- `Service Class`: Performs the core logic (e.g., reading/writing).
- `JSON Schema`: External file used to validate YAML configuration.

### Plugin Rules
- Plugins implement interfaces from `FlowEngine.Abstractions`.
- They do **not** depend on the core engine directly.
- They may optionally use **core services** (like JavaScript engines) via injected abstractions.

---

## 🧱 Architecture

### Projects

```plaintext
FlowEngine/
├── Abstractions/         # Interfaces, ports, models
├── Core/                 # DAG executor, plugin loader
├── Cli/                  # Command-line runner
├── Plugins/
│   ├── Delimited/        # Example CSV plugin
│   └── ...
```

---

## 📦 .NET Specific Considerations

- **Streaming APIs**: Prefer `FileStream`, `StreamReader`, and `StreamWriter` for high-performance streaming.
- **Async IO**: Use `async`/`await` with `ReadLineAsync`, `WriteLineAsync` where applicable.
- **Immutable Data**: Internal data rows (`Row`) should be treated as immutable; use cloning/copying to apply changes.
- **Dependency Injection**: Services should be injected using `IServiceProvider` to maintain testability and extensibility.
- **JSON Schema Validation**: Use libraries like `NJsonSchema` for validating plugin configurations.
- **Dynamic Loading**: Plugins can be dynamically loaded from DLLs using reflection.

---

## 🔍 Design Philosophy

- **Streaming-first**: The engine favors pipelined data flow.
- **Isolated Steps**: Each step is stateless and only communicates through defined ports.
- **Composable & Reusable**: All components can be reused or extended through the plugin system.
- **Declarative Jobs**: Users define jobs in YAML with validation before execution.

---

## 🔜 Future Extensions

- Web-based UI for job management
- Plugin discovery and publishing system
- Built-in metrics and tracing
- UI-based visual DAG editor

---

## 📁 File Example (YAML)

```yaml
job:
  name: sample-job
  steps:
    - id: source-csv
      type: DelimitedSource
      config:
        path: /data/input.csv
    - id: transform-uppercase
      type: JavaScriptTransform
      config:
        script: "row.col1 = row.col1.toUpperCase(); return row;"
    - id: output-csv
      type: DelimitedSink
      config:
        path: /data/output.csv
  connections:
    - from: source-csv.rows
      to: transform-uppercase.input
    - from: transform-uppercase.output
      to: output-csv.input
```

---

## ✅ Summary

FlowEngine provides a robust foundation for building modular, declarative, and high-performance data workflows in .NET. With strong plugin architecture, streaming focus, and extensibility via YAML and interfaces, it is well-suited for ETL, transformation pipelines, and custom data processing tasks.