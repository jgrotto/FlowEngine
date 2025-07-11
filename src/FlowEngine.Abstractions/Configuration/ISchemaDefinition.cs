namespace FlowEngine.Abstractions.Configuration;

/// <summary>
/// Represents a schema definition loaded from YAML configuration.
/// This is converted to ISchema during runtime processing.
/// </summary>
public interface ISchemaDefinition
{
    /// <summary>
    /// Gets the name of this schema.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Gets the version of this schema.
    /// </summary>
    string Version { get; }

    /// <summary>
    /// Gets the field definitions for this schema.
    /// </summary>
    IReadOnlyList<IFieldDefinition> Fields { get; }

    /// <summary>
    /// Gets optional metadata for this schema.
    /// </summary>
    IReadOnlyDictionary<string, object>? Metadata { get; }

    /// <summary>
    /// Gets schema transformation definitions for evolution.
    /// </summary>
    IReadOnlyList<ISchemaTransformation>? Transformations { get; }
}

/// <summary>
/// Represents a field definition in a schema from YAML.
/// </summary>
public interface IFieldDefinition
{
    /// <summary>
    /// Gets the name of this field.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Gets the type name of this field (e.g., "int32", "string").
    /// </summary>
    string Type { get; }

    /// <summary>
    /// Gets the explicit index of this field for ArrayRow optimization.
    /// Must be sequential starting from 0.
    /// </summary>
    int Index { get; }

    /// <summary>
    /// Gets whether this field is required.
    /// </summary>
    bool Required { get; }

    /// <summary>
    /// Gets whether this field is nullable.
    /// </summary>
    bool IsNullable { get; }

    /// <summary>
    /// Gets the optional description of this field.
    /// </summary>
    string? Description { get; }

    /// <summary>
    /// Gets the default value for this field if any.
    /// </summary>
    object? DefaultValue { get; }
}

/// <summary>
/// Represents a schema transformation for evolution.
/// </summary>
public interface ISchemaTransformation
{
    /// <summary>
    /// Gets the source version this transformation applies from.
    /// </summary>
    string FromVersion { get; }

    /// <summary>
    /// Gets the target version this transformation produces.
    /// </summary>
    string ToVersion { get; }

    /// <summary>
    /// Gets the type of transformation (add_field, remove_field, rename_field, etc.).
    /// </summary>
    string TransformationType { get; }

    /// <summary>
    /// Gets the transformation-specific parameters.
    /// </summary>
    IReadOnlyDictionary<string, object> Parameters { get; }
}
