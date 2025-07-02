using FlowEngine.Abstractions.Data;

namespace FlowEngine.Core.Data;

/// <summary>
/// Factory implementation for creating ArrayRow instances.
/// Provides the bridge between plugins and the concrete ArrayRow implementation.
/// </summary>
public sealed class ArrayRowFactory : IArrayRowFactory
{
    /// <inheritdoc />
    public IArrayRow Create(ISchema schema, object?[] values)
    {
        return new ArrayRow(schema, values);
    }

    /// <inheritdoc />
    public IArrayRow CreateFromDictionary(ISchema schema, IReadOnlyDictionary<string, object?> data)
    {
        return ArrayRow.FromDictionary(schema, data);
    }

    /// <inheritdoc />
    public IArrayRow CreateEmpty(ISchema schema)
    {
        return ArrayRow.Empty(schema);
    }

    /// <inheritdoc />
    public IArrayRow CreateWithDefaults(ISchema schema)
    {
        var values = new object?[schema.ColumnCount];
        
        for (int i = 0; i < schema.Columns.Length; i++)
        {
            var column = schema.Columns[i];
            values[i] = GetDefaultValue(column.DataType);
        }
        
        return new ArrayRow(schema, values);
    }

    /// <inheritdoc />
    public IArrayRowBuilder CreateBuilder(ISchema schema)
    {
        return new ArrayRowBuilder(schema, this);
    }

    /// <summary>
    /// Gets the default value for a given type.
    /// </summary>
    /// <param name="type">The type to get default value for</param>
    /// <returns>The default value</returns>
    private static object? GetDefaultValue(Type type)
    {
        if (type.IsValueType)
        {
            return Activator.CreateInstance(type);
        }
        
        return null;
    }
}

/// <summary>
/// Builder implementation for constructing ArrayRows with fluent syntax.
/// </summary>
internal sealed class ArrayRowBuilder : IArrayRowBuilder
{
    private readonly ISchema _schema;
    private readonly IArrayRowFactory _factory;
    private readonly object?[] _values;
    private readonly bool[] _isSet;

    /// <summary>
    /// Initializes a new instance of the ArrayRowBuilder class.
    /// </summary>
    /// <param name="schema">The schema for the row being built</param>
    /// <param name="factory">The factory for creating the final ArrayRow</param>
    public ArrayRowBuilder(ISchema schema, IArrayRowFactory factory)
    {
        _schema = schema ?? throw new ArgumentNullException(nameof(schema));
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
        _values = new object?[schema.ColumnCount];
        _isSet = new bool[schema.ColumnCount];
    }

    /// <inheritdoc />
    public ISchema Schema => _schema;

    /// <inheritdoc />
    public IArrayRowBuilder Set(string columnName, object? value)
    {
        var index = _schema.GetIndex(columnName);
        if (index < 0)
            throw new ArgumentException($"Column '{columnName}' does not exist in the schema", nameof(columnName));
        
        return Set(index, value);
    }

    /// <inheritdoc />
    public IArrayRowBuilder Set(int index, object? value)
    {
        if (index < 0 || index >= _values.Length)
            throw new ArgumentOutOfRangeException(nameof(index), index, "Index is out of range");
        
        _values[index] = value;
        _isSet[index] = true;
        
        return this;
    }

    /// <inheritdoc />
    public IArrayRowBuilder SetMany(IReadOnlyDictionary<string, object?> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        
        foreach (var kvp in values)
        {
            Set(kvp.Key, kvp.Value);
        }
        
        return this;
    }

    /// <inheritdoc />
    public object? Get(string columnName)
    {
        var index = _schema.GetIndex(columnName);
        if (index < 0)
            throw new ArgumentException($"Column '{columnName}' does not exist in the schema", nameof(columnName));
        
        return Get(index);
    }

    /// <inheritdoc />
    public object? Get(int index)
    {
        if (index < 0 || index >= _values.Length)
            throw new ArgumentOutOfRangeException(nameof(index), index, "Index is out of range");
        
        return _values[index];
    }

    /// <inheritdoc />
    public bool TryGet(string columnName, out object? value)
    {
        var index = _schema.GetIndex(columnName);
        if (index < 0)
        {
            value = null;
            return false;
        }
        
        value = _values[index];
        return _isSet[index];
    }

    /// <inheritdoc />
    public bool HasValue(string columnName)
    {
        var index = _schema.GetIndex(columnName);
        return index >= 0 && _isSet[index];
    }

    /// <inheritdoc />
    public bool HasValue(int index)
    {
        return index >= 0 && index < _isSet.Length && _isSet[index];
    }

    /// <inheritdoc />
    public IArrayRowBuilder Clear(string columnName)
    {
        var index = _schema.GetIndex(columnName);
        if (index < 0)
            throw new ArgumentException($"Column '{columnName}' does not exist in the schema", nameof(columnName));
        
        _values[index] = null;
        _isSet[index] = false;
        
        return this;
    }

    /// <inheritdoc />
    public IArrayRowBuilder ClearAll()
    {
        Array.Clear(_values);
        Array.Clear(_isSet);
        
        return this;
    }

    /// <inheritdoc />
    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        
        for (int i = 0; i < _schema.Columns.Length; i++)
        {
            var column = _schema.Columns[i];
            
            // Check if required columns are set
            if (!column.IsNullable && !_isSet[i])
            {
                errors.Add($"Required column '{column.Name}' has not been set");
            }
            
            // Check type compatibility if value is set
            if (_isSet[i] && _values[i] != null)
            {
                var valueType = _values[i]!.GetType();
                if (!IsTypeCompatible(valueType, column.DataType))
                {
                    errors.Add($"Column '{column.Name}' expects type {column.DataType.Name} but got {valueType.Name}");
                }
            }
        }
        
        return errors;
    }

    /// <inheritdoc />
    public IArrayRow Build()
    {
        return Build(validateRequired: false);
    }

    /// <inheritdoc />
    public IArrayRow Build(bool validateRequired)
    {
        if (validateRequired)
        {
            var errors = Validate();
            if (errors.Count > 0)
            {
                throw new InvalidOperationException($"Builder validation failed: {string.Join(", ", errors)}");
            }
        }
        
        // Fill unset values with defaults
        var finalValues = new object?[_values.Length];
        for (int i = 0; i < _values.Length; i++)
        {
            if (_isSet[i])
            {
                finalValues[i] = _values[i];
            }
            else
            {
                var column = _schema.Columns[i];
                finalValues[i] = column.IsNullable ? null : GetDefaultValue(column.DataType);
            }
        }
        
        return _factory.Create(_schema, finalValues);
    }

    /// <summary>
    /// Checks if a value type is compatible with the expected column type.
    /// </summary>
    /// <param name="valueType">The actual type of the value</param>
    /// <param name="expectedType">The expected column type</param>
    /// <returns>True if compatible</returns>
    private static bool IsTypeCompatible(Type valueType, Type expectedType)
    {
        // Direct match
        if (valueType == expectedType)
            return true;
        
        // Check if value type can be assigned to expected type
        if (expectedType.IsAssignableFrom(valueType))
            return true;
        
        // Handle nullable types
        var underlyingExpectedType = Nullable.GetUnderlyingType(expectedType);
        if (underlyingExpectedType != null)
        {
            return IsTypeCompatible(valueType, underlyingExpectedType);
        }
        
        // Handle numeric conversions
        if (IsNumericType(valueType) && IsNumericType(expectedType))
        {
            return true;
        }
        
        return false;
    }

    /// <summary>
    /// Checks if a type is numeric.
    /// </summary>
    /// <param name="type">Type to check</param>
    /// <returns>True if numeric</returns>
    private static bool IsNumericType(Type type)
    {
        return type == typeof(byte) || type == typeof(sbyte) ||
               type == typeof(short) || type == typeof(ushort) ||
               type == typeof(int) || type == typeof(uint) ||
               type == typeof(long) || type == typeof(ulong) ||
               type == typeof(float) || type == typeof(double) ||
               type == typeof(decimal);
    }

    /// <summary>
    /// Gets the default value for a given type.
    /// </summary>
    /// <param name="type">The type to get default value for</param>
    /// <returns>The default value</returns>
    private static object? GetDefaultValue(Type type)
    {
        if (type.IsValueType)
        {
            return Activator.CreateInstance(type);
        }
        
        return null;
    }
}