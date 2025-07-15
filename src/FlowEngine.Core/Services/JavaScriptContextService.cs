using FlowEngine.Abstractions.Data;
using FlowEngine.Abstractions.Factories;
using FlowEngine.Core.Data;
using FlowEngine.Core.Factories;
using Microsoft.Extensions.Logging;

namespace FlowEngine.Core.Services;

/// <summary>
/// Service for creating JavaScript execution contexts with access to input, output, validation, routing, and utilities.
/// </summary>
public class JavaScriptContextService : IJavaScriptContextService
{
    private readonly IArrayRowFactory _arrayRowFactory;
    private readonly ILogger<JavaScriptContextService> _logger;

    /// <summary>
    /// Initializes a new instance of the JavaScriptContextService class.
    /// </summary>
    /// <param name="arrayRowFactory">Factory for creating array rows</param>
    /// <param name="logger">Logger instance for diagnostics</param>
    public JavaScriptContextService(IArrayRowFactory arrayRowFactory, ILogger<JavaScriptContextService> logger)
    {
        _arrayRowFactory = arrayRowFactory;
        _logger = logger;
    }

    /// <summary>
    /// Creates a JavaScript execution context with all required subsystems.
    /// </summary>
    public IJavaScriptContext CreateContext(
        IArrayRow currentRow,
        ISchema inputSchema,
        ISchema outputSchema,
        ProcessingState state)
    {
        return new JavaScriptContext
        {
            Input = new InputContext(currentRow, inputSchema),
            Output = new OutputContext(outputSchema, _arrayRowFactory),
            Validate = new ValidationContext(currentRow, inputSchema),
            Route = new RoutingContext(state),
            Utils = new UtilityContext(_logger)
        };
    }
}

/// <summary>
/// JavaScript execution context implementation providing access to all subsystems.
/// </summary>
internal class JavaScriptContext : IJavaScriptContext
{
    public IInputContext Input { get; init; } = null!;
    public IOutputContext Output { get; init; } = null!;
    public IValidationContext Validate { get; init; } = null!;
    public IRoutingContext Route { get; init; } = null!;
    public IUtilityContext Utils { get; init; } = null!;
}

/// <summary>
/// Input context for accessing current row data and schema information.
/// </summary>
internal class InputContext : IInputContext
{
    private readonly IArrayRow _currentRow;
    private readonly ISchema _schema;

    public InputContext(IArrayRow currentRow, ISchema schema)
    {
        _currentRow = currentRow;
        _schema = schema;
    }

    /// <summary>
    /// Gets the current row data as a JavaScript-friendly object.
    /// </summary>
    public object Current()
    {
        var result = new Dictionary<string, object?>();

        for (int i = 0; i < _schema.ColumnCount; i++)
        {
            var column = _schema.Columns[i];
            var fieldName = column.Name;
            var fieldValue = _currentRow[i];
            result[fieldName] = fieldValue;
        }

        return result;
    }

    /// <summary>
    /// Gets the input schema as a JavaScript-friendly object.
    /// </summary>
    public object Schema()
    {
        var fields = new List<object>();

        for (int i = 0; i < _schema.ColumnCount; i++)
        {
            var column = _schema.Columns[i];

            fields.Add(new
            {
                Name = column.Name,
                Type = column.DataType.Name,
                IsNullable = column.IsNullable,
                Index = i
            });
        }

        return new
        {
            Fields = fields,
            FieldCount = _schema.ColumnCount
        };
    }

    /// <summary>
    /// Gets a specific field value from the current row.
    /// </summary>
    public object? GetValue(string fieldName)
    {
        var fieldIndex = _schema.GetIndex(fieldName);
        if (fieldIndex == -1)
        {
            return null;
        }

        return _currentRow[fieldIndex];
    }
}

/// <summary>
/// Output context for setting result fields and creating output rows.
/// </summary>
internal class OutputContext : IOutputContext
{
    private readonly ISchema _outputSchema;
    private readonly IArrayRowFactory _arrayRowFactory;
    private readonly Dictionary<string, object?> _modifications = new();

    public OutputContext(ISchema outputSchema, IArrayRowFactory arrayRowFactory)
    {
        _outputSchema = outputSchema;
        _arrayRowFactory = arrayRowFactory;
    }

    /// <summary>
    /// Sets a field value in the output row.
    /// </summary>
    public void SetField(string name, object? value)
    {
        var fieldIndex = _outputSchema.GetIndex(name);
        if (fieldIndex == -1)
        {
            throw new InvalidOperationException($"Field '{name}' not found in output schema");
        }

        _modifications[name] = value;
    }

    /// <summary>
    /// Gets the current output modifications.
    /// </summary>
    public IReadOnlyDictionary<string, object?> GetModifications()
    {
        return _modifications.AsReadOnly();
    }

    /// <summary>
    /// Creates the result row with all modifications applied.
    /// </summary>
    public IArrayRow GetResultRow()
    {
        // Use the factory method that accepts a dictionary of field values
        return _arrayRowFactory.CreateRow(_outputSchema, _modifications);
    }
}

/// <summary>
/// Validation context for business rule validation.
/// </summary>
internal class ValidationContext : IValidationContext
{
    private readonly IArrayRow _currentRow;
    private readonly ISchema _schema;
    private readonly List<string> _errors = new();

    public ValidationContext(IArrayRow currentRow, ISchema schema)
    {
        _currentRow = currentRow;
        _schema = schema;
    }

    /// <summary>
    /// Validates that required fields are present and not null.
    /// </summary>
    public bool Required(params string[] fieldNames)
    {
        bool allValid = true;

        foreach (var fieldName in fieldNames)
        {
            var fieldIndex = _schema.GetIndex(fieldName);
            if (fieldIndex == -1)
            {
                _errors.Add($"Field '{fieldName}' not found in schema");
                allValid = false;
                continue;
            }

            var value = _currentRow[fieldIndex];
            if (value == null)
            {
                _errors.Add($"Required field '{fieldName}' is null");
                allValid = false;
            }
        }

        return allValid;
    }

    /// <summary>
    /// Validates a business rule expression (simplified implementation).
    /// </summary>
    public bool BusinessRule(string expression)
    {
        // TODO: Implement proper expression evaluation
        // For now, return true as a placeholder
        return true;
    }

    /// <summary>
    /// Gets validation error messages.
    /// </summary>
    public IReadOnlyList<string> GetErrors()
    {
        return _errors.AsReadOnly();
    }
}

/// <summary>
/// Routing context for conditional data flow.
/// </summary>
internal class RoutingContext : IRoutingContext
{
    private readonly ProcessingState _state;
    private readonly List<RoutingTarget> _targets = new();

    public RoutingContext(ProcessingState state)
    {
        _state = state;
    }

    /// <summary>
    /// Sends the current row to a routing target (moves the row).
    /// </summary>
    public void Send(string queueName)
    {
        _targets.Add(new RoutingTarget(queueName, RoutingMode.Move));
    }

    /// <summary>
    /// Sends a copy of the current row to a routing target.
    /// </summary>
    public void SendCopy(string queueName)
    {
        _targets.Add(new RoutingTarget(queueName, RoutingMode.Copy));
    }

    /// <summary>
    /// Gets all routing targets for the current row.
    /// </summary>
    public IReadOnlyList<RoutingTarget> GetTargets()
    {
        return _targets.AsReadOnly();
    }
}

/// <summary>
/// Utility context for common operations.
/// </summary>
internal class UtilityContext : IUtilityContext
{
    private readonly ILogger? _logger;

    /// <summary>
    /// Initializes a new instance of the UtilityContext.
    /// </summary>
    /// <param name="logger">Optional logger for diagnostic information</param>
    public UtilityContext(ILogger? logger = null)
    {
        _logger = logger;
    }

    /// <summary>
    /// Gets the current UTC timestamp.
    /// </summary>
    public DateTime Now()
    {
        return DateTime.UtcNow;
    }

    /// <summary>
    /// Generates a new GUID.
    /// </summary>
    public string NewGuid()
    {
        return Guid.NewGuid().ToString();
    }

    /// <summary>
    /// Formats a value according to the specified format.
    /// </summary>
    public string Format(object value, string format)
    {
        if (value == null)
        {
            return string.Empty;
        }

        if (value is DateTime dateTime)
        {
            return dateTime.ToString(format);
        }

        if (value is decimal || value is double || value is float)
        {
            return string.Format($"{{0:{format}}}", value);
        }

        return value.ToString() ?? string.Empty;
    }

    /// <summary>
    /// Logs a message with the specified level.
    /// </summary>
    /// <param name="level">Log level (e.g., 'info', 'warn', 'error')</param>
    /// <param name="message">Message to log</param>
    public void Log(string level, string message)
    {
        if (_logger == null)
        {
            // Fallback to console if no logger is available
            Console.WriteLine($"[{level.ToUpper()}] {message}");
            return;
        }

        // Map string level to Microsoft.Extensions.Logging levels
        switch (level.ToLowerInvariant())
        {
            case "trace":
                _logger.LogTrace("[JavaScript] {Message}", message);
                break;
            case "debug":
                _logger.LogDebug("[JavaScript] {Message}", message);
                break;
            case "info":
            case "information":
                _logger.LogInformation("[JavaScript] {Message}", message);
                break;
            case "warn":
            case "warning":
                _logger.LogWarning("[JavaScript] {Message}", message);
                break;
            case "error":
                _logger.LogError("[JavaScript] {Message}", message);
                break;
            case "critical":
            case "fatal":
                _logger.LogCritical("[JavaScript] {Message}", message);
                break;
            default:
                _logger.LogInformation("[JavaScript] [{Level}] {Message}", level, message);
                break;
        }
    }
}
