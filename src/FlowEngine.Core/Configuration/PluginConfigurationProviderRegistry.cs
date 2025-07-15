using FlowEngine.Abstractions.Configuration;
using FlowEngine.Abstractions.Plugins;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using ValidationResult = FlowEngine.Abstractions.ValidationResult;

namespace FlowEngine.Core.Configuration;

/// <summary>
/// Discovers and manages plugin configuration providers with comprehensive schema validation.
/// Enables plugin self-registration and provides validation services for configuration creation.
/// </summary>
public class PluginConfigurationProviderRegistry
{
    private readonly Dictionary<string, IPluginConfigurationProvider> _providers = new();
    private readonly Dictionary<string, PluginMetadata> _metadata = new();
    private readonly ILogger<PluginConfigurationProviderRegistry> _logger;
    private readonly JsonSchemaValidator _schemaValidator;
    private readonly object _registryLock = new();
    private bool _isDiscoveryComplete = false;

    /// <summary>
    /// Initializes a new instance of the PluginConfigurationProviderRegistry.
    /// </summary>
    /// <param name="logger">Logger for diagnostic information</param>
    /// <param name="schemaValidator">JSON schema validator for configuration validation</param>
    public PluginConfigurationProviderRegistry(
        ILogger<PluginConfigurationProviderRegistry> logger,
        JsonSchemaValidator schemaValidator)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _schemaValidator = schemaValidator ?? throw new ArgumentNullException(nameof(schemaValidator));
    }

    /// <summary>
    /// Registers a configuration provider manually.
    /// Used for explicit provider registration or testing.
    /// </summary>
    /// <param name="provider">The provider to register</param>
    /// <exception cref="ArgumentNullException">Thrown when provider is null</exception>
    /// <exception cref="InvalidOperationException">Thrown when provider for plugin type already exists</exception>
    public void RegisterProvider(IPluginConfigurationProvider provider)
    {
        if (provider == null)
            throw new ArgumentNullException(nameof(provider));

        lock (_registryLock)
        {
            if (_providers.ContainsKey(provider.PluginTypeName))
            {
                throw new InvalidOperationException(
                    $"Provider for plugin type '{provider.PluginTypeName}' is already registered");
            }

            // Validate the provider's schema before registration
            var schemaValidation = _schemaValidator.ValidateSchemaAsync(provider.GetConfigurationJsonSchema()).Result;
            if (!schemaValidation.IsValid)
            {
                throw new InvalidOperationException(
                    $"Provider for plugin type '{provider.PluginTypeName}' has invalid JSON schema: {string.Join(", ", schemaValidation.Errors)}");
            }

            _providers[provider.PluginTypeName] = provider;
            _metadata[provider.PluginTypeName] = provider.GetPluginMetadata();

            _logger.LogInformation("Registered configuration provider for plugin type: {PluginType}", 
                provider.PluginTypeName);
        }
    }

    /// <summary>
    /// Gets provider for specific plugin type.
    /// Returns null if no provider is registered for the type.
    /// </summary>
    /// <param name="pluginTypeName">The plugin type name to get provider for</param>
    /// <returns>The provider for the plugin type, or null if not found</returns>
    public IPluginConfigurationProvider? GetProvider(string pluginTypeName)
    {
        if (string.IsNullOrEmpty(pluginTypeName))
            return null;

        lock (_registryLock)
        {
            return _providers.GetValueOrDefault(pluginTypeName);
        }
    }

    /// <summary>
    /// Discovers providers from assemblies using attribute scanning.
    /// Scans for types with PluginConfigurationProviderAttribute and registers them.
    /// </summary>
    /// <param name="assemblies">Assemblies to scan for providers</param>
    /// <returns>Number of providers discovered and registered</returns>
    public async Task<int> DiscoverProvidersAsync(IEnumerable<Assembly> assemblies)
    {
        var discoveredCount = 0;
        var providersToRegister = new List<IPluginConfigurationProvider>();

        _logger.LogInformation("Starting plugin configuration provider discovery across {AssemblyCount} assemblies", 
            assemblies.Count());

        // Discover providers from all assemblies first
        foreach (var assembly in assemblies)
        {
            try
            {
                var assemblyProviders = await DiscoverProvidersFromAssemblyAsync(assembly);
                providersToRegister.AddRange(assemblyProviders);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to discover providers from assembly: {AssemblyName}", 
                    assembly.FullName);
            }
        }

        // Register all discovered providers
        foreach (var provider in providersToRegister)
        {
            try
            {
                RegisterProvider(provider);
                discoveredCount++;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to register discovered provider: {ProviderType}", 
                    provider.GetType().FullName);
            }
        }

        lock (_registryLock)
        {
            _isDiscoveryComplete = true;
        }

        _logger.LogInformation("Plugin configuration provider discovery completed. " +
            "Discovered: {DiscoveredCount}, Total registered: {TotalCount}", 
            discoveredCount, _providers.Count);

        return discoveredCount;
    }

    /// <summary>
    /// Validates configuration against plugin's JSON schema.
    /// Provides early validation before plugin configuration creation.
    /// </summary>
    /// <param name="pluginTypeName">The plugin type name</param>
    /// <param name="definition">The plugin definition to validate</param>
    /// <returns>Validation result with success status and error messages</returns>
    public async Task<ValidationResult> ValidatePluginConfigurationAsync(string pluginTypeName, IPluginDefinition definition)
    {
        var provider = GetProvider(pluginTypeName);
        if (provider == null)
        {
            return ValidationResult.Failure($"No configuration provider found for plugin type: {pluginTypeName}");
        }

        try
        {
            // First validate using provider's custom validation logic
            var providerValidation = provider.ValidateConfiguration(definition);
            if (!providerValidation.IsValid)
            {
                // Convert from Plugins.ValidationResult to simple ValidationResult
                var errorMessages = providerValidation.Errors.Select(e => e.Message).ToArray();
                return ValidationResult.Failure(errorMessages);
            }

            // Then validate against JSON schema
            var configData = definition.Configuration?.ToDictionary(kvp => kvp.Key, kvp => kvp.Value) ?? new Dictionary<string, object>();
            var schemaValidation = await _schemaValidator.ValidateAsync(
                configData, 
                provider.GetConfigurationJsonSchema(), 
                pluginTypeName);

            return schemaValidation;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Configuration validation failed for plugin type: {PluginType}", pluginTypeName);
            return ValidationResult.Failure($"Validation error: {ex.Message}");
        }
    }

    /// <summary>
    /// Gets all registered providers with their metadata.
    /// Used for plugin discovery and management UIs.
    /// </summary>
    /// <returns>Collection of providers with their metadata</returns>
    public IEnumerable<(IPluginConfigurationProvider Provider, PluginMetadata Metadata)> GetAllProviders()
    {
        lock (_registryLock)
        {
            return _providers.Values
                .Select(provider => (provider, _metadata[provider.PluginTypeName]))
                .ToList();
        }
    }

    /// <summary>
    /// Gets providers filtered by category.
    /// </summary>
    /// <param name="category">The plugin category to filter by</param>
    /// <returns>Providers matching the specified category</returns>
    public IEnumerable<(IPluginConfigurationProvider Provider, PluginMetadata Metadata)> GetProvidersByCategory(PluginCategory category)
    {
        return GetAllProviders().Where(p => p.Metadata.Category == category);
    }

    /// <summary>
    /// Searches providers by tag.
    /// </summary>
    /// <param name="tag">The tag to search for</param>
    /// <returns>Providers that have the specified tag</returns>
    public IEnumerable<(IPluginConfigurationProvider Provider, PluginMetadata Metadata)> SearchProvidersByTag(string tag)
    {
        if (string.IsNullOrEmpty(tag))
            return [];

        return GetAllProviders().Where(p => 
            p.Metadata.Tags.Any(t => t.Contains(tag, StringComparison.OrdinalIgnoreCase)));
    }

    /// <summary>
    /// Gets registry statistics for monitoring and diagnostics.
    /// </summary>
    /// <returns>Registry statistics including provider counts and discovery status</returns>
    public RegistryStatistics GetStatistics()
    {
        lock (_registryLock)
        {
            var providersByCategory = _metadata.Values
                .GroupBy(m => m.Category)
                .ToDictionary(g => g.Key, g => g.Count());

            return new RegistryStatistics
            {
                TotalProviders = _providers.Count,
                IsDiscoveryComplete = _isDiscoveryComplete,
                ProvidersByCategory = providersByCategory
            };
        }
    }

    /// <summary>
    /// Discovers providers from a single assembly.
    /// </summary>
    private async Task<IEnumerable<IPluginConfigurationProvider>> DiscoverProvidersFromAssemblyAsync(Assembly assembly)
    {
        var providers = new List<IPluginConfigurationProvider>();

        try
        {
            var providerTypes = assembly.GetTypes()
                .Where(type => 
                    !type.IsAbstract && 
                    !type.IsInterface &&
                    typeof(IPluginConfigurationProvider).IsAssignableFrom(type) &&
                    type.GetCustomAttribute<PluginConfigurationProviderAttribute>() != null)
                .ToList();

            foreach (var providerType in providerTypes)
            {
                try
                {
                    if (Activator.CreateInstance(providerType) is IPluginConfigurationProvider provider)
                    {
                        providers.Add(provider);
                        _logger.LogDebug("Discovered provider: {ProviderType} for plugin: {PluginType}", 
                            providerType.Name, provider.PluginTypeName);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to instantiate provider type: {ProviderType}", providerType.FullName);
                }
            }
        }
        catch (ReflectionTypeLoadException ex)
        {
            _logger.LogWarning("Failed to load types from assembly {AssemblyName}: {ErrorCount} errors", 
                assembly.FullName, ex.LoaderExceptions.Length);
        }

        return providers;
    }
}

/// <summary>
/// Attribute to mark classes as plugin configuration providers for auto-discovery.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
public class PluginConfigurationProviderAttribute : Attribute
{
    /// <summary>
    /// Optional priority for provider registration (higher values register first).
    /// </summary>
    public int Priority { get; set; } = 0;
}

/// <summary>
/// Statistics about the provider registry for monitoring and diagnostics.
/// </summary>
public class RegistryStatistics
{
    /// <summary>
    /// Total number of registered providers.
    /// </summary>
    public int TotalProviders { get; init; }

    /// <summary>
    /// Whether provider discovery has completed.
    /// </summary>
    public bool IsDiscoveryComplete { get; init; }

    /// <summary>
    /// Count of providers by category.
    /// </summary>
    public Dictionary<PluginCategory, int> ProvidersByCategory { get; init; } = new();
}