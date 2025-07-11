using FlowEngine.Abstractions.Data;
using System.Collections.Immutable;

namespace FlowEngine.Abstractions.Plugins;

/// <summary>
/// Contains the core business logic for plugin data processing.
/// Implements ArrayRow optimization and dependency injection patterns.
/// </summary>
public interface IPluginService : IDisposable
{
    /// <summary>
    /// Gets the unique identifier for this service instance.
    /// </summary>
    string Id { get; }

    /// <summary>
    /// Gets the configuration used by this service.
    /// </summary>
    IPluginConfiguration Configuration { get; }

    /// <summary>
    /// Gets performance metrics for this service.
    /// </summary>
    ServiceMetrics Metrics { get; }

    /// <summary>
    /// Initializes the service with configuration and dependencies.
    /// </summary>
    /// <param name="configuration">Plugin configuration</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Task representing the initialization operation</returns>
    Task InitializeAsync(IPluginConfiguration configuration, CancellationToken cancellationToken = default);

    /// <summary>
    /// Processes a single data chunk with ArrayRow optimization.
    /// </summary>
    /// <param name="input">Input data chunk</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Processed data chunk</returns>
    Task<IChunk> ProcessChunkAsync(IChunk input, CancellationToken cancellationToken = default);

    /// <summary>
    /// Processes a single row with ArrayRow optimization.
    /// </summary>
    /// <param name="input">Input data row</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Processed data row</returns>
    Task<IArrayRow> ProcessRowAsync(IArrayRow input, CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates that the service can process the given input schema.
    /// </summary>
    /// <param name="inputSchema">Input schema to validate</param>
    /// <returns>Schema compatibility result</returns>
    SchemaCompatibilityResult ValidateInputSchema(ISchema inputSchema);

    /// <summary>
    /// Gets the output schema that this service will produce.
    /// </summary>
    /// <param name="inputSchema">Input schema</param>
    /// <returns>Output schema</returns>
    ISchema GetOutputSchema(ISchema inputSchema);

    /// <summary>
    /// Performs a health check on the service.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Collection of health check results</returns>
    Task<IEnumerable<HealthCheck>> HealthCheckAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Starts the service for streaming operations.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Task representing the start operation</returns>
    Task StartAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Stops the service gracefully.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Task representing the stop operation</returns>
    Task StopAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Performance metrics for plugin services.
/// </summary>
public sealed record ServiceMetrics
{
    /// <summary>
    /// Gets the total number of chunks processed.
    /// </summary>
    public long TotalChunks { get; init; }

    /// <summary>
    /// Gets the total number of rows processed.
    /// </summary>
    public long TotalRows { get; init; }

    /// <summary>
    /// Gets the total processing time.
    /// </summary>
    public TimeSpan TotalProcessingTime { get; init; }

    /// <summary>
    /// Gets the current memory usage in bytes.
    /// </summary>
    public long CurrentMemoryUsage { get; init; }

    /// <summary>
    /// Gets the peak memory usage in bytes.
    /// </summary>
    public long PeakMemoryUsage { get; init; }

    /// <summary>
    /// Gets the number of errors encountered.
    /// </summary>
    public long ErrorCount { get; init; }

    /// <summary>
    /// Gets the number of warnings encountered.
    /// </summary>
    public long WarningCount { get; init; }

    /// <summary>
    /// Gets the timestamp when these metrics were captured.
    /// </summary>
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Calculates the average processing time per chunk.
    /// </summary>
    public TimeSpan AverageChunkProcessingTime =>
        TotalChunks > 0 ? TimeSpan.FromTicks(TotalProcessingTime.Ticks / TotalChunks) : TimeSpan.Zero;

    /// <summary>
    /// Calculates the throughput in rows per second.
    /// </summary>
    public double RowsPerSecond =>
        TotalProcessingTime.TotalSeconds > 0 ? TotalRows / TotalProcessingTime.TotalSeconds : 0.0;
}

/// <summary>
/// Result of schema compatibility validation.
/// </summary>
public sealed record SchemaCompatibilityResult
{
    /// <summary>
    /// Gets whether the schemas are compatible.
    /// </summary>
    public required bool IsCompatible { get; init; }

    /// <summary>
    /// Gets any compatibility issues found.
    /// </summary>
    public ImmutableArray<CompatibilityIssue> Issues { get; init; } = ImmutableArray<CompatibilityIssue>.Empty;

    /// <summary>
    /// Gets any optimization issues found.
    /// </summary>
    public ImmutableArray<OptimizationIssue> OptimizationIssues { get; init; } = ImmutableArray<OptimizationIssue>.Empty;

    /// <summary>
    /// Gets additional metadata about the compatibility check.
    /// </summary>
    public ImmutableDictionary<string, object>? Metadata { get; init; }

    /// <summary>
    /// Creates a successful compatibility result.
    /// </summary>
    /// <returns>A successful SchemaCompatibilityResult</returns>
    public static SchemaCompatibilityResult Success() =>
        new() { IsCompatible = true };

    /// <summary>
    /// Creates a successful compatibility result with optimization issues.
    /// </summary>
    /// <param name="optimizationIssues">Optimization issues to include</param>
    /// <returns>A successful SchemaCompatibilityResult with optimization issues</returns>
    public static SchemaCompatibilityResult SuccessWithOptimizations(params OptimizationIssue[] optimizationIssues) =>
        new() { IsCompatible = true, OptimizationIssues = optimizationIssues.ToImmutableArray() };

    /// <summary>
    /// Creates a failed compatibility result.
    /// </summary>
    /// <param name="issues">Compatibility issues</param>
    /// <returns>A failed SchemaCompatibilityResult</returns>
    public static SchemaCompatibilityResult Failure(params CompatibilityIssue[] issues) =>
        new() { IsCompatible = false, Issues = issues.ToImmutableArray() };
}
