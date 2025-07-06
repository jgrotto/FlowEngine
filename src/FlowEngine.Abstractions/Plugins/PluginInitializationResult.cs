using System.Collections.Immutable;

namespace FlowEngine.Abstractions.Plugins;

/// <summary>
/// Result of plugin initialization.
/// </summary>
public sealed record PluginInitializationResult
{
    /// <summary>
    /// Gets whether initialization was successful.
    /// </summary>
    public required bool Success { get; init; }

    /// <summary>
    /// Gets the initialization result message.
    /// </summary>
    public required string Message { get; init; }

    /// <summary>
    /// Gets the time taken for initialization.
    /// </summary>
    public required TimeSpan InitializationTime { get; init; }

    /// <summary>
    /// Gets any errors that occurred during initialization.
    /// </summary>
    public ImmutableArray<string> Errors { get; init; } = ImmutableArray<string>.Empty;

    /// <summary>
    /// Gets any warnings that occurred during initialization.
    /// </summary>
    public ImmutableArray<string> Warnings { get; init; } = ImmutableArray<string>.Empty;
}