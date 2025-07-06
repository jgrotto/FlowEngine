namespace FlowEngine.Core.Configuration;

internal sealed class PipelineData
{
    public string? Name { get; set; }
    public string? Version { get; set; }
    public string? Description { get; set; }
    public List<PluginData>? Plugins { get; set; }
    public List<ConnectionData>? Connections { get; set; }
    public SettingsData? Settings { get; set; }
    public Dictionary<string, object>? Metadata { get; set; }
}