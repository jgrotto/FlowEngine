using FlowEngine.Abstractions;
using FlowEngine.Abstractions.Configuration;
using FlowEngine.Abstractions.Plugins;
using FlowEngine.Core.Configuration.Yaml;
using Microsoft.Extensions.Logging;

namespace FlowEngine.Core.Configuration;

/// <summary>
/// Implementation of pipeline configuration loaded from YAML.
/// Provides validation and type-safe access to pipeline definition data.
/// </summary>
public sealed class PipelineConfiguration : IPipelineConfiguration
{
    private readonly PipelineConfigurationData _data;

    /// <summary>
    /// Initializes a new pipeline configuration from parsed YAML data.
    /// </summary>
    /// <param name="data">Parsed configuration data</param>
    internal PipelineConfiguration(PipelineConfigurationData data)
    {
        _data = data ?? throw new ArgumentNullException(nameof(data));

        // Initialize plugins without type resolution (will be resolved later)
        Plugins = _data.Pipeline?.Plugins?.Select(p => new PluginConfiguration(p)).ToArray() ?? Array.Empty<IPluginDefinition>();
    }

    /// <summary>
    /// Initializes plugin type resolution using the provided resolver.
    /// </summary>
    /// <param name="typeResolver">Plugin type resolver service</param>
    internal async Task InitializePluginTypesAsync(IPluginTypeResolver typeResolver)
    {
        if (_data.Pipeline?.Plugins == null || _data.Pipeline.Plugins.Count == 0)
        {
            return;
        }

        var resolvedPlugins = new List<IPluginDefinition>();

        foreach (var pluginData in _data.Pipeline.Plugins)
        {
            PluginTypeResolution? resolution = null;

            // Try to resolve the type if it looks like a short name
            if (!string.IsNullOrEmpty(pluginData.Type) && !pluginData.Type.Contains('.'))
            {
                resolution = await typeResolver.ResolveTypeAsync(pluginData.Type);
            }

            resolvedPlugins.Add(new PluginConfiguration(pluginData, resolution));
        }

        Plugins = resolvedPlugins.AsReadOnly();
    }

    /// <inheritdoc />
    public string Name => _data.Pipeline?.Name ?? throw new InvalidOperationException("Pipeline name is required");

    /// <inheritdoc />
    public string Version => _data.Pipeline?.Version ?? "1.0.0";

    /// <inheritdoc />
    public string? Description => _data.Pipeline?.Description;

    /// <inheritdoc />
    public IReadOnlyList<IPluginDefinition> Plugins { get; private set; } = Array.Empty<IPluginDefinition>();

    /// <inheritdoc />
    public IReadOnlyList<IConnectionConfiguration> Connections =>
        _data.Pipeline?.Connections?.Select(c => new ConnectionConfiguration(c)).ToArray() ?? Array.Empty<IConnectionConfiguration>();

    /// <inheritdoc />
    public IPipelineSettings Settings => new PipelineSettings(_data.Pipeline?.Settings);

    /// <inheritdoc />
    public IReadOnlyDictionary<string, object>? Metadata => _data.Pipeline?.Metadata;

    /// <inheritdoc />
    public ConfigurationValidationResult Validate()
    {
        var errors = new List<string>();
        var warnings = new List<string>();

        // Validate required fields
        if (string.IsNullOrWhiteSpace(Name))
        {
            errors.Add("Pipeline name is required");
        }

        if (Plugins.Count == 0)
        {
            errors.Add("Pipeline must contain at least one plugin");
        }

        // Validate plugin names are unique
        var pluginNames = Plugins.Select(p => p.Name).ToList();
        var duplicateNames = pluginNames.GroupBy(n => n).Where(g => g.Count() > 1).Select(g => g.Key);
        foreach (var name in duplicateNames)
        {
            errors.Add($"Duplicate plugin name: {name}");
        }

        // Validate connections reference existing plugins
        var pluginNameSet = new HashSet<string>(pluginNames);
        foreach (var connection in Connections)
        {
            if (!pluginNameSet.Contains(connection.From))
            {
                errors.Add($"Connection references unknown source plugin: {connection.From}");
            }

            if (!pluginNameSet.Contains(connection.To))
            {
                errors.Add($"Connection references unknown destination plugin: {connection.To}");
            }
        }

        // Validate schema configurations
        foreach (var plugin in Plugins.Cast<PluginConfiguration>())
        {
            if (plugin.Schema is SchemaConfiguration schemaConfig)
            {
                var schemaValidation = schemaConfig.Validate();
                errors.AddRange(schemaValidation.Errors);
                warnings.AddRange(schemaValidation.Warnings);
            }
        }

        return errors.Count > 0
            ? ConfigurationValidationResult.Failure(errors.ToArray())
            : warnings.Count > 0
                ? ConfigurationValidationResult.SuccessWithWarnings(warnings.ToArray())
                : ConfigurationValidationResult.Success();
    }

    /// <summary>
    /// Loads a pipeline configuration from a YAML file.
    /// </summary>
    /// <param name="filePath">Path to the YAML configuration file</param>
    /// <param name="logger">Logger for diagnostic information</param>
    /// <returns>Loaded pipeline configuration</returns>
    /// <exception cref="ConfigurationException">Thrown when loading or parsing fails</exception>
    public static async Task<IPipelineConfiguration> LoadFromFileAsync(string filePath, ILogger<YamlConfigurationParser> logger)
    {
        return await LoadFromFileAsync(filePath, null, logger);
    }

    /// <summary>
    /// Loads a pipeline configuration from a YAML file with automatic plugin type resolution.
    /// </summary>
    /// <param name="filePath">Path to the YAML configuration file</param>
    /// <param name="typeResolver">Optional plugin type resolver for short name resolution</param>
    /// <param name="logger">Logger for diagnostic information</param>
    /// <returns>Loaded pipeline configuration</returns>
    /// <exception cref="ConfigurationException">Thrown when loading or parsing fails</exception>
    public static async Task<IPipelineConfiguration> LoadFromFileAsync(string filePath, IPluginTypeResolver? typeResolver, ILogger<YamlConfigurationParser> logger)
    {
        try
        {
            var parser = new YamlConfigurationParser(logger);
            var data = await parser.ParseFileAsync(filePath, null); // Use the overload that supports global variables
            var config = new PipelineConfiguration(data);

            // Resolve plugin types if resolver is provided
            if (typeResolver != null)
            {
                await config.InitializePluginTypesAsync(typeResolver);
            }

            // Validate the configuration
            var validation = config.Validate();
            if (!validation.IsValid)
            {
                throw new ConfigurationException(
                    $"Configuration validation failed: {string.Join(", ", validation.Errors)}");
            }

            return config;
        }
        catch (YamlConfigurationException ex)
        {
            throw new ConfigurationException($"Failed to parse YAML configuration: {ex.Message}", ex);
        }
        catch (Exception ex) when (!(ex is ConfigurationException))
        {
            throw new ConfigurationException($"Failed to load configuration from file: {filePath}", ex);
        }
    }

    /// <summary>
    /// Loads a pipeline configuration from YAML content.
    /// </summary>
    /// <param name="yamlContent">YAML configuration content</param>
    /// <param name="logger">Logger for diagnostic information</param>
    /// <returns>Loaded pipeline configuration</returns>
    /// <exception cref="ConfigurationException">Thrown when parsing fails</exception>
    public static IPipelineConfiguration LoadFromYaml(string yamlContent, ILogger<YamlConfigurationParser> logger)
    {
        return LoadFromYaml(yamlContent, null, logger);
    }

    /// <summary>
    /// Loads a pipeline configuration from YAML content with automatic plugin type resolution.
    /// </summary>
    /// <param name="yamlContent">YAML configuration content</param>
    /// <param name="typeResolver">Optional plugin type resolver for short name resolution</param>
    /// <param name="logger">Logger for diagnostic information</param>
    /// <returns>Loaded pipeline configuration</returns>
    /// <exception cref="ConfigurationException">Thrown when parsing fails</exception>
    public static IPipelineConfiguration LoadFromYaml(string yamlContent, IPluginTypeResolver? typeResolver, ILogger<YamlConfigurationParser> logger)
    {
        try
        {
            var parser = new YamlConfigurationParser(logger);
            var data = parser.ParseYamlContent(yamlContent, "", null); // Use the overload that supports global variables
            var config = new PipelineConfiguration(data);

            // Resolve plugin types if resolver is provided
            if (typeResolver != null)
            {
                var task = config.InitializePluginTypesAsync(typeResolver);
                task.Wait(); // Synchronous wait for now, could be made async in the future
            }

            // Validate the configuration
            var validation = config.Validate();
            if (!validation.IsValid)
            {
                throw new ConfigurationException(
                    $"Configuration validation failed: {string.Join(", ", validation.Errors)}");
            }

            return config;
        }
        catch (YamlConfigurationException ex)
        {
            throw new ConfigurationException($"Failed to parse YAML configuration: {ex.Message}", ex);
        }
        catch (Exception ex) when (!(ex is ConfigurationException))
        {
            throw new ConfigurationException("Failed to parse YAML configuration", ex);
        }
    }
}
