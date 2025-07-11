using FlowEngine.Abstractions;
using FlowEngine.Abstractions.Data;
using FlowEngine.Abstractions.Factories;
using Microsoft.Extensions.Logging;
using System.Collections.Frozen;
using System.Globalization;

namespace FlowEngine.Core.Factories;

/// <summary>
/// Core implementation of IDataTypeService.
/// Provides type-safe operations for working with FlowEngine data types.
/// </summary>
public sealed class DataTypeService : IDataTypeService
{
    private readonly ILogger<DataTypeService> _logger;

    // Pre-built mappings for performance
    private static readonly FrozenDictionary<string, Type> DataTypeStringToType = new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase)
    {
        ["int32"] = typeof(int),
        ["int"] = typeof(int),
        ["int64"] = typeof(long),
        ["long"] = typeof(long),
        ["float32"] = typeof(float),
        ["float"] = typeof(float),
        ["float64"] = typeof(double),
        ["double"] = typeof(double),
        ["decimal"] = typeof(decimal),
        ["bool"] = typeof(bool),
        ["boolean"] = typeof(bool),
        ["string"] = typeof(string),
        ["datetime"] = typeof(DateTime),
        ["datetimeoffset"] = typeof(DateTimeOffset),
        ["timespan"] = typeof(TimeSpan),
        ["guid"] = typeof(Guid),
        ["byte"] = typeof(byte),
        ["sbyte"] = typeof(sbyte),
        ["int16"] = typeof(short),
        ["short"] = typeof(short),
        ["uint16"] = typeof(ushort),
        ["ushort"] = typeof(ushort),
        ["uint32"] = typeof(uint),
        ["uint"] = typeof(uint),
        ["uint64"] = typeof(ulong),
        ["ulong"] = typeof(ulong),
        ["char"] = typeof(char),
        ["bytes"] = typeof(byte[])
    }.ToFrozenDictionary();

    private static readonly FrozenDictionary<Type, string> TypeToDataTypeString = new Dictionary<Type, string>
    {
        [typeof(int)] = "int32",
        [typeof(long)] = "int64",
        [typeof(float)] = "float32",
        [typeof(double)] = "float64",
        [typeof(decimal)] = "decimal",
        [typeof(bool)] = "bool",
        [typeof(string)] = "string",
        [typeof(DateTime)] = "datetime",
        [typeof(DateTimeOffset)] = "datetimeoffset",
        [typeof(TimeSpan)] = "timespan",
        [typeof(Guid)] = "guid",
        [typeof(byte)] = "byte",
        [typeof(sbyte)] = "sbyte",
        [typeof(short)] = "int16",
        [typeof(ushort)] = "uint16",
        [typeof(uint)] = "uint32",
        [typeof(ulong)] = "uint64",
        [typeof(char)] = "char",
        [typeof(byte[])] = "bytes"
    }.ToFrozenDictionary();

    /// <summary>
    /// Initializes a new DataTypeService.
    /// </summary>
    /// <param name="logger">Logger for service operations</param>
    public DataTypeService(ILogger<DataTypeService> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public object? ConvertValue(object? value, Type targetType)
    {
        var result = ConvertValueWithDetails(value, targetType);
        if (result.IsSuccess)
        {
            return result.Value;
        }

        throw new InvalidCastException(result.ErrorMessage);
    }

    /// <inheritdoc />
    public bool TryConvertValue(object? value, Type targetType, out object? convertedValue)
    {
        var result = ConvertValueWithDetails(value, targetType);
        convertedValue = result.Value;
        return result.IsSuccess;
    }

    /// <inheritdoc />
    public TypeConversionResult ConvertValueWithDetails(object? value, Type targetType)
    {
        ArgumentNullException.ThrowIfNull(targetType);

        var sourceType = value?.GetType();

        // Handle null values
        if (value == null)
        {
            if (IsNullableType(targetType) || !targetType.IsValueType)
            {
                return TypeConversionResult.Success(null, sourceType, targetType);
            }
            else
            {
                return TypeConversionResult.Failure($"Cannot convert null to non-nullable type {targetType.Name}", sourceType, targetType);
            }
        }

        // Handle same type or compatible assignment
        if (targetType.IsAssignableFrom(sourceType))
        {
            return TypeConversionResult.Success(value, sourceType, targetType);
        }

        // Handle nullable target types
        var underlyingTargetType = GetUnderlyingType(targetType);
        if (underlyingTargetType != targetType)
        {
            var underlyingResult = ConvertValueWithDetails(value, underlyingTargetType);
            if (underlyingResult.IsSuccess)
            {
                return TypeConversionResult.Success(underlyingResult.Value, sourceType, targetType);
            }
            return underlyingResult;
        }

        try
        {
            // Special conversions
            var converted = PerformSpecialConversions(value, targetType);
            if (converted != null || targetType == typeof(string))
            {
                return TypeConversionResult.Success(converted, sourceType, targetType);
            }

            // Standard type conversion
            var standardConverted = Convert.ChangeType(value, targetType, CultureInfo.InvariantCulture);
            return TypeConversionResult.Success(standardConverted, sourceType, targetType);
        }
        catch (Exception ex)
        {
            _logger.LogDebug("Failed to convert {SourceType} to {TargetType}: {Error}",
                sourceType?.Name, targetType.Name, ex.Message);

            return TypeConversionResult.Failure(
                $"Cannot convert {sourceType?.Name ?? "null"} to {targetType.Name}: {ex.Message}",
                sourceType,
                targetType);
        }
    }

    /// <inheritdoc />
    public ValidationResult ValidateValue(object? value, ColumnDefinition columnDef)
    {
        ArgumentNullException.ThrowIfNull(columnDef);

        var errors = new List<string>();

        // Check nullability
        if (value == null)
        {
            if (!columnDef.IsNullable && !IsNullableType(columnDef.DataType))
            {
                errors.Add($"Value cannot be null for non-nullable column '{columnDef.Name}'");
            }
            return errors.Count == 0 ? ValidationResult.Success() : ValidationResult.Failure(errors.ToArray());
        }

        // Check type compatibility
        if (!IsValueCompatible(value, columnDef))
        {
            if (!TryConvertValue(value, columnDef.DataType, out _))
            {
                errors.Add($"Value of type {value.GetType().Name} is not compatible with column type {columnDef.DataType.Name}");
            }
        }

        return errors.Count == 0 ? ValidationResult.Success() : ValidationResult.Failure(errors.ToArray());
    }

    /// <inheritdoc />
    public object? GetDefaultValue(ColumnDefinition columnDef)
    {
        ArgumentNullException.ThrowIfNull(columnDef);

        // Use explicit default if specified
        if (columnDef.DefaultValue != null)
        {
            if (TryConvertValue(columnDef.DefaultValue, columnDef.DataType, out var convertedDefault))
            {
                return convertedDefault;
            }
        }

        // Return null for nullable types
        if (columnDef.IsNullable || IsNullableType(columnDef.DataType))
        {
            return null;
        }

        // Return default value for value types
        if (columnDef.DataType.IsValueType)
        {
            return Activator.CreateInstance(columnDef.DataType);
        }

        // Return null for reference types (they are nullable by default)
        return null;
    }

    /// <inheritdoc />
    public bool IsValueCompatible(object? value, ColumnDefinition columnDef)
    {
        ArgumentNullException.ThrowIfNull(columnDef);

        if (value == null)
        {
            return columnDef.IsNullable || IsNullableType(columnDef.DataType) || !columnDef.DataType.IsValueType;
        }

        var valueType = value.GetType();
        var targetType = columnDef.DataType;

        // Check direct assignment compatibility
        if (targetType.IsAssignableFrom(valueType))
        {
            return true;
        }

        // Check if conversion is possible
        return TryConvertValue(value, targetType, out _);
    }

    /// <inheritdoc />
    public Type GetNetTypeFromDataTypeString(string dataTypeString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataTypeString);

        if (DataTypeStringToType.TryGetValue(dataTypeString, out var type))
        {
            return type;
        }

        throw new ArgumentException($"Unknown data type string: {dataTypeString}");
    }

    /// <inheritdoc />
    public string GetDataTypeStringFromNetType(Type netType)
    {
        ArgumentNullException.ThrowIfNull(netType);

        // Handle nullable types
        var underlyingType = GetUnderlyingType(netType);

        if (TypeToDataTypeString.TryGetValue(underlyingType, out var dataTypeString))
        {
            return dataTypeString;
        }

        throw new ArgumentException($"Unsupported .NET type: {netType.Name}");
    }

    /// <inheritdoc />
    public bool IsNullableType(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);

        return !type.IsValueType ||
               (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Nullable<>));
    }

    /// <inheritdoc />
    public Type GetUnderlyingType(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);

        return Nullable.GetUnderlyingType(type) ?? type;
    }

    /// <inheritdoc />
    public long EstimateValueSize(object? value)
    {
        if (value == null)
        {
            return 8; // Reference size
        }

        return value switch
        {
            byte or sbyte => 1,
            short or ushort => 2,
            int or uint or float => 4,
            long or ulong or double or DateTime or DateTimeOffset => 8,
            decimal => 16,
            Guid => 16,
            char => 2,
            bool => 1,
            string str => 24 + (str.Length * 2), // Approximate string overhead + characters
            byte[] bytes => 24 + bytes.Length, // Array overhead + bytes
            TimeSpan => 8,
            _ => 64 // Conservative estimate for unknown types
        };
    }

    /// <inheritdoc />
    public long EstimateColumnSize(ColumnDefinition columnDef)
    {
        ArgumentNullException.ThrowIfNull(columnDef);

        var baseSize = columnDef.DataType switch
        {
            Type t when t == typeof(byte) || t == typeof(sbyte) => 1,
            Type t when t == typeof(short) || t == typeof(ushort) => 2,
            Type t when t == typeof(int) || t == typeof(uint) || t == typeof(float) => 4,
            Type t when t == typeof(long) || t == typeof(ulong) || t == typeof(double) || t == typeof(DateTime) => 8,
            Type t when t == typeof(decimal) => 16,
            Type t when t == typeof(string) => 50, // Average string size
            Type t when t == typeof(Guid) => 16,
            Type t when t == typeof(char) => 2,
            Type t when t == typeof(bool) => 1,
            Type t when t == typeof(byte[]) => 100, // Average byte array size
            _ => 8
        };

        // Add nullable overhead if applicable
        if (columnDef.IsNullable && columnDef.DataType.IsValueType)
        {
            baseSize += 8; // Nullable<T> overhead
        }

        return baseSize;
    }

    /// <inheritdoc />
    public ColumnValidationResult ValidateAndNormalizeColumn(ColumnDefinition columnDef)
    {
        ArgumentNullException.ThrowIfNull(columnDef);

        var errors = new List<string>();
        var warnings = new List<string>();

        // Validate column name
        if (string.IsNullOrWhiteSpace(columnDef.Name))
        {
            errors.Add("Column name cannot be null or empty");
        }

        // Validate data type
        if (columnDef.DataType == null)
        {
            errors.Add("Column data type cannot be null");
        }
        else if (!IsSupportedType(columnDef.DataType))
        {
            errors.Add($"Unsupported data type: {columnDef.DataType.Name}");
        }

        // Validate default value if specified
        if (columnDef.DefaultValue != null && columnDef.DataType != null)
        {
            if (!TryConvertValue(columnDef.DefaultValue, columnDef.DataType, out _))
            {
                errors.Add($"Default value is not compatible with column type {columnDef.DataType.Name}");
            }
        }

        if (errors.Count > 0)
        {
            return ColumnValidationResult.Failure(errors.ToArray());
        }

        // Create normalized column (no changes needed for now)
        var normalizedColumn = columnDef;
        return ColumnValidationResult.Success(normalizedColumn);
    }

    /// <inheritdoc />
    public string[] GetSupportedDataTypes()
    {
        return DataTypeStringToType.Keys.ToArray();
    }

    /// <inheritdoc />
    public TypeCoercionResult CoerceTypes(object? value1, object? value2)
    {
        if (value1 == null && value2 == null)
        {
            return TypeCoercionResult.Success(typeof(object), null, null);
        }

        if (value1 == null)
        {
            var type2 = value2!.GetType();
            return TypeCoercionResult.Success(type2, null, value2);
        }

        if (value2 == null)
        {
            var type1 = value1.GetType();
            return TypeCoercionResult.Success(type1, value1, null);
        }

        var sourceType1 = value1.GetType();
        var sourceType2 = value2.GetType();

        // If types are the same, no coercion needed
        if (sourceType1 == sourceType2)
        {
            return TypeCoercionResult.Success(sourceType1, value1, value2);
        }

        // Try to find a common type
        var commonType = FindCommonType(sourceType1, sourceType2);
        if (commonType != null)
        {
            if (TryConvertValue(value1, commonType, out var converted1) &&
                TryConvertValue(value2, commonType, out var converted2))
            {
                return TypeCoercionResult.Success(commonType, converted1, converted2);
            }
        }

        return TypeCoercionResult.Failure($"Cannot find common type for {sourceType1.Name} and {sourceType2.Name}");
    }

    /// <summary>
    /// Performs special type conversions that aren't handled by standard Convert.ChangeType.
    /// </summary>
    private object? PerformSpecialConversions(object value, Type targetType)
    {
        // String conversions
        if (targetType == typeof(string))
        {
            return value.ToString();
        }

        // Guid conversions
        if (targetType == typeof(Guid) && value is string str)
        {
            if (Guid.TryParse(str, out var guid))
            {
                return guid;
            }
        }

        // DateTime conversions
        if (targetType == typeof(DateTime) && value is string dateStr)
        {
            if (DateTime.TryParse(dateStr, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dateTime))
            {
                return dateTime;
            }
        }

        // TimeSpan conversions
        if (targetType == typeof(TimeSpan) && value is string timeStr)
        {
            if (TimeSpan.TryParse(timeStr, CultureInfo.InvariantCulture, out var timeSpan))
            {
                return timeSpan;
            }
        }

        return null;
    }

    /// <summary>
    /// Checks if a type is supported by FlowEngine.
    /// </summary>
    private static bool IsSupportedType(Type type)
    {
        var underlyingType = Nullable.GetUnderlyingType(type) ?? type;
        return TypeToDataTypeString.ContainsKey(underlyingType);
    }

    /// <summary>
    /// Finds a common type that both source types can be converted to.
    /// </summary>
    private Type? FindCommonType(Type type1, Type type2)
    {
        // Numeric type hierarchy for coercion
        var numericTypes = new[]
        {
            typeof(byte), typeof(sbyte), typeof(short), typeof(ushort),
            typeof(int), typeof(uint), typeof(long), typeof(ulong),
            typeof(float), typeof(double), typeof(decimal)
        };

        var index1 = Array.IndexOf(numericTypes, type1);
        var index2 = Array.IndexOf(numericTypes, type2);

        // If both are numeric types, use the higher precision type
        if (index1 >= 0 && index2 >= 0)
        {
            return numericTypes[Math.Max(index1, index2)];
        }

        // String is a common fallback
        return typeof(string);
    }
}
