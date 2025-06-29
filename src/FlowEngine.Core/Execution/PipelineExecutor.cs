using FlowEngine.Abstractions;
using FlowEngine.Abstractions.Channels;
using FlowEngine.Abstractions.Configuration;
using FlowEngine.Abstractions.Execution;
using FlowEngine.Abstractions.Plugins;
using FlowEngine.Core.Channels;
using System.Collections.Concurrent;
using System.Diagnostics;

namespace FlowEngine.Core.Execution;

/// <summary>
/// High-performance pipeline executor with DAG-based dependency management and parallel processing.
/// Orchestrates plugin execution, data flow, and resource management across the pipeline.
/// </summary>
public sealed class PipelineExecutor : IPipelineExecutor
{
    private readonly IPluginManager _pluginManager;
    private readonly IDagAnalyzer _dagAnalyzer;
    private readonly ConcurrentDictionary<string, IPlugin> _loadedPlugins = new();
    private readonly ConcurrentDictionary<string, IDataChannel<IChunk>> _channels = new();
    private readonly ConcurrentDictionary<string, PluginExecutionMetrics> _pluginMetrics = new();
    private readonly CancellationTokenSource _cancellationTokenSource = new();
    private readonly object _statusLock = new();

    private PipelineExecutionStatus _status = PipelineExecutionStatus.Idle;
    private IPipelineConfiguration? _currentConfiguration;
    private readonly Stopwatch _executionStopwatch = new();
    private bool _disposed;

    /// <summary>
    /// Initializes a new pipeline executor with the specified dependencies.
    /// </summary>
    /// <param name="pluginManager">Plugin manager for loading and managing plugins</param>
    /// <param name="dagAnalyzer">DAG analyzer for dependency management</param>
    public PipelineExecutor(IPluginManager pluginManager, IDagAnalyzer dagAnalyzer)
    {
        _pluginManager = pluginManager ?? throw new ArgumentNullException(nameof(pluginManager));
        _dagAnalyzer = dagAnalyzer ?? throw new ArgumentNullException(nameof(dagAnalyzer));
    }

    /// <summary>
    /// Initializes a new pipeline executor with default dependencies.
    /// </summary>
    public PipelineExecutor() : this(new Plugins.PluginManager(), new DagAnalyzer())
    {
    }

    /// <inheritdoc />
    public PipelineExecutionStatus Status
    {
        get
        {
            lock (_statusLock)
            {
                return _status;
            }
        }
    }

    /// <inheritdoc />
    public PipelineExecutionMetrics Metrics => BuildCurrentMetrics();

    /// <inheritdoc />
    public event EventHandler<PipelineStatusChangedEventArgs>? StatusChanged;

    /// <inheritdoc />
#pragma warning disable CS0067 // Event is never used - part of public interface
    public event EventHandler<PluginChunkProcessedEventArgs>? PluginChunkProcessed;
#pragma warning restore CS0067

    /// <inheritdoc />
    public event EventHandler<PipelineErrorEventArgs>? PipelineError;

    /// <inheritdoc />
    public event EventHandler<PipelineCompletedEventArgs>? PipelineCompleted;

    /// <inheritdoc />
    public async Task<PipelineExecutionResult> ExecuteAsync(IPipelineConfiguration configuration, CancellationToken cancellationToken = default)
    {
        if (configuration == null)
            throw new ArgumentNullException(nameof(configuration));

        ThrowIfDisposed();

        // Check if already running
        lock (_statusLock)
        {
            if (_status == PipelineExecutionStatus.Running)
                throw new PipelineExecutionException("Pipeline is already running");
        }

        _currentConfiguration = configuration;
        var linkedToken = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _cancellationTokenSource.Token).Token;

        try
        {
            SetStatus(PipelineExecutionStatus.Initializing);
            _executionStopwatch.Restart();

            // Validate pipeline
            var validation = await ValidatePipelineAsync(configuration);
            if (!validation.IsValid)
            {
                var errors = validation.Errors.Select(e => new PipelineError
                {
                    PluginName = "Pipeline",
                    Message = e,
                    IsRecoverable = false
                }).ToArray();

                SetStatus(PipelineExecutionStatus.Failed);
                return CreateFailedResult(errors);
            }

            // Load and initialize plugins
            await LoadPluginsAsync(configuration, linkedToken);

            // Create channels
            await CreateChannelsAsync(configuration, linkedToken);

            // Execute pipeline
            SetStatus(PipelineExecutionStatus.Running);
            var result = await ExecutePipelineAsync(validation.ExecutionOrder, linkedToken);

            _executionStopwatch.Stop();
            SetStatus(result.IsSuccess ? PipelineExecutionStatus.Completed : PipelineExecutionStatus.Failed);

            // Raise completion event
            try
            {
                PipelineCompleted?.Invoke(this, new PipelineCompletedEventArgs(result));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Pipeline completed event handler failed: {ex}");
            }

            return result;
        }
        catch (OperationCanceledException) when (linkedToken.IsCancellationRequested)
        {
            _executionStopwatch.Stop();
            SetStatus(PipelineExecutionStatus.Cancelled);

            return new PipelineExecutionResult
            {
                IsSuccess = false,
                ExecutionTime = _executionStopwatch.Elapsed,
                TotalRowsProcessed = GetTotalRowsProcessed(),
                TotalChunksProcessed = GetTotalChunksProcessed(),
                FinalStatus = PipelineExecutionStatus.Cancelled,
                PluginMetrics = _pluginMetrics.ToDictionary(kvp => kvp.Key, kvp => kvp.Value)
            };
        }
        catch (Exception ex)
        {
            _executionStopwatch.Stop();
            SetStatus(PipelineExecutionStatus.Failed);

            var error = new PipelineError
            {
                PluginName = "Pipeline",
                Message = ex.Message,
                Exception = ex,
                IsRecoverable = false
            };

            try
            {
                PipelineError?.Invoke(this, new PipelineErrorEventArgs(error));
            }
            catch
            {
                // Ignore event handler errors
            }

            throw new PipelineExecutionException($"Pipeline execution failed: {ex.Message}", ex)
            {
                PipelineName = configuration.Name,
                ExecutionContext = new Dictionary<string, object>
                {
                    ["ExecutionTime"] = _executionStopwatch.Elapsed,
                    ["Status"] = _status,
                    ["LoadedPlugins"] = _loadedPlugins.Keys.ToArray()
                }
            };
        }
        finally
        {
            await CleanupAsync();
        }
    }

    /// <inheritdoc />
    public async Task<PipelineValidationResult> ValidatePipelineAsync(IPipelineConfiguration configuration)
    {
        if (configuration == null)
            throw new ArgumentNullException(nameof(configuration));

        ThrowIfDisposed();

        try
        {
            var errors = new List<string>();
            var warnings = new List<string>();

            // Validate basic configuration
            if (string.IsNullOrWhiteSpace(configuration.Name))
                errors.Add("Pipeline name is required");

            if (configuration.Plugins.Count == 0)
                errors.Add("Pipeline must contain at least one plugin");

            // Validate DAG structure
            var dagValidation = _dagAnalyzer.ValidateDag(configuration);
            errors.AddRange(dagValidation.Errors);
            warnings.AddRange(dagValidation.Warnings);

            if (errors.Count > 0)
                return PipelineValidationResult.Failure(errors.ToArray());

            // Get execution order and schema flow
            var dagAnalysis = _dagAnalyzer.AnalyzePipeline(configuration);
            var schemaFlow = await AnalyzeSchemaFlowAsync(configuration, dagAnalysis.ExecutionOrder);

            return PipelineValidationResult.Success(dagAnalysis.ExecutionOrder, schemaFlow, warnings.ToArray());
        }
        catch (Exception ex)
        {
            return PipelineValidationResult.Failure($"Pipeline validation failed: {ex.Message}");
        }
    }

    /// <inheritdoc />
    public async Task PauseAsync()
    {
        ThrowIfDisposed();

        lock (_statusLock)
        {
            if (_status != PipelineExecutionStatus.Running)
                return;
        }

        SetStatus(PipelineExecutionStatus.Paused);
        await Task.CompletedTask; // Placeholder for pause implementation
    }

    /// <inheritdoc />
    public async Task ResumeAsync()
    {
        ThrowIfDisposed();

        lock (_statusLock)
        {
            if (_status != PipelineExecutionStatus.Paused)
                return;
        }

        SetStatus(PipelineExecutionStatus.Running);
        await Task.CompletedTask; // Placeholder for resume implementation
    }

    /// <inheritdoc />
    public async Task StopAsync(TimeSpan? timeout = null)
    {
        ThrowIfDisposed();

        var effectiveTimeout = timeout ?? TimeSpan.FromSeconds(30);

        lock (_statusLock)
        {
            if (_status == PipelineExecutionStatus.Idle || 
                _status == PipelineExecutionStatus.Completed ||
                _status == PipelineExecutionStatus.Failed ||
                _status == PipelineExecutionStatus.Cancelled)
            {
                return;
            }
        }

        SetStatus(PipelineExecutionStatus.Stopping);

        try
        {
            _cancellationTokenSource.Cancel();

            // Wait for graceful shutdown
            using var timeoutCts = new CancellationTokenSource(effectiveTimeout);
            await Task.Delay(100, timeoutCts.Token); // Brief delay to allow cleanup

            await CleanupAsync();
        }
        catch (OperationCanceledException)
        {
            // Timeout occurred, force cleanup
            await CleanupAsync();
        }
        finally
        {
            SetStatus(PipelineExecutionStatus.Cancelled);
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;

        try
        {
            await StopAsync(TimeSpan.FromSeconds(10));
            await CleanupAsync();

            _cancellationTokenSource.Dispose();
            _pluginManager?.Dispose();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error during pipeline executor disposal: {ex}");
        }
        finally
        {
            _disposed = true;
        }
    }

    private async Task LoadPluginsAsync(IPipelineConfiguration configuration, CancellationToken cancellationToken)
    {
        var loadTasks = configuration.Plugins.Select(async pluginConfig =>
        {
            cancellationToken.ThrowIfCancellationRequested();

            var plugin = await _pluginManager.LoadPluginAsync(pluginConfig);
            _loadedPlugins[pluginConfig.Name] = plugin;

            // Initialize plugin metrics
            _pluginMetrics[pluginConfig.Name] = new PluginExecutionMetrics
            {
                PluginName = pluginConfig.Name
            };

            return plugin;
        });

        await Task.WhenAll(loadTasks);
    }

    private async Task CreateChannelsAsync(IPipelineConfiguration configuration, CancellationToken cancellationToken)
    {
        foreach (var connection in configuration.Connections)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var channelId = $"{connection.From}→{connection.To}";
            var channelConfig = connection.Channel ?? configuration.Settings.DefaultChannel ?? CreateDefaultChannelConfiguration();

            var channel = new DataChannel<IChunk>(channelId, channelConfig);
            _channels[channelId] = channel;
        }

        await Task.CompletedTask;
    }

    private async Task<PipelineExecutionResult> ExecutePipelineAsync(IReadOnlyList<string> executionOrder, CancellationToken cancellationToken)
    {
        var errors = new List<PipelineError>();
        var totalRowsProcessed = 0L;
        var totalChunksProcessed = 0L;

        try
        {
            // Start all plugins in execution order
            var pluginTasks = new List<Task>();

            foreach (var pluginName in executionOrder)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (!_loadedPlugins.TryGetValue(pluginName, out var plugin))
                {
                    errors.Add(new PipelineError
                    {
                        PluginName = pluginName,
                        Message = "Plugin not loaded",
                        IsRecoverable = false
                    });
                    continue;
                }

                var task = ExecutePluginAsync(plugin, pluginName, cancellationToken);
                pluginTasks.Add(task);
            }

            // Wait for all plugins to complete
            await Task.WhenAll(pluginTasks);

            // Calculate totals
            totalRowsProcessed = _pluginMetrics.Values.Sum(m => m.RowsProcessed);
            totalChunksProcessed = _pluginMetrics.Values.Sum(m => m.ChunksProcessed);

            return new PipelineExecutionResult
            {
                IsSuccess = errors.Count == 0,
                ExecutionTime = _executionStopwatch.Elapsed,
                TotalRowsProcessed = totalRowsProcessed,
                TotalChunksProcessed = totalChunksProcessed,
                Errors = errors,
                PluginMetrics = _pluginMetrics.ToDictionary(kvp => kvp.Key, kvp => kvp.Value),
                FinalStatus = errors.Count == 0 ? PipelineExecutionStatus.Completed : PipelineExecutionStatus.Failed
            };
        }
        catch (Exception ex)
        {
            errors.Add(new PipelineError
            {
                PluginName = "Pipeline",
                Message = ex.Message,
                Exception = ex,
                IsRecoverable = false
            });

            return CreateFailedResult(errors);
        }
    }

    private async Task ExecutePluginAsync(IPlugin plugin, string pluginName, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var chunksProcessed = 0L;
        var rowsProcessed = 0L;
        var errorCount = 0;

        try
        {
            // Plugin execution logic would go here
            // For now, this is a placeholder that simulates plugin execution

            switch (plugin)
            {
                case ISourcePlugin sourcePlugin:
                    await ExecuteSourcePluginAsync(sourcePlugin, pluginName, cancellationToken);
                    break;

                case ITransformPlugin transformPlugin:
                    await ExecuteTransformPluginAsync(transformPlugin, pluginName, cancellationToken);
                    break;

                case ISinkPlugin sinkPlugin:
                    await ExecuteSinkPluginAsync(sinkPlugin, pluginName, cancellationToken);
                    break;

                default:
                    throw new PipelineExecutionException($"Unknown plugin type: {plugin.GetType().Name}");
            }
        }
        catch (Exception ex)
        {
            errorCount++;

            var error = new PipelineError
            {
                PluginName = pluginName,
                Message = ex.Message,
                Exception = ex,
                IsRecoverable = ex is not PipelineExecutionException
            };

            try
            {
                PipelineError?.Invoke(this, new PipelineErrorEventArgs(error));
            }
            catch
            {
                // Ignore event handler errors
            }

            throw;
        }
        finally
        {
            stopwatch.Stop();

            // Update plugin metrics
            _pluginMetrics[pluginName] = new PluginExecutionMetrics
            {
                PluginName = pluginName,
                ExecutionTime = stopwatch.Elapsed,
                ChunksProcessed = chunksProcessed,
                RowsProcessed = rowsProcessed,
                ErrorCount = errorCount
            };
        }
    }

    private async Task ExecuteSourcePluginAsync(ISourcePlugin sourcePlugin, string pluginName, CancellationToken cancellationToken)
    {
        // Placeholder implementation for source plugin execution
        await Task.Delay(100, cancellationToken); // Simulate work
    }

    private async Task ExecuteTransformPluginAsync(ITransformPlugin transformPlugin, string pluginName, CancellationToken cancellationToken)
    {
        // Placeholder implementation for transform plugin execution
        await Task.Delay(100, cancellationToken); // Simulate work
    }

    private async Task ExecuteSinkPluginAsync(ISinkPlugin sinkPlugin, string pluginName, CancellationToken cancellationToken)
    {
        // Placeholder implementation for sink plugin execution
        await Task.Delay(100, cancellationToken); // Simulate work
    }

    private Task<IReadOnlyList<SchemaFlowStep>> AnalyzeSchemaFlowAsync(IPipelineConfiguration configuration, IReadOnlyList<string> executionOrder)
    {
        var schemaFlow = new List<SchemaFlowStep>();

        // Placeholder implementation for schema flow analysis
        foreach (var pluginName in executionOrder)
        {
            var pluginConfig = configuration.Plugins.First(p => p.Name == pluginName);
            var inputSchema = pluginConfig.Schema?.ToSchema();

            schemaFlow.Add(new SchemaFlowStep
            {
                PluginName = pluginName,
                InputSchema = inputSchema,
                OutputSchema = inputSchema, // Placeholder - would analyze actual schema transformations
                Description = $"Schema flow through {pluginName}"
            });
        }

        return Task.FromResult<IReadOnlyList<SchemaFlowStep>>(schemaFlow);
    }

    private PipelineExecutionResult CreateFailedResult(IReadOnlyList<PipelineError> errors)
    {
        return new PipelineExecutionResult
        {
            IsSuccess = false,
            ExecutionTime = _executionStopwatch.Elapsed,
            TotalRowsProcessed = GetTotalRowsProcessed(),
            TotalChunksProcessed = GetTotalChunksProcessed(),
            Errors = errors,
            PluginMetrics = _pluginMetrics.ToDictionary(kvp => kvp.Key, kvp => kvp.Value),
            FinalStatus = PipelineExecutionStatus.Failed
        };
    }

    private PipelineExecutionMetrics BuildCurrentMetrics()
    {
        return new PipelineExecutionMetrics
        {
            TotalExecutionTime = _executionStopwatch.Elapsed,
            TotalRowsProcessed = GetTotalRowsProcessed(),
            TotalChunksProcessed = GetTotalChunksProcessed(),
            ErrorCount = _pluginMetrics.Values.Sum(m => m.ErrorCount),
            PluginMetrics = _pluginMetrics.ToDictionary(kvp => kvp.Key, kvp => kvp.Value),
            ChannelMetrics = new Dictionary<string, ChannelExecutionMetrics>() // Placeholder
        };
    }

    private long GetTotalRowsProcessed()
    {
        return _pluginMetrics.Values.Sum(m => m.RowsProcessed);
    }

    private long GetTotalChunksProcessed()
    {
        return _pluginMetrics.Values.Sum(m => m.ChunksProcessed);
    }

    private async Task CleanupAsync()
    {
        // Unload plugins
        var unloadTasks = _loadedPlugins.Values.Select(async plugin =>
        {
            try
            {
                await _pluginManager.UnloadPluginAsync(plugin);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error unloading plugin: {ex}");
            }
        });

        await Task.WhenAll(unloadTasks);
        _loadedPlugins.Clear();

        // Dispose channels
        var disposeTasks = _channels.Values.Select(async channel =>
        {
            try
            {
                await channel.DisposeAsync();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error disposing channel: {ex}");
            }
        });

        await Task.WhenAll(disposeTasks);
        _channels.Clear();

        _pluginMetrics.Clear();
    }

    private void SetStatus(PipelineExecutionStatus newStatus, string? message = null)
    {
        PipelineExecutionStatus oldStatus;

        lock (_statusLock)
        {
            if (_status == newStatus)
                return;

            oldStatus = _status;
            _status = newStatus;
        }

        try
        {
            StatusChanged?.Invoke(this, new PipelineStatusChangedEventArgs(oldStatus, newStatus, message));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Status changed event handler failed: {ex}");
        }
    }

    private static IChannelConfiguration CreateDefaultChannelConfiguration()
    {
        return new DefaultChannelConfiguration();
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(PipelineExecutor));
    }
}

/// <summary>
/// Default channel configuration implementation.
/// </summary>
internal sealed class DefaultChannelConfiguration : IChannelConfiguration
{
    public int BufferSize => 1000;
    public int BackpressureThreshold => 80;
    public ChannelFullMode FullMode => ChannelFullMode.Wait;
    public TimeSpan Timeout => TimeSpan.FromSeconds(30);
}