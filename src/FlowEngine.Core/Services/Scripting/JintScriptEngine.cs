using Jint;
using Jint.Runtime;
using Microsoft.Extensions.ObjectPool;
using System.Collections.Concurrent;

namespace FlowEngine.Core.Services.Scripting;

/// <summary>
/// Jint-based JavaScript engine implementation providing pure .NET script execution.
/// Offers excellent compatibility and debugging support with moderate performance.
/// </summary>
/// <remarks>
/// Jint is a pure .NET JavaScript engine that provides:
/// - Full ECMAScript 5.1 compatibility with ES6+ features
/// - Excellent debugging and error reporting
/// - Native .NET integration without P/Invoke
/// - Consistent cross-platform behavior
/// 
/// Performance Characteristics:
/// - Compilation: 10-50ms for typical scripts
/// - Execution: 2-5x slower than V8 but predictable
/// - Memory: Efficient garbage collection integration
/// - Startup: Fast initialization, no native dependencies
/// 
/// Best suited for:
/// - Development and testing environments
/// - Cross-platform deployments without native dependencies
/// - Scripts requiring detailed debugging information
/// - Scenarios where consistent behavior is more important than peak performance
/// </remarks>
internal sealed class JintScriptEngine : IScriptEngine
{
    private readonly Engine _engine;
    private readonly TimeSpan _defaultTimeout;
    private readonly long _defaultMemoryLimit;
    private readonly int _defaultRecursionLimit;
    private readonly ConcurrentDictionary<string, object?> _globals;
    
    private bool _isDisposed;
    private TimeSpan _maxExecutionTime;
    private long _memoryLimit;
    private int _recursionLimit;

    /// <inheritdoc />
    public ScriptEngineType EngineType => ScriptEngineType.Jint;

    /// <inheritdoc />
    public bool IsHealthy => !_isDisposed && _engine != null;

    /// <inheritdoc />
    public bool IsDisposed => _isDisposed;

    /// <inheritdoc />
    public TimeSpan MaxExecutionTime
    {
        get => _maxExecutionTime;
        set
        {
            _maxExecutionTime = value;
            if (value > TimeSpan.Zero)
            {
                _engine.SetValue("__timeout", value.TotalMilliseconds);
            }
        }
    }

    /// <inheritdoc />
    public long MemoryLimit
    {
        get => _memoryLimit;
        set
        {
            _memoryLimit = value;
            // Note: Jint doesn't have direct memory limit support
            // We track this for monitoring purposes
        }
    }

    /// <inheritdoc />
    public int RecursionLimit
    {
        get => _recursionLimit;
        set
        {
            _recursionLimit = value;
            _engine.Options.Constraints.MaxRecursionDepth = value;
        }
    }

    /// <summary>
    /// Initializes a new instance of the JintScriptEngine class.
    /// </summary>
    /// <param name="options">Script engine configuration options</param>
    public JintScriptEngine(ScriptEngineOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        _defaultTimeout = options.ExecutionLimits.DefaultMaxExecutionTime;
        _defaultMemoryLimit = options.ExecutionLimits.DefaultMaxMemoryUsage;
        _defaultRecursionLimit = options.ExecutionLimits.DefaultMaxRecursionDepth;
        _globals = new ConcurrentDictionary<string, object?>();

        // Configure Jint engine with security and performance settings
        _engine = new Engine(cfg => cfg
            .AllowClr() // Allow calling .NET methods
            .Strict(options.Compilation.DefaultStrictMode)
            .DebugMode(options.Compilation.DefaultEnableDebugging)
            .TimeoutInterval(_defaultTimeout)
            .MaxRecursionDepth(_defaultRecursionLimit)
            .CatchClrExceptions(true));

        // Set initial limits
        _maxExecutionTime = _defaultTimeout;
        _memoryLimit = _defaultMemoryLimit;
        _recursionLimit = _defaultRecursionLimit;

        InitializeBuiltins();
    }

    /// <inheritdoc />
    public async Task<object> CompileAsync(
        string script, 
        ScriptCompilationOptions options, 
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrWhiteSpace(script);
        ArgumentNullException.ThrowIfNull(options);

        try
        {
            // Jint compiles scripts on-demand during execution
            // We'll prepare and validate the script here
            var preparedScript = new JintCompiledScript
            {
                Source = script,
                Options = options,
                CompileTime = DateTimeOffset.UtcNow
            };

            // Validate syntax by attempting to parse
            await Task.Run(() =>
            {
                try
                {
                    _engine.PrepareScript(script);
                }
                catch (ParserException ex)
                {
                    throw new ScriptCompilationException(
                        ex.Message,
                        ScriptEngineType.Jint,
                        ex.LineNumber,
                        ex.Column,
                        ex);
                }
            }, cancellationToken);

            return preparedScript;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (ScriptCompilationException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new ScriptCompilationException(
                ex.Message,
                ScriptEngineType.Jint,
                innerException: ex);
        }
    }

    /// <inheritdoc />
    public async Task<T?> ExecuteAsync<T>(
        object compiledScript, 
        object? context = null, 
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(compiledScript);

        if (compiledScript is not JintCompiledScript jintScript)
        {
            throw new ArgumentException(
                $"Compiled script must be of type {nameof(JintCompiledScript)}", 
                nameof(compiledScript));
        }

        try
        {
            // Set up cancellation
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            if (_maxExecutionTime > TimeSpan.Zero)
            {
                cts.CancelAfter(_maxExecutionTime);
            }

            // Apply context if provided
            if (context != null)
            {
                ApplyExecutionContext(context);
            }

            // Execute the script
            var result = await Task.Run(() =>
            {
                try
                {
                    var value = _engine.Evaluate(jintScript.Source);
                    return ConvertJintValue<T>(value);
                }
                catch (JavaScriptException ex)
                {
                    throw new ScriptExecutionException(
                        ex.Message,
                        ScriptEngineType.Jint,
                        TimeSpan.Zero,
                        innerException: ex);
                }
                catch (TimeoutException ex)
                {
                    throw new ScriptExecutionException(
                        "Script execution timed out",
                        ScriptEngineType.Jint,
                        _maxExecutionTime,
                        isTimeout: true,
                        innerException: ex);
                }
            }, cts.Token);

            return result;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            throw new ScriptExecutionException(
                "Script execution timed out",
                ScriptEngineType.Jint,
                _maxExecutionTime,
                isTimeout: true);
        }
        catch (ScriptExecutionException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new ScriptExecutionException(
                ex.Message,
                ScriptEngineType.Jint,
                TimeSpan.Zero,
                innerException: ex);
        }
    }

    /// <inheritdoc />
    public void SetGlobal(string name, object? value)
    {
        ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        try
        {
            _globals[name] = value;
            _engine.SetValue(name, value);
        }
        catch (Exception ex)
        {
            throw new ArgumentException($"Failed to set global variable '{name}': {ex.Message}", ex);
        }
    }

    /// <inheritdoc />
    public object? GetGlobal(string name)
    {
        ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        return _globals.TryGetValue(name, out var value) ? value : null;
    }

    /// <inheritdoc />
    public void RemoveGlobal(string name)
    {
        ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        _globals.TryRemove(name, out _);
        try
        {
            _engine.SetValue(name, Jint.Native.JsValue.Undefined);
        }
        catch
        {
            // Ignore errors when removing globals
        }
    }

    /// <inheritdoc />
    public void RegisterFunction(string name, Delegate function)
    {
        ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(function);

        try
        {
            _engine.SetValue(name, function);
        }
        catch (Exception ex)
        {
            throw new ArgumentException($"Failed to register function '{name}': {ex.Message}", ex);
        }
    }

    /// <inheritdoc />
    public void Reset()
    {
        ThrowIfDisposed();

        try
        {
            // Clear all globals
            foreach (var global in _globals.Keys.ToList())
            {
                RemoveGlobal(global);
            }

            // Re-initialize built-in functions
            InitializeBuiltins();
        }
        catch (Exception)
        {
            // Ignore reset errors - engine will be discarded if unhealthy
        }
    }

    /// <inheritdoc />
    public void CollectGarbage()
    {
        // Jint uses .NET garbage collection
        // Force a collection to free up memory
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    /// <inheritdoc />
    public bool ValidateHealth()
    {
        if (_isDisposed)
            return false;

        try
        {
            // Simple health check - execute a basic script
            _engine.Evaluate("1 + 1");
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_isDisposed)
            return;

        try
        {
            _globals.Clear();
            _engine?.Dispose();
        }
        catch
        {
            // Ignore disposal errors
        }
        finally
        {
            _isDisposed = true;
        }
    }

    private void InitializeBuiltins()
    {
        // Add safe console functions
        _engine.SetValue("console", new
        {
            log = new Action<object?>(obj => { /* Safe console implementation */ }),
            warn = new Action<object?>(obj => { /* Safe console implementation */ }),
            error = new Action<object?>(obj => { /* Safe console implementation */ })
        });

        // Add safe Math and Date objects (Jint provides these by default)
        // Additional built-ins can be added here
    }

    private void ApplyExecutionContext(object context)
    {
        switch (context)
        {
            case IDictionary<string, object> dictionary:
                foreach (var kvp in dictionary)
                {
                    SetGlobal(kvp.Key, kvp.Value);
                }
                break;
                
            default:
                SetGlobal("context", context);
                break;
        }
    }

    private static T? ConvertJintValue<T>(Jint.Native.JsValue value)
    {
        if (value.IsNull() || value.IsUndefined())
            return default(T);

        try
        {
            // Handle common type conversions
            var targetType = typeof(T);
            var jsObject = value.ToObject();

            if (jsObject is T directMatch)
                return directMatch;

            // Attempt type conversion
            return (T?)Convert.ChangeType(jsObject, targetType);
        }
        catch
        {
            // Return default if conversion fails
            return default(T);
        }
    }

    private void ThrowIfDisposed()
    {
        if (_isDisposed)
        {
            throw new ObjectDisposedException(nameof(JintScriptEngine));
        }
    }
}

/// <summary>
/// Compiled script representation for Jint engine.
/// </summary>
internal sealed class JintCompiledScript
{
    /// <summary>
    /// Gets or sets the JavaScript source code.
    /// </summary>
    public required string Source { get; init; }

    /// <summary>
    /// Gets or sets the compilation options used.
    /// </summary>
    public required ScriptCompilationOptions Options { get; init; }

    /// <summary>
    /// Gets or sets the compilation timestamp.
    /// </summary>
    public required DateTimeOffset CompileTime { get; init; }
}

/// <summary>
/// Object pool policy for Jint script engines.
/// </summary>
public sealed class JintEnginePooledObjectPolicy : IPooledObjectPolicy<IScriptEngine>
{
    private readonly ScriptEngineOptions _options;

    /// <summary>
    /// Initializes a new instance of the JintEnginePooledObjectPolicy class.
    /// </summary>
    /// <param name="options">Script engine configuration options</param>
    public JintEnginePooledObjectPolicy(ScriptEngineOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    /// <inheritdoc />
    public IScriptEngine Create()
    {
        return new JintScriptEngine(_options);
    }

    /// <inheritdoc />
    public bool Return(IScriptEngine obj)
    {
        if (obj == null || obj.IsDisposed)
            return false;

        try
        {
            // Reset engine state for reuse
            obj.Reset();
            
            // Perform garbage collection
            obj.CollectGarbage();
            
            // Validate engine health
            return obj.ValidateHealth();
        }
        catch
        {
            // Engine is unhealthy, don't return to pool
            try
            {
                obj.Dispose();
            }
            catch
            {
                // Ignore disposal errors
            }
            
            return false;
        }
    }
}

/// <summary>
/// Placeholder object pool policy for V8 script engines.
/// V8 integration requires Microsoft.ClearScript.V8 package which has native dependencies.
/// </summary>
public sealed class V8EnginePooledObjectPolicy : IPooledObjectPolicy<IScriptEngine>
{
    private readonly ScriptEngineOptions _options;

    /// <summary>
    /// Initializes a new instance of the V8EnginePooledObjectPolicy class.
    /// </summary>
    /// <param name="options">Script engine configuration options</param>
    public V8EnginePooledObjectPolicy(ScriptEngineOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    /// <inheritdoc />
    public IScriptEngine Create()
    {
        // For Phase 3.1, we'll use Jint as fallback when V8 is not available
        // TODO: Implement actual V8 engine when Microsoft.ClearScript.V8 is added
        return new JintScriptEngine(_options);
    }

    /// <inheritdoc />
    public bool Return(IScriptEngine obj)
    {
        if (obj == null || obj.IsDisposed)
            return false;

        try
        {
            obj.Reset();
            obj.CollectGarbage();
            return obj.ValidateHealth();
        }
        catch
        {
            try
            {
                obj.Dispose();
            }
            catch
            {
                // Ignore disposal errors
            }
            
            return false;
        }
    }
}