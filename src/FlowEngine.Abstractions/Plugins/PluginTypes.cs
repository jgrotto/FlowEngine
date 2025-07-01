using System.Collections.Immutable;

namespace FlowEngine.Abstractions.Plugins;

/// <summary>
/// Individual health check result.
/// </summary>
public sealed record HealthCheck
{
    /// <summary>
    /// Gets the check name.
    /// </summary>
    public required string Name { get; init; }
    
    /// <summary>
    /// Gets whether the check passed.
    /// </summary>
    public required bool Passed { get; init; }
    
    /// <summary>
    /// Gets check details or error message.
    /// </summary>
    public string? Details { get; init; }

    /// <summary>
    /// Gets the check execution time.
    /// </summary>
    public required TimeSpan ExecutionTime { get; init; }

    /// <summary>
    /// Gets the check severity level.
    /// </summary>
    public required HealthCheckSeverity Severity { get; init; }
}

/// <summary>
/// Health check severity levels.
/// </summary>
public enum HealthCheckSeverity
{
    /// <summary>
    /// Information only, no action required.
    /// </summary>
    Info,

    /// <summary>
    /// Warning that should be monitored.
    /// </summary>
    Warning,

    /// <summary>
    /// Error that affects functionality.
    /// </summary>
    Error,

    /// <summary>
    /// Critical error that requires immediate attention.
    /// </summary>
    Critical
}

/// <summary>
/// Plugin metadata and information.
/// </summary>
public sealed record PluginMetadata
{
    /// <summary>
    /// Gets the unique plugin identifier.
    /// </summary>
    public required string Id { get; init; }
    
    /// <summary>
    /// Gets the plugin name.
    /// </summary>
    public required string Name { get; init; }
    
    /// <summary>
    /// Gets the plugin type (Source, Transform, Sink).
    /// </summary>
    public required string Type { get; init; }
    
    /// <summary>
    /// Gets the plugin version.
    /// </summary>
    public required string Version { get; init; }
    
    /// <summary>
    /// Gets the plugin description.
    /// </summary>
    public string? Description { get; init; }
    
    /// <summary>
    /// Gets the plugin author.
    /// </summary>
    public string? Author { get; init; }
    
    /// <summary>
    /// Gets additional plugin tags.
    /// </summary>
    public ImmutableArray<string> Tags { get; init; } = ImmutableArray<string>.Empty;

    /// <summary>
    /// Gets plugin capabilities and features.
    /// </summary>
    public ImmutableArray<string> Capabilities { get; init; } = ImmutableArray<string>.Empty;

    /// <summary>
    /// Gets minimum FlowEngine version required.
    /// </summary>
    public string? MinimumEngineVersion { get; init; }
}

/// <summary>
/// Plugin health status information.
/// </summary>
public sealed record PluginHealth
{
    /// <summary>
    /// Gets whether the plugin is healthy.
    /// </summary>
    public required bool IsHealthy { get; init; }
    
    /// <summary>
    /// Gets the plugin state.
    /// </summary>
    public required PluginState State { get; init; }
    
    /// <summary>
    /// Gets health check details.
    /// </summary>
    public required ImmutableArray<HealthCheck> Checks { get; init; }
    
    /// <summary>
    /// Gets the last health check time.
    /// </summary>
    public required DateTimeOffset LastChecked { get; init; }

    /// <summary>
    /// Gets the overall health score (0-100).
    /// </summary>
    public required int HealthScore { get; init; }

    /// <summary>
    /// Gets any recommendations for improving health.
    /// </summary>
    public ImmutableArray<string> Recommendations { get; init; } = ImmutableArray<string>.Empty;
}

/// <summary>
/// Plugin lifecycle states.
/// </summary>
public enum PluginState
{
    /// <summary>
    /// Plugin has been created but not initialized.
    /// </summary>
    Created,
    
    /// <summary>
    /// Plugin is being initialized.
    /// </summary>
    Initializing,
    
    /// <summary>
    /// Plugin has been initialized and is ready to start.
    /// </summary>
    Initialized,
    
    /// <summary>
    /// Plugin is being started.
    /// </summary>
    Starting,
    
    /// <summary>
    /// Plugin is running and processing data.
    /// </summary>
    Running,
    
    /// <summary>
    /// Plugin is being stopped.
    /// </summary>
    Stopping,
    
    /// <summary>
    /// Plugin has been stopped and can be restarted.
    /// </summary>
    Stopped,
    
    /// <summary>
    /// Plugin has failed and requires intervention.
    /// </summary>
    Failed,
    
    /// <summary>
    /// Plugin has been disposed and cannot be used.
    /// </summary>
    Disposed
}

/// <summary>
/// Plugin performance metrics.
/// </summary>
public sealed record PluginMetrics
{
    /// <summary>
    /// Gets the plugin identifier.
    /// </summary>
    public required string PluginId { get; init; }
    
    /// <summary>
    /// Gets the current plugin state.
    /// </summary>
    public required PluginState State { get; init; }
    
    /// <summary>
    /// Gets the total number of items processed.
    /// </summary>
    public required long TotalProcessed { get; init; }
    
    /// <summary>
    /// Gets the total number of errors encountered.
    /// </summary>
    public required long ErrorCount { get; init; }
    
    /// <summary>
    /// Gets the average processing time in milliseconds.
    /// </summary>
    public required double AverageProcessingTimeMs { get; init; }
    
    /// <summary>
    /// Gets the current memory usage in bytes.
    /// </summary>
    public required long MemoryUsageBytes { get; init; }
    
    /// <summary>
    /// Gets the timestamp of last activity.
    /// </summary>
    public required DateTimeOffset LastActivityAt { get; init; }

    /// <summary>
    /// Gets the processing throughput in items per second.
    /// </summary>
    public required double ThroughputPerSecond { get; init; }

    /// <summary>
    /// Gets the error rate as a percentage.
    /// </summary>
    public double ErrorRate => TotalProcessed > 0 ? (double)ErrorCount / TotalProcessed * 100 : 0;

    /// <summary>
    /// Gets component-specific metrics.
    /// </summary>
    public ImmutableDictionary<string, object> ComponentMetrics { get; init; } = 
        ImmutableDictionary<string, object>.Empty;
}

