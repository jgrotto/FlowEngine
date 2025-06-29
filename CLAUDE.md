# CLAUDE.md - FlowEngine Development Instructions v1.1

**Project**: FlowEngine - Schema-First ArrayRow Data Processing Engine  
**Language**: C# / .NET 8.0  
**Architecture**: ArrayRow-based, high-performance, schema-aware design  
**Phase**: V1.0 Production Implementation  
**Performance Target**: 200K-500K rows/sec (validated)

---

## 🎯 Claude Code Role & Expectations

### **Your Role: Senior .NET Performance Engineer**
You are implementing the **FlowEngine schema-first ArrayRow architecture** based on validated performance benchmarks showing **3.25x improvement** over the previous DictionaryRow approach. Your focus is on delivering production-ready, high-performance C# code.

### **Core Responsibilities**
- **Performance-First Implementation**: Every decision prioritizes throughput and memory efficiency
- **Schema-Aware Development**: Implement compile-time validation and type safety
- **Production Quality**: Enterprise-grade error handling, observability, and resource management
- **.NET 8 Optimization**: Leverage dynamic PGO, tiered compilation, and advanced GC features

---

## 🚀 **ArrayRow Architecture Fundamentals**

### **Performance-Validated Design Decisions** ✅

Based on comprehensive benchmarking, these architectural choices are **final and validated**:

| Component | Decision | Performance Benefit | Rationale |
|-----------|----------|-------------------|-----------|
| **Data Model** | ArrayRow with Schema | **3.25x faster field access** | O(1) indexed access vs O(log n) dictionary |
| **Memory Model** | Immutable with pooling | **60-70% memory reduction** | Efficient copying with object reuse |
| **Processing Model** | Chunked streaming | **Memory-bounded operation** | Handles unlimited datasets |
| **Validation Model** | Schema-first, compile-time | **Eliminates runtime errors** | Type safety with performance |

### **Key Performance Characteristics** 📊
```csharp
// ArrayRow: O(1) field access via pre-calculated indexes
public object? this[string columnName] => 
    _schema.TryGetIndex(columnName, out var index) ? _values[index] : null;

// Schema: Cached indexes for zero lookup overhead  
private readonly ImmutableDictionary<string, int> _columnIndexes;

// Chunking: Memory-bounded processing regardless of dataset size
public async IAsyncEnumerable<Chunk> GetChunksAsync(ChunkingOptions options);
```

---

## 📋 **Implementation Priority Guidelines**

### **🔴 Critical Path Items** (Never Compromise)
1. **ArrayRow Performance**: Field access must be <15ns
2. **Schema Validation**: Compile-time compatibility checking
3. **Memory Management**: Bounded memory usage with pooling
4. **Chunked Processing**: Streaming without materialization

### **🟡 Important Features** (Optimize When Possible)
1. **Error Handling**: Comprehensive but performance-aware
2. **Plugin Loading**: Fast startup with assembly isolation  
3. **JavaScript Integration**: Engine pooling and compilation caching
4. **Observability**: Rich diagnostics with minimal overhead

### **🟢 Enhancement Features** (After Core Complete)
1. **UI Abstractions**: Future-proofing for visual designers
2. **Advanced Analytics**: Performance monitoring and optimization
3. **Extended Plugins**: Additional data sources and transforms
4. **Distributed Processing**: Multi-node processing capabilities

---

## 🏗️ **Schema-First Development Patterns**

### **Schema Definition Pattern**
```csharp
// ✅ CORRECT: Schema-first with explicit validation
public class CustomerProcessingStep : IStepProcessor
{
    private readonly Schema _inputSchema;
    private readonly Schema _outputSchema;
    
    public CustomerProcessingStep()
    {
        // Define schemas at construction for compile-time validation
        _inputSchema = new Schema(new[]
        {
            new ColumnDefinition { Name = "CustomerId", DataType = typeof(int), IsNullable = false },
            new ColumnDefinition { Name = "Name", DataType = typeof(string), IsNullable = false },
            new ColumnDefinition { Name = "Email", DataType = typeof(string), IsNullable = true }
        });
        
        _outputSchema = _inputSchema.AddColumns(
            new ColumnDefinition { Name = "ProcessedDate", DataType = typeof(DateTime) }
        );
    }
    
    public Schema ValidateSchema(Schema inputSchema)
    {
        if (!inputSchema.IsCompatibleWith(_inputSchema))
            throw new SchemaValidationException("Input schema mismatch");
        return _outputSchema;
    }
}
```

### **ArrayRow Processing Pattern**
```csharp
// ✅ CORRECT: High-performance ArrayRow operations
public ArrayRow ProcessCustomer(ArrayRow input)
{
    // O(1) field access via schema indexes
    var customerId = (int)input["CustomerId"];
    var name = (string)input["Name"];
    var email = input["Email"] as string;
    
    // Efficient immutable operations
    return input.With("ProcessedDate", DateTime.UtcNow)
               .With("Email", NormalizeEmail(email));
}

// ❌ AVOID: Dictionary-style operations (breaks performance)
public Row ProcessCustomerOld(Row input)
{
    var dict = input.ToDictionary(); // Performance killer!
    dict["ProcessedDate"] = DateTime.UtcNow;
    return Row.FromDictionary(dict);
}
```

### **Memory-Efficient Chunking Pattern**
```csharp
// ✅ CORRECT: Streaming with bounded memory
public async IAsyncEnumerable<Chunk> ProcessDataAsync(IAsyncEnumerable<Chunk> inputChunks)
{
    await foreach (var chunk in inputChunks)
    {
        using (chunk) // Automatic disposal and pooling
        {
            var processedRows = new ArrayRow[chunk.RowCount];
            var span = chunk.Rows; // Zero-copy access
            
            for (int i = 0; i < span.Length; i++)
            {
                processedRows[i] = ProcessRow(span[i]);
            }
            
            yield return new Chunk(_outputSchema, processedRows, chunk.Metadata);
        }
    }
}
```

---

## 🧠 **Senior Developer Decision-Making Framework**

### **When to Ask Questions** 🤔
Ask when encountering these scenarios:

**🔴 Performance Impact Unclear**
> "The specification mentions schema caching, but doesn't specify the eviction policy. Should I implement LRU with a max cache size of 1000 schemas, or use weak references for memory pressure?"

**🔴 .NET 8 Optimization Opportunities**
> "I can leverage .NET 8's improved dynamic PGO for this hot path. Should I structure the code to avoid inlining hints and let PGO optimize based on actual usage patterns?"

**🔴 Memory Management Trade-offs**
> "Object pooling for ArrayRow instances shows 30% allocation reduction, but adds complexity. The specification doesn't specify pool sizing - should I implement adaptive pool sizes based on memory pressure?"

**🔴 Cross-Platform Performance**
> "Linux file I/O performance is 15% slower than Windows in my tests. Should I implement platform-specific optimizations or accept the variance?"

### **When to Raise Concerns** 🚨
Identify potential issues proactively:

**⚠️ Performance Bottlenecks**
> "The current schema validation approach requires reflection on every step initialization. This could cause 200ms+ startup delays for jobs with 50+ steps. Consider schema pre-compilation."

**⚠️ Memory Pressure Issues**
> "ArrayRow pooling strategy could cause memory fragmentation under high concurrent load. The current implementation doesn't account for different schema sizes - large schemas will pollute small schema pools."

**⚠️ Scalability Limitations**
> "Channel-based port communication shows backpressure at 100+ connections per output port. Consider implementing connection multiplexing or batched delivery for high fan-out scenarios."

### **When to Suggest Improvements** 💡
Proactively enhance the architecture:

**🎯 .NET 8 Leverage Opportunities**
> "We can use .NET 8's FrozenDictionary for schema column indexes since they're immutable. This provides 2-3x faster lookups for schemas with 10+ columns."

**🎯 Performance Optimization Ideas**
> "Consider implementing SIMD vectorization for bulk numeric transformations using .NET 8's improved Vector<T> support. This could provide 4-8x speedup for mathematical operations."

**🎯 Architecture Enhancements**
> "The current error handling preserves full ArrayRow context, but this can be expensive for large rows. Consider implementing lightweight error context that only captures field names and values on demand."

---

## 🏁 **Implementation Patterns by Component**

### **ArrayRow Implementation Pattern**
```csharp
// ✅ High-performance implementation with .NET 8 optimizations
public sealed class ArrayRow : IEquatable<ArrayRow>
{
    private readonly object?[] _values;
    private readonly Schema _schema;
    private readonly int _hashCode; // Cached for performance
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public object? this[string columnName]
    {
        get
        {
            // Leverage .NET 8 improved bounds checking elimination
            var index = _schema.GetIndex(columnName);
            return (uint)index < (uint)_values.Length ? _values[index] : null;
        }
    }
    
    // Optimized equality for .NET 8 Span operations
    public bool Equals(ArrayRow? other)
    {
        return other != null && 
               _hashCode == other._hashCode && 
               _schema.Equals(other._schema) &&
               _values.AsSpan().SequenceEqual(other._values.AsSpan());
    }
}
```

### **Memory Management Pattern**
```csharp
// ✅ .NET 8 optimized memory management
public sealed class ArrayRowMemoryManager : IMemoryManager
{
    private readonly ConcurrentDictionary<string, ObjectPool<ArrayRow>> _schemaPools;
    
    public async ValueTask<MemoryAllocation<ArrayRow>> AllocateRowsAsync(int count, Schema schema)
    {
        // Leverage .NET 8 improved GC pressure detection
        var gcInfo = GC.GetGCMemoryInfo();
        var memoryPressure = (double)gcInfo.MemoryLoadBytes / gcInfo.HighMemoryLoadThresholdBytes;
        
        if (memoryPressure > 0.8)
        {
            // Reduce allocation size under pressure
            count = Math.Min(count, 1000);
        }
        
        var pool = GetOrCreateSchemaPool(schema);
        var allocation = new ArrayRow[count];
        
        return new MemoryAllocation<ArrayRow>(allocation, pool);
    }
    
    private ObjectPool<ArrayRow> GetOrCreateSchemaPool(Schema schema)
    {
        return _schemaPools.GetOrAdd(schema.Signature, _ => 
            new DefaultObjectPoolProvider().Create(new SchemaSpecificRowPolicy(schema)));
    }
}
```

### **Schema Validation Pattern**
```csharp
// ✅ Compile-time validation with performance optimization
public sealed class SchemaValidator
{
    private readonly ConcurrentDictionary<string, ValidationResult> _validationCache;
    
    public ValidationResult ValidateCompatibility(Schema source, Schema target)
    {
        var cacheKey = $"{source.Signature}→{target.Signature}";
        
        return _validationCache.GetOrAdd(cacheKey, _ =>
        {
            // Expensive validation only done once per schema pair
            return PerformCompatibilityCheck(source, target);
        });
    }
    
    [MethodImpl(MethodImplOptions.NoInlining)] // Let .NET 8 PGO optimize
    private ValidationResult PerformCompatibilityCheck(Schema source, Schema target)
    {
        var errors = new List<ValidationError>();
        
        foreach (var targetColumn in target.Columns)
        {
            var sourceColumn = source.GetColumn(targetColumn.Name);
            if (sourceColumn == null)
            {
                errors.Add(new ValidationError($"Missing column: {targetColumn.Name}"));
                continue;
            }
            
            if (!IsTypeCompatible(sourceColumn.DataType, targetColumn.DataType))
            {
                errors.Add(new ValidationError($"Type mismatch: {targetColumn.Name}"));
            }
        }
        
        return new ValidationResult(errors);
    }
}
```

---

## 🔧 **.NET 8 Specific Optimizations**

### **Dynamic PGO Integration**
```csharp
// ✅ Structure code for optimal PGO benefits
public class FieldProcessor
{
    // Don't force inlining - let PGO decide based on usage
    [MethodImpl(MethodImplOptions.NoInlining)]
    public object? ProcessField(ArrayRow row, string fieldName)
    {
        // PGO will optimize based on most common field names
        return fieldName switch
        {
            "CustomerId" => ProcessCustomerId(row),
            "Name" => ProcessName(row),
            "Email" => ProcessEmail(row),
            _ => ProcessGenericField(row, fieldName)
        };
    }
}
```

### **Vectorization Opportunities**
```csharp
// ✅ Leverage .NET 8 improved vectorization
public static class NumericProcessor
{
    public static void ApplyMultiplier(Span<decimal> values, decimal multiplier)
    {
        // .NET 8 JIT will auto-vectorize this loop for supported types
        for (int i = 0; i < values.Length; i++)
        {
            values[i] *= multiplier;
        }
    }
    
    // For more complex operations, use explicit vectorization
    public static void ProcessNumericArray(Span<float> input, Span<float> output)
    {
        if (Vector.IsHardwareAccelerated && input.Length >= Vector<float>.Count)
        {
            var vectorSpan = MemoryMarshal.Cast<float, Vector<float>>(input);
            var outputVectorSpan = MemoryMarshal.Cast<float, Vector<float>>(output);
            
            for (int i = 0; i < vectorSpan.Length; i++)
            {
                outputVectorSpan[i] = Vector.Sqrt(vectorSpan[i]);
            }
        }
    }
}
```

### **Tiered Compilation Optimization**
```csharp
// ✅ Optimize for tiered compilation
public class HotPathProcessor
{
    private static int _callCount;
    
    public ArrayRow ProcessRow(ArrayRow input)
    {
        // Track method "heat" for tier optimization
        Interlocked.Increment(ref _callCount);
        
        // Hot path - will be optimized to tier 1 quickly
        return ProcessCustomerData(input);
    }
    
    // Keep hot paths simple to help JIT optimization
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ArrayRow ProcessCustomerData(ArrayRow input)
    {
        var name = (string)input["Name"];
        return input.With("ProcessedName", name?.ToUpperInvariant());
    }
}
```

---

## 🐧 **WSL/Windows Development Environment**

### **Path Handling for Dual Environment**
```csharp
// ✅ CORRECT: Cross-platform path handling
public static class PathUtilities
{
    public static string NormalizePath(string path)
    {
        // Handle WSL/Windows path translation
        if (path.StartsWith("/mnt/c/", StringComparison.OrdinalIgnoreCase))
        {
            // Convert WSL path to Windows path
            return path.Replace("/mnt/c/", "C:\\").Replace('/', '\\');
        }
        
        // Expand environment variables
        path = Environment.ExpandEnvironmentVariables(path);
        
        // Use .NET cross-platform APIs
        return Path.GetFullPath(path);
    }
    
    public static bool FileExistsAnyCase(string filePath)
    {
        if (File.Exists(filePath)) return true;
        
        // Handle case-sensitive file systems
        var directory = Path.GetDirectoryName(filePath);
        var fileName = Path.GetFileName(filePath);
        
        if (Directory.Exists(directory))
        {
            return Directory.GetFiles(directory, fileName, SearchOption.TopDirectoryOnly)
                           .Any(f => string.Equals(Path.GetFileName(f), fileName, 
                                                  StringComparison.OrdinalIgnoreCase));
        }
        
        return false;
    }
}
```

### **Development Commands**
```bash
# WSL-specific development commands (use .exe suffix for Windows tools)

# Build and test
dotnet.exe build /mnt/c/source/FlowEngine/FlowEngine.sln --configuration Release
dotnet.exe test /mnt/c/source/FlowEngine/tests/ --logger "console;verbosity=detailed"

# Performance benchmarking
dotnet.exe run --project /mnt/c/source/FlowEngine/benchmarks/FlowEngine.Benchmarks/ --configuration Release

# Create new components
dotnet.exe new classlib -n FlowEngine.Plugins.NewPlugin -o /mnt/c/source/FlowEngine/src/FlowEngine.Plugins/NewPlugin/

# Package for distribution
dotnet.exe pack /mnt/c/source/FlowEngine/src/FlowEngine.Core/ --configuration Release --output /mnt/c/source/FlowEngine/packages/
```

---

## 📊 **Performance Monitoring & Benchmarking**

### **Continuous Performance Validation**
```csharp
// ✅ BenchmarkDotNet integration for regression detection
[MemoryDiagnoser]
[DisassemblyDiagnoser] // View .NET 8 optimized assembly
[SimpleJob(RuntimeMoniker.Net80)]
public class ArrayRowBenchmarks
{
    private ArrayRow[] _testRows;
    private Schema _schema;
    
    [GlobalSetup]
    public void Setup()
    {
        _schema = CreateBenchmarkSchema();
        _testRows = CreateBenchmarkData(_schema, 10_000);
    }
    
    [Benchmark(Baseline = true)]
    public long FieldAccessPerformance()
    {
        long sum = 0;
        foreach (var row in _testRows)
        {
            sum += (int)row["CustomerId"]; // Target: <15ns per access
        }
        return sum;
    }
    
    [Benchmark]
    public void RowTransformationPerformance()
    {
        foreach (var row in _testRows)
        {
            var transformed = row.With("ProcessedDate", DateTime.UtcNow);
            // Validate transformation efficiency
        }
    }
    
    [Benchmark]
    public async Task ChunkedProcessingThroughput()
    {
        var dataset = new ArrayDataset(_schema, _testRows.ToAsyncEnumerable());
        var options = new ChunkingOptions { TargetChunkSize = 5000 };
        
        long processedCount = 0;
        await foreach (var chunk in dataset.GetChunksAsync(options))
        {
            using (chunk)
            {
                processedCount += chunk.RowCount;
            }
        }
        
        // Target: 200K+ rows/sec
    }
}
```

### **Memory Pressure Testing**
```csharp
// ✅ Validate memory-bounded operation
[Fact]
public async Task ValidateMemoryBoundedProcessing()
{
    var beforeMemory = GC.GetTotalMemory(true);
    var schema = CreateLargeSchema(50); // 50 columns
    
    // Process 10M rows - should not increase memory significantly
    await ProcessLargeDataset(schema, 10_000_000);
    
    var afterMemory = GC.GetTotalMemory(true);
    var memoryGrowth = afterMemory - beforeMemory;
    
    // Memory growth should be bounded regardless of dataset size
    Assert.True(memoryGrowth < 100_000_000, // 100MB max
        $"Memory growth {memoryGrowth:N0} bytes exceeds acceptable limit");
}
```

---

## 🚨 **Critical Error Scenarios & Handling**

### **Schema Validation Errors**
```csharp
// ✅ Comprehensive schema error handling
public class SchemaValidationService
{
    public void ValidateSchemaCompatibility(Schema source, Schema target)
    {
        var errors = new List<string>();
        
        foreach (var targetColumn in target.Columns)
        {
            var sourceColumn = source.GetColumn(targetColumn.Name);
            
            if (sourceColumn == null)
            {
                errors.Add($"Missing required column '{targetColumn.Name}' in source schema");
                continue;
            }
            
            if (!IsTypeCompatible(sourceColumn.DataType, targetColumn.DataType))
            {
                errors.Add($"Type mismatch for column '{targetColumn.Name}': " +
                          $"source is {sourceColumn.DataType.Name}, target expects {targetColumn.DataType.Name}");
            }
            
            if (!sourceColumn.IsNullable && targetColumn.IsNullable)
            {
                // This is safe - can always make required fields optional
                continue;
            }
            
            if (sourceColumn.IsNullable && !targetColumn.IsNullable)
            {
                errors.Add($"Column '{targetColumn.Name}' cannot be made non-nullable: source allows nulls");
            }
        }
        
        if (errors.Count > 0)
        {
            throw new SchemaValidationException(
                $"Schema compatibility validation failed:\n{string.Join("\n", errors)}")
            {
                SourceSchema = source,
                TargetSchema = target,
                ValidationErrors = errors
            };
        }
    }
}
```

### **Memory Pressure Handling**
```csharp
// ✅ Graceful memory pressure response
public class MemoryPressureHandler
{
    private readonly ILogger<MemoryPressureHandler> _logger;
    
    public async Task<ProcessingResult> HandleMemoryPressure(
        IAsyncEnumerable<ArrayRow> data, 
        Schema schema)
    {
        var gcInfo = GC.GetGCMemoryInfo();
        var memoryPressure = (double)gcInfo.MemoryLoadBytes / gcInfo.HighMemoryLoadThresholdBytes;
        
        if (memoryPressure > 0.9)
        {
            _logger.LogWarning("Critical memory pressure detected: {Pressure:P1}. Initiating spillover.", memoryPressure);
            
            // Force GC to free up memory
            GC.Collect(2, GCCollectionMode.Forced, true);
            
            // Reduce chunk sizes and enable spillover
            var options = new ChunkingOptions 
            { 
                TargetChunkSize = 1000, // Reduced from default 5000
                PressureStrategy = MemoryPressureStrategy.SpillToDisk 
            };
            
            return await ProcessWithSpillover(data, schema, options);
        }
        
        return await ProcessInMemory(data, schema);
    }
}
```

### **Performance Regression Detection**
```csharp
// ✅ Automated performance regression detection
public class PerformanceRegressionTests
{
    private const int PERFORMANCE_TOLERANCE_PERCENT = 5;
    
    [Theory]
    [InlineData(10_000, 100_000)]   // 10K rows, min 100K rows/sec
    [InlineData(100_000, 250_000)]  // 100K rows, min 250K rows/sec  
    [InlineData(1_000_000, 200_000)] // 1M rows, min 200K rows/sec
    public async Task DetectThroughputRegression(int rowCount, int minRowsPerSecond)
    {
        var schema = CreateStandardSchema();
        var testData = CreateTestData(schema, rowCount);
        
        var stopwatch = Stopwatch.StartNew();
        var processedCount = await ProcessTestData(testData);
        stopwatch.Stop();
        
        var actualRowsPerSecond = (int)(processedCount / stopwatch.Elapsed.TotalSeconds);
        
        Assert.True(actualRowsPerSecond >= minRowsPerSecond, 
            $"Performance regression detected: {actualRowsPerSecond:N0} rows/sec < {minRowsPerSecond:N0} rows/sec minimum");
        
        // Log performance metrics for tracking
        _logger.LogInformation("Performance validation: {RowCount:N0} rows processed at {Throughput:N0} rows/sec", 
            rowCount, actualRowsPerSecond);
    }
}
```

---

## 🔍 **Debugging & Diagnostics Patterns**

### **Performance Profiling Integration**
```csharp
// ✅ Built-in performance profiling for development
public class DiagnosticArrayRowProcessor
{
    private static readonly ActivitySource ActivitySource = new("FlowEngine.ArrayRow");
    
    public ArrayRow ProcessRow(ArrayRow input)
    {
        using var activity = ActivitySource.StartActivity("ProcessRow");
        activity?.SetTag("schema.signature", input.Schema.Signature);
        activity?.SetTag("row.columnCount", input.ColumnCount.ToString());
        
        var result = PerformProcessing(input);
        
        activity?.SetTag("processing.duration", activity.Duration.TotalMicroseconds.ToString());
        return result;
    }
    
    // Use .NET 8 improved activity tracking
    private ArrayRow PerformProcessing(ArrayRow input)
    {
        // Processing logic with automatic performance tracking
        return input.With("ProcessedAt", DateTime.UtcNow);
    }
}
```

### **Schema Flow Debugging**
```csharp
// ✅ Schema transformation tracking for debugging
public class SchemaFlowTracker
{
    private readonly ILogger<SchemaFlowTracker> _logger;
    
    public void TrackSchemaTransformation(Schema source, Schema target, string stepId)
    {
        var addedColumns = target.Columns.Where(c => !source.HasColumn(c.Name)).ToList();
        var removedColumns = source.Columns.Where(c => !target.HasColumn(c.Name)).ToList();
        var modifiedColumns = target.Columns.Where(c => 
        {
            var sourceCol = source.GetColumn(c.Name);
            return sourceCol != null && !sourceCol.DataType.Equals(c.DataType);
        }).ToList();
        
        _logger.LogDebug("Schema flow in step {StepId}: {SourceColumns} → {TargetColumns}",
            stepId, source.ColumnCount, target.ColumnCount);
        
        if (addedColumns.Count > 0)
        {
            _logger.LogDebug("Added columns: {AddedColumns}", 
                string.Join(", ", addedColumns.Select(c => $"{c.Name}:{c.DataType.Name}")));
        }
        
        if (removedColumns.Count > 0)
        {
            _logger.LogDebug("Removed columns: {RemovedColumns}",
                string.Join(", ", removedColumns.Select(c => c.Name)));
        }
        
        if (modifiedColumns.Count > 0)
        {
            _logger.LogDebug("Modified columns: {ModifiedColumns}",
                string.Join(", ", modifiedColumns.Select(c => c.Name)));
        }
    }
}
```

---

## 📚 **Reference Architecture & Patterns**

### **Current FlowEngine v0.5 Specifications**
Based on the documented architecture, implement these key components in order:

1. **Core Data Model** (`FlowEngine.Core/Data/`)
   - `ArrayRow` - High-performance immutable data record
   - `Schema` - Type-safe column definitions with validation
   - `Chunk` - Memory-efficient batch processing

2. **Memory Management** (`FlowEngine.Core/Memory/`)
   - `IMemoryManager` - .NET 8 optimized allocation and spillover
   - `SchemaCache` - Cached schema instances for reuse
   - `ObjectPool<T>` integration for ArrayRow instances

3. **Port System** (`FlowEngine.Core/Ports/`)
   - `IInputPort`/`IOutputPort` - Schema-aware data flow
   - `Connection` - Channel-based communication with validation
   - `ConnectionManager` - Automatic port wiring

4. **DAG Execution** (`FlowEngine.Core/Execution/`)
   - `DagValidator` - Schema compatibility and cycle detection
   - `DagExecutor` - Topological execution with parallelization
   - `ExecutionPlan` - Optimized step ordering

### **Project Structure** 📁
```
FlowEngine/
├── src/
│   ├── FlowEngine.Abstractions/     # Core interfaces (schema-aware)
│   ├── FlowEngine.Core/             # ArrayRow engine implementation
│   ├── FlowEngine.Cli/              # Command-line interface
│   └── FlowEngine.Plugins/
│       ├── Delimited/               # CSV/TSV with ArrayRow optimization
│       ├── FixedWidth/              # Fixed-width file processing
│       └── JavaScriptTransform/     # Schema-aware JS transforms
├── tests/
│   ├── FlowEngine.Core.Tests/       # Unit tests with benchmarks
│   ├── FlowEngine.Integration.Tests/ # End-to-end scenarios
│   └── FlowEngine.Performance.Tests/ # Regression testing
├── benchmarks/
│   └── FlowEngine.Benchmarks/       # BenchmarkDotNet performance validation
└── docs/
    ├── specifications/              # Technical specifications
    └── examples/                    # Sample YAML jobs and configs
```

---

## 🎯 **Implementation Quality Gates**

### **Performance Requirements** ⚡
All implementations must meet these validated performance targets:

| Component | Requirement | Validation Method |
|-----------|-------------|-------------------|
| **ArrayRow Field Access** | <15ns per operation | BenchmarkDotNet micro-benchmark |
| **Schema Validation** | <1ms for 50-column schema | Unit test with timing |
| **Chunk Processing** | <5ms per 5K-row chunk | Integration benchmark |
| **Memory Growth** | <100MB for unlimited datasets | Memory pressure test |
| **End-to-End Throughput** | 200K+ rows/sec | Pipeline benchmark |

### **Code Quality Standards** ✅
```csharp
// ✅ REQUIRED: All public APIs must have XML documentation
/// <summary>
/// Processes customer data with schema validation and performance optimization.
/// Achieves O(1) field access through pre-calculated schema indexes.
/// </summary>
/// <param name="input">Source customer data with validated schema</param>
/// <returns>Processed customer data with additional computed fields</returns>
/// <exception cref="SchemaValidationException">Thrown when input schema is incompatible</exception>
public ArrayRow ProcessCustomer(ArrayRow input)

// ✅ REQUIRED: Performance-critical paths must be benchmarked
[Benchmark]
public void ValidateFieldAccessPerformance()

// ✅ REQUIRED: Memory allocations must be tracked
[MemoryDiagnoser]
public class MemoryAllocationTests

// ✅ REQUIRED: Schema compatibility must be validated
public void ValidateSchemaCompatibility(Schema source, Schema target)
```

### **Testing Requirements** 🧪
- **Unit Test Coverage**: >90% for core components
- **Performance Benchmarks**: All hot paths must have BenchmarkDotNet tests
- **Memory Validation**: No memory leaks over 30-minute runs
- **Schema Testing**: All schema transformations must be validated
- **Cross-Platform**: Identical behavior on Windows/Linux/.NET 8

---

## 🚀 **Getting Started Commands**

### **Project Setup**
```bash
# Clone and build (WSL environment)
git clone <repository-url> /mnt/c/source/FlowEngine
cd /mnt/c/source/FlowEngine

# Build with .NET 8 optimizations
dotnet.exe build FlowEngine.sln --configuration Release

# Run performance benchmarks
dotnet.exe run --project benchmarks/FlowEngine.Benchmarks/ --configuration Release

# Run all tests including performance validation
dotnet.exe test tests/ --logger "console;verbosity=detailed" --configuration Release
```

### **Create New Component**
```bash
# Generate ArrayRow-compatible component
dotnet.exe new classlib -n FlowEngine.NewComponent -o src/FlowEngine.NewComponent/
dotnet.exe sln FlowEngine.sln add src/FlowEngine.NewComponent/FlowEngine.NewComponent.csproj

# Add to solution with proper references
dotnet.exe add src/FlowEngine.NewComponent/ reference src/FlowEngine.Abstractions/
```

### **Performance Validation**
```bash
# Validate ArrayRow performance targets
dotnet.exe run --project benchmarks/FlowEngine.Benchmarks/ --configuration Release --filter "*ArrayRow*"

# Memory pressure testing
dotnet.exe test tests/FlowEngine.Core.Tests/ --filter "MemoryBounded" --configuration Release

# Schema compatibility validation
dotnet.exe test tests/FlowEngine.Core.Tests/ --filter "Schema" --configuration Release
```

---

## 📋 **Final Implementation Checklist**

### **Before Starting Development** ✅
- [ ] Read and understand ArrayRow performance characteristics (3.25x improvement)
- [ ] Review schema-first design principles and validation requirements
- [ ] Set up .NET 8 development environment with BenchmarkDotNet
- [ ] Understand WSL/Windows dual environment path handling

### **During Implementation** ✅
- [ ] All ArrayRow operations achieve <15ns field access
- [ ] Schema validation happens at compile-time, not runtime
- [ ] Memory usage remains bounded regardless of dataset size
- [ ] All public APIs have comprehensive XML documentation
- [ ] Performance benchmarks validate all optimization claims

### **Before Code Review** ✅
- [ ] BenchmarkDotNet tests demonstrate performance improvements
- [ ] Memory allocation tests show no leaks or unbounded growth
- [ ] Schema compatibility tests cover all transformation scenarios
- [ ] Cross-platform tests validate Windows/Linux behavior
- [ ] Integration tests demonstrate end-to-end 200K+ rows/sec throughput

### **Production Readiness** ✅
- [ ] Performance regression tests prevent future degradation
- [ ] Comprehensive error handling with schema context preservation
- [ ] Observable diagnostics with minimal performance overhead
- [ ] Production configuration templates with .NET 8 optimizations
- [ ] Deployment guides for enterprise environments

---

**Document Version**: 1.1  
**Performance Validated**: June 28, 2025  
**Ready For**: Production implementation with ArrayRow architecture  
**Next Review**: Upon V1.0 implementation completion