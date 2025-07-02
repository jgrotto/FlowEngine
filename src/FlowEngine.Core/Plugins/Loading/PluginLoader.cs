using FlowEngine.Abstractions.Plugins;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.Loader;

namespace FlowEngine.Core.Plugins.Loading;

/// <summary>
/// Manages dynamic loading and lifecycle of plugin assemblies using isolated AssemblyLoadContext.
/// Supports hot-swapping, plugin validation, and proper resource cleanup.
/// </summary>
public sealed class PluginLoader : IPluginLoader
{
    private readonly ILogger<PluginLoader> _logger;
    private readonly ConcurrentDictionary<string, PluginDescriptor> _loadedPlugins = new();
    private readonly ConcurrentDictionary<string, WeakReference> _loadContextReferences = new();
    private readonly object _loadLock = new();
    private bool _disposed;

    /// <inheritdoc />
    public event EventHandler<PluginLoadedEventArgs>? PluginLoaded;

    /// <inheritdoc />
    public event EventHandler<PluginUnloadedEventArgs>? PluginUnloaded;

    /// <inheritdoc />
    public event EventHandler<PluginLoadFailedEventArgs>? PluginLoadFailed;

    /// <summary>
    /// Initializes a new instance of the PluginLoader class.
    /// </summary>
    /// <param name="logger">Logger instance</param>
    public PluginLoader(ILogger<PluginLoader> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<PluginDescriptor> LoadPluginAsync(string assemblyPath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(assemblyPath))
            throw new ArgumentException("Assembly path cannot be null or empty", nameof(assemblyPath));

        if (!File.Exists(assemblyPath))
            throw new FileNotFoundException($"Plugin assembly not found: {assemblyPath}");

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        
        _logger.LogInformation("Loading plugin from assembly: {AssemblyPath}", assemblyPath);

        try
        {
            // Validate plugin before loading
            var validationResult = await ValidatePluginAsync(assemblyPath, cancellationToken);
            if (!validationResult.IsValid)
            {
                var errorMessage = $"Plugin validation failed: {string.Join(", ", validationResult.Errors)}";
                _logger.LogError(errorMessage);
                throw new InvalidOperationException(errorMessage);
            }

            lock (_loadLock)
            {
                // Generate unique plugin ID based on assembly info
                var assemblyName = AssemblyName.GetAssemblyName(assemblyPath);
                var pluginId = $"{assemblyName.Name}_{assemblyName.Version}_{Path.GetFileNameWithoutExtension(assemblyPath)}";

                // Check if plugin is already loaded
                if (_loadedPlugins.ContainsKey(pluginId))
                {
                    var existingPlugin = _loadedPlugins[pluginId];
                    _logger.LogWarning("Plugin {PluginId} is already loaded from {AssemblyPath}", pluginId, existingPlugin.AssemblyPath);
                    return existingPlugin;
                }

                // Create isolated load context
                var loadContext = new PluginAssemblyLoadContext(pluginId, assemblyPath);
                _loadContextReferences[pluginId] = new WeakReference(loadContext);

                // Load the plugin assembly
                var assembly = loadContext.LoadPluginAssembly();

                // Find plugin types in the assembly
                var pluginTypes = FindPluginTypes(assembly);
                if (pluginTypes.Count == 0)
                {
                    throw new InvalidOperationException($"No plugin types found in assembly: {assemblyPath}");
                }

                if (pluginTypes.Count > 1)
                {
                    _logger.LogWarning("Multiple plugin types found in assembly {AssemblyPath}. Using first: {PluginType}", 
                        assemblyPath, pluginTypes[0].FullName);
                }

                var pluginType = pluginTypes[0];
                var pluginMetadata = ExtractPluginMetadata(pluginType);

                // Create plugin factory
                var pluginFactory = CreatePluginFactory(pluginType);

                // Create plugin descriptor
                var descriptor = new PluginDescriptor
                {
                    Id = pluginId,
                    Name = pluginMetadata.Name,
                    Version = pluginMetadata.Version,
                    Type = pluginMetadata.Type,
                    AssemblyPath = assemblyPath,
                    LoadContext = loadContext,
                    PluginFactory = pluginFactory,
                    LoadedAt = DateTimeOffset.UtcNow,
                    Status = PluginLoadStatus.Loaded,
                    Metadata = pluginMetadata.AdditionalMetadata
                };

                _loadedPlugins[pluginId] = descriptor;

                stopwatch.Stop();
                
                _logger.LogInformation("Successfully loaded plugin {PluginId} ({PluginName} v{Version}) in {ElapsedMs}ms", 
                    pluginId, pluginMetadata.Name, pluginMetadata.Version, stopwatch.ElapsedMilliseconds);

                // Raise loaded event
                PluginLoaded?.Invoke(this, new PluginLoadedEventArgs 
                { 
                    Plugin = descriptor, 
                    LoadTime = stopwatch.Elapsed 
                });

                return descriptor;
            }
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogError(ex, "Failed to load plugin from assembly: {AssemblyPath}", assemblyPath);
            
            // Raise load failed event
            PluginLoadFailed?.Invoke(this, new PluginLoadFailedEventArgs 
            { 
                AssemblyPath = assemblyPath, 
                Exception = ex 
            });
            
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<IPlugin> LoadPluginWithConfigurationAsync(string assemblyPath, IPluginConfiguration configuration, CancellationToken cancellationToken = default)
    {
        var descriptor = await LoadPluginAsync(assemblyPath, cancellationToken);
        
        try
        {
            var plugin = descriptor.PluginFactory(configuration);
            _logger.LogDebug("Created plugin instance {PluginId} with configuration", descriptor.Id);
            return plugin;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create plugin instance {PluginId} with configuration", descriptor.Id);
            throw new InvalidOperationException($"Failed to create plugin instance: {ex.Message}", ex);
        }
    }

    /// <inheritdoc />
    public async Task<bool> UnloadPluginAsync(string pluginId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(pluginId))
            return false;

        if (!_loadedPlugins.TryGetValue(pluginId, out var descriptor))
        {
            _logger.LogWarning("Plugin {PluginId} not found for unloading", pluginId);
            return false;
        }

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        
        _logger.LogInformation("Unloading plugin {PluginId}", pluginId);

        try
        {
            // Remove from loaded plugins
            _loadedPlugins.TryRemove(pluginId, out _);

            // Unload the assembly load context
            var loadContext = descriptor.LoadContext;
            if (loadContext.IsAlive())
            {
                loadContext.Unload();
                
                // Wait for unload to complete (with timeout)
                var unloadTask = WaitForUnloadAsync(pluginId);
                var timeoutTask = Task.Delay(TimeSpan.FromSeconds(30), cancellationToken);
                var completedTask = await Task.WhenAny(unloadTask, timeoutTask);
                
                if (completedTask == timeoutTask)
                {
                    _logger.LogWarning("Plugin {PluginId} unload timed out after 30 seconds", pluginId);
                }
            }

            stopwatch.Stop();
            
            _logger.LogInformation("Successfully unloaded plugin {PluginId} in {ElapsedMs}ms", pluginId, stopwatch.ElapsedMilliseconds);

            // Raise unloaded event
            PluginUnloaded?.Invoke(this, new PluginUnloadedEventArgs 
            { 
                Plugin = descriptor, 
                UnloadTime = stopwatch.Elapsed 
            });

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to unload plugin {PluginId}", pluginId);
            return false;
        }
    }

    /// <inheritdoc />
    public IReadOnlyCollection<PluginDescriptor> GetLoadedPlugins()
    {
        return _loadedPlugins.Values.ToList();
    }

    /// <inheritdoc />
    public PluginDescriptor? GetPlugin(string pluginId)
    {
        return _loadedPlugins.TryGetValue(pluginId, out var descriptor) ? descriptor : null;
    }

    /// <inheritdoc />
    public async Task<PluginValidationResult> ValidatePluginAsync(string assemblyPath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(assemblyPath))
            return PluginValidationResult.Failure("Assembly path cannot be null or empty");

        if (!File.Exists(assemblyPath))
            return PluginValidationResult.Failure($"Assembly file not found: {assemblyPath}");

        try
        {
            // Load assembly for validation without creating a persistent load context
            var tempContext = new PluginAssemblyLoadContext($"temp_validation_{Guid.NewGuid():N}", assemblyPath);
            
            try
            {
                var assembly = tempContext.LoadPluginAssembly();
                
                // Validate assembly has plugin types
                var pluginTypes = FindPluginTypes(assembly);
                if (pluginTypes.Count == 0)
                {
                    return PluginValidationResult.Failure("No plugin types implementing IPlugin found in assembly");
                }

                // Validate plugin types have proper constructors
                var validationErrors = new List<string>();
                foreach (var pluginType in pluginTypes)
                {
                    if (!ValidatePluginType(pluginType, out var errors))
                    {
                        validationErrors.AddRange(errors);
                    }
                }

                if (validationErrors.Count > 0)
                {
                    return PluginValidationResult.Failure(validationErrors.ToArray());
                }

                return PluginValidationResult.Success(pluginTypes[0]);
            }
            finally
            {
                // Unload the temporary context
                tempContext.Unload();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Plugin validation failed for assembly: {AssemblyPath}", assemblyPath);
            return PluginValidationResult.Failure($"Validation error: {ex.Message}");
        }
    }

    /// <inheritdoc />
    public async Task<HotSwapResult> HotSwapPluginAsync(string pluginId, string newAssemblyPath, CancellationToken cancellationToken = default)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        
        _logger.LogInformation("Hot-swapping plugin {PluginId} with new assembly: {NewAssemblyPath}", pluginId, newAssemblyPath);

        try
        {
            // Get the current plugin
            if (!_loadedPlugins.TryGetValue(pluginId, out var currentPlugin))
            {
                return new HotSwapResult
                {
                    Success = false,
                    Message = $"Plugin {pluginId} not found for hot-swap"
                };
            }

            // Validate the new assembly
            var validationResult = await ValidatePluginAsync(newAssemblyPath, cancellationToken);
            if (!validationResult.IsValid)
            {
                return new HotSwapResult
                {
                    Success = false,
                    Message = $"New assembly validation failed: {string.Join(", ", validationResult.Errors)}"
                };
            }

            // Unload the current plugin
            var unloadSuccess = await UnloadPluginAsync(pluginId, cancellationToken);
            if (!unloadSuccess)
            {
                return new HotSwapResult
                {
                    Success = false,
                    Message = "Failed to unload current plugin for hot-swap"
                };
            }

            // Load the new plugin
            var newPlugin = await LoadPluginAsync(newAssemblyPath, cancellationToken);

            stopwatch.Stop();

            return new HotSwapResult
            {
                Success = true,
                Message = $"Successfully hot-swapped plugin {pluginId}",
                SwapTime = stopwatch.Elapsed,
                PreviousConfiguration = currentPlugin.Metadata.ContainsKey("Configuration") 
                    ? currentPlugin.Metadata["Configuration"] as IPluginConfiguration 
                    : null
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Hot-swap failed for plugin {PluginId}", pluginId);
            return new HotSwapResult
            {
                Success = false,
                Message = $"Hot-swap failed: {ex.Message}",
                SwapTime = stopwatch.Elapsed
            };
        }
    }

    /// <summary>
    /// Finds all types in an assembly that implement IPlugin.
    /// </summary>
    /// <param name="assembly">Assembly to search</param>
    /// <returns>List of plugin types</returns>
    private static List<Type> FindPluginTypes(Assembly assembly)
    {
        var pluginTypes = new List<Type>();
        
        try
        {
            var types = assembly.GetTypes();
            foreach (var type in types)
            {
                if (type.IsClass && !type.IsAbstract && typeof(IPlugin).IsAssignableFrom(type))
                {
                    pluginTypes.Add(type);
                }
            }
        }
        catch (ReflectionTypeLoadException ex)
        {
            // Handle partial type loading
            foreach (var type in ex.Types)
            {
                if (type != null && type.IsClass && !type.IsAbstract && typeof(IPlugin).IsAssignableFrom(type))
                {
                    pluginTypes.Add(type);
                }
            }
        }

        return pluginTypes;
    }

    /// <summary>
    /// Validates that a plugin type has the necessary structure for instantiation.
    /// </summary>
    /// <param name="pluginType">Plugin type to validate</param>
    /// <param name="errors">Validation errors if any</param>
    /// <returns>True if valid</returns>
    private static bool ValidatePluginType(Type pluginType, out List<string> errors)
    {
        errors = new List<string>();

        // Check for public constructors
        var constructors = pluginType.GetConstructors(BindingFlags.Public | BindingFlags.Instance);
        if (constructors.Length == 0)
        {
            errors.Add($"Plugin type {pluginType.FullName} has no public constructors");
            return false;
        }

        // For now, just validate that constructors exist
        // In a full implementation, we would validate constructor parameters match DI container
        return true;
    }

    /// <summary>
    /// Extracts metadata from a plugin type.
    /// </summary>
    /// <param name="pluginType">Plugin type</param>
    /// <returns>Plugin metadata</returns>
    private static PluginMetadata ExtractPluginMetadata(Type pluginType)
    {
        // For now, extract basic metadata from type and assembly
        // In a full implementation, this would use attributes or interfaces
        var assembly = pluginType.Assembly;
        var assemblyName = assembly.GetName();

        return new PluginMetadata
        {
            Name = pluginType.Name.Replace("Plugin", ""),
            Version = assemblyName.Version?.ToString() ?? "1.0.0",
            Type = InferPluginType(pluginType.Name),
            AdditionalMetadata = new Dictionary<string, object>
            {
                ["AssemblyName"] = assemblyName.Name ?? "Unknown",
                ["TypeName"] = pluginType.FullName ?? "Unknown",
                ["Location"] = assembly.Location
            }
        };
    }

    /// <summary>
    /// Infers plugin type from the class name.
    /// </summary>
    /// <param name="typeName">Plugin type name</param>
    /// <returns>Inferred plugin type</returns>
    private static string InferPluginType(string typeName)
    {
        if (typeName.Contains("Source", StringComparison.OrdinalIgnoreCase))
            return "Source";
        if (typeName.Contains("Transform", StringComparison.OrdinalIgnoreCase))
            return "Transform";
        if (typeName.Contains("Sink", StringComparison.OrdinalIgnoreCase))
            return "Sink";
        
        return "Unknown";
    }

    /// <summary>
    /// Creates a factory function for instantiating plugins.
    /// </summary>
    /// <param name="pluginType">Plugin type</param>
    /// <returns>Plugin factory function</returns>
    private static Func<IPluginConfiguration, IPlugin> CreatePluginFactory(Type pluginType)
    {
        return configuration =>
        {
            // For now, use simple reflection-based instantiation
            // In a full implementation, this would integrate with DI container
            try
            {
                // Try to find a constructor that takes configuration
                var constructors = pluginType.GetConstructors();
                
                foreach (var constructor in constructors)
                {
                    var parameters = constructor.GetParameters();
                    if (parameters.Length == 0)
                    {
                        // Parameterless constructor
                        return (IPlugin)Activator.CreateInstance(pluginType)!;
                    }
                    
                    // For now, just try the first constructor with parameters
                    // In a real implementation, we'd match parameter types and use DI
                    var args = new object[parameters.Length];
                    for (int i = 0; i < parameters.Length; i++)
                    {
                        if (typeof(IPluginConfiguration).IsAssignableFrom(parameters[i].ParameterType))
                        {
                            args[i] = configuration;
                        }
                        else
                        {
                            // Use default value or null
                            args[i] = parameters[i].HasDefaultValue ? parameters[i].DefaultValue : null;
                        }
                    }
                    
                    return (IPlugin)Activator.CreateInstance(pluginType, args)!;
                }

                return (IPlugin)Activator.CreateInstance(pluginType)!;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to create plugin instance of type {pluginType.FullName}: {ex.Message}", ex);
            }
        };
    }

    /// <summary>
    /// Waits for a plugin assembly load context to be unloaded.
    /// </summary>
    /// <param name="pluginId">Plugin identifier</param>
    /// <returns>Task that completes when unload is finished</returns>
    private async Task WaitForUnloadAsync(string pluginId)
    {
        if (!_loadContextReferences.TryGetValue(pluginId, out var weakRef))
            return;

        // Wait for the weak reference to be collected
        while (weakRef.IsAlive)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            await Task.Delay(100);
        }

        _loadContextReferences.TryRemove(pluginId, out _);
    }

    /// <summary>
    /// Disposes the plugin loader and unloads all plugins.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
            return;

        _logger.LogInformation("Disposing plugin loader and unloading {PluginCount} plugins", _loadedPlugins.Count);

        // Unload all plugins
        var unloadTasks = _loadedPlugins.Keys.Select(pluginId => UnloadPluginAsync(pluginId));
        
        try
        {
            Task.WaitAll(unloadTasks.ToArray(), TimeSpan.FromSeconds(60));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during plugin loader disposal");
        }

        _loadedPlugins.Clear();
        _loadContextReferences.Clear();
        _disposed = true;

        _logger.LogInformation("Plugin loader disposed");
    }
}

/// <summary>
/// Internal plugin metadata structure.
/// </summary>
internal sealed record PluginMetadata
{
    public required string Name { get; init; }
    public required string Version { get; init; }
    public required string Type { get; init; }
    public IReadOnlyDictionary<string, object> AdditionalMetadata { get; init; } = new Dictionary<string, object>();
}