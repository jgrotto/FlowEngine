# FlowEngine

**High-Performance Data Processing Engine for .NET**

FlowEngine is a modular, stream-oriented data processing engine built for C#/.NET that delivers exceptional performance while maintaining memory-bounded operation. Process unlimited datasets through configurable pipelines with a plugin architecture designed for both simplicity and extensibility.

## Key Features

🚀 **High Performance**: 200K+ rows/sec throughput with 3.25x improvement over traditional approaches  
🔄 **Stream Processing**: Handle unlimited data sizes with memory-bounded operation  
🧩 **Plugin Architecture**: Extend functionality through isolated, schema-aware plugins  
📋 **Schema-First Design**: Type-safe data transformation with compile-time validation  
⚡ **ArrayRow Technology**: O(1) field access with 60-70% memory reduction  
🔀 **DAG Execution**: Complex workflows with parallel execution capabilities  

## Quick Start

### Installation
```bash
git clone https://github.com/yourorg/flowengine.git
cd flowengine
dotnet build --configuration Release
```

### Hello World Pipeline

Create a simple pipeline configuration (`hello-world.yaml`):
```yaml
pipeline:
  name: "HelloWorld"
  
  plugins:
    - name: "source"
      type: "DelimitedSource"
      config:
        filePath: "data/customers.csv"
        delimiter: ","
        hasHeaders: true
        
        # Define the data structure this plugin outputs
        columns:
          - name: "customerId"
            type: "int32"
            index: 0
            required: true
          - name: "firstName"
            type: "string"
            index: 1
            required: true
          - name: "lastName"
            type: "string"
            index: 2
            required: true
          - name: "email"
            type: "string"
            index: 3
            required: false
    
    - name: "transform"
      type: "JavaScriptTransform"
      config:
        # Define the data structure this plugin outputs
        columns:
          - name: "customerId"
            type: "int32"
            index: 0
          - name: "fullName"
            type: "string"
            index: 1
          - name: "email"
            type: "string"
            index: 2
        
        script: |
          function transform(row) {
            return {
              customerId: row.customerId,
              fullName: row.firstName + ' ' + row.lastName,
              email: row.email || ''
            };
          }
    
    - name: "sink"
      type: "DelimitedSink"
      config:
        filePath: "output/processed.csv"
        # Sink doesn't need column definitions - it uses what it receives
        
  connections:
    - from: "source"
      to: "transform"
    - from: "transform"
      to: "sink"
```

Run the pipeline:
```bash
FlowEngine.exe hello-world.yaml
```

## Architecture

FlowEngine processes data through a **directed acyclic graph (DAG)** of plugins connected via typed ports:

```
┌─────────────┐    ┌─────────────┐    ┌─────────────┐
│   Source    │    │ Transform   │    │    Sink     │
│   Plugin    ├───►│   Plugin    ├───►│   Plugin    │
│             │    │             │    │             │
└─────────────┘    └─────────────┘    └─────────────┘
```

### Core Components

- **ArrayRow**: High-performance data structure with O(1) field access
- **Schema System**: Type-safe data transformation with validation
- **Plugin System**: Isolated plugins with resource governance
- **Execution Engine**: Parallel DAG execution with error handling
- **Channel System**: Memory-bounded streaming between components

## Built-in Plugins

### Sources
- **DelimitedSource**: CSV, TSV, and custom delimited files
- **FixedWidthSource**: Fixed-width format files
- **MockSource**: Generate test data for development

### Transforms
- **JavaScriptTransform**: Custom business logic with JavaScript
- **FieldMapper**: Map and rename fields
- **DataValidator**: Validate data against schemas

### Sinks
- **DelimitedSink**: Output to CSV, TSV, and custom formats
- **ConsoleSink**: Display data for debugging
- **MockSink**: Discard data for performance testing

## Performance

FlowEngine is built for performance with validated benchmarks:

| Metric | Target | Achieved |
|--------|---------|----------|
| **Throughput** | 200K+ rows/sec | 500K+ rows/sec |
| **Field Access** | <20ns | <15ns |
| **Memory Usage** | Bounded | 60-70% reduction |
| **Startup Time** | <2 seconds | <1 second |

## Command Line Interface

```bash
# Execute a pipeline
FlowEngine.exe pipeline.yaml

# Validate configuration without running
FlowEngine.exe pipeline.yaml --validate

# Set environment variables
FlowEngine.exe pipeline.yaml --variable ENV=production

# Verbose output for debugging
FlowEngine.exe pipeline.yaml --verbose

# Show help
FlowEngine.exe --help
```

## Plugin Development

Create custom plugins using the five-component architecture:

```csharp
public class MyTransformPlugin : IPlugin
{
    public string Name => "MyTransform";
    public string Version => "1.0.0";
    
    public IStepProcessor CreateProcessor(IServiceProvider serviceProvider)
    {
        return new MyTransformProcessor();
    }
}

public class MyTransformProcessor : TransformProcessorBase
{
    protected override ArrayRow ProcessRow(ArrayRow input)
    {
        // Transform logic here
        var name = (string)input["name"];
        return input.With("upperName", name.ToUpper());
    }
}
```

See the [Plugin Development Guide](docs/plugin-development.md) for detailed instructions.

## Configuration

Pipeline configurations use YAML with data structure definitions:

```yaml
pipeline:
  name: "DataProcessingPipeline"
  version: "1.0.0"
  
  # Global settings
  settings:
    maxMemoryMB: 2048
    chunkSize: 5000
    
  # Plugin definitions
  plugins:
    - name: "reader"
      type: "DelimitedSource"
      config:
        filePath: "${INPUT_FILE}"
        encoding: "UTF-8"
        
        # Define the columns this plugin will output
        columns:
          - name: "id"
            type: "int32"
            index: 0              # Sequential indexing for performance
            required: true
          - name: "name"
            type: "string"
            index: 1
            required: true
          - name: "amount"
            type: "decimal"
            index: 2
            required: false
        
  # Data flow connections
  connections:
    - from: "reader.output"
      to: "processor.input"
```

## Requirements

- **.NET 8.0** or later
- **Windows 10/11** or **Linux** (Ubuntu 20.04+)
- **4GB RAM** minimum (8GB recommended for large datasets)

## Documentation

- **[Architecture Guide](docs/architecture.md)**: Detailed system architecture
- **[Plugin Development](docs/plugin-development.md)**: Create custom plugins
- **[Configuration Reference](docs/configuration.md)**: Complete YAML schema
- **[Performance Guide](docs/performance.md)**: Optimization strategies
- **[Troubleshooting](docs/troubleshooting.md)**: Common issues and solutions

## Contributing

1. Fork the repository
2. Create a feature branch (`git checkout -b feature/amazing-feature`)
3. Commit your changes (`git commit -m 'Add amazing feature'`)
4. Push to the branch (`git push origin feature/amazing-feature`)
5. Open a Pull Request

Please read [CONTRIBUTING.md](CONTRIBUTING.md) for development guidelines and coding standards.

## License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.

## Support

- **Documentation**: [docs.flowengine.io](https://docs.flowengine.io)
- **Issues**: [GitHub Issues](https://github.com/yourorg/flowengine/issues)
- **Discussions**: [GitHub Discussions](https://github.com/yourorg/flowengine/discussions)
- **Email**: support@flowengine.io

---

**FlowEngine** - *Process data at the speed of thought* ⚡