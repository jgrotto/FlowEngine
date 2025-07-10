using FlowEngine.Abstractions.Data;
using FlowEngine.Abstractions.Plugins;
using FlowEngine.Abstractions.Factories;
using FlowEngine.Abstractions.Services;
using Microsoft.Extensions.Logging;
using System.Collections.Immutable;

namespace DelimitedSource;

/// <summary>
/// DelimitedSource plugin for reading CSV and other delimited files with schema inference.
/// </summary>
public sealed class DelimitedSourcePlugin : ISourcePlugin
{
    private readonly ILogger<DelimitedSourcePlugin> _logger;
    private readonly ISchemaFactory? _schemaFactory;
    private readonly IArrayRowFactory? _arrayRowFactory;
    private readonly IChunkFactory? _chunkFactory;
    private readonly IDatasetFactory? _datasetFactory;
    private readonly IDataTypeService? _dataTypeService;
    private readonly IMemoryManager? _memoryManager;
    private readonly IPerformanceMonitor? _performanceMonitor;
    private readonly IChannelTelemetry? _channelTelemetry;

    private DelimitedSourceConfiguration? _configuration;
    private PluginState _state = PluginState.Created;
    private bool _disposed;

    public DelimitedSourcePlugin(
        ILogger<DelimitedSourcePlugin> logger,
        ISchemaFactory? schemaFactory = null,
        IArrayRowFactory? arrayRowFactory = null,
        IChunkFactory? chunkFactory = null,
        IDatasetFactory? datasetFactory = null,
        IDataTypeService? dataTypeService = null,
        IMemoryManager? memoryManager = null,
        IPerformanceMonitor? performanceMonitor = null,
        IChannelTelemetry? channelTelemetry = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _schemaFactory = schemaFactory;
        _arrayRowFactory = arrayRowFactory;
        _chunkFactory = chunkFactory;
        _datasetFactory = datasetFactory;
        _dataTypeService = dataTypeService;
        _memoryManager = memoryManager;
        _performanceMonitor = performanceMonitor;
        _channelTelemetry = channelTelemetry;
    }

    public string Id => "delimited-source";
    public string Name => "Delimited Source";
    public string Version => "1.0.0";
    public string Description => "High-performance CSV and delimited file source with schema inference";
    public string Author => "FlowEngine Team";
    public string PluginType => "Source";
    public PluginState State => _state;
    public IPluginConfiguration Configuration => _configuration ?? throw new InvalidOperationException("Plugin not configured");
    public ImmutableDictionary<string, object>? Metadata => ImmutableDictionary<string, object>.Empty;
    public bool SupportsHotSwapping => true;
    public ISchema? OutputSchema => _configuration?.OutputSchema;

    public event EventHandler<PluginStateChangedEventArgs>? StateChanged;

    public async Task<PluginInitializationResult> InitializeAsync(
        IPluginConfiguration configuration,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Initializing DelimitedSource plugin");

        if (configuration is not DelimitedSourceConfiguration sourceConfig)
        {
            return new PluginInitializationResult
            {
                Success = false,
                Message = $"Expected DelimitedSourceConfiguration, got {configuration?.GetType().Name ?? "null"}",
                InitializationTime = TimeSpan.Zero,
                Errors = ImmutableArray.Create("Invalid configuration type")
            };
        }

        // Validate file exists and is accessible
        if (!File.Exists(sourceConfig.FilePath))
        {
            return new PluginInitializationResult
            {
                Success = false,
                Message = $"File not found: {sourceConfig.FilePath}",
                InitializationTime = TimeSpan.Zero,
                Errors = ImmutableArray.Create("File does not exist")
            };
        }

        try
        {
            // Test file access
            using var stream = File.OpenRead(sourceConfig.FilePath);
            
            // Validate encoding
            System.Text.Encoding.GetEncoding(sourceConfig.Encoding);
        }
        catch (Exception ex)
        {
            return new PluginInitializationResult
            {
                Success = false,
                Message = $"File access error: {ex.Message}",
                InitializationTime = TimeSpan.Zero,
                Errors = ImmutableArray.Create(ex.Message)
            };
        }

        var oldState = _state;
        _configuration = sourceConfig;
        _state = PluginState.Initialized;
        
        StateChanged?.Invoke(this, new PluginStateChangedEventArgs { PreviousState = oldState, CurrentState = _state });

        _logger.LogInformation("DelimitedSource plugin initialized successfully for file: {FilePath}", 
            sourceConfig.FilePath);

        return new PluginInitializationResult
        {
            Success = true,
            Message = "Plugin initialized successfully",
            InitializationTime = TimeSpan.FromMilliseconds(50)
        };
    }

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Starting DelimitedSource plugin");

        if (_state != PluginState.Initialized)
            throw new InvalidOperationException($"Plugin must be initialized before starting. Current state: {_state}");

        var oldState = _state;
        _state = PluginState.Running;
        StateChanged?.Invoke(this, new PluginStateChangedEventArgs { PreviousState = oldState, CurrentState = _state });
        
        _logger.LogInformation("DelimitedSource plugin started successfully");

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Stopping DelimitedSource plugin");
        
        var oldState = _state;
        _state = PluginState.Stopped;
        StateChanged?.Invoke(this, new PluginStateChangedEventArgs { PreviousState = oldState, CurrentState = _state });
        
        _logger.LogInformation("DelimitedSource plugin stopped successfully");

        return Task.CompletedTask;
    }

    public Task<HotSwapResult> HotSwapAsync(
        IPluginConfiguration newConfiguration,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Performing hot-swap for DelimitedSource plugin");

        if (newConfiguration is not DelimitedSourceConfiguration newSourceConfig)
        {
            return Task.FromResult(new HotSwapResult
            {
                Success = false,
                Message = $"Expected DelimitedSourceConfiguration, got {newConfiguration?.GetType().Name ?? "null"}",
                SwapTime = TimeSpan.Zero
            });
        }

        // Validate new file exists
        if (!File.Exists(newSourceConfig.FilePath))
        {
            return Task.FromResult(new HotSwapResult
            {
                Success = false,
                Message = $"New file not found: {newSourceConfig.FilePath}",
                SwapTime = TimeSpan.Zero
            });
        }

        _configuration = newSourceConfig;
        _logger.LogInformation("Hot-swap completed successfully to file: {FilePath}", newSourceConfig.FilePath);

        return Task.FromResult(new HotSwapResult
        {
            Success = true,
            Message = "Configuration updated successfully",
            SwapTime = TimeSpan.FromMilliseconds(10)
        });
    }

    public Task<IEnumerable<HealthCheck>> HealthCheckAsync(CancellationToken cancellationToken = default)
    {
        var checks = new List<HealthCheck>();

        if (_disposed)
        {
            checks.Add(new HealthCheck
            {
                Name = "Plugin Lifecycle",
                Passed = false,
                Details = "Plugin has been disposed",
                ExecutionTime = TimeSpan.FromMilliseconds(1),
                Severity = HealthCheckSeverity.Error
            });
            return Task.FromResult<IEnumerable<HealthCheck>>(checks);
        }

        // Check plugin state
        checks.Add(new HealthCheck
        {
            Name = "Plugin State",
            Passed = _state == PluginState.Running,
            Details = $"Plugin is in {_state} state",
            ExecutionTime = TimeSpan.FromMilliseconds(1),
            Severity = _state == PluginState.Running ? HealthCheckSeverity.Info : HealthCheckSeverity.Warning
        });

        // Check file accessibility if configured
        if (_configuration != null)
        {
            var fileExists = File.Exists(_configuration.FilePath);
            checks.Add(new HealthCheck
            {
                Name = "File Accessibility",
                Passed = fileExists,
                Details = fileExists ? "Source file is accessible" : $"Source file not found: {_configuration.FilePath}",
                ExecutionTime = TimeSpan.FromMilliseconds(1),
                Severity = fileExists ? HealthCheckSeverity.Info : HealthCheckSeverity.Error
            });
        }

        return Task.FromResult<IEnumerable<HealthCheck>>(checks);
    }

    public ValidationResult ValidateInputSchema(ISchema? inputSchema)
    {
        // Source plugins don't have input schemas, so this is always valid
        return ValidationResult.Success();
    }

    // ISourcePlugin implementation
    public long? EstimatedRowCount => null; // Unknown row count for delimited files
    public bool IsSeekable => false; // Seeking not supported for simplicity
    public SourcePosition? CurrentPosition => null; // Position tracking not implemented

    public async IAsyncEnumerable<IChunk> ProduceAsync([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (_state != PluginState.Running)
            throw new PluginExecutionException($"Plugin must be running to produce data. Current state: {_state}");

        if (_configuration == null)
            throw new PluginExecutionException("Plugin not configured");

        _logger.LogInformation("ProduceAsync: Starting data production from file: {FilePath}", _configuration.FilePath);

        var sourceService = GetSourceService();
        IDataset dataset;
        
        try
        {
            _logger.LogDebug("ProduceAsync: Calling ReadFileAsync");
            dataset = await sourceService.ReadFileAsync(cancellationToken);
            _logger.LogDebug("ProduceAsync: ReadFileAsync completed successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ProduceAsync: Error loading data from file: {FilePath}", _configuration.FilePath);
            throw new PluginExecutionException($"Failed to load data: {ex.Message}", ex);
        }

        using (dataset)
        {
            // Use the dataset's chunk iterator directly with our configured chunk size
            var chunkingOptions = new ChunkingOptions { PreferredChunkSize = _configuration.ChunkSize };
            
            _logger.LogDebug("ProduceAsync: Starting chunk iteration");
            var chunkCount = 0;
            await foreach (var chunk in dataset.GetChunksAsync(chunkingOptions, cancellationToken))
            {
                chunkCount++;
                _logger.LogDebug("ProduceAsync: Produced chunk {ChunkNumber} with {RowCount} rows", chunkCount, chunk.RowCount);
                yield return chunk;
            }
            _logger.LogDebug("ProduceAsync: Completed chunk iteration - produced {TotalChunks} chunks", chunkCount);
        }

        _logger.LogInformation("ProduceAsync: Completed data production from file: {FilePath}", _configuration.FilePath);
    }

    public Task SeekAsync(SourcePosition position, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("Seeking is not supported by DelimitedSource plugin");
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _logger.LogInformation("Disposing DelimitedSource plugin");

        if (_state == PluginState.Running)
            StopAsync().GetAwaiter().GetResult();

        var oldState = _state;
        _disposed = true;
        _state = PluginState.Disposed;
        StateChanged?.Invoke(this, new PluginStateChangedEventArgs { PreviousState = oldState, CurrentState = _state });

        _logger.LogInformation("DelimitedSource plugin disposed successfully");
    }

    // Helper method to get the service for reading files
    public DelimitedSourceService GetSourceService()
    {
        return new DelimitedSourceService(
            this, 
            _logger, 
            _schemaFactory, 
            _arrayRowFactory, 
            _chunkFactory, 
            _datasetFactory,
            _dataTypeService,
            _memoryManager,
            _channelTelemetry);
    }
}