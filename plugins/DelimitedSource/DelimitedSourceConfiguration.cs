using FlowEngine.Abstractions.Data;
using FlowEngine.Abstractions.Plugins;
using System.Collections.Immutable;

namespace DelimitedSource;

/// <summary>
/// Configuration for DelimitedSource plugin with file reading and schema management.
/// </summary>
public sealed class DelimitedSourceConfiguration : IPluginConfiguration
{
    // File settings
    public required string FilePath { get; init; }
    public string Delimiter { get; init; } = ",";
    public bool HasHeaders { get; init; } = true;
    public string Encoding { get; init; } = "UTF-8";
    public int ChunkSize { get; init; } = 5000;

    // Schema definition
    public ISchema? OutputSchema { get; init; }

    // Schema inference settings
    public bool InferSchema { get; init; } = false;
    public int InferenceSampleRows { get; init; } = 1000;

    // Error handling
    public bool SkipMalformedRows { get; init; } = false;
    public int MaxErrors { get; init; } = 100;

    // IPluginConfiguration implementation
    public string PluginId { get; init; } = "delimited-source";
    public string Name { get; init; } = "Delimited Source";
    public string Version { get; init; } = "1.0.0";
    public string PluginType { get; init; } = "Source";
    public ISchema? InputSchema { get; init; } = null; // Sources don't have input schemas
    public bool SupportsHotSwapping { get; init; } = true;

    private readonly ImmutableDictionary<string, int> _inputFieldIndexes = ImmutableDictionary<string, int>.Empty;
    private readonly ImmutableDictionary<string, int> _outputFieldIndexes;
    private readonly ImmutableDictionary<string, object> _properties = ImmutableDictionary<string, object>.Empty;

    public DelimitedSourceConfiguration()
    {
        _outputFieldIndexes = ImmutableDictionary<string, int>.Empty;
    }

    public ImmutableDictionary<string, int> InputFieldIndexes => _inputFieldIndexes;
    public ImmutableDictionary<string, int> OutputFieldIndexes => _outputFieldIndexes;
    public ImmutableDictionary<string, object> Properties => _properties;

    public int GetInputFieldIndex(string fieldName)
    {
        throw new InvalidOperationException("Source plugins do not have input fields");
    }

    public int GetOutputFieldIndex(string fieldName)
    {
        if (_outputFieldIndexes.TryGetValue(fieldName, out var index))
        {
            return index;
        }

        // Try to get from output schema if available
        if (OutputSchema != null)
        {
            var schemaIndex = OutputSchema.GetIndex(fieldName);
            if (schemaIndex >= 0)
            {
                return schemaIndex;
            }
        }

        throw new ArgumentException($"Field '{fieldName}' not found in output schema", nameof(fieldName));
    }

    public bool IsCompatibleWith(ISchema inputSchema)
    {
        // Sources are compatible with any input since they generate data
        return true;
    }

    public T GetProperty<T>(string key)
    {
        if (!_properties.TryGetValue(key, out var value))
        {
            throw new KeyNotFoundException($"Property '{key}' not found");
        }

        if (value is T typedValue)
        {
            return typedValue;
        }

        return (T)Convert.ChangeType(value, typeof(T));
    }

    public bool TryGetProperty<T>(string key, out T? value)
    {
        value = default;
        if (!_properties.TryGetValue(key, out var objectValue))
        {
            return false;
        }

        try
        {
            if (objectValue is T typedValue)
            {
                value = typedValue;
                return true;
            }
            value = (T)Convert.ChangeType(objectValue, typeof(T));
            return true;
        }
        catch
        {
            return false;
        }
    }
}
