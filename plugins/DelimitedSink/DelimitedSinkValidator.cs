using FlowEngine.Abstractions.Data;
using FlowEngine.Abstractions.Plugins;
using System.Collections.Immutable;
using ValidationResult = FlowEngine.Abstractions.Plugins.ValidationResult;

namespace DelimitedSink;

/// <summary>
/// Validator for DelimitedSink plugin using JSON Schema and business rule validation.
/// </summary>
public sealed class DelimitedSinkValidator : SchemaValidatedPluginValidator<DelimitedSinkConfiguration>
{
    /// <summary>
    /// Gets the embedded JSON Schema resource name for DelimitedSink configuration.
    /// </summary>
    protected override string? GetSchemaResourceName() => "DelimitedSink.Schemas.DelimitedSinkConfiguration.json";

    /// <summary>
    /// Validates semantic/business logic requirements for DelimitedSink.
    /// Called after JSON Schema validation passes.
    /// </summary>
    protected override ValidationResult ValidateSemantics(DelimitedSinkConfiguration configuration)
    {
        var errors = new List<ValidationError>();

        // 1. Output directory validation
        var directory = Path.GetDirectoryName(Path.GetFullPath(configuration.FilePath));
        if (!string.IsNullOrEmpty(directory))
        {
            if (!Directory.Exists(directory) && !configuration.CreateDirectories)
            {
                errors.Add(new ValidationError
                {
                    Code = "OUTPUT_DIRECTORY_NOT_FOUND",
                    Message = $"Output directory '{directory}' does not exist and CreateDirectories is false",
                    Severity = ValidationSeverity.Error,
                    Remediation = "Set CreateDirectories=true or ensure the output directory exists"
                });
            }
            else if (Directory.Exists(directory))
            {
                // Check directory write permissions
                try
                {
                    var testFile = Path.Combine(directory, $"test_{Guid.NewGuid()}.tmp");
                    File.WriteAllText(testFile, "test");
                    File.Delete(testFile);
                }
                catch (Exception ex)
                {
                    errors.Add(new ValidationError
                    {
                        Code = "OUTPUT_DIRECTORY_ACCESS_DENIED",
                        Message = $"Cannot write to output directory '{directory}': {ex.Message}",
                        Severity = ValidationSeverity.Error,
                        Remediation = "Ensure the process has write permissions to the output directory"
                    });
                }
            }
        }

        // 2. Existing file validation
        if (File.Exists(configuration.FilePath))
        {
            if (!configuration.AppendMode && !configuration.OverwriteExisting)
            {
                errors.Add(new ValidationError
                {
                    Code = "FILE_EXISTS_NO_OVERWRITE",
                    Message = $"Output file '{configuration.FilePath}' exists but OverwriteExisting is false",
                    Severity = ValidationSeverity.Error,
                    Remediation = "Set OverwriteExisting=true, use AppendMode=true, or choose a different file path"
                });
            }
            else if (!configuration.AppendMode)
            {
                // Check if file is writable for overwrite
                try
                {
                    using var stream = File.OpenWrite(configuration.FilePath);
                    // File is writable
                }
                catch (Exception ex)
                {
                    errors.Add(new ValidationError
                    {
                        Code = "OUTPUT_FILE_ACCESS_DENIED",
                        Message = $"Cannot overwrite existing file '{configuration.FilePath}': {ex.Message}",
                        Severity = ValidationSeverity.Error,
                        Remediation = "Ensure the file is not locked and the process has write permissions"
                    });
                }
            }
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

        // 4. Delimiter validation
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

        // 5. Performance validation
        if (configuration.BufferSize < 1024)
        {
            errors.Add(new ValidationError
            {
                Code = "SMALL_BUFFER_SIZE",
                Message = $"Buffer size {configuration.BufferSize} is very small and may impact performance",
                Severity = ValidationSeverity.Warning,
                Remediation = "Consider increasing buffer size to at least 8192 bytes for better performance"
            });
        }

        if (configuration.FlushInterval > 10000)
        {
            errors.Add(new ValidationError
            {
                Code = "LARGE_FLUSH_INTERVAL",
                Message = $"Flush interval {configuration.FlushInterval} is very large and may impact data durability",
                Severity = ValidationSeverity.Warning,
                Remediation = "Consider reducing flush interval to 5000 or less for better data durability"
            });
        }

        // 6. Column mapping validation
        if (configuration.ColumnMappings != null && configuration.ColumnMappings.Count > 0)
        {
            var duplicateOutputColumns = configuration.ColumnMappings
                .GroupBy(m => m.OutputColumn)
                .Where(g => g.Count() > 1)
                .Select(g => g.Key);

            foreach (var duplicate in duplicateOutputColumns)
            {
                errors.Add(new ValidationError
                {
                    Code = "DUPLICATE_OUTPUT_COLUMN",
                    Message = $"Output column '{duplicate}' is mapped multiple times",
                    Severity = ValidationSeverity.Error,
                    Remediation = "Ensure each output column is mapped only once"
                });
            }
        }

        return errors.Any(e => e.Severity == ValidationSeverity.Error)
            ? ValidationResult.Failure(errors.ToArray())
            : ValidationResult.Success();
    }

    /// <inheritdoc />
    public override SchemaCompatibilityResult ValidateSchemaCompatibility(ISchema? inputSchema, ISchema? outputSchema)
    {
        // DelimitedSink validates input schema compatibility
        if (inputSchema == null)
        {
            return SchemaCompatibilityResult.Failure(new CompatibilityIssue
            {
                Type = "MissingInputSchema",
                Severity = "Error",
                Description = "DelimitedSink requires an input schema to determine column structure",
                Resolution = "Provide an input schema or ensure the previous plugin in the pipeline produces a schema"
            });
        }

        var issues = new List<CompatibilityIssue>();

        // Check that all column types are CSV-compatible
        var csvCompatibleTypes = new HashSet<Type>
        {
            typeof(string), typeof(int), typeof(long), typeof(decimal),
            typeof(double), typeof(bool), typeof(DateTime), typeof(DateTimeOffset)
        };

        foreach (var column in inputSchema.Columns)
        {
            if (!csvCompatibleTypes.Contains(column.DataType))
            {
                issues.Add(new CompatibilityIssue
                {
                    Type = "UnsupportedDataType",
                    Severity = "Warning",
                    Description = $"Column '{column.Name}' has type '{column.DataType.Name}' which may not format well in CSV",
                    Resolution = "Consider converting complex types to string representation before writing to CSV"
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

        // Check for sequential indexing for optimal reading
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

        // Check for reasonable column count
        if (schema.Columns.Length > 100)
        {
            issues.Add(new OptimizationIssue
            {
                Type = "TooManyColumns",
                Severity = "Warning",
                Description = $"Schema has {schema.Columns.Length} columns, which may impact CSV write performance",
                Recommendation = "Consider splitting into multiple files or filtering columns for better performance"
            });
        }

        return issues.Any()
            ? OptimizationValidationResult.Failure(issues.ToArray())
            : OptimizationValidationResult.Success();
    }

    /// <inheritdoc />
    public override ValidationResult ValidateProperties(ImmutableDictionary<string, object> properties, ISchema? schema = null)
    {
        // DelimitedSink doesn't have additional property validation beyond configuration
        return ValidationResult.Success();
    }
}
