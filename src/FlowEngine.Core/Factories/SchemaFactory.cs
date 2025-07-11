using FlowEngine.Abstractions;
using FlowEngine.Abstractions.Data;
using FlowEngine.Abstractions.Factories;
using FlowEngine.Core.Data;
using Microsoft.Extensions.Logging;

namespace FlowEngine.Core.Factories;

/// <summary>
/// Core implementation of ISchemaFactory.
/// Creates optimized Schema instances with validation and metadata support.
/// </summary>
public sealed class SchemaFactory : ISchemaFactory
{
    private readonly ILogger<SchemaFactory> _logger;

    /// <summary>
    /// Initializes a new SchemaFactory.
    /// </summary>
    /// <param name="logger">Logger for factory operations</param>
    public SchemaFactory(ILogger<SchemaFactory> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public ISchema CreateSchema(ColumnDefinition[] columns)
    {
        ArgumentNullException.ThrowIfNull(columns);

        var validation = ValidateColumns(columns);
        if (!validation.IsValid)
        {
            throw new ArgumentException($"Invalid column definitions: {string.Join(", ", validation.Errors)}");
        }

        _logger.LogDebug("Creating schema with {ColumnCount} columns", columns.Length);
        return Schema.GetOrCreate(columns);
    }

    /// <inheritdoc />
    public ISchema CreateSchema(ColumnDefinition[] columns, IReadOnlyDictionary<string, object>? metadata)
    {
        ArgumentNullException.ThrowIfNull(columns);

        var validation = ValidateColumns(columns);
        if (!validation.IsValid)
        {
            throw new ArgumentException($"Invalid column definitions: {string.Join(", ", validation.Errors)}");
        }

        _logger.LogDebug("Creating schema with {ColumnCount} columns and metadata", columns.Length);
        return Schema.GetOrCreate(columns);
    }

    /// <inheritdoc />
    public ISchema CreateSchema(string name, string version, ColumnDefinition[] columns)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(version);
        ArgumentNullException.ThrowIfNull(columns);

        var validation = ValidateColumns(columns);
        if (!validation.IsValid)
        {
            throw new ArgumentException($"Invalid column definitions: {string.Join(", ", validation.Errors)}");
        }

        _logger.LogDebug("Creating schema '{Name}' v{Version} with {ColumnCount} columns", name, version, columns.Length);

        var metadata = new Dictionary<string, object>
        {
            ["Name"] = name,
            ["Version"] = version
        };

        return Schema.GetOrCreate(columns);
    }

    /// <inheritdoc />
    public ISchema CreateSchema(string name, string version, ColumnDefinition[] columns, IReadOnlyDictionary<string, object>? metadata)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(version);
        ArgumentNullException.ThrowIfNull(columns);

        var validation = ValidateColumns(columns);
        if (!validation.IsValid)
        {
            throw new ArgumentException($"Invalid column definitions: {string.Join(", ", validation.Errors)}");
        }

        _logger.LogDebug("Creating schema '{Name}' v{Version} with {ColumnCount} columns and metadata", name, version, columns.Length);

        var combinedMetadata = new Dictionary<string, object>
        {
            ["Name"] = name,
            ["Version"] = version
        };

        if (metadata != null)
        {
            foreach (var kvp in metadata)
            {
                combinedMetadata[kvp.Key] = kvp.Value;
            }
        }

        return Schema.GetOrCreate(columns);
    }

    /// <inheritdoc />
    public ValidationResult ValidateColumns(ColumnDefinition[] columns)
    {
        ArgumentNullException.ThrowIfNull(columns);

        var errors = new List<string>();

        if (columns.Length == 0)
        {
            errors.Add("Schema must contain at least one column");
            return ValidationResult.Failure(errors.ToArray());
        }

        var columnNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (int i = 0; i < columns.Length; i++)
        {
            var column = columns[i];

            // Validate column name
            if (string.IsNullOrWhiteSpace(column.Name))
            {
                errors.Add($"Column at index {i} has null or empty name");
                continue;
            }

            if (!columnNames.Add(column.Name))
            {
                errors.Add($"Duplicate column name: {column.Name}");
            }

            // Validate data type
            if (column.DataType == null)
            {
                errors.Add($"Column '{column.Name}' has null data type");
            }
            else if (!IsSupportedDataType(column.DataType))
            {
                errors.Add($"Column '{column.Name}' has unsupported data type: {column.DataType}");
            }

            // Validate index
            if (column.Index < 0)
            {
                errors.Add($"Column '{column.Name}' has negative index: {column.Index}");
            }
        }

        // Validate index sequence - all columns must have sequential indexes
        for (int i = 0; i < columns.Length; i++)
        {
            if (columns[i].Index != i)
            {
                errors.Add($"Column '{columns[i].Name}' has index {columns[i].Index} but should be {i} for sequential ordering");
            }
        }


        return errors.Count == 0 ? ValidationResult.Success() : ValidationResult.Failure(errors.ToArray());
    }

    /// <inheritdoc />
    public ISchema ExtendSchema(ISchema baseSchema, ColumnDefinition[] additionalColumns)
    {
        ArgumentNullException.ThrowIfNull(baseSchema);
        ArgumentNullException.ThrowIfNull(additionalColumns);

        if (additionalColumns.Length == 0)
        {
            return baseSchema;
        }

        var validation = ValidateColumns(additionalColumns);
        if (!validation.IsValid)
        {
            throw new ArgumentException($"Invalid additional column definitions: {string.Join(", ", validation.Errors)}");
        }

        // Check for name conflicts
        var baseColumnNames = new HashSet<string>(baseSchema.Columns.Select(c => c.Name), StringComparer.OrdinalIgnoreCase);
        var conflicts = additionalColumns.Where(c => baseColumnNames.Contains(c.Name)).ToArray();
        if (conflicts.Length > 0)
        {
            throw new ArgumentException($"Column name conflicts: {string.Join(", ", conflicts.Select(c => c.Name))}");
        }

        _logger.LogDebug("Extending schema with {AdditionalColumns} columns", additionalColumns.Length);

        // Combine columns with proper indexing
        var allColumns = new ColumnDefinition[baseSchema.ColumnCount + additionalColumns.Length];
        for (int i = 0; i < baseSchema.ColumnCount; i++)
        {
            allColumns[i] = baseSchema.Columns[i];
        }

        for (int i = 0; i < additionalColumns.Length; i++)
        {
            var column = additionalColumns[i];
            allColumns[baseSchema.ColumnCount + i] = column with { Index = baseSchema.ColumnCount + i };
        }

        return Schema.GetOrCreate(allColumns);
    }

    /// <inheritdoc />
    public ISchema ProjectSchema(ISchema baseSchema, string[] columnNames)
    {
        ArgumentNullException.ThrowIfNull(baseSchema);
        ArgumentNullException.ThrowIfNull(columnNames);

        if (columnNames.Length == 0)
        {
            throw new ArgumentException("Must specify at least one column for projection", nameof(columnNames));
        }

        var baseColumnMap = baseSchema.Columns.ToDictionary(c => c.Name, c => c, StringComparer.OrdinalIgnoreCase);
        var projectedColumns = new ColumnDefinition[columnNames.Length];

        for (int i = 0; i < columnNames.Length; i++)
        {
            var columnName = columnNames[i];
            if (!baseColumnMap.TryGetValue(columnName, out var column))
            {
                throw new ArgumentException($"Column '{columnName}' not found in base schema");
            }

            projectedColumns[i] = column with { Index = i };
        }

        _logger.LogDebug("Projecting schema to {ProjectedColumns} columns", columnNames.Length);

        return Schema.GetOrCreate(projectedColumns);
    }

    /// <summary>
    /// Checks if a data type is supported by FlowEngine.
    /// </summary>
    private static bool IsSupportedDataType(Type dataType)
    {
        // Support all basic .NET types
        if (dataType.IsPrimitive || dataType == typeof(string) || dataType == typeof(decimal))
        {
            return true;
        }

        // Support common nullable types
        if (dataType.IsGenericType && dataType.GetGenericTypeDefinition() == typeof(Nullable<>))
        {
            return IsSupportedDataType(Nullable.GetUnderlyingType(dataType)!);
        }

        // Support common value types
        return dataType == typeof(DateTime) ||
               dataType == typeof(DateTimeOffset) ||
               dataType == typeof(TimeSpan) ||
               dataType == typeof(Guid) ||
               dataType == typeof(byte[]);
    }
}
