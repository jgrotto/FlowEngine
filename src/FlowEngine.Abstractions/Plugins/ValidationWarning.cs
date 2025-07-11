namespace FlowEngine.Abstractions.Plugins;

/// <summary>
/// Validation warning information.
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
    /// Gets the recommended action for the warning.
    /// </summary>
    public string? Recommendation { get; init; }

    /// <summary>
    /// Gets the property or field name related to the warning.
    /// </summary>
    public string? PropertyName { get; init; }
}
