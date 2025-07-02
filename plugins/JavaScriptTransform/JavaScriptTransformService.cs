using FlowEngine.Abstractions.Data;
using FlowEngine.Abstractions.Plugins;
using FlowEngine.Abstractions.Services;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Diagnostics;

namespace JavaScriptTransform;

/// <summary>
/// Core business logic service for JavaScriptTransform plugin implementing ArrayRow-optimized transformations.
/// Integrates with IScriptEngineService for high-performance JavaScript execution.
/// </summary>
public sealed class JavaScriptTransformService : IPluginService
{
    private readonly IScriptEngineService _scriptEngineService;
    private readonly IArrayRowFactory _arrayRowFactory;
    private readonly ILogger<JavaScriptTransformService> _logger;
    private JavaScriptTransformConfiguration _configuration;
    private readonly object _configurationLock = new();

    // Performance tracking
    private long _processedRows;
    private long _errorCount;
    private readonly ConcurrentBag<TimeSpan> _processingTimes = new();
    private DateTimeOffset _lastUpdated;

    // Script compilation cache
    private ICompiledScript? _compiledScript;
    private string? _lastScriptHash;

    // Field index caches for performance
    private int[]? _inputFieldIndexes;
    private int[]? _outputFieldIndexes;
    private string[]? _inputFieldNames;
    private string[]? _outputFieldNames;

    /// <summary>
    /// Initializes a new instance of the JavaScriptTransformService class.
    /// </summary>
    /// <param name="scriptEngineService">Script engine service for JavaScript execution</param>
    /// <param name="arrayRowFactory">Factory for creating ArrayRow instances</param>
    /// <param name="configuration">Plugin configuration</param>
    /// <param name="logger">Logger instance</param>
    public JavaScriptTransformService(
        IScriptEngineService scriptEngineService,
        IArrayRowFactory arrayRowFactory,
        JavaScriptTransformConfiguration configuration,
        ILogger<JavaScriptTransformService> logger)
    {
        _scriptEngineService = scriptEngineService ?? throw new ArgumentNullException(nameof(scriptEngineService));
        _arrayRowFactory = arrayRowFactory ?? throw new ArgumentNullException(nameof(arrayRowFactory));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _lastUpdated = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Initializes the service asynchronously.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Task representing the initialization operation</returns>
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Initializing JavaScriptTransformService");

        try
        {
            // Pre-compile and cache the script
            await CompileScriptAsync(cancellationToken);
            
            // Pre-calculate field indexes for performance
            CacheFieldIndexes();
            
            _logger.LogDebug("JavaScriptTransformService initialized successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize JavaScriptTransformService");
            throw;
        }
    }

    /// <summary>
    /// Starts the service asynchronously.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Task representing the start operation</returns>
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Starting JavaScriptTransformService");
        
        // Reset metrics
        _processedRows = 0;
        _errorCount = 0;
        _processingTimes.Clear();
        _lastUpdated = DateTimeOffset.UtcNow;
        
        await Task.CompletedTask;
    }

    /// <summary>
    /// Stops the service asynchronously.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Task representing the stop operation</returns>
    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Stopping JavaScriptTransformService");
        await Task.CompletedTask;
    }

    /// <summary>
    /// Processes a single ArrayRow through JavaScript transformation with optimized field access.
    /// </summary>
    /// <param name="row">Input row to transform</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Transformed row</returns>
    public async Task<IArrayRow> ProcessRowAsync(IArrayRow row, CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        
        try
        {
            // Create input data object for JavaScript with optimized field access
            var inputData = CreateInputDataObject(row);
            
            // Execute JavaScript transformation
            var result = await ExecuteScriptAsync(inputData, cancellationToken);
            
            // Convert result back to ArrayRow with optimized field assignment
            var outputRow = CreateOutputRow(result);
            
            stopwatch.Stop();
            _processingTimes.Add(stopwatch.Elapsed);
            Interlocked.Increment(ref _processedRows);
            _lastUpdated = DateTimeOffset.UtcNow;
            
            return outputRow;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            Interlocked.Increment(ref _errorCount);
            _lastUpdated = DateTimeOffset.UtcNow;
            
            _logger.LogError(ex, "Error processing row through JavaScript transform");
            
            if (!_configuration.ContinueOnError)
                throw;
            
            // Return empty row or original row based on configuration
            return _configuration.OutputSchema != null 
                ? _arrayRowFactory.CreateEmpty(_configuration.OutputSchema)
                : row;
        }
    }

    /// <summary>
    /// Processes multiple rows in batches for improved performance.
    /// </summary>
    /// <param name="rows">Input rows to transform</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Transformed rows</returns>
    public async IAsyncEnumerable<IArrayRow> ProcessRowsAsync(
        IAsyncEnumerable<IArrayRow> rows, 
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var batch = new List<IArrayRow>(_configuration.BatchSize);
        
        await foreach (var row in rows.WithCancellation(cancellationToken))
        {
            batch.Add(row);
            
            if (batch.Count >= _configuration.BatchSize)
            {
                await foreach (var processedRow in ProcessBatchAsync(batch, cancellationToken))
                {
                    yield return processedRow;
                }
                batch.Clear();
            }
        }
        
        // Process remaining rows
        if (batch.Count > 0)
        {
            await foreach (var processedRow in ProcessBatchAsync(batch, cancellationToken))
            {
                yield return processedRow;
            }
        }
    }

    /// <summary>
    /// Checks the health of the service.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Service health status</returns>
    public async Task<ServiceHealth> CheckHealthAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            // Check if script is compiled and ready
            if (_compiledScript == null)
            {
                return new ServiceHealth
                {
                    Status = HealthStatus.Unhealthy,
                    Message = "JavaScript script is not compiled",
                    CheckedAt = DateTimeOffset.UtcNow
                };
            }

            // Check script engine service health
            var scriptEngineHealth = await _scriptEngineService.GetHealthAsync(cancellationToken);
            if (!scriptEngineHealth.IsHealthy)
            {
                return new ServiceHealth
                {
                    Status = HealthStatus.Degraded,
                    Message = $"Script engine is unhealthy: {scriptEngineHealth.Message}",
                    CheckedAt = DateTimeOffset.UtcNow
                };
            }

            // Check error rate
            var errorRate = _processedRows > 0 ? (double)_errorCount / _processedRows : 0;
            if (errorRate > 0.1) // 10% error rate
            {
                return new ServiceHealth
                {
                    Status = HealthStatus.Degraded,
                    Message = $"High error rate: {errorRate:P2}",
                    CheckedAt = DateTimeOffset.UtcNow,
                    Details = new Dictionary<string, object>
                    {
                        ["ProcessedRows"] = _processedRows,
                        ["ErrorCount"] = _errorCount,
                        ["ErrorRate"] = errorRate
                    }.ToImmutableDictionary()
                };
            }

            return new ServiceHealth
            {
                Status = HealthStatus.Healthy,
                Message = "Service is healthy and JavaScript transform is ready",
                CheckedAt = DateTimeOffset.UtcNow,
                Details = new Dictionary<string, object>
                {
                    ["ProcessedRows"] = _processedRows,
                    ["ErrorCount"] = _errorCount,
                    ["ErrorRate"] = errorRate,
                    ["ScriptCompiled"] = _compiledScript != null,
                    ["FunctionName"] = _configuration.FunctionName
                }.ToImmutableDictionary()
            };
        }
        catch (Exception ex)
        {
            return new ServiceHealth
            {
                Status = HealthStatus.Unhealthy,
                Message = $"Health check failed: {ex.Message}",
                CheckedAt = DateTimeOffset.UtcNow
            };
        }
    }

    /// <summary>
    /// Gets current service metrics.
    /// </summary>
    /// <returns>Service performance metrics</returns>
    public ServiceMetrics GetMetrics()
    {
        var processingTimes = _processingTimes.ToArray();
        var averageTime = processingTimes.Length > 0 
            ? TimeSpan.FromTicks((long)processingTimes.Average(t => t.Ticks))
            : TimeSpan.Zero;

        var processingRate = _processedRows > 0 && _lastUpdated > DateTimeOffset.MinValue
            ? _processedRows / (DateTimeOffset.UtcNow - _lastUpdated).TotalSeconds
            : 0;

        return new ServiceMetrics
        {
            ProcessedRows = _processedRows,
            ErrorCount = _errorCount,
            ProcessingRate = processingRate,
            AverageProcessingTime = averageTime,
            MemoryUsage = GC.GetTotalMemory(false),
            LastUpdated = _lastUpdated
        };
    }

    /// <summary>
    /// Hot-swaps the service configuration.
    /// </summary>
    /// <param name="newConfiguration">New configuration</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Task representing the hot-swap operation</returns>
    public async Task HotSwapConfigurationAsync(JavaScriptTransformConfiguration newConfiguration, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Performing hot-swap configuration update for JavaScript transform");

        lock (_configurationLock)
        {
            _configuration = newConfiguration ?? throw new ArgumentNullException(nameof(newConfiguration));
        }

        // Recompile script if it changed
        await CompileScriptAsync(cancellationToken);
        
        // Recalculate field indexes
        CacheFieldIndexes();

        _logger.LogInformation("Hot-swap configuration update completed for JavaScript transform");
    }

    /// <summary>
    /// Disposes the service and releases resources.
    /// </summary>
    public void Dispose()
    {
        _logger.LogInformation("JavaScript transform service disposed");
    }

    /// <summary>
    /// Compiles the JavaScript script and caches it for performance.
    /// </summary>
    private async Task CompileScriptAsync(CancellationToken cancellationToken)
    {
        var scriptCode = _configuration.GetEffectiveScriptCode();
        var scriptHash = ComputeScriptHash(scriptCode);

        // Check if script has changed
        if (_configuration.EnableScriptCaching && _lastScriptHash == scriptHash && _compiledScript != null)
        {
            _logger.LogDebug("Using cached compiled script");
            return;
        }

        _logger.LogDebug("Compiling JavaScript script");

        try
        {
            var compilationOptions = new ScriptCompilationOptions
            {
                EnableStrictMode = true,
                EnableSecurityValidation = true,
                EnableDebugging = _configuration.EnableDetailedErrors
            };
            
            _compiledScript = await _scriptEngineService.CompileAsync(scriptCode, compilationOptions, cancellationToken);
            _lastScriptHash = scriptHash;
            
            _logger.LogDebug("JavaScript script compiled successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to compile JavaScript script");
            throw;
        }
    }

    /// <summary>
    /// Pre-calculates and caches field indexes for optimal ArrayRow access.
    /// </summary>
    private void CacheFieldIndexes()
    {
        // Cache input field information
        if (_configuration.InputSchema != null)
        {
            var inputColumns = _configuration.InputSchema.Columns.ToArray();
            _inputFieldNames = inputColumns.Select(c => c.Name).ToArray();
            _inputFieldIndexes = Enumerable.Range(0, inputColumns.Length).ToArray();
        }

        // Cache output field information
        if (_configuration.OutputSchema != null)
        {
            var outputColumns = _configuration.OutputSchema.Columns.ToArray();
            _outputFieldNames = outputColumns.Select(c => c.Name).ToArray();
            _outputFieldIndexes = Enumerable.Range(0, outputColumns.Length).ToArray();
        }

        _logger.LogDebug("Field indexes cached for optimal ArrayRow access");
    }

    /// <summary>
    /// Creates an input data object for JavaScript with optimized field access.
    /// </summary>
    private Dictionary<string, object?> CreateInputDataObject(IArrayRow row)
    {
        var inputData = new Dictionary<string, object?>();

        if (_inputFieldNames != null && _inputFieldIndexes != null)
        {
            // Use pre-calculated indexes for O(1) access
            for (int i = 0; i < _inputFieldNames.Length && i < _inputFieldIndexes.Length; i++)
            {
                var fieldName = _inputFieldNames[i];
                var fieldIndex = _inputFieldIndexes[i];
                
                if (fieldIndex < row.ColumnCount)
                {
                    inputData[fieldName] = row[fieldIndex];
                }
            }
        }
        else
        {
            // Fallback to using row's column names
            for (int i = 0; i < row.ColumnCount; i++)
            {
                var columnName = row.ColumnNames[i];
                inputData[columnName] = row[i];
            }
        }

        return inputData;
    }

    /// <summary>
    /// Executes the JavaScript transformation function.
    /// </summary>
    private async Task<object?> ExecuteScriptAsync(Dictionary<string, object?> inputData, CancellationToken cancellationToken)
    {
        if (_compiledScript == null)
            throw new InvalidOperationException("Script is not compiled");

        var executionOptions = new ScriptExecutionOptions
        {
            TimeoutMs = _configuration.ScriptTimeoutMs,
            MaxMemoryBytes = _configuration.MaxMemoryBytes,
            GlobalVariables = _configuration.GlobalVariables.ToImmutableDictionary(),
            EnableDetailedErrors = _configuration.EnableDetailedErrors
        };

        return await _scriptEngineService.ExecuteAsync(
            _compiledScript, 
            _configuration.FunctionName, 
            new object[] { inputData }, 
            executionOptions, 
            cancellationToken);
    }

    /// <summary>
    /// Creates an output ArrayRow from JavaScript execution result.
    /// </summary>
    private IArrayRow CreateOutputRow(object? result)
    {
        if (_configuration.OutputSchema == null)
            throw new InvalidOperationException("Output schema is required for creating output rows");

        if (result is not Dictionary<string, object?> resultDict)
        {
            throw new InvalidOperationException($"JavaScript transform must return an object, got: {result?.GetType().Name ?? "null"}");
        }

        var builder = _arrayRowFactory.CreateBuilder(_configuration.OutputSchema);

        // Use pre-calculated field information for optimal performance
        if (_outputFieldNames != null && _outputFieldIndexes != null)
        {
            for (int i = 0; i < _outputFieldNames.Length; i++)
            {
                var fieldName = _outputFieldNames[i];
                if (resultDict.TryGetValue(fieldName, out var value))
                {
                    builder.Set(i, value);
                }
            }
        }
        else
        {
            // Fallback to dictionary-based approach
            foreach (var kvp in resultDict)
            {
                try
                {
                    builder.Set(kvp.Key, kvp.Value);
                }
                catch (ArgumentException)
                {
                    // Field not in output schema, skip
                    if (_configuration.EnableDetailedErrors)
                    {
                        _logger.LogWarning("Field '{FieldName}' from JavaScript result not found in output schema", kvp.Key);
                    }
                }
            }
        }

        return builder.Build(_configuration.ValidateOutput);
    }

    /// <summary>
    /// Processes a batch of rows with optional parallel processing.
    /// </summary>
    private async IAsyncEnumerable<IArrayRow> ProcessBatchAsync(
        IList<IArrayRow> batch, 
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (_configuration.EnableParallelProcessing && batch.Count > 1)
        {
            var parallelOptions = new ParallelQuery<IArrayRow>(batch)
                .WithDegreeOfParallelism(_configuration.MaxDegreeOfParallelism)
                .WithCancellation(cancellationToken);

            var results = new ConcurrentBag<(int Index, IArrayRow Row)>();
            var exceptions = new ConcurrentBag<Exception>();

            await Task.Run(() =>
            {
                try
                {
                    parallelOptions.ForAll(row =>
                    {
                        try
                        {
                            var index = batch.IndexOf(row);
                            var processedRow = ProcessRowAsync(row, cancellationToken).GetAwaiter().GetResult();
                            results.Add((index, processedRow));
                        }
                        catch (Exception ex)
                        {
                            exceptions.Add(ex);
                        }
                    });
                }
                catch (Exception ex)
                {
                    exceptions.Add(ex);
                }
            }, cancellationToken);

            if (exceptions.Count > 0 && !_configuration.ContinueOnError)
            {
                throw new AggregateException("Batch processing failed", exceptions);
            }

            // Return results in original order
            var orderedResults = results.OrderBy(r => r.Index).Select(r => r.Row);
            foreach (var result in orderedResults)
            {
                yield return result;
            }
        }
        else
        {
            // Sequential processing
            foreach (var row in batch)
            {
                yield return await ProcessRowAsync(row, cancellationToken);
            }
        }
    }

    /// <summary>
    /// Computes a hash of the script content for caching purposes.
    /// </summary>
    private static string ComputeScriptHash(string script)
    {
        using var sha256 = System.Security.Cryptography.SHA256.Create();
        var hash = sha256.ComputeHash(System.Text.Encoding.UTF8.GetBytes(script));
        return Convert.ToBase64String(hash);
    }
}

