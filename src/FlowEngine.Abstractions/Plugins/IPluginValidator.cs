using FlowEngine.Abstractions.Data;
using System.Collections.Immutable;

namespace FlowEngine.Abstractions.Plugins;

/// <summary>
/// Validates plugin configurations and schemas with comprehensive error reporting.
/// Implements validation rules for ArrayRow optimization and schema compatibility.
/// </summary>
public interface IPluginValidator
{
    /// <summary>
    /// Validates a plugin configuration for correctness and completeness.
    /// </summary>
    /// <param name="configuration">Configuration to validate</param>
    /// <returns>Validation result with errors and warnings</returns>
    ValidationResult ValidateConfiguration(IPluginConfiguration configuration);

    /// <summary>
    /// Validates schema compatibility between input and output schemas.
    /// </summary>
    /// <param name="inputSchema">Input schema</param>
    /// <param name="outputSchema">Output schema</param>
    /// <returns>Schema compatibility result</returns>
    SchemaCompatibilityResult ValidateSchemaCompatibility(ISchema? inputSchema, ISchema? outputSchema);

    /// <summary>
    /// Validates ArrayRow optimization requirements for a schema.
    /// </summary>
    /// <param name="schema">Schema to validate</param>
    /// <returns>Validation result with optimization issues</returns>
    OptimizationValidationResult ValidateArrayRowOptimization(ISchema schema);

    /// <summary>
    /// Validates plugin configuration properties for type safety and constraints.
    /// </summary>
    /// <param name="properties">Properties to validate</param>
    /// <param name="schema">Schema context for validation</param>
    /// <returns>Validation result with property-specific errors</returns>
    ValidationResult ValidateProperties(ImmutableDictionary<string, object> properties, ISchema? schema = null);

    /// <summary>
    /// Performs comprehensive validation of a plugin configuration.
    /// </summary>
    /// <param name="configuration">Configuration to validate</param>
    /// <returns>Comprehensive validation result</returns>
    ComprehensiveValidationResult ValidateComprehensive(IPluginConfiguration configuration);
}

/// <summary>
/// Result of configuration validation.
/// </summary>
public sealed record ValidationResult
{
    /// <summary>
    /// Gets whether the validation passed.
    /// </summary>
    public required bool IsValid { get; init; }

    /// <summary>
    /// Gets validation errors that prevent execution.
    /// </summary>
    public ImmutableArray<ValidationError> Errors { get; init; } = ImmutableArray<ValidationError>.Empty;

    /// <summary>
    /// Gets validation warnings that don't prevent execution.
    /// </summary>
    public ImmutableArray<ValidationWarning> Warnings { get; init; } = ImmutableArray<ValidationWarning>.Empty;

    /// <summary>
    /// Gets additional metadata about the validation.
    /// </summary>
    public ImmutableDictionary<string, object>? Metadata { get; init; }

    /// <summary>
    /// Creates a successful validation result.
    /// </summary>
    /// <returns>A successful ValidationResult</returns>
    public static ValidationResult Success() =>
        new() { IsValid = true };

    /// <summary>
    /// Creates a successful validation result with warnings.
    /// </summary>
    /// <param name="warnings">Warnings to include</param>
    /// <returns>A successful ValidationResult with warnings</returns>
    public static ValidationResult SuccessWithWarnings(params ValidationWarning[] warnings) =>
        new() { IsValid = true, Warnings = warnings.ToImmutableArray() };

    /// <summary>
    /// Creates a failed validation result.
    /// </summary>
    /// <param name="errors">Validation errors</param>
    /// <returns>A failed ValidationResult</returns>
    public static ValidationResult Failure(params ValidationError[] errors) =>
        new() { IsValid = false, Errors = errors.ToImmutableArray() };

    /// <summary>
    /// Creates a failed validation result with warnings.
    /// </summary>
    /// <param name="errors">Validation errors</param>
    /// <param name="warnings">Validation warnings</param>
    /// <returns>A failed ValidationResult with errors and warnings</returns>
    public static ValidationResult FailureWithWarnings(ValidationError[] errors, ValidationWarning[] warnings) =>
        new() { IsValid = false, Errors = errors.ToImmutableArray(), Warnings = warnings.ToImmutableArray() };
}

/// <summary>
/// Result of ArrayRow optimization validation.
/// </summary>
public sealed record OptimizationValidationResult
{
    /// <summary>
    /// Gets whether the optimization validation passed.
    /// </summary>
    public required bool IsOptimized { get; init; }

    /// <summary>
    /// Gets optimization issues found.
    /// </summary>
    public ImmutableArray<OptimizationIssue> Issues { get; init; } = ImmutableArray<OptimizationIssue>.Empty;

    /// <summary>
    /// Gets recommendations for optimization improvements.
    /// </summary>
    public ImmutableArray<string> Recommendations { get; init; } = ImmutableArray<string>.Empty;

    /// <summary>
    /// Gets the expected performance impact.
    /// </summary>
    public string? PerformanceImpact { get; init; }

    /// <summary>
    /// Creates a successful optimization validation result.
    /// </summary>
    /// <returns>A successful OptimizationValidationResult</returns>
    public static OptimizationValidationResult Success() =>
        new() { IsOptimized = true };

    /// <summary>
    /// Creates a failed optimization validation result.
    /// </summary>
    /// <param name="issues">Optimization issues</param>
    /// <returns>A failed OptimizationValidationResult</returns>
    public static OptimizationValidationResult Failure(params OptimizationIssue[] issues) =>
        new() { IsOptimized = false, Issues = issues.ToImmutableArray() };
}

/// <summary>
/// Comprehensive validation result combining all validation types.
/// </summary>
public sealed record ComprehensiveValidationResult
{
    /// <summary>
    /// Gets whether all validations passed.
    /// </summary>
    public required bool IsValid { get; init; }

    /// <summary>
    /// Gets the configuration validation result.
    /// </summary>
    public required ValidationResult ConfigurationValidation { get; init; }

    /// <summary>
    /// Gets the schema compatibility validation result.
    /// </summary>
    public SchemaCompatibilityResult? SchemaCompatibilityValidation { get; init; }

    /// <summary>
    /// Gets the ArrayRow optimization validation result.
    /// </summary>
    public OptimizationValidationResult? OptimizationValidation { get; init; }

    /// <summary>
    /// Gets the properties validation result.
    /// </summary>
    public ValidationResult? PropertiesValidation { get; init; }

    /// <summary>
    /// Gets the overall validation score (0-100).
    /// </summary>
    public int ValidationScore { get; init; }

    /// <summary>
    /// Gets additional metadata about the comprehensive validation.
    /// </summary>
    public ImmutableDictionary<string, object>? Metadata { get; init; }

    /// <summary>
    /// Creates a successful comprehensive validation result.
    /// </summary>
    /// <param name="configurationValidation">Configuration validation result</param>
    /// <param name="score">Validation score</param>
    /// <returns>A successful ComprehensiveValidationResult</returns>
    public static ComprehensiveValidationResult Success(ValidationResult configurationValidation, int score = 100) =>
        new() { IsValid = true, ConfigurationValidation = configurationValidation, ValidationScore = score };

    /// <summary>
    /// Creates a failed comprehensive validation result.
    /// </summary>
    /// <param name="configurationValidation">Configuration validation result</param>
    /// <param name="score">Validation score</param>
    /// <returns>A failed ComprehensiveValidationResult</returns>
    public static ComprehensiveValidationResult Failure(ValidationResult configurationValidation, int score = 0) =>
        new() { IsValid = false, ConfigurationValidation = configurationValidation, ValidationScore = score };
}
