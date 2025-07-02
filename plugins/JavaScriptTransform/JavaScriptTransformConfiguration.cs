using FlowEngine.Abstractions.Data;
// FlowEngine.Core excluded due to compilation errors
// using FlowEngine.Core.Data;
// using FlowEngine.Core.Plugins.Base;
using System.Collections.Immutable;
using System.Text.Json.Serialization;

namespace JavaScriptTransform;

/// <summary>
/// Configuration component for JavaScriptTransform plugin implementing schema-first design.
/// Provides JavaScript transformation configuration with ArrayRow optimization.
/// </summary>
/// <remarks>
/// This implements the Configuration component of the five-component pattern:
/// Configuration → Validator → Plugin → Processor → Service
/// 
/// Key Features:
/// - Schema-first design with explicit field indexing
/// - ArrayRow optimization through pre-calculated field access
/// - JavaScript script configuration and validation
/// - Performance-oriented parameter management
/// 
/// Performance: Pre-calculated field indexes enable O(1) ArrayRow access
/// Security: Script validation and sandboxing configuration
/// Flexibility: Support for input/output schema transformation
/// </remarks>
public sealed class JavaScriptTransformConfiguration : IPluginConfiguration
{
    /// <summary>
    /// JavaScript code to execute for each row transformation.
    /// </summary>
    [JsonPropertyName("script")]
    public required string Script { get; init; }

    /// <summary>
    /// Script execution timeout in milliseconds.
    /// </summary>
    [JsonPropertyName("timeoutMs")]
    public int TimeoutMs { get; init; } = 5000;

    /// <summary>
    /// Whether to continue processing when script execution fails.
    /// </summary>
    [JsonPropertyName("continueOnError")]
    public bool ContinueOnError { get; init; } = false;

    /// <summary>
    /// Maximum script complexity allowed (0-1000).
    /// </summary>
    [JsonPropertyName("maxComplexity")]
    public int MaxComplexity { get; init; } = 100;

    /// <summary>
    /// Whether to enable script debugging support.
    /// </summary>
    [JsonPropertyName("enableDebugging")]
    public bool EnableDebugging { get; init; } = false;

    /// <summary>
    /// Script optimization level.
    /// </summary>
    [JsonPropertyName("optimizationLevel")]
    public string OptimizationLevel { get; init; } = "Balanced";

    /// <summary>
    /// Batch size for chunk processing.
    /// </summary>
    [JsonPropertyName("batchSize")]
    public int BatchSize { get; init; } = 1000;

    /// <summary>
    /// Maximum memory usage in bytes.
    /// </summary>
    [JsonPropertyName("maxMemoryBytes")]
    public long MaxMemoryBytes { get; init; } = 100 * 1024 * 1024; // 100MB

    private readonly ISchema _inputSchema;
    private readonly ISchema _outputSchema;
    private readonly ImmutableDictionary<string, object> _parameters;

    /// <summary>
    /// Initializes a new instance of JavaScriptTransformConfiguration.
    /// </summary>
    /// <param name="script">JavaScript transformation script</param>
    /// <param name="inputSchema">Input data schema with field definitions</param>
    /// <param name="outputSchema">Output data schema (null to use input schema)</param>
    /// <param name="pluginId">Unique plugin identifier</param>
    /// <param name="timeoutMs">Script execution timeout</param>
    /// <param name="continueOnError">Continue on script errors</param>
    /// <param name="maxComplexity">Maximum script complexity</param>
    /// <param name="enableDebugging">Enable script debugging</param>
    /// <param name="optimizationLevel">Script optimization level</param>
    /// <param name="batchSize">Processing batch size</param>
    /// <param name="maxMemoryBytes">Maximum memory usage</param>
    public JavaScriptTransformConfiguration(
        string script,
        ISchema inputSchema,
        ISchema? outputSchema = null,
        string pluginId = "javascript-transform",
        int timeoutMs = 5000,
        bool continueOnError = false,
        int maxComplexity = 100,
        bool enableDebugging = false,
        string optimizationLevel = "Balanced",
        int batchSize = 1000,
        long maxMemoryBytes = 100 * 1024 * 1024)
    {
        Script = script ?? throw new ArgumentNullException(nameof(script));
        _inputSchema = inputSchema ?? throw new ArgumentNullException(nameof(inputSchema));
        _outputSchema = outputSchema ?? inputSchema;
        TimeoutMs = timeoutMs;
        ContinueOnError = continueOnError;
        MaxComplexity = maxComplexity;
        EnableDebugging = enableDebugging;
        OptimizationLevel = optimizationLevel;
        BatchSize = batchSize;
        MaxMemoryBytes = maxMemoryBytes;

        // Build parameters dictionary
        _parameters = ImmutableDictionary.CreateBuilder<string, object>()
            .Add("Script", Script)
            .Add("TimeoutMs", TimeoutMs)
            .Add("ContinueOnError", ContinueOnError)
            .Add("MaxComplexity", MaxComplexity)
            .Add("EnableDebugging", EnableDebugging)
            .Add("OptimizationLevel", OptimizationLevel)
            .Add("BatchSize", BatchSize)
            .Add("MaxMemoryBytes", MaxMemoryBytes)
            .Add("PluginId", pluginId)
            .ToImmutable();
    }

    /// <inheritdoc />
    public override ISchema InputSchema => _inputSchema;

    /// <inheritdoc />
    public override ISchema OutputSchema => _outputSchema;

    /// <inheritdoc />
    public override ImmutableDictionary<string, object> Parameters => _parameters;

    /// <inheritdoc />
    public override string PluginType => "Transform";

    /// <inheritdoc />
    public override string PluginId => _parameters.TryGetValue("PluginId", out var id) ? id.ToString()! : "javascript-transform";

    /// <inheritdoc />
    public override string Version => "3.0.0";

    /// <inheritdoc />
    public override bool SupportsHotSwapping => true;

    /// <inheritdoc />
    public override long EstimatedMemoryUsage => 
        base.EstimatedMemoryUsage + MaxMemoryBytes + (Script.Length * 2); // Script storage overhead

    /// <inheritdoc />
    public override ImmutableArray<string> CompatibilityConstraints => 
        ImmutableArray.Create("RequiresScriptEngine", "TransformPlugin");

    /// <inheritdoc />
    protected override void ValidateParameters(List<string> errors, List<string> warnings)
    {
        // Validate script
        if (string.IsNullOrWhiteSpace(Script))
        {
            errors.Add("Script cannot be null or empty");
        }
        else if (Script.Length > 1024 * 1024) // 1MB limit
        {
            errors.Add("Script size cannot exceed 1MB");
        }

        // Validate timeout
        if (TimeoutMs <= 0)
        {
            errors.Add("TimeoutMs must be positive");
        }
        else if (TimeoutMs > 300000) // 5 minute limit
        {
            warnings.Add("TimeoutMs exceeds 5 minutes, may impact pipeline performance");
        }

        // Validate complexity
        if (MaxComplexity < 1 || MaxComplexity > 1000)
        {
            errors.Add("MaxComplexity must be between 1 and 1000");
        }

        // Validate optimization level
        var validLevels = new[] { "None", "Balanced", "Maximum" };
        if (!validLevels.Contains(OptimizationLevel))
        {
            errors.Add($"OptimizationLevel must be one of: {string.Join(", ", validLevels)}");
        }

        // Validate batch size
        if (BatchSize <= 0)
        {
            errors.Add("BatchSize must be positive");
        }
        else if (BatchSize > 10000)
        {
            warnings.Add("BatchSize exceeds 10000, may cause memory pressure");
        }

        // Validate memory limit
        if (MaxMemoryBytes <= 0)
        {
            errors.Add("MaxMemoryBytes must be positive");
        }
        else if (MaxMemoryBytes > 1024 * 1024 * 1024) // 1GB limit
        {
            warnings.Add("MaxMemoryBytes exceeds 1GB, may cause system memory pressure");
        }
    }

    /// <inheritdoc />
    public override PluginConfigurationBase WithParameters(ImmutableDictionary<string, object> parameterUpdates)
    {
        var updatedParams = Parameters.SetItems(parameterUpdates);

        return new JavaScriptTransformConfiguration(
            script: GetParameterValue<string>(updatedParams, "Script", Script),
            inputSchema: InputSchema,
            outputSchema: OutputSchema,
            pluginId: GetParameterValue<string>(updatedParams, "PluginId", PluginId),
            timeoutMs: GetParameterValue<int>(updatedParams, "TimeoutMs", TimeoutMs),
            continueOnError: GetParameterValue<bool>(updatedParams, "ContinueOnError", ContinueOnError),
            maxComplexity: GetParameterValue<int>(updatedParams, "MaxComplexity", MaxComplexity),
            enableDebugging: GetParameterValue<bool>(updatedParams, "EnableDebugging", EnableDebugging),
            optimizationLevel: GetParameterValue<string>(updatedParams, "OptimizationLevel", OptimizationLevel),
            batchSize: GetParameterValue<int>(updatedParams, "BatchSize", BatchSize),
            maxMemoryBytes: GetParameterValue<long>(updatedParams, "MaxMemoryBytes", MaxMemoryBytes));
    }

    /// <summary>
    /// Creates a configuration for basic string field transformation.
    /// </summary>
    /// <param name="script">JavaScript transformation script</param>
    /// <param name="inputFieldNames">Names of input fields</param>
    /// <param name="outputFieldNames">Names of output fields (null to use input names)</param>
    /// <param name="pluginId">Plugin identifier</param>
    /// <returns>Configuration for string field transformation</returns>
    public static JavaScriptTransformConfiguration ForStringFields(
        string script,
        string[] inputFieldNames,
        string[]? outputFieldNames = null,
        string pluginId = "javascript-transform")
    {
        var inputColumns = inputFieldNames.Select((name, index) => new ColumnDefinition
        {
            Name = name,
            DataType = typeof(string),
            IsNullable = true,
            Index = index
        }).ToArray();

        var outputColumns = (outputFieldNames ?? inputFieldNames).Select((name, index) => new ColumnDefinition
        {
            Name = name,
            DataType = typeof(string),
            IsNullable = true,
            Index = index
        }).ToArray();

        var inputSchema = Schema.Create("JavaScriptTransformInput", inputColumns);
        var outputSchema = Schema.Create("JavaScriptTransformOutput", outputColumns);

        return new JavaScriptTransformConfiguration(
            script: script,
            inputSchema: inputSchema,
            outputSchema: outputSchema,
            pluginId: pluginId);
    }

    /// <summary>
    /// Gets a parameter value with type conversion and fallback.
    /// </summary>
    private static T GetParameterValue<T>(ImmutableDictionary<string, object> parameters, string key, T defaultValue)
    {
        if (!parameters.TryGetValue(key, out var value))
            return defaultValue;

        try
        {
            return value is T typedValue ? typedValue : (T)Convert.ChangeType(value, typeof(T));
        }
        catch
        {
            return defaultValue;
        }
    }
}