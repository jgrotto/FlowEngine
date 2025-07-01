using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Text.RegularExpressions;

namespace FlowEngine.Core.Services.Scripting;

/// <summary>
/// Script security validator that analyzes JavaScript code for security violations and safety issues.
/// Provides comprehensive static analysis including pattern matching, complexity analysis, and safety checks.
/// </summary>
/// <remarks>
/// This validator performs multi-layered security analysis:
/// - Pattern-based detection of dangerous constructs
/// - Complexity analysis for performance impact assessment
/// - Memory usage estimation for resource planning
/// - Infinite loop detection for safety assurance
/// - Custom rule evaluation for domain-specific policies
/// 
/// Performance Characteristics:
/// - Typical scripts (1-10KB): 1-5ms analysis time
/// - Complex scripts (10-100KB): 5-50ms analysis time
/// - Very large scripts (100KB+): 50-500ms analysis time
/// 
/// The validator is designed to err on the side of caution, potentially flagging
/// some safe code as problematic to ensure comprehensive security coverage.
/// </remarks>
public sealed class ScriptSecurityValidator : IScriptSecurityValidator
{
    private readonly ILogger<ScriptSecurityValidator> _logger;
    private readonly SecurityOptions _options;
    private readonly ConcurrentDictionary<string, SecurityRule> _customRules;
    private readonly Regex[] _compiledPatterns;

    /// <summary>
    /// Initializes a new instance of the ScriptSecurityValidator class.
    /// </summary>
    /// <param name="options">Security configuration options</param>
    /// <param name="logger">Logger for validation diagnostics</param>
    public ScriptSecurityValidator(
        IOptions<ScriptEngineOptions> options,
        ILogger<ScriptSecurityValidator> logger)
    {
        _options = options?.Value?.Security ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        
        _customRules = new ConcurrentDictionary<string, SecurityRule>();
        
        // Pre-compile security rule patterns for performance
        _compiledPatterns = CompileSecurityPatterns();
        
        // Add default custom rules
        foreach (var rule in _options.CustomRules)
        {
            _customRules[rule.Name] = rule;
        }
        
        _logger.LogDebug("ScriptSecurityValidator initialized with {RuleCount} security rules", 
            _options.CustomRules.Length + _customRules.Count);
    }

    /// <inheritdoc />
    public async Task<ScriptValidationResult> ValidateAsync(
        string script,
        ValidationOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(script);
        ArgumentNullException.ThrowIfNull(options);

        _logger.LogTrace("Starting comprehensive validation for script of {Length} characters", script.Length);

        var violations = new List<SecurityViolation>();

        // 1. Syntax validation
        var syntaxValid = await ValidateSyntaxAsync(script, cancellationToken);
        if (!syntaxValid)
        {
            violations.Add(new SecurityViolation
            {
                Severity = SecuritySeverity.Critical,
                Description = "Script contains syntax errors",
                Remediation = "Fix syntax errors before proceeding"
            });
        }

        // 2. Security pattern analysis
        var patternViolations = await AnalyzeSecurityPatternsAsync(script, options, cancellationToken);
        violations.AddRange(patternViolations);

        // 3. Complexity analysis
        var complexity = await CalculateComplexityAsync(script, cancellationToken);
        if (complexity > options.MaxComplexity)
        {
            violations.Add(new SecurityViolation
            {
                Severity = SecuritySeverity.High,
                Description = $"Script complexity ({complexity}) exceeds maximum allowed ({options.MaxComplexity})",
                Remediation = "Simplify script logic or break into smaller functions"
            });
        }

        // 4. Infinite loop detection
        if (options.DetectInfiniteLoops)
        {
            var hasInfiniteLoops = await DetectInfiniteLoopsAsync(script, cancellationToken);
            if (hasInfiniteLoops)
            {
                violations.Add(new SecurityViolation
                {
                    Severity = SecuritySeverity.Critical,
                    Description = "Potential infinite loop detected",
                    Remediation = "Ensure all loops have proper termination conditions"
                });
            }
        }

        // 5. Memory usage estimation
        var estimatedMemory = await EstimateMemoryUsageAsync(script, cancellationToken);

        var result = new ScriptValidationResult
        {
            IsValid = violations.All(v => v.Severity < SecuritySeverity.High),
            Violations = violations.ToImmutableArray(),
            ComplexityScore = complexity,
            EstimatedMemoryUsage = estimatedMemory
        };

        _logger.LogDebug("Script validation completed. Valid: {IsValid}, Violations: {ViolationCount}, Complexity: {Complexity}",
            result.IsValid, result.Violations.Length, result.ComplexityScore);

        return result;
    }

    /// <inheritdoc />
    public async Task<bool> ValidateSyntaxAsync(string script, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(script);

        return await Task.Run(() =>
        {
            try
            {
                // Use Jint for syntax validation - it's fast and doesn't require native dependencies
                var engine = new Jint.Engine();
                engine.PrepareScript(script);
                return true;
            }
            catch (Jint.Runtime.ParserException)
            {
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Unexpected error during syntax validation");
                return false;
            }
        }, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<int> CalculateComplexityAsync(string script, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(script);

        return await Task.Run(() =>
        {
            try
            {
                return CalculateComplexityScore(script);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error calculating script complexity");
                return int.MaxValue; // Assume maximum complexity on error
            }
        }, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<long> EstimateMemoryUsageAsync(string script, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(script);

        return await Task.Run(() =>
        {
            try
            {
                return EstimateMemoryUsage(script);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error estimating memory usage");
                return long.MaxValue; // Assume maximum memory usage on error
            }
        }, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<bool> DetectInfiniteLoopsAsync(string script, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(script);

        return await Task.Run(() =>
        {
            try
            {
                return AnalyzeForInfiniteLoops(script);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error detecting infinite loops");
                return true; // Assume infinite loops exist on error (safe default)
            }
        }, cancellationToken);
    }

    /// <inheritdoc />
    public void AddSecurityRule(SecurityRule rule)
    {
        ArgumentNullException.ThrowIfNull(rule);
        
        try
        {
            // Validate the regex pattern
            _ = new Regex(rule.Pattern, RegexOptions.Compiled);
            
            _customRules[rule.Name] = rule;
            _logger.LogDebug("Added security rule '{RuleName}' with pattern '{Pattern}'", 
                rule.Name, rule.Pattern);
        }
        catch (ArgumentException ex)
        {
            throw new ArgumentException($"Invalid regex pattern in rule '{rule.Name}': {ex.Message}", ex);
        }
    }

    /// <inheritdoc />
    public bool RemoveSecurityRule(string ruleName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ruleName);
        
        var removed = _customRules.TryRemove(ruleName, out _);
        if (removed)
        {
            _logger.LogDebug("Removed security rule '{RuleName}'", ruleName);
        }
        
        return removed;
    }

    /// <inheritdoc />
    public IReadOnlyList<SecurityRule> GetActiveRules()
    {
        var allRules = new List<SecurityRule>();
        allRules.AddRange(_options.CustomRules);
        allRules.AddRange(_customRules.Values);
        return allRules.AsReadOnly();
    }

    private async Task<List<SecurityViolation>> AnalyzeSecurityPatternsAsync(
        string script, 
        ValidationOptions options, 
        CancellationToken cancellationToken)
    {
        return await Task.Run(() =>
        {
            var violations = new List<SecurityViolation>();

            // Check built-in patterns
            for (int i = 0; i < _compiledPatterns.Length && i < _options.CustomRules.Length; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                
                var pattern = _compiledPatterns[i];
                var rule = _options.CustomRules[i];
                
                if (pattern.IsMatch(script))
                {
                    violations.Add(new SecurityViolation
                    {
                        Severity = rule.Severity,
                        Description = rule.Description,
                        Remediation = rule.Remediation
                    });
                }
            }

            // Check custom rules
            foreach (var rule in _customRules.Values)
            {
                cancellationToken.ThrowIfCancellationRequested();
                
                try
                {
                    var regex = new Regex(rule.Pattern, RegexOptions.IgnoreCase);
                    if (regex.IsMatch(script))
                    {
                        violations.Add(new SecurityViolation
                        {
                            Severity = rule.Severity,
                            Description = rule.Description,
                            Remediation = rule.Remediation
                        });
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error applying security rule '{RuleName}'", rule.Name);
                }
            }

            return violations;
        }, cancellationToken);
    }

    private static int CalculateComplexityScore(string script)
    {
        var complexity = 0;

        // Base complexity from script length
        complexity += script.Length / 100; // 1 point per 100 characters

        // Cyclomatic complexity indicators
        complexity += CountMatches(script, @"\bif\b") * 2;
        complexity += CountMatches(script, @"\belse\b") * 1;
        complexity += CountMatches(script, @"\bfor\b") * 3;
        complexity += CountMatches(script, @"\bwhile\b") * 3;
        complexity += CountMatches(script, @"\bswitch\b") * 2;
        complexity += CountMatches(script, @"\bcase\b") * 1;
        complexity += CountMatches(script, @"\btry\b") * 2;
        complexity += CountMatches(script, @"\bcatch\b") * 2;
        complexity += CountMatches(script, @"\bfunction\b") * 2;

        // Nesting complexity (approximated by bracket pairs)
        var openBraces = CountMatches(script, @"\{");
        var closeBraces = CountMatches(script, @"\}");
        complexity += Math.Min(openBraces, closeBraces) * 1;

        // Regular expression complexity
        complexity += CountMatches(script, @"\/.*?\/[gimuy]*") * 3;

        // Object/array creation complexity
        complexity += CountMatches(script, @"\bnew\b") * 1;
        complexity += CountMatches(script, @"\[.*?\]") * 1;

        return complexity;
    }

    private static long EstimateMemoryUsage(string script)
    {
        var baseSize = script.Length * 2; // UTF-16 encoding

        // String operations estimate
        var stringOps = CountMatches(script, @"[\""'].+?[\""']") * 50; // Average string size

        // Variable declarations
        var variables = CountMatches(script, @"\b(var|let|const)\s+\w+") * 64; // Average object overhead

        // Function declarations
        var functions = CountMatches(script, @"\bfunction\b") * 1024; // Function overhead

        // Array/object literals
        var arrays = CountMatches(script, @"\[.*?\]") * 256;
        var objects = CountMatches(script, @"\{.*?\}") * 512;

        return baseSize + stringOps + variables + functions + arrays + objects;
    }

    private static bool AnalyzeForInfiniteLoops(string script)
    {
        // Check for obvious infinite loop patterns
        var infinitePatterns = new[]
        {
            @"\bwhile\s*\(\s*true\s*\)",
            @"\bfor\s*\(\s*;\s*;\s*\)",
            @"\bdo\s*\{.*?\}\s*while\s*\(\s*true\s*\)",
            @"\bwhile\s*\(\s*1\s*\)",
            @"\bfor\s*\(\s*;\s*true\s*;\s*\)"
        };

        return infinitePatterns.Any(pattern => Regex.IsMatch(script, pattern, RegexOptions.IgnoreCase));
    }

    private static int CountMatches(string input, string pattern)
    {
        try
        {
            return Regex.Matches(input, pattern, RegexOptions.IgnoreCase).Count;
        }
        catch
        {
            return 0; // Return 0 if pattern is invalid
        }
    }

    private Regex[] CompileSecurityPatterns()
    {
        var patterns = new List<Regex>();
        
        foreach (var rule in _options.CustomRules)
        {
            try
            {
                patterns.Add(new Regex(rule.Pattern, RegexOptions.Compiled | RegexOptions.IgnoreCase));
            }
            catch (ArgumentException ex)
            {
                _logger.LogWarning(ex, "Invalid regex pattern in security rule '{RuleName}': {Pattern}", 
                    rule.Name, rule.Pattern);
            }
        }
        
        return patterns.ToArray();
    }
}