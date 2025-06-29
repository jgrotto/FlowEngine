# FlowEngine Core Engine Implementation Specification v1.1
**Version**: 1.1  
**Status**: Current - Production Implementation Ready  
**Authors**: FlowEngine Team  
**Performance Validated**: June 28, 2025  
**Related Documents**: Core Architecture v1.1, Core Testing v1.1

---

## Document Control

| Version | Date | Author | Changes |
|---------|------|--------|---------|
| 1.0 | 2025-06-27 | FlowEngine Team | Initial implementation with DictionaryRow |
| 1.1 | 2025-06-28 | FlowEngine Team | ArrayRow implementation, schema-first design, validated performance |

---

## 1. Overview

### 1.1 Purpose and Scope
This specification defines the **detailed implementation requirements** for FlowEngine's core execution engine with ArrayRow architecture. It provides:

**This specification defines:**
- Complete interface definitions and class structures
- ArrayRow and Schema implementation with performance optimizations
- Memory management implementation with object pooling and spillover
- Port system implementation with schema validation
- DAG execution implementation with error context preservation
- Performance patterns and .NET 8 optimizations

### 1.2 Dependencies
**External Dependencies:**
- .NET 8.0 runtime with performance optimizations enabled
- C# 12.0 language features (primary constructors, collection expressions)
- System.Collections.Immutable for schema definitions
- System.Text.Json for configuration and schema serialization
- Microsoft.Extensions.ObjectPool for array pooling

**Internal Dependencies:**
- Core Engine Architecture Specification v1.1 (design principles)
- FlowEngine High-Level Project Overview v0.5 (performance requirements)

**Specification References:**
- Core Engine Architecture v1.1 (architectural context)
- Core Engine Testing v1.1 (validation requirements)
- Plugin API Specification (plugin integration interfaces)

### 1.3 Out of Scope
**Implementation Focus**: This document provides detailed implementation specifications. For architectural context, see companion documents:
- **Architecture Overview**: Component relationships, design patterns → Core Architecture v1.1
- **Testing Strategy**: Benchmarks, validation scenarios → Core Testing v1.1
- **Plugin Integration**: Plugin loading, resource governance → Plugin API Specification

**Future Implementation**: Advanced features deferred to V2 (async processing, distributed execution, schema evolution)

---

## 2. Implementation Goals

### 2.1 Primary Objectives
1. **Validated Performance**: Deliver 3.25x performance improvement through ArrayRow with O(1) field access
2. **Memory Efficiency**: Achieve 60-70% memory reduction through optimized data layout and pooling
3. **Type Safety**: Comprehensive schema validation with compile-time guarantees
4. **Resource Predictability**: Memory-bounded operation with intelligent spillover management
5. **Production Reliability**: Enterprise-grade error handling with schema context preservation

### 2.2 Implementation Quality Standards
**Performance Requirements**: All implementations must meet validated benchmarks:
- ArrayRow field access: <15ns per operation
- Schema validation: <1ms for 50-column schema
- Chunk processing: <5ms per 5K-row chunk
- End-to-end throughput: 200K+ rows/sec

**Code Quality Requirements**:
- Complete XML documentation for all public APIs
- Comprehensive error handling with meaningful error messages
- Thread-safe implementations where specified
- Resource cleanup with proper disposal patterns
- Performance optimization in hot code paths

### 2.3 .NET 8 Optimization Patterns
**Performance-First Implementation**:
- Use `Span<T>` and `Memory<T>` for high-performance array operations
- Leverage `ArrayPool<T>` for minimizing GC pressure
- Implement `IEquatable<T>` with cached hash codes for fast comparisons
- Use primary constructors and collection expressions for clean code
- Apply `[MethodImpl(MethodImplOptions.AggressiveInlining)]` for hot paths

---

## 3. Core Data Structures Implementation

### 3.1 ArrayRow Implementation

#### 3.1.1 Core ArrayRow Class
```csharp
/// <summary>
/// High-performance, immutable data row with schema-driven field access.
/// Provides 3.25x performance improvement over DictionaryRow.
/// Achieves O(1) field access through pre-calculated schema indexes.
/// </summary>
public sealed class ArrayRow : IEquatable<ArrayRow>, IDisposable
{
    private readonly object?[] _values;
    private readonly Schema _schema;
    private readonly int _hashCode;
    private volatile int _disposed;
    
    /// <summary>
    /// Creates ArrayRow with schema validation and value copying for immutability.
    /// </summary>
    public ArrayRow(Schema schema, object?[] values)
    {
        _schema = schema ?? throw new ArgumentNullException(nameof(schema));
        
        if (values == null)
            throw new ArgumentNullException(nameof(values));
        if (values.Length != schema.ColumnCount)
            throw new ArgumentException($"Value array length ({values.Length}) must match schema column count ({schema.ColumnCount})");
        
        // Validate values against schema constraints
        _schema.ValidateValues(values);
        
        // Copy array for immutability (critical for fan-out scenarios)
        _values = new object?[values.Length];
        Array.Copy(values, _values, values.Length);
        
        // Pre-calculate hash code for fast equality comparisons
        _hashCode = CalculateHashCode();
    }
    
    /// <summary>
    /// Gets the schema associated with this row.
    /// </summary>
    public Schema Schema => _schema;
    
    /// <summary>
    /// Gets the number of columns in this row.
    /// </summary>
    public int ColumnCount => _values.Length;
    
    /// <summary>
    /// Gets column names for this row.
    /// </summary>
    public IReadOnlyList<string> ColumnNames => _schema.ColumnNames;
    
    /// <summary>
    /// Fast field access via pre-calculated schema index (O(1) operation).
    /// This is the primary performance optimization over DictionaryRow.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public object? this[string columnName]
    {
        get
        {
            var index = _schema.GetIndex(columnName);
            return index >= 0 ? _values[index] : null;
        }
    }
    
    /// <summary>
    /// Direct indexed access for maximum performance (O(1) operation).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public object? this[int index]
    {
        get
        {
            if (index < 0 || index >= _values.Length)
                throw new ArgumentOutOfRangeException(nameof(index));
            return _values[index];
        }
    }
    
    /// <summary>
    /// Gets typed field value with optional default.
    /// </summary>
    public T? GetValue<T>(string columnName, T? defaultValue = default)
    {
        var value = this[columnName];
        return value is T typedValue ? typedValue : defaultValue;
    }
    
    /// <summary>
    /// Direct array access for maximum performance (internal use only).
    /// </summary>
    internal ReadOnlySpan<object?> Values => _disposed == 0 ? _values.AsSpan() : ReadOnlySpan<object?>.Empty;
    
    /// <summary>
    /// Creates a new ArrayRow with modified field value (immutable operation).
    /// Uses schema validation to ensure type safety.
    /// </summary>
    public ArrayRow With(string columnName, object? value)
    {
        var index = _schema.GetIndex(columnName);
        if (index < 0)
            throw new ArgumentException($"Column '{columnName}' not found in schema");
        
        // Validate value against schema constraints
        _schema.ValidateValue(index, value);
        
        // Create new array with modified value
        var newValues = new object?[_values.Length];
        Array.Copy(_values, newValues, _values.Length);
        newValues[index] = value;
        
        return new ArrayRow(_schema, newValues);
    }
    
    /// <summary>
    /// Creates a new ArrayRow with multiple field modifications.
    /// More efficient than multiple With() calls.
    /// </summary>
    public ArrayRow WithValues(IReadOnlyDictionary<string, object?> updates)
    {
        if (updates == null || updates.Count == 0)
            return this;
        
        var newValues = new object?[_values.Length];
        Array.Copy(_values, newValues, _values.Length);
        
        foreach (var kvp in updates)
        {
            var index = _schema.GetIndex(kvp.Key);
            if (index < 0)
                throw new ArgumentException($"Column '{kvp.Key}' not found in schema");
            
            _schema.ValidateValue(index, kvp.Value);
            newValues[index] = kvp.Value;
        }
        
        return new ArrayRow(_schema, newValues);
    }
    
    /// <summary>
    /// Equality comparison using cached hash codes for performance.
    /// </summary>
    public bool Equals(ArrayRow? other)
    {
        if (ReferenceEquals(this, other)) return true;
        if (other is null) return false;
        
        // Fast hash code comparison first
        if (_hashCode != other._hashCode) return false;
        
        // Schema must match
        if (!_schema.Equals(other._schema)) return false;
        
        // Value comparison
        return _values.AsSpan().SequenceEqual(other._values.AsSpan());
    }
    
    public override bool Equals(object? obj) => obj is ArrayRow other && Equals(other);
    public override int GetHashCode() => _hashCode;
    
    private int CalculateHashCode()
    {
        var hash = new HashCode();
        hash.Add(_schema);
        foreach (var value in _values)
        {
            hash.Add(value);
        }
        return hash.ToHashCode();
    }
    
    /// <summary>
    /// Converts ArrayRow to dictionary (for debugging/compatibility).
    /// Note: This is expensive and should not be used in hot paths.
    /// </summary>
    public IReadOnlyDictionary<string, object?> ToDictionary()
    {
        var result = new Dictionary<string, object?>();
        
        for (int i = 0; i < _values.Length; i++)
        {
            result[_schema.ColumnNames[i]] = _values[i];
        }
        
        return result;
    }
    
    /// <summary>
    /// Creates ArrayRow from dictionary (for migration/compatibility support).
    /// Note: This is expensive and should not be used in hot paths.
    /// </summary>
    public static ArrayRow FromDictionary(Schema schema, IReadOnlyDictionary<string, object?> data)
    {
        var values = new object?[schema.ColumnCount];
        
        for (int i = 0; i < schema.ColumnCount; i++)
        {
            var columnName = schema.ColumnNames[i];
            values[i] = data.GetValueOrDefault(columnName);
        }
        
        return new ArrayRow(schema, values);
    }
    
    /// <summary>
    /// Resource cleanup for object pool integration.
    /// </summary>
    public void Dispose()
    {
        if (Interlocked.CompareExchange(ref _disposed, 1, 0) == 0)
        {
            // Clear sensitive data
            Array.Clear(_values, 0, _values.Length);
        }
    }
    
    public override string ToString()
    {
        if (_disposed != 0) return "[Disposed ArrayRow]";
        
        var fields = new List<string>();
        for (int i = 0; i < Math.Min(_values.Length, 3); i++)
        {
            fields.Add($"{_schema.ColumnNames[i]}={_values[i]}");
        }
        
        var suffix = _values.Length > 3 ? $"...({_values.Length} total)" : "";
        return $"ArrayRow[{string.Join(", ", fields)}{suffix}]";
    }
}
```

### 3.2 Schema Management Implementation

#### 3.2.1 Schema Core Class
```csharp
/// <summary>
/// Defines data structure and validation rules for ArrayRow.
/// Provides high-performance field access via pre-calculated indexes.
/// Immutable and thread-safe for caching and reuse.
/// </summary>
public sealed class Schema : IEquatable<Schema>
{
    private readonly ImmutableArray<ColumnDefinition> _columns;
    private readonly ImmutableDictionary<string, int> _columnIndexes;
    private readonly ImmutableArray<string> _columnNames;
    private readonly string _signature;
    private readonly int _hashCode;
    
    public Schema(IEnumerable<ColumnDefinition> columns)
    {
        if (columns == null)
            throw new ArgumentNullException(nameof(columns));
        
        _columns = columns.ToImmutableArray();
        
        if (_columns.IsEmpty)
            throw new ArgumentException("Schema must have at least one column");
        
        // Build column index lookup for O(1) access
        var indexBuilder = ImmutableDictionary.CreateBuilder<string, int>(StringComparer.OrdinalIgnoreCase);
        var nameBuilder = ImmutableArray.CreateBuilder<string>(_columns.Length);
        
        for (int i = 0; i < _columns.Length; i++)
        {
            var column = _columns[i];
            if (string.IsNullOrWhiteSpace(column.Name))
                throw new ArgumentException($"Column at index {i} has null or empty name");
            
            if (indexBuilder.ContainsKey(column.Name))
                throw new ArgumentException($"Duplicate column name: {column.Name}");
            
            indexBuilder.Add(column.Name, i);
            nameBuilder.Add(column.Name);
        }
        
        _columnIndexes = indexBuilder.ToImmutable();
        _columnNames = nameBuilder.ToImmutable();
        
        // Create signature for caching and comparison
        _signature = string.Join(",", _columns.Select(c => $"{c.Name}:{c.DataType.Name}:{c.IsNullable}"));
        _hashCode = _signature.GetHashCode();
    }
    
    /// <summary>
    /// Gets all column definitions.
    /// </summary>
    public IReadOnlyList<ColumnDefinition> Columns => _columns;
    
    /// <summary>
    /// Gets column names in index order.
    /// </summary>
    public IReadOnlyList<string> ColumnNames => _columnNames;
    
    /// <summary>
    /// Gets the number of columns in this schema.
    /// </summary>
    public int ColumnCount => _columns.Length;
    
    /// <summary>
    /// Gets unique signature for caching and comparison.
    /// </summary>
    public string Signature => _signature;
    
    /// <summary>
    /// Fast column index lookup (O(1) operation).
    /// Returns -1 if column not found.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int GetIndex(string columnName)
    {
        return _columnIndexes.GetValueOrDefault(columnName, -1);
    }
    
    /// <summary>
    /// Tries to get column index with out parameter.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryGetIndex(string columnName, out int index)
    {
        return _columnIndexes.TryGetValue(columnName, out index);
    }
    
    /// <summary>
    /// Gets column definition by name.
    /// </summary>
    public ColumnDefinition? GetColumn(string columnName)
    {
        var index = GetIndex(columnName);
        return index >= 0 ? _columns[index] : null;
    }
    
    /// <summary>
    /// Gets column definition by index.
    /// </summary>
    public ColumnDefinition GetColumn(int index)
    {
        if (index < 0 || index >= _columns.Length)
            throw new ArgumentOutOfRangeException(nameof(index));
        return _columns[index];
    }
    
    /// <summary>
    /// Validates array of values against schema constraints.
    /// Throws detailed exceptions for validation failures.
    /// </summary>
    public void ValidateValues(object?[] values)
    {
        if (values == null)
            throw new ArgumentNullException(nameof(values));
        if (values.Length != _columns.Length)
            throw new ArgumentException($"Value count ({values.Length}) doesn't match schema column count ({_columns.Length})");
        
        for (int i = 0; i < values.Length; i++)
        {
            ValidateValue(i, values[i]);
        }
    }
    
    /// <summary>
    /// Validates single value against column constraints.
    /// </summary>
    public void ValidateValue(int columnIndex, object? value)
    {
        if (columnIndex < 0 || columnIndex >= _columns.Length)
            throw new ArgumentOutOfRangeException(nameof(columnIndex));
        
        var column = _columns[columnIndex];
        
        // Null value validation
        if (value == null)
        {
            if (!column.IsNullable)
                throw new ArgumentException($"Column '{column.Name}' does not allow null values");
            return;
        }
        
        // Type validation
        if (!column.DataType.IsInstanceOfType(value))
        {
            throw new ArgumentException($"Value '{value}' is not compatible with column '{column.Name}' type '{column.DataType.Name}'");
        }
        
        // Additional constraint validation (if implemented)
        column.ValidateConstraints(value);
    }
    
    /// <summary>
    /// Checks schema compatibility for port connections.
    /// </summary>
    public bool IsCompatibleWith(Schema other)
    {
        if (other == null) return false;
        if (ReferenceEquals(this, other)) return true;
        
        // For now, schemas must be identical
        // Future: implement structural compatibility
        return Equals(other);
    }
    
    /// <summary>
    /// Creates new schema with additional column.
    /// </summary>
    public Schema WithColumn(ColumnDefinition column)
    {
        if (column == null)
            throw new ArgumentNullException(nameof(column));
        
        if (_columnIndexes.ContainsKey(column.Name))
            throw new ArgumentException($"Column '{column.Name}' already exists");
        
        return new Schema(_columns.Add(column));
    }
    
    /// <summary>
    /// Creates new schema without specified column.
    /// </summary>
    public Schema WithoutColumn(string columnName)
    {
        var index = GetIndex(columnName);
        if (index < 0)
            throw new ArgumentException($"Column '{columnName}' not found");
        
        return new Schema(_columns.RemoveAt(index));
    }
    
    /// <summary>
    /// High-performance equality comparison using cached signatures.
    /// </summary>
    public bool Equals(Schema? other)
    {
        if (ReferenceEquals(this, other)) return true;
        if (other is null) return false;
        
        // Fast signature comparison
        return _signature == other._signature;
    }
    
    public override bool Equals(object? obj) => obj is Schema other && Equals(other);
    public override int GetHashCode() => _hashCode;
    
    public override string ToString() => $"Schema[{_columns.Length} columns: {string.Join(", ", _columnNames.Take(3))}{(_columns.Length > 3 ? "..." : "")}]";
}
```

#### 3.2.2 Column Definition Implementation
```csharp
/// <summary>
/// Defines a single column in a schema with type and constraint information.
/// </summary>
public sealed record ColumnDefinition
{
    public ColumnDefinition(string name, Type dataType, bool isNullable = true, IReadOnlyList<IColumnConstraint>? constraints = null)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Column name cannot be null or empty", nameof(name));
        
        Name = name;
        DataType = dataType ?? throw new ArgumentNullException(nameof(dataType));
        IsNullable = isNullable;
        Constraints = constraints ?? Array.Empty<IColumnConstraint>();
    }
    
    /// <summary>
    /// Column name (case-insensitive for lookups).
    /// </summary>
    public string Name { get; }
    
    /// <summary>
    /// .NET type for this column.
    /// </summary>
    public Type DataType { get; }
    
    /// <summary>
    /// Whether this column allows null values.
    /// </summary>
    public bool IsNullable { get; }
    
    /// <summary>
    /// Additional validation constraints.
    /// </summary>
    public IReadOnlyList<IColumnConstraint> Constraints { get; }
    
    /// <summary>
    /// Validates value against all column constraints.
    /// </summary>
    public void ValidateConstraints(object value)
    {
        foreach (var constraint in Constraints)
        {
            constraint.Validate(value, Name);
        }
    }
    
    /// <summary>
    /// Creates column definition for common .NET types.
    /// </summary>
    public static ColumnDefinition String(string name, bool isNullable = true, int? maxLength = null)
    {
        var constraints = new List<IColumnConstraint>();
        if (maxLength.HasValue)
            constraints.Add(new MaxLengthConstraint(maxLength.Value));
        
        return new ColumnDefinition(name, typeof(string), isNullable, constraints);
    }
    
    public static ColumnDefinition Int32(string name, bool isNullable = false, int? minValue = null, int? maxValue = null)
    {
        var constraints = new List<IColumnConstraint>();
        if (minValue.HasValue || maxValue.HasValue)
            constraints.Add(new RangeConstraint(minValue, maxValue));
        
        return new ColumnDefinition(name, typeof(int), isNullable, constraints);
    }
    
    public static ColumnDefinition DateTime(string name, bool isNullable = true)
        => new(name, typeof(DateTime), isNullable);
    
    public static ColumnDefinition Decimal(string name, bool isNullable = true, decimal? minValue = null, decimal? maxValue = null)
    {
        var constraints = new List<IColumnConstraint>();
        if (minValue.HasValue || maxValue.HasValue)
            constraints.Add(new RangeConstraint(minValue, maxValue));
        
        return new ColumnDefinition(name, typeof(decimal), isNullable, constraints);
    }
}

/// <summary>
/// Interface for column value constraints.
/// </summary>
public interface IColumnConstraint
{
    void Validate(object value, string columnName);
}

/// <summary>
/// Maximum length constraint for string columns.
/// </summary>
public sealed record MaxLengthConstraint(int MaxLength) : IColumnConstraint
{
    public void Validate(object value, string columnName)
    {
        if (value is string str && str.Length > MaxLength)
            throw new ArgumentException($"Column '{columnName}' value exceeds maximum length of {MaxLength}");
    }
}

/// <summary>
/// Range constraint for numeric columns.
/// </summary>
public sealed record RangeConstraint(IComparable? MinValue, IComparable? MaxValue) : IColumnConstraint
{
    public void Validate(object value, string columnName)
    {
        if (value is not IComparable comparable) return;
        
        if (MinValue != null && comparable.CompareTo(MinValue) < 0)
            throw new ArgumentException($"Column '{columnName}' value {value} is below minimum {MinValue}");
        
        if (MaxValue != null && comparable.CompareTo(MaxValue) > 0)
            throw new ArgumentException($"Column '{columnName}' value {value} exceeds maximum {MaxValue}");
    }
}
```

### 3.3 Dataset and Chunk Implementation

#### 3.3.1 Dataset Interface and Implementation
```csharp
/// <summary>
/// Represents a streaming collection of ArrayRow data with schema awareness.
/// Supports memory-bounded processing through chunking.
/// </summary>
public interface IDataset : IAsyncDisposable
{
    /// <summary>
    /// Schema for all rows in this dataset.
    /// </summary>
    Schema Schema { get; }
    
    /// <summary>
    /// Estimated row count if known (may be null for unbounded streams).
    /// </summary>
    long? EstimatedRowCount { get; }
    
    /// <summary>
    /// Gets chunks of data for streaming processing.
    /// </summary>
    IAsyncEnumerable<Chunk> GetChunksAsync(ChunkingOptions options, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Gets all rows as async enumerable (convenience method).
    /// </summary>
    IAsyncEnumerable<ArrayRow> GetRowsAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Configuration for chunk size and behavior.
/// </summary>
public sealed record ChunkingOptions
{
    public static readonly ChunkingOptions Default = new();
    
    /// <summary>
    /// Target number of rows per chunk.
    /// </summary>
    public int TargetRowsPerChunk { get; init; } = 5000;
    
    /// <summary>
    /// Maximum memory per chunk in bytes.
    /// </summary>
    public long MaxMemoryPerChunk { get; init; } = 50 * 1024 * 1024; // 50MB
    
    /// <summary>
    /// Whether to adapt chunk size based on memory pressure.
    /// </summary>
    public bool AdaptiveChunkSize { get; init; } = true;
    
    /// <summary>
    /// Calculates optimal chunk size based on schema and memory constraints.
    /// </summary>
    public int CalculateOptimalSize(Schema schema)
    {
        if (!AdaptiveChunkSize) return TargetRowsPerChunk;
        
        // Estimate bytes per row based on schema
        var estimatedBytesPerRow = EstimateBytesPerRow(schema);
        var maxRowsByMemory = (int)(MaxMemoryPerChunk / estimatedBytesPerRow);
        
        return Math.Min(TargetRowsPerChunk, Math.Max(100, maxRowsByMemory));
    }
    
    private static long EstimateBytesPerRow(Schema schema)
    {
        // Conservative estimation of memory usage per row
        long totalBytes = 40; // Base ArrayRow overhead
        
        foreach (var column in schema.Columns)
        {
            totalBytes += column.DataType.Name switch
            {
                nameof(String) => 50, // Average string length estimate
                nameof(Int32) => 8,
                nameof(Int64) => 12,
                nameof(DateTime) => 12,
                nameof(Decimal) => 20,
                nameof(Double) => 12,
                nameof(Boolean) => 4,
                _ => 16 // Generic object reference
            };
        }
        
        return totalBytes;
    }
}

/// <summary>
/// In-memory dataset implementation for testing and small datasets.
/// </summary>
public sealed class MemoryDataset : IDataset
{
    private readonly IReadOnlyList<ArrayRow> _rows;
    private volatile bool _disposed;
    
    public MemoryDataset(Schema schema, IEnumerable<ArrayRow> rows)
    {
        Schema = schema ?? throw new ArgumentNullException(nameof(schema));
        
        if (rows == null)
            throw new ArgumentNullException(nameof(rows));
        
        _rows = rows.ToList();
        
        // Validate all rows have correct schema
        foreach (var row in _rows)
        {
            if (!row.Schema.Equals(schema))
                throw new ArgumentException("All rows must have the same schema as the dataset");
        }
    }
    
    public Schema Schema { get; }
    
    public long? EstimatedRowCount => _rows.Count;
    
    public async IAsyncEnumerable<Chunk> GetChunksAsync(
        ChunkingOptions options, 
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(MemoryDataset));
        
        var chunkSize = options.CalculateOptimalSize(Schema);
        
        for (int i = 0; i < _rows.Count; i += chunkSize)
        {
            cancellationToken.ThrowIfCancellationRequested();
            
            var chunkRows = _rows.Skip(i).Take(chunkSize).ToArray();
            yield return new Chunk(Schema, chunkRows);
            
            // Yield control to prevent blocking
            await Task.Yield();
        }
    }
    
    public async IAsyncEnumerable<ArrayRow> GetRowsAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var chunk in GetChunksAsync(ChunkingOptions.Default, cancellationToken))
        {
            foreach (var row in chunk.Rows)
            {
                yield return row;
            }
        }
    }
    
    public ValueTask DisposeAsync()
    {
        _disposed = true;
        return ValueTask.CompletedTask;
    }
}
```

#### 3.3.2 Chunk Implementation
```csharp
/// <summary>
/// Fixed-size collection of ArrayRows for memory-bounded processing.
/// Designed for high-performance enumeration and minimal allocations.
/// </summary>
public sealed class Chunk : IDisposable
{
    private readonly ArrayRow[] _rows;
    private readonly Schema _schema;
    private volatile int _disposed;
    
    public Chunk(Schema schema, ArrayRow[] rows)
    {
        _schema = schema ?? throw new ArgumentNullException(nameof(schema));
        _rows = rows ?? throw new ArgumentNullException(nameof(rows));
        
        // Validate all rows have correct schema
        foreach (var row in _rows)
        {
            if (!row.Schema.Equals(schema))
                throw new ArgumentException("All rows must have the same schema as the chunk");
        }
    }
    
    /// <summary>
    /// Schema for all rows in this chunk.
    /// </summary>
    public Schema Schema => _schema;
    
    /// <summary>
    /// Number of rows in this chunk.
    /// </summary>
    public int RowCount => _rows.Length;
    
    /// <summary>
    /// High-performance access to rows as ReadOnlySpan.
    /// </summary>
    public ReadOnlySpan<ArrayRow> Rows => _disposed == 0 ? _rows.AsSpan() : ReadOnlySpan<ArrayRow>.Empty;
    
    /// <summary>
    /// Enumerates rows with minimal allocation.
    /// </summary>
    public IEnumerable<ArrayRow> EnumerateRows()
    {
        if (_disposed != 0)
            throw new ObjectDisposedException(nameof(Chunk));
        
        return _rows;
    }
    
    /// <summary>
    /// Gets row at specified index.
    /// </summary>
    public ArrayRow this[int index]
    {
        get
        {
            if (_disposed != 0)
                throw new ObjectDisposedException(nameof(Chunk));
            if (index < 0 || index >= _rows.Length)
                throw new ArgumentOutOfRangeException(nameof(index));
            
            return _rows[index];
        }
    }
    
    /// <summary>
    /// Creates new chunk with transformed rows.
    /// </summary>
    public Chunk Transform(Func<ArrayRow, ArrayRow> transformer)
    {
        if (_disposed != 0)
            throw new ObjectDisposedException(nameof(Chunk));
        if (transformer == null)
            throw new ArgumentNullException(nameof(transformer));
        
        var transformedRows = new ArrayRow[_rows.Length];
        for (int i = 0; i < _rows.Length; i++)
        {
            transformedRows[i] = transformer(_rows[i]);
        }
        
        return new Chunk(_schema, transformedRows);
    }
    
    /// <summary>
    /// Creates new chunk with filtered rows.
    /// </summary>
    public Chunk Filter(Func<ArrayRow, bool> predicate)
    {
        if (_disposed != 0)
            throw new ObjectDisposedException(nameof(Chunk));
        if (predicate == null)
            throw new ArgumentNullException(nameof(predicate));
        
        var filteredRows = _rows.Where(predicate).ToArray();
        return new Chunk(_schema, filteredRows);
    }
    
    /// <summary>
    /// Estimates memory usage of this chunk.
    /// </summary>
    public long EstimateMemoryUsage()
    {
        // Base chunk overhead
        long totalBytes = 100;
        
        // ArrayRow overhead per row
        totalBytes += _rows.Length * 40;
        
        // Estimate value storage
        foreach (var row in _rows)
        {
            totalBytes += EstimateRowMemoryUsage(row);
        }
        
        return totalBytes;
    }
    
    private static long EstimateRowMemoryUsage(ArrayRow row)
    {
        long bytes = 0;
        foreach (var column in row.Schema.Columns)
        {
            var value = row[column.Name];
            bytes += value switch
            {
                null => 0,
                string str => str.Length * 2 + 24, // UTF-16 + string overhead
                int => 4,
                long => 8,
                decimal => 16,
                DateTime => 8,
                double => 8,
                bool => 1,
                _ => 24 // Estimated object overhead
            };
        }
        return bytes;
    }
    
    public void Dispose()
    {
        if (Interlocked.CompareExchange(ref _disposed, 1, 0) == 0)
        {
            // Dispose all rows to return them to pools
            foreach (var row in _rows)
            {
                row.Dispose();
            }
        }
    }
    
    public override string ToString() => 
        _disposed != 0 ? "[Disposed Chunk]" : $"Chunk[{_rows.Length} rows, Schema: {_schema.ColumnCount} columns]";
}
```

---

## 4. Memory Management Implementation

### 4.1 Memory Manager Interface and Core Implementation

```csharp
/// <summary>
/// Central memory management for ArrayRow allocation, pooling, and spillover.
/// Provides memory-bounded operation with intelligent resource management.
/// </summary>
public interface IMemoryManager : IDisposable
{
    /// <summary>
    /// Current memory usage in bytes.
    /// </summary>
    long CurrentMemoryUsage { get; }
    
    /// <summary>
    /// Maximum memory allowed before spillover.
    /// </summary>
    long MaxMemoryThreshold { get; }
    
    /// <summary>
    /// Whether spillover is currently active.
    /// </summary>
    bool IsSpilloverActive { get; }
    
    /// <summary>
    /// Allocates array for ArrayRow with pooling optimization.
    /// </summary>
    MemoryAllocation<object?> AllocateArray(int size);
    
    /// <summary>
    /// Creates ArrayRow pool for specific schema.
    /// </summary>
    IObjectPool<ArrayRow> CreateRowPool(Schema schema);
    
    /// <summary>
    /// Triggers spillover for large datasets.
    /// </summary>
    Task<ISpilloverHandle> SpilloverAsync(IAsyncEnumerable<Chunk> chunks, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Memory pressure monitoring event.
    /// </summary>
    event EventHandler<MemoryPressureEventArgs>? MemoryPressure;
}

/// <summary>
/// Default memory manager implementation with ArrayPool and spillover support.
/// </summary>
public sealed class DefaultMemoryManager : IMemoryManager
{
    private readonly ArrayPool<object?> _arrayPool;
    private readonly ConcurrentDictionary<string, SchemaRowPool> _rowPools;
    private readonly ISpilloverProvider _spilloverProvider;
    private readonly MemoryMonitor _memoryMonitor;
    private readonly object _lockObject = new();
    private volatile bool _disposed;
    
    public DefaultMemoryManager(MemoryManagerOptions? options = null)
    {
        var opts = options ?? MemoryManagerOptions.Default;
        
        _arrayPool = ArrayPool<object?>.Create(opts.MaxArraysRetained, opts.MaxArrayLength);
        _rowPools = new ConcurrentDictionary<string, SchemaRowPool>();
        _spilloverProvider = opts.SpilloverProvider ?? new FileSpilloverProvider(opts.SpilloverDirectory);
        _memoryMonitor = new MemoryMonitor(opts.MaxMemoryThreshold, opts.PressureCheckInterval);
        
        _memoryMonitor.MemoryPressure += OnMemoryPressure;
    }
    
    public long CurrentMemoryUsage => _memoryMonitor.CurrentUsage;
    public long MaxMemoryThreshold => _memoryMonitor.MaxThreshold;
    public bool IsSpilloverActive => _spilloverProvider.IsActive;
    
    public event EventHandler<MemoryPressureEventArgs>? MemoryPressure;
    
    /// <summary>
    /// Allocates array with automatic pooling and disposal tracking.
    /// </summary>
    public MemoryAllocation<object?> AllocateArray(int size)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(DefaultMemoryManager));
        
        var array = _arrayPool.Get(size);
        _memoryMonitor.RecordAllocation(size * IntPtr.Size);
        
        return new MemoryAllocation<object?>(array, size, () =>
        {
            // Clear array before returning to pool for security
            Array.Clear(array, 0, size);
            _arrayPool.Return(array);
            _memoryMonitor.RecordDeallocation(size * IntPtr.Size);
        });
    }
    
    /// <summary>
    /// Creates or retrieves cached row pool for schema.
    /// </summary>
    public IObjectPool<ArrayRow> CreateRowPool(Schema schema)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(DefaultMemoryManager));
        
        return _rowPools.GetOrAdd(schema.Signature, _ => new SchemaRowPool(schema, this));
    }
    
    /// <summary>
    /// Implements intelligent spillover when memory pressure is high.
    /// </summary>
    public async Task<ISpilloverHandle> SpilloverAsync(IAsyncEnumerable<Chunk> chunks, CancellationToken cancellationToken = default)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(DefaultMemoryManager));
        
        return await _spilloverProvider.SpilloverAsync(chunks, cancellationToken);
    }
    
    private void OnMemoryPressure(object? sender, MemoryPressureEventArgs e)
    {
        MemoryPressure?.Invoke(this, e);
        
        // Automatic cleanup on high pressure
        if (e.PressureLevel >= MemoryPressureLevel.High)
        {
            TriggerCleanup();
        }
    }
    
    private void TriggerCleanup()
    {
        lock (_lockObject)
        {
            // Clear least recently used row pools
            var oldPools = _rowPools.Values
                .OrderBy(p => p.LastAccess)
                .Take(_rowPools.Count / 2)
                .ToList();
            
            foreach (var pool in oldPools)
            {
                _rowPools.TryRemove(pool.Schema.Signature, out _);
                pool.Dispose();
            }
            
            // Force garbage collection
            GC.Collect(2, GCCollectionMode.Forced, blocking: true);
        }
    }
    
    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
            
            _memoryMonitor.MemoryPressure -= OnMemoryPressure;
            _memoryMonitor.Dispose();
            
            foreach (var pool in _rowPools.Values)
            {
                pool.Dispose();
            }
            _rowPools.Clear();
            
            _spilloverProvider.Dispose();
        }
    }
}

/// <summary>
/// Configuration options for memory manager.
/// </summary>
public sealed record MemoryManagerOptions
{
    public static readonly MemoryManagerOptions Default = new();
    
    /// <summary>
    /// Maximum memory threshold before spillover (default: 1GB).
    /// </summary>
    public long MaxMemoryThreshold { get; init; } = 1024 * 1024 * 1024;
    
    /// <summary>
    /// Maximum arrays retained in pool per schema.
    /// </summary>
    public int MaxArraysRetained { get; init; } = 1000;
    
    /// <summary>
    /// Maximum array length for pooling.
    /// </summary>
    public int MaxArrayLength { get; init; } = 10000;
    
    /// <summary>
    /// How often to check memory pressure.
    /// </summary>
    public TimeSpan PressureCheckInterval { get; init; } = TimeSpan.FromSeconds(5);
    
    /// <summary>
    /// Directory for spillover files.
    /// </summary>
    public string SpilloverDirectory { get; init; } = Path.GetTempPath();
    
    /// <summary>
    /// Custom spillover provider.
    /// </summary>
    public ISpilloverProvider? SpilloverProvider { get; init; }
}

/// <summary>
/// Schema-specific ArrayRow object pool with lifecycle management.
/// </summary>
public sealed class SchemaRowPool : IObjectPool<ArrayRow>, IDisposable
{
    private readonly Schema _schema;
    private readonly IMemoryManager _memoryManager;
    private readonly ConcurrentQueue<ArrayRow> _pool = new();
    private readonly object _lockObject = new();
    private volatile int _currentCount;
    private volatile DateTime _lastAccess = DateTime.UtcNow;
    private volatile bool _disposed;
    
    private const int MaxPoolSize = 100;
    
    public SchemaRowPool(Schema schema, IMemoryManager memoryManager)
    {
        _schema = schema ?? throw new ArgumentNullException(nameof(schema));
        _memoryManager = memoryManager ?? throw new ArgumentNullException(nameof(memoryManager));
    }
    
    public Schema Schema => _schema;
    public DateTime LastAccess => _lastAccess;
    
    public ArrayRow Get()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(SchemaRowPool));
        
        _lastAccess = DateTime.UtcNow;
        
        if (_pool.TryDequeue(out var row))
        {
            Interlocked.Decrement(ref _currentCount);
            return row;
        }
        
        // Create new ArrayRow with pooled array allocation
        using var allocation = _memoryManager.AllocateArray(_schema.ColumnCount);
        var values = allocation.Span.ToArray();
        return new ArrayRow(_schema, values);
    }
    
    public void Return(ArrayRow obj)
    {
        if (_disposed || obj == null || !obj.Schema.Equals(_schema))
            return;
        
        // Only pool if under limit
        if (_currentCount < MaxPoolSize)
        {
            _pool.Enqueue(obj);
            Interlocked.Increment(ref _currentCount);
        }
        else
        {
            obj.Dispose();
        }
    }
    
    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
            
            // Dispose all pooled rows
            while (_pool.TryDequeue(out var row))
            {
                row.Dispose();
            }
        }
    }
}

/// <summary>
/// Memory allocation wrapper with automatic cleanup.
/// </summary>
public sealed class MemoryAllocation<T> : IDisposable
{
    private readonly T[] _array;
    private readonly Action? _onDispose;
    private volatile int _disposed;
    
    public MemoryAllocation(T[] array, int length, Action? onDispose = null)
    {
        _array = array ?? throw new ArgumentNullException(nameof(array));
        Length = length;
        _onDispose = onDispose;
    }
    
    public Span<T> Span => _disposed == 0 ? _array.AsSpan(0, Length) : Span<T>.Empty;
    public Memory<T> Memory => _disposed == 0 ? _array.AsMemory(0, Length) : Memory<T>.Empty;
    public int Length { get; }
    
    public void Dispose()
    {
        if (Interlocked.CompareExchange(ref _disposed, 1, 0) == 0)
        {
            _onDispose?.Invoke();
        }
    }
}
```

### 4.2 Spillover Implementation

```csharp
/// <summary>
/// Interface for spillover storage providers.
/// </summary>
public interface ISpilloverProvider : IDisposable
{
    bool IsActive { get; }
    Task<ISpilloverHandle> SpilloverAsync(IAsyncEnumerable<Chunk> chunks, CancellationToken cancellationToken = default);
}

/// <summary>
/// Handle for accessing spilled data.
/// </summary>
public interface ISpilloverHandle : IAsyncDisposable
{
    Schema Schema { get; }
    long EstimatedRowCount { get; }
    IAsyncEnumerable<Chunk> GetChunksAsync(ChunkingOptions options, CancellationToken cancellationToken = default);
}

/// <summary>
/// File-based spillover implementation with compression.
/// </summary>
public sealed class FileSpilloverProvider : ISpilloverProvider
{
    private readonly string _spilloverDirectory;
    private readonly ConcurrentDictionary<string, FileSpilloverHandle> _activeHandles = new();
    private volatile bool _disposed;
    
    public FileSpilloverProvider(string spilloverDirectory)
    {
        _spilloverDirectory = spilloverDirectory ?? throw new ArgumentNullException(nameof(spilloverDirectory));
        Directory.CreateDirectory(_spilloverDirectory);
    }
    
    public bool IsActive => _activeHandles.Count > 0;
    
    public async Task<ISpilloverHandle> SpilloverAsync(IAsyncEnumerable<Chunk> chunks, CancellationToken cancellationToken = default)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(FileSpilloverProvider));
        
        var spillFile = Path.Combine(_spilloverDirectory, $"spill_{Guid.NewGuid():N}.dat");
        var handle = new FileSpilloverHandle(spillFile);
        
        _activeHandles[spillFile] = handle;
        
        try
        {
            await handle.WriteChunksAsync(chunks, cancellationToken);
            return handle;
        }
        catch
        {
            _activeHandles.TryRemove(spillFile, out _);
            await handle.DisposeAsync();
            throw;
        }
    }
    
    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
            
            foreach (var handle in _activeHandles.Values)
            {
                handle.DisposeAsync().AsTask().Wait(TimeSpan.FromSeconds(5));
            }
            _activeHandles.Clear();
        }
    }
}

/// <summary>
/// File-based spillover handle with binary serialization.
/// </summary>
public sealed class FileSpilloverHandle : ISpilloverHandle
{
    private readonly string _filePath;
    private Schema? _schema;
    private long _estimatedRowCount;
    private volatile bool _disposed;
    
    public FileSpilloverHandle(string filePath)
    {
        _filePath = filePath ?? throw new ArgumentNullException(nameof(filePath));
    }
    
    public Schema Schema => _schema ?? throw new InvalidOperationException("Spillover not initialized");
    public long EstimatedRowCount => _estimatedRowCount;
    
    internal async Task WriteChunksAsync(IAsyncEnumerable<Chunk> chunks, CancellationToken cancellationToken)
    {
        using var fileStream = new FileStream(_filePath, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize: 64 * 1024);
        using var gzipStream = new GZipStream(fileStream, CompressionLevel.Fastest);
        using var writer = new BinaryWriter(gzipStream);
        
        await foreach (var chunk in chunks.WithCancellation(cancellationToken))
        {
            if (_schema == null)
            {
                _schema = chunk.Schema;
                WriteSchema(writer, _schema);
            }
            
            WriteChunk(writer, chunk);
            _estimatedRowCount += chunk.RowCount;
        }
    }
    
    public async IAsyncEnumerable<Chunk> GetChunksAsync(
        ChunkingOptions options, 
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(FileSpilloverHandle));
        
        using var fileStream = new FileStream(_filePath, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 64 * 1024);
        using var gzipStream = new GZipStream(fileStream, CompressionMode.Decompress);
        using var reader = new BinaryReader(gzipStream);
        
        var schema = ReadSchema(reader);
        var targetChunkSize = options.CalculateOptimalSize(schema);
        
        var currentChunkRows = new List<ArrayRow>(targetChunkSize);
        
        try
        {
            while (reader.BaseStream.Position < reader.BaseStream.Length)
            {
                cancellationToken.ThrowIfCancellationRequested();
                
                var chunk = ReadChunk(reader, schema);
                
                foreach (var row in chunk.EnumerateRows())
                {
                    currentChunkRows.Add(row);
                    
                    if (currentChunkRows.Count >= targetChunkSize)
                    {
                        yield return new Chunk(schema, currentChunkRows.ToArray());
                        currentChunkRows.Clear();
                    }
                }
            }
            
            // Return final partial chunk
            if (currentChunkRows.Count > 0)
            {
                yield return new Chunk(schema, currentChunkRows.ToArray());
            }
        }
        finally
        {
            // Cleanup any remaining rows
            foreach (var row in currentChunkRows)
            {
                row.Dispose();
            }
        }
    }
    
    private static void WriteSchema(BinaryWriter writer, Schema schema)
    {
        writer.Write(schema.Signature);
        writer.Write(schema.ColumnCount);
        
        foreach (var column in schema.Columns)
        {
            writer.Write(column.Name);
            writer.Write(column.DataType.FullName ?? string.Empty);
            writer.Write(column.IsNullable);
        }
    }
    
    private static Schema ReadSchema(BinaryReader reader)
    {
        var signature = reader.ReadString();
        var columnCount = reader.ReadInt32();
        var columns = new List<ColumnDefinition>(columnCount);
        
        for (int i = 0; i < columnCount; i++)
        {
            var name = reader.ReadString();
            var typeName = reader.ReadString();
            var isNullable = reader.ReadBoolean();
            
            var type = Type.GetType(typeName) ?? typeof(object);
            columns.Add(new ColumnDefinition(name, type, isNullable));
        }
        
        return new Schema(columns);
    }
    
    private static void WriteChunk(BinaryWriter writer, Chunk chunk)
    {
        writer.Write(chunk.RowCount);
        
        foreach (var row in chunk.EnumerateRows())
        {
            WriteRow(writer, row);
        }
    }
    
    private static Chunk ReadChunk(BinaryReader reader, Schema schema)
    {
        var rowCount = reader.ReadInt32();
        var rows = new ArrayRow[rowCount];
        
        for (int i = 0; i < rowCount; i++)
        {
            rows[i] = ReadRow(reader, schema);
        }
        
        return new Chunk(schema, rows);
    }
    
    private static void WriteRow(BinaryWriter writer, ArrayRow row)
    {
        foreach (var value in row.Values)
        {
            WriteValue(writer, value);
        }
    }
    
    private static ArrayRow ReadRow(BinaryReader reader, Schema schema)
    {
        var values = new object?[schema.ColumnCount];
        
        for (int i = 0; i < schema.ColumnCount; i++)
        {
            values[i] = ReadValue(reader, schema.GetColumn(i).DataType);
        }
        
        return new ArrayRow(schema, values);
    }
    
    private static void WriteValue(BinaryWriter writer, object? value)
    {
        if (value == null)
        {
            writer.Write(false); // null marker
            return;
        }
        
        writer.Write(true); // non-null marker
        
        switch (value)
        {
            case string str:
                writer.Write(str);
                break;
            case int i:
                writer.Write(i);
                break;
            case long l:
                writer.Write(l);
                break;
            case decimal d:
                writer.Write(d);
                break;
            case DateTime dt:
                writer.Write(dt.ToBinary());
                break;
            case double dbl:
                writer.Write(dbl);
                break;
            case bool b:
                writer.Write(b);
                break;
            default:
                // Fallback to JSON serialization
                var json = JsonSerializer.Serialize(value);
                writer.Write(json);
                break;
        }
    }
    
    private static object? ReadValue(BinaryReader reader, Type targetType)
    {
        var hasValue = reader.ReadBoolean();
        if (!hasValue) return null;
        
        if (targetType == typeof(string))
            return reader.ReadString();
        if (targetType == typeof(int))
            return reader.ReadInt32();
        if (targetType == typeof(long))
            return reader.ReadInt64();
        if (targetType == typeof(decimal))
            return reader.ReadDecimal();
        if (targetType == typeof(DateTime))
            return DateTime.FromBinary(reader.ReadInt64());
        if (targetType == typeof(double))
            return reader.ReadDouble();
        if (targetType == typeof(bool))
            return reader.ReadBoolean();
        
        // Fallback: JSON deserialization
        var json = reader.ReadString();
        return JsonSerializer.Deserialize(json, targetType);
    }
    
    public async ValueTask DisposeAsync()
    {
        if (!_disposed)
        {
            _disposed = true;
            
            try
            {
                if (File.Exists(_filePath))
                {
                    File.Delete(_filePath);
                }
            }
            catch
            {
                // Best effort cleanup
            }
        }
    }
}
```

---

## 5. Port System Implementation

### 5.1 Port Interfaces and Core Implementation

```csharp
/// <summary>
/// Base interface for all ports in the FlowEngine system.
/// Provides schema awareness and connection management.
/// </summary>
public interface IPort
{
    /// <summary>
    /// Unique identifier for this port.
    /// </summary>
    string Id { get; }
    
    /// <summary>
    /// Schema for data flowing through this port.
    /// </summary>
    Schema Schema { get; }
    
    /// <summary>
    /// Whether this port is currently connected.
    /// </summary>
    bool IsConnected { get; }
    
    /// <summary>
    /// Connection state change events.
    /// </summary>
    event EventHandler<PortConnectionEventArgs>? ConnectionChanged;
}

/// <summary>
/// Input port for receiving data from upstream steps.
/// </summary>
public interface IInputPort : IPort
{
    /// <summary>
    /// Receives chunks from connected output port.
    /// </summary>
    IAsyncEnumerable<Chunk> ReceiveChunksAsync(CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Connects to an output port with schema validation.
    /// </summary>
    void ConnectFrom(IOutputPort outputPort);
    
    /// <summary>
    /// Disconnects from current output port.
    /// </summary>
    void Disconnect();
}

/// <summary>
/// Output port for sending data to downstream steps.
/// </summary>
public interface IOutputPort : IPort
{
    /// <summary>
    /// Sends chunks to all connected input ports.
    /// </summary>
    Task SendChunkAsync(Chunk chunk, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Connects to an input port with schema validation.
    /// </summary>
    void ConnectTo(IInputPort inputPort);
    
    /// <summary>
    /// Disconnects from specific input port.
    /// </summary>
    void DisconnectFrom(IInputPort inputPort);
    
    /// <summary>
    /// Signals completion to all connected ports.
    /// </summary>
    Task CompleteAsync();
    
    /// <summary>
    /// Gets all connected input ports.
    /// </summary>
    IReadOnlyList<IInputPort> ConnectedPorts { get; }
}

/// <summary>
/// Default implementation of input port with buffering and validation.
/// </summary>
public sealed class InputPort : IInputPort, IDisposable
{
    private readonly string _id;
    private readonly Schema _schema;
    private readonly Channel<Chunk> _channel;
    private readonly ChannelWriter<Chunk> _writer;
    private readonly ChannelReader<Chunk> _reader;
    private IOutputPort? _connectedPort;
    private volatile bool _disposed;
    
    public InputPort(string id, Schema schema, BoundedChannelOptions? channelOptions = null)
    {
        _id = id ?? throw new ArgumentNullException(nameof(id));
        _schema = schema ?? throw new ArgumentNullException(nameof(schema));
        
        var options = channelOptions ?? new BoundedChannelOptions(1000)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false
        };
        
        _channel = Channel.CreateBounded<Chunk>(options);
        _writer = _channel.Writer;
        _reader = _channel.Reader;
    }
    
    public string Id => _id;
    public Schema Schema => _schema;
    public bool IsConnected => _connectedPort != null;
    
    public event EventHandler<PortConnectionEventArgs>? ConnectionChanged;
    
    public async IAsyncEnumerable<Chunk> ReceiveChunksAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(InputPort));
        
        await foreach (var chunk in _reader.ReadAllAsync(cancellationToken))
        {
            // Validate chunk schema matches port schema
            if (!chunk.Schema.IsCompatibleWith(_schema))
            {
                throw new InvalidOperationException($"Received chunk with incompatible schema. Expected: {_schema.Signature}, Received: {chunk.Schema.Signature}");
            }
            
            yield return chunk;
        }
    }
    
    public void ConnectFrom(IOutputPort outputPort)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(InputPort));
        if (outputPort == null)
            throw new ArgumentNullException(nameof(outputPort));
        
        // Validate schema compatibility
        if (!outputPort.Schema.IsCompatibleWith(_schema))
        {
            throw new ArgumentException($"Output port schema ({outputPort.Schema.Signature}) is not compatible with input port schema ({_schema.Signature})");
        }
        
        if (_connectedPort != null)
        {
            throw new InvalidOperationException($"Input port {_id} is already connected");
        }
        
        _connectedPort = outputPort;
        outputPort.ConnectTo(this);
        
        ConnectionChanged?.Invoke(this, new PortConnectionEventArgs(_id, outputPort.Id, true));
    }
    
    public void Disconnect()
    {
        if (_connectedPort != null)
        {
            var oldPortId = _connectedPort.Id;
            _connectedPort.DisconnectFrom(this);
            _connectedPort = null;
            _writer.Complete();
            
            ConnectionChanged?.Invoke(this, new PortConnectionEventArgs(_id, oldPortId, false));
        }
    }
    
    internal async Task ReceiveChunkAsync(Chunk chunk, CancellationToken cancellationToken = default)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(InputPort));
        
        await _writer.WriteAsync(chunk, cancellationToken);
    }
    
    internal void CompleteReceiving()
    {
        _writer.Complete();
    }
    
    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
            Disconnect();
            _writer.Complete();
        }
    }
    
    public override string ToString() => $"InputPort[{_id}, Schema: {_schema.ColumnCount} columns, Connected: {IsConnected}]";
}

/// <summary>
/// Default implementation of output port with fan-out support.
/// </summary>
public sealed class OutputPort : IOutputPort, IDisposable
{
    private readonly string _id;
    private readonly Schema _schema;
    private readonly ConcurrentDictionary<string, IInputPort> _connectedPorts;
    private volatile bool _disposed;
    
    public OutputPort(string id, Schema schema)
    {
        _id = id ?? throw new ArgumentNullException(nameof(id));
        _schema = schema ?? throw new ArgumentNullException(nameof(schema));
        _connectedPorts = new ConcurrentDictionary<string, IInputPort>();
    }
    
    public string Id => _id;
    public Schema Schema => _schema;
    public bool IsConnected => _connectedPorts.Count > 0;
    public IReadOnlyList<IInputPort> ConnectedPorts => _connectedPorts.Values.ToList();
    
    public event EventHandler<PortConnectionEventArgs>? ConnectionChanged;
    
    public async Task SendChunkAsync(Chunk chunk, CancellationToken cancellationToken = default)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(OutputPort));
        if (chunk == null)
            throw new ArgumentNullException(nameof(chunk));
        
        // Validate chunk schema matches port schema
        if (!chunk.Schema.IsCompatibleWith(_schema))
        {
            throw new ArgumentException($"Chunk schema ({chunk.Schema.Signature}) is not compatible with port schema ({_schema.Signature})");
        }
        
        if (_connectedPorts.IsEmpty)
        {
            // No connected ports - dispose chunk to return to pool
            chunk.Dispose();
            return;
        }
        
        // Fan-out to all connected ports
        var sendTasks = new List<Task>(_connectedPorts.Count);
        
        foreach (var inputPort in _connectedPorts.Values)
        {
            if (inputPort is InputPort concretePort)
            {
                sendTasks.Add(concretePort.ReceiveChunkAsync(chunk, cancellationToken));
            }
        }
        
        await Task.WhenAll(sendTasks);
    }
    
    public void ConnectTo(IInputPort inputPort)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(OutputPort));
        if (inputPort == null)
            throw new ArgumentNullException(nameof(inputPort));
        
        // Validate schema compatibility
        if (!_schema.IsCompatibleWith(inputPort.Schema))
        {
            throw new ArgumentException($"Output port schema ({_schema.Signature}) is not compatible with input port schema ({inputPort.Schema.Signature})");
        }
        
        if (_connectedPorts.TryAdd(inputPort.Id, inputPort))
        {
            ConnectionChanged?.Invoke(this, new PortConnectionEventArgs(_id, inputPort.Id, true));
        }
    }
    
    public void DisconnectFrom(IInputPort inputPort)
    {
        if (inputPort != null && _connectedPorts.TryRemove(inputPort.Id, out _))
        {
            ConnectionChanged?.Invoke(this, new PortConnectionEventArgs(_id, inputPort.Id, false));
        }
    }
    
    public async Task CompleteAsync()
    {
        if (_disposed)
            return;
        
        // Signal completion to all connected input ports
        foreach (var inputPort in _connectedPorts.Values)
        {
            if (inputPort is InputPort concretePort)
            {
                concretePort.CompleteReceiving();
            }
        }
        
        await Task.CompletedTask;
    }
    
    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
            
            // Disconnect from all ports
            foreach (var inputPort in _connectedPorts.Values.ToList())
            {
                DisconnectFrom(inputPort);
            }
            
            _connectedPorts.Clear();
        }
    }
    
    public override string ToString() => $"OutputPort[{_id}, Schema: {_schema.ColumnCount} columns, Connected: {_connectedPorts.Count} ports]";
}

/// <summary>
/// Event arguments for port connection state changes.
/// </summary>
public sealed class PortConnectionEventArgs : EventArgs
{
    public PortConnectionEventArgs(string sourcePortId, string targetPortId, bool isConnected)
    {
        SourcePortId = sourcePortId;
        TargetPortId = targetPortId;
        IsConnected = isConnected;
        Timestamp = DateTime.UtcNow;
    }
    
    public string SourcePortId { get; }
    public string TargetPortId { get; }
    public bool IsConnected { get; }
    public DateTime Timestamp { get; }
    
    public override string ToString() => 
        $"Port {(IsConnected ? "Connected" : "Disconnected")}: {SourcePortId} -> {TargetPortId} at {Timestamp:HH:mm:ss.fff}";
}
```

---

## 6. DAG Execution Implementation

### 6.1 Step Processor Interface

```csharp
/// <summary>
/// Interface for processing steps that transform data streams.
/// All plugins must implement this interface for step execution.
/// </summary>
public interface IStepProcessor : IDisposable
{
    /// <summary>
    /// Unique identifier for this step instance.
    /// </summary>
    string StepId { get; }
    
    /// <summary>
    /// Input ports for receiving data.
    /// </summary>
    IReadOnlyList<IInputPort> InputPorts { get; }
    
    /// <summary>
    /// Output ports for sending data.
    /// </summary>
    IReadOnlyList<IOutputPort> OutputPorts { get; }
    
    /// <summary>
    /// Initializes the step processor with configuration.
    /// </summary>
    Task InitializeAsync(IStepConfiguration configuration, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Executes the step processing logic.
    /// </summary>
    Task ExecuteAsync(ProcessingContext context, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Validates that the step can process with current port connections.
    /// </summary>
    StepValidationResult Validate();
}

/// <summary>
/// Processing context provided to step processors during execution.
/// </summary>
public sealed class ProcessingContext
{
    public ProcessingContext(string jobId, string correlationId, IMemoryManager memoryManager, ILogger logger)
    {
        JobId = jobId ?? throw new ArgumentNullException(nameof(jobId));
        CorrelationId = correlationId ?? throw new ArgumentNullException(nameof(correlationId));
        MemoryManager = memoryManager ?? throw new ArgumentNullException(nameof(memoryManager));
        Logger = logger ?? throw new ArgumentNullException(nameof(logger));
        Properties = new ConcurrentDictionary<string, object>();
    }
    
    /// <summary>
    /// Unique identifier for the current job execution.
    /// </summary>
    public string JobId { get; }
    
    /// <summary>
    /// Correlation identifier for tracking and debugging.
    /// </summary>
    public string CorrelationId { get; }
    
    /// <summary>
    /// Memory manager for efficient allocation and spillover.
    /// </summary>
    public IMemoryManager MemoryManager { get; }
    
    /// <summary>
    /// Logger for step execution events.
    /// </summary>
    public ILogger Logger { get; }
    
    /// <summary>
    /// Custom properties for step communication.
    /// </summary>
    public ConcurrentDictionary<string, object> Properties { get; }
    
    /// <summary>
    /// Cancellation token for the current execution.
    /// </summary>
    public CancellationToken CancellationToken { get; set; }
}

/// <summary>
/// Result of step validation.
/// </summary>
public sealed class StepValidationResult
{
    public static readonly StepValidationResult Success = new(true, Array.Empty<string>());
    
    public StepValidationResult(bool isValid, IEnumerable<string> errors)
    {
        IsValid = isValid;
        Errors = errors?.ToList() ?? new List<string>();
    }
    
    public bool IsValid { get; }
    public IReadOnlyList<string> Errors { get; }
    
    public static StepValidationResult Failure(params string[] errors) => new(false, errors);
    public static StepValidationResult Failure(IEnumerable<string> errors) => new(false, errors);
    
    public override string ToString() => IsValid ? "Valid" : $"Invalid: {string.Join(", ", Errors)}";
}
```

### 6.2 DAG Validator Implementation

```csharp
/// <summary>
/// Validates DAG structure, schema compatibility, and execution feasibility.
/// </summary>
public sealed class DagValidator
{
    private readonly ILogger<DagValidator> _logger;
    
    public DagValidator(ILogger<DagValidator> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }
    
    /// <summary>
    /// Validates complete DAG for execution readiness.
    /// </summary>
    public DagValidationResult ValidateJob(JobDefinition job)
    {
        if (job == null)
            throw new ArgumentNullException(nameof(job));
        
        var errors = new List<string>();
        var warnings = new List<string>();
        
        // Validate basic structure
        ValidateBasicStructure(job, errors);
        
        // Validate step definitions
        ValidateSteps(job, errors, warnings);
        
        // Validate connections and schema compatibility
        ValidateConnections(job, errors, warnings);
        
        // Validate for cycles
        ValidateCycles(job, errors);
        
        // Validate execution order
        ValidateExecutionOrder(job, errors, warnings);
        
        var isValid = errors.Count == 0;
        _logger.LogInformation("DAG validation completed. Valid: {IsValid}, Errors: {ErrorCount}, Warnings: {WarningCount}", 
            isValid, errors.Count, warnings.Count);
        
        return new DagValidationResult(isValid, errors, warnings);
    }
    
    private static void ValidateBasicStructure(JobDefinition job, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(job.Id))
            errors.Add("Job must have a valid ID");
        
        if (job.Steps == null || job.Steps.Count == 0)
            errors.Add("Job must have at least one step");
        
        if (job.Connections == null)
            errors.Add("Job must have connections definition");
    }
    
    private static void ValidateSteps(JobDefinition job, List<string> errors, List<string> warnings)
    {
        var stepIds = new HashSet<string>();
        
        foreach (var step in job.Steps)
        {
            if (string.IsNullOrWhiteSpace(step.Id))
            {
                errors.Add("All steps must have valid IDs");
                continue;
            }
            
            if (!stepIds.Add(step.Id))
            {
                errors.Add($"Duplicate step ID: {step.Id}");
            }
            
            if (string.IsNullOrWhiteSpace(step.Type))
            {
                errors.Add($"Step {step.Id} must have a valid type");
            }
            
            // Validate step-specific configuration
            ValidateStepConfiguration(step, errors, warnings);
        }
    }
    
    private static void ValidateStepConfiguration(StepDefinition step, List<string> errors, List<string> warnings)
    {
        // Plugin-specific validation would be implemented here
        // For now, basic configuration validation
        
        if (step.Configuration == null)
        {
            warnings.Add($"Step {step.Id} has no configuration");
            return;
        }
        
        // Validate common configuration patterns
        if (step.Configuration.ContainsKey("inputSchema"))
        {
            // Validate input schema format
            try
            {
                var schemaConfig = step.Configuration["inputSchema"];
                // Schema validation logic here
            }
            catch (Exception ex)
            {
                errors.Add($"Step {step.Id} has invalid input schema: {ex.Message}");
            }
        }
    }
    
    private static void ValidateConnections(JobDefinition job, List<string> errors, List<string> warnings)
    {
        var stepLookup = job.Steps.ToDictionary(s => s.Id);
        
        foreach (var connection in job.Connections)
        {
            // Validate source step exists
            if (!stepLookup.ContainsKey(connection.FromStepId))
            {
                errors.Add($"Connection references non-existent source step: {connection.FromStepId}");
                continue;
            }
            
            // Validate target step exists
            if (!stepLookup.ContainsKey(connection.ToStepId))
            {
                errors.Add($"Connection references non-existent target step: {connection.ToStepId}");
                continue;
            }
            
            // Schema compatibility validation would require step processor instantiation
            // This would be done during execution planning phase
            
            warnings.Add($"Schema compatibility for connection {connection.FromStepId} -> {connection.ToStepId} will be validated at runtime");
        }
    }
    
    private static void ValidateCycles(JobDefinition job, List<string> errors)
    {
        var graph = BuildDependencyGraph(job);
        var visited = new HashSet<string>();
        var recursionStack = new HashSet<string>();
        
        foreach (var stepId in graph.Keys)
        {
            if (HasCycle(graph, stepId, visited, recursionStack))
            {
                errors.Add($"Circular dependency detected involving step: {stepId}");
                return; // One cycle detection is sufficient
            }
        }
    }
    
    private static Dictionary<string, List<string>> BuildDependencyGraph(JobDefinition job)
    {
        var graph = new Dictionary<string, List<string>>();
        
        // Initialize all steps
        foreach (var step in job.Steps)
        {
            graph[step.Id] = new List<string>();
        }
        
        // Add dependencies (reverse of connections - dependencies point to prerequisites)
        foreach (var connection in job.Connections)
        {
            graph[connection.ToStepId].Add(connection.FromStepId);
        }
        
        return graph;
    }
    
    private static bool HasCycle(Dictionary<string, List<string>> graph, string node, 
        HashSet<string> visited, HashSet<string> recursionStack)
    {
        if (recursionStack.Contains(node))
            return true;
        
        if (visited.Contains(node))
            return false;
        
        visited.Add(node);
        recursionStack.Add(node);
        
        foreach (var dependency in graph[node])
        {
            if (HasCycle(graph, dependency, visited, recursionStack))
                return true;
        }
        
        recursionStack.Remove(node);
        return false;
    }
    
    private static void ValidateExecutionOrder(JobDefinition job, List<string> errors, List<string> warnings)
    {
        try
        {
            var order = TopologicalSort(job);
            
            if (order.Count != job.Steps.Count)
            {
                errors.Add("Unable to determine valid execution order for all steps");
                return;
            }
            
            // Check for potential parallelization opportunities
            var parallelGroups = 0;
            var currentLevel = new HashSet<string> { order[0] };
            
            for (int i = 1; i < order.Count; i++)
            {
                var step = order[i];
                var dependencies = GetStepDependencies(job, step);
                
                if (dependencies.All(d => !currentLevel.Contains(d)))
                {
                    currentLevel.Add(step);
                }
                else
                {
                    parallelGroups++;
                    currentLevel = new HashSet<string> { step };
                }
            }
            
            if (parallelGroups < job.Steps.Count / 2)
            {
                warnings.Add("Limited parallelization opportunities detected. Consider restructuring for better performance.");
            }
        }
        catch (Exception ex)
        {
            errors.Add($"Failed to validate execution order: {ex.Message}");
        }
    }
    
    private static List<string> TopologicalSort(JobDefinition job)
    {
        var graph = BuildDependencyGraph(job);
        var inDegree = new Dictionary<string, int>();
        var result = new List<string>();
        var queue = new Queue<string>();
        
        // Initialize in-degree count
        foreach (var stepId in graph.Keys)
        {
            inDegree[stepId] = 0;
        }
        
        foreach (var connections in graph.Values)
        {
            foreach (var dependency in connections)
            {
                inDegree[dependency]++;
            }
        }
        
        // Find all steps with no dependencies
        foreach (var kvp in inDegree)
        {
            if (kvp.Value == 0)
            {
                queue.Enqueue(kvp.Key);
            }
        }
        
        // Process steps in topological order
        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            result.Add(current);
            
            foreach (var dependent in GetStepDependents(job, current))
            {
                inDegree[dependent]--;
                if (inDegree[dependent] == 0)
                {
                    queue.Enqueue(dependent);
                }
            }
        }
        
        return result;
    }
    
    private static List<string> GetStepDependencies(JobDefinition job, string stepId)
    {
        return job.Connections
            .Where(c => c.ToStepId == stepId)
            .Select(c => c.FromStepId)
            .ToList();
    }
    
    private static List<string> GetStepDependents(JobDefinition job, string stepId)
    {
        return job.Connections
            .Where(c => c.FromStepId == stepId)
            .Select(c => c.ToStepId)
            .ToList();
    }
}

/// <summary>
/// Result of DAG validation.
/// </summary>
public sealed class DagValidationResult
{
    public DagValidationResult(bool isValid, IEnumerable<string> errors, IEnumerable<string> warnings)
    {
        IsValid = isValid;
        Errors = errors?.ToList() ?? new List<string>();
        Warnings = warnings?.ToList() ?? new List<string>();
    }
    
    public bool IsValid { get; }
    public IReadOnlyList<string> Errors { get; }
    public IReadOnlyList<string> Warnings { get; }
    
    public override string ToString()
    {
        if (IsValid)
            return $"Valid DAG ({Warnings.Count} warnings)";
        
        return $"Invalid DAG ({Errors.Count} errors, {Warnings.Count} warnings)";
    }
}
```

### 6.3 DAG Executor Implementation

```csharp
/// <summary>
/// Executes validated DAGs with parallelization and error handling.
/// </summary>
public sealed class DagExecutor : IDisposable
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IMemoryManager _memoryManager;
    private readonly ILogger<DagExecutor> _logger;
    private readonly SemaphoreSlim _executionSemaphore;
    private volatile bool _disposed;
    
    public DagExecutor(IServiceProvider serviceProvider, IMemoryManager memoryManager, ILogger<DagExecutor> logger)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _memoryManager = memoryManager ?? throw new ArgumentNullException(nameof(memoryManager));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _executionSemaphore = new SemaphoreSlim(Environment.ProcessorCount);
    }
    
    /// <summary>
    /// Executes a validated job definition.
    /// </summary>
    public async Task<JobExecutionResult> ExecuteAsync(JobDefinition job, CancellationToken cancellationToken = default)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(DagExecutor));
        if (job == null)
            throw new ArgumentNullException(nameof(job));
        
        var correlationId = Guid.NewGuid().ToString("N");
        var executionStart = DateTime.UtcNow;
        
        _logger.LogInformation("Starting job execution: {JobId} (Correlation: {CorrelationId})", job.Id, correlationId);
        
        try
        {
            // Create processing context
            var context = new ProcessingContext(job.Id, correlationId, _memoryManager, _logger)
            {
                CancellationToken = cancellationToken
            };
            
            // Build execution plan
            var executionPlan = await CreateExecutionPlanAsync(job, context, cancellationToken);
            
            // Execute plan
            await ExecutePlanAsync(executionPlan, context, cancellationToken);
            
            var duration = DateTime.UtcNow - executionStart;
            _logger.LogInformation("Job execution completed successfully: {JobId} in {Duration:mm\\:ss\\.fff}", job.Id, duration);
            
            return JobExecutionResult.Success(job.Id, correlationId, duration);
        }
        catch (OperationCanceledException)
        {
            var duration = DateTime.UtcNow - executionStart;
            _logger.LogWarning("Job execution cancelled: {JobId} after {Duration:mm\\:ss\\.fff}", job.Id, duration);
            return JobExecutionResult.Cancelled(job.Id, correlationId, duration);
        }
        catch (Exception ex)
        {
            var duration = DateTime.UtcNow - executionStart;
            _logger.LogError(ex, "Job execution failed: {JobId} after {Duration:mm\\:ss\\.fff}", job.Id, duration);
            return JobExecutionResult.Failed(job.Id, correlationId, duration, ex);
        }
    }
    
    private async Task<ExecutionPlan> CreateExecutionPlanAsync(JobDefinition job, ProcessingContext context, CancellationToken cancellationToken)
    {
        var stepProcessors = new Dictionary<string, IStepProcessor>();
        var connections = new List<(IOutputPort Output, IInputPort Input)>();
        
        // Create step processors
        foreach (var stepDef in job.Steps)
        {
            var processor = await CreateStepProcessorAsync(stepDef, context, cancellationToken);
            stepProcessors[stepDef.Id] = processor;
        }
        
        // Create connections
        foreach (var connectionDef in job.Connections)
        {
            var sourceProcessor = stepProcessors[connectionDef.FromStepId];
            var targetProcessor = stepProcessors[connectionDef.ToStepId];
            
            var outputPort = sourceProcessor.OutputPorts.FirstOrDefault(p => p.Id == connectionDef.FromPortId);
            var inputPort = targetProcessor.InputPorts.FirstOrDefault(p => p.Id == connectionDef.ToPortId);
            
            if (outputPort == null)
                throw new InvalidOperationException($"Output port {connectionDef.FromPortId} not found on step {connectionDef.FromStepId}");
            
            if (inputPort == null)
                throw new InvalidOperationException($"Input port {connectionDef.ToPortId} not found on step {connectionDef.ToStepId}");
            
            // Validate schema compatibility
            if (!outputPort.Schema.IsCompatibleWith(inputPort.Schema))
            {
                throw new InvalidOperationException($"Schema mismatch between {connectionDef.FromStepId}.{connectionDef.FromPortId} and {connectionDef.ToStepId}.{connectionDef.ToPortId}");
            }
            
            inputPort.ConnectFrom(outputPort);
            connections.Add((outputPort, inputPort));
        }
        
        // Determine execution order
        var executionOrder = DetermineExecutionOrder(job, stepProcessors);
        
        return new ExecutionPlan(stepProcessors, connections, executionOrder);
    }
    
    private async Task<IStepProcessor> CreateStepProcessorAsync(StepDefinition stepDef, ProcessingContext context, CancellationToken cancellationToken)
    {
        // This would use a plugin factory/registry in the full implementation
        // For now, assume we have a way to create processors by type
        var processorType = GetProcessorType(stepDef.Type);
        var processor = (IStepProcessor)ActivatorUtilities.CreateInstance(_serviceProvider, processorType);
        
        // Create step configuration
        var configuration = new StepConfiguration(stepDef.Configuration ?? new Dictionary<string, object>());
        
        await processor.InitializeAsync(configuration, cancellationToken);
        
        // Validate processor
        var validationResult = processor.Validate();
        if (!validationResult.IsValid)
        {
            throw new InvalidOperationException($"Step {stepDef.Id} validation failed: {string.Join(", ", validationResult.Errors)}");
        }
        
        return processor;
    }
    
    private static Type GetProcessorType(string stepType)
    {
        // This would be implemented by the plugin system
        // For now, return a placeholder
        return stepType switch
        {
            "DelimitedFileReader" => typeof(DelimitedFileReaderProcessor),
            "DelimitedFileWriter" => typeof(DelimitedFileWriterProcessor),
            "JavaScriptTransform" => typeof(JavaScriptTransformProcessor),
            _ => throw new NotSupportedException($"Step type {stepType} is not supported")
        };
    }
    
    private static List<List<string>> DetermineExecutionOrder(JobDefinition job, Dictionary<string, IStepProcessor> processors)
    {
        // Group steps by execution level (steps that can run in parallel)
        var levels = new List<List<string>>();
        var remaining = new HashSet<string>(processors.Keys);
        var completed = new HashSet<string>();
        
        while (remaining.Count > 0)
        {
            var currentLevel = new List<string>();
            
            foreach (var stepId in remaining.ToList())
            {
                var dependencies = job.Connections
                    .Where(c => c.ToStepId == stepId)
                    .Select(c => c.FromStepId)
                    .ToList();
                
                if (dependencies.All(d => completed.Contains(d)))
                {
                    currentLevel.Add(stepId);
                    remaining.Remove(stepId);
                }
            }
            
            if (currentLevel.Count == 0)
            {
                throw new InvalidOperationException("Unable to determine execution order - possible circular dependency");
            }
            
            levels.Add(currentLevel);
            completed.UnionWith(currentLevel);
        }
        
        return levels;
    }
    
    private async Task ExecutePlanAsync(ExecutionPlan plan, ProcessingContext context, CancellationToken cancellationToken)
    {
        foreach (var level in plan.ExecutionOrder)
        {
            // Execute all steps in this level in parallel
            var levelTasks = level.Select(async stepId =>
            {
                await _executionSemaphore.WaitAsync(cancellationToken);
                try
                {
                    var processor = plan.StepProcessors[stepId];
                    await processor.ExecuteAsync(context, cancellationToken);
                }
                finally
                {
                    _executionSemaphore.Release();
                }
            });
            
            await Task.WhenAll(levelTasks);
        }
        
        // Complete all output ports
        foreach (var processor in plan.StepProcessors.Values)
        {
            foreach (var outputPort in processor.OutputPorts)
            {
                await outputPort.CompleteAsync();
            }
        }
    }
    
    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
            _executionSemaphore.Dispose();
        }
    }
}

/// <summary>
/// Execution plan containing processors, connections, and execution order.
/// </summary>
public sealed class ExecutionPlan
{
    public ExecutionPlan(
        Dictionary<string, IStepProcessor> stepProcessors,
        List<(IOutputPort Output, IInputPort Input)> connections,
        List<List<string>> executionOrder)
    {
        StepProcessors = stepProcessors ?? throw new ArgumentNullException(nameof(stepProcessors));
        Connections = connections ?? throw new ArgumentNullException(nameof(connections));
        ExecutionOrder = executionOrder ?? throw new ArgumentNullException(nameof(executionOrder));
    }
    
    public Dictionary<string, IStepProcessor> StepProcessors { get; }
    public List<(IOutputPort Output, IInputPort Input)> Connections { get; }
    public List<List<string>> ExecutionOrder { get; }
    
    public override string ToString() => 
        $"ExecutionPlan[{StepProcessors.Count} steps, {Connections.Count} connections, {ExecutionOrder.Count} levels]";
}

/// <summary>
/// Result of job execution.
/// </summary>
public sealed class JobExecutionResult
{
    private JobExecutionResult(string jobId, string correlationId, TimeSpan duration, JobExecutionStatus status, Exception? exception = null)
    {
        JobId = jobId;
        CorrelationId = correlationId;
        Duration = duration;
        Status = status;
        Exception = exception;
        Timestamp = DateTime.UtcNow;
    }
    
    public string JobId { get; }
    public string CorrelationId { get; }
    public TimeSpan Duration { get; }
    public JobExecutionStatus Status { get; }
    public Exception? Exception { get; }
    public DateTime Timestamp { get; }
    
    public static JobExecutionResult Success(string jobId, string correlationId, TimeSpan duration) =>
        new(jobId, correlationId, duration, JobExecutionStatus.Succeeded);
    
    public static JobExecutionResult Failed(string jobId, string correlationId, TimeSpan duration, Exception exception) =>
        new(jobId, correlationId, duration, JobExecutionStatus.Failed, exception);
    
    public static JobExecutionResult Cancelled(string jobId, string correlationId, TimeSpan duration) =>
        new(jobId, correlationId, duration, JobExecutionStatus.Cancelled);
    
    public override string ToString() => $"{Status} - {JobId} ({Duration:mm\\:ss\\.fff})";
}

/// <summary>
/// Job execution status enumeration.
/// </summary>
public enum JobExecutionStatus
{
    Succeeded,
    Failed,
    Cancelled
}
```

---

## 7. Configuration Models

### 7.1 Job Definition Models

```csharp
/// <summary>
/// Complete job definition with steps and connections.
/// </summary>
public sealed class JobDefinition
{
    public JobDefinition(string id, string name, IEnumerable<StepDefinition> steps, IEnumerable<ConnectionDefinition> connections)
    {
        Id = id ?? throw new ArgumentNullException(nameof(id));
        Name = name ?? throw new ArgumentNullException(nameof(name));
        Steps = steps?.ToList() ?? throw new ArgumentNullException(nameof(steps));
        Connections = connections?.ToList() ?? throw new ArgumentNullException(nameof(connections));
        Metadata = new Dictionary<string, object>();
    }
    
    /// <summary>
    /// Unique identifier for this job.
    /// </summary>
    public string Id { get; }
    
    /// <summary>
    /// Human-readable name for this job.
    /// </summary>
    public string Name { get; }
    
    /// <summary>
    /// Processing steps in this job.
    /// </summary>
    public IReadOnlyList<StepDefinition> Steps { get; }
    
    /// <summary>
    /// Connections between steps.
    /// </summary>
    public IReadOnlyList<ConnectionDefinition> Connections { get; }
    
    /// <summary>
    /// Additional metadata for this job.
    /// </summary>
    public Dictionary<string, object> Metadata { get; }
    
    /// <summary>
    /// Gets step by ID.
    /// </summary>
    public StepDefinition? GetStep(string stepId) => Steps.FirstOrDefault(s => s.Id == stepId);
    
    /// <summary>
    /// Gets all connections for a specific step.
    /// </summary>
    public IEnumerable<ConnectionDefinition> GetConnectionsForStep(string stepId) =>
        Connections.Where(c => c.FromStepId == stepId || c.ToStepId == stepId);
    
    public override string ToString() => $"Job[{Id}: {Steps.Count} steps, {Connections.Count} connections]";
}

/// <summary>
/// Definition of a processing step within a job.
/// </summary>
public sealed class StepDefinition
{
    public StepDefinition(string id, string type, IReadOnlyDictionary<string, object>? configuration = null)
    {
        Id = id ?? throw new ArgumentNullException(nameof(id));
        Type = type ?? throw new ArgumentNullException(nameof(type));
        Configuration = configuration ?? new Dictionary<string, object>();
    }
    
    /// <summary>
    /// Unique identifier for this step within the job.
    /// </summary>
    public string Id { get; }
    
    /// <summary>
    /// Step processor type (maps to plugin implementation).
    /// </summary>
    public string Type { get; }
    
    /// <summary>
    /// Step-specific configuration.
    /// </summary>
    public IReadOnlyDictionary<string, object> Configuration { get; }
    
    /// <summary>
    /// Gets typed configuration value.
    /// </summary>
    public T? GetConfigValue<T>(string key, T? defaultValue = default)
    {
        if (Configuration.TryGetValue(key, out var value))
        {
            return value is T typedValue ? typedValue : defaultValue;
        }
        return defaultValue;
    }
    
    /// <summary>
    /// Gets configuration value as string.
    /// </summary>
    public string? GetConfigString(string key, string? defaultValue = null) =>
        GetConfigValue(key, defaultValue);
    
    /// <summary>
    /// Gets configuration value as integer.
    /// </summary>
    public int GetConfigInt(string key, int defaultValue = 0) =>
        GetConfigValue(key, defaultValue);
    
    /// <summary>
    /// Gets configuration value as boolean.
    /// </summary>
    public bool GetConfigBool(string key, bool defaultValue = false) =>
        GetConfigValue(key, defaultValue);
    
    public override string ToString() => $"Step[{Id} ({Type})]";
}

/// <summary>
/// Definition of a connection between two steps.
/// </summary>
public sealed class ConnectionDefinition
{
    public ConnectionDefinition(string fromStepId, string fromPortId, string toStepId, string toPortId)
    {
        FromStepId = fromStepId ?? throw new ArgumentNullException(nameof(fromStepId));
        FromPortId = fromPortId ?? throw new ArgumentNullException(nameof(fromPortId));
        ToStepId = toStepId ?? throw new ArgumentNullException(nameof(toStepId));
        ToPortId = toPortId ?? throw new ArgumentNullException(nameof(toPortId));
    }
    
    /// <summary>
    /// Source step identifier.
    /// </summary>
    public string FromStepId { get; }
    
    /// <summary>
    /// Source output port identifier.
    /// </summary>
    public string FromPortId { get; }
    
    /// <summary>
    /// Target step identifier.
    /// </summary>
    public string ToStepId { get; }
    
    /// <summary>
    /// Target input port identifier.
    /// </summary>
    public string ToPortId { get; }
    
    public override string ToString() => $"Connection[{FromStepId}.{FromPortId} -> {ToStepId}.{ToPortId}]";
}

/// <summary>
/// Configuration for step processors.
/// </summary>
public interface IStepConfiguration
{
    /// <summary>
    /// Gets configuration value by key.
    /// </summary>
    T? GetValue<T>(string key, T? defaultValue = default);
    
    /// <summary>
    /// Gets all configuration keys.
    /// </summary>
    IEnumerable<string> GetKeys();
    
    /// <summary>
    /// Checks if configuration contains a key.
    /// </summary>
    bool ContainsKey(string key);
    
    /// <summary>
    /// Gets configuration as dictionary.
    /// </summary>
    IReadOnlyDictionary<string, object> ToDictionary();
}

/// <summary>
/// Default implementation of step configuration.
/// </summary>
public sealed class StepConfiguration : IStepConfiguration
{
    private readonly IReadOnlyDictionary<string, object> _configuration;
    
    public StepConfiguration(IReadOnlyDictionary<string, object> configuration)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
    }
    
    public T? GetValue<T>(string key, T? defaultValue = default)
    {
        if (_configuration.TryGetValue(key, out var value))
        {
            if (value is T directValue)
                return directValue;
            
            // Attempt type conversion
            try
            {
                return (T?)Convert.ChangeType(value, typeof(T));
            }
            catch
            {
                return defaultValue;
            }
        }
        
        return defaultValue;
    }
    
    public IEnumerable<string> GetKeys() => _configuration.Keys;
    
    public bool ContainsKey(string key) => _configuration.ContainsKey(key);
    
    public IReadOnlyDictionary<string, object> ToDictionary() => _configuration;
    
    public override string ToString() => $"StepConfiguration[{_configuration.Count} keys]";
}
```

---

## 8. Error Handling Implementation

### 8.1 Exception Hierarchy

```csharp
/// <summary>
/// Base exception for all FlowEngine-specific errors.
/// </summary>
public abstract class FlowEngineException : Exception
{
    protected FlowEngineException(string message) : base(message) { }
    protected FlowEngineException(string message, Exception innerException) : base(message, innerException) { }
    
    /// <summary>
    /// Correlation ID for error tracking.
    /// </summary>
    public string? CorrelationId { get; set; }
    
    /// <summary>
    /// Step ID where the error occurred.
    /// </summary>
    public string? StepId { get; set; }
    
    /// <summary>
    /// Additional context information.
    /// </summary>
    public Dictionary<string, object> Context { get; } = new();
}

/// <summary>
/// Schema validation and compatibility errors.
/// </summary>
public sealed class SchemaException : FlowEngineException
{
    public SchemaException(string message) : base(message) { }
    public SchemaException(string message, Exception innerException) : base(message, innerException) { }
    
    /// <summary>
    /// Expected schema signature.
    /// </summary>
    public string? ExpectedSchema { get; set; }
    
    /// <summary>
    /// Actual schema signature.
    /// </summary>
    public string? ActualSchema { get; set; }
    
    /// <summary>
    /// Column name where validation failed.
    /// </summary>
    public string? ColumnName { get; set; }
    
    public override string ToString()
    {
        var details = new List<string>();
        if (!string.IsNullOrEmpty(ColumnName)) details.Add($"Column: {ColumnName}");
        if (!string.IsNullOrEmpty(ExpectedSchema)) details.Add($"Expected: {ExpectedSchema}");
        if (!string.IsNullOrEmpty(ActualSchema)) details.Add($"Actual: {ActualSchema}");
        
        var contextInfo = details.Count > 0 ? $" ({string.Join(", ", details)})" : "";
        return $"{GetType().Name}: {Message}{contextInfo}";
    }
}

/// <summary>
/// Memory management and resource errors.
/// </summary>
public sealed class MemoryException : FlowEngineException
{
    public MemoryException(string message) : base(message) { }
    public MemoryException(string message, Exception innerException) : base(message, innerException) { }
    
    /// <summary>
    /// Current memory usage when error occurred.
    /// </summary>
    public long CurrentMemoryUsage { get; set; }
    
    /// <summary>
    /// Memory threshold that was exceeded.
    /// </summary>
    public long MemoryThreshold { get; set; }
    
    /// <summary>
    /// Whether spillover was attempted.
    /// </summary>
    public bool SpilloverAttempted { get; set; }
}

/// <summary>
/// Step processing and execution errors.
/// </summary>
public sealed class ProcessingException : FlowEngineException
{
    public ProcessingException(string message) : base(message) { }
    public ProcessingException(string message, Exception innerException) : base(message, innerException) { }
    
    /// <summary>
    /// Row index where processing failed (if applicable).
    /// </summary>
    public long? RowIndex { get; set; }
    
    /// <summary>
    /// Chunk index where processing failed (if applicable).
    /// </summary>
    public int? ChunkIndex { get; set; }
    
    /// <summary>
    /// Processing stage where error occurred.
    /// </summary>
    public string? ProcessingStage { get; set; }
}

/// <summary>
/// DAG validation and execution errors.
/// </summary>
public sealed class DagException : FlowEngineException
{
    public DagException(string message) : base(message) { }
    public DagException(string message, Exception innerException) : base(message, innerException) { }
    
    /// <summary>
    /// Job ID where the error occurred.
    /// </summary>
    public string? JobId { get; set; }
    
    /// <summary>
    /// Validation errors that led to this exception.
    /// </summary>
    public IReadOnlyList<string> ValidationErrors { get; set; } = Array.Empty<string>();
}

/// <summary>
/// Plugin loading and execution errors.
/// </summary>
public sealed class PluginException : FlowEngineException
{
    public PluginException(string message) : base(message) { }
    public PluginException(string message, Exception innerException) : base(message, innerException) { }
    
    /// <summary>
    /// Plugin assembly name.
    /// </summary>
    public string? PluginAssembly { get; set; }
    
    /// <summary>
    /// Plugin type name.
    /// </summary>
    public string? PluginType { get; set; }
    
    /// <summary>
    /// Plugin version.
    /// </summary>
    public string? PluginVersion { get; set; }
}
```

### 8.2 Error Context and Propagation

```csharp
/// <summary>
/// Captures detailed error context for debugging and monitoring.
/// </summary>
public sealed class ErrorContext
{
    public ErrorContext(string correlationId, string? stepId = null)
    {
        CorrelationId = correlationId ?? throw new ArgumentNullException(nameof(correlationId));
        StepId = stepId;
        Timestamp = DateTime.UtcNow;
        Properties = new Dictionary<string, object>();
    }
    
    /// <summary>
    /// Correlation ID for tracking errors across steps.
    /// </summary>
    public string CorrelationId { get; }
    
    /// <summary>
    /// Step ID where error occurred.
    /// </summary>
    public string? StepId { get; }
    
    /// <summary>
    /// When the error occurred.
    /// </summary>
    public DateTime Timestamp { get; }
    
    /// <summary>
    /// Additional context properties.
    /// </summary>
    public Dictionary<string, object> Properties { get; }
    
    /// <summary>
    /// Schema context if available.
    /// </summary>
    public Schema? Schema { get; set; }
    
    /// <summary>
    /// Row context if available.
    /// </summary>
    public ArrayRow? Row { get; set; }
    
    /// <summary>
    /// Chunk context if available.
    /// </summary>
    public int? ChunkIndex { get; set; }
    
    /// <summary>
    /// Row index within chunk if available.
    /// </summary>
    public int? RowIndex { get; set; }
    
    /// <summary>
    /// Memory usage at time of error.
    /// </summary>
    public long? MemoryUsage { get; set; }
    
    /// <summary>
    /// Adds property to context.
    /// </summary>
    public ErrorContext WithProperty(string key, object value)
    {
        Properties[key] = value;
        return this;
    }
    
    /// <summary>
    /// Creates FlowEngine exception with this context.
    /// </summary>
    public T EnrichException<T>(T exception) where T : FlowEngineException
    {
        exception.CorrelationId = CorrelationId;
        exception.StepId = StepId;
        
        foreach (var kvp in Properties)
        {
            exception.Context[kvp.Key] = kvp.Value;
        }
        
        if (Schema != null) exception.Context["Schema"] = Schema.Signature;
        if (Row != null) exception.Context["RowData"] = Row.ToDictionary();
        if (ChunkIndex.HasValue) exception.Context["ChunkIndex"] = ChunkIndex.Value;
        if (RowIndex.HasValue) exception.Context["RowIndex"] = RowIndex.Value;
        if (MemoryUsage.HasValue) exception.Context["MemoryUsage"] = MemoryUsage.Value;
        
        return exception;
    }
    
    public override string ToString() =>
        $"ErrorContext[{CorrelationId}, Step: {StepId ?? "N/A"}, Time: {Timestamp:HH:mm:ss.fff}]";
}

/// <summary>
/// Handles error propagation and recovery strategies.
/// </summary>
public sealed class ErrorHandler
{
    private readonly ILogger<ErrorHandler> _logger;
    private readonly ErrorHandlingOptions _options;
    
    public ErrorHandler(ILogger<ErrorHandler> logger, ErrorHandlingOptions? options = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options = options ?? ErrorHandlingOptions.Default;
    }
    
    /// <summary>
    /// Handles error with appropriate strategy.
    /// </summary>
    public async Task<ErrorHandlingResult> HandleErrorAsync(
        Exception exception, 
        ErrorContext context, 
        CancellationToken cancellationToken = default)
    {
        // Log error with full context
        LogError(exception, context);
        
        // Apply error handling strategy
        var strategy = DetermineStrategy(exception, context);
        
        switch (strategy)
        {
            case ErrorHandlingStrategy.Continue:
                return ErrorHandlingResult.Continue();
            
            case ErrorHandlingStrategy.Retry:
                return await HandleRetryAsync(exception, context, cancellationToken);
            
            case ErrorHandlingStrategy.Skip:
                return ErrorHandlingResult.Skip();
            
            case ErrorHandlingStrategy.Fail:
            default:
                return ErrorHandlingResult.Fail(exception);
        }
    }
    
    private void LogError(Exception exception, ErrorContext context)
    {
        var logContext = new Dictionary<string, object>
        {
            ["CorrelationId"] = context.CorrelationId,
            ["StepId"] = context.StepId ?? "Unknown",
            ["ExceptionType"] = exception.GetType().Name,
            ["Timestamp"] = context.Timestamp
        };
        
        // Add context properties
        foreach (var kvp in context.Properties)
        {
            logContext[$"Context.{kvp.Key}"] = kvp.Value;
        }
        
        using var scope = _logger.BeginScope(logContext);
        
        if (exception is FlowEngineException flowException)
        {
            _logger.LogError(flowException, "FlowEngine error in step {StepId}: {Message}", 
                context.StepId ?? "Unknown", exception.Message);
        }
        else
        {
            _logger.LogError(exception, "Unexpected error in step {StepId}: {Message}", 
                context.StepId ?? "Unknown", exception.Message);
        }
    }
    
    private ErrorHandlingStrategy DetermineStrategy(Exception exception, ErrorContext context)
    {
        // Apply configured rules
        foreach (var rule in _options.Rules)
        {
            if (rule.Matches(exception, context))
            {
                return rule.Strategy;
            }
        }
        
        // Default strategy based on exception type
        return exception switch
        {
            OperationCanceledException => ErrorHandlingStrategy.Fail,
            OutOfMemoryException => ErrorHandlingStrategy.Fail,
            SchemaException => ErrorHandlingStrategy.Fail,
            DagException => ErrorHandlingStrategy.Fail,
            ProcessingException when _options.AllowProcessingRetries => ErrorHandlingStrategy.Retry,
            _ => _options.DefaultStrategy
        };
    }
    
    private async Task<ErrorHandlingResult> HandleRetryAsync(
        Exception exception, 
        ErrorContext context, 
        CancellationToken cancellationToken)
    {
        var retryCount = context.Properties.GetValueOrDefault("RetryCount", 0);
        
        if (retryCount is int count && count >= _options.MaxRetries)
        {
            _logger.LogWarning("Maximum retry count ({MaxRetries}) exceeded for step {StepId}", 
                _options.MaxRetries, context.StepId);
            return ErrorHandlingResult.Fail(exception);
        }
        
        // Increment retry count
        context.Properties["RetryCount"] = count + 1;
        
        // Wait before retry
        var delay = CalculateRetryDelay(count);
        await Task.Delay(delay, cancellationToken);
        
        _logger.LogInformation("Retrying operation (attempt {Attempt}/{MaxRetries}) for step {StepId}", 
            count + 1, _options.MaxRetries, context.StepId);
        
        return ErrorHandlingResult.Retry();
    }
    
    private TimeSpan CalculateRetryDelay(int retryCount)
    {
        // Exponential backoff with jitter
        var baseDelay = _options.BaseRetryDelay.TotalMilliseconds;
        var exponentialDelay = baseDelay * Math.Pow(2, retryCount);
        var jitter = Random.Shared.NextDouble() * 0.1; // 10% jitter
        var finalDelay = exponentialDelay * (1 + jitter);
        
        return TimeSpan.FromMilliseconds(Math.Min(finalDelay, _options.MaxRetryDelay.TotalMilliseconds));
    }
}

/// <summary>
/// Result of error handling operation.
/// </summary>
public sealed class ErrorHandlingResult
{
    private ErrorHandlingResult(ErrorHandlingAction action, Exception? exception = null)
    {
        Action = action;
        Exception = exception;
    }
    
    public ErrorHandlingAction Action { get; }
    public Exception? Exception { get; }
    
    public static ErrorHandlingResult Continue() => new(ErrorHandlingAction.Continue);
    public static ErrorHandlingResult Retry() => new(ErrorHandlingAction.Retry);
    public static ErrorHandlingResult Skip() => new(ErrorHandlingAction.Skip);
    public static ErrorHandlingResult Fail(Exception exception) => new(ErrorHandlingAction.Fail, exception);
    
    public override string ToString() => Exception == null ? Action.ToString() : $"{Action}: {Exception.Message}";
}

/// <summary>
/// Error handling action enumeration.
/// </summary>
public enum ErrorHandlingAction
{
    Continue,
    Retry,
    Skip,
    Fail
}

/// <summary>
/// Error handling strategy enumeration.
/// </summary>
public enum ErrorHandlingStrategy
{
    Continue,
    Retry,
    Skip,
    Fail
}

/// <summary>
/// Configuration for error handling behavior.
/// </summary>
public sealed class ErrorHandlingOptions
{
    public static readonly ErrorHandlingOptions Default = new();
    
    /// <summary>
    /// Default error handling strategy.
    /// </summary>
    public ErrorHandlingStrategy DefaultStrategy { get; init; } = ErrorHandlingStrategy.Fail;
    
    /// <summary>
    /// Maximum number of retries for transient errors.
    /// </summary>
    public int MaxRetries { get; init; } = 3;
    
    /// <summary>
    /// Base delay between retries.
    /// </summary>
    public TimeSpan BaseRetryDelay { get; init; } = TimeSpan.FromSeconds(1);
    
    /// <summary>
    /// Maximum delay between retries.
    /// </summary>
    public TimeSpan MaxRetryDelay { get; init; } = TimeSpan.FromMinutes(1);
    
    /// <summary>
    /// Whether to allow retries for processing exceptions.
    /// </summary>
    public bool AllowProcessingRetries { get; init; } = true;
    
    /// <summary>
    /// Custom error handling rules.
    /// </summary>
    public IReadOnlyList<ErrorHandlingRule> Rules { get; init; } = Array.Empty<ErrorHandlingRule>();
}

/// <summary>
/// Custom error handling rule.
/// </summary>
public sealed class ErrorHandlingRule
{
    public ErrorHandlingRule(Func<Exception, ErrorContext, bool> predicate, ErrorHandlingStrategy strategy, string? description = null)
    {
        Predicate = predicate ?? throw new ArgumentNullException(nameof(predicate));
        Strategy = strategy;
        Description = description;
    }
    
    public Func<Exception, ErrorContext, bool> Predicate { get; }
    public ErrorHandlingStrategy Strategy { get; }
    public string? Description { get; }
    
    public bool Matches(Exception exception, ErrorContext context) => Predicate(exception, context);
    
    public override string ToString() => Description ?? $"Rule -> {Strategy}";
}
```

---

## 9. Performance Optimization Patterns

### 9.1 Hot Path Optimizations

```csharp
/// <summary>
/// Performance-optimized field access patterns for ArrayRow.
/// </summary>
public static class ArrayRowOptimizations
{
    /// <summary>
    /// Batch field access for multiple rows with same schema.
    /// Eliminates repeated schema lookups.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void BatchFieldAccess<T>(ReadOnlySpan<ArrayRow> rows, string fieldName, Span<T> results) where T : class
    {
        if (rows.IsEmpty) return;
        
        // Get field index once for all rows
        var fieldIndex = rows[0].Schema.GetIndex(fieldName);
        if (fieldIndex < 0)
        {
            results.Clear();
            return;
        }
        
        // Fast indexed access for all rows
        for (int i = 0; i < rows.Length; i++)
        {
            results[i] = (T)rows[i][fieldIndex];
        }
    }
    
    /// <summary>
    /// High-performance row comparison using cached hash codes.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool FastEquals(ArrayRow left, ArrayRow right)
    {
        // Fast reference check
        if (ReferenceEquals(left, right)) return true;
        if (left is null || right is null) return false;
        
        // Fast hash comparison
        if (left.GetHashCode() != right.GetHashCode()) return false;
        
        // Schema comparison
        if (!ReferenceEquals(left.Schema, right.Schema)) return false;
        
        // Value comparison using spans
        return left.Values.SequenceEqual(right.Values);
    }
    
    /// <summary>
    /// Optimized row transformation using pre-allocated arrays.
    /// </summary>
    public static ArrayRow Transform(ArrayRow source, ReadOnlySpan<(int SourceIndex, int TargetIndex, Func<object?, object?> Transform)> mappings, Schema targetSchema)
    {
        using var allocation = MemoryPool<object?>.Shared.Rent(targetSchema.ColumnCount);
        var targetValues = allocation.Memory.Span;
        
        // Apply transformations
        foreach (var (sourceIndex, targetIndex, transform) in mappings)
        {
            var sourceValue = source[sourceIndex];
            targetValues[targetIndex] = transform(sourceValue);
        }
        
        return new ArrayRow(targetSchema, targetValues.ToArray());
    }
}

/// <summary>
/// Memory pool optimizations for high-throughput scenarios.
/// </summary>
public static class MemoryOptimizations
{
    private static readonly MemoryPool<object?> SharedPool = MemoryPool<object?>.Shared;
    
    /// <summary>
    /// Rents memory for ArrayRow creation with automatic disposal.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static IMemoryOwner<object?> RentForRow(int columnCount)
    {
        return SharedPool.Rent(columnCount);
    }
    
    /// <summary>
    /// High-performance chunk processing with minimal allocations.
    /// </summary>
    public static async Task ProcessChunkOptimized<T>(
        Chunk chunk, 
        Func<ArrayRow, T> processor, 
        ICollection<T> results,
        CancellationToken cancellationToken = default)
    {
        var rows = chunk.Rows;
        var processedResults = new T[rows.Length];
        
        // Process in parallel if chunk is large enough
        if (rows.Length > 100)
        {
            await Task.Run(() =>
            {
                Parallel.For(0, rows.Length, new ParallelOptions
                {
                    CancellationToken = cancellationToken,
                    MaxDegreeOfParallelism = Environment.ProcessorCount
                }, i =>
                {
                    processedResults[i] = processor(rows[i]);
                });
            }, cancellationToken);
        }
        else
        {
            // Sequential processing for small chunks
            for (int i = 0; i < rows.Length; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                processedResults[i] = processor(rows[i]);
            }
        }
        
        // Add results
        foreach (var result in processedResults)
        {
            results.Add(result);
        }
    }
}

/// <summary>
/// Schema caching optimizations for repeated operations.
/// </summary>
public sealed class SchemaCache
{
    private readonly ConcurrentDictionary<string, Schema> _cache = new();
    private readonly ConcurrentDictionary<string, int[]> _indexCache = new();
    
    /// <summary>
    /// Gets or creates schema with caching.
    /// </summary>
    public Schema GetOrCreate(string signature, Func<Schema> factory)
    {
        return _cache.GetOrAdd(signature, _ => factory());
    }
    
    /// <summary>
    /// Gets cached field indexes for schema.
    /// </summary>
    public int[] GetFieldIndexes(Schema schema, string[] fieldNames)
    {
        var cacheKey = $"{schema.Signature}:{string.Join(",", fieldNames)}";
        
        return _indexCache.GetOrAdd(cacheKey, _ =>
        {
            var indexes = new int[fieldNames.Length];
            for (int i = 0; i < fieldNames.Length; i++)
            {
                indexes[i] = schema.GetIndex(fieldNames[i]);
            }
            return indexes;
        });
    }
    
    /// <summary>
    /// Clears cache to free memory.
    /// </summary>
    public void Clear()
    {
        _cache.Clear();
        _indexCache.Clear();
    }
    
    /// <summary>
    /// Gets cache statistics.
    /// </summary>
    public (int SchemaCount, int IndexCacheCount) GetStatistics() => (_cache.Count, _indexCache.Count);
}
```

---

## 10. Conclusion

This implementation specification provides **complete technical details** for building the FlowEngine core engine with ArrayRow architecture. The specification delivers:

### Key Implementation Features:
- **ArrayRow Data Model**: High-performance, schema-aware data representation with O(1) field access
- **Schema Management**: Comprehensive type system with validation and caching
- **Memory Management**: Intelligent pooling, spillover, and resource governance
- **Port System**: Schema-validated inter-step communication with fan-out support
- **DAG Execution**: Parallel execution with comprehensive error handling
- **Performance Optimizations**: .NET 8 patterns and validated performance characteristics

### Production-Ready Characteristics:
- **Type Safety**: Comprehensive validation and compile-time guarantees
- **Error Handling**: Schema-aware error context with recovery strategies
- **Resource Management**: Memory-bounded operation with predictable characteristics
- **Monitoring Integration**: Built-in observability hooks and performance metrics
- **Extensibility**: Plugin integration points and future enhancement support

### Implementation Guidance:
All interfaces, classes, and patterns provide:
- Complete method implementations with performance optimizations
- Comprehensive error handling with meaningful error messages
- Resource cleanup with proper disposal patterns
- Thread-safe implementations where required
- XML documentation for all public APIs

### Next Steps:
This implementation specification enables:
1. **Core Development**: Direct implementation of all specified interfaces and classes
2. **Performance Validation**: Benchmark implementation against specified targets
3. **Plugin Development**: Extension through established integration points
4. **Testing Implementation**: Comprehensive validation using Core Testing v1.1 specification

---

**Document Version**: 1.1  
**Implementation Ready**: June 28, 2025  
**Performance Validated**: 3.25x improvement, 60-70% memory reduction  
**Next Review**: Upon production implementation completion</summary>
public sealed class JobDefin