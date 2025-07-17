using FlowEngine.Core.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace FlowEngine.Core.Configuration.Yaml;

/// <summary>
/// Dedicated YAML configuration parser that handles proper deserialization with type conversion.
/// Replaces the complex normalization logic with proper parse-time type resolution.
/// </summary>
public class YamlConfigurationParser
{
    private readonly ILogger<YamlConfigurationParser> _logger;
    private readonly IDeserializer _deserializer;

    /// <summary>
    /// Initializes a new instance of the YamlConfigurationParser with proper type converters.
    /// </summary>
    /// <param name="logger">Logger for diagnostic information</param>
    public YamlConfigurationParser(ILogger<YamlConfigurationParser> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _deserializer = CreateConfiguredDeserializer();
    }

    /// <summary>
    /// Parses a YAML configuration file and returns compatible pipeline configuration data.
    /// </summary>
    /// <param name="filePath">Path to the YAML configuration file</param>
    /// <returns>Parsed pipeline configuration</returns>
    /// <exception cref="FileNotFoundException">Thrown when the configuration file is not found</exception>
    /// <exception cref="YamlConfigurationException">Thrown when YAML parsing fails</exception>
    internal async Task<PipelineConfigurationData> ParseFileAsync(string filePath)
    {
        return await ParseFileAsync(filePath, null);
    }

    /// <summary>
    /// Parses a YAML configuration file with global variables and returns compatible pipeline configuration data.
    /// </summary>
    /// <param name="filePath">Path to the YAML configuration file</param>
    /// <param name="globalVariables">Global variables for variable substitution (optional)</param>
    /// <returns>Parsed pipeline configuration</returns>
    /// <exception cref="FileNotFoundException">Thrown when the configuration file is not found</exception>
    /// <exception cref="YamlConfigurationException">Thrown when YAML parsing fails</exception>
    internal async Task<PipelineConfigurationData> ParseFileAsync(string filePath, GlobalVariables? globalVariables)
    {
        try
        {
            _logger.LogDebug("Starting YAML configuration parsing for file: {FilePath}", filePath);

            if (!File.Exists(filePath))
            {
                throw new FileNotFoundException($"Configuration file not found: {filePath}");
            }

            var yamlContent = await File.ReadAllTextAsync(filePath);
            return ParseYamlContent(yamlContent, filePath, globalVariables);
        }
        catch (Exception ex) when (!(ex is FileNotFoundException || ex is YamlConfigurationException))
        {
            _logger.LogError(ex, "Failed to parse YAML configuration file: {FilePath}", filePath);
            throw new YamlConfigurationException($"Failed to parse YAML configuration: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Parses YAML content string and returns compatible pipeline configuration data.
    /// </summary>
    /// <param name="yamlContent">YAML content as string</param>
    /// <param name="sourcePath">Source path for error reporting (optional)</param>
    /// <returns>Parsed pipeline configuration</returns>
    /// <exception cref="YamlConfigurationException">Thrown when YAML parsing fails</exception>
    internal PipelineConfigurationData ParseYamlContent(string yamlContent, string sourcePath = "")
    {
        return ParseYamlContent(yamlContent, sourcePath, null);
    }

    /// <summary>
    /// Parses YAML content string with global variables and returns compatible pipeline configuration data.
    /// </summary>
    /// <param name="yamlContent">YAML content as string</param>
    /// <param name="sourcePath">Source path for error reporting (optional)</param>
    /// <param name="globalVariables">Global variables for variable substitution (optional)</param>
    /// <returns>Parsed pipeline configuration</returns>
    /// <exception cref="YamlConfigurationException">Thrown when YAML parsing fails</exception>
    internal PipelineConfigurationData ParseYamlContent(string yamlContent, string sourcePath = "", GlobalVariables? globalVariables = null)
    {
        try
        {
            _logger.LogDebug("Parsing YAML content from source: {SourcePath}", sourcePath);

            if (string.IsNullOrWhiteSpace(yamlContent))
            {
                throw new YamlConfigurationException("YAML content cannot be null or empty");
            }

            // Parse to strongly-typed YAML configuration
            var yamlConfiguration = _deserializer.Deserialize<YamlPipelineConfiguration>(yamlContent);
            
            if (yamlConfiguration == null)
            {
                throw new YamlConfigurationException("Failed to deserialize YAML content - result is null");
            }

            // Setup global variables with configuration file path
            var variables = globalVariables ?? new GlobalVariables();
            if (!string.IsNullOrEmpty(sourcePath))
            {
                variables.SetConfigurationFilePath(sourcePath);
            }

            // Load variables from YAML if present
            if (yamlConfiguration.Variables != null)
            {
                _logger.LogInformation("Loading {Count} variables from YAML", yamlConfiguration.Variables.Count);
                foreach (var kvp in yamlConfiguration.Variables)
                {
                    if (!variables.IsReadOnly)
                    {
                        variables.SetVariable(kvp.Key, kvp.Value);
                        _logger.LogDebug("Loaded variable: {Name} = {Value}", kvp.Key, kvp.Value);
                    }
                }
            }
            else
            {
                _logger.LogWarning("No variables section found in YAML");
            }

            // Perform variable substitution on the entire configuration
            _logger.LogInformation("Starting variable substitution on configuration");
            PerformVariableSubstitution(yamlConfiguration, variables);
            _logger.LogInformation("Variable substitution completed");

            // Convert to existing PipelineConfigurationData structure
            // Note: Using null logger for now - would need ILoggerFactory for proper logging
            var adapter = new YamlConfigurationAdapter(Microsoft.Extensions.Logging.Abstractions.NullLogger<YamlConfigurationAdapter>.Instance);
            var configuration = adapter.ConvertToPipelineConfigurationData(yamlConfiguration);

            _logger.LogInformation("Successfully parsed YAML configuration from {SourcePath}", sourcePath);
            return configuration;
        }
        catch (YamlDotNet.Core.YamlException ex)
        {
            _logger.LogError(ex, "YAML parsing error in {SourcePath}: {Message}", sourcePath, ex.Message);
            throw new YamlConfigurationException($"YAML parsing error: {ex.Message}", ex);
        }
        catch (Exception ex) when (!(ex is YamlConfigurationException))
        {
            _logger.LogError(ex, "Unexpected error while parsing YAML content from {SourcePath}", sourcePath);
            throw new YamlConfigurationException($"Unexpected error during YAML parsing: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Performs variable substitution on the entire YAML configuration.
    /// </summary>
    /// <param name="configuration">YAML configuration to process</param>
    /// <param name="variables">Global variables for substitution</param>
    private static void PerformVariableSubstitution(YamlPipelineConfiguration configuration, GlobalVariables variables)
    {
        // Substitute in pipeline definition
        if (configuration.Pipeline != null)
        {
            SubstituteInPipelineDefinition(configuration.Pipeline, variables);
        }
    }

    /// <summary>
    /// Performs variable substitution in a pipeline definition.
    /// </summary>
    /// <param name="pipeline">Pipeline definition to process</param>
    /// <param name="variables">Global variables for substitution</param>
    private static void SubstituteInPipelineDefinition(YamlPipelineDefinition pipeline, GlobalVariables variables)
    {
        // Substitute in pipeline properties
        var originalName = pipeline.Name;
        pipeline.Name = variables.SubstituteVariables(pipeline.Name);
        Console.WriteLine($"[DEBUG] Pipeline name: '{originalName}' -> '{pipeline.Name}'");
        
        pipeline.Version = variables.SubstituteVariables(pipeline.Version);
        if (pipeline.Description != null)
        {
            pipeline.Description = variables.SubstituteVariables(pipeline.Description);
        }

        // Substitute in plugins
        if (pipeline.Plugins != null)
        {
            foreach (var plugin in pipeline.Plugins)
            {
                Console.WriteLine($"[DEBUG] Before plugin substitution - Plugin: {plugin.Name}");
                if (plugin.Config.ContainsKey("FilePath"))
                {
                    Console.WriteLine($"[DEBUG] FilePath before: {plugin.Config["FilePath"]}");
                }
                SubstituteInPluginDefinition(plugin, variables);
                if (plugin.Config.ContainsKey("FilePath"))
                {
                    Console.WriteLine($"[DEBUG] FilePath after: {plugin.Config["FilePath"]}");
                }
            }
        }

        // Substitute in connections
        if (pipeline.Connections != null)
        {
            foreach (var connection in pipeline.Connections)
            {
                SubstituteInConnectionDefinition(connection, variables);
            }
        }

        // Substitute in settings
        if (pipeline.Settings != null)
        {
            SubstituteInPipelineSettings(pipeline.Settings, variables);
        }

        // Substitute in metadata
        if (pipeline.Metadata != null)
        {
            SubstituteInDictionary(pipeline.Metadata, variables);
        }
    }

    /// <summary>
    /// Performs variable substitution in a plugin definition.
    /// </summary>
    /// <param name="plugin">Plugin definition to process</param>
    /// <param name="variables">Global variables for substitution</param>
    private static void SubstituteInPluginDefinition(YamlPluginDefinition plugin, GlobalVariables variables)
    {
        plugin.Name = variables.SubstituteVariables(plugin.Name);
        plugin.Type = variables.SubstituteVariables(plugin.Type);
        if (plugin.Assembly != null)
        {
            plugin.Assembly = variables.SubstituteVariables(plugin.Assembly);
        }

        // Substitute in config dictionary
        SubstituteInDictionary(plugin.Config, variables);

        // Substitute in metadata
        if (plugin.Metadata != null)
        {
            SubstituteInDictionary(plugin.Metadata, variables);
        }
    }

    /// <summary>
    /// Performs variable substitution in a connection definition.
    /// </summary>
    /// <param name="connection">Connection definition to process</param>
    /// <param name="variables">Global variables for substitution</param>
    private static void SubstituteInConnectionDefinition(YamlConnectionDefinition connection, GlobalVariables variables)
    {
        connection.From = variables.SubstituteVariables(connection.From);
        connection.To = variables.SubstituteVariables(connection.To);
        if (connection.Type != null)
        {
            connection.Type = variables.SubstituteVariables(connection.Type);
        }

        // Substitute in config dictionary
        if (connection.Config != null)
        {
            SubstituteInDictionary(connection.Config, variables);
        }

        // Substitute in metadata
        if (connection.Metadata != null)
        {
            SubstituteInDictionary(connection.Metadata, variables);
        }
    }

    /// <summary>
    /// Performs variable substitution in pipeline settings.
    /// </summary>
    /// <param name="settings">Pipeline settings to process</param>
    /// <param name="variables">Global variables for substitution</param>
    private static void SubstituteInPipelineSettings(YamlPipelineSettings settings, GlobalVariables variables)
    {
        settings.ExecutionMode = variables.SubstituteVariables(settings.ExecutionMode);

        // Note: Numeric values don't need substitution unless they're strings that parse to numbers

        if (settings.Retry != null)
        {
            settings.Retry.BackoffStrategy = variables.SubstituteVariables(settings.Retry.BackoffStrategy);
        }

        if (settings.Monitoring != null)
        {
            settings.Monitoring.LogLevel = variables.SubstituteVariables(settings.Monitoring.LogLevel);
        }

        if (settings.Custom != null)
        {
            SubstituteInDictionary(settings.Custom, variables);
        }
    }

    /// <summary>
    /// Performs variable substitution in a dictionary recursively.
    /// </summary>
    /// <param name="dictionary">Dictionary to process</param>
    /// <param name="variables">Global variables for substitution</param>
    private static void SubstituteInDictionary(Dictionary<string, object> dictionary, GlobalVariables variables)
    {
        var keysToUpdate = new List<string>();

        foreach (var kvp in dictionary.ToList())
        {
            var newValue = SubstituteInValue(kvp.Value, variables);
            dictionary[kvp.Key] = newValue;
        }
    }

    /// <summary>
    /// Performs variable substitution on a single value recursively.
    /// </summary>
    /// <param name="value">Value to process</param>
    /// <param name="variables">Global variables for substitution</param>
    /// <returns>Value with variables substituted</returns>
    private static object SubstituteInValue(object value, GlobalVariables variables)
    {
        switch (value)
        {
            case string str:
                return variables.SubstituteVariables(str);
            case Dictionary<string, object> dict:
                SubstituteInDictionary(dict, variables);
                return dict;
            case List<object> list:
                return list.Select(item => SubstituteInValue(item, variables)).ToList();
            case object[] array:
                return array.Select(item => SubstituteInValue(item, variables)).ToArray();
            default:
                return value; // Return unchanged for other types (numbers, booleans, etc.)
        }
    }

    /// <summary>
    /// Creates a standard YamlDotNet deserializer without custom type converters.
    /// Simplified approach using built-in YamlDotNet capabilities.
    /// </summary>
    private static IDeserializer CreateConfiguredDeserializer()
    {
        return new DeserializerBuilder()
            // Use PascalCase naming convention to match existing plugin configuration providers
            .WithNamingConvention(PascalCaseNamingConvention.Instance)
            
            // Ignore unknown properties to allow for flexible configuration
            .IgnoreUnmatchedProperties()
            
            // Build the deserializer
            .Build();
    }

    /// <summary>
    /// Validates that the parsed configuration has the required structure.
    /// </summary>
    /// <param name="configuration">Configuration to validate</param>
    /// <exception cref="YamlConfigurationException">Thrown when configuration is invalid</exception>
    private void ValidateConfiguration(YamlPipelineConfiguration configuration)
    {
        if (configuration.Pipeline == null)
        {
            throw new YamlConfigurationException("Configuration must contain a 'pipeline' section");
        }

        if (configuration.Pipeline.Plugins == null || configuration.Pipeline.Plugins.Count == 0)
        {
            throw new YamlConfigurationException("Pipeline must contain at least one plugin");
        }

        foreach (var plugin in configuration.Pipeline.Plugins)
        {
            if (string.IsNullOrWhiteSpace(plugin.Name))
            {
                throw new YamlConfigurationException("All plugins must have a name");
            }

            if (string.IsNullOrWhiteSpace(plugin.Type))
            {
                throw new YamlConfigurationException($"Plugin '{plugin.Name}' must have a type");
            }
        }
    }
}

/// <summary>
/// Exception thrown when YAML configuration parsing fails.
/// </summary>
public class YamlConfigurationException : Exception
{
    public YamlConfigurationException(string message) : base(message) { }
    public YamlConfigurationException(string message, Exception innerException) : base(message, innerException) { }
}