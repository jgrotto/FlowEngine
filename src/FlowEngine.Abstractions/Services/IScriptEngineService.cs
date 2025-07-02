using System.Collections.Immutable;

namespace FlowEngine.Abstractions.Services;

/// <summary>
/// Primary interface for centralized script engine operations.
/// Provides engine pooling, script compilation caching, and security validation.
/// </summary>
/// <remarks>
/// This service is managed by the FlowEngine core and shared across all plugins.
/// Plugins should never create their own script engines directly.
/// 
/// Performance: Engine pooling reduces allocation overhead.
/// Security: Centralized script validation and sandboxing.
/// Efficiency: Compiled script caching with LRU eviction.
/// </remarks>
public interface IScriptEngineService
{
    /// <summary>
    /// Compiles a script with caching support for improved performance.
    /// Scripts are cached by source hash and compilation options.
    /// </summary>
    /// <param name="script">JavaScript source code to compile</param>
    /// <param name="options">Compilation configuration options</param>
    /// <param name="cancellationToken">Cancellation token for cooperative cancellation</param>
    /// <returns>Compiled script ready for execution</returns>
    /// <exception cref="ScriptSecurityException">Script violates security constraints</exception>
    /// <exception cref="ScriptCompilationException">Script has syntax errors or compilation failures</exception>
    /// <remarks>
    /// Performance: Cached compilation results reduce repeated overhead.
    /// Security: All scripts validated before compilation.
    /// </remarks>
    Task<ICompiledScript> CompileAsync(
        string script, 
        ScriptCompilationOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Executes a compiled script function with parameters and resource limits.
    /// </summary>
    /// <param name="script">Previously compiled script</param>
    /// <param name="functionName">Name of the function to call</param>
    /// <param name="parameters">Parameters to pass to the function</param>
    /// <param name="options">Execution configuration and resource limits</param>
    /// <param name="cancellationToken">Cancellation token for cooperative cancellation</param>
    /// <returns>Result of script function execution</returns>
    /// <exception cref="ScriptTimeoutException">Script execution exceeded timeout</exception>
    /// <exception cref="ScriptMemoryException">Script exceeded memory limits</exception>
    /// <exception cref="ScriptRuntimeException">Script threw an exception during execution</exception>
    Task<object?> ExecuteAsync(
        ICompiledScript script,
        string functionName,
        object[]? parameters = null,
        ScriptExecutionOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates a script against security policies without compilation.
    /// Useful for configuration validation and security auditing.
    /// </summary>
    /// <param name="script">JavaScript source code to validate</param>
    /// <param name="options">Validation configuration options</param>
    /// <param name="cancellationToken">Cancellation token for cooperative cancellation</param>
    /// <returns>Validation result with security and syntax findings</returns>
    Task<ScriptValidationResult> ValidateAsync(
        string script,
        ScriptValidationOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the current health status of the script engine service.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Service health information</returns>
    Task<ScriptEngineHealth> GetHealthAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets current performance metrics for the script engine service.
    /// </summary>
    /// <returns>Script engine performance metrics</returns>
    ScriptEngineMetrics GetMetrics();

    /// <summary>
    /// Clears the compiled script cache.
    /// Useful for memory management or when script security policies change.
    /// </summary>
    /// <param name="predicate">Optional predicate to selectively clear scripts</param>
    /// <returns>Number of scripts removed from cache</returns>
    int ClearCache(Func<ICompiledScript, bool>? predicate = null);
}

/// <summary>
/// Represents a compiled script ready for execution.
/// </summary>
public interface ICompiledScript
{
    /// <summary>
    /// Gets the unique identifier for this compiled script.
    /// </summary>
    string Id { get; }

    /// <summary>
    /// Gets the source code hash used for caching.
    /// </summary>
    string SourceHash { get; }

    /// <summary>
    /// Gets the timestamp when the script was compiled.
    /// </summary>
    DateTimeOffset CompiledAt { get; }

    /// <summary>
    /// Gets the compilation options used.
    /// </summary>
    ScriptCompilationOptions Options { get; }

    /// <summary>
    /// Gets the estimated memory usage of the compiled script.
    /// </summary>
    long EstimatedMemoryBytes { get; }

    /// <summary>
    /// Gets whether the script has been validated for security.
    /// </summary>
    bool IsSecurityValidated { get; }
}

/// <summary>
/// Configuration options for script compilation.
/// </summary>
public sealed record ScriptCompilationOptions
{
    /// <summary>
    /// Gets whether to enable strict mode for JavaScript compilation.
    /// </summary>
    public bool EnableStrictMode { get; init; } = true;

    /// <summary>
    /// Gets whether to enable debugging information in compiled scripts.
    /// </summary>
    public bool EnableDebugging { get; init; } = false;

    /// <summary>
    /// Gets whether to enable additional security validations.
    /// </summary>
    public bool EnableSecurityValidation { get; init; } = true;

    /// <summary>
    /// Gets custom compilation flags for the JavaScript engine.
    /// </summary>
    public ImmutableDictionary<string, object> CompilationFlags { get; init; } = 
        ImmutableDictionary<string, object>.Empty;
}

/// <summary>
/// Configuration options for script execution.
/// </summary>
public sealed record ScriptExecutionOptions
{
    /// <summary>
    /// Gets the maximum execution time in milliseconds.
    /// </summary>
    public int TimeoutMs { get; init; } = 5000;

    /// <summary>
    /// Gets the maximum memory limit in bytes.
    /// </summary>
    public long MaxMemoryBytes { get; init; } = 10 * 1024 * 1024; // 10MB

    /// <summary>
    /// Gets global variables to make available during execution.
    /// </summary>
    public ImmutableDictionary<string, object> GlobalVariables { get; init; } = 
        ImmutableDictionary<string, object>.Empty;

    /// <summary>
    /// Gets whether to enable detailed error reporting.
    /// </summary>
    public bool EnableDetailedErrors { get; init; } = false;

    /// <summary>
    /// Gets the execution context isolation level.
    /// </summary>
    public ScriptIsolationLevel IsolationLevel { get; init; } = ScriptIsolationLevel.Standard;
}

/// <summary>
/// Configuration options for script validation.
/// </summary>
public sealed record ScriptValidationOptions
{
    /// <summary>
    /// Gets whether to perform syntax validation.
    /// </summary>
    public bool ValidateSyntax { get; init; } = true;

    /// <summary>
    /// Gets whether to perform security validation.
    /// </summary>
    public bool ValidateSecurity { get; init; } = true;

    /// <summary>
    /// Gets whether to check for performance anti-patterns.
    /// </summary>
    public bool ValidatePerformance { get; init; } = true;

    /// <summary>
    /// Gets custom security policies to apply.
    /// </summary>
    public ImmutableArray<string> SecurityPolicies { get; init; } = ImmutableArray<string>.Empty;
}

/// <summary>
/// Result of script validation operation.
/// </summary>
public sealed record ScriptValidationResult
{
    /// <summary>
    /// Gets whether the script passed all validations.
    /// </summary>
    public required bool IsValid { get; init; }

    /// <summary>
    /// Gets validation errors found.
    /// </summary>
    public required ImmutableArray<ScriptValidationIssue> Errors { get; init; }

    /// <summary>
    /// Gets validation warnings found.
    /// </summary>
    public required ImmutableArray<ScriptValidationIssue> Warnings { get; init; }

    /// <summary>
    /// Gets performance recommendations.
    /// </summary>
    public ImmutableArray<string> PerformanceRecommendations { get; init; } = ImmutableArray<string>.Empty;

    /// <summary>
    /// Gets the time taken for validation.
    /// </summary>
    public required TimeSpan ValidationTime { get; init; }
}

/// <summary>
/// Represents a script validation issue.
/// </summary>
public sealed record ScriptValidationIssue
{
    /// <summary>
    /// Gets the issue code.
    /// </summary>
    public required string Code { get; init; }

    /// <summary>
    /// Gets the issue message.
    /// </summary>
    public required string Message { get; init; }

    /// <summary>
    /// Gets the severity level.
    /// </summary>
    public required ScriptValidationSeverity Severity { get; init; }

    /// <summary>
    /// Gets the line number where the issue was found.
    /// </summary>
    public int? LineNumber { get; init; }

    /// <summary>
    /// Gets the column number where the issue was found.
    /// </summary>
    public int? ColumnNumber { get; init; }

    /// <summary>
    /// Gets additional context about the issue.
    /// </summary>
    public ImmutableDictionary<string, object> Context { get; init; } = 
        ImmutableDictionary<string, object>.Empty;
}

/// <summary>
/// Script engine health information.
/// </summary>
public sealed record ScriptEngineHealth
{
    /// <summary>
    /// Gets whether the script engine is healthy.
    /// </summary>
    public required bool IsHealthy { get; init; }

    /// <summary>
    /// Gets the health status message.
    /// </summary>
    public required string Message { get; init; }

    /// <summary>
    /// Gets the number of active script engines.
    /// </summary>
    public required int ActiveEngines { get; init; }

    /// <summary>
    /// Gets the number of cached compiled scripts.
    /// </summary>
    public required int CachedScripts { get; init; }

    /// <summary>
    /// Gets the total memory usage in bytes.
    /// </summary>
    public required long MemoryUsageBytes { get; init; }

    /// <summary>
    /// Gets the timestamp of the health check.
    /// </summary>
    public required DateTimeOffset CheckedAt { get; init; }
}

/// <summary>
/// Script engine performance metrics.
/// </summary>
public sealed record ScriptEngineMetrics
{
    /// <summary>
    /// Gets the total number of scripts compiled.
    /// </summary>
    public required long TotalCompilations { get; init; }

    /// <summary>
    /// Gets the total number of script executions.
    /// </summary>
    public required long TotalExecutions { get; init; }

    /// <summary>
    /// Gets the number of compilation cache hits.
    /// </summary>
    public required long CacheHits { get; init; }

    /// <summary>
    /// Gets the number of compilation cache misses.
    /// </summary>
    public required long CacheMisses { get; init; }

    /// <summary>
    /// Gets the average compilation time.
    /// </summary>
    public required TimeSpan AverageCompilationTime { get; init; }

    /// <summary>
    /// Gets the average execution time.
    /// </summary>
    public required TimeSpan AverageExecutionTime { get; init; }

    /// <summary>
    /// Gets the number of execution errors.
    /// </summary>
    public required long ExecutionErrors { get; init; }

    /// <summary>
    /// Gets the number of timeout errors.
    /// </summary>
    public required long TimeoutErrors { get; init; }

    /// <summary>
    /// Gets the current memory usage in bytes.
    /// </summary>
    public required long MemoryUsageBytes { get; init; }
}

/// <summary>
/// Script execution isolation levels.
/// </summary>
public enum ScriptIsolationLevel
{
    /// <summary>
    /// Standard isolation with basic sandboxing.
    /// </summary>
    Standard,

    /// <summary>
    /// Enhanced isolation with additional security restrictions.
    /// </summary>
    Enhanced,

    /// <summary>
    /// Maximum isolation with strict security policies.
    /// </summary>
    Maximum
}

/// <summary>
/// Script validation severity levels.
/// </summary>
public enum ScriptValidationSeverity
{
    /// <summary>
    /// Informational message.
    /// </summary>
    Info,

    /// <summary>
    /// Warning that should be addressed.
    /// </summary>
    Warning,

    /// <summary>
    /// Error that prevents script execution.
    /// </summary>
    Error,

    /// <summary>
    /// Critical security or safety issue.
    /// </summary>
    Critical
}

// Exception types for script engine operations
public class ScriptSecurityException : Exception
{
    public ScriptSecurityException(string message) : base(message) { }
    public ScriptSecurityException(string message, Exception innerException) : base(message, innerException) { }
}

public class ScriptCompilationException : Exception
{
    public ScriptCompilationException(string message) : base(message) { }
    public ScriptCompilationException(string message, Exception innerException) : base(message, innerException) { }
}

public class ScriptTimeoutException : Exception
{
    public ScriptTimeoutException(string message) : base(message) { }
    public ScriptTimeoutException(string message, Exception innerException) : base(message, innerException) { }
}

public class ScriptMemoryException : Exception
{
    public ScriptMemoryException(string message) : base(message) { }
    public ScriptMemoryException(string message, Exception innerException) : base(message, innerException) { }
}

public class ScriptRuntimeException : Exception
{
    public ScriptRuntimeException(string message) : base(message) { }
    public ScriptRuntimeException(string message, Exception innerException) : base(message, innerException) { }
}