# FlowEngine Documentation

**High-Performance Data Processing Engine for .NET**

Welcome to the FlowEngine documentation. This guide provides comprehensive information for users, developers, and contributors.

## 📚 Documentation Overview

### 🚀 Getting Started

- **[README](../README.md)**: Project overview and quick start guide
- **[Architecture](./architecture.md)**: Core system design and patterns
- **[Configuration](./configuration.md)**: Complete configuration reference
- **[Troubleshooting](./troubleshooting.md)**: Common issues and solutions

### 🔧 User Guides

- **[JavaScript Transform](./javascript-transform.md)**: Complete JavaScript Transform API reference
- **[Performance](./performance.md)**: Performance optimization and benchmarks
- **[Configuration](./configuration.md)**: Pipeline and plugin configuration reference

### 👨‍💻 Developer Guides

- **[Plugin Development](./plugin-development.md)**: Create custom plugins
- **[Development Guidelines](./development/)**: Development standards and practices
- **[Specifications](./specifications/)**: Technical specifications

### 📊 Project Reviews

- **[Reviews](./reviews/)**: Sprint reviews and project analysis
- **[Specifications Archive](./specifications/archive/)**: Historical specifications

## 🏗️ Architecture Overview

FlowEngine follows a **thin plugin calling Core services** architecture:

```
┌─────────────────────────────────────────────────────────────────────┐
│                        FlowEngine.Core                              │
│                                                                     │
│  ┌─────────────────────┐  ┌─────────────────────────────────────┐  │
│  │ Business Services   │  │ Infrastructure Services             │  │
│  │                     │  │                                     │  │
│  │ • ScriptEngine      │  │ • ArrayRowFactory                   │  │
│  │ • ContextService    │  │ • SchemaFactory                     │  │
│  │ • ValidationService │  │ • ChunkFactory                      │  │
│  │ • EnrichmentService │  │ • MemoryManager                     │  │
│  └─────────────────────┘  └─────────────────────────────────────┘  │
└─────────────────────────────────────────────────────────────────────┘
           ▲                                 ▲
           │                                 │
   ┌───────────────────────────────────────────────────────────────────┐
   │                    Thin Plugins                                   │
   │                                                                   │
   │  ┌─────────────────┐  ┌─────────────────┐  ┌─────────────────┐  │
   │  │ DelimitedSource │  │ JavaScript      │  │ DelimitedSink   │  │
   │  │                 │  │ Transform       │  │                 │  │
   │  │ • Configuration │  │ • Configuration │  │ • Configuration │  │
   │  │ • Orchestration │  │ • Orchestration │  │ • Orchestration │  │
   │  │ • Data Flow     │  │ • Data Flow     │  │ • Data Flow     │  │
   │  └─────────────────┘  └─────────────────┘  └─────────────────┘  │
   └───────────────────────────────────────────────────────────────────┘
```

## 🚀 Performance Highlights

### Validated Performance Metrics

| Component | Target | Achieved | Status |
|-----------|---------|----------|--------|
| **Core ArrayRow Processing** | 200K rows/sec | **441K rows/sec** | ✅ 220% of target |
| **Field Access Performance** | <20ns | **0.4ns** | ✅ 50x faster |
| **Chunk Processing** | 500K rows/sec | **1.9M rows/sec** | ✅ 380% of target |
| **JavaScript Transform** | 5K rows/sec | **7-13K rows/sec** | ✅ Exceeds minimum |

### Key Performance Features

- **ArrayRow Technology**: Sub-nanosecond field access
- **Chunk-based Streaming**: Memory-bounded processing
- **Engine Pooling**: Reusable JavaScript engines
- **Schema Optimization**: Compile-time field access patterns

## 📋 Quick Reference

### Essential Commands

```bash
# Build and test
dotnet.exe build FlowEngine.sln --configuration Release
dotnet.exe test tests/ --configuration Release

# Run pipeline
dotnet.exe run --project src/FlowEngine.Cli/ -- pipeline.yaml

# Performance testing
dotnet.exe test tests/DelimitedPlugins.Tests/ --filter "Performance"
```

### Basic Pipeline Configuration

```yaml
pipeline:
  name: "DataProcessing"
  
  steps:
    - id: source
      type: delimited-source
      config:
        filePath: "input.csv"
        hasHeaders: true
        
    - id: transform
      type: javascript-transform
      config:
        script: |
          function process(context) {
            const current = context.input.current();
            context.output.setField('result', current.field.toUpperCase());
            return true;
          }
        outputSchema:
          fields:
            - {name: result, type: string}
            
    - id: sink
      type: delimited-sink
      config:
        filePath: "output.csv"
        
  connections:
    - from: source
      to: transform
    - from: transform
      to: sink
```

## 📚 Documentation Structure

### Core Documentation

- **[architecture.md](./architecture.md)**: System architecture and design principles
- **[performance.md](./performance.md)**: Performance metrics and optimization
- **[javascript-transform.md](./javascript-transform.md)**: JavaScript Transform API
- **[plugin-development.md](./plugin-development.md)**: Plugin development guide
- **[configuration.md](./configuration.md)**: Configuration reference
- **[troubleshooting.md](./troubleshooting.md)**: Problem diagnosis and solutions

### Development Documentation

- **[development/coding-standards.md](./development/coding-standards.md)**: Code quality standards
- **[development/json-schema-validation.md](./development/json-schema-validation.md)**: Schema validation patterns

### Technical Specifications

- **[specifications/current/](./specifications/current/)**: Current technical specifications
- **[specifications/archive/](./specifications/archive/)**: Historical specifications

### Project Reviews

- **[reviews/sprint-3-5-complete-review.md](./reviews/sprint-3-5-complete-review.md)**: Sprint 3 & 3.5 analysis
- **[reviews/sprint-3-implementation-review.md](./reviews/sprint-3-implementation-review.md)**: Sprint 3 review
- **[reviews/sprint-2-development-review.md](./reviews/sprint-2-development-review.md)**: Sprint 2 review

## 🛠️ Development Workflow

### 1. Setup Development Environment

```bash
# Clone repository
git clone https://github.com/yourorg/flowengine.git
cd flowengine

# Build solution
dotnet.exe build FlowEngine.sln --configuration Release

# Run tests
dotnet.exe test tests/ --configuration Release
```

### 2. Create Custom Plugin

```bash
# Create plugin project
dotnet new classlib -n MyPlugin -o plugins/MyPlugin/

# Add to solution
dotnet sln FlowEngine.sln add plugins/MyPlugin/MyPlugin.csproj

# Add dependencies
dotnet add plugins/MyPlugin/ package FlowEngine.Abstractions
```

### 3. Performance Validation

```bash
# Run performance tests
dotnet.exe test tests/DelimitedPlugins.Tests/ --filter "Performance" --logger "console;verbosity=detailed"

# Monitor performance
dotnet.exe run --project src/FlowEngine.Cli/ -- pipeline.yaml --monitor-performance
```

## 🔧 Configuration Examples

### Development Configuration

```yaml
# Development pipeline with debugging
pipeline:
  name: "Development"
  
  settings:
    logLevel: "Debug"
    enableVerboseLogging: true
    chunkSize: 1000
    
  steps:
    - id: source
      type: delimited-source
      config:
        filePath: "test-data/sample.csv"
        chunkSize: 100
```

### Production Configuration

```yaml
# Production pipeline optimized for performance
pipeline:
  name: "Production"
  
  settings:
    logLevel: "Information"
    maxMemoryMB: 16384
    chunkSize: 20000
    parallelism: 32
    
  steps:
    - id: source
      type: delimited-source
      config:
        filePath: "/data/production/input.csv"
        chunkSize: 10000
        bufferSize: 524288
```

## 📊 Testing and Validation

### Test Categories

```bash
# Unit tests
dotnet.exe test tests/FlowEngine.Core.Tests/

# Integration tests
dotnet.exe test tests/DelimitedPlugins.Tests/

# Performance tests
dotnet.exe test tests/DelimitedPlugins.Tests/ --filter "Performance"

# End-to-end tests
dotnet.exe test tests/DelimitedPlugins.Tests/CoreIntegrationTests.cs
```

### Performance Benchmarks

```bash
# Core performance validation
dotnet.exe test tests/DelimitedPlugins.Tests/ --filter "CorePerformanceTests"

# JavaScript Transform performance
dotnet.exe test tests/DelimitedPlugins.Tests/ --filter "JavaScriptTransformPerformanceTests"
```

## 🔍 Troubleshooting Quick Reference

### Common Issues

1. **Performance Issues**: Check chunk size, memory allocation, and JavaScript optimization
2. **Configuration Errors**: Validate YAML syntax and plugin configurations
3. **Build Errors**: Ensure all dependencies are properly referenced
4. **Memory Issues**: Monitor memory usage and adjust chunk sizes

### Diagnostic Commands

```bash
# Check system health
dotnet.exe run --project src/FlowEngine.Cli/ -- --health-check

# Validate configuration
dotnet.exe run --project src/FlowEngine.Cli/ -- pipeline.yaml --validate

# Performance profiling
dotnet.exe run --project src/FlowEngine.Cli/ -- pipeline.yaml --profile
```

## 🤝 Contributing

### Development Guidelines

1. **Follow Architecture**: Use thin plugin pattern with Core services
2. **Write Tests**: Comprehensive unit and integration tests
3. **Document Code**: Clear XML documentation and comments
4. **Performance Focus**: Validate performance impact of changes
5. **Code Quality**: Follow established coding standards

### Contribution Process

1. Fork the repository
2. Create feature branch
3. Implement changes with tests
4. Run performance benchmarks
5. Submit pull request

## 📞 Support

### Resources

- **GitHub Repository**: [FlowEngine](https://github.com/yourorg/flowengine)
- **GitHub Issues**: Bug reports and feature requests
- **GitHub Discussions**: Questions and community support
- **Documentation**: Comprehensive guides and API reference

### Community

- **Stack Overflow**: Tag questions with `flowengine`
- **Discord**: Real-time community chat
- **Blog**: Technical articles and updates

---

## 📋 Documentation Status

| Document | Status | Last Updated |
|----------|--------|--------------|
| architecture.md | ✅ Complete | 2025-07-09 |
| performance.md | ✅ Complete | 2025-07-09 |
| javascript-transform.md | ✅ Complete | 2025-07-09 |
| plugin-development.md | ✅ Complete | 2025-07-09 |
| configuration.md | ✅ Complete | 2025-07-09 |
| troubleshooting.md | ✅ Complete | 2025-07-09 |

**FlowEngine Documentation** - *Comprehensive, current, and production-ready* 🚀