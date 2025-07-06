namespace FlowEngine.Abstractions.Plugins;

/// <summary>
/// Health check result.
/// </summary>
public sealed record HealthCheck
{
    /// <summary>
    /// Gets the name of the health check.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Gets whether the health check passed.
    /// </summary>
    public required bool Passed { get; init; }

    /// <summary>
    /// Gets additional details about the health check.
    /// </summary>
    public string? Details { get; init; }

    /// <summary>
    /// Gets the time taken to execute the health check.
    /// </summary>
    public required TimeSpan ExecutionTime { get; init; }

    /// <summary>
    /// Gets the severity of the health check result.
    /// </summary>
    public required HealthCheckSeverity Severity { get; init; }
}