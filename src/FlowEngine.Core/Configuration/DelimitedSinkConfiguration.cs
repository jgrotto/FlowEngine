using FlowEngine.Abstractions;
using FlowEngine.Abstractions.Data;
using FlowEngine.Abstractions.Plugins;
using System.Collections.Immutable;

namespace FlowEngine.Core.Configuration;

/// <summary>
/// Configuration for DelimitedSink plugin with comprehensive CSV writing options.
/// </summary>
public sealed class DelimitedSinkConfiguration : IPluginConfiguration
{
    /// <inheritdoc />
    public required string Name { get; init; }

    /// <inheritdoc />
    public string PluginId => Name;

    /// <inheritdoc />
    public string Version => "1.0.0";

    /// <inheritdoc />
    public string PluginType => "Sink";

    /// <summary>
    /// Path to the CSV file to write to.
    /// </summary>
    public required string FilePath { get; init; }

    /// <summary>
    /// Whether to write headers in the first row.
    /// </summary>
    public bool HasHeaders { get; init; } = true;

    /// <summary>
    /// Field delimiter character (default: comma).
    /// </summary>
    public string Delimiter { get; init; } = ",";

    /// <summary>
    /// Text encoding for the output file.
    /// </summary>
    public string Encoding { get; init; } = "UTF-8";

    /// <summary>
    /// Buffer size for file writing operations.
    /// </summary>
    public int BufferSize { get; init; } = 8192;

    /// <summary>
    /// Interval in milliseconds between buffer flushes.
    /// </summary>
    public int FlushInterval { get; init; } = 1000;

    /// <summary>
    /// Whether to create the output directory if it doesn't exist.
    /// </summary>
    public bool CreateDirectory { get; init; } = false;

    /// <summary>
    /// Whether to overwrite existing files.
    /// </summary>
    public bool OverwriteExisting { get; init; } = false;

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