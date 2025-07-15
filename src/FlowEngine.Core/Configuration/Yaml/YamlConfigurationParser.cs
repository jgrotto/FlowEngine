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
        try
        {
            _logger.LogDebug("Starting YAML configuration parsing for file: {FilePath}", filePath);

            if (!File.Exists(filePath))
            {
                throw new FileNotFoundException($"Configuration file not found: {filePath}");
            }

            var yamlContent = await File.ReadAllTextAsync(filePath);
            return ParseYamlContent(yamlContent, filePath);
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