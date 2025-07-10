using FlowEngine.Abstractions.Configuration;
using FlowEngine.Abstractions.Data;
using FlowEngine.Abstractions.Plugins;
using FlowEngine.Core.Configuration;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Collections.Immutable;

namespace FlowEngine.Core.Plugins;

/// <summary>
/// High-level plugin management implementation that coordinates plugin loading, configuration, and lifecycle.
/// Provides the main entry point for plugin operations in the FlowEngine pipeline.
/// </summary>
public sealed class PluginManager : IPluginManager
{
    private readonly IPluginLoader _pluginLoader;
    private readonly IPluginRegistry _pluginRegistry;
    private readonly IPluginDiscoveryService? _discoveryService;
    private readonly IPluginConfigurationMapper _configurationMapper;
    private readonly ILogger<PluginManager> _logger;
    private readonly ConcurrentDictionary<string, LoadedPluginEntry> _loadedPlugins = new();
    private readonly object _loadLock = new();
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the PluginManager.
    /// </summary>
    /// <param name="pluginLoader">Plugin loader for loading assemblies</param>
    /// <param name="pluginRegistry">Plugin registry for type discovery</param>
    /// <param name="logger">Logger for plugin operations</param>
    /// <param name="discoveryService">Optional plugin discovery service for enhanced plugin discovery</param>
    public PluginManager(IPluginLoader pluginLoader, IPluginRegistry pluginRegistry, ILogger<PluginManager> logger, IPluginDiscoveryService? discoveryService = null, IPluginConfigurationMapper? configurationMapper = null)
    {
        _pluginLoader = pluginLoader ?? throw new ArgumentNullException(nameof(pluginLoader));
        _pluginRegistry = pluginRegistry ?? throw new ArgumentNullException(nameof(pluginRegistry));
        _discoveryService = discoveryService;
        _configurationMapper = configurationMapper ?? new PluginConfigurationMapper(Microsoft.Extensions.Logging.LoggerFactory.Create(builder => builder.AddConsole()).CreateLogger<PluginConfigurationMapper>());
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        // Subscribe to loader events
        _pluginLoader.PluginLoaded += OnPluginLoaded;
        _pluginLoader.PluginUnloaded += OnPluginUnloaded;
        _pluginLoader.PluginLoadFailed += OnPluginLoadFailed;
    }

    /// <inheritdoc />
    public async Task<IPlugin> LoadPluginAsync(IPluginDefinition definition)
    {
        ThrowIfDisposed();

        if (definition == null)
            throw new ArgumentNullException(nameof(definition));

        if (string.IsNullOrWhiteSpace(definition.Name))
            throw new ArgumentException("Plugin definition must have a name", nameof(definition));

        if (string.IsNullOrWhiteSpace(definition.Type))
            throw new ArgumentException("Plugin definition must have a type", nameof(definition));

        _logger.LogInformation("Loading plugin '{PluginName}' of type '{PluginType}'", definition.Name, definition.Type);

        lock (_loadLock)
        {
            if (_loadedPlugins.ContainsKey(definition.Name))
                throw new PluginLoadException($"Plugin '{definition.Name}' is already loaded");
        }

        try
        {
            // Step 1: Find the plugin type in the registry
            var pluginTypeInfo = _pluginRegistry.GetPluginType(definition.Type);
            if (pluginTypeInfo == null)
            {
                // Try to scan for the plugin in common locations
                await ScanForPluginType(definition.Type);
                pluginTypeInfo = _pluginRegistry.GetPluginType(definition.Type);
            }

            // Step 2: Load the plugin using the loader
            IPlugin plugin;
            if (pluginTypeInfo != null)
            {
                // Plugin type found in registry - use registry information
                plugin = await _pluginLoader.LoadPluginAsync<IPlugin>(
                    pluginTypeInfo.AssemblyPath, 
                    pluginTypeInfo.TypeName,
                    PluginIsolationLevel.Shared);
            }
            else if (!string.IsNullOrWhiteSpace(definition.AssemblyPath))
            {
                // Plugin type not in registry but assembly path provided - load directly
                _logger.LogInformation("Plugin type '{PluginType}' not found in registry, loading directly from assembly '{AssemblyPath}'", 
                    definition.Type, definition.AssemblyPath);
                
                plugin = await _pluginLoader.LoadPluginAsync<IPlugin>(
                    definition.AssemblyPath, 
                    definition.Type,
                    PluginIsolationLevel.Shared);
            }
            else
            {
                throw new PluginLoadException($"Plugin type '{definition.Type}' not found in registry and no assembly path provided");
            }

            // Step 3: Initialize the plugin with configuration
            await InitializePluginAsync(plugin, definition);

            // Step 4: Track the loaded plugin
            var entry = new LoadedPluginEntry
            {
                Name = definition.Name,
                Plugin = plugin,
                Definition = definition,
                LoadedAt = DateTimeOffset.UtcNow
            };

            lock (_loadLock)
            {
                _loadedPlugins[definition.Name] = entry;
            }

            _logger.LogInformation("Successfully loaded plugin '{PluginName}'", definition.Name);
            return plugin;
        }
        catch (Exception ex) when (!(ex is PluginLoadException))
        {
            _logger.LogError(ex, "Failed to load plugin '{PluginName}' of type '{PluginType}'", definition.Name, definition.Type);
            throw new PluginLoadException($"Failed to load plugin '{definition.Name}': {ex.Message}", ex);
        }
    }

    /// <inheritdoc />
    public async Task UnloadPluginAsync(IPlugin plugin)
    {
        ThrowIfDisposed();

        if (plugin == null)
            throw new ArgumentNullException(nameof(plugin));

        LoadedPluginEntry? entry = null;
        lock (_loadLock)
        {
            entry = _loadedPlugins.Values.FirstOrDefault(e => ReferenceEquals(e.Plugin, plugin));
            if (entry != null)
                _loadedPlugins.TryRemove(entry.Name, out _);
        }

        if (entry == null)
        {
            _logger.LogWarning("Attempted to unload plugin that is not managed by this manager");
            return;
        }

        try
        {
            _logger.LogInformation("Unloading plugin '{PluginName}'", entry.Name);
            await _pluginLoader.UnloadPluginAsync(plugin);
            _logger.LogInformation("Successfully unloaded plugin '{PluginName}'", entry.Name);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to unload plugin '{PluginName}'", entry.Name);
            throw;
        }
    }

    /// <inheritdoc />
    public IPlugin? GetPlugin(string name)
    {
        ThrowIfDisposed();

        if (string.IsNullOrWhiteSpace(name))
            return null;

        lock (_loadLock)
        {
            return _loadedPlugins.TryGetValue(name, out var entry) ? entry.Plugin : null;
        }
    }

    /// <inheritdoc />
    public IReadOnlyCollection<IPlugin> GetAllPlugins()
    {
        ThrowIfDisposed();

        lock (_loadLock)
        {
            return _loadedPlugins.Values.Select(e => e.Plugin).ToArray();
        }
    }

    /// <inheritdoc />
    public IReadOnlyCollection<T> GetPlugins<T>() where T : class, IPlugin
    {
        ThrowIfDisposed();

        lock (_loadLock)
        {
            return _loadedPlugins.Values
                .Select(e => e.Plugin)
                .OfType<T>()
                .ToArray();
        }
    }

    /// <inheritdoc />
    public IReadOnlyCollection<PluginInfo> GetPluginInfo()
    {
        ThrowIfDisposed();

        var loaderPlugins = _pluginLoader.GetLoadedPlugins();
        return loaderPlugins;
    }

    /// <inheritdoc />
    public async Task<ValidationResult> ValidatePluginAsync(IPluginDefinition definition)
    {
        ThrowIfDisposed();

        if (definition == null)
            return ValidationResult.Failure(new ValidationError 
            { 
                Code = "NULL_DEFINITION", 
                Message = "Plugin definition cannot be null",
                Severity = ValidationSeverity.Error 
            });

        var errors = new List<ValidationError>();

        // Validate required fields
        if (string.IsNullOrWhiteSpace(definition.Name))
            errors.Add(new ValidationError 
            { 
                Code = "MISSING_NAME", 
                Message = "Plugin name is required",
                Severity = ValidationSeverity.Error 
            });

        if (string.IsNullOrWhiteSpace(definition.Type))
            errors.Add(new ValidationError 
            { 
                Code = "MISSING_TYPE", 
                Message = "Plugin type is required",
                Severity = ValidationSeverity.Error 
            });

        // Check if plugin type exists
        if (!string.IsNullOrWhiteSpace(definition.Type))
        {
            var pluginTypeInfo = _pluginRegistry.GetPluginType(definition.Type);
            if (pluginTypeInfo == null)
            {
                // Try to scan for the plugin type
                await ScanForPluginType(definition.Type);
                pluginTypeInfo = _pluginRegistry.GetPluginType(definition.Type);
                
                if (pluginTypeInfo == null)
                    errors.Add(new ValidationError 
                    { 
                        Code = "TYPE_NOT_FOUND", 
                        Message = $"Plugin type '{definition.Type}' not found",
                        Severity = ValidationSeverity.Error 
                    });
            }
        }

        return errors.Count > 0 
            ? ValidationResult.Failure(errors.ToArray())
            : ValidationResult.Success();
    }

    /// <inheritdoc />
    public async Task<IPlugin> ReloadPluginAsync(string name, IPluginDefinition definition)
    {
        ThrowIfDisposed();

        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Plugin name cannot be null or empty", nameof(name));

        // Unload existing plugin if it exists
        var existingPlugin = GetPlugin(name);
        if (existingPlugin != null)
        {
            await UnloadPluginAsync(existingPlugin);
        }

        // Load the new plugin
        return await LoadPluginAsync(definition);
    }

    /// <inheritdoc />
    public event EventHandler<PluginLoadedEventArgs>? PluginLoaded;

    /// <inheritdoc />
    public event EventHandler<PluginUnloadedEventArgs>? PluginUnloaded;

    /// <inheritdoc />
    public event EventHandler<PluginErrorEventArgs>? PluginError;

    /// <inheritdoc />
    public async Task<IReadOnlyCollection<DiscoveredPlugin>> DiscoverAvailablePluginsAsync()
    {
        ThrowIfDisposed();

        if (_discoveryService == null)
        {
            _logger.LogWarning("Plugin discovery service not available. Using basic directory scanning.");
            return await DiscoverPluginsBasicAsync();
        }

        try
        {
            _logger.LogInformation("Starting enhanced plugin discovery across default directories");
            var plugins = await _discoveryService.DiscoverPluginsAsync();
            
            _logger.LogInformation("Discovered {Count} plugins with enhanced discovery", plugins.Count);
            return plugins;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Enhanced plugin discovery failed, falling back to basic scanning");
            return await DiscoverPluginsBasicAsync();
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyCollection<DiscoveredPlugin>> DiscoverPluginsInDirectoryAsync(string directoryPath, bool includeSubdirectories = true)
    {
        ThrowIfDisposed();

        if (string.IsNullOrWhiteSpace(directoryPath))
            throw new ArgumentException("Directory path cannot be null or empty", nameof(directoryPath));

        if (_discoveryService == null)
        {
            _logger.LogWarning("Plugin discovery service not available. Using basic directory scanning for: {DirectoryPath}", directoryPath);
            return await DiscoverPluginsBasicInDirectoryAsync(directoryPath, includeSubdirectories);
        }

        try
        {
            _logger.LogInformation("Starting enhanced plugin discovery in directory: {DirectoryPath}", directoryPath);
            var plugins = await _discoveryService.DiscoverPluginsAsync(directoryPath, includeSubdirectories);
            
            _logger.LogInformation("Discovered {Count} plugins in directory: {DirectoryPath}", plugins.Count, directoryPath);
            return plugins;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Enhanced plugin discovery failed for directory {DirectoryPath}, falling back to basic scanning", directoryPath);
            return await DiscoverPluginsBasicInDirectoryAsync(directoryPath, includeSubdirectories);
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
            return;

        try
        {
            // Unload all plugins
            var tasks = new List<Task>();
            
            lock (_loadLock)
            {
                foreach (var entry in _loadedPlugins.Values)
                {
                    tasks.Add(UnloadPluginAsync(entry.Plugin));
                }
                _loadedPlugins.Clear();
            }

            // Wait for all plugins to unload (with timeout)
            if (tasks.Count > 0)
                Task.WaitAll(tasks.ToArray(), TimeSpan.FromSeconds(10));

            // Unsubscribe from events
            _pluginLoader.PluginLoaded -= OnPluginLoaded;
            _pluginLoader.PluginUnloaded -= OnPluginUnloaded;
            _pluginLoader.PluginLoadFailed -= OnPluginLoadFailed;

            _logger.LogInformation("Plugin manager disposed");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during plugin manager disposal");
        }
        finally
        {
            _disposed = true;
        }
    }

    private async Task InitializePluginAsync(IPlugin plugin, IPluginDefinition definition)
    {
        try
        {
            // Create configuration for the plugin
            var configuration = await CreatePluginConfigurationAsync(definition);
            
            // Initialize the plugin
            var result = await plugin.InitializeAsync(configuration);
            
            if (!result.Success)
            {
                var errors = string.Join(", ", result.Errors);
                throw new PluginLoadException($"Plugin initialization failed: {result.Message}. Errors: {errors}");
            }

            _logger.LogDebug("Plugin '{PluginName}' initialized successfully in {InitTime}ms", 
                definition.Name, result.InitializationTime.TotalMilliseconds);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize plugin '{PluginName}'", definition.Name);
            throw new PluginLoadException($"Failed to initialize plugin '{definition.Name}': {ex.Message}", ex);
        }
    }

    private async Task<Abstractions.Plugins.IPluginConfiguration> CreatePluginConfigurationAsync(IPluginDefinition definition)
    {
        // Use the enhanced configuration mapper for all plugin types
        return await _configurationMapper.CreateConfigurationAsync(definition);
    }

    private Task<Abstractions.Plugins.IPluginConfiguration> CreateTemplatePluginConfigurationAsync(IPluginDefinition definition)
    {
        var assemblyPath = definition.AssemblyPath ?? throw new PluginLoadException("Assembly path required for TemplatePlugin");
        var assembly = System.Reflection.Assembly.LoadFrom(assemblyPath);
        var configType = assembly.GetType("TemplatePlugin.TemplatePluginConfiguration");
        
        if (configType == null)
            throw new PluginLoadException("TemplatePluginConfiguration type not found in plugin assembly");

        try
        {
            // Extract configuration values from YAML with proper type conversion
            var config = definition.Configuration ?? (IReadOnlyDictionary<string, object>)new Dictionary<string, object>();
            
            var rowCount = ExtractConfigValue<int>(config, "RowCount", 1000);
            var batchSize = ExtractConfigValue<int>(config, "BatchSize", 100);
            var dataType = ExtractConfigValue<string>(config, "DataType", "Customer");
            
            // Validate configuration values
            if (rowCount < 1 || rowCount > 5_000_000)
                throw new PluginLoadException($"RowCount must be between 1 and 5,000,000, got: {rowCount}");
                
            if (batchSize < 1 || batchSize > 50_000)
                throw new PluginLoadException($"BatchSize must be between 1 and 50,000, got: {batchSize}");
                
            if (string.IsNullOrWhiteSpace(dataType))
                throw new PluginLoadException("DataType cannot be null or empty");

            // Create configuration instance using reflection with proper property initialization
            var configInstance = Activator.CreateInstance(configType, new object[0]);
            if (configInstance == null)
                throw new PluginLoadException("Failed to create TemplatePluginConfiguration instance");

            // Since TemplatePluginConfiguration properties are init-only, we need to use reflection
            // to set the values after construction, or create a new instance with the values
            var properties = configType.GetProperties()
                .Where(p => p.CanWrite || (p.SetMethod?.IsPublic == true))
                .ToArray();

            // Try to find the properties and set them via reflection
            var rowCountProp = configType.GetProperty("RowCount");
            var batchSizeProp = configType.GetProperty("BatchSize");
            var dataTypeProp = configType.GetProperty("DataType");

            // For init-only properties, we need to use a different approach
            // Create a new instance using object initializer syntax via compiled expression
            // or use unsafe reflection to set init properties
            
            // Alternative: Create new instance with computed values using Activator and property setting
            var constructor = configType.GetConstructor(Type.EmptyTypes);
            if (constructor != null)
            {
                configInstance = constructor.Invoke(null);
                
                // Use reflection to set init-only properties (this works for init accessors)
                SetInitProperty(configInstance, "RowCount", rowCount);
                SetInitProperty(configInstance, "BatchSize", batchSize);
                SetInitProperty(configInstance, "DataType", dataType);
            }

            _logger.LogInformation("✅ Successfully bound YAML configuration values to TemplatePluginConfiguration: RowCount={RowCount}, BatchSize={BatchSize}, DataType={DataType}", 
                rowCount, batchSize, dataType);

            return Task.FromResult((Abstractions.Plugins.IPluginConfiguration)configInstance);
        }
        catch (Exception ex) when (!(ex is PluginLoadException))
        {
            _logger.LogError(ex, "Failed to bind configuration for TemplatePlugin");
            throw new PluginLoadException($"Configuration binding failed: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Extracts and converts a configuration value from the YAML configuration dictionary.
    /// </summary>
    /// <typeparam name="T">The target type for the configuration value</typeparam>
    /// <param name="config">The configuration dictionary from YAML</param>
    /// <param name="key">The configuration key</param>
    /// <param name="defaultValue">The default value if key is not found</param>
    /// <returns>The converted configuration value</returns>
    private static T ExtractConfigValue<T>(IReadOnlyDictionary<string, object> config, string key, T defaultValue)
    {
        if (!config.TryGetValue(key, out var value))
            return defaultValue;

        try
        {
            // Handle different value types from YAML parsing
            return value switch
            {
                T directValue => directValue,
                string strValue when typeof(T) == typeof(int) => (T)(object)int.Parse(strValue),
                string strValue when typeof(T) == typeof(string) => (T)(object)strValue,
                int intValue when typeof(T) == typeof(int) => (T)(object)intValue,
                long longValue when typeof(T) == typeof(int) => (T)(object)(int)longValue,
                _ => (T)Convert.ChangeType(value, typeof(T))
            };
        }
        catch (Exception ex)
        {
            throw new PluginLoadException($"Failed to convert configuration value '{key}' of type {value?.GetType().Name} to {typeof(T).Name}: {ex.Message}", ex);
        }
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


    private async Task ScanForPluginType(string typeName)
    {
        try
        {
            // Try scanning common plugin directories
            var pluginDirectories = GetPluginDirectories();
            
            foreach (var directory in pluginDirectories)
            {
                if (Directory.Exists(directory))
                {
                    _logger.LogDebug("Scanning directory '{Directory}' for plugin type '{TypeName}'", directory, typeName);
                    await _pluginRegistry.ScanDirectoryAsync(directory);
                    
                    // Check if we found the type
                    if (_pluginRegistry.GetPluginType(typeName) != null)
                    {
                        _logger.LogDebug("Found plugin type '{TypeName}' in directory '{Directory}'", typeName, directory);
                        break;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to scan for plugin type '{TypeName}'", typeName);
        }
    }

    private static string[] GetPluginDirectories()
    {
        // Common plugin directories to scan
        var appDirectory = AppDomain.CurrentDomain.BaseDirectory;
        return new[]
        {
            Path.Combine(appDirectory, "plugins"),
            Path.Combine(appDirectory, "Plugins"), 
            Path.Combine(appDirectory, "..", "plugins"),
            appDirectory // Current directory as fallback
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
            _logger.LogError(ex, "Error in plugin loaded event handler");
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
            _logger.LogError(ex, "Error in plugin unloaded event handler");
        }
    }

    private void OnPluginLoadFailed(object? sender, PluginLoadFailedEventArgs e)
    {
        try
        {
            var errorArgs = new PluginErrorEventArgs(e.TypeName, e.Exception, "Load");
            PluginError?.Invoke(this, errorArgs);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in plugin load failed event handler");
        }
    }

    /// <summary>
    /// Basic plugin discovery fallback when enhanced discovery service is not available.
    /// </summary>
    private async Task<IReadOnlyCollection<DiscoveredPlugin>> DiscoverPluginsBasicAsync()
    {
        var allPlugins = new List<DiscoveredPlugin>();
        var directories = GetPluginDirectories();

        foreach (var directory in directories)
        {
            if (Directory.Exists(directory))
            {
                var plugins = await DiscoverPluginsBasicInDirectoryAsync(directory, includeSubdirectories: true);
                allPlugins.AddRange(plugins);
            }
        }

        return allPlugins.AsReadOnly();
    }

    /// <summary>
    /// Basic plugin discovery for a specific directory.
    /// </summary>
    private async Task<IReadOnlyCollection<DiscoveredPlugin>> DiscoverPluginsBasicInDirectoryAsync(string directoryPath, bool includeSubdirectories)
    {
        var discoveredPlugins = new List<DiscoveredPlugin>();

        try
        {
            // Scan for assemblies and register them
            await _pluginRegistry.ScanDirectoryAsync(directoryPath, "*.dll");

            // Create synthetic discovered plugins from registry
            var pluginTypes = _pluginRegistry.GetPluginTypes()
                .Where(pt => pt.AssemblyPath.StartsWith(directoryPath, StringComparison.OrdinalIgnoreCase))
                .ToList();

            foreach (var pluginType in pluginTypes)
            {
                var manifest = new PluginManifest
                {
                    Id = pluginType.TypeName,
                    Name = pluginType.FriendlyName,
                    Version = pluginType.Version ?? "1.0.0",
                    Description = pluginType.Description,
                    Author = "Unknown",
                    Category = pluginType.Category,
                    AssemblyFileName = Path.GetFileName(pluginType.AssemblyPath),
                    PluginTypeName = pluginType.TypeName,
                    Dependencies = Array.Empty<PluginDependency>(),
                    SupportedInputSchemas = pluginType.SupportedSchemas,
                    SupportedOutputSchemas = Array.Empty<string>(),
                    RequiresConfiguration = pluginType.RequiresConfiguration,
                    ConfigurationSchemaFile = null,
                    Metadata = pluginType.Metadata,
                    MinimumFlowEngineVersion = null
                };

                var discoveredPlugin = new DiscoveredPlugin
                {
                    Manifest = manifest,
                    DirectoryPath = Path.GetDirectoryName(pluginType.AssemblyPath)!,
                    ManifestFilePath = string.Empty,
                    AssemblyPath = pluginType.AssemblyPath,
                    HasManifest = false,
                    TypeInfo = pluginType
                };

                discoveredPlugins.Add(discoveredPlugin);
            }

            _logger.LogDebug("Basic discovery found {Count} plugins in directory: {DirectoryPath}", 
                discoveredPlugins.Count, directoryPath);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Basic plugin discovery failed for directory: {DirectoryPath}", directoryPath);
        }

        return discoveredPlugins.AsReadOnly();
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(PluginManager));
    }
}

/// <summary>
/// Represents a loaded plugin entry.
/// </summary>
internal sealed class LoadedPluginEntry
{
    public required string Name { get; init; }
    public required IPlugin Plugin { get; init; }
    public required IPluginDefinition Definition { get; init; }
    public DateTimeOffset LoadedAt { get; init; }
}

/// <summary>
/// Simple plugin configuration implementation for Sprint 1.
/// </summary>
internal sealed class SimplePluginConfiguration : Abstractions.Plugins.IPluginConfiguration
{
    private readonly IPluginDefinition _definition;

    public SimplePluginConfiguration(IPluginDefinition definition)
    {
        _definition = definition ?? throw new ArgumentNullException(nameof(definition));
    }

    public string PluginId => _definition.Name;
    public string Name => _definition.Name;
    public string Version => "1.0.0"; // Default version
    public string PluginType => _definition.Type;
    public ISchema? InputSchema => null; // TODO: Implement schema mapping
    public ISchema? OutputSchema => null; // TODO: Implement schema mapping
    public bool SupportsHotSwapping => false; // Default to false
    public ImmutableDictionary<string, int> InputFieldIndexes => ImmutableDictionary<string, int>.Empty;
    public ImmutableDictionary<string, int> OutputFieldIndexes => ImmutableDictionary<string, int>.Empty;
    public ImmutableDictionary<string, object> Properties => _definition.Configuration?.ToImmutableDictionary() ?? ImmutableDictionary<string, object>.Empty;

    public int GetInputFieldIndex(string fieldName) => throw new NotSupportedException("Field indexing not implemented in simple configuration");
    public int GetOutputFieldIndex(string fieldName) => throw new NotSupportedException("Field indexing not implemented in simple configuration");
    public bool IsCompatibleWith(ISchema inputSchema) => true; // Default to compatible
    public T GetProperty<T>(string key) => Properties.TryGetValue(key, out var value) && value is T typed ? typed : default!;
    public bool TryGetProperty<T>(string key, out T? value)
    {
        value = default;
        if (Properties.TryGetValue(key, out var obj) && obj is T typed)
        {
            value = typed;
            return true;
        }
        return false;
    }
}

