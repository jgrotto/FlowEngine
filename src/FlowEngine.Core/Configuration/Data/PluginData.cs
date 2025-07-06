namespace FlowEngine.Core.Configuration;

internal sealed class PluginData
{
    public string? Name { get; set; }
    public string? Type { get; set; }
    public string? Assembly { get; set; }
    public SchemaData? Schema { get; set; }
    public Dictionary<string, object>? Config { get; set; }
    public ResourceLimitsData? ResourceLimits { get; set; }
}