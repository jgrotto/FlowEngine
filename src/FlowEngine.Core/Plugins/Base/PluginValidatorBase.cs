using FlowEngine.Abstractions.Data;
using Microsoft.Extensions.Logging;
using System.Collections.Immutable;

namespace FlowEngine.Core.Plugins.Base;

/// <summary>
/// Base class for all plugin validators in the five-component architecture.
/// Provides comprehensive validation for configuration, schema compatibility, and performance constraints.
/// </summary>
/// <remarks>
/// This base class implements the Validator component of the five-component pattern:
/// Configuration → Validator → Plugin → Processor → Service
/// 
/// Key Responsibilities:
/// - Validate plugin configuration for correctness and performance
/// - Check schema compatibility with connected plugins
/// - Verify ArrayRow optimization requirements
/// - Validate resource constraints and limits
/// - Perform security and safety checks
/// 
/// Performance Requirements:
/// - Configuration validation must complete in &lt;1ms for typical configurations
/// - Schema validation must complete in &lt;5ms for complex schemas (50+ fields)
/// - Validation results should be cached when possible
/// 
/// The Validator ensures that plugins are correctly configured before execution
/// and that they will perform optimally within the FlowEngine architecture.
/// </remarks>
/// <typeparam name="TConfiguration">Type of configuration this validator validates</typeparam>
public abstract class PluginValidatorBase<TConfiguration>
    where TConfiguration : PluginConfigurationBase
{
    /// <summary>
    /// Logger for validation diagnostics and error reporting.
    /// </summary>
    protected readonly ILogger Logger;

    /// <summary>
    /// Initializes a new instance of the PluginValidatorBase class.
    /// </summary>
    /// <param name="logger">Logger for validation diagnostics</param>
    protected PluginValidatorBase(ILogger logger)
    {
        Logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Validates a plugin configuration for correctness, performance, and compatibility.
    /// </summary>
    /// <param name="configuration">Configuration to validate</param>
    /// <returns>Comprehensive validation result</returns>
    /// <remarks>
    /// Performs multi-layered validation including:
    /// 1. Basic configuration structure validation
    /// 2. Schema validation and ArrayRow optimization checks
    /// 3. Performance constraint validation
    /// 4. Plugin-specific validation rules
    /// 5. Security and safety validation
    /// 
    /// This method is called during plugin initialization and configuration updates.
    /// Results should be cached when possible to avoid repeated validation overhead.
    /// </remarks>
    public virtual async Task<PluginValidationResult> ValidateAsync(TConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        Logger.LogDebug("Starting validation for {PluginType} plugin {PluginId}", 
            configuration.PluginType, configuration.PluginId);

        var errors = new List<ValidationError>();
        var warnings = new List<ValidationWarning>();
        var metrics = new ValidationMetrics();

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            // 1. Basic configuration validation
            await ValidateConfigurationStructure(configuration, errors, warnings, metrics);

            // 2. Schema validation
            await ValidateSchemas(configuration, errors, warnings, metrics);

            // 3. ArrayRow optimization validation
            await ValidateArrayRowOptimization(configuration, errors, warnings, metrics);

            // 4. Performance constraint validation
            await ValidatePerformanceConstraints(configuration, errors, warnings, metrics);

            // 5. Plugin-specific validation
            await ValidatePluginSpecific(configuration, errors, warnings, metrics);

            // 6. Security validation
            await ValidateSecurityConstraints(configuration, errors, warnings, metrics);

            stopwatch.Stop();
            metrics = metrics with { TotalValidationTimeMs = stopwatch.ElapsedMilliseconds };

            var result = new PluginValidationResult
            {
                IsValid = errors.Count == 0,
                Errors = errors.ToImmutableArray(),
                Warnings = warnings.ToImmutableArray(),
                Metrics = metrics,
                ValidatedAt = DateTimeOffset.UtcNow
            };

            Logger.LogDebug("Validation completed for {PluginType} plugin {PluginId} in {Duration}ms. Valid: {IsValid}, Errors: {ErrorCount}, Warnings: {WarningCount}",
                configuration.PluginType, configuration.PluginId, stopwatch.ElapsedMilliseconds, 
                result.IsValid, result.Errors.Length, result.Warnings.Length);

            return result;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            Logger.LogError(ex, "Validation failed for {PluginType} plugin {PluginId}", 
                configuration.PluginType, configuration.PluginId);

            return new PluginValidationResult
            {
                IsValid = false,
                Errors = ImmutableArray.Create(new ValidationError
                {
                    Severity = ValidationSeverity.Critical,
                    Code = "VALIDATION_EXCEPTION",
                    Message = $"Validation failed with exception: {ex.Message}",
                    Details = ex.ToString()
                }),
                Warnings = ImmutableArray<ValidationWarning>.Empty,
                Metrics = metrics with { TotalValidationTimeMs = stopwatch.ElapsedMilliseconds },
                ValidatedAt = DateTimeOffset.UtcNow
            };
        }
    }

    /// <summary>
    /// Validates schema compatibility between this plugin and a source plugin.
    /// </summary>
    /// <param name="configuration">This plugin's configuration</param>
    /// <param name="sourceSchema">Source plugin's output schema</param>
    /// <returns>Schema compatibility validation result</returns>
    /// <remarks>
    /// Validates that data can flow from the source plugin to this plugin
    /// without type errors, missing fields, or performance degradation.
    /// 
    /// Compatibility checks include:
    /// - Required field presence
    /// - Type compatibility and safe conversions
    /// - Field index optimization alignment
    /// - Performance impact assessment
    /// </remarks>
    public virtual async Task<SchemaCompatibilityResult> ValidateCompatibilityAsync(
        TConfiguration configuration, 
        ISchema sourceSchema)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(sourceSchema);

        Logger.LogDebug("Validating schema compatibility for {PluginType} plugin {PluginId}", 
            configuration.PluginType, configuration.PluginId);

        var issues = new List<CompatibilityIssue>();
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            // Check basic compatibility
            var isCompatible = configuration.IsCompatibleWith(sourceSchema);
            if (!isCompatible)
            {
                issues.Add(new CompatibilityIssue
                {
                    Severity = CompatibilitySeverity.Critical,
                    Type = CompatibilityIssueType.IncompatibleSchema,
                    Message = "Source schema is not compatible with input schema",
                    SourceField = null,
                    TargetField = null
                });
            }

            // Detailed field-by-field analysis
            await ValidateFieldCompatibility(configuration.InputSchema, sourceSchema, issues);

            // Performance impact analysis
            await ValidatePerformanceCompatibility(configuration, sourceSchema, issues);

            stopwatch.Stop();

            var result = new SchemaCompatibilityResult
            {
                IsCompatible = issues.All(i => i.Severity < CompatibilitySeverity.Critical),
                Issues = issues.ToImmutableArray(),
                ValidationTimeMs = stopwatch.ElapsedMilliseconds,
                ValidatedAt = DateTimeOffset.UtcNow
            };

            Logger.LogDebug("Schema compatibility validation completed in {Duration}ms. Compatible: {IsCompatible}, Issues: {IssueCount}",
                stopwatch.ElapsedMilliseconds, result.IsCompatible, result.Issues.Length);

            return result;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            Logger.LogError(ex, "Schema compatibility validation failed for {PluginType} plugin {PluginId}", 
                configuration.PluginType, configuration.PluginId);

            return new SchemaCompatibilityResult
            {
                IsCompatible = false,
                Issues = ImmutableArray.Create(new CompatibilityIssue
                {
                    Severity = CompatibilitySeverity.Critical,
                    Type = CompatibilityIssueType.ValidationError,
                    Message = $"Compatibility validation failed: {ex.Message}",
                    SourceField = null,
                    TargetField = null
                }),
                ValidationTimeMs = stopwatch.ElapsedMilliseconds,
                ValidatedAt = DateTimeOffset.UtcNow
            };
        }
    }

    /// <summary>
    /// Validates the basic structure and integrity of the configuration.
    /// </summary>
    /// <param name="configuration">Configuration to validate</param>
    /// <param name="errors">List to add validation errors</param>
    /// <param name="warnings">List to add validation warnings</param>
    /// <param name="metrics">Validation metrics to update</param>
    protected virtual async Task ValidateConfigurationStructure(
        TConfiguration configuration, 
        List<ValidationError> errors, 
        List<ValidationWarning> warnings,
        ValidationMetrics metrics)
    {
        await Task.CompletedTask; // Base implementation is synchronous

        // Validate required properties
        if (string.IsNullOrWhiteSpace(configuration.PluginId))
        {
            errors.Add(new ValidationError
            {
                Severity = ValidationSeverity.Critical,
                Code = "MISSING_PLUGIN_ID",
                Message = "PluginId cannot be null or empty",
                Details = "Every plugin must have a unique identifier"
            });
        }

        if (string.IsNullOrWhiteSpace(configuration.PluginType))
        {
            errors.Add(new ValidationError
            {
                Severity = ValidationSeverity.Critical,
                Code = "MISSING_PLUGIN_TYPE",
                Message = "PluginType cannot be null or empty",
                Details = "Plugin type must be specified (Source, Transform, Sink)"
            });
        }

        if (string.IsNullOrWhiteSpace(configuration.Version))
        {
            warnings.Add(new ValidationWarning
            {
                Code = "MISSING_VERSION",
                Message = "Plugin version is not specified",
                Recommendation = "Specify a semantic version for better plugin management"
            });
        }

        // Validate basic configuration result
        var configValidation = configuration.ValidateConfiguration();
        if (!configValidation.IsValid)
        {
            foreach (var error in configValidation.Errors)
            {
                errors.Add(new ValidationError
                {
                    Severity = ValidationSeverity.High,
                    Code = "CONFIG_VALIDATION_ERROR",
                    Message = error,
                    Details = "Configuration base validation failed"
                });
            }
        }

        foreach (var warning in configValidation.Warnings)
        {
            warnings.Add(new ValidationWarning
            {
                Code = "CONFIG_VALIDATION_WARNING",
                Message = warning,
                Recommendation = "Review configuration for optimal performance"
            });
        }

        metrics = metrics with { ConfigurationValidationTimeMs = 1 }; // Approximate time
    }

    /// <summary>
    /// Validates input and output schemas for structure and consistency.
    /// </summary>
    /// <param name="configuration">Configuration to validate</param>
    /// <param name="errors">List to add validation errors</param>
    /// <param name="warnings">List to add validation warnings</param>
    /// <param name="metrics">Validation metrics to update</param>
    protected virtual async Task ValidateSchemas(
        TConfiguration configuration, 
        List<ValidationError> errors, 
        List<ValidationWarning> warnings,
        ValidationMetrics metrics)
    {
        await Task.CompletedTask; // Base implementation is synchronous

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        // Validate input schema
        if (configuration.InputSchema == null)
        {
            errors.Add(new ValidationError
            {
                Severity = ValidationSeverity.Critical,
                Code = "MISSING_INPUT_SCHEMA",
                Message = "InputSchema cannot be null",
                Details = "All plugins must define an input schema"
            });
        }
        else
        {
            ValidateSchemaStructure(configuration.InputSchema, "Input", errors, warnings);
        }

        // Validate output schema
        if (configuration.OutputSchema == null)
        {
            errors.Add(new ValidationError
            {
                Severity = ValidationSeverity.Critical,
                Code = "MISSING_OUTPUT_SCHEMA",
                Message = "OutputSchema cannot be null",
                Details = "All plugins must define an output schema"
            });
        }
        else
        {
            ValidateSchemaStructure(configuration.OutputSchema, "Output", errors, warnings);
        }

        stopwatch.Stop();
        metrics = metrics with { SchemaValidationTimeMs = stopwatch.ElapsedMilliseconds };
    }

    /// <summary>
    /// Validates ArrayRow optimization requirements.
    /// </summary>
    /// <param name="configuration">Configuration to validate</param>
    /// <param name="errors">List to add validation errors</param>
    /// <param name="warnings">List to add validation warnings</param>
    /// <param name="metrics">Validation metrics to update</param>
    protected virtual async Task ValidateArrayRowOptimization(
        TConfiguration configuration, 
        List<ValidationError> errors, 
        List<ValidationWarning> warnings,
        ValidationMetrics metrics)
    {
        await Task.CompletedTask; // Base implementation is synchronous

        // Validate field index pre-calculation
        var inputIndexes = configuration.InputFieldIndexes;
        var outputIndexes = configuration.OutputFieldIndexes;

        if (inputIndexes.IsEmpty && configuration.InputSchema?.ColumnCount > 0)
        {
            errors.Add(new ValidationError
            {
                Severity = ValidationSeverity.High,
                Code = "MISSING_INPUT_INDEXES",
                Message = "Input field indexes are not pre-calculated",
                Details = "ArrayRow optimization requires pre-calculated field indexes"
            });
        }

        if (outputIndexes.IsEmpty && configuration.OutputSchema?.ColumnCount > 0)
        {
            errors.Add(new ValidationError
            {
                Severity = ValidationSeverity.High,
                Code = "MISSING_OUTPUT_INDEXES",
                Message = "Output field indexes are not pre-calculated",
                Details = "ArrayRow optimization requires pre-calculated field indexes"
            });
        }

        // Check for optimal field ordering
        if (configuration.InputSchema != null && configuration.InputSchema.ColumnCount > 5)
        {
            warnings.Add(new ValidationWarning
            {
                Code = "FIELD_ORDERING_OPTIMIZATION",
                Message = $"Input schema has {configuration.InputSchema.ColumnCount} fields",
                Recommendation = "Place most frequently accessed fields at indexes 0-2 for optimal performance"
            });
        }
    }

    /// <summary>
    /// Validates performance constraints and resource limits.
    /// </summary>
    /// <param name="configuration">Configuration to validate</param>
    /// <param name="errors">List to add validation errors</param>
    /// <param name="warnings">List to add validation warnings</param>
    /// <param name="metrics">Validation metrics to update</param>
    protected virtual async Task ValidatePerformanceConstraints(
        TConfiguration configuration, 
        List<ValidationError> errors, 
        List<ValidationWarning> warnings,
        ValidationMetrics metrics)
    {
        await Task.CompletedTask; // Base implementation is synchronous

        // Check estimated memory usage
        var estimatedMemory = configuration.EstimatedMemoryUsage;
        if (estimatedMemory > 100 * 1024 * 1024) // 100MB
        {
            warnings.Add(new ValidationWarning
            {
                Code = "HIGH_MEMORY_USAGE",
                Message = $"Estimated memory usage is {estimatedMemory / (1024 * 1024)}MB",
                Recommendation = "Consider optimizing configuration to reduce memory usage"
            });
        }

        // Check schema complexity
        var totalFields = (configuration.InputSchema?.ColumnCount ?? 0) + 
                         (configuration.OutputSchema?.ColumnCount ?? 0);
        
        if (totalFields > 100)
        {
            warnings.Add(new ValidationWarning
            {
                Code = "HIGH_SCHEMA_COMPLEXITY",
                Message = $"Total field count is {totalFields}",
                Recommendation = "High field counts may impact performance. Consider schema optimization."
            });
        }
    }

    /// <summary>
    /// Validates plugin-specific configuration requirements.
    /// Override this method to implement plugin-specific validation logic.
    /// </summary>
    /// <param name="configuration">Configuration to validate</param>
    /// <param name="errors">List to add validation errors</param>
    /// <param name="warnings">List to add validation warnings</param>
    /// <param name="metrics">Validation metrics to update</param>
    protected virtual async Task ValidatePluginSpecific(
        TConfiguration configuration, 
        List<ValidationError> errors, 
        List<ValidationWarning> warnings,
        ValidationMetrics metrics)
    {
        await Task.CompletedTask; // Override in derived classes
    }

    /// <summary>
    /// Validates security constraints and safety requirements.
    /// </summary>
    /// <param name="configuration">Configuration to validate</param>
    /// <param name="errors">List to add validation errors</param>
    /// <param name="warnings">List to add validation warnings</param>
    /// <param name="metrics">Validation metrics to update</param>
    protected virtual async Task ValidateSecurityConstraints(
        TConfiguration configuration, 
        List<ValidationError> errors, 
        List<ValidationWarning> warnings,
        ValidationMetrics metrics)
    {
        await Task.CompletedTask; // Base implementation is synchronous

        // Check for sensitive data in configuration
        foreach (var parameter in configuration.Parameters)
        {
            if (IsPasswordParameter(parameter.Key) && parameter.Value is string value && value.Length < 8)
            {
                warnings.Add(new ValidationWarning
                {
                    Code = "WEAK_PASSWORD",
                    Message = $"Parameter '{parameter.Key}' appears to be a weak password",
                    Recommendation = "Use strong passwords with at least 8 characters"
                });
            }
        }
    }

    /// <summary>
    /// Validates field-by-field compatibility between schemas.
    /// </summary>
    /// <param name="targetSchema">Target schema (this plugin's input)</param>
    /// <param name="sourceSchema">Source schema (upstream plugin's output)</param>
    /// <param name="issues">List to add compatibility issues</param>
    protected virtual async Task ValidateFieldCompatibility(
        ISchema targetSchema, 
        ISchema sourceSchema, 
        List<CompatibilityIssue> issues)
    {
        await Task.CompletedTask; // Base implementation is synchronous

        foreach (var targetColumn in targetSchema.Columns)
        {
            var sourceColumn = sourceSchema.GetColumn(targetColumn.Name);
            
            if (sourceColumn == null)
            {
                if (!targetColumn.IsNullable)
                {
                    issues.Add(new CompatibilityIssue
                    {
                        Severity = CompatibilitySeverity.Critical,
                        Type = CompatibilityIssueType.MissingRequiredField,
                        Message = $"Required field '{targetColumn.Name}' is missing from source schema",
                        SourceField = null,
                        TargetField = targetColumn.Name
                    });
                }
                else
                {
                    issues.Add(new CompatibilityIssue
                    {
                        Severity = CompatibilitySeverity.Warning,
                        Type = CompatibilityIssueType.MissingOptionalField,
                        Message = $"Optional field '{targetColumn.Name}' is missing from source schema",
                        SourceField = null,
                        TargetField = targetColumn.Name
                    });
                }
                continue;
            }

            // Check type compatibility
            if (!AreTypesCompatible(sourceColumn.DataType, targetColumn.DataType))
            {
                issues.Add(new CompatibilityIssue
                {
                    Severity = CompatibilitySeverity.High,
                    Type = CompatibilityIssueType.TypeMismatch,
                    Message = $"Field '{targetColumn.Name}' type mismatch: source is {sourceColumn.DataType.Name}, target expects {targetColumn.DataType.Name}",
                    SourceField = sourceColumn.Name,
                    TargetField = targetColumn.Name
                });
            }
        }
    }

    /// <summary>
    /// Validates performance impact of schema compatibility.
    /// </summary>
    /// <param name="configuration">Configuration to validate</param>
    /// <param name="sourceSchema">Source schema</param>
    /// <param name="issues">List to add compatibility issues</param>
    protected virtual async Task ValidatePerformanceCompatibility(
        TConfiguration configuration, 
        ISchema sourceSchema, 
        List<CompatibilityIssue> issues)
    {
        await Task.CompletedTask; // Base implementation is synchronous

        // Check if source schema has significantly more fields than needed
        var excessFields = sourceSchema.ColumnCount - configuration.InputSchema.ColumnCount;
        if (excessFields > 10)
        {
            issues.Add(new CompatibilityIssue
            {
                Severity = CompatibilitySeverity.Warning,
                Type = CompatibilityIssueType.PerformanceImpact,
                Message = $"Source schema has {excessFields} excess fields that will be ignored",
                SourceField = null,
                TargetField = null
            });
        }
    }

    /// <summary>
    /// Validates the structure of a schema.
    /// </summary>
    /// <param name="schema">Schema to validate</param>
    /// <param name="schemaType">Type of schema (Input/Output) for error reporting</param>
    /// <param name="errors">List to add validation errors</param>
    /// <param name="warnings">List to add validation warnings</param>
    private static void ValidateSchemaStructure(
        ISchema schema, 
        string schemaType, 
        List<ValidationError> errors, 
        List<ValidationWarning> warnings)
    {
        if (schema.ColumnCount == 0)
        {
            warnings.Add(new ValidationWarning
            {
                Code = "EMPTY_SCHEMA",
                Message = $"{schemaType} schema has no columns",
                Recommendation = "Ensure schema defines all necessary fields"
            });
            return;
        }

        // Check for duplicate column names
        var columnNames = new HashSet<string>();
        foreach (var column in schema.Columns)
        {
            if (!columnNames.Add(column.Name))
            {
                errors.Add(new ValidationError
                {
                    Severity = ValidationSeverity.High,
                    Code = "DUPLICATE_COLUMN",
                    Message = $"{schemaType} schema contains duplicate column: {column.Name}",
                    Details = "Column names must be unique within a schema"
                });
            }

            // Validate column name
            if (string.IsNullOrWhiteSpace(column.Name))
            {
                errors.Add(new ValidationError
                {
                    Severity = ValidationSeverity.High,
                    Code = "INVALID_COLUMN_NAME",
                    Message = "Column name cannot be null or whitespace",
                    Details = "All columns must have valid names"
                });
            }
        }
    }

    /// <summary>
    /// Checks if two types are compatible for data flow.
    /// </summary>
    /// <param name="sourceType">Source data type</param>
    /// <param name="targetType">Target data type</param>
    /// <returns>True if types are compatible</returns>
    private static bool AreTypesCompatible(Type sourceType, Type targetType)
    {
        if (sourceType == targetType)
            return true;

        // Check for safe conversions
        if (targetType == typeof(string))
            return true; // Everything can convert to string

        if (IsNumericType(sourceType) && IsNumericType(targetType))
            return true; // Numeric types are generally compatible

        return false;
    }

    /// <summary>
    /// Checks if a type is numeric.
    /// </summary>
    /// <param name="type">Type to check</param>
    /// <returns>True if type is numeric</returns>
    private static bool IsNumericType(Type type)
    {
        return type == typeof(int) || type == typeof(long) || type == typeof(float) || 
               type == typeof(double) || type == typeof(decimal) || type == typeof(short) ||
               type == typeof(byte) || type == typeof(uint) || type == typeof(ulong) ||
               type == typeof(ushort) || type == typeof(sbyte);
    }

    /// <summary>
    /// Checks if a parameter name indicates a password or sensitive data.
    /// </summary>
    /// <param name="parameterName">Parameter name to check</param>
    /// <returns>True if parameter appears to be sensitive</returns>
    private static bool IsPasswordParameter(string parameterName)
    {
        var lowerName = parameterName.ToLowerInvariant();
        return lowerName.Contains("password") || lowerName.Contains("secret") || 
               lowerName.Contains("key") || lowerName.Contains("token");
    }
}

/// <summary>
/// Comprehensive validation result for plugin configuration.
/// </summary>
public sealed record PluginValidationResult
{
    /// <summary>
    /// Gets whether the plugin configuration is valid.
    /// </summary>
    public required bool IsValid { get; init; }
    
    /// <summary>
    /// Gets validation errors that prevent plugin execution.
    /// </summary>
    public required ImmutableArray<ValidationError> Errors { get; init; }
    
    /// <summary>
    /// Gets validation warnings that may affect performance or reliability.
    /// </summary>
    public required ImmutableArray<ValidationWarning> Warnings { get; init; }
    
    /// <summary>
    /// Gets validation performance metrics.
    /// </summary>
    public required ValidationMetrics Metrics { get; init; }
    
    /// <summary>
    /// Gets when this validation was performed.
    /// </summary>
    public required DateTimeOffset ValidatedAt { get; init; }
}

/// <summary>
/// Schema compatibility validation result.
/// </summary>
public sealed record SchemaCompatibilityResult
{
    /// <summary>
    /// Gets whether the schemas are compatible.
    /// </summary>
    public required bool IsCompatible { get; init; }
    
    /// <summary>
    /// Gets compatibility issues found during validation.
    /// </summary>
    public required ImmutableArray<CompatibilityIssue> Issues { get; init; }
    
    /// <summary>
    /// Gets validation time in milliseconds.
    /// </summary>
    public required long ValidationTimeMs { get; init; }
    
    /// <summary>
    /// Gets when this validation was performed.
    /// </summary>
    public required DateTimeOffset ValidatedAt { get; init; }
}

/// <summary>
/// Validation error information.
/// </summary>
public sealed record ValidationError
{
    /// <summary>
    /// Gets the severity level of this error.
    /// </summary>
    public required ValidationSeverity Severity { get; init; }
    
    /// <summary>
    /// Gets the error code for programmatic handling.
    /// </summary>
    public required string Code { get; init; }
    
    /// <summary>
    /// Gets the human-readable error message.
    /// </summary>
    public required string Message { get; init; }
    
    /// <summary>
    /// Gets additional error details.
    /// </summary>
    public string? Details { get; init; }
}

/// <summary>
/// Validation warning information.
/// </summary>
public sealed record ValidationWarning
{
    /// <summary>
    /// Gets the warning code for programmatic handling.
    /// </summary>
    public required string Code { get; init; }
    
    /// <summary>
    /// Gets the human-readable warning message.
    /// </summary>
    public required string Message { get; init; }
    
    /// <summary>
    /// Gets recommended action to address the warning.
    /// </summary>
    public string? Recommendation { get; init; }
}

/// <summary>
/// Schema compatibility issue information.
/// </summary>
public sealed record CompatibilityIssue
{
    /// <summary>
    /// Gets the severity level of this compatibility issue.
    /// </summary>
    public required CompatibilitySeverity Severity { get; init; }
    
    /// <summary>
    /// Gets the type of compatibility issue.
    /// </summary>
    public required CompatibilityIssueType Type { get; init; }
    
    /// <summary>
    /// Gets the human-readable issue message.
    /// </summary>
    public required string Message { get; init; }
    
    /// <summary>
    /// Gets the source field name (if applicable).
    /// </summary>
    public string? SourceField { get; init; }
    
    /// <summary>
    /// Gets the target field name (if applicable).
    /// </summary>
    public string? TargetField { get; init; }
}

/// <summary>
/// Validation performance metrics.
/// </summary>
public sealed record ValidationMetrics
{
    /// <summary>
    /// Gets total validation time in milliseconds.
    /// </summary>
    public long TotalValidationTimeMs { get; init; }
    
    /// <summary>
    /// Gets configuration validation time in milliseconds.
    /// </summary>
    public long ConfigurationValidationTimeMs { get; init; }
    
    /// <summary>
    /// Gets schema validation time in milliseconds.
    /// </summary>
    public long SchemaValidationTimeMs { get; init; }
    
    /// <summary>
    /// Gets plugin-specific validation time in milliseconds.
    /// </summary>
    public long PluginSpecificValidationTimeMs { get; init; }
}

/// <summary>
/// Validation error severity levels.
/// </summary>
public enum ValidationSeverity
{
    /// <summary>
    /// Low severity - informational only.
    /// </summary>
    Low,
    
    /// <summary>
    /// Medium severity - may cause issues.
    /// </summary>
    Medium,
    
    /// <summary>
    /// High severity - should be addressed.
    /// </summary>
    High,
    
    /// <summary>
    /// Critical severity - prevents plugin execution.
    /// </summary>
    Critical
}

/// <summary>
/// Schema compatibility severity levels.
/// </summary>
public enum CompatibilitySeverity
{
    /// <summary>
    /// Information only.
    /// </summary>
    Info,
    
    /// <summary>
    /// Warning - may cause issues.
    /// </summary>
    Warning,
    
    /// <summary>
    /// High severity - should be addressed.
    /// </summary>
    High,
    
    /// <summary>
    /// Critical severity - prevents compatibility.
    /// </summary>
    Critical
}

/// <summary>
/// Types of schema compatibility issues.
/// </summary>
public enum CompatibilityIssueType
{
    /// <summary>
    /// Required field is missing from source schema.
    /// </summary>
    MissingRequiredField,
    
    /// <summary>
    /// Optional field is missing from source schema.
    /// </summary>
    MissingOptionalField,
    
    /// <summary>
    /// Field types are incompatible.
    /// </summary>
    TypeMismatch,
    
    /// <summary>
    /// Schemas are fundamentally incompatible.
    /// </summary>
    IncompatibleSchema,
    
    /// <summary>
    /// Performance may be impacted.
    /// </summary>
    PerformanceImpact,
    
    /// <summary>
    /// Validation error occurred.
    /// </summary>
    ValidationError
}