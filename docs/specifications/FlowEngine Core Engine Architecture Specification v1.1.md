# FlowEngine Core Engine Architecture Specification v1.1
**Version**: 1.1  
**Status**: Current - Production Implementation Ready  
**Authors**: FlowEngine Team  
**Performance Validated**: June 28, 2025  
**Related Documents**: Core Implementation v1.1, Core Testing v1.1

---

## Document Control

| Version | Date | Author | Changes |
|---------|------|--------|---------|
| 1.0 | 2025-06-27 | FlowEngine Team | Initial architecture with DictionaryRow |
| 1.1 | 2025-06-28 | FlowEngine Team | ArrayRow architecture, schema-first design, 3.25x performance |

---

## 1. Overview

### 1.1 Purpose and Scope
This specification defines the **architectural foundation** of FlowEngine's core execution engine with **schema-first ArrayRow architecture**. It covers:

**This specification defines:**
- High-level component architecture and relationships
- Schema-first design principles and data flow patterns
- ArrayRow data model conceptual framework
- Port system architecture for inter-step communication
- DAG validation and execution strategy
- Memory management architectural approach
- Integration points with other FlowEngine specifications

### 1.2 Dependencies
**External Dependencies:**
- .NET 8.0 runtime with performance optimizations
- C# 12.0 language features (primary constructors, collection expressions)

**Internal Dependencies:**
- FlowEngine High-Level Project Overview v0.5 (Document #19)
- FlowEngine Implementation Plan v1.1 (Document #21)

**Specification References:**
- Core Engine Implementation Specification v1.1 (detailed interfaces)
- Core Engine Testing Specification v1.1 (validation approach)
- Plugin API Specification (future - plugin integration points)

### 1.3 Out of Scope
**Architecture Focus**: This document provides architectural guidance and design decisions. For detailed implementation specifications, see the companion documents:
- **Implementation Details**: Interface definitions, data structures, performance patterns → Core Implementation v1.1
- **Testing Strategy**: Benchmarks, validation approaches, test specifications → Core Testing v1.1
- **Plugin Integration**: Plugin loading, isolation, resource governance → Plugin API Specification

**Future Architecture**: V2 considerations for distributed processing, async patterns, and schema evolution

---

## 2. Design Goals

### 2.1 Primary Objectives
1. **Schema-First Performance**: Deliver 3.25x performance improvement through pre-calculated field indexes and optimized memory layout
2. **Memory-Bounded Operation**: Predictable memory usage with intelligent spillover regardless of input dataset size
3. **Type Safety with Performance**: Leverage C# type system while maintaining O(1) field access patterns
4. **Production-Ready Reliability**: Comprehensive error handling with schema context preservation
5. **Streaming-First Architecture**: Process arbitrarily large datasets without loading into memory

### 2.2 Non-Goals
- **Real-time Processing**: Sub-millisecond latency requirements (batch processing focus)
- **Distributed Architecture**: V1 focuses on single-machine optimization
- **Schema Evolution**: Runtime schema changes within job execution
- **Transaction Support**: ACID properties across multiple processing steps

### 2.3 Design Principles
1. **Performance-First**: Every architectural decision prioritizes performance characteristics
2. **Schema Awareness**: Type information drives optimization and validation throughout the system
3. **Immutable Data Flow**: Prevent corruption in fan-out scenarios through immutable data structures
4. **Resource Determinism**: Predictable resource consumption patterns for enterprise deployment
5. **Composition Over Inheritance**: Favor composition and interfaces for flexibility and testability

### 2.4 Trade-offs Accepted

| Trade-off | Chosen Approach | Alternative | Rationale |
|-----------|-----------------|-------------|-----------|
| Memory Layout | ArrayRow with pre-calculated indexes | DictionaryRow flexibility | 3.25x performance gain justifies reduced runtime flexibility |
| Data Immutability | Memory copying for safety | In-place mutations | Enterprise reliability requirements over memory optimization |
| Processing Model | Synchronous streaming V1 | Async/await complexity | Simplified debugging and predictable performance |
| Schema Flexibility | Compile-time validation | Runtime schema discovery | Type safety and performance optimization |

---

## 3. Architectural Design

### 3.1 System Architecture Overview

#### 3.1.1 High-Level Component Architecture
```
┌─────────────────────────────────────────────────────────────────────┐
│                    FlowEngine Core v1.1                             │
├─────────────────────────────────────────────────────────────────────┤
│                                                                     │
│  ┌─────────────────┐  ┌─────────────────┐  ┌─────────────────────┐ │
│  │  Schema Engine  │  │   Job Executor  │  │   Memory Manager    │ │
│  │                 │  │                 │  │                     │ │
│  │ • Schema Cache  │  │ • DAG Validator │  │ • Pool Management   │ │
│  │ • Validation    │  │ • DAG Executor  │  │ • Spillover Engine  │ │
│  │ • Compatibility │  │ • Error Context │  │ • Pressure Monitor  │ │
│  └─────────────────┘  └─────────────────┘  └─────────────────────┘ │
│                                                                     │
├─────────────────────────────────────────────────────────────────────┤
│                         Port System                                 │
│  ┌─────────────┐  ┌─────────────┐  ┌────────────┐  ┌─────────────┐ │
│  │ Input Port  │──│ Connection  │──│ Validation │──│ Output Port │ │
│  │             │  │             │  │            │  │             │ │
│  │ • Schema    │  │ • Buffering │  │ • Schema   │  │ • Schema    │ │
│  │ • Buffering │  │ • Flow Ctrl │  │ • Compat   │  │ • Chunking  │ │
│  └─────────────┘  └─────────────┘  └────────────┘  └─────────────┘ │
│                                                                     │
├─────────────────────────────────────────────────────────────────────┤
│                       Data Layer                                    │
│  ┌─────────────┐  ┌─────────────┐  ┌─────────────┐  ┌────────────┐ │
│  │  ArrayRow   │  │   Schema    │  │   Dataset   │  │   Chunk    │ │
│  │             │  │             │  │             │  │            │ │
│  │ • O(1) Acc  │  │ • Metadata  │  │ • Streaming │  │ • Memory   │ │
│  │ • Immutable │  │ • Index Map │  │ • Chunked   │  │ • Pooled   │ │
│  │ • Pooled    │  │ • Cached    │  │ • Bounded   │  │ • Spillover│ │
│  └─────────────┘  └─────────────┘  └─────────────┘  └────────────┘ │
└─────────────────────────────────────────────────────────────────────┘
```

### 3.2 Core Architectural Patterns

#### 3.2.1 Schema-First Data Flow
**Design Pattern**: All data processing operations are driven by pre-established schema information, enabling compile-time optimizations and runtime validation.

**Key Benefits**:
- **Performance**: Pre-calculated field indexes eliminate dictionary lookups
- **Type Safety**: Compile-time validation prevents runtime type errors
- **Memory Efficiency**: Optimized memory layout based on field types
- **Validation**: Schema compatibility checking across processing boundaries

**Data Flow Pattern**:
```
Schema Definition → Index Calculation → ArrayRow Creation → Processing → Validation
      ↓                    ↓                   ↓              ↓           ↓
Type Metadata → Field Mapping → Memory Layout → O(1) Access → Compat Check
```

#### 3.2.2 Memory-Bounded Processing
**Design Pattern**: Intelligent memory management with predictable usage patterns and automatic spillover capabilities.

**Architecture Components**:
- **Memory Pools**: Reusable array allocation to minimize GC pressure
- **Spillover Engine**: Automatic disk-based storage when memory thresholds are exceeded
- **Pressure Monitoring**: Real-time memory usage tracking and adaptive behavior
- **Chunk Management**: Fixed-size processing units for predictable memory consumption

#### 3.2.3 Immutable Data Streams
**Design Pattern**: All data structures are immutable to prevent corruption in fan-out scenarios while maintaining performance through object pooling.

**Implementation Strategy**:
- **ArrayRow Immutability**: Read-only access to underlying data arrays
- **Copy-on-Write**: Efficient copying for data transformation scenarios
- **Pool Return**: Automatic return to object pools after processing
- **Reference Tracking**: Safe sharing across multiple processing paths

### 3.3 Component Architecture

#### 3.3.1 Schema Engine
**Responsibility**: Centralized schema management, validation, and optimization.

**Key Capabilities**:
- **Schema Registry**: Cached schema definitions with version management
- **Index Calculation**: Pre-compute field indexes for optimal ArrayRow performance
- **Compatibility Validation**: Cross-step schema compatibility checking
- **Type Mapping**: .NET type system integration with schema definitions

**Integration Points**:
- Port System: Schema validation for connections
- Data Layer: ArrayRow creation and field mapping
- Job Executor: Schema-aware DAG validation

#### 3.3.2 Job Executor
**Responsibility**: DAG validation, execution orchestration, and error context management.

**Key Capabilities**:
- **DAG Validation**: Schema-aware dependency and compatibility checking
- **Execution Orchestration**: Topological sort with parallel execution opportunities
- **Error Context**: Schema-aware error propagation and correlation
- **Resource Coordination**: Integration with memory manager for resource allocation

#### 3.3.3 Memory Manager
**Responsibility**: Memory allocation, pooling, spillover, and pressure monitoring.

**Key Capabilities**:
- **Pool Management**: ArrayPool integration for high-performance array reuse
- **Spillover Coordination**: Automatic disk-based storage for memory-bounded operation
- **Pressure Monitoring**: Real-time memory usage tracking and threshold management
- **Resource Governance**: Integration with plugin resource limits

#### 3.3.4 Port System
**Responsibility**: Inter-step communication with schema validation and flow control.

**Key Capabilities**:
- **Schema-Aware Connections**: Compile-time compatibility validation
- **Buffering Strategy**: Memory-bounded buffering with spillover integration
- **Flow Control**: Backpressure management and adaptive buffering
- **Type Safety**: Runtime validation of schema compatibility

### 3.4 Data Architecture

#### 3.4.1 ArrayRow Data Model
**Conceptual Framework**: High-performance, schema-aware data representation optimized for streaming processing.

**Architectural Characteristics**:
- **O(1) Field Access**: Pre-calculated field indexes eliminate dictionary lookups
- **Memory Efficiency**: Contiguous memory layout optimized for cache performance
- **Immutability**: Read-only access prevents data corruption
- **Pool Integration**: Automatic lifecycle management through object pooling

**Performance Characteristics**:
- **3.25x faster** than DictionaryRow for field access operations
- **60-70% memory reduction** through optimized layout
- **Cache-friendly**: Contiguous memory access patterns
- **GC-optimized**: Reduced allocation pressure through pooling

#### 3.4.2 Schema Management
**Conceptual Framework**: Centralized type information management driving performance optimizations.

**Architectural Characteristics**:
- **Compile-time Validation**: Schema compatibility checking before execution
- **Index Pre-calculation**: Field access patterns computed at schema creation
- **Caching Strategy**: Frequently used schemas cached for performance
- **Version Management**: Schema evolution support for future compatibility

#### 3.4.3 Chunk-Based Processing
**Conceptual Framework**: Fixed-size processing units enabling memory-bounded operation.

**Architectural Characteristics**:
- **Predictable Memory Usage**: Fixed chunk sizes enable accurate memory planning
- **Spillover Integration**: Seamless transition to disk-based storage
- **Parallel Processing**: Independent chunks enable future parallel processing
- **Resource Governance**: Chunk-level resource allocation and tracking

---

## 4. Integration Architecture

### 4.1 Plugin Integration Points
**Plugin API Compatibility**: The core engine provides specific integration points for plugin development while maintaining performance characteristics.

**Key Integration Patterns**:
- **Schema-Aware Processing**: Plugins receive pre-validated schema information
- **ArrayRow Interface**: Direct access to high-performance data structures
- **Resource Governance**: Automatic resource limit enforcement
- **Error Context**: Schema-aware error propagation through plugin boundaries

### 4.2 Configuration Integration
**Configuration-Driven Architecture**: Core engine components adapt behavior based on configuration while maintaining type safety.

**Integration Approach**:
- **Schema Validation**: Configuration schemas validated against core engine capabilities
- **Resource Limits**: Memory and processing limits configured per job
- **Performance Tuning**: Chunk sizes, pool sizes, and spillover thresholds
- **Debug Integration**: Observability features integrated at architectural level

### 4.3 Observability Integration
**Built-in Observability**: Architecture includes observability hooks without performance impact.

**Observability Points**:
- **Schema Events**: Schema creation, validation, compatibility checking
- **Memory Events**: Pool allocation, spillover events, pressure threshold alerts
- **Processing Events**: Chunk processing, error propagation, performance metrics
- **Resource Events**: Memory usage, processing times, throughput measurements

---

## 5. Performance Architecture

### 5.1 Performance Design Principles
**Performance-First Architecture**: Every architectural decision evaluated for performance impact with validated benchmarks.

**Key Performance Patterns**:
1. **Pre-calculated Indexes**: Eliminate runtime dictionary lookups
2. **Memory Pool Reuse**: Minimize GC pressure through object pooling
3. **Contiguous Memory**: Cache-friendly data layout patterns
4. **Minimal Allocations**: Reduce allocation pressure in hot paths
5. **Batch Processing**: Chunk-based processing for optimal throughput

### 5.2 Validated Performance Characteristics
**Benchmark-Driven Design**: All performance claims validated through comprehensive benchmarking.

**Confirmed Performance Metrics**:
- **Field Access**: 3.25x faster than DictionaryRow approach
- **Memory Usage**: 60-70% reduction in memory consumption
- **Throughput**: 200K-500K rows/sec on development hardware
- **Scalability**: Linear performance scaling with data volume
- **Resource Predictability**: Bounded memory usage regardless of input size

### 5.3 Performance Optimization Points
**Architecture Optimization Areas**: Specific architectural decisions designed for performance optimization.

**Optimization Focus Areas**:
- **Hot Path Optimization**: ArrayRow field access optimized for minimal CPU usage
- **Memory Layout**: Schema-driven memory layout for cache efficiency
- **Allocation Patterns**: Pool-based allocation to minimize GC impact
- **Pipeline Efficiency**: Chunk-based processing for optimal throughput
- **Resource Utilization**: Memory-bounded operation for predictable resource usage

---

## 6. Future Architecture Considerations

### 6.1 V2 Architecture Extensions
**Extensibility Planning**: Current architecture designed to support future enhancements without breaking changes.

**Planned Extensions**:
- **Async Processing**: Architecture supports future async/await patterns
- **Distributed Processing**: Component isolation enables future distribution
- **Schema Evolution**: Version management foundation for runtime schema changes
- **Advanced Caching**: Schema and data caching extensions for multi-job scenarios

### 6.2 Integration Evolution
**Plugin Architecture Evolution**: Current design supports advanced plugin capabilities.

**Future Capabilities**:
- **Native Code Integration**: Foreign function interface (FFI) support
- **GPU Acceleration**: Compute-intensive transform acceleration
- **Advanced Analytics**: Machine learning and statistical processing integration
- **Real-time Processing**: Sub-second latency optimization for future requirements

---

## 7. Architectural Decision Records

### 7.1 ArrayRow vs DictionaryRow Decision
**Decision**: Adopt ArrayRow with pre-calculated field indexes over flexible DictionaryRow approach.

**Context**: Original DictionaryRow provided flexibility but created performance bottlenecks in field access patterns.

**Consequences**:
- **Positive**: 3.25x performance improvement, 60-70% memory reduction, type safety
- **Negative**: Reduced runtime flexibility, increased schema management complexity
- **Mitigation**: Comprehensive schema validation and robust error handling

### 7.2 Synchronous vs Async Processing Decision
**Decision**: Implement synchronous processing model for V1 with async preparation for V2.

**Context**: Async/await patterns add complexity but enable advanced scenarios.

**Consequences**:
- **Positive**: Simplified debugging, predictable performance, easier testing
- **Negative**: Limited parallelization opportunities in V1
- **Mitigation**: Architecture designed to support async patterns in future versions

### 7.3 Memory Management Strategy Decision
**Decision**: Implement memory-bounded processing with intelligent spillover over unlimited memory usage.

**Context**: Enterprise deployment requires predictable resource consumption.

**Consequences**:
- **Positive**: Predictable memory usage, enterprise-ready deployment characteristics
- **Negative**: Added complexity in spillover management
- **Mitigation**: Comprehensive testing and performance validation of spillover scenarios

---

## 8. Conclusion

### Key Architectural Achievements
This architecture specification defines a **production-ready, high-performance data processing engine** that delivers:

- **Validated Performance**: 3.25x improvement with 60-70% memory reduction
- **Enterprise Reliability**: Memory-bounded operation with comprehensive error handling
- **Type Safety**: Schema-first design with compile-time validation
- **Extensibility**: Plugin integration points and future enhancement support
- **Operational Readiness**: Built-in observability and resource governance

### Implementation Readiness
The architecture provides complete guidance for:
- Component relationships and integration patterns
- Performance optimization strategies and validated characteristics
- Plugin integration points and resource governance
- Future extensibility and enhancement planning
- Operational deployment and monitoring considerations

### Next Steps
**Implementation Path**: This architectural foundation enables:
1. **Core Implementation**: Detailed interface and data structure implementation (Core Implementation v1.1)
2. **Testing Validation**: Comprehensive testing and benchmark validation (Core Testing v1.1)
3. **Plugin Integration**: Plugin API development based on established integration points
4. **Production Deployment**: Enterprise-ready deployment with operational characteristics

---

**Document Version**: 1.1  
**Architecture Validated**: June 28, 2025  
**Ready for**: Production Implementation  
**Next Review**: Upon Core Implementation v1.1 completion