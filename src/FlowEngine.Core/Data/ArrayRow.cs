using System.Runtime.CompilerServices;
using FlowEngine.Abstractions;

namespace FlowEngine.Core.Data;

/// <summary>
/// High-performance immutable row implementation with O(1) field access.
/// Optimized for .NET 8 with aggressive inlining and memory-efficient operations.
/// Target performance: &lt;15ns field access, 200K+ rows/sec processing.
/// </summary>
public sealed class ArrayRow : IArrayRow
{
    private readonly object?[] _values;
    private readonly ISchema _schema;
    private readonly int _hashCode;

    /// <summary>
    /// Initializes a new ArrayRow with the specified schema and values.
    /// </summary>
    /// <param name="schema">The schema defining the structure of this row</param>
    /// <param name="values">The values for each column</param>
    /// <exception cref="ArgumentNullException">Thrown when schema or values is null</exception>
    /// <exception cref="ArgumentException">Thrown when values length doesn't match schema column count</exception>
    public ArrayRow(ISchema schema, object?[] values)
    {
        _schema = schema ?? throw new ArgumentNullException(nameof(schema));
        ArgumentNullException.ThrowIfNull(values);

        if (values.Length != schema.ColumnCount)
        {
            throw new ArgumentException(
                $"Values array length ({values.Length}) must match schema column count ({schema.ColumnCount})", 
                nameof(values));
        }

        // Clone the array to ensure immutability
        _values = new object?[values.Length];
        Array.Copy(values, _values, values.Length);

        _hashCode = CalculateHashCode();
    }

    /// <summary>
    /// Internal constructor for efficient copying during With operations.
    /// </summary>
    private ArrayRow(ISchema schema, object?[] values, bool skipValidation)
    {
        _schema = schema;
        _values = values;
        _hashCode = CalculateHashCode();
    }

    /// <inheritdoc />
    public ISchema Schema => _schema;

    /// <inheritdoc />
    public int ColumnCount => _values.Length;

    /// <inheritdoc />
    public object? this[string columnName]
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            var index = _schema.GetIndex(columnName);
            // Use bounds checking elimination optimization for .NET 8
            return (uint)index < (uint)_values.Length ? _values[index] : null;
        }
    }

    /// <inheritdoc />
    public object? this[int index]
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            if ((uint)index >= (uint)_values.Length)
                throw new ArgumentOutOfRangeException(nameof(index), index, "Index is out of range");
            
            return _values[index];
        }
    }

    /// <inheritdoc />
    public IArrayRow With(string columnName, object? value)
    {
        ArgumentNullException.ThrowIfNull(columnName);

        var index = _schema.GetIndex(columnName);
        if (index < 0)
            throw new ArgumentException($"Column '{columnName}' does not exist in the schema", nameof(columnName));

        return With(index, value);
    }

    /// <inheritdoc />
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public IArrayRow With(int index, object? value)
    {
        if ((uint)index >= (uint)_values.Length)
            throw new ArgumentOutOfRangeException(nameof(index), index, "Index is out of range");

        // If the value is the same, return this instance (optimization)
        if (ReferenceEquals(_values[index], value) || 
            (_values[index]?.Equals(value) == true))
        {
            return this;
        }

        // Create a new array with the updated value
        var newValues = new object?[_values.Length];
        Array.Copy(_values, newValues, _values.Length);
        newValues[index] = value;

        return new ArrayRow(_schema, newValues, skipValidation: true);
    }

    /// <inheritdoc />
    public IArrayRow WithMany(IReadOnlyDictionary<string, object?> updates)
    {
        ArgumentNullException.ThrowIfNull(updates);

        if (updates.Count == 0)
            return this;

        // Validate all column names first
        var indexUpdates = new Dictionary<int, object?>(updates.Count);
        foreach (var kvp in updates)
        {
            var index = _schema.GetIndex(kvp.Key);
            if (index < 0)
                throw new ArgumentException($"Column '{kvp.Key}' does not exist in the schema", nameof(updates));
            
            indexUpdates[index] = kvp.Value;
        }

        // Check if any values actually changed
        bool hasChanges = false;
        foreach (var kvp in indexUpdates)
        {
            if (!ReferenceEquals(_values[kvp.Key], kvp.Value) && 
                !(_values[kvp.Key]?.Equals(kvp.Value) == true))
            {
                hasChanges = true;
                break;
            }
        }

        if (!hasChanges)
            return this;

        // Create new array with all updates
        var newValues = new object?[_values.Length];
        Array.Copy(_values, newValues, _values.Length);
        
        foreach (var kvp in indexUpdates)
        {
            newValues[kvp.Key] = kvp.Value;
        }

        return new ArrayRow(_schema, newValues, skipValidation: true);
    }

    /// <inheritdoc />
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public T? GetValue<T>(string columnName)
    {
        var value = this[columnName];
        return value switch
        {
            null => default(T),
            T typed => typed,
            _ => (T)value
        };
    }

    /// <inheritdoc />
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public T? GetValue<T>(int index)
    {
        var value = this[index];
        return value switch
        {
            null => default(T),
            T typed => typed,
            _ => (T)value
        };
    }

    /// <inheritdoc />
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryGetValue<T>(string columnName, out T? value)
    {
        try
        {
            // First check if the column exists
            if (!_schema.HasColumn(columnName))
            {
                value = default(T);
                return false;
            }

            var obj = this[columnName];
            if (obj == null)
            {
                value = default(T);
                return true;
            }

            if (obj is T typed)
            {
                value = typed;
                return true;
            }

            value = (T)obj;
            return true;
        }
        catch
        {
            value = default(T);
            return false;
        }
    }

    /// <inheritdoc />
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ReadOnlySpan<object?> AsSpan()
    {
        return _values.AsSpan();
    }

    /// <inheritdoc />
    public IReadOnlyList<string> ColumnNames => _schema.Columns.Select(c => c.Name).ToArray();

    /// <inheritdoc />
    public bool Equals(IArrayRow? other)
    {
        if (other is not ArrayRow otherRow)
            return false;

        return ReferenceEquals(this, otherRow) ||
               (_hashCode == otherRow._hashCode &&
                ReferenceEquals(_schema, otherRow._schema) &&
                _values.AsSpan().SequenceEqual(otherRow._values.AsSpan()));
    }

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is IArrayRow row && Equals(row);

    /// <inheritdoc />
    public override int GetHashCode() => _hashCode;

    /// <inheritdoc />
    public override string ToString()
    {
        var columns = _schema.Columns;
        var pairs = new string[columns.Length];
        
        for (int i = 0; i < columns.Length; i++)
        {
            pairs[i] = $"{columns[i].Name}={_values[i]?.ToString() ?? "null"}";
        }

        return $"ArrayRow({string.Join(", ", pairs)})";
    }

    /// <summary>
    /// Creates a new ArrayRow from a dictionary of column names and values.
    /// </summary>
    /// <param name="schema">The schema for the row</param>
    /// <param name="data">The column data</param>
    /// <returns>A new ArrayRow instance</returns>
    public static ArrayRow FromDictionary(ISchema schema, IReadOnlyDictionary<string, object?> data)
    {
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentNullException.ThrowIfNull(data);

        var values = new object?[schema.ColumnCount];
        
        for (int i = 0; i < schema.Columns.Length; i++)
        {
            var column = schema.Columns[i];
            values[i] = data.TryGetValue(column.Name, out var value) ? value : null;
        }

        return new ArrayRow(schema, values);
    }

    /// <summary>
    /// Creates a new ArrayRow with all null values.
    /// </summary>
    /// <param name="schema">The schema for the row</param>
    /// <returns>A new ArrayRow instance with null values</returns>
    public static ArrayRow Empty(ISchema schema)
    {
        ArgumentNullException.ThrowIfNull(schema);
        
        var values = new object?[schema.ColumnCount];
        return new ArrayRow(schema, values);
    }

    private int CalculateHashCode()
    {
        unchecked
        {
            int hash = 17;
            hash = hash * 31 + _schema.GetHashCode();
            
            foreach (var value in _values)
            {
                hash = hash * 31 + (value?.GetHashCode() ?? 0);
            }
            
            return hash;
        }
    }
}