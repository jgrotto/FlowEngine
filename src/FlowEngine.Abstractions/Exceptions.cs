namespace FlowEngine.Abstractions;

/// <summary>
/// Exception thrown when schema validation fails.
/// </summary>
public sealed class SchemaValidationException : Exception
{
    /// <summary>
    /// Initializes a new instance of the SchemaValidationException class.
    /// </summary>
    /// <param name="message">The error message</param>
    public SchemaValidationException(string message) : base(message)
    {
    }

    /// <summary>
    /// Initializes a new instance of the SchemaValidationException class.
    /// </summary>
    /// <param name="message">The error message</param>
    /// <param name="innerException">The inner exception</param>
    public SchemaValidationException(string message, Exception innerException) : base(message, innerException)
    {
    }

    /// <summary>
    /// Gets or sets the source schema that caused the validation failure.
    /// </summary>
    public ISchema? SourceSchema { get; init; }

    /// <summary>
    /// Gets or sets the target schema that caused the validation failure.
    /// </summary>
    public ISchema? TargetSchema { get; init; }

    /// <summary>
    /// Gets or sets the list of validation errors.
    /// </summary>
    public IReadOnlyList<string>? ValidationErrors { get; init; }
}

/// <summary>
/// Exception thrown when memory allocation fails due to resource constraints.
/// </summary>
public sealed class MemoryAllocationException : Exception
{
    /// <summary>
    /// Initializes a new instance of the MemoryAllocationException class.
    /// </summary>
    /// <param name="message">The error message</param>
    public MemoryAllocationException(string message) : base(message)
    {
    }

    /// <summary>
    /// Initializes a new instance of the MemoryAllocationException class.
    /// </summary>
    /// <param name="message">The error message</param>
    /// <param name="innerException">The inner exception</param>
    public MemoryAllocationException(string message, Exception innerException) : base(message, innerException)
    {
    }

    /// <summary>
    /// Gets or sets the requested allocation size in bytes.
    /// </summary>
    public long RequestedSize { get; init; }

    /// <summary>
    /// Gets or sets the available memory at the time of failure in bytes.
    /// </summary>
    public long AvailableMemory { get; init; }

    /// <summary>
    /// Gets or sets the memory pressure level at the time of failure (0.0 to 1.0).
    /// </summary>
    public double MemoryPressure { get; init; }
}

/// <summary>
/// Exception thrown when chunk processing operations fail.
/// </summary>
public sealed class ChunkProcessingException : Exception
{
    /// <summary>
    /// Initializes a new instance of the ChunkProcessingException class.
    /// </summary>
    /// <param name="message">The error message</param>
    public ChunkProcessingException(string message) : base(message)
    {
    }

    /// <summary>
    /// Initializes a new instance of the ChunkProcessingException class.
    /// </summary>
    /// <param name="message">The error message</param>
    /// <param name="innerException">The inner exception</param>
    public ChunkProcessingException(string message, Exception innerException) : base(message, innerException)
    {
    }

    /// <summary>
    /// Gets or sets the chunk that was being processed when the error occurred.
    /// </summary>
    public IChunk? Chunk { get; init; }

    /// <summary>
    /// Gets or sets the index of the row being processed when the error occurred.
    /// </summary>
    public int? RowIndex { get; init; }

    /// <summary>
    /// Gets or sets the name of the column being processed when the error occurred.
    /// </summary>
    public string? ColumnName { get; init; }
}