using FlowEngine.Abstractions.Configuration;

namespace FlowEngine.Core.Configuration;

/// <summary>
/// Implementation of connection configuration from YAML data.
/// </summary>
internal sealed class ConnectionConfiguration : IConnectionConfiguration
{
    private readonly ConnectionData _data;

    public ConnectionConfiguration(ConnectionData data)
    {
        _data = data ?? throw new ArgumentNullException(nameof(data));
    }

    /// <inheritdoc />
    public string From => _data.From ?? throw new InvalidOperationException("Connection source is required");

    /// <inheritdoc />
    public string To => _data.To ?? throw new InvalidOperationException("Connection destination is required");

    /// <inheritdoc />
    public string? FromPort => _data.FromPort;

    /// <inheritdoc />
    public string? ToPort => _data.ToPort;

    /// <inheritdoc />
    public IChannelConfiguration? Channel => _data.Channel != null ? new ChannelConfiguration(_data.Channel) : null;
}