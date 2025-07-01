using FlowEngine.Abstractions.Data;

namespace FlowEngine.Abstractions.Plugins;

/// <summary>
/// Represents a plugin validator that ensures configuration correctness and compatibility.
/// Provides comprehensive validation for plugin setup, schema compatibility, and performance optimization.
/// </summary>
/// <typeparam name="TConfiguration">Type of configuration this validator handles</typeparam>
/// <remarks>
/// Plugin validators perform:
/// - Configuration syntax and semantic validation
/// - Schema compatibility checking between plugins
/// - Performance optimization validation (ArrayRow field indexing)
/// - Security and safety constraint verification
/// - Plugin-specific business rule validation
/// 
/// Validation should be fast (typically &lt;10ms) and comprehensive to catch
/// configuration errors before plugin initialization and execution.
/// </remarks>
public interface IPluginValidator<in TConfiguration>
    where TConfiguration : IPluginConfiguration
{
    /// <summary>
    /// Validates a plugin configuration for correctness and compatibility.
    /// </summary>
    /// <param name="configuration">Configuration to validate</param>
    /// <param name="cancellationToken">Cancellation token for validation timeout</param>
    /// <returns>Validation result with detailed error information</returns>
    /// <exception cref="ArgumentNullException">Configuration is null</exception>
    /// <remarks>
    /// Validation includes:
    /// - Configuration property validation
    /// - Schema structure and field type validation
    /// - ArrayRow optimization verification (sequential field indexing)
    /// - Plugin-specific constraint checking
    /// - Performance impact assessment
    /// 
    /// This method should complete within 10ms for typical configurations
    /// to avoid impacting plugin initialization performance.
    /// </remarks>
    Task<ValidationResult> ValidateAsync(TConfiguration configuration, CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates configuration synchronously for performance-critical scenarios.
    /// </summary>
    /// <param name="configuration">Configuration to validate</param>
    /// <returns>Validation result with detailed error information</returns>
    /// <exception cref="ArgumentNullException">Configuration is null</exception>
    /// <remarks>
    /// This method provides faster validation for scenarios where:
    /// - Validation is needed in synchronous code paths
    /// - Configuration has already been pre-validated
    /// - Only basic validation is required
    /// 
    /// For comprehensive validation, prefer ValidateAsync.
    /// </remarks>
    ValidationResult Validate(TConfiguration configuration);

    /// <summary>
    /// Validates schema compatibility between two plugins for pipeline connections.
    /// </summary>
    /// <param name="sourceConfiguration">Source plugin configuration</param>
    /// <param name="targetSchema">Target plugin input schema</param>
    /// <param name="cancellationToken">Cancellation token for validation timeout</param>
    /// <returns>Schema compatibility result with detailed mismatch information</returns>
    /// <remarks>
    /// Schema compatibility checking ensures:
    /// - Field names and types match between source output and target input
    /// - Required fields are present in source output
    /// - Data type conversions are safe and supported
    /// - Field ordering supports ArrayRow optimization
    /// 
    /// This validation is critical for preventing runtime errors in data pipelines.
    /// </remarks>
    Task<SchemaCompatibilityResult> ValidateCompatibilityAsync(
        TConfiguration sourceConfiguration, 
        ISchema targetSchema, 
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates that the configuration supports ArrayRow optimization requirements.
    /// </summary>
    /// <param name="configuration">Configuration to validate</param>
    /// <returns>ArrayRow optimization validation result</returns>
    /// <remarks>
    /// ArrayRow optimization validation ensures:
    /// - Field indexes are sequential (0, 1, 2, ...)
    /// - No gaps in field indexing
    /// - Index mappings are consistent with schema definitions
    /// - Performance targets can be achieved (&lt;15ns field access)
    /// 
    /// This validation is essential for achieving >200K rows/sec throughput.
    /// </remarks>
    ArrayRowOptimizationResult ValidateArrayRowOptimization(TConfiguration configuration);

    /// <summary>
    /// Validates plugin-specific business rules and constraints.
    /// </summary>
    /// <param name="configuration">Configuration to validate</param>
    /// <param name="cancellationToken">Cancellation token for validation timeout</param>
    /// <returns>Plugin-specific validation result</returns>
    /// <remarks>
    /// This method allows validators to implement custom validation logic
    /// specific to their plugin type and domain requirements.
    /// </remarks>
    Task<ValidationResult> ValidatePluginSpecificAsync(TConfiguration configuration, CancellationToken cancellationToken = default);
}

/// <summary>
/// Represents the result of a configuration validation operation.
/// </summary>
public sealed record ValidationResult
{
    /// <summary>
    /// Gets whether the validation passed.
    /// </summary>
    public required bool IsValid { get; init; }

    /// <summary>
    /// Gets the validation errors encountered.
    /// </summary>
    public required IReadOnlyList<ValidationError> Errors { get; init; }

    /// <summary>
    /// Gets the validation warnings.
    /// </summary>
    public required IReadOnlyList<ValidationWarning> Warnings { get; init; }

    /// <summary>
    /// Gets the validation execution time.
    /// </summary>
    public required TimeSpan ValidationTime { get; init; }

    /// <summary>
    /// Gets additional validation metadata.
    /// </summary>
    public IReadOnlyDictionary<string, object>? Metadata { get; init; }

    /// <summary>
    /// Creates a successful validation result.
    /// </summary>
    /// <param name="validationTime">Time taken for validation</param>
    /// <param name="warnings">Optional warnings</param>
    /// <returns>Successful validation result</returns>
    public static ValidationResult Success(
        TimeSpan validationTime, 
        IReadOnlyList<ValidationWarning>? warnings = null)
    {
        return new ValidationResult
        {
            IsValid = true,
            Errors = Array.Empty<ValidationError>(),
            Warnings = warnings ?? Array.Empty<ValidationWarning>(),
            ValidationTime = validationTime
        };
    }

    /// <summary>
    /// Creates a failed validation result.
    /// </summary>
    /// <param name="errors">Validation errors</param>
    /// <param name="validationTime">Time taken for validation</param>
    /// <param name="warnings">Optional warnings</param>
    /// <returns>Failed validation result</returns>
    public static ValidationResult Failure(
        IReadOnlyList<ValidationError> errors,
        TimeSpan validationTime,
        IReadOnlyList<ValidationWarning>? warnings = null)
    {
        return new ValidationResult
        {
            IsValid = false,
            Errors = errors,
            Warnings = warnings ?? Array.Empty<ValidationWarning>(),
            ValidationTime = validationTime
        };
    }
}

/// <summary>
/// Represents a validation error.
/// </summary>
public sealed record ValidationError
{
    /// <summary>
    /// Gets the error code.
    /// </summary>
    public required string Code { get; init; }

    /// <summary>
    /// Gets the error message.
    /// </summary>
    public required string Message { get; init; }

    /// <summary>
    /// Gets the property path where the error occurred.
    /// </summary>
    public string? PropertyPath { get; init; }

    /// <summary>
    /// Gets the error severity.
    /// </summary>
    public required ValidationSeverity Severity { get; init; }

    /// <summary>
    /// Gets suggested remediation for the error.
    /// </summary>
    public string? Remediation { get; init; }
}

/// <summary>
/// Represents a validation warning.
/// </summary>
public sealed record ValidationWarning
{
    /// <summary>
    /// Gets the warning code.
    /// </summary>
    public required string Code { get; init; }

    /// <summary>
    /// Gets the warning message.
    /// </summary>
    public required string Message { get; init; }

    /// <summary>
    /// Gets the property path where the warning occurred.
    /// </summary>
    public string? PropertyPath { get; init; }

    /// <summary>
    /// Gets suggested action for the warning.
    /// </summary>
    public string? SuggestedAction { get; init; }
}

/// <summary>
/// Represents the result of schema compatibility validation.
/// </summary>
public sealed record SchemaCompatibilityResult
{
    /// <summary>
    /// Gets whether the schemas are compatible.
    /// </summary>
    public required bool IsCompatible { get; init; }

    /// <summary>
    /// Gets the compatibility issues found.
    /// </summary>
    public required IReadOnlyList<CompatibilityIssue> Issues { get; init; }

    /// <summary>
    /// Gets the compatibility score (0-100).
    /// </summary>
    public required int CompatibilityScore { get; init; }

    /// <summary>
    /// Gets suggested modifications for compatibility.
    /// </summary>
    public IReadOnlyList<string>? SuggestedModifications { get; init; }
}

/// <summary>
/// Represents a schema compatibility issue.
/// </summary>
public sealed record CompatibilityIssue
{
    /// <summary>
    /// Gets the issue type.
    /// </summary>
    public required CompatibilityIssueType Type { get; init; }

    /// <summary>
    /// Gets the field name where the issue occurred.
    /// </summary>
    public required string FieldName { get; init; }

    /// <summary>
    /// Gets the issue description.
    /// </summary>
    public required string Description { get; init; }

    /// <summary>
    /// Gets the issue severity.
    /// </summary>
    public required ValidationSeverity Severity { get; init; }
}

/// <summary>
/// Represents the result of ArrayRow optimization validation.
/// </summary>
public sealed record ArrayRowOptimizationResult
{
    /// <summary>
    /// Gets whether ArrayRow optimization is properly configured.
    /// </summary>
    public required bool IsOptimized { get; init; }

    /// <summary>
    /// Gets the optimization issues found.
    /// </summary>
    public required IReadOnlyList<OptimizationIssue> Issues { get; init; }

    /// <summary>
    /// Gets the expected performance improvement factor.
    /// </summary>
    public required double PerformanceMultiplier { get; init; }

    /// <summary>
    /// Gets the estimated field access time in nanoseconds.
    /// </summary>
    public required double EstimatedFieldAccessTimeNs { get; init; }
}

/// <summary>
/// Represents an ArrayRow optimization issue.
/// </summary>
public sealed record OptimizationIssue
{
    /// <summary>
    /// Gets the issue type.
    /// </summary>
    public required OptimizationIssueType Type { get; init; }

    /// <summary>
    /// Gets the field name where the issue occurred.
    /// </summary>
    public string? FieldName { get; init; }

    /// <summary>
    /// Gets the issue description.
    /// </summary>
    public required string Description { get; init; }

    /// <summary>
    /// Gets the performance impact of this issue.
    /// </summary>
    public required PerformanceImpact Impact { get; init; }
}

/// <summary>
/// Validation severity levels.
/// </summary>
public enum ValidationSeverity
{
    /// <summary>
    /// Information only, no action required.
    /// </summary>
    Info,

    /// <summary>
    /// Warning that should be addressed but doesn't prevent operation.
    /// </summary>
    Warning,

    /// <summary>
    /// Error that prevents successful operation.
    /// </summary>
    Error,

    /// <summary>
    /// Critical error that could cause system instability.
    /// </summary>
    Critical
}

/// <summary>
/// Schema compatibility issue types.
/// </summary>
public enum CompatibilityIssueType
{
    /// <summary>
    /// Field is missing in target schema.
    /// </summary>
    MissingField,

    /// <summary>
    /// Field type mismatch between schemas.
    /// </summary>
    TypeMismatch,

    /// <summary>
    /// Field is required in target but optional in source.
    /// </summary>
    RequiredFieldMissing,

    /// <summary>
    /// Field ordering is incompatible with ArrayRow optimization.
    /// </summary>
    IncompatibleOrdering,

    /// <summary>
    /// Data conversion is unsafe or lossy.
    /// </summary>
    UnsafeConversion
}

/// <summary>
/// ArrayRow optimization issue types.
/// </summary>
public enum OptimizationIssueType
{
    /// <summary>
    /// Field indexes are not sequential.
    /// </summary>
    NonSequentialIndexes,

    /// <summary>
    /// Gap in field index sequence.
    /// </summary>
    IndexGap,

    /// <summary>
    /// Field index mapping is inconsistent with schema.
    /// </summary>
    InconsistentMapping,

    /// <summary>
    /// Field indexes are not pre-calculated.
    /// </summary>
    MissingPreCalculation,

    /// <summary>
    /// Schema changes would break existing index mappings.
    /// </summary>
    IndexBreakingChange
}

/// <summary>
/// Performance impact levels.
/// </summary>
public enum PerformanceImpact
{
    /// <summary>
    /// No measurable performance impact.
    /// </summary>
    None,

    /// <summary>
    /// Minor performance impact (&lt;10% degradation).
    /// </summary>
    Low,

    /// <summary>
    /// Moderate performance impact (10-30% degradation).
    /// </summary>
    Medium,

    /// <summary>
    /// High performance impact (30-50% degradation).
    /// </summary>
    High,

    /// <summary>
    /// Severe performance impact (>50% degradation).
    /// </summary>
    Critical
}