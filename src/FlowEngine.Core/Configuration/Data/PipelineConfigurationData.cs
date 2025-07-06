namespace FlowEngine.Core.Configuration;

/// <summary>
/// Data structure for deserializing YAML pipeline configuration.
/// </summary>
internal sealed class PipelineConfigurationData
{
    public PipelineData? Pipeline { get; set; }
}