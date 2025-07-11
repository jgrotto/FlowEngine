using FlowEngine.Abstractions.Configuration;

namespace FlowEngine.Abstractions.Execution;

/// <summary>
/// Interface for analyzing and validating Directed Acyclic Graph (DAG) structures in pipelines.
/// Provides cycle detection, topological sorting, and dependency analysis.
/// </summary>
public interface IDagAnalyzer
{
    /// <summary>
    /// Analyzes a pipeline configuration and builds a DAG representation.
    /// </summary>
    /// <param name="configuration">Pipeline configuration to analyze</param>
    /// <returns>DAG analysis result</returns>
    DagAnalysisResult AnalyzePipeline(IPipelineConfiguration configuration);

    /// <summary>
    /// Performs topological sorting to determine execution order.
    /// </summary>
    /// <param name="configuration">Pipeline configuration</param>
    /// <returns>Ordered list of plugin names for execution</returns>
    /// <exception cref="CyclicDependencyException">Thrown when cycles are detected</exception>
    IReadOnlyList<string> GetExecutionOrder(IPipelineConfiguration configuration);

    /// <summary>
    /// Detects cycles in the pipeline dependency graph.
    /// </summary>
    /// <param name="configuration">Pipeline configuration</param>
    /// <returns>List of cycles found, empty if no cycles</returns>
    IReadOnlyList<DependencyCycle> DetectCycles(IPipelineConfiguration configuration);

    /// <summary>
    /// Gets all dependencies for a specific plugin.
    /// </summary>
    /// <param name="configuration">Pipeline configuration</param>
    /// <param name="pluginName">Plugin to get dependencies for</param>
    /// <returns>List of plugin names that this plugin depends on</returns>
    IReadOnlyList<string> GetDependencies(IPipelineConfiguration configuration, string pluginName);

    /// <summary>
    /// Gets all dependents for a specific plugin.
    /// </summary>
    /// <param name="configuration">Pipeline configuration</param>
    /// <param name="pluginName">Plugin to get dependents for</param>
    /// <returns>List of plugin names that depend on this plugin</returns>
    IReadOnlyList<string> GetDependents(IPipelineConfiguration configuration, string pluginName);

    /// <summary>
    /// Validates that the pipeline forms a valid DAG structure.
    /// </summary>
    /// <param name="configuration">Pipeline configuration</param>
    /// <returns>Validation result</returns>
    DagValidationResult ValidateDag(IPipelineConfiguration configuration);
}

/// <summary>
/// Result of DAG analysis.
/// </summary>
public sealed record DagAnalysisResult
{
    /// <summary>
    /// Gets whether the DAG is valid (acyclic).
    /// </summary>
    public required bool IsValid { get; init; }

    /// <summary>
    /// Gets the execution order determined by topological sort.
    /// </summary>
    public IReadOnlyList<string> ExecutionOrder { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Gets any cycles detected in the graph.
    /// </summary>
    public IReadOnlyList<DependencyCycle> Cycles { get; init; } = Array.Empty<DependencyCycle>();

    /// <summary>
    /// Gets the dependency graph representation.
    /// </summary>
    public required DependencyGraph Graph { get; init; }

    /// <summary>
    /// Gets analysis warnings.
    /// </summary>
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Gets isolated nodes (plugins with no connections).
    /// </summary>
    public IReadOnlyList<string> IsolatedNodes { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Gets source nodes (plugins with no incoming connections).
    /// </summary>
    public IReadOnlyList<string> SourceNodes { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Gets sink nodes (plugins with no outgoing connections).
    /// </summary>
    public IReadOnlyList<string> SinkNodes { get; init; } = Array.Empty<string>();
}

/// <summary>
/// Result of DAG validation.
/// </summary>
public sealed record DagValidationResult
{
    /// <summary>
    /// Gets whether the DAG is valid.
    /// </summary>
    public required bool IsValid { get; init; }

    /// <summary>
    /// Gets validation error messages.
    /// </summary>
    public IReadOnlyList<string> Errors { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Gets validation warning messages.
    /// </summary>
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Creates a successful validation result.
    /// </summary>
    /// <param name="warnings">Optional warnings</param>
    public static DagValidationResult Success(params string[] warnings) =>
        new() { IsValid = true, Warnings = warnings };

    /// <summary>
    /// Creates a failed validation result.
    /// </summary>
    /// <param name="errors">Error messages</param>
    public static DagValidationResult Failure(params string[] errors) =>
        new() { IsValid = false, Errors = errors };
}

/// <summary>
/// Represents a dependency cycle in the pipeline.
/// </summary>
public sealed record DependencyCycle
{
    /// <summary>
    /// Gets the plugins involved in the cycle.
    /// </summary>
    public required IReadOnlyList<string> Plugins { get; init; }

    /// <summary>
    /// Gets the connections that form the cycle.
    /// </summary>
    public required IReadOnlyList<DependencyEdge> Edges { get; init; }

    /// <summary>
    /// Gets a human-readable description of the cycle.
    /// </summary>
    public string Description => $"Cycle: {string.Join(" → ", Plugins)} → {Plugins[0]}";
}

/// <summary>
/// Represents the dependency graph structure.
/// </summary>
public sealed record DependencyGraph
{
    /// <summary>
    /// Gets all nodes (plugins) in the graph.
    /// </summary>
    public required IReadOnlySet<string> Nodes { get; init; }

    /// <summary>
    /// Gets all edges (connections) in the graph.
    /// </summary>
    public required IReadOnlyList<DependencyEdge> Edges { get; init; }

    /// <summary>
    /// Gets the adjacency list representation.
    /// Key is the plugin name, value is the list of plugins it connects to.
    /// </summary>
    public required IReadOnlyDictionary<string, IReadOnlyList<string>> AdjacencyList { get; init; }

    /// <summary>
    /// Gets the reverse adjacency list representation.
    /// Key is the plugin name, value is the list of plugins that connect to it.
    /// </summary>
    public required IReadOnlyDictionary<string, IReadOnlyList<string>> ReverseAdjacencyList { get; init; }

    /// <summary>
    /// Gets the in-degree for each node (number of incoming connections).
    /// </summary>
    public required IReadOnlyDictionary<string, int> InDegrees { get; init; }

    /// <summary>
    /// Gets the out-degree for each node (number of outgoing connections).
    /// </summary>
    public required IReadOnlyDictionary<string, int> OutDegrees { get; init; }
}

/// <summary>
/// Represents a dependency edge between two plugins.
/// </summary>
public sealed record DependencyEdge
{
    /// <summary>
    /// Gets the source plugin name.
    /// </summary>
    public required string From { get; init; }

    /// <summary>
    /// Gets the destination plugin name.
    /// </summary>
    public required string To { get; init; }

    /// <summary>
    /// Gets the source port name (if specified).
    /// </summary>
    public string? FromPort { get; init; }

    /// <summary>
    /// Gets the destination port name (if specified).
    /// </summary>
    public string? ToPort { get; init; }

    /// <summary>
    /// Gets the connection configuration.
    /// </summary>
    public IConnectionConfiguration? ConnectionConfig { get; init; }

    /// <summary>
    /// Gets a string representation of the edge.
    /// </summary>
    public override string ToString()
    {
        var fromPart = FromPort != null ? $"{From}:{FromPort}" : From;
        var toPart = ToPort != null ? $"{To}:{ToPort}" : To;
        return $"{fromPart} → {toPart}";
    }
}

/// <summary>
/// Exception thrown when cyclic dependencies are detected in the pipeline.
/// </summary>
public sealed class CyclicDependencyException : Exception
{
    /// <summary>
    /// Initializes a new cyclic dependency exception.
    /// </summary>
    /// <param name="message">Error message</param>
    /// <param name="cycles">Detected cycles</param>
    public CyclicDependencyException(string message, IReadOnlyList<DependencyCycle> cycles) : base(message)
    {
        Cycles = cycles ?? throw new ArgumentNullException(nameof(cycles));
    }

    /// <summary>
    /// Initializes a new cyclic dependency exception with inner exception.
    /// </summary>
    /// <param name="message">Error message</param>
    /// <param name="cycles">Detected cycles</param>
    /// <param name="innerException">Inner exception</param>
    public CyclicDependencyException(string message, IReadOnlyList<DependencyCycle> cycles, Exception innerException) : base(message, innerException)
    {
        Cycles = cycles ?? throw new ArgumentNullException(nameof(cycles));
    }

    /// <summary>
    /// Gets the cycles that were detected.
    /// </summary>
    public IReadOnlyList<DependencyCycle> Cycles { get; }
}

/// <summary>
/// Exception thrown when pipeline execution fails.
/// </summary>
public sealed class PipelineExecutionException : Exception
{
    /// <summary>
    /// Initializes a new pipeline execution exception.
    /// </summary>
    /// <param name="message">Error message</param>
    public PipelineExecutionException(string message) : base(message)
    {
    }

    /// <summary>
    /// Initializes a new pipeline execution exception with inner exception.
    /// </summary>
    /// <param name="message">Error message</param>
    /// <param name="innerException">Inner exception</param>
    public PipelineExecutionException(string message, Exception innerException) : base(message, innerException)
    {
    }

    /// <summary>
    /// Gets or sets the pipeline that failed.
    /// </summary>
    public string? PipelineName { get; init; }

    /// <summary>
    /// Gets or sets the plugin where the failure occurred.
    /// </summary>
    public string? FailedPlugin { get; init; }

    /// <summary>
    /// Gets or sets the execution context when the failure occurred.
    /// </summary>
    public IReadOnlyDictionary<string, object>? ExecutionContext { get; init; }
}
