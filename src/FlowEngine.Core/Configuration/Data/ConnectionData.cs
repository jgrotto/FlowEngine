namespace FlowEngine.Core.Configuration;

internal sealed class ConnectionData
{
    public string? From { get; set; }
    public string? To { get; set; }
    public string? FromPort { get; set; }
    public string? ToPort { get; set; }
    public ChannelData? Channel { get; set; }
}
