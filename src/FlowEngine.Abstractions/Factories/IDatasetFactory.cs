using FlowEngine.Abstractions.Data;

namespace FlowEngine.Abstractions.Factories;

/// <summary>
/// Factory interface for creating Dataset instances.
/// Provides efficient dataset creation with proper memory management and streaming support.
/// </summary>
public interface IDatasetFactory
{
    /// <summary>
    /// Creates a dataset from an array of chunks.
    /// </summary>
    /// <param name="chunks">Array of chunks for the dataset</param>
    /// <returns>New dataset instance</returns>
    /// <exception cref="ArgumentNullException">Thrown when chunks is null</exception>
    /// <exception cref="ArgumentException">Thrown when chunks have mismatched schemas</exception>
    IDataset CreateDataset(IChunk[] chunks);

    /// <summary>
    /// Creates a dataset with explicit schema and chunk data.
    /// </summary>
    /// <param name="schema">Schema for all chunks in the dataset</param>
    /// <param name="chunks">Array of chunks for the dataset</param>
    /// <returns>New dataset instance</returns>
    IDataset CreateDataset(ISchema schema, IChunk[] chunks);

    /// <summary>
    /// Creates a dataset from a sequence of chunks with lazy evaluation.
    /// Useful for streaming scenarios where chunks are generated on-demand.
    /// </summary>
    /// <param name="schema">Schema for all chunks in the dataset</param>
    /// <param name="chunks">Enumerable sequence of chunks</param>
    /// <returns>New dataset instance</returns>
    IDataset CreateDataset(ISchema schema, IEnumerable<IChunk> chunks);

    /// <summary>
    /// Creates a dataset from an array of rows, automatically chunking for optimal performance.
    /// </summary>
    /// <param name="schema">Schema for all rows</param>
    /// <param name="rows">Array of rows to chunk</param>
    /// <param name="chunkSize">Optional chunk size (uses optimal size if not specified)</param>
    /// <returns>New dataset instance</returns>
    IDataset CreateDatasetFromRows(ISchema schema, IArrayRow[] rows, int? chunkSize = null);

    /// <summary>
    /// Creates a dataset from a sequence of rows with automatic chunking.
    /// </summary>
    /// <param name="schema">Schema for all rows</param>
    /// <param name="rows">Enumerable sequence of rows</param>
    /// <param name="chunkSize">Optional chunk size (uses optimal size if not specified)</param>
    /// <returns>New dataset instance</returns>
    IDataset CreateDatasetFromRows(ISchema schema, IEnumerable<IArrayRow> rows, int? chunkSize = null);

    /// <summary>
    /// Creates an empty dataset with the specified schema.
    /// </summary>
    /// <param name="schema">Schema for the dataset</param>
    /// <returns>New empty dataset instance</returns>
    IDataset CreateEmptyDataset(ISchema schema);

    /// <summary>
    /// Creates a dataset with metadata and optional partitioning information.
    /// </summary>
    /// <param name="schema">Schema for the dataset</param>
    /// <param name="chunks">Array of chunks for the dataset</param>
    /// <param name="metadata">Dataset-level metadata</param>
    /// <param name="partitionInfo">Optional partitioning information</param>
    /// <returns>New dataset instance</returns>
    IDataset CreateDataset(ISchema schema, IChunk[] chunks, IReadOnlyDictionary<string, object>? metadata, object? partitionInfo = null);

    /// <summary>
    /// Combines multiple datasets into a single dataset.
    /// All datasets must have compatible schemas.
    /// </summary>
    /// <param name="datasets">Array of datasets to combine</param>
    /// <returns>New dataset instance containing all chunks</returns>
    /// <exception cref="ArgumentException">Thrown when datasets have incompatible schemas</exception>
    Task<IDataset> CombineDatasets(IDataset[] datasets);

    /// <summary>
    /// Creates a dataset containing a subset of chunks from a source dataset.
    /// </summary>
    /// <param name="source">Source dataset to slice from</param>
    /// <param name="startChunkIndex">Starting chunk index (inclusive)</param>
    /// <param name="chunkCount">Number of chunks to include</param>
    /// <returns>New dataset instance with sliced chunks</returns>
    Task<IDataset> SliceDataset(IDataset source, int startChunkIndex, int chunkCount);

    /// <summary>
    /// Creates a dataset with projected columns from a source dataset.
    /// </summary>
    /// <param name="source">Source dataset to project from</param>
    /// <param name="targetSchema">Schema defining which columns to include</param>
    /// <returns>New dataset instance with projected columns</returns>
    Task<IDataset> ProjectDataset(IDataset source, ISchema targetSchema);

    /// <summary>
    /// Creates a streaming dataset that yields chunks on-demand.
    /// Useful for very large datasets that don't fit in memory.
    /// </summary>
    /// <param name="schema">Schema for the dataset</param>
    /// <param name="chunkProvider">Function that provides chunks on demand</param>
    /// <param name="estimatedChunkCount">Estimated total number of chunks (for progress tracking)</param>
    /// <returns>New streaming dataset instance</returns>
    IDataset CreateStreamingDataset(ISchema schema, Func<IEnumerable<IChunk>> chunkProvider, long? estimatedChunkCount = null);

    /// <summary>
    /// Validates dataset structure without creating a dataset instance.
    /// </summary>
    /// <param name="schema">Schema to validate against</param>
    /// <param name="chunks">Chunks to validate</param>
    /// <returns>Validation result indicating success or failure with details</returns>
    ValidationResult ValidateDatasetStructure(ISchema schema, IChunk[] chunks);

    /// <summary>
    /// Gets optimal partitioning configuration for the given dataset characteristics.
    /// </summary>
    /// <param name="schema">Schema of the dataset</param>
    /// <param name="estimatedRowCount">Estimated total number of rows</param>
    /// <param name="targetMemorySize">Target memory size per partition in bytes</param>
    /// <returns>Recommended partitioning configuration</returns>
    DatasetPartitioningConfig GetOptimalPartitioning(ISchema schema, long estimatedRowCount, long targetMemorySize);
}

/// <summary>
/// Configuration for dataset partitioning optimization.
/// </summary>
public sealed class DatasetPartitioningConfig
{
    /// <summary>
    /// Recommended number of chunks per partition.
    /// </summary>
    public int ChunksPerPartition { get; init; }

    /// <summary>
    /// Recommended number of rows per chunk.
    /// </summary>
    public int RowsPerChunk { get; init; }

    /// <summary>
    /// Estimated memory usage per partition in bytes.
    /// </summary>
    public long EstimatedMemoryPerPartition { get; init; }

    /// <summary>
    /// Recommended partitioning strategy.
    /// </summary>
    public string PartitioningStrategy { get; init; } = "RowBased";
}