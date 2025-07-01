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
public interface IPluginService : IAsyncDisposable
{
    /// <summary>
    /// Gets the current service state.
    /// </summary>
    ServiceState State { get; }

    /// <summary>
    /// Gets whether this service can process the specified schema.
    /// </summary>
    /// <param name="schema">Schema to check compatibility</param>
    /// <returns>True if service can process the schema</returns>
    bool CanProcess(ISchema schema);

    /// <summary>
    /// Initializes the service with resource allocation and preparation.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token for initialization</param>
    /// <returns>Task representing the initialization operation</returns>
    /// <exception cref="ServiceInitializationException">Service initialization failed</exception>
    /// <remarks>
    /// Initialization process:
    /// 1. Validate configuration compatibility
    /// 2. Allocate required resources
    /// 3. Prepare processing optimizations
    /// 4. Initialize service-specific components
    /// 5. Transition to Initialized state
    /// </remarks>
    Task InitializeAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Starts the service and begins processing operations.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token for start operation</param>
    /// <returns>Task representing the start operation</returns>
    /// <exception cref="InvalidOperationException">Service not in correct state for starting</exception>
    /// <remarks>
    /// Start process:
    /// 1. Verify initialized state
    /// 2. Start background operations
    /// 3. Enable processing capabilities
    /// 4. Transition to Running state
    /// 
    /// After starting, the service is ready to process data chunks.
    /// </remarks>
    Task StartAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Stops the service and suspends processing operations.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token for stop operation</param>
    /// <returns>Task representing the stop operation</returns>
    /// <remarks>
    /// Stop process:
    /// 1. Complete pending operations
    /// 2. Stop background operations
    /// 3. Preserve state for restart
    /// 4. Transition to Stopped state
    /// </remarks>
    Task StopAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Processes a single data chunk with ArrayRow optimization.
    /// </summary>
    /// <param name="chunk">Data chunk to process</param>
    /// <param name="cancellationToken">Cancellation token for processing</param>
    /// <returns>Processed data chunk</returns>
    /// <remarks>
    /// This method implements the core processing logic for the plugin.
    /// It should leverage pre-calculated field indexes for optimal performance.
    /// 
    /// Performance: Should achieve >200K rows/sec for typical operations.
    /// Memory: Should use bounded memory regardless of input size.
    /// </remarks>
    Task<IChunk> ProcessChunkAsync(IChunk chunk, CancellationToken cancellationToken = default);

    /// <summary>
    /// Processes a single ArrayRow with optimized field access.
    /// </summary>
    /// <param name="row">ArrayRow to process</param>
    /// <param name="cancellationToken">Cancellation token for processing</param>
    /// <returns>Processed ArrayRow</returns>
    /// <remarks>
    /// This method provides fine-grained row processing with ArrayRow optimization.
    /// Use pre-calculated field indexes for O(1) field access.
    /// 
    /// Performance: Target <15ns field access time.
    /// </remarks>
    Task<IArrayRow> ProcessRowAsync(IArrayRow row, CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates the service configuration with hot-swapping support.
    /// </summary>
    /// <param name="newConfiguration">New configuration to apply</param>
    /// <param name="cancellationToken">Cancellation token for update operation</param>
    /// <returns>Task representing the configuration update operation</returns>
    /// <exception cref="NotSupportedException">Service does not support hot-swapping</exception>
    /// <remarks>
    /// Configuration update process:
    /// 1. Validate new configuration
    /// 2. Prepare for configuration change
    /// 3. Apply new configuration atomically
    /// 4. Update ArrayRow optimizations
    /// 5. Verify successful update
    /// </remarks>
    Task UpdateConfigurationAsync(IPluginConfiguration newConfiguration, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets current service performance metrics and statistics.
    /// </summary>
    /// <returns>Current service performance metrics</returns>
    /// <remarks>
    /// Metrics include processing throughput, error rates, resource usage,
    /// and service-specific performance indicators.
    /// </remarks>
    ServiceMetrics GetMetrics();

    /// <summary>
    /// Performs a health check on the service and its components.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token for health check timeout</param>
    /// <returns>Service health check result</returns>
    /// <remarks>
    /// Health checking verifies:
    /// - Service state and operation
    /// - Resource availability and limits
    /// - Performance within acceptable ranges
    /// - Processing capabilities
    /// </remarks>
    Task<ServiceHealth> CheckHealthAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates that the service can handle the specified data types and volumes.
    /// </summary>
    /// <param name="schema">Input schema to validate</param>
    /// <param name="estimatedVolume">Estimated data volume per hour</param>
    /// <returns>Service capability validation result</returns>
    /// <remarks>
    /// This method helps determine if the service can handle expected workloads
    /// and provides capacity planning information.
    /// </remarks>
    Task<ServiceCapabilityResult> ValidateCapabilityAsync(ISchema schema, long estimatedVolume);
}

/// <summary>
/// Service lifecycle states.
/// </summary>
public enum ServiceState
{
    /// <summary>
    /// Service has been created but not initialized.
    /// </summary>
    Created,
    
    /// <summary>
    /// Service is being initialized.
    /// </summary>
    Initializing,
    
    /// <summary>
    /// Service has been initialized and is ready to start.
    /// </summary>
    Initialized,
    
    /// <summary>
    /// Service is being started.
    /// </summary>
    Starting,
    
    /// <summary>
    /// Service is running and processing data.
    /// </summary>
    Running,
    
    /// <summary>
    /// Service is being stopped.
    /// </summary>
    Stopping,
    
    /// <summary>
    /// Service has been stopped and can be restarted.
    /// </summary>
    Stopped,
    
    /// <summary>
    /// Service has failed and requires intervention.
    /// </summary>
    Failed,
    
    /// <summary>
    /// Service has been disposed and cannot be used.
    /// </summary>
    Disposed
}

/// <summary>
/// Service performance metrics.
/// </summary>
public sealed record ServiceMetrics
{
    /// <summary>
    /// Gets the total number of chunks processed.
    /// </summary>
    public required long TotalChunksProcessed { get; init; }
    
    /// <summary>
    /// Gets the total number of rows processed.
    /// </summary>
    public required long TotalRowsProcessed { get; init; }
    
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
    /// Gets the processing throughput in items per second.
    /// </summary>
    public required double ThroughputPerSecond { get; init; }
    
    /// <summary>
    /// Gets the timestamp of last processed item.
    /// </summary>
    public required DateTimeOffset LastProcessedAt { get; init; }

    /// <summary>
    /// Gets the average field access time in nanoseconds.
    /// </summary>
    public required double AverageFieldAccessTimeNs { get; init; }

    /// <summary>
    /// Gets the ArrayRow optimization efficiency (0-100).
    /// </summary>
    public required int OptimizationEfficiency { get; init; }
    
    /// <summary>
    /// Gets the error rate as a percentage.
    /// </summary>
    public double ErrorRate => TotalRowsProcessed > 0 ? (double)ErrorCount / TotalRowsProcessed * 100 : 0;
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
    /// Gets the service state.
    /// </summary>
    public required ServiceState State { get; init; }
    
    /// <summary>
    /// Gets the resource utilization status.
    /// </summary>
    public required ResourceUtilization ResourceUtilization { get; init; }
    
    /// <summary>
    /// Gets health check details.
    /// </summary>
    public required IReadOnlyList<HealthCheck> Checks { get; init; }
    
    /// <summary>
    /// Gets the last health check time.
    /// </summary>
    public required DateTimeOffset LastChecked { get; init; }

    /// <summary>
    /// Gets the current performance score (0-100).
    /// </summary>
    public required int PerformanceScore { get; init; }

    /// <summary>
    /// Gets the service capacity utilization (0-100).
    /// </summary>
    public required int CapacityUtilization { get; init; }
}

/// <summary>
/// Resource utilization information.
/// </summary>
public sealed record ResourceUtilization
{
    /// <summary>
    /// Gets the CPU utilization percentage (0-100).
    /// </summary>
    public required int CpuUtilization { get; init; }

    /// <summary>
    /// Gets the memory utilization percentage (0-100).
    /// </summary>
    public required int MemoryUtilization { get; init; }

    /// <summary>
    /// Gets the I/O utilization percentage (0-100).
    /// </summary>
    public required int IoUtilization { get; init; }

    /// <summary>
    /// Gets whether resource limits are approaching.
    /// </summary>
    public bool IsApproachingLimits => CpuUtilization > 80 || MemoryUtilization > 80 || IoUtilization > 80;
}

/// <summary>
/// Service capability validation result.
/// </summary>
public sealed record ServiceCapabilityResult
{
    /// <summary>
    /// Gets whether the service can handle the specified workload.
    /// </summary>
    public required bool CanHandle { get; init; }

    /// <summary>
    /// Gets the maximum sustainable throughput in rows per second.
    /// </summary>
    public required long MaxThroughputPerSecond { get; init; }

    /// <summary>
    /// Gets the estimated resource requirements.
    /// </summary>
    public required ResourceRequirements ResourceRequirements { get; init; }

    /// <summary>
    /// Gets any limitations or constraints.
    /// </summary>
    public required IReadOnlyList<string> Limitations { get; init; }

    /// <summary>
    /// Gets performance recommendations.
    /// </summary>
    public required IReadOnlyList<string> Recommendations { get; init; }
}

/// <summary>
/// Resource requirements for a workload.
/// </summary>
public sealed record ResourceRequirements
{
    /// <summary>
    /// Gets the estimated CPU cores required.
    /// </summary>
    public required double CpuCores { get; init; }

    /// <summary>
    /// Gets the estimated memory required in bytes.
    /// </summary>
    public required long MemoryBytes { get; init; }

    /// <summary>
    /// Gets the estimated disk I/O in bytes per second.
    /// </summary>
    public required long DiskIoBytesPerSecond { get; init; }

    /// <summary>
    /// Gets the estimated network I/O in bytes per second.
    /// </summary>
    public required long NetworkIoBytesPerSecond { get; init; }
}

/// <summary>
/// Exception thrown when service initialization fails.
/// </summary>
public sealed class ServiceInitializationException : Exception
{
    public ServiceInitializationException(string message) : base(message) { }
    public ServiceInitializationException(string message, Exception innerException) : base(message, innerException) { }
}