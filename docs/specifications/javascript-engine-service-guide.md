# JavaScript Engine Service - Architecture & Implementation Guide

**Document Type**: Architecture & Implementation Guide  
**Length**: 5 pages  
**Audience**: Core Team, Plugin Developers  
**Status**: Complete v1.0

---

## Overview

The JavaScript Engine Service is a **core FlowEngine service** that provides centralized script compilation, caching, and execution capabilities. It enables multiple plugins to efficiently share JavaScript engines while maintaining isolation, security, and performance.

**Key Principle**: Script engines are expensive resources that must be pooled and shared across the application, not created per-plugin.

---

## Architecture

### High-Level Design

```
┌─────────────────────────────────────────────────────────────────┐
│                    FlowEngine Core Services                      │
├─────────────────────────────────────────────────────────────────┤
│                                                                 │
│  ┌─────────────────────────────────────────────────────────┐  │
│  │              JavaScript Engine Service                    │  │
│  │                                                          │  │
│  │  ┌────────────────┐  ┌────────────────┐  ┌───────────┐ │  │
│  │  │  Engine Pool    │  │ Script Cache   │  │ Security  │ │  │
│  │  │                 │  │                │  │ Sandbox   │ │  │
│  │  │ • V8 Engines   │  │ • Compiled     │  │           │ │  │
│  │  │ • Jint Engines │  │ • LRU Eviction │  │ • Limits  │ │  │
│  │  │ • Auto-scaling │  │ • TTL Support  │  │ • Timeout │ │  │
│  │  └────────────────┘  └────────────────┘  └───────────┘ │  │
│  └─────────────────────────────────────────────────────────┘  │
│                              ▲                                  │
│  ┌───────────────────────────┼────────────────────────────┐   │
│  │         Plugin Layer       │                            │   │
│  │  ┌──────────────┐  ┌──────┼─────────┐  ┌────────────┐ │   │
│  │  │ JS Transform │  │ JS Validator   │  │ JS Router  │ │   │
│  │  │   Plugin     │  │    Plugin      │  │   Plugin   │ │   │
│  │  └──────────────┘  └────────────────┘  └────────────┘ │   │
│  └─────────────────────────────────────────────────────────┘  │
└─────────────────────────────────────────────────────────────────┘
```

### Core Interfaces

```csharp
namespace FlowEngine.Core.Services.Scripting
{
    /// <summary>
    /// Primary interface for script engine operations
    /// </summary>
    public interface IScriptEngineService
    {
        /// <summary>
        /// Compiles a script with caching support
        /// </summary>
        Task<CompiledScript> CompileAsync(
            string script, 
            ScriptCompilationOptions options,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Creates an execution context for a compiled script
        /// </summary>
        Task<IScriptExecution> CreateExecutionAsync(
            CompiledScript script,
            ExecutionOptions options,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Validates script safety and syntax
        /// </summary>
        Task<ScriptValidationResult> ValidateAsync(
            string script,
            ValidationOptions options,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Gets current service health and metrics
        /// </summary>
        ScriptEngineHealth GetHealth();
    }

    /// <summary>
    /// Represents a compiled script that can be executed multiple times
    /// </summary>
    public sealed class CompiledScript
    {
        public string Id { get; }
        public string SourceHash { get; }
        public ScriptMetadata Metadata { get; }
        public DateTimeOffset CompiledAt { get; }
        public long CompilationTimeMs { get; }
        internal object CompiledCode { get; } // Engine-specific compiled representation
    }

    /// <summary>
    /// Execution context for a script
    /// </summary>
    public interface IScriptExecution : IAsyncDisposable
    {
        /// <summary>
        /// Executes the script with provided context
        /// </summary>
        Task<T> ExecuteAsync<T>(
            object context, 
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Sets a global variable in the execution context
        /// </summary>
        void SetGlobal(string name, object value);

        /// <summary>
        /// Registers a callback function
        /// </summary>
        void RegisterFunction(string name, Delegate function);
    }
}
```

---

## Implementation

### Service Implementation

```csharp
public class ScriptEngineService : IScriptEngineService, IHostedService
{
    private readonly IMemoryCache _scriptCache;
    private readonly ObjectPool<IScriptEngine> _v8EnginePool;
    private readonly ObjectPool<IScriptEngine> _jintEnginePool;
    private readonly IScriptSecurityValidator _securityValidator;
    private readonly ILogger<ScriptEngineService> _logger;
    private readonly ScriptEngineOptions _options;
    private readonly ScriptEngineMetrics _metrics;

    public ScriptEngineService(
        IOptions<ScriptEngineOptions> options,
        IMemoryCache memoryCache,
        IScriptSecurityValidator securityValidator,
        ILogger<ScriptEngineService> logger,
        IMetricsCollector metricsCollector)
    {
        _options = options.Value;
        _scriptCache = memoryCache;
        _securityValidator = securityValidator;
        _logger = logger;
        _metrics = new ScriptEngineMetrics(metricsCollector);

        // Initialize engine pools
        _v8EnginePool = CreateEnginePool(ScriptEngineType.V8);
        _jintEnginePool = CreateEnginePool(ScriptEngineType.Jint);
    }

    public async Task<CompiledScript> CompileAsync(
        string script, 
        ScriptCompilationOptions options,
        CancellationToken cancellationToken)
    {
        // Generate cache key
        var cacheKey = GenerateCacheKey(script, options);
        
        // Try to get from cache
        if (_scriptCache.TryGetValue<CompiledScript>(cacheKey, out var cached))
        {
            _metrics.RecordCacheHit();
            return cached;
        }

        _metrics.RecordCacheMiss();

        // Validate script security
        var validationResult = await _securityValidator.ValidateAsync(script, cancellationToken);
        if (!validationResult.IsValid)
        {
            throw new ScriptSecurityException(validationResult.Violations);
        }

        // Compile script
        var stopwatch = Stopwatch.StartNew();
        var engine = GetEngineFromPool(options.EngineType);
        
        try
        {
            var compiled = await engine.CompileAsync(script, options, cancellationToken);
            stopwatch.Stop();

            var compiledScript = new CompiledScript
            {
                Id = Guid.NewGuid().ToString(),
                SourceHash = HashScript(script),
                Metadata = ExtractMetadata(script, options),
                CompiledAt = DateTimeOffset.UtcNow,
                CompilationTimeMs = stopwatch.ElapsedMilliseconds,
                CompiledCode = compiled
            };

            // Cache the compiled script
            var cacheEntryOptions = new MemoryCacheEntryOptions
            {
                SlidingExpiration = _options.ScriptCacheExpiration,
                Size = script.Length,
                PostEvictionCallbacks =
                {
                    new PostEvictionCallbackRegistration
                    {
                        EvictionCallback = OnScriptEvicted,
                        State = compiledScript
                    }
                }
            };

            _scriptCache.Set(cacheKey, compiledScript, cacheEntryOptions);
            _metrics.RecordCompilation(stopwatch.ElapsedMilliseconds, script.Length);

            return compiledScript;
        }
        finally
        {
            ReturnEngineToPool(engine, options.EngineType);
        }
    }

    public async Task<IScriptExecution> CreateExecutionAsync(
        CompiledScript script,
        ExecutionOptions options,
        CancellationToken cancellationToken)
    {
        var engine = GetEngineFromPool(options.EngineType);
        
        // Create execution context with security constraints
        var context = new ScriptExecutionContext(
            engine,
            script,
            options,
            _options.ExecutionLimits,
            () => ReturnEngineToPool(engine, options.EngineType));

        // Apply security sandbox
        await ApplySecuritySandbox(context, options);

        return context;
    }

    private ObjectPool<IScriptEngine> CreateEnginePool(ScriptEngineType type)
    {
        var poolProvider = new DefaultObjectPoolProvider
        {
            MaximumRetained = _options.EnginePoolSize
        };

        var policy = type switch
        {
            ScriptEngineType.V8 => new V8EnginePooledObjectPolicy(_options),
            ScriptEngineType.Jint => new JintEnginePooledObjectPolicy(_options),
            _ => throw new NotSupportedException($"Engine type {type} not supported")
        };

        return poolProvider.Create(policy);
    }
}
```

### Engine Pool Management

```csharp
public class V8EnginePooledObjectPolicy : IPooledObjectPolicy<IScriptEngine>
{
    private readonly ScriptEngineOptions _options;

    public V8EnginePooledObjectPolicy(ScriptEngineOptions options)
    {
        _options = options;
    }

    public IScriptEngine Create()
    {
        var engine = new V8ScriptEngine();
        
        // Configure engine limits
        engine.MaxExecutionTime = _options.ExecutionLimits.MaxExecutionTime;
        engine.MemoryLimit = _options.ExecutionLimits.MaxMemoryUsage;
        engine.RecursionLimit = _options.ExecutionLimits.MaxRecursionDepth;
        
        // Set up built-in functions
        engine.AddHostObject("console", new ScriptConsole());
        engine.AddHostType("Math", typeof(Math));
        engine.AddHostType("Date", typeof(DateTime));
        
        return engine;
    }

    public bool Return(IScriptEngine engine)
    {
        if (engine.IsDisposed)
            return false;

        try
        {
            // Reset engine state
            engine.Reset();
            
            // Clear any accumulated data
            engine.CollectGarbage();
            
            // Check health
            return engine.IsHealthy();
        }
        catch
        {
            // Engine is unhealthy, don't return to pool
            return false;
        }
    }
}
```

---

## Configuration

### Application Configuration

```yaml
scriptEngine:
  # Engine pool configuration
  enginePoolSize: 10
  engineType: "V8"  # V8, Jint, or Auto
  
  # Compilation settings
  compilation:
    timeout: 5000ms
    optimizationLevel: "Balanced"  # None, Balanced, Maximum
    strictMode: true
    
  # Execution limits
  executionLimits:
    maxExecutionTime: 30000ms
    maxMemoryUsage: 100MB
    maxRecursionDepth: 1000
    maxIterations: 1000000
    
  # Caching configuration
  scriptCache:
    enabled: true
    maxSize: 1000  # Maximum number of cached scripts
    expiration: 1h
    evictionPolicy: "LRU"
    
  # Security settings
  security:
    enableSandbox: true
    allowedGlobals:
      - "Math"
      - "Date"
      - "JSON"
    blockedGlobals:
      - "process"
      - "require"
      - "eval"
    scriptAnalysis:
      detectInfiniteLoops: true
      detectMemoryLeaks: true
      maxComplexity: 100
```

### Dependency Injection

```csharp
// In Startup.cs or Program.cs
public void ConfigureServices(IServiceCollection services)
{
    // Register script engine service
    services.Configure<ScriptEngineOptions>(Configuration.GetSection("scriptEngine"));
    services.AddSingleton<IScriptEngineService, ScriptEngineService>();
    services.AddSingleton<IScriptSecurityValidator, ScriptSecurityValidator>();
    services.AddHostedService<ScriptEngineService>(); // For pool warm-up
    
    // Register memory cache for script caching
    services.AddMemoryCache(options =>
    {
        options.SizeLimit = Configuration.GetValue<long>("scriptEngine:scriptCache:maxSize");
    });
    
    // Register metrics
    services.AddSingleton<IMetricsCollector, PrometheusMetricsCollector>();
}
```

---

## Usage in Plugins

### JavaScript Transform Plugin Example

```csharp
public class JavaScriptTransformService
{
    private readonly IScriptEngineService _scriptEngine;
    private readonly ILogger<JavaScriptTransformService> _logger;

    public JavaScriptTransformService(
        IScriptEngineService scriptEngine,
        ILogger<JavaScriptTransformService> logger)
    {
        _scriptEngine = scriptEngine;
        _logger = logger;
    }

    public async Task<ProcessResult> ProcessAsync(
        Chunk chunk,
        JavaScriptTransformConfig config,
        CancellationToken cancellationToken)
    {
        // Compile script (will use cache if available)
        var compiledScript = await _scriptEngine.CompileAsync(
            config.Script,
            new ScriptCompilationOptions
            {
                EngineType = config.EngineType,
                OptimizationLevel = OptimizationLevel.Maximum,
                EnableDebugging = config.EnableDebugging
            },
            cancellationToken);

        // Create execution context
        using var execution = await _scriptEngine.CreateExecutionAsync(
            compiledScript,
            new ExecutionOptions
            {
                EngineType = config.EngineType,
                Timeout = config.ExecutionTimeout,
                MemoryLimit = config.MemoryLimit
            },
            cancellationToken);

        // Set up context
        var context = new ScriptContext(chunk.Schema);
        execution.SetGlobal("context", context);
        execution.RegisterFunction("log", (Action<string>)(msg => _logger.LogDebug(msg)));

        // Process rows
        var outputRows = new List<ArrayRow>();
        foreach (var row in chunk.Rows)
        {
            execution.SetGlobal("row", row);
            
            var result = await execution.ExecuteAsync<object>(
                config.Script,
                cancellationToken);

            if (result != null)
            {
                outputRows.Add(ConvertToArrayRow(result, config.OutputSchema));
            }
        }

        return ProcessResult.Success(new Chunk(outputRows, config.OutputSchema));
    }
}
```

---

## Performance Considerations

### Engine Pool Sizing

```csharp
// Calculate optimal pool size based on workload
public static int CalculateOptimalPoolSize(WorkloadCharacteristics workload)
{
    var coreCount = Environment.ProcessorCount;
    var baseSize = coreCount * 2; // Starting point
    
    // Adjust based on script complexity
    var complexityMultiplier = workload.AverageScriptComplexity switch
    {
        ScriptComplexity.Simple => 1.0,
        ScriptComplexity.Moderate => 1.5,
        ScriptComplexity.Complex => 2.0,
        _ => 1.5
    };
    
    // Adjust based on execution time
    var executionMultiplier = workload.AverageExecutionTimeMs switch
    {
        < 100 => 1.0,
        < 500 => 1.5,
        < 1000 => 2.0,
        _ => 3.0
    };
    
    var optimalSize = (int)(baseSize * complexityMultiplier * executionMultiplier);
    
    // Apply bounds
    return Math.Clamp(optimalSize, 4, 50);
}
```

### Monitoring and Metrics

```csharp
public class ScriptEngineMetrics
{
    private readonly IMetricsCollector _collector;
    
    // Compilation metrics
    public void RecordCompilation(long durationMs, int scriptSize)
    {
        _collector.RecordHistogram("script_compilation_duration_ms", durationMs);
        _collector.RecordHistogram("script_size_bytes", scriptSize);
        _collector.IncrementCounter("script_compilations_total");
    }
    
    // Cache metrics
    public void RecordCacheHit() => _collector.IncrementCounter("script_cache_hits_total");
    public void RecordCacheMiss() => _collector.IncrementCounter("script_cache_misses_total");
    
    // Pool metrics
    public void RecordPoolMetrics(string engineType, int active, int idle, int total)
    {
        _collector.RecordGauge($"engine_pool_active_{engineType}", active);
        _collector.RecordGauge($"engine_pool_idle_{engineType}", idle);
        _collector.RecordGauge($"engine_pool_total_{engineType}", total);
    }
    
    // Execution metrics
    public void RecordExecution(long durationMs, bool success, string? error = null)
    {
        _collector.RecordHistogram("script_execution_duration_ms", durationMs,
            ("success", success.ToString()),
            ("error", error ?? "none"));
    }
}
```

---

## Security Considerations

### Script Validation

```csharp
public class ScriptSecurityValidator : IScriptSecurityValidator
{
    private readonly SecurityOptions _options;
    private readonly ILogger<ScriptSecurityValidator> _logger;

    public async Task<ScriptValidationResult> ValidateAsync(
        string script,
        CancellationToken cancellationToken)
    {
        var violations = new List<SecurityViolation>();

        // Check for blocked patterns
        foreach (var pattern in _options.BlockedPatterns)
        {
            if (Regex.IsMatch(script, pattern.Pattern, pattern.Options))
            {
                violations.Add(new SecurityViolation(
                    pattern.Severity,
                    pattern.Description,
                    pattern.Remediation));
            }
        }

        // Analyze complexity
        var complexity = AnalyzeComplexity(script);
        if (complexity > _options.MaxComplexity)
        {
            violations.Add(new SecurityViolation(
                SecuritySeverity.High,
                $"Script complexity ({complexity}) exceeds maximum allowed ({_options.MaxComplexity})",
                "Simplify script logic or break into smaller functions"));
        }

        // Check for infinite loops
        if (_options.DetectInfiniteLoops && await HasPotentialInfiniteLoop(script))
        {
            violations.Add(new SecurityViolation(
                SecuritySeverity.Critical,
                "Potential infinite loop detected",
                "Ensure all loops have proper termination conditions"));
        }

        return new ScriptValidationResult(violations);
    }
}
```

### Sandbox Configuration

```csharp
public class ScriptExecutionContext : IScriptExecution
{
    private readonly IScriptEngine _engine;
    private readonly ExecutionLimits _limits;
    private readonly CancellationTokenSource _timeoutCts;

    public ScriptExecutionContext(
        IScriptEngine engine,
        CompiledScript script,
        ExecutionOptions options,
        ExecutionLimits limits,
        Action disposeCallback)
    {
        _engine = engine;
        _limits = limits;
        _timeoutCts = new CancellationTokenSource(limits.MaxExecutionTime);
        
        // Apply security sandbox
        ApplySandbox();
    }

    private void ApplySandbox()
    {
        // Remove dangerous globals
        _engine.RemoveGlobal("process");
        _engine.RemoveGlobal("require");
        _engine.RemoveGlobal("eval");
        _engine.RemoveGlobal("Function");
        
        // Add safe globals
        _engine.AddHostObject("console", new SafeConsole(_limits));
        _engine.AddHostType("Math", typeof(Math));
        _engine.AddHostType("JSON", typeof(JsonConvert));
        
        // Set resource limits
        _engine.MaxExecutionTime = _limits.MaxExecutionTime;
        _engine.MemoryLimit = _limits.MaxMemoryUsage;
        _engine.RecursionLimit = _limits.MaxRecursionDepth;
    }
}
```

---

## Best Practices

### For Core Team

1. **Pool Sizing**: Monitor actual usage and adjust pool size dynamically
2. **Cache Strategy**: Use sliding expiration for frequently used scripts
3. **Security First**: Always validate scripts before compilation
4. **Metrics**: Track all operations for performance optimization
5. **Health Checks**: Implement comprehensive health monitoring

### For Plugin Developers

1. **Never create engines directly** - Always use IScriptEngineService
2. **Compile once, execute many** - Cache compiled scripts
3. **Handle timeouts gracefully** - Scripts can be terminated
4. **Test with limits** - Ensure scripts work within constraints
5. **Use provided context objects** - Don't try to access external resources

---

## Troubleshooting

### Common Issues

| Issue | Cause | Solution |
|-------|-------|----------|
| Script compilation timeout | Complex script or infinite loop | Simplify script, check loops |
| Engine pool exhaustion | Too many concurrent executions | Increase pool size or optimize scripts |
| Memory limit exceeded | Script creating large objects | Optimize memory usage, increase limits |
| Security validation failure | Blocked patterns or high complexity | Review security rules, refactor script |
| Cache memory pressure | Too many cached scripts | Reduce cache size or TTL |

### Debugging

Enable detailed logging:
```yaml
logging:
  logLevel:
    FlowEngine.Core.Services.Scripting: Debug
```

Monitor metrics:
```bash
# Check engine pool usage
curl http://localhost:9090/metrics | grep engine_pool

# Check script cache hit rate  
curl http://localhost:9090/metrics | grep script_cache

# Check execution times
curl http://localhost:9090/metrics | grep script_execution_duration
```

---

**Related Documents**: [Plugin Architecture Guide] | [JavaScript Transform Plugin] | [Performance Tuning Guide]