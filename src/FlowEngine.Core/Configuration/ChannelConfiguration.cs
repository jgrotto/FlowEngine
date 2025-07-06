using FlowEngine.Abstractions.Configuration;

namespace FlowEngine.Core.Configuration;

/// <summary>
/// Implementation of channel configuration from YAML data.
/// </summary>
internal sealed class ChannelConfiguration : IChannelConfiguration
{
    private readonly ChannelData _data;

    public ChannelConfiguration(ChannelData data)
    {
        _data = data ?? throw new ArgumentNullException(nameof(data));
    }

    /// <inheritdoc />
    public int BufferSize => _data.BufferSize;

    /// <inheritdoc />
    public int BackpressureThreshold => _data.BackpressureThreshold;

    /// <inheritdoc />
    public ChannelFullMode FullMode => ParseFullMode(_data.FullMode);

    /// <inheritdoc />
    public TimeSpan Timeout => TimeSpan.FromSeconds(_data.TimeoutSeconds);

    private static ChannelFullMode ParseFullMode(string? mode)
    {
        return mode?.ToLowerInvariant() switch
        {
            "wait" => ChannelFullMode.Wait,
            "dropoldest" => ChannelFullMode.DropOldest,
            "dropnewest" => ChannelFullMode.DropNewest,
            "throwexception" => ChannelFullMode.ThrowException,
            _ => ChannelFullMode.Wait
        };
    }
}