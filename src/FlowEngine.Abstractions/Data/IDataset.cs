using System.Runtime.CompilerServices;

namespace FlowEngine.Abstractions.Data;

/// <summary>
/// Represents a collection of data chunks with a common schema.
/// Provides streaming access to large datasets with memory-efficient processing.
/// </summary>
public interface IDataset : IAsyncDisposable, IDisposable
{
    /// <summary>
    /// Gets the schema that defines the structure of all chunks in this dataset.
    /// </summary>
    ISchema Schema { get; }

    /// <summary>
    /// Gets the total number of rows in this dataset, if known.
    /// May be null for streaming datasets where the total count is unknown.
    /// </summary>
    long? RowCount { get; }

    /// <summary>
    /// Gets the number of chunks in this dataset, if known.
    /// May be null for streaming datasets where the chunk count is unknown.
    /// </summary>
    int? ChunkCount { get; }

    /// <summary>
    /// Gets whether this dataset has been disposed.
    /// </summary>
    bool IsDisposed { get; }

    /// <summary>
    /// Gets metadata associated with this dataset.
    /// </summary>
    IReadOnlyDictionary<string, object> Metadata { get; }

    /// <summary>
    /// Gets all chunks in this dataset as an async enumerable for streaming processing.
    /// </summary>
    /// <param name="options">Optional chunking configuration</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Async enumerable of chunks</returns>
    IAsyncEnumerable<IChunk> GetChunksAsync(
        ChunkingOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default);

    /// <summary>
    /// Materializes the dataset into memory as a list of rows.
    /// Use with caution for large datasets as it may consume significant memory.
    /// </summary>
    /// <param name="maxRows">Maximum number of rows to materialize</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of materialized rows</returns>
    Task<IList<IArrayRow>> MaterializeAsync(long maxRows = long.MaxValue, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a new dataset with a different schema by applying a transform function to each row.
    /// </summary>
    /// <param name="newSchema">The schema for the transformed dataset</param>
    /// <param name="transform">Function to transform each row</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>New dataset with transformed data</returns>
    Task<IDataset> TransformSchemaAsync(ISchema newSchema, Func<IArrayRow, IArrayRow> transform, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a new dataset containing only rows that match the specified predicate.
    /// </summary>
    /// <param name="predicate">Function to test each row</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>New dataset with filtered data</returns>
    Task<IDataset> FilterAsync(Func<IArrayRow, bool> predicate, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a new dataset with updated metadata.
    /// </summary>
    /// <param name="metadata">New metadata dictionary</param>
    /// <returns>New dataset instance with updated metadata</returns>
    IDataset WithMetadata(IReadOnlyDictionary<string, object>? metadata);
}

/// <summary>
/// Configuration options for chunk processing.
/// </summary>
public sealed record ChunkingOptions
{
    /// <summary>
    /// Gets or sets the preferred size of each chunk in rows.
    /// </summary>
    public int PreferredChunkSize { get; init; } = 1000;

    /// <summary>
    /// Gets or sets the maximum size of each chunk in rows.
    /// </summary>
    public int MaxChunkSize { get; init; } = 10000;

    /// <summary>
    /// Gets or sets whether to optimize chunks for memory usage.
    /// </summary>
    public bool OptimizeForMemory { get; init; } = true;

    /// <summary>
    /// Gets or sets whether to maintain row order across chunks.
    /// </summary>
    public bool PreserveOrder { get; init; } = true;
}