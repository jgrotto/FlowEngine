using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace FlowEngine.Core.Monitoring;

/// <summary>
/// Structured logger that provides consistent, searchable logging for production systems.
/// Implements high-performance logging with structured data for monitoring and alerting.
/// </summary>
public sealed class StructuredLogger : IDisposable
{
    private readonly ILogger _logger;
    private readonly StructuredLoggingConfiguration _configuration;
    private readonly Dictionary<string, object> _contextData = new();
    private readonly object _lock = new();
    private bool _disposed;

    /// <summary>
    /// Gets the current log context data.
    /// </summary>
    public IReadOnlyDictionary<string, object> ContextData
    {
        get
        {
            lock (_lock)
            {
                return _contextData.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
            }
        }
    }

    public StructuredLogger(ILogger logger, StructuredLoggingConfiguration? configuration = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _configuration = configuration ?? new StructuredLoggingConfiguration();

        // Add default context
        AddContext("MachineName", Environment.MachineName);
        AddContext("ProcessId", Environment.ProcessId);
        AddContext("ThreadId", Environment.CurrentManagedThreadId);
    }

    /// <summary>
    /// Adds persistent context data that will be included in all log entries.
    /// </summary>
    public void AddContext(string key, object value)
    {
        if (string.IsNullOrEmpty(key))
        {
            throw new ArgumentException("Key cannot be null or empty", nameof(key));
        }

        lock (_lock)
        {
            _contextData[key] = value;
        }
    }

    /// <summary>
    /// Removes context data.
    /// </summary>
    public void RemoveContext(string key)
    {
        if (string.IsNullOrEmpty(key))
        {
            return;
        }

        lock (_lock)
        {
            _contextData.Remove(key);
        }
    }

    /// <summary>
    /// Logs a structured message with performance metrics.
    /// </summary>
    public void LogPerformance(string operation, TimeSpan duration, Dictionary<string, object>? metrics = null, LogLevel logLevel = LogLevel.Information)
    {
        var perfData = new Dictionary<string, object>
        {
            ["operation"] = operation,
            ["duration_ms"] = duration.TotalMilliseconds,
            ["duration_ticks"] = duration.Ticks,
            ["category"] = "Performance"
        };

        if (metrics != null)
        {
            foreach (var kvp in metrics)
            {
                perfData[$"metric_{kvp.Key}"] = kvp.Value;
            }
        }

        LogStructured(logLevel, "Performance metric recorded for operation {Operation} in {Duration}ms",
            perfData, operation, duration.TotalMilliseconds);
    }

    /// <summary>
    /// Logs plugin-related structured information.
    /// </summary>
    public void LogPlugin(LogLevel logLevel, string message, string pluginName, string? assemblyPath = null,
        string? typeName = null, Dictionary<string, object>? additionalData = null)
    {
        var pluginData = new Dictionary<string, object>
        {
            ["plugin_name"] = pluginName,
            ["category"] = "Plugin"
        };

        if (!string.IsNullOrEmpty(assemblyPath))
        {
            pluginData["assembly_path"] = assemblyPath;
            pluginData["assembly_name"] = Path.GetFileName(assemblyPath);
        }

        if (!string.IsNullOrEmpty(typeName))
        {
            pluginData["type_name"] = typeName;
        }

        if (additionalData != null)
        {
            foreach (var kvp in additionalData)
            {
                pluginData[kvp.Key] = kvp.Value;
            }
        }

        LogStructured(logLevel, message, pluginData);
    }

    /// <summary>
    /// Logs data processing structured information.
    /// </summary>
    public void LogDataProcessing(LogLevel logLevel, string message, int? rowCount = null, int? columnCount = null,
        string? schemaName = null, TimeSpan? processingTime = null, Dictionary<string, object>? additionalData = null)
    {
        var dataData = new Dictionary<string, object>
        {
            ["category"] = "DataProcessing"
        };

        if (rowCount.HasValue)
        {
            dataData["row_count"] = rowCount.Value;
        }

        if (columnCount.HasValue)
        {
            dataData["column_count"] = columnCount.Value;
        }

        if (!string.IsNullOrEmpty(schemaName))
        {
            dataData["schema_name"] = schemaName;
        }

        if (processingTime.HasValue)
        {
            dataData["processing_time_ms"] = processingTime.Value.TotalMilliseconds;
            dataData["rows_per_second"] = rowCount.HasValue && processingTime.Value.TotalSeconds > 0
                ? rowCount.Value / processingTime.Value.TotalSeconds : 0;
        }

        if (additionalData != null)
        {
            foreach (var kvp in additionalData)
            {
                dataData[kvp.Key] = kvp.Value;
            }
        }

        LogStructured(logLevel, message, dataData);
    }

    /// <summary>
    /// Logs memory-related structured information.
    /// </summary>
    public void LogMemory(LogLevel logLevel, string message, long? memoryUsed = null, long? memoryRequested = null,
        double? memoryPressure = null, Dictionary<string, object>? additionalData = null)
    {
        var memoryData = new Dictionary<string, object>
        {
            ["category"] = "Memory",
            ["total_memory_mb"] = GC.GetTotalMemory(false) / (1024.0 * 1024.0),
            ["working_set_mb"] = Environment.WorkingSet / (1024.0 * 1024.0)
        };

        if (memoryUsed.HasValue)
        {
            memoryData["memory_used_mb"] = memoryUsed.Value / (1024.0 * 1024.0);
        }

        if (memoryRequested.HasValue)
        {
            memoryData["memory_requested_mb"] = memoryRequested.Value / (1024.0 * 1024.0);
        }

        if (memoryPressure.HasValue)
        {
            memoryData["memory_pressure"] = memoryPressure.Value;
        }

        if (additionalData != null)
        {
            foreach (var kvp in additionalData)
            {
                memoryData[kvp.Key] = kvp.Value;
            }
        }

        LogStructured(logLevel, message, memoryData);
    }

    /// <summary>
    /// Logs an error with structured context and exception details.
    /// </summary>
    public void LogError(Exception exception, string message, Dictionary<string, object>? additionalData = null,
        [CallerMemberName] string? memberName = null, [CallerFilePath] string? filePath = null, [CallerLineNumber] int lineNumber = 0)
    {
        var errorData = new Dictionary<string, object>
        {
            ["category"] = "Error",
            ["exception_type"] = exception.GetType().Name,
            ["exception_message"] = exception.Message,
            ["stack_trace"] = exception.StackTrace ?? "",
            ["caller_member"] = memberName ?? "Unknown",
            ["caller_file"] = Path.GetFileName(filePath ?? "Unknown"),
            ["caller_line"] = lineNumber
        };

        if (exception.InnerException != null)
        {
            errorData["inner_exception_type"] = exception.InnerException.GetType().Name;
            errorData["inner_exception_message"] = exception.InnerException.Message;
        }

        if (additionalData != null)
        {
            foreach (var kvp in additionalData)
            {
                errorData[kvp.Key] = kvp.Value;
            }
        }

        LogStructured(LogLevel.Error, message, errorData, exception);
    }

    /// <summary>
    /// Logs structured information with automatic context enrichment.
    /// </summary>
    public void LogStructured(LogLevel logLevel, string message, Dictionary<string, object>? data = null, params object[] args)
    {
        if (!_logger.IsEnabled(logLevel))
        {
            return;
        }

        var enrichedData = new Dictionary<string, object>();

        // Add persistent context
        lock (_lock)
        {
            foreach (var kvp in _contextData)
            {
                enrichedData[kvp.Key] = kvp.Value;
            }
        }

        // Add runtime context
        enrichedData["timestamp"] = DateTimeOffset.UtcNow;
        enrichedData["log_level"] = logLevel.ToString();

        // Add activity context if available
        var activity = Activity.Current;
        if (activity != null)
        {
            enrichedData["activity_id"] = activity.Id;
            enrichedData["trace_id"] = activity.TraceId.ToString();
            enrichedData["span_id"] = activity.SpanId.ToString();

            // Add activity tags
            foreach (var tag in activity.Tags.Take(_configuration.MaxActivityTags))
            {
                enrichedData[$"activity_{tag.Key}"] = tag.Value ?? "";
            }
        }

        // Add provided data
        if (data != null)
        {
            foreach (var kvp in data)
            {
                enrichedData[kvp.Key] = kvp.Value;
            }
        }

        // Use log scope for structured data
        using var scope = _logger.BeginScope(enrichedData);

        if (args.Length > 0 && args[0] is Exception exception)
        {
            _logger.Log(logLevel, exception, message, args.Skip(1).ToArray());
        }
        else
        {
            _logger.Log(logLevel, message, args);
        }

        // Optionally write to structured file if configured
        if (_configuration.EnableStructuredFileOutput)
        {
            WriteToStructuredFile(logLevel, message, enrichedData);
        }
    }

    /// <summary>
    /// Creates a performance measurement scope that automatically logs when disposed.
    /// </summary>
    public PerformanceScope MeasurePerformance(string operation, Dictionary<string, object>? initialMetrics = null)
    {
        return new PerformanceScope(this, operation, initialMetrics);
    }

    /// <summary>
    /// Creates a child logger with additional context.
    /// </summary>
    public StructuredLogger CreateChildLogger(string contextKey, object contextValue)
    {
        var childLogger = new StructuredLogger(_logger, _configuration);

        // Copy existing context
        lock (_lock)
        {
            foreach (var kvp in _contextData)
            {
                childLogger.AddContext(kvp.Key, kvp.Value);
            }
        }

        // Add new context
        childLogger.AddContext(contextKey, contextValue);

        return childLogger;
    }

    private void WriteToStructuredFile(LogLevel logLevel, string message, Dictionary<string, object> data)
    {
        if (string.IsNullOrEmpty(_configuration.StructuredLogFilePath))
        {
            return;
        }

        try
        {
            var logEntry = new
            {
                timestamp = DateTimeOffset.UtcNow,
                level = logLevel.ToString(),
                message = message,
                data = data
            };

            var json = JsonSerializer.Serialize(logEntry, new JsonSerializerOptions
            {
                WriteIndented = false,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });

            // Append to file (in production, would use a more sophisticated file writer)
            File.AppendAllLines(_configuration.StructuredLogFilePath, new[] { json });
        }
        catch (Exception ex)
        {
            // Don't let logging errors crash the application
            _logger.LogWarning(ex, "Failed to write structured log to file");
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        try
        {
            lock (_lock)
            {
                _contextData.Clear();
            }
        }
        finally
        {
            _disposed = true;
        }
    }
}

/// <summary>
/// Configuration for structured logging.
/// </summary>
public sealed class StructuredLoggingConfiguration
{
    /// <summary>
    /// Gets or sets whether to enable structured file output.
    /// </summary>
    public bool EnableStructuredFileOutput { get; set; } = false;

    /// <summary>
    /// Gets or sets the file path for structured log output.
    /// </summary>
    public string? StructuredLogFilePath { get; set; }

    /// <summary>
    /// Gets or sets the maximum number of activity tags to include.
    /// </summary>
    public int MaxActivityTags { get; set; } = 10;

    /// <summary>
    /// Gets or sets whether to include sensitive data in logs.
    /// </summary>
    public bool IncludeSensitiveData { get; set; } = false;

    /// <summary>
    /// Gets or sets the maximum size of structured data to log.
    /// </summary>
    public int MaxStructuredDataSize { get; set; } = 4096;
}

/// <summary>
/// Performance measurement scope that automatically logs performance metrics when disposed.
/// </summary>
public sealed class PerformanceScope : IDisposable
{
    private readonly StructuredLogger _logger;
    private readonly string _operation;
    private readonly Dictionary<string, object> _metrics;
    private readonly Stopwatch _stopwatch;
    private readonly long _initialMemory;
    private bool _disposed;

    internal PerformanceScope(StructuredLogger logger, string operation, Dictionary<string, object>? initialMetrics)
    {
        _logger = logger;
        _operation = operation;
        _metrics = initialMetrics?.ToDictionary(kvp => kvp.Key, kvp => kvp.Value) ?? new Dictionary<string, object>();
        _stopwatch = Stopwatch.StartNew();
        _initialMemory = GC.GetTotalMemory(false);
    }

    /// <summary>
    /// Adds a metric to be included in the performance log.
    /// </summary>
    public void AddMetric(string key, object value)
    {
        _metrics[key] = value;
    }

    /// <summary>
    /// Adds multiple metrics to be included in the performance log.
    /// </summary>
    public void AddMetrics(Dictionary<string, object> metrics)
    {
        foreach (var kvp in metrics)
        {
            _metrics[kvp.Key] = kvp.Value;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        try
        {
            _stopwatch.Stop();
            var finalMemory = GC.GetTotalMemory(false);
            var memoryDelta = finalMemory - _initialMemory;

            var performanceMetrics = new Dictionary<string, object>(_metrics)
            {
                ["memory_delta_bytes"] = memoryDelta,
                ["memory_delta_mb"] = memoryDelta / (1024.0 * 1024.0),
                ["initial_memory_mb"] = _initialMemory / (1024.0 * 1024.0),
                ["final_memory_mb"] = finalMemory / (1024.0 * 1024.0)
            };

            _logger.LogPerformance(_operation, _stopwatch.Elapsed, performanceMetrics);
        }
        finally
        {
            _disposed = true;
        }
    }
}

/// <summary>
/// Factory for creating structured loggers with consistent configuration.
/// </summary>
public sealed class StructuredLoggerFactory
{
    private readonly ILoggerFactory _loggerFactory;
    private readonly StructuredLoggingConfiguration _configuration;

    public StructuredLoggerFactory(ILoggerFactory loggerFactory, StructuredLoggingConfiguration? configuration = null)
    {
        _loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
        _configuration = configuration ?? new StructuredLoggingConfiguration();
    }

    /// <summary>
    /// Creates a structured logger for the specified category.
    /// </summary>
    public StructuredLogger CreateLogger(string categoryName)
    {
        var logger = _loggerFactory.CreateLogger(categoryName);
        return new StructuredLogger(logger, _configuration);
    }

    /// <summary>
    /// Creates a structured logger for the specified type.
    /// </summary>
    public StructuredLogger CreateLogger<T>()
    {
        return CreateLogger(typeof(T).FullName ?? typeof(T).Name);
    }
}

/// <summary>
/// Extension methods for easier structured logging.
/// </summary>
public static class StructuredLoggerExtensions
{
    /// <summary>
    /// Logs a plugin loading event.
    /// </summary>
    public static void LogPluginLoading(this StructuredLogger logger, string pluginName, string assemblyPath, string typeName)
    {
        logger.LogPlugin(LogLevel.Information, "Loading plugin {PluginName} from {AssemblyPath}",
            pluginName, assemblyPath, typeName, new Dictionary<string, object>
            {
                ["event"] = "plugin_loading"
            });
    }

    /// <summary>
    /// Logs a plugin loaded event.
    /// </summary>
    public static void LogPluginLoaded(this StructuredLogger logger, string pluginName, TimeSpan loadTime)
    {
        logger.LogPlugin(LogLevel.Information, "Plugin {PluginName} loaded successfully in {LoadTime}ms",
            pluginName, additionalData: new Dictionary<string, object>
            {
                ["event"] = "plugin_loaded",
                ["load_time_ms"] = loadTime.TotalMilliseconds
            });
    }

    /// <summary>
    /// Logs a data processing event.
    /// </summary>
    public static void LogDataProcessed(this StructuredLogger logger, int rowCount, TimeSpan processingTime, string operation)
    {
        logger.LogDataProcessing(LogLevel.Information, "Processed {RowCount} rows in {ProcessingTime}ms for operation {Operation}",
            rowCount, processingTime: processingTime, additionalData: new Dictionary<string, object>
            {
                ["event"] = "data_processed",
                ["operation"] = operation,
                ["throughput_rows_per_second"] = rowCount / Math.Max(processingTime.TotalSeconds, 0.001)
            });
    }

    /// <summary>
    /// Logs a memory allocation event.
    /// </summary>
    public static void LogMemoryAllocated(this StructuredLogger logger, long bytes, string purpose)
    {
        logger.LogMemory(LogLevel.Debug, "Allocated {Bytes} bytes for {Purpose}", bytes, additionalData: new Dictionary<string, object>
        {
            ["event"] = "memory_allocated",
            ["purpose"] = purpose,
            ["allocation_mb"] = bytes / (1024.0 * 1024.0)
        });
    }
}
