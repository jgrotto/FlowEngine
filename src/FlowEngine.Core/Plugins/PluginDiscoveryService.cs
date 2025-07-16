using FlowEngine.Abstractions.Plugins;
using Microsoft.Extensions.Logging;
using System.Reflection;

namespace FlowEngine.Core.Plugins;

/// <summary>
/// Service for discovering plugins in the standard ./plugins/ directory.
/// Simplified implementation focused on single-directory scanning for predictable deployment.
/// </summary>
public sealed class PluginDiscoveryService : IPluginDiscoveryService
{
    private readonly ILogger<PluginDiscoveryService> _logger;
    private static readonly string DefaultPluginDirectory = Path.Combine(AppContext.BaseDirectory, "plugins");

    /// <summary>
    /// Initializes a new instance of the plugin discovery service.
    /// </summary>
    public PluginDiscoveryService(ILogger<PluginDiscoveryService> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<DiscoveredPlugin>> DiscoverPluginsAsync(CancellationToken cancellationToken = default)
    {
        return await DiscoverPluginsAsync(DefaultPluginDirectory, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<DiscoveredPlugin>> DiscoverPluginsAsync(string pluginDirectory, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(pluginDirectory))
        {
            throw new ArgumentException("Plugin directory cannot be null or empty", nameof(pluginDirectory));
        }

        if (!Directory.Exists(pluginDirectory))
        {
            _logger.LogDebug("Plugin directory does not exist: {PluginDirectory}", pluginDirectory);
            return Array.Empty<DiscoveredPlugin>();
        }

        _logger.LogInformation("Discovering plugins in directory: {PluginDirectory}", pluginDirectory);

        var plugins = new List<DiscoveredPlugin>();

        try
        {
            // Simple single-directory scan - no recursion, no complex options
            var dllFiles = Directory.GetFiles(pluginDirectory, "*.dll");
            
            _logger.LogDebug("Found {DllCount} DLL files to examine", dllFiles.Length);

            foreach (var dllFile in dllFiles)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    var plugin = await LoadPluginInfoAsync(dllFile, cancellationToken);
                    if (plugin != null)
                    {
                        plugins.Add(plugin);
                        _logger.LogDebug("Discovered plugin: {PluginName} from {AssemblyPath}", plugin.Name, plugin.AssemblyPath);
                    }
                }
                catch (Exception ex)
                {
                    // Log and continue - don't fail entire discovery for one bad plugin
                    _logger.LogWarning(ex, "Failed to load plugin from {DllFile}", dllFile);
                }
            }

            _logger.LogInformation("Plugin discovery completed. Found {PluginCount} plugins", plugins.Count);
            return plugins.AsReadOnly();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during plugin discovery in {PluginDirectory}", pluginDirectory);
            return Array.Empty<DiscoveredPlugin>();
        }
    }

    /// <summary>
    /// Loads plugin information from an assembly file.
    /// </summary>
    private async Task<DiscoveredPlugin?> LoadPluginInfoAsync(string assemblyPath, CancellationToken cancellationToken)
    {
        try
        {
            // Load assembly for inspection (not execution)
            var assembly = Assembly.LoadFrom(assemblyPath);
            
            // Look for types implementing IPlugin
            var pluginTypes = assembly.GetTypes()
                .Where(t => !t.IsAbstract && !t.IsInterface)
                .Where(t => typeof(IPlugin).IsAssignableFrom(t))
                .ToList();

            if (pluginTypes.Count == 0)
            {
                _logger.LogDebug("No plugin types found in {AssemblyPath}", assemblyPath);
                return null;
            }

            if (pluginTypes.Count > 1)
            {
                _logger.LogWarning("Multiple plugin types found in {AssemblyPath}, using first: {PluginType}", 
                    assemblyPath, pluginTypes[0].FullName);
            }

            var pluginType = pluginTypes[0];
            var assemblyName = assembly.GetName();

            // Create simplified PluginTypeInfo
            var typeInfo = new PluginTypeInfo
            {
                TypeName = pluginType.FullName ?? pluginType.Name,
                FriendlyName = pluginType.Name,
                AssemblyPath = assemblyPath,
                AssemblyName = assemblyName.Name ?? Path.GetFileNameWithoutExtension(assemblyPath),
                Category = DeterminePluginCategory(pluginType),
                IsBuiltIn = false,
                RequiresConfiguration = false
            };

            return new DiscoveredPlugin
            {
                Name = assemblyName.Name ?? Path.GetFileNameWithoutExtension(assemblyPath),
                AssemblyPath = assemblyPath,
                TypeInfo = typeInfo,
                Version = assemblyName.Version
            };
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to load plugin info from {AssemblyPath}", assemblyPath);
            return null;
        }
    }

    /// <summary>
    /// Determines the plugin category based on implemented interfaces.
    /// </summary>
    private static PluginCategory DeterminePluginCategory(Type pluginType)
    {
        var interfaces = pluginType.GetInterfaces();

        if (interfaces.Any(i => i.Name.Contains("Source")))
            return PluginCategory.Source;
        
        if (interfaces.Any(i => i.Name.Contains("Transform")))
            return PluginCategory.Transform;
        
        if (interfaces.Any(i => i.Name.Contains("Sink")))
            return PluginCategory.Sink;

        return PluginCategory.Other;
    }
}