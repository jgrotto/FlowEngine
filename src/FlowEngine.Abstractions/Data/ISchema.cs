using System.Collections.Immutable;

namespace FlowEngine.Abstractions.Data;

/// <summary>
/// Defines the structure and metadata for data rows with O(1) field access.
/// Provides compile-time schema validation and type safety for high-performance data processing.
/// </summary>
public interface ISchema : IEquatable<ISchema>
{
    /// <summary>
    /// Gets the column definitions in this schema.
    /// </summary>
    ImmutableArray<ColumnDefinition> Columns { get; }

    /// <summary>
    /// Gets the number of columns in this schema.
    /// </summary>
    int ColumnCount { get; }

    /// <summary>
    /// Gets the unique signature for this schema for caching and compatibility checks.
    /// </summary>
    string Signature { get; }

    /// <summary>
    /// Gets the zero-based index of the specified column name.
    /// Returns -1 if the column is not found.
    /// </summary>
    /// <param name="columnName">The name of the column to find</param>
    /// <returns>The zero-based index of the column, or -1 if not found</returns>
    int GetIndex(string columnName);

    /// <summary>
    /// Attempts to get the zero-based index of the specified column name.
    /// </summary>
    /// <param name="columnName">The name of the column to find</param>
    /// <param name="index">When this method returns, contains the zero-based index of the column if found; otherwise, -1</param>
    /// <returns>true if the column was found; otherwise, false</returns>
    bool TryGetIndex(string columnName, out int index);

    /// <summary>
    /// Gets the column definition by name.
    /// </summary>
    /// <param name="columnName">The name of the column</param>
    /// <returns>The column definition, or null if not found</returns>
    ColumnDefinition? GetColumn(string columnName);

    /// <summary>
    /// Determines if this schema has a column with the specified name.
    /// </summary>
    /// <param name="columnName">The name of the column to check</param>
    /// <returns>true if the column exists; otherwise, false</returns>
    bool HasColumn(string columnName);

    /// <summary>
    /// Determines if this schema is compatible with another schema.
    /// Compatible schemas can have data flow between them with appropriate transformations.
    /// </summary>
    /// <param name="other">The schema to check compatibility with</param>
    /// <returns>true if the schemas are compatible; otherwise, false</returns>
    bool IsCompatibleWith(ISchema other);

    /// <summary>
    /// Creates a new schema with additional columns.
    /// </summary>
    /// <param name="columns">The columns to add</param>
    /// <returns>A new schema with the additional columns</returns>
    ISchema AddColumns(params ColumnDefinition[] columns);

    /// <summary>
    /// Creates a new schema with the specified columns removed.
    /// </summary>
    /// <param name="columnNames">The names of the columns to remove</param>
    /// <returns>A new schema without the specified columns</returns>
    ISchema RemoveColumns(params string[] columnNames);
}

/// <summary>
/// Defines a column in a schema with type information and constraints.
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
    /// Gets or sets whether the column can contain null values.
    /// </summary>
    public bool IsNullable { get; init; } = true;

    /// <summary>
    /// Gets or sets the optional description of the column.
    /// </summary>
    public string? Description { get; init; }

    /// <summary>
    /// Gets or sets the zero-based index of this column for ArrayRow optimization.
    /// Must be sequential (0, 1, 2, ...) for optimal performance.
    /// </summary>
    public int Index { get; init; }

    /// <summary>
    /// Gets or sets optional metadata associated with the column.
    /// </summary>
    public ImmutableDictionary<string, object>? Metadata { get; init; }
}