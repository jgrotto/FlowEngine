using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace FlowEngine.Core.Configuration;

/// <summary>
/// Manages global variables and provides variable substitution capabilities.
/// Supports both static variables set during initialization and computed variables
/// that are evaluated on-demand.
/// </summary>
public sealed class GlobalVariables
{
    private readonly Dictionary<string, object> _variables = new();
    private readonly Dictionary<string, Func<object>> _computedVariables = new();
    private volatile bool _isReadOnly = false;
    private string? _configurationFilePath;

    /// <summary>
    /// Initializes a new instance of GlobalVariables with built-in variables.
    /// </summary>
    public GlobalVariables()
    {
        SetupBuiltInVariables();
    }

    /// <summary>
    /// Sets the configuration file path for built-in variables.
    /// </summary>
    /// <param name="configFilePath">Path to the configuration file</param>
    public void SetConfigurationFilePath(string configFilePath)
    {
        _configurationFilePath = configFilePath;
        UpdateFileBasedBuiltInVariables();
    }

    /// <summary>
    /// Sets a static variable value.
    /// </summary>
    /// <param name="name">Variable name (must be valid identifier)</param>
    /// <param name="value">Variable value</param>
    /// <exception cref="InvalidOperationException">Thrown when variables are read-only</exception>
    /// <exception cref="ArgumentException">Thrown when variable name is invalid</exception>
    public void SetVariable(string name, object value)
    {
        if (_isReadOnly)
            throw new InvalidOperationException($"Variables are read-only during pipeline execution. Cannot modify '{name}'.");

        ValidateVariableName(name);
        _variables[name] = value;
    }

    /// <summary>
    /// Gets a variable value with type conversion.
    /// </summary>
    /// <typeparam name="T">Target type for conversion</typeparam>
    /// <param name="name">Variable name</param>
    /// <returns>Variable value converted to target type</returns>
    /// <exception cref="KeyNotFoundException">Thrown when variable is not found</exception>
    public T GetVariable<T>(string name)
    {
        var value = GetVariableValue(name);
        return ConvertValue<T>(value);
    }

    /// <summary>
    /// Gets a variable value as object.
    /// </summary>
    /// <param name="name">Variable name</param>
    /// <returns>Variable value</returns>
    /// <exception cref="KeyNotFoundException">Thrown when variable is not found</exception>
    public object GetVariableValue(string name)
    {
        // Check static variables first
        if (_variables.TryGetValue(name, out var value))
            return value;

        // Check computed variables
        if (_computedVariables.TryGetValue(name, out var computedFunc))
            return computedFunc();

        throw new KeyNotFoundException($"Variable '{name}' is not defined.");
    }

    /// <summary>
    /// Checks if a variable exists.
    /// </summary>
    /// <param name="name">Variable name</param>
    /// <returns>True if variable exists, false otherwise</returns>
    public bool HasVariable(string name)
    {
        return _variables.ContainsKey(name) || _computedVariables.ContainsKey(name);
    }

    /// <summary>
    /// Sets a computed variable that is evaluated on-demand.
    /// Computed variables are always allowed, even when read-only.
    /// </summary>
    /// <param name="name">Variable name</param>
    /// <param name="valueFactory">Function to compute the value</param>
    public void SetComputedVariable(string name, Func<object> valueFactory)
    {
        ArgumentNullException.ThrowIfNull(valueFactory);
        ValidateVariableName(name);
        _computedVariables[name] = valueFactory;
    }

    /// <summary>
    /// Gets all variables as a read-only dictionary.
    /// Computed variables are evaluated at the time of this call.
    /// </summary>
    /// <returns>Read-only dictionary of all variables</returns>
    public IReadOnlyDictionary<string, object> GetAllVariables()
    {
        var result = new Dictionary<string, object>(_variables);

        // Evaluate computed variables
        foreach (var computed in _computedVariables)
        {
            try
            {
                result[computed.Key] = computed.Value();
            }
            catch (Exception ex)
            {
                // If computed variable fails, include error as value
                result[computed.Key] = $"[Error: {ex.Message}]";
            }
        }

        return result.AsReadOnly();
    }

    /// <summary>
    /// Performs variable substitution on input string.
    /// Supports: {VariableName} and {VariableName:Format}
    /// </summary>
    /// <param name="input">Input string with variable placeholders</param>
    /// <returns>String with variables substituted</returns>
    /// <exception cref="KeyNotFoundException">Thrown when referenced variable is not found</exception>
    public string SubstituteVariables(string input)
    {
        if (string.IsNullOrEmpty(input))
            return input;

        return VariableSubstitutionEngine.Substitute(input, this);
    }

    /// <summary>
    /// Loads variables from command-line format "name=value".
    /// </summary>
    /// <param name="variableAssignment">Variable assignment in "name=value" format</param>
    /// <exception cref="ArgumentException">Thrown when format is invalid</exception>
    public void SetVariableFromString(string variableAssignment)
    {
        if (string.IsNullOrWhiteSpace(variableAssignment))
            throw new ArgumentException("Variable assignment cannot be empty.", nameof(variableAssignment));

        var equalIndex = variableAssignment.IndexOf('=');
        if (equalIndex <= 0 || equalIndex == variableAssignment.Length - 1)
            throw new ArgumentException($"Invalid variable assignment format: '{variableAssignment}'. Expected 'name=value'.");

        var name = variableAssignment[..equalIndex].Trim();
        var value = variableAssignment[(equalIndex + 1)..].Trim();

        // Try to parse as different types
        var parsedValue = ParseValue(value);
        SetVariable(name, parsedValue);
    }

    /// <summary>
    /// Loads variable from environment variable.
    /// </summary>
    /// <param name="environmentVariableName">Environment variable name</param>
    /// <exception cref="ArgumentException">Thrown when environment variable is not found</exception>
    public void SetVariableFromEnvironment(string environmentVariableName)
    {
        var value = Environment.GetEnvironmentVariable(environmentVariableName);
        if (value == null)
            throw new ArgumentException($"Environment variable '{environmentVariableName}' is not defined.");

        SetVariable(environmentVariableName, value);
    }

    /// <summary>
    /// Loads variables from JSON file.
    /// </summary>
    /// <param name="filePath">Path to JSON file containing variables</param>
    /// <exception cref="FileNotFoundException">Thrown when file is not found</exception>
    /// <exception cref="JsonException">Thrown when JSON is invalid</exception>
    public void LoadFromFile(string filePath)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException($"Variables file not found: {filePath}");

        var jsonContent = File.ReadAllText(filePath);
        var variables = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(jsonContent);

        if (variables == null)
            return;

        foreach (var kvp in variables)
        {
            var value = ConvertJsonElement(kvp.Value);
            SetVariable(kvp.Key, value);
        }
    }

    /// <summary>
    /// Makes variables read-only. Called by FlowEngine before pipeline execution.
    /// </summary>
    public void MakeReadOnly()
    {
        _isReadOnly = true;
    }

    /// <summary>
    /// Gets the read-only state of the variables.
    /// </summary>
    public bool IsReadOnly => _isReadOnly;

    private static void ValidateVariableName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Variable name cannot be empty.", nameof(name));

        if (!IsValidIdentifier(name))
            throw new ArgumentException($"Invalid variable name: '{name}'. Variable names must be valid identifiers.");
    }

    private static bool IsValidIdentifier(string name)
    {
        if (string.IsNullOrEmpty(name))
            return false;

        // Must start with letter or underscore
        if (!char.IsLetter(name[0]) && name[0] != '_')
            return false;

        // Remaining characters must be letters, digits, or underscores
        return name.Skip(1).All(c => char.IsLetterOrDigit(c) || c == '_');
    }

    private static object ParseValue(string value)
    {
        // Try to parse as different types in order of specificity
        if (bool.TryParse(value, out var boolValue))
            return boolValue;

        if (int.TryParse(value, out var intValue))
            return intValue;

        if (double.TryParse(value, out var doubleValue))
            return doubleValue;

        if (DateTime.TryParse(value, out var dateValue))
            return dateValue;

        // Default to string
        return value;
    }

    private void SetupBuiltInVariables()
    {
        // DateTime variables
        SetComputedVariable("RunDate", () => DateTime.Now);
        SetComputedVariable("RunDateUtc", () => DateTime.UtcNow);
        SetComputedVariable("Timestamp", () => DateTimeOffset.Now.ToUnixTimeSeconds());

        // Environment variables
        SetComputedVariable("MachineName", () => Environment.MachineName);
        SetComputedVariable("UserName", () => Environment.UserName);
        SetComputedVariable("ProcessId", () => Environment.ProcessId);
        SetComputedVariable("WorkingDir", () => Environment.CurrentDirectory);

        // Configuration-based variables (updated when config file is set)
        SetComputedVariable("ConfigFile", () => _configurationFilePath ?? string.Empty);
        SetComputedVariable("ConfigDir", () => 
        {
            if (string.IsNullOrEmpty(_configurationFilePath))
                return Environment.CurrentDirectory;
            return Path.GetDirectoryName(_configurationFilePath) ?? Environment.CurrentDirectory;
        });

        // Computed variables based on other variables
        SetComputedVariable("FileBase", () =>
        {
            if (HasVariable("InputFile"))
            {
                var inputFile = GetVariable<string>("InputFile");
                return Path.GetFileNameWithoutExtension(inputFile);
            }
            return string.Empty;
        });
    }

    private void UpdateFileBasedBuiltInVariables()
    {
        // The computed variables will automatically use the updated _configurationFilePath
        // No additional work needed as they evaluate on-demand
    }

    private static object ConvertJsonElement(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString()!,
            JsonValueKind.Number => element.TryGetInt32(out var intVal) ? intVal : element.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null!,
            JsonValueKind.Array => element.EnumerateArray().Select(ConvertJsonElement).ToArray(),
            JsonValueKind.Object => element.EnumerateObject().ToDictionary(prop => prop.Name, prop => ConvertJsonElement(prop.Value)),
            _ => element.ToString()
        };
    }

    private static T ConvertValue<T>(object value)
    {
        if (value is T directValue)
            return directValue;

        // Handle nullable types
        var targetType = typeof(T);
        var underlyingType = Nullable.GetUnderlyingType(targetType);
        if (underlyingType != null)
        {
            if (value == null)
                return default!;
            targetType = underlyingType;
        }

        // Convert using system conversion
        try
        {
            return (T)Convert.ChangeType(value, targetType, CultureInfo.InvariantCulture);
        }
        catch (Exception ex)
        {
            throw new InvalidCastException($"Cannot convert value '{value}' to type '{typeof(T).Name}'.", ex);
        }
    }
}

/// <summary>
/// High-performance variable substitution engine with regex-based pattern matching.
/// </summary>
public static class VariableSubstitutionEngine
{
    // Pattern: {VariableName} or {VariableName:Format}
    private static readonly Regex SubstitutionPattern = new(
        @"\{(?<name>[A-Za-z_][A-Za-z0-9_]*?)(?::(?<format>[^}]+))?\}",
        RegexOptions.Compiled | RegexOptions.CultureInvariant
    );

    /// <summary>
    /// Performs variable substitution on input string.
    /// </summary>
    /// <param name="input">Input string with variable placeholders</param>
    /// <param name="variables">Global variables instance</param>
    /// <returns>String with variables substituted</returns>
    public static string Substitute(string input, GlobalVariables variables)
    {
        if (string.IsNullOrEmpty(input))
            return input;

        return SubstitutionPattern.Replace(input, match =>
        {
            var variableName = match.Groups["name"].Value;
            var format = match.Groups["format"].Value;

            if (!variables.HasVariable(variableName))
                throw new KeyNotFoundException($"Undefined variable: {variableName}");

            var value = variables.GetVariableValue(variableName);
            return FormatValue(value, format);
        });
    }

    private static string FormatValue(object value, string format)
    {
        if (string.IsNullOrEmpty(format))
            return value?.ToString() ?? string.Empty;

        // Handle DateTime formatting
        if (value is DateTime dateTime)
            return dateTime.ToString(format, CultureInfo.InvariantCulture);

        if (value is DateTimeOffset dateTimeOffset)
            return dateTimeOffset.ToString(format, CultureInfo.InvariantCulture);

        // Handle numeric formatting
        if (value is IFormattable formattable)
            return formattable.ToString(format, CultureInfo.InvariantCulture);

        // Handle string operations (like Path operations)
        if (value is string stringValue)
            return FormatStringValue(stringValue, format);

        // Default formatting
        return value?.ToString() ?? string.Empty;
    }

    private static string FormatStringValue(string value, string format)
    {
        return format.ToLowerInvariant() switch
        {
            "filenamewithoutext" or "filenamewithouextension" => Path.GetFileNameWithoutExtension(value),
            "filename" => Path.GetFileName(value),
            "directory" or "dir" => Path.GetDirectoryName(value) ?? string.Empty,
            "extension" or "ext" => Path.GetExtension(value),
            "fullpath" => Path.GetFullPath(value),
            "upper" => value.ToUpperInvariant(),
            "lower" => value.ToLowerInvariant(),
            _ => value
        };
    }
}