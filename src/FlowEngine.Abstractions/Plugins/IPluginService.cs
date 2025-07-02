using FlowEngine.Abstractions.Data;

namespace FlowEngine.Abstractions.Plugins;

/// <summary>
/// Represents a plugin service that implements core business logic with ArrayRow optimization.
/// Provides the fundamental data processing capabilities for plugins.
/// </summary>
/// <remarks>
/// The service interface defines the core business logic component in the five-component architecture:
/// Configuration → Validator → Plugin → Processor → Service
/// 
/// Key responsibilities:
/// - Implement core business logic for data processing
/// - Provide ArrayRow-optimized operations for maximum performance
/// - Handle plugin-specific data transformations
/// - Maintain processing state and error handling
/// - Support configuration updates and hot-swapping
/// 
/// Performance Requirements:
/// - Achieve O(1) field access through pre-calculated indexes
/// - Support >200K rows/sec throughput for typical operations
/// - Memory usage must be bounded and predictable
/// - Error handling must not significantly impact performance
/// </remarks>
public interface IPluginService : IDisposable
{
    /// <summary>
    /// Initializes the service with resource allocation and preparation.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token for initialization</param>
    /// <returns>Task representing the initialization operation</returns>
    Task InitializeAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Starts the service and begins processing operations.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token for start operation</param>
    /// <returns>Task representing the start operation</returns>
    Task StartAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Stops the service and suspends processing operations.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token for stop operation</param>
    /// <returns>Task representing the stop operation</returns>
    Task StopAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Processes a single ArrayRow with optimized field access.
    /// </summary>
    /// <param name="row">ArrayRow to process</param>
    /// <param name="cancellationToken">Cancellation token for processing</param>
    /// <returns>Processed ArrayRow</returns>
    Task<IArrayRow> ProcessRowAsync(IArrayRow row, CancellationToken cancellationToken = default);

    /// <summary>
    /// Performs a health check on the service and its components.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token for health check timeout</param>
    /// <returns>Service health check result</returns>
    Task<ServiceHealth> CheckHealthAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Service health status information.
/// </summary>
public sealed record ServiceHealth
{
    /// <summary>
    /// Gets whether the service is healthy.
    /// </summary>
    public required bool IsHealthy { get; init; }
    
    /// <summary>
    /// Gets the health status.
    /// </summary>
    public required HealthStatus Status { get; init; }
    
    /// <summary>
    /// Gets the health message.
    /// </summary>
    public required string Message { get; init; }
    
    /// <summary>
    /// Gets additional health details.
    /// </summary>
    public System.Collections.Immutable.ImmutableDictionary<string, object> Details { get; init; } = 
        System.Collections.Immutable.ImmutableDictionary<string, object>.Empty;
    
    /// <summary>
    /// Gets the timestamp of health check.
    /// </summary>
    public required DateTimeOffset CheckedAt { get; init; }
}

/// <summary>
/// Health status enumeration.
/// </summary>
public enum HealthStatus
{
    /// <summary>
    /// Service is healthy and operating normally.
    /// </summary>
    Healthy,

    /// <summary>
    /// Service is degraded but still functional.
    /// </summary>
    Degraded,

    /// <summary>
    /// Service is unhealthy and may not be functional.
    /// </summary>
    Unhealthy,

    /// <summary>
    /// Service status is unknown.
    /// </summary>
    Unknown
}