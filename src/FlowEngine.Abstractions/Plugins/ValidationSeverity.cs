namespace FlowEngine.Abstractions.Plugins;

/// <summary>
/// Severity levels for validation messages.
/// </summary>
public enum ValidationSeverity
{
    /// <summary>
    /// Informational message that doesn't affect functionality.
    /// </summary>
    Info,

    /// <summary>
    /// Warning that may affect performance or functionality.
    /// </summary>
    Warning,

    /// <summary>
    /// Error that prevents proper functionality.
    /// </summary>
    Error
}