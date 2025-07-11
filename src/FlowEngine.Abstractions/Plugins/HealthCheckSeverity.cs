namespace FlowEngine.Abstractions.Plugins;

/// <summary>
/// Severity levels for health checks.
/// </summary>
public enum HealthCheckSeverity
{
    /// <summary>
    /// Informational health check result.
    /// </summary>
    Info,

    /// <summary>
    /// Warning level health check result.
    /// </summary>
    Warning,

    /// <summary>
    /// Error level health check result.
    /// </summary>
    Error,

    /// <summary>
    /// Critical error health check result.
    /// </summary>
    Critical
}
