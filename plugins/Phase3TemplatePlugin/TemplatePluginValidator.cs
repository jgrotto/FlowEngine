using FlowEngine.Abstractions.Data;
using FlowEngine.Abstractions.Plugins;
using System.ComponentModel.DataAnnotations;

namespace Phase3TemplatePlugin;

/// <summary>
/// COMPONENT 2: Validator
/// 
/// Demonstrates proper Phase 3 validation with:
/// - Configuration validation with detailed errors
/// - Schema compatibility checking
/// - ArrayRow optimization validation
/// - Performance constraint validation
/// </summary>
public sealed class TemplatePluginValidator : IPluginValidator<IPluginConfiguration>
{
    /// <inheritdoc />
    public async Task<ValidationResult> ValidateAsync(IPluginConfiguration configuration, CancellationToken cancellationToken = default)
    {
        if (configuration is not TemplatePluginConfiguration templateConfig)
        {
            return ValidationResult.Failure(
                new[]
                {
                    new ValidationError
                    {
                        Code = "INVALID_CONFIGURATION_TYPE",
                        Message = "Configuration must be TemplatePluginConfiguration",
                        Severity = ValidationSeverity.Error,
                        Remediation = "Provide a valid TemplatePluginConfiguration instance"
                    }
                },
                TimeSpan.FromMilliseconds(1));
        }

        var errors = new List<ValidationError>();
        var warnings = new List<ValidationWarning>();

        // Validate using data annotations
        var annotationErrors = ValidateDataAnnotations(templateConfig);
        errors.AddRange(annotationErrors);

        // Validate business rules
        var businessRuleErrors = ValidateBusinessRules(templateConfig);
        errors.AddRange(businessRuleErrors);

        // Performance validation
        var performanceWarnings = ValidatePerformanceConstraints(templateConfig);
        warnings.AddRange(performanceWarnings);

        // Schema validation (if output schema provided)
        if (templateConfig.OutputSchema != null)
        {
            var schemaErrors = ValidateOutputSchema(templateConfig.OutputSchema);
            errors.AddRange(schemaErrors);
        }

        return new ValidationResult
        {
            IsValid = !errors.Any(),
            Errors = errors,
            Warnings = warnings.ToArray(),
            ValidationTime = TimeSpan.FromMilliseconds(5)
        };
    }

    /// <inheritdoc />
    public ValidationResult Validate(IPluginConfiguration configuration)
    {
        return ValidateAsync(configuration).Result;
    }

    /// <inheritdoc />
    public async Task<SchemaCompatibilityResult> ValidateCompatibilityAsync(
        IPluginConfiguration sourceConfiguration,
        ISchema targetSchema,
        CancellationToken cancellationToken = default)
    {
        var issues = new List<CompatibilityIssue>();
        var compatibilityScore = 100;

        if (sourceConfiguration is not TemplatePluginConfiguration templateConfig)
        {
            return new SchemaCompatibilityResult
            {
                IsCompatible = false,
                Issues = new[]
                {
                    new CompatibilityIssue
                    {
                        Type = "CONFIGURATION_TYPE_MISMATCH",
                        Severity = "Error",
                        Description = "Invalid configuration type for compatibility check"
                    }
                },
                CompatibilityScore = 0
            };
        }

        // For source plugins, they shouldn't have target schemas
        if (targetSchema != null)
        {
            issues.Add(new CompatibilityIssue
            {
                Type = "UNEXPECTED_TARGET_SCHEMA",
                Severity = "Warning",
                Description = "Source plugins typically don't have target schemas"
            });
            compatibilityScore -= 10;
        }

        // Validate output schema compatibility if both are provided
        if (templateConfig.OutputSchema != null && targetSchema != null)
        {
            var schemaIssues = ValidateSchemaCompatibility(templateConfig.OutputSchema, targetSchema);
            issues.AddRange(schemaIssues);
            compatibilityScore -= schemaIssues.Count * 15;
        }

        return new SchemaCompatibilityResult
        {
            IsCompatible = !issues.Any(i => i.Severity == "Error"),
            Issues = issues.ToArray(),
            CompatibilityScore = Math.Max(0, compatibilityScore)
        };
    }

    /// <inheritdoc />
    public ArrayRowOptimizationResult ValidateArrayRowOptimization(IPluginConfiguration configuration)
    {
        if (configuration is not TemplatePluginConfiguration templateConfig)
        {
            return new ArrayRowOptimizationResult
            {
                IsOptimized = false,
                Issues = new[]
                {
                    new OptimizationIssue
                    {
                        Type = "INVALID_CONFIGURATION",
                        Severity = "Error",
                        Description = "Configuration type not supported for optimization validation",
                        Recommendation = "Use TemplatePluginConfiguration"
                    }
                },
                PerformanceMultiplier = 0.1,
                EstimatedFieldAccessTimeNs = 1000.0
            };
        }

        var issues = new List<OptimizationIssue>();
        var isOptimized = true;
        var performanceMultiplier = 1.0;
        var fieldAccessTime = 10.0; // Optimized: ~10ns per field access

        // Validate output schema optimization
        if (templateConfig.OutputSchema != null)
        {
            var schemaIssues = ValidateSchemaOptimization(templateConfig.OutputSchema);
            issues.AddRange(schemaIssues);
            
            if (schemaIssues.Any())
            {
                isOptimized = false;
                performanceMultiplier = 0.3; // 70% performance penalty
                fieldAccessTime = 100.0; // Non-optimized: ~100ns per field access
            }
        }

        // Validate field index mappings
        var indexIssues = ValidateFieldIndexMappings(templateConfig);
        issues.AddRange(indexIssues);

        if (indexIssues.Any())
        {
            isOptimized = false;
            performanceMultiplier = Math.Min(performanceMultiplier, 0.5);
            fieldAccessTime = Math.Max(fieldAccessTime, 50.0);
        }

        return new ArrayRowOptimizationResult
        {
            IsOptimized = isOptimized,
            Issues = issues.ToArray(),
            PerformanceMultiplier = performanceMultiplier,
            EstimatedFieldAccessTimeNs = fieldAccessTime
        };
    }

    /// <inheritdoc />
    public async Task<ValidationResult> ValidatePluginSpecificAsync(IPluginConfiguration configuration, CancellationToken cancellationToken = default)
    {
        if (configuration is not TemplatePluginConfiguration templateConfig)
        {
            return ValidationResult.Failure(
                new[]
                {
                    new ValidationError
                    {
                        Code = "INVALID_CONFIGURATION_TYPE",
                        Message = "Plugin-specific validation requires TemplatePluginConfiguration",
                        Severity = ValidationSeverity.Error,
                        Remediation = "Use correct configuration type"
                    }
                },
                TimeSpan.FromMilliseconds(1));
        }

        var errors = new List<ValidationError>();

        // Template-specific validation: Data type support
        var supportedDataTypes = new[] { "Customer", "Product", "Order", "Generic" };
        if (!supportedDataTypes.Contains(templateConfig.DataType))
        {
            errors.Add(new ValidationError
            {
                Code = "UNSUPPORTED_DATA_TYPE",
                Message = $"Data type '{templateConfig.DataType}' is not supported",
                Severity = ValidationSeverity.Error,
                Remediation = $"Use one of: {string.Join(", ", supportedDataTypes)}"
            });
        }

        // Template-specific validation: Performance constraints
        var estimatedMemoryMB = (templateConfig.RowCount * templateConfig.BatchSize * 100) / (1024 * 1024);
        if (estimatedMemoryMB > 500) // 500MB limit
        {
            errors.Add(new ValidationError
            {
                Code = "MEMORY_LIMIT_EXCEEDED",
                Message = $"Estimated memory usage ({estimatedMemoryMB}MB) exceeds limit (500MB)",
                Severity = ValidationSeverity.Error,
                Remediation = "Reduce RowCount or BatchSize to stay within memory limits"
            });
        }

        return new ValidationResult
        {
            IsValid = !errors.Any(),
            Errors = errors,
            Warnings = Array.Empty<ValidationWarning>(),
            ValidationTime = TimeSpan.FromMilliseconds(10)
        };
    }

    // === Helper Methods ===

    private static List<ValidationError> ValidateDataAnnotations(TemplatePluginConfiguration config)
    {
        var errors = new List<ValidationError>();
        var context = new ValidationContext(config);
        var results = new List<System.ComponentModel.DataAnnotations.ValidationResult>();

        if (!Validator.TryValidateObject(config, context, results, true))
        {
            foreach (var result in results)
            {
                errors.Add(new ValidationError
                {
                    Code = "DATA_ANNOTATION_VIOLATION",
                    Message = result.ErrorMessage ?? "Validation failed",
                    Severity = ValidationSeverity.Error,
                    Remediation = "Check configuration values against constraints"
                });
            }
        }

        return errors;
    }

    private static List<ValidationError> ValidateBusinessRules(TemplatePluginConfiguration config)
    {
        var errors = new List<ValidationError>();

        // Business rule: BatchSize should not exceed RowCount
        if (config.BatchSize > config.RowCount)
        {
            errors.Add(new ValidationError
            {
                Code = "BATCH_SIZE_EXCEEDS_ROW_COUNT",
                Message = "BatchSize cannot be larger than RowCount",
                Severity = ValidationSeverity.Error,
                Remediation = "Set BatchSize <= RowCount"
            });
        }

        // Business rule: Reasonable batch size for performance
        if (config.BatchSize > 10000)
        {
            errors.Add(new ValidationError
            {
                Code = "BATCH_SIZE_TOO_LARGE",
                Message = "Large batch sizes may cause memory pressure",
                Severity = ValidationSeverity.Warning,
                Remediation = "Consider reducing batch size for better memory usage"
            });
        }

        return errors;
    }

    private static List<ValidationWarning> ValidatePerformanceConstraints(TemplatePluginConfiguration config)
    {
        var warnings = new List<ValidationWarning>();

        // Performance warning: Large datasets
        if (config.RowCount > 100_000)
        {
            warnings.Add(new ValidationWarning
            {
                Code = "LARGE_DATASET_WARNING",
                Message = "Large row counts may impact processing time",
                Recommendation = "Consider processing in smaller batches or using streaming"
            });
        }

        // Performance warning: Small batch sizes
        if (config.BatchSize < 100)
        {
            warnings.Add(new ValidationWarning
            {
                Code = "SMALL_BATCH_SIZE",
                Message = "Small batch sizes may reduce throughput",
                Recommendation = "Consider increasing batch size for better performance"
            });
        }

        return warnings;
    }

    private static List<ValidationError> ValidateOutputSchema(ISchema schema)
    {
        var errors = new List<ValidationError>();

        // Validate sequential field indexing (critical for ArrayRow)
        for (int i = 0; i < schema.Columns.Length; i++)
        {
            var column = schema.Columns[i];
            if (column.Index != i)
            {
                errors.Add(new ValidationError
                {
                    Code = "INVALID_FIELD_INDEX",
                    Message = $"Column '{column.Name}' has index {column.Index}, expected {i}",
                    Severity = ValidationSeverity.Error,
                    Remediation = "Field indexes must be sequential starting from 0"
                });
            }
        }

        return errors;
    }

    private static List<CompatibilityIssue> ValidateSchemaCompatibility(ISchema sourceSchema, ISchema targetSchema)
    {
        var issues = new List<CompatibilityIssue>();

        // Check column count compatibility
        if (sourceSchema.Columns.Length != targetSchema.Columns.Length)
        {
            issues.Add(new CompatibilityIssue
            {
                Type = "COLUMN_COUNT_MISMATCH",
                Severity = "Error",
                Description = $"Source has {sourceSchema.Columns.Length} columns, target has {targetSchema.Columns.Length}"
            });
        }

        // Check column type compatibility
        var minColumnCount = Math.Min(sourceSchema.Columns.Length, targetSchema.Columns.Length);
        for (int i = 0; i < minColumnCount; i++)
        {
            var sourceCol = sourceSchema.Columns[i];
            var targetCol = targetSchema.Columns[i];

            if (sourceCol.DataType != targetCol.DataType)
            {
                issues.Add(new CompatibilityIssue
                {
                    Type = "TYPE_MISMATCH",
                    Severity = "Error",
                    Description = $"Column {i}: source type {sourceCol.DataType.Name}, target type {targetCol.DataType.Name}"
                });
            }
        }

        return issues;
    }

    private static List<OptimizationIssue> ValidateSchemaOptimization(ISchema schema)
    {
        var issues = new List<OptimizationIssue>();

        // Check for sequential indexing
        for (int i = 0; i < schema.Columns.Length; i++)
        {
            if (schema.Columns[i].Index != i)
            {
                issues.Add(new OptimizationIssue
                {
                    Type = "NON_SEQUENTIAL_INDEXING",
                    Severity = "Error",
                    Description = $"Column {schema.Columns[i].Name} has non-sequential index {schema.Columns[i].Index}",
                    Recommendation = "Use sequential field indexes (0, 1, 2, ...) for ArrayRow optimization"
                });
            }
        }

        return issues;
    }

    private static List<OptimizationIssue> ValidateFieldIndexMappings(TemplatePluginConfiguration config)
    {
        var issues = new List<OptimizationIssue>();

        // Validate output field index mappings
        if (config.OutputSchema != null)
        {
            var expectedIndexes = Enumerable.Range(0, config.OutputSchema.Columns.Length).ToHashSet();
            var actualIndexes = config.OutputFieldIndexes.Values.ToHashSet();

            if (!expectedIndexes.SetEquals(actualIndexes))
            {
                issues.Add(new OptimizationIssue
                {
                    Type = "FIELD_INDEX_MAPPING_MISMATCH",
                    Severity = "Error",
                    Description = "Output field index mappings don't match schema column count",
                    Recommendation = "Ensure field index mappings are consistent with schema"
                });
            }
        }

        return issues;
    }
}