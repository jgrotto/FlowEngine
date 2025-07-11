namespace FlowEngine.Abstractions.Plugins;

/// <summary>
/// ArrayRow optimization issue.
/// </summary>
public sealed record OptimizationIssue
{
    /// <summary>
    /// Gets the type of optimization issue.
    /// </summary>
    public required string Type { get; init; }

    /// <summary>
    /// Gets the severity of the issue.
    /// </summary>
    public required string Severity { get; init; }

    /// <summary>
    /// Gets the description of the issue.
    /// </summary>
    public required string Description { get; init; }

    /// <summary>
    /// Gets the recommended fix for the issue.
    /// </summary>
    public string? Recommendation { get; init; }
}
