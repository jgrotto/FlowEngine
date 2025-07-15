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

namespace DelimitedSink;

/// <summary>
/// Configuration provider for DelimitedSink plugin with comprehensive CSV/TSV output validation.
/// Handles creation and validation of delimited file sink configurations.
/// </summary>
[PluginConfigurationProvider]
public class DelimitedSinkConfigurationProvider : IPluginConfigurationProvider
{
    private readonly ILogger<DelimitedSinkConfigurationProvider>? _logger;
    private readonly ISchemaFactory? _schemaFactory;
    private static readonly string _jsonSchema = LoadEmbeddedJsonSchema();

    /// <summary>
    /// Initializes a new instance of the DelimitedSinkConfigurationProvider.
    /// </summary>
    public DelimitedSinkConfigurationProvider()
    {
        // Note: Logger and SchemaFactory are optional since this provider may be instantiated during discovery
    }

    /// <summary>
    /// Initializes a new instance with logger and schema factory support.
    /// </summary>
    /// <param name="logger">Optional logger for diagnostic information</param>
    /// <param name="schemaFactory">Optional schema factory for creating schemas</param>
    public DelimitedSinkConfigurationProvider(
        ILogger<DelimitedSinkConfigurationProvider>? logger = null,
        ISchemaFactory? schemaFactory = null)
    {
        _logger = logger;
        _schemaFactory = schemaFactory;
    }

    /// <summary>
    /// Gets the plugin type name this provider handles.
    /// </summary>
    public string PluginTypeName => "DelimitedSink.DelimitedSinkPlugin";

    /// <summary>
    /// Gets the configuration type this provider creates.
    /// </summary>
    public Type ConfigurationType => typeof(DelimitedSinkConfiguration);

    /// <summary>
    /// Determines if this provider can handle the specified plugin type.
    /// </summary>
    /// <param name="pluginTypeName">The plugin type name to check</param>
    /// <returns>True if this provider can handle the plugin type</returns>
    public bool CanHandle(string pluginTypeName)
    {
        return pluginTypeName == PluginTypeName ||
               pluginTypeName == "DelimitedSink" ||
               pluginTypeName.EndsWith(".DelimitedSinkPlugin");
    }

    /// <summary>
    /// Creates a DelimitedSink configuration from the plugin definition.
    /// </summary>
    /// <param name="definition">Plugin definition with configuration data</param>
    /// <returns>Strongly-typed delimited sink configuration</returns>
    public async Task<FlowEngine.Abstractions.Plugins.IPluginConfiguration> CreateConfigurationAsync(IPluginDefinition definition)
    {
        _logger?.LogDebug("Creating DelimitedSink configuration for plugin: {PluginName}", definition.Name);

        var config = definition.Configuration ?? new Dictionary<string, object>();

        // Extract main configuration values
        var filePath = GetConfigValue<string>(config, "FilePath") ?? GetConfigValue<string>(config, "filePath") ?? 
            throw new ArgumentException("FilePath is required for DelimitedSink plugin");

        var delimiter = GetConfigValue<string>(config, "Delimiter") ?? GetConfigValue<string>(config, "delimiter") ?? ",";
        var includeHeaders = GetConfigValue<bool?>(config, "IncludeHeaders") ?? GetConfigValue<bool?>(config, "includeHeaders") ?? true;
        var encoding = GetConfigValue<string>(config, "Encoding") ?? GetConfigValue<string>(config, "encoding") ?? "UTF-8";
        var lineEnding = GetConfigValue<string>(config, "LineEnding") ?? GetConfigValue<string>(config, "lineEnding") ?? "System";
        var appendMode = GetConfigValue<bool?>(config, "AppendMode") ?? GetConfigValue<bool?>(config, "appendMode") ?? false;

        // Performance configuration
        var bufferSize = GetConfigValue<int?>(config, "BufferSize") ?? GetConfigValue<int?>(config, "bufferSize") ?? 65536;
        var flushInterval = GetConfigValue<int?>(config, "FlushInterval") ?? GetConfigValue<int?>(config, "flushInterval") ?? 1000;

        // File handling configuration
        var createDirectories = GetConfigValue<bool?>(config, "CreateDirectories") ?? GetConfigValue<bool?>(config, "createDirectories") ?? true;
        var overwriteExisting = GetConfigValue<bool?>(config, "OverwriteExisting") ?? GetConfigValue<bool?>(config, "overwriteExisting") ?? true;

        // Schema and mapping configuration
        ISchema? inputSchema = null;
        var inputSchemaData = GetConfigValue<Dictionary<string, object>>(config, "InputSchema") ?? 
                             GetConfigValue<Dictionary<string, object>>(config, "inputSchema");
        if (inputSchemaData != null)
        {
            inputSchema = CreateInputSchema(inputSchemaData);
        }

        var columnMappings = CreateColumnMappings(config);

        // Create the configuration instance
        var sinkConfig = new DelimitedSinkConfiguration
        {
            Name = definition.Name,
            PluginId = definition.Name,
            FilePath = filePath,
            Delimiter = delimiter,
            IncludeHeaders = includeHeaders,
            Encoding = encoding,
            LineEnding = lineEnding,
            AppendMode = appendMode,
            BufferSize = bufferSize,
            FlushInterval = flushInterval,
            CreateDirectories = createDirectories,
            OverwriteExisting = overwriteExisting,
            InputSchema = inputSchema,
            ColumnMappings = columnMappings
        };

        _logger?.LogInformation("Successfully created DelimitedSink configuration for plugin: {PluginName}", definition.Name);

        await Task.CompletedTask; // Keep async signature for interface compliance
        return sinkConfig;
    }

    /// <summary>
    /// Gets the JSON schema for DelimitedSink configuration validation.
    /// </summary>
    /// <returns>JSON schema string for configuration validation</returns>
    public string GetConfigurationJsonSchema()
    {
        return _jsonSchema;
    }

    /// <summary>
    /// Validates a plugin definition against DelimitedSink requirements.
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
            // Validate file path format and directory
            if (filePath.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
            {
                errors.Add(new FlowEngine.Abstractions.Plugins.ValidationError
                {
                    Code = "INVALID_FILEPATH",
                    Message = "FilePath contains invalid characters",
                    Severity = FlowEngine.Abstractions.Plugins.ValidationSeverity.Error
                });
            }

            var directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(directory) && directory.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
            {
                errors.Add(new FlowEngine.Abstractions.Plugins.ValidationError
                {
                    Code = "INVALID_DIRECTORY_PATH",
                    Message = "Output directory path contains invalid characters",
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

        // Validate line ending
        var lineEnding = GetConfigValue<string>(config, "LineEnding") ?? GetConfigValue<string>(config, "lineEnding");
        if (!string.IsNullOrEmpty(lineEnding) && !IsValidLineEnding(lineEnding))
        {
            errors.Add(new FlowEngine.Abstractions.Plugins.ValidationError
            {
                Code = "INVALID_LINE_ENDING",
                Message = "LineEnding must be one of: System, CRLF, LF, CR",
                Severity = FlowEngine.Abstractions.Plugins.ValidationSeverity.Error
            });
        }

        // Validate buffer size
        var bufferSize = GetConfigValue<int?>(config, "BufferSize") ?? GetConfigValue<int?>(config, "bufferSize");
        if (bufferSize.HasValue && (bufferSize < 1024 || bufferSize > 1048576))
        {
            errors.Add(new FlowEngine.Abstractions.Plugins.ValidationError
            {
                Code = "INVALID_BUFFER_SIZE",
                Message = "BufferSize must be between 1024 and 1048576 bytes",
                Severity = FlowEngine.Abstractions.Plugins.ValidationSeverity.Error
            });
        }

        // Validate flush interval
        var flushInterval = GetConfigValue<int?>(config, "FlushInterval") ?? GetConfigValue<int?>(config, "flushInterval");
        if (flushInterval.HasValue && (flushInterval < 1 || flushInterval > 100000))
        {
            errors.Add(new FlowEngine.Abstractions.Plugins.ValidationError
            {
                Code = "INVALID_FLUSH_INTERVAL",
                Message = "FlushInterval must be between 1 and 100000",
                Severity = FlowEngine.Abstractions.Plugins.ValidationSeverity.Error
            });
        }

        // Validate input schema if provided
        var inputSchemaData = GetConfigValue<Dictionary<string, object>>(config, "InputSchema") ?? 
                             GetConfigValue<Dictionary<string, object>>(config, "inputSchema");
        if (inputSchemaData != null)
        {
            ValidateInputSchemaConfiguration(inputSchemaData, errors);
        }

        // Validate column mappings if provided
        var columnMappingsData = GetConfigValue<List<object>>(config, "ColumnMappings") ?? 
                                GetConfigValue<List<object>>(config, "columnMappings");
        if (columnMappingsData != null)
        {
            ValidateColumnMappingsConfiguration(columnMappingsData, errors);
        }

        return errors.Count == 0
            ? FlowEngine.Abstractions.Plugins.ValidationResult.Success()
            : FlowEngine.Abstractions.Plugins.ValidationResult.Failure(errors.ToArray());
    }

    /// <summary>
    /// Gets input schema requirements for delimited sinks.
    /// DelimitedSink can accept any input schema (flexible sink).
    /// </summary>
    /// <returns>Null indicating this plugin accepts any input schema</returns>
    public ISchemaRequirement? GetInputSchemaRequirement()
    {
        // Sink plugins are generally flexible and can accept various input schemas
        return null;
    }

    /// <summary>
    /// Determines the output schema from the configuration.
    /// </summary>
    /// <param name="configuration">Plugin configuration</param>
    /// <returns>Output schema based on configuration</returns>
    public ISchema GetOutputSchema(FlowEngine.Abstractions.Plugins.IPluginConfiguration configuration)
    {
        if (configuration is not DelimitedSinkConfiguration sinkConfig)
            throw new ArgumentException("Configuration must be DelimitedSinkConfiguration", nameof(configuration));

        // Sink plugins don't produce output data - they write to files
        // Return null to indicate no output schema
        return null!; // This may need to be adjusted based on interface requirements
    }

    /// <summary>
    /// Gets plugin metadata for discovery and documentation.
    /// </summary>
    /// <returns>Comprehensive metadata about the DelimitedSink plugin</returns>
    public PluginMetadata GetPluginMetadata()
    {
        return new PluginMetadata
        {
            Name = "Delimited Sink",
            Description = "Write data to delimited files (CSV, TSV, etc.) with configurable formatting options. " +
                         "Supports column mapping, custom delimiters, and performance tuning.",
            Version = "1.0.0",
            Author = "FlowEngine Team",
            Category = PluginCategory.Sink,
            Tags = new[] { "csv", "tsv", "delimited", "file", "sink", "output", "export" },
            DocumentationUrl = new Uri("https://flowengine.dev/docs/plugins/delimited-sink"),
            MinimumEngineVersion = "1.0.0",
            License = "MIT",
            IsStable = true,
            Capabilities = new[] { "file-writing", "column-mapping", "buffered-output", "format-conversion" }
        };
    }

    /// <summary>
    /// Creates input schema from configuration data.
    /// </summary>
    private ISchema CreateInputSchema(IReadOnlyDictionary<string, object> schemaData)
    {
        var columnsData = GetConfigValue<List<object>>(schemaData, "Columns") ?? 
                         GetConfigValue<List<object>>(schemaData, "columns") ?? 
                         new List<object>();

        var columns = columnsData.Select(columnObj =>
        {
            var columnDict = columnObj as Dictionary<string, object> ?? 
                           throw new ArgumentException("InputSchema.Columns must contain valid column objects");

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
                IsNullable = true, // Input data can typically have null values
                Description = GetConfigValue<string>(columnDict, "Description") ?? GetConfigValue<string>(columnDict, "description")
            };
        }).ToArray();

        // Create schema using factory if available, otherwise create a basic implementation
        return _schemaFactory?.CreateSchema(columns) ?? new BasicSchema(columns);
    }

    /// <summary>
    /// Creates column mappings from configuration data.
    /// </summary>
    private IReadOnlyList<ColumnMapping>? CreateColumnMappings(IReadOnlyDictionary<string, object> config)
    {
        var mappingsData = GetConfigValue<List<object>>(config, "ColumnMappings") ?? 
                          GetConfigValue<List<object>>(config, "columnMappings");

        if (mappingsData == null || mappingsData.Count == 0)
            return null;

        var mappings = mappingsData.Select(mappingObj =>
        {
            var mappingDict = mappingObj as Dictionary<string, object> ?? 
                            throw new ArgumentException("ColumnMappings must contain valid mapping objects");

            var sourceColumn = GetConfigValue<string>(mappingDict, "SourceColumn") ?? GetConfigValue<string>(mappingDict, "sourceColumn") ?? 
                              throw new ArgumentException("SourceColumn is required in column mapping");
            var outputColumn = GetConfigValue<string>(mappingDict, "OutputColumn") ?? GetConfigValue<string>(mappingDict, "outputColumn") ?? 
                              throw new ArgumentException("OutputColumn is required in column mapping");
            var format = GetConfigValue<string>(mappingDict, "Format") ?? GetConfigValue<string>(mappingDict, "format");

            return new ColumnMapping
            {
                SourceColumn = sourceColumn,
                OutputColumn = outputColumn,
                Format = format
            };
        }).ToList();

        return mappings;
    }

    /// <summary>
    /// Validates input schema configuration and adds errors to the list.
    /// </summary>
    private void ValidateInputSchemaConfiguration(IReadOnlyDictionary<string, object> schemaData, List<FlowEngine.Abstractions.Plugins.ValidationError> errors)
    {
        var columnsData = GetConfigValue<List<object>>(schemaData, "Columns") ?? 
                         GetConfigValue<List<object>>(schemaData, "columns");

        if (columnsData == null || columnsData.Count == 0)
        {
            errors.Add(new FlowEngine.Abstractions.Plugins.ValidationError
            {
                Code = "INPUT_COLUMNS_REQUIRED",
                Message = "InputSchema.Columns is required and must contain at least one column",
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
                    Message = "Each column in InputSchema.Columns must be a valid object",
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
    /// Validates column mappings configuration and adds errors to the list.
    /// </summary>
    private void ValidateColumnMappingsConfiguration(IEnumerable<object> mappingsData, List<FlowEngine.Abstractions.Plugins.ValidationError> errors)
    {
        var outputColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var mappingObj in mappingsData)
        {
            if (mappingObj is not Dictionary<string, object> mappingDict)
            {
                errors.Add(new FlowEngine.Abstractions.Plugins.ValidationError
                {
                    Code = "INVALID_MAPPING_DEFINITION",
                    Message = "Each column mapping must be a valid object",
                    Severity = FlowEngine.Abstractions.Plugins.ValidationSeverity.Error
                });
                continue;
            }

            var sourceColumn = GetConfigValue<string>(mappingDict, "SourceColumn") ?? GetConfigValue<string>(mappingDict, "sourceColumn");
            if (string.IsNullOrWhiteSpace(sourceColumn))
            {
                errors.Add(new FlowEngine.Abstractions.Plugins.ValidationError
                {
                    Code = "SOURCE_COLUMN_REQUIRED",
                    Message = "SourceColumn is required in column mapping",
                    Severity = FlowEngine.Abstractions.Plugins.ValidationSeverity.Error
                });
            }

            var outputColumn = GetConfigValue<string>(mappingDict, "OutputColumn") ?? GetConfigValue<string>(mappingDict, "outputColumn");
            if (string.IsNullOrWhiteSpace(outputColumn))
            {
                errors.Add(new FlowEngine.Abstractions.Plugins.ValidationError
                {
                    Code = "OUTPUT_COLUMN_REQUIRED",
                    Message = "OutputColumn is required in column mapping",
                    Severity = FlowEngine.Abstractions.Plugins.ValidationSeverity.Error
                });
            }
            else if (!outputColumns.Add(outputColumn))
            {
                errors.Add(new FlowEngine.Abstractions.Plugins.ValidationError
                {
                    Code = "DUPLICATE_OUTPUT_COLUMN",
                    Message = $"Output column '{outputColumn}' is mapped multiple times",
                    Severity = FlowEngine.Abstractions.Plugins.ValidationSeverity.Warning
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
        var resourceName = "DelimitedSink.Schemas.DelimitedSinkConfiguration.json";
        
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

    /// <summary>
    /// Validates if a line ending string is supported.
    /// </summary>
    private static bool IsValidLineEnding(string lineEnding)
    {
        var validLineEndings = new[] { "System", "CRLF", "LF", "CR" };
        return validLineEndings.Contains(lineEnding, StringComparer.OrdinalIgnoreCase);
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