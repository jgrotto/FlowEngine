using FlowEngine.Abstractions.Configuration;
using FlowEngine.Abstractions.Execution;
using FlowEngine.Abstractions.Plugins;
using FlowEngine.Core.Configuration;
using FlowEngine.Core.Execution;
using FlowEngine.Core.Plugins;

namespace FlowEngine.Core;

/// <summary>
/// Main coordinator for FlowEngine operations providing a simplified API for pipeline execution.
/// Integrates all core components: plugin management, DAG analysis, and pipeline execution.
/// </summary>
public sealed class FlowEngineCoordinator : IAsyncDisposable
{
    private readonly IPluginManager _pluginManager;
    private readonly IDagAnalyzer _dagAnalyzer;
    private readonly IPipelineExecutor _pipelineExecutor;
    private readonly IPluginRegistry _pluginRegistry;
    private readonly IPluginTypeResolver _pluginTypeResolver;
    private bool _disposed;

    /// <summary>
    /// Initializes a new FlowEngine coordinator with the specified components.
    /// </summary>
    /// <param name="pluginManager">Plugin manager for loading and managing plugins</param>
    /// <param name="dagAnalyzer">DAG analyzer for dependency analysis</param>
    /// <param name="pipelineExecutor">Pipeline executor for orchestrating execution</param>
    /// <param name="pluginRegistry">Plugin registry for type discovery</param>
    /// <param name="pluginTypeResolver">Plugin type resolver for short name resolution</param>
    public FlowEngineCoordinator(
        IPluginManager pluginManager,
        IDagAnalyzer dagAnalyzer,
        IPipelineExecutor pipelineExecutor,
        IPluginRegistry pluginRegistry,
        IPluginTypeResolver pluginTypeResolver)
    {
        _pluginManager = pluginManager ?? throw new ArgumentNullException(nameof(pluginManager));
        _dagAnalyzer = dagAnalyzer ?? throw new ArgumentNullException(nameof(dagAnalyzer));
        _pipelineExecutor = pipelineExecutor ?? throw new ArgumentNullException(nameof(pipelineExecutor));
        _pluginRegistry = pluginRegistry ?? throw new ArgumentNullException(nameof(pluginRegistry));
        _pluginTypeResolver = pluginTypeResolver ?? throw new ArgumentNullException(nameof(pluginTypeResolver));
    }


    /// <summary>
    /// Gets the plugin manager for managing plugin lifecycle.
    /// </summary>
    public IPluginManager PluginManager => _pluginManager;

    /// <summary>
    /// Gets the DAG analyzer for dependency analysis.
    /// </summary>
    public IDagAnalyzer DagAnalyzer => _dagAnalyzer;

    /// <summary>
    /// Gets the pipeline executor for orchestrating execution.
    /// </summary>
    public IPipelineExecutor PipelineExecutor => _pipelineExecutor;

    /// <summary>
    /// Gets the plugin registry for type discovery.
    /// </summary>
    public IPluginRegistry PluginRegistry => _pluginRegistry;

    /// <summary>
    /// Executes a pipeline from a YAML configuration file.
    /// </summary>
    /// <param name="configurationFilePath">Path to the YAML configuration file</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Pipeline execution result</returns>
    /// <exception cref="ArgumentException">Thrown when file path is invalid</exception>
    /// <exception cref="FileNotFoundException">Thrown when configuration file is not found</exception>
    /// <exception cref="PipelineExecutionException">Thrown when pipeline execution fails</exception>
    public async Task<PipelineExecutionResult> ExecutePipelineFromFileAsync(string configurationFilePath, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        if (string.IsNullOrWhiteSpace(configurationFilePath))
        {
            throw new ArgumentException("Configuration file path cannot be null or empty", nameof(configurationFilePath));
        }

        if (!File.Exists(configurationFilePath))
        {
            throw new FileNotFoundException($"Configuration file not found: {configurationFilePath}");
        }

        try
        {
            var configuration = await PipelineConfiguration.LoadFromFileAsync(configurationFilePath, _pluginTypeResolver, Microsoft.Extensions.Logging.Abstractions.NullLogger<Configuration.Yaml.YamlConfigurationParser>.Instance);
            return await _pipelineExecutor.ExecuteAsync(configuration, cancellationToken);
        }
        catch (Exception ex) when (!(ex is PipelineExecutionException))
        {
            throw new PipelineExecutionException($"Failed to execute pipeline from file '{configurationFilePath}': {ex.Message}", ex)
            {
                ExecutionContext = new Dictionary<string, object>
                {
                    ["ConfigurationFile"] = configurationFilePath
                }
            };
        }
    }

    /// <summary>
    /// Executes a pipeline from YAML configuration content.
    /// </summary>
    /// <param name="yamlContent">YAML configuration content</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Pipeline execution result</returns>
    /// <exception cref="ArgumentException">Thrown when YAML content is invalid</exception>
    /// <exception cref="PipelineExecutionException">Thrown when pipeline execution fails</exception>
    public async Task<PipelineExecutionResult> ExecutePipelineFromYamlAsync(string yamlContent, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        if (string.IsNullOrWhiteSpace(yamlContent))
        {
            throw new ArgumentException("YAML content cannot be null or empty", nameof(yamlContent));
        }

        try
        {
            var configuration = PipelineConfiguration.LoadFromYaml(yamlContent, _pluginTypeResolver, Microsoft.Extensions.Logging.Abstractions.NullLogger<Configuration.Yaml.YamlConfigurationParser>.Instance);
            return await _pipelineExecutor.ExecuteAsync(configuration, cancellationToken);
        }
        catch (Exception ex) when (!(ex is PipelineExecutionException))
        {
            throw new PipelineExecutionException($"Failed to execute pipeline from YAML content: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Validates a pipeline configuration without executing it.
    /// </summary>
    /// <param name="configurationFilePath">Path to the YAML configuration file</param>
    /// <returns>Pipeline validation result</returns>
    /// <exception cref="ArgumentException">Thrown when file path is invalid</exception>
    /// <exception cref="FileNotFoundException">Thrown when configuration file is not found</exception>
    public async Task<PipelineValidationResult> ValidatePipelineAsync(string configurationFilePath)
    {
        ThrowIfDisposed();

        if (string.IsNullOrWhiteSpace(configurationFilePath))
        {
            throw new ArgumentException("Configuration file path cannot be null or empty", nameof(configurationFilePath));
        }

        if (!File.Exists(configurationFilePath))
        {
            throw new FileNotFoundException($"Configuration file not found: {configurationFilePath}");
        }

        try
        {
            var configuration = await PipelineConfiguration.LoadFromFileAsync(configurationFilePath, _pluginTypeResolver, Microsoft.Extensions.Logging.Abstractions.NullLogger<Configuration.Yaml.YamlConfigurationParser>.Instance);
            return await _pipelineExecutor.ValidatePipelineAsync(configuration);
        }
        catch (Exception ex)
        {
            return PipelineValidationResult.Failure($"Pipeline validation failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Validates a pipeline configuration from YAML content.
    /// </summary>
    /// <param name="yamlContent">YAML configuration content</param>
    /// <returns>Pipeline validation result</returns>
    /// <exception cref="ArgumentException">Thrown when YAML content is invalid</exception>
    public async Task<PipelineValidationResult> ValidatePipelineFromYamlAsync(string yamlContent)
    {
        ThrowIfDisposed();

        if (string.IsNullOrWhiteSpace(yamlContent))
        {
            throw new ArgumentException("YAML content cannot be null or empty", nameof(yamlContent));
        }

        try
        {
            var configuration = PipelineConfiguration.LoadFromYaml(yamlContent, _pluginTypeResolver, Microsoft.Extensions.Logging.Abstractions.NullLogger<Configuration.Yaml.YamlConfigurationParser>.Instance);
            return await _pipelineExecutor.ValidatePipelineAsync(configuration);
        }
        catch (Exception ex)
        {
            return PipelineValidationResult.Failure($"Pipeline validation failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Discovers plugins in the specified directory.
    /// </summary>
    /// <param name="pluginDirectory">Directory to scan for plugins</param>
    /// <param name="searchPattern">File pattern to match (default: *.dll)</param>
    /// <returns>Task that completes when plugin discovery is finished</returns>
    /// <exception cref="ArgumentException">Thrown when directory path is invalid</exception>
    /// <exception cref="DirectoryNotFoundException">Thrown when plugin directory is not found</exception>
    public async Task DiscoverPluginsAsync(string pluginDirectory, string searchPattern = "*.dll")
    {
        ThrowIfDisposed();

        if (string.IsNullOrWhiteSpace(pluginDirectory))
        {
            throw new ArgumentException("Plugin directory cannot be null or empty", nameof(pluginDirectory));
        }

        if (!Directory.Exists(pluginDirectory))
        {
            throw new DirectoryNotFoundException($"Plugin directory not found: {pluginDirectory}");
        }

        await _pluginRegistry.ScanDirectoryAsync(pluginDirectory, searchPattern);
    }

    /// <summary>
    /// Gets information about all discovered plugins.
    /// </summary>
    /// <param name="category">Optional category filter</param>
    /// <returns>Collection of plugin type information</returns>
    public IReadOnlyCollection<PluginTypeInfo> GetAvailablePlugins(PluginCategory? category = null)
    {
        ThrowIfDisposed();
        return _pluginRegistry.GetPluginTypes(category);
    }

    /// <summary>
    /// Gets information about all loaded plugins.
    /// </summary>
    /// <returns>Collection of loaded plugin information</returns>
    public IReadOnlyCollection<PluginInfo> GetLoadedPlugins()
    {
        ThrowIfDisposed();
        return _pluginManager.GetPluginInfo();
    }

    /// <summary>
    /// Gets current pipeline execution status.
    /// </summary>
    public PipelineExecutionStatus Status => _pipelineExecutor.Status;

    /// <summary>
    /// Gets current pipeline execution metrics.
    /// </summary>
    public PipelineExecutionMetrics Metrics => _pipelineExecutor.Metrics;

    /// <summary>
    /// Stops pipeline execution gracefully.
    /// </summary>
    /// <param name="timeout">Maximum time to wait for graceful shutdown</param>
    /// <returns>Task that completes when pipeline is stopped</returns>
    public async Task StopPipelineAsync(TimeSpan? timeout = null)
    {
        ThrowIfDisposed();
        await _pipelineExecutor.StopAsync(timeout);
    }

    /// <summary>
    /// Pauses pipeline execution if currently running.
    /// </summary>
    /// <returns>Task that completes when pipeline is paused</returns>
    public async Task PausePipelineAsync()
    {
        ThrowIfDisposed();
        await _pipelineExecutor.PauseAsync();
    }

    /// <summary>
    /// Resumes a paused pipeline execution.
    /// </summary>
    /// <returns>Task that completes when pipeline is resumed</returns>
    public async Task ResumePipelineAsync()
    {
        ThrowIfDisposed();
        await _pipelineExecutor.ResumeAsync();
    }

    /// <summary>
    /// Event raised when pipeline execution status changes.
    /// </summary>
    public event EventHandler<PipelineStatusChangedEventArgs>? StatusChanged
    {
        add => _pipelineExecutor.StatusChanged += value;
        remove => _pipelineExecutor.StatusChanged -= value;
    }

    /// <summary>
    /// Event raised when a plugin completes processing a chunk.
    /// </summary>
    public event EventHandler<PluginChunkProcessedEventArgs>? PluginChunkProcessed
    {
        add => _pipelineExecutor.PluginChunkProcessed += value;
        remove => _pipelineExecutor.PluginChunkProcessed -= value;
    }

    /// <summary>
    /// Event raised when a pipeline error occurs.
    /// </summary>
    public event EventHandler<PipelineErrorEventArgs>? PipelineError
    {
        add => _pipelineExecutor.PipelineError += value;
        remove => _pipelineExecutor.PipelineError -= value;
    }

    /// <summary>
    /// Event raised when pipeline execution completes.
    /// </summary>
    public event EventHandler<PipelineCompletedEventArgs>? PipelineCompleted
    {
        add => _pipelineExecutor.PipelineCompleted += value;
        remove => _pipelineExecutor.PipelineCompleted -= value;
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        try
        {
            await _pipelineExecutor.DisposeAsync();
            _pluginManager?.Dispose();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error during FlowEngine coordinator disposal: {ex}");
        }
        finally
        {
            _disposed = true;
        }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(FlowEngineCoordinator));
        }
    }

}
