namespace FlowEngine.Abstractions.Plugins;

/// <summary>
/// Base exception for all plugin-related errors.
/// </summary>
public abstract class PluginException : Exception
{
    /// <summary>
    /// Initializes a new plugin exception.
    /// </summary>
    /// <param name="message">Error message</param>
    protected PluginException(string message) : base(message)
    {
    }

    /// <summary>
    /// Initializes a new plugin exception with inner exception.
    /// </summary>
    /// <param name="message">Error message</param>
    /// <param name="innerException">Inner exception</param>
    protected PluginException(string message, Exception innerException) : base(message, innerException)
    {
    }

    /// <summary>
    /// Gets or sets the name of the plugin that caused this exception.
    /// </summary>
    public string? PluginName { get; init; }

    /// <summary>
    /// Gets or sets the version of the plugin that caused this exception.
    /// </summary>
    public string? PluginVersion { get; init; }

    /// <summary>
    /// Gets or sets additional context about the error for debugging.
    /// </summary>
    public IReadOnlyDictionary<string, object>? Context { get; init; }
}

/// <summary>
/// Exception thrown when plugin initialization fails.
/// </summary>
public sealed class PluginInitializationException : PluginException
{
    /// <summary>
    /// Initializes a new plugin initialization exception.
    /// </summary>
    /// <param name="message">Error message</param>
    public PluginInitializationException(string message) : base(message)
    {
    }

    /// <summary>
    /// Initializes a new plugin initialization exception with inner exception.
    /// </summary>
    /// <param name="message">Error message</param>
    /// <param name="innerException">Inner exception</param>
    public PluginInitializationException(string message, Exception innerException) : base(message, innerException)
    {
    }

    /// <summary>
    /// Gets or sets the configuration that caused the initialization failure.
    /// </summary>
    public IReadOnlyDictionary<string, object>? FailedConfiguration { get; init; }
}

/// <summary>
/// Exception thrown when plugin execution fails during data processing.
/// </summary>
public sealed class PluginExecutionException : PluginException
{
    /// <summary>
    /// Initializes a new plugin execution exception.
    /// </summary>
    /// <param name="message">Error message</param>
    public PluginExecutionException(string message) : base(message)
    {
    }

    /// <summary>
    /// Initializes a new plugin execution exception with inner exception.
    /// </summary>
    /// <param name="message">Error message</param>
    /// <param name="innerException">Inner exception</param>
    public PluginExecutionException(string message, Exception innerException) : base(message, innerException)
    {
    }

    /// <summary>
    /// Gets or sets the chunk that was being processed when the error occurred.
    /// </summary>
    public IChunk? FailedChunk { get; init; }

    /// <summary>
    /// Gets or sets the row index within the chunk where the error occurred.
    /// </summary>
    public int? FailedRowIndex { get; init; }

    /// <summary>
    /// Gets or sets whether this error is recoverable and processing can continue.
    /// </summary>
    public bool IsRecoverable { get; init; }
}

/// <summary>
/// Exception thrown when plugin loading or assembly isolation fails.
/// </summary>
public sealed class PluginLoadException : PluginException
{
    /// <summary>
    /// Initializes a new plugin load exception.
    /// </summary>
    /// <param name="message">Error message</param>
    public PluginLoadException(string message) : base(message)
    {
    }

    /// <summary>
    /// Initializes a new plugin load exception with inner exception.
    /// </summary>
    /// <param name="message">Error message</param>
    /// <param name="innerException">Inner exception</param>
    public PluginLoadException(string message, Exception innerException) : base(message, innerException)
    {
    }

    /// <summary>
    /// Gets or sets the assembly path that failed to load.
    /// </summary>
    public string? AssemblyPath { get; init; }

    /// <summary>
    /// Gets or sets the plugin type name that failed to load.
    /// </summary>
    public string? TypeName { get; init; }

    /// <summary>
    /// Gets or sets the isolation level that was attempted.
    /// </summary>
    public PluginIsolationLevel? IsolationLevel { get; init; }
}

/// <summary>
/// Exception thrown when plugin resource limits are exceeded.
/// </summary>
public sealed class PluginResourceException : PluginException
{
    /// <summary>
    /// Initializes a new plugin resource exception.
    /// </summary>
    /// <param name="message">Error message</param>
    public PluginResourceException(string message) : base(message)
    {
    }

    /// <summary>
    /// Initializes a new plugin resource exception with inner exception.
    /// </summary>
    /// <param name="message">Error message</param>
    /// <param name="innerException">Inner exception</param>
    public PluginResourceException(string message, Exception innerException) : base(message, innerException)
    {
    }

    /// <summary>
    /// Gets or sets the type of resource that was exceeded.
    /// </summary>
    public ResourceType ResourceType { get; init; }

    /// <summary>
    /// Gets or sets the limit that was exceeded.
    /// </summary>
    public long Limit { get; init; }

    /// <summary>
    /// Gets or sets the actual usage that caused the limit to be exceeded.
    /// </summary>
    public long ActualUsage { get; init; }
}

/// <summary>
/// Level of isolation for plugin loading.
/// </summary>
public enum PluginIsolationLevel
{
    /// <summary>
    /// Plugin loads in the main application domain (no isolation).
    /// Fastest performance but no memory cleanup.
    /// </summary>
    Shared,

    /// <summary>
    /// Plugin loads in an isolated AssemblyLoadContext.
    /// Balanced performance with memory cleanup capability.
    /// </summary>
    Isolated,

    /// <summary>
    /// Plugin loads in a fully isolated context with restricted permissions.
    /// Highest security but potential performance overhead.
    /// </summary>
    Sandboxed
}

/// <summary>
/// Types of resources that can be limited for plugin execution.
/// </summary>
public enum ResourceType
{
    /// <summary>
    /// Memory usage in bytes.
    /// </summary>
    Memory,

    /// <summary>
    /// CPU time in milliseconds.
    /// </summary>
    CpuTime,

    /// <summary>
    /// Execution timeout in milliseconds.
    /// </summary>
    ExecutionTime,

    /// <summary>
    /// Number of file handles.
    /// </summary>
    FileHandles,

    /// <summary>
    /// Network connections.
    /// </summary>
    NetworkConnections,

    /// <summary>
    /// Disk space usage in bytes.
    /// </summary>
    DiskSpace
}