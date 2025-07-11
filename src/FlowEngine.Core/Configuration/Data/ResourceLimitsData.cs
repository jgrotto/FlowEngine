namespace FlowEngine.Core.Configuration;

internal sealed class ResourceLimitsData
{
    public int? MaxMemoryMB { get; set; }
    public int? TimeoutSeconds { get; set; }
    public bool? EnableSpillover { get; set; }
    public int? MaxFileHandles { get; set; }
}
