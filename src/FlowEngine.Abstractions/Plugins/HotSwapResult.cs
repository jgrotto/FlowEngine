using System.Collections.Immutable;

namespace FlowEngine.Abstractions.Plugins;

/// <summary>
/// Result of a hot-swap operation.
/// </summary>
public sealed record HotSwapResult
{
    /// <summary>
    /// Gets whether the hot-swap was successful.
    /// </summary>
    public required bool Success { get; init; }

    /// <summary>
    /// Gets the hot-swap result message.
    /// </summary>
    public required string Message { get; init; }

    /// <summary>
    /// Gets the time taken for the hot-swap operation.
    /// </summary>
    public required TimeSpan SwapTime { get; init; }

    /// <summary>
    /// Gets any errors that occurred during hot-swap.
    /// </summary>
    public ImmutableArray<string> Errors { get; init; } = ImmutableArray<string>.Empty;
}
