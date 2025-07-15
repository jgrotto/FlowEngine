using System;
using System.Collections.Generic;
using System.Linq;
using ValidationResult = FlowEngine.Abstractions.ValidationResult;

namespace FlowEngine.Abstractions.Data;

/// <summary>
/// Defines schema requirements for plugin input validation.
/// Used to validate plugin chain compatibility and ensure data flows correctly between plugins.
/// </summary>
public interface ISchemaRequirement
{
    /// <summary>
    /// Validates if the provided schema meets plugin requirements.
    /// Used during pipeline validation to ensure plugin chain compatibility.
    /// </summary>
    /// <param name="schema">The schema to validate against requirements</param>
    /// <returns>Validation result indicating success or failure with specific error messages</returns>
    ValidationResult ValidateSchema(ISchema schema);
    
    /// <summary>
    /// Gets required fields for this plugin.
    /// Used for documentation and validation of input schemas.
    /// </summary>
    IEnumerable<FieldRequirement> RequiredFields { get; }
    
    /// <summary>
    /// Indicates if plugin can accept any schema (flexible plugins like JavaScript Transform).
    /// When true, RequiredFields may be empty and ValidateSchema should return success for any schema.
    /// </summary>
    bool AcceptsAnySchema { get; }
    
    /// <summary>
    /// Gets human-readable description of requirements for documentation and error messages.
    /// Should describe what the plugin expects and how to provide compatible data.
    /// </summary>
    string RequirementDescription { get; }
}

/// <summary>
/// Defines a required field for plugin input with validation rules.
/// </summary>
public class FieldRequirement
{
    /// <summary>
    /// Name of the required field. Must match exactly in the input schema.
    /// </summary>
    public string FieldName { get; init; } = string.Empty;
    
    /// <summary>
    /// Expected .NET type for the field data.
    /// Used for type compatibility validation.
    /// </summary>
    public Type ExpectedType { get; init; } = typeof(object);
    
    /// <summary>
    /// Whether this field is required (true) or optional (false).
    /// Required fields must be present in input schema.
    /// </summary>
    public bool IsRequired { get; init; } = true;
    
    /// <summary>
    /// Human-readable description of the field's purpose and format.
    /// Used for documentation and error messages.
    /// </summary>
    public string? Description { get; init; }
    
    /// <summary>
    /// Allowed values for the field (null if any value is acceptable).
    /// Used for enumeration validation (e.g., status codes, categories).
    /// </summary>
    public IEnumerable<string>? AllowedValues { get; init; }
    
    /// <summary>
    /// Minimum value for numeric fields (null if no minimum).
    /// </summary>
    public object? MinValue { get; init; }
    
    /// <summary>
    /// Maximum value for numeric fields (null if no maximum).
    /// </summary>
    public object? MaxValue { get; init; }
    
    /// <summary>
    /// Regular expression pattern for string fields (null if no pattern required).
    /// </summary>
    public string? Pattern { get; init; }

    /// <summary>
    /// Initializes a new FieldRequirement with basic validation.
    /// </summary>
    /// <param name="fieldName">Name of the required field</param>
    /// <param name="expectedType">Expected .NET type (null for any type)</param>
    /// <param name="isRequired">Whether the field is required</param>
    public FieldRequirement(string fieldName, Type? expectedType, bool isRequired)
    {
        FieldName = fieldName ?? throw new ArgumentNullException(nameof(fieldName));
        ExpectedType = expectedType ?? typeof(object);
        IsRequired = isRequired;
    }

    /// <summary>
    /// Validates a field from the schema against this requirement.
    /// </summary>
    /// <param name="schemaFields">Dictionary of schema fields by name</param>
    /// <returns>Validation result for this specific field</returns>
    public ValidationResult ValidateField(IDictionary<string, ColumnDefinition> schemaFields)
    {
        if (!schemaFields.TryGetValue(FieldName, out var column))
        {
            return IsRequired 
                ? ValidationResult.Failure($"Required field '{FieldName}' is missing from schema")
                : ValidationResult.Success();
        }

        var errors = new List<string>();

        // Type validation if specified
        if (ExpectedType != typeof(object) && column.DataType != ExpectedType)
        {
            // Check for compatible types
            if (!AreTypesCompatible(column.DataType, ExpectedType))
            {
                errors.Add($"Field '{FieldName}' type mismatch: expected {ExpectedType.Name}, got {column.DataType.Name}");
            }
        }

        return errors.Count == 0 ? ValidationResult.Success() : ValidationResult.Failure(errors.ToArray());
    }

    /// <summary>
    /// Checks if two types are compatible for field validation.
    /// </summary>
    private static bool AreTypesCompatible(Type actualType, Type expectedType)
    {
        if (actualType == expectedType)
            return true;

        // Handle nullable types
        var actualUnderlying = Nullable.GetUnderlyingType(actualType);
        var expectedUnderlying = Nullable.GetUnderlyingType(expectedType);

        if (actualUnderlying != null && actualUnderlying == expectedType)
            return true;

        if (expectedUnderlying != null && expectedUnderlying == actualType)
            return true;

        // Numeric compatibility
        var numericTypes = new[] { typeof(int), typeof(long), typeof(decimal), typeof(double), typeof(float) };
        if (numericTypes.Contains(actualType) && numericTypes.Contains(expectedType))
            return true;

        // String accepts everything (via ToString())
        if (expectedType == typeof(string))
            return true;

        return false;
    }
}