namespace FlowEngine.Abstractions.Data;

/// <summary>
/// Represents a memory-efficient batch of rows for high-throughput streaming processing.
/// Implements memory-bounded processing with automatic resource management and disposal.
/// </summary>
public interface IChunk : IDisposable
{
    /// <summary>
    /// Gets the schema that defines the structure of all rows in this chunk.
    /// </summary>
    ISchema Schema { get; }

    /// <summary>
    /// Gets the number of rows in this chunk.
    /// </summary>
    int RowCount { get; }

    /// <summary>
    /// Gets all rows in this chunk as a read-only span for high-performance iteration.
    /// </summary>
    ReadOnlySpan<IArrayRow> Rows { get; }

    /// <summary>
    /// Gets the row at the specified index.
    /// </summary>
    /// <param name="index">The zero-based index of the row</param>
    /// <returns>The row at the specified index</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when index is out of range</exception>
    IArrayRow this[int index] { get; }

    /// <summary>
    /// Gets optional metadata associated with this chunk (e.g., source information, processing hints).
    /// </summary>
    IReadOnlyDictionary<string, object>? Metadata { get; }

    /// <summary>
    /// Creates a new chunk with the specified rows, maintaining the same schema and metadata.
    /// </summary>
    /// <param name="rows">The rows for the new chunk</param>
    /// <returns>A new chunk with the specified rows</returns>
    IChunk WithRows(IEnumerable<IArrayRow> rows);

    /// <summary>
    /// Creates a new chunk with additional metadata.
    /// </summary>
    /// <param name="metadata">The metadata to add or update</param>
    /// <returns>A new chunk with the updated metadata</returns>
    IChunk WithMetadata(IReadOnlyDictionary<string, object>? metadata);

    /// <summary>
    /// Enumerates all rows in this chunk.
    /// </summary>
    /// <returns>An enumerable of all rows in this chunk</returns>
    IEnumerable<IArrayRow> GetRows();

    /// <summary>
    /// Determines if this chunk has been disposed.
    /// </summary>
    bool IsDisposed { get; }

    /// <summary>
    /// Gets the approximate memory size of this chunk in bytes.
    /// Used for memory pressure monitoring and spillover decisions.
    /// </summary>
    long ApproximateMemorySize { get; }
}


/// <summary>
/// Defines strategies for handling memory pressure during chunk processing.
/// </summary>
public enum MemoryPressureStrategy
{
    /// <summary>
    /// Reduce chunk sizes to use less memory.
    /// </summary>
    ReduceChunkSize,

    /// <summary>
    /// Spill chunks to disk when memory pressure is high.
    /// </summary>
    SpillToDisk,

    /// <summary>
    /// Force garbage collection to free up memory.
    /// </summary>
    ForceGarbageCollection,

    /// <summary>
    /// Throw an exception when memory pressure is too high.
    /// </summary>
    ThrowException
}