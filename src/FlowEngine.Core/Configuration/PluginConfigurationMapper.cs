using FlowEngine.Abstractions.Configuration;
using FlowEngine.Abstractions.Data;
using FlowEngine.Abstractions.Plugins;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading.Tasks;
using ValidationResult = FlowEngine.Abstractions.ValidationResult;

namespace FlowEngine.Core.Configuration;

/// <summary>
/// Enhanced plugin configuration mapper that uses self-registered providers with schema validation.
/// Replaces hard-coded plugin mappings with a flexible provider-based system.
/// </summary>
public sealed class PluginConfigurationMapperWithProviders : IPluginConfigurationMapper
{
    private readonly PluginConfigurationProviderRegistry _providerRegistry;
    private readonly ILogger<PluginConfigurationMapperWithProviders> _logger;

    /// <summary>
    /// Initializes a new instance of the plugin configuration mapper with provider support.
    /// </summary>
    /// <param name="providerRegistry">Registry of plugin configuration providers</param>
    /// <param name="logger">Logger for configuration mapping operations</param>
    public PluginConfigurationMapperWithProviders(
        PluginConfigurationProviderRegistry providerRegistry,
        ILogger<PluginConfigurationMapperWithProviders> logger)
    {
        _providerRegistry = providerRegistry ?? throw new ArgumentNullException(nameof(providerRegistry));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Creates configuration for the plugin using registered providers with validation.
    /// </summary>
    /// <param name="definition">Plugin definition with configuration data</param>
    /// <returns>Strongly-typed plugin configuration</returns>
    /// <exception cref="ConfigurationException">Thrown when configuration validation fails or provider not found</exception>
    public async Task<FlowEngine.Abstractions.Plugins.IPluginConfiguration> CreateConfigurationAsync(IPluginDefinition definition)
    {
        if (definition == null)
            throw new ArgumentNullException(nameof(definition));

        if (string.IsNullOrEmpty(definition.Type))
            throw new ArgumentException("Plugin definition must have a type", nameof(definition));

        _logger.LogDebug("Creating configuration for plugin: {PluginName} of type: {PluginType}", 
            definition.Name, definition.Type);

        try
        {
            // Step 1: Validate configuration against schema
            var validationResult = await _providerRegistry.ValidatePluginConfigurationAsync(definition.Type, definition);
            if (!validationResult.IsValid)
            {
                var errors = string.Join(", ", validationResult.Errors);
                var message = $"Plugin configuration validation failed for '{definition.Name}' ({definition.Type}): {errors}";
                _logger.LogError(message);
                throw new ConfigurationException(message);
            }

            // Step 2: Try plugin-specific provider first
            var provider = _providerRegistry.GetProvider(definition.Type);
            if (provider != null)
            {
                _logger.LogDebug("Using specific provider for plugin type: {PluginType}", definition.Type);
                var configuration = await provider.CreateConfigurationAsync(definition);
                
                _logger.LogInformation("Successfully created configuration for plugin: {PluginName} using provider: {ProviderType}", 
                    definition.Name, provider.GetType().Name);
                
                return configuration;
            }

            // Step 3: Fallback to generic configuration for unknown plugin types
            _logger.LogWarning("No specific provider found for plugin type: {PluginType}, using generic configuration", 
                definition.Type);
            
            return await CreateGenericConfigurationAsync(definition);
        }
        catch (ConfigurationException)
        {
            // Re-throw configuration exceptions as-is
            throw;
        }
        catch (Exception ex)
        {
            var message = $"Failed to create configuration for plugin '{definition.Name}' ({definition.Type}): {ex.Message}";
            _logger.LogError(ex, message);
            throw new ConfigurationException(message, ex);
        }
    }

    /// <summary>
    /// Creates a generic configuration for plugins without specific providers.
    /// This serves as a fallback for plugins that don't have registered configuration providers.
    /// </summary>
    /// <param name="definition">Plugin definition</param>
    /// <returns>Generic plugin configuration</returns>
    private async Task<FlowEngine.Abstractions.Plugins.IPluginConfiguration> CreateGenericConfigurationAsync(IPluginDefinition definition)
    {
        _logger.LogDebug("Creating generic configuration for plugin: {PluginName}", definition.Name);

        // Create a simple generic configuration that wraps the raw configuration data
        var genericConfig = new GenericPluginConfiguration
        {
            Name = definition.Name,
            PluginId = definition.Name,
            Version = "1.0.0",
            PluginType = definition.Type,
            Properties = definition.Configuration?.ToImmutableDictionary() ?? 
                        System.Collections.Immutable.ImmutableDictionary<string, object>.Empty
        };

        await Task.CompletedTask; // Keep async signature for consistency
        return genericConfig;
    }

    /// <summary>
    /// Gets provider information for diagnostic purposes.
    /// </summary>
    /// <returns>Collection of registered providers with metadata</returns>
    public IEnumerable<(string PluginType, string ProviderType, PluginMetadata Metadata)> GetRegisteredProviders()
    {
        return _providerRegistry.GetAllProviders()
            .Select(p => (p.Provider.PluginTypeName, p.Provider.GetType().Name, p.Metadata));
    }

    /// <summary>
    /// Validates a plugin definition without creating the configuration.
    /// Useful for early validation and UI feedback.
    /// </summary>
    /// <param name="definition">Plugin definition to validate</param>
    /// <returns>Validation result with success status and error messages</returns>
    public async Task<ValidationResult> ValidateDefinitionAsync(IPluginDefinition definition)
    {
        if (definition == null)
            return ValidationResult.Failure("Plugin definition cannot be null");

        if (string.IsNullOrEmpty(definition.Type))
            return ValidationResult.Failure("Plugin definition must have a type");

        return await _providerRegistry.ValidatePluginConfigurationAsync(definition.Type, definition);
    }

    /// <summary>
    /// Registers a configuration mapping for a specific plugin type.
    /// Note: This method is for backward compatibility. The new provider-based system
    /// uses auto-discovery and RegisterProvider() instead.
    /// </summary>
    /// <param name="pluginType">Full type name of the plugin</param>
    /// <param name="mapping">Configuration mapping implementation</param>
    public void RegisterMapping(string pluginType, PluginConfigurationMapping mapping)
    {
        _logger.LogWarning("RegisterMapping called with legacy API. This method is deprecated. " +
            "Use provider-based configuration instead for plugin type: {PluginType}", pluginType);
        // This method is kept for interface compatibility but does nothing in the new provider-based system
    }
}

/// <summary>
/// Generic plugin configuration for plugins without specific providers.
/// Serves as a fallback configuration type.
/// </summary>
internal class GenericPluginConfiguration : FlowEngine.Abstractions.Plugins.IPluginConfiguration
{
    public string Name { get; init; } = string.Empty;
    public string PluginId { get; init; } = string.Empty;
    public string Version { get; init; } = string.Empty;
    public string PluginType { get; init; } = string.Empty;
    public bool SupportsHotSwapping { get; init; } = false;
    public ISchema? InputSchema { get; init; }
    public ISchema? OutputSchema { get; init; }
    public System.Collections.Immutable.ImmutableDictionary<string, int> InputFieldIndexes { get; init; } = 
        System.Collections.Immutable.ImmutableDictionary<string, int>.Empty;
    public System.Collections.Immutable.ImmutableDictionary<string, int> OutputFieldIndexes { get; init; } = 
        System.Collections.Immutable.ImmutableDictionary<string, int>.Empty;
    public System.Collections.Immutable.ImmutableDictionary<string, object> Properties { get; init; } = 
        System.Collections.Immutable.ImmutableDictionary<string, object>.Empty;

    public int GetInputFieldIndex(string fieldName) => 
        InputFieldIndexes.GetValueOrDefault(fieldName, -1);

    public int GetOutputFieldIndex(string fieldName) => 
        OutputFieldIndexes.GetValueOrDefault(fieldName, -1);

    public bool IsCompatibleWith(ISchema inputSchema) => true; // Generic config accepts any schema

    public T GetProperty<T>(string key) => 
        Properties.TryGetValue(key, out var value) && value is T typed ? typed : default!;

    public bool TryGetProperty<T>(string key, out T? value)
    {
        if (Properties.TryGetValue(key, out var obj) && obj is T typed)
        {
            value = typed;
            return true;
        }
        value = default;
        return false;
    }
}

