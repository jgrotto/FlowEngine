using FlowEngine.Abstractions;
using FlowEngine.Abstractions.Data;
using FlowEngine.Abstractions.Plugins;
using System.Collections.Immutable;

namespace FlowEngine.Core.Configuration;

/// <summary>
/// Configuration for JavaScriptTransform plugin with script execution options.
/// </summary>
public sealed class JavaScriptTransformConfiguration : IPluginConfiguration
{
    /// <inheritdoc />
    public required string Name { get; init; }

    /// <inheritdoc />
    public string PluginId => Name;

    /// <inheritdoc />
    public string Version => "1.0.0";

    /// <inheritdoc />
    public string PluginType => "Transform";

    /// <summary>
    /// JavaScript code to execute for data transformation.
    /// </summary>
    public required string Script { get; init; }

    /// <summary>
    /// Number of rows to process in each chunk.
    /// </summary>
    public int ChunkSize { get; init; } = 1000;

    /// <summary>
    /// Timeout in seconds for script execution.
    /// </summary>
    public int TimeoutSeconds { get; init; } = 30;

    /// <inheritdoc />
    public ISchema? InputSchema => null;

    /// <inheritdoc />
    public ISchema? OutputSchema => null;

    /// <inheritdoc />
    public bool SupportsHotSwapping => false;

    /// <inheritdoc />
    public ImmutableDictionary<string, int> InputFieldIndexes => ImmutableDictionary<string, int>.Empty;

    /// <inheritdoc />
    public ImmutableDictionary<string, int> OutputFieldIndexes => ImmutableDictionary<string, int>.Empty;

    /// <inheritdoc />
    public ImmutableDictionary<string, object> Properties { get; init; } = ImmutableDictionary<string, object>.Empty;

    /// <inheritdoc />
    public int GetInputFieldIndex(string fieldName) => -1;

    /// <inheritdoc />
    public int GetOutputFieldIndex(string fieldName) => -1;

    /// <inheritdoc />
    public bool IsCompatibleWith(ISchema inputSchema) => true;

    /// <inheritdoc />
    public T GetProperty<T>(string key) => Properties.TryGetValue(key, out var value) && value is T typed ? typed : default!;

    /// <inheritdoc />
    public bool TryGetProperty<T>(string key, out T? value)
    {
        value = default;
        if (Properties.TryGetValue(key, out var obj) && obj is T typed)
        {
            value = typed;
            return true;
        }
        return false;
    }
}