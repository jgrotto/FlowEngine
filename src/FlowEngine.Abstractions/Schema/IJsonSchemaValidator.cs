using System.Collections.Immutable;
using System.Text.Json;

namespace FlowEngine.Abstractions.Schema;

/// <summary>
/// Interface for JSON schema validation within plugin configurations.
/// Provides embedded schema validation with detailed error reporting.
/// </summary>
public interface IJsonSchemaValidator
{
    /// <summary>
    /// Validates a JSON document against an embedded schema.
    /// </summary>
    /// <param name="jsonDocument">JSON document to validate</param>
    /// <param name="schemaDefinition">Schema definition in JSON Schema format</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Validation result with detailed error information</returns>
    Task<JsonSchemaValidationResult> ValidateAsync(
        JsonDocument jsonDocument,
        JsonSchemaDefinition schemaDefinition,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates a JSON string against an embedded schema.
    /// </summary>
    /// <param name="jsonString">JSON string to validate</param>
    /// <param name="schemaDefinition">Schema definition in JSON Schema format</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Validation result with detailed error information</returns>
    Task<JsonSchemaValidationResult> ValidateAsync(
        string jsonString,
        JsonSchemaDefinition schemaDefinition,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates an object against an embedded schema by serializing to JSON first.
    /// </summary>
    /// <param name="obj">Object to validate</param>
    /// <param name="schemaDefinition">Schema definition in JSON Schema format</param>
    /// <param name="serializerOptions">JSON serializer options</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Validation result with detailed error information</returns>
    Task<JsonSchemaValidationResult> ValidateObjectAsync<T>(
        T obj,
        JsonSchemaDefinition schemaDefinition,
        JsonSerializerOptions? serializerOptions = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a schema definition from a .NET type using reflection and attributes.
    /// </summary>
    /// <typeparam name="T">Type to create schema for</typeparam>
    /// <param name="options">Schema generation options</param>
    /// <returns>Generated JSON schema definition</returns>
    JsonSchemaDefinition CreateSchemaFromType<T>(JsonSchemaGenerationOptions? options = null);

    /// <summary>
    /// Validates that a schema definition itself is valid JSON Schema.
    /// </summary>
    /// <param name="schemaDefinition">Schema definition to validate</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Schema validation result</returns>
    Task<JsonSchemaValidationResult> ValidateSchemaAsync(
        JsonSchemaDefinition schemaDefinition,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Represents a JSON Schema definition with embedded validation rules.
/// </summary>
public sealed record JsonSchemaDefinition
{
    /// <summary>
    /// Gets the JSON Schema version (e.g., "https://json-schema.org/draft/2020-12/schema").
    /// </summary>
    public string? Schema { get; init; } = "https://json-schema.org/draft/2020-12/schema";

    /// <summary>
    /// Gets the unique identifier for this schema.
    /// </summary>
    public string? Id { get; init; }

    /// <summary>
    /// Gets the schema title.
    /// </summary>
    public string? Title { get; init; }

    /// <summary>
    /// Gets the schema description.
    /// </summary>
    public string? Description { get; init; }

    /// <summary>
    /// Gets the schema type (object, array, string, number, etc.).
    /// </summary>
    public string? Type { get; init; }

    /// <summary>
    /// Gets the properties definition for object schemas.
    /// </summary>
    public ImmutableDictionary<string, JsonSchemaDefinition>? Properties { get; init; }

    /// <summary>
    /// Gets the required property names for object schemas.
    /// </summary>
    public ImmutableArray<string>? Required { get; init; }

    /// <summary>
    /// Gets additional properties configuration.
    /// </summary>
    public bool? AdditionalProperties { get; init; }

    /// <summary>
    /// Gets the items schema for array types.
    /// </summary>
    public JsonSchemaDefinition? Items { get; init; }

    /// <summary>
    /// Gets the minimum value for numeric types.
    /// </summary>
    public decimal? Minimum { get; init; }

    /// <summary>
    /// Gets the maximum value for numeric types.
    /// </summary>
    public decimal? Maximum { get; init; }

    /// <summary>
    /// Gets the minimum length for string types.
    /// </summary>
    public int? MinLength { get; init; }

    /// <summary>
    /// Gets the maximum length for string types.
    /// </summary>
    public int? MaxLength { get; init; }

    /// <summary>
    /// Gets the pattern (regex) for string validation.
    /// </summary>
    public string? Pattern { get; init; }

    /// <summary>
    /// Gets the enumeration of allowed values.
    /// </summary>
    public ImmutableArray<JsonElement>? Enum { get; init; }

    /// <summary>
    /// Gets the default value for this property.
    /// </summary>
    public JsonElement? Default { get; init; }

    /// <summary>
    /// Gets examples for this property.
    /// </summary>
    public ImmutableArray<JsonElement>? Examples { get; init; }

    /// <summary>
    /// Gets custom validation annotations.
    /// </summary>
    public ImmutableDictionary<string, JsonElement>? Annotations { get; init; }

    /// <summary>
    /// Creates a simple object schema definition.
    /// </summary>
    /// <param name="title">Schema title</param>
    /// <param name="description">Schema description</param>
    /// <param name="properties">Object properties</param>
    /// <param name="required">Required property names</param>
    /// <returns>Object schema definition</returns>
    public static JsonSchemaDefinition CreateObjectSchema(
        string title,
        string? description = null,
        ImmutableDictionary<string, JsonSchemaDefinition>? properties = null,
        ImmutableArray<string>? required = null)
    {
        return new JsonSchemaDefinition
        {
            Title = title,
            Description = description,
            Type = "object",
            Properties = properties ?? ImmutableDictionary<string, JsonSchemaDefinition>.Empty,
            Required = required,
            AdditionalProperties = false
        };
    }

    /// <summary>
    /// Creates a simple string schema definition.
    /// </summary>
    /// <param name="description">Property description</param>
    /// <param name="pattern">Validation pattern (regex)</param>
    /// <param name="minLength">Minimum length</param>
    /// <param name="maxLength">Maximum length</param>
    /// <returns>String schema definition</returns>
    public static JsonSchemaDefinition CreateStringSchema(
        string? description = null,
        string? pattern = null,
        int? minLength = null,
        int? maxLength = null)
    {
        return new JsonSchemaDefinition
        {
            Type = "string",
            Description = description,
            Pattern = pattern,
            MinLength = minLength,
            MaxLength = maxLength
        };
    }

    /// <summary>
    /// Creates a simple number schema definition.
    /// </summary>
    /// <param name="description">Property description</param>
    /// <param name="minimum">Minimum value</param>
    /// <param name="maximum">Maximum value</param>
    /// <returns>Number schema definition</returns>
    public static JsonSchemaDefinition CreateNumberSchema(
        string? description = null,
        decimal? minimum = null,
        decimal? maximum = null)
    {
        return new JsonSchemaDefinition
        {
            Type = "number",
            Description = description,
            Minimum = minimum,
            Maximum = maximum
        };
    }

    /// <summary>
    /// Creates a simple boolean schema definition.
    /// </summary>
    /// <param name="description">Property description</param>
    /// <returns>Boolean schema definition</returns>
    public static JsonSchemaDefinition CreateBooleanSchema(string? description = null)
    {
        return new JsonSchemaDefinition
        {
            Type = "boolean",
            Description = description
        };
    }
}

/// <summary>
/// Result of JSON schema validation.
/// </summary>
public sealed record JsonSchemaValidationResult
{
    /// <summary>
    /// Gets whether the validation passed.
    /// </summary>
    public required bool IsValid { get; init; }

    /// <summary>
    /// Gets validation errors found.
    /// </summary>
    public required ImmutableArray<JsonSchemaValidationError> Errors { get; init; }

    /// <summary>
    /// Gets validation warnings found.
    /// </summary>
    public required ImmutableArray<JsonSchemaValidationWarning> Warnings { get; init; }

    /// <summary>
    /// Gets the time taken for validation.
    /// </summary>
    public required TimeSpan ValidationTime { get; init; }

    /// <summary>
    /// Gets the schema that was used for validation.
    /// </summary>
    public JsonSchemaDefinition? Schema { get; init; }

    /// <summary>
    /// Creates a successful validation result.
    /// </summary>
    /// <param name="validationTime">Time taken for validation</param>
    /// <param name="schema">Schema used for validation</param>
    /// <returns>Successful validation result</returns>
    public static JsonSchemaValidationResult Success(TimeSpan validationTime, JsonSchemaDefinition? schema = null)
    {
        return new JsonSchemaValidationResult
        {
            IsValid = true,
            Errors = ImmutableArray<JsonSchemaValidationError>.Empty,
            Warnings = ImmutableArray<JsonSchemaValidationWarning>.Empty,
            ValidationTime = validationTime,
            Schema = schema
        };
    }

    /// <summary>
    /// Creates a failed validation result.
    /// </summary>
    /// <param name="errors">Validation errors</param>
    /// <param name="validationTime">Time taken for validation</param>
    /// <param name="warnings">Validation warnings</param>
    /// <param name="schema">Schema used for validation</param>
    /// <returns>Failed validation result</returns>
    public static JsonSchemaValidationResult Failure(
        ImmutableArray<JsonSchemaValidationError> errors,
        TimeSpan validationTime,
        ImmutableArray<JsonSchemaValidationWarning>? warnings = null,
        JsonSchemaDefinition? schema = null)
    {
        return new JsonSchemaValidationResult
        {
            IsValid = false,
            Errors = errors,
            Warnings = warnings ?? ImmutableArray<JsonSchemaValidationWarning>.Empty,
            ValidationTime = validationTime,
            Schema = schema
        };
    }
}

/// <summary>
/// Represents a JSON schema validation error.
/// </summary>
public sealed record JsonSchemaValidationError
{
    /// <summary>
    /// Gets the error code.
    /// </summary>
    public required string Code { get; init; }

    /// <summary>
    /// Gets the error message.
    /// </summary>
    public required string Message { get; init; }

    /// <summary>
    /// Gets the JSON path where the error occurred.
    /// </summary>
    public required string Path { get; init; }

    /// <summary>
    /// Gets the schema path that caused the error.
    /// </summary>
    public string? SchemaPath { get; init; }

    /// <summary>
    /// Gets the actual value that caused the error.
    /// </summary>
    public JsonElement? ActualValue { get; init; }

    /// <summary>
    /// Gets the expected value or constraint.
    /// </summary>
    public JsonElement? ExpectedValue { get; init; }

    /// <summary>
    /// Gets additional error context.
    /// </summary>
    public ImmutableDictionary<string, JsonElement>? Context { get; init; }
}

/// <summary>
/// Represents a JSON schema validation warning.
/// </summary>
public sealed record JsonSchemaValidationWarning
{
    /// <summary>
    /// Gets the warning code.
    /// </summary>
    public required string Code { get; init; }

    /// <summary>
    /// Gets the warning message.
    /// </summary>
    public required string Message { get; init; }

    /// <summary>
    /// Gets the JSON path where the warning occurred.
    /// </summary>
    public required string Path { get; init; }

    /// <summary>
    /// Gets additional warning context.
    /// </summary>
    public ImmutableDictionary<string, JsonElement>? Context { get; init; }
}

/// <summary>
/// Options for JSON schema generation from .NET types.
/// </summary>
public sealed record JsonSchemaGenerationOptions
{
    /// <summary>
    /// Gets whether to include descriptions from XML documentation.
    /// </summary>
    public bool IncludeXmlDocumentation { get; init; } = true;

    /// <summary>
    /// Gets whether to treat nullable reference types as optional.
    /// </summary>
    public bool NullableReferencesOptional { get; init; } = true;

    /// <summary>
    /// Gets whether to include example values from attributes.
    /// </summary>
    public bool IncludeExamples { get; init; } = true;

    /// <summary>
    /// Gets whether to include default values from property initializers.
    /// </summary>
    public bool IncludeDefaults { get; init; } = true;

    /// <summary>
    /// Gets the property naming policy to use.
    /// </summary>
    public JsonNamingPolicy? PropertyNamingPolicy { get; init; }

    /// <summary>
    /// Gets custom type converters for schema generation.
    /// </summary>
    public ImmutableDictionary<Type, Func<Type, JsonSchemaDefinition>> CustomConverters { get; init; } = 
        ImmutableDictionary<Type, Func<Type, JsonSchemaDefinition>>.Empty;
}