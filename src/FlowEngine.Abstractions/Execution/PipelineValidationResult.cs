using FlowEngine.Abstractions.Data;

namespace FlowEngine.Abstractions.Execution;

/// <summary>
/// Result of pipeline validation.
/// </summary>
public sealed record PipelineValidationResult
{
    /// <summary>
    /// Gets whether the pipeline is valid.
    /// </summary>
    public required bool IsValid { get; init; }

    /// <summary>
    /// Gets validation error messages.
    /// </summary>
    public IReadOnlyList<string> Errors { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Gets validation warning messages.
    /// </summary>
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Gets the execution order determined by dependency analysis.
    /// </summary>
    public IReadOnlyList<string> ExecutionOrder { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Gets schema flow analysis showing how schemas transform through the pipeline.
    /// </summary>
    public IReadOnlyList<SchemaFlowStep> SchemaFlow { get; init; } = Array.Empty<SchemaFlowStep>();

    /// <summary>
    /// Creates a successful validation result.
    /// </summary>
    /// <param name="executionOrder">Determined execution order</param>
    /// <param name="schemaFlow">Schema transformation flow</param>
    /// <param name="warnings">Optional warnings</param>
    public static PipelineValidationResult Success(IReadOnlyList<string> executionOrder, IReadOnlyList<SchemaFlowStep> schemaFlow, params string[] warnings) =>
        new() { IsValid = true, ExecutionOrder = executionOrder, SchemaFlow = schemaFlow, Warnings = warnings };

    /// <summary>
    /// Creates a failed validation result.
    /// </summary>
    /// <param name="errors">Error messages</param>
    public static PipelineValidationResult Failure(params string[] errors) =>
        new() { IsValid = false, Errors = errors };
}
