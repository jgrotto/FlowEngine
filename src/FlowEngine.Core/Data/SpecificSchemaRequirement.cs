using FlowEngine.Abstractions.Data;
using System;
using System.Collections.Generic;
using System.Linq;
using ValidationResult = FlowEngine.Abstractions.ValidationResult;

namespace FlowEngine.Core.Data;

/// <summary>
/// Specific schema requirement that validates exact schema compatibility.
/// Requires the input schema to match exactly with the expected schema structure,
/// making it suitable for sink plugins or strict data processors.
/// </summary>
public class SpecificSchemaRequirement : ISchemaRequirement
{
    private readonly ISchema _expectedSchema;
    private readonly bool _allowAdditionalFields;
    private readonly bool _strictTypeMatching;

    /// <summary>
    /// Collection of required fields derived from the expected schema.
    /// </summary>
    public IEnumerable<FieldRequirement> RequiredFields { get; }

    /// <summary>
    /// Always false for specific requirements - we have exact schema expectations.
    /// </summary>
    public bool AcceptsAnySchema => false;

    /// <summary>
    /// Human-readable description of the schema requirement.
    /// </summary>
    public string RequirementDescription { get; }

    /// <summary>
    /// Initializes a new specific schema requirement with exact schema matching.
    /// </summary>
    /// <param name="expectedSchema">The exact schema that input must match</param>
    /// <param name="allowAdditionalFields">Whether to allow additional fields beyond expected schema</param>
    /// <param name="strictTypeMatching">Whether type matching must be exact or allow compatible types</param>
    /// <param name="description">Optional description of the requirement</param>
    public SpecificSchemaRequirement(
        ISchema expectedSchema, 
        bool allowAdditionalFields = false,
        bool strictTypeMatching = true,
        string? description = null)
    {
        _expectedSchema = expectedSchema ?? throw new ArgumentNullException(nameof(expectedSchema));
        _allowAdditionalFields = allowAdditionalFields;
        _strictTypeMatching = strictTypeMatching;
        
        RequiredFields = _expectedSchema.Columns.Select(column => 
            new FieldRequirement(column.Name, column.DataType, !column.IsNullable)).ToList();
        
        RequirementDescription = description ?? GenerateDescription();
    }

    /// <summary>
    /// Validates that the provided schema exactly matches the expected schema structure.
    /// Checks field names, types, nullability, and optionally field order.
    /// </summary>
    /// <param name="schema">The schema to validate against requirements</param>
    /// <returns>Validation result indicating exact compatibility or specific mismatches</returns>
    public ValidationResult ValidateSchema(ISchema schema)
    {
        if (schema == null)
            return ValidationResult.Failure(new[] { "Schema cannot be null" });

        var errors = new List<string>();
        var inputFields = schema.Columns.ToDictionary(c => c.Name, c => c, StringComparer.OrdinalIgnoreCase);
        var expectedFields = _expectedSchema.Columns.ToDictionary(c => c.Name, c => c, StringComparer.OrdinalIgnoreCase);

        // Validate all expected fields are present with correct types
        foreach (var expectedColumn in _expectedSchema.Columns)
        {
            if (!inputFields.TryGetValue(expectedColumn.Name, out var inputColumn))
            {
                errors.Add($"Required field '{expectedColumn.Name}' is missing from input schema");
                continue;
            }

            // Validate type compatibility
            if (_strictTypeMatching)
            {
                if (inputColumn.DataType != expectedColumn.DataType)
                {
                    errors.Add($"Field '{expectedColumn.Name}' type mismatch: expected {expectedColumn.DataType.Name}, got {inputColumn.DataType.Name}");
                }
            }
            else
            {
                if (!AreTypesCompatible(inputColumn.DataType, expectedColumn.DataType))
                {
                    errors.Add($"Field '{expectedColumn.Name}' has incompatible type: expected {expectedColumn.DataType.Name} or compatible, got {inputColumn.DataType.Name}");
                }
            }

            // Validate nullability compatibility
            if (!expectedColumn.IsNullable && inputColumn.IsNullable)
            {
                errors.Add($"Field '{expectedColumn.Name}' cannot be nullable in input when expected schema requires non-null");
            }
        }

        // Check for unexpected additional fields
        if (!_allowAdditionalFields)
        {
            var additionalFields = inputFields.Keys.Except(expectedFields.Keys, StringComparer.OrdinalIgnoreCase).ToList();
            if (additionalFields.Count > 0)
            {
                errors.Add($"Unexpected additional fields in input schema: {string.Join(", ", additionalFields)}");
            }
        }

        // Validate field count matches if not allowing additional fields
        if (!_allowAdditionalFields && inputFields.Count != expectedFields.Count)
        {
            errors.Add($"Schema field count mismatch: expected {expectedFields.Count} fields, got {inputFields.Count}");
        }

        return errors.Count == 0 
            ? ValidationResult.Success() 
            : ValidationResult.Failure(errors.ToArray());
    }

    /// <summary>
    /// Creates a specific requirement for exact schema matching.
    /// </summary>
    /// <param name="expectedSchema">The schema that input must exactly match</param>
    /// <param name="description">Optional description</param>
    /// <returns>Specific schema requirement with exact matching</returns>
    public static SpecificSchemaRequirement ExactMatch(ISchema expectedSchema, string? description = null)
    {
        return new SpecificSchemaRequirement(expectedSchema, false, true, description);
    }

    /// <summary>
    /// Creates a specific requirement that allows additional fields but validates core structure.
    /// </summary>
    /// <param name="expectedSchema">The core schema structure that must be present</param>
    /// <param name="description">Optional description</param>
    /// <returns>Specific schema requirement allowing additional fields</returns>
    public static SpecificSchemaRequirement CoreStructure(ISchema expectedSchema, string? description = null)
    {
        return new SpecificSchemaRequirement(expectedSchema, true, true, description);
    }

    /// <summary>
    /// Creates a specific requirement with flexible type matching for compatible types.
    /// </summary>
    /// <param name="expectedSchema">The expected schema with flexible type matching</param>
    /// <param name="allowAdditionalFields">Whether to allow additional fields</param>
    /// <param name="description">Optional description</param>
    /// <returns>Specific schema requirement with flexible type compatibility</returns>
    public static SpecificSchemaRequirement FlexibleTypes(
        ISchema expectedSchema, 
        bool allowAdditionalFields = false, 
        string? description = null)
    {
        return new SpecificSchemaRequirement(expectedSchema, allowAdditionalFields, false, description);
    }

    /// <summary>
    /// Determines if two types are compatible for flexible type matching.
    /// </summary>
    private static bool AreTypesCompatible(Type inputType, Type expectedType)
    {
        // Exact match
        if (inputType == expectedType)
            return true;

        // Nullable to non-nullable compatibility
        var inputUnderlyingType = Nullable.GetUnderlyingType(inputType);
        var expectedUnderlyingType = Nullable.GetUnderlyingType(expectedType);
        
        if (inputUnderlyingType != null && inputUnderlyingType == expectedType)
            return true;
        
        if (expectedUnderlyingType != null && expectedUnderlyingType == inputType)
            return true;

        // Numeric type compatibility (can be extended)
        var numericTypes = new[] { typeof(int), typeof(long), typeof(decimal), typeof(double), typeof(float) };
        if (numericTypes.Contains(inputType) && numericTypes.Contains(expectedType))
            return true;

        // String compatibility with other types (everything can convert to string)
        if (expectedType == typeof(string))
            return true;

        return false;
    }

    /// <summary>
    /// Generates a default description based on the expected schema.
    /// </summary>
    private string GenerateDescription()
    {
        var fieldCount = _expectedSchema.Columns.Count();
        var additionalText = _allowAdditionalFields ? " (additional fields allowed)" : " (exact match required)";
        var typeText = _strictTypeMatching ? "strict" : "flexible";
        
        return $"Requires exact schema with {fieldCount} fields, {typeText} type matching{additionalText}";
    }
}