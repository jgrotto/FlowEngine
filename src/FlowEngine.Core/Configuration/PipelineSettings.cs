using FlowEngine.Abstractions.Configuration;

namespace FlowEngine.Core.Configuration;

/// <summary>
/// Implementation of pipeline settings from YAML data.
/// </summary>
internal sealed class PipelineSettings : IPipelineSettings
{
    private readonly SettingsData? _data;

    public PipelineSettings(SettingsData? data)
    {
        _data = data;
    }

    /// <inheritdoc />
    public IChannelConfiguration? DefaultChannel =>
        _data?.DefaultChannel != null ? new ChannelConfiguration(_data.DefaultChannel) : null;

    /// <inheritdoc />
    public IResourceLimits? DefaultResourceLimits =>
        _data?.DefaultResourceLimits != null ? new ResourceLimits(_data.DefaultResourceLimits) : null;

    /// <inheritdoc />
    public IMonitoringConfiguration? Monitoring =>
        _data?.Monitoring != null ? new MonitoringConfiguration(_data.Monitoring) : null;

    /// <inheritdoc />
    public IReadOnlyDictionary<string, object> Custom =>
        _data?.Custom ?? (IReadOnlyDictionary<string, object>)new Dictionary<string, object>();
}
