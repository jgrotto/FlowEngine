using FlowEngine.Abstractions.Data;

namespace FlowEngine.Core.Services;

/// <summary>
/// Core service for JavaScript script execution with engine pooling and performance optimization.
/// </summary>
public interface IScriptEngineService : IDisposable
{
    /// <summary>
    /// Compiles a JavaScript script for optimized execution.
    /// </summary>
    /// <param name="script">JavaScript code to compile</param>
    /// <param name="options">Script compilation options</param>
    /// <returns>Compiled script instance</returns>
    Task<CompiledScript> CompileAsync(string script, ScriptOptions options);

    /// <summary>
    /// Executes a compiled script with the provided context.
    /// </summary>
    /// <param name="script">Compiled script to execute</param>
    /// <param name="context">JavaScript context for execution</param>
    /// <returns>Script execution result</returns>
    Task<ScriptResult> ExecuteAsync(CompiledScript script, IJavaScriptContext context);

    /// <summary>
    /// Gets engine performance statistics.
    /// </summary>
    ScriptEngineStats GetStats();
}

/// <summary>
/// Represents a compiled JavaScript script with caching support.
/// </summary>
public class CompiledScript : IDisposable
{
    /// <summary>
    /// Gets the unique identifier for this compiled script.
    /// </summary>
    public string ScriptId { get; }

    /// <summary>
    /// Gets the original JavaScript source code.
    /// </summary>
    public string Source { get; }

    /// <summary>
    /// Gets the UTC timestamp when this script was compiled.
    /// </summary>
    public DateTime CompiledAt { get; }

    /// <summary>
    /// Gets or sets the prepared script instance for optimization.
    /// </summary>
    public object? PreparedScript { get; internal set; }

    /// <summary>
    /// Initializes a new instance of the CompiledScript class.
    /// </summary>
    /// <param name="scriptId">Unique identifier for the script</param>
    /// <param name="source">JavaScript source code</param>
    public CompiledScript(string scriptId, string source)
    {
        ScriptId = scriptId;
        Source = source;
        CompiledAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Disposes of the compiled script resources.
    /// </summary>
    public void Dispose()
    {
        // Jint doesn't require explicit disposal
        PreparedScript = null;
    }
}

/// <summary>
/// Script execution result with success status and return value.
/// </summary>
public class ScriptResult
{
    /// <summary>
    /// Gets a value indicating whether the script execution was successful.
    /// </summary>
    public bool Success { get; init; }

    /// <summary>
    /// Gets the return value from the script execution.
    /// </summary>
    public object? ReturnValue { get; init; }

    /// <summary>
    /// Gets the error message if the script execution failed.
    /// </summary>
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// Gets the exception that caused the script execution to fail.
    /// </summary>
    public Exception? Exception { get; init; }

    /// <summary>
    /// Creates a successful script result with the specified return value.
    /// </summary>
    /// <param name="returnValue">The return value from the script</param>
    /// <returns>A successful script result</returns>
    public static ScriptResult CreateSuccess(object? returnValue) => new()
    {
        Success = true,
        ReturnValue = returnValue
    };

    /// <summary>
    /// Creates an error script result with the specified error message and optional exception.
    /// </summary>
    /// <param name="errorMessage">The error message</param>
    /// <param name="exception">The exception that caused the error</param>
    /// <returns>An error script result</returns>
    public static ScriptResult CreateError(string errorMessage, Exception? exception = null) => new()
    {
        Success = false,
        ErrorMessage = errorMessage,
        Exception = exception
    };
}

/// <summary>
/// Script compilation and execution options.
/// </summary>
public class ScriptOptions
{
    /// <summary>
    /// Gets or sets the maximum execution time for the script.
    /// </summary>
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Gets or sets the maximum memory limit for script execution in bytes.
    /// </summary>
    public long MemoryLimit { get; init; } = 10 * 1024 * 1024; // 10MB

    /// <summary>
    /// Gets or sets a value indicating whether script caching is enabled.
    /// </summary>
    public bool EnableCaching { get; init; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether debugging is enabled.
    /// </summary>
    public bool EnableDebugging { get; init; } = false;
}

/// <summary>
/// Script engine performance statistics.
/// </summary>
public class ScriptEngineStats
{
    /// <summary>
    /// Gets the total number of scripts compiled.
    /// </summary>
    public int ScriptsCompiled { get; internal set; }

    /// <summary>
    /// Gets the total number of scripts executed.
    /// </summary>
    public int ScriptsExecuted { get; internal set; }

    /// <summary>
    /// Gets the total time spent on script compilation.
    /// </summary>
    public TimeSpan TotalCompilationTime { get; internal set; }

    /// <summary>
    /// Gets the total time spent on script execution.
    /// </summary>
    public TimeSpan TotalExecutionTime { get; internal set; }

    /// <summary>
    /// Gets the current memory usage in bytes.
    /// </summary>
    public long MemoryUsage { get; internal set; }

    /// <summary>
    /// Gets the number of cache hits.
    /// </summary>
    public int CacheHits { get; internal set; }

    /// <summary>
    /// Gets the number of cache misses.
    /// </summary>
    public int CacheMisses { get; internal set; }

    /// <summary>
    /// Gets the size of the engine pool.
    /// </summary>
    public int EnginePoolSize { get; internal set; }

    /// <summary>
    /// Gets the number of active engines in the pool.
    /// </summary>
    public int EnginePoolActive { get; internal set; }

    /// <summary>
    /// Gets the number of script executions using pre-compiled AST.
    /// </summary>
    public int AstExecutions { get; internal set; }

    /// <summary>
    /// Gets the number of script executions using string parsing (fallback).
    /// </summary>
    public int StringExecutions { get; internal set; }

    /// <summary>
    /// Gets the AST utilization rate as a percentage (0-100).
    /// </summary>
    public double AstUtilizationRate => ScriptsExecuted > 0 ? (AstExecutions * 100.0) / ScriptsExecuted : 0.0;
}
