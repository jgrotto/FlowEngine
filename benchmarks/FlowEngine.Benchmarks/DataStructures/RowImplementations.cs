using System.Collections.Immutable;
using Microsoft.Extensions.ObjectPool;

namespace FlowEngine.Benchmarks.DataStructures;

/// <summary>
/// Option 1: Current Design (Dictionary-based) - Baseline
/// </summary>
public sealed class DictionaryRow : IEquatable<DictionaryRow>
{
    private readonly Dictionary<string, object?> _data;
    private readonly int _hashCode;

    public DictionaryRow(Dictionary<string, object?> data)
    {
        _data = new Dictionary<string, object?>(data, StringComparer.OrdinalIgnoreCase);
        _hashCode = CalculateHashCode();
    }

    public DictionaryRow(IEnumerable<KeyValuePair<string, object?>> data)
    {
        _data = new Dictionary<string, object?>(data, StringComparer.OrdinalIgnoreCase);
        _hashCode = CalculateHashCode();
    }

    public object? this[string key] => _data.GetValueOrDefault(key);

    public DictionaryRow With(string key, object? value)
    {
        var newData = new Dictionary<string, object?>(_data, StringComparer.OrdinalIgnoreCase)
        {
            [key] = value
        };
        return new DictionaryRow(newData);
    }

    public DictionaryRow Without(string key)
    {
        var newData = new Dictionary<string, object?>(_data, StringComparer.OrdinalIgnoreCase);
        newData.Remove(key);
        return new DictionaryRow(newData);
    }

    public IReadOnlyCollection<string> ColumnNames => _data.Keys.ToList();
    public int ColumnCount => _data.Count;

    public bool Equals(DictionaryRow? other) => 
        other != null && _hashCode == other._hashCode && DataEquals(other);

    public override bool Equals(object? obj) => obj is DictionaryRow other && Equals(other);
    public override int GetHashCode() => _hashCode;

    private bool DataEquals(DictionaryRow other) => 
        _data.Count == other._data.Count && 
        _data.All(kvp => other._data.TryGetValue(kvp.Key, out var value) && Equals(kvp.Value, value));

    private int CalculateHashCode()
    {
        unchecked
        {
            int hash = 17;
            foreach (var kvp in _data.OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase))
            {
                hash = hash * 31 + kvp.Key.GetHashCode();
                hash = hash * 31 + (kvp.Value?.GetHashCode() ?? 0);
            }
            return hash;
        }
    }
}

/// <summary>
/// Option 2: Immutable Dictionary with Structural Sharing
/// </summary>
public sealed class ImmutableRow : IEquatable<ImmutableRow>
{
    private readonly ImmutableDictionary<string, object?> _data;
    private readonly int _hashCode;

    public ImmutableRow(ImmutableDictionary<string, object?> data)
    {
        _data = data ?? throw new ArgumentNullException(nameof(data));
        _hashCode = CalculateHashCode();
    }

    public ImmutableRow(IEnumerable<KeyValuePair<string, object?>> data)
    {
        _data = data.ToImmutableDictionary(StringComparer.OrdinalIgnoreCase);
        _hashCode = CalculateHashCode();
    }

    public object? this[string key] => _data.GetValueOrDefault(key);

    public ImmutableRow With(string key, object? value)
    {
        return new ImmutableRow(_data.SetItem(key, value));
    }

    public ImmutableRow Without(string key)
    {
        return new ImmutableRow(_data.Remove(key));
    }

    public IReadOnlyCollection<string> ColumnNames => _data.Keys.ToList();
    public int ColumnCount => _data.Count;

    public bool Equals(ImmutableRow? other) => 
        other != null && _hashCode == other._hashCode && ReferenceEquals(_data, other._data);

    public override bool Equals(object? obj) => obj is ImmutableRow other && Equals(other);
    public override int GetHashCode() => _hashCode;

    private int CalculateHashCode()
    {
        unchecked
        {
            int hash = 17;
            foreach (var kvp in _data.OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase))
            {
                hash = hash * 31 + kvp.Key.GetHashCode();
                hash = hash * 31 + (kvp.Value?.GetHashCode() ?? 0);
            }
            return hash;
        }
    }
}

/// <summary>
/// Option 3: Array-based with Schema (Fastest but requires fixed schema)
/// </summary>
public sealed class ArrayRow : IEquatable<ArrayRow>
{
    private readonly object?[] _values;
    private readonly Schema _schema;
    private readonly int _hashCode;

    public ArrayRow(Schema schema, object?[] values)
    {
        _schema = schema ?? throw new ArgumentNullException(nameof(schema));
        if (values.Length != schema.ColumnCount)
            throw new ArgumentException("Values array length must match schema column count");
        
        _values = (object?[])values.Clone();
        _hashCode = CalculateHashCode();
    }

    public object? this[string key]
    {
        get
        {
            var index = _schema.GetIndex(key);
            return index >= 0 ? _values[index] : null;
        }
    }

    public ArrayRow With(string key, object? value)
    {
        var newValues = (object?[])_values.Clone();
        var index = _schema.GetIndex(key);
        if (index >= 0) 
            newValues[index] = value;
        return new ArrayRow(_schema, newValues);
    }

    public ArrayRow Without(string key)
    {
        // For array-based rows, we can't really remove columns without changing schema
        // This would be handled by creating a new schema, but for benchmarking we'll
        // just set to null
        var newValues = (object?[])_values.Clone();
        var index = _schema.GetIndex(key);
        if (index >= 0)
            newValues[index] = null;
        return new ArrayRow(_schema, newValues);
    }

    public IReadOnlyCollection<string> ColumnNames => _schema.ColumnNames;
    public int ColumnCount => _schema.ColumnCount;
    public Schema Schema => _schema;

    public bool Equals(ArrayRow? other) => 
        other != null && 
        ReferenceEquals(_schema, other._schema) && 
        _values.SequenceEqual(other._values);

    public override bool Equals(object? obj) => obj is ArrayRow other && Equals(other);
    public override int GetHashCode() => _hashCode;

    private int CalculateHashCode()
    {
        unchecked
        {
            int hash = 17;
            foreach (var value in _values)
            {
                hash = hash * 31 + (value?.GetHashCode() ?? 0);
            }
            return hash;
        }
    }
}

/// <summary>
/// Option 4: Pooled Mutable Row with Immutable Interface
/// Note: This breaks true immutability for performance testing
/// </summary>
public sealed class PooledRow : IDisposable
{
    private static readonly ObjectPool<PooledRow> Pool = 
        new DefaultObjectPoolProvider().Create(new PooledRowPolicy());

    private Dictionary<string, object?> _data = new();
    private bool _disposed;

    public PooledRow() { } // Public constructor for pool

    public static PooledRow Create(IEnumerable<KeyValuePair<string, object?>> data)
    {
        var row = Pool.Get();
        row._data.Clear();
        row._disposed = false;
        foreach (var kvp in data)
            row._data[kvp.Key] = kvp.Value;
        return row;
    }

    public object? this[string key] => _disposed ? null : _data.GetValueOrDefault(key);

    public PooledRow Clone()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(PooledRow));
        return Create(_data);
    }

    public PooledRow With(string key, object? value)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(PooledRow));
        var clone = Clone();
        clone._data[key] = value;
        return clone;
    }

    public IReadOnlyCollection<string> ColumnNames => 
        _disposed ? Array.Empty<string>() : _data.Keys.ToList();
    
    public int ColumnCount => _disposed ? 0 : _data.Count;

    public void Dispose()
    {
        if (!_disposed)
        {
            _data.Clear();
            _disposed = true;
            Pool.Return(this);
        }
    }
}

/// <summary>
/// Policy for ObjectPool creation of PooledRow instances
/// </summary>
public class PooledRowPolicy : IPooledObjectPolicy<PooledRow>
{
    public PooledRow Create() => new PooledRow();

    public bool Return(PooledRow obj)
    {
        // Reset state when returning to pool
        return true;
    }
}