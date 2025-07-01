using System.Collections.Concurrent;
using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using System.Text;
using FlowEngine.Abstractions;
using FlowEngine.Abstractions.Data;

namespace FlowEngine.Core.Data;

/// <summary>
/// High-performance schema implementation with O(1) field lookups and intelligent caching.
/// Optimized for .NET 8 with FrozenDictionary for maximum lookup performance.
/// </summary>
public sealed class Schema : ISchema
{
    private readonly ImmutableArray<ColumnDefinition> _columns;
    private readonly FrozenDictionary<string, int> _columnIndexes;
    private readonly FrozenDictionary<string, ColumnDefinition> _columnsByName;
    private readonly string _signature;
    private readonly int _hashCode;
    
    // Global schema cache to avoid recreation of identical schemas
    private static readonly ConcurrentDictionary<string, Schema> _schemaCache = new();

    /// <summary>
    /// Initializes a new schema with the specified column definitions.
    /// </summary>
    /// <param name="columns">The column definitions for this schema</param>
    /// <exception cref="ArgumentNullException">Thrown when columns is null</exception>
    /// <exception cref="ArgumentException">Thrown when columns is empty or contains duplicates</exception>
    public Schema(IEnumerable<ColumnDefinition> columns)
    {
        ArgumentNullException.ThrowIfNull(columns);

        var columnArray = columns.ToArray();
        if (columnArray.Length == 0)
            throw new ArgumentException("Schema must contain at least one column", nameof(columns));

        // Check for duplicate column names (case-insensitive)
        var duplicates = columnArray
            .GroupBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToArray();

        if (duplicates.Length > 0)
            throw new ArgumentException($"Duplicate column names found: {string.Join(", ", duplicates)}", nameof(columns));

        _columns = columnArray.ToImmutableArray();

        // Build column indexes for O(1) lookup performance using FrozenDictionary
        var indexDict = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var nameDict = new Dictionary<string, ColumnDefinition>(StringComparer.OrdinalIgnoreCase);

        for (int i = 0; i < columnArray.Length; i++)
        {
            var column = columnArray[i];
            indexDict.Add(column.Name, i);
            nameDict.Add(column.Name, column);
        }

        _columnIndexes = indexDict.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
        _columnsByName = nameDict.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

        // Generate signature for caching and equality comparisons
        _signature = GenerateSignature(columnArray);
        _hashCode = _signature.GetHashCode(StringComparison.OrdinalIgnoreCase);
    }

    /// <inheritdoc />
    public ImmutableArray<ColumnDefinition> Columns => _columns;

    /// <inheritdoc />
    public int ColumnCount => _columns.Length;

    /// <inheritdoc />
    public string Signature => _signature;

    /// <inheritdoc />
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int GetIndex(string columnName)
    {
        return _columnIndexes.TryGetValue(columnName, out var index) ? index : -1;
    }

    /// <inheritdoc />
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryGetIndex(string columnName, out int index)
    {
        if (_columnIndexes.TryGetValue(columnName, out index))
        {
            return true;
        }
        
        index = -1;
        return false;
    }

    /// <inheritdoc />
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ColumnDefinition? GetColumn(string columnName)
    {
        return _columnsByName.TryGetValue(columnName, out var column) ? column : null;
    }

    /// <inheritdoc />
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool HasColumn(string columnName)
    {
        return _columnIndexes.ContainsKey(columnName);
    }

    /// <inheritdoc />
    public bool IsCompatibleWith(ISchema other)
    {
        ArgumentNullException.ThrowIfNull(other);

        // Fast path: identical schemas are always compatible
        if (ReferenceEquals(this, other) || Equals(other))
            return true;

        // Check if all columns in the target schema exist in this schema with compatible types
        foreach (var targetColumn in other.Columns)
        {
            var sourceColumn = GetColumn(targetColumn.Name);
            if (sourceColumn == null)
                return false;

            // Check type compatibility
            if (!IsTypeCompatible(sourceColumn.DataType, targetColumn.DataType))
                return false;

            // Check nullability compatibility (source nullable -> target non-nullable is invalid)
            if (sourceColumn.IsNullable && !targetColumn.IsNullable)
                return false;
        }

        return true;
    }

    /// <inheritdoc />
    public ISchema AddColumns(params ColumnDefinition[] columns)
    {
        ArgumentNullException.ThrowIfNull(columns);

        if (columns.Length == 0)
            return this;

        var allColumns = _columns.Concat(columns);
        return GetOrCreate(allColumns);
    }

    /// <inheritdoc />
    public ISchema RemoveColumns(params string[] columnNames)
    {
        ArgumentNullException.ThrowIfNull(columnNames);

        if (columnNames.Length == 0)
            return this;

        var namesToRemove = new HashSet<string>(columnNames, StringComparer.OrdinalIgnoreCase);
        var remainingColumns = _columns.Where(c => !namesToRemove.Contains(c.Name));
        
        return GetOrCreate(remainingColumns);
    }

    /// <inheritdoc />
    public bool Equals(ISchema? other)
    {
        if (other is not Schema otherSchema)
            return false;

        return ReferenceEquals(this, otherSchema) || 
               (_hashCode == otherSchema._hashCode && string.Equals(_signature, otherSchema._signature, StringComparison.OrdinalIgnoreCase));
    }

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is ISchema schema && Equals(schema);

    /// <inheritdoc />
    public override int GetHashCode() => _hashCode;

    /// <inheritdoc />
    public override string ToString() => $"Schema({ColumnCount} columns): {_signature}";

    /// <summary>
    /// Gets or creates a cached schema instance with the specified columns.
    /// This method ensures that identical schemas share the same instance for memory efficiency.
    /// </summary>
    /// <param name="columns">The column definitions for the schema</param>
    /// <returns>A cached schema instance</returns>
    public static Schema GetOrCreate(IEnumerable<ColumnDefinition> columns)
    {
        ArgumentNullException.ThrowIfNull(columns);

        var columnArray = columns.ToArray();
        var signature = GenerateSignature(columnArray);

        return _schemaCache.GetOrAdd(signature, _ => new Schema(columnArray));
    }

    /// <summary>
    /// Clears the schema cache. This method should only be used in testing scenarios.
    /// </summary>
    internal static void ClearCache()
    {
        _schemaCache.Clear();
    }

    private static string GenerateSignature(ColumnDefinition[] columns)
    {
        var sb = new StringBuilder();
        
        foreach (var column in columns.OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase))
        {
            if (sb.Length > 0)
                sb.Append('|');
            
            sb.Append(column.Name);
            sb.Append(':');
            sb.Append(column.DataType.Name);
            
            if (column.IsNullable)
                sb.Append('?');
        }

        return sb.ToString();
    }

    private static bool IsTypeCompatible(Type sourceType, Type targetType)
    {
        // Exact match is always compatible
        if (sourceType == targetType)
            return true;

        // Check if target type is assignable from source type
        if (targetType.IsAssignableFrom(sourceType))
            return true;

        // Handle common numeric conversions
        if (IsNumericType(sourceType) && IsNumericType(targetType))
        {
            return IsNumericConversionSafe(sourceType, targetType);
        }

        // Handle nullable types
        var sourceNullable = Nullable.GetUnderlyingType(sourceType);
        var targetNullable = Nullable.GetUnderlyingType(targetType);

        if (sourceNullable != null && targetNullable != null)
            return IsTypeCompatible(sourceNullable, targetNullable);

        if (sourceNullable != null)
            return IsTypeCompatible(sourceNullable, targetType);

        if (targetNullable != null)
            return IsTypeCompatible(sourceType, targetNullable);

        return false;
    }

    private static bool IsNumericType(Type type)
    {
        return type == typeof(byte) || type == typeof(sbyte) ||
               type == typeof(short) || type == typeof(ushort) ||
               type == typeof(int) || type == typeof(uint) ||
               type == typeof(long) || type == typeof(ulong) ||
               type == typeof(float) || type == typeof(double) ||
               type == typeof(decimal);
    }

    private static bool IsNumericConversionSafe(Type sourceType, Type targetType)
    {
        // This is a simplified implementation - in production, you might want more sophisticated logic
        var sourceSize = GetNumericSize(sourceType);
        var targetSize = GetNumericSize(targetType);
        
        // Generally allow conversion to larger types
        return targetSize >= sourceSize;
    }

    private static int GetNumericSize(Type type)
    {
        return type.Name switch
        {
            nameof(Byte) => 1,
            nameof(SByte) => 1,
            nameof(Int16) => 2,
            nameof(UInt16) => 2,
            nameof(Int32) => 4,
            nameof(UInt32) => 4,
            nameof(Single) => 4,
            nameof(Int64) => 8,
            nameof(UInt64) => 8,
            nameof(Double) => 8,
            nameof(Decimal) => 16,
            _ => 0
        };
    }
}