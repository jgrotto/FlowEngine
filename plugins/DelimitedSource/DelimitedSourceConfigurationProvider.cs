using FlowEngine.Abstractions.Configuration;
using FlowEngine.Abstractions.Data;
using FlowEngine.Abstractions.Factories;
using FlowEngine.Abstractions.Plugins;
using FlowEngine.Core.Configuration;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

namespace DelimitedSource;

/// <summary>
/// Configuration provider for DelimitedSource plugin with comprehensive CSV/TSV file validation.
/// Handles creation and validation of delimited file source configurations.
/// </summary>
[PluginConfigurationProvider]
public class DelimitedSourceConfigurationProvider : IPluginConfigurationProvider
{
    private readonly ILogger<DelimitedSourceConfigurationProvider>? _logger;
    private readonly ISchemaFactory? _schemaFactory;
    private static readonly string _jsonSchema = LoadEmbeddedJsonSchema();

    /// <summary>
    /// Initializes a new instance of the DelimitedSourceConfigurationProvider.
    /// </summary>
    public DelimitedSourceConfigurationProvider()
    {
        // Note: Logger and SchemaFactory are optional since this provider may be instantiated during discovery
    }

    /// <summary>
    /// Initializes a new instance with logger and schema factory support.
    /// </summary>
    /// <param name="logger">Optional logger for diagnostic information</param>
    /// <param name="schemaFactory">Optional schema factory for creating output schemas</param>
    public DelimitedSourceConfigurationProvider(
        ILogger<DelimitedSourceConfigurationProvider>? logger = null,
        ISchemaFactory? schemaFactory = null)
    {
        _logger = logger;
        _schemaFactory = schemaFactory;
    }

    /// <summary>
    /// Gets the plugin type name this provider handles.
    /// </summary>
    public string PluginTypeName => "DelimitedSource.DelimitedSourcePlugin";

    /// <summary>
    /// Gets the configuration type this provider creates.
    /// </summary>
    public Type ConfigurationType => typeof(DelimitedSourceConfiguration);

    /// <summary>
    /// Determines if this provider can handle the specified plugin type.
    /// </summary>
    /// <param name="pluginTypeName">The plugin type name to check</param>
    /// <returns>True if this provider can handle the plugin type</returns>
    public bool CanHandle(string pluginTypeName)
    {
        return pluginTypeName == PluginTypeName ||
               pluginTypeName == "DelimitedSource" ||
               pluginTypeName.EndsWith(".DelimitedSourcePlugin");
    }

    /// <summary>
    /// Creates a DelimitedSource configuration from the plugin definition.
    /// </summary>
    /// <param name="definition">Plugin definition with configuration data</param>
    /// <returns>Strongly-typed delimited source configuration</returns>
    public async Task<FlowEngine.Abstractions.Plugins.IPluginConfiguration> CreateConfigurationAsync(IPluginDefinition definition)
    {
        _logger?.LogDebug("Creating DelimitedSource configuration for plugin: {PluginName}", definition.Name);

        var config = definition.Configuration ?? new Dictionary<string, object>();

        // Extract main configuration values
        var filePath = GetConfigValue<string>(config, "FilePath") ?? GetConfigValue<string>(config, "filePath") ?? 
            throw new ArgumentException("FilePath is required for DelimitedSource plugin");

        var delimiter = GetConfigValue<string>(config, "Delimiter") ?? GetConfigValue<string>(config, "delimiter") ?? ",";
        var hasHeaders = GetConfigValue<bool?>(config, "HasHeaders") ?? GetConfigValue<bool?>(config, "hasHeaders") ?? true;
        var encoding = GetConfigValue<string>(config, "Encoding") ?? GetConfigValue<string>(config, "encoding") ?? "UTF-8";
        var chunkSize = GetConfigValue<int?>(config, "ChunkSize") ?? GetConfigValue<int?>(config, "chunkSize") ?? 5000;

        // Schema configuration
        var inferSchema = GetConfigValue<bool?>(config, "InferSchema") ?? GetConfigValue<bool?>(config, "inferSchema") ?? false;
        var inferenceSampleRows = GetConfigValue<int?>(config, "InferenceSampleRows") ?? GetConfigValue<int?>(config, "inferenceSampleRows") ?? 1000;

        // Error handling configuration
        var skipMalformedRows = GetConfigValue<bool?>(config, "SkipMalformedRows") ?? GetConfigValue<bool?>(config, "skipMalformedRows") ?? false;
        var maxErrors = GetConfigValue<int?>(config, "MaxErrors") ?? GetConfigValue<int?>(config, "maxErrors") ?? 100;

        // Create output schema if provided
        ISchema? outputSchema = null;
        if (!inferSchema)
        {
            outputSchema = CreateOutputSchema(config);
        }

        // Create the configuration instance
        var sourceConfig = new DelimitedSourceConfiguration
        {
            Name = definition.Name,
            PluginId = definition.Name,
            FilePath = filePath,
            Delimiter = delimiter,
            HasHeaders = hasHeaders,
            Encoding = encoding,
            ChunkSize = chunkSize,
            OutputSchema = outputSchema,
            InferSchema = inferSchema,
            InferenceSampleRows = inferenceSampleRows,
            SkipMalformedRows = skipMalformedRows,
            MaxErrors = maxErrors
        };

        _logger?.LogInformation("Successfully created DelimitedSource configuration for plugin: {PluginName}", definition.Name);

        await Task.CompletedTask; // Keep async signature for interface compliance
        return sourceConfig;
    }

    /// <summary>
    /// Gets the JSON schema for DelimitedSource configuration validation.
    /// </summary>
    /// <returns>JSON schema string for configuration validation</returns>
    public string GetConfigurationJsonSchema()
    {
        return _jsonSchema;
    }

    /// <summary>
    /// Validates a plugin definition against DelimitedSource requirements.
    /// </summary>
    /// <param name="definition">Plugin definition to validate</param>
    /// <returns>Validation result with success status and error messages</returns>
    public FlowEngine.Abstractions.Plugins.ValidationResult ValidateConfiguration(IPluginDefinition definition)
    {
        var errors = new List<FlowEngine.Abstractions.Plugins.ValidationError>();

        var config = definition.Configuration ?? new Dictionary<string, object>();

        // Validate file path
        var filePath = GetConfigValue<string>(config, "FilePath") ?? GetConfigValue<string>(config, "filePath");
        if (string.IsNullOrWhiteSpace(filePath))
        {
            errors.Add(new FlowEngine.Abstractions.Plugins.ValidationError
            {
                Code = "FILEPATH_REQUIRED",
                Message = "FilePath is required and cannot be empty",
                Severity = FlowEngine.Abstractions.Plugins.ValidationSeverity.Error
            });
        }
        else
        {
            // Validate file path format (basic validation - actual file existence would be checked at runtime)
            if (filePath.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
            {
                errors.Add(new FlowEngine.Abstractions.Plugins.ValidationError
                {
                    Code = "INVALID_FILEPATH",
                    Message = "FilePath contains invalid characters",
                    Severity = FlowEngine.Abstractions.Plugins.ValidationSeverity.Error
                });
            }
        }

        // Validate delimiter
        var delimiter = GetConfigValue<string>(config, "Delimiter") ?? GetConfigValue<string>(config, "delimiter");
        if (!string.IsNullOrEmpty(delimiter) && delimiter.Length > 1)
        {
            errors.Add(new FlowEngine.Abstractions.Plugins.ValidationError
            {
                Code = "INVALID_DELIMITER",
                Message = "Delimiter must be a single character",
                Severity = FlowEngine.Abstractions.Plugins.ValidationSeverity.Error
            });
        }

        // Validate encoding
        var encoding = GetConfigValue<string>(config, "Encoding") ?? GetConfigValue<string>(config, "encoding");
        if (!string.IsNullOrEmpty(encoding) && !IsValidEncoding(encoding))
        {
            errors.Add(new FlowEngine.Abstractions.Plugins.ValidationError
            {
                Code = "INVALID_ENCODING",
                Message = "Encoding must be one of: UTF-8, UTF-16, ASCII",
                Severity = FlowEngine.Abstractions.Plugins.ValidationSeverity.Error
            });
        }

        // Validate chunk size
        var chunkSize = GetConfigValue<int?>(config, "ChunkSize") ?? GetConfigValue<int?>(config, "chunkSize");
        if (chunkSize.HasValue && (chunkSize < 100 || chunkSize > 50000))
        {
            errors.Add(new FlowEngine.Abstractions.Plugins.ValidationError
            {
                Code = "INVALID_CHUNK_SIZE",
                Message = "ChunkSize must be between 100 and 50000",
                Severity = FlowEngine.Abstractions.Plugins.ValidationSeverity.Error
            });
        }

        // Always require explicit OutputSchema (InferSchema is deprecated)
        ValidateOutputSchemaConfiguration(config, errors);

        // Validate inference sample rows
        var inferenceSampleRows = GetConfigValue<int?>(config, "InferenceSampleRows") ?? GetConfigValue<int?>(config, "inferenceSampleRows");
        if (inferenceSampleRows.HasValue && (inferenceSampleRows < 10 || inferenceSampleRows > 10000))
        {
            errors.Add(new FlowEngine.Abstractions.Plugins.ValidationError
            {
                Code = "INVALID_INFERENCE_SAMPLE_ROWS",
                Message = "InferenceSampleRows must be between 10 and 10000",
                Severity = FlowEngine.Abstractions.Plugins.ValidationSeverity.Error
            });
        }

        // Validate max errors
        var maxErrors = GetConfigValue<int?>(config, "MaxErrors") ?? GetConfigValue<int?>(config, "maxErrors");
        if (maxErrors.HasValue && (maxErrors < 0 || maxErrors > 10000))
        {
            errors.Add(new FlowEngine.Abstractions.Plugins.ValidationError
            {
                Code = "INVALID_MAX_ERRORS",
                Message = "MaxErrors must be between 0 and 10000",
                Severity = FlowEngine.Abstractions.Plugins.ValidationSeverity.Error
            });
        }

        return errors.Count == 0
            ? FlowEngine.Abstractions.Plugins.ValidationResult.Success()
            : FlowEngine.Abstractions.Plugins.ValidationResult.Failure(errors.ToArray());
    }

    /// <summary>
    /// Gets input schema requirements for delimited sources.
    /// DelimitedSource doesn't consume input (it's a source plugin).
    /// </summary>
    /// <returns>Null indicating this plugin doesn't require input</returns>
    public ISchemaRequirement? GetInputSchemaRequirement()
    {
        // Source plugins don't consume input data
        return null;
    }

    /// <summary>
    /// Determines the output schema from the configuration.
    /// </summary>
    /// <param name="configuration">Plugin configuration</param>
    /// <returns>Output schema based on configuration</returns>
    public ISchema GetOutputSchema(FlowEngine.Abstractions.Plugins.IPluginConfiguration configuration)
    {
        if (configuration is not DelimitedSourceConfiguration sourceConfig)
            throw new ArgumentException("Configuration must be DelimitedSourceConfiguration", nameof(configuration));

        if (sourceConfig.OutputSchema != null)
        {
            return sourceConfig.OutputSchema;
        }

        if (sourceConfig.InferSchema)
        {
            // For inference mode, return a placeholder schema indicating schema will be determined at runtime
            var placeholderColumns = new[]
            {
                new ColumnDefinition
                {
                    Name = "InferredAtRuntime",
                    DataType = typeof(object),
                    Index = 0,
                    IsNullable = true,
                    Description = "Schema will be inferred from file data at runtime"
                }
            };

            return _schemaFactory?.CreateSchema(placeholderColumns) ?? new BasicSchema(placeholderColumns);
        }

        throw new InvalidOperationException("OutputSchema must be provided when InferSchema is false");
    }

    /// <summary>
    /// Gets plugin metadata for discovery and documentation.
    /// </summary>
    /// <returns>Comprehensive metadata about the DelimitedSource plugin</returns>
    public PluginMetadata GetPluginMetadata()
    {
        return new PluginMetadata
        {
            Name = "Delimited Source",
            Description = "Read data from delimited files (CSV, TSV, etc.) with configurable parsing options. " +
                         "Supports schema inference, custom delimiters, and error handling.",
            Version = "1.0.0",
            Author = "FlowEngine Team",
            Category = PluginCategory.Source,
            Tags = new[] { "csv", "tsv", "delimited", "file", "source", "data-ingestion" },
            DocumentationUrl = new Uri("https://flowengine.dev/docs/plugins/delimited-source"),
            MinimumEngineVersion = "1.0.0",
            License = "MIT",
            IsStable = true,
            Capabilities = new[] { "file-reading", "schema-inference", "chunked-processing", "error-recovery" }
        };
    }

    /// <summary>
    /// Creates output schema from configuration data.
    /// </summary>
    private ISchema CreateOutputSchema(IReadOnlyDictionary<string, object> config)
    {
        var outputSchemaData = GetConfigValue<Dictionary<string, object>>(config, "OutputSchema") ?? 
                              GetConfigValue<Dictionary<string, object>>(config, "outputSchema");

        if (outputSchemaData == null)
            throw new ArgumentException("OutputSchema is required when InferSchema is false");

        var columnsData = GetConfigValue<List<object>>(outputSchemaData, "Columns") ?? 
                         GetConfigValue<List<object>>(outputSchemaData, "columns") ?? 
                         new List<object>();

        var columns = columnsData.Select(columnObj =>
        {
            var columnDict = columnObj as Dictionary<string, object> ?? 
                           throw new ArgumentException("OutputSchema.Columns must contain valid column objects");

            var name = GetConfigValue<string>(columnDict, "Name") ?? GetConfigValue<string>(columnDict, "name") ?? 
                       throw new ArgumentException("Column name is required");
            var type = GetConfigValue<string>(columnDict, "Type") ?? GetConfigValue<string>(columnDict, "type") ?? 
                       throw new ArgumentException("Column type is required");
            var index = GetConfigValue<int?>(columnDict, "Index") ?? GetConfigValue<int?>(columnDict, "index");
            if (!index.HasValue)
                throw new ArgumentException("Column index is required");

            return new ColumnDefinition
            {
                Name = name,
                DataType = ConvertStringToType(type),
                Index = index.Value,
                IsNullable = true, // Delimited files typically allow null values
                Description = GetConfigValue<string>(columnDict, "Description") ?? GetConfigValue<string>(columnDict, "description")
            };
        }).ToArray();

        // Create schema using factory if available, otherwise create a basic implementation
        return _schemaFactory?.CreateSchema(columns) ?? new BasicSchema(columns);
    }

    /// <summary>
    /// Validates output schema configuration and adds errors to the list.
    /// </summary>
    private void ValidateOutputSchemaConfiguration(IReadOnlyDictionary<string, object> config, List<FlowEngine.Abstractions.Plugins.ValidationError> errors)
    {
        var outputSchemaData = GetConfigValue<Dictionary<string, object>>(config, "OutputSchema") ?? 
                              GetConfigValue<Dictionary<string, object>>(config, "outputSchema");

        if (outputSchemaData == null)
        {
            errors.Add(new FlowEngine.Abstractions.Plugins.ValidationError
            {
                Code = "OUTPUT_SCHEMA_REQUIRED",
                Message = "OutputSchema configuration is required (InferSchema is deprecated)",
                Severity = FlowEngine.Abstractions.Plugins.ValidationSeverity.Error
            });
            return;
        }

        var columnsData = GetConfigValue<List<object>>(outputSchemaData, "Columns") ?? 
                         GetConfigValue<List<object>>(outputSchemaData, "columns");

        if (columnsData == null || columnsData.Count == 0)
        {
            errors.Add(new FlowEngine.Abstractions.Plugins.ValidationError
            {
                Code = "OUTPUT_COLUMNS_REQUIRED",
                Message = "OutputSchema.Columns is required and must contain at least one column",
                Severity = FlowEngine.Abstractions.Plugins.ValidationSeverity.Error
            });
            return;
        }

        // Validate each column
        var usedIndexes = new HashSet<int>();
        foreach (var columnObj in columnsData)
        {
            if (columnObj is not Dictionary<string, object> columnDict)
            {
                errors.Add(new FlowEngine.Abstractions.Plugins.ValidationError
                {
                    Code = "INVALID_COLUMN_DEFINITION",
                    Message = "Each column in OutputSchema.Columns must be a valid object",
                    Severity = FlowEngine.Abstractions.Plugins.ValidationSeverity.Error
                });
                continue;
            }

            var columnName = GetConfigValue<string>(columnDict, "Name") ?? GetConfigValue<string>(columnDict, "name");
            if (string.IsNullOrWhiteSpace(columnName))
            {
                errors.Add(new FlowEngine.Abstractions.Plugins.ValidationError
                {
                    Code = "COLUMN_NAME_REQUIRED",
                    Message = "Column name is required and cannot be empty",
                    Severity = FlowEngine.Abstractions.Plugins.ValidationSeverity.Error
                });
            }

            var columnType = GetConfigValue<string>(columnDict, "Type") ?? GetConfigValue<string>(columnDict, "type");
            if (string.IsNullOrWhiteSpace(columnType))
            {
                errors.Add(new FlowEngine.Abstractions.Plugins.ValidationError
                {
                    Code = "COLUMN_TYPE_REQUIRED",
                    Message = $"Column type is required for column '{columnName}'",
                    Severity = FlowEngine.Abstractions.Plugins.ValidationSeverity.Error
                });
            }
            else if (!IsValidColumnType(columnType))
            {
                errors.Add(new FlowEngine.Abstractions.Plugins.ValidationError
                {
                    Code = "INVALID_COLUMN_TYPE",
                    Message = $"Invalid column type '{columnType}' for column '{columnName}'. Supported types: string, int32, int64, decimal, double, datetime, bool",
                    Severity = FlowEngine.Abstractions.Plugins.ValidationSeverity.Error
                });
            }

            var columnIndex = GetConfigValue<int?>(columnDict, "Index") ?? GetConfigValue<int?>(columnDict, "index");
            if (!columnIndex.HasValue)
            {
                errors.Add(new FlowEngine.Abstractions.Plugins.ValidationError
                {
                    Code = "COLUMN_INDEX_REQUIRED",
                    Message = $"Column index is required for column '{columnName}'",
                    Severity = FlowEngine.Abstractions.Plugins.ValidationSeverity.Error
                });
            }
            else if (columnIndex < 0)
            {
                errors.Add(new FlowEngine.Abstractions.Plugins.ValidationError
                {
                    Code = "INVALID_COLUMN_INDEX",
                    Message = $"Column index must be >= 0 for column '{columnName}'",
                    Severity = FlowEngine.Abstractions.Plugins.ValidationSeverity.Error
                });
            }
            else if (!usedIndexes.Add(columnIndex.Value))
            {
                errors.Add(new FlowEngine.Abstractions.Plugins.ValidationError
                {
                    Code = "DUPLICATE_COLUMN_INDEX",
                    Message = $"Column index {columnIndex.Value} is used by multiple columns",
                    Severity = FlowEngine.Abstractions.Plugins.ValidationSeverity.Error
                });
            }
        }
    }

    /// <summary>
    /// Loads the embedded JSON schema from the assembly.
    /// </summary>
    private static string LoadEmbeddedJsonSchema()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var resourceName = "DelimitedSource.Schemas.DelimitedSourceConfiguration.json";
        
        using var stream = assembly.GetManifestResourceStream(resourceName);
        if (stream == null)
            throw new InvalidOperationException($"Could not find embedded resource: {resourceName}");
        
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    /// <summary>
    /// Gets a configuration value with type conversion.
    /// </summary>
    private T? GetConfigValue<T>(IReadOnlyDictionary<string, object> config, string key)
    {
        if (!config.TryGetValue(key, out var value))
            return default;

        if (value is T directValue)
            return directValue;

        try
        {
            return (T)Convert.ChangeType(value, typeof(T));
        }
        catch
        {
            return default;
        }
    }

    /// <summary>
    /// Converts string type name to .NET Type.
    /// </summary>
    private static Type ConvertStringToType(string typeName)
    {
        return typeName.ToLowerInvariant() switch
        {
            "string" => typeof(string),
            "int32" => typeof(int),
            "int64" => typeof(long),
            "decimal" => typeof(decimal),
            "double" => typeof(double),
            "datetime" => typeof(DateTime),
            "bool" => typeof(bool),
            _ => typeof(string) // Default fallback
        };
    }

    /// <summary>
    /// Validates if a column type string is supported.
    /// </summary>
    private static bool IsValidColumnType(string typeName)
    {
        var validTypes = new[] { "string", "int32", "int64", "decimal", "double", "datetime", "bool" };
        return validTypes.Contains(typeName.ToLowerInvariant());
    }

    /// <summary>
    /// Validates if an encoding string is supported.
    /// </summary>
    private static bool IsValidEncoding(string encoding)
    {
        var validEncodings = new[] { "UTF-8", "UTF-16", "ASCII" };
        return validEncodings.Contains(encoding, StringComparer.OrdinalIgnoreCase);
    }
}

/// <summary>
/// Basic schema implementation for cases where schema factory is not available.
/// </summary>
internal class BasicSchema : ISchema
{
    private readonly ImmutableArray<ColumnDefinition> _columns;

    /// <summary>
    /// Gets the column definitions for this schema.
    /// </summary>
    public ImmutableArray<ColumnDefinition> Columns => _columns;

    /// <summary>
    /// Gets the number of columns in this schema.
    /// </summary>
    public int ColumnCount => _columns.Length;

    /// <summary>
    /// Gets the schema signature for equality checks.
    /// </summary>
    public string Signature => string.Join("|", _columns.Select(c => $"{c.Name}:{c.DataType.Name}"));

    /// <summary>
    /// Initializes a new BasicSchema with the specified columns.
    /// </summary>
    /// <param name="columns">Column definitions for the schema</param>
    public BasicSchema(IEnumerable<ColumnDefinition> columns)
    {
        _columns = (columns ?? throw new ArgumentNullException(nameof(columns))).ToImmutableArray();
    }

    /// <summary>
    /// Gets the index of a column by name.
    /// </summary>
    /// <param name="columnName">Name of the column</param>
    /// <returns>Column index</returns>
    /// <exception cref="ArgumentException">Thrown when column is not found</exception>
    public int GetIndex(string columnName)
    {
        for (int i = 0; i < _columns.Length; i++)
        {
            if (string.Equals(_columns[i].Name, columnName, StringComparison.OrdinalIgnoreCase))
                return i;
        }
        throw new ArgumentException($"Column '{columnName}' not found in schema", nameof(columnName));
    }

    /// <summary>
    /// Gets a column definition by name.
    /// </summary>
    /// <param name="columnName">Name of the column</param>
    /// <returns>Column definition</returns>
    /// <exception cref="ArgumentException">Thrown when column is not found</exception>
    public ColumnDefinition? GetColumn(string columnName)
    {
        return _columns.FirstOrDefault(c => string.Equals(c.Name, columnName, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Checks if a column exists in the schema.
    /// </summary>
    /// <param name="columnName">Name of the column</param>
    /// <returns>True if column exists</returns>
    public bool HasColumn(string columnName)
    {
        return _columns.Any(c => string.Equals(c.Name, columnName, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Tries to get the index of a field by name.
    /// </summary>
    /// <param name="fieldName">Name of the field to find</param>
    /// <param name="index">Output parameter for the field index</param>
    /// <returns>True if the field was found, false otherwise</returns>
    public bool TryGetIndex(string fieldName, out int index)
    {
        for (int i = 0; i < _columns.Length; i++)
        {
            if (string.Equals(_columns[i].Name, fieldName, StringComparison.OrdinalIgnoreCase))
            {
                index = i;
                return true;
            }
        }
        index = -1;
        return false;
    }

    /// <summary>
    /// Adds columns to create a new schema.
    /// </summary>
    /// <param name="columnsToAdd">Columns to add</param>
    /// <returns>New schema with added columns</returns>
    public ISchema AddColumns(params ColumnDefinition[] columnsToAdd)
    {
        var newColumns = _columns.ToList();
        int nextIndex = _columns.Length;
        
        foreach (var column in columnsToAdd)
        {
            var newColumn = column with { Index = nextIndex++ };
            newColumns.Add(newColumn);
        }
        
        return new BasicSchema(newColumns);
    }

    /// <summary>
    /// Removes columns to create a new schema.
    /// </summary>
    /// <param name="columnNames">Names of columns to remove</param>
    /// <returns>New schema with columns removed</returns>
    public ISchema RemoveColumns(params string[] columnNames)
    {
        var columnsToKeep = _columns.Where(c => !columnNames.Contains(c.Name, StringComparer.OrdinalIgnoreCase)).ToList();
        
        // Reindex the remaining columns
        for (int i = 0; i < columnsToKeep.Count; i++)
        {
            columnsToKeep[i] = columnsToKeep[i] with { Index = i };
        }
        
        return new BasicSchema(columnsToKeep);
    }

    /// <summary>
    /// Validates if this schema is compatible with another schema.
    /// </summary>
    /// <param name="other">Schema to compare with</param>
    /// <returns>True if schemas are compatible</returns>
    public bool IsCompatibleWith(ISchema other)
    {
        if (other == null) return false;

        var thisColumns = _columns.ToDictionary(c => c.Name, StringComparer.OrdinalIgnoreCase);
        
        foreach (var otherColumn in other.Columns)
        {
            if (!thisColumns.TryGetValue(otherColumn.Name, out var thisColumn))
                continue;

            // Check type compatibility
            if (thisColumn.DataType != otherColumn.DataType)
                return false;
        }

        return true;
    }

    /// <summary>
    /// Determines equality with another schema.
    /// </summary>
    public bool Equals(ISchema? other)
    {
        return other != null && Signature == other.Signature;
    }

    /// <summary>
    /// Determines equality based on column definitions.
    /// </summary>
    public override bool Equals(object? obj)
    {
        return obj is ISchema other && Equals(other);
    }

    /// <summary>
    /// Gets hash code based on signature.
    /// </summary>
    public override int GetHashCode()
    {
        return Signature.GetHashCode();
    }
}
