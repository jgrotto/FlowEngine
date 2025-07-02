namespace FlowEngine.Abstractions.Data;

/// <summary>
/// Represents a dataset that provides streaming access to data chunks.
/// Supports memory-bounded processing regardless of dataset size.
/// </summary>
public interface IDataset : IAsyncDisposable, IDisposable
{
    /// <summary>
    /// Gets the schema that defines the structure of all data in this dataset.
    /// </summary>
    ISchema Schema { get; }

    /// <summary>
    /// Gets the total number of rows in the dataset, if known.
    /// Returns null if the row count cannot be determined without materializing the dataset.
    /// </summary>
    long? RowCount { get; }

    /// <summary>
    /// Gets the number of chunks in the dataset, if known.
    /// Returns null if the chunk count cannot be determined without processing.
    /// </summary>
    int? ChunkCount { get; }

    /// <summary>
    /// Gets whether this dataset has been disposed.
    /// </summary>
    bool IsDisposed { get; }

    /// <summary>
    /// Provides streaming access to data chunks for memory-efficient processing.
    /// </summary>
    /// <param name="options">Options for controlling chunk processing behavior</param>
    /// <param name="cancellationToken">Cancellation token for cooperative cancellation</param>
    /// <returns>Async enumerable of data chunks</returns>
    IAsyncEnumerable<IChunk> GetChunksAsync(ChunkingOptions? options = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a new dataset with a different schema, applying the transformation to all data.
    /// </summary>
    /// <param name="newSchema">The new schema to apply</param>
    /// <param name="transformer">Function to transform rows from old schema to new schema</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>New dataset with transformed schema and data</returns>
    Task<IDataset> TransformSchemaAsync(ISchema newSchema, Func<IArrayRow, IArrayRow> transformer, CancellationToken cancellationToken = default);

    /// <summary>
    /// Filters the dataset based on a predicate, returning a new dataset with matching rows.
    /// </summary>
    /// <param name="predicate">Function to test each row</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>New dataset containing only rows that match the predicate</returns>
    Task<IDataset> FilterAsync(Func<IArrayRow, bool> predicate, CancellationToken cancellationToken = default);

    /// <summary>
    /// Materializes a portion of the dataset into memory for operations that require random access.
    /// </summary>
    /// <param name="maxRows">Maximum number of rows to materialize</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Materialized subset of the dataset</returns>
    Task<IList<IArrayRow>> MaterializeAsync(long maxRows = 10000, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets metadata associated with this dataset.
    /// </summary>
    IReadOnlyDictionary<string, object> Metadata { get; }

    /// <summary>
    /// Creates a new dataset with additional or updated metadata.
    /// </summary>
    /// <param name="metadata">Metadata to add or update</param>
    /// <returns>New dataset with updated metadata</returns>
    IDataset WithMetadata(IReadOnlyDictionary<string, object> metadata);
}

/// <summary>
/// Extension methods for IDataset to provide common operations.
/// </summary>
public static class DatasetExtensions
{
    /// <summary>
    /// Counts the total number of rows in the dataset by enumerating all chunks.
    /// Use sparingly as this materializes the entire dataset.
    /// </summary>
    /// <param name="dataset">Dataset to count</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Total number of rows</returns>
    public static async Task<long> CountRowsAsync(this IDataset dataset, CancellationToken cancellationToken = default)
    {
        if (dataset.RowCount.HasValue)
        {
            return dataset.RowCount.Value;
        }

        var totalRows = 0L;
        await foreach (var chunk in dataset.GetChunksAsync(cancellationToken: cancellationToken))
        {
            using (chunk)
            {
                totalRows += chunk.RowCount;
            }
        }

        return totalRows;
    }

    /// <summary>
    /// Takes the first N rows from the dataset.
    /// </summary>
    /// <param name="dataset">Source dataset</param>
    /// <param name="count">Number of rows to take</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Dataset containing the first N rows</returns>
    public static async Task<IDataset> TakeAsync(this IDataset dataset, long count, CancellationToken cancellationToken = default)
    {
        var chunks = new List<IChunk>();
        var totalTaken = 0L;

        await foreach (var chunk in dataset.GetChunksAsync(cancellationToken: cancellationToken))
        {
            using (chunk)
            {
                if (totalTaken >= count)
                    break;

                var remainingToTake = count - totalTaken;
                if (chunk.RowCount <= remainingToTake)
                {
                    // Take the entire chunk
                    chunks.Add(chunk.WithRows(chunk.GetRows()));
                    totalTaken += chunk.RowCount;
                }
                else
                {
                    // Take partial chunk
                    var rowsToTake = chunk.GetRows().Take((int)remainingToTake);
                    chunks.Add(chunk.WithRows(rowsToTake));
                    totalTaken += remainingToTake;
                    break;
                }
            }
        }

        return new MemoryDataset(dataset.Schema, chunks);
    }

    /// <summary>
    /// Skips the first N rows from the dataset.
    /// </summary>
    /// <param name="dataset">Source dataset</param>
    /// <param name="count">Number of rows to skip</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Dataset with the first N rows skipped</returns>
    public static async Task<IDataset> SkipAsync(this IDataset dataset, long count, CancellationToken cancellationToken = default)
    {
        var chunks = new List<IChunk>();
        var totalSkipped = 0L;

        await foreach (var chunk in dataset.GetChunksAsync(cancellationToken: cancellationToken))
        {
            using (chunk)
            {
                if (totalSkipped < count)
                {
                    var remainingToSkip = count - totalSkipped;
                    if (chunk.RowCount <= remainingToSkip)
                    {
                        // Skip the entire chunk
                        totalSkipped += chunk.RowCount;
                        continue;
                    }
                    else
                    {
                        // Skip partial chunk
                        var rowsToKeep = chunk.GetRows().Skip((int)remainingToSkip);
                        chunks.Add(chunk.WithRows(rowsToKeep));
                        totalSkipped = count;
                    }
                }
                else
                {
                    // Keep the entire chunk
                    chunks.Add(chunk.WithRows(chunk.GetRows()));
                }
            }
        }

        return new MemoryDataset(dataset.Schema, chunks);
    }
}

/// <summary>
/// Simple in-memory implementation of IDataset for small datasets.
/// </summary>
internal sealed class MemoryDataset : IDataset
{
    private readonly List<IChunk> _chunks;
    private bool _disposed;

    public MemoryDataset(ISchema schema, IEnumerable<IChunk> chunks, IReadOnlyDictionary<string, object>? metadata = null)
    {
        Schema = schema ?? throw new ArgumentNullException(nameof(schema));
        _chunks = chunks?.ToList() ?? throw new ArgumentNullException(nameof(chunks));
        Metadata = metadata ?? new Dictionary<string, object>();
        RowCount = _chunks.Sum(c => c.RowCount);
        ChunkCount = _chunks.Count;
    }

    public ISchema Schema { get; }
    public long? RowCount { get; }
    public int? ChunkCount { get; }
    public bool IsDisposed => _disposed;
    public IReadOnlyDictionary<string, object> Metadata { get; }

    public async IAsyncEnumerable<IChunk> GetChunksAsync(ChunkingOptions? options = null, CancellationToken cancellationToken = default)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(MemoryDataset));

        foreach (var chunk in _chunks)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return chunk;
        }

        await Task.CompletedTask;
    }

    public Task<IDataset> TransformSchemaAsync(ISchema newSchema, Func<IArrayRow, IArrayRow> transformer, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException("Schema transformation not implemented in MemoryDataset");
    }

    public Task<IDataset> FilterAsync(Func<IArrayRow, bool> predicate, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException("Filtering not implemented in MemoryDataset");
    }

    public async Task<IList<IArrayRow>> MaterializeAsync(long maxRows = 10000, CancellationToken cancellationToken = default)
    {
        var rows = new List<IArrayRow>();
        var count = 0L;

        await foreach (var chunk in GetChunksAsync(cancellationToken: cancellationToken))
        {
            using (chunk)
            {
                foreach (var row in chunk.GetRows())
                {
                    if (count >= maxRows)
                        break;
                    
                    rows.Add(row);
                    count++;
                }
            }
        }

        return rows;
    }

    public IDataset WithMetadata(IReadOnlyDictionary<string, object> metadata)
    {
        return new MemoryDataset(Schema, _chunks, metadata);
    }

    public void Dispose()
    {
        if (_disposed) return;

        foreach (var chunk in _chunks)
        {
            chunk?.Dispose();
        }
        _chunks.Clear();
        _disposed = true;
    }

    public async ValueTask DisposeAsync()
    {
        Dispose();
        await Task.CompletedTask;
    }
}