using FlowEngine.Abstractions.Configuration;
using FlowEngine.Abstractions.Plugins;

namespace FlowEngine.Core.Configuration;

/// <summary>
/// Implementation of plugin definition from YAML data.
/// Represents the parsed plugin definition before conversion to runtime configuration.
/// </summary>
internal sealed class PluginConfiguration : IPluginDefinition
{
    private readonly PluginData _data;

    public PluginConfiguration(PluginData data)
    {
        _data = data ?? throw new ArgumentNullException(nameof(data));
    }

    /// <inheritdoc />
    public string Name => _data.Name ?? throw new InvalidOperationException("Plugin name is required");

    /// <inheritdoc />
    public string Type => _data.Type ?? throw new InvalidOperationException("Plugin type is required");

    /// <inheritdoc />
    public string? AssemblyPath => _data.Assembly;

    /// <inheritdoc />
    public ISchemaDefinition? InputSchema => null; // TODO: Implement schema parsing

    /// <inheritdoc />
    public ISchemaDefinition? OutputSchema => null; // TODO: Implement schema parsing

    /// <inheritdoc />
    public IReadOnlyDictionary<string, object> Configuration => 
        _data.Config ?? (IReadOnlyDictionary<string, object>)new Dictionary<string, object>();

    /// <inheritdoc />
    public bool SupportsHotSwapping => false; // TODO: Parse from YAML

    /// <inheritdoc />
    public IReadOnlyDictionary<string, object>? Metadata => null; // TODO: Parse from YAML

    // Legacy properties for backward compatibility
    public string? Assembly => _data.Assembly;
    public ISchemaConfiguration? Schema => _data.Schema != null ? new SchemaConfiguration(_data.Schema) : null;
    public IReadOnlyDictionary<string, object> Config => Configuration;
    public IResourceLimits? ResourceLimits => 
        _data.ResourceLimits != null ? new ResourceLimits(_data.ResourceLimits) : null;
}