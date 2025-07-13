# FlowEngine Architecture Discussion Summary

## Meeting Overview
**Date**: June 27, 2025  
**Purpose**: Analysis and refinement of FlowEngine High-Level Overview v0.1  
**Focus**: Architectural decisions and technical implementation strategy

---

## Key Architectural Decisions

### 1. **Data Format Scope - DECIDED**
- **Initial Focus**: Flat record formats only (delimited, fixed-width, SQL)
- **Rationale**: Prioritize processing speed, efficiency, and robust functionality over nested data complexity
- **Future**: Nested formats (JSON, XML) can be added later after core performance is optimized

### 2. **JavaScript Transform as Cornerstone - DECIDED**
- **Strategic Position**: JavaScript transforms are a cornerstone feature, not just another plugin
- **Template System**: Provide template-based scripting for non-developers (cleansing, routing, validation patterns)
- **Developer API**: Robust Fluent API through context object instead of direct row manipulation
- **Context Object**: Passes global variables, statistics, data records, and API methods to scripts

### 3. **Open Source Package Strategy - DECIDED**
- **Principle**: Prioritize open source packages over proprietary solutions
- **Key Packages**: 
  - **Jint** for JavaScript engine (pure .NET, good performance)
  - **CsvHelper** for delimited processing
  - **BenchmarkDotNet** for performance testing
  - **McMaster.NETCore.Plugins** for plugin architecture

### 4. **Ports as Dataset Containers - DECIDED**
- **Architecture**: Ports wrap immutable datasets, not just connection points
- **Flow Control**: Ports provide buffering, backpressure, and flow management
- **Unconnected Behaviors**:
  - Unconnected output ports = "bit bucket" (valid scenario)
  - Unconnected input ports = "dead branch" (warning, not error)

### 5. **Immutable Datasets with Chunking - DECIDED**
- **Immutability**: Datasets are immutable to prevent data corruption in branching scenarios
- **Chunking Strategy**: Process data in chunks to ensure flow-through and prevent memory bottlenecks
- **Branching Safety**: Multiple steps can consume same dataset without interference

---

## Technical Implementation Insights

### JavaScript Transform Context Design
```csharp
public class TransformContext
{
    public Row CurrentRow { get; }
    public IDictionary<string, object> GlobalVariables { get; }
    public ProcessingStatistics Stats { get; }
    public IRowBuilder CreateRow();
    public IValidator Validate { get; }
    public IDataCleaner Clean { get; }
    public IRouting Route { get; }
}
```

### Port Architecture Refinement
- Ports contain datasets, not just connection metadata
- Support multiple input/output ports per step
- Built-in flow control and buffering capabilities
- Validation warns about unconnected input ports

### Chunking Strategy
- Process data in configurable chunk sizes (1000-10000 rows)
- Enable flow-through processing for large datasets
- Support backpressure and memory management
- Each branch gets independent enumerators over immutable chunks

---

## Recommended Package Stack

### Core Processing
- **NJsonSchema**: JSON schema validation
- **YamlDotNet**: YAML configuration parsing
- **Jint**: JavaScript engine for transforms

### Data Handling
- **CsvHelper**: High-performance CSV processing
- **System.Threading.Channels**: Async streaming and backpressure
- **Microsoft.Data.SqlClient**: SQL connectivity

### Architecture & Performance
- **McMaster.NETCore.Plugins**: Plugin system with assembly isolation
- **BenchmarkDotNet**: Performance measurement
- **FluentValidation**: Configuration validation
- **Polly**: Resilience patterns (retry, circuit breaker)

---

## Outstanding Questions for Future Discussion

### JavaScript Transform Details
1. Should transforms support multiple output ports for routing?
2. How to handle async operations (database lookups, API calls)?
3. What JavaScript libraries should be pre-loaded?
4. Debugging capabilities for script development?

### Performance & Scalability
1. Optimal chunk size strategies for different scenarios?
2. Memory vs speed trade-offs in execution modes?
3. Support for parallel processing of independent branches?
4. Checkpoint/resume capabilities for long-running jobs?

### Plugin Architecture
1. Plugin versioning and dependency management?
2. Dynamic plugin discovery and loading?
3. Plugin isolation and security considerations?
4. Template marketplace for JavaScript transforms?

---

## Next Steps Assessment

### High-Level Overview Revision
**Recommendation**: **Not yet ready** for major revision

**Reasoning**:
- Current v0.1 provides solid foundation
- Recent decisions (JavaScript as cornerstone, immutable datasets, chunking) represent significant architectural insights
- Need to resolve outstanding technical questions before documenting final architecture
- Template system and context API design need further elaboration

### Suggested Approach
1. **Continue architectural discussions** on outstanding questions
2. **Prototype key components** (JavaScript context, chunking mechanism)
3. **Define plugin interfaces** more concretely
4. **Create v0.2 overview** incorporating all architectural decisions

### Priority Areas for Next Discussion
1. **JavaScript Transform Implementation**: Context API design, template system, engine integration
2. **Plugin Interface Definition**: Standardized interfaces, metadata, lifecycle management
3. **Performance Architecture**: Chunking mechanics, memory management, parallel processing
4. **Error Handling Strategy**: Resilience patterns, validation, debugging support

---

## Key Strengths of Current Direction
- **Clear focus** on flat records enables performance optimization
- **JavaScript transforms** provide powerful extensibility without complex plugin development
- **Immutable datasets** solve data integrity challenges elegantly
- **Chunking strategy** addresses memory and flow-through requirements
- **Open source approach** ensures long-term viability and community adoption