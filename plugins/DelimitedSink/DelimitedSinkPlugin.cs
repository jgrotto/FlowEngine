using FlowEngine.Abstractions.Data;
using FlowEngine.Abstractions.Plugins;
using FlowEngine.Abstractions.Factories;
using FlowEngine.Abstractions.Services;
using Microsoft.Extensions.Logging;
using System.Collections.Immutable;

namespace DelimitedSink;

/// <summary>
/// DelimitedSink plugin for writing CSV and other delimited files with flexible formatting.
/// </summary>
public sealed class DelimitedSinkPlugin : ISinkPlugin
{
    private readonly ILogger<DelimitedSinkPlugin> _logger;
    private readonly ISchemaFactory? _schemaFactory;
    private readonly IArrayRowFactory? _arrayRowFactory;
    private readonly IChunkFactory? _chunkFactory;
    private readonly IDatasetFactory? _datasetFactory;
    private readonly IDataTypeService? _dataTypeService;
    private readonly IMemoryManager? _memoryManager;
    private readonly IPerformanceMonitor? _performanceMonitor;
    private readonly IChannelTelemetry? _channelTelemetry;

    private DelimitedSinkConfiguration? _configuration;
    private PluginState _state = PluginState.Created;
    private bool _disposed;

    public DelimitedSinkPlugin(
        ILogger<DelimitedSinkPlugin> logger,
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

    public string Id => "delimited-sink";
    public string Name => "Delimited Sink";
    public string Version => "1.0.0";
    public string Description => "High-performance CSV and delimited file sink with flexible formatting";
    public string Author => "FlowEngine Team";
    public string PluginType => "Sink";
    public PluginState State => _state;
    public IPluginConfiguration Configuration => _configuration ?? throw new InvalidOperationException("Plugin not configured");
    public ImmutableDictionary<string, object>? Metadata => ImmutableDictionary<string, object>.Empty;
    public bool SupportsHotSwapping => true;
    public ISchema? OutputSchema => null; // Sink plugins don't produce output

    public event EventHandler<PluginStateChangedEventArgs>? StateChanged;

    public async Task<PluginInitializationResult> InitializeAsync(
        IPluginConfiguration configuration,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Initializing DelimitedSink plugin");

        if (configuration is not DelimitedSinkConfiguration sinkConfig)
        {
            return new PluginInitializationResult
            {
                Success = false,
                Message = $"Expected DelimitedSinkConfiguration, got {configuration?.GetType().Name ?? "null"}",
                InitializationTime = TimeSpan.Zero,
                Errors = ImmutableArray.Create("Invalid configuration type")
            };
        }

        // Validate output directory and file access
        try
        {
            var directory = Path.GetDirectoryName(Path.GetFullPath(sinkConfig.FilePath));
            
            // Create directories if needed and allowed
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                if (sinkConfig.CreateDirectories)
                {
                    Directory.CreateDirectory(directory);
                    _logger.LogInformation("Created output directory: {Directory}", directory);
                }
                else
                {
                    return new PluginInitializationResult
                    {
                        Success = false,
                        Message = $"Output directory '{directory}' does not exist and CreateDirectories is false",
                        InitializationTime = TimeSpan.Zero,
                        Errors = ImmutableArray.Create("Output directory not found")
                    };
                }
            }

            // Validate file access
            if (File.Exists(sinkConfig.FilePath) && !sinkConfig.AppendMode && !sinkConfig.OverwriteExisting)
            {
                return new PluginInitializationResult
                {
                    Success = false,
                    Message = $"Output file '{sinkConfig.FilePath}' exists but overwriting is not allowed",
                    InitializationTime = TimeSpan.Zero,
                    Errors = ImmutableArray.Create("File exists and overwrite not allowed")
                };
            }

            // Test write access
            if (!string.IsNullOrEmpty(directory))
            {
                var testFile = Path.Combine(directory, $"test_{Guid.NewGuid()}.tmp");
                await File.WriteAllTextAsync(testFile, "test", cancellationToken);
                File.Delete(testFile);
            }

            // Validate encoding
            System.Text.Encoding.GetEncoding(sinkConfig.Encoding);
        }
        catch (Exception ex)
        {
            return new PluginInitializationResult
            {
                Success = false,
                Message = $"File system validation error: {ex.Message}",
                InitializationTime = TimeSpan.Zero,
                Errors = ImmutableArray.Create(ex.Message)
            };
        }

        var oldState = _state;
        _configuration = sinkConfig;
        _state = PluginState.Initialized;
        
        StateChanged?.Invoke(this, new PluginStateChangedEventArgs { PreviousState = oldState, CurrentState = _state });

        _logger.LogInformation("DelimitedSink plugin initialized successfully for file: {FilePath}", 
            sinkConfig.FilePath);

        return new PluginInitializationResult
        {
            Success = true,
            Message = "Plugin initialized successfully",
            InitializationTime = TimeSpan.FromMilliseconds(50)
        };
    }

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Starting DelimitedSink plugin");

        if (_state != PluginState.Initialized)
            throw new InvalidOperationException($"Plugin must be initialized before starting. Current state: {_state}");

        var oldState = _state;
        _state = PluginState.Running;
        StateChanged?.Invoke(this, new PluginStateChangedEventArgs { PreviousState = oldState, CurrentState = _state });
        
        _logger.LogInformation("DelimitedSink plugin started successfully");

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Stopping DelimitedSink plugin");
        
        var oldState = _state;
        _state = PluginState.Stopped;
        StateChanged?.Invoke(this, new PluginStateChangedEventArgs { PreviousState = oldState, CurrentState = _state });
        
        _logger.LogInformation("DelimitedSink plugin stopped successfully");

        return Task.CompletedTask;
    }

    public Task<HotSwapResult> HotSwapAsync(
        IPluginConfiguration newConfiguration,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Performing hot-swap for DelimitedSink plugin");

        if (newConfiguration is not DelimitedSinkConfiguration newSinkConfig)
        {
            return Task.FromResult(new HotSwapResult
            {
                Success = false,
                Message = $"Expected DelimitedSinkConfiguration, got {newConfiguration?.GetType().Name ?? "null"}",
                SwapTime = TimeSpan.Zero
            });
        }

        // Validate new output path is accessible
        try
        {
            var directory = Path.GetDirectoryName(Path.GetFullPath(newSinkConfig.FilePath));
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory) && !newSinkConfig.CreateDirectories)
            {
                return Task.FromResult(new HotSwapResult
                {
                    Success = false,
                    Message = $"New output directory '{directory}' does not exist",
                    SwapTime = TimeSpan.Zero
                });
            }
        }
        catch (Exception ex)
        {
            return Task.FromResult(new HotSwapResult
            {
                Success = false,
                Message = $"Error validating new configuration: {ex.Message}",
                SwapTime = TimeSpan.Zero
            });
        }

        _configuration = newSinkConfig;
        _logger.LogInformation("Hot-swap completed successfully to file: {FilePath}", newSinkConfig.FilePath);

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

        // Check output path accessibility if configured
        if (_configuration != null)
        {
            var directory = Path.GetDirectoryName(Path.GetFullPath(_configuration.FilePath));
            var isAccessible = string.IsNullOrEmpty(directory) || Directory.Exists(directory);
            
            checks.Add(new HealthCheck
            {
                Name = "Output Path Accessibility",
                Passed = isAccessible,
                Details = isAccessible 
                    ? "Output path is accessible" 
                    : $"Output directory not accessible: {directory}",
                ExecutionTime = TimeSpan.FromMilliseconds(1),
                Severity = isAccessible ? HealthCheckSeverity.Info : HealthCheckSeverity.Error
            });

            // Check disk space if directory exists
            if (!string.IsNullOrEmpty(directory) && Directory.Exists(directory))
            {
                try
                {
                    var drive = new DriveInfo(directory);
                    var hasSpace = drive.AvailableFreeSpace > 100 * 1024 * 1024; // 100MB minimum
                    
                    checks.Add(new HealthCheck
                    {
                        Name = "Disk Space",
                        Passed = hasSpace,
                        Details = $"Available space: {drive.AvailableFreeSpace / (1024 * 1024)}MB",
                        ExecutionTime = TimeSpan.FromMilliseconds(1),
                        Severity = hasSpace ? HealthCheckSeverity.Info : HealthCheckSeverity.Warning
                    });
                }
                catch (Exception ex)
                {
                    checks.Add(new HealthCheck
                    {
                        Name = "Disk Space",
                        Passed = false,
                        Details = $"Could not check disk space: {ex.Message}",
                        ExecutionTime = TimeSpan.FromMilliseconds(1),
                        Severity = HealthCheckSeverity.Warning
                    });
                }
            }
        }

        return Task.FromResult<IEnumerable<HealthCheck>>(checks);
    }

    public ValidationResult ValidateInputSchema(ISchema? inputSchema)
    {
        if (inputSchema == null)
        {
            return ValidationResult.Failure(new ValidationError
            {
                Code = "MISSING_INPUT_SCHEMA",
                Message = "DelimitedSink requires an input schema to determine output structure",
                Severity = ValidationSeverity.Error,
                Remediation = "Ensure the previous plugin in the pipeline provides a schema"
            });
        }

        // Check that all column types can be written to CSV
        var errors = new List<ValidationError>();
        var supportedTypes = new HashSet<Type>
        {
            typeof(string), typeof(int), typeof(long), typeof(decimal), 
            typeof(double), typeof(bool), typeof(DateTime), typeof(DateTimeOffset)
        };

        foreach (var column in inputSchema.Columns)
        {
            if (!supportedTypes.Contains(column.DataType))
            {
                errors.Add(new ValidationError
                {
                    Code = "UNSUPPORTED_COLUMN_TYPE",
                    Message = $"Column '{column.Name}' has unsupported type '{column.DataType.Name}' for CSV output",
                    Severity = ValidationSeverity.Warning,
                    Remediation = "Complex types will be converted to string representation"
                });
            }
        }

        return errors.Any(e => e.Severity == ValidationSeverity.Error)
            ? ValidationResult.Failure(errors.ToArray())
            : ValidationResult.Success();
    }

    // ISinkPlugin implementation - DelimitedSink accepts any schema
    public ISchema InputSchema => _schemaFactory?.CreateSchema(Array.Empty<ColumnDefinition>()) ?? throw new InvalidOperationException("SchemaFactory not available");
    public bool SupportsTransactions => false; // Transactions not supported for file writes
    public WriteMode Mode => WriteMode.Buffered; // File writing is buffered
    public TransactionState? CurrentTransaction => null; // No transactions

    public async Task ConsumeAsync(IAsyncEnumerable<IChunk> input, CancellationToken cancellationToken = default)
    {
        if (_state != PluginState.Running)
            throw new PluginExecutionException($"Plugin must be running to consume data. Current state: {_state}");

        if (_configuration == null)
            throw new PluginExecutionException("Plugin not configured");

        _logger.LogInformation("Starting data consumption to file: {FilePath}", _configuration.FilePath);

        try
        {
            var sinkService = GetSinkService();
            await foreach (var chunk in input.WithCancellation(cancellationToken))
            {
                _logger.LogDebug("Consuming chunk with {RowCount} rows", chunk.RowCount);
                await sinkService.ProcessChunkAsync(chunk, cancellationToken);
            }
            
            // Ensure all data is flushed
            await FlushAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error consuming data to file: {FilePath}", _configuration.FilePath);
            throw new PluginExecutionException($"Failed to consume data: {ex.Message}", ex);
        }

        _logger.LogInformation("Completed data consumption to file: {FilePath}", _configuration.FilePath);
    }

    public Task<string> BeginTransactionAsync(CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("Transactions are not supported by DelimitedSink plugin");
    }

    public Task CommitTransactionAsync(string transactionId, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("Transactions are not supported by DelimitedSink plugin");
    }

    public Task RollbackTransactionAsync(string transactionId, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("Transactions are not supported by DelimitedSink plugin");
    }

    public async Task FlushAsync(CancellationToken cancellationToken = default)
    {
        if (_configuration == null)
            return;

        _logger.LogDebug("Flushing buffered data to file: {FilePath}", _configuration.FilePath);
        
        // File I/O is automatically flushed when streams are disposed
        // For more sophisticated buffering, this would trigger actual flush operations
        await Task.CompletedTask;
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _logger.LogInformation("Disposing DelimitedSink plugin");

        if (_state == PluginState.Running)
            StopAsync().GetAwaiter().GetResult();

        var oldState = _state;
        _disposed = true;
        _state = PluginState.Disposed;
        StateChanged?.Invoke(this, new PluginStateChangedEventArgs { PreviousState = oldState, CurrentState = _state });

        _logger.LogInformation("DelimitedSink plugin disposed successfully");
    }

    // Helper method to get the service for writing files
    public DelimitedSinkService GetSinkService()
    {
        return new DelimitedSinkService(
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