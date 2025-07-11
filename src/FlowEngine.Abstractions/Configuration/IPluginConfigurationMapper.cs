using FlowEngine.Abstractions.Plugins;

namespace FlowEngine.Abstractions.Configuration;

/// <summary>
/// Interface for mapping plugin definitions to strongly-typed configuration objects.
/// Provides automatic type conversion and validation for plugin-specific configurations.
/// </summary>
public interface IPluginConfigurationMapper
{
    /// <summary>
    /// Creates a strongly-typed configuration object from a plugin definition.
    /// </summary>
    /// <param name="definition">Plugin definition containing configuration data</param>
    /// <returns>Strongly-typed plugin configuration</returns>
    /// <exception cref="PluginLoadException">Thrown when configuration creation fails</exception>
    Task<FlowEngine.Abstractions.Plugins.IPluginConfiguration> CreateConfigurationAsync(IPluginDefinition definition);

    /// <summary>
    /// Registers a configuration mapping for a specific plugin type.
    /// </summary>
    /// <param name="pluginType">Full type name of the plugin</param>
    /// <param name="mapping">Configuration mapping implementation</param>
    void RegisterMapping(string pluginType, PluginConfigurationMapping mapping);
}
