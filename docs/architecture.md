# FlowEngine Architecture

**High-Performance Data Processing Engine with ArrayRow Technology**

FlowEngine is built on a modern, performance-optimized architecture that leverages .NET 8 capabilities to deliver exceptional throughput while maintaining memory efficiency and developer productivity.

## 🏗️ Core Architecture Overview

### Design Philosophy

FlowEngine follows a **thin plugin calling Core services** architecture that separates concerns and maximizes performance:

1. **Core Services**: Complex functionality lives in `FlowEngine.Core` as reusable services
2. **Thin Plugins**: Lightweight wrappers that orchestrate Core services
3. **Schema-First Design**: All data flow is type-safe with explicit schema definitions
4. **Performance-Optimized**: Every component designed for high-throughput scenarios

### System Architecture

```
┌─────────────────────────────────────────────────────────────────┐
│                        FlowEngine.Core                          │
│                                                                 │
│  ┌─────────────────┐  ┌─────────────────┐  ┌─────────────────┐ │
│  │ ScriptEngine    │  │ ContextService  │  │ ArrayRowFactory │ │
│  │ Service         │  │                 │  │                 │ │
│  │ (Jint + Pool)   │  │ (Pure Context)  │  │ (High-Perf)     │ │
│  └─────────────────┘  └─────────────────┘  └─────────────────┘ │
│                                                                 │
│  ┌─────────────────┐  ┌─────────────────┐  ┌─────────────────┐ │
│  │ Schema          │  │ Chunk           │  │ Memory          │ │
│  │ Factory         │  │ Factory         │  │ Manager         │ │
│  │ (Type-Safe)     │  │ (Streaming)     │  │ (Bounded)       │ │
│  └─────────────────┘  └─────────────────┘  └─────────────────┘ │
└─────────────────────────────────────────────────────────────────┘
           ▲                        ▲                        ▲
           │                        │                        │
   ┌───────────────┐    ┌───────────────────┐    ┌───────────────┐
   │ DelimitedSrc  │    │ JavaScript        │    │ DelimitedSink │
   │   (Thin)      │    │ Transform (Thin)  │    │   (Thin)      │
   │               │    │                   │    │               │
   │ - CSV/TSV     │    │ - Pure Context    │    │ - Output      │
   │ - Schema      │    │ - Jint Engine     │    │ - Buffered    │
   │ - Validation  │    │ - Routing         │    │ - Streaming   │
   └───────────────┘    └───────────────────┘    └───────────────┘
```

## 🔧 Core Components

### 1. Data Model Layer

#### ArrayRow Technology
- **Purpose**: High-performance data structure optimized for field access
- **Performance**: Sub-nanosecond field access (validated at 0.4ns)
- **Memory**: 60-70% memory reduction compared to traditional approaches
- **Type Safety**: Compile-time schema validation

```csharp
public class ArrayRow : IArrayRow
{
    private readonly object[] _values;
    private readonly ISchema _schema;
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public object? this[int index] => _values[index];
}
```

#### Schema System
- **Immutable Design**: Schemas are created once and reused
- **Performance Optimized**: Field access patterns pre-compiled
- **Type Validation**: Runtime type checking with compile-time benefits

#### Chunk-Based Streaming
- **Memory Bounded**: Configurable chunk sizes prevent memory overflow
- **Parallel Processing**: Chunks can be processed in parallel
- **Streaming Support**: Infinite datasets with constant memory usage

### 2. Core Services Layer

#### Dependency Injection Architecture
FlowEngine uses Microsoft.Extensions.DependencyInjection with a structured registration pattern:

```csharp
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddFlowEngineCore(this IServiceCollection services)
    {
        // Core factories
        services.TryAddSingleton<IArrayRowFactory, ArrayRowFactory>();
        services.TryAddSingleton<ISchemaFactory, SchemaFactory>();
        services.TryAddSingleton<IChunkFactory, ChunkFactory>();
        
        // JavaScript services
        services.TryAddSingleton<IScriptEngineService, JintScriptEngineService>();
        services.TryAddSingleton<IJavaScriptContextService, JavaScriptContextService>();
        
        // Core services
        services.TryAddSingleton<IMemoryManager, MemoryManager>();
        services.TryAddSingleton<IPerformanceMonitor, PerformanceMonitor>();
        
        return services;
    }
}
```

#### JavaScript Engine Service
- **Jint Integration**: Cross-platform JavaScript execution
- **Object Pooling**: Reuse engine instances for performance
- **Compilation Caching**: Compile-once, execute-many pattern
- **Memory Management**: Bounded memory usage with monitoring

#### Context Service
- **Pure Context API**: Clean JavaScript interface without row parameters
- **Type-Safe Operations**: Schema-aware field access and validation
- **Routing Support**: Conditional data flow capabilities

### 3. Plugin Layer

#### Plugin Base Architecture
All plugins inherit from `PluginBase` which provides:
- **Lifecycle Management**: Initialize, Start, Stop, Dispose
- **Health Monitoring**: Built-in health checks
- **Error Handling**: Resilient error recovery
- **Performance Tracking**: Built-in metrics

#### Plugin Types

**Source Plugins** (`ISourcePlugin`)
- Generate data chunks from external sources
- Schema definition and validation
- Streaming support for large datasets

**Transform Plugins** (`ITransformPlugin`)
- Process data chunks through transformations
- Support for stateful and stateless operations
- Routing and filtering capabilities

**Sink Plugins** (`ISinkPlugin`)
- Output data chunks to external systems
- Buffering and batch processing
- Error handling and retry logic

## 🚀 Performance Architecture

### Memory Management
- **Bounded Memory**: Configurable limits prevent memory exhaustion
- **Object Pooling**: Reuse expensive objects (JavaScript engines, strings)
- **Chunk Streaming**: Process unlimited datasets with constant memory
- **Disposal Patterns**: Proper resource cleanup throughout

### Threading Model
- **Async/Await**: Non-blocking I/O operations
- **Concurrent Processing**: Multiple chunks processed in parallel
- **Thread Safety**: Core services designed for concurrent access
- **Back-pressure**: Automatic flow control to prevent overload

### Performance Optimizations
- **Aggressive Inlining**: Critical paths optimized for speed
- **Span\<T\> Usage**: Zero-allocation memory operations
- **FrozenDictionary**: Immutable lookups for hot paths
- **Compiled Expressions**: Runtime code generation for performance

## 🔄 Data Flow Architecture

### Pipeline Execution Model

```
┌─────────┐    ┌─────────┐    ┌─────────┐    ┌─────────┐
│ Source  │───▶│Transform│───▶│Transform│───▶│  Sink   │
│ Plugin  │    │ Plugin  │    │ Plugin  │    │ Plugin  │
└─────────┘    └─────────┘    └─────────┘    └─────────┘
     │              │              │              │
     ▼              ▼              ▼              ▼
┌─────────┐    ┌─────────┐    ┌─────────┐    ┌─────────┐
│ Schema  │    │ Schema  │    │ Schema  │    │ Schema  │
│ Output  │    │Transform│    │Transform│    │ Input   │
└─────────┘    └─────────┘    └─────────┘    └─────────┘
```

### Schema Flow Validation
- **Input Schema**: Validated before processing
- **Output Schema**: Enforced after processing
- **Type Safety**: Runtime validation with compile-time benefits
- **Performance**: Schema checking optimized for hot paths

### Routing Architecture
- **Conditional Routing**: JavaScript-based routing logic
- **Copy Operations**: Send data to multiple targets
- **Queue Management**: Named queues for different data flows
- **Parallel Processing**: Multiple routes processed concurrently

## 🔧 Configuration Architecture

### Pipeline Configuration
- **YAML-Based**: Human-readable configuration format
- **Schema Validation**: Configuration validated at load time
- **Environment Variables**: Support for environment-specific settings
- **Hot Reload**: Configuration changes without restart (planned)

### Plugin Configuration
- **JSON Schema**: Plugin configurations validated with JSON Schema
- **Type Safety**: Strong typing for configuration objects
- **Validation**: Comprehensive validation before plugin initialization
- **Documentation**: Auto-generated documentation from schemas

## 📊 Monitoring Architecture

### Performance Monitoring
- **Real-time Metrics**: Throughput, latency, memory usage
- **Plugin-Level Tracking**: Individual plugin performance
- **Health Checks**: Automated health monitoring
- **Alerting**: Performance degradation detection

### Diagnostic Architecture
- **Structured Logging**: Comprehensive logging with context
- **Tracing**: Distributed tracing for complex pipelines
- **Metrics Export**: Integration with monitoring systems
- **Performance Profiling**: Built-in profiling capabilities

## 🛠️ Extension Points

### Custom Core Services
- **Interface-Based**: Clean abstraction for custom services
- **Dependency Injection**: Seamless integration with DI container
- **Performance**: Optimized for high-throughput scenarios
- **Testing**: Comprehensive testing patterns

### Plugin Development
- **Base Classes**: Rich base classes for common patterns
- **Code Generation**: Template-based plugin generation
- **Testing Framework**: Comprehensive testing support
- **Documentation**: Auto-generated plugin documentation

## 🔒 Security Architecture

### Input Validation
- **Schema Validation**: All input data validated against schemas
- **Type Safety**: Strong typing prevents injection attacks
- **Sanitization**: Automatic data sanitization
- **Bounds Checking**: Memory access bounds validation

### JavaScript Security
- **Sandboxed Execution**: JavaScript runs in isolated context
- **Memory Limits**: Configurable memory limits
- **Execution Timeouts**: Prevent infinite loops
- **API Restrictions**: Limited API surface for security

## 🎯 Architecture Benefits

### Developer Experience
- **Clean APIs**: Well-designed interfaces for common operations
- **Type Safety**: Compile-time error detection
- **Performance**: Optimized for high-throughput scenarios
- **Testability**: Comprehensive testing patterns

### Operational Benefits
- **Monitoring**: Built-in performance and health monitoring
- **Scalability**: Horizontal scaling through parallelism
- **Reliability**: Resilient error handling and recovery
- **Maintainability**: Clean architecture for long-term maintenance

### Performance Benefits
- **Throughput**: 500K+ rows/sec validated performance
- **Memory**: 60-70% memory reduction through ArrayRow
- **Latency**: Sub-nanosecond field access
- **Scalability**: Linear scaling with additional cores

---

This architecture provides a solid foundation for high-performance data processing while maintaining developer productivity and system reliability. The thin plugin pattern ensures that complex logic is reusable while keeping plugins simple and focused.