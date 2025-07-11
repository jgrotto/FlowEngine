using FlowEngine.Abstractions.Configuration;

namespace FlowEngine.Core.Configuration;

/// <summary>
/// Implementation of field configuration from YAML data.
/// </summary>
internal sealed class FieldConfiguration : IFieldConfiguration
{
    private readonly FieldData _data;

    public FieldConfiguration(FieldData data)
    {
        _data = data ?? throw new ArgumentNullException(nameof(data));
    }

    /// <inheritdoc />
    public string Name => _data.Name ?? throw new InvalidOperationException("Field name is required");

    /// <inheritdoc />
    public string Type => _data.Type ?? throw new InvalidOperationException("Field type is required");

    /// <inheritdoc />
    public bool Required => _data.Required;

    /// <inheritdoc />
    public object? Default => _data.Default;

    /// <inheritdoc />
    public IReadOnlyDictionary<string, object>? Metadata => _data.Metadata;
}
