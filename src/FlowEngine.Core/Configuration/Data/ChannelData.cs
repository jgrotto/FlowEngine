namespace FlowEngine.Core.Configuration;

internal sealed class ChannelData
{
    public int BufferSize { get; set; } = 1000;
    public int BackpressureThreshold { get; set; } = 80;
    public string? FullMode { get; set; } = "Wait";
    public int TimeoutSeconds { get; set; } = 30;
}