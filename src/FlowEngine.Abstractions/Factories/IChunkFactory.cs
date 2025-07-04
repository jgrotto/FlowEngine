using FlowEngine.Abstractions.Data;

namespace FlowEngine.Abstractions.Factories;

/// <summary>
/// Factory interface for creating Chunk instances.
/// Provides efficient chunk creation with proper memory management and optimization.
/// </summary>
public interface IChunkFactory
{
    /// <summary>
    /// Creates a chunk from an array of ArrayRows.
    /// </summary>
    /// <param name="rows">Array of rows for the chunk</param>
    /// <returns>New chunk instance</returns>
    /// <exception cref="ArgumentNullException">Thrown when rows is null</exception>
    /// <exception cref="ArgumentException">Thrown when rows have mismatched schemas</exception>
    IChunk CreateChunk(IArrayRow[] rows);

    /// <summary>
    /// Creates a chunk with explicit schema and row data.
    /// </summary>
    /// <param name="schema">Schema for all rows in the chunk</param>
    /// <param name="rows">Array of rows for the chunk</param>
    /// <returns>New chunk instance</returns>
    IChunk CreateChunk(ISchema schema, IArrayRow[] rows);

    /// <summary>
    /// Creates a chunk from a sequence of rows with lazy evaluation.
    /// Useful for streaming scenarios where rows are generated on-demand.
    /// </summary>
    /// <param name="schema">Schema for all rows in the chunk</param>
    /// <param name="rows">Enumerable sequence of rows</param>
    /// <returns>New chunk instance</returns>
    IChunk CreateChunk(ISchema schema, IEnumerable<IArrayRow> rows);

    /// <summary>
    /// Creates an empty chunk with the specified schema.
    /// </summary>
    /// <param name="schema">Schema for the chunk</param>
    /// <returns>New empty chunk instance</returns>
    IChunk CreateEmptyChunk(ISchema schema);

    /// <summary>
    /// Creates a chunk with the specified capacity for efficient bulk loading.
    /// </summary>
    /// <param name="schema">Schema for the chunk</param>
    /// <param name="capacity">Initial capacity (number of rows)</param>
    /// <returns>New chunk instance with reserved capacity</returns>
    IChunk CreateChunkWithCapacity(ISchema schema, int capacity);

    /// <summary>
    /// Creates a chunk from raw columnar data.
    /// Useful for high-performance scenarios with pre-organized column data.
    /// </summary>
    /// <param name="schema">Schema for the chunk</param>
    /// <param name="columnData">Array of column arrays (column-major format)</param>
    /// <param name="rowCount">Number of rows in the data</param>
    /// <returns>New chunk instance</returns>
    IChunk CreateChunkFromColumns(ISchema schema, object?[][] columnData, int rowCount);

    /// <summary>
    /// Combines multiple chunks into a single chunk.
    /// All chunks must have compatible schemas.
    /// </summary>
    /// <param name="chunks">Array of chunks to combine</param>
    /// <returns>New chunk instance containing all rows</returns>
    /// <exception cref="ArgumentException">Thrown when chunks have incompatible schemas</exception>
    IChunk CombineChunks(IChunk[] chunks);

    /// <summary>
    /// Creates a chunk containing a subset of rows from a source chunk.
    /// </summary>
    /// <param name="source">Source chunk to slice from</param>
    /// <param name="startIndex">Starting row index (inclusive)</param>
    /// <param name="length">Number of rows to include</param>
    /// <returns>New chunk instance with sliced rows</returns>
    IChunk SliceChunk(IChunk source, int startIndex, int length);

    /// <summary>
    /// Creates a chunk with projected columns from a source chunk.
    /// </summary>
    /// <param name="source">Source chunk to project from</param>
    /// <param name="targetSchema">Schema defining which columns to include</param>
    /// <returns>New chunk instance with projected columns</returns>
    IChunk ProjectChunk(IChunk source, ISchema targetSchema);

    /// <summary>
    /// Validates chunk data without creating a chunk instance.
    /// </summary>
    /// <param name="schema">Schema to validate against</param>
    /// <param name="rows">Rows to validate</param>
    /// <returns>Validation result indicating success or failure with details</returns>
    ValidationResult ValidateChunkData(ISchema schema, IArrayRow[] rows);

    /// <summary>
    /// Gets the optimal chunk size for the given schema and memory constraints.
    /// </summary>
    /// <param name="schema">Schema to calculate optimal size for</param>
    /// <param name="targetMemorySize">Target memory size in bytes (optional)</param>
    /// <returns>Recommended number of rows per chunk</returns>
    int GetOptimalChunkSize(ISchema schema, long? targetMemorySize = null);
}