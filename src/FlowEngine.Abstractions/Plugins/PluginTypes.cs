using System.Collections.Immutable;

namespace FlowEngine.Abstractions.Plugins;

/// <summary>
/// Represents the current state of a plugin in its lifecycle.
/// </summary>
public enum PluginState
{
    /// <summary>
    /// Plugin has been created but not initialized.
    /// </summary>
    Created,

    /// <summary>
    /// Plugin has been initialized with configuration.
    /// </summary>
    Initialized,

    /// <summary>
    /// Plugin is running and processing data.
    /// </summary>
    Running,

    /// <summary>
    /// Plugin has been stopped but can be restarted.
    /// </summary>
    Stopped,

    /// <summary>
    /// Plugin encountered an error and cannot process data.
    /// </summary>
    Failed,

    /// <summary>
    /// Plugin has been disposed and cannot be used.
    /// </summary>
    Disposed
}

/// <summary>
/// Represents the current state of a plugin processor.
/// </summary>
public enum ProcessorState
{
    /// <summary>
    /// Processor has been created but not initialized.
    /// </summary>
    Created,

    /// <summary>
    /// Processor has been initialized and is ready to start.
    /// </summary>
    Initialized,

    /// <summary>
    /// Processor is running and processing data.
    /// </summary>
    Running,

    /// <summary>
    /// Processor has been stopped but can be restarted.
    /// </summary>
    Stopped,

    /// <summary>
    /// Processor encountered an error and cannot process data.
    /// </summary>
    Failed,

    /// <summary>
    /// Processor has been disposed and cannot be used.
    /// </summary>
    Disposed
}

/// <summary>
/// Severity levels for validation messages.
/// </summary>
public enum ValidationSeverity
{
    /// <summary>
    /// Informational message that doesn't affect functionality.
    /// </summary>
    Info,

    /// <summary>
    /// Warning that may affect performance or functionality.
    /// </summary>
    Warning,

    /// <summary>
    /// Error that prevents proper functionality.
    /// </summary>
    Error
}

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

/// <summary>
/// Status of data flow through the system.
/// </summary>
public enum DataFlowStatus
{
    /// <summary>
    /// Data is flowing normally.
    /// </summary>
    Normal,

    /// <summary>
    /// Data flow is slower than expected.
    /// </summary>
    Slow,

    /// <summary>
    /// Data flow has been paused.
    /// </summary>
    Paused,

    /// <summary>
    /// Data flow has stopped due to an error.
    /// </summary>
    Failed,

    /// <summary>
    /// Data flow has been cancelled.
    /// </summary>
    Cancelled
}

/// <summary>
/// Result of plugin initialization.
/// </summary>
public sealed record PluginInitializationResult
{
    /// <summary>
    /// Gets whether initialization was successful.
    /// </summary>
    public required bool Success { get; init; }

    /// <summary>
    /// Gets the initialization result message.
    /// </summary>
    public required string Message { get; init; }

    /// <summary>
    /// Gets the time taken for initialization.
    /// </summary>
    public required TimeSpan InitializationTime { get; init; }

    /// <summary>
    /// Gets any errors that occurred during initialization.
    /// </summary>
    public ImmutableArray<string> Errors { get; init; } = ImmutableArray<string>.Empty;

    /// <summary>
    /// Gets any warnings that occurred during initialization.
    /// </summary>
    public ImmutableArray<string> Warnings { get; init; } = ImmutableArray<string>.Empty;
}

/// <summary>
/// Result of a hot-swap operation.
/// </summary>
public sealed record HotSwapResult
{
    /// <summary>
    /// Gets whether the hot-swap was successful.
    /// </summary>
    public required bool Success { get; init; }

    /// <summary>
    /// Gets the hot-swap result message.
    /// </summary>
    public required string Message { get; init; }

    /// <summary>
    /// Gets the time taken for the hot-swap operation.
    /// </summary>
    public required TimeSpan SwapTime { get; init; }

    /// <summary>
    /// Gets any errors that occurred during hot-swap.
    /// </summary>
    public ImmutableArray<string> Errors { get; init; } = ImmutableArray<string>.Empty;
}

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

/// <summary>
/// Event arguments for plugin state changes.
/// </summary>
public sealed class PluginStateChangedEventArgs : EventArgs
{
    /// <summary>
    /// Gets the previous state of the plugin.
    /// </summary>
    public required PluginState PreviousState { get; init; }

    /// <summary>
    /// Gets the current state of the plugin.
    /// </summary>
    public required PluginState CurrentState { get; init; }

    /// <summary>
    /// Gets the reason for the state change.
    /// </summary>
    public string? Reason { get; init; }

    /// <summary>
    /// Gets the time when the state change occurred.
    /// </summary>
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
}