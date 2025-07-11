using FlowEngine.Abstractions.Data;

namespace FlowEngine.Abstractions.Configuration;

/// <summary>
/// Represents a plugin definition loaded from YAML configuration.
/// This is the parsed representation before conversion to runtime IPluginConfiguration.
/// </summary>
public interface IPluginDefinition
{
    /// <summary>
    /// Gets the unique name of this plugin instance in the pipeline.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Gets the type name of the plugin to load.
    /// </summary>
    string Type { get; }

    /// <summary>
    /// Gets the assembly path where the plugin can be found.
    /// </summary>
    string? AssemblyPath { get; }

    /// <summary>
    /// Gets the input schema definition for this plugin.
    /// </summary>
    ISchemaDefinition? InputSchema { get; }

    /// <summary>
    /// Gets the output schema definition for this plugin.
    /// </summary>
    ISchemaDefinition? OutputSchema { get; }

    /// <summary>
    /// Gets the configuration properties for this plugin.
    /// </summary>
    IReadOnlyDictionary<string, object> Configuration { get; }

    /// <summary>
    /// Gets whether this plugin supports hot-swapping.
    /// </summary>
    bool SupportsHotSwapping { get; }

    /// <summary>
    /// Gets optional metadata for this plugin.
    /// </summary>
    IReadOnlyDictionary<string, object>? Metadata { get; }
}
