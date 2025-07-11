using FlowEngine.Abstractions.Data;
using System.Collections.Immutable;
using Microsoft.Extensions.Logging;

namespace FlowEngine.Abstractions.Plugins;

/// <summary>
/// Abstract base class for FlowEngine plugins implementing the five-component architecture.
/// Provides common functionality and lifecycle management for plugin development.
/// </summary>
public abstract class PluginBase : IPlugin
{
    private readonly ILogger _logger;
    private PluginState _state = PluginState.Created;
    private readonly object _stateLock = new();
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the PluginBase class.
    /// </summary>
    /// <param name="logger">Logger instance for this plugin</param>
    protected PluginBase(ILogger logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        Id = Guid.NewGuid().ToString();
    }

    /// <inheritdoc />
    public virtual string Id { get; protected set; }

    /// <inheritdoc />
    public abstract string Name { get; }

    /// <inheritdoc />
    public abstract string Version { get; }

    /// <inheritdoc />
    public abstract string Description { get; }

    /// <inheritdoc />
    public abstract string Author { get; }

    /// <inheritdoc />
    public abstract string PluginType { get; }

    /// <inheritdoc />
    public PluginState State
    {
        get
        {
            lock (_stateLock)
            {
                return _state;
            }
        }
        protected set
        {
            PluginState previousState;
            lock (_stateLock)
            {
                previousState = _state;
                _state = value;
            }

            if (previousState != value)
            {
                OnStateChanged(previousState, value);
            }
        }
    }

    /// <inheritdoc />
    public abstract IPluginConfiguration Configuration { get; protected set; }

    /// <inheritdoc />
    public virtual ImmutableDictionary<string, object>? Metadata { get; protected set; }

    /// <inheritdoc />
    public abstract bool SupportsHotSwapping { get; }

    /// <inheritdoc />
    public abstract ISchema? OutputSchema { get; }

    /// <summary>
    /// Gets the logger instance for this plugin.
    /// </summary>
    protected ILogger Logger => _logger;

    /// <inheritdoc />
    public virtual async Task<PluginInitializationResult> InitializeAsync(IPluginConfiguration configuration, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        if (State != PluginState.Created)
        {
            return new PluginInitializationResult
            {
                Success = false,
                Message = $"Plugin is in {State} state and cannot be initialized",
                InitializationTime = TimeSpan.Zero,
                Errors = ImmutableArray.Create("Plugin already initialized")
            };
        }

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            _logger.LogDebug("Initializing plugin {PluginName} with configuration", Name);

            var result = await InitializeInternalAsync(configuration, cancellationToken);

            if (result.Success)
            {
                Configuration = configuration;
                State = PluginState.Initialized;
                _logger.LogInformation("Plugin {PluginName} initialized successfully in {ElapsedMs}ms", Name, stopwatch.ElapsedMilliseconds);
            }
            else
            {
                State = PluginState.Failed;
                _logger.LogError("Plugin {PluginName} initialization failed: {Message}", Name, result.Message);
            }

            return result with { InitializationTime = stopwatch.Elapsed };
        }
        catch (Exception ex)
        {
            State = PluginState.Failed;
            _logger.LogError(ex, "Plugin {PluginName} initialization failed with exception", Name);

            return new PluginInitializationResult
            {
                Success = false,
                Message = $"Initialization failed: {ex.Message}",
                InitializationTime = stopwatch.Elapsed,
                Errors = ImmutableArray.Create(ex.Message)
            };
        }
    }

    /// <inheritdoc />
    public virtual async Task StartAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        if (State != PluginState.Initialized)
        {
            throw new InvalidOperationException($"Plugin must be initialized before starting. Current state: {State}");
        }

        try
        {
            _logger.LogDebug("Starting plugin {PluginName}", Name);

            await StartInternalAsync(cancellationToken);

            State = PluginState.Running;
            _logger.LogInformation("Plugin {PluginName} started successfully", Name);
        }
        catch (Exception ex)
        {
            State = PluginState.Failed;
            _logger.LogError(ex, "Plugin {PluginName} start failed", Name);
            throw;
        }
    }

    /// <inheritdoc />
    public virtual async Task StopAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        if (State != PluginState.Running)
        {
            _logger.LogWarning("Plugin {PluginName} is not running (state: {State})", Name, State);
            return;
        }

        try
        {
            _logger.LogDebug("Stopping plugin {PluginName}", Name);

            await StopInternalAsync(cancellationToken);

            State = PluginState.Stopped;
            _logger.LogInformation("Plugin {PluginName} stopped successfully", Name);
        }
        catch (Exception ex)
        {
            State = PluginState.Failed;
            _logger.LogError(ex, "Plugin {PluginName} stop failed", Name);
            throw;
        }
    }

    /// <inheritdoc />
    public virtual async Task<HotSwapResult> HotSwapAsync(IPluginConfiguration newConfiguration, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        if (!SupportsHotSwapping)
        {
            return new HotSwapResult
            {
                Success = false,
                Message = "Plugin does not support hot-swapping",
                SwapTime = TimeSpan.Zero,
                Errors = ImmutableArray.Create("Hot-swapping not supported")
            };
        }

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            _logger.LogDebug("Performing hot-swap for plugin {PluginName}", Name);

            var result = await HotSwapInternalAsync(newConfiguration, cancellationToken);

            if (result.Success)
            {
                Configuration = newConfiguration;
                _logger.LogInformation("Plugin {PluginName} hot-swapped successfully in {ElapsedMs}ms", Name, stopwatch.ElapsedMilliseconds);
            }
            else
            {
                _logger.LogError("Plugin {PluginName} hot-swap failed: {Message}", Name, result.Message);
            }

            return result with { SwapTime = stopwatch.Elapsed };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Plugin {PluginName} hot-swap failed with exception", Name);

            return new HotSwapResult
            {
                Success = false,
                Message = $"Hot-swap failed: {ex.Message}",
                SwapTime = stopwatch.Elapsed,
                Errors = ImmutableArray.Create(ex.Message)
            };
        }
    }

    /// <inheritdoc />
    public virtual async Task<IEnumerable<HealthCheck>> HealthCheckAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        var healthChecks = new List<HealthCheck>
        {
            new HealthCheck
            {
                Name = "Plugin State",
                Passed = State == PluginState.Running || State == PluginState.Initialized,
                Details = $"Current state: {State}",
                ExecutionTime = TimeSpan.Zero,
                Severity = State == PluginState.Failed ? HealthCheckSeverity.Critical : HealthCheckSeverity.Info
            }
        };

        var additionalChecks = await PerformAdditionalHealthChecksAsync(cancellationToken);
        healthChecks.AddRange(additionalChecks);

        return healthChecks;
    }

    /// <inheritdoc />
    public virtual ValidationResult ValidateInputSchema(ISchema? inputSchema)
    {
        // Default implementation - subclasses can override for specific validation
        return ValidationResult.Success();
    }

    /// <inheritdoc />
    public event EventHandler<PluginStateChangedEventArgs>? StateChanged;

    /// <summary>
    /// Performs plugin-specific initialization logic.
    /// </summary>
    /// <param name="configuration">Plugin configuration</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Initialization result</returns>
    protected abstract Task<PluginInitializationResult> InitializeInternalAsync(IPluginConfiguration configuration, CancellationToken cancellationToken);

    /// <summary>
    /// Performs plugin-specific start logic.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Task representing the start operation</returns>
    protected abstract Task StartInternalAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Performs plugin-specific stop logic.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Task representing the stop operation</returns>
    protected abstract Task StopInternalAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Performs plugin-specific hot-swap logic.
    /// </summary>
    /// <param name="newConfiguration">New configuration</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Hot-swap result</returns>
    protected virtual Task<HotSwapResult> HotSwapInternalAsync(IPluginConfiguration newConfiguration, CancellationToken cancellationToken)
    {
        return Task.FromResult(new HotSwapResult
        {
            Success = false,
            Message = "Hot-swap not implemented",
            SwapTime = TimeSpan.Zero,
            Errors = ImmutableArray.Create("Hot-swap not implemented")
        });
    }

    /// <summary>
    /// Performs additional plugin-specific health checks.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Additional health check results</returns>
    protected virtual Task<IEnumerable<HealthCheck>> PerformAdditionalHealthChecksAsync(CancellationToken cancellationToken)
    {
        return Task.FromResult(Enumerable.Empty<HealthCheck>());
    }

    /// <summary>
    /// Called when the plugin state changes.
    /// </summary>
    /// <param name="previousState">Previous state</param>
    /// <param name="currentState">Current state</param>
    protected virtual void OnStateChanged(PluginState previousState, PluginState currentState)
    {
        StateChanged?.Invoke(this, new PluginStateChangedEventArgs
        {
            PreviousState = previousState,
            CurrentState = currentState
        });
    }

    /// <summary>
    /// Throws an exception if the plugin has been disposed.
    /// </summary>
    protected void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(GetType().Name);
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Disposes the plugin resources.
    /// </summary>
    /// <param name="disposing">Whether disposing is being called from Dispose method</param>
    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed && disposing)
        {
            try
            {
                if (State == PluginState.Running)
                {
                    StopAsync().GetAwaiter().GetResult();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error stopping plugin {PluginName} during disposal", Name);
            }
            finally
            {
                State = PluginState.Disposed;
                _disposed = true;
            }
        }
    }
}
