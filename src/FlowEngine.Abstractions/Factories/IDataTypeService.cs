using FlowEngine.Abstractions.Data;

namespace FlowEngine.Abstractions.Factories;

/// <summary>
/// Service interface for data type conversion, validation, and utility operations.
/// Provides type-safe operations for working with FlowEngine data types.
/// </summary>
public interface IDataTypeService
{
    /// <summary>
    /// Converts a value to the specified target type with validation.
    /// </summary>
    /// <param name="value">Value to convert</param>
    /// <param name="targetType">Target .NET type</param>
    /// <returns>Converted value or null if conversion fails</returns>
    object? ConvertValue(object? value, Type targetType);

    /// <summary>
    /// Attempts to convert a value to the specified target type.
    /// </summary>
    /// <param name="value">Value to convert</param>
    /// <param name="targetType">Target .NET type</param>
    /// <param name="convertedValue">Output parameter for converted value</param>
    /// <returns>True if conversion succeeded, false otherwise</returns>
    bool TryConvertValue(object? value, Type targetType, out object? convertedValue);

    /// <summary>
    /// Converts a value to the target type with detailed error information.
    /// </summary>
    /// <param name="value">Value to convert</param>
    /// <param name="targetType">Target .NET type</param>
    /// <returns>Conversion result with success status and error details</returns>
    TypeConversionResult ConvertValueWithDetails(object? value, Type targetType);

    /// <summary>
    /// Validates a value against a column definition.
    /// </summary>
    /// <param name="value">Value to validate</param>
    /// <param name="columnDef">Column definition to validate against</param>
    /// <returns>Validation result indicating success or failure with details</returns>
    ValidationResult ValidateValue(object? value, ColumnDefinition columnDef);

    /// <summary>
    /// Gets the default value for a column definition.
    /// </summary>
    /// <param name="columnDef">Column definition</param>
    /// <returns>Default value for the column type</returns>
    object? GetDefaultValue(ColumnDefinition columnDef);

    /// <summary>
    /// Determines if a value is compatible with a column definition.
    /// </summary>
    /// <param name="value">Value to check</param>
    /// <param name="columnDef">Column definition</param>
    /// <returns>True if value is compatible, false otherwise</returns>
    bool IsValueCompatible(object? value, ColumnDefinition columnDef);

    /// <summary>
    /// Gets the .NET type that corresponds to a FlowEngine data type string.
    /// </summary>
    /// <param name="dataTypeString">FlowEngine data type string (e.g., "int32", "string", "datetime")</param>
    /// <returns>.NET type corresponding to the data type string</returns>
    /// <exception cref="ArgumentException">Thrown when data type string is not recognized</exception>
    Type GetNetTypeFromDataTypeString(string dataTypeString);

    /// <summary>
    /// Gets the FlowEngine data type string from a .NET type.
    /// </summary>
    /// <param name="netType">.NET type</param>
    /// <returns>FlowEngine data type string</returns>
    /// <exception cref="ArgumentException">Thrown when .NET type is not supported</exception>
    string GetDataTypeStringFromNetType(Type netType);

    /// <summary>
    /// Determines if a .NET type is nullable.
    /// </summary>
    /// <param name="type">.NET type to check</param>
    /// <returns>True if type is nullable, false otherwise</returns>
    bool IsNullableType(Type type);

    /// <summary>
    /// Gets the underlying type for nullable types, or the type itself for non-nullable types.
    /// </summary>
    /// <param name="type">.NET type</param>
    /// <returns>Underlying non-nullable type</returns>
    Type GetUnderlyingType(Type type);

    /// <summary>
    /// Estimates the memory size of a value in bytes.
    /// </summary>
    /// <param name="value">Value to estimate size for</param>
    /// <returns>Estimated memory size in bytes</returns>
    long EstimateValueSize(object? value);

    /// <summary>
    /// Estimates the memory size of a column value based on its definition.
    /// </summary>
    /// <param name="columnDef">Column definition</param>
    /// <returns>Estimated average memory size per value in bytes</returns>
    long EstimateColumnSize(ColumnDefinition columnDef);

    /// <summary>
    /// Validates and normalizes a column definition.
    /// </summary>
    /// <param name="columnDef">Column definition to validate</param>
    /// <returns>Validation result with normalized column definition if successful</returns>
    ColumnValidationResult ValidateAndNormalizeColumn(ColumnDefinition columnDef);

    /// <summary>
    /// Gets supported data type strings.
    /// </summary>
    /// <returns>Array of supported FlowEngine data type strings</returns>
    string[] GetSupportedDataTypes();

    /// <summary>
    /// Performs type coercion for mixed-type operations.
    /// Finds a common type that both values can be converted to.
    /// </summary>
    /// <param name="value1">First value</param>
    /// <param name="value2">Second value</param>
    /// <returns>Type coercion result with common type and converted values</returns>
    TypeCoercionResult CoerceTypes(object? value1, object? value2);
}

/// <summary>
/// Result of a type conversion operation.
/// </summary>
public sealed class TypeConversionResult
{
    /// <summary>
    /// Whether the conversion succeeded.
    /// </summary>
    public bool IsSuccess { get; init; }

    /// <summary>
    /// The converted value (valid only if IsSuccess is true).
    /// </summary>
    public object? Value { get; init; }

    /// <summary>
    /// Error message if conversion failed.
    /// </summary>
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// Source type that was converted from.
    /// </summary>
    public Type? SourceType { get; init; }

    /// <summary>
    /// Target type that was converted to.
    /// </summary>
    public Type? TargetType { get; init; }

    /// <summary>
    /// Creates a successful conversion result.
    /// </summary>
    public static TypeConversionResult Success(object? value, Type? sourceType, Type targetType) =>
        new() { IsSuccess = true, Value = value, SourceType = sourceType, TargetType = targetType };

    /// <summary>
    /// Creates a failed conversion result.
    /// </summary>
    public static TypeConversionResult Failure(string errorMessage, Type? sourceType, Type targetType) =>
        new() { IsSuccess = false, ErrorMessage = errorMessage, SourceType = sourceType, TargetType = targetType };
}

/// <summary>
/// Result of column validation and normalization.
/// </summary>
public sealed class ColumnValidationResult
{
    /// <summary>
    /// Whether the validation succeeded.
    /// </summary>
    public bool IsSuccess { get; init; }

    /// <summary>
    /// The normalized column definition (valid only if IsSuccess is true).
    /// </summary>
    public ColumnDefinition? NormalizedColumn { get; init; }

    /// <summary>
    /// Validation errors if validation failed.
    /// </summary>
    public string[] Errors { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Validation warnings.
    /// </summary>
    public string[] Warnings { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Creates a successful validation result.
    /// </summary>
    public static ColumnValidationResult Success(ColumnDefinition normalizedColumn) =>
        new() { IsSuccess = true, NormalizedColumn = normalizedColumn };

    /// <summary>
    /// Creates a failed validation result.
    /// </summary>
    public static ColumnValidationResult Failure(params string[] errors) =>
        new() { IsSuccess = false, Errors = errors };
}

/// <summary>
/// Result of type coercion operation.
/// </summary>
public sealed class TypeCoercionResult
{
    /// <summary>
    /// Whether coercion succeeded.
    /// </summary>
    public bool IsSuccess { get; init; }

    /// <summary>
    /// Common type that both values can be converted to.
    /// </summary>
    public Type? CommonType { get; init; }

    /// <summary>
    /// First value converted to common type.
    /// </summary>
    public object? Value1 { get; init; }

    /// <summary>
    /// Second value converted to common type.
    /// </summary>
    public object? Value2 { get; init; }

    /// <summary>
    /// Error message if coercion failed.
    /// </summary>
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// Creates a successful coercion result.
    /// </summary>
    public static TypeCoercionResult Success(Type commonType, object? value1, object? value2) =>
        new() { IsSuccess = true, CommonType = commonType, Value1 = value1, Value2 = value2 };

    /// <summary>
    /// Creates a failed coercion result.
    /// </summary>
    public static TypeCoercionResult Failure(string errorMessage) =>
        new() { IsSuccess = false, ErrorMessage = errorMessage };
}
