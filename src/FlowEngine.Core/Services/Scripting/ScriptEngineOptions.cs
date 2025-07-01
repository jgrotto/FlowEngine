using System.Collections.Immutable;
using System.Text.RegularExpressions;

namespace FlowEngine.Core.Services.Scripting;

/// <summary>
/// Configuration options for the script engine service.
/// Controls engine pooling, compilation settings, execution limits, and security.
/// </summary>
public sealed class ScriptEngineOptions
{
    /// <summary>
    /// Gets or sets the engine pool configuration.
    /// </summary>
    public EnginePoolOptions EnginePool { get; set; } = new();

    /// <summary>
    /// Gets or sets the compilation configuration.
    /// </summary>
    public CompilationOptions Compilation { get; set; } = new();

    /// <summary>
    /// Gets or sets the execution limits configuration.
    /// </summary>
    public ExecutionLimitsOptions ExecutionLimits { get; set; } = new();

    /// <summary>
    /// Gets or sets the script cache configuration.
    /// </summary>
    public ScriptCacheOptions ScriptCache { get; set; } = new();

    /// <summary>
    /// Gets or sets the security configuration.
    /// </summary>
    public SecurityOptions Security { get; set; } = new();
}

/// <summary>
/// Engine pool configuration options.
/// </summary>
public sealed class EnginePoolOptions
{
    /// <summary>
    /// Gets or sets the number of engines to maintain in each pool.
    /// Default is 2x processor count.
    /// </summary>
    public int PoolSize { get; set; } = Environment.ProcessorCount * 2;

    /// <summary>
    /// Gets or sets the default engine type to use when Auto is specified.
    /// </summary>
    public ScriptEngineType DefaultEngineType { get; set; } = ScriptEngineType.V8;

    /// <summary>
    /// Gets or sets the maximum time to wait for an engine from the pool.
    /// </summary>
    public TimeSpan AcquisitionTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Gets or sets whether to warm up engines on service startup.
    /// </summary>
    public bool WarmupOnStartup { get; set; } = true;

    /// <summary>
    /// Gets or sets the engine health check interval.
    /// </summary>
    public TimeSpan HealthCheckInterval { get; set; } = TimeSpan.FromMinutes(5);
}

/// <summary>
/// Script compilation configuration options.
/// </summary>
public sealed class CompilationOptions
{
    /// <summary>
    /// Gets or sets the default compilation timeout.
    /// </summary>
    public TimeSpan DefaultTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Gets or sets the default optimization level.
    /// </summary>
    public OptimizationLevel DefaultOptimizationLevel { get; set; } = OptimizationLevel.Balanced;

    /// <summary>
    /// Gets or sets whether to enable strict mode by default.
    /// </summary>
    public bool DefaultStrictMode { get; set; } = true;

    /// <summary>
    /// Gets or sets whether to enable debugging support by default.
    /// </summary>
    public bool DefaultEnableDebugging { get; set; } = false;
}

/// <summary>
/// Execution limits configuration options.
/// </summary>
public sealed class ExecutionLimitsOptions
{
    /// <summary>
    /// Gets or sets the default maximum execution time.
    /// </summary>
    public TimeSpan DefaultMaxExecutionTime { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Gets or sets the default maximum memory usage in bytes.
    /// </summary>
    public long DefaultMaxMemoryUsage { get; set; } = 100 * 1024 * 1024; // 100MB

    /// <summary>
    /// Gets or sets the default maximum recursion depth.
    /// </summary>
    public int DefaultMaxRecursionDepth { get; set; } = 1000;

    /// <summary>
    /// Gets or sets the default maximum number of iterations.
    /// </summary>
    public long DefaultMaxIterations { get; set; } = 1_000_000;

    /// <summary>
    /// Gets or sets whether to enable sandbox restrictions by default.
    /// </summary>
    public bool DefaultEnableSandbox { get; set; } = true;
}

/// <summary>
/// Script cache configuration options.
/// </summary>
public sealed class ScriptCacheOptions
{
    /// <summary>
    /// Gets or sets whether script caching is enabled.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Gets or sets the maximum number of cached scripts.
    /// </summary>
    public int MaxSize { get; set; } = 1000;

    /// <summary>
    /// Gets or sets the cache expiration time.
    /// </summary>
    public TimeSpan Expiration { get; set; } = TimeSpan.FromHours(1);

    /// <summary>
    /// Gets or sets the cache eviction policy.
    /// </summary>
    public CacheEvictionPolicy EvictionPolicy { get; set; } = CacheEvictionPolicy.LRU;

    /// <summary>
    /// Gets or sets the maximum total memory for cached scripts in bytes.
    /// </summary>
    public long MaxMemoryUsage { get; set; } = 500 * 1024 * 1024; // 500MB
}

/// <summary>
/// Security configuration options.
/// </summary>
public sealed class SecurityOptions
{
    /// <summary>
    /// Gets or sets whether security validation is enabled.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Gets or sets whether sandbox restrictions are enabled.
    /// </summary>
    public bool EnableSandbox { get; set; } = true;

    /// <summary>
    /// Gets or sets the allowed global objects.
    /// </summary>
    public ImmutableHashSet<string> AllowedGlobals { get; set; } = ImmutableHashSet.Create(
        "Math", "Date", "JSON", "console");

    /// <summary>
    /// Gets or sets the blocked global objects.
    /// </summary>
    public ImmutableHashSet<string> BlockedGlobals { get; set; } = ImmutableHashSet.Create(
        "process", "require", "eval", "Function", "setTimeout", "setInterval");

    /// <summary>
    /// Gets or sets script analysis options.
    /// </summary>
    public ScriptAnalysisOptions ScriptAnalysis { get; set; } = new();

    /// <summary>
    /// Gets or sets the maximum allowed script complexity.
    /// </summary>
    public int MaxComplexity { get; set; } = 100;

    /// <summary>
    /// Gets or sets custom security rules.
    /// </summary>
    public ImmutableArray<SecurityRule> CustomRules { get; set; } = CreateDefaultSecurityRules();

    private static ImmutableArray<SecurityRule> CreateDefaultSecurityRules()
    {
        return ImmutableArray.Create(
            new SecurityRule
            {
                Name = "NoEval",
                Pattern = @"\beval\s*\(",
                Severity = SecuritySeverity.Critical,
                Description = "Use of eval() function detected",
                Remediation = "Remove eval() calls and use safer alternatives"
            },
            new SecurityRule
            {
                Name = "NoFunction",
                Pattern = @"\bFunction\s*\(",
                Severity = SecuritySeverity.Critical,
                Description = "Use of Function() constructor detected",
                Remediation = "Remove Function() constructor and define functions normally"
            },
            new SecurityRule
            {
                Name = "NoSetTimeout",
                Pattern = @"\bsetTimeout\s*\(",
                Severity = SecuritySeverity.High,
                Description = "Use of setTimeout() detected",
                Remediation = "Remove setTimeout() calls as they are not allowed in sandboxed execution"
            },
            new SecurityRule
            {
                Name = "NoSetInterval",
                Pattern = @"\bsetInterval\s*\(",
                Severity = SecuritySeverity.High,
                Description = "Use of setInterval() detected",
                Remediation = "Remove setInterval() calls as they are not allowed in sandboxed execution"
            },
            new SecurityRule
            {
                Name = "NoInfiniteLoop",
                Pattern = @"\bwhile\s*\(\s*true\s*\)",
                Severity = SecuritySeverity.High,
                Description = "Potential infinite loop detected (while(true))",
                Remediation = "Ensure loop has proper termination condition"
            },
            new SecurityRule
            {
                Name = "NoFor",
                Pattern = @"\bfor\s*\(\s*;\s*;\s*\)",
                Severity = SecuritySeverity.High,
                Description = "Potential infinite loop detected (for(;;))",
                Remediation = "Ensure loop has proper termination condition"
            }
        );
    }
}

/// <summary>
/// Script analysis configuration options.
/// </summary>
public sealed class ScriptAnalysisOptions
{
    /// <summary>
    /// Gets or sets whether to detect potential infinite loops.
    /// </summary>
    public bool DetectInfiniteLoops { get; set; } = true;

    /// <summary>
    /// Gets or sets whether to detect potential memory leaks.
    /// </summary>
    public bool DetectMemoryLeaks { get; set; } = true;

    /// <summary>
    /// Gets or sets whether to analyze script complexity.
    /// </summary>
    public bool AnalyzeComplexity { get; set; } = true;

    /// <summary>
    /// Gets or sets whether to validate variable naming conventions.
    /// </summary>
    public bool ValidateVariableNames { get; set; } = false;

    /// <summary>
    /// Gets or sets whether to check for unused variables.
    /// </summary>
    public bool CheckUnusedVariables { get; set; } = false;
}

/// <summary>
/// Cache eviction policies.
/// </summary>
public enum CacheEvictionPolicy
{
    /// <summary>
    /// Least Recently Used eviction.
    /// </summary>
    LRU,

    /// <summary>
    /// Least Frequently Used eviction.
    /// </summary>
    LFU,

    /// <summary>
    /// First In, First Out eviction.
    /// </summary>
    FIFO,

    /// <summary>
    /// Time-based expiration only.
    /// </summary>
    TimeOnly
}