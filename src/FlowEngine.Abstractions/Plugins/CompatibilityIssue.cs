namespace FlowEngine.Abstractions.Plugins;

/// <summary>
/// Schema compatibility issue.
/// </summary>
public sealed record CompatibilityIssue
{
    /// <summary>
    /// Gets the type of compatibility issue.
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
    /// Gets the suggested resolution for the issue.
    /// </summary>
    public string? Resolution { get; init; }
}