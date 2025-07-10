using System.Diagnostics;

namespace FlowEngine.Abstractions.ErrorHandling;

/// <summary>
/// Provides contextual information for error handling and recovery operations.
/// Centralizes error context collection for consistent diagnostics.
/// </summary>
public sealed class ErrorContext
{
    /// <summary>
    /// Gets the operation being performed when the error occurred.
    /// </summary>
    public string OperationName { get; }
    
    /// <summary>
    /// Gets the component where the error occurred.
    /// </summary>
    public string Component { get; }
    
    /// <summary>
    /// Gets additional metadata about the error context.
    /// </summary>
    public IReadOnlyDictionary<string, object> Metadata { get; }
    
    /// <summary>
    /// Gets the activity ID for distributed tracing.
    /// </summary>
    public string? ActivityId { get; }
    
    /// <summary>
    /// Gets the correlation ID for request tracking.
    /// </summary>
    public string? CorrelationId { get; }
    
    /// <summary>
    /// Gets the user context if available.
    /// </summary>
    public string? UserId { get; }
    
    /// <summary>
    /// Gets the timestamp when the context was created.
    /// </summary>
    public DateTimeOffset Timestamp { get; }
    
    /// <summary>
    /// Gets the thread ID where the error occurred.
    /// </summary>
    public int ThreadId { get; }
    
    /// <summary>
    /// Gets the process ID where the error occurred.
    /// </summary>
    public int ProcessId { get; }
    
    /// <summary>
    /// Gets the machine name where the error occurred.
    /// </summary>
    public string MachineName { get; }
    
    private ErrorContext(
        string operationName,
        string component,
        Dictionary<string, object> metadata,
        string? activityId,
        string? correlationId,
        string? userId)
    {
        OperationName = operationName;
        Component = component;
        Metadata = metadata.AsReadOnly();
        ActivityId = activityId;
        CorrelationId = correlationId;
        UserId = userId;
        Timestamp = DateTimeOffset.UtcNow;
        ThreadId = Environment.CurrentManagedThreadId;
        ProcessId = Environment.ProcessId;
        MachineName = Environment.MachineName;
    }
    
    /// <summary>
    /// Creates a new error context builder.
    /// </summary>
    public static ErrorContextBuilder Create(string operationName, string component)
    {
        return new ErrorContextBuilder(operationName, component);
    }
    
    /// <summary>
    /// Creates error context from the current execution environment.
    /// </summary>
    public static ErrorContext FromCurrentContext(string operationName, string component)
    {
        var builder = Create(operationName, component);
        
        // Add current activity information if available
        var activity = Activity.Current;
        if (activity != null)
        {
            builder.WithActivityId(activity.Id);
            
            // Add activity tags as metadata
            foreach (var tag in activity.Tags)
            {
                builder.WithMetadata($"Activity_{tag.Key}", tag.Value ?? "");
            }
        }
        
        return builder.Build();
    }
    
    /// <summary>
    /// Converts the error context to a dictionary suitable for logging.
    /// </summary>
    public Dictionary<string, object> ToLogDictionary()
    {
        var logData = new Dictionary<string, object>
        {
            ["OperationName"] = OperationName,
            ["Component"] = Component,
            ["Timestamp"] = Timestamp,
            ["ThreadId"] = ThreadId,
            ["ProcessId"] = ProcessId,
            ["MachineName"] = MachineName
        };
        
        if (!string.IsNullOrEmpty(ActivityId))
            logData["ActivityId"] = ActivityId;
            
        if (!string.IsNullOrEmpty(CorrelationId))
            logData["CorrelationId"] = CorrelationId;
            
        if (!string.IsNullOrEmpty(UserId))
            logData["UserId"] = UserId;
        
        // Add metadata with prefix
        foreach (var kvp in Metadata)
        {
            logData[$"Context_{kvp.Key}"] = kvp.Value;
        }
        
        return logData;
    }
    
    /// <summary>
    /// Builder class for creating error contexts with fluent API.
    /// </summary>
    public sealed class ErrorContextBuilder
    {
        private readonly string _operationName;
        private readonly string _component;
        private readonly Dictionary<string, object> _metadata = new();
        private string? _activityId;
        private string? _correlationId;
        private string? _userId;
        
        internal ErrorContextBuilder(string operationName, string component)
        {
            _operationName = operationName ?? throw new ArgumentNullException(nameof(operationName));
            _component = component ?? throw new ArgumentNullException(nameof(component));
        }
        
        /// <summary>
        /// Adds metadata to the error context.
        /// </summary>
        public ErrorContextBuilder WithMetadata(string key, object value)
        {
            _metadata[key] = value;
            return this;
        }
        
        /// <summary>
        /// Adds multiple metadata items to the error context.
        /// </summary>
        public ErrorContextBuilder WithMetadata(IEnumerable<KeyValuePair<string, object>> metadata)
        {
            foreach (var kvp in metadata)
            {
                _metadata[kvp.Key] = kvp.Value;
            }
            return this;
        }
        
        /// <summary>
        /// Sets the activity ID for distributed tracing.
        /// </summary>
        public ErrorContextBuilder WithActivityId(string? activityId)
        {
            _activityId = activityId;
            return this;
        }
        
        /// <summary>
        /// Sets the correlation ID for request tracking.
        /// </summary>
        public ErrorContextBuilder WithCorrelationId(string? correlationId)
        {
            _correlationId = correlationId;
            return this;
        }
        
        /// <summary>
        /// Sets the user context.
        /// </summary>
        public ErrorContextBuilder WithUserId(string? userId)
        {
            _userId = userId;
            return this;
        }
        
        /// <summary>
        /// Adds plugin-specific context information.
        /// </summary>
        public ErrorContextBuilder WithPluginContext(string pluginName, string? assemblyPath = null, string? typeName = null)
        {
            WithMetadata("PluginName", pluginName);
            if (!string.IsNullOrEmpty(assemblyPath))
                WithMetadata("AssemblyPath", assemblyPath);
            if (!string.IsNullOrEmpty(typeName))
                WithMetadata("TypeName", typeName);
            return this;
        }
        
        /// <summary>
        /// Adds data processing context information.
        /// </summary>
        public ErrorContextBuilder WithDataContext(int? rowIndex = null, string? columnName = null, int? chunkSize = null)
        {
            if (rowIndex.HasValue)
                WithMetadata("RowIndex", rowIndex.Value);
            if (!string.IsNullOrEmpty(columnName))
                WithMetadata("ColumnName", columnName);
            if (chunkSize.HasValue)
                WithMetadata("ChunkSize", chunkSize.Value);
            return this;
        }
        
        /// <summary>
        /// Adds performance context information.
        /// </summary>
        public ErrorContextBuilder WithPerformanceContext(TimeSpan? duration = null, long? memoryUsed = null, int? itemsProcessed = null)
        {
            if (duration.HasValue)
                WithMetadata("Duration", duration.Value.TotalMilliseconds);
            if (memoryUsed.HasValue)
                WithMetadata("MemoryUsedBytes", memoryUsed.Value);
            if (itemsProcessed.HasValue)
                WithMetadata("ItemsProcessed", itemsProcessed.Value);
            return this;
        }
        
        /// <summary>
        /// Builds the error context.
        /// </summary>
        public ErrorContext Build()
        {
            return new ErrorContext(_operationName, _component, _metadata, _activityId, _correlationId, _userId);
        }
    }
}

/// <summary>
/// Provides factory methods for creating common error contexts.
/// </summary>
public static class ErrorContextFactory
{
    /// <summary>
    /// Creates error context for plugin operations.
    /// </summary>
    public static ErrorContext ForPlugin(string operationName, string pluginName, string? assemblyPath = null, string? typeName = null)
    {
        return ErrorContext.Create(operationName, "PluginManager")
            .WithPluginContext(pluginName, assemblyPath, typeName)
            .Build();
    }
    
    /// <summary>
    /// Creates error context for data processing operations.
    /// </summary>
    public static ErrorContext ForDataProcessing(string operationName, int? rowIndex = null, string? columnName = null, int? chunkSize = null)
    {
        return ErrorContext.Create(operationName, "DataProcessor")
            .WithDataContext(rowIndex, columnName, chunkSize)
            .Build();
    }
    
    /// <summary>
    /// Creates error context for JavaScript engine operations.
    /// </summary>
    public static ErrorContext ForJavaScript(string operationName, string? scriptName = null, int? lineNumber = null)
    {
        var builder = ErrorContext.Create(operationName, "JavaScriptEngine");
        
        if (!string.IsNullOrEmpty(scriptName))
            builder.WithMetadata("ScriptName", scriptName);
        if (lineNumber.HasValue)
            builder.WithMetadata("LineNumber", lineNumber.Value);
            
        return builder.Build();
    }
    
    /// <summary>
    /// Creates error context for I/O operations.
    /// </summary>
    public static ErrorContext ForIO(string operationName, string? filePath = null, long? fileSize = null)
    {
        var builder = ErrorContext.Create(operationName, "FileSystem");
        
        if (!string.IsNullOrEmpty(filePath))
        {
            builder.WithMetadata("FilePath", filePath);
            builder.WithMetadata("FileName", Path.GetFileName(filePath));
        }
        if (fileSize.HasValue)
            builder.WithMetadata("FileSize", fileSize.Value);
            
        return builder.Build();
    }
}