namespace FlowEngine.Core.Services.Scripting;

/// <summary>
/// Abstraction for different JavaScript engine implementations.
/// Provides a unified interface for V8, Jint, and future engine types.
/// </summary>
/// <remarks>
/// This interface allows FlowEngine to support multiple JavaScript engines
/// with consistent behavior and resource management. Implementations should
/// be thread-safe for compilation but not for execution (use pooling).
/// 
/// Performance Considerations:
/// - Compilation should be cached and reused across executions
/// - Engines should be pooled to avoid allocation overhead
/// - Resource limits should be enforced consistently
/// - Disposal should clean up native resources properly
/// </remarks>
public interface IScriptEngine : IDisposable
{
    /// <summary>
    /// Gets the type of this script engine.
    /// </summary>
    ScriptEngineType EngineType { get; }

    /// <summary>
    /// Gets whether this engine instance is healthy and ready for use.
    /// </summary>
    bool IsHealthy { get; }

    /// <summary>
    /// Gets whether this engine has been disposed.
    /// </summary>
    bool IsDisposed { get; }

    /// <summary>
    /// Gets or sets the maximum execution time for scripts.
    /// </summary>
    TimeSpan MaxExecutionTime { get; set; }

    /// <summary>
    /// Gets or sets the memory limit for script execution in bytes.
    /// </summary>
    long MemoryLimit { get; set; }

    /// <summary>
    /// Gets or sets the maximum recursion depth.
    /// </summary>
    int RecursionLimit { get; set; }

    /// <summary>
    /// Compiles a JavaScript script for efficient repeated execution.
    /// </summary>
    /// <param name="script">JavaScript source code to compile</param>
    /// <param name="options">Compilation options and settings</param>
    /// <param name="cancellationToken">Cancellation token for compilation timeout</param>
    /// <returns>Engine-specific compiled script object</returns>
    /// <exception cref="ScriptCompilationException">Compilation failed due to syntax errors</exception>
    /// <remarks>
    /// Compilation should validate syntax and optimize the script for execution.
    /// The returned object is engine-specific and should only be used with
    /// the same engine type that compiled it.
    /// </remarks>
    Task<object> CompileAsync(
        string script, 
        ScriptCompilationOptions options, 
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Executes a previously compiled script with the provided context.
    /// </summary>
    /// <typeparam name="T">Expected return type from script execution</typeparam>
    /// <param name="compiledScript">Previously compiled script object</param>
    /// <param name="context">Context data available to the script</param>
    /// <param name="cancellationToken">Cancellation token for execution timeout</param>
    /// <returns>Result of script execution</returns>
    /// <exception cref="ScriptExecutionException">Script execution failed or timed out</exception>
    /// <remarks>
    /// Execution should respect resource limits and cancellation tokens.
    /// The context object is made available to the script as global variables.
    /// </remarks>
    Task<T?> ExecuteAsync<T>(
        object compiledScript, 
        object? context = null, 
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Sets a global variable in the engine context.
    /// </summary>
    /// <param name="name">Variable name (must be valid JavaScript identifier)</param>
    /// <param name="value">Variable value</param>
    /// <exception cref="ArgumentException">Invalid variable name or value</exception>
    /// <remarks>
    /// Global variables persist across script executions until reset or disposed.
    /// Variable names must be valid JavaScript identifiers.
    /// </remarks>
    void SetGlobal(string name, object? value);

    /// <summary>
    /// Gets a global variable from the engine context.
    /// </summary>
    /// <param name="name">Variable name to retrieve</param>
    /// <returns>Variable value or null if not found</returns>
    object? GetGlobal(string name);

    /// <summary>
    /// Removes a global variable from the engine context.
    /// </summary>
    /// <param name="name">Variable name to remove</param>
    void RemoveGlobal(string name);

    /// <summary>
    /// Registers a .NET function that can be called from JavaScript.
    /// </summary>
    /// <param name="name">Function name in JavaScript</param>
    /// <param name="function">Delegate to invoke when script calls function</param>
    /// <exception cref="ArgumentException">Invalid function name or delegate</exception>
    /// <remarks>
    /// Registered functions provide controlled communication between
    /// scripts and .NET code. Function names must be valid identifiers.
    /// </remarks>
    void RegisterFunction(string name, Delegate function);

    /// <summary>
    /// Resets the engine to a clean state, removing all variables and functions.
    /// </summary>
    /// <remarks>
    /// This method should be called when returning an engine to the pool
    /// to ensure isolation between different script executions.
    /// </remarks>
    void Reset();

    /// <summary>
    /// Forces garbage collection in the script engine.
    /// </summary>
    /// <remarks>
    /// This method should be called periodically to free memory used
    /// by script objects and prevent memory leaks.
    /// </remarks>
    void CollectGarbage();

    /// <summary>
    /// Validates that the engine is in a healthy state for reuse.
    /// </summary>
    /// <returns>True if the engine is healthy, false if it should be discarded</returns>
    /// <remarks>
    /// This method is called by the object pool to determine if an engine
    /// can be safely reused or should be disposed and recreated.
    /// </remarks>
    bool ValidateHealth();
}