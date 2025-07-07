using FlowEngine.Abstractions.Data;
using FlowEngine.Abstractions.Plugins;
using System.Collections.Immutable;

namespace DelimitedSink;

/// <summary>
/// Configuration for DelimitedSink plugin with file writing and formatting options.
/// </summary>
public sealed class DelimitedSinkConfiguration : IPluginConfiguration
{
    // File settings
    public required string FilePath { get; init; }
    public string Delimiter { get; init; } = ",";
    public bool IncludeHeaders { get; init; } = true;
    public string Encoding { get; init; } = "UTF-8";
    public string LineEnding { get; init; } = "System";
    public bool AppendMode { get; init; } = false;
    
    // Performance settings
    public int BufferSize { get; init; } = 65536;
    public int FlushInterval { get; init; } = 1000;
    
    // File handling
    public bool CreateDirectories { get; init; } = true;
    public bool OverwriteExisting { get; init; } = true;
    
    // Schema and mapping
    public ISchema? InputSchema { get; init; }
    public IReadOnlyList<ColumnMapping>? ColumnMappings { get; init; }

    // IPluginConfiguration implementation
    public string PluginId { get; init; } = "delimited-sink";
    public string Name { get; init; } = "Delimited Sink";
    public string Version { get; init; } = "1.0.0";
    public string PluginType { get; init; } = "Sink";
    public ISchema? OutputSchema { get; init; } = null; // Sinks don't produce output
    public bool SupportsHotSwapping { get; init; } = true;

    private readonly ImmutableDictionary<string, int> _inputFieldIndexes;
    private readonly ImmutableDictionary<string, int> _outputFieldIndexes = ImmutableDictionary<string, int>.Empty;
    private readonly ImmutableDictionary<string, object> _properties = ImmutableDictionary<string, object>.Empty;

    public DelimitedSinkConfiguration()
    {
        _inputFieldIndexes = ImmutableDictionary<string, int>.Empty;
    }

    public ImmutableDictionary<string, int> InputFieldIndexes => _inputFieldIndexes;
    public ImmutableDictionary<string, int> OutputFieldIndexes => _outputFieldIndexes;
    public ImmutableDictionary<string, object> Properties => _properties;

    public int GetInputFieldIndex(string fieldName)
    {
        if (_inputFieldIndexes.TryGetValue(fieldName, out var index))
            return index;
        
        // Try to get from input schema if available
        if (InputSchema != null)
        {
            var schemaIndex = InputSchema.GetIndex(fieldName);
            if (schemaIndex >= 0)
                return schemaIndex;
        }

        throw new ArgumentException($"Field '{fieldName}' not found in input schema", nameof(fieldName));
    }

    public int GetOutputFieldIndex(string fieldName)
    {
        throw new InvalidOperationException("Sink plugins do not have output fields");
    }

    public bool IsCompatibleWith(ISchema inputSchema)
    {
        // If we have an explicit input schema, check compatibility
        if (InputSchema != null)
        {
            // Basic compatibility check - all required columns present
            foreach (var column in InputSchema.Columns)
            {
                if (inputSchema.GetIndex(column.Name) < 0)
                    return false;
            }
        }

        // Otherwise, sinks can generally accept any input schema
        return true;
    }

    public T GetProperty<T>(string key)
    {
        if (!_properties.TryGetValue(key, out var value))
            throw new KeyNotFoundException($"Property '{key}' not found");

        if (value is T typedValue)
            return typedValue;

        return (T)Convert.ChangeType(value, typeof(T));
    }

    public bool TryGetProperty<T>(string key, out T? value)
    {
        value = default;
        if (!_properties.TryGetValue(key, out var objectValue))
            return false;

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

/// <summary>
/// Column mapping configuration for transforming input data to output format.
/// </summary>
public sealed record ColumnMapping
{
    public required string SourceColumn { get; init; }
    public required string OutputColumn { get; init; }
    public string? Format { get; init; }
}