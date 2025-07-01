namespace FlowEngine.Core.Services.Scripting;

/// <summary>
/// Interface for validating script security and safety before compilation and execution.
/// Provides static analysis capabilities to detect potentially harmful or problematic code.
/// </summary>
/// <remarks>
/// Security validation is a critical component of the script engine service that helps
/// prevent malicious code execution, infinite loops, and resource exhaustion attacks.
/// 
/// The validator analyzes JavaScript source code for:
/// - Dangerous function calls (eval, Function constructor, etc.)
/// - Potential infinite loops and recursive patterns  
/// - Memory allocation patterns that could cause leaks
/// - Complexity metrics that might indicate performance issues
/// - Custom security rules defined by administrators
/// 
/// Performance Impact:
/// - Static analysis adds 1-5ms overhead for typical scripts
/// - Complex scripts may take up to 100ms for full analysis
/// - Results should be cached to avoid repeated validation
/// </remarks>
public interface IScriptSecurityValidator
{
    /// <summary>
    /// Validates a JavaScript script for security violations and safety issues.
    /// Performs comprehensive static analysis including syntax, complexity, and security checks.
    /// </summary>
    /// <param name="script">JavaScript source code to validate</param>
    /// <param name="options">Validation options and security rules</param>
    /// <param name="cancellationToken">Cancellation token for validation timeout</param>
    /// <returns>Validation result with detailed violation information</returns>
    /// <exception cref="ArgumentException">Script is null or empty</exception>
    /// <exception cref="OperationCanceledException">Validation timed out</exception>
    /// <remarks>
    /// This method performs comprehensive analysis including:
    /// - Syntax validation and parsing
    /// - Security pattern matching against known dangerous constructs
    /// - Complexity analysis for performance impact assessment
    /// - Memory usage estimation for resource planning
    /// - Custom rule evaluation based on configuration
    /// 
    /// The validation process is designed to be fast and accurate, typically
    /// completing within 1-5ms for scripts under 10KB in size.
    /// </remarks>
    Task<ScriptValidationResult> ValidateAsync(
        string script,
        ValidationOptions options,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates script syntax without performing security analysis.
    /// Provides fast syntax-only validation for performance-critical scenarios.
    /// </summary>
    /// <param name="script">JavaScript source code to validate</param>
    /// <param name="cancellationToken">Cancellation token for validation timeout</param>
    /// <returns>True if syntax is valid, false otherwise</returns>
    /// <exception cref="ArgumentException">Script is null or empty</exception>
    /// <remarks>
    /// This method performs only syntax validation and parsing without
    /// the overhead of security analysis. Use this method when:
    /// - Scripts are from trusted sources
    /// - Performance is critical and security has been validated elsewhere
    /// - Only syntax checking is needed
    /// 
    /// Typical performance: &lt;1ms for scripts under 10KB.
    /// </remarks>
    Task<bool> ValidateSyntaxAsync(
        string script,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Calculates the complexity score of a JavaScript script.
    /// Analyzes code structure to estimate computational complexity and performance impact.
    /// </summary>
    /// <param name="script">JavaScript source code to analyze</param>
    /// <param name="cancellationToken">Cancellation token for analysis timeout</param>
    /// <returns>Complexity score from 0 (simple) to 1000+ (very complex)</returns>
    /// <exception cref="ArgumentException">Script is null or empty</exception>
    /// <remarks>
    /// Complexity calculation considers:
    /// - Cyclomatic complexity (branching and loops)
    /// - Nesting depth and function call chains
    /// - Object creation and manipulation patterns
    /// - Recursive function definitions
    /// - Regular expression usage and complexity
    /// 
    /// Complexity Score Guidelines:
    /// - 0-20: Simple scripts (linear execution, basic operations)
    /// - 21-50: Moderate complexity (some branching, basic loops)
    /// - 51-100: Complex scripts (nested structures, multiple functions)
    /// - 100+: Very complex (deep nesting, complex algorithms)
    /// 
    /// Scripts with complexity scores above 100 should be reviewed for
    /// performance impact and potential optimization opportunities.
    /// </remarks>
    Task<int> CalculateComplexityAsync(
        string script,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Estimates the memory usage of a JavaScript script during execution.
    /// Analyzes code patterns to predict memory allocation and usage.
    /// </summary>
    /// <param name="script">JavaScript source code to analyze</param>
    /// <param name="cancellationToken">Cancellation token for analysis timeout</param>
    /// <returns>Estimated memory usage in bytes</returns>
    /// <exception cref="ArgumentException">Script is null or empty</exception>
    /// <remarks>
    /// Memory estimation considers:
    /// - Variable declarations and scope
    /// - Object and array creation patterns
    /// - String manipulation and concatenation
    /// - Function closure capture
    /// - Potential memory leaks from circular references
    /// 
    /// The estimation is conservative and may overestimate usage to provide
    /// a safety margin for resource planning. Actual usage may vary based on:
    /// - Engine optimization strategies
    /// - Garbage collection timing
    /// - Input data size and complexity
    /// - Runtime execution patterns
    /// </remarks>
    Task<long> EstimateMemoryUsageAsync(
        string script,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Detects potential infinite loops in JavaScript code.
    /// Analyzes loop structures and termination conditions for safety.
    /// </summary>
    /// <param name="script">JavaScript source code to analyze</param>
    /// <param name="cancellationToken">Cancellation token for analysis timeout</param>
    /// <returns>True if potential infinite loops are detected</returns>
    /// <exception cref="ArgumentException">Script is null or empty</exception>
    /// <remarks>
    /// Infinite loop detection analyzes:
    /// - While loops with constant or non-changing conditions
    /// - For loops with improper increment/decrement
    /// - Recursive functions without proper base cases
    /// - Do-while loops with invariant conditions
    /// 
    /// Detection Limitations:
    /// - Static analysis cannot detect all runtime conditions
    /// - Complex conditional logic may produce false positives/negatives
    /// - Data-dependent termination conditions are difficult to analyze
    /// 
    /// This method provides best-effort detection and should be combined
    /// with execution timeouts for comprehensive protection.
    /// </remarks>
    Task<bool> DetectInfiniteLoopsAsync(
        string script,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds a custom security rule to the validator.
    /// Allows runtime registration of new security patterns and constraints.
    /// </summary>
    /// <param name="rule">Security rule to add</param>
    /// <exception cref="ArgumentNullException">Rule is null</exception>
    /// <exception cref="ArgumentException">Rule pattern is invalid</exception>
    /// <remarks>
    /// Custom rules enable application-specific security policies beyond
    /// the default set. Rules are applied during validation and can:
    /// - Block specific function calls or patterns
    /// - Enforce coding standards and conventions
    /// - Implement domain-specific security requirements
    /// 
    /// Rules use regular expressions for pattern matching and should be
    /// tested thoroughly to avoid false positives that block valid code.
    /// </remarks>
    void AddSecurityRule(SecurityRule rule);

    /// <summary>
    /// Removes a custom security rule from the validator.
    /// </summary>
    /// <param name="ruleName">Name of the rule to remove</param>
    /// <returns>True if the rule was found and removed, false otherwise</returns>
    /// <remarks>
    /// This method allows dynamic management of security rules without
    /// requiring service restart. Useful for:
    /// - Temporary rule adjustments during troubleshooting
    /// - Runtime configuration updates
    /// - A/B testing of security policies
    /// </remarks>
    bool RemoveSecurityRule(string ruleName);

    /// <summary>
    /// Gets all currently active security rules.
    /// </summary>
    /// <returns>Read-only collection of active security rules</returns>
    /// <remarks>
    /// This method provides visibility into the current security policy
    /// for debugging, auditing, and configuration validation purposes.
    /// </remarks>
    IReadOnlyList<SecurityRule> GetActiveRules();
}