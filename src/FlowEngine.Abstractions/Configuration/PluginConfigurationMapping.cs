using FlowEngine.Abstractions.Plugins;

namespace FlowEngine.Abstractions.Configuration;

/// <summary>
/// Configuration mapping definition for a specific plugin type.
/// Defines how to convert plugin definition data to strongly-typed configuration.
/// </summary>
public sealed class PluginConfigurationMapping
{
    /// <summary>
    /// Gets or sets the configuration type for this plugin.
    /// </summary>
    public Type ConfigurationType { get; set; } = typeof(object);

    /// <summary>
    /// Gets or sets the factory function for creating configuration instances.
    /// </summary>
    public Func<IPluginDefinition, Task<FlowEngine.Abstractions.Plugins.IPluginConfiguration>> CreateConfigurationAsync { get; set; } = 
        definition => Task.FromException<FlowEngine.Abstractions.Plugins.IPluginConfiguration>(new NotImplementedException("Configuration mapping not implemented"));

    /// <summary>
    /// Gets or sets the validation function for configuration data.
    /// </summary>
    public Func<IPluginDefinition, Task<ValidationResult>>? ValidateConfigurationAsync { get; set; }

    /// <summary>
    /// Gets or sets metadata about this configuration mapping.
    /// </summary>
    public IReadOnlyDictionary<string, object>? Metadata { get; set; }
}