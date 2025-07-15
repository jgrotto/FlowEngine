using FlowEngine.Abstractions.Configuration;
using FlowEngine.Abstractions.Data;
using FlowEngine.Abstractions.Factories;
using FlowEngine.Abstractions.Plugins;
using FlowEngine.Core.Configuration;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

namespace JavaScriptTransform;

/// <summary>
/// Configuration provider for JavaScript Transform plugin with comprehensive schema validation.
/// Handles creation and validation of JavaScript transform configurations.
/// </summary>
[PluginConfigurationProvider]
public class JavaScriptTransformConfigurationProvider : IPluginConfigurationProvider
{
    private readonly ILogger<JavaScriptTransformConfigurationProvider>? _logger;
    private readonly ISchemaFactory? _schemaFactory;
    private static readonly string _jsonSchema = LoadEmbeddedJsonSchema();

    /// <summary>
    /// Initializes a new instance of the JavaScriptTransformConfigurationProvider.
    /// </summary>
    public JavaScriptTransformConfigurationProvider()
    {
        // Note: Logger and SchemaFactory are optional since this provider may be instantiated during discovery
    }

    /// <summary>
    /// Initializes a new instance with logger and schema factory support.
    /// </summary>
    /// <param name="logger">Optional logger for diagnostic information</param>
    /// <param name="schemaFactory">Optional schema factory for creating output schemas</param>
    public JavaScriptTransformConfigurationProvider(
        ILogger<JavaScriptTransformConfigurationProvider>? logger = null,
        ISchemaFactory? schemaFactory = null)
    {
        _logger = logger;
        _schemaFactory = schemaFactory;
    }

    /// <summary>
    /// Gets the plugin type name this provider handles.
    /// </summary>
    public string PluginTypeName => "JavaScriptTransform.JavaScriptTransformPlugin";

    /// <summary>
    /// Gets the configuration type this provider creates.
    /// </summary>
    public Type ConfigurationType => typeof(JavaScriptTransformConfiguration);

    /// <summary>
    /// Determines if this provider can handle the specified plugin type.
    /// </summary>
    /// <param name="pluginTypeName">The plugin type name to check</param>
    /// <returns>True if this provider can handle the plugin type</returns>
    public bool CanHandle(string pluginTypeName)
    {
        return pluginTypeName == PluginTypeName ||
               pluginTypeName == "JavaScriptTransform" ||
               pluginTypeName.EndsWith(".JavaScriptTransformPlugin");
    }

    /// <summary>
    /// Creates a JavaScriptTransform configuration from the plugin definition.
    /// </summary>
    /// <param name="definition">Plugin definition with configuration data</param>
    /// <returns>Strongly-typed JavaScript transform configuration</returns>
    public async Task<FlowEngine.Abstractions.Plugins.IPluginConfiguration> CreateConfigurationAsync(IPluginDefinition definition)
    {
        _logger?.LogDebug("Creating JavaScriptTransform configuration for plugin: {PluginName}", definition.Name);

        var config = definition.Configuration ?? new Dictionary<string, object>();

        // Extract main configuration values
        var script = GetConfigValue<string>(config, "Script") ?? GetConfigValue<string>(config, "script") ?? 
            throw new ArgumentException("Script is required for JavaScriptTransform plugin");

        // Create engine configuration
        var engineConfig = CreateEngineConfiguration(config);
        var outputSchemaConfig = CreateOutputSchemaConfiguration(config);
        var performanceConfig = CreatePerformanceConfiguration(config);

        // Create the configuration instance
        var jsConfig = new JavaScriptTransformConfiguration
        {
            Name = definition.Name,
            PluginId = definition.Name,
            Script = script,
            Engine = engineConfig,
            OutputSchema = outputSchemaConfig,
            Performance = performanceConfig
        };

        _logger?.LogInformation("Successfully created JavaScriptTransform configuration for plugin: {PluginName}", definition.Name);

        await Task.CompletedTask; // Keep async signature for interface compliance
        return jsConfig;
    }

    /// <summary>
    /// Gets the JSON schema for JavaScriptTransform configuration validation.
    /// </summary>
    /// <returns>JSON schema string for configuration validation</returns>
    public string GetConfigurationJsonSchema()
    {
        return _jsonSchema;
    }

    /// <summary>
    /// Validates a plugin definition against JavaScriptTransform requirements.
    /// </summary>
    /// <param name="definition">Plugin definition to validate</param>
    /// <returns>Validation result with success status and error messages</returns>
    public FlowEngine.Abstractions.Plugins.ValidationResult ValidateConfiguration(IPluginDefinition definition)
    {
        var errors = new List<FlowEngine.Abstractions.Plugins.ValidationError>();

        var config = definition.Configuration ?? new Dictionary<string, object>();

        // Validate script presence and basic structure
        var script = GetConfigValue<string>(config, "Script") ?? GetConfigValue<string>(config, "script");
        if (string.IsNullOrWhiteSpace(script))
        {
            errors.Add(new FlowEngine.Abstractions.Plugins.ValidationError
            {
                Code = "SCRIPT_REQUIRED",
                Message = "Script is required and cannot be empty",
                Severity = FlowEngine.Abstractions.Plugins.ValidationSeverity.Error
            });
        }
        else if (!script.Contains("function process(context)"))
        {
            errors.Add(new FlowEngine.Abstractions.Plugins.ValidationError
            {
                Code = "SCRIPT_INVALID_STRUCTURE",
                Message = "Script must contain a 'function process(context)' function",
                Severity = FlowEngine.Abstractions.Plugins.ValidationSeverity.Error
            });
        }

        // Validate output schema configuration
        var outputSchemaValid = ValidateOutputSchemaConfiguration(config, errors);

        // Validate engine configuration
        ValidateEngineConfiguration(config, errors);

        return errors.Count == 0
            ? FlowEngine.Abstractions.Plugins.ValidationResult.Success()
            : FlowEngine.Abstractions.Plugins.ValidationResult.Failure(errors.ToArray());
    }

    /// <summary>
    /// Gets input schema requirements for JavaScript transforms.
    /// JavaScriptTransform accepts any input schema (flexible).
    /// </summary>
    /// <returns>Null indicating this plugin accepts any input schema</returns>
    public ISchemaRequirement? GetInputSchemaRequirement()
    {
        // JavaScript transforms are flexible and can work with any input schema
        // Return null to indicate no specific requirements
        return null;
    }

    /// <summary>
    /// Determines the output schema from the configuration.
    /// </summary>
    /// <param name="configuration">Plugin configuration</param>
    /// <returns>Output schema based on configuration</returns>
    public ISchema GetOutputSchema(FlowEngine.Abstractions.Plugins.IPluginConfiguration configuration)
    {
        if (configuration is not JavaScriptTransformConfiguration jsConfig)
            throw new ArgumentException("Configuration must be JavaScriptTransformConfiguration", nameof(configuration));

        // Convert OutputFieldDefinitions to ColumnDefinitions
        var columns = jsConfig.OutputSchema.Fields.Select((field, index) => new ColumnDefinition
        {
            Name = field.Name,
            DataType = ConvertStringToType(field.Type),
            Index = index,
            IsNullable = !field.Required,
            Description = field.Description
        }).ToArray();

        // Create schema using factory if available, otherwise create a basic implementation
        if (_schemaFactory != null)
        {
            return _schemaFactory.CreateSchema(columns);
        }

        // Create a basic schema implementation for cases where factory is not available
        return new BasicSchema(columns);
    }

    /// <summary>
    /// Gets plugin metadata for discovery and documentation.
    /// </summary>
    /// <returns>Comprehensive metadata about the JavaScript Transform plugin</returns>
    public PluginMetadata GetPluginMetadata()
    {
        return new PluginMetadata
        {
            Name = "JavaScript Transform",
            Description = "Transform data using JavaScript code with a process(context) function. " +
                         "Supports custom field mappings, calculations, and data enrichment.",
            Version = "1.0.0",
            Author = "FlowEngine Team",
            Category = PluginCategory.Transform,
            Tags = new[] { "javascript", "transform", "scripting", "custom", "mapping" },
            DocumentationUrl = new Uri("https://flowengine.dev/docs/plugins/javascript-transform"),
            MinimumEngineVersion = "1.0.0",
            License = "MIT",
            IsStable = true,
            Capabilities = new[] { "custom-scripting", "field-mapping", "data-enrichment", "validation" }
        };
    }

    /// <summary>
    /// Creates engine configuration from plugin definition data.
    /// </summary>
    private EngineConfiguration CreateEngineConfiguration(IReadOnlyDictionary<string, object> config)
    {
        var engineData = GetConfigValue<Dictionary<string, object>>(config, "Engine") ?? 
                        GetConfigValue<Dictionary<string, object>>(config, "engine") ?? 
                        new Dictionary<string, object>();

        return new EngineConfiguration
        {
            Timeout = GetConfigValue<int?>(engineData, "Timeout") ?? GetConfigValue<int?>(engineData, "timeout") ?? 5000,
            MemoryLimit = GetConfigValue<long?>(engineData, "MemoryLimit") ?? GetConfigValue<long?>(engineData, "memoryLimit") ?? 10485760,
            EnableCaching = GetConfigValue<bool?>(engineData, "EnableCaching") ?? GetConfigValue<bool?>(engineData, "enableCaching") ?? true,
            EnableDebugging = GetConfigValue<bool?>(engineData, "EnableDebugging") ?? GetConfigValue<bool?>(engineData, "enableDebugging") ?? false
        };
    }

    /// <summary>
    /// Creates output schema configuration from plugin definition data.
    /// </summary>
    private OutputSchemaConfiguration CreateOutputSchemaConfiguration(IReadOnlyDictionary<string, object> config)
    {
        var outputSchemaData = GetConfigValue<Dictionary<string, object>>(config, "OutputSchema") ?? 
                              GetConfigValue<Dictionary<string, object>>(config, "outputSchema") ?? 
                              throw new ArgumentException("OutputSchema is required for JavaScriptTransform plugin");

        var fieldsData = GetConfigValue<List<object>>(outputSchemaData, "Fields") ?? 
                        GetConfigValue<List<object>>(outputSchemaData, "fields") ?? 
                        new List<object>();

        var fields = fieldsData.Select(fieldObj =>
        {
            var fieldDict = fieldObj as Dictionary<string, object> ?? 
                           throw new ArgumentException("OutputSchema.Fields must contain valid field objects");

            return new OutputFieldDefinition
            {
                Name = GetConfigValue<string>(fieldDict, "Name") ?? GetConfigValue<string>(fieldDict, "name") ?? 
                       throw new ArgumentException("Field name is required"),
                Type = GetConfigValue<string>(fieldDict, "Type") ?? GetConfigValue<string>(fieldDict, "type") ?? 
                       throw new ArgumentException("Field type is required"),
                Required = GetConfigValue<bool?>(fieldDict, "Required") ?? GetConfigValue<bool?>(fieldDict, "required") ?? false,
                Description = GetConfigValue<string>(fieldDict, "Description") ?? GetConfigValue<string>(fieldDict, "description")
            };
        }).ToList();

        return new OutputSchemaConfiguration
        {
            InferFromScript = GetConfigValue<bool?>(outputSchemaData, "InferFromScript") ?? 
                             GetConfigValue<bool?>(outputSchemaData, "inferFromScript") ?? false,
            Fields = fields
        };
    }

    /// <summary>
    /// Creates performance configuration from plugin definition data.
    /// </summary>
    private PerformanceConfiguration CreatePerformanceConfiguration(IReadOnlyDictionary<string, object> config)
    {
        var perfData = GetConfigValue<Dictionary<string, object>>(config, "Performance") ?? 
                      GetConfigValue<Dictionary<string, object>>(config, "performance") ?? 
                      new Dictionary<string, object>();

        return new PerformanceConfiguration
        {
            TargetThroughput = GetConfigValue<int?>(perfData, "TargetThroughput") ?? 
                              GetConfigValue<int?>(perfData, "targetThroughput") ?? 120000,
            EnableMonitoring = GetConfigValue<bool?>(perfData, "EnableMonitoring") ?? 
                              GetConfigValue<bool?>(perfData, "enableMonitoring") ?? true,
            LogPerformanceWarnings = GetConfigValue<bool?>(perfData, "LogPerformanceWarnings") ?? 
                                    GetConfigValue<bool?>(perfData, "logPerformanceWarnings") ?? true
        };
    }

    /// <summary>
    /// Validates output schema configuration and adds errors to the list.
    /// </summary>
    private bool ValidateOutputSchemaConfiguration(IReadOnlyDictionary<string, object> config, List<FlowEngine.Abstractions.Plugins.ValidationError> errors)
    {
        var outputSchemaData = GetConfigValue<Dictionary<string, object>>(config, "OutputSchema") ?? 
                              GetConfigValue<Dictionary<string, object>>(config, "outputSchema");

        if (outputSchemaData == null)
        {
            errors.Add(new FlowEngine.Abstractions.Plugins.ValidationError
            {
                Code = "OUTPUT_SCHEMA_REQUIRED",
                Message = "OutputSchema configuration is required",
                Severity = FlowEngine.Abstractions.Plugins.ValidationSeverity.Error
            });
            return false;
        }

        var fieldsData = GetConfigValue<List<object>>(outputSchemaData, "Fields") ?? 
                        GetConfigValue<List<object>>(outputSchemaData, "fields");

        if (fieldsData == null || fieldsData.Count == 0)
        {
            errors.Add(new FlowEngine.Abstractions.Plugins.ValidationError
            {
                Code = "OUTPUT_FIELDS_REQUIRED",
                Message = "OutputSchema.Fields is required and must contain at least one field",
                Severity = FlowEngine.Abstractions.Plugins.ValidationSeverity.Error
            });
            return false;
        }

        // Validate each field
        foreach (var fieldObj in fieldsData)
        {
            if (fieldObj is not Dictionary<string, object> fieldDict)
            {
                errors.Add(new FlowEngine.Abstractions.Plugins.ValidationError
                {
                    Code = "INVALID_FIELD_DEFINITION",
                    Message = "Each field in OutputSchema.Fields must be a valid object",
                    Severity = FlowEngine.Abstractions.Plugins.ValidationSeverity.Error
                });
                continue;
            }

            var fieldName = GetConfigValue<string>(fieldDict, "Name") ?? GetConfigValue<string>(fieldDict, "name");
            if (string.IsNullOrWhiteSpace(fieldName))
            {
                errors.Add(new FlowEngine.Abstractions.Plugins.ValidationError
                {
                    Code = "FIELD_NAME_REQUIRED",
                    Message = "Field name is required and cannot be empty",
                    Severity = FlowEngine.Abstractions.Plugins.ValidationSeverity.Error
                });
            }

            var fieldType = GetConfigValue<string>(fieldDict, "Type") ?? GetConfigValue<string>(fieldDict, "type");
            if (string.IsNullOrWhiteSpace(fieldType))
            {
                errors.Add(new FlowEngine.Abstractions.Plugins.ValidationError
                {
                    Code = "FIELD_TYPE_REQUIRED",
                    Message = $"Field type is required for field '{fieldName}'",
                    Severity = FlowEngine.Abstractions.Plugins.ValidationSeverity.Error
                });
            }
            else if (!IsValidFieldType(fieldType))
            {
                errors.Add(new FlowEngine.Abstractions.Plugins.ValidationError
                {
                    Code = "INVALID_FIELD_TYPE",
                    Message = $"Invalid field type '{fieldType}' for field '{fieldName}'. Supported types: string, integer, decimal, double, boolean, datetime, date, time",
                    Severity = FlowEngine.Abstractions.Plugins.ValidationSeverity.Error
                });
            }
        }

        return errors.Count == 0;
    }

    /// <summary>
    /// Validates engine configuration and adds errors to the list.
    /// </summary>
    private void ValidateEngineConfiguration(IReadOnlyDictionary<string, object> config, List<FlowEngine.Abstractions.Plugins.ValidationError> errors)
    {
        var engineData = GetConfigValue<Dictionary<string, object>>(config, "Engine") ?? 
                        GetConfigValue<Dictionary<string, object>>(config, "engine");

        if (engineData == null) return; // Engine config is optional

        var timeout = GetConfigValue<int?>(engineData, "Timeout") ?? GetConfigValue<int?>(engineData, "timeout");
        if (timeout.HasValue && (timeout < 100 || timeout > 30000))
        {
            errors.Add(new FlowEngine.Abstractions.Plugins.ValidationError
            {
                Code = "INVALID_TIMEOUT",
                Message = "Engine timeout must be between 100ms and 30000ms",
                Severity = FlowEngine.Abstractions.Plugins.ValidationSeverity.Error
            });
        }

        var memoryLimit = GetConfigValue<long?>(engineData, "MemoryLimit") ?? GetConfigValue<long?>(engineData, "memoryLimit");
        if (memoryLimit.HasValue && (memoryLimit < 1048576 || memoryLimit > 104857600))
        {
            errors.Add(new FlowEngine.Abstractions.Plugins.ValidationError
            {
                Code = "INVALID_MEMORY_LIMIT",
                Message = "Engine memory limit must be between 1MB and 100MB",
                Severity = FlowEngine.Abstractions.Plugins.ValidationSeverity.Error
            });
        }
    }

    /// <summary>
    /// Loads the embedded JSON schema from the assembly.
    /// </summary>
    private static string LoadEmbeddedJsonSchema()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var resourceName = "JavaScriptTransform.Schemas.JavaScriptTransformConfiguration.json";
        
        using var stream = assembly.GetManifestResourceStream(resourceName);
        if (stream == null)
            throw new InvalidOperationException($"Could not find embedded resource: {resourceName}");
        
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    /// <summary>
    /// Gets a configuration value with type conversion.
    /// </summary>
    private T? GetConfigValue<T>(IReadOnlyDictionary<string, object> config, string key)
    {
        if (!config.TryGetValue(key, out var value))
            return default;

        if (value is T directValue)
            return directValue;

        try
        {
            return (T)Convert.ChangeType(value, typeof(T));
        }
        catch
        {
            return default;
        }
    }

    /// <summary>
    /// Converts string type name to .NET Type.
    /// </summary>
    private static Type ConvertStringToType(string typeName)
    {
        return typeName.ToLowerInvariant() switch
        {
            "string" => typeof(string),
            "integer" => typeof(int),
            "decimal" => typeof(decimal),
            "double" => typeof(double),
            "boolean" => typeof(bool),
            "datetime" => typeof(DateTime),
            "date" => typeof(DateOnly),
            "time" => typeof(TimeOnly),
            _ => typeof(string) // Default fallback
        };
    }

    /// <summary>
    /// Validates if a field type string is supported.
    /// </summary>
    private static bool IsValidFieldType(string typeName)
    {
        var validTypes = new[] { "string", "integer", "decimal", "double", "boolean", "datetime", "date", "time" };
        return validTypes.Contains(typeName.ToLowerInvariant());
    }
}

/// <summary>
/// Basic schema implementation for cases where schema factory is not available.
/// </summary>
internal class BasicSchema : ISchema
{
    private readonly ImmutableArray<ColumnDefinition> _columns;

    /// <summary>
    /// Gets the column definitions for this schema.
    /// </summary>
    public ImmutableArray<ColumnDefinition> Columns => _columns;

    /// <summary>
    /// Gets the number of columns in this schema.
    /// </summary>
    public int ColumnCount => _columns.Length;

    /// <summary>
    /// Gets the schema signature for equality checks.
    /// </summary>
    public string Signature => string.Join("|", _columns.Select(c => $"{c.Name}:{c.DataType.Name}"));


    /// <summary>
    /// Initializes a new BasicSchema with the specified columns.
    /// </summary>
    /// <param name="columns">Column definitions for the schema</param>
    public BasicSchema(IEnumerable<ColumnDefinition> columns)
    {
        _columns = (columns ?? throw new ArgumentNullException(nameof(columns))).ToImmutableArray();
    }

    /// <summary>
    /// Gets the index of a column by name.
    /// </summary>
    /// <param name="columnName">Name of the column</param>
    /// <returns>Column index</returns>
    /// <exception cref="ArgumentException">Thrown when column is not found</exception>
    public int GetIndex(string columnName)
    {
        for (int i = 0; i < _columns.Length; i++)
        {
            if (string.Equals(_columns[i].Name, columnName, StringComparison.OrdinalIgnoreCase))
                return i;
        }
        throw new ArgumentException($"Column '{columnName}' not found in schema", nameof(columnName));
    }

    /// <summary>
    /// Gets a column definition by name.
    /// </summary>
    /// <param name="columnName">Name of the column</param>
    /// <returns>Column definition</returns>
    /// <exception cref="ArgumentException">Thrown when column is not found</exception>
    public ColumnDefinition? GetColumn(string columnName)
    {
        return _columns.FirstOrDefault(c => string.Equals(c.Name, columnName, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Checks if a column exists in the schema.
    /// </summary>
    /// <param name="columnName">Name of the column</param>
    /// <returns>True if column exists</returns>
    public bool HasColumn(string columnName)
    {
        return _columns.Any(c => string.Equals(c.Name, columnName, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Tries to get the index of a field by name.
    /// </summary>
    /// <param name="fieldName">Name of the field to find</param>
    /// <param name="index">Output parameter for the field index</param>
    /// <returns>True if the field was found, false otherwise</returns>
    public bool TryGetIndex(string fieldName, out int index)
    {
        for (int i = 0; i < _columns.Length; i++)
        {
            if (string.Equals(_columns[i].Name, fieldName, StringComparison.OrdinalIgnoreCase))
            {
                index = i;
                return true;
            }
        }
        index = -1;
        return false;
    }

    /// <summary>
    /// Adds columns to create a new schema.
    /// </summary>
    /// <param name="columnsToAdd">Columns to add</param>
    /// <returns>New schema with added columns</returns>
    public ISchema AddColumns(params ColumnDefinition[] columnsToAdd)
    {
        var newColumns = _columns.ToList();
        int nextIndex = _columns.Length;
        
        foreach (var column in columnsToAdd)
        {
            var newColumn = column with { Index = nextIndex++ };
            newColumns.Add(newColumn);
        }
        
        return new BasicSchema(newColumns);
    }

    /// <summary>
    /// Removes columns to create a new schema.
    /// </summary>
    /// <param name="columnNames">Names of columns to remove</param>
    /// <returns>New schema with columns removed</returns>
    public ISchema RemoveColumns(params string[] columnNames)
    {
        var columnsToKeep = _columns.Where(c => !columnNames.Contains(c.Name, StringComparer.OrdinalIgnoreCase)).ToList();
        
        // Reindex the remaining columns
        for (int i = 0; i < columnsToKeep.Count; i++)
        {
            columnsToKeep[i] = columnsToKeep[i] with { Index = i };
        }
        
        return new BasicSchema(columnsToKeep);
    }

    /// <summary>
    /// Gets the field index by name, or -1 if not found.
    /// </summary>
    /// <param name="fieldName">Name of the field</param>
    /// <returns>Field index or -1 if not found</returns>
    public int GetFieldIndex(string fieldName)
    {
        return TryGetIndex(fieldName, out int index) ? index : -1;
    }

    /// <summary>
    /// Validates if this schema is compatible with another schema.
    /// </summary>
    /// <param name="other">Schema to compare with</param>
    /// <returns>True if schemas are compatible</returns>
    public bool IsCompatibleWith(ISchema other)
    {
        if (other == null) return false;

        var thisColumns = _columns.ToDictionary(c => c.Name, StringComparer.OrdinalIgnoreCase);
        
        foreach (var otherColumn in other.Columns)
        {
            if (!thisColumns.TryGetValue(otherColumn.Name, out var thisColumn))
                continue;

            // Check type compatibility
            if (thisColumn.DataType != otherColumn.DataType)
                return false;
        }

        return true;
    }

    /// <summary>
    /// Determines equality with another schema.
    /// </summary>
    public bool Equals(ISchema? other)
    {
        return other != null && Signature == other.Signature;
    }

    /// <summary>
    /// Determines equality based on column definitions.
    /// </summary>
    public override bool Equals(object? obj)
    {
        return obj is ISchema other && Equals(other);
    }

    /// <summary>
    /// Gets hash code based on signature.
    /// </summary>
    public override int GetHashCode()
    {
        return Signature.GetHashCode();
    }
}