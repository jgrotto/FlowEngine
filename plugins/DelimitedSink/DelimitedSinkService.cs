using FlowEngine.Abstractions.Data;
using FlowEngine.Abstractions.Plugins;
using FlowEngine.Abstractions.Factories;
using FlowEngine.Abstractions.Services;
using Microsoft.Extensions.Logging;
using CsvHelper;
using CsvHelper.Configuration;
using System.Globalization;
using System.Text;

namespace DelimitedSink;

/// <summary>
/// Service for writing CSV and delimited files using CsvHelper with high performance.
/// </summary>
public sealed class DelimitedSinkService : IPluginService
{
    private readonly DelimitedSinkPlugin _plugin;
    private readonly ILogger _logger;
    private readonly ISchemaFactory? _schemaFactory;
    private readonly IArrayRowFactory? _arrayRowFactory;
    private readonly IChunkFactory? _chunkFactory;
    private readonly IDatasetFactory? _datasetFactory;
    private readonly IDataTypeService? _dataTypeService;
    private readonly IChannelTelemetry? _channelTelemetry;

    private DelimitedSinkConfiguration? _configuration;
    private StreamWriter? _streamWriter;
    private CsvWriter? _csvWriter;
    private bool _headersWritten;
    private long _rowsWritten;
    private long _chunksProcessed;
    private int _errorCount;
    private DateTime _lastFlush = DateTime.UtcNow;
    private TimeSpan _totalProcessingTime;
    private bool _disposed;

    public DelimitedSinkService(
        DelimitedSinkPlugin plugin,
        ILogger logger,
        ISchemaFactory? schemaFactory = null,
        IArrayRowFactory? arrayRowFactory = null,
        IChunkFactory? chunkFactory = null,
        IDatasetFactory? datasetFactory = null,
        IDataTypeService? dataTypeService = null,
        IChannelTelemetry? channelTelemetry = null)
    {
        _plugin = plugin ?? throw new ArgumentNullException(nameof(plugin));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _schemaFactory = schemaFactory;
        _arrayRowFactory = arrayRowFactory;
        _chunkFactory = chunkFactory;
        _datasetFactory = datasetFactory;
        _dataTypeService = dataTypeService;
        _channelTelemetry = channelTelemetry;
    }

    public string Id => _plugin.Id + "-service";
    public IPluginConfiguration Configuration => _plugin.Configuration;

    public ServiceMetrics Metrics => new()
    {
        TotalChunks = _chunksProcessed,
        TotalRows = _rowsWritten,
        TotalProcessingTime = _totalProcessingTime,
        CurrentMemoryUsage = GC.GetTotalMemory(false),
        PeakMemoryUsage = GC.GetTotalMemory(false),
        ErrorCount = _errorCount,
        WarningCount = 0,
        Timestamp = DateTimeOffset.UtcNow
    };

    public Task InitializeAsync(IPluginConfiguration configuration, CancellationToken cancellationToken = default)
    {
        if (configuration is not DelimitedSinkConfiguration sinkConfig)
        {
            throw new ArgumentException("Expected DelimitedSinkConfiguration", nameof(configuration));
        }

        _configuration = sinkConfig;

        // Create output directory if needed
        var directory = Path.GetDirectoryName(Path.GetFullPath(sinkConfig.FilePath));
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            if (sinkConfig.CreateDirectories)
            {
                Directory.CreateDirectory(directory);
                _logger.LogInformation("Created output directory: {Directory}", directory);
            }
            else
            {
                throw new DirectoryNotFoundException($"Output directory '{directory}' does not exist");
            }
        }

        // Initialize stream writer
        var encoding = Encoding.GetEncoding(sinkConfig.Encoding);
        var fileMode = sinkConfig.AppendMode ? FileMode.Append : FileMode.Create;
        var fileStream = new FileStream(sinkConfig.FilePath, fileMode, FileAccess.Write, FileShare.Read, sinkConfig.BufferSize);
        _streamWriter = new StreamWriter(fileStream, encoding, sinkConfig.BufferSize);

        // Configure CSV writer
        var csvConfig = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            Delimiter = sinkConfig.Delimiter,
            HasHeaderRecord = sinkConfig.IncludeHeaders,
            ShouldQuote = args => args.Field.Contains(sinkConfig.Delimiter) || args.Field.Contains('\n') || args.Field.Contains('\r'),
            NewLine = GetNewLineString(sinkConfig.LineEnding)
        };

        _csvWriter = new CsvWriter(_streamWriter, csvConfig);
        _headersWritten = sinkConfig.AppendMode; // Skip headers if appending

        _logger.LogWarning("DEBUG: DelimitedSinkService initialized for file: {FilePath} with FileMode: {FileMode}", sinkConfig.FilePath, sinkConfig.AppendMode ? "Append" : "Create");
        return Task.CompletedTask;
    }

    public async Task<IChunk> ProcessChunkAsync(IChunk input, CancellationToken cancellationToken = default)
    {
        if (_configuration == null || _csvWriter == null)
        {
            throw new InvalidOperationException("Service not initialized");
        }

        var startTime = DateTime.UtcNow;

        try
        {
            // Write headers if needed
            if (!_headersWritten && _configuration.IncludeHeaders)
            {
                await WriteHeadersAsync(input.Schema, cancellationToken);
                _headersWritten = true;
            }

            // Optimized: Write all rows in chunk with minimal flushing
            for (int i = 0; i < input.Rows.Length; i++)
            {
                await WriteRowAsync(input.Rows[i], input.Schema, cancellationToken);
                _rowsWritten++;
            }

            // Optimized: Flush only once per chunk instead of per FlushInterval
            // This reduces I/O operations significantly
            await FlushAsync(cancellationToken);

            _chunksProcessed++;
            var processingTime = DateTime.UtcNow - startTime;
            _totalProcessingTime = _totalProcessingTime.Add(processingTime);

            // Record telemetry for chunk processing
            var channelName = $"delimited_sink_{Path.GetFileName(_configuration.FilePath)}";
            _channelTelemetry?.RecordRead(channelName, input.Rows.Length, processingTime);

            // Sink plugins pass through the input chunk unchanged
            return input;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing chunk in DelimitedSink");
            _errorCount++;
            throw;
        }
    }

    public async Task<IArrayRow> ProcessRowAsync(IArrayRow input, CancellationToken cancellationToken = default)
    {
        if (_configuration == null || _csvWriter == null)
        {
            throw new InvalidOperationException("Service not initialized");
        }

        var startTime = DateTime.UtcNow;

        try
        {
            // Write headers if needed (derive schema from first row)
            if (!_headersWritten && _configuration.IncludeHeaders)
            {
                // For single row processing, we need the schema - assume it's available
                var schema = _configuration.InputSchema ?? throw new InvalidOperationException("Schema required for header writing");
                await WriteHeadersAsync(schema, cancellationToken);
                _headersWritten = true;
            }

            await WriteRowAsync(input, _configuration.InputSchema, cancellationToken);
            _rowsWritten++;

            // Flush periodically
            if (_rowsWritten % _configuration.FlushInterval == 0)
            {
                await FlushAsync(cancellationToken);
            }

            var processingTime = DateTime.UtcNow - startTime;
            _totalProcessingTime = _totalProcessingTime.Add(processingTime);

            // Sink plugins pass through the input row unchanged
            return input;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing row in DelimitedSink");
            _errorCount++;
            throw;
        }
    }

    private async Task WriteHeadersAsync(ISchema schema, CancellationToken cancellationToken)
    {
        if (_csvWriter == null)
        {
            return;
        }

        var headers = GetOutputColumnNames(schema);
        foreach (var header in headers)
        {
            _csvWriter.WriteField(header);
        }
        await _csvWriter.NextRecordAsync();
    }

    public SchemaCompatibilityResult ValidateInputSchema(ISchema inputSchema)
    {
        if (inputSchema == null)
        {
            return SchemaCompatibilityResult.Failure(new CompatibilityIssue
            {
                Type = "MissingInputSchema",
                Severity = "Error",
                Description = "DelimitedSink requires an input schema to determine column structure",
                Resolution = "Provide an input schema or ensure the previous plugin in the pipeline produces a schema"
            });
        }

        var issues = new List<CompatibilityIssue>();

        // Check that all column types are CSV-compatible
        var csvCompatibleTypes = new HashSet<Type>
        {
            typeof(string), typeof(int), typeof(long), typeof(decimal),
            typeof(double), typeof(bool), typeof(DateTime), typeof(DateTimeOffset)
        };

        // Types that are completely incompatible with CSV output
        var incompatibleTypes = new HashSet<Type>
        {
            typeof(object), typeof(byte[]), typeof(Stream), typeof(IntPtr)
        };

        foreach (var column in inputSchema.Columns)
        {
            if (!csvCompatibleTypes.Contains(column.DataType))
            {
                // Check if this is a completely incompatible type
                var isIncompatible = incompatibleTypes.Contains(column.DataType) ||
                                   column.DataType.IsArray && column.DataType != typeof(string);

                issues.Add(new CompatibilityIssue
                {
                    Type = isIncompatible ? "IncompatibleDataType" : "UnsupportedDataType",
                    Severity = isIncompatible ? "Error" : "Warning",
                    Description = isIncompatible
                        ? $"Column '{column.Name}' has type '{column.DataType.Name}' which cannot be serialized to CSV format"
                        : $"Column '{column.Name}' has type '{column.DataType.Name}' which may not format well in CSV",
                    Resolution = isIncompatible
                        ? "Remove this column or convert it to a supported type before writing to CSV"
                        : "Consider converting complex types to string representation before writing to CSV"
                });
            }
        }

        return issues.Any(i => i.Severity == "Error")
            ? SchemaCompatibilityResult.Failure(issues.ToArray())
            : SchemaCompatibilityResult.Success();
    }

    public ISchema GetOutputSchema(ISchema inputSchema)
    {
        // Sink plugins pass through the input schema unchanged
        return inputSchema;
    }

    public async Task<IEnumerable<HealthCheck>> HealthCheckAsync(CancellationToken cancellationToken = default)
    {
        return await _plugin.HealthCheckAsync(cancellationToken);
    }

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        return _plugin.StartAsync(cancellationToken);
    }

    private async Task WriteRowAsync(IArrayRow row, ISchema? schema, CancellationToken cancellationToken)
    {
        if (_csvWriter == null)
        {
            return;
        }

        var outputColumns = GetOutputColumnMappings(schema);

        foreach (var (sourceIndex, format) in outputColumns)
        {
            var value = row[sourceIndex];
            var formattedValue = FormatValue(value, format);
            _csvWriter.WriteField(formattedValue);
        }

        await _csvWriter.NextRecordAsync();
    }

    private string[] GetOutputColumnNames(ISchema schema)
    {
        if (_configuration?.ColumnMappings != null && _configuration.ColumnMappings.Count > 0)
        {
            return _configuration.ColumnMappings.Select(m => m.OutputColumn).ToArray();
        }

        return schema.Columns.Select(c => c.Name).ToArray();
    }

    private (int sourceIndex, string? format)[] GetOutputColumnMappings(ISchema? schema)
    {
        if (schema == null)
        {
            throw new InvalidOperationException("Schema is required for column mapping");
        }

        if (_configuration?.ColumnMappings != null && _configuration.ColumnMappings.Count > 0)
        {
            return _configuration.ColumnMappings
                .Select(m => (schema.GetIndex(m.SourceColumn), m.Format))
                .ToArray();
        }

        return schema.Columns.Select(c => (c.Index, (string?)null)).ToArray();
    }

    private string FormatValue(object? value, string? format)
    {
        if (value == null)
        {
            return string.Empty;
        }

        if (!string.IsNullOrEmpty(format))
        {
            try
            {
                return value switch
                {
                    DateTime dt => dt.ToString(format),
                    DateTimeOffset dto => dto.ToString(format),
                    decimal d => d.ToString(format),
                    double db => db.ToString(format),
                    float f => f.ToString(format),
                    int i => i.ToString(format),
                    long l => l.ToString(format),
                    _ => value.ToString() ?? string.Empty
                };
            }
            catch
            {
                // Fall back to default formatting if custom format fails
            }
        }

        return value.ToString() ?? string.Empty;
    }

    private bool ShouldFlush()
    {
        if (_configuration == null)
        {
            return false;
        }

        var timeSinceLastFlush = DateTime.UtcNow - _lastFlush;
        return timeSinceLastFlush.TotalMilliseconds >= _configuration.FlushInterval;
    }

    public async Task FlushAsync(CancellationToken cancellationToken = default)
    {
        if (_csvWriter != null && _streamWriter != null)
        {
            await _csvWriter.FlushAsync();
            await _streamWriter.FlushAsync();
            _lastFlush = DateTime.UtcNow;

            // Record buffer utilization telemetry
            if (_configuration != null)
            {
                var channelName = $"delimited_sink_{Path.GetFileName(_configuration.FilePath)}";
                // Estimate buffer usage based on rows written since last flush
                var estimatedUsage = (int)(_rowsWritten % _configuration.FlushInterval);
                _channelTelemetry?.RecordBufferUtilization(channelName, _configuration.BufferSize, estimatedUsage);
            }
        }
    }

    private static string GetNewLineString(string lineEnding)
    {
        return lineEnding switch
        {
            "CRLF" => "\r\n",
            "LF" => "\n",
            "CR" => "\r",
            "System" => Environment.NewLine,
            _ => Environment.NewLine
        };
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        await FlushAsync(cancellationToken);
        _logger.LogInformation("DelimitedSinkService stopped, total rows written: {RowsWritten}", _rowsWritten);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _csvWriter?.Dispose();
        _streamWriter?.Dispose();
        _disposed = true;

        _logger.LogInformation("DelimitedSinkService disposed");
    }
}
