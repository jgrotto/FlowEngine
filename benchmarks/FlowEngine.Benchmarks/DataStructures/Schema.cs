using System.Collections.Concurrent;

namespace FlowEngine.Benchmarks.DataStructures;

/// <summary>
/// Simple schema implementation for ArrayRow performance testing.
/// Maps column names to array indices for fast lookups.
/// </summary>
public sealed class Schema
{
    private readonly Dictionary<string, int> _columnIndices;
    private readonly string[] _columnNames;
    private static readonly ConcurrentDictionary<string, Schema> _cache = new();

    public Schema(string[] columnNames)
    {
        _columnNames = columnNames ?? throw new ArgumentNullException(nameof(columnNames));
        _columnIndices = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        
        for (int i = 0; i < columnNames.Length; i++)
        {
            _columnIndices[columnNames[i]] = i;
        }
    }

    public int GetIndex(string columnName)
    {
        return _columnIndices.TryGetValue(columnName, out var index) ? index : -1;
    }

    public string[] ColumnNames => _columnNames;
    public int ColumnCount => _columnNames.Length;

    /// <summary>
    /// Cache schemas to avoid recreation in benchmarks.
    /// </summary>
    public static Schema GetOrCreate(string[] columnNames)
    {
        var key = string.Join("|", columnNames);
        return _cache.GetOrAdd(key, _ => new Schema(columnNames));
    }
}