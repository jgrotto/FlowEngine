using FlowEngine.Abstractions.Data;
using System;
using System.Collections.Generic;
using System.Linq;
using ValidationResult = FlowEngine.Abstractions.ValidationResult;

namespace FlowEngine.Core.Data;

/// <summary>
/// Flexible schema requirement that accepts any schema as long as required fields are present.
/// Allows additional fields beyond the required set, making it suitable for transform plugins
/// that need specific input fields but can pass through additional data.
/// </summary>
public class FlexibleSchemaRequirement : ISchemaRequirement
{
    /// <summary>
    /// Collection of required fields that must be present in the input schema.
    /// </summary>
    public IEnumerable<FieldRequirement> RequiredFields { get; }

    /// <summary>
    /// Always false for flexible requirements - we have specific field requirements.
    /// </summary>
    public bool AcceptsAnySchema => false;

    /// <summary>
    /// Human-readable description of the schema requirement.
    /// </summary>
    public string RequirementDescription { get; }

    /// <summary>
    /// Initializes a new flexible schema requirement with the specified field requirements.
    /// </summary>
    /// <param name="requiredFields">Fields that must be present in the input schema</param>
    /// <param name="description">Optional description of the requirement</param>
    public FlexibleSchemaRequirement(
        IEnumerable<FieldRequirement> requiredFields, 
        string? description = null)
    {
        RequiredFields = requiredFields ?? throw new ArgumentNullException(nameof(requiredFields));
        RequirementDescription = description ?? GenerateDescription();
    }

    /// <summary>
    /// Validates that the provided schema contains all required fields with compatible types.
    /// Additional fields in the schema are allowed and will be preserved.
    /// </summary>
    /// <param name="schema">The schema to validate against requirements</param>
    /// <returns>Validation result indicating success or specific field requirement failures</returns>
    public ValidationResult ValidateSchema(ISchema schema)
    {
        if (schema == null)
            return ValidationResult.Failure(new[] { "Schema cannot be null" });

        var errors = new List<string>();
        var schemaFields = schema.Columns.ToDictionary(c => c.Name, c => c, StringComparer.OrdinalIgnoreCase);

        foreach (var requirement in RequiredFields)
        {
            var fieldValidation = requirement.ValidateField(schemaFields);
            if (!fieldValidation.IsValid)
            {
                errors.AddRange(fieldValidation.Errors);
            }
        }

        return errors.Count == 0 
            ? ValidationResult.Success() 
            : ValidationResult.Failure(errors.ToArray());
    }

    /// <summary>
    /// Creates a flexible requirement for common transform scenarios.
    /// </summary>
    /// <param name="requiredFieldNames">Names of fields that must be present (any type)</param>
    /// <param name="description">Optional description</param>
    /// <returns>Flexible schema requirement with basic field presence requirements</returns>
    public static FlexibleSchemaRequirement ForFields(IEnumerable<string> requiredFieldNames, string? description = null)
    {
        var requirements = requiredFieldNames.Select(name => 
            new FieldRequirement(name, null, false)).ToList();
        
        return new FlexibleSchemaRequirement(requirements, description);
    }

    /// <summary>
    /// Creates a flexible requirement for typed field requirements.
    /// </summary>
    /// <param name="typedFields">Dictionary of field names to required types</param>
    /// <param name="description">Optional description</param>
    /// <returns>Flexible schema requirement with type validation</returns>
    public static FlexibleSchemaRequirement ForTypedFields(
        IDictionary<string, Type> typedFields, 
        string? description = null)
    {
        var requirements = typedFields.Select(kvp => 
            new FieldRequirement(kvp.Key, kvp.Value, false)).ToList();
        
        return new FlexibleSchemaRequirement(requirements, description);
    }

    /// <summary>
    /// Generates a default description based on the required fields.
    /// </summary>
    private string GenerateDescription()
    {
        var fieldNames = RequiredFields.Select(f => f.FieldName).ToList();
        
        if (fieldNames.Count == 0)
            return "Accepts any schema (no specific requirements)";
        
        if (fieldNames.Count == 1)
            return $"Requires field: {fieldNames[0]}";
        
        if (fieldNames.Count <= 3)
            return $"Requires fields: {string.Join(", ", fieldNames)}";
        
        return $"Requires {fieldNames.Count} fields: {string.Join(", ", fieldNames.Take(2))}, and {fieldNames.Count - 2} more";
    }
}