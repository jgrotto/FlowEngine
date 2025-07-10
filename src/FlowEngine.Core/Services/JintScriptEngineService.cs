using Jint;
using Jint.Native;
using Jint.Native.Object;
using Jint.Runtime;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.ObjectPool;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;

namespace FlowEngine.Core.Services;

/// <summary>
/// Jint-based script engine service with engine pooling and performance optimization.
/// </summary>
public class JintScriptEngineService : IScriptEngineService
{
    private readonly ObjectPool<Engine> _enginePool;
    private readonly ILogger<JintScriptEngineService> _logger;
    private readonly ConcurrentDictionary<string, CompiledScript> _scriptCache = new();
    private readonly ScriptEngineStats _stats = new();
    private readonly object _statsLock = new();
    private bool _disposed = false;

    /// <summary>
    /// Initializes a new instance of the JintScriptEngineService class.
    /// </summary>
    /// <param name="logger">Logger instance for diagnostics</param>
    public JintScriptEngineService(ILogger<JintScriptEngineService> logger)
    {
        _logger = logger;
        _enginePool = CreateEnginePool();
    }

    /// <summary>
    /// Compiles JavaScript code with caching support.
    /// </summary>
    public async Task<CompiledScript> CompileAsync(string script, ScriptOptions options)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(JintScriptEngineService));

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

        // Compile the script
        var stopwatch = Stopwatch.StartNew();
        
        try
        {
            var compiledScript = new CompiledScript(scriptHash, script);
            
            // Validate script contains required process function
            await ValidateScriptAsync(script);
            
            // Pre-compile script for validation
            var engine = _enginePool.Get();
            try
            {
                engine.Execute(script);
                _logger.LogDebug("Script pre-compilation successful");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Script pre-compilation failed, will compile at runtime");
                // Continue without pre-compilation
            }
            finally
            {
                _enginePool.Return(engine);
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
                _logger.LogDebug("Compiled and cached script with hash: {ScriptHash} in {ElapsedMs}ms", 
                    scriptHash, stopwatch.ElapsedMilliseconds);
            }
            
            return compiledScript;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to compile JavaScript: {Script}", script.Substring(0, Math.Min(script.Length, 200)));
            throw new InvalidOperationException("Script compilation failed", ex);
        }
    }

    /// <summary>
    /// Executes compiled script with context and timeout protection.
    /// </summary>
    public async Task<ScriptResult> ExecuteAsync(CompiledScript compiledScript, IJavaScriptContext context)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(JintScriptEngineService));

        var stopwatch = Stopwatch.StartNew();
        var engine = _enginePool.Get();
        
        try
        {
            // Set up context objects in JavaScript engine
            engine.SetValue("context", context);
            
            // Execute the script
            engine.Execute(compiledScript.Source);
            
            // Check that process function exists
            var processFunction = engine.GetValue("process");
            if (processFunction.IsUndefined() || !processFunction.IsObject())
            {
                return ScriptResult.CreateError("Script must define a process(context) function");
            }
            
            // Execute the process function
            var result = await ExecuteWithTimeoutAsync(engine, "process(context)");
            
            stopwatch.Stop();
            
            // Update statistics
            lock (_statsLock)
            {
                _stats.ScriptsExecuted++;
                _stats.TotalExecutionTime = _stats.TotalExecutionTime.Add(stopwatch.Elapsed);
                _stats.MemoryUsage = GC.GetTotalMemory(false);
            }
            
            _logger.LogTrace("Script executed in {ElapsedMs}ms", stopwatch.ElapsedMilliseconds);
            
            // Convert result to CLR type
            var clrResult = ConvertJsValueToClr(result);
            return ScriptResult.CreateSuccess(clrResult);
        }
        catch (JavaScriptException ex)
        {
            _logger.LogError(ex, "JavaScript execution error in script {ScriptId}: {ErrorMessage}", 
                compiledScript.ScriptId, ex.Message);
            return ScriptResult.CreateError($"JavaScript execution failed: {ex.Message}", ex);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error during script execution for script {ScriptId}", compiledScript.ScriptId);
            return ScriptResult.CreateError($"Script execution failed: {ex.Message}", ex);
        }
        finally
        {
            _enginePool.Return(engine);
        }
    }

    /// <summary>
    /// Gets current engine performance statistics.
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
                EnginePoolActive = 0 // TODO: Get from pool policy
            };
        }
    }

    /// <summary>
    /// Creates engine pool with custom policy.
    /// </summary>
    private ObjectPool<Engine> CreateEnginePool()
    {
        var policy = new JintEnginePooledObjectPolicy();
        var provider = new DefaultObjectPoolProvider();
        return provider.Create(policy);
    }

    /// <summary>
    /// Executes script with timeout protection.
    /// </summary>
    private async Task<JsValue> ExecuteWithTimeoutAsync(Engine engine, string script)
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
            throw new InvalidOperationException($"Script execution timed out after {timeout.TotalMilliseconds}ms");
        }
    }

    /// <summary>
    /// Converts Jint JsValue to CLR type.
    /// </summary>
    private object? ConvertJsValueToClr(JsValue jsValue)
    {
        if (jsValue.IsNull() || jsValue.IsUndefined())
            return null;

        if (jsValue.IsString())
            return jsValue.AsString();
        
        if (jsValue.IsBoolean())
            return jsValue.AsBoolean();
        
        if (jsValue.IsNumber())
            return jsValue.AsNumber();
        
        if (jsValue.IsDate())
            return jsValue.AsDate().ToDateTime();

        if (jsValue.IsObject())
        {
            var jsObject = jsValue.AsObject();
            var result = new Dictionary<string, object?>();
            
            foreach (var property in jsObject.GetOwnProperties())
            {
                var key = property.Key.AsString();
                var value = property.Value.Value;
                result[key] = ConvertJsValueToClr(value);
            }
            
            return result;
        }

        return jsValue.ToObject();
    }

    /// <summary>
    /// Validates that the script contains required functions.
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
    /// </summary>
    private static string ComputeScriptHash(string script)
    {
        using var sha256 = SHA256.Create();
        var hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(script));
        return Convert.ToHexString(hash)[..16]; // Use first 16 characters
    }

    /// <summary>
    /// Disposes of engine resources.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        
        // Dispose cached scripts
        foreach (var script in _scriptCache.Values)
        {
            script.Dispose();
        }
        _scriptCache.Clear();
        
        _logger.LogInformation("JintScriptEngineService disposed. Final stats - Compiled: {Compiled}, Executed: {Executed}, Cache hits: {CacheHits}",
            _stats.ScriptsCompiled, _stats.ScriptsExecuted, _stats.CacheHits);
    }
}

/// <summary>
/// Pooled object policy for Jint engines.
/// </summary>
internal class JintEnginePooledObjectPolicy : PooledObjectPolicy<Engine>
{
    public override Engine Create()
    {
        var options = new Options()
            .TimeoutInterval(TimeSpan.FromSeconds(5))
            .MaxStatements(100000)
            .Strict(false)
            .AllowClrWrite(false); // Security: prevent writing to CLR objects

        return new Engine(options);
    }

    public override bool Return(Engine engine)
    {
        // Jint engines don't need explicit reset, just return true
        // Engine state is automatically managed by Jint
        return true;
    }
}
