namespace FlowEngine.Abstractions.Plugins;

/// <summary>
/// Validation error information.
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
    /// Gets the severity of the error.
    /// </summary>
    public required ValidationSeverity Severity { get; init; }

    /// <summary>
    /// Gets the suggested remediation for the error.
    /// </summary>
    public string? Remediation { get; init; }

    /// <summary>
    /// Gets the property or field name related to the error.
    /// </summary>
    public string? PropertyName { get; init; }
}