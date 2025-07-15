using Jint;
using Jint.Runtime;
using Microsoft.Extensions.Logging;

namespace FlowEngine.Core.Services;

/// <summary>
/// Basic Jint-based script engine service without complex features.
/// Focused on simple, reliable script execution for data processing pipelines.
/// No pooling, minimal caching, straightforward execution.
/// </summary>
public class BasicJintScriptEngineService : IScriptEngineService
{
    private readonly ILogger<BasicJintScriptEngineService> _logger;
    private readonly Dictionary<string, string> _scriptCache = new();
    private int _scriptsCompiled = 0;
    private int _scriptsExecuted = 0;
    private bool _disposed = false;

    /// <summary>
    /// Initializes a new instance of the BasicJintScriptEngineService class.
    /// </summary>
    /// <param name="logger">Logger instance for diagnostics</param>
    public BasicJintScriptEngineService(ILogger<BasicJintScriptEngineService> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Compiles JavaScript code with minimal validation.
    /// </summary>
    public Task<CompiledScript> CompileAsync(string script, ScriptOptions options)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(BasicJintScriptEngineService));

        // Basic validation - just check for process function
        if (!script.Contains("process"))
        {
            throw new InvalidOperationException("Script must contain a process function");
        }

        var scriptId = script.GetHashCode().ToString("X8");
        
        // Simple caching - just store the script text
        if (options.EnableCaching)
        {
            _scriptCache[scriptId] = script;
        }

        _scriptsCompiled++;
        _logger.LogDebug("Compiled script {ScriptId}", scriptId);

        var compiledScript = new CompiledScript(scriptId, script);
        return Task.FromResult(compiledScript);
    }

    /// <summary>
    /// Executes compiled script with basic error handling.
    /// </summary>
    public Task<ScriptResult> ExecuteAsync(CompiledScript compiledScript, IJavaScriptContext context)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(BasicJintScriptEngineService));

        try
        {
            // Create fresh engine for each execution (simple and safe)
            var engine = new Engine(options =>
                options.TimeoutInterval(TimeSpan.FromSeconds(5))
                       .MaxStatements(100000)
                       .Strict(true)
                       .AllowClrWrite(false));

            // Set up context in JavaScript engine
            engine.SetValue("context", context);

            // Execute script
            engine.Execute(compiledScript.Source);

            // Get and call process function
            var processFunction = engine.GetValue("process");
            if (processFunction.IsUndefined())
            {
                return Task.FromResult(ScriptResult.CreateError("Script must define a process function"));
            }

            // Execute process(context)
            var result = engine.Invoke(processFunction, context);
            
            _scriptsExecuted++;
            _logger.LogTrace("Executed script {ScriptId}", compiledScript.ScriptId);

            // Convert result to CLR type
            var clrResult = ConvertJsValue(result);
            return Task.FromResult(ScriptResult.CreateSuccess(clrResult));
        }
        catch (JavaScriptException ex)
        {
            _logger.LogError(ex, "JavaScript execution error in script {ScriptId}", compiledScript.ScriptId);
            return Task.FromResult(ScriptResult.CreateError($"JavaScript error: {ex.Message}", ex));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error executing script {ScriptId}", compiledScript.ScriptId);
            return Task.FromResult(ScriptResult.CreateError($"Execution failed: {ex.Message}", ex));
        }
    }

    /// <summary>
    /// Gets basic engine statistics.
    /// </summary>
    public ScriptEngineStats GetStats()
    {
        return new ScriptEngineStats
        {
            ScriptsCompiled = _scriptsCompiled,
            ScriptsExecuted = _scriptsExecuted,
            CacheHits = 0, // We don't track this in basic version
            CacheMisses = _scriptsCompiled,
            EnginePoolSize = 0, // No pooling
            EnginePoolActive = 0, // No pooling
            AstExecutions = _scriptsExecuted, // All executions are direct
            StringExecutions = 0
        };
    }

    /// <summary>
    /// Converts Jint JsValue to CLR type (basic conversion).
    /// </summary>
    private static object? ConvertJsValue(Jint.Native.JsValue jsValue)
    {
        if (jsValue.IsNull() || jsValue.IsUndefined())
            return null;
        
        if (jsValue.IsString())
            return jsValue.AsString();
        
        if (jsValue.IsBoolean())
            return jsValue.AsBoolean();
        
        if (jsValue.IsNumber())
            return jsValue.AsNumber();
        
        // For complex types, just return the string representation
        return jsValue.ToString();
    }

    /// <summary>
    /// Disposes of engine resources.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
            return;

        _scriptCache.Clear();
        _disposed = true;
        
        _logger.LogInformation("BasicJintScriptEngineService disposed. Scripts compiled: {Compiled}, executed: {Executed}",
            _scriptsCompiled, _scriptsExecuted);
    }
}