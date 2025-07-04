namespace FlowEngine.Abstractions.Plugins;

/// <summary>
/// Temporary stub interface for Sprint 1 MVP.
/// TODO: Define proper configurable plugin interface.
/// </summary>
public interface IConfigurablePlugin : IPlugin
{
    /// <summary>
    /// Configures the plugin with the specified settings.
    /// </summary>
    /// <param name="config">Configuration settings</param>
    /// <returns>Task that completes when configuration is applied</returns>
    Task ConfigureAsync(IReadOnlyDictionary<string, object> config);
}