namespace FlowEngine.Core.Configuration;

internal sealed class MonitoringData
{
    public bool EnableMetrics { get; set; } = true;
    public int MetricsIntervalSeconds { get; set; } = 10;
    public bool EnableTracing { get; set; } = false;
    public Dictionary<string, object>? Custom { get; set; }
}