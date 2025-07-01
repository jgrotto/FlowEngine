using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace FlowEngine.Core.Services.Scripting;

/// <summary>
/// Script execution context that provides isolated script execution with resource management.
/// Manages engine lifecycle, security sandboxing, and performance monitoring.
/// </summary>
/// <remarks>
/// This class wraps a pooled script engine and provides:
/// - Automatic engine return to pool on disposal
/// - Resource limit enforcement (memory, time, recursion)
/// - Security sandbox application with controlled globals
/// - Performance monitoring and metrics collection
/// - Cancellation support with proper cleanup
/// 
/// The execution context is single-use and should be disposed after each script execution
/// to ensure proper resource management and engine pool utilization.
/// </remarks>
internal sealed class ScriptExecutionContext : IScriptExecution
{
    private readonly IScriptEngine _engine;
    private readonly CompiledScript _script;
    private readonly ExecutionOptions _options;
    private readonly ExecutionLimitsOptions _limits;
    private readonly Action _disposeCallback;
    private readonly ILogger _logger;
    private readonly CancellationTokenSource _timeoutCts;
    private readonly Stopwatch _executionStopwatch;
    
    private bool _isDisposed;
    private int _executionCount;

    /// <summary>
    /// Initializes a new instance of the ScriptExecutionContext class.
    /// </summary>
    /// <param name="engine">Script engine instance from the pool</param>
    /// <param name="script">Compiled script to execute</param>
    /// <param name="options">Execution options and resource limits</param>
    /// <param name="limits">Default execution limits from configuration</param>
    /// <param name="disposeCallback">Callback to return engine to pool</param>
    /// <param name="logger">Logger for execution diagnostics</param>
    public ScriptExecutionContext(
        IScriptEngine engine,
        CompiledScript script,
        ExecutionOptions options,
        ExecutionLimitsOptions limits,
        Action disposeCallback,
        ILogger logger)
    {
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        _script = script ?? throw new ArgumentNullException(nameof(script));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _limits = limits ?? throw new ArgumentNullException(nameof(limits));
        _disposeCallback = disposeCallback ?? throw new ArgumentNullException(nameof(disposeCallback));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        
        _executionStopwatch = new Stopwatch();
        _timeoutCts = new CancellationTokenSource(_options.Timeout);
        
        // Configure engine limits
        ConfigureEngineLimits();
        
        _logger.LogDebug("Created script execution context for {EngineType} engine with {Timeout}ms timeout",
            _engine.EngineType, _options.Timeout.TotalMilliseconds);
    }

    /// <inheritdoc />
    public async Task<T?> ExecuteAsync<T>(object? context = null, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        
        var executionNumber = Interlocked.Increment(ref _executionCount);
        _logger.LogDebug("Starting script execution #{ExecutionNumber}", executionNumber);
        
        // Create combined cancellation token
        using var combinedCts = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken, _timeoutCts.Token);
        
        _executionStopwatch.Restart();
        
        try
        {
            // Set context if provided
            if (context != null)
            {
                SetContextGlobals(context);
            }
            
            // Execute the script
            var result = await _engine.ExecuteAsync<T>(_script.CompiledCode, context, combinedCts.Token);
            
            _executionStopwatch.Stop();
            _logger.LogDebug("Script execution #{ExecutionNumber} completed in {Duration}ms", 
                executionNumber, _executionStopwatch.ElapsedMilliseconds);
            
            return result;
        }
        catch (OperationCanceledException) when (_timeoutCts.Token.IsCancellationRequested)
        {
            _executionStopwatch.Stop();
            var timeoutException = new ScriptExecutionException(
                "Script execution timed out",
                _engine.EngineType,
                _executionStopwatch.Elapsed,
                isTimeout: true);
                
            _logger.LogWarning(timeoutException, "Script execution #{ExecutionNumber} timed out after {Duration}ms",
                executionNumber, _executionStopwatch.ElapsedMilliseconds);
                
            throw timeoutException;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _executionStopwatch.Stop();
            _logger.LogDebug("Script execution #{ExecutionNumber} was cancelled after {Duration}ms",
                executionNumber, _executionStopwatch.ElapsedMilliseconds);
            throw;
        }
        catch (Exception ex)
        {
            _executionStopwatch.Stop();
            var executionException = new ScriptExecutionException(
                ex.Message,
                _engine.EngineType,
                _executionStopwatch.Elapsed,
                innerException: ex);
                
            _logger.LogError(executionException, "Script execution #{ExecutionNumber} failed after {Duration}ms",
                executionNumber, _executionStopwatch.ElapsedMilliseconds);
                
            throw executionException;
        }
    }

    /// <inheritdoc />
    public void SetGlobal(string name, object? value)
    {
        ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        
        if (!IsValidJavaScriptIdentifier(name))
        {
            throw new ArgumentException($"'{name}' is not a valid JavaScript identifier", nameof(name));
        }
        
        try
        {
            _engine.SetGlobal(name, value);
            _logger.LogTrace("Set global variable '{Name}' to {ValueType}", name, value?.GetType().Name ?? "null");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to set global variable '{Name}'", name);
            throw;
        }
    }

    /// <inheritdoc />
    public void RegisterFunction(string name, Delegate function)
    {
        ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(function);
        
        if (!IsValidJavaScriptIdentifier(name))
        {
            throw new ArgumentException($"'{name}' is not a valid JavaScript identifier", nameof(name));
        }
        
        try
        {
            _engine.RegisterFunction(name, function);
            _logger.LogTrace("Registered function '{Name}' with signature {Signature}", 
                name, function.Method.ToString());
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to register function '{Name}'", name);
            throw;
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_isDisposed)
            return;

        try
        {
            _logger.LogDebug("Disposing script execution context after {ExecutionCount} executions",
                _executionCount);
            
            // Stop execution timer
            _timeoutCts?.Cancel();
            _timeoutCts?.Dispose();
            
            // Clean up engine state for next use
            try
            {
                _engine.Reset();
                _engine.CollectGarbage();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error cleaning up engine state during disposal");
            }
            
            // Return engine to pool
            _disposeCallback();
            
            _executionStopwatch?.Stop();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during script execution context disposal");
        }
        finally
        {
            _isDisposed = true;
        }
    }

    private void ConfigureEngineLimits()
    {
        try
        {
            // Set execution timeout
            _engine.MaxExecutionTime = _options.Timeout != TimeSpan.Zero 
                ? _options.Timeout 
                : _limits.DefaultMaxExecutionTime;
            
            // Set memory limit
            _engine.MemoryLimit = _options.MemoryLimit > 0 
                ? _options.MemoryLimit 
                : _limits.DefaultMaxMemoryUsage;
            
            // Set recursion limit
            _engine.RecursionLimit = _options.RecursionLimit > 0 
                ? _options.RecursionLimit 
                : _limits.DefaultMaxRecursionDepth;
            
            _logger.LogTrace("Configured engine limits: Timeout={Timeout}ms, Memory={Memory}MB, Recursion={Recursion}",
                _engine.MaxExecutionTime.TotalMilliseconds,
                _engine.MemoryLimit / (1024 * 1024),
                _engine.RecursionLimit);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to configure some engine limits");
        }
    }

    private void SetContextGlobals(object context)
    {
        if (context == null)
            return;
            
        try
        {
            // Handle different context types
            switch (context)
            {
                case IDictionary<string, object> dictionary:
                    foreach (var kvp in dictionary)
                    {
                        if (IsValidJavaScriptIdentifier(kvp.Key))
                        {
                            _engine.SetGlobal(kvp.Key, kvp.Value);
                        }
                    }
                    break;
                    
                default:
                    // Set the entire context object as 'context' global
                    _engine.SetGlobal("context", context);
                    break;
            }
            
            _logger.LogTrace("Set context globals from {ContextType}", context.GetType().Name);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to set context globals");
        }
    }

    private static bool IsValidJavaScriptIdentifier(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return false;
            
        // Basic JavaScript identifier validation
        // Must start with letter, $, or _
        // Can contain letters, digits, $, or _
        if (!char.IsLetter(name[0]) && name[0] != '$' && name[0] != '_')
            return false;
            
        for (int i = 1; i < name.Length; i++)
        {
            var c = name[i];
            if (!char.IsLetterOrDigit(c) && c != '$' && c != '_')
                return false;
        }
        
        // Check for reserved JavaScript keywords
        var reservedWords = new HashSet<string>
        {
            "abstract", "arguments", "await", "boolean", "break", "byte", "case", "catch",
            "char", "class", "const", "continue", "debugger", "default", "delete", "do",
            "double", "else", "enum", "eval", "export", "extends", "false", "final",
            "finally", "float", "for", "function", "goto", "if", "implements", "import",
            "in", "instanceof", "int", "interface", "let", "long", "native", "new",
            "null", "package", "private", "protected", "public", "return", "short",
            "static", "super", "switch", "synchronized", "this", "throw", "throws",
            "transient", "true", "try", "typeof", "var", "void", "volatile", "while",
            "with", "yield"
        };
        
        return !reservedWords.Contains(name.ToLowerInvariant());
    }

    private void ThrowIfDisposed()
    {
        if (_isDisposed)
        {
            throw new ObjectDisposedException(nameof(ScriptExecutionContext));
        }
    }
}