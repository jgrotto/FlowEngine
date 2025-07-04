using FlowEngine.Abstractions;
using FlowEngine.Abstractions.Channels;
using FlowEngine.Abstractions.Configuration;
using FlowEngine.Abstractions.Data;
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
    private readonly ConcurrentDictionary<string, MutablePluginMetrics> _pluginMetrics = new();
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

            // Set up schemas for all plugins
            await SetupSchemasAsync(configuration, validation.ExecutionOrder, linkedToken);

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
                PluginMetrics = _pluginMetrics.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.ToImmutable())
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

            // TODO: Fix interface mismatch in Sprint 1 - proper plugin configuration needed
            var plugin = await _pluginManager.LoadPluginAsync((FlowEngine.Abstractions.Plugins.IPluginConfiguration)pluginConfig);
            _loadedPlugins[pluginConfig.Name] = plugin;

            // Initialize plugin metrics
            _pluginMetrics[pluginConfig.Name] = new MutablePluginMetrics
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

    private async Task SetupSchemasAsync(IPipelineConfiguration configuration, IReadOnlyList<string> executionOrder, CancellationToken cancellationToken)
    {
        var schemaMap = new Dictionary<string, ISchema>();
        
        // Process plugins in execution order to propagate schemas
        foreach (var pluginName in executionOrder)
        {
            cancellationToken.ThrowIfCancellationRequested();
            
            if (!_loadedPlugins.TryGetValue(pluginName, out var plugin))
                continue;
                
            switch (plugin)
            {
                case ISourcePlugin sourcePlugin:
                    // Source plugins define their output schema
                    if (sourcePlugin.OutputSchema != null)
                    {
                        schemaMap[pluginName] = sourcePlugin.OutputSchema;
                    }
                    break;
                    
                case ITransformPlugin transformPlugin:
                    // Transform plugins need input schema and define output schema
                    var inputSchema = GetInputSchemaForPlugin(pluginName, configuration, schemaMap);
                    if (inputSchema != null)
                    {
                        // Validate input schema
                        var validationResult = transformPlugin.ValidateInputSchema(inputSchema);
                        if (!validationResult.IsValid)
                        {
                            throw new PipelineExecutionException($"Schema validation failed for transform plugin '{pluginName}': {string.Join(", ", validationResult.Errors)}");
                        }
                        
                        // Set schema if plugin supports it
                        if (transformPlugin is ISchemaAwarePlugin schemaAware)
                        {
                            await schemaAware.SetSchemaAsync(inputSchema, cancellationToken);
                        }
                        
                        // Record output schema
                        if (transformPlugin.OutputSchema != null)
                        {
                            schemaMap[pluginName] = transformPlugin.OutputSchema;
                        }
                    }
                    break;
                    
                case ISinkPlugin sinkPlugin:
                    // Sink plugins need input schema
                    var sinkInputSchema = GetInputSchemaForPlugin(pluginName, configuration, schemaMap);
                    if (sinkInputSchema != null)
                    {
                        var validationResult = sinkPlugin.ValidateInputSchema(sinkInputSchema);
                        if (!validationResult.IsValid)
                        {
                            throw new PipelineExecutionException($"Schema validation failed for sink plugin '{pluginName}': {string.Join(", ", validationResult.Errors)}");
                        }
                    }
                    break;
            }
        }
    }
    
    private ISchema? GetInputSchemaForPlugin(string pluginName, IPipelineConfiguration configuration, Dictionary<string, ISchema> schemaMap)
    {
        // Find the connection that provides input to this plugin
        var inputConnection = configuration.Connections.FirstOrDefault(c => c.To == pluginName);
        if (inputConnection != null && schemaMap.TryGetValue(inputConnection.From, out var inputSchema))
        {
            return inputSchema;
        }
        
        return null;
    }

    private async Task<PipelineExecutionResult> ExecutePipelineAsync(IReadOnlyList<string> executionOrder, CancellationToken cancellationToken)
    {
        var errors = new List<PipelineError>();
        var totalRowsProcessed = 0L;
        var totalChunksProcessed = 0L;

        try
        {
            // Execute plugins in dependency order
            // Sources first, then transforms, then sinks
            var sourcePlugins = new List<(string name, IPlugin plugin)>();
            var transformPlugins = new List<(string name, IPlugin plugin)>();
            var sinkPlugins = new List<(string name, IPlugin plugin)>();

            // Categorize plugins
            foreach (var pluginName in executionOrder)
            {
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

                switch (plugin)
                {
                    case ISourcePlugin:
                        sourcePlugins.Add((pluginName, plugin));
                        break;
                    case ITransformPlugin:
                        transformPlugins.Add((pluginName, plugin));
                        break;
                    case ISinkPlugin:
                        sinkPlugins.Add((pluginName, plugin));
                        break;
                }
            }

            // Start all plugins concurrently but coordinate their execution
            var allTasks = new List<Task>();
            
            // Start sink plugins first (they'll wait for data)
            foreach (var (name, plugin) in sinkPlugins)
            {
                allTasks.Add(ExecutePluginAsync(plugin, name, cancellationToken));
            }
            
            // Start transform plugins (they'll wait for input and provide output)
            foreach (var (name, plugin) in transformPlugins)
            {
                allTasks.Add(ExecutePluginAsync(plugin, name, cancellationToken));
            }
            
            // Start source plugins last (they'll produce data)
            foreach (var (name, plugin) in sourcePlugins)
            {
                allTasks.Add(ExecutePluginAsync(plugin, name, cancellationToken));
            }

            // Wait for all plugins to complete
            await Task.WhenAll(allTasks);

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
                PluginMetrics = _pluginMetrics.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.ToImmutable()),
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

            // Update plugin metrics - metrics are already updated in the specific execution methods
            // No need to overwrite here since we track metrics during execution
        }
    }

    private async Task ExecuteSourcePluginAsync(ISourcePlugin sourcePlugin, string pluginName, CancellationToken cancellationToken)
    {
        var metrics = _pluginMetrics[pluginName];
        var stopwatch = Stopwatch.StartNew();

        try
        {
            // Find output channels for this source plugin
            var outputChannels = GetOutputChannelsForPlugin(pluginName);
            
            // Execute source plugin and send data to output channels
            await foreach (var chunk in sourcePlugin.ProduceAsync(cancellationToken))
            {
                metrics.ChunksProcessed++;
                metrics.RowsProcessed += chunk.RowCount;

                // Send chunk to all output channels
                foreach (var channel in outputChannels)
                {
                    await channel.WriteAsync(chunk, cancellationToken);
                }
            }

            // Signal completion to output channels
            foreach (var channel in outputChannels)
            {
                channel.Complete();
            }
        }
        finally
        {
            stopwatch.Stop();
            metrics.ExecutionTime = stopwatch.Elapsed;
        }
    }

    private async Task ExecuteTransformPluginAsync(ITransformPlugin transformPlugin, string pluginName, CancellationToken cancellationToken)
    {
        var metrics = _pluginMetrics[pluginName];
        var stopwatch = Stopwatch.StartNew();

        try
        {
            // Get input and output channels
            var inputChannels = GetInputChannelsForPlugin(pluginName);
            var outputChannels = GetOutputChannelsForPlugin(pluginName);

            // Merge input from all input channels
            var inputChunks = MergeInputChannels(inputChannels, cancellationToken);

            // Transform data and send to output channels
            await foreach (var outputChunk in transformPlugin.TransformAsync(inputChunks, cancellationToken))
            {
                metrics.ChunksProcessed++;
                metrics.RowsProcessed += outputChunk.RowCount;

                // Send to all output channels
                foreach (var channel in outputChannels)
                {
                    await channel.WriteAsync(outputChunk, cancellationToken);
                }
            }

            // Signal completion to output channels
            foreach (var channel in outputChannels)
            {
                channel.Complete();
            }
        }
        finally
        {
            stopwatch.Stop();
            metrics.ExecutionTime = stopwatch.Elapsed;
        }
    }

    private async Task ExecuteSinkPluginAsync(ISinkPlugin sinkPlugin, string pluginName, CancellationToken cancellationToken)
    {
        var metrics = _pluginMetrics[pluginName];
        var stopwatch = Stopwatch.StartNew();

        try
        {
            // Get input channels
            var inputChannels = GetInputChannelsForPlugin(pluginName);

            // Create async enumerable that tracks metrics while consuming
            var trackingInputChunks = TrackingAsyncEnumerable(MergeInputChannels(inputChannels, cancellationToken), metrics);

            // Consume data
            await sinkPlugin.ConsumeAsync(trackingInputChunks, cancellationToken);
        }
        finally
        {
            stopwatch.Stop();
            metrics.ExecutionTime = stopwatch.Elapsed;
        }
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
            PluginMetrics = _pluginMetrics.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.ToImmutable()),
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
            PluginMetrics = _pluginMetrics.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.ToImmutable()),
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

    private IList<IDataChannel<IChunk>> GetOutputChannelsForPlugin(string pluginName)
    {
        var outputChannels = new List<IDataChannel<IChunk>>();
        
        foreach (var kvp in _channels)
        {
            var channelId = kvp.Key;
            var channel = kvp.Value;
            
            // Channel ID format is "FromPlugin→ToPlugin"
            if (channelId.StartsWith($"{pluginName}→"))
            {
                outputChannels.Add(channel);
            }
        }
        
        return outputChannels;
    }

    private IList<IDataChannel<IChunk>> GetInputChannelsForPlugin(string pluginName)
    {
        var inputChannels = new List<IDataChannel<IChunk>>();
        
        foreach (var kvp in _channels)
        {
            var channelId = kvp.Key;
            var channel = kvp.Value;
            
            // Channel ID format is "FromPlugin→ToPlugin"
            if (channelId.EndsWith($"→{pluginName}"))
            {
                inputChannels.Add(channel);
            }
        }
        
        return inputChannels;
    }

    private async IAsyncEnumerable<IChunk> MergeInputChannels(IList<IDataChannel<IChunk>> inputChannels, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (inputChannels.Count == 0)
        {
            yield break;
        }
        
        if (inputChannels.Count == 1)
        {
            // Single input channel - direct enumeration
            await foreach (var chunk in inputChannels[0].ReadAllAsync(cancellationToken))
            {
                yield return chunk;
            }
        }
        else
        {
            // Multiple input channels - merge them
            // For simplicity, we'll read from channels sequentially
            // A more advanced implementation could interleave chunks
            foreach (var channel in inputChannels)
            {
                await foreach (var chunk in channel.ReadAllAsync(cancellationToken))
                {
                    yield return chunk;
                }
            }
        }
    }

    private async IAsyncEnumerable<IChunk> TrackingAsyncEnumerable(IAsyncEnumerable<IChunk> source, MutablePluginMetrics metrics, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var chunk in source.WithCancellation(cancellationToken))
        {
            metrics.ChunksProcessed++;
            metrics.RowsProcessed += chunk.RowCount;
            yield return chunk;
        }
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

/// <summary>
/// Mutable metrics tracking for plugin execution.
/// </summary>
internal sealed class MutablePluginMetrics
{
    public string PluginName { get; set; } = string.Empty;
    public TimeSpan ExecutionTime { get; set; }
    public long ChunksProcessed { get; set; }
    public long RowsProcessed { get; set; }
    public long MemoryUsage { get; set; }
    public int ErrorCount { get; set; }

    public PluginExecutionMetrics ToImmutable()
    {
        return new PluginExecutionMetrics
        {
            PluginName = PluginName,
            ExecutionTime = ExecutionTime,
            ChunksProcessed = ChunksProcessed,
            RowsProcessed = RowsProcessed,
            MemoryUsage = MemoryUsage,
            ErrorCount = ErrorCount
        };
    }
}