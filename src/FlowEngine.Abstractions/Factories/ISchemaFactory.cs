using FlowEngine.Abstractions.Data;

namespace FlowEngine.Abstractions.Factories;

/// <summary>
/// Factory interface for creating Schema instances.
/// Bridges external plugins to Core schema creation without direct Core dependencies.
/// </summary>
public interface ISchemaFactory
{
    /// <summary>
    /// Creates a schema from column definitions.
    /// </summary>
    /// <param name="columns">Array of column definitions</param>
    /// <returns>New schema instance</returns>
    /// <exception cref="ArgumentNullException">Thrown when columns is null</exception>
    /// <exception cref="ArgumentException">Thrown when columns array is empty or contains invalid definitions</exception>
    ISchema CreateSchema(ColumnDefinition[] columns);

    /// <summary>
    /// Creates a schema from column definitions with metadata.
    /// </summary>
    /// <param name="columns">Array of column definitions</param>
    /// <param name="metadata">Schema-level metadata</param>
    /// <returns>New schema instance</returns>
    ISchema CreateSchema(ColumnDefinition[] columns, IReadOnlyDictionary<string, object>? metadata);

    /// <summary>
    /// Creates a schema with explicit name and version.
    /// </summary>
    /// <param name="name">Schema name</param>
    /// <param name="version">Schema version</param>
    /// <param name="columns">Array of column definitions</param>
    /// <returns>New schema instance</returns>
    ISchema CreateSchema(string name, string version, ColumnDefinition[] columns);

    /// <summary>
    /// Creates a schema with all properties specified.
    /// </summary>
    /// <param name="name">Schema name</param>
    /// <param name="version">Schema version</param>
    /// <param name="columns">Array of column definitions</param>
    /// <param name="metadata">Schema-level metadata</param>
    /// <returns>New schema instance</returns>
    ISchema CreateSchema(string name, string version, ColumnDefinition[] columns, IReadOnlyDictionary<string, object>? metadata);

    /// <summary>
    /// Validates column definitions without creating a schema.
    /// </summary>
    /// <param name="columns">Array of column definitions to validate</param>
    /// <returns>Validation result indicating success or failure with details</returns>
    ValidationResult ValidateColumns(ColumnDefinition[] columns);

    /// <summary>
    /// Creates a derived schema with additional columns.
    /// </summary>
    /// <param name="baseSchema">Base schema to extend</param>
    /// <param name="additionalColumns">Additional columns to append</param>
    /// <returns>New schema with combined columns</returns>
    ISchema ExtendSchema(ISchema baseSchema, ColumnDefinition[] additionalColumns);

    /// <summary>
    /// Creates a derived schema with a subset of columns.
    /// </summary>
    /// <param name="baseSchema">Base schema to project from</param>
    /// <param name="columnNames">Names of columns to include in projection</param>
    /// <returns>New schema with selected columns only</returns>
    ISchema ProjectSchema(ISchema baseSchema, string[] columnNames);
}
