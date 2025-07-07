using FlowEngine.Abstractions.Data;
using FlowEngine.Abstractions.Plugins;
using ValidationResult = FlowEngine.Abstractions.Plugins.ValidationResult;

namespace TemplatePlugin;

/// <summary>
/// COMPONENT 2: Validator
/// 
/// Demonstrates proper Phase 3 validation with:
/// - JSON Schema-based configuration validation
/// - Data schema compatibility checking 
/// - ArrayRow optimization validation
/// - Semantic validation with business logic
/// </summary>
public sealed class TemplatePluginValidator : SchemaValidatedPluginValidator<TemplatePluginConfiguration>
{
    /// <summary>
    /// Gets the embedded JSON Schema resource name for TemplatePlugin configuration.
    /// </summary>
    protected override string? GetSchemaResourceName() => "TemplatePlugin.Schemas.TemplatePluginConfiguration.json";

    /// <summary>
    /// Validates semantic/business logic requirements for TemplatePlugin.
    /// Called after JSON Schema validation passes.
    /// </summary>
    protected override ValidationResult ValidateSemantics(TemplatePluginConfiguration configuration)
    {
        var errors = new List<ValidationError>();

        // Business logic validation that can't be expressed in JSON Schema
        
        // 1. Performance optimization validation
        if (configuration.BatchSize > configuration.RowCount)
        {
            errors.Add(new ValidationError
            {
                Code = "BATCH_SIZE_EXCEEDS_ROW_COUNT",
                Message = $"BatchSize ({configuration.BatchSize}) cannot exceed RowCount ({configuration.RowCount})",
                Severity = ValidationSeverity.Warning,
                Remediation = "Set BatchSize to be less than or equal to RowCount for optimal processing"
            });
        }

        // 2. Performance impact validation
        if (configuration.RowCount > 100000 && configuration.BatchSize < 1000)
        {
            errors.Add(new ValidationError
            {
                Code = "SUBOPTIMAL_BATCH_SIZE",
                Message = $"Small BatchSize ({configuration.BatchSize}) may impact performance for large RowCount ({configuration.RowCount})",
                Severity = ValidationSeverity.Warning,
                Remediation = "Consider increasing BatchSize to at least 1000 for better performance with large datasets"
            });
        }

        // 3. Data type specific validation
        if (configuration.DataType == "Sequential" && configuration.RowCount > 1000000)
        {
            errors.Add(new ValidationError
            {
                Code = "SEQUENTIAL_PERFORMANCE_WARNING",
                Message = "Sequential data generation may be slow for very large datasets",
                Severity = ValidationSeverity.Warning,
                Remediation = "Consider using 'Random' or 'TestData' types for better performance with large datasets"
            });
        }

        // 4. Delay validation for production scenarios
        if (configuration.DelayMs > 0 && configuration.RowCount > 10000)
        {
            errors.Add(new ValidationError
            {
                Code = "DELAY_WITH_LARGE_DATASET",
                Message = $"Delay ({configuration.DelayMs}ms) combined with large dataset ({configuration.RowCount} rows) will significantly impact performance",
                Severity = ValidationSeverity.Warning,
                Remediation = "Remove delay or reduce RowCount for production scenarios"
            });
        }

        return errors.Any() ? ValidationResult.Failure(errors.ToArray()) : ValidationResult.Success();
    }
   
    /// <inheritdoc />
    public override SchemaCompatibilityResult ValidateSchemaCompatibility(ISchema? inputSchema, ISchema? outputSchema)
    {
        // For a source plugin, input schema compatibility is not applicable
        // but we can validate output schema compatibility
        if (outputSchema != null)
        {
            var schemaValidation = ValidateSchema(outputSchema);
            if (!schemaValidation.IsValid)
            {
                var compatibilityIssues = schemaValidation.Errors.Select(e => new CompatibilityIssue
                {
                    Type = e.Code,
                    Severity = e.Severity.ToString(),
                    Description = e.Message,
                    Resolution = e.Remediation
                }).ToArray();
                return SchemaCompatibilityResult.Failure(compatibilityIssues);
            }
        }
        
        return SchemaCompatibilityResult.Success();
    }

    /// <inheritdoc />
    public override OptimizationValidationResult ValidateArrayRowOptimization(ISchema schema)
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
    public override ValidationResult ValidateProperties(System.Collections.Immutable.ImmutableDictionary<string, object> properties, ISchema? schema = null)
    {
        // Template implementation - no specific property validation needed
        return ValidationResult.Success();
    }

    /// <summary>
    /// Validates the data processing schema for Template Plugin compatibility.
    /// This validates the plugin's data schema, NOT the overall YAML job configuration.
    /// </summary>
    /// <param name="schema">The schema to validate</param>
    /// <returns>Validation result with specific error messages</returns>
    public ValidationResult ValidateSchema(ISchema schema)
    {
        var errors = new List<ValidationError>();

        // Validate supported column types for Template Plugin
        var supportedTypes = new HashSet<Type>
        {
            typeof(int),
            typeof(string),
            typeof(decimal),
            typeof(DateTime),
            typeof(DateTimeOffset),
            typeof(bool)
        };

        foreach (var column in schema.Columns)
        {
            // Check if column type is supported
            if (!supportedTypes.Contains(column.DataType))
            {
                errors.Add(new ValidationError
                {
                    Code = "UNSUPPORTED_COLUMN_TYPE",
                    Message = $"TemplatePlugin does not support '{column.DataType.Name}' type for column '{column.Name}'",
                    Severity = ValidationSeverity.Error,
                    Remediation = $"Use one of the supported types: {string.Join(", ", supportedTypes.Select(t => t.Name))}"
                });
            }
        }

        // Verify field indexes are sequential (0, 1, 2, ...) for ArrayRow optimization
        for (int i = 0; i < schema.Columns.Length; i++)
        {
            var column = schema.Columns[i];
            if (column.Index != i)
            {
                errors.Add(new ValidationError
                {
                    Code = "NON_SEQUENTIAL_INDEX",
                    Message = $"Field indexes must be sequential starting from 0. Column '{column.Name}' has index {column.Index}, expected {i}",
                    Severity = ValidationSeverity.Error,
                    Remediation = "Reorder columns to have sequential indexes (0, 1, 2, ...) for optimal ArrayRow performance"
                });
            }
        }

        // Check required vs optional fields match plugin capabilities
        var requiredColumns = schema.Columns.Where(c => !c.IsNullable).ToArray();
        if (requiredColumns.Length == 0)
        {
            errors.Add(new ValidationError
            {
                Code = "NO_REQUIRED_COLUMNS",
                Message = "TemplatePlugin requires at least one non-nullable column to generate meaningful data",
                Severity = ValidationSeverity.Warning,
                Remediation = "Mark at least one column as required (IsNullable = false)"
            });
        }

        // Performance validation - warn about too many columns
        if (schema.Columns.Length > 20)
        {
            errors.Add(new ValidationError
            {
                Code = "TOO_MANY_COLUMNS",
                Message = $"Schema has {schema.Columns.Length} columns. TemplatePlugin performance may degrade with more than 20 columns",
                Severity = ValidationSeverity.Warning,
                Remediation = "Consider reducing the number of columns or using batch processing optimization"
            });
        }

        return errors.Any() ? ValidationResult.Failure(errors.ToArray()) : ValidationResult.Success();
    }

}
