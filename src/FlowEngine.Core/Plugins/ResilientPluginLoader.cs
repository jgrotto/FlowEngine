using FlowEngine.Abstractions.Plugins;
using Microsoft.Extensions.Logging;
using System.Reflection;

namespace FlowEngine.Core.Plugins;

/// <summary>
/// Enhanced plugin loader with retry mechanisms and detailed error context.
/// </summary>
public sealed class ResilientPluginLoader : IPluginLoader
{
    private readonly IPluginLoader _baseLoader;
    private readonly IRetryPolicy _retryPolicy;
    private readonly ILogger<ResilientPluginLoader> _logger;

    /// <summary>
    /// Initializes a new instance of the resilient plugin loader.
    /// </summary>
    public ResilientPluginLoader(
        IPluginLoader baseLoader,
        IRetryPolicy retryPolicy,
        ILogger<ResilientPluginLoader> logger)
    {
        _baseLoader = baseLoader ?? throw new ArgumentNullException(nameof(baseLoader));
        _retryPolicy = retryPolicy ?? throw new ArgumentNullException(nameof(retryPolicy));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        // Forward events from base loader
        _baseLoader.PluginLoaded += OnPluginLoaded;
        _baseLoader.PluginUnloaded += OnPluginUnloaded;
        _baseLoader.PluginLoadFailed += OnPluginLoadFailed;
    }

    /// <inheritdoc />
    public event EventHandler<PluginLoadedEventArgs>? PluginLoaded;

    /// <inheritdoc />
    public event EventHandler<PluginUnloadedEventArgs>? PluginUnloaded;

    /// <inheritdoc />
    public event EventHandler<PluginLoadFailedEventArgs>? PluginLoadFailed;

    /// <inheritdoc />
    public async Task<T> LoadPluginAsync<T>(
        string assemblyPath, 
        string typeName, 
        PluginIsolationLevel isolationLevel = PluginIsolationLevel.Isolated) where T : class, IPlugin
    {
        var context = ErrorContextBuilder.Create("LoadPlugin", "ResilientPluginLoader")
            .WithProperty("AssemblyPath", assemblyPath)
            .WithProperty("TypeName", typeName)
            .WithProperty("IsolationLevel", isolationLevel.ToString())
            .Build();

        try
        {
            // Pre-load validation
            await ValidatePluginLoadPreconditionsAsync(assemblyPath, typeName, context);

            // Execute with retry policy
            var retryContext = new RetryContext
            {
                OperationName = $"LoadPlugin:{Path.GetFileName(assemblyPath)}:{typeName}",
                Properties = new Dictionary<string, object>
                {
                    ["AssemblyPath"] = assemblyPath,
                    ["TypeName"] = typeName,
                    ["IsolationLevel"] = isolationLevel
                }
            };

            return await _retryPolicy.ExecuteAsync(async () =>
            {
                try
                {
                    _logger.LogDebug("Loading plugin {TypeName} from {AssemblyPath} with isolation {IsolationLevel}",
                        typeName, assemblyPath, isolationLevel);

                    var plugin = await _baseLoader.LoadPluginAsync<T>(assemblyPath, typeName, isolationLevel);

                    _logger.LogInformation("Successfully loaded plugin {TypeName} from {AssemblyPath}",
                        typeName, assemblyPath);

                    return plugin;
                }
                catch (Exception ex)
                {
                    var enhancedException = EnhancePluginLoadException(ex, context);
                    _logger.LogWarning(enhancedException,
                        "Plugin load attempt failed for {TypeName} from {AssemblyPath}: {ErrorMessage}",
                        typeName, assemblyPath, ex.Message);
                    throw enhancedException;
                }
            }, retryContext);
        }
        catch (OperationRetryExhaustedException retryEx)
        {
            _logger.LogError(retryEx,
                "Plugin {TypeName} failed to load after all retry attempts from {AssemblyPath}",
                typeName, assemblyPath);

            var finalException = new PluginLoadException(
                $"Failed to load plugin {typeName} after {retryEx.TotalAttempts} attempts over {retryEx.TotalDuration.TotalMilliseconds:F1}ms",
                retryEx.InnerException ?? retryEx)
            {
                AssemblyPath = assemblyPath,
                TypeName = typeName,
                IsolationLevel = isolationLevel,
                Context = new Dictionary<string, object>
                {
                    ["ErrorContext"] = context,
                    ["RetryAttempts"] = retryEx.TotalAttempts,
                    ["TotalDuration"] = retryEx.TotalDuration,
                    ["RetryContext"] = retryEx.Context
                }
            };

            throw finalException;
        }
        catch (Exception ex)
        {
            var enhancedException = EnhancePluginLoadException(ex, context);
            _logger.LogError(enhancedException,
                "Plugin {TypeName} failed to load from {AssemblyPath} with non-retryable error",
                typeName, assemblyPath);
            throw enhancedException;
        }
    }

    /// <inheritdoc />
    public Task UnloadPluginAsync(IPlugin plugin)
    {
        if (plugin == null)
            throw new ArgumentNullException(nameof(plugin));

        var context = ErrorContextBuilder.Create("UnloadPlugin", "ResilientPluginLoader")
            .WithProperty("PluginType", plugin.GetType().FullName ?? "Unknown")
            .Build();

        try
        {
            _logger.LogDebug("Unloading plugin {PluginType}", plugin.GetType().FullName);
            
            return _baseLoader.UnloadPluginAsync(plugin);
        }
        catch (Exception ex)
        {
            var enhancedException = new PluginLoadException(
                $"Failed to unload plugin {plugin.GetType().FullName}: {ex.Message}", ex)
            {
                TypeName = plugin.GetType().FullName ?? "Unknown",
                Context = new Dictionary<string, object>
                {
                    ["ErrorContext"] = context,
                    ["Operation"] = "Unload"
                }
            };

            _logger.LogError(enhancedException,
                "Failed to unload plugin {PluginType}", plugin.GetType().FullName);
            throw enhancedException;
        }
    }

    /// <inheritdoc />
    public IReadOnlyCollection<PluginInfo> GetLoadedPlugins()
    {
        try
        {
            return _baseLoader.GetLoadedPlugins();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving loaded plugins information");
            // Return empty collection rather than throwing to avoid breaking callers
            return Array.Empty<PluginInfo>();
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        try
        {
            _baseLoader.PluginLoaded -= OnPluginLoaded;
            _baseLoader.PluginUnloaded -= OnPluginUnloaded;
            _baseLoader.PluginLoadFailed -= OnPluginLoadFailed;

            _baseLoader.Dispose();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error disposing resilient plugin loader");
        }
    }

    /// <summary>
    /// Validates preconditions for plugin loading.
    /// </summary>
    private async Task ValidatePluginLoadPreconditionsAsync(string assemblyPath, string typeName, IErrorContext context)
    {
        // Validate assembly file exists and is accessible
        if (!File.Exists(assemblyPath))
        {
            throw new PluginLoadException($"Plugin assembly file not found: {assemblyPath}")
            {
                AssemblyPath = assemblyPath,
                TypeName = typeName,
                Context = new Dictionary<string, object>
                {
                    ["ErrorContext"] = context,
                    ["ValidationFailure"] = "AssemblyFileNotFound"
                }
            };
        }

        // Validate assembly accessibility
        try
        {
            using var fileStream = File.OpenRead(assemblyPath);
            // File is accessible
        }
        catch (UnauthorizedAccessException ex)
        {
            throw new PluginLoadException($"Access denied to plugin assembly: {assemblyPath}", ex)
            {
                AssemblyPath = assemblyPath,
                TypeName = typeName,
                Context = new Dictionary<string, object>
                {
                    ["ErrorContext"] = context,
                    ["ValidationFailure"] = "AssemblyAccessDenied"
                }
            };
        }
        catch (IOException ex)
        {
            throw new PluginLoadException($"I/O error accessing plugin assembly: {assemblyPath}", ex)
            {
                AssemblyPath = assemblyPath,
                TypeName = typeName,
                Context = new Dictionary<string, object>
                {
                    ["ErrorContext"] = context,
                    ["ValidationFailure"] = "AssemblyIOError"
                }
            };
        }

        // Validate assembly metadata without loading
        try
        {
            var assemblyName = AssemblyName.GetAssemblyName(assemblyPath);
            _logger.LogDebug("Plugin assembly metadata validated: {AssemblyName} v{Version}",
                assemblyName.Name, assemblyName.Version);
        }
        catch (BadImageFormatException ex)
        {
            throw new PluginLoadException($"Invalid assembly format: {assemblyPath}", ex)
            {
                AssemblyPath = assemblyPath,
                TypeName = typeName,
                Context = new Dictionary<string, object>
                {
                    ["ErrorContext"] = context,
                    ["ValidationFailure"] = "InvalidAssemblyFormat"
                }
            };
        }

        await Task.CompletedTask; // For future async validation
    }

    /// <summary>
    /// Enhances plugin load exceptions with additional context.
    /// </summary>
    private static PluginLoadException EnhancePluginLoadException(Exception originalException, IErrorContext context)
    {
        if (originalException is PluginLoadException existingPluginEx)
        {
            // Add context to existing plugin exception
            var enhancedContext = new Dictionary<string, object>(existingPluginEx.Context ?? new Dictionary<string, object>())
            {
                ["ErrorContext"] = context,
                ["EnhancedAt"] = DateTimeOffset.UtcNow
            };

            return new PluginLoadException(existingPluginEx.Message, existingPluginEx.InnerException ?? existingPluginEx)
            {
                AssemblyPath = existingPluginEx.AssemblyPath,
                TypeName = existingPluginEx.TypeName,
                IsolationLevel = existingPluginEx.IsolationLevel,
                Context = enhancedContext
            };
        }

        // Create new plugin exception with enhanced context
        var message = $"Plugin load failed: {originalException.Message}";
        return new PluginLoadException(message, originalException)
        {
            AssemblyPath = context.Properties.TryGetValue("AssemblyPath", out var path) ? path?.ToString() : null,
            TypeName = context.Properties.TryGetValue("TypeName", out var type) ? type?.ToString() : null,
            IsolationLevel = context.Properties.TryGetValue("IsolationLevel", out var isolation) 
                ? Enum.TryParse<PluginIsolationLevel>(isolation?.ToString(), out var level) ? level : null 
                : null,
            Context = new Dictionary<string, object>
            {
                ["ErrorContext"] = context,
                ["OriginalExceptionType"] = originalException.GetType().FullName ?? "Unknown",
                ["EnhancedAt"] = DateTimeOffset.UtcNow
            }
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
            PluginLoadFailed?.Invoke(this, e);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in plugin load failed event handler");
        }
    }
}