using FlowEngine.Abstractions.Data;
using FlowEngine.Abstractions.Plugins;
using FlowEngine.Abstractions.Schema;
using System.Collections.Immutable;
using System.Text.Json;

namespace JavaScriptTransform;

/// <summary>
/// Configuration for JavaScriptTransform plugin implementing the five-component architecture.
/// Supports inline scripts, file-based scripts, and schema transformations with ArrayRow optimization.
/// </summary>
[PluginConfigurationSchema("Transform", "3.0.0", "High-performance JavaScript transformation plugin")]
public sealed class JavaScriptTransformConfiguration : IPluginConfiguration
{
    /// <summary>
    /// Gets the unique plugin identifier.
    /// </summary>
    public string PluginId { get; } = "javascript-transform";

    /// <summary>
    /// Gets the plugin name.
    /// </summary>
    public string Name { get; } = "JavaScriptTransform";

    /// <summary>
    /// Gets the plugin version.
    /// </summary>
    public string Version { get; } = "3.0.0";

    /// <summary>
    /// Gets the plugin type.
    /// </summary>
    public string PluginType { get; } = "Transform";

    /// <summary>
    /// Gets whether this plugin supports hot-swapping configuration updates.
    /// </summary>
    public bool SupportsHotSwapping { get; } = true;

    /// <summary>
    /// Gets the input data schema definition.
    /// </summary>
    [ArrayRowSchema("Input schema with ArrayRow optimization", requiresSequentialIndexes: true, maxFields: 100)]
    public ISchema? InputSchema { get; init; }

    /// <summary>
    /// Gets the output data schema definition.
    /// </summary>
    [ArrayRowSchema("Output schema with ArrayRow optimization", requiresSequentialIndexes: true, maxFields: 100)]
    public ISchema? OutputSchema { get; init; }

    /// <summary>
    /// Gets the JavaScript code to execute for each row transformation.
    /// This takes precedence over ScriptFilePath if both are provided.
    /// </summary>
    [JsonSchema(Type = "string", Description = "JavaScript code for row transformation")]
    public string? ScriptCode { get; init; }

    /// <summary>
    /// Gets the path to a JavaScript file containing the transformation logic.
    /// Used when ScriptCode is not provided.
    /// </summary>
    [FilePathSchema("Path to JavaScript file containing transformation logic", mustExist: true, ".js", ".mjs")]
    public string? ScriptFilePath { get; init; }

    /// <summary>
    /// Gets the name of the JavaScript function to call for row transformation.
    /// Default is "transform" if not specified.
    /// </summary>
    [JsonSchema(Type = "string", Description = "JavaScript function name to call", Pattern = "^[a-zA-Z_$][a-zA-Z0-9_$]*$", Required = true)]
    public string FunctionName { get; init; } = "transform";

    /// <summary>
    /// Gets the timeout for script execution per row in milliseconds.
    /// Default is 1000ms (1 second) to prevent runaway scripts.
    /// </summary>
    [PerformanceSchema("Script execution timeout in milliseconds", recommendedMin: 100, recommendedMax: 5000, 
        performanceNote: "Lower values improve responsiveness but may cause script failures")]
    public int ScriptTimeoutMs { get; init; } = 1000;

    /// <summary>
    /// Gets the maximum memory limit for script execution in bytes.
    /// Default is 10MB to prevent memory exhaustion.
    /// </summary>
    [PerformanceSchema("Maximum memory usage per script execution", recommendedMin: 1048576, recommendedMax: 104857600,
        performanceNote: "Higher values allow complex transformations but increase memory pressure")]
    public long MaxMemoryBytes { get; init; } = 10 * 1024 * 1024; // 10MB

    /// <summary>
    /// Gets whether to enable detailed error reporting for script failures.
    /// Useful for debugging but may impact performance.
    /// </summary>
    public bool EnableDetailedErrors { get; init; } = false;

    /// <summary>
    /// Gets whether to cache compiled scripts for better performance.
    /// Recommended for production use.
    /// </summary>
    public bool EnableScriptCaching { get; init; } = true;

    /// <summary>
    /// Gets the batch size for processing rows.
    /// Larger batches improve performance but use more memory.
    /// </summary>
    [PerformanceSchema("Number of rows to process in each batch", recommendedMin: 100, recommendedMax: 10000,
        performanceNote: "Larger batches improve throughput but increase memory usage")]
    public int BatchSize { get; init; } = 1000;

    /// <summary>
    /// Gets whether to enable parallel processing of batches.
    /// Can significantly improve performance for CPU-intensive transformations.
    /// </summary>
    [JsonSchema(Type = "boolean", Description = "Enable parallel processing for improved performance")]
    public bool EnableParallelProcessing { get; init; } = true;

    /// <summary>
    /// Gets the maximum degree of parallelism for batch processing.
    /// </summary>
    [PerformanceSchema("Maximum number of parallel processing threads", recommendedMin: 1, recommendedMax: 16,
        performanceNote: "Should not exceed CPU core count for optimal performance")]
    public int MaxDegreeOfParallelism { get; init; } = Environment.ProcessorCount;

    /// <summary>
    /// Gets whether to validate the output schema against transformed data.
    /// Useful for development but adds overhead in production.
    /// </summary>
    public bool ValidateOutput { get; init; } = false;

    /// <summary>
    /// Gets whether to continue processing when a row transformation fails.
    /// When false, the first error stops the entire transformation.
    /// </summary>
    public bool ContinueOnError { get; init; } = true;

    /// <summary>
    /// Gets the maximum number of errors to tolerate before stopping.
    /// Only used when ContinueOnError is true.
    /// </summary>
    public int MaxErrorThreshold { get; init; } = 100;

    /// <summary>
    /// Gets global JavaScript variables to make available in the script context.
    /// These are read-only constants available to all script executions.
    /// </summary>
    public IReadOnlyDictionary<string, object> GlobalVariables { get; init; } = 
        new Dictionary<string, object>();

    /// <summary>
    /// Gets custom JavaScript modules to load before script execution.
    /// Allows for reusable utility functions and libraries.
    /// </summary>
    public IReadOnlyList<string> ImportModules { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Gets the pre-calculated field indexes for input schema optimization.
    /// Maps field names to zero-based array indexes for O(1) ArrayRow access.
    /// </summary>
    public ImmutableDictionary<string, int> InputFieldIndexes => _inputFieldIndexes ??= CalculateInputFieldIndexes();

    /// <summary>
    /// Gets the pre-calculated field indexes for output schema optimization.
    /// Maps field names to zero-based array indexes for O(1) ArrayRow access.
    /// </summary>
    public ImmutableDictionary<string, int> OutputFieldIndexes => _outputFieldIndexes ??= CalculateOutputFieldIndexes();

    /// <summary>
    /// Gets plugin-specific configuration properties.
    /// </summary>
    public ImmutableDictionary<string, object> Properties => _properties ??= CalculateProperties();

    private ImmutableDictionary<string, int>? _inputFieldIndexes;
    private ImmutableDictionary<string, int>? _outputFieldIndexes;
    private ImmutableDictionary<string, object>? _properties;

    /// <summary>
    /// Gets the effective JavaScript code to execute.
    /// Returns ScriptCode if provided, otherwise loads from ScriptFilePath.
    /// </summary>
    /// <returns>JavaScript code string</returns>
    /// <exception cref="InvalidOperationException">When neither ScriptCode nor ScriptFilePath is provided</exception>
    public string GetEffectiveScriptCode()
    {
        if (!string.IsNullOrWhiteSpace(ScriptCode))
        {
            return ScriptCode;
        }

        if (!string.IsNullOrWhiteSpace(ScriptFilePath))
        {
            if (!File.Exists(ScriptFilePath))
                throw new FileNotFoundException($"Script file not found: {ScriptFilePath}");

            return File.ReadAllText(ScriptFilePath);
        }

        throw new InvalidOperationException("Either ScriptCode or ScriptFilePath must be provided");
    }

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
    /// Creates a new configuration with updated script code.
    /// </summary>
    /// <param name="newScriptCode">New JavaScript code</param>
    /// <returns>New configuration instance</returns>
    public JavaScriptTransformConfiguration WithScriptCode(string newScriptCode)
    {
        return this with { ScriptCode = newScriptCode };
    }

    /// <summary>
    /// Creates a new configuration with updated output schema.
    /// </summary>
    /// <param name="newOutputSchema">New output schema</param>
    /// <returns>New configuration instance</returns>
    public JavaScriptTransformConfiguration WithOutputSchema(ISchema newOutputSchema)
    {
        return this with { OutputSchema = newOutputSchema };
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
    /// Calculates the configuration properties as an immutable dictionary.
    /// </summary>
    /// <returns>Configuration properties</returns>
    private ImmutableDictionary<string, object> CalculateProperties()
    {
        var builder = ImmutableDictionary.CreateBuilder<string, object>()
            .Add(nameof(ScriptCode), ScriptCode ?? string.Empty)
            .Add(nameof(ScriptFilePath), ScriptFilePath ?? string.Empty)
            .Add(nameof(FunctionName), FunctionName)
            .Add(nameof(ScriptTimeoutMs), ScriptTimeoutMs)
            .Add(nameof(MaxMemoryBytes), MaxMemoryBytes)
            .Add(nameof(EnableDetailedErrors), EnableDetailedErrors)
            .Add(nameof(EnableScriptCaching), EnableScriptCaching)
            .Add(nameof(BatchSize), BatchSize)
            .Add(nameof(EnableParallelProcessing), EnableParallelProcessing)
            .Add(nameof(MaxDegreeOfParallelism), MaxDegreeOfParallelism)
            .Add(nameof(ValidateOutput), ValidateOutput)
            .Add(nameof(ContinueOnError), ContinueOnError)
            .Add(nameof(MaxErrorThreshold), MaxErrorThreshold);

        if (GlobalVariables.Count > 0)
        {
            builder.Add(nameof(GlobalVariables), JsonSerializer.Serialize(GlobalVariables));
        }

        if (ImportModules.Count > 0)
        {
            builder.Add(nameof(ImportModules), JsonSerializer.Serialize(ImportModules));
        }

        return builder.ToImmutable();
    }
}