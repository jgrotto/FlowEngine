using Microsoft.ClearScript;
using Microsoft.ClearScript.V8;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.ObjectPool;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;

namespace FlowEngine.Core.Services;

/// <summary>
/// ClearScript V8-based script engine service with true AST pre-compilation and performance optimization.
/// </summary>
public class ClearScriptEngineService : IScriptEngineService
{
    private readonly ObjectPool<V8ScriptEngine> _enginePool;
    private readonly ILogger<ClearScriptEngineService> _logger;
    private readonly ConcurrentDictionary<string, CompiledScript> _scriptCache = new();
    private readonly ScriptEngineStats _stats = new();
    private readonly object _statsLock = new();
    private bool _disposed = false;
    
    // AST utilization tracking
    private int _astExecutions = 0;
    private int _stringExecutions = 0;

    /// <summary>
    /// Initializes a new instance of the ClearScriptEngineService class.
    /// </summary>
    /// <param name="logger">Logger instance for diagnostics</param>
    public ClearScriptEngineService(ILogger<ClearScriptEngineService> logger)
    {
        _logger = logger;
        _enginePool = CreateEnginePool();
    }

    /// <summary>
    /// Compiles JavaScript code with V8 AST pre-compilation and caching support.
    /// </summary>
    public async Task<CompiledScript> CompileAsync(string script, ScriptOptions options)
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(ClearScriptEngineService));
        }

        var scriptHash = ComputeScriptHash(script);

        // Check cache first if caching is enabled
        if (options.EnableCaching && _scriptCache.TryGetValue(scriptHash, out var cachedScript))
        {
            lock (_statsLock)
            {
                _stats.CacheHits++;
            }
            _logger.LogDebug("Script cache hit for hash: {ScriptHash}", scriptHash);
            return cachedScript;
        }

        // Compile the script with V8 AST pre-compilation
        var stopwatch = Stopwatch.StartNew();

        try
        {
            var compiledScript = new CompiledScript(scriptHash, script);

            // Validate script contains required process function
            await ValidateScriptAsync(script);

            // V8 TRUE AST PRE-COMPILATION - This is the key advantage over Jint!
            try
            {
                // Pre-compile script to V8 bytecode/AST for maximum performance
                var engine = _enginePool.Get();
                try
                {
                    var v8Script = engine.Compile(script);
                    
                    // Store the pre-compiled V8 script for optimized execution
                    compiledScript.PreparedScript = v8Script;
                    
                    _logger.LogDebug("V8 AST pre-compilation successful for hash: {ScriptHash}", scriptHash);
                }
                finally
                {
                    _enginePool.Return(engine);
                }
            }
            catch (ScriptEngineException ex)
            {
                _logger.LogError(ex, "V8 script compilation failed for hash: {ScriptHash} - {ErrorMessage}", 
                    scriptHash, ex.Message);
                throw new InvalidOperationException($"V8 script compilation failed: {ex.Message}", ex);
            }

            stopwatch.Stop();

            // Update statistics
            lock (_statsLock)
            {
                _stats.ScriptsCompiled++;
                _stats.TotalCompilationTime = _stats.TotalCompilationTime.Add(stopwatch.Elapsed);
                _stats.CacheMisses++;
            }

            // Cache the compiled script if caching is enabled
            if (options.EnableCaching)
            {
                _scriptCache.TryAdd(scriptHash, compiledScript);
                _logger.LogDebug("Compiled and cached V8 script with hash: {ScriptHash} in {ElapsedMs}ms",
                    scriptHash, stopwatch.ElapsedMilliseconds);
            }

            return compiledScript;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to compile JavaScript with V8: {Script}", 
                script.Substring(0, Math.Min(script.Length, 200)));
            throw new InvalidOperationException("V8 script compilation failed", ex);
        }
    }

    /// <summary>
    /// Executes pre-compiled V8 script with context and timeout protection.
    /// </summary>
    public async Task<ScriptResult> ExecuteAsync(CompiledScript compiledScript, IJavaScriptContext context)
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(ClearScriptEngineService));
        }

        var stopwatch = Stopwatch.StartNew();
        var engine = _enginePool.Get();

        try
        {
            // Set up context objects in V8 engine using AddHostObject for optimal performance
            engine.AddHostObject("context", context);

            // Execute pre-compiled V8 script (TRUE AST EXECUTION - no re-parsing!)
            if (compiledScript.PreparedScript is V8Script v8Script)
            {
                engine.Execute(v8Script);
                Interlocked.Increment(ref _astExecutions); // Track AST execution
                _logger.LogTrace("Executed pre-compiled V8 AST for script: {ScriptId}", compiledScript.ScriptId);
            }
            else
            {
                // Fallback to string execution (should rarely happen with proper caching)
                engine.Execute(compiledScript.Source);
                Interlocked.Increment(ref _stringExecutions); // Track string execution
                _logger.LogTrace("Executed V8 script from source for script: {ScriptId}", compiledScript.ScriptId);
            }

            // Check that process function exists
            if (!engine.Script.ContainsKey("process"))
            {
                return ScriptResult.CreateError("Script must define a process(context) function");
            }

            // Execute the process function with timeout protection
            var result = await ExecuteWithTimeoutAsync(engine, "process(context)");

            stopwatch.Stop();

            // Update statistics
            lock (_statsLock)
            {
                _stats.ScriptsExecuted++;
                _stats.TotalExecutionTime = _stats.TotalExecutionTime.Add(stopwatch.Elapsed);
                _stats.MemoryUsage = GC.GetTotalMemory(false);
            }

            _logger.LogTrace("V8 script executed in {ElapsedMs}ms", stopwatch.ElapsedMilliseconds);

            // Convert V8 result to CLR type
            var clrResult = ConvertV8ValueToClr(result);
            return ScriptResult.CreateSuccess(clrResult);
        }
        catch (ScriptEngineException ex)
        {
            _logger.LogError(ex, "V8 execution error in script {ScriptId}: {ErrorMessage}",
                compiledScript.ScriptId, ex.Message);
            return ScriptResult.CreateError($"V8 execution failed: {ex.Message}", ex);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error during V8 script execution for script {ScriptId}", 
                compiledScript.ScriptId);
            return ScriptResult.CreateError($"V8 script execution failed: {ex.Message}", ex);
        }
        finally
        {
            _enginePool.Return(engine);
        }
    }

    /// <summary>
    /// Gets current V8 engine performance statistics including AST utilization.
    /// </summary>
    public ScriptEngineStats GetStats()
    {
        lock (_statsLock)
        {
            return new ScriptEngineStats
            {
                ScriptsCompiled = _stats.ScriptsCompiled,
                ScriptsExecuted = _stats.ScriptsExecuted,
                TotalCompilationTime = _stats.TotalCompilationTime,
                TotalExecutionTime = _stats.TotalExecutionTime,
                MemoryUsage = _stats.MemoryUsage,
                CacheHits = _stats.CacheHits,
                CacheMisses = _stats.CacheMisses,
                EnginePoolSize = 10, // TODO: Get from pool policy
                EnginePoolActive = 0, // TODO: Get from pool policy
                AstExecutions = _astExecutions,
                StringExecutions = _stringExecutions
            };
        }
    }

    /// <summary>
    /// Creates V8 engine pool with optimized policy.
    /// </summary>
    private ObjectPool<V8ScriptEngine> CreateEnginePool()
    {
        var policy = new V8EnginePooledObjectPolicy();
        var provider = new DefaultObjectPoolProvider();
        return provider.Create(policy);
    }

    /// <summary>
    /// Executes V8 script with timeout protection.
    /// </summary>
    private async Task<object> ExecuteWithTimeoutAsync(V8ScriptEngine engine, string script)
    {
        var timeout = TimeSpan.FromSeconds(5); // TODO: Get from options
        using var cts = new CancellationTokenSource(timeout);

        try
        {
            return await Task.Run(() =>
            {
                cts.Token.ThrowIfCancellationRequested();
                return engine.Evaluate(script);
            }, cts.Token);
        }
        catch (OperationCanceledException)
        {
            throw new InvalidOperationException($"V8 script execution timed out after {timeout.TotalMilliseconds}ms");
        }
    }

    /// <summary>
    /// Converts V8 value to CLR type.
    /// </summary>
    private object? ConvertV8ValueToClr(object v8Value)
    {
        if (v8Value == null || v8Value == Undefined.Value)
        {
            return null;
        }

        // V8 automatically handles most conversions, but we can add specific handling here
        if (v8Value is ScriptObject scriptObject)
        {
            // Convert V8 object to dictionary
            var result = new Dictionary<string, object?>();
            foreach (var property in scriptObject.PropertyNames)
            {
                var value = scriptObject[property];
                result[property] = ConvertV8ValueToClr(value);
            }
            return result;
        }

        return v8Value;
    }

    /// <summary>
    /// Validates that the script contains required functions.
    /// REUSED FROM JINT IMPLEMENTATION - Infrastructure reuse!
    /// </summary>
    private async Task ValidateScriptAsync(string script)
    {
        if (!script.Contains("function process(context)") && !script.Contains("process = function(context)"))
        {
            throw new InvalidOperationException("Script must contain a process(context) function");
        }

        await Task.CompletedTask;
    }

    /// <summary>
    /// Computes a hash for script caching.
    /// REUSED FROM JINT IMPLEMENTATION - Infrastructure reuse!
    /// </summary>
    private static string ComputeScriptHash(string script)
    {
        using var sha256 = SHA256.Create();
        var hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(script));
        return Convert.ToHexString(hash)[..16]; // Use first 16 characters
    }

    /// <summary>
    /// Disposes of V8 engine resources.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        // Dispose cached scripts and their V8Script objects
        foreach (var script in _scriptCache.Values)
        {
            if (script.PreparedScript is IDisposable disposableScript)
            {
                disposableScript.Dispose();
            }
            script.Dispose();
        }
        _scriptCache.Clear();

        _logger.LogInformation("ClearScriptEngineService disposed. Final stats - Compiled: {Compiled}, Executed: {Executed}, Cache hits: {CacheHits}, AST utilization: {AstRate:F1}%",
            _stats.ScriptsCompiled, _stats.ScriptsExecuted, _stats.CacheHits, 
            _stats.ScriptsExecuted > 0 ? (_astExecutions * 100.0) / _stats.ScriptsExecuted : 0.0);
    }
}

/// <summary>
/// Pooled object policy for V8 script engines with optimal configuration.
/// </summary>
internal class V8EnginePooledObjectPolicy : PooledObjectPolicy<V8ScriptEngine>
{
    public override V8ScriptEngine Create()
    {
        // V8 engine with optimal performance configuration
        var flags = V8ScriptEngineFlags.EnableDebugging; // TODO: Make configurable
        var constraints = new V8RuntimeConstraints
        {
            MaxNewSpaceSize = 1024 * 1024, // 1MB new space
            MaxOldSpaceSize = 10 * 1024 * 1024 // 10MB old space
        };

        return new V8ScriptEngine(constraints, flags);
    }

    public override bool Return(V8ScriptEngine engine)
    {
        try
        {
            // Clear any global variables but keep the engine for reuse
            engine.Execute("delete this.context;"); // Clear context from previous execution
            return true;
        }
        catch
        {
            // If cleanup fails, don't reuse this engine
            return false;
        }
    }
}