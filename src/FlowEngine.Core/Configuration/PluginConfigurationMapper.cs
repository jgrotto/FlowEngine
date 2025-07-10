using FlowEngine.Abstractions;
using FlowEngine.Abstractions.Configuration;
using FlowEngine.Abstractions.Data;
using FlowEngine.Abstractions.Factories;
using FlowEngine.Abstractions.Plugins;
using Microsoft.Extensions.Logging;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Reflection;
using System.Text.Json;

namespace FlowEngine.Core.Configuration;

/// <summary>
/// Enhanced plugin configuration mapping service that supports strongly-typed configuration
/// for various plugin types with automatic type conversion and validation.
/// </summary>
public sealed class PluginConfigurationMapper : IPluginConfigurationMapper
{
    private readonly ILogger<PluginConfigurationMapper> _logger;
    private readonly ISchemaFactory _schemaFactory;
    private readonly ConcurrentDictionary<string, PluginConfigurationMapping> _mappings = new();
    private readonly JsonSerializerOptions _jsonOptions;

    /// <summary>
    /// Initializes a new instance of the plugin configuration mapper.
    /// </summary>
    /// <param name="logger">Logger for configuration mapping operations</param>
    /// <param name="schemaFactory">Factory for creating schema objects</param>
    public PluginConfigurationMapper(ILogger<PluginConfigurationMapper> logger, ISchemaFactory schemaFactory)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _schemaFactory = schemaFactory ?? throw new ArgumentNullException(nameof(schemaFactory));
        
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = true,
            AllowTrailingCommas = true
        };
        
        // Register default mappings for known plugin types
        RegisterDefaultMappings();
    }

    /// <summary>
    /// Maps plugin definition configuration to strongly-typed plugin configuration.
    /// </summary>
    /// <param name="definition">Plugin definition with configuration data</param>
    /// <returns>Strongly-typed plugin configuration</returns>
    public async Task<FlowEngine.Abstractions.Plugins.IPluginConfiguration> CreateConfigurationAsync(IPluginDefinition definition)
    {
        if (definition == null)
            throw new ArgumentNullException(nameof(definition));

        _logger.LogDebug("Creating configuration for plugin '{PluginName}' of type '{PluginType}'", 
            definition.Name, definition.Type);

        try
        {
            // Check if we have a specific mapping for this plugin type
            if (_mappings.TryGetValue(definition.Type, out var mapping))
            {
                _logger.LogDebug("Using registered configuration mapping for plugin type '{PluginType}'", definition.Type);
                return await mapping.CreateConfigurationAsync(definition);
            }

            // Try to discover configuration type from assembly
            var discoveredConfig = await DiscoverConfigurationTypeAsync(definition);
            if (discoveredConfig != null)
            {
                _logger.LogDebug("Using discovered configuration type for plugin '{PluginName}'", definition.Name);
                return discoveredConfig;
            }

            // Fall back to simple configuration
            _logger.LogWarning("Using simple configuration for unknown plugin type '{PluginType}'", definition.Type);
            return new FlowEngine.Core.Plugins.SimplePluginConfiguration(definition);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create configuration for plugin '{PluginName}' of type '{PluginType}'", 
                definition.Name, definition.Type);
            throw new PluginLoadException($"Failed to create configuration: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Registers a configuration mapping for a specific plugin type.
    /// </summary>
    /// <param name="pluginType">Full type name of the plugin</param>
    /// <param name="mapping">Configuration mapping implementation</param>
    public void RegisterMapping(string pluginType, PluginConfigurationMapping mapping)
    {
        if (string.IsNullOrWhiteSpace(pluginType))
            throw new ArgumentException("Plugin type cannot be null or empty", nameof(pluginType));
        
        if (mapping == null)
            throw new ArgumentNullException(nameof(mapping));

        _mappings.AddOrUpdate(pluginType, mapping, (key, oldValue) => mapping);
        _logger.LogInformation("Registered configuration mapping for plugin type '{PluginType}'", pluginType);
    }

    /// <summary>
    /// Registers default configuration mappings for known plugin types.
    /// </summary>
    private void RegisterDefaultMappings()
    {
        // DelimitedSource plugin mapping
        RegisterMapping("DelimitedSource.DelimitedSourcePlugin", new PluginConfigurationMapping
        {
            ConfigurationType = typeof(object), // Will be resolved at runtime from plugin assembly
            CreateConfigurationAsync = async (definition) => await CreateDelimitedSourceConfigurationAsync(definition)
        });

        // DelimitedSink plugin mapping
        RegisterMapping("DelimitedSink.DelimitedSinkPlugin", new PluginConfigurationMapping
        {
            ConfigurationType = typeof(object), // Will be resolved at runtime from plugin assembly
            CreateConfigurationAsync = async (definition) => await CreateDelimitedSinkConfigurationAsync(definition)
        });

        // JavaScriptTransform plugin mapping
        RegisterMapping("JavaScriptTransform.JavaScriptTransformPlugin", new PluginConfigurationMapping
        {
            ConfigurationType = typeof(object), // Will be resolved at runtime from plugin assembly
            CreateConfigurationAsync = async (definition) => await CreateJavaScriptTransformConfigurationAsync(definition)
        });

        // TemplatePlugin mapping removed - will be handled by dynamic discovery or fallback to SimplePluginConfiguration
        // TODO: Create new SinkTemplate, SourceTemplate, TransformTemplate plugins with proper architecture later
    }

    /// <summary>
    /// Creates configuration for DelimitedSource plugin using its own configuration type.
    /// </summary>
    private async Task<FlowEngine.Abstractions.Plugins.IPluginConfiguration> CreateDelimitedSourceConfigurationAsync(IPluginDefinition definition)
    {
        var config = definition.Configuration ?? new Dictionary<string, object>();
        
        // Use reflection to create the plugin's own configuration type
        var assemblyPath = definition.AssemblyPath ?? throw new PluginLoadException("Assembly path required for DelimitedSource plugin");
        var assembly = Assembly.LoadFrom(assemblyPath);
        var configType = assembly.GetType("DelimitedSource.DelimitedSourceConfiguration");
        
        if (configType == null)
            throw new PluginLoadException("DelimitedSourceConfiguration type not found in plugin assembly");

        // Extract configuration values
        var filePath = ExtractConfigValue<string>(config, "FilePath") ?? 
            throw new PluginLoadException("FilePath is required for DelimitedSource plugin");
        var hasHeaders = ExtractConfigValue<bool>(config, "HasHeaders", true);
        var delimiter = ExtractConfigValue<string>(config, "Delimiter", ",");
        var chunkSize = ExtractConfigValue<int>(config, "ChunkSize", 1000);
        var encoding = ExtractConfigValue<string>(config, "Encoding", "UTF-8");
        var inferSchema = ExtractConfigValue<bool>(config, "InferSchema", true);
        var inferenceSampleRows = ExtractConfigValue<int>(config, "InferenceSampleRows", 100);
        var skipMalformedRows = ExtractConfigValue<bool>(config, "SkipMalformedRows", false);
        var maxErrors = ExtractConfigValue<int>(config, "MaxErrors", 0);
        
        // Extract OutputSchema if provided
        ISchema? outputSchema = null;
        if (config.TryGetValue("OutputSchema", out var schemaConfig) && schemaConfig != null)
        {
            outputSchema = ParseSchemaFromConfig(schemaConfig);
        }

        // Create instance using object initializer pattern via reflection
        var configInstance = Activator.CreateInstance(configType);
        
        // Set properties using reflection
        SetInitProperty(configInstance, "FilePath", filePath);
        SetInitProperty(configInstance, "HasHeaders", hasHeaders);
        SetInitProperty(configInstance, "Delimiter", delimiter);
        SetInitProperty(configInstance, "ChunkSize", chunkSize);
        SetInitProperty(configInstance, "Encoding", encoding);
        SetInitProperty(configInstance, "InferSchema", inferSchema);
        SetInitProperty(configInstance, "InferenceSampleRows", inferenceSampleRows);
        SetInitProperty(configInstance, "SkipMalformedRows", skipMalformedRows);
        SetInitProperty(configInstance, "MaxErrors", maxErrors);
        
        // Set OutputSchema if provided
        if (outputSchema != null)
        {
            SetInitProperty(configInstance, "OutputSchema", outputSchema);
        }
        
        return (FlowEngine.Abstractions.Plugins.IPluginConfiguration)configInstance;
    }

    /// <summary>
    /// Creates configuration for DelimitedSink plugin using its own configuration type.
    /// </summary>
    private async Task<FlowEngine.Abstractions.Plugins.IPluginConfiguration> CreateDelimitedSinkConfigurationAsync(IPluginDefinition definition)
    {
        var config = definition.Configuration ?? new Dictionary<string, object>();
        
        // Use reflection to create the plugin's own configuration type
        var assemblyPath = definition.AssemblyPath ?? throw new PluginLoadException("Assembly path required for DelimitedSink plugin");
        var assembly = Assembly.LoadFrom(assemblyPath);
        var configType = assembly.GetType("DelimitedSink.DelimitedSinkConfiguration");
        
        if (configType == null)
            throw new PluginLoadException("DelimitedSinkConfiguration type not found in plugin assembly");

        // Extract configuration values
        var filePath = ExtractConfigValue<string>(config, "FilePath") ?? 
            throw new PluginLoadException("FilePath is required for DelimitedSink plugin");
        var includeHeaders = ExtractConfigValue<bool>(config, "HasHeaders", true); // Map HasHeaders to IncludeHeaders
        var delimiter = ExtractConfigValue<string>(config, "Delimiter", ",");
        var encoding = ExtractConfigValue<string>(config, "Encoding", "UTF-8");
        var bufferSize = ExtractConfigValue<int>(config, "BufferSize", 8192);
        var flushInterval = ExtractConfigValue<int>(config, "FlushInterval", 1000);
        var createDirectories = ExtractConfigValue<bool>(config, "CreateDirectory", true); // Map CreateDirectory to CreateDirectories
        var overwriteExisting = ExtractConfigValue<bool>(config, "OverwriteExisting", true);

        // Create instance using object initializer pattern via reflection
        var configInstance = Activator.CreateInstance(configType);
        
        // Set properties using reflection (use correct property names from plugin)
        SetInitProperty(configInstance, "FilePath", filePath);
        SetInitProperty(configInstance, "IncludeHeaders", includeHeaders);
        SetInitProperty(configInstance, "Delimiter", delimiter);
        SetInitProperty(configInstance, "Encoding", encoding);
        SetInitProperty(configInstance, "BufferSize", bufferSize);
        SetInitProperty(configInstance, "FlushInterval", flushInterval);
        SetInitProperty(configInstance, "CreateDirectories", createDirectories);
        SetInitProperty(configInstance, "OverwriteExisting", overwriteExisting);
        
        return (FlowEngine.Abstractions.Plugins.IPluginConfiguration)configInstance;
    }

    /// <summary>
    /// Creates configuration for JavaScriptTransform plugin using its own configuration type.
    /// </summary>
    private async Task<FlowEngine.Abstractions.Plugins.IPluginConfiguration> CreateJavaScriptTransformConfigurationAsync(IPluginDefinition definition)
    {
        var config = definition.Configuration ?? new Dictionary<string, object>();
        
        // Use reflection to create the plugin's own configuration type
        var assemblyPath = definition.AssemblyPath ?? throw new PluginLoadException("Assembly path required for JavaScriptTransform plugin");
        var assembly = Assembly.LoadFrom(assemblyPath);
        var configType = assembly.GetType("JavaScriptTransform.JavaScriptTransformConfiguration");
        var engineConfigType = assembly.GetType("JavaScriptTransform.EngineConfiguration");
        var outputSchemaConfigType = assembly.GetType("JavaScriptTransform.OutputSchemaConfiguration");
        var outputFieldDefType = assembly.GetType("JavaScriptTransform.OutputFieldDefinition");
        var performanceConfigType = assembly.GetType("JavaScriptTransform.PerformanceConfiguration");
        
        if (configType == null)
            throw new PluginLoadException("JavaScriptTransformConfiguration type not found in plugin assembly");

        // Extract configuration values
        var script = ExtractConfigValue<string>(config, "Script") ?? 
            throw new PluginLoadException("Script is required for JavaScriptTransform plugin");
        var timeoutSeconds = ExtractConfigValue<int>(config, "TimeoutSeconds", 30);

        // Create Engine configuration
        var engineConfig = Activator.CreateInstance(engineConfigType);
        SetInitProperty(engineConfig, "Timeout", timeoutSeconds * 1000); // Convert to milliseconds
        
        // Create output schema configuration from YAML
        var outputSchemaConfig = Activator.CreateInstance(outputSchemaConfigType);
        var fieldsListType = typeof(List<>).MakeGenericType(outputFieldDefType);
        var fieldsList = Activator.CreateInstance(fieldsListType);
        
        // Parse OutputSchema from YAML configuration if provided
        if (config.TryGetValue("OutputSchema", out var schemaConfig) && schemaConfig != null)
        {
            var schema = ParseSchemaFromConfig(schemaConfig);
            
            // Convert schema columns to JavaScriptTransform output field definitions
            foreach (var column in schema.Columns)
            {
                var outputField = Activator.CreateInstance(outputFieldDefType);
                SetInitProperty(outputField, "Name", column.Name);
                SetInitProperty(outputField, "Type", ConvertTypeToString(column.DataType));
                SetInitProperty(outputField, "Required", !column.IsNullable);
                
                // Add the field to the list
                var addMethod = fieldsListType.GetMethod("Add");
                addMethod?.Invoke(fieldsList, new[] { outputField });
            }
        }
        else
        {
            // Fallback: Add basic output field if no schema provided
            var outputField = Activator.CreateInstance(outputFieldDefType);
            SetInitProperty(outputField, "Name", "result");
            SetInitProperty(outputField, "Type", "string");
            SetInitProperty(outputField, "Required", false);
            
            // Add the field to the list
            var addMethod = fieldsListType.GetMethod("Add");
            addMethod?.Invoke(fieldsList, new[] { outputField });
        }
        
        SetInitProperty(outputSchemaConfig, "Fields", fieldsList);
        
        // Create Performance configuration
        var performanceConfig = Activator.CreateInstance(performanceConfigType);
        
        // Create main configuration instance
        var configInstance = Activator.CreateInstance(configType);
        
        // Set properties using reflection
        SetInitProperty(configInstance, "Script", script);
        SetInitProperty(configInstance, "Engine", engineConfig);
        SetInitProperty(configInstance, "OutputSchema", outputSchemaConfig);
        SetInitProperty(configInstance, "Performance", performanceConfig);
        
        return (FlowEngine.Abstractions.Plugins.IPluginConfiguration)configInstance;
    }

    // TemplatePlugin configuration method removed - will be handled by dynamic discovery fallback
    // TODO: Implement proper template plugin architecture with SinkTemplate, SourceTemplate, TransformTemplate

    /// <summary>
    /// Attempts to discover configuration type from plugin assembly.
    /// </summary>
    private async Task<FlowEngine.Abstractions.Plugins.IPluginConfiguration?> DiscoverConfigurationTypeAsync(IPluginDefinition definition)
    {
        if (string.IsNullOrWhiteSpace(definition.AssemblyPath))
            return null;

        try
        {
            var assembly = Assembly.LoadFrom(definition.AssemblyPath);
            var pluginType = assembly.GetType(definition.Type);
            
            if (pluginType == null)
                return null;

            // Look for configuration type with common naming patterns
            var configTypeNames = new[]
            {
                $"{definition.Type}Configuration",
                $"{pluginType.Name}Configuration",
                $"{pluginType.Namespace}.{pluginType.Name}Configuration"
            };

            foreach (var configTypeName in configTypeNames)
            {
                var configType = assembly.GetType(configTypeName);
                if (configType != null)
                {
                    _logger.LogDebug("Found configuration type '{ConfigType}' for plugin '{PluginType}'", 
                        configType.Name, definition.Type);
                    
                    return await CreateDynamicConfigurationAsync(definition, configType);
                }
            }

            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to discover configuration type for plugin '{PluginType}'", definition.Type);
            return null;
        }
    }

    /// <summary>
    /// Creates configuration dynamically using reflection.
    /// </summary>
    private async Task<FlowEngine.Abstractions.Plugins.IPluginConfiguration> CreateDynamicConfigurationAsync(IPluginDefinition definition, Type configType)
    {
        var config = definition.Configuration ?? new Dictionary<string, object>();
        var configInstance = Activator.CreateInstance(configType);
        
        // Map configuration properties using reflection
        foreach (var property in configType.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (property.CanWrite && config.TryGetValue(property.Name, out var value))
            {
                try
                {
                    var convertedValue = ConvertValue(value, property.PropertyType);
                    property.SetValue(configInstance, convertedValue);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to set property '{PropertyName}' on configuration type '{ConfigType}'", 
                        property.Name, configType.Name);
                }
            }
        }

        return new DynamicPluginConfiguration(definition, configInstance);
    }

    /// <summary>
    /// Extracts and converts configuration value with type safety.
    /// </summary>
    private T? ExtractConfigValue<T>(IReadOnlyDictionary<string, object> config, string key, T? defaultValue = default)
    {
        if (!config.TryGetValue(key, out var value))
            return defaultValue;

        try
        {
            return (T?)ConvertValue(value, typeof(T));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to convert configuration value '{Key}' to type '{Type}', using default", 
                key, typeof(T).Name);
            return defaultValue;
        }
    }

    /// <summary>
    /// Converts value to target type with comprehensive type handling.
    /// </summary>
    private object? ConvertValue(object? value, Type targetType)
    {
        if (value == null)
            return null;

        if (targetType.IsAssignableFrom(value.GetType()))
            return value;

        // Handle nullable types
        if (targetType.IsGenericType && targetType.GetGenericTypeDefinition() == typeof(Nullable<>))
        {
            var underlyingType = Nullable.GetUnderlyingType(targetType);
            if (underlyingType != null)
                return ConvertValue(value, underlyingType);
        }

        // Handle string conversion
        if (targetType == typeof(string))
            return value.ToString();

        // Handle enum conversion
        if (targetType.IsEnum)
        {
            if (value is string stringValue)
                return Enum.Parse(targetType, stringValue, true);
            return Enum.ToObject(targetType, value);
        }

        // Handle JSON deserialization for complex types
        if (value is string jsonString && !targetType.IsPrimitive && targetType != typeof(string))
        {
            try
            {
                return JsonSerializer.Deserialize(jsonString, targetType, _jsonOptions);
            }
            catch
            {
                // Fall back to regular conversion
            }
        }

        // Use Convert.ChangeType for primitive types
        return Convert.ChangeType(value, targetType);
    }

    /// <summary>
    /// Sets an init-only property using reflection.
    /// </summary>
    /// <param name="instance">The object instance</param>
    /// <param name="propertyName">The property name</param>
    /// <param name="value">The value to set</param>
    private static void SetInitProperty(object instance, string propertyName, object value)
    {
        var property = instance.GetType().GetProperty(propertyName);
        if (property == null)
            throw new PluginLoadException($"Property '{propertyName}' not found on configuration type");

        if (!property.CanWrite && property.SetMethod?.IsPublic != true)
        {
            // For init-only properties, we need to use the backing field or reflection tricks
            var backingField = instance.GetType().GetField($"<{propertyName}>k__BackingField", 
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            
            if (backingField != null)
            {
                backingField.SetValue(instance, value);
                return;
            }
            
            // Try alternative approach - use the init setter via reflection
            var setMethod = property.GetSetMethod(true); // Get non-public setter
            if (setMethod != null)
            {
                setMethod.Invoke(instance, new[] { value });
                return;
            }
            
            throw new PluginLoadException($"Unable to set init-only property '{propertyName}' on configuration type");
        }
        else
        {
            property.SetValue(instance, value);
        }
    }
    
    /// <summary>
    /// Parses schema configuration from YAML into an ISchema object using the schema factory.
    /// </summary>
    private ISchema ParseSchemaFromConfig(object schemaConfig)
    {
        _logger.LogDebug("ParseSchemaFromConfig called with object type: {Type}", schemaConfig?.GetType().Name ?? "null");
        
        // Handle different dictionary types from YAML parsing
        Dictionary<string, object> schemaDict;
        switch (schemaConfig)
        {
            case Dictionary<string, object> stringKeyDict:
                schemaDict = stringKeyDict;
                break;
            case Dictionary<object, object> objectKeyDict:
                // Convert Dictionary<object, object> to Dictionary<string, object>
                schemaDict = objectKeyDict.ToDictionary(
                    kvp => kvp.Key?.ToString() ?? string.Empty,
                    kvp => kvp.Value);
                break;
            case IDictionary dict:
                // Convert any IDictionary to Dictionary<string, object>
                schemaDict = new Dictionary<string, object>();
                foreach (DictionaryEntry entry in dict)
                {
                    schemaDict[entry.Key?.ToString() ?? string.Empty] = entry.Value ?? new object();
                }
                break;
            default:
                _logger.LogError("OutputSchema is not a dictionary. Actual type: {Type}, Value: {Value}", 
                    schemaConfig?.GetType().Name ?? "null", schemaConfig?.ToString() ?? "null");
                throw new PluginLoadException($"OutputSchema must be a dictionary, got: {schemaConfig?.GetType().Name ?? "null"}");
        }
        
        _logger.LogDebug("Schema dictionary has {Count} keys: {Keys}", 
            schemaDict.Count, string.Join(", ", schemaDict.Keys));
        
        // Parse columns
        var columns = new List<ColumnDefinition>();
        if (schemaDict.TryGetValue("Columns", out var columnsObj))
        {
            _logger.LogDebug("Columns object type: {Type}", columnsObj?.GetType().Name ?? "null");
            
            // Handle different list types from YAML parsing
            List<object> columnsList;
            switch (columnsObj)
            {
                case List<object> objList:
                    columnsList = objList;
                    break;
                case IList list:
                    columnsList = new List<object>();
                    foreach (var item in list)
                    {
                        columnsList.Add(item);
                    }
                    break;
                default:
                    _logger.LogError("Columns is not a list. Type: {Type}", columnsObj?.GetType().Name ?? "null");
                    throw new PluginLoadException($"Columns must be a list, got: {columnsObj?.GetType().Name ?? "null"}");
            }
            
            _logger.LogDebug("Processing {Count} columns", columnsList.Count);
            
            foreach (var columnObj in columnsList)
            {
                _logger.LogDebug("Column object type: {Type}", columnObj?.GetType().Name ?? "null");
                
                // Convert column object to dictionary
                Dictionary<string, object> columnDict;
                switch (columnObj)
                {
                    case Dictionary<string, object> stringKeyDict:
                        columnDict = stringKeyDict;
                        break;
                    case Dictionary<object, object> objectKeyDict:
                        columnDict = objectKeyDict.ToDictionary(
                            kvp => kvp.Key?.ToString() ?? string.Empty,
                            kvp => kvp.Value);
                        break;
                    case IDictionary dict:
                        columnDict = new Dictionary<string, object>();
                        foreach (DictionaryEntry entry in dict)
                        {
                            columnDict[entry.Key?.ToString() ?? string.Empty] = entry.Value ?? new object();
                        }
                        break;
                    default:
                        _logger.LogError("Column is not a dictionary. Type: {Type}", columnObj?.GetType().Name ?? "null");
                        continue; // Skip this column
                }
                
                var columnName = ExtractConfigValue<string>(columnDict, "Name") ?? 
                    throw new PluginLoadException("Column Name is required");
                var columnType = ExtractConfigValue<string>(columnDict, "Type") ?? 
                    throw new PluginLoadException("Column Type is required");
                var index = ExtractConfigValue<int>(columnDict, "Index", 0);
                var isNullable = ExtractConfigValue<bool>(columnDict, "IsNullable", false);
                
                _logger.LogDebug("Processing column: {Name}, Type: {Type}, Index: {Index}", 
                    columnName, columnType, index);
                
                // Convert string type to .NET Type
                var dataType = columnType.ToLowerInvariant() switch
                {
                    "string" => typeof(string),
                    "integer" => typeof(int),
                    "decimal" => typeof(decimal),
                    "boolean" => typeof(bool),
                    "datetime" => typeof(DateTime),
                    _ => throw new PluginLoadException($"Unsupported column type: {columnType}")
                };
                
                columns.Add(new ColumnDefinition
                {
                    Name = columnName,
                    DataType = dataType,
                    Index = index,
                    IsNullable = isNullable
                });
            }
        }
        else
        {
            _logger.LogWarning("No Columns found in schema dictionary");
        }
        
        _logger.LogInformation("Created schema with {Count} columns", columns.Count);
        
        // Use the schema factory to create the schema
        return _schemaFactory.CreateSchema(columns.ToArray());
    }
    
    /// <summary>
    /// Converts .NET Type to string representation for JavaScriptTransform plugin.
    /// </summary>
    private static string ConvertTypeToString(Type dataType)
    {
        return dataType.Name.ToLowerInvariant() switch
        {
            "string" => "string",
            "int32" => "integer",
            "decimal" => "decimal", 
            "boolean" => "boolean",
            "datetime" => "datetime",
            _ => "string" // Default fallback
        };
    }
}