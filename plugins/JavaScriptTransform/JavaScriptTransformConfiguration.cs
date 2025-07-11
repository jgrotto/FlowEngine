using FlowEngine.Abstractions.Data;
using FlowEngine.Abstractions.Plugins;
using System.Collections.Immutable;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace JavaScriptTransform;

/// <summary>
/// Configuration for JavaScript Transform plugin with pure context API support.
/// Mode 1: Inline scripts with explicit output schema definition.
/// </summary>
public class JavaScriptTransformConfiguration : IPluginConfiguration
{
    /// <summary>
    /// Plugin identifier.
    /// </summary>
    public string PluginId { get; init; } = "JavaScript Transform";

    /// <summary>
    /// Plugin name.
    /// </summary>
    public string Name { get; init; } = "JavaScript Transform";

    /// <summary>
    /// Plugin version.
    /// </summary>
    public string Version { get; init; } = "1.0.0";

    /// <summary>
    /// Plugin type.
    /// </summary>
    public string PluginType { get; init; } = "Transform";

    /// <summary>
    /// Whether the plugin supports hot swapping.
    /// </summary>
    public bool SupportsHotSwapping { get; init; } = false;

    /// <summary>
    /// Input schema.
    /// </summary>
    public ISchema? InputSchema { get; set; }

    /// <summary>
    /// Output schema interface property.
    /// </summary>
    ISchema? IPluginConfiguration.OutputSchema => null; // Will be computed from OutputSchema config

    /// <summary>
    /// Input field indexes for quick lookup.
    /// </summary>
    public ImmutableDictionary<string, int> InputFieldIndexes { get; private set; } = ImmutableDictionary<string, int>.Empty;

    /// <summary>
    /// Output field indexes for quick lookup.
    /// </summary>
    public ImmutableDictionary<string, int> OutputFieldIndexes { get; private set; } = ImmutableDictionary<string, int>.Empty;

    /// <summary>
    /// Configuration properties.
    /// </summary>
    public ImmutableDictionary<string, object> Properties { get; private set; } = ImmutableDictionary<string, object>.Empty;

    /// <summary>
    /// The JavaScript code that will be executed for each row.
    /// Must contain a process(context) function that returns boolean.
    /// </summary>
    [Required]
    [StringLength(50000, MinimumLength = 10)]
    public string Script { get; init; } = string.Empty;

    /// <summary>
    /// JavaScript engine configuration settings.
    /// </summary>
    public EngineConfiguration Engine { get; init; } = new();

    /// <summary>
    /// Output schema configuration for the transform.
    /// Mode 1 requires explicit schema definition.
    /// </summary>
    [Required]
    public OutputSchemaConfiguration OutputSchema { get; init; } = new();

    /// <summary>
    /// Performance and monitoring settings.
    /// </summary>
    public PerformanceConfiguration Performance { get; init; } = new();

    /// <summary>
    /// Validates the configuration for correctness.
    /// </summary>
    public bool IsValid()
    {
        if (string.IsNullOrWhiteSpace(Script))
        {
            return false;
        }

        if (!Script.Contains("function process(context)"))
        {
            return false;
        }

        if (OutputSchema?.Fields == null || OutputSchema.Fields.Count == 0)
        {
            return false;
        }

        return true;
    }

    /// <summary>
    /// Gets configuration validation errors.
    /// </summary>
    public IEnumerable<string> GetValidationErrors()
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(Script))
        {
            errors.Add("Script is required and cannot be empty");
        }

        if (!Script.Contains("function process(context)"))
        {
            errors.Add("Script must contain a 'function process(context)' function");
        }

        if (OutputSchema?.Fields == null || OutputSchema.Fields.Count == 0)
        {
            errors.Add("OutputSchema.Fields is required and must contain at least one field");
        }

        if (Engine.Timeout < 100 || Engine.Timeout > 30000)
        {
            errors.Add("Engine.Timeout must be between 100ms and 30000ms");
        }

        if (Engine.MemoryLimit < 1048576 || Engine.MemoryLimit > 104857600) // 1MB to 100MB
        {
            errors.Add("Engine.MemoryLimit must be between 1MB and 100MB");
        }

        return errors;
    }

    /// <summary>
    /// Gets the index of an input field by name.
    /// </summary>
    public int GetInputFieldIndex(string fieldName)
    {
        return InputSchema?.TryGetIndex(fieldName, out int index) == true ? index : -1;
    }

    /// <summary>
    /// Gets the index of an output field by name.
    /// </summary>
    public int GetOutputFieldIndex(string fieldName)
    {
        var field = OutputSchema?.Fields?.FirstOrDefault(f => f.Name == fieldName);
        return field != null ? OutputSchema.Fields.IndexOf(field) : -1;
    }

    /// <summary>
    /// Checks if this configuration is compatible with the given input schema.
    /// </summary>
    public bool IsCompatibleWith(ISchema inputSchema)
    {
        // JavaScript transforms can work with any input schema
        return true;
    }

    /// <summary>
    /// Gets a configuration property value.
    /// </summary>
    public T GetProperty<T>(string key)
    {
        return Properties.TryGetValue(key, out var value) && value is T typedValue ? typedValue : default!;
    }

    /// <summary>
    /// Tries to get a configuration property value.
    /// </summary>
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

/// <summary>
/// JavaScript engine configuration settings.
/// </summary>
public class EngineConfiguration
{
    /// <summary>
    /// Script execution timeout in milliseconds.
    /// </summary>
    [Range(100, 30000)]
    public int Timeout { get; init; } = 5000;

    /// <summary>
    /// Memory limit for script execution in bytes.
    /// </summary>
    [Range(1048576, 104857600)] // 1MB to 100MB
    public long MemoryLimit { get; init; } = 10485760; // 10MB

    /// <summary>
    /// Enable script compilation caching for performance.
    /// </summary>
    public bool EnableCaching { get; init; } = true;

    /// <summary>
    /// Enable debugging support (reduces performance).
    /// </summary>
    public bool EnableDebugging { get; init; } = false;
}

/// <summary>
/// Output schema configuration for Mode 1.
/// </summary>
public class OutputSchemaConfiguration
{
    /// <summary>
    /// Infer schema from script (Mode 2 feature, disabled in Mode 1).
    /// </summary>
    public bool InferFromScript { get; init; } = false;

    /// <summary>
    /// Explicit field definitions for output schema.
    /// Required in Mode 1.
    /// </summary>
    [Required]
    public List<OutputFieldDefinition> Fields { get; init; } = new();
}

/// <summary>
/// Output field definition for schema creation.
/// </summary>
public class OutputFieldDefinition
{
    /// <summary>
    /// Field name in the output schema.
    /// </summary>
    [Required]
    [StringLength(100, MinimumLength = 1)]
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// Field data type as string (e.g., "string", "integer", "decimal", "datetime").
    /// </summary>
    [Required]
    public string Type { get; init; } = string.Empty;

    /// <summary>
    /// Whether the field is required in the output.
    /// </summary>
    public bool Required { get; init; } = false;

    /// <summary>
    /// Field description for documentation.
    /// </summary>
    public string? Description { get; init; }
}

/// <summary>
/// Performance and monitoring configuration.
/// </summary>
public class PerformanceConfiguration
{
    /// <summary>
    /// Target throughput in rows per second for monitoring.
    /// </summary>
    public int TargetThroughput { get; init; } = 120000;

    /// <summary>
    /// Enable performance monitoring and telemetry.
    /// </summary>
    public bool EnableMonitoring { get; init; } = true;

    /// <summary>
    /// Log performance warnings when below target.
    /// </summary>
    public bool LogPerformanceWarnings { get; init; } = true;
}
