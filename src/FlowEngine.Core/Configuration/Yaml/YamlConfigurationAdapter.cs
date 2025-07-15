using FlowEngine.Core.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;

namespace FlowEngine.Core.Configuration.Yaml;

/// <summary>
/// Adapter class that converts strongly-typed YAML configuration to existing PipelineConfigurationData structure.
/// This eliminates the need for complex normalization logic while maintaining compatibility.
/// </summary>
public class YamlConfigurationAdapter
{
    private readonly ILogger<YamlConfigurationAdapter> _logger;

    /// <summary>
    /// Initializes a new instance of the YamlConfigurationAdapter.
    /// </summary>
    /// <param name="logger">Logger for diagnostic information</param>
    public YamlConfigurationAdapter(ILogger<YamlConfigurationAdapter> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Converts a strongly-typed YAML configuration to PipelineConfigurationData.
    /// </summary>
    /// <param name="yamlConfig">Strongly-typed YAML configuration</param>
    /// <returns>Converted PipelineConfigurationData</returns>
    internal PipelineConfigurationData ConvertToPipelineConfigurationData(YamlPipelineConfiguration yamlConfig)
    {
        try
        {
            _logger.LogDebug("Converting YAML configuration to PipelineConfigurationData");

            return new PipelineConfigurationData
            {
                Pipeline = ConvertPipelineDefinition(yamlConfig.Pipeline)
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to convert YAML configuration to PipelineConfigurationData");
            throw new YamlConfigurationException($"Failed to convert YAML configuration: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Converts a YAML pipeline definition to PipelineData.
    /// </summary>
    /// <param name="yamlPipeline">YAML pipeline definition</param>
    /// <returns>Converted PipelineData</returns>
    private PipelineData ConvertPipelineDefinition(YamlPipelineDefinition yamlPipeline)
    {
        return new PipelineData
        {
            Name = yamlPipeline.Name,
            Version = yamlPipeline.Version,
            Description = yamlPipeline.Description,
            Plugins = yamlPipeline.Plugins?.Select(ConvertPluginDefinition).ToList() ?? new List<PluginData>(),
            Connections = yamlPipeline.Connections?.Select(ConvertConnectionDefinition).ToList() ?? new List<ConnectionData>(),
            Settings = yamlPipeline.Settings != null ? ConvertPipelineSettings(yamlPipeline.Settings) : null,
            Metadata = yamlPipeline.Metadata ?? new Dictionary<string, object>()
        };
    }

    /// <summary>
    /// Converts a YAML plugin definition to PluginData.
    /// </summary>
    /// <param name="yamlPlugin">YAML plugin definition</param>
    /// <returns>Converted PluginData</returns>
    private PluginData ConvertPluginDefinition(YamlPluginDefinition yamlPlugin)
    {
        return new PluginData
        {
            Name = yamlPlugin.Name,
            Type = yamlPlugin.Type,
            Assembly = yamlPlugin.Assembly,
            Config = ConvertPluginConfig(yamlPlugin.Config, yamlPlugin.Type),
            Schema = null, // Schema would be set separately if needed
            ResourceLimits = null // ResourceLimits would be set separately if needed
        };
    }

    /// <summary>
    /// Converts plugin configuration based on plugin type.
    /// This ensures proper type conversion and validation.
    /// </summary>
    /// <param name="config">Plugin configuration dictionary</param>
    /// <param name="pluginType">Plugin type for specialized conversion</param>
    /// <returns>Converted configuration dictionary</returns>
    private Dictionary<string, object> ConvertPluginConfig(Dictionary<string, object> config, string pluginType)
    {
        var convertedConfig = new Dictionary<string, object>();

        foreach (var kvp in config)
        {
            var key = kvp.Key; // Keep original PascalCase naming - no conversion
            var value = ConvertConfigValue(kvp.Value, key, pluginType);
            convertedConfig[key] = value;
        }

        return convertedConfig;
    }

    /// <summary>
    /// Converts a configuration value based on its type and context.
    /// </summary>
    /// <param name="value">Configuration value to convert</param>
    /// <param name="key">Configuration key for context</param>
    /// <param name="pluginType">Plugin type for specialized conversion</param>
    /// <returns>Converted configuration value</returns>
    private object ConvertConfigValue(object value, string key, string pluginType)
    {
        // YamlDotNet creates Dictionary<object, object> which we need to convert to Dictionary<string, object>
        
        return value switch
        {
            // Handle nested dictionaries (like OutputSchema) - ensure exact Dictionary<string, object> type
            Dictionary<string, object> dict => ConvertNestedDictionary(dict, key, pluginType),
            Dictionary<object, object> objDict => ConvertObjectDictionary(objDict, key, pluginType),
            IDictionary<string, object> dict => new Dictionary<string, object>(ConvertNestedDictionary(dict.ToDictionary(kv => kv.Key, kv => kv.Value), key, pluginType)),
            IDictionary<object, object> objDict => ConvertObjectDictionary(objDict.ToDictionary(kv => kv.Key, kv => kv.Value), key, pluginType),
            
            // Handle lists (like Columns in OutputSchema) - ensure exact List<object> type
            List<object> list => ConvertList(list, key, pluginType),
            IList<object> list => ConvertList(list.ToList(), key, pluginType),
            
            // Handle primitive values
            string strValue => ConvertStringValue(strValue),
            
            // Return as-is for other types
            _ => value
        };
    }

    /// <summary>
    /// Converts a nested dictionary based on its context.
    /// </summary>
    /// <param name="dict">Dictionary to convert</param>
    /// <param name="key">Parent key for context</param>
    /// <param name="pluginType">Plugin type for specialized conversion</param>
    /// <returns>Converted dictionary</returns>
    private Dictionary<string, object> ConvertNestedDictionary(Dictionary<string, object> dict, string key, string pluginType)
    {
        var converted = new Dictionary<string, object>();

        foreach (var kvp in dict)
        {
            var nestedKey = kvp.Key; // Keep original PascalCase naming
            var nestedValue = ConvertConfigValue(kvp.Value, nestedKey, pluginType);
            converted[nestedKey] = nestedValue ?? throw new InvalidOperationException($"Null value not allowed for key {nestedKey}");
        }

        return converted;
    }

    /// <summary>
    /// Converts a Dictionary<object, object> to Dictionary<string, object> for plugin compatibility.
    /// </summary>
    /// <param name="dict">Dictionary to convert</param>
    /// <param name="key">Parent key for context</param>
    /// <param name="pluginType">Plugin type for specialized conversion</param>
    /// <returns>Converted dictionary</returns>
    private Dictionary<string, object> ConvertObjectDictionary(Dictionary<object, object> dict, string key, string pluginType)
    {
        var converted = new Dictionary<string, object>();

        foreach (var kvp in dict)
        {
            var nestedKey = kvp.Key?.ToString() ?? throw new InvalidOperationException($"Null key not allowed in dictionary for {key}");
            var nestedValue = ConvertConfigValue(kvp.Value, nestedKey, pluginType);
            converted[nestedKey] = nestedValue ?? throw new InvalidOperationException($"Null value not allowed for key {nestedKey}");
        }

        return converted;
    }

    /// <summary>
    /// Converts a list of values based on context.
    /// </summary>
    /// <param name="list">List to convert</param>
    /// <param name="key">Parent key for context</param>
    /// <param name="pluginType">Plugin type for specialized conversion</param>
    /// <returns>Converted list</returns>
    private List<object> ConvertList(List<object> list, string key, string pluginType)
    {
        var converted = new List<object>();

        foreach (var item in list)
        {
            var convertedItem = ConvertConfigValue(item, key, pluginType);
            converted.Add(convertedItem ?? throw new InvalidOperationException($"Null value not allowed in list for key {key}"));
        }

        return converted;
    }

    /// <summary>
    /// Converts a YAML connection definition to ConnectionData.
    /// </summary>
    /// <param name="yamlConnection">YAML connection definition</param>
    /// <returns>Converted ConnectionData</returns>
    private ConnectionData ConvertConnectionDefinition(YamlConnectionDefinition yamlConnection)
    {
        return new ConnectionData
        {
            From = yamlConnection.From,
            To = yamlConnection.To,
            // Note: ConnectionData has FromPort/ToPort instead of Type/Config/Metadata
            FromPort = null, // These would need to be extracted from Config if needed
            ToPort = null,
            Channel = null // Would need to be configured if channel settings are provided
        };
    }

    /// <summary>
    /// Converts YAML pipeline settings to SettingsData.
    /// </summary>
    /// <param name="yamlSettings">YAML pipeline settings</param>
    /// <returns>Converted SettingsData</returns>
    private SettingsData ConvertPipelineSettings(YamlPipelineSettings yamlSettings)
    {
        return new SettingsData
        {
            // Note: SettingsData has different structure - mapping to available fields
            DefaultChannel = null, // Would need to be configured from settings
            DefaultResourceLimits = null, // Would need to be configured from settings
            Monitoring = yamlSettings.Monitoring != null ? ConvertMonitoringSettings(yamlSettings.Monitoring) : null,
            Custom = yamlSettings.Custom ?? new Dictionary<string, object>()
        };
    }

    // Note: RetryData doesn't exist in the current codebase - removing this method for now
    // Would need to be implemented if retry functionality is added

    /// <summary>
    /// Converts YAML monitoring settings to MonitoringData.
    /// </summary>
    /// <param name="yamlMonitoring">YAML monitoring settings</param>
    /// <returns>Converted MonitoringData</returns>
    private MonitoringData ConvertMonitoringSettings(YamlMonitoringConfiguration yamlMonitoring)
    {
        return new MonitoringData
        {
            // Map to actual MonitoringData properties
            EnableMetrics = yamlMonitoring.CollectPerformanceMetrics, // Map closest equivalent
            MetricsIntervalSeconds = yamlMonitoring.MetricsIntervalMs / 1000, // Convert ms to seconds
            EnableTracing = false, // Not available in YAML config
            Custom = new Dictionary<string, object>
            {
                {"CollectPerformanceMetrics", yamlMonitoring.CollectPerformanceMetrics},
                {"CollectMemoryMetrics", yamlMonitoring.CollectMemoryMetrics},
                {"CollectErrorMetrics", yamlMonitoring.CollectErrorMetrics},
                {"LogLevel", yamlMonitoring.LogLevel}
            }
        };
    }

    /// <summary>
    /// Converts a string to PascalCase.
    /// </summary>
    /// <param name="input">Input string</param>
    /// <returns>PascalCase string</returns>
    private static string ToPascalCase(string input)
    {
        if (string.IsNullOrEmpty(input))
            return input;

        // Handle already PascalCase strings
        if (char.IsUpper(input[0]))
            return input;

        // Convert camelCase to PascalCase
        return char.ToUpperInvariant(input[0]) + input.Substring(1);
    }

    /// <summary>
    /// Converts string values to appropriate types for configuration validation.
    /// Handles the YAML parsing issue where scalar values are parsed as strings.
    /// </summary>
    /// <param name="value">String value to convert</param>
    /// <returns>Converted value</returns>
    private static object ConvertStringValue(string value)
    {
        // Try boolean conversion first
        if (string.Equals(value, "true", StringComparison.OrdinalIgnoreCase))
            return true;
        if (string.Equals(value, "false", StringComparison.OrdinalIgnoreCase))
            return false;

        // Try integer conversion
        if (int.TryParse(value, out var intValue))
            return intValue;

        // Try decimal conversion
        if (decimal.TryParse(value, out var decimalValue))
            return decimalValue;

        // Try double conversion
        if (double.TryParse(value, out var doubleValue))
            return doubleValue;

        // Return as string if no conversion is possible
        return value;
    }
}