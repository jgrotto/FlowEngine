# FlowEngine Phase 2 Quick Start Guide

**Reading Time**: 3 minutes  
**Goal**: Get Phase 2 development environment running in 15 minutes  

---

## What's New in Phase 2

**Enterprise Features**: Plugin isolation, schema evolution, auto-tuning  
**Performance**: Maintains 14M+ rows/sec with advanced features  
**Architecture**: AssemblyLoadContext isolation, versioned schemas, configurable channels  

## 5-Minute Setup

### 1. Clone & Build
```bash
git clone [repo]/FlowEngine
cd FlowEngine
dotnet build -c Release
```

### 2. Install Templates
```bash
dotnet new install FlowEngine.Templates
dotnet new flowengine-plugin --name MyPlugin
```

### 3. Hello World Pipeline
```yaml
# hello-world.yml
pipeline:
  name: "HelloWorld"
  
  plugins:
    - name: "source"
      type: "ArraySource"
      schema:
        version: "1.0.0"
        fields:
          - name: "id"
            type: "int32"
            required: true
          - name: "message"
            type: "string"
            required: true
      data:
        - [1, "Hello"]
        - [2, "World"]
    
    - name: "sink"
      type: "ConsoleSink"
      
  connections:
    - from: "source"
      to: "sink"
```

### 4. Run Pipeline
```bash
dotnet run -- --config hello-world.yml
```

## Key Concepts (30 seconds each)

**Plugin Isolation**: Each plugin runs in separate AssemblyLoadContext  
**Schema Evolution**: Backward-compatible versioning with automatic migration  
**Channel Configuration**: Explicit buffer sizes with auto-tuning foundation  
**Resource Governance**: Memory limits and timeout enforcement per plugin  

## Next Steps

**Plugin Development**: [FlowEngine Plugin Development Guide](plugin-dev-guide.md)  
**Configuration**: [FlowEngine Configuration Reference](config-reference.md)  
**Performance**: [FlowEngine Performance Tuning Guide](performance-guide.md)  

## Troubleshooting

**Plugin Load Failed**: Check AssemblyLoadContext isolation logs  
**Schema Mismatch**: Verify schema versions and migration paths  
**Performance Issues**: Review channel buffer sizes and memory limits  

**Support**: [Discord] | **Docs**: [docs.flowengine.io] | **Issues**: [GitHub]