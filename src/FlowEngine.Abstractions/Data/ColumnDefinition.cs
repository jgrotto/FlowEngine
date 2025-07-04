namespace FlowEngine.Abstractions.Data;

/// <summary>
/// Defines a column in a schema with type information and metadata.
/// Optimized for ArrayRow field access with explicit indexing.
/// </summary>
public sealed record ColumnDefinition
{
    /// <summary>
    /// Gets or sets the name of the column.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Gets or sets the data type of the column.
    /// </summary>
    public required Type DataType { get; init; }

    /// <summary>
    /// Gets or sets whether the column allows null values.
    /// </summary>
    public bool IsNullable { get; init; }

    /// <summary>
    /// Gets or sets the zero-based index of this column for ArrayRow optimization.
    /// Must be sequential (0, 1, 2, ...) for optimal performance.
    /// </summary>
    public int Index { get; init; }

    /// <summary>
    /// Gets or sets the optional description of this column.
    /// </summary>
    public string? Description { get; init; }

    /// <summary>
    /// Gets or sets the optional default value for this column.
    /// </summary>
    public object? DefaultValue { get; init; }

    /// <summary>
    /// Gets or sets optional metadata for this column.
    /// </summary>
    public IReadOnlyDictionary<string, object>? Metadata { get; init; }

    /// <summary>
    /// Gets or sets the maximum length for string columns.
    /// </summary>
    public int? MaxLength { get; init; }

    /// <summary>
    /// Gets or sets the precision for decimal columns.
    /// </summary>
    public int? Precision { get; init; }

    /// <summary>
    /// Gets or sets the scale for decimal columns.
    /// </summary>
    public int? Scale { get; init; }

    /// <summary>
    /// Validates that this column definition is properly configured.
    /// </summary>
    /// <returns>True if valid, false otherwise</returns>
    public bool IsValid()
    {
        if (string.IsNullOrWhiteSpace(Name))
            return false;

        if (Index < 0)
            return false;

        if (DataType == null)
            return false;

        return true;
    }

    /// <summary>
    /// Gets the .NET type name for this column, handling nullable types.
    /// </summary>
    public string TypeName => IsNullable && DataType.IsValueType
        ? $"{DataType.Name}?"
        : DataType.Name;
}