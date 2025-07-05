using FlowEngine.Abstractions;
using FlowEngine.Abstractions.Data;

namespace FlowEngine.Core.Data;

/// <summary>
/// Memory-efficient chunk implementation for batch processing of ArrayRows.
/// Implements automatic disposal and memory management for high-throughput streaming.
/// </summary>
public sealed class Chunk : IChunk
{
    private readonly IArrayRow[] _rows;
    private readonly ISchema _schema;
    private readonly IReadOnlyDictionary<string, object>? _metadata;
    private bool _disposed;

    /// <summary>
    /// Initializes a new chunk with the specified schema and rows.
    /// </summary>
    /// <param name="schema">The schema for all rows in this chunk</param>
    /// <param name="rows">The rows in this chunk</param>
    /// <param name="metadata">Optional metadata associated with this chunk</param>
    /// <exception cref="ArgumentNullException">Thrown when schema or rows is null</exception>
    /// <exception cref="ArgumentException">Thrown when any row has an incompatible schema</exception>
    public Chunk(ISchema schema, IEnumerable<IArrayRow> rows, IReadOnlyDictionary<string, object>? metadata = null)
    {
        _schema = schema ?? throw new ArgumentNullException(nameof(schema));
        ArgumentNullException.ThrowIfNull(rows);

        _rows = rows.ToArray();
        _metadata = metadata;

        // Validate that all rows have compatible schemas
        for (int i = 0; i < _rows.Length; i++)
        {
            var row = _rows[i];
            if (row == null)
            {
                throw new ArgumentException($"Row at index {i} is null", nameof(rows));
            }

            if (!row.Schema.Equals(_schema))
            {
                throw new ArgumentException($"Row at index {i} has incompatible schema", nameof(rows));
            }
        }
    }

    /// <summary>
    /// Internal constructor for efficient chunk operations.
    /// </summary>
    private Chunk(ISchema schema, IArrayRow[] rows, IReadOnlyDictionary<string, object>? metadata, bool skipValidation)
    {
        _schema = schema;
        _rows = rows;
        _metadata = metadata;
    }

    /// <inheritdoc />
    public ISchema Schema
    {
        get
        {
            ThrowIfDisposed();
            return _schema;
        }
    }

    /// <inheritdoc />
    public int RowCount
    {
        get
        {
            ThrowIfDisposed();
            return _rows.Length;
        }
    }

    /// <inheritdoc />
    public ReadOnlySpan<IArrayRow> Rows
    {
        get
        {
            ThrowIfDisposed();
            return _rows.AsSpan();
        }
    }

    /// <inheritdoc />
    public IArrayRow this[int index]
    {
        get
        {
            ThrowIfDisposed();
            
            if ((uint)index >= (uint)_rows.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(index), index, "Index is out of range");
            }

            return _rows[index];
        }
    }

    /// <inheritdoc />
    public IReadOnlyDictionary<string, object>? Metadata
    {
        get
        {
            ThrowIfDisposed();
            return _metadata;
        }
    }

    /// <inheritdoc />
    public bool IsDisposed => _disposed;

    /// <inheritdoc />
    public long ApproximateMemorySize
    {
        get
        {
            ThrowIfDisposed();
            
            // Rough estimation: object overhead + array + row references + metadata
            const int objectOverhead = 24; // Approximate object overhead on 64-bit
            const int referenceSize = 8;   // 64-bit reference size
            
            long size = objectOverhead; // This object
            size += _rows.Length * referenceSize; // Array of row references
            
            // Add estimated row sizes (simplified - actual implementation might be more sophisticated)
            size += _rows.Length * (_schema.ColumnCount * referenceSize + objectOverhead);
            
            // Add metadata size if present
            if (_metadata != null)
            {
                size += _metadata.Count * (referenceSize * 2 + objectOverhead); // Key-value pairs
            }
            
            return size;
        }
    }

    /// <inheritdoc />
    public IChunk WithRows(IEnumerable<IArrayRow> rows)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(rows);

        return new Chunk(_schema, rows, _metadata);
    }

    /// <inheritdoc />
    public IChunk WithMetadata(IReadOnlyDictionary<string, object>? metadata)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(metadata);

        var combinedMetadata = _metadata == null 
            ? metadata 
            : new Dictionary<string, object>(_metadata).Concat(metadata).ToDictionary(kvp => kvp.Key, kvp => kvp.Value);

        return new Chunk(_schema, _rows, combinedMetadata, skipValidation: true);
    }

    /// <inheritdoc />
    public IEnumerable<IArrayRow> GetRows()
    {
        ThrowIfDisposed();
        
        // Return a copy to prevent modification during enumeration
        for (int i = 0; i < _rows.Length; i++)
        {
            yield return _rows[i];
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (!_disposed)
        {
            // Clear references to help GC
            Array.Clear(_rows, 0, _rows.Length);
            _disposed = true;
        }
    }

    /// <inheritdoc />
    public override string ToString()
    {
        if (_disposed)
        {
            return "Chunk(Disposed)";
        }

        return $"Chunk({RowCount} rows, Schema: {_schema.Signature})";
    }

    /// <summary>
    /// Creates an empty chunk with the specified schema.
    /// </summary>
    /// <param name="schema">The schema for the chunk</param>
    /// <param name="metadata">Optional metadata</param>
    /// <returns>An empty chunk</returns>
    public static Chunk Empty(ISchema schema, IReadOnlyDictionary<string, object>? metadata = null)
    {
        ArgumentNullException.ThrowIfNull(schema);
        return new Chunk(schema, Array.Empty<IArrayRow>(), metadata, skipValidation: true);
    }

    /// <summary>
    /// Creates a chunk from an array of rows (optimized for performance).
    /// </summary>
    /// <param name="schema">The schema for all rows</param>
    /// <param name="rows">The array of rows</param>
    /// <param name="metadata">Optional metadata</param>
    /// <returns>A new chunk</returns>
    public static Chunk FromArray(ISchema schema, IArrayRow[] rows, IReadOnlyDictionary<string, object>? metadata = null)
    {
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentNullException.ThrowIfNull(rows);

        // Clone the array to ensure immutability
        var clonedRows = new IArrayRow[rows.Length];
        Array.Copy(rows, clonedRows, rows.Length);

        return new Chunk(schema, clonedRows, metadata, skipValidation: false);
    }

    /// <summary>
    /// Splits this chunk into smaller chunks of the specified size.
    /// </summary>
    /// <param name="chunkSize">The maximum size of each resulting chunk</param>
    /// <returns>An enumerable of smaller chunks</returns>
    public IEnumerable<IChunk> Split(int chunkSize)
    {
        ThrowIfDisposed();
        
        if (chunkSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(chunkSize), "Chunk size must be positive");
        }

        if (_rows.Length <= chunkSize)
        {
            yield return this;
            yield break;
        }

        for (int i = 0; i < _rows.Length; i += chunkSize)
        {
            var length = Math.Min(chunkSize, _rows.Length - i);
            var chunkRows = new IArrayRow[length];
            Array.Copy(_rows, i, chunkRows, 0, length);

            yield return new Chunk(_schema, chunkRows, _metadata, skipValidation: true);
        }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(Chunk));
        }
    }
}
