using FlowEngine.Abstractions.Data;
using FlowEngine.Abstractions.Plugins;
using ValidationResult = FlowEngine.Abstractions.Plugins.ValidationResult;

namespace Phase3TemplatePlugin;

/// <summary>
/// COMPONENT 2: Validator
/// 
/// Demonstrates proper Phase 3 validation with:
/// - Configuration validation with detailed errors
/// - Schema compatibility checking 
/// - ArrayRow optimization validation
/// - Performance constraint validation (simplified for template)
/// </summary>
public sealed class TemplatePluginValidator : IPluginValidator
{
    /// <inheritdoc />
    public ValidationResult ValidateConfiguration(IPluginConfiguration configuration)
    {
        if (configuration is not TemplatePluginConfiguration templateConfig)
        {
            return ValidationResult.Failure(
                new ValidationError
                {
                    Code = "INVALID_CONFIGURATION_TYPE",
                    Message = "Configuration must be TemplatePluginConfiguration",
                    Severity = ValidationSeverity.Error,
                    Remediation = "Provide a valid TemplatePluginConfiguration instance"
                });
        }

        // Basic validation - for template purposes
        if (templateConfig.RowCount <= 0)
        {
            return ValidationResult.Failure(
                new ValidationError
                {
                    Code = "INVALID_ROW_COUNT",
                    Message = "RowCount must be greater than 0",
                    Severity = ValidationSeverity.Error
                });
        }

        if (templateConfig.BatchSize <= 0)
        {
            return ValidationResult.Failure(
                new ValidationError
                {
                    Code = "INVALID_BATCH_SIZE", 
                    Message = "BatchSize must be greater than 0",
                    Severity = ValidationSeverity.Error
                });
        }

        return ValidationResult.Success();
    }

    /// <inheritdoc />
    public SchemaCompatibilityResult ValidateSchemaCompatibility(ISchema? inputSchema, ISchema? outputSchema)
    {
        // For a source plugin, input schema compatibility is not applicable
        return SchemaCompatibilityResult.Success();
    }

    /// <inheritdoc />
    public OptimizationValidationResult ValidateArrayRowOptimization(ISchema schema)
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
    public ValidationResult ValidateProperties(System.Collections.Immutable.ImmutableDictionary<string, object> properties, ISchema? schema = null)
    {
        // Template implementation - no specific property validation needed
        return ValidationResult.Success();
    }

    /// <inheritdoc />
    public ComprehensiveValidationResult ValidateComprehensive(IPluginConfiguration configuration)
    {
        var configValidation = ValidateConfiguration(configuration);
        
        return configValidation.IsValid
            ? ComprehensiveValidationResult.Success(configValidation, 100)
            : ComprehensiveValidationResult.Failure(configValidation, 0);
    }
}