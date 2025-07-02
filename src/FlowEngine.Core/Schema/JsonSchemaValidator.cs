using FlowEngine.Abstractions.Schema;
using Microsoft.Extensions.Logging;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace FlowEngine.Core.Schema;

/// <summary>
/// Implementation of JSON schema validation for plugin configurations.
/// Provides comprehensive validation with detailed error reporting and performance optimization.
/// </summary>
public sealed class JsonSchemaValidator : IJsonSchemaValidator
{
    private readonly ILogger<JsonSchemaValidator> _logger;
    private readonly JsonSerializerOptions _defaultSerializerOptions;

    /// <summary>
    /// Initializes a new instance of the JsonSchemaValidator class.
    /// </summary>
    /// <param name="logger">Logger instance</param>
    public JsonSchemaValidator(ILogger<JsonSchemaValidator> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _defaultSerializerOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true,
            PropertyNameCaseInsensitive = true
        };
    }

    /// <summary>
    /// Validates a JSON document against an embedded schema.
    /// </summary>
    public async Task<JsonSchemaValidationResult> ValidateAsync(
        JsonDocument jsonDocument,
        JsonSchemaDefinition schemaDefinition,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        
        try
        {
            _logger.LogDebug("Starting JSON schema validation");

            var errors = new List<JsonSchemaValidationError>();
            var warnings = new List<JsonSchemaValidationWarning>();

            // Validate the document root against the schema
            await ValidateElement(jsonDocument.RootElement, schemaDefinition, "$", errors, warnings, cancellationToken);

            stopwatch.Stop();

            var result = errors.Count == 0
                ? JsonSchemaValidationResult.Success(stopwatch.Elapsed, schemaDefinition)
                : JsonSchemaValidationResult.Failure(
                    errors.ToImmutableArray(),
                    stopwatch.Elapsed,
                    warnings.ToImmutableArray(),
                    schemaDefinition);

            _logger.LogDebug("JSON schema validation completed in {ElapsedMs}ms with {ErrorCount} errors and {WarningCount} warnings",
                stopwatch.ElapsedMilliseconds, errors.Count, warnings.Count);

            return result;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogError(ex, "JSON schema validation failed after {ElapsedMs}ms", stopwatch.ElapsedMilliseconds);

            return JsonSchemaValidationResult.Failure(
                ImmutableArray.Create(new JsonSchemaValidationError
                {
                    Code = "VALIDATION_EXCEPTION",
                    Message = $"Schema validation failed with exception: {ex.Message}",
                    Path = "$"
                }),
                stopwatch.Elapsed,
                schema: schemaDefinition);
        }
    }

    /// <summary>
    /// Validates a JSON string against an embedded schema.
    /// </summary>
    public async Task<JsonSchemaValidationResult> ValidateAsync(
        string jsonString,
        JsonSchemaDefinition schemaDefinition,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(jsonString))
        {
            return JsonSchemaValidationResult.Failure(
                ImmutableArray.Create(new JsonSchemaValidationError
                {
                    Code = "EMPTY_JSON",
                    Message = "JSON string is null or empty",
                    Path = "$"
                }),
                TimeSpan.Zero);
        }

        try
        {
            using var jsonDocument = JsonDocument.Parse(jsonString);
            return await ValidateAsync(jsonDocument, schemaDefinition, cancellationToken);
        }
        catch (JsonException ex)
        {
            return JsonSchemaValidationResult.Failure(
                ImmutableArray.Create(new JsonSchemaValidationError
                {
                    Code = "INVALID_JSON",
                    Message = $"Invalid JSON: {ex.Message}",
                    Path = "$"
                }),
                TimeSpan.Zero);
        }
    }

    /// <summary>
    /// Validates an object against an embedded schema by serializing to JSON first.
    /// </summary>
    public async Task<JsonSchemaValidationResult> ValidateObjectAsync<T>(
        T obj,
        JsonSchemaDefinition schemaDefinition,
        JsonSerializerOptions? serializerOptions = null,
        CancellationToken cancellationToken = default)
    {
        if (obj == null)
        {
            return JsonSchemaValidationResult.Failure(
                ImmutableArray.Create(new JsonSchemaValidationError
                {
                    Code = "NULL_OBJECT",
                    Message = "Object to validate is null",
                    Path = "$"
                }),
                TimeSpan.Zero);
        }

        try
        {
            var options = serializerOptions ?? _defaultSerializerOptions;
            var jsonString = JsonSerializer.Serialize(obj, options);
            return await ValidateAsync(jsonString, schemaDefinition, cancellationToken);
        }
        catch (Exception ex)
        {
            return JsonSchemaValidationResult.Failure(
                ImmutableArray.Create(new JsonSchemaValidationError
                {
                    Code = "SERIALIZATION_ERROR",
                    Message = $"Failed to serialize object for validation: {ex.Message}",
                    Path = "$"
                }),
                TimeSpan.Zero);
        }
    }

    /// <summary>
    /// Creates a schema definition from a .NET type using reflection and attributes.
    /// </summary>
    public JsonSchemaDefinition CreateSchemaFromType<T>(JsonSchemaGenerationOptions? options = null)
    {
        return CreateSchemaFromType(typeof(T), options ?? new JsonSchemaGenerationOptions());
    }

    /// <summary>
    /// Validates that a schema definition itself is valid JSON Schema.
    /// </summary>
    public async Task<JsonSchemaValidationResult> ValidateSchemaAsync(
        JsonSchemaDefinition schemaDefinition,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var errors = new List<JsonSchemaValidationError>();

        // Basic schema validation
        if (string.IsNullOrWhiteSpace(schemaDefinition.Type))
        {
            errors.Add(new JsonSchemaValidationError
            {
                Code = "MISSING_TYPE",
                Message = "Schema must specify a type",
                Path = "$.type"
            });
        }

        // Validate numeric constraints
        if (schemaDefinition.Minimum.HasValue && schemaDefinition.Maximum.HasValue)
        {
            if (schemaDefinition.Minimum.Value > schemaDefinition.Maximum.Value)
            {
                errors.Add(new JsonSchemaValidationError
                {
                    Code = "INVALID_NUMERIC_RANGE",
                    Message = "Minimum value cannot be greater than maximum value",
                    Path = "$.minimum"
                });
            }
        }

        // Validate string constraints
        if (schemaDefinition.MinLength.HasValue && schemaDefinition.MaxLength.HasValue)
        {
            if (schemaDefinition.MinLength.Value > schemaDefinition.MaxLength.Value)
            {
                errors.Add(new JsonSchemaValidationError
                {
                    Code = "INVALID_LENGTH_RANGE",
                    Message = "Minimum length cannot be greater than maximum length",
                    Path = "$.minLength"
                });
            }
        }

        // Validate pattern if specified
        if (!string.IsNullOrWhiteSpace(schemaDefinition.Pattern))
        {
            try
            {
                _ = new Regex(schemaDefinition.Pattern);
            }
            catch (ArgumentException)
            {
                errors.Add(new JsonSchemaValidationError
                {
                    Code = "INVALID_PATTERN",
                    Message = "Pattern is not a valid regular expression",
                    Path = "$.pattern"
                });
            }
        }

        stopwatch.Stop();

        return errors.Count == 0
            ? JsonSchemaValidationResult.Success(stopwatch.Elapsed)
            : JsonSchemaValidationResult.Failure(errors.ToImmutableArray(), stopwatch.Elapsed);
    }

    /// <summary>
    /// Creates a schema definition from a .NET type using reflection and attributes.
    /// </summary>
    private JsonSchemaDefinition CreateSchemaFromType(Type type, JsonSchemaGenerationOptions options)
    {
        var schemaAttr = type.GetCustomAttribute<JsonSchemaAttribute>();
        var pluginAttr = type.GetCustomAttribute<PluginConfigurationSchemaAttribute>();

        var title = pluginAttr?.PluginType ?? type.Name;
        var description = schemaAttr?.Description ?? $"Schema for {type.Name}";

        if (type.IsClass && type != typeof(string))
        {
            // Object schema
            var properties = ImmutableDictionary.CreateBuilder<string, JsonSchemaDefinition>();
            var required = new List<string>();

            foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (property.CanRead)
                {
                    var propertySchema = CreateSchemaFromProperty(property, options);
                    var propertyName = GetPropertyName(property, options);
                    
                    properties.Add(propertyName, propertySchema);

                    var propertySchemaAttr = property.GetCustomAttribute<JsonSchemaAttribute>();
                    if (propertySchemaAttr?.Required == true || 
                        (property.PropertyType.IsValueType && Nullable.GetUnderlyingType(property.PropertyType) == null))
                    {
                        required.Add(propertyName);
                    }
                }
            }

            return JsonSchemaDefinition.CreateObjectSchema(
                title,
                description,
                properties.ToImmutable(),
                required.ToImmutableArray());
        }
        else
        {
            // Primitive schema
            return CreateSchemaFromPrimitiveType(type, schemaAttr);
        }
    }

    /// <summary>
    /// Creates a schema definition for a property.
    /// </summary>
    private JsonSchemaDefinition CreateSchemaFromProperty(PropertyInfo property, JsonSchemaGenerationOptions options)
    {
        var attr = property.GetCustomAttribute<JsonSchemaAttribute>();
        var filePathAttr = property.GetCustomAttribute<FilePathSchemaAttribute>();
        var performanceAttr = property.GetCustomAttribute<PerformanceSchemaAttribute>();
        var formatAttr = property.GetCustomAttribute<FormatSchemaAttribute>();

        // Use specific attribute if available
        if (filePathAttr != null)
        {
            return CreateFilePathSchema(filePathAttr);
        }
        if (performanceAttr != null)
        {
            return CreatePerformanceSchema(performanceAttr);
        }
        if (formatAttr != null)
        {
            return CreateFormatSchema(formatAttr);
        }

        // Use property type to determine schema
        var propertyType = property.PropertyType;
        var underlyingType = Nullable.GetUnderlyingType(propertyType) ?? propertyType;

        return CreateSchemaFromPrimitiveType(underlyingType, attr);
    }

    /// <summary>
    /// Creates a schema definition for primitive types.
    /// </summary>
    private JsonSchemaDefinition CreateSchemaFromPrimitiveType(Type type, JsonSchemaAttribute? attr)
    {
        if (type == typeof(string))
        {
            return JsonSchemaDefinition.CreateStringSchema(
                attr?.Description,
                attr?.Pattern,
                attr?.MinLength,
                attr?.MaxLength);
        }
        else if (type == typeof(bool))
        {
            return JsonSchemaDefinition.CreateBooleanSchema(attr?.Description);
        }
        else if (IsNumericType(type))
        {
            return JsonSchemaDefinition.CreateNumberSchema(
                attr?.Description,
                attr?.Minimum.HasValue ? (decimal)attr.Minimum.Value : null,
                attr?.Maximum.HasValue ? (decimal)attr.Maximum.Value : null);
        }
        else if (type.IsEnum)
        {
            var enumValues = Enum.GetNames(type)
                .Select(name => JsonSerializer.SerializeToElement(name))
                .ToImmutableArray();

            return new JsonSchemaDefinition
            {
                Type = "string",
                Description = attr?.Description ?? $"Enumeration of {type.Name} values",
                Enum = enumValues
            };
        }

        // Default to object for complex types
        return new JsonSchemaDefinition
        {
            Type = "object",
            Description = attr?.Description ?? $"Complex type: {type.Name}"
        };
    }

    /// <summary>
    /// Creates a schema definition for file path attributes.
    /// </summary>
    private JsonSchemaDefinition CreateFilePathSchema(FilePathSchemaAttribute attr)
    {
        return new JsonSchemaDefinition
        {
            Type = "string",
            Description = attr.Description,
            Pattern = attr.Pattern,
            MinLength = 1,
            Annotations = ImmutableDictionary<string, JsonElement>.Empty
                .Add("mustExist", JsonSerializer.SerializeToElement(attr.MustExist))
                .Add("allowedExtensions", JsonSerializer.SerializeToElement(attr.AllowedExtensions))
        };
    }

    /// <summary>
    /// Creates a schema definition for performance attributes.
    /// </summary>
    private JsonSchemaDefinition CreatePerformanceSchema(PerformanceSchemaAttribute attr)
    {
        var annotations = ImmutableDictionary<string, JsonElement>.Empty;
        
        if (attr.RecommendedMin.HasValue)
            annotations = annotations.Add("recommendedMin", JsonSerializer.SerializeToElement(attr.RecommendedMin.Value));
        if (attr.RecommendedMax.HasValue)
            annotations = annotations.Add("recommendedMax", JsonSerializer.SerializeToElement(attr.RecommendedMax.Value));
        if (!string.IsNullOrWhiteSpace(attr.PerformanceNote))
            annotations = annotations.Add("performanceNote", JsonSerializer.SerializeToElement(attr.PerformanceNote));

        return new JsonSchemaDefinition
        {
            Type = "number",
            Description = attr.Description,
            Minimum = attr.Minimum.HasValue ? (decimal)attr.Minimum.Value : null,
            Maximum = attr.Maximum.HasValue ? (decimal)attr.Maximum.Value : null,
            Annotations = annotations
        };
    }

    /// <summary>
    /// Creates a schema definition for format attributes.
    /// </summary>
    private JsonSchemaDefinition CreateFormatSchema(FormatSchemaAttribute attr)
    {
        return new JsonSchemaDefinition
        {
            Type = "string",
            Description = attr.Description,
            Annotations = ImmutableDictionary<string, JsonElement>.Empty
                .Add("format", JsonSerializer.SerializeToElement(attr.Format!))
        };
    }

    /// <summary>
    /// Gets the JSON property name for a .NET property.
    /// </summary>
    private string GetPropertyName(PropertyInfo property, JsonSchemaGenerationOptions options)
    {
        var jsonPropertyName = property.GetCustomAttribute<System.Text.Json.Serialization.JsonPropertyNameAttribute>()?.Name;
        if (!string.IsNullOrWhiteSpace(jsonPropertyName))
            return jsonPropertyName;

        return options.PropertyNamingPolicy?.ConvertName(property.Name) ?? property.Name;
    }

    /// <summary>
    /// Checks if a type is numeric.
    /// </summary>
    private static bool IsNumericType(Type type)
    {
        return type == typeof(byte) || type == typeof(sbyte) ||
               type == typeof(short) || type == typeof(ushort) ||
               type == typeof(int) || type == typeof(uint) ||
               type == typeof(long) || type == typeof(ulong) ||
               type == typeof(float) || type == typeof(double) ||
               type == typeof(decimal);
    }

    /// <summary>
    /// Validates a JSON element against a schema definition.
    /// </summary>
    private async Task ValidateElement(
        JsonElement element,
        JsonSchemaDefinition schema,
        string path,
        List<JsonSchemaValidationError> errors,
        List<JsonSchemaValidationWarning> warnings,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // Validate type
        if (!IsValidType(element, schema.Type))
        {
            errors.Add(new JsonSchemaValidationError
            {
                Code = "TYPE_MISMATCH",
                Message = $"Expected type '{schema.Type}', but got '{GetJsonElementType(element)}'",
                Path = path,
                ActualValue = element,
                ExpectedValue = JsonSerializer.SerializeToElement(schema.Type)
            });
            return; // Don't continue validation if type is wrong
        }

        // Type-specific validation
        switch (schema.Type?.ToLowerInvariant())
        {
            case "object":
                await ValidateObject(element, schema, path, errors, warnings, cancellationToken);
                break;
            case "array":
                await ValidateArray(element, schema, path, errors, warnings, cancellationToken);
                break;
            case "string":
                ValidateString(element, schema, path, errors, warnings);
                break;
            case "number":
            case "integer":
                ValidateNumber(element, schema, path, errors, warnings);
                break;
        }

        // Validate enum if specified
        if (schema.Enum?.Length > 0)
        {
            var isValidEnum = schema.Enum.Value.Any(enumValue => 
                JsonElement.DeepEquals(element, enumValue));
            
            if (!isValidEnum)
            {
                errors.Add(new JsonSchemaValidationError
                {
                    Code = "ENUM_VIOLATION",
                    Message = $"Value is not one of the allowed enumeration values",
                    Path = path,
                    ActualValue = element
                });
            }
        }
    }

    /// <summary>
    /// Validates an object element against schema.
    /// </summary>
    private async Task ValidateObject(
        JsonElement element,
        JsonSchemaDefinition schema,
        string path,
        List<JsonSchemaValidationError> errors,
        List<JsonSchemaValidationWarning> warnings,
        CancellationToken cancellationToken)
    {
        if (element.ValueKind != JsonValueKind.Object)
            return;

        var properties = schema.Properties ?? ImmutableDictionary<string, JsonSchemaDefinition>.Empty;
        var required = schema.Required ?? ImmutableArray<string>.Empty;

        // Check required properties
        foreach (var requiredProp in required)
        {
            if (!element.TryGetProperty(requiredProp, out _))
            {
                errors.Add(new JsonSchemaValidationError
                {
                    Code = "MISSING_REQUIRED_PROPERTY",
                    Message = $"Required property '{requiredProp}' is missing",
                    Path = path,
                    ExpectedValue = JsonSerializer.SerializeToElement(requiredProp)
                });
            }
        }

        // Validate each property
        foreach (var jsonProperty in element.EnumerateObject())
        {
            if (properties.TryGetValue(jsonProperty.Name, out var propertySchema))
            {
                await ValidateElement(
                    jsonProperty.Value,
                    propertySchema,
                    $"{path}.{jsonProperty.Name}",
                    errors,
                    warnings,
                    cancellationToken);
            }
            else if (schema.AdditionalProperties == false)
            {
                warnings.Add(new JsonSchemaValidationWarning
                {
                    Code = "ADDITIONAL_PROPERTY",
                    Message = $"Additional property '{jsonProperty.Name}' is not defined in schema",
                    Path = $"{path}.{jsonProperty.Name}"
                });
            }
        }
    }

    /// <summary>
    /// Validates an array element against schema.
    /// </summary>
    private async Task ValidateArray(
        JsonElement element,
        JsonSchemaDefinition schema,
        string path,
        List<JsonSchemaValidationError> errors,
        List<JsonSchemaValidationWarning> warnings,
        CancellationToken cancellationToken)
    {
        if (element.ValueKind != JsonValueKind.Array)
            return;

        var arrayLength = element.GetArrayLength();

        // Validate length constraints
        if (schema.MinLength.HasValue && arrayLength < schema.MinLength.Value)
        {
            errors.Add(new JsonSchemaValidationError
            {
                Code = "ARRAY_TOO_SHORT",
                Message = $"Array length {arrayLength} is less than minimum {schema.MinLength.Value}",
                Path = path,
                ActualValue = JsonSerializer.SerializeToElement(arrayLength),
                ExpectedValue = JsonSerializer.SerializeToElement(schema.MinLength.Value)
            });
        }

        if (schema.MaxLength.HasValue && arrayLength > schema.MaxLength.Value)
        {
            errors.Add(new JsonSchemaValidationError
            {
                Code = "ARRAY_TOO_LONG",
                Message = $"Array length {arrayLength} exceeds maximum {schema.MaxLength.Value}",
                Path = path,
                ActualValue = JsonSerializer.SerializeToElement(arrayLength),
                ExpectedValue = JsonSerializer.SerializeToElement(schema.MaxLength.Value)
            });
        }

        // Validate items if schema is specified
        if (schema.Items != null)
        {
            int index = 0;
            foreach (var item in element.EnumerateArray())
            {
                await ValidateElement(
                    item,
                    schema.Items,
                    $"{path}[{index}]",
                    errors,
                    warnings,
                    cancellationToken);
                index++;
            }
        }
    }

    /// <summary>
    /// Validates a string element against schema.
    /// </summary>
    private void ValidateString(
        JsonElement element,
        JsonSchemaDefinition schema,
        string path,
        List<JsonSchemaValidationError> errors,
        List<JsonSchemaValidationWarning> warnings)
    {
        if (element.ValueKind != JsonValueKind.String)
            return;

        var stringValue = element.GetString() ?? string.Empty;

        // Validate length constraints
        if (schema.MinLength.HasValue && stringValue.Length < schema.MinLength.Value)
        {
            errors.Add(new JsonSchemaValidationError
            {
                Code = "STRING_TOO_SHORT",
                Message = $"String length {stringValue.Length} is less than minimum {schema.MinLength.Value}",
                Path = path,
                ActualValue = element,
                ExpectedValue = JsonSerializer.SerializeToElement(schema.MinLength.Value)
            });
        }

        if (schema.MaxLength.HasValue && stringValue.Length > schema.MaxLength.Value)
        {
            errors.Add(new JsonSchemaValidationError
            {
                Code = "STRING_TOO_LONG",
                Message = $"String length {stringValue.Length} exceeds maximum {schema.MaxLength.Value}",
                Path = path,
                ActualValue = element,
                ExpectedValue = JsonSerializer.SerializeToElement(schema.MaxLength.Value)
            });
        }

        // Validate pattern if specified
        if (!string.IsNullOrWhiteSpace(schema.Pattern))
        {
            try
            {
                var regex = new Regex(schema.Pattern);
                if (!regex.IsMatch(stringValue))
                {
                    errors.Add(new JsonSchemaValidationError
                    {
                        Code = "PATTERN_MISMATCH",
                        Message = $"String does not match required pattern: {schema.Pattern}",
                        Path = path,
                        ActualValue = element,
                        ExpectedValue = JsonSerializer.SerializeToElement(schema.Pattern)
                    });
                }
            }
            catch (ArgumentException ex)
            {
                warnings.Add(new JsonSchemaValidationWarning
                {
                    Code = "INVALID_PATTERN",
                    Message = $"Schema pattern is invalid: {ex.Message}",
                    Path = path
                });
            }
        }
    }

    /// <summary>
    /// Validates a number element against schema.
    /// </summary>
    private void ValidateNumber(
        JsonElement element,
        JsonSchemaDefinition schema,
        string path,
        List<JsonSchemaValidationError> errors,
        List<JsonSchemaValidationWarning> warnings)
    {
        if (element.ValueKind != JsonValueKind.Number)
            return;

        if (!element.TryGetDecimal(out var numericValue))
            return;

        // Validate minimum constraint
        if (schema.Minimum.HasValue && numericValue < schema.Minimum.Value)
        {
            errors.Add(new JsonSchemaValidationError
            {
                Code = "NUMBER_TOO_SMALL",
                Message = $"Number {numericValue} is less than minimum {schema.Minimum.Value}",
                Path = path,
                ActualValue = element,
                ExpectedValue = JsonSerializer.SerializeToElement(schema.Minimum.Value)
            });
        }

        // Validate maximum constraint
        if (schema.Maximum.HasValue && numericValue > schema.Maximum.Value)
        {
            errors.Add(new JsonSchemaValidationError
            {
                Code = "NUMBER_TOO_LARGE",
                Message = $"Number {numericValue} exceeds maximum {schema.Maximum.Value}",
                Path = path,
                ActualValue = element,
                ExpectedValue = JsonSerializer.SerializeToElement(schema.Maximum.Value)
            });
        }
    }

    /// <summary>
    /// Checks if a JSON element matches the expected type.
    /// </summary>
    private bool IsValidType(JsonElement element, string? expectedType)
    {
        if (string.IsNullOrWhiteSpace(expectedType))
            return true;

        return expectedType.ToLowerInvariant() switch
        {
            "object" => element.ValueKind == JsonValueKind.Object,
            "array" => element.ValueKind == JsonValueKind.Array,
            "string" => element.ValueKind == JsonValueKind.String,
            "number" => element.ValueKind == JsonValueKind.Number,
            "integer" => element.ValueKind == JsonValueKind.Number && IsInteger(element),
            "boolean" => element.ValueKind == JsonValueKind.True || element.ValueKind == JsonValueKind.False,
            "null" => element.ValueKind == JsonValueKind.Null,
            _ => true
        };
    }

    /// <summary>
    /// Checks if a numeric JSON element represents an integer.
    /// </summary>
    private bool IsInteger(JsonElement element)
    {
        return element.TryGetInt64(out _);
    }

    /// <summary>
    /// Gets the string representation of a JSON element type.
    /// </summary>
    private string GetJsonElementType(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.Object => "object",
            JsonValueKind.Array => "array",
            JsonValueKind.String => "string",
            JsonValueKind.Number => "number",
            JsonValueKind.True or JsonValueKind.False => "boolean",
            JsonValueKind.Null => "null",
            _ => "unknown"
        };
    }
}