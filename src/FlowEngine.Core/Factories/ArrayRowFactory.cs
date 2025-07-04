using FlowEngine.Abstractions;
using FlowEngine.Abstractions.Data;
using FlowEngine.Abstractions.Factories;
using FlowEngine.Core.Data;
using Microsoft.Extensions.Logging;

namespace FlowEngine.Core.Factories;

/// <summary>
/// Core implementation of IArrayRowFactory.
/// Creates optimized ArrayRow instances with high-performance field access.
/// </summary>
public sealed class ArrayRowFactory : IArrayRowFactory
{
    private readonly ILogger<ArrayRowFactory> _logger;
    private readonly IDataTypeService _dataTypeService;

    /// <summary>
    /// Initializes a new ArrayRowFactory.
    /// </summary>
    /// <param name="logger">Logger for factory operations</param>
    /// <param name="dataTypeService">Data type service for validation and conversion</param>
    public ArrayRowFactory(ILogger<ArrayRowFactory> logger, IDataTypeService dataTypeService)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _dataTypeService = dataTypeService ?? throw new ArgumentNullException(nameof(dataTypeService));
    }

    /// <inheritdoc />
    public IArrayRow CreateRow(ISchema schema, object?[] values)
    {
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentNullException.ThrowIfNull(values);

        if (values.Length != schema.ColumnCount)
        {
            throw new ArgumentException(
                $"Values array length ({values.Length}) must match schema column count ({schema.ColumnCount})", 
                nameof(values));
        }

        // Validate and convert values
        var convertedValues = new object?[values.Length];
        for (int i = 0; i < values.Length; i++)
        {
            var column = schema.Columns[i];
            var value = values[i];

            // Validate and convert the value
            if (value != null && !_dataTypeService.IsValueCompatible(value, column))
            {
                if (_dataTypeService.TryConvertValue(value, column.DataType, out var convertedValue))
                {
                    convertedValues[i] = convertedValue;
                    _logger.LogDebug("Converted value for column '{ColumnName}' from {SourceType} to {TargetType}", 
                        column.Name, value.GetType().Name, column.DataType.Name);
                }
                else
                {
                    throw new ArgumentException(
                        $"Value for column '{column.Name}' at index {i} is not compatible with type {column.DataType.Name}");
                }
            }
            else
            {
                convertedValues[i] = value;
            }
        }

        return new ArrayRow(schema, convertedValues);
    }

    /// <inheritdoc />
    public IArrayRow CreateRow(ISchema schema, Func<int, object?> valueInitializer)
    {
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentNullException.ThrowIfNull(valueInitializer);

        var values = new object?[schema.ColumnCount];
        for (int i = 0; i < schema.ColumnCount; i++)
        {
            values[i] = valueInitializer(i);
        }

        return CreateRow(schema, values);
    }

    /// <inheritdoc />
    public IArrayRow CreateRow(ISchema schema, IReadOnlyDictionary<string, object?> fieldValues)
    {
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentNullException.ThrowIfNull(fieldValues);

        var values = new object?[schema.ColumnCount];
        
        // Map field values to the correct positions
        for (int i = 0; i < schema.ColumnCount; i++)
        {
            var column = schema.Columns[i];
            if (fieldValues.TryGetValue(column.Name, out var value))
            {
                values[i] = value;
            }
            else
            {
                // Use default value if not provided
                values[i] = _dataTypeService.GetDefaultValue(column);
            }
        }

        return CreateRow(schema, values);
    }

    /// <inheritdoc />
    public IArrayRow CreateRowWithDefaults(ISchema schema)
    {
        ArgumentNullException.ThrowIfNull(schema);

        var values = new object?[schema.ColumnCount];
        for (int i = 0; i < schema.ColumnCount; i++)
        {
            values[i] = _dataTypeService.GetDefaultValue(schema.Columns[i]);
        }

        _logger.LogDebug("Created row with default values for schema with {ColumnCount} columns", schema.ColumnCount);
        return new ArrayRow(schema, values);
    }

    /// <inheritdoc />
    public IArrayRow CreateRowWithUpdates(IArrayRow source, IReadOnlyDictionary<string, object?> fieldUpdates)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(fieldUpdates);

        if (fieldUpdates.Count == 0)
        {
            return source; // No updates, return original
        }

        var schema = source.Schema;
        var values = new object?[schema.ColumnCount];
        
        // Copy existing values
        for (int i = 0; i < schema.ColumnCount; i++)
        {
            values[i] = source[i];
        }

        // Apply updates
        foreach (var update in fieldUpdates)
        {
            var fieldIndex = schema.GetIndex(update.Key);
            if (fieldIndex >= 0)
            {
                values[fieldIndex] = update.Value;
            }
            else
            {
                throw new ArgumentException($"Field '{update.Key}' not found in schema");
            }
        }

        _logger.LogDebug("Created row with {UpdateCount} field updates", fieldUpdates.Count);
        return CreateRow(schema, values);
    }

    /// <inheritdoc />
    public IArrayRow ProjectRow(IArrayRow source, ISchema targetSchema)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(targetSchema);

        var values = new object?[targetSchema.ColumnCount];
        var sourceSchema = source.Schema;

        for (int i = 0; i < targetSchema.ColumnCount; i++)
        {
            var targetColumn = targetSchema.Columns[i];
            var sourceIndex = sourceSchema.GetIndex(targetColumn.Name);
            
            if (sourceIndex >= 0)
            {
                values[i] = source[sourceIndex];
            }
            else
            {
                // Field not found in source, use default value
                values[i] = _dataTypeService.GetDefaultValue(targetColumn);
                _logger.LogDebug("Field '{FieldName}' not found in source row, using default value", targetColumn.Name);
            }
        }

        return new ArrayRow(targetSchema, values);
    }

    /// <inheritdoc />
    public ValidationResult ValidateValues(ISchema schema, object?[] values)
    {
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentNullException.ThrowIfNull(values);

        var errors = new List<string>();

        if (values.Length != schema.ColumnCount)
        {
            errors.Add($"Values array length ({values.Length}) must match schema column count ({schema.ColumnCount})");
            return ValidationResult.Failure(errors.ToArray());
        }

        for (int i = 0; i < values.Length; i++)
        {
            var column = schema.Columns[i];
            var value = values[i];
            
            var validation = _dataTypeService.ValidateValue(value, column);
            if (!validation.IsValid)
            {
                errors.AddRange(validation.Errors.Select(e => $"Column '{column.Name}' at index {i}: {e}"));
            }
        }

        return errors.Count == 0 ? ValidationResult.Success() : ValidationResult.Failure(errors.ToArray());
    }

    /// <inheritdoc />
    public IArrayRow[] CreateRows(ISchema schema, object?[][] valueArrays)
    {
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentNullException.ThrowIfNull(valueArrays);

        if (valueArrays.Length == 0)
        {
            return Array.Empty<IArrayRow>();
        }

        var rows = new IArrayRow[valueArrays.Length];
        
        for (int i = 0; i < valueArrays.Length; i++)
        {
            rows[i] = CreateRow(schema, valueArrays[i]);
        }

        _logger.LogDebug("Created batch of {RowCount} rows", valueArrays.Length);
        return rows;
    }
}