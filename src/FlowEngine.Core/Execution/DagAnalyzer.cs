using FlowEngine.Abstractions.Configuration;
using FlowEngine.Abstractions.Execution;

namespace FlowEngine.Core.Execution;

/// <summary>
/// High-performance DAG analyzer with cycle detection and topological sorting.
/// Provides comprehensive dependency analysis for pipeline execution planning.
/// </summary>
public sealed class DagAnalyzer : IDagAnalyzer
{
    /// <inheritdoc />
    public DagAnalysisResult AnalyzePipeline(IPipelineConfiguration configuration)
    {
        if (configuration == null)
        {
            throw new ArgumentNullException(nameof(configuration));
        }

        var graph = BuildDependencyGraph(configuration);
        var cycles = DetectCyclesInternal(graph);
        var isValid = cycles.Count == 0;
        var warnings = new List<string>();

        // Analyze graph characteristics
        var isolatedNodes = FindIsolatedNodes(graph);
        var sourceNodes = FindSourceNodes(graph);
        var sinkNodes = FindSinkNodes(graph);

        // Add warnings for unusual graph structures
        if (isolatedNodes.Count > 0)
        {
            warnings.Add($"Found {isolatedNodes.Count} isolated plugin(s) with no connections: {string.Join(", ", isolatedNodes)}");
        }

        if (sourceNodes.Count == 0 && graph.Nodes.Count > 0)
        {
            warnings.Add("No source plugins found - pipeline may not receive input data");
        }

        if (sinkNodes.Count == 0 && graph.Nodes.Count > 0)
        {
            warnings.Add("No sink plugins found - processed data may not be output anywhere");
        }

        // Determine execution order
        var executionOrder = isValid ? GetExecutionOrderInternal(graph) : Array.Empty<string>();

        return new DagAnalysisResult
        {
            IsValid = isValid,
            ExecutionOrder = executionOrder,
            Cycles = cycles,
            Graph = graph,
            Warnings = warnings,
            IsolatedNodes = isolatedNodes,
            SourceNodes = sourceNodes,
            SinkNodes = sinkNodes
        };
    }

    /// <inheritdoc />
    public IReadOnlyList<string> GetExecutionOrder(IPipelineConfiguration configuration)
    {
        if (configuration == null)
        {
            throw new ArgumentNullException(nameof(configuration));
        }

        var graph = BuildDependencyGraph(configuration);
        var cycles = DetectCyclesInternal(graph);

        if (cycles.Count > 0)
        {
            var cycleDescriptions = cycles.Select(c => c.Description);
            throw new CyclicDependencyException(
                $"Cannot determine execution order due to {cycles.Count} cycle(s): {string.Join("; ", cycleDescriptions)}",
                cycles);
        }

        return GetExecutionOrderInternal(graph);
    }

    /// <inheritdoc />
    public IReadOnlyList<DependencyCycle> DetectCycles(IPipelineConfiguration configuration)
    {
        if (configuration == null)
        {
            throw new ArgumentNullException(nameof(configuration));
        }

        var graph = BuildDependencyGraph(configuration);
        return DetectCyclesInternal(graph);
    }

    /// <inheritdoc />
    public IReadOnlyList<string> GetDependencies(IPipelineConfiguration configuration, string pluginName)
    {
        if (configuration == null)
        {
            throw new ArgumentNullException(nameof(configuration));
        }

        if (string.IsNullOrWhiteSpace(pluginName))
        {
            throw new ArgumentException("Plugin name cannot be null or empty", nameof(pluginName));
        }

        var graph = BuildDependencyGraph(configuration);

        return graph.ReverseAdjacencyList.TryGetValue(pluginName, out var dependencies)
            ? dependencies
            : Array.Empty<string>();
    }

    /// <inheritdoc />
    public IReadOnlyList<string> GetDependents(IPipelineConfiguration configuration, string pluginName)
    {
        if (configuration == null)
        {
            throw new ArgumentNullException(nameof(configuration));
        }

        if (string.IsNullOrWhiteSpace(pluginName))
        {
            throw new ArgumentException("Plugin name cannot be null or empty", nameof(pluginName));
        }

        var graph = BuildDependencyGraph(configuration);

        return graph.AdjacencyList.TryGetValue(pluginName, out var dependents)
            ? dependents
            : Array.Empty<string>();
    }

    /// <inheritdoc />
    public DagValidationResult ValidateDag(IPipelineConfiguration configuration)
    {
        if (configuration == null)
        {
            throw new ArgumentNullException(nameof(configuration));
        }

        var errors = new List<string>();
        var warnings = new List<string>();

        try
        {
            var analysis = AnalyzePipeline(configuration);

            if (!analysis.IsValid)
            {
                foreach (var cycle in analysis.Cycles)
                {
                    errors.Add($"Cyclic dependency detected: {cycle.Description}");
                }
            }

            warnings.AddRange(analysis.Warnings);

            // Additional validation checks
            ValidatePluginConnections(configuration, errors, warnings);
            ValidatePortReferences(configuration, errors, warnings);

            return errors.Count > 0
                ? DagValidationResult.Failure(errors.ToArray())
                : DagValidationResult.Success(warnings.ToArray());
        }
        catch (Exception ex)
        {
            errors.Add($"DAG validation failed: {ex.Message}");
            return DagValidationResult.Failure(errors.ToArray());
        }
    }

    private static DependencyGraph BuildDependencyGraph(IPipelineConfiguration configuration)
    {
        var nodes = configuration.Plugins.Select(p => p.Name).ToHashSet();
        var edges = new List<DependencyEdge>();
        var adjacencyList = new Dictionary<string, List<string>>();
        var reverseAdjacencyList = new Dictionary<string, List<string>>();
        var inDegrees = new Dictionary<string, int>();
        var outDegrees = new Dictionary<string, int>();

        // Initialize dictionaries
        foreach (var node in nodes)
        {
            adjacencyList[node] = new List<string>();
            reverseAdjacencyList[node] = new List<string>();
            inDegrees[node] = 0;
            outDegrees[node] = 0;
        }

        // Build edges from connections
        foreach (var connection in configuration.Connections)
        {
            if (!nodes.Contains(connection.From))
            {
                throw new InvalidOperationException($"Connection references unknown source plugin: {connection.From}");
            }

            if (!nodes.Contains(connection.To))
            {
                throw new InvalidOperationException($"Connection references unknown destination plugin: {connection.To}");
            }

            var edge = new DependencyEdge
            {
                From = connection.From,
                To = connection.To,
                FromPort = connection.FromPort,
                ToPort = connection.ToPort,
                ConnectionConfig = connection
            };

            edges.Add(edge);
            adjacencyList[connection.From].Add(connection.To);
            reverseAdjacencyList[connection.To].Add(connection.From);
            outDegrees[connection.From]++;
            inDegrees[connection.To]++;
        }

        // Convert to readonly collections
        var readonlyAdjacencyList = adjacencyList.ToDictionary(
            kvp => kvp.Key,
            kvp => (IReadOnlyList<string>)kvp.Value.ToArray());

        var readonlyReverseAdjacencyList = reverseAdjacencyList.ToDictionary(
            kvp => kvp.Key,
            kvp => (IReadOnlyList<string>)kvp.Value.ToArray());

        return new DependencyGraph
        {
            Nodes = nodes,
            Edges = edges,
            AdjacencyList = readonlyAdjacencyList,
            ReverseAdjacencyList = readonlyReverseAdjacencyList,
            InDegrees = inDegrees,
            OutDegrees = outDegrees
        };
    }

    private static IReadOnlyList<DependencyCycle> DetectCyclesInternal(DependencyGraph graph)
    {
        var cycles = new List<DependencyCycle>();
        var visited = new HashSet<string>();
        var recursionStack = new HashSet<string>();
        var currentPath = new List<string>();

        foreach (var node in graph.Nodes)
        {
            if (!visited.Contains(node))
            {
                DetectCyclesRecursive(node, graph, visited, recursionStack, currentPath, cycles);
            }
        }

        return cycles;
    }

    private static void DetectCyclesRecursive(
        string node,
        DependencyGraph graph,
        HashSet<string> visited,
        HashSet<string> recursionStack,
        List<string> currentPath,
        List<DependencyCycle> cycles)
    {
        visited.Add(node);
        recursionStack.Add(node);
        currentPath.Add(node);

        if (graph.AdjacencyList.TryGetValue(node, out var neighbors))
        {
            foreach (var neighbor in neighbors)
            {
                if (!visited.Contains(neighbor))
                {
                    DetectCyclesRecursive(neighbor, graph, visited, recursionStack, currentPath, cycles);
                }
                else if (recursionStack.Contains(neighbor))
                {
                    // Found a cycle
                    var cycleStart = currentPath.IndexOf(neighbor);
                    var cycleNodes = currentPath.Skip(cycleStart).ToList();
                    var cycleEdges = BuildCycleEdges(cycleNodes, graph);

                    var cycle = new DependencyCycle
                    {
                        Plugins = cycleNodes,
                        Edges = cycleEdges
                    };

                    cycles.Add(cycle);
                }
            }
        }

        recursionStack.Remove(node);
        currentPath.RemoveAt(currentPath.Count - 1);
    }

    private static IReadOnlyList<DependencyEdge> BuildCycleEdges(List<string> cycleNodes, DependencyGraph graph)
    {
        var edges = new List<DependencyEdge>();

        for (int i = 0; i < cycleNodes.Count; i++)
        {
            var from = cycleNodes[i];
            var to = cycleNodes[(i + 1) % cycleNodes.Count];

            var edge = graph.Edges.FirstOrDefault(e => e.From == from && e.To == to);
            if (edge != null)
            {
                edges.Add(edge);
            }
        }

        return edges;
    }

    private static IReadOnlyList<string> GetExecutionOrderInternal(DependencyGraph graph)
    {
        // Kahn's algorithm for topological sorting
        var inDegrees = new Dictionary<string, int>(graph.InDegrees);
        var queue = new Queue<string>();
        var result = new List<string>();

        // Find all nodes with in-degree 0
        foreach (var node in graph.Nodes)
        {
            if (inDegrees[node] == 0)
            {
                queue.Enqueue(node);
            }
        }

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            result.Add(current);

            // Reduce in-degree for all neighbors
            if (graph.AdjacencyList.TryGetValue(current, out var neighbors))
            {
                foreach (var neighbor in neighbors)
                {
                    inDegrees[neighbor]--;
                    if (inDegrees[neighbor] == 0)
                    {
                        queue.Enqueue(neighbor);
                    }
                }
            }
        }

        // If result doesn't contain all nodes, there must be cycles
        if (result.Count != graph.Nodes.Count)
        {
            var cycles = DetectCyclesInternal(graph);
            var cycleDescriptions = cycles.Select(c => c.Description);
            throw new CyclicDependencyException(
                $"Topological sort failed due to cycles: {string.Join("; ", cycleDescriptions)}",
                cycles);
        }

        return result;
    }

    private static IReadOnlyList<string> FindIsolatedNodes(DependencyGraph graph)
    {
        return graph.Nodes
            .Where(node => graph.InDegrees[node] == 0 && graph.OutDegrees[node] == 0)
            .ToArray();
    }

    private static IReadOnlyList<string> FindSourceNodes(DependencyGraph graph)
    {
        return graph.Nodes
            .Where(node => graph.InDegrees[node] == 0 && graph.OutDegrees[node] > 0)
            .ToArray();
    }

    private static IReadOnlyList<string> FindSinkNodes(DependencyGraph graph)
    {
        return graph.Nodes
            .Where(node => graph.InDegrees[node] > 0 && graph.OutDegrees[node] == 0)
            .ToArray();
    }

    private static void ValidatePluginConnections(IPipelineConfiguration configuration, List<string> errors, List<string> warnings)
    {
        var pluginNames = configuration.Plugins.Select(p => p.Name).ToHashSet();

        foreach (var connection in configuration.Connections)
        {
            if (!pluginNames.Contains(connection.From))
            {
                errors.Add($"Connection references unknown source plugin: {connection.From}");
            }

            if (!pluginNames.Contains(connection.To))
            {
                errors.Add($"Connection references unknown destination plugin: {connection.To}");
            }

            // Self-connections
            if (connection.From == connection.To)
            {
                warnings.Add($"Plugin '{connection.From}' connects to itself - this may cause infinite loops");
            }
        }
    }

    private static void ValidatePortReferences(IPipelineConfiguration configuration, List<string> errors, List<string> warnings)
    {
        foreach (var connection in configuration.Connections)
        {
            // Validate port names if specified
            if (!string.IsNullOrWhiteSpace(connection.FromPort))
            {
                // In a full implementation, we would validate that the plugin actually has this port
                // For now, just warn about potential issues
                if (connection.FromPort.Contains(" ") || connection.FromPort.Contains("\t"))
                {
                    warnings.Add($"Port name '{connection.FromPort}' contains whitespace - this may cause issues");
                }
            }

            if (!string.IsNullOrWhiteSpace(connection.ToPort))
            {
                if (connection.ToPort.Contains(" ") || connection.ToPort.Contains("\t"))
                {
                    warnings.Add($"Port name '{connection.ToPort}' contains whitespace - this may cause issues");
                }
            }
        }
    }
}
