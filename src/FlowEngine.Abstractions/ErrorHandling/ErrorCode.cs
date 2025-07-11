using System.ComponentModel;

namespace FlowEngine.Abstractions.ErrorHandling;

/// <summary>
/// Standardized error codes for FlowEngine operations.
/// Provides consistent error identification and categorization for production diagnostics.
/// </summary>
public enum ErrorCode
{
    // General Errors (1000-1099)
    [Description("An unknown error occurred")]
    Unknown = 1000,

    [Description("Operation timeout")]
    Timeout = 1001,

    [Description("Operation was cancelled")]
    Cancelled = 1002,

    [Description("Resource temporarily unavailable")]
    ResourceUnavailable = 1003,

    [Description("Configuration is invalid")]
    InvalidConfiguration = 1004,

    // Schema and Data Errors (2000-2099)
    [Description("Schema validation failed")]
    SchemaValidationFailed = 2000,

    [Description("Schema incompatibility detected")]
    SchemaIncompatible = 2001,

    [Description("Required field is missing")]
    MissingRequiredField = 2002,

    [Description("Field type mismatch")]
    FieldTypeMismatch = 2003,

    [Description("Data format is invalid")]
    InvalidDataFormat = 2004,

    [Description("Data corruption detected")]
    DataCorruption = 2005,

    // Memory and Resource Errors (3000-3099)
    [Description("Memory allocation failed")]
    MemoryAllocationFailed = 3000,

    [Description("Memory limit exceeded")]
    MemoryLimitExceeded = 3001,

    [Description("Resource limit exceeded")]
    ResourceLimitExceeded = 3002,

    [Description("Disk space insufficient")]
    InsufficientDiskSpace = 3003,

    // Plugin Errors (4000-4099)
    [Description("Plugin loading failed")]
    PluginLoadFailed = 4000,

    [Description("Plugin initialization failed")]
    PluginInitializationFailed = 4001,

    [Description("Plugin execution failed")]
    PluginExecutionFailed = 4002,

    [Description("Plugin configuration invalid")]
    PluginConfigurationInvalid = 4003,

    [Description("Plugin not found")]
    PluginNotFound = 4004,

    [Description("Plugin dependency missing")]
    PluginDependencyMissing = 4005,

    [Description("Plugin version incompatible")]
    PluginVersionIncompatible = 4006,

    // Data Processing Errors (5000-5099)
    [Description("Chunk processing failed")]
    ChunkProcessingFailed = 5000,

    [Description("Data transformation failed")]
    DataTransformationFailed = 5001,

    [Description("Data validation failed")]
    DataValidationFailed = 5002,

    [Description("Batch processing failed")]
    BatchProcessingFailed = 5003,

    [Description("Pipeline execution failed")]
    PipelineExecutionFailed = 5004,

    // I/O and External Service Errors (6000-6099)
    [Description("File not found")]
    FileNotFound = 6000,

    [Description("File access denied")]
    FileAccessDenied = 6001,

    [Description("Network connection failed")]
    NetworkConnectionFailed = 6002,

    [Description("Database connection failed")]
    DatabaseConnectionFailed = 6003,

    [Description("External service unavailable")]
    ExternalServiceUnavailable = 6004,

    [Description("Authentication failed")]
    AuthenticationFailed = 6005,

    [Description("Authorization failed")]
    AuthorizationFailed = 6006,

    // JavaScript Engine Errors (7000-7099)
    [Description("JavaScript compilation failed")]
    JavaScriptCompilationFailed = 7000,

    [Description("JavaScript execution failed")]
    JavaScriptExecutionFailed = 7001,

    [Description("JavaScript timeout")]
    JavaScriptTimeout = 7002,

    [Description("JavaScript memory limit exceeded")]
    JavaScriptMemoryLimitExceeded = 7003,

    [Description("JavaScript context error")]
    JavaScriptContextError = 7004
}

/// <summary>
/// Extension methods for ErrorCode enum.
/// </summary>
public static class ErrorCodeExtensions
{
    /// <summary>
    /// Gets the description for an error code.
    /// </summary>
    public static string GetDescription(this ErrorCode errorCode)
    {
        var field = errorCode.GetType().GetField(errorCode.ToString());
        var attribute = field?.GetCustomAttributes(typeof(DescriptionAttribute), false)
                             .Cast<DescriptionAttribute>()
                             .FirstOrDefault();
        return attribute?.Description ?? errorCode.ToString();
    }

    /// <summary>
    /// Determines if an error code represents a retryable error.
    /// </summary>
    public static bool IsRetryable(this ErrorCode errorCode)
    {
        return errorCode switch
        {
            ErrorCode.Timeout => true,
            ErrorCode.ResourceUnavailable => true,
            ErrorCode.MemoryAllocationFailed => true,
            ErrorCode.NetworkConnectionFailed => true,
            ErrorCode.DatabaseConnectionFailed => true,
            ErrorCode.ExternalServiceUnavailable => true,
            ErrorCode.JavaScriptTimeout => true,
            ErrorCode.ChunkProcessingFailed => true,

            // Non-retryable errors
            ErrorCode.SchemaValidationFailed => false,
            ErrorCode.InvalidConfiguration => false,
            ErrorCode.PluginNotFound => false,
            ErrorCode.FileNotFound => false,
            ErrorCode.AuthenticationFailed => false,
            ErrorCode.AuthorizationFailed => false,
            ErrorCode.JavaScriptCompilationFailed => false,

            _ => false
        };
    }

    /// <summary>
    /// Gets the severity level for an error code.
    /// </summary>
    public static ErrorSeverity GetSeverity(this ErrorCode errorCode)
    {
        return errorCode switch
        {
            ErrorCode.DataCorruption => ErrorSeverity.Critical,
            ErrorCode.MemoryLimitExceeded => ErrorSeverity.Critical,
            ErrorCode.InsufficientDiskSpace => ErrorSeverity.Critical,

            ErrorCode.PluginLoadFailed => ErrorSeverity.High,
            ErrorCode.PipelineExecutionFailed => ErrorSeverity.High,
            ErrorCode.AuthenticationFailed => ErrorSeverity.High,
            ErrorCode.AuthorizationFailed => ErrorSeverity.High,

            ErrorCode.SchemaValidationFailed => ErrorSeverity.Medium,
            ErrorCode.DataValidationFailed => ErrorSeverity.Medium,
            ErrorCode.PluginConfigurationInvalid => ErrorSeverity.Medium,

            ErrorCode.Timeout => ErrorSeverity.Low,
            ErrorCode.ResourceUnavailable => ErrorSeverity.Low,

            _ => ErrorSeverity.Medium
        };
    }
}

/// <summary>
/// Error severity levels for proper escalation and handling.
/// </summary>
public enum ErrorSeverity
{
    /// <summary>
    /// Low severity - typically transient issues that may resolve automatically.
    /// </summary>
    Low = 0,

    /// <summary>
    /// Medium severity - issues that may impact functionality but are recoverable.
    /// </summary>
    Medium = 1,

    /// <summary>
    /// High severity - significant issues that impact core functionality.
    /// </summary>
    High = 2,

    /// <summary>
    /// Critical severity - system-threatening issues requiring immediate attention.
    /// </summary>
    Critical = 3
}
