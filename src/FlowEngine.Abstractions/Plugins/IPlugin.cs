using Microsoft.Extensions.Configuration;

namespace FlowEngine.Abstractions.Plugins;

/// <summary>
/// Base interface for all FlowEngine plugins providing lifecycle management and schema definition.
/// Plugins are isolated in separate AssemblyLoadContext instances for security and versioning.
/// </summary>
public interface IPlugin : IAsyncDisposable
{
    /// <summary>
    /// Gets the unique name of this plugin instance.
    /// Must match the name specified in the pipeline configuration.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Gets the output schema produced by this plugin.
    /// For sink plugins, this may be null as they don't produce output.
    /// </summary>
    ISchema? OutputSchema { get; }

    /// <summary>
    /// Gets the plugin version for compatibility checking.
    /// Should follow semantic versioning (e.g., "1.0.0").
    /// </summary>
    string Version { get; }

    /// <summary>
    /// Gets optional metadata about this plugin for diagnostics and monitoring.
    /// </summary>
    IReadOnlyDictionary<string, object>? Metadata { get; }

    /// <summary>
    /// Initializes the plugin with configuration and validates settings.
    /// Called once during pipeline startup before any data processing.
    /// </summary>
    /// <param name="config">Plugin-specific configuration from YAML</param>
    /// <param name="cancellationToken">Cancellation token for initialization timeout</param>
    /// <returns>Task representing the initialization operation</returns>
    /// <exception cref="PluginInitializationException">Thrown when initialization fails</exception>
    Task InitializeAsync(IConfiguration config, CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates that this plugin can process the given input schema.
    /// Called during pipeline validation before execution starts.
    /// </summary>
    /// <param name="inputSchema">The schema of data this plugin will receive</param>
    /// <returns>Validation result indicating compatibility</returns>
    PluginValidationResult ValidateInputSchema(ISchema? inputSchema);

    /// <summary>
    /// Gets performance metrics and status information for monitoring.
    /// Called periodically during pipeline execution for observability.
    /// </summary>
    /// <returns>Current plugin performance metrics</returns>
    PluginMetrics GetMetrics();
}

/// <summary>
/// Result of plugin input schema validation.
/// </summary>
public sealed record PluginValidationResult
{
    /// <summary>
    /// Gets whether the validation passed.
    /// </summary>
    public bool IsValid { get; init; }

    /// <summary>
    /// Gets validation error messages if validation failed.
    /// </summary>
    public IReadOnlyList<string> Errors { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Gets optional warnings that don't prevent execution but may affect performance.
    /// </summary>
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Gets the plugin type that was validated (for plugin manager use).
    /// </summary>
    public Type? PluginType { get; init; }

    /// <summary>
    /// Creates a successful validation result.
    /// </summary>
    public static PluginValidationResult Success() => new() { IsValid = true };

    /// <summary>
    /// Creates a successful validation result with plugin type.
    /// </summary>
    /// <param name="pluginType">The validated plugin type</param>
    /// <param name="warnings">Optional warning messages</param>
    public static PluginValidationResult Success(Type? pluginType, params string[] warnings) =>
        new() { IsValid = true, PluginType = pluginType, Warnings = warnings };

    /// <summary>
    /// Creates a successful validation result with warnings.
    /// </summary>
    /// <param name="warnings">Warning messages</param>
    public static PluginValidationResult SuccessWithWarnings(params string[] warnings) => 
        new() { IsValid = true, Warnings = warnings };

    /// <summary>
    /// Creates a failed validation result.
    /// </summary>
    /// <param name="errors">Error messages</param>
    public static PluginValidationResult Failure(params string[] errors) => 
        new() { IsValid = false, Errors = errors };
}

/// <summary>
/// Performance metrics for plugin monitoring and optimization.
/// </summary>
public sealed record PluginMetrics
{
    /// <summary>
    /// Gets the total number of chunks processed by this plugin.
    /// </summary>
    public long TotalChunksProcessed { get; init; }

    /// <summary>
    /// Gets the total number of rows processed by this plugin.
    /// </summary>
    public long TotalRowsProcessed { get; init; }

    /// <summary>
    /// Gets the total processing time for all operations.
    /// </summary>
    public TimeSpan TotalProcessingTime { get; init; }

    /// <summary>
    /// Gets the current memory usage of this plugin in bytes.
    /// </summary>
    public long CurrentMemoryUsage { get; init; }

    /// <summary>
    /// Gets the peak memory usage of this plugin in bytes.
    /// </summary>
    public long PeakMemoryUsage { get; init; }

    /// <summary>
    /// Gets the number of errors encountered during processing.
    /// </summary>
    public long ErrorCount { get; init; }

    /// <summary>
    /// Gets the timestamp when these metrics were captured.
    /// </summary>
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Gets optional plugin-specific metrics.
    /// </summary>
    public IReadOnlyDictionary<string, object>? CustomMetrics { get; init; }

    /// <summary>
    /// Calculates the average processing time per chunk.
    /// </summary>
    public TimeSpan AverageChunkProcessingTime => 
        TotalChunksProcessed > 0 ? TimeSpan.FromTicks(TotalProcessingTime.Ticks / TotalChunksProcessed) : TimeSpan.Zero;

    /// <summary>
    /// Calculates the throughput in rows per second.
    /// </summary>
    public double RowsPerSecond => 
        TotalProcessingTime.TotalSeconds > 0 ? TotalRowsProcessed / TotalProcessingTime.TotalSeconds : 0.0;
}