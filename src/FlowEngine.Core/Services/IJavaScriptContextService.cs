using FlowEngine.Abstractions.Data;

namespace FlowEngine.Core.Services;

/// <summary>
/// Core service for creating and managing JavaScript execution contexts.
/// </summary>
public interface IJavaScriptContextService
{
    /// <summary>
    /// Creates a JavaScript execution context with access to input data, validation, routing, and utilities.
    /// </summary>
    /// <param name="currentRow">Current row being processed</param>
    /// <param name="inputSchema">Input data schema</param>
    /// <param name="outputSchema">Output data schema</param>
    /// <param name="state">Processing state including routing results</param>
    /// <returns>JavaScript context for script execution</returns>
    IJavaScriptContext CreateContext(
        IArrayRow currentRow,
        ISchema inputSchema,
        ISchema outputSchema,
        ProcessingState state);
}

/// <summary>
/// JavaScript execution context providing access to input, output, validation, routing, and utilities.
/// </summary>
public interface IJavaScriptContext
{
    /// <summary>
    /// Input context for accessing current row data and schema.
    /// </summary>
    IInputContext Input { get; }

    /// <summary>
    /// Output context for setting result fields.
    /// </summary>
    IOutputContext Output { get; }

    /// <summary>
    /// Validation context for business rule validation.
    /// </summary>
    IValidationContext Validate { get; }

    /// <summary>
    /// Routing context for conditional data flow.
    /// </summary>
    IRoutingContext Route { get; }

    /// <summary>
    /// Utility context for common operations.
    /// </summary>
    IUtilityContext Utils { get; }
}

/// <summary>
/// Context for accessing input data and schema information.
/// </summary>
public interface IInputContext
{
    /// <summary>
    /// Gets the current row data as a JavaScript-friendly object.
    /// </summary>
    object Current();

    /// <summary>
    /// Gets the input schema as a JavaScript-friendly object.
    /// </summary>
    object Schema();

    /// <summary>
    /// Gets a specific field value from the current row.
    /// </summary>
    object? GetValue(string fieldName);
}

/// <summary>
/// Context for setting output fields and creating result rows.
/// </summary>
public interface IOutputContext
{
    /// <summary>
    /// Sets a field value in the output row.
    /// </summary>
    void SetField(string name, object? value);

    /// <summary>
    /// Gets the current output modifications.
    /// </summary>
    IReadOnlyDictionary<string, object?> GetModifications();

    /// <summary>
    /// Creates the result row with all modifications applied.
    /// </summary>
    IArrayRow GetResultRow();
}

/// <summary>
/// Context for validation operations.
/// </summary>
public interface IValidationContext
{
    /// <summary>
    /// Validates that required fields are present and not null.
    /// </summary>
    bool Required(params string[] fieldNames);

    /// <summary>
    /// Validates a business rule expression.
    /// </summary>
    bool BusinessRule(string expression);

    /// <summary>
    /// Gets validation error messages.
    /// </summary>
    IReadOnlyList<string> GetErrors();
}

/// <summary>
/// Context for routing operations.
/// </summary>
public interface IRoutingContext
{
    /// <summary>
    /// Sends the current row to a routing target (moves the row).
    /// </summary>
    void Send(string queueName);

    /// <summary>
    /// Sends a copy of the current row to a routing target.
    /// </summary>
    void SendCopy(string queueName);

    /// <summary>
    /// Gets all routing targets for the current row.
    /// </summary>
    IReadOnlyList<RoutingTarget> GetTargets();
}

/// <summary>
/// Context for utility operations.
/// </summary>
public interface IUtilityContext
{
    /// <summary>
    /// Gets the current UTC timestamp.
    /// </summary>
    DateTime Now();

    /// <summary>
    /// Generates a new GUID.
    /// </summary>
    string NewGuid();

    /// <summary>
    /// Formats a value according to the specified format.
    /// </summary>
    string Format(object value, string format);
}

/// <summary>
/// Represents a routing target for conditional data flow.
/// </summary>
public class RoutingTarget
{
    /// <summary>
    /// Gets the name of the routing queue.
    /// </summary>
    public string QueueName { get; }
    
    /// <summary>
    /// Gets the routing mode (move or copy).
    /// </summary>
    public RoutingMode Mode { get; }

    /// <summary>
    /// Initializes a new instance of the RoutingTarget class.
    /// </summary>
    /// <param name="queueName">The name of the routing queue</param>
    /// <param name="mode">The routing mode</param>
    public RoutingTarget(string queueName, RoutingMode mode)
    {
        QueueName = queueName;
        Mode = mode;
    }
}

/// <summary>
/// Routing mode for data flow control.
/// </summary>
public enum RoutingMode
{
    /// <summary>
    /// Move the row to the target (original row is consumed).
    /// </summary>
    Move,

    /// <summary>
    /// Copy the row to the target (original row continues processing).
    /// </summary>
    Copy
}

/// <summary>
/// Processing state including routing results.
/// </summary>
public class ProcessingState
{
    /// <summary>
    /// Gets or sets the routing results for conditional data flow.
    /// </summary>
    public Dictionary<string, List<IArrayRow>> RoutingResults { get; init; } = new();
}