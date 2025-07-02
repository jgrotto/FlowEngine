using FlowEngine.Abstractions.Data;
using FlowEngine.Abstractions.Plugins;
using FlowEngine.Abstractions.Schema;
using System.Collections.Immutable;

namespace DelimitedSource;

/// <summary>
/// Configuration for DelimitedSource source plugin.
/// Implements ArrayRow optimization with pre-calculated field indexes.
/// </summary>
[PluginConfigurationSchema("Source", "3.0.0", "High-performance delimited file source plugin")]
public sealed class DelimitedSourceConfiguration : IPluginConfiguration
{
    /// <summary>
    /// Gets the unique plugin identifier.
    /// </summary>
    public string PluginId { get; } = "delimited-source";

    /// <summary>
    /// Gets the plugin name.
    /// </summary>
    public string Name { get; } = "DelimitedSource";

    /// <summary>
    /// Gets the plugin version.
    /// </summary>
    public string Version { get; } = "3.0.0";

    /// <summary>
    /// Gets the plugin type.
    /// </summary>
    public string PluginType { get; } = "Source";

    /// <summary>
    /// Gets whether this plugin supports hot-swapping configuration updates.
    /// </summary>
    public bool SupportsHotSwapping { get; } = true;

    /// <summary>
    /// Gets the input data schema definition.
    /// Source plugins typically don't have input schemas as they generate data.
    /// </summary>
    public ISchema? InputSchema { get; init; } = null;

    /// <summary>
    /// Gets the output data schema definition.
    /// Inferred from file headers or explicitly configured.
    /// </summary>
    [ArrayRowSchema("Output schema definition inferred from file or explicitly configured")]
    public ISchema? OutputSchema { get; init; }

    /// <summary>
    /// Gets plugin-specific configuration properties.
    /// </summary>
    public ImmutableDictionary<string, object> Properties => _properties ??= CalculateProperties();

    private ImmutableDictionary<string, object>? _properties;

    /// <summary>
    /// Gets the path to the delimited file to read.
    /// </summary>
    [FilePathSchema("Path to the delimited file to read", mustExist: true, ".csv", ".tsv", ".txt")]
    public required string FilePath { get; init; }

    /// <summary>
    /// Gets the delimiter character used to separate fields.
    /// </summary>
    [JsonSchema(Type = "string", Description = "Character used to separate fields", Required = true)]
    public string Delimiter { get; init; } = ",";

    /// <summary>
    /// Gets whether the file has a header row.
    /// </summary>
    public bool HasHeader { get; init; } = true;

    /// <summary>
    /// Gets the text encoding of the file.
    /// </summary>
    public string Encoding { get; init; } = "UTF-8";

    /// <summary>
    /// Gets the character used for quoting fields.
    /// </summary>
    public string QuoteCharacter { get; init; } = "\"";

    /// <summary>
    /// Gets the character used for escaping quotes within quoted fields.
    /// </summary>
    public string EscapeCharacter { get; init; } = "\"";

    /// <summary>
    /// Gets the batch size for reading chunks of data.
    /// Optimized for memory usage and performance.
    /// </summary>
    [PerformanceSchema("Number of rows to read in each batch")]
    public int BatchSize { get; init; } = 10000;

    /// <summary>
    /// Gets the buffer size for file reading operations.
    /// </summary>
    [PerformanceSchema("File reading buffer size in bytes")]
    public int BufferSize { get; init; } = 8192;

    /// <summary>
    /// Gets whether to trim whitespace from field values.
    /// </summary>
    public bool TrimValues { get; init; } = true;

    /// <summary>
    /// Gets whether to skip empty lines.
    /// </summary>
    public bool SkipEmptyLines { get; init; } = true;

    /// <summary>
    /// Gets whether to enable parallel processing of batches.
    /// </summary>
    public bool EnableParallelProcessing { get; init; } = true;

    /// <summary>
    /// Gets the maximum degree of parallelism for processing.
    /// </summary>
    public int MaxDegreeOfParallelism { get; init; } = Environment.ProcessorCount;

    /// <summary>
    /// Gets whether to perform schema validation on each row.
    /// </summary>
    public bool ValidateSchema { get; init; } = true;

    /// <summary>
    /// Gets the maximum number of parsing errors to tolerate before failing.
    /// </summary>
    public int MaxErrorThreshold { get; init; } = 100;

    /// <summary>
    /// Gets whether to continue processing when encountering errors.
    /// </summary>
    public bool ContinueOnError { get; init; } = true;

    /// <summary>
    /// Gets the pre-calculated field indexes for input schema optimization.
    /// Source plugins typically don't have input schemas, so this returns empty.
    /// </summary>
    public ImmutableDictionary<string, int> InputFieldIndexes => ImmutableDictionary<string, int>.Empty;

    /// <summary>
    /// Gets the pre-calculated field indexes for output schema optimization.
    /// </summary>
    public ImmutableDictionary<string, int> OutputFieldIndexes => _outputFieldIndexes ??= CalculateOutputFieldIndexes();

    private ImmutableDictionary<string, int>? _outputFieldIndexes;

    /// <summary>
    /// Calculates the configuration properties as an immutable dictionary.
    /// </summary>
    /// <returns>Configuration properties</returns>
    private ImmutableDictionary<string, object> CalculateProperties()
    {
        return ImmutableDictionary.CreateBuilder<string, object>()
            .Add(nameof(FilePath), FilePath)
            .Add(nameof(Delimiter), Delimiter)
            .Add(nameof(HasHeader), HasHeader)
            .Add(nameof(Encoding), Encoding)
            .Add(nameof(BatchSize), BatchSize)
            .Add(nameof(EnableParallelProcessing), EnableParallelProcessing)
            .Add(nameof(MaxDegreeOfParallelism), MaxDegreeOfParallelism)
            .Add(nameof(ValidateSchema), ValidateSchema)
            .Add(nameof(MaxErrorThreshold), MaxErrorThreshold)
            .Add(nameof(ContinueOnError), ContinueOnError)
            .ToImmutable();
    }

    /// <summary>
    /// Updates the output schema with dynamically inferred schema from file.
    /// </summary>
    /// <param name="inferredSchema">Schema inferred from file analysis</param>
    /// <returns>New configuration instance with updated schema</returns>
    public DelimitedSourceConfiguration WithOutputSchema(ISchema inferredSchema)
    {
        return this with
        {
            OutputSchema = inferredSchema
        };
    }

    /// <summary>
    /// Calculates the pre-calculated field indexes for output schema optimization.
    /// </summary>
    /// <returns>Field indexes dictionary</returns>
    private ImmutableDictionary<string, int> CalculateOutputFieldIndexes()
    {
        if (OutputSchema?.Columns == null)
            return ImmutableDictionary<string, int>.Empty;

        var builder = ImmutableDictionary.CreateBuilder<string, int>();
        var columns = OutputSchema.Columns.ToList();
        
        for (int i = 0; i < columns.Count; i++)
        {
            builder.Add(columns[i].Name, i);
        }

        return builder.ToImmutable();
    }

    /// <summary>
    /// Gets the field index for an input field name with O(1) lookup.
    /// Source plugins typically don't have input schemas.
    /// </summary>
    /// <param name="fieldName">Name of the input field</param>
    /// <returns>Zero-based field index</returns>
    /// <exception cref="ArgumentException">Field not found in input schema</exception>
    public int GetInputFieldIndex(string fieldName)
    {
        throw new NotSupportedException("Source plugins do not have input schemas");
    }

    /// <summary>
    /// Gets the field index for an output field name with O(1) lookup.
    /// </summary>
    /// <param name="fieldName">Name of the output field</param>
    /// <returns>Zero-based field index</returns>
    /// <exception cref="ArgumentException">Field not found in output schema</exception>
    public int GetOutputFieldIndex(string fieldName)
    {
        if (OutputFieldIndexes.TryGetValue(fieldName, out var index))
            return index;
        
        throw new ArgumentException($"Field '{fieldName}' not found in output schema", nameof(fieldName));
    }

    /// <summary>
    /// Validates whether this configuration is compatible with the specified input schema.
    /// Source plugins typically don't consume input data.
    /// </summary>
    /// <param name="inputSchema">Input schema to check compatibility</param>
    /// <returns>True if schemas are compatible</returns>
    public bool IsCompatibleWith(ISchema inputSchema)
    {
        // Source plugins don't consume input data, so they're always compatible
        return true;
    }

    /// <summary>
    /// Gets a strongly-typed configuration property.
    /// </summary>
    /// <typeparam name="T">Expected property type</typeparam>
    /// <param name="key">Property key</param>
    /// <returns>Property value</returns>
    /// <exception cref="KeyNotFoundException">Property not found</exception>
    /// <exception cref="InvalidCastException">Property cannot be cast to specified type</exception>
    public T GetProperty<T>(string key)
    {
        if (!Properties.TryGetValue(key, out var value))
            throw new KeyNotFoundException($"Property '{key}' not found");

        if (value is T typedValue)
            return typedValue;

        throw new InvalidCastException($"Property '{key}' cannot be cast to {typeof(T).Name}");
    }

    /// <summary>
    /// Attempts to get a strongly-typed configuration property.
    /// </summary>
    /// <typeparam name="T">Expected property type</typeparam>
    /// <param name="key">Property key</param>
    /// <param name="value">Property value if found</param>
    /// <returns>True if property exists and can be cast to specified type</returns>
    public bool TryGetProperty<T>(string key, out T? value)
    {
        if (Properties.TryGetValue(key, out var objValue) && objValue is T typedValue)
        {
            value = typedValue;
            return true;
        }

        value = default;
        return false;
    }
}