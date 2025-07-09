using FlowEngine.Abstractions;
using FlowEngine.Abstractions.Configuration;
using FlowEngine.Abstractions.Data;
using FlowEngine.Abstractions.Plugins;
using System.Collections.Immutable;

namespace FlowEngine.Core.Configuration;

/// <summary>
/// Dynamic plugin configuration wrapper that holds a discovered configuration object.
/// Provides type-safe access to plugin configuration through reflection.
/// </summary>
public sealed class DynamicPluginConfiguration : FlowEngine.Abstractions.Plugins.IPluginConfiguration
{
    private readonly IPluginDefinition _definition;
    private readonly object _configurationInstance;

    /// <summary>
    /// Initializes a new dynamic plugin configuration.
    /// </summary>
    /// <param name="definition">Plugin definition</param>
    /// <param name="configurationInstance">Configuration instance created via reflection</param>
    public DynamicPluginConfiguration(IPluginDefinition definition, object configurationInstance)
    {
        _definition = definition ?? throw new ArgumentNullException(nameof(definition));
        _configurationInstance = configurationInstance ?? throw new ArgumentNullException(nameof(configurationInstance));
    }

    /// <inheritdoc />
    public string Name => _definition.Name;

    /// <inheritdoc />
    public string PluginId => _definition.Name;

    /// <inheritdoc />
    public string Version => "1.0.0";

    /// <inheritdoc />
    public string PluginType => _definition.Type;

    /// <inheritdoc />
    public ISchema? InputSchema => ConvertSchemaDefinition(_definition.InputSchema);

    /// <inheritdoc />
    public ISchema? OutputSchema => ConvertSchemaDefinition(_definition.OutputSchema);

    /// <inheritdoc />
    public bool SupportsHotSwapping => _definition.SupportsHotSwapping;

    /// <inheritdoc />
    public ImmutableDictionary<string, int> InputFieldIndexes => ImmutableDictionary<string, int>.Empty;

    /// <inheritdoc />
    public ImmutableDictionary<string, int> OutputFieldIndexes => ImmutableDictionary<string, int>.Empty;

    /// <inheritdoc />
    public ImmutableDictionary<string, object> Properties => _definition.Configuration?.ToImmutableDictionary() ?? ImmutableDictionary<string, object>.Empty;

    /// <summary>
    /// Gets the underlying configuration instance.
    /// </summary>
    public object ConfigurationInstance => _configurationInstance;

    /// <inheritdoc />
    public int GetInputFieldIndex(string fieldName) => -1;

    /// <inheritdoc />
    public int GetOutputFieldIndex(string fieldName) => -1;

    /// <inheritdoc />
    public bool IsCompatibleWith(ISchema inputSchema) => true;

    /// <inheritdoc />
    public T GetProperty<T>(string key)
    {
        // Try to get from original configuration first
        if (Properties.TryGetValue(key, out var value) && value is T typed)
            return typed;

        // Try to get from configuration instance via reflection
        try
        {
            var property = _configurationInstance.GetType().GetProperty(key);
            if (property != null && property.CanRead)
            {
                var propertyValue = property.GetValue(_configurationInstance);
                if (propertyValue is T propertyTyped)
                    return propertyTyped;
            }
        }
        catch
        {
            // Ignore reflection errors
        }

        return default!;
    }

    /// <inheritdoc />
    public bool TryGetProperty<T>(string key, out T? value)
    {
        value = default;

        // Try to get from original configuration first
        if (Properties.TryGetValue(key, out var obj) && obj is T typed)
        {
            value = typed;
            return true;
        }

        // Try to get from configuration instance via reflection
        try
        {
            var property = _configurationInstance.GetType().GetProperty(key);
            if (property != null && property.CanRead)
            {
                var propertyValue = property.GetValue(_configurationInstance);
                if (propertyValue is T propertyTyped)
                {
                    value = propertyTyped;
                    return true;
                }
            }
        }
        catch
        {
            // Ignore reflection errors
        }

        return false;
    }

    /// <summary>
    /// Converts ISchemaDefinition to ISchema for runtime processing.
    /// </summary>
    /// <param name="schemaDefinition">Schema definition from configuration</param>
    /// <returns>Runtime schema or null if no definition provided</returns>
    private ISchema? ConvertSchemaDefinition(ISchemaDefinition? schemaDefinition)
    {
        if (schemaDefinition == null)
            return null;

        // For now, return null as schema conversion is not fully implemented
        // TODO: Implement proper schema conversion from ISchemaDefinition to ISchema
        // This would require access to ISchemaFactory and proper type parsing
        return null;
    }
}