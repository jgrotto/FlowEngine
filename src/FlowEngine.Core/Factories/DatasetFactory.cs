using FlowEngine.Abstractions;
using FlowEngine.Abstractions.Data;
using FlowEngine.Abstractions.Factories;
using FlowEngine.Core.Data;
using Microsoft.Extensions.Logging;

namespace FlowEngine.Core.Factories;

/// <summary>
/// Core implementation of IDatasetFactory.
/// Creates optimized Dataset instances with efficient memory management and streaming support.
/// </summary>
public sealed class DatasetFactory : IDatasetFactory
{
    private readonly ILogger<DatasetFactory> _logger;
    private readonly IChunkFactory _chunkFactory;
    private readonly IMemoryManager? _memoryManager;

    // Default configuration
    private const int DefaultChunkSize = 10000;
    private const long DefaultTargetMemorySize = 64 * 1024 * 1024; // 64MB per partition

    /// <summary>
    /// Initializes a new DatasetFactory.
    /// </summary>
    /// <param name="logger">Logger for factory operations</param>
    /// <param name="chunkFactory">Factory for creating Chunk instances</param>
    /// <param name="memoryManager">Optional memory manager for optimization</param>
    public DatasetFactory(
        ILogger<DatasetFactory> logger,
        IChunkFactory chunkFactory,
        IMemoryManager? memoryManager = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _chunkFactory = chunkFactory ?? throw new ArgumentNullException(nameof(chunkFactory));
        _memoryManager = memoryManager;
    }

    /// <inheritdoc />
    public IDataset CreateDataset(IChunk[] chunks)
    {
        ArgumentNullException.ThrowIfNull(chunks);

        if (chunks.Length == 0)
        {
            throw new ArgumentException("Cannot create dataset with empty chunk array", nameof(chunks));
        }

        // Validate all chunks have the same schema
        var schema = chunks[0].Schema;
        for (int i = 1; i < chunks.Length; i++)
        {
            if (!AreSchemasSame(schema, chunks[i].Schema))
            {
                throw new ArgumentException($"Chunk at index {i} has mismatched schema");
            }
        }

        var totalRows = chunks.Sum(c => c.RowCount);
        _logger.LogDebug("Creating dataset with {ChunkCount} chunks, {TotalRows} total rows", chunks.Length, totalRows);

        return new Dataset(schema, chunks);
    }

    /// <inheritdoc />
    public IDataset CreateDataset(ISchema schema, IChunk[] chunks)
    {
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentNullException.ThrowIfNull(chunks);

        // Validate all chunks match the provided schema
        for (int i = 0; i < chunks.Length; i++)
        {
            if (!AreSchemasSame(schema, chunks[i].Schema))
            {
                throw new ArgumentException($"Chunk at index {i} does not match the provided schema");
            }
        }

        var totalRows = chunks.Sum(c => c.RowCount);
        _logger.LogDebug("Creating dataset with explicit schema, {ChunkCount} chunks, {TotalRows} total rows",
            chunks.Length, totalRows);

        return new Dataset(schema, chunks);
    }

    /// <inheritdoc />
    public IDataset CreateDataset(ISchema schema, IEnumerable<IChunk> chunks)
    {
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentNullException.ThrowIfNull(chunks);

        var chunkArray = chunks.ToArray();
        return CreateDataset(schema, chunkArray);
    }

    /// <inheritdoc />
    public IDataset CreateDatasetFromRows(ISchema schema, IArrayRow[] rows, int? chunkSize = null)
    {
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentNullException.ThrowIfNull(rows);

        if (rows.Length == 0)
        {
            return CreateEmptyDataset(schema);
        }

        var actualChunkSize = chunkSize ?? _chunkFactory.GetOptimalChunkSize(schema);
        var chunks = new List<IChunk>();

        for (int i = 0; i < rows.Length; i += actualChunkSize)
        {
            var chunkRows = new ArraySegment<IArrayRow>(rows, i, Math.Min(actualChunkSize, rows.Length - i));
            var chunk = _chunkFactory.CreateChunk(schema, chunkRows.ToArray());
            chunks.Add(chunk);
        }

        _logger.LogDebug("Created dataset from {RowCount} rows using chunk size {ChunkSize}, resulting in {ChunkCount} chunks",
            rows.Length, actualChunkSize, chunks.Count);

        return new Dataset(schema, chunks.ToArray());
    }

    /// <inheritdoc />
    public IDataset CreateDatasetFromRows(ISchema schema, IEnumerable<IArrayRow> rows, int? chunkSize = null)
    {
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentNullException.ThrowIfNull(rows);

        var actualChunkSize = chunkSize ?? _chunkFactory.GetOptimalChunkSize(schema);
        var chunks = new List<IChunk>();
        var currentChunkRows = new List<IArrayRow>(actualChunkSize);

        foreach (var row in rows)
        {
            currentChunkRows.Add(row);

            if (currentChunkRows.Count >= actualChunkSize)
            {
                var chunk = _chunkFactory.CreateChunk(schema, currentChunkRows.ToArray());
                chunks.Add(chunk);
                currentChunkRows.Clear();
            }
        }

        // Handle remaining rows
        if (currentChunkRows.Count > 0)
        {
            var chunk = _chunkFactory.CreateChunk(schema, currentChunkRows.ToArray());
            chunks.Add(chunk);
        }

        var totalRows = chunks.Sum(c => c.RowCount);
        _logger.LogDebug("Created dataset from enumerable rows: {TotalRows} rows in {ChunkCount} chunks",
            totalRows, chunks.Count);

        return new Dataset(schema, chunks.ToArray());
    }

    /// <inheritdoc />
    public IDataset CreateEmptyDataset(ISchema schema)
    {
        ArgumentNullException.ThrowIfNull(schema);

        _logger.LogDebug("Creating empty dataset");
        return new Dataset(schema, Array.Empty<IChunk>());
    }

    /// <inheritdoc />
    public IDataset CreateDataset(ISchema schema, IChunk[] chunks, IReadOnlyDictionary<string, object>? metadata, object? partitionInfo = null)
    {
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentNullException.ThrowIfNull(chunks);

        // For now, create a basic dataset. In a full implementation, this would include
        // metadata and partitioning information in the dataset structure
        var dataset = CreateDataset(schema, chunks);

        _logger.LogDebug("Created dataset with metadata and partition info: {ChunkCount} chunks", chunks.Length);
        return dataset;
    }

    /// <inheritdoc />
    public async Task<IDataset> CombineDatasets(IDataset[] datasets)
    {
        ArgumentNullException.ThrowIfNull(datasets);

        if (datasets.Length == 0)
        {
            throw new ArgumentException("Cannot combine empty dataset array", nameof(datasets));
        }

        if (datasets.Length == 1)
        {
            return datasets[0];
        }

        // Validate all datasets have compatible schemas
        var schema = datasets[0].Schema;
        for (int i = 1; i < datasets.Length; i++)
        {
            if (!AreSchemasSame(schema, datasets[i].Schema))
            {
                throw new ArgumentException($"Dataset at index {i} has incompatible schema");
            }
        }

        var allChunks = new List<IChunk>();
        long totalRows = 0;

        foreach (var dataset in datasets)
        {
            await foreach (var chunk in dataset.GetChunksAsync())
            {
                allChunks.Add(chunk);
            }
            totalRows += dataset.RowCount ?? 0;
        }

        _logger.LogDebug("Combined {DatasetCount} datasets into single dataset: {TotalRows} rows in {TotalChunks} chunks",
            datasets.Length, totalRows, allChunks.Count);

        return new Dataset(schema, allChunks.ToArray());
    }

    /// <inheritdoc />
    public async Task<IDataset> SliceDataset(IDataset source, int startChunkIndex, int chunkCount)
    {
        ArgumentNullException.ThrowIfNull(source);

        if (startChunkIndex < 0)
        {
            throw new ArgumentException("Start chunk index cannot be negative", nameof(startChunkIndex));
        }

        if (chunkCount < 0)
        {
            throw new ArgumentException("Chunk count cannot be negative", nameof(chunkCount));
        }

        var sourceChunkCount = source.ChunkCount ?? 0;
        if (startChunkIndex + chunkCount > sourceChunkCount)
        {
            throw new ArgumentException("Slice range exceeds source dataset bounds");
        }

        if (chunkCount == 0)
        {
            return CreateEmptyDataset(source.Schema);
        }

        var slicedChunks = new List<IChunk>();
        int currentIndex = 0;

        await foreach (var chunk in source.GetChunksAsync())
        {
            if (currentIndex >= startChunkIndex && currentIndex < startChunkIndex + chunkCount)
            {
                slicedChunks.Add(chunk);
            }
            currentIndex++;

            if (currentIndex >= startChunkIndex + chunkCount)
            {
                break;
            }
        }

        _logger.LogDebug("Sliced dataset: chunks {StartIndex} to {EndIndex} ({ChunkCount} chunks)",
            startChunkIndex, startChunkIndex + chunkCount - 1, chunkCount);

        return new Dataset(source.Schema, slicedChunks.ToArray());
    }

    /// <inheritdoc />
    public async Task<IDataset> ProjectDataset(IDataset source, ISchema targetSchema)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(targetSchema);

        var projectedChunks = new List<IChunk>();

        await foreach (var chunk in source.GetChunksAsync())
        {
            var projectedChunk = _chunkFactory.ProjectChunk(chunk, targetSchema);
            projectedChunks.Add(projectedChunk);
        }

        _logger.LogDebug("Projected dataset from {SourceColumns} to {TargetColumns} columns",
            source.Schema.ColumnCount, targetSchema.ColumnCount);

        return new Dataset(targetSchema, projectedChunks);
    }

    /// <inheritdoc />
    public IDataset CreateStreamingDataset(ISchema schema, Func<IEnumerable<IChunk>> chunkProvider, long? estimatedChunkCount = null)
    {
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentNullException.ThrowIfNull(chunkProvider);

        // For now, materialize all chunks. In a full implementation, this would create
        // a streaming dataset that yields chunks on-demand
        var chunks = chunkProvider().ToArray();

        _logger.LogDebug("Created streaming dataset with {ChunkCount} chunks (estimated: {EstimatedCount})",
            chunks.Length, estimatedChunkCount);

        return new Dataset(schema, chunks);
    }

    /// <inheritdoc />
    public ValidationResult ValidateDatasetStructure(ISchema schema, IChunk[] chunks)
    {
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentNullException.ThrowIfNull(chunks);

        var errors = new List<string>();

        for (int i = 0; i < chunks.Length; i++)
        {
            var chunk = chunks[i];
            if (chunk == null)
            {
                errors.Add($"Chunk at index {i} is null");
                continue;
            }

            if (!AreSchemasSame(schema, chunk.Schema))
            {
                errors.Add($"Chunk at index {i} has mismatched schema");
            }
        }

        return errors.Count == 0 ? ValidationResult.Success() : ValidationResult.Failure(errors.ToArray());
    }

    /// <inheritdoc />
    public DatasetPartitioningConfig GetOptimalPartitioning(ISchema schema, long estimatedRowCount, long targetMemorySize)
    {
        ArgumentNullException.ThrowIfNull(schema);

        if (estimatedRowCount <= 0)
        {
            throw new ArgumentException("Estimated row count must be positive", nameof(estimatedRowCount));
        }

        if (targetMemorySize <= 0)
        {
            throw new ArgumentException("Target memory size must be positive", nameof(targetMemorySize));
        }

        // Calculate optimal chunk size based on memory constraints
        var optimalChunkSize = _chunkFactory.GetOptimalChunkSize(schema, targetMemorySize / 4); // Use 1/4 of target for chunk size
        var totalChunks = (int)Math.Ceiling((double)estimatedRowCount / optimalChunkSize);

        // Calculate chunks per partition to stay within memory limits
        var chunksPerPartition = Math.Max(1, (int)(targetMemorySize / (optimalChunkSize * EstimateRowMemorySize(schema))));

        var config = new DatasetPartitioningConfig
        {
            ChunksPerPartition = chunksPerPartition,
            RowsPerChunk = optimalChunkSize,
            EstimatedMemoryPerPartition = chunksPerPartition * optimalChunkSize * EstimateRowMemorySize(schema),
            PartitioningStrategy = "RowBased"
        };

        _logger.LogDebug("Calculated optimal partitioning: {ChunksPerPartition} chunks per partition, {RowsPerChunk} rows per chunk",
            config.ChunksPerPartition, config.RowsPerChunk);

        return config;
    }

    /// <summary>
    /// Checks if two schemas are functionally the same.
    /// </summary>
    private static bool AreSchemasSame(ISchema schema1, ISchema schema2)
    {
        if (ReferenceEquals(schema1, schema2))
        {
            return true;
        }

        if (schema1.ColumnCount != schema2.ColumnCount)
        {
            return false;
        }

        for (int i = 0; i < schema1.ColumnCount; i++)
        {
            var col1 = schema1.Columns[i];
            var col2 = schema2.Columns[i];

            if (col1.Name != col2.Name || col1.DataType != col2.DataType)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Estimates memory size for a single row in the schema.
    /// </summary>
    private static long EstimateRowMemorySize(ISchema schema)
    {
        long size = 64; // Base object overhead

        foreach (var column in schema.Columns)
        {
            size += EstimateColumnMemorySize(column.DataType);
        }

        return size;
    }

    /// <summary>
    /// Estimates memory size for a column data type.
    /// </summary>
    private static long EstimateColumnMemorySize(Type dataType)
    {
        if (dataType == typeof(byte) || dataType == typeof(sbyte))
        {
            return 1;
        }

        if (dataType == typeof(short) || dataType == typeof(ushort))
        {
            return 2;
        }

        if (dataType == typeof(int) || dataType == typeof(uint) || dataType == typeof(float))
        {
            return 4;
        }

        if (dataType == typeof(long) || dataType == typeof(ulong) || dataType == typeof(double) || dataType == typeof(DateTime))
        {
            return 8;
        }

        if (dataType == typeof(decimal))
        {
            return 16;
        }

        if (dataType == typeof(string))
        {
            return 50; // Average string size estimate
        }

        if (dataType == typeof(Guid))
        {
            return 16;
        }

        // Default for unknown types
        return 8;
    }
}
