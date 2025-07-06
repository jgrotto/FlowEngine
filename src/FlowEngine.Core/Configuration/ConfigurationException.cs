namespace FlowEngine.Core.Configuration;

/// <summary>
/// Exception thrown when configuration loading or validation fails.
/// </summary>
public sealed class ConfigurationException : Exception
{
    /// <summary>
    /// Initializes a new configuration exception with the specified message.
    /// </summary>
    /// <param name="message">The error message</param>
    public ConfigurationException(string message) : base(message)
    {
    }

    /// <summary>
    /// Initializes a new configuration exception with the specified message and inner exception.
    /// </summary>
    /// <param name="message">The error message</param>
    /// <param name="innerException">The inner exception</param>
    public ConfigurationException(string message, Exception innerException) : base(message, innerException)
    {
    }
}