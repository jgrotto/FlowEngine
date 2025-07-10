using FlowEngine.Abstractions.Plugins;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace FlowEngine.Core.Lifecycle;

/// <summary>
/// Manages graceful shutdown of FlowEngine components with proper cleanup and resource disposal.
/// Ensures data integrity and prevents resource leaks during application termination.
/// </summary>
public sealed class GracefulShutdownService : IDisposable
{
    private readonly ILogger<GracefulShutdownService> _logger;
    private readonly List<IShutdownHandler> _shutdownHandlers = new();
    private readonly CancellationTokenSource _shutdownCancellation = new();
    private readonly object _lock = new();
    private readonly ShutdownConfiguration _configuration;
    private bool _disposed;
    private bool _shutdownInProgress;

    /// <summary>
    /// Event raised when shutdown is initiated.
    /// </summary>
    public event EventHandler<ShutdownInitiatedEventArgs>? ShutdownInitiated;

    /// <summary>
    /// Event raised when shutdown is completed.
    /// </summary>
    public event EventHandler<ShutdownCompletedEventArgs>? ShutdownCompleted;

    /// <summary>
    /// Gets whether shutdown is currently in progress.
    /// </summary>
    public bool IsShutdownInProgress => _shutdownInProgress;

    public GracefulShutdownService(ILogger<GracefulShutdownService> logger, ShutdownConfiguration? configuration = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _configuration = configuration ?? new ShutdownConfiguration();

        // Register for process exit events
        AppDomain.CurrentDomain.ProcessExit += OnProcessExit;
        Console.CancelKeyPress += OnCancelKeyPress;

        _logger.LogInformation("Graceful shutdown service initialized with timeout {Timeout}ms", 
            _configuration.ShutdownTimeout.TotalMilliseconds);
    }

    /// <summary>
    /// Registers a shutdown handler to be called during graceful shutdown.
    /// </summary>
    public void RegisterShutdownHandler(IShutdownHandler handler)
    {
        if (handler == null)
            throw new ArgumentNullException(nameof(handler));

        lock (_lock)
        {
            _shutdownHandlers.Add(handler);
        }

        _logger.LogDebug("Registered shutdown handler: {HandlerType}", handler.GetType().Name);
    }

    /// <summary>
    /// Registers a shutdown action with priority.
    /// </summary>
    public void RegisterShutdownAction(string name, Func<CancellationToken, Task> action, ShutdownPriority priority = ShutdownPriority.Normal)
    {
        if (string.IsNullOrEmpty(name))
            throw new ArgumentException("Name cannot be null or empty", nameof(name));
        if (action == null)
            throw new ArgumentNullException(nameof(action));

        var handler = new ActionShutdownHandler(name, action, priority);
        RegisterShutdownHandler(handler);
    }

    /// <summary>
    /// Initiates graceful shutdown of all registered components.
    /// </summary>
    public async Task InitiateShutdownAsync(string reason = "Manual shutdown", CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            if (_shutdownInProgress)
            {
                _logger.LogWarning("Shutdown already in progress, ignoring duplicate request");
                return;
            }
            _shutdownInProgress = true;
        }

        var stopwatch = Stopwatch.StartNew();
        
        try
        {
            _logger.LogInformation("Initiating graceful shutdown. Reason: {Reason}", reason);
            
            // Raise shutdown initiated event
            RaiseShutdownInitiated(reason);

            // Create combined cancellation token
            using var combinedCts = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken, _shutdownCancellation.Token);
            
            // Set timeout for the entire shutdown process
            combinedCts.CancelAfter(_configuration.ShutdownTimeout);

            // Group handlers by priority and execute in order
            var handlerGroups = _shutdownHandlers
                .GroupBy(h => h.Priority)
                .OrderByDescending(g => g.Key)
                .ToArray();

            var totalHandlers = _shutdownHandlers.Count;
            var completedHandlers = 0;
            var failedHandlers = new List<string>();

            _logger.LogInformation("Executing shutdown for {HandlerCount} handlers in {GroupCount} priority groups", 
                totalHandlers, handlerGroups.Length);

            foreach (var group in handlerGroups)
            {
                var priority = group.Key;
                var handlers = group.ToArray();
                
                _logger.LogInformation("Executing {HandlerCount} shutdown handlers with priority {Priority}", 
                    handlers.Length, priority);

                // Execute handlers in parallel within the same priority group
                var tasks = handlers.Select(handler => ExecuteHandlerSafelyAsync(handler, combinedCts.Token)).ToArray();
                
                try
                {
                    await Task.WhenAll(tasks);
                }
                catch (OperationCanceledException) when (combinedCts.Token.IsCancellationRequested)
                {
                    _logger.LogWarning("Shutdown timeout exceeded during priority {Priority} handlers", priority);
                    break;
                }

                // Check results
                for (int i = 0; i < tasks.Length; i++)
                {
                    var task = tasks[i];
                    var handler = handlers[i];
                    
                    if (task.IsCompletedSuccessfully)
                    {
                        completedHandlers++;
                        _logger.LogDebug("Handler {HandlerName} completed successfully", handler.Name);
                    }
                    else
                    {
                        failedHandlers.Add(handler.Name);
                        _logger.LogWarning("Handler {HandlerName} failed or was cancelled", handler.Name);
                    }
                }

                // Brief delay between priority groups to allow cleanup
                if (group != handlerGroups.Last())
                {
                    await Task.Delay(_configuration.InterGroupDelay, combinedCts.Token);
                }
            }

            stopwatch.Stop();
            
            var result = new ShutdownResult
            {
                Success = failedHandlers.Count == 0,
                TotalHandlers = totalHandlers,
                CompletedHandlers = completedHandlers,
                FailedHandlers = failedHandlers.ToArray(),
                Duration = stopwatch.Elapsed,
                Reason = reason
            };

            _logger.LogInformation("Graceful shutdown completed in {Duration}ms. Success: {Success}, Completed: {Completed}/{Total}, Failed: {Failed}", 
                stopwatch.ElapsedMilliseconds, result.Success, result.CompletedHandlers, result.TotalHandlers, result.FailedHandlers.Length);

            // Raise shutdown completed event
            RaiseShutdownCompleted(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during graceful shutdown");
            
            var errorResult = new ShutdownResult
            {
                Success = false,
                TotalHandlers = _shutdownHandlers.Count,
                CompletedHandlers = 0,
                FailedHandlers = new[] { "ShutdownService" },
                Duration = stopwatch.Elapsed,
                Reason = reason,
                ErrorMessage = ex.Message
            };
            
            RaiseShutdownCompleted(errorResult);
        }
        finally
        {
            lock (_lock)
            {
                _shutdownInProgress = false;
            }
        }
    }

    /// <summary>
    /// Forces immediate shutdown without waiting for handlers to complete.
    /// </summary>
    public void ForceShutdown(string reason = "Forced shutdown")
    {
        _logger.LogWarning("Forcing immediate shutdown. Reason: {Reason}", reason);
        
        _shutdownCancellation.Cancel();
        
        // Give a brief moment for cleanup
        Thread.Sleep(100);
        
        _logger.LogWarning("Force shutdown completed");
    }

    private async Task ExecuteHandlerSafelyAsync(IShutdownHandler handler, CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogDebug("Executing shutdown handler: {HandlerName}", handler.Name);
            
            var timeout = handler.Timeout ?? _configuration.HandlerTimeout;
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(timeout);
            
            await handler.ShutdownAsync(timeoutCts.Token);
            
            _logger.LogDebug("Handler {HandlerName} completed successfully", handler.Name);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning("Handler {HandlerName} was cancelled due to shutdown timeout", handler.Name);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Handler {HandlerName} failed during shutdown", handler.Name);
            
            if (_configuration.FailFastOnHandlerError)
            {
                throw;
            }
        }
    }

    private void OnProcessExit(object? sender, EventArgs e)
    {
        _logger.LogInformation("Process exit detected, initiating graceful shutdown");
        
        // Use a synchronous version for process exit since we can't await
        var shutdownTask = InitiateShutdownAsync("Process exit");
        
        try
        {
            shutdownTask.Wait(_configuration.ShutdownTimeout);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during process exit shutdown");
        }
    }

    private void OnCancelKeyPress(object? sender, ConsoleCancelEventArgs e)
    {
        _logger.LogInformation("Console cancel key pressed (Ctrl+C), initiating graceful shutdown");
        
        // Cancel the default behavior to allow graceful shutdown
        e.Cancel = true;
        
        // Initiate shutdown in background
        _ = Task.Run(async () =>
        {
            try
            {
                await InitiateShutdownAsync("Console cancel key");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during console cancel shutdown");
            }
            finally
            {
                // Exit after shutdown attempt
                Environment.Exit(0);
            }
        });
    }

    private void RaiseShutdownInitiated(string reason)
    {
        try
        {
            ShutdownInitiated?.Invoke(this, new ShutdownInitiatedEventArgs(reason));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error in shutdown initiated event handler");
        }
    }

    private void RaiseShutdownCompleted(ShutdownResult result)
    {
        try
        {
            ShutdownCompleted?.Invoke(this, new ShutdownCompletedEventArgs(result));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error in shutdown completed event handler");
        }
    }

    // Service lifecycle methods
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Graceful shutdown service started");
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Graceful shutdown service stopping");
        await InitiateShutdownAsync("Service.StopAsync", cancellationToken);
    }

    public void Dispose()
    {
        if (_disposed) return;

        try
        {
            // Unregister from process events
            AppDomain.CurrentDomain.ProcessExit -= OnProcessExit;
            Console.CancelKeyPress -= OnCancelKeyPress;
            
            // Dispose shutdown handlers
            lock (_lock)
            {
                foreach (var handler in _shutdownHandlers.OfType<IDisposable>())
                {
                    try
                    {
                        handler.Dispose();
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Error disposing shutdown handler");
                    }
                }
                _shutdownHandlers.Clear();
            }
            
            _shutdownCancellation?.Dispose();
            
            _logger.LogInformation("Graceful shutdown service disposed");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error during graceful shutdown service disposal");
        }
        finally
        {
            _disposed = true;
        }
    }
}

/// <summary>
/// Configuration for graceful shutdown behavior.
/// </summary>
public sealed class ShutdownConfiguration
{
    /// <summary>
    /// Gets or sets the maximum time to wait for all shutdown handlers to complete.
    /// </summary>
    public TimeSpan ShutdownTimeout { get; set; } = TimeSpan.FromSeconds(30);
    
    /// <summary>
    /// Gets or sets the timeout for individual shutdown handlers.
    /// </summary>
    public TimeSpan HandlerTimeout { get; set; } = TimeSpan.FromSeconds(10);
    
    /// <summary>
    /// Gets or sets the delay between executing different priority groups.
    /// </summary>
    public TimeSpan InterGroupDelay { get; set; } = TimeSpan.FromMilliseconds(100);
    
    /// <summary>
    /// Gets or sets whether to fail fast if any handler encounters an error.
    /// </summary>
    public bool FailFastOnHandlerError { get; set; } = false;
    
    /// <summary>
    /// Gets or sets whether to automatically register for process exit events.
    /// </summary>
    public bool RegisterProcessExitHandler { get; set; } = true;
    
    /// <summary>
    /// Gets or sets whether to automatically register for console cancel events.
    /// </summary>
    public bool RegisterConsoleCancelHandler { get; set; } = true;
}

/// <summary>
/// Interface for components that need to perform cleanup during shutdown.
/// </summary>
public interface IShutdownHandler
{
    /// <summary>
    /// Gets the name of the shutdown handler for logging purposes.
    /// </summary>
    string Name { get; }
    
    /// <summary>
    /// Gets the priority of this handler. Higher priority handlers are executed first.
    /// </summary>
    ShutdownPriority Priority { get; }
    
    /// <summary>
    /// Gets the timeout for this specific handler, or null to use the default.
    /// </summary>
    TimeSpan? Timeout { get; }
    
    /// <summary>
    /// Performs shutdown operations for this component.
    /// </summary>
    Task ShutdownAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Priority levels for shutdown handlers.
/// </summary>
public enum ShutdownPriority
{
    /// <summary>
    /// Low priority - cleanup tasks that can be safely interrupted.
    /// </summary>
    Low = 0,
    
    /// <summary>
    /// Normal priority - standard cleanup operations.
    /// </summary>
    Normal = 1,
    
    /// <summary>
    /// High priority - critical shutdown operations that must complete.
    /// </summary>
    High = 2,
    
    /// <summary>
    /// Critical priority - essential operations that must complete for data integrity.
    /// </summary>
    Critical = 3
}

/// <summary>
/// Result of a shutdown operation.
/// </summary>
public sealed class ShutdownResult
{
    /// <summary>
    /// Gets whether the shutdown completed successfully.
    /// </summary>
    public bool Success { get; set; }
    
    /// <summary>
    /// Gets the total number of shutdown handlers.
    /// </summary>
    public int TotalHandlers { get; set; }
    
    /// <summary>
    /// Gets the number of handlers that completed successfully.
    /// </summary>
    public int CompletedHandlers { get; set; }
    
    /// <summary>
    /// Gets the names of handlers that failed.
    /// </summary>
    public string[] FailedHandlers { get; set; } = Array.Empty<string>();
    
    /// <summary>
    /// Gets the total duration of the shutdown process.
    /// </summary>
    public TimeSpan Duration { get; set; }
    
    /// <summary>
    /// Gets the reason for the shutdown.
    /// </summary>
    public string Reason { get; set; } = string.Empty;
    
    /// <summary>
    /// Gets the error message if shutdown failed.
    /// </summary>
    public string? ErrorMessage { get; set; }
}

/// <summary>
/// Event arguments for shutdown initiated event.
/// </summary>
public sealed class ShutdownInitiatedEventArgs : EventArgs
{
    public string Reason { get; }
    public DateTimeOffset Timestamp { get; }
    
    public ShutdownInitiatedEventArgs(string reason)
    {
        Reason = reason;
        Timestamp = DateTimeOffset.UtcNow;
    }
}

/// <summary>
/// Event arguments for shutdown completed event.
/// </summary>
public sealed class ShutdownCompletedEventArgs : EventArgs
{
    public ShutdownResult Result { get; }
    
    public ShutdownCompletedEventArgs(ShutdownResult result)
    {
        Result = result;
    }
}

/// <summary>
/// Shutdown handler that wraps an action delegate.
/// </summary>
internal sealed class ActionShutdownHandler : IShutdownHandler
{
    private readonly Func<CancellationToken, Task> _action;

    public string Name { get; }
    public ShutdownPriority Priority { get; }
    public TimeSpan? Timeout { get; }

    public ActionShutdownHandler(string name, Func<CancellationToken, Task> action, ShutdownPriority priority, TimeSpan? timeout = null)
    {
        Name = name;
        _action = action;
        Priority = priority;
        Timeout = timeout;
    }

    public async Task ShutdownAsync(CancellationToken cancellationToken)
    {
        await _action(cancellationToken);
    }
}

/// <summary>
/// Shutdown handler for plugin managers.
/// </summary>
public sealed class PluginManagerShutdownHandler : IShutdownHandler
{
    private readonly IPluginManager _pluginManager;
    private readonly ILogger<PluginManagerShutdownHandler> _logger;

    public string Name => "PluginManager";
    public ShutdownPriority Priority => ShutdownPriority.High;
    public TimeSpan? Timeout => TimeSpan.FromSeconds(15);

    public PluginManagerShutdownHandler(IPluginManager pluginManager, ILogger<PluginManagerShutdownHandler> logger)
    {
        _pluginManager = pluginManager ?? throw new ArgumentNullException(nameof(pluginManager));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task ShutdownAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Shutting down plugin manager");
        
        try
        {
            var plugins = _pluginManager.GetAllPlugins();
            _logger.LogInformation("Unloading {PluginCount} plugins", plugins.Count);
            
            var unloadTasks = plugins.Select(plugin => _pluginManager.UnloadPluginAsync(plugin)).ToArray();
            await Task.WhenAll(unloadTasks);
            
            _logger.LogInformation("All plugins unloaded successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during plugin manager shutdown");
            throw;
        }
    }
}

/// <summary>
/// Extension methods for easier shutdown handler registration.
/// </summary>
public static class GracefulShutdownExtensions
{
    /// <summary>
    /// Registers a plugin manager for graceful shutdown.
    /// </summary>
    public static void RegisterPluginManager(this GracefulShutdownService shutdownService, IPluginManager pluginManager, ILogger<PluginManagerShutdownHandler> logger)
    {
        var handler = new PluginManagerShutdownHandler(pluginManager, logger);
        shutdownService.RegisterShutdownHandler(handler);
    }
    
    /// <summary>
    /// Registers a disposable object for shutdown cleanup.
    /// </summary>
    public static void RegisterDisposable(this GracefulShutdownService shutdownService, string name, IDisposable disposable, ShutdownPriority priority = ShutdownPriority.Normal)
    {
        shutdownService.RegisterShutdownAction(name, _ =>
        {
            disposable?.Dispose();
            return Task.CompletedTask;
        }, priority);
    }
    
    /// <summary>
    /// Registers an async disposable object for shutdown cleanup.
    /// </summary>
    public static void RegisterAsyncDisposable(this GracefulShutdownService shutdownService, string name, IAsyncDisposable asyncDisposable, ShutdownPriority priority = ShutdownPriority.Normal)
    {
        shutdownService.RegisterShutdownAction(name, async cancellationToken =>
        {
            if (asyncDisposable != null)
            {
                await asyncDisposable.DisposeAsync();
            }
        }, priority);
    }
}