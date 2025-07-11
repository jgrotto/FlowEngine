using FlowEngine.Abstractions.Configuration;

namespace FlowEngine.Core.Configuration;

/// <summary>
/// Implementation of monitoring configuration from YAML data.
/// </summary>
internal sealed class MonitoringConfiguration : IMonitoringConfiguration
{
    private readonly MonitoringData _data;

    public MonitoringConfiguration(MonitoringData data)
    {
        _data = data ?? throw new ArgumentNullException(nameof(data));
    }

    /// <inheritdoc />
    public bool EnableMetrics => _data.EnableMetrics;

    /// <inheritdoc />
    public TimeSpan MetricsInterval => TimeSpan.FromSeconds(_data.MetricsIntervalSeconds);

    /// <inheritdoc />
    public bool EnableTracing => _data.EnableTracing;

    /// <inheritdoc />
    public IReadOnlyDictionary<string, object> Custom =>
        _data.Custom ?? (IReadOnlyDictionary<string, object>)new Dictionary<string, object>();
}
