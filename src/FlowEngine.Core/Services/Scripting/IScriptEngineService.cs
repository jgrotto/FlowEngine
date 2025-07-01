using System.Collections.Immutable;

namespace FlowEngine.Core.Services.Scripting;

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
    Task<CompiledScript> CompileAsync(
        string script, 
        ScriptCompilationOptions options,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates an execution context for a compiled script with resource limits.
    /// Each execution context provides an isolated environment with security constraints.
    /// </summary>
    /// <param name="script">Previously compiled script to execute</param>
    /// <param name="options">Execution configuration and resource limits</param>
    /// <param name="cancellationToken">Cancellation token for cooperative cancellation</param>
    /// <returns>Script execution context ready for use</returns>
    /// <exception cref="ResourceExhaustionException">No available engines in pool</exception>
    /// <remarks>
    /// Resource Management: Engines returned to pool after disposal.
    /// Security: Sandbox applied with configurable restrictions.
    /// Performance: Engines pre-warmed in object pool.
    /// </remarks>
    Task<IScriptExecution> CreateExecutionAsync(
        CompiledScript script,
        ExecutionOptions options,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates script safety, syntax, and complexity before compilation.
    /// Performs static analysis to detect security violations and infinite loops.
    /// </summary>
    /// <param name="script">JavaScript source code to validate</param>
    /// <param name="options">Validation configuration and security rules</param>
    /// <param name="cancellationToken">Cancellation token for cooperative cancellation</param>
    /// <returns>Validation result with detailed error information</returns>
    /// <remarks>
    /// Security: Detects potentially malicious patterns.
    /// Performance: Identifies scripts that may cause performance issues.
    /// Reliability: Validates syntax before expensive compilation.
    /// </remarks>
    Task<ScriptValidationResult> ValidateAsync(
        string script,
        ValidationOptions options,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets current service health metrics and engine pool status.
    /// Provides real-time visibility into service performance and resource usage.
    /// </summary>
    /// <returns>Current health status and performance metrics</returns>
    /// <remarks>
    /// Monitoring: Real-time service health visibility.
    /// Optimization: Pool sizing and cache hit rate metrics.
    /// Troubleshooting: Detailed error and performance statistics.
    /// </remarks>
    ScriptEngineHealth GetHealth();
}

/// <summary>
/// Represents a compiled script that can be executed multiple times efficiently.
/// Immutable and thread-safe for concurrent execution.
/// </summary>
public sealed class CompiledScript
{
    /// <summary>
    /// Gets the unique identifier for this compiled script.
    /// </summary>
    public required string Id { get; init; }
    
    /// <summary>
    /// Gets the SHA-256 hash of the source code for cache key generation.
    /// </summary>
    public required string SourceHash { get; init; }
    
    /// <summary>
    /// Gets metadata extracted from the script during compilation.
    /// </summary>
    public required ScriptMetadata Metadata { get; init; }
    
    /// <summary>
    /// Gets the timestamp when this script was compiled.
    /// </summary>
    public required DateTimeOffset CompiledAt { get; init; }
    
    /// <summary>
    /// Gets the compilation time in milliseconds for performance tracking.
    /// </summary>
    public required long CompilationTimeMs { get; init; }
    
    /// <summary>
    /// Gets the script engine type used for compilation.
    /// </summary>
    public required ScriptEngineType EngineType { get; init; }
    
    /// <summary>
    /// Internal compiled representation specific to the script engine.
    /// </summary>
    public required object CompiledCode { get; init; }
}

/// <summary>
/// Execution context for a compiled script with resource management and security.
/// </summary>
public interface IScriptExecution : IAsyncDisposable
{
    /// <summary>
    /// Executes the script with provided context data and returns the result.
    /// </summary>
    /// <typeparam name="T">Expected return type from script execution</typeparam>
    /// <param name="context">Context data available to the script</param>
    /// <param name="cancellationToken">Cancellation token for execution timeout</param>
    /// <returns>Script execution result</returns>
    /// <exception cref="ScriptExecutionException">Script runtime error or timeout</exception>
    /// <exception cref="ScriptSecurityException">Script attempted forbidden operation</exception>
    /// <remarks>
    /// Performance: Direct engine execution without marshaling overhead.
    /// Security: Sandboxed execution with resource limits.
    /// Reliability: Automatic timeout and cancellation support.
    /// </remarks>
    Task<T?> ExecuteAsync<T>(
        object? context = null, 
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Sets a global variable in the execution context.
    /// Variables are available to scripts during execution.
    /// </summary>
    /// <param name="name">Variable name (must be valid JavaScript identifier)</param>
    /// <param name="value">Variable value (must be serializable)</param>
    /// <exception cref="ArgumentException">Invalid variable name or value</exception>
    /// <remarks>
    /// Performance: Variables cached in engine context.
    /// Security: Variable names validated for safety.
    /// </remarks>
    void SetGlobal(string name, object? value);

    /// <summary>
    /// Registers a callback function that scripts can invoke.
    /// Enables controlled communication between scripts and .NET code.
    /// </summary>
    /// <param name="name">Function name (must be valid JavaScript identifier)</param>
    /// <param name="function">Delegate to invoke when script calls function</param>
    /// <exception cref="ArgumentException">Invalid function name or delegate</exception>
    /// <remarks>
    /// Security: Only explicitly registered functions are accessible.
    /// Performance: Direct delegate invocation without reflection.
    /// </remarks>
    void RegisterFunction(string name, Delegate function);
}

/// <summary>
/// Configuration options for script compilation.
/// </summary>
public sealed record ScriptCompilationOptions
{
    /// <summary>
    /// Gets or sets the script engine type to use for compilation.
    /// </summary>
    public ScriptEngineType EngineType { get; init; } = ScriptEngineType.Auto;
    
    /// <summary>
    /// Gets or sets the optimization level for compilation.
    /// </summary>
    public OptimizationLevel OptimizationLevel { get; init; } = OptimizationLevel.Balanced;
    
    /// <summary>
    /// Gets or sets whether to enable debugging support.
    /// </summary>
    public bool EnableDebugging { get; init; } = false;
    
    /// <summary>
    /// Gets or sets whether to enable strict mode.
    /// </summary>
    public bool StrictMode { get; init; } = true;
    
    /// <summary>
    /// Gets or sets the compilation timeout.
    /// </summary>
    public TimeSpan CompilationTimeout { get; init; } = TimeSpan.FromSeconds(30);
}

/// <summary>
/// Configuration options for script execution.
/// </summary>
public sealed record ExecutionOptions
{
    /// <summary>
    /// Gets or sets the script engine type to use for execution.
    /// </summary>
    public ScriptEngineType EngineType { get; init; } = ScriptEngineType.Auto;
    
    /// <summary>
    /// Gets or sets the maximum execution time.
    /// </summary>
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(30);
    
    /// <summary>
    /// Gets or sets the maximum memory usage in bytes.
    /// </summary>
    public long MemoryLimit { get; init; } = 100 * 1024 * 1024; // 100MB
    
    /// <summary>
    /// Gets or sets the maximum recursion depth.
    /// </summary>
    public int RecursionLimit { get; init; } = 1000;
    
    /// <summary>
    /// Gets or sets whether to enable sandbox restrictions.
    /// </summary>
    public bool EnableSandbox { get; init; } = true;
}

/// <summary>
/// Configuration options for script validation.
/// </summary>
public sealed record ValidationOptions
{
    /// <summary>
    /// Gets or sets whether to detect potential infinite loops.
    /// </summary>
    public bool DetectInfiniteLoops { get; init; } = true;
    
    /// <summary>
    /// Gets or sets whether to analyze memory usage patterns.
    /// </summary>
    public bool DetectMemoryLeaks { get; init; } = true;
    
    /// <summary>
    /// Gets or sets the maximum allowed script complexity.
    /// </summary>
    public int MaxComplexity { get; init; } = 100;
    
    /// <summary>
    /// Gets or sets security validation rules.
    /// </summary>
    public ImmutableArray<SecurityRule> SecurityRules { get; init; } = ImmutableArray<SecurityRule>.Empty;
}

/// <summary>
/// Supported script engine types.
/// </summary>
public enum ScriptEngineType
{
    /// <summary>
    /// Automatically select the best available engine.
    /// </summary>
    Auto,
    
    /// <summary>
    /// Microsoft ClearScript V8 engine (high performance).
    /// </summary>
    V8,
    
    /// <summary>
    /// Jint JavaScript engine (pure .NET).
    /// </summary>
    Jint
}

/// <summary>
/// Script compilation optimization levels.
/// </summary>
public enum OptimizationLevel
{
    /// <summary>
    /// No optimization (fastest compilation).
    /// </summary>
    None,
    
    /// <summary>
    /// Balanced optimization (default).
    /// </summary>
    Balanced,
    
    /// <summary>
    /// Maximum optimization (best performance).
    /// </summary>
    Maximum
}

/// <summary>
/// Metadata extracted from compiled scripts.
/// </summary>
public sealed record ScriptMetadata
{
    /// <summary>
    /// Gets the estimated complexity score of the script.
    /// </summary>
    public required int ComplexityScore { get; init; }
    
    /// <summary>
    /// Gets the approximate size of the compiled script in bytes.
    /// </summary>
    public required long SizeBytes { get; init; }
    
    /// <summary>
    /// Gets functions defined in the script.
    /// </summary>
    public required ImmutableArray<string> Functions { get; init; }
    
    /// <summary>
    /// Gets global variables referenced in the script.
    /// </summary>
    public required ImmutableArray<string> GlobalReferences { get; init; }
}

/// <summary>
/// Result of script validation with detailed error information.
/// </summary>
public sealed record ScriptValidationResult
{
    /// <summary>
    /// Gets whether the script passed all validation checks.
    /// </summary>
    public required bool IsValid { get; init; }
    
    /// <summary>
    /// Gets validation violations found in the script.
    /// </summary>
    public required ImmutableArray<SecurityViolation> Violations { get; init; }
    
    /// <summary>
    /// Gets the calculated complexity score.
    /// </summary>
    public required int ComplexityScore { get; init; }
    
    /// <summary>
    /// Gets estimated memory usage in bytes.
    /// </summary>
    public required long EstimatedMemoryUsage { get; init; }
}

/// <summary>
/// Security violation detected during script validation.
/// </summary>
public sealed record SecurityViolation
{
    /// <summary>
    /// Gets the severity level of this violation.
    /// </summary>
    public required SecuritySeverity Severity { get; init; }
    
    /// <summary>
    /// Gets a description of the violation.
    /// </summary>
    public required string Description { get; init; }
    
    /// <summary>
    /// Gets suggested remediation for the violation.
    /// </summary>
    public required string Remediation { get; init; }
    
    /// <summary>
    /// Gets the line number where the violation was detected.
    /// </summary>
    public int? LineNumber { get; init; }
    
    /// <summary>
    /// Gets the column number where the violation was detected.
    /// </summary>
    public int? ColumnNumber { get; init; }
}

/// <summary>
/// Security violation severity levels.
/// </summary>
public enum SecuritySeverity
{
    /// <summary>
    /// Low severity - Warning only.
    /// </summary>
    Low,
    
    /// <summary>
    /// Medium severity - May cause issues.
    /// </summary>
    Medium,
    
    /// <summary>
    /// High severity - Should be addressed.
    /// </summary>
    High,
    
    /// <summary>
    /// Critical severity - Must be fixed.
    /// </summary>
    Critical
}

/// <summary>
/// Security rule for script validation.
/// </summary>
public sealed record SecurityRule
{
    /// <summary>
    /// Gets the name of this security rule.
    /// </summary>
    public required string Name { get; init; }
    
    /// <summary>
    /// Gets the regular expression pattern to match.
    /// </summary>
    public required string Pattern { get; init; }
    
    /// <summary>
    /// Gets the severity if this rule is violated.
    /// </summary>
    public required SecuritySeverity Severity { get; init; }
    
    /// <summary>
    /// Gets a description of what this rule checks.
    /// </summary>
    public required string Description { get; init; }
    
    /// <summary>
    /// Gets suggested remediation for violations.
    /// </summary>
    public required string Remediation { get; init; }
}

/// <summary>
/// Health status and metrics for the script engine service.
/// </summary>
public sealed record ScriptEngineHealth
{
    /// <summary>
    /// Gets whether the service is healthy and operational.
    /// </summary>
    public required bool IsHealthy { get; init; }
    
    /// <summary>
    /// Gets engine pool statistics by engine type.
    /// </summary>
    public required ImmutableDictionary<ScriptEngineType, EnginePoolStats> PoolStats { get; init; }
    
    /// <summary>
    /// Gets script cache statistics.
    /// </summary>
    public required CacheStats CacheStats { get; init; }
    
    /// <summary>
    /// Gets performance metrics.
    /// </summary>
    public required PerformanceMetrics Performance { get; init; }
    
    /// <summary>
    /// Gets recent errors if any.
    /// </summary>
    public required ImmutableArray<string> RecentErrors { get; init; }
}

/// <summary>
/// Statistics for an engine pool.
/// </summary>
public sealed record EnginePoolStats
{
    /// <summary>
    /// Gets the total number of engines in the pool.
    /// </summary>
    public required int TotalEngines { get; init; }
    
    /// <summary>
    /// Gets the number of engines currently in use.
    /// </summary>
    public required int ActiveEngines { get; init; }
    
    /// <summary>
    /// Gets the number of idle engines available for use.
    /// </summary>
    public required int IdleEngines { get; init; }
    
    /// <summary>
    /// Gets the number of engines created since service start.
    /// </summary>
    public required long TotalCreated { get; init; }
    
    /// <summary>
    /// Gets the number of engines destroyed due to errors.
    /// </summary>
    public required long TotalDestroyed { get; init; }
}

/// <summary>
/// Statistics for the script cache.
/// </summary>
public sealed record CacheStats
{
    /// <summary>
    /// Gets the number of scripts currently cached.
    /// </summary>
    public required int CachedScripts { get; init; }
    
    /// <summary>
    /// Gets the total cache hit count.
    /// </summary>
    public required long TotalHits { get; init; }
    
    /// <summary>
    /// Gets the total cache miss count.
    /// </summary>
    public required long TotalMisses { get; init; }
    
    /// <summary>
    /// Gets the cache hit ratio as a percentage.
    /// </summary>
    public double HitRatio => TotalHits + TotalMisses > 0 
        ? (double)TotalHits / (TotalHits + TotalMisses) * 100 
        : 0;
    
    /// <summary>
    /// Gets the total memory used by cached scripts in bytes.
    /// </summary>
    public required long MemoryUsageBytes { get; init; }
}

/// <summary>
/// Performance metrics for the script engine service.
/// </summary>
public sealed record PerformanceMetrics
{
    /// <summary>
    /// Gets the average script compilation time in milliseconds.
    /// </summary>
    public required double AverageCompilationTimeMs { get; init; }
    
    /// <summary>
    /// Gets the average script execution time in milliseconds.
    /// </summary>
    public required double AverageExecutionTimeMs { get; init; }
    
    /// <summary>
    /// Gets the total number of scripts compiled.
    /// </summary>
    public required long TotalCompilations { get; init; }
    
    /// <summary>
    /// Gets the total number of script executions.
    /// </summary>
    public required long TotalExecutions { get; init; }
    
    /// <summary>
    /// Gets the number of compilation errors.
    /// </summary>
    public required long CompilationErrors { get; init; }
    
    /// <summary>
    /// Gets the number of execution errors.
    /// </summary>
    public required long ExecutionErrors { get; init; }
}