using FlowEngine.Abstractions.Data;
using FlowEngine.Abstractions.Plugins;
using System.Collections.Immutable;
using ValidationResult = FlowEngine.Abstractions.Plugins.ValidationResult;

namespace DelimitedSource;

/// <summary>
/// Validator for DelimitedSource plugin using JSON Schema and business rule validation.
/// </summary>
public sealed class DelimitedSourceValidator : SchemaValidatedPluginValidator<DelimitedSourceConfiguration>
{
    /// <summary>
    /// Gets the embedded JSON Schema resource name for DelimitedSource configuration.
    /// </summary>
    protected override string? GetSchemaResourceName() => "DelimitedSource.Schemas.DelimitedSourceConfiguration.json";

    /// <summary>
    /// Validates semantic/business logic requirements for DelimitedSource.
    /// Called after JSON Schema validation passes.
    /// </summary>
    protected override ValidationResult ValidateSemantics(DelimitedSourceConfiguration configuration)
    {
        var errors = new List<ValidationError>();

        // 1. File accessibility validation
        if (!File.Exists(configuration.FilePath))
        {
            errors.Add(new ValidationError
            {
                Code = "FILE_NOT_FOUND",
                Message = $"File '{configuration.FilePath}' does not exist",
                Severity = ValidationSeverity.Error,
                Remediation = "Ensure the file path is correct and the file exists"
            });
            return ValidationResult.Failure(errors.ToArray()); // Can't validate further
        }

        // 2. File access validation
        try
        {
            using var stream = File.OpenRead(configuration.FilePath);
            // File is accessible
        }
        catch (Exception ex)
        {
            errors.Add(new ValidationError
            {
                Code = "FILE_ACCESS_DENIED",
                Message = $"Cannot access file '{configuration.FilePath}': {ex.Message}",
                Severity = ValidationSeverity.Error,
                Remediation = "Check file permissions and ensure the file is not locked"
            });
            return ValidationResult.Failure(errors.ToArray());
        }

        // 3. Encoding validation
        try
        {
            System.Text.Encoding.GetEncoding(configuration.Encoding);
        }
        catch (ArgumentException)
        {
            errors.Add(new ValidationError
            {
                Code = "INVALID_ENCODING",
                Message = $"Encoding '{configuration.Encoding}' is not supported",
                Severity = ValidationSeverity.Error,
                Remediation = "Use a supported encoding like 'UTF-8', 'UTF-16', or 'ASCII'"
            });
        }

        // 4. Schema configuration validation
        if (!configuration.InferSchema && configuration.OutputSchema == null)
        {
            errors.Add(new ValidationError
            {
                Code = "NO_SCHEMA_SOURCE",
                Message = "Either InferSchema must be true or OutputSchema must be provided",
                Severity = ValidationSeverity.Error,
                Remediation = "Set InferSchema=true or provide an explicit OutputSchema"
            });
        }

        // 5. Performance warnings
        if (configuration.ChunkSize > 20000)
        {
            errors.Add(new ValidationError
            {
                Code = "LARGE_CHUNK_SIZE",
                Message = $"Chunk size {configuration.ChunkSize} may cause memory issues with large files",
                Severity = ValidationSeverity.Warning,
                Remediation = "Consider reducing chunk size to 10000 or less for better memory usage"
            });
        }

        if (configuration.ChunkSize < 1000)
        {
            errors.Add(new ValidationError
            {
                Code = "SMALL_CHUNK_SIZE",
                Message = $"Chunk size {configuration.ChunkSize} may impact performance",
                Severity = ValidationSeverity.Warning,
                Remediation = "Consider increasing chunk size to at least 1000 for better performance"
            });
        }

        // 6. Delimiter validation
        if (string.IsNullOrEmpty(configuration.Delimiter) || configuration.Delimiter.Length != 1)
        {
            errors.Add(new ValidationError
            {
                Code = "INVALID_DELIMITER",
                Message = "Delimiter must be exactly one character",
                Severity = ValidationSeverity.Error,
                Remediation = "Use a single character delimiter like ',', '\\t', '|', or ';'"
            });
        }

        return errors.Any(e => e.Severity == ValidationSeverity.Error)
            ? ValidationResult.Failure(errors.ToArray())
            : ValidationResult.Success();
    }

    /// <inheritdoc />
    public override SchemaCompatibilityResult ValidateSchemaCompatibility(ISchema? inputSchema, ISchema? outputSchema)
    {
        // DelimitedSource is a source plugin, so it doesn't have input schema requirements
        // We can validate that the output schema is compatible with CSV data

        if (outputSchema == null)
        {
            return SchemaCompatibilityResult.Success();
        }

        var issues = new List<CompatibilityIssue>();

        // Check that all column types are CSV-compatible
        var csvCompatibleTypes = new HashSet<Type>
        {
            typeof(string), typeof(int), typeof(long), typeof(decimal),
            typeof(double), typeof(bool), typeof(DateTime)
        };

        foreach (var column in outputSchema.Columns)
        {
            if (!csvCompatibleTypes.Contains(column.DataType))
            {
                issues.Add(new CompatibilityIssue
                {
                    Type = "UnsupportedDataType",
                    Severity = "Error",
                    Description = $"Column '{column.Name}' has unsupported type '{column.DataType.Name}' for CSV processing",
                    Resolution = $"Use one of the supported types: {string.Join(", ", csvCompatibleTypes.Select(t => t.Name))}"
                });
            }
        }

        return issues.Any(i => i.Severity == "Error")
            ? SchemaCompatibilityResult.Failure(issues.ToArray())
            : SchemaCompatibilityResult.Success();
    }

    /// <inheritdoc />
    public override OptimizationValidationResult ValidateArrayRowOptimization(ISchema schema)
    {
        var issues = new List<OptimizationIssue>();

        // Check for sequential indexing
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

        // Check for optimal column count
        if (schema.Columns.Length > 50)
        {
            issues.Add(new OptimizationIssue
            {
                Type = "TooManyColumns",
                Severity = "Warning",
                Description = $"Schema has {schema.Columns.Length} columns, which may impact performance",
                Recommendation = "Consider splitting into multiple schemas or using projection for better performance"
            });
        }

        return issues.Any()
            ? OptimizationValidationResult.Failure(issues.ToArray())
            : OptimizationValidationResult.Success();
    }

    /// <inheritdoc />
    public override ValidationResult ValidateProperties(ImmutableDictionary<string, object> properties, ISchema? schema = null)
    {
        // DelimitedSource doesn't have additional property validation beyond configuration
        return ValidationResult.Success();
    }
}
