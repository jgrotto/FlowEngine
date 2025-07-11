namespace FlowEngine.Core.Configuration;

internal sealed class SettingsData
{
    public ChannelData? DefaultChannel { get; set; }
    public ResourceLimitsData? DefaultResourceLimits { get; set; }
    public MonitoringData? Monitoring { get; set; }
    public Dictionary<string, object>? Custom { get; set; }
}
