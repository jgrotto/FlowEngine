using FlowEngine.Abstractions.Configuration;

namespace FlowEngine.Core.Configuration;

/// <summary>
/// Implementation of resource limits from YAML data.
/// </summary>
internal sealed class ResourceLimits : IResourceLimits
{
    private readonly ResourceLimitsData _data;

    public ResourceLimits(ResourceLimitsData data)
    {
        _data = data ?? throw new ArgumentNullException(nameof(data));
    }

    /// <inheritdoc />
    public int? MaxMemoryMB => _data.MaxMemoryMB;

    /// <inheritdoc />
    public int? TimeoutSeconds => _data.TimeoutSeconds;

    /// <inheritdoc />
    public bool? EnableSpillover => _data.EnableSpillover;

    /// <inheritdoc />
    public int? MaxFileHandles => _data.MaxFileHandles;
}
