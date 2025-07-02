using System.Text.Json;

namespace FlowEngine.Abstractions.Schema;

/// <summary>
/// Attribute to specify JSON schema validation for a property or class.
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Class | AttributeTargets.Field, AllowMultiple = false)]
public class JsonSchemaAttribute : Attribute
{
    /// <summary>
    /// Gets the schema type (string, number, boolean, object, array).
    /// </summary>
    public string? Type { get; init; }

    /// <summary>
    /// Gets the description for this property.
    /// </summary>
    public string? Description { get; init; }

    /// <summary>
    /// Gets the validation pattern (regex) for string types.
    /// </summary>
    public string? Pattern { get; init; }

    /// <summary>
    /// Gets the minimum value for numeric types.
    /// </summary>
    public double? Minimum { get; init; }

    /// <summary>
    /// Gets the maximum value for numeric types.
    /// </summary>
    public double? Maximum { get; init; }

    /// <summary>
    /// Gets the minimum length for string and array types.
    /// </summary>
    public int? MinLength { get; init; }

    /// <summary>
    /// Gets the maximum length for string and array types.
    /// </summary>
    public int? MaxLength { get; init; }

    /// <summary>
    /// Gets whether this property is required.
    /// </summary>
    public bool Required { get; init; } = false;

    /// <summary>
    /// Gets the default value as a JSON string.
    /// </summary>
    public string? DefaultValue { get; init; }

    /// <summary>
    /// Gets example values as JSON strings.
    /// </summary>
    public string[]? Examples { get; init; }

    /// <summary>
    /// Gets enumeration values as JSON strings.
    /// </summary>
    public string[]? Enum { get; init; }

    /// <summary>
    /// Gets the format specification for string types (e.g., "email", "uri", "date-time").
    /// </summary>
    public string? Format { get; init; }
}

/// <summary>
/// Attribute to specify that a property should be validated as a file path.
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false)]
public sealed class FilePathSchemaAttribute : JsonSchemaAttribute
{
    /// <summary>
    /// Initializes a new instance of the FilePathSchemaAttribute class.
    /// </summary>
    /// <param name="description">Description of the file path</param>
    /// <param name="mustExist">Whether the file must exist</param>
    /// <param name="extensions">Allowed file extensions (e.g., ".csv", ".json")</param>
    public FilePathSchemaAttribute(string? description = null, bool mustExist = true, params string[]? extensions)
    {
        Type = "string";
        Description = description ?? "File path";
        MustExist = mustExist;
        AllowedExtensions = extensions;
        
        // Create pattern for file path validation
        if (extensions?.Length > 0)
        {
            var extensionsPattern = string.Join("|", extensions.Select(ext => 
                ext.StartsWith('.') ? "\\" + ext : "\\." + ext));
            Pattern = $@"^.+({extensionsPattern})$";
        }
    }

    /// <summary>
    /// Gets whether the file must exist.
    /// </summary>
    public bool MustExist { get; }

    /// <summary>
    /// Gets the allowed file extensions.
    /// </summary>
    public string[]? AllowedExtensions { get; }
}

/// <summary>
/// Attribute to specify that a property should be validated as a directory path.
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false)]
public sealed class DirectoryPathSchemaAttribute : JsonSchemaAttribute
{
    /// <summary>
    /// Initializes a new instance of the DirectoryPathSchemaAttribute class.
    /// </summary>
    /// <param name="description">Description of the directory path</param>
    /// <param name="mustExist">Whether the directory must exist</param>
    public DirectoryPathSchemaAttribute(string? description = null, bool mustExist = true)
    {
        Type = "string";
        Description = description ?? "Directory path";
        MustExist = mustExist;
        MinLength = 1;
    }

    /// <summary>
    /// Gets whether the directory must exist.
    /// </summary>
    public bool MustExist { get; }
}

/// <summary>
/// Attribute to specify that a numeric property has performance implications.
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false)]
public sealed class PerformanceSchemaAttribute : JsonSchemaAttribute
{
    /// <summary>
    /// Initializes a new instance of the PerformanceSchemaAttribute class.
    /// </summary>
    /// <param name="description">Description of the performance impact</param>
    /// <param name="recommendedMin">Recommended minimum value</param>
    /// <param name="recommendedMax">Recommended maximum value</param>
    /// <param name="performanceNote">Additional performance guidance</param>
    public PerformanceSchemaAttribute(
        string? description = null, 
        double? recommendedMin = null, 
        double? recommendedMax = null,
        string? performanceNote = null)
    {
        Type = "number";
        Description = description;
        RecommendedMin = recommendedMin;
        RecommendedMax = recommendedMax;
        PerformanceNote = performanceNote;
        
        if (recommendedMin.HasValue)
            Minimum = recommendedMin.Value;
        if (recommendedMax.HasValue)
            Maximum = recommendedMax.Value;
    }

    /// <summary>
    /// Gets the recommended minimum value for optimal performance.
    /// </summary>
    public double? RecommendedMin { get; }

    /// <summary>
    /// Gets the recommended maximum value for optimal performance.
    /// </summary>
    public double? RecommendedMax { get; }

    /// <summary>
    /// Gets additional performance guidance.
    /// </summary>
    public string? PerformanceNote { get; }
}

/// <summary>
/// Attribute to specify that a string property should be validated as a specific format.
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false)]
public sealed class FormatSchemaAttribute : JsonSchemaAttribute
{
    /// <summary>
    /// Initializes a new instance of the FormatSchemaAttribute class.
    /// </summary>
    /// <param name="format">The format to validate (e.g., "email", "uri", "date-time")</param>
    /// <param name="description">Description of the format requirement</param>
    public FormatSchemaAttribute(string format, string? description = null)
    {
        Type = "string";
        Format = format ?? throw new ArgumentNullException(nameof(format));
        Description = description ?? $"Must be a valid {format}";
    }
}

/// <summary>
/// Attribute to specify that a property contains sensitive information.
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false)]
public sealed class SensitiveSchemaAttribute : JsonSchemaAttribute
{
    /// <summary>
    /// Initializes a new instance of the SensitiveSchemaAttribute class.
    /// </summary>
    /// <param name="description">Description of the sensitive data</param>
    /// <param name="encryptionRequired">Whether encryption is required</param>
    public SensitiveSchemaAttribute(string? description = null, bool encryptionRequired = false)
    {
        Type = "string";
        Description = description ?? "Sensitive information";
        EncryptionRequired = encryptionRequired;
        MinLength = 1;
    }

    /// <summary>
    /// Gets whether encryption is required for this sensitive property.
    /// </summary>
    public bool EncryptionRequired { get; }
}

/// <summary>
/// Attribute to specify schema validation for ArrayRow field definitions.
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false)]
public sealed class ArrayRowSchemaAttribute : JsonSchemaAttribute
{
    /// <summary>
    /// Initializes a new instance of the ArrayRowSchemaAttribute class.
    /// </summary>
    /// <param name="description">Description of the ArrayRow schema requirements</param>
    /// <param name="requiresSequentialIndexes">Whether sequential field indexes are required</param>
    /// <param name="maxFields">Maximum number of fields allowed</param>
    public ArrayRowSchemaAttribute(
        string? description = null, 
        bool requiresSequentialIndexes = true,
        int? maxFields = null)
    {
        Type = "object";
        Description = description ?? "Schema definition with ArrayRow optimization";
        RequiresSequentialIndexes = requiresSequentialIndexes;
        MaxFields = maxFields;
    }

    /// <summary>
    /// Gets whether sequential field indexes are required for ArrayRow optimization.
    /// </summary>
    public bool RequiresSequentialIndexes { get; }

    /// <summary>
    /// Gets the maximum number of fields allowed for optimal performance.
    /// </summary>
    public int? MaxFields { get; }
}

/// <summary>
/// Attribute to mark a property as containing plugin-specific configuration.
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Class, AllowMultiple = false)]
public sealed class PluginConfigurationSchemaAttribute : JsonSchemaAttribute
{
    /// <summary>
    /// Initializes a new instance of the PluginConfigurationSchemaAttribute class.
    /// </summary>
    /// <param name="pluginType">The type of plugin this configuration is for</param>
    /// <param name="version">The configuration schema version</param>
    /// <param name="description">Description of the configuration</param>
    public PluginConfigurationSchemaAttribute(
        string pluginType, 
        string version = "1.0.0", 
        string? description = null)
    {
        PluginType = pluginType ?? throw new ArgumentNullException(nameof(pluginType));
        Version = version ?? throw new ArgumentNullException(nameof(version));
        Description = description ?? $"Configuration for {pluginType} plugin";
        Type = "object";
    }

    /// <summary>
    /// Gets the plugin type this configuration is for.
    /// </summary>
    public string PluginType { get; }

    /// <summary>
    /// Gets the configuration schema version.
    /// </summary>
    public string Version { get; }
}