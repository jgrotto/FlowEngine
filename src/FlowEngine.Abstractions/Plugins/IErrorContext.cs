using System.Collections.Immutable;
using System.Security;

namespace FlowEngine.Abstractions.Plugins;

/// <summary>
/// Interface for providing rich error context information.
/// </summary>
public interface IErrorContext
{
    /// <summary>
    /// Gets the operation that was being performed when the error occurred.
    /// </summary>
    string Operation { get; }

    /// <summary>
    /// Gets the component or plugin where the error occurred.
    /// </summary>
    string Component { get; }

    /// <summary>
    /// Gets additional context properties.
    /// </summary>
    IReadOnlyDictionary<string, object> Properties { get; }

    /// <summary>
    /// Gets the timestamp when the error occurred.
    /// </summary>
    DateTimeOffset Timestamp { get; }

    /// <summary>
    /// Gets the correlation ID for tracking related operations.
    /// </summary>
    string CorrelationId { get; }

    /// <summary>
    /// Gets the user or system context when the error occurred.
    /// </summary>
    string? UserContext { get; }

    /// <summary>
    /// Gets environment information where the error occurred.
    /// </summary>
    EnvironmentContext Environment { get; }
}

/// <summary>
/// Builder for creating error contexts with rich information.
/// </summary>
public interface IErrorContextBuilder
{
    /// <summary>
    /// Sets the operation name.
    /// </summary>
    IErrorContextBuilder WithOperation(string operation);

    /// <summary>
    /// Sets the component name.
    /// </summary>
    IErrorContextBuilder WithComponent(string component);

    /// <summary>
    /// Adds a property to the context.
    /// </summary>
    IErrorContextBuilder WithProperty(string key, object value);

    /// <summary>
    /// Adds multiple properties to the context.
    /// </summary>
    IErrorContextBuilder WithProperties(IReadOnlyDictionary<string, object> properties);

    /// <summary>
    /// Sets the correlation ID.
    /// </summary>
    IErrorContextBuilder WithCorrelationId(string correlationId);

    /// <summary>
    /// Sets the user context.
    /// </summary>
    IErrorContextBuilder WithUserContext(string userContext);

    /// <summary>
    /// Builds the error context.
    /// </summary>
    IErrorContext Build();
}

/// <summary>
/// Environment context information.
/// </summary>
public sealed record EnvironmentContext
{
    /// <summary>
    /// Gets the machine name where the error occurred.
    /// </summary>
    public string MachineName { get; init; } = Environment.MachineName;

    /// <summary>
    /// Gets the operating system information.
    /// </summary>
    public string OperatingSystem { get; init; } = Environment.OSVersion.ToString();

    /// <summary>
    /// Gets the .NET runtime version.
    /// </summary>
    public string RuntimeVersion { get; init; } = Environment.Version.ToString();

    /// <summary>
    /// Gets the current working directory.
    /// </summary>
    public string WorkingDirectory { get; init; } = Environment.CurrentDirectory;

    /// <summary>
    /// Gets the process ID.
    /// </summary>
    public int ProcessId { get; init; } = Environment.ProcessId;

    /// <summary>
    /// Gets the available memory at the time of error.
    /// </summary>
    public long AvailableMemory { get; init; }

    /// <summary>
    /// Gets the CPU usage percentage at the time of error.
    /// </summary>
    public double? CpuUsage { get; init; }

    /// <summary>
    /// Creates a new environment context with current system information.
    /// </summary>
    public static EnvironmentContext Create()
    {
        return new EnvironmentContext
        {
            AvailableMemory = GC.GetTotalMemory(forceFullCollection: false)
        };
    }
}

/// <summary>
/// Default implementation of error context.
/// </summary>
public sealed record ErrorContext : IErrorContext
{
    /// <inheritdoc />
    public required string Operation { get; init; }

    /// <inheritdoc />
    public required string Component { get; init; }

    /// <inheritdoc />
    public IReadOnlyDictionary<string, object> Properties { get; init; } = new Dictionary<string, object>();

    /// <inheritdoc />
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;

    /// <inheritdoc />
    public required string CorrelationId { get; init; }

    /// <inheritdoc />
    public string? UserContext { get; init; }

    /// <inheritdoc />
    public EnvironmentContext Environment { get; init; } = EnvironmentContext.Create();
}

/// <summary>
/// Default implementation of error context builder.
/// </summary>
public sealed class ErrorContextBuilder : IErrorContextBuilder
{
    private string _operation = "Unknown";
    private string _component = "Unknown";
    private readonly Dictionary<string, object> _properties = new();
    private string _correlationId = Guid.NewGuid().ToString();
    private string? _userContext;

    /// <inheritdoc />
    public IErrorContextBuilder WithOperation(string operation)
    {
        _operation = operation ?? throw new ArgumentNullException(nameof(operation));
        return this;
    }

    /// <inheritdoc />
    public IErrorContextBuilder WithComponent(string component)
    {
        _component = component ?? throw new ArgumentNullException(nameof(component));
        return this;
    }

    /// <inheritdoc />
    public IErrorContextBuilder WithProperty(string key, object value)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("Property key cannot be null or empty", nameof(key));

        _properties[key] = value;
        return this;
    }

    /// <inheritdoc />
    public IErrorContextBuilder WithProperties(IReadOnlyDictionary<string, object> properties)
    {
        if (properties == null)
            throw new ArgumentNullException(nameof(properties));

        foreach (var kvp in properties)
        {
            _properties[kvp.Key] = kvp.Value;
        }
        return this;
    }

    /// <inheritdoc />
    public IErrorContextBuilder WithCorrelationId(string correlationId)
    {
        _correlationId = correlationId ?? throw new ArgumentNullException(nameof(correlationId));
        return this;
    }

    /// <inheritdoc />
    public IErrorContextBuilder WithUserContext(string userContext)
    {
        _userContext = userContext;
        return this;
    }

    /// <inheritdoc />
    public IErrorContext Build()
    {
        return new ErrorContext
        {
            Operation = _operation,
            Component = _component,
            Properties = _properties.ToImmutableDictionary(),
            CorrelationId = _correlationId,
            UserContext = _userContext
        };
    }

    /// <summary>
    /// Creates a new error context builder.
    /// </summary>
    public static IErrorContextBuilder Create() => new ErrorContextBuilder();

    /// <summary>
    /// Creates a new error context builder with operation and component.
    /// </summary>
    public static IErrorContextBuilder Create(string operation, string component) =>
        new ErrorContextBuilder().WithOperation(operation).WithComponent(component);
}