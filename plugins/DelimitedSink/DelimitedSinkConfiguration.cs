using FlowEngine.Abstractions.Data;
using FlowEngine.Abstractions.Plugins;
using FlowEngine.Abstractions.Schema;
using System.Collections.Immutable;

namespace DelimitedSink;

/// <summary>
/// Configuration for DelimitedSink plugin implementing the five-component architecture.
/// Supports high-performance writing of delimited files with ArrayRow optimization.
/// </summary>
[PluginConfigurationSchema("Sink", "3.0.0", "High-performance delimited file sink plugin")]
public sealed class DelimitedSinkConfiguration : IPluginConfiguration
{
    /// <summary>
    /// Gets the unique plugin identifier.
    /// </summary>
    public string PluginId { get; } = "delimited-sink";

    /// <summary>
    /// Gets the plugin name.
    /// </summary>
    public string Name { get; } = "DelimitedSink";

    /// <summary>
    /// Gets the plugin version.
    /// </summary>
    public string Version { get; } = "3.0.0";

    /// <summary>
    /// Gets the plugin type.
    /// </summary>
    public string PluginType { get; } = "Sink";

    /// <summary>
    /// Gets whether this plugin supports hot-swapping configuration updates.
    /// </summary>
    public bool SupportsHotSwapping { get; } = true;

    /// <summary>
    /// Gets the input data schema definition.
    /// Sink plugins consume data from transform plugins.
    /// </summary>
    [ArrayRowSchema("Input schema with ArrayRow optimization for high-performance writing", requiresSequentialIndexes: true)]
    public ISchema? InputSchema { get; init; }

    /// <summary>
    /// Gets the output data schema definition.
    /// Sink plugins typically don't produce output data.
    /// </summary>
    public ISchema? OutputSchema { get; init; } = null;

    /// <summary>
    /// Gets the path where the delimited file will be written.
    /// </summary>
    [FilePathSchema("Path where the delimited file will be written", mustExist: false, ".csv", ".tsv", ".txt")]
    public required string FilePath { get; init; }

    /// <summary>
    /// Gets the delimiter character used to separate fields.
    /// </summary>
    [JsonSchema(Type = "string", Description = "Character used to separate fields", MinLength = 1, MaxLength = 5, Required = true)]
    public string Delimiter { get; init; } = ",";

    /// <summary>
    /// Gets whether to include a header row in the output file.
    /// </summary>
    public bool IncludeHeader { get; init; } = true;

    /// <summary>
    /// Gets the text encoding for the output file.
    /// </summary>
    [JsonSchema(Type = "string", Description = "Text encoding for output file", Enum = new[] { "UTF-8", "UTF-16", "ASCII", "Latin1" })]
    public string Encoding { get; init; } = "UTF-8";

    /// <summary>
    /// Gets the character used for quoting fields that contain special characters.
    /// </summary>
    public string QuoteCharacter { get; init; } = "\"";

    /// <summary>
    /// Gets the character used for escaping quotes within quoted fields.
    /// </summary>
    public string EscapeCharacter { get; init; } = "\"";

    /// <summary>
    /// Gets the line ending style for the output file.
    /// </summary>
    [JsonSchema(Type = "string", Description = "Line ending style", Enum = new[] { "CRLF", "LF", "CR" })]
    public string LineEnding { get; init; } = "CRLF";

    /// <summary>
    /// Gets the batch size for writing chunks of data.
    /// Optimized for I/O efficiency and memory usage.
    /// </summary>
    [PerformanceSchema("Number of rows to write in each batch", recommendedMin: 1000, recommendedMax: 50000,
        performanceNote: "Larger batches improve I/O efficiency but increase memory usage")]
    public int BatchSize { get; init; } = 10000;

    /// <summary>
    /// Gets the buffer size for file writing operations.
    /// </summary>
    [PerformanceSchema("File writing buffer size in bytes", recommendedMin: 4096, recommendedMax: 65536,
        performanceNote: "Larger buffers improve I/O performance for large files")]
    public int BufferSize { get; init; } = 8192;

    /// <summary>
    /// Gets whether to enable parallel processing of batches.
    /// </summary>
    public bool EnableParallelProcessing { get; init; } = true;

    /// <summary>
    /// Gets the maximum degree of parallelism for processing.
    /// </summary>
    [PerformanceSchema("Maximum number of parallel writing threads", recommendedMin: 1, recommendedMax: 4,
        performanceNote: "File I/O rarely benefits from high parallelism")]
    public int MaxDegreeOfParallelism { get; init; } = Math.Min(Environment.ProcessorCount, 4);

    /// <summary>
    /// Gets whether to flush the file buffer after each batch.
    /// Ensures data is written to disk but may impact performance.
    /// </summary>
    public bool FlushAfterBatch { get; init; } = false;

    /// <summary>
    /// Gets whether to append to an existing file or overwrite it.
    /// </summary>
    public bool AppendToFile { get; init; } = false;

    /// <summary>
    /// Gets whether to compress the output file using gzip.
    /// </summary>
    public bool EnableCompression { get; init; } = false;

    /// <summary>
    /// Gets the compression level when compression is enabled (1-9).
    /// </summary>
    [PerformanceSchema("Compression level (1=fastest, 9=best compression)", recommendedMin: 1, recommendedMax: 9,
        performanceNote: "Higher compression levels trade CPU time for smaller files")]
    public int CompressionLevel { get; init; } = 6;

    /// <summary>
    /// Gets whether to validate the input schema against written data.
    /// Useful for development but adds overhead in production.
    /// </summary>
    public bool ValidateInput { get; init; } = false;

    /// <summary>
    /// Gets whether to continue processing when a row write fails.
    /// When false, the first error stops the entire operation.
    /// </summary>
    public bool ContinueOnError { get; init; } = true;

    /// <summary>
    /// Gets the maximum number of write errors to tolerate before stopping.
    /// Only used when ContinueOnError is true.
    /// </summary>
    public int MaxErrorThreshold { get; init; } = 100;

    /// <summary>
    /// Gets the maximum file size in bytes before creating a new file.
    /// Zero means no limit (single file output).
    /// </summary>
    [PerformanceSchema("Maximum file size in bytes before file rotation", recommendedMin: 0, recommendedMax: 2147483648,
        performanceNote: "File rotation can help manage very large datasets")]
    public long MaxFileSizeBytes { get; init; } = 0;

    /// <summary>
    /// Gets plugin-specific configuration properties.
    /// </summary>
    public ImmutableDictionary<string, object> Properties => _properties ??= CalculateProperties();

    private ImmutableDictionary<string, object>? _properties;

    /// <summary>
    /// Gets the pre-calculated field indexes for input schema optimization.
    /// Maps field names to zero-based array indexes for O(1) ArrayRow access.
    /// </summary>
    public ImmutableDictionary<string, int> InputFieldIndexes => _inputFieldIndexes ??= CalculateInputFieldIndexes();

    /// <summary>
    /// Gets the pre-calculated field indexes for output schema optimization.
    /// Sink plugins typically don't have output schemas, so this returns empty.
    /// </summary>
    public ImmutableDictionary<string, int> OutputFieldIndexes => ImmutableDictionary<string, int>.Empty;

    private ImmutableDictionary<string, int>? _inputFieldIndexes;

    /// <summary>
    /// Gets the field index for an input field name with O(1) lookup.
    /// </summary>
    /// <param name="fieldName">Name of the input field</param>
    /// <returns>Zero-based field index</returns>
    /// <exception cref="ArgumentException">Field not found in input schema</exception>
    public int GetInputFieldIndex(string fieldName)
    {
        if (InputFieldIndexes.TryGetValue(fieldName, out var index))
            return index;
        
        throw new ArgumentException($"Field '{fieldName}' not found in input schema", nameof(fieldName));
    }

    /// <summary>
    /// Gets the field index for an output field name with O(1) lookup.
    /// Sink plugins typically don't have output schemas.
    /// </summary>
    /// <param name="fieldName">Name of the output field</param>
    /// <returns>Zero-based field index</returns>
    /// <exception cref="ArgumentException">Field not found in output schema</exception>
    public int GetOutputFieldIndex(string fieldName)
    {
        throw new NotSupportedException("Sink plugins do not have output schemas");
    }

    /// <summary>
    /// Validates whether this configuration is compatible with the specified input schema.
    /// </summary>
    /// <param name="inputSchema">Input schema to check compatibility</param>
    /// <returns>True if schemas are compatible</returns>
    public bool IsCompatibleWith(ISchema inputSchema)
    {
        if (InputSchema == null)
            return true; // No specific input schema required

        // Check that all required input fields are present
        foreach (var requiredColumn in InputSchema.Columns)
        {
            if (!requiredColumn.IsNullable)
            {
                var inputColumn = inputSchema.Columns.FirstOrDefault(c => 
                    c.Name.Equals(requiredColumn.Name, StringComparison.OrdinalIgnoreCase));
                
                if (inputColumn == null)
                    return false; // Required field missing
            }
        }

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

    /// <summary>
    /// Creates a new configuration with updated input schema.
    /// </summary>
    /// <param name="newInputSchema">New input schema</param>
    /// <returns>New configuration instance</returns>
    public DelimitedSinkConfiguration WithInputSchema(ISchema newInputSchema)
    {
        return this with { InputSchema = newInputSchema };
    }

    /// <summary>
    /// Gets the effective line ending string based on the LineEnding setting.
    /// </summary>
    /// <returns>Line ending string</returns>
    public string GetLineEnding()
    {
        return LineEnding.ToUpperInvariant() switch
        {
            "CRLF" => "\r\n",
            "LF" => "\n",
            "CR" => "\r",
            _ => Environment.NewLine
        };
    }

    /// <summary>
    /// Gets the effective file path with file rotation support.
    /// </summary>
    /// <param name="fileIndex">File index for rotation (0 for first file)</param>
    /// <returns>File path with rotation suffix if needed</returns>
    public string GetEffectiveFilePath(int fileIndex = 0)
    {
        if (MaxFileSizeBytes <= 0 || fileIndex == 0)
            return FilePath;

        var directory = Path.GetDirectoryName(FilePath) ?? "";
        var fileName = Path.GetFileNameWithoutExtension(FilePath);
        var extension = Path.GetExtension(FilePath);
        
        return Path.Combine(directory, $"{fileName}_{fileIndex:D3}{extension}");
    }

    /// <summary>
    /// Calculates the pre-calculated field indexes for input schema optimization.
    /// </summary>
    /// <returns>Field indexes dictionary</returns>
    private ImmutableDictionary<string, int> CalculateInputFieldIndexes()
    {
        if (InputSchema?.Columns == null)
            return ImmutableDictionary<string, int>.Empty;

        var builder = ImmutableDictionary.CreateBuilder<string, int>();
        var columns = InputSchema.Columns.ToList();
        
        for (int i = 0; i < columns.Count; i++)
        {
            builder.Add(columns[i].Name, i);
        }

        return builder.ToImmutable();
    }

    /// <summary>
    /// Calculates the configuration properties as an immutable dictionary.
    /// </summary>
    /// <returns>Configuration properties</returns>
    private ImmutableDictionary<string, object> CalculateProperties()
    {
        return ImmutableDictionary.CreateBuilder<string, object>()
            .Add(nameof(FilePath), FilePath)
            .Add(nameof(Delimiter), Delimiter)
            .Add(nameof(IncludeHeader), IncludeHeader)
            .Add(nameof(Encoding), Encoding)
            .Add(nameof(BatchSize), BatchSize)
            .Add(nameof(BufferSize), BufferSize)
            .Add(nameof(EnableParallelProcessing), EnableParallelProcessing)
            .Add(nameof(MaxDegreeOfParallelism), MaxDegreeOfParallelism)
            .Add(nameof(FlushAfterBatch), FlushAfterBatch)
            .Add(nameof(AppendToFile), AppendToFile)
            .Add(nameof(EnableCompression), EnableCompression)
            .Add(nameof(CompressionLevel), CompressionLevel)
            .Add(nameof(ValidateInput), ValidateInput)
            .Add(nameof(ContinueOnError), ContinueOnError)
            .Add(nameof(MaxErrorThreshold), MaxErrorThreshold)
            .Add(nameof(MaxFileSizeBytes), MaxFileSizeBytes)
            .ToImmutable();
    }
}