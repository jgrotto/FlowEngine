namespace FlowEngine.Abstractions.Plugins;

/// <summary>
/// Health status for services and components.
/// </summary>
public enum HealthStatus
{
    /// <summary>
    /// Component is functioning normally.
    /// </summary>
    Healthy,

    /// <summary>
    /// Component is functioning but with reduced capability.
    /// </summary>
    Degraded,

    /// <summary>
    /// Component is not functioning properly.
    /// </summary>
    Unhealthy
}
