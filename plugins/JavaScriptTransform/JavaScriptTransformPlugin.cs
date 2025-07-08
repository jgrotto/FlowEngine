using FlowEngine.Abstractions.Data;
using FlowEngine.Abstractions.Plugins;
using FlowEngine.Core.Data;
using FlowEngine.Core.Services;
using Microsoft.Extensions.Logging;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace JavaScriptTransform;

/// <summary>
/// Thin JavaScript Transform plugin that calls Core services for script execution.
/// Implements Mode 1: Inline scripts with explicit output schema definition.
/// </summary>
public class JavaScriptTransformPlugin : PluginBase, ITransformPlugin
{
    private readonly IScriptEngineService _scriptEngine;
    private readonly IJavaScriptContextService _contextService;
    private readonly ILogger<JavaScriptTransformPlugin> _logger;
    
    private CompiledScript? _compiledScript;
    private ISchema? _outputSchema;
    private ISchema? _inputSchema;
    private JavaScriptTransformConfiguration? _configuration;
    private readonly Stopwatch _performanceTimer = new();
    private int _processedRows = 0;

    /// <summary>
    /// Initializes a new instance of the JavaScriptTransformPlugin class.
    /// </summary>
    /// <param name="scriptEngine">Core script engine service for JavaScript execution</param>
    /// <param name="contextService">Core context service for JavaScript context creation</param>
    /// <param name="logger">Logger instance for diagnostics</param>
    public JavaScriptTransformPlugin(
        IScriptEngineService scriptEngine,
        IJavaScriptContextService contextService,
        ILogger<JavaScriptTransformPlugin> logger)
        : base(logger)
    {
        _scriptEngine = scriptEngine ?? throw new ArgumentNullException(nameof(scriptEngine));
        _contextService = contextService ?? throw new ArgumentNullException(nameof(contextService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    // Abstract property implementations
    public override string Name => "JavaScript Transform";
    public override string Version => "1.0.0";
    public override string Description => "Thin JavaScript Transform plugin using Core services for high-performance data processing";
    public override string Author => "FlowEngine Team";
    public override string PluginType => "Transform";
    public override bool SupportsHotSwapping => false;
    public override IPluginConfiguration Configuration 
    { 
        get => _configuration ?? throw new InvalidOperationException("Plugin not initialized"); 
        protected set => _configuration = (JavaScriptTransformConfiguration)value; 
    }

    /// <summary>
    /// Gets the input schema for the transform.
    /// </summary>
    public ISchema InputSchema => _inputSchema ?? throw new InvalidOperationException("Input schema not set");

    /// <summary>
    /// Gets the output schema for the transform.
    /// </summary>
    public override ISchema? OutputSchema => _outputSchema;

    /// <summary>
    /// Indicates if this transform is stateful.
    /// </summary>
    public bool IsStateful => false;

    /// <summary>
    /// Gets the transform mode.
    /// </summary>
    public TransformMode Mode => TransformMode.OneToOne;

    /// <summary>
    /// Initializes the JavaScript Transform plugin using Core services.
    /// </summary>
    protected override async Task<PluginInitializationResult> InitializeInternalAsync(IPluginConfiguration configuration, CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("Initializing JavaScript Transform plugin");
            _configuration = (JavaScriptTransformConfiguration)configuration;

            // Validate configuration
            if (!_configuration.IsValid())
            {
                var errors = string.Join(", ", _configuration.GetValidationErrors());
                return new PluginInitializationResult
                {
                    Success = false,
                    Message = $"Configuration validation failed: {errors}",
                    InitializationTime = TimeSpan.Zero,
                    Errors = ImmutableArray.Create(errors)
                };
            }

            // Create script options for Core service
            var scriptOptions = new ScriptOptions
            {
                Timeout = TimeSpan.FromMilliseconds(_configuration.Engine.Timeout),
                MemoryLimit = _configuration.Engine.MemoryLimit,
                EnableCaching = _configuration.Engine.EnableCaching,
                EnableDebugging = _configuration.Engine.EnableDebugging
            };

            // Compile the JavaScript script using Core service
            _compiledScript = await _scriptEngine.CompileAsync(_configuration.Script, scriptOptions);
            _logger.LogDebug("JavaScript script compiled successfully using Core service");

            // Create output schema from configuration
            _outputSchema = await CreateOutputSchemaAsync(_configuration.OutputSchema);
            _logger.LogDebug("Output schema created with {ColumnCount} columns", _outputSchema.ColumnCount);

            _logger.LogInformation("JavaScript Transform plugin initialized successfully");
            return new PluginInitializationResult
            {
                Success = true,
                Message = "Plugin initialized successfully",
                InitializationTime = TimeSpan.FromMilliseconds(50),
                Errors = ImmutableArray<string>.Empty,
                Warnings = ImmutableArray<string>.Empty
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize JavaScript Transform plugin");
            return new PluginInitializationResult
            {
                Success = false,
                Message = $"Initialization failed: {ex.Message}",
                InitializationTime = TimeSpan.Zero,
                Errors = ImmutableArray.Create(ex.Message)
            };
        }
    }

    /// <summary>
    /// Starts the plugin for processing.
    /// </summary>
    protected override async Task StartInternalAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Starting JavaScript Transform plugin");
        _performanceTimer.Start();
        _processedRows = 0;
        await Task.CompletedTask;
    }

    /// <summary>
    /// Stops the plugin processing.
    /// </summary>
    protected override async Task StopInternalAsync(CancellationToken cancellationToken)
    {
        _performanceTimer.Stop();
        
        if (_configuration?.Performance.EnableMonitoring == true && _processedRows > 0)
        {
            var throughput = _processedRows / _performanceTimer.Elapsed.TotalSeconds;
            _logger.LogInformation(
                "JavaScript Transform plugin stopped. Processed {RowCount} rows in {ElapsedMs}ms. Throughput: {Throughput:F0} rows/sec",
                _processedRows,
                _performanceTimer.ElapsedMilliseconds,
                throughput);

            if (throughput < _configuration.Performance.TargetThroughput && _configuration.Performance.LogPerformanceWarnings)
            {
                _logger.LogWarning(
                    "Performance below target: {ActualThroughput:F0} rows/sec < {TargetThroughput} rows/sec",
                    throughput,
                    _configuration.Performance.TargetThroughput);
            }
        }

        await Task.CompletedTask;
    }

    /// <summary>
    /// Disposes plugin resources.
    /// </summary>
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            try
            {
                _logger.LogInformation("Disposing JavaScript Transform plugin");
                
                _compiledScript?.Dispose();
                
                // Get statistics from Core service
                var stats = _scriptEngine.GetStats();
                _logger.LogInformation(
                    "Core script engine statistics - Compiled: {Compiled}, Executed: {Executed}, Cache hits: {CacheHits}",
                    stats.ScriptsCompiled,
                    stats.ScriptsExecuted,
                    stats.CacheHits);

                _logger.LogInformation("JavaScript Transform plugin disposed successfully");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during JavaScript Transform plugin disposal");
            }
        }
        
        base.Dispose(disposing);
    }

    /// <summary>
    /// Validates the input schema compatibility.
    /// </summary>
    public override ValidationResult ValidateInputSchema(ISchema? inputSchema)
    {
        _inputSchema = inputSchema;
        // JavaScript transforms can work with any input schema
        return ValidationResult.Success();
    }

    /// <summary>
    /// Transforms the input data chunks using Core services.
    /// </summary>
    public async IAsyncEnumerable<IChunk> TransformAsync(
        IAsyncEnumerable<IChunk> input,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var chunk in input.WithCancellation(cancellationToken))
        {
            var transformedChunk = await TransformChunkAsync(chunk, cancellationToken);
            yield return transformedChunk;
        }
    }

    /// <summary>
    /// Flushes any remaining data for stateful transforms.
    /// </summary>
    public async IAsyncEnumerable<IChunk> FlushAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        // This transform is stateless, so nothing to flush
        yield break;
    }

    /// <summary>
    /// Performs additional plugin health checks.
    /// </summary>
    protected override async Task<IEnumerable<HealthCheck>> PerformAdditionalHealthChecksAsync(CancellationToken cancellationToken)
    {
        var healthChecks = new List<HealthCheck>();

        try
        {
            // Get statistics from Core service
            var stats = _scriptEngine.GetStats();
            
            // Check performance metrics
            if (_configuration?.Performance.EnableMonitoring == true && _processedRows > 100)
            {
                var currentThroughput = _processedRows / _performanceTimer.Elapsed.TotalSeconds;
                var targetThroughput = _configuration.Performance.TargetThroughput;
                
                if (currentThroughput < targetThroughput * 0.8) // 80% of target
                {
                    healthChecks.Add(new HealthCheck
                    {
                        Name = "Performance",
                        Passed = false,
                        Details = $"Performance below 80% of target: {currentThroughput:F0} < {targetThroughput * 0.8:F0} rows/sec",
                        ExecutionTime = TimeSpan.Zero,
                        Severity = HealthCheckSeverity.Warning
                    });
                }
            }

            // Check memory usage from Core service
            if (stats.MemoryUsage > _configuration?.Engine.MemoryLimit * 0.9) // 90% of limit
            {
                healthChecks.Add(new HealthCheck
                {
                    Name = "Memory Usage",
                    Passed = false,
                    Details = $"Memory usage near limit: {stats.MemoryUsage / 1024 / 1024:F1}MB",
                    ExecutionTime = TimeSpan.Zero,
                    Severity = HealthCheckSeverity.Warning
                });
            }

            // Add success health check if no issues
            if (healthChecks.Count == 0)
            {
                healthChecks.Add(new HealthCheck
                {
                    Name = "JavaScript Transform",
                    Passed = true,
                    Details = "Plugin healthy",
                    ExecutionTime = TimeSpan.Zero,
                    Severity = HealthCheckSeverity.Info
                });
            }

            await Task.CompletedTask;
            return healthChecks;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during health check");
            healthChecks.Add(new HealthCheck
            {
                Name = "JavaScript Transform",
                Passed = false,
                Details = $"Health check failed: {ex.Message}",
                ExecutionTime = TimeSpan.Zero,
                Severity = HealthCheckSeverity.Critical
            });
            return healthChecks;
        }
    }

    /// <summary>
    /// Transforms a single chunk of data using Core services.
    /// </summary>
    private async Task<IChunk> TransformChunkAsync(IChunk chunk, CancellationToken cancellationToken)
    {
        if (_compiledScript == null || _outputSchema == null)
            throw new InvalidOperationException("Plugin not properly initialized");

        var transformedRows = new List<ArrayRow>();
        var routingResults = new Dictionary<string, List<IArrayRow>>();

        var rows = chunk.Rows.ToArray();
        for (int i = 0; i < rows.Length; i++)
        {
            var row = rows[i];
            
            // Create processing state for routing
            var processingState = new ProcessingState
            {
                RoutingResults = routingResults
            };
            
            // Create JavaScript context using Core service
            var context = _contextService.CreateContext(row, _inputSchema!, _outputSchema, processingState);
            
            // Execute JavaScript transformation using Core service
            var result = await _scriptEngine.ExecuteAsync(_compiledScript, context);
            
            // Check if the script executed successfully
            if (result.Success)
            {
                // If script returned true, include the row in output
                if (result.ReturnValue is true)
                {
                    // Get the result row from the output context
                    var transformedRow = context.Output.GetResultRow();
                    transformedRows.Add((ArrayRow)transformedRow);
                }
                
                // Handle routing targets
                foreach (var target in context.Route.GetTargets())
                {
                    if (!routingResults.ContainsKey(target.QueueName))
                        routingResults[target.QueueName] = new List<IArrayRow>();
                        
                    var rowToRoute = target.Mode == RoutingMode.Copy ? row : row;
                    routingResults[target.QueueName].Add(rowToRoute);
                }
            }
            else
            {
                _logger.LogError("JavaScript execution failed for row {RowIndex}: {ErrorMessage}", i, result.ErrorMessage);
                // Continue processing other rows even if one fails
            }
            
            IncrementProcessedRows();
        }

        // Create new chunk with transformed rows
        var newChunk = chunk.WithRows(transformedRows);
        
        // Add routing results to chunk metadata if any
        if (routingResults.Count > 0)
        {
            var metadata = new Dictionary<string, object>(chunk.Metadata)
            {
                ["RoutingResults"] = routingResults
            };
            newChunk = newChunk.WithMetadata(metadata);
        }
        
        return newChunk;
    }

    /// <summary>
    /// Creates output schema from configuration.
    /// </summary>
    private async Task<ISchema> CreateOutputSchemaAsync(OutputSchemaConfiguration outputConfig)
    {
        var columns = new List<ColumnDefinition>();
        
        for (int i = 0; i < outputConfig.Fields.Count; i++)
        {
            var field = outputConfig.Fields[i];
            var dataType = field.Type.ToLower() switch
            {
                "string" => typeof(string),
                "integer" => typeof(int),
                "decimal" => typeof(decimal),
                "double" => typeof(double),
                "boolean" => typeof(bool),
                "datetime" => typeof(DateTime),
                "date" => typeof(DateOnly),
                "time" => typeof(TimeOnly),
                _ => typeof(string) // Default to string for unknown types
            };

            // Create column definition using FlowEngine types
            var column = new ColumnDefinition
            {
                Name = field.Name,
                DataType = dataType,
                IsNullable = !field.Required,
                Index = i
            };
            columns.Add(column);
        }

        // Create schema using FlowEngine's static factory method
        var schema = Schema.GetOrCreate(columns);
        
        await Task.CompletedTask;
        return schema;
    }

    /// <summary>
    /// Increments the processed rows counter for performance monitoring.
    /// </summary>
    internal void IncrementProcessedRows()
    {
        Interlocked.Increment(ref _processedRows);
    }
}