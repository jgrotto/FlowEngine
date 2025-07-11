using FlowEngine.Abstractions.Data;

namespace FlowEngine.Abstractions.Execution;

/// <summary>
/// Information about a schema transformation step in the pipeline.
/// </summary>
public sealed record SchemaFlowStep
{
    /// <summary>
    /// Gets the plugin name that performs this transformation.
    /// </summary>
    public required string PluginName { get; init; }

    /// <summary>
    /// Gets the input schema for this step.
    /// </summary>
    public ISchema? InputSchema { get; init; }

    /// <summary>
    /// Gets the output schema for this step.
    /// </summary>
    public ISchema? OutputSchema { get; init; }

    /// <summary>
    /// Gets the schema transformation description.
    /// </summary>
    public string? Description { get; init; }
}
