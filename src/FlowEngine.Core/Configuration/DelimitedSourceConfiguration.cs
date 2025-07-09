using FlowEngine.Abstractions;
using FlowEngine.Abstractions.Data;
using FlowEngine.Abstractions.Plugins;
using System.Collections.Immutable;

namespace FlowEngine.Core.Configuration;

/// <summary>
/// Configuration for DelimitedSource plugin with comprehensive CSV reading options.
/// </summary>
public sealed class DelimitedSourceConfiguration : IPluginConfiguration
{
    /// <inheritdoc />
    public required string Name { get; init; }

    /// <inheritdoc />
    public string PluginId => Name;

    /// <inheritdoc />
    public string Version => "1.0.0";

    /// <inheritdoc />
    public string PluginType => "Source";

    /// <summary>
    /// Path to the CSV file to read from.
    /// </summary>
    public required string FilePath { get; init; }

    /// <summary>
    /// Whether the CSV file has headers in the first row.
    /// </summary>
    public bool HasHeaders { get; init; } = true;

    /// <summary>
    /// Field delimiter character (default: comma).
    /// </summary>
    public string Delimiter { get; init; } = ",";

    /// <summary>
    /// Number of rows to process in each chunk.
    /// </summary>
    public int ChunkSize { get; init; } = 1000;

    /// <summary>
    /// Text encoding of the CSV file.
    /// </summary>
    public string Encoding { get; init; } = "UTF-8";

    /// <summary>
    /// Whether to automatically infer schema from data.
    /// </summary>
    public bool InferSchema { get; init; } = true;

    /// <summary>
    /// Number of rows to sample for schema inference.
    /// </summary>
    public int InferenceSampleRows { get; init; } = 100;

    /// <summary>
    /// Whether to skip malformed rows instead of failing.
    /// </summary>
    public bool SkipMalformedRows { get; init; } = false;

    /// <summary>
    /// Maximum number of errors to tolerate before failing.
    /// </summary>
    public int MaxErrors { get; init; } = 0;

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