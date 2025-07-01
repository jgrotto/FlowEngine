using FlowEngine.Abstractions.Data;
using System.Collections.Immutable;

namespace FlowEngine.Abstractions.Plugins;

/// <summary>
/// Represents plugin configuration with schema definitions and metadata.
/// Provides the foundation for plugin setup and ArrayRow optimization.
/// </summary>
/// <remarks>
/// Plugin configurations define:
/// - Input and output schemas with explicit field definitions
/// - Performance optimization settings
/// - Plugin-specific parameters and options
/// - Schema compatibility and validation rules
/// 
/// For optimal performance, configurations should pre-calculate field indexes
/// to enable O(1) ArrayRow field access during data processing.
/// </remarks>
public interface IPluginConfiguration
{
    /// <summary>
    /// Gets the unique plugin identifier.
    /// </summary>
    string PluginId { get; }

    /// <summary>
    /// Gets the plugin name.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Gets the plugin version.
    /// </summary>
    string Version { get; }

    /// <summary>
    /// Gets the input data schema definition.
    /// </summary>
    ISchema InputSchema { get; }

    /// <summary>
    /// Gets the output data schema definition.
    /// </summary>
    ISchema OutputSchema { get; }

    /// <summary>
    /// Gets whether this plugin supports hot-swapping configuration updates.
    /// </summary>
    bool SupportsHotSwapping { get; }

    /// <summary>
    /// Gets the pre-calculated field indexes for input schema optimization.
    /// Maps field names to zero-based array indexes for O(1) ArrayRow access.
    /// </summary>
    /// <remarks>
    /// These indexes MUST be sequential (0, 1, 2, ...) for optimal performance.
    /// Use GetInputFieldIndex(fieldName) for safe field access.
    /// </remarks>
    ImmutableDictionary<string, int> InputFieldIndexes { get; }

    /// <summary>
    /// Gets the pre-calculated field indexes for output schema optimization.
    /// Maps field names to zero-based array indexes for O(1) ArrayRow access.
    /// </summary>
    /// <remarks>
    /// These indexes MUST be sequential (0, 1, 2, ...) for optimal performance.
    /// Use GetOutputFieldIndex(fieldName) for safe field access.
    /// </remarks>
    ImmutableDictionary<string, int> OutputFieldIndexes { get; }

    /// <summary>
    /// Gets the field index for an input field name with O(1) lookup.
    /// </summary>
    /// <param name="fieldName">Name of the input field</param>
    /// <returns>Zero-based field index</returns>
    /// <exception cref="ArgumentException">Field not found in input schema</exception>
    int GetInputFieldIndex(string fieldName);

    /// <summary>
    /// Gets the field index for an output field name with O(1) lookup.
    /// </summary>
    /// <param name="fieldName">Name of the output field</param>
    /// <returns>Zero-based field index</returns>
    /// <exception cref="ArgumentException">Field not found in output schema</exception>
    int GetOutputFieldIndex(string fieldName);

    /// <summary>
    /// Validates whether this configuration is compatible with the specified input schema.
    /// </summary>
    /// <param name="inputSchema">Input schema to check compatibility</param>
    /// <returns>True if schemas are compatible</returns>
    bool IsCompatibleWith(ISchema inputSchema);

    /// <summary>
    /// Gets plugin-specific configuration properties.
    /// </summary>
    ImmutableDictionary<string, object> Properties { get; }

    /// <summary>
    /// Gets a strongly-typed configuration property.
    /// </summary>
    /// <typeparam name="T">Expected property type</typeparam>
    /// <param name="key">Property key</param>
    /// <returns>Property value</returns>
    /// <exception cref="KeyNotFoundException">Property not found</exception>
    /// <exception cref="InvalidCastException">Property cannot be cast to specified type</exception>
    T GetProperty<T>(string key);

    /// <summary>
    /// Attempts to get a strongly-typed configuration property.
    /// </summary>
    /// <typeparam name="T">Expected property type</typeparam>
    /// <param name="key">Property key</param>
    /// <param name="value">Property value if found</param>
    /// <returns>True if property exists and can be cast to specified type</returns>
    bool TryGetProperty<T>(string key, out T? value);
}