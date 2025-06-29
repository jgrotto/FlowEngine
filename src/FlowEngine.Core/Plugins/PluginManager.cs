using FlowEngine.Abstractions;
using FlowEngine.Abstractions.Configuration;
using FlowEngine.Abstractions.Plugins;
using Microsoft.Extensions.Configuration;
using System.Collections.Concurrent;
using System.Reflection;

namespace FlowEngine.Core.Plugins;

/// <summary>
/// High-performance plugin manager that coordinates plugin loading, configuration, and lifecycle management.
/// Provides centralized plugin management with isolation, monitoring, and error handling.
/// </summary>
public sealed class PluginManager : IPluginManager
{
    private readonly IPluginLoader _pluginLoader;
    private readonly ConcurrentDictionary<string, ManagedPlugin> _plugins = new();
    private readonly object _managementLock = new();
    private bool _disposed;

    /// <summary>
    /// Initializes a new plugin manager with the specified plugin loader.
    /// </summary>
    /// <param name="pluginLoader">Plugin loader for assembly management</param>
    public PluginManager(IPluginLoader pluginLoader)
    {
        _pluginLoader = pluginLoader ?? throw new ArgumentNullException(nameof(pluginLoader));
        
        // Subscribe to loader events
        _pluginLoader.PluginLoaded += OnPluginLoaded;
        _pluginLoader.PluginUnloaded += OnPluginUnloaded;
        _pluginLoader.PluginLoadFailed += OnPluginLoadFailed;
    }

    /// <summary>
    /// Initializes a new plugin manager with a default plugin loader.
    /// </summary>
    public PluginManager() : this(new PluginLoader())
    {
    }

    /// <inheritdoc />
    public event EventHandler<PluginLoadedEventArgs>? PluginLoaded;

    /// <inheritdoc />
    public event EventHandler<PluginUnloadedEventArgs>? PluginUnloaded;

    /// <inheritdoc />
    public event EventHandler<PluginErrorEventArgs>? PluginError;

    /// <inheritdoc />
    public async Task<IPlugin> LoadPluginAsync(IPluginConfiguration configuration)
    {
        ThrowIfDisposed();

        if (configuration == null)
            throw new ArgumentNullException(nameof(configuration));

        if (string.IsNullOrWhiteSpace(configuration.Name))
            throw new ArgumentException("Plugin name cannot be null or empty", nameof(configuration));

        if (string.IsNullOrWhiteSpace(configuration.Type))
            throw new ArgumentException("Plugin type cannot be null or empty", nameof(configuration));

        lock (_managementLock)
        {
            if (_plugins.ContainsKey(configuration.Name))
                throw new PluginLoadException($"Plugin '{configuration.Name}' is already loaded");
        }

        try
        {
            // Determine the assembly path
            var assemblyPath = ResolveAssemblyPath(configuration);

            // Determine isolation level from configuration
            var isolationLevel = DetermineIsolationLevel(configuration);

            // Load the plugin using the appropriate interface type
            var plugin = await LoadPluginByTypeAsync(assemblyPath, configuration.Type, isolationLevel);

            // Configure the plugin
            await ConfigurePluginAsync(plugin, configuration);

            // Track the plugin
            var managedPlugin = new ManagedPlugin
            {
                Name = configuration.Name,
                Plugin = plugin,
                Configuration = configuration,
                LoadedAt = DateTimeOffset.UtcNow,
                Status = PluginStatus.Loaded
            };

            lock (_managementLock)
            {
                _plugins[configuration.Name] = managedPlugin;
            }

            return plugin;
        }
        catch (Exception ex) when (!(ex is PluginLoadException))
        {
            var pluginException = new PluginLoadException($"Failed to load plugin '{configuration.Name}' of type '{configuration.Type}'", ex);
            
            try
            {
                PluginError?.Invoke(this, new PluginErrorEventArgs(configuration.Name, pluginException, "Load"));
            }
            catch
            {
                // Ignore event handler errors
            }

            throw pluginException;
        }
    }

    /// <inheritdoc />
    public async Task UnloadPluginAsync(IPlugin plugin)
    {
        ThrowIfDisposed();

        if (plugin == null)
            throw new ArgumentNullException(nameof(plugin));

        var managedPlugin = FindManagedPlugin(plugin);
        if (managedPlugin == null)
            return; // Plugin not managed by this manager

        try
        {
            // Remove from tracking first
            lock (_managementLock)
            {
                _plugins.TryRemove(managedPlugin.Name, out _);
            }

            // Unload through the plugin loader
            await _pluginLoader.UnloadPluginAsync(plugin);
        }
        catch (Exception ex)
        {
            try
            {
                PluginError?.Invoke(this, new PluginErrorEventArgs(managedPlugin.Name, ex, "Unload"));
            }
            catch
            {
                // Ignore event handler errors
            }

            throw;
        }
    }

    /// <inheritdoc />
    public IPlugin? GetPlugin(string name)
    {
        ThrowIfDisposed();

        if (string.IsNullOrWhiteSpace(name))
            return null;

        return _plugins.TryGetValue(name, out var managedPlugin) ? managedPlugin.Plugin : null;
    }

    /// <inheritdoc />
    public IReadOnlyCollection<T> GetPlugins<T>() where T : class, IPlugin
    {
        ThrowIfDisposed();

        lock (_managementLock)
        {
            return _plugins.Values
                .Select(mp => mp.Plugin)
                .OfType<T>()
                .ToArray();
        }
    }

    /// <inheritdoc />
    public IReadOnlyCollection<IPlugin> GetAllPlugins()
    {
        ThrowIfDisposed();

        lock (_managementLock)
        {
            return _plugins.Values
                .Select(mp => mp.Plugin)
                .ToArray();
        }
    }

    /// <inheritdoc />
    public IReadOnlyCollection<PluginInfo> GetPluginInfo()
    {
        ThrowIfDisposed();

        var loaderInfo = _pluginLoader.GetLoadedPlugins();
        
        lock (_managementLock)
        {
            return _plugins.Values
                .Select(mp => CreatePluginInfo(mp, loaderInfo))
                .ToArray();
        }
    }

    /// <inheritdoc />
    public Task<PluginValidationResult> ValidatePluginAsync(IPluginConfiguration configuration)
    {
        ThrowIfDisposed();

        if (configuration == null)
            throw new ArgumentNullException(nameof(configuration));

        var errors = new List<string>();
        var warnings = new List<string>();

        // Validate basic configuration
        if (string.IsNullOrWhiteSpace(configuration.Name))
            errors.Add("Plugin name is required");

        if (string.IsNullOrWhiteSpace(configuration.Type))
            errors.Add("Plugin type is required");

        if (errors.Count > 0)
            return Task.FromResult(PluginValidationResult.Failure(errors.ToArray()));

        try
        {
            // Validate assembly path
            var assemblyPath = ResolveAssemblyPath(configuration);
            if (!File.Exists(assemblyPath))
            {
                errors.Add($"Assembly file not found: {assemblyPath}");
                return Task.FromResult(PluginValidationResult.Failure(errors.ToArray()));
            }

            // Try to load the assembly and find the type
            var assembly = Assembly.LoadFrom(assemblyPath);
            var type = assembly.GetType(configuration.Type);

            if (type == null)
            {
                errors.Add($"Type '{configuration.Type}' not found in assembly '{assemblyPath}'");
                return Task.FromResult(PluginValidationResult.Failure(errors.ToArray()));
            }

            // Validate that type implements IPlugin
            if (!typeof(IPlugin).IsAssignableFrom(type))
            {
                errors.Add($"Type '{configuration.Type}' does not implement IPlugin interface");
                return Task.FromResult(PluginValidationResult.Failure(errors.ToArray()));
            }

            // Check for public parameterless constructor
            if (type.GetConstructor(Type.EmptyTypes) == null)
            {
                errors.Add($"Type '{configuration.Type}' must have a public parameterless constructor");
                return Task.FromResult(PluginValidationResult.Failure(errors.ToArray()));
            }

            // Validate schema if required
            if (configuration.Schema != null)
            {
                try
                {
                    var schema = configuration.Schema.ToSchema();
                    if (schema.ColumnCount == 0)
                        warnings.Add("Plugin schema contains no columns");
                }
                catch (Exception ex)
                {
                    errors.Add($"Invalid plugin schema: {ex.Message}");
                }
            }

            // Check for duplicate plugin names
            if (_plugins.ContainsKey(configuration.Name))
                warnings.Add($"Plugin '{configuration.Name}' is already loaded");

            var result = errors.Count > 0 
                ? PluginValidationResult.Failure(errors.ToArray())
                : PluginValidationResult.Success(type, warnings.ToArray());
            return Task.FromResult(result);
        }
        catch (Exception ex)
        {
            errors.Add($"Validation failed: {ex.Message}");
            return Task.FromResult(PluginValidationResult.Failure(errors.ToArray()));
        }
    }

    /// <inheritdoc />
    public async Task<IPlugin> ReloadPluginAsync(string name, IPluginConfiguration configuration)
    {
        ThrowIfDisposed();

        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Plugin name cannot be null or empty", nameof(name));

        if (configuration == null)
            throw new ArgumentNullException(nameof(configuration));

        // Unload existing plugin if it exists
        var existingPlugin = GetPlugin(name);
        if (existingPlugin != null)
        {
            await UnloadPluginAsync(existingPlugin);
        }

        // Load with new configuration
        return await LoadPluginAsync(configuration);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
            return;

        try
        {
            // Unload all plugins
            var unloadTasks = new List<Task>();
            
            lock (_managementLock)
            {
                foreach (var managedPlugin in _plugins.Values)
                {
                    unloadTasks.Add(UnloadPluginAsync(managedPlugin.Plugin));
                }
            }

            // Wait for all plugins to unload
            Task.WaitAll(unloadTasks.ToArray(), TimeSpan.FromSeconds(30));

            // Dispose the plugin loader
            _pluginLoader?.Dispose();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error during plugin manager disposal: {ex}");
        }
        finally
        {
            _disposed = true;
        }
    }

    private string ResolveAssemblyPath(IPluginConfiguration configuration)
    {
        if (!string.IsNullOrWhiteSpace(configuration.Assembly))
        {
            // Use explicit assembly path
            if (Path.IsPathRooted(configuration.Assembly))
                return configuration.Assembly;

            // Relative to current directory
            return Path.GetFullPath(configuration.Assembly);
        }

        // Try to find built-in plugin assembly
        var builtInAssemblyName = $"FlowEngine.Plugins.{configuration.Type}.dll";
        var possiblePaths = new[]
        {
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "plugins", builtInAssemblyName),
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, builtInAssemblyName),
            Path.Combine(Directory.GetCurrentDirectory(), "plugins", builtInAssemblyName),
            Path.Combine(Directory.GetCurrentDirectory(), builtInAssemblyName)
        };

        foreach (var path in possiblePaths)
        {
            if (File.Exists(path))
                return path;
        }

        throw new PluginLoadException($"Cannot resolve assembly for plugin type '{configuration.Type}'. Specify the Assembly property in configuration.");
    }

    private static PluginIsolationLevel DetermineIsolationLevel(IPluginConfiguration configuration)
    {
        // Check for custom isolation level in config
        if (configuration.Config.TryGetValue("IsolationLevel", out var isolationValue) && 
            isolationValue is string isolationString &&
            Enum.TryParse<PluginIsolationLevel>(isolationString, true, out var isolation))
        {
            return isolation;
        }

        // Use isolated by default for external assemblies
        return !string.IsNullOrWhiteSpace(configuration.Assembly) 
            ? PluginIsolationLevel.Isolated 
            : PluginIsolationLevel.Shared;
    }

    private async Task<IPlugin> LoadPluginByTypeAsync(string assemblyPath, string typeName, PluginIsolationLevel isolationLevel)
    {
        // Try to determine the specific plugin interface type
        var assembly = Assembly.LoadFrom(assemblyPath);
        var type = assembly.GetType(typeName) ?? throw new PluginLoadException($"Type {typeName} not found in assembly");

        if (typeof(ISourcePlugin).IsAssignableFrom(type))
            return await _pluginLoader.LoadPluginAsync<ISourcePlugin>(assemblyPath, typeName, isolationLevel);

        if (typeof(ITransformPlugin).IsAssignableFrom(type))
            return await _pluginLoader.LoadPluginAsync<ITransformPlugin>(assemblyPath, typeName, isolationLevel);

        if (typeof(ISinkPlugin).IsAssignableFrom(type))
            return await _pluginLoader.LoadPluginAsync<ISinkPlugin>(assemblyPath, typeName, isolationLevel);

        // Fall back to base IPlugin interface
        return await _pluginLoader.LoadPluginAsync<IPlugin>(assemblyPath, typeName, isolationLevel);
    }

    private static async Task ConfigurePluginAsync(IPlugin plugin, IPluginConfiguration configuration)
    {
        // Create configuration object
        var configBuilder = new ConfigurationBuilder();
        foreach (var kvp in configuration.Config)
        {
            configBuilder.AddInMemoryCollection(new[] { new KeyValuePair<string, string?>(kvp.Key, kvp.Value?.ToString()) });
        }
        var config = configBuilder.Build();

        // Initialize the plugin with its configuration
        await plugin.InitializeAsync(config);

        // Apply schema if provided and plugin supports it
        if (configuration.Schema != null && plugin is ISchemaAwarePlugin schemaAware)
        {
            var schema = configuration.Schema.ToSchema();
            await schemaAware.SetSchemaAsync(schema);
        }

        // Apply resource limits if provided
        if (configuration.ResourceLimits != null && plugin is IResourceLimitedPlugin resourceLimited)
        {
            await resourceLimited.SetResourceLimitsAsync(configuration.ResourceLimits);
        }
    }

    private ManagedPlugin? FindManagedPlugin(IPlugin plugin)
    {
        lock (_managementLock)
        {
            return _plugins.Values.FirstOrDefault(mp => ReferenceEquals(mp.Plugin, plugin));
        }
    }

    private static PluginInfo CreatePluginInfo(ManagedPlugin managedPlugin, IReadOnlyCollection<PluginInfo> loaderInfo)
    {
        // Find corresponding loader info
        var loaderPluginInfo = loaderInfo.FirstOrDefault(pi => 
            pi.TypeName == managedPlugin.Plugin.GetType().FullName);

        return new PluginInfo
        {
            Id = loaderPluginInfo?.Id ?? managedPlugin.Name,
            TypeName = managedPlugin.Plugin.GetType().FullName ?? managedPlugin.Plugin.GetType().Name,
            AssemblyPath = managedPlugin.Plugin.GetType().Assembly.Location,
            IsolationLevel = loaderPluginInfo?.IsolationLevel ?? PluginIsolationLevel.Shared,
            LoadedAt = managedPlugin.LoadedAt,
            Status = managedPlugin.Status,
            ContextName = loaderPluginInfo?.ContextName,
            Metadata = loaderPluginInfo?.Metadata
        };
    }

    private void OnPluginLoaded(object? sender, PluginLoadedEventArgs e)
    {
        try
        {
            PluginLoaded?.Invoke(this, e);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Plugin loaded event handler failed: {ex}");
        }
    }

    private void OnPluginUnloaded(object? sender, PluginUnloadedEventArgs e)
    {
        try
        {
            PluginUnloaded?.Invoke(this, e);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Plugin unloaded event handler failed: {ex}");
        }
    }

    private void OnPluginLoadFailed(object? sender, PluginLoadFailedEventArgs e)
    {
        try
        {
            PluginError?.Invoke(this, new PluginErrorEventArgs(
                Path.GetFileNameWithoutExtension(e.AssemblyPath), 
                e.Exception, 
                "Load"));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Plugin load failed event handler failed: {ex}");
        }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(PluginManager));
    }
}

/// <summary>
/// Internal tracking information for managed plugins.
/// </summary>
internal sealed class ManagedPlugin
{
    public required string Name { get; init; }
    public required IPlugin Plugin { get; init; }
    public required IPluginConfiguration Configuration { get; init; }
    public required DateTimeOffset LoadedAt { get; init; }
    public required PluginStatus Status { get; init; }
}

/// <summary>
/// Interface for plugins that can be configured.
/// </summary>
public interface IConfigurablePlugin
{
    /// <summary>
    /// Configures the plugin with the specified settings.
    /// </summary>
    /// <param name="config">Configuration settings</param>
    /// <returns>Task that completes when configuration is applied</returns>
    Task ConfigureAsync(IReadOnlyDictionary<string, object> config);
}

/// <summary>
/// Interface for plugins that are schema-aware.
/// </summary>
public interface ISchemaAwarePlugin
{
    /// <summary>
    /// Sets the schema for this plugin.
    /// </summary>
    /// <param name="schema">Schema to apply</param>
    /// <returns>Task that completes when schema is applied</returns>
    Task SetSchemaAsync(ISchema schema);
}

/// <summary>
/// Interface for plugins that support resource limits.
/// </summary>
public interface IResourceLimitedPlugin
{
    /// <summary>
    /// Sets resource limits for this plugin.
    /// </summary>
    /// <param name="limits">Resource limits to apply</param>
    /// <returns>Task that completes when limits are applied</returns>
    Task SetResourceLimitsAsync(IResourceLimits limits);
}