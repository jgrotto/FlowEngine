namespace FlowEngine.Core.Configuration;

internal sealed class SchemaData
{
    public string? Name { get; set; }
    public string? Version { get; set; }
    public List<FieldData>? Fields { get; set; }
    public Dictionary<string, object>? Metadata { get; set; }
}
