using System.Reflection;
using NJsonSchema;
using System.Collections.Immutable;
using FlowEngine.Abstractions.Data;

namespace FlowEngine.Abstractions.Plugins;

/// <summary>
/// Base class for plugin validators that use JSON Schema for configuration validation.
/// Provides declarative validation with separation between structural and semantic validation.
/// </summary>
/// <typeparam name="TConfig">The plugin configuration type</typeparam>
public abstract class SchemaValidatedPluginValidator<TConfig> : IPluginValidator
    where TConfig : IPluginConfiguration
{
    private JsonSchema? _cachedSchema;
    private readonly object _schemaLock = new();

    /// <summary>
    /// Gets the JSON Schema for configuration validation.
    /// Schema is loaded once and cached for performance.
    /// </summary>
    protected JsonSchema ConfigurationSchema
    {
        get
        {
            if (_cachedSchema != null) return _cachedSchema;
            
            lock (_schemaLock)
            {
                if (_cachedSchema != null) return _cachedSchema;
                
                var schemaJson = GetSchemaJson();
                _cachedSchema = JsonSchema.FromJsonAsync(schemaJson).GetAwaiter().GetResult();
                return _cachedSchema;
            }
        }
    }

    /// <summary>
    /// Gets the JSON Schema as a string. Override this method to provide the schema.
    /// </summary>
    /// <returns>JSON Schema as a string</returns>
    protected virtual string GetSchemaJson()
    {
        // Try to load from embedded resource first
        var resourceName = GetSchemaResourceName();
        if (!string.IsNullOrEmpty(resourceName))
        {
            var assembly = GetType().Assembly;
            using var stream = assembly.GetManifestResourceStream(resourceName);
            if (stream != null)
            {
                using var reader = new StreamReader(stream);
                return reader.ReadToEnd();
            }
        }

        // Fallback to abstract method
        return GetSchemaJsonContent();
    }

    /// <summary>
    /// Gets the name of the embedded resource containing the JSON Schema.
    /// Return null if not using embedded resources.
    /// </summary>
    /// <returns>The resource name or null</returns>
    protected virtual string? GetSchemaResourceName() => null;

    /// <summary>
    /// Gets the JSON Schema content directly. 
    /// Override this if not using embedded resources.
    /// </summary>
    /// <returns>JSON Schema as a string</returns>
    protected virtual string GetSchemaJsonContent()
    {
        throw new NotImplementedException($"Either override {nameof(GetSchemaResourceName)} to use embedded resources or {nameof(GetSchemaJsonContent)} to provide schema directly.");
    }

    /// <inheritdoc />
    public ValidationResult ValidateConfiguration(IPluginConfiguration configuration)
    {
        if (configuration is not TConfig typedConfig)
        {
            return ValidationResult.Failure(
                new ValidationError
                {
                    Code = "INVALID_CONFIGURATION_TYPE",
                    Message = $"Configuration must be of type {typeof(TConfig).Name}",
                    Severity = ValidationSeverity.Error,
                    Remediation = $"Provide a valid {typeof(TConfig).Name} instance"
                });
        }

        var errors = new List<ValidationError>();

        // 1. Structural validation using JSON Schema
        var structuralErrors = ValidateStructural(typedConfig);
        errors.AddRange(structuralErrors);

        // 2. Semantic validation (business logic)
        if (!structuralErrors.Any(e => e.Severity == ValidationSeverity.Error))
        {
            var semanticValidation = ValidateSemantics(typedConfig);
            if (!semanticValidation.IsValid)
            {
                errors.AddRange(semanticValidation.Errors);
            }
        }

        return errors.Any() ? ValidationResult.Failure(errors.ToArray()) : ValidationResult.Success();
    }

    /// <summary>
    /// Validates the configuration structure using JSON Schema.
    /// </summary>
    /// <param name="configuration">The configuration to validate</param>
    /// <returns>List of structural validation errors</returns>
    protected virtual List<ValidationError> ValidateStructural(TConfig configuration)
    {
        var errors = new List<ValidationError>();

        try
        {
            // Convert configuration to dictionary for JSON Schema validation
            var configDict = ConfigurationToDictionary(configuration);
            
            // Convert dictionary to JSON for validation
            var configJson = System.Text.Json.JsonSerializer.Serialize(configDict);
            
            // Validate against JSON Schema
            var validationErrors = ConfigurationSchema.Validate(configJson);
            
            foreach (var error in validationErrors)
            {
                errors.Add(new ValidationError
                {
                    Code = "SCHEMA_VALIDATION_ERROR",
                    Message = $"{error.Path}: {error.Kind}",
                    Severity = ValidationSeverity.Error,
                    Remediation = $"Fix the configuration property at path: {error.Path}"
                });
            }
        }
        catch (Exception ex)
        {
            errors.Add(new ValidationError
            {
                Code = "SCHEMA_VALIDATION_EXCEPTION",
                Message = $"JSON Schema validation failed: {ex.Message}",
                Severity = ValidationSeverity.Error,
                Remediation = "Check the JSON Schema configuration and ensure it's valid"
            });
        }

        return errors;
    }

    /// <summary>
    /// Validates semantic/business logic requirements.
    /// Override this method to implement plugin-specific validation.
    /// </summary>
    /// <param name="configuration">The validated configuration</param>
    /// <returns>Semantic validation result</returns>
    protected abstract ValidationResult ValidateSemantics(TConfig configuration);

    /// <summary>
    /// Converts configuration object to dictionary for JSON Schema validation.
    /// Override if custom conversion logic is needed.
    /// </summary>
    /// <param name="configuration">The configuration to convert</param>
    /// <returns>Dictionary representation</returns>
    protected virtual IDictionary<string, object?> ConfigurationToDictionary(TConfig configuration)
    {
        var result = new Dictionary<string, object?>();
        var properties = typeof(TConfig).GetProperties(BindingFlags.Public | BindingFlags.Instance);

        foreach (var property in properties)
        {
            if (property.CanRead)
            {
                var value = property.GetValue(configuration);
                
                // Convert complex objects to JSON-serializable forms
                if (value != null)
                {
                    // Skip properties that aren't JSON-serializable or aren't part of the configuration schema
                    if (IsJsonSerializable(value))
                    {
                        result[property.Name] = value;
                    }
                    else if (property.Name == "OutputSchema" && value is ISchema schema)
                    {
                        // Convert schema to a simple representation for validation
                        result[property.Name] = ConvertSchemaToDict(schema);
                    }
                    // Skip other complex types that aren't needed for JSON Schema validation
                }
                else
                {
                    result[property.Name] = value;
                }
            }
        }

        return result;
    }

    /// <summary>
    /// Checks if a value can be JSON serialized.
    /// </summary>
    private static bool IsJsonSerializable(object value)
    {
        var type = value.GetType();
        return type.IsPrimitive || 
               type == typeof(string) || 
               type == typeof(DateTime) || 
               type == typeof(DateTimeOffset) || 
               type == typeof(decimal) ||
               type == typeof(Guid) ||
               type.IsEnum;
    }

    /// <summary>
    /// Converts a schema to a dictionary representation for JSON validation.
    /// </summary>
    private static object ConvertSchemaToDict(ISchema schema)
    {
        return new
        {
            Columns = schema.Columns.Select(c => new
            {
                Name = c.Name,
                Type = c.DataType.Name.ToLowerInvariant(), // Convert Type to string
                Index = c.Index,
                Required = !c.IsNullable
            }).ToArray()
        };
    }

    /// <inheritdoc />
    public virtual SchemaCompatibilityResult ValidateSchemaCompatibility(ISchema? inputSchema, ISchema? outputSchema)
    {
        // Default implementation - override for specific schema compatibility requirements
        return SchemaCompatibilityResult.Success();
    }

    /// <inheritdoc />
    public virtual OptimizationValidationResult ValidateArrayRowOptimization(ISchema schema)
    {
        // Check that schema has sequential field indexes for ArrayRow optimization
        var issues = new List<OptimizationIssue>();
        
        for (int i = 0; i < schema.Columns.Length; i++)
        {
            var column = schema.Columns[i];
            if (column.Index != i)
            {
                issues.Add(new OptimizationIssue
                {
                    Type = "NonSequentialIndex",
                    Severity = "Warning",
                    Description = $"Column {column.Name} has index {column.Index}, expected {i}",
                    Recommendation = "Use sequential indexing (0, 1, 2, ...) for optimal ArrayRow performance"
                });
            }
        }

        return issues.Any() 
            ? OptimizationValidationResult.Failure(issues.ToArray())
            : OptimizationValidationResult.Success();
    }

    /// <inheritdoc />
    public virtual ValidationResult ValidateProperties(ImmutableDictionary<string, object> properties, ISchema? schema = null)
    {
        // Default implementation - override for specific property validation
        return ValidationResult.Success();
    }

    /// <inheritdoc />
    public virtual ComprehensiveValidationResult ValidateComprehensive(IPluginConfiguration configuration)
    {
        var configValidation = ValidateConfiguration(configuration);
        
        // Calculate confidence score based on validation results
        var confidenceScore = configValidation.IsValid ? 100 : 
            Math.Max(0, 100 - (configValidation.Errors.Count(e => e.Severity == ValidationSeverity.Error) * 50));
        
        return configValidation.IsValid
            ? ComprehensiveValidationResult.Success(configValidation, confidenceScore)
            : ComprehensiveValidationResult.Failure(configValidation, confidenceScore);
    }
}
