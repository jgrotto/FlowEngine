using System.Collections.Immutable;
using FlowEngine.Abstractions;
using FlowEngine.Abstractions.Configuration;
using FlowEngine.Abstractions.Data;
using FlowEngine.Core.Data;

namespace FlowEngine.Core.Configuration;

/// <summary>
/// Implementation of schema configuration from YAML data.
/// </summary>
internal sealed class SchemaConfiguration : ISchemaConfiguration
{
    private readonly SchemaData _data;

    public SchemaConfiguration(SchemaData data)
    {
        _data = data ?? throw new ArgumentNullException(nameof(data));
    }

    /// <inheritdoc />
    public string Name => _data.Name ?? throw new InvalidOperationException("Schema name is required");

    /// <inheritdoc />
    public string Version => _data.Version ?? "1.0.0";

    /// <inheritdoc />
    public IReadOnlyList<IFieldConfiguration> Fields =>
        _data.Fields?.Select(f => new FieldConfiguration(f)).ToArray() ?? Array.Empty<IFieldConfiguration>();

    /// <inheritdoc />
    public IReadOnlyDictionary<string, object>? Metadata => _data.Metadata;

    /// <inheritdoc />
    public ISchema ToSchema()
    {
        var columns = Fields.Select(field => new ColumnDefinition
        {
            Name = field.Name,
            DataType = ParseDataType(field.Type),
            IsNullable = !field.Required,
            Description = field.Metadata?.TryGetValue("description", out var desc) == true ? desc?.ToString() : null,
            Metadata = field.Metadata?.ToImmutableDictionary() ?? ImmutableDictionary<string, object>.Empty
        }).ToArray();

        return Schema.GetOrCreate(columns);
    }

    /// <summary>
    /// Validates this schema configuration.
    /// </summary>
    public ConfigurationValidationResult Validate()
    {
        var errors = new List<string>();
        var warnings = new List<string>();

        if (string.IsNullOrWhiteSpace(Name))
            errors.Add("Schema name is required");

        if (Fields.Count == 0)
            errors.Add("Schema must contain at least one field");

        // Validate field names are unique
        var fieldNames = Fields.Select(f => f.Name).ToList();
        var duplicateNames = fieldNames.GroupBy(n => n).Where(g => g.Count() > 1).Select(g => g.Key);
        foreach (var name in duplicateNames)
            errors.Add($"Duplicate field name: {name}");

        // Validate field types
        foreach (var field in Fields)
        {
            if (string.IsNullOrWhiteSpace(field.Name))
                errors.Add("Field name is required");

            try
            {
                ParseDataType(field.Type);
            }
            catch (ArgumentException ex)
            {
                errors.Add($"Invalid field type '{field.Type}' for field '{field.Name}': {ex.Message}");
            }
        }

        return errors.Count > 0 
            ? ConfigurationValidationResult.Failure(errors.ToArray())
            : warnings.Count > 0 
                ? ConfigurationValidationResult.SuccessWithWarnings(warnings.ToArray())
                : ConfigurationValidationResult.Success();
    }

    private static Type ParseDataType(string typeName)
    {
        return typeName?.ToLowerInvariant() switch
        {
            "int32" or "int" => typeof(int),
            "int64" or "long" => typeof(long),
            "float32" or "float" => typeof(float),
            "float64" or "double" => typeof(double),
            "decimal" => typeof(decimal),
            "bool" or "boolean" => typeof(bool),
            "string" => typeof(string),
            "datetime" => typeof(DateTime),
            "datetimeoffset" => typeof(DateTimeOffset),
            "timespan" => typeof(TimeSpan),
            "guid" => typeof(Guid),
            "byte" => typeof(byte),
            "sbyte" => typeof(sbyte),
            "int16" or "short" => typeof(short),
            "uint16" or "ushort" => typeof(ushort),
            "uint32" or "uint" => typeof(uint),
            "uint64" or "ulong" => typeof(ulong),
            _ => throw new ArgumentException($"Unknown data type: {typeName}")
        };
    }
}