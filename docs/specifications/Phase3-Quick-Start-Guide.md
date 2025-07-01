# FlowEngine Phase 3: Quick Start Guide

**Reading Time**: 3 minutes  
**Goal**: Create your first five-component plugin in 15 minutes  

---

## Prerequisites

- .NET 8 SDK installed
- FlowEngine Phase 2 core libraries
- Basic understanding of YAML configuration

## 15-Minute Setup

### 1. Environment Setup (3 minutes)

```bash
# Clone and build core engine
git clone https://github.com/yourorg/flowengine.git
cd flowengine
dotnet build --configuration Release

# Verify installation
dotnet run -- --version
```

### 2. Create Your First Plugin (7 minutes)

```bash
# Generate plugin template
dotnet run -- plugin create --name MyFirstPlugin --type Transform

# This creates:
# - MyFirstPlugin/Configuration.cs
# - MyFirstPlugin/Validator.cs  
# - MyFirstPlugin/Plugin.cs
# - MyFirstPlugin/Processor.cs
# - MyFirstPlugin/Service.cs
# - MyFirstPlugin/plugin.yaml
```

### 3. Configure and Test (5 minutes)

Edit `MyFirstPlugin/plugin.yaml`:
```yaml
plugin:
  name: MyFirstPlugin
  version: 1.0.0
  
configuration:
  inputSchema:
    fields:
      - name: "name"
        type: "string"
      - name: "age" 
        type: "int32"
  
  outputSchema:
    fields:
      - name: "name"
        type: "string"
      - name: "ageCategory"
        type: "string"
```

Test your plugin:
```bash
# Validate plugin structure
dotnet run -- plugin validate --path ./MyFirstPlugin

# Test with sample data
echo "name,age\nJohn,25\nJane,17" | dotnet run -- pipeline run --config pipeline.yaml
```

---

## Key Concepts (30 seconds each)

**Five Components**: Every plugin has Configuration → Validator → Plugin → Processor → Service  
**ArrayRow**: High-performance data structure (3.25x faster than alternatives)  
**Schema-First**: Define input/output schemas before implementation  
**CLI-Driven**: All operations through command-line interface  

---

## Next Steps

- **Configuration**: Read [Five-Component Configuration Reference](./Phase3-Configuration-Reference.md)
- **Development**: Follow [Plugin Development Guide](./Phase3-Plugin-Development-Guide.md)
- **CLI**: Explore [CLI Operations Guide](./Phase3-CLI-Operations-Guide.md)

---

## Common Issues

**Plugin validation fails**: Check schema field types match exactly  
**Performance slow**: Ensure ArrayRow field access uses pre-calculated indexes  
**CLI commands not found**: Verify FlowEngine is in your PATH  

**Need Help?** See [Troubleshooting Guide](./Phase3-Troubleshooting-Guide.md)