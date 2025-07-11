using System.Runtime.Serialization;
using FlowEngine.Abstractions.Data;

namespace FlowEngine.Abstractions.ErrorHandling;

/// <summary>
/// Base exception class for all FlowEngine operations with structured error information.
/// Provides consistent error handling across the entire FlowEngine system.
/// </summary>
[Serializable]
public abstract class FlowEngineException : Exception
{
    /// <summary>
    /// Gets the standardized error code for this exception.
    /// </summary>
    public ErrorCode ErrorCode { get; }

    /// <summary>
    /// Gets the severity level of this error.
    /// </summary>
    public ErrorSeverity Severity => ErrorCode.GetSeverity();

    /// <summary>
    /// Gets whether this error is potentially retryable.
    /// </summary>
    public bool IsRetryable => ErrorCode.IsRetryable();

    /// <summary>
    /// Gets additional context information about the error.
    /// </summary>
    public IReadOnlyDictionary<string, object> Context { get; }

    /// <summary>
    /// Gets the operation that was being performed when the error occurred.
    /// </summary>
    public string? OperationName { get; }

    /// <summary>
    /// Gets the timestamp when the error occurred.
    /// </summary>
    public DateTimeOffset Timestamp { get; }

    /// <summary>
    /// Initializes a new instance of FlowEngineException.
    /// </summary>
    protected FlowEngineException(
        ErrorCode errorCode,
        string message,
        string? operationName = null,
        Exception? innerException = null,
        Dictionary<string, object>? context = null)
        : base(message, innerException)
    {
        ErrorCode = errorCode;
        OperationName = operationName;
        Timestamp = DateTimeOffset.UtcNow;
        Context = context?.AsReadOnly() ?? new Dictionary<string, object>().AsReadOnly();
    }

    /// <summary>
    /// Serialization constructor.
    /// </summary>
    protected FlowEngineException(SerializationInfo info, StreamingContext context)
        : base(info, context)
    {
        ErrorCode = (ErrorCode)(info.GetValue(nameof(ErrorCode), typeof(ErrorCode)) ?? ErrorCode.Unknown);
        OperationName = info.GetString(nameof(OperationName));
        Timestamp = info.GetValue(nameof(Timestamp), typeof(DateTimeOffset)) is DateTimeOffset ts ? ts : DateTimeOffset.UtcNow;

        var contextData = info.GetValue(nameof(Context), typeof(Dictionary<string, object>)) as Dictionary<string, object>;
        Context = contextData?.AsReadOnly() ?? new Dictionary<string, object>().AsReadOnly();
    }

    /// <summary>
    /// Serialization method.
    /// </summary>
    [Obsolete]
    public override void GetObjectData(SerializationInfo info, StreamingContext context)
    {
        base.GetObjectData(info, context);
        info.AddValue(nameof(ErrorCode), ErrorCode);
        info.AddValue(nameof(OperationName), OperationName);
        info.AddValue(nameof(Timestamp), Timestamp);
        info.AddValue(nameof(Context), Context.ToDictionary(kvp => kvp.Key, kvp => kvp.Value));
    }

    /// <summary>
    /// Creates a detailed error message with all context information.
    /// </summary>
    public virtual string GetDetailedMessage()
    {
        var details = new List<string>
        {
            $"Error: {Message}",
            $"Code: {ErrorCode} ({(int)ErrorCode})",
            $"Severity: {Severity}",
            $"Time: {Timestamp:yyyy-MM-dd HH:mm:ss.fff} UTC"
        };

        if (!string.IsNullOrEmpty(OperationName))
        {
            details.Add($"Operation: {OperationName}");
        }

        if (Context.Any())
        {
            details.Add("Context:");
            foreach (var kvp in Context)
            {
                details.Add($"  {kvp.Key}: {kvp.Value}");
            }
        }

        if (InnerException != null)
        {
            details.Add($"Inner Exception: {InnerException.GetType().Name}: {InnerException.Message}");
        }

        return string.Join(Environment.NewLine, details);
    }

    /// <summary>
    /// Creates a summary suitable for logging and monitoring.
    /// </summary>
    public virtual Dictionary<string, object> ToLogContext()
    {
        var logContext = new Dictionary<string, object>
        {
            ["ErrorCode"] = ErrorCode,
            ["ErrorCodeNumeric"] = (int)ErrorCode,
            ["Severity"] = Severity.ToString(),
            ["SeverityNumeric"] = (int)Severity,
            ["IsRetryable"] = IsRetryable,
            ["Timestamp"] = Timestamp,
            ["Message"] = Message
        };

        if (!string.IsNullOrEmpty(OperationName))
        {
            logContext["OperationName"] = OperationName;
        }

        if (InnerException != null)
        {
            logContext["InnerExceptionType"] = InnerException.GetType().Name;
            logContext["InnerExceptionMessage"] = InnerException.Message;
        }

        // Add context data with prefix to avoid conflicts
        foreach (var kvp in Context)
        {
            logContext[$"Context_{kvp.Key}"] = kvp.Value;
        }

        return logContext;
    }
}

/// <summary>
/// Exception thrown when schema validation fails with enhanced context.
/// </summary>
[Serializable]
public sealed class FlowEngineSchemaException : FlowEngineException
{
    /// <summary>
    /// Gets the source schema that caused the validation failure.
    /// </summary>
    public ISchema? SourceSchema { get; }

    /// <summary>
    /// Gets the target schema that caused the validation failure.
    /// </summary>
    public ISchema? TargetSchema { get; }

    /// <summary>
    /// Gets the list of validation errors.
    /// </summary>
    public IReadOnlyList<string> ValidationErrors { get; }

    public FlowEngineSchemaException(
        string message,
        ISchema? sourceSchema = null,
        ISchema? targetSchema = null,
        IReadOnlyList<string>? validationErrors = null,
        string? operationName = null,
        Exception? innerException = null)
        : base(ErrorCode.SchemaValidationFailed, message, operationName, innerException, CreateContext(sourceSchema, targetSchema, validationErrors))
    {
        SourceSchema = sourceSchema;
        TargetSchema = targetSchema;
        ValidationErrors = validationErrors ?? Array.Empty<string>();
    }

    private FlowEngineSchemaException(SerializationInfo info, StreamingContext context)
        : base(info, context)
    {
        ValidationErrors = info.GetValue(nameof(ValidationErrors), typeof(string[])) as string[] ?? Array.Empty<string>();
    }

    [Obsolete]
    public override void GetObjectData(SerializationInfo info, StreamingContext context)
    {
        base.GetObjectData(info, context);
        info.AddValue(nameof(ValidationErrors), ValidationErrors.ToArray());
    }

    private static Dictionary<string, object> CreateContext(ISchema? sourceSchema, ISchema? targetSchema, IReadOnlyList<string>? validationErrors)
    {
        var context = new Dictionary<string, object>();

        if (sourceSchema != null)
        {
            context["SourceSchemaSignature"] = sourceSchema.Signature;
            context["SourceColumnCount"] = sourceSchema.ColumnCount;
        }

        if (targetSchema != null)
        {
            context["TargetSchemaSignature"] = targetSchema.Signature;
            context["TargetColumnCount"] = targetSchema.ColumnCount;
        }

        if (validationErrors?.Any() == true)
        {
            context["ValidationErrorCount"] = validationErrors.Count;
            context["ValidationErrors"] = string.Join("; ", validationErrors);
        }

        return context;
    }
}

/// <summary>
/// Exception thrown when memory allocation fails with enhanced diagnostics.
/// </summary>
[Serializable]
public sealed class FlowEngineMemoryException : FlowEngineException
{
    /// <summary>
    /// Gets the requested allocation size in bytes.
    /// </summary>
    public long RequestedSize { get; }

    /// <summary>
    /// Gets the available memory at the time of failure in bytes.
    /// </summary>
    public long AvailableMemory { get; }

    /// <summary>
    /// Gets the memory pressure level at the time of failure (0.0 to 1.0).
    /// </summary>
    public double MemoryPressure { get; }

    public FlowEngineMemoryException(
        string message,
        long requestedSize = 0,
        long availableMemory = 0,
        double memoryPressure = 0.0,
        string? operationName = null,
        Exception? innerException = null)
        : base(ErrorCode.MemoryAllocationFailed, message, operationName, innerException, CreateContext(requestedSize, availableMemory, memoryPressure))
    {
        RequestedSize = requestedSize;
        AvailableMemory = availableMemory;
        MemoryPressure = memoryPressure;
    }

    private FlowEngineMemoryException(SerializationInfo info, StreamingContext context)
        : base(info, context)
    {
        RequestedSize = info.GetInt64(nameof(RequestedSize));
        AvailableMemory = info.GetInt64(nameof(AvailableMemory));
        MemoryPressure = info.GetDouble(nameof(MemoryPressure));
    }

    [Obsolete]
    public override void GetObjectData(SerializationInfo info, StreamingContext context)
    {
        base.GetObjectData(info, context);
        info.AddValue(nameof(RequestedSize), RequestedSize);
        info.AddValue(nameof(AvailableMemory), AvailableMemory);
        info.AddValue(nameof(MemoryPressure), MemoryPressure);
    }

    private static Dictionary<string, object> CreateContext(long requestedSize, long availableMemory, double memoryPressure)
    {
        return new Dictionary<string, object>
        {
            ["RequestedSizeMB"] = requestedSize / (1024.0 * 1024.0),
            ["AvailableMemoryMB"] = availableMemory / (1024.0 * 1024.0),
            ["MemoryPressure"] = memoryPressure,
            ["MemoryPressurePercent"] = $"{memoryPressure * 100:F1}%"
        };
    }
}

/// <summary>
/// Exception thrown when plugin operations fail with enhanced plugin context.
/// </summary>
[Serializable]
public sealed class FlowEnginePluginException : FlowEngineException
{
    /// <summary>
    /// Gets the name of the plugin that caused the error.
    /// </summary>
    public string? PluginName { get; }

    /// <summary>
    /// Gets the assembly path of the plugin.
    /// </summary>
    public string? AssemblyPath { get; }

    /// <summary>
    /// Gets the type name of the plugin.
    /// </summary>
    public string? TypeName { get; }

    /// <summary>
    /// Gets whether this plugin error is recoverable.
    /// </summary>
    public bool IsRecoverable { get; }

    public FlowEnginePluginException(
        ErrorCode errorCode,
        string message,
        string? pluginName = null,
        string? assemblyPath = null,
        string? typeName = null,
        bool isRecoverable = false,
        string? operationName = null,
        Exception? innerException = null)
        : base(errorCode, message, operationName, innerException, CreateContext(pluginName, assemblyPath, typeName, isRecoverable))
    {
        PluginName = pluginName;
        AssemblyPath = assemblyPath;
        TypeName = typeName;
        IsRecoverable = isRecoverable;
    }

    private FlowEnginePluginException(SerializationInfo info, StreamingContext context)
        : base(info, context)
    {
        PluginName = info.GetString(nameof(PluginName));
        AssemblyPath = info.GetString(nameof(AssemblyPath));
        TypeName = info.GetString(nameof(TypeName));
        IsRecoverable = info.GetBoolean(nameof(IsRecoverable));
    }

    [Obsolete]
    public override void GetObjectData(SerializationInfo info, StreamingContext context)
    {
        base.GetObjectData(info, context);
        info.AddValue(nameof(PluginName), PluginName);
        info.AddValue(nameof(AssemblyPath), AssemblyPath);
        info.AddValue(nameof(TypeName), TypeName);
        info.AddValue(nameof(IsRecoverable), IsRecoverable);
    }

    private static Dictionary<string, object> CreateContext(string? pluginName, string? assemblyPath, string? typeName, bool isRecoverable)
    {
        var context = new Dictionary<string, object>
        {
            ["IsRecoverable"] = isRecoverable
        };

        if (!string.IsNullOrEmpty(pluginName))
        {
            context["PluginName"] = pluginName;
        }

        if (!string.IsNullOrEmpty(assemblyPath))
        {
            context["AssemblyPath"] = assemblyPath;
            context["AssemblyFileName"] = Path.GetFileName(assemblyPath);
        }

        if (!string.IsNullOrEmpty(typeName))
        {
            context["TypeName"] = typeName;
        }

        return context;
    }
}
