using FlowEngine.Abstractions.Data;

namespace FlowEngine.Abstractions.Plugins;

/// <summary>
/// Represents a plugin processor that orchestrates data flow and coordinates processing operations.
/// Provides chunked data processing with performance optimization and flow control.
/// </summary>
/// <remarks>
/// The processor interface defines the data flow orchestration component in the five-component architecture:
/// Configuration → Validator → Plugin → Processor → Service
/// 
/// Key responsibilities:
/// - Orchestrate data flow between input and output
/// - Manage chunked processing for memory efficiency
/// - Coordinate with Service component for actual data processing
/// - Handle backpressure and flow control
/// - Provide performance monitoring and metrics
/// 
/// Performance Requirements:
/// - Support >200K rows/sec throughput for ArrayRow processing
/// - Memory usage must remain bounded regardless of dataset size
/// - Chunking must prevent memory pressure while maintaining performance
/// - Error handling must not impact overall pipeline throughput
/// </remarks>
public interface IPluginProcessor : IAsyncDisposable
{
    /// <summary>
    /// Gets the current processor state.
    /// </summary>
    ProcessorState State { get; }

    /// <summary>
    /// Gets whether this processor is compatible with the specified service.
    /// </summary>
    /// <param name="service">Service to check compatibility</param>
    /// <returns>True if processor is compatible with the service</returns>
    bool IsServiceCompatible(IPluginService service);

    /// <summary>
    /// Initializes the processor with channel setup and validation.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token for initialization</param>
    /// <returns>Task representing the initialization operation</returns>
    /// <exception cref="ProcessorInitializationException">Processor initialization failed</exception>
    /// <remarks>
    /// Initialization process:
    /// 1. Validate service compatibility
    /// 2. Set up input and output channels
    /// 3. Configure processing parameters
    /// 4. Prepare for data flow
    /// 5. Transition to Initialized state
    /// </remarks>
    Task InitializeAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Starts the processor and begins data processing operations.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token for start operation</param>
    /// <returns>Task representing the start operation</returns>
    /// <exception cref="InvalidOperationException">Processor not in correct state for starting</exception>
    /// <remarks>
    /// Start process:
    /// 1. Verify initialized state
    /// 2. Start processing task
    /// 3. Begin monitoring data flow
    /// 4. Transition to Running state
    /// 
    /// The processor will continuously process data chunks until stopped.
    /// </remarks>
    Task StartAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Stops the processor and suspends processing operations.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token for stop operation</param>
    /// <returns>Task representing the stop operation</returns>
    /// <remarks>
    /// Stop process:
    /// 1. Signal processing cancellation
    /// 2. Complete pending chunks
    /// 3. Stop processing task
    /// 4. Clean up resources
    /// 5. Transition to Stopped state
    /// </remarks>
    Task StopAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Processes a single data chunk using the coordinated service component.
    /// </summary>
    /// <param name="chunk">Data chunk to process</param>
    /// <param name="cancellationToken">Cancellation token for processing</param>
    /// <returns>Processed data chunk</returns>
    /// <remarks>
    /// This method coordinates with the Service component to process data
    /// while maintaining performance metrics and error handling.
    /// 
    /// Performance: Should achieve >200K rows/sec for typical operations.
    /// </remarks>
    Task<IChunk> ProcessChunkAsync(IChunk chunk, CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates the processor configuration with hot-swapping support.
    /// </summary>
    /// <param name="newConfiguration">New configuration to apply</param>
    /// <param name="cancellationToken">Cancellation token for update operation</param>
    /// <returns>Task representing the configuration update operation</returns>
    /// <exception cref="NotSupportedException">Processor does not support hot-swapping</exception>
    /// <remarks>
    /// Configuration update process:
    /// 1. Validate new configuration compatibility
    /// 2. Prepare for configuration change
    /// 3. Apply new configuration atomically
    /// 4. Verify successful update
    /// </remarks>
    Task UpdateConfigurationAsync(IPluginConfiguration newConfiguration, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets current processor performance metrics and statistics.
    /// </summary>
    /// <returns>Current processor performance metrics</returns>
    /// <remarks>
    /// Metrics include processing throughput, error rates, resource usage,
    /// and flow control statistics.
    /// </remarks>
    ProcessorMetrics GetMetrics();

    /// <summary>
    /// Performs a health check on the processor and its data flow.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token for health check timeout</param>
    /// <returns>Processor health check result</returns>
    /// <remarks>
    /// Health checking verifies:
    /// - Processor state and operation
    /// - Data flow integrity
    /// - Performance within acceptable ranges
    /// - Service coordination health
    /// </remarks>
    Task<ProcessorHealth> CheckHealthAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Processor lifecycle states.
/// </summary>
public enum ProcessorState
{
    /// <summary>
    /// Processor has been created but not initialized.
    /// </summary>
    Created,
    
    /// <summary>
    /// Processor is being initialized.
    /// </summary>
    Initializing,
    
    /// <summary>
    /// Processor has been initialized and is ready to start.
    /// </summary>
    Initialized,
    
    /// <summary>
    /// Processor is being started.
    /// </summary>
    Starting,
    
    /// <summary>
    /// Processor is running and processing data.
    /// </summary>
    Running,
    
    /// <summary>
    /// Processor is being stopped.
    /// </summary>
    Stopping,
    
    /// <summary>
    /// Processor has been stopped and can be restarted.
    /// </summary>
    Stopped,
    
    /// <summary>
    /// Processor has failed and requires intervention.
    /// </summary>
    Failed,
    
    /// <summary>
    /// Processor has been disposed and cannot be used.
    /// </summary>
    Disposed
}

/// <summary>
/// Processor performance metrics.
/// </summary>
public sealed record ProcessorMetrics
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
    /// Gets the current backpressure level (0-100).
    /// </summary>
    public required int BackpressureLevel { get; init; }

    /// <summary>
    /// Gets the average chunk size in rows.
    /// </summary>
    public required double AverageChunkSize { get; init; }

    /// <summary>
    /// Gets the error rate as a percentage.
    /// </summary>
    public double ErrorRate => TotalRowsProcessed > 0 ? (double)ErrorCount / TotalRowsProcessed * 100 : 0;
}

/// <summary>
/// Processor health status information.
/// </summary>
public sealed record ProcessorHealth
{
    /// <summary>
    /// Gets whether the processor is healthy.
    /// </summary>
    public required bool IsHealthy { get; init; }
    
    /// <summary>
    /// Gets the processor state.
    /// </summary>
    public required ProcessorState State { get; init; }
    
    /// <summary>
    /// Gets the data flow status.
    /// </summary>
    public required DataFlowStatus FlowStatus { get; init; }
    
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
}

/// <summary>
/// Data flow status information.
/// </summary>
public enum DataFlowStatus
{
    /// <summary>
    /// Data flow is operating normally.
    /// </summary>
    Normal,

    /// <summary>
    /// Data flow is experiencing minor slowdowns.
    /// </summary>
    Degraded,

    /// <summary>
    /// Data flow is experiencing significant backpressure.
    /// </summary>
    Backpressure,

    /// <summary>
    /// Data flow has stalled.
    /// </summary>
    Stalled,

    /// <summary>
    /// Data flow has failed.
    /// </summary>
    Failed
}

/// <summary>
/// Exception thrown when processor initialization fails.
/// </summary>
public sealed class ProcessorInitializationException : Exception
{
    public ProcessorInitializationException(string message) : base(message) { }
    public ProcessorInitializationException(string message, Exception innerException) : base(message, innerException) { }
}