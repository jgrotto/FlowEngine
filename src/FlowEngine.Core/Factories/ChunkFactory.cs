using FlowEngine.Abstractions;
using FlowEngine.Abstractions.Data;
using FlowEngine.Abstractions.Factories;
using FlowEngine.Core.Data;
using Microsoft.Extensions.Logging;

namespace FlowEngine.Core.Factories;

/// <summary>
/// Core implementation of IChunkFactory.
/// Creates optimized Chunk instances with efficient memory management and streaming support.
/// </summary>
public sealed class ChunkFactory : IChunkFactory
{
    private readonly ILogger<ChunkFactory> _logger;
    private readonly IArrayRowFactory _arrayRowFactory;
    private readonly IMemoryManager? _memoryManager;

    // Default optimal chunk size configuration
    private const int DefaultChunkSize = 10000;
    private const long DefaultTargetMemorySize = 16 * 1024 * 1024; // 16MB

    /// <summary>
    /// Initializes a new ChunkFactory.
    /// </summary>
    /// <param name="logger">Logger for factory operations</param>
    /// <param name="arrayRowFactory">Factory for creating ArrayRow instances</param>
    /// <param name="memoryManager">Optional memory manager for optimization</param>
    public ChunkFactory(
        ILogger<ChunkFactory> logger,
        IArrayRowFactory arrayRowFactory,
        IMemoryManager? memoryManager = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _arrayRowFactory = arrayRowFactory ?? throw new ArgumentNullException(nameof(arrayRowFactory));
        _memoryManager = memoryManager;
    }

    /// <inheritdoc />
    public IChunk CreateChunk(IArrayRow[] rows)
    {
        ArgumentNullException.ThrowIfNull(rows);

        if (rows.Length == 0)
        {
            throw new ArgumentException("Cannot create chunk with empty row array", nameof(rows));
        }

        // Validate all rows have the same schema
        var schema = rows[0].Schema;
        for (int i = 1; i < rows.Length; i++)
        {
            if (!ReferenceEquals(rows[i].Schema, schema) && !AreSchemasSame(schema, rows[i].Schema))
            {
                throw new ArgumentException($"Row at index {i} has mismatched schema");
            }
        }

        _logger.LogDebug("Creating chunk with {RowCount} rows", rows.Length);
        return new Chunk(schema, rows);
    }

    /// <inheritdoc />
    public IChunk CreateChunk(ISchema schema, IArrayRow[] rows)
    {
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentNullException.ThrowIfNull(rows);

        // Validate all rows match the provided schema
        for (int i = 0; i < rows.Length; i++)
        {
            if (!ReferenceEquals(rows[i].Schema, schema) && !AreSchemasSame(schema, rows[i].Schema))
            {
                throw new ArgumentException($"Row at index {i} does not match the provided schema");
            }
        }

        _logger.LogDebug("Creating chunk with explicit schema and {RowCount} rows", rows.Length);
        return new Chunk(schema, rows);
    }

    /// <inheritdoc />
    public IChunk CreateChunk(ISchema schema, IEnumerable<IArrayRow> rows)
    {
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentNullException.ThrowIfNull(rows);

        var rowArray = rows.ToArray();
        return CreateChunk(schema, rowArray);
    }

    /// <inheritdoc />
    public IChunk CreateEmptyChunk(ISchema schema)
    {
        ArgumentNullException.ThrowIfNull(schema);

        _logger.LogDebug("Creating empty chunk with schema");
        return new Chunk(schema, Array.Empty<IArrayRow>());
    }

    /// <inheritdoc />
    public IChunk CreateChunkWithCapacity(ISchema schema, int capacity)
    {
        ArgumentNullException.ThrowIfNull(schema);

        if (capacity < 0)
        {
            throw new ArgumentException("Capacity cannot be negative", nameof(capacity));
        }

        _logger.LogDebug("Creating chunk with capacity {Capacity}", capacity);

        // For now, create an empty chunk. In a full implementation, this might 
        // pre-allocate memory or use a specialized chunk type that can grow efficiently
        return new Chunk(schema, Array.Empty<IArrayRow>());
    }

    /// <inheritdoc />
    public IChunk CreateChunkFromColumns(ISchema schema, object?[][] columnData, int rowCount)
    {
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentNullException.ThrowIfNull(columnData);

        if (columnData.Length != schema.ColumnCount)
        {
            throw new ArgumentException(
                $"Column data array length ({columnData.Length}) must match schema column count ({schema.ColumnCount})",
                nameof(columnData));
        }

        if (rowCount < 0)
        {
            throw new ArgumentException("Row count cannot be negative", nameof(rowCount));
        }

        // Validate all columns have the correct number of rows
        for (int col = 0; col < columnData.Length; col++)
        {
            if (columnData[col].Length != rowCount)
            {
                throw new ArgumentException($"Column {col} has {columnData[col].Length} values but expected {rowCount}");
            }
        }

        _logger.LogDebug("Creating chunk from columnar data: {RowCount} rows, {ColumnCount} columns", rowCount, schema.ColumnCount);

        // Convert column-major to row-major format
        var rows = new IArrayRow[rowCount];
        for (int row = 0; row < rowCount; row++)
        {
            var values = new object?[schema.ColumnCount];
            for (int col = 0; col < schema.ColumnCount; col++)
            {
                values[col] = columnData[col][row];
            }
            rows[row] = _arrayRowFactory.CreateRow(schema, values);
        }

        return new Chunk(schema, rows);
    }

    /// <inheritdoc />
    public IChunk CombineChunks(IChunk[] chunks)
    {
        ArgumentNullException.ThrowIfNull(chunks);

        if (chunks.Length == 0)
        {
            throw new ArgumentException("Cannot combine empty chunk array", nameof(chunks));
        }

        if (chunks.Length == 1)
        {
            return chunks[0];
        }

        // Validate all chunks have compatible schemas
        var schema = chunks[0].Schema;
        for (int i = 1; i < chunks.Length; i++)
        {
            if (!AreSchemasSame(schema, chunks[i].Schema))
            {
                throw new ArgumentException($"Chunk at index {i} has incompatible schema");
            }
        }

        var totalRowCount = chunks.Sum(c => c.RowCount);
        var combinedRows = new IArrayRow[totalRowCount];

        int currentIndex = 0;
        foreach (var chunk in chunks)
        {
            for (int i = 0; i < chunk.RowCount; i++)
            {
                combinedRows[currentIndex++] = chunk[i];
            }
        }

        _logger.LogDebug("Combined {ChunkCount} chunks into single chunk with {TotalRows} rows", chunks.Length, totalRowCount);
        return new Chunk(schema, combinedRows);
    }

    /// <inheritdoc />
    public IChunk SliceChunk(IChunk source, int startIndex, int length)
    {
        ArgumentNullException.ThrowIfNull(source);

        if (startIndex < 0)
        {
            throw new ArgumentException("Start index cannot be negative", nameof(startIndex));
        }

        if (length < 0)
        {
            throw new ArgumentException("Length cannot be negative", nameof(length));
        }

        if (startIndex + length > source.RowCount)
        {
            throw new ArgumentException("Slice range exceeds source chunk bounds");
        }

        if (length == 0)
        {
            return CreateEmptyChunk(source.Schema);
        }

        var slicedRows = source.GetRows().Skip(startIndex).Take(length).ToArray();

        _logger.LogDebug("Sliced chunk: {StartIndex} to {EndIndex} ({Length} rows)", startIndex, startIndex + length - 1, length);
        return new Chunk(source.Schema, slicedRows);
    }

    /// <inheritdoc />
    public IChunk ProjectChunk(IChunk source, ISchema targetSchema)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(targetSchema);

        var projectedRows = new IArrayRow[source.RowCount];
        for (int i = 0; i < source.RowCount; i++)
        {
            projectedRows[i] = _arrayRowFactory.ProjectRow(source.GetRows().ElementAt(i), targetSchema);
        }

        _logger.LogDebug("Projected chunk from {SourceColumns} to {TargetColumns} columns",
            source.Schema.ColumnCount, targetSchema.ColumnCount);
        return new Chunk(targetSchema, projectedRows);
    }

    /// <inheritdoc />
    public ValidationResult ValidateChunkData(ISchema schema, IArrayRow[] rows)
    {
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentNullException.ThrowIfNull(rows);

        var errors = new List<string>();

        for (int i = 0; i < rows.Length; i++)
        {
            var row = rows[i];
            if (row == null)
            {
                errors.Add($"Row at index {i} is null");
                continue;
            }

            if (!AreSchemasSame(schema, row.Schema))
            {
                errors.Add($"Row at index {i} has mismatched schema");
            }
        }

        return errors.Count == 0 ? ValidationResult.Success() : ValidationResult.Failure(errors.ToArray());
    }

    /// <inheritdoc />
    public int GetOptimalChunkSize(ISchema schema, long? targetMemorySize = null)
    {
        ArgumentNullException.ThrowIfNull(schema);

        var targetSize = targetMemorySize ?? DefaultTargetMemorySize;

        // Estimate memory per row
        long estimatedRowSize = 0;
        foreach (var column in schema.Columns)
        {
            estimatedRowSize += EstimateColumnMemorySize(column.DataType);
        }

        // Add overhead for ArrayRow structure
        estimatedRowSize += 64; // Rough estimate for object overhead

        if (estimatedRowSize <= 0)
        {
            return DefaultChunkSize;
        }

        var optimalSize = (int)(targetSize / estimatedRowSize);

        // Clamp to reasonable bounds
        optimalSize = Math.Max(100, Math.Min(optimalSize, 100000));

        _logger.LogDebug("Calculated optimal chunk size: {OptimalSize} (estimated row size: {RowSize} bytes)",
            optimalSize, estimatedRowSize);

        return optimalSize;
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
