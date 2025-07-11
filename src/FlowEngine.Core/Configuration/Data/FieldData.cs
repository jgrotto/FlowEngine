namespace FlowEngine.Core.Configuration;

internal sealed class FieldData
{
    public string? Name { get; set; }
    public string? Type { get; set; }
    public bool Required { get; set; } = true;
    public object? Default { get; set; }
    public Dictionary<string, object>? Metadata { get; set; }
}
