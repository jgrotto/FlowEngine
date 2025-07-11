using FlowEngine.Abstractions.Plugins;
using FlowEngine.Abstractions.Factories;
using FlowEngine.Abstractions.Services;
using FlowEngine.Core.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.Loader;

namespace FlowEngine.Core.Plugins;

/// <summary>
/// High-performance plugin loader with AssemblyLoadContext-based isolation.
/// Provides secure loading, unloading, and lifecycle management of plugins with memory cleanup.
/// </summary>
public sealed class PluginLoader : IPluginLoader
{
    private readonly ConcurrentDictionary<string, LoadedPluginContext> _loadedPlugins = new();
    private readonly object _loadLock = new();
    private readonly ISchemaFactory _schemaFactory;
    private readonly IArrayRowFactory _arrayRowFactory;
    private readonly IChunkFactory _chunkFactory;
    private readonly IDatasetFactory _datasetFactory;
    private readonly IDataTypeService _dataTypeService;
    private readonly IMemoryManager _memoryManager;
    private readonly IPerformanceMonitor _performanceMonitor;
    private readonly IChannelTelemetry _channelTelemetry;
    private readonly IScriptEngineService? _scriptEngineService;
    private readonly IJavaScriptContextService? _javaScriptContextService;
    private readonly ILogger<PluginLoader> _logger;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the PluginLoader with comprehensive service dependencies.
    /// </summary>
    /// <param name="schemaFactory">Factory for creating schemas</param>
    /// <param name="arrayRowFactory">Factory for creating array rows</param>
    /// <param name="chunkFactory">Factory for creating chunks</param>
    /// <param name="datasetFactory">Factory for creating datasets</param>
    /// <param name="dataTypeService">Service for data type operations</param>
    /// <param name="memoryManager">Service for memory management and pooling</param>
    /// <param name="performanceMonitor">Service for performance monitoring</param>
    /// <param name="channelTelemetry">Service for channel telemetry</param>
    /// <param name="scriptEngineService">Service for JavaScript script execution (optional)</param>
    /// <param name="javaScriptContextService">Service for JavaScript context creation (optional)</param>
    /// <param name="logger">Logger for plugin loading operations</param>
    public PluginLoader(
        ISchemaFactory schemaFactory,
        IArrayRowFactory arrayRowFactory,
        IChunkFactory chunkFactory,
        IDatasetFactory datasetFactory,
        IDataTypeService dataTypeService,
        IMemoryManager memoryManager,
        IPerformanceMonitor performanceMonitor,
        IChannelTelemetry channelTelemetry,
        ILogger<PluginLoader> logger,
        IScriptEngineService? scriptEngineService = null,
        IJavaScriptContextService? javaScriptContextService = null)
    {
        _schemaFactory = schemaFactory ?? throw new ArgumentNullException(nameof(schemaFactory));
        _arrayRowFactory = arrayRowFactory ?? throw new ArgumentNullException(nameof(arrayRowFactory));
        _chunkFactory = chunkFactory ?? throw new ArgumentNullException(nameof(chunkFactory));
        _datasetFactory = datasetFactory ?? throw new ArgumentNullException(nameof(datasetFactory));
        _dataTypeService = dataTypeService ?? throw new ArgumentNullException(nameof(dataTypeService));
        _memoryManager = memoryManager ?? throw new ArgumentNullException(nameof(memoryManager));
        _performanceMonitor = performanceMonitor ?? throw new ArgumentNullException(nameof(performanceMonitor));
        _channelTelemetry = channelTelemetry ?? throw new ArgumentNullException(nameof(channelTelemetry));
        _scriptEngineService = scriptEngineService;
        _javaScriptContextService = javaScriptContextService;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public event EventHandler<PluginLoadedEventArgs>? PluginLoaded;

    /// <inheritdoc />
    public event EventHandler<PluginUnloadedEventArgs>? PluginUnloaded;

    /// <inheritdoc />
    public event EventHandler<PluginLoadFailedEventArgs>? PluginLoadFailed;

    /// <inheritdoc />
    public async Task<T> LoadPluginAsync<T>(string assemblyPath, string typeName, PluginIsolationLevel isolationLevel = PluginIsolationLevel.Isolated) where T : class, IPlugin
    {
        ThrowIfDisposed();

        if (string.IsNullOrWhiteSpace(assemblyPath))
        {
            throw new ArgumentException("Assembly path cannot be null or empty", nameof(assemblyPath));
        }

        if (string.IsNullOrWhiteSpace(typeName))
        {
            throw new ArgumentException("Type name cannot be null or empty", nameof(typeName));
        }

        if (!File.Exists(assemblyPath))
        {
            throw new PluginLoadException($"Assembly file not found: {assemblyPath}") { AssemblyPath = assemblyPath, TypeName = typeName };
        }

        var pluginId = GeneratePluginId(assemblyPath, typeName);

        lock (_loadLock)
        {
            if (_loadedPlugins.ContainsKey(pluginId))
            {
                throw new PluginLoadException($"Plugin {typeName} from {assemblyPath} is already loaded") { AssemblyPath = assemblyPath, TypeName = typeName };
            }
        }

        try
        {
            var context = await LoadPluginContextAsync<T>(assemblyPath, typeName, isolationLevel);

            lock (_loadLock)
            {
                _loadedPlugins[pluginId] = context;
            }

            var pluginInfo = new PluginInfo
            {
                Id = pluginId,
                TypeName = typeName,
                AssemblyPath = assemblyPath,
                IsolationLevel = isolationLevel,
                LoadedAt = DateTimeOffset.UtcNow,
                Status = PluginStatus.Loaded,
                ContextName = context.LoadContext?.Name,
                Metadata = await ExtractPluginMetadataAsync(context.Plugin)
            };

            // Raise the loaded event
            try
            {
                PluginLoaded?.Invoke(this, new PluginLoadedEventArgs(pluginInfo, context.Plugin));
            }
            catch (Exception ex)
            {
                // Don't let event handler failures break plugin loading
                System.Diagnostics.Debug.WriteLine($"Plugin loaded event handler failed: {ex}");
            }

            return (T)context.Plugin;
        }
        catch (Exception ex) when (!(ex is PluginLoadException))
        {
            var loadException = new PluginLoadException($"Failed to load plugin {typeName} from {assemblyPath}", ex)
            {
                AssemblyPath = assemblyPath,
                TypeName = typeName,
                IsolationLevel = isolationLevel
            };

            try
            {
                PluginLoadFailed?.Invoke(this, new PluginLoadFailedEventArgs(assemblyPath, typeName, loadException));
            }
            catch
            {
                // Ignore event handler failures
            }

            throw loadException;
        }
    }

    /// <inheritdoc />
    public async Task UnloadPluginAsync(IPlugin plugin)
    {
        ThrowIfDisposed();

        if (plugin == null)
        {
            throw new ArgumentNullException(nameof(plugin));
        }

        var pluginContext = FindPluginContext(plugin);
        if (pluginContext == null)
        {
            return; // Plugin not managed by this loader
        }

        var pluginInfo = CreatePluginInfoFromContext(pluginContext);

        try
        {
            // Dispose the plugin first
            if (plugin is IAsyncDisposable asyncDisposable)
            {
                await asyncDisposable.DisposeAsync();
            }
            else if (plugin is IDisposable disposable)
            {
                disposable.Dispose();
            }

            // Remove from tracking
            lock (_loadLock)
            {
                _loadedPlugins.TryRemove(pluginContext.Id, out _);
            }

            // Unload the context if it's isolated
            if (pluginContext.LoadContext != null && pluginContext.LoadContext.IsCollectible)
            {
                var weakRef = new WeakReference(pluginContext.LoadContext);
                pluginContext.LoadContext.Unload();

                // Wait for GC to collect the context
                for (int i = 0; i < 10 && weakRef.IsAlive; i++)
                {
                    GC.Collect();
                    GC.WaitForPendingFinalizers();
                    await Task.Delay(100);
                }

                if (weakRef.IsAlive)
                {
                    System.Diagnostics.Debug.WriteLine($"Warning: Plugin context {pluginContext.LoadContext.Name} was not collected after unload");
                }
            }

            // Raise the unloaded event
            try
            {
                PluginUnloaded?.Invoke(this, new PluginUnloadedEventArgs(pluginInfo));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Plugin unloaded event handler failed: {ex}");
            }
        }
        catch (Exception ex)
        {
            throw new PluginLoadException($"Failed to unload plugin {plugin.GetType().Name}", ex);
        }
    }

    /// <inheritdoc />
    public IReadOnlyCollection<PluginInfo> GetLoadedPlugins()
    {
        ThrowIfDisposed();

        lock (_loadLock)
        {
            return _loadedPlugins.Values
                .Select(CreatePluginInfoFromContext)
                .ToArray();
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        try
        {
            // Unload all plugins
            var tasks = new List<Task>();

            lock (_loadLock)
            {
                foreach (var context in _loadedPlugins.Values)
                {
                    tasks.Add(UnloadPluginAsync(context.Plugin));
                }
            }

            // Wait for all plugins to unload (with timeout)
            Task.WaitAll(tasks.ToArray(), TimeSpan.FromSeconds(30));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error during plugin loader disposal: {ex}");
        }
        finally
        {
            _disposed = true;
        }
    }

    private async Task<LoadedPluginContext> LoadPluginContextAsync<T>(string assemblyPath, string typeName, PluginIsolationLevel isolationLevel) where T : class, IPlugin
    {
        switch (isolationLevel)
        {
            case PluginIsolationLevel.Shared:
                return await LoadSharedPluginAsync<T>(assemblyPath, typeName);

            case PluginIsolationLevel.Isolated:
                return await LoadIsolatedPluginAsync<T>(assemblyPath, typeName);

            case PluginIsolationLevel.Sandboxed:
                return await LoadSandboxedPluginAsync<T>(assemblyPath, typeName);

            default:
                throw new ArgumentException($"Unsupported isolation level: {isolationLevel}", nameof(isolationLevel));
        }
    }

    private Task<LoadedPluginContext> LoadSharedPluginAsync<T>(string assemblyPath, string typeName) where T : class, IPlugin
    {
        var assembly = Assembly.LoadFrom(assemblyPath);
        var type = assembly.GetType(typeName) ?? throw new PluginLoadException($"Type {typeName} not found in assembly {assemblyPath}");

        if (!typeof(T).IsAssignableFrom(type))
        {
            throw new PluginLoadException($"Type {typeName} does not implement {typeof(T).Name}");
        }

        var plugin = CreatePluginInstance<T>(type);

        // Note: Plugin initialization will be handled by the plugin manager with configuration

        var context = new LoadedPluginContext
        {
            Id = GeneratePluginId(assemblyPath, typeName),
            Plugin = plugin,
            LoadContext = null, // Shared context
            IsolationLevel = PluginIsolationLevel.Shared
        };

        return Task.FromResult(context);
    }

    private Task<LoadedPluginContext> LoadIsolatedPluginAsync<T>(string assemblyPath, string typeName) where T : class, IPlugin
    {
        var contextName = $"Plugin_{Path.GetFileNameWithoutExtension(assemblyPath)}_{Guid.NewGuid():N}";
        var loadContext = new PluginAssemblyLoadContext(contextName, assemblyPath, isCollectible: true);

        try
        {
            var assembly = loadContext.LoadFromAssemblyPath(assemblyPath);
            var type = assembly.GetType(typeName) ?? throw new PluginLoadException($"Type {typeName} not found in assembly {assemblyPath}");

            if (!typeof(T).IsAssignableFrom(type))
            {
                throw new PluginLoadException($"Type {typeName} does not implement {typeof(T).Name}");
            }

            var plugin = CreatePluginInstance<T>(type);

            // Note: Plugin initialization will be handled by the plugin manager with configuration

            var context = new LoadedPluginContext
            {
                Id = GeneratePluginId(assemblyPath, typeName),
                Plugin = plugin,
                LoadContext = loadContext,
                IsolationLevel = PluginIsolationLevel.Isolated
            };

            return Task.FromResult(context);
        }
        catch
        {
            // Cleanup on failure
            loadContext.Unload();
            throw;
        }
    }

    private async Task<LoadedPluginContext> LoadSandboxedPluginAsync<T>(string assemblyPath, string typeName) where T : class, IPlugin
    {
        // For now, sandboxed loading is the same as isolated
        // In a full implementation, this would apply additional security restrictions
        var context = await LoadIsolatedPluginAsync<T>(assemblyPath, typeName);
        return new LoadedPluginContext
        {
            Id = context.Id,
            Plugin = context.Plugin,
            LoadContext = context.LoadContext,
            IsolationLevel = PluginIsolationLevel.Sandboxed
        };
    }

    private static string GeneratePluginId(string assemblyPath, string typeName)
    {
        return $"{Path.GetFileName(assemblyPath)}::{typeName}";
    }

    private LoadedPluginContext? FindPluginContext(IPlugin plugin)
    {
        lock (_loadLock)
        {
            return _loadedPlugins.Values.FirstOrDefault(c => ReferenceEquals(c.Plugin, plugin));
        }
    }

    private static PluginInfo CreatePluginInfoFromContext(LoadedPluginContext context)
    {
        return new PluginInfo
        {
            Id = context.Id,
            TypeName = context.Plugin.GetType().FullName ?? context.Plugin.GetType().Name,
            AssemblyPath = context.Plugin.GetType().Assembly.Location,
            IsolationLevel = context.IsolationLevel,
            LoadedAt = DateTimeOffset.UtcNow, // This should be stored in context
            Status = PluginStatus.Loaded,
            ContextName = context.LoadContext?.Name
        };
    }

    private static async Task<IReadOnlyDictionary<string, object>?> ExtractPluginMetadataAsync(IPlugin plugin)
    {
        try
        {
            var metadata = new Dictionary<string, object>
            {
                ["AssemblyName"] = plugin.GetType().Assembly.GetName().Name ?? "Unknown",
                ["AssemblyVersion"] = plugin.GetType().Assembly.GetName().Version?.ToString() ?? "Unknown",
                ["TypeName"] = plugin.GetType().FullName ?? plugin.GetType().Name
            };

            // Get plugin-specific metadata if available
            if (plugin is IPluginMetadata metadataProvider)
            {
                var pluginMetadata = await metadataProvider.GetMetadataAsync();
                foreach (var kvp in pluginMetadata)
                {
                    metadata[kvp.Key] = kvp.Value;
                }
            }

            return metadata;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Creates a plugin instance with comprehensive dependency injection support.
    /// Supports injection of factory services, loggers, and other dependencies.
    /// </summary>
    private T CreatePluginInstance<T>(Type pluginType) where T : class, IPlugin
    {
        try
        {
            // Get all constructors ordered by parameter count (most specific first)
            var constructors = pluginType.GetConstructors()
                .OrderByDescending(c => c.GetParameters().Length)
                .ToArray();

            foreach (var constructor in constructors)
            {
                var parameters = constructor.GetParameters();
                var args = new object?[parameters.Length];
                bool canInstantiate = true;

                for (int i = 0; i < parameters.Length; i++)
                {
                    var paramType = parameters[i].ParameterType;
                    var arg = ResolveConstructorParameter(paramType, pluginType);

                    if (arg == null && !IsNullableParameter(parameters[i]))
                    {
                        canInstantiate = false;
                        break;
                    }

                    args[i] = arg;
                }

                if (canInstantiate)
                {
                    _logger.LogDebug("Creating plugin {PluginType} with {ParameterCount} dependencies",
                        pluginType.Name, parameters.Length);

                    return (T)(Activator.CreateInstance(pluginType, args) ??
                        throw new PluginLoadException($"Failed to create instance of {pluginType.Name}"));
                }
            }

            // If no constructor could be satisfied, provide detailed error
            var constructorInfo = string.Join("; ", constructors.Select(c =>
                $"({string.Join(", ", c.GetParameters().Select(p => p.ParameterType.Name))})"));

            throw new PluginLoadException(
                $"No suitable constructor found for plugin type {pluginType.Name}. " +
                $"Available constructors: {constructorInfo}. " +
                $"Plugin constructors should use supported dependency types: " +
                $"ISchemaFactory, IArrayRowFactory, IChunkFactory, IDatasetFactory, IDataTypeService, " +
                $"IMemoryManager, IPerformanceMonitor, IChannelTelemetry, ILogger, ILogger<T>");
        }
        catch (Exception ex) when (!(ex is PluginLoadException))
        {
            throw new PluginLoadException($"Failed to create instance of {pluginType.Name}: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Resolves a constructor parameter by type, providing appropriate dependencies.
    /// </summary>
    private object? ResolveConstructorParameter(Type parameterType, Type pluginType)
    {
        // Factory services
        if (parameterType == typeof(ISchemaFactory))
        {
            return _schemaFactory;
        }

        if (parameterType == typeof(IArrayRowFactory))
        {
            return _arrayRowFactory;
        }

        if (parameterType == typeof(IChunkFactory))
        {
            return _chunkFactory;
        }

        if (parameterType == typeof(IDatasetFactory))
        {
            return _datasetFactory;
        }

        if (parameterType == typeof(IDataTypeService))
        {
            return _dataTypeService;
        }

        // Monitoring and telemetry services
        if (parameterType == typeof(IMemoryManager))
        {
            return _memoryManager;
        }

        if (parameterType == typeof(IPerformanceMonitor))
        {
            return _performanceMonitor;
        }

        if (parameterType == typeof(IChannelTelemetry))
        {
            return _channelTelemetry;
        }

        // JavaScript services
        if (parameterType == typeof(IScriptEngineService))
        {
            return _scriptEngineService;
        }

        if (parameterType == typeof(IJavaScriptContextService))
        {
            return _javaScriptContextService;
        }

        // Logger services
        if (parameterType == typeof(ILogger))
        {
            return _logger;
        }

        if (parameterType.IsGenericType &&
            parameterType.GetGenericTypeDefinition() == typeof(ILogger<>))
        {
            var loggerType = parameterType.GetGenericArguments()[0];
            if (loggerType == pluginType)
            {
                // Create typed logger for the plugin
                var nullLoggerType = typeof(NullLogger<>).MakeGenericType(pluginType);
                return Activator.CreateInstance(nullLoggerType);
            }
        }

        // Return null for unsupported types - caller will check if parameter is optional
        return null;
    }

    /// <summary>
    /// Determines if a parameter is nullable (either reference type or Nullable&lt;T&gt;).
    /// </summary>
    private static bool IsNullableParameter(ParameterInfo parameter)
    {
        return !parameter.ParameterType.IsValueType ||
               Nullable.GetUnderlyingType(parameter.ParameterType) != null ||
               parameter.HasDefaultValue;
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(PluginLoader));
        }
    }
}

/// <summary>
/// Context information for a loaded plugin.
/// </summary>
internal sealed class LoadedPluginContext
{
    public required string Id { get; init; }
    public required IPlugin Plugin { get; init; }
    public AssemblyLoadContext? LoadContext { get; init; }
    public required PluginIsolationLevel IsolationLevel { get; init; }
    public DateTimeOffset LoadedAt { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// Custom AssemblyLoadContext for plugin isolation.
/// </summary>
internal sealed class PluginAssemblyLoadContext : AssemblyLoadContext
{
    private readonly string _pluginPath;
    private readonly string _pluginDirectory;

    public PluginAssemblyLoadContext(string name, string pluginPath, bool isCollectible = true)
        : base(name, isCollectible)
    {
        _pluginPath = pluginPath ?? throw new ArgumentNullException(nameof(pluginPath));
        _pluginDirectory = Path.GetDirectoryName(_pluginPath) ?? throw new ArgumentException("Invalid plugin path", nameof(pluginPath));

        // Subscribe to assembly resolve events for dependency resolution
        Resolving += OnResolving;
    }

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        // Allow loading of core FlowEngine assemblies from the host context
        if (IsFlowEngineAssembly(assemblyName))
        {
            return null; // Return null to defer to default context
        }

        // Try to load from plugin directory
        var assemblyPath = Path.Combine(_pluginDirectory, $"{assemblyName.Name}.dll");
        if (File.Exists(assemblyPath))
        {
            return LoadFromAssemblyPath(assemblyPath);
        }

        // For other assemblies, defer to the default context
        return null;
    }

    private Assembly? OnResolving(AssemblyLoadContext context, AssemblyName assemblyName)
    {
        // Try to resolve dependencies from the plugin directory
        var possiblePaths = new[]
        {
            Path.Combine(_pluginDirectory, $"{assemblyName.Name}.dll"),
            Path.Combine(_pluginDirectory, "lib", $"{assemblyName.Name}.dll"),
            Path.Combine(_pluginDirectory, "deps", $"{assemblyName.Name}.dll")
        };

        foreach (var path in possiblePaths)
        {
            if (File.Exists(path))
            {
                try
                {
                    return LoadFromAssemblyPath(path);
                }
                catch
                {
                    // Continue to next path
                }
            }
        }

        return null;
    }

    private static bool IsFlowEngineAssembly(AssemblyName assemblyName)
    {
        var name = assemblyName.Name;
        return name != null && (
            name.StartsWith("FlowEngine.Abstractions", StringComparison.OrdinalIgnoreCase) ||
            name.StartsWith("FlowEngine.Core", StringComparison.OrdinalIgnoreCase) ||
            name.StartsWith("System.", StringComparison.OrdinalIgnoreCase) ||
            name.StartsWith("Microsoft.", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("netstandard", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("mscorlib", StringComparison.OrdinalIgnoreCase)
        );
    }
}

/// <summary>
/// Interface for plugins that can provide metadata.
/// </summary>
public interface IPluginMetadata
{
    /// <summary>
    /// Gets metadata for this plugin.
    /// </summary>
    /// <returns>Plugin metadata</returns>
    Task<IReadOnlyDictionary<string, object>> GetMetadataAsync();
}
