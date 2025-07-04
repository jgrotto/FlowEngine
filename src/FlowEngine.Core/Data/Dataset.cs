using FlowEngine.Abstractions.Data;
using System.Collections.Immutable;
using System.Runtime.CompilerServices;

namespace FlowEngine.Core.Data;

/// <summary>
/// Core implementation of IDataset that provides a collection of chunks with a common schema.
/// Optimized for memory-efficient streaming and high-performance data processing.
/// </summary>
public sealed class Dataset : IDataset
{
    private readonly ISchema _schema;
    private readonly IList<IChunk> _chunks;
    private readonly IReadOnlyDictionary<string, object> _metadata;
    private bool _disposed;

    /// <summary>
    /// Initializes a new Dataset with the specified schema and chunks.
    /// </summary>
    /// <param name="schema">The schema defining the structure of all chunks</param>
    /// <param name="chunks">The collection of chunks that make up this dataset</param>
    /// <param name="metadata">Optional metadata associated with this dataset</param>
    public Dataset(ISchema schema, IChunk[] chunks, IReadOnlyDictionary<string, object>? metadata = null)
    {
        _schema = schema ?? throw new ArgumentNullException(nameof(schema));
        _chunks = chunks ?? throw new ArgumentNullException(nameof(chunks));
        _metadata = metadata ?? ImmutableDictionary<string, object>.Empty;

        // Validate that all chunks have compatible schemas
        foreach (var chunk in _chunks)
        {
            if (!chunk.Schema.IsCompatibleWith(_schema))
            {
                throw new ArgumentException($"Chunk schema is not compatible with dataset schema", nameof(chunks));
            }
        }
    }

    /// <summary>
    /// Initializes a new Dataset with the specified schema and chunks.
    /// </summary>
    /// <param name="schema">The schema defining the structure of all chunks</param>
    /// <param name="chunks">The collection of chunks that make up this dataset</param>
    /// <param name="metadata">Optional metadata associated with this dataset</param>
    public Dataset(ISchema schema, IList<IChunk> chunks, IReadOnlyDictionary<string, object>? metadata = null)
    {
        _schema = schema ?? throw new ArgumentNullException(nameof(schema));
        _chunks = chunks ?? throw new ArgumentNullException(nameof(chunks));
        _metadata = metadata ?? ImmutableDictionary<string, object>.Empty;

        // Validate that all chunks have compatible schemas
        foreach (var chunk in _chunks)
        {
            if (!chunk.Schema.IsCompatibleWith(_schema))
            {
                throw new ArgumentException($"Chunk schema is not compatible with dataset schema", nameof(chunks));
            }
        }
    }

    /// <inheritdoc />
    public ISchema Schema => _schema;

    /// <inheritdoc />
    public long? RowCount
    {
        get
        {
            ThrowIfDisposed();
            return _chunks.Sum(c => c.RowCount);
        }
    }

    /// <inheritdoc />
    public int? ChunkCount
    {
        get
        {
            ThrowIfDisposed();
            return _chunks.Count;
        }
    }

    /// <inheritdoc />
    public bool IsDisposed => _disposed;

    /// <inheritdoc />
    public IReadOnlyDictionary<string, object> Metadata => _metadata;

    /// <inheritdoc />
    public async IAsyncEnumerable<IChunk> GetChunksAsync(
        ChunkingOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        foreach (var chunk in _chunks)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return chunk;
            
            // Yield control to allow other async operations to continue
            await Task.Yield();
        }
    }

    /// <inheritdoc />
    public async Task<IList<IArrayRow>> MaterializeAsync(long maxRows = long.MaxValue, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        var result = new List<IArrayRow>();
        long totalRows = 0;

        await foreach (var chunk in GetChunksAsync(cancellationToken: cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            
            foreach (var row in chunk.GetRows())
            {
                if (totalRows >= maxRows) break;
                result.Add(row);
                totalRows++;
            }
            
            if (totalRows >= maxRows) break;
        }

        return result;
    }

    /// <inheritdoc />
    public async Task<IDataset> TransformSchemaAsync(ISchema newSchema, Func<IArrayRow, IArrayRow> transform, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        
        ArgumentNullException.ThrowIfNull(newSchema);
        ArgumentNullException.ThrowIfNull(transform);

        var transformedChunks = new List<IChunk>();

        await foreach (var chunk in GetChunksAsync(cancellationToken: cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            
            var transformedRows = chunk.GetRows().Select(transform).ToList();
            
            // Create new chunk with transformed data
            // Note: This assumes we have a Chunk implementation available
            var transformedChunk = new Chunk(newSchema, transformedRows.ToArray(), chunk.Metadata);
            transformedChunks.Add(transformedChunk);
        }

        return new Dataset(newSchema, transformedChunks.ToArray(), _metadata);
    }

    /// <inheritdoc />
    public async Task<IDataset> FilterAsync(Func<IArrayRow, bool> predicate, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        
        ArgumentNullException.ThrowIfNull(predicate);

        var filteredChunks = new List<IChunk>();

        await foreach (var chunk in GetChunksAsync(cancellationToken: cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            
            var filteredRows = chunk.GetRows().Where(predicate).ToList();
            
            if (filteredRows.Count > 0)
            {
                // Create new chunk with filtered data
                var filteredChunk = new Chunk(_schema, filteredRows.ToArray(), chunk.Metadata);
                filteredChunks.Add(filteredChunk);
            }
        }

        return new Dataset(_schema, filteredChunks.ToArray(), _metadata);
    }

    /// <inheritdoc />
    public IDataset WithMetadata(IReadOnlyDictionary<string, object>? metadata)
    {
        ThrowIfDisposed();
        
        return new Dataset(_schema, _chunks.ToArray(), metadata ?? ImmutableDictionary<string, object>.Empty);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed) return;

        try
        {
            foreach (var chunk in _chunks)
            {
                chunk?.Dispose();
            }
        }
        finally
        {
            _disposed = true;
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;

        try
        {
            foreach (var chunk in _chunks)
            {
                if (chunk is IAsyncDisposable asyncDisposable)
                {
                    await asyncDisposable.DisposeAsync();
                }
                else
                {
                    chunk?.Dispose();
                }
            }
        }
        finally
        {
            _disposed = true;
        }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(Dataset));
    }
}