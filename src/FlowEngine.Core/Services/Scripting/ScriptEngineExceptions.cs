using System.Collections.Immutable;

namespace FlowEngine.Core.Services.Scripting;

/// <summary>
/// Base exception for all script engine related errors.
/// </summary>
public abstract class ScriptEngineException : Exception
{
    /// <summary>
    /// Initializes a new instance of the ScriptEngineException class.
    /// </summary>
    protected ScriptEngineException(string message) : base(message)
    {
    }

    /// <summary>
    /// Initializes a new instance of the ScriptEngineException class with inner exception.
    /// </summary>
    protected ScriptEngineException(string message, Exception innerException) : base(message, innerException)
    {
    }
}

/// <summary>
/// Exception thrown when script violates security constraints.
/// </summary>
public sealed class ScriptSecurityException : ScriptEngineException
{
    /// <summary>
    /// Gets the security violations that caused this exception.
    /// </summary>
    public ImmutableArray<SecurityViolation> Violations { get; }

    /// <summary>
    /// Initializes a new instance of the ScriptSecurityException class.
    /// </summary>
    /// <param name="violations">Security violations detected in the script</param>
    public ScriptSecurityException(ImmutableArray<SecurityViolation> violations)
        : base(FormatSecurityMessage(violations))
    {
        Violations = violations;
    }

    /// <summary>
    /// Initializes a new instance of the ScriptSecurityException class with a single violation.
    /// </summary>
    /// <param name="violation">Security violation detected in the script</param>
    public ScriptSecurityException(SecurityViolation violation)
        : this(ImmutableArray.Create(violation))
    {
    }

    private static string FormatSecurityMessage(ImmutableArray<SecurityViolation> violations)
    {
        if (violations.IsEmpty)
            return "Script security validation failed";

        var criticalCount = violations.Count(v => v.Severity == SecuritySeverity.Critical);
        var highCount = violations.Count(v => v.Severity == SecuritySeverity.High);
        var mediumCount = violations.Count(v => v.Severity == SecuritySeverity.Medium);
        var lowCount = violations.Count(v => v.Severity == SecuritySeverity.Low);

        var summary = $"Script security validation failed with {violations.Length} violation(s)";
        
        if (criticalCount > 0) summary += $", {criticalCount} critical";
        if (highCount > 0) summary += $", {highCount} high";
        if (mediumCount > 0) summary += $", {mediumCount} medium";
        if (lowCount > 0) summary += $", {lowCount} low";

        return summary + ". " + string.Join("; ", violations.Select(v => v.Description));
    }
}

/// <summary>
/// Exception thrown when script compilation fails.
/// </summary>
public sealed class ScriptCompilationException : ScriptEngineException
{
    /// <summary>
    /// Gets the line number where the compilation error occurred.
    /// </summary>
    public int? LineNumber { get; }

    /// <summary>
    /// Gets the column number where the compilation error occurred.
    /// </summary>
    public int? ColumnNumber { get; }

    /// <summary>
    /// Gets the script engine type that failed compilation.
    /// </summary>
    public ScriptEngineType EngineType { get; }

    /// <summary>
    /// Initializes a new instance of the ScriptCompilationException class.
    /// </summary>
    /// <param name="message">Error message describing the compilation failure</param>
    /// <param name="engineType">Script engine type that failed</param>
    /// <param name="lineNumber">Line number where error occurred</param>
    /// <param name="columnNumber">Column number where error occurred</param>
    /// <param name="innerException">Original engine-specific exception</param>
    public ScriptCompilationException(
        string message, 
        ScriptEngineType engineType,
        int? lineNumber = null, 
        int? columnNumber = null,
        Exception? innerException = null)
        : base(FormatCompilationMessage(message, engineType, lineNumber, columnNumber), innerException)
    {
        LineNumber = lineNumber;
        ColumnNumber = columnNumber;
        EngineType = engineType;
    }

    private static string FormatCompilationMessage(
        string message, 
        ScriptEngineType engineType, 
        int? lineNumber, 
        int? columnNumber)
    {
        var formatted = $"Script compilation failed ({engineType}): {message}";
        
        if (lineNumber.HasValue)
        {
            formatted += $" at line {lineNumber}";
            if (columnNumber.HasValue)
                formatted += $", column {columnNumber}";
        }

        return formatted;
    }
}

/// <summary>
/// Exception thrown when script execution fails or times out.
/// </summary>
public sealed class ScriptExecutionException : ScriptEngineException
{
    /// <summary>
    /// Gets the script execution time before the error occurred.
    /// </summary>
    public TimeSpan ExecutionTime { get; }

    /// <summary>
    /// Gets whether the execution was terminated due to timeout.
    /// </summary>
    public bool IsTimeout { get; }

    /// <summary>
    /// Gets whether the execution was terminated due to memory limit.
    /// </summary>
    public bool IsMemoryLimit { get; }

    /// <summary>
    /// Gets the script engine type that failed execution.
    /// </summary>
    public ScriptEngineType EngineType { get; }

    /// <summary>
    /// Initializes a new instance of the ScriptExecutionException class.
    /// </summary>
    /// <param name="message">Error message describing the execution failure</param>
    /// <param name="engineType">Script engine type that failed</param>
    /// <param name="executionTime">Time spent executing before failure</param>
    /// <param name="isTimeout">Whether the failure was due to timeout</param>
    /// <param name="isMemoryLimit">Whether the failure was due to memory limit</param>
    /// <param name="innerException">Original engine-specific exception</param>
    public ScriptExecutionException(
        string message,
        ScriptEngineType engineType,
        TimeSpan executionTime,
        bool isTimeout = false,
        bool isMemoryLimit = false,
        Exception? innerException = null)
        : base(FormatExecutionMessage(message, engineType, executionTime, isTimeout, isMemoryLimit), innerException)
    {
        ExecutionTime = executionTime;
        IsTimeout = isTimeout;
        IsMemoryLimit = isMemoryLimit;
        EngineType = engineType;
    }

    private static string FormatExecutionMessage(
        string message,
        ScriptEngineType engineType,
        TimeSpan executionTime,
        bool isTimeout,
        bool isMemoryLimit)
    {
        var formatted = $"Script execution failed ({engineType})";
        
        if (isTimeout)
            formatted += " due to timeout";
        else if (isMemoryLimit)
            formatted += " due to memory limit exceeded";
        
        formatted += $" after {executionTime.TotalMilliseconds:F1}ms: {message}";
        
        return formatted;
    }
}

/// <summary>
/// Exception thrown when no script engines are available in the pool.
/// </summary>
public sealed class ResourceExhaustionException : ScriptEngineException
{
    /// <summary>
    /// Gets the script engine type that was requested.
    /// </summary>
    public ScriptEngineType RequestedEngineType { get; }

    /// <summary>
    /// Gets the number of engines currently active.
    /// </summary>
    public int ActiveEngines { get; }

    /// <summary>
    /// Gets the maximum number of engines in the pool.
    /// </summary>
    public int MaxEngines { get; }

    /// <summary>
    /// Initializes a new instance of the ResourceExhaustionException class.
    /// </summary>
    /// <param name="requestedEngineType">Script engine type that was requested</param>
    /// <param name="activeEngines">Number of engines currently active</param>
    /// <param name="maxEngines">Maximum number of engines in pool</param>
    public ResourceExhaustionException(
        ScriptEngineType requestedEngineType,
        int activeEngines,
        int maxEngines)
        : base($"No {requestedEngineType} script engines available in pool. " +
               $"Active: {activeEngines}, Maximum: {maxEngines}. " +
               $"Consider increasing pool size or reducing concurrent script executions.")
    {
        RequestedEngineType = requestedEngineType;
        ActiveEngines = activeEngines;
        MaxEngines = maxEngines;
    }
}

/// <summary>
/// Exception thrown when script engine configuration is invalid.
/// </summary>
public sealed class ScriptEngineConfigurationException : ScriptEngineException
{
    /// <summary>
    /// Gets the configuration parameter that caused the error.
    /// </summary>
    public string? ConfigurationParameter { get; }

    /// <summary>
    /// Initializes a new instance of the ScriptEngineConfigurationException class.
    /// </summary>
    /// <param name="message">Error message describing the configuration issue</param>
    /// <param name="configurationParameter">Parameter name that caused the error</param>
    /// <param name="innerException">Original configuration exception</param>
    public ScriptEngineConfigurationException(
        string message,
        string? configurationParameter = null,
        Exception? innerException = null)
        : base(FormatConfigurationMessage(message, configurationParameter), innerException)
    {
        ConfigurationParameter = configurationParameter;
    }

    private static string FormatConfigurationMessage(string message, string? parameter)
    {
        var formatted = "Script engine configuration error";
        
        if (!string.IsNullOrEmpty(parameter))
            formatted += $" in parameter '{parameter}'";
        
        return formatted + $": {message}";
    }
}