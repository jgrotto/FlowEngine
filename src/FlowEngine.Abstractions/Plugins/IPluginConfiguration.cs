using FlowEngine.Abstractions.Data;
using System.Collections.Immutable;

namespace FlowEngine.Abstractions.Plugins;

/// <summary>
/// Represents configuration for a plugin with schema and performance optimization support.
/// Implements ArrayRow optimization through pre-calculated field indexes.
/// </summary>
public interface IPluginConfiguration
{
    /// <summary>
    /// Gets the unique identifier for this plugin instance.
    /// </summary>
    string PluginId { get; }

    /// <summary>
    /// Gets the display name of this plugin.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Gets the version of this plugin.
    /// </summary>
    string Version { get; }

    /// <summary>
    /// Gets the type of this plugin (Source, Transform, Sink).
    /// </summary>
    string PluginType { get; }

    /// <summary>
    /// Gets the input schema expected by this plugin.
    /// Null for source plugins that don't consume input.
    /// </summary>
    ISchema? InputSchema { get; }

    /// <summary>
    /// Gets the output schema produced by this plugin.
    /// Null for sink plugins that don't produce output.
    /// </summary>
    ISchema? OutputSchema { get; }

    /// <summary>
    /// Gets whether this plugin supports hot-swapping of configuration.
    /// </summary>
    bool SupportsHotSwapping { get; }

    /// <summary>
    /// Gets pre-calculated field indexes for input schema (ArrayRow optimization).
    /// Maps field names to zero-based column indexes for O(1) field access.
    /// </summary>
    ImmutableDictionary<string, int> InputFieldIndexes { get; }

    /// <summary>
    /// Gets pre-calculated field indexes for output schema (ArrayRow optimization).
    /// Maps field names to zero-based column indexes for O(1) field access.
    /// </summary>
    ImmutableDictionary<string, int> OutputFieldIndexes { get; }

    /// <summary>
    /// Gets configuration properties for this plugin.
    /// </summary>
    ImmutableDictionary<string, object> Properties { get; }

    /// <summary>
    /// Gets the zero-based index of an input field for ArrayRow optimization.
    /// </summary>
    /// <param name="fieldName">Name of the field</param>
    /// <returns>Zero-based column index</returns>
    /// <exception cref="ArgumentException">Thrown if field is not found</exception>
    int GetInputFieldIndex(string fieldName);

    /// <summary>
    /// Gets the zero-based index of an output field for ArrayRow optimization.
    /// </summary>
    /// <param name="fieldName">Name of the field</param>
    /// <returns>Zero-based column index</returns>
    /// <exception cref="ArgumentException">Thrown if field is not found</exception>
    int GetOutputFieldIndex(string fieldName);

    /// <summary>
    /// Determines if this configuration is compatible with the given input schema.
    /// </summary>
    /// <param name="inputSchema">Schema to check compatibility with</param>
    /// <returns>True if compatible, false otherwise</returns>
    bool IsCompatibleWith(ISchema inputSchema);

    /// <summary>
    /// Gets a strongly-typed property value.
    /// </summary>
    /// <typeparam name="T">Type of the property</typeparam>
    /// <param name="key">Property key</param>
    /// <returns>Property value</returns>
    /// <exception cref="KeyNotFoundException">Thrown if key is not found</exception>
    /// <exception cref="InvalidCastException">Thrown if value cannot be cast to T</exception>
    T GetProperty<T>(string key);

    /// <summary>
    /// Attempts to get a strongly-typed property value.
    /// </summary>
    /// <typeparam name="T">Type of the property</typeparam>
    /// <param name="key">Property key</param>
    /// <param name="value">Property value if found</param>
    /// <returns>True if property found and successfully cast, false otherwise</returns>
    bool TryGetProperty<T>(string key, out T? value);
}