using FlowEngine.Abstractions.Data;
using FlowEngine.Abstractions.Plugins;
using FlowEngine.Abstractions.Factories;
using FlowEngine.Abstractions.Services;
using Microsoft.Extensions.Logging;
using CsvHelper;
using CsvHelper.Configuration;
using System.Globalization;
using System.Text;

namespace DelimitedSource;

/// <summary>
/// Service implementation for DelimitedSource plugin with CsvHelper integration.
/// </summary>
public sealed class DelimitedSourceService : IPluginService
{
    private readonly DelimitedSourcePlugin _plugin;
    private readonly ILogger _logger;
    private readonly ISchemaFactory? _schemaFactory;
    private readonly IArrayRowFactory? _arrayRowFactory;
    private readonly IChunkFactory? _chunkFactory;
    private readonly IDatasetFactory? _datasetFactory;
    private readonly IDataTypeService? _dataTypeService;
    private readonly IMemoryManager? _memoryManager;
    private readonly IChannelTelemetry? _channelTelemetry;

    private long _processedRows;
    private long _processedChunks;
    private int _errorCount;
    private DateTimeOffset _startTime = DateTimeOffset.UtcNow;
    private TimeSpan _totalProcessingTime;

    public DelimitedSourceService(
        DelimitedSourcePlugin plugin,
        ILogger logger,
        ISchemaFactory? schemaFactory = null,
        IArrayRowFactory? arrayRowFactory = null,
        IChunkFactory? chunkFactory = null,
        IDatasetFactory? datasetFactory = null,
        IDataTypeService? dataTypeService = null,
        IMemoryManager? memoryManager = null,
        IChannelTelemetry? channelTelemetry = null)
    {
        _plugin = plugin ?? throw new ArgumentNullException(nameof(plugin));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _schemaFactory = schemaFactory;
        _arrayRowFactory = arrayRowFactory;
        _chunkFactory = chunkFactory;
        _datasetFactory = datasetFactory;
        _dataTypeService = dataTypeService;
        _memoryManager = memoryManager;
        _channelTelemetry = channelTelemetry;
    }

    public string Id => _plugin.Id + "-service";
    public IPluginConfiguration Configuration => _plugin.Configuration;
    
    public ServiceMetrics Metrics => new()
    {
        TotalChunks = _processedChunks,
        TotalRows = _processedRows,
        TotalProcessingTime = _totalProcessingTime,
        CurrentMemoryUsage = GC.GetTotalMemory(false),
        PeakMemoryUsage = GC.GetTotalMemory(false),
        ErrorCount = _errorCount,
        WarningCount = 0,
        Timestamp = DateTimeOffset.UtcNow
    };

    public async Task InitializeAsync(IPluginConfiguration configuration, CancellationToken cancellationToken = default)
    {
        // Service initialization happens through plugin initialization
        await Task.CompletedTask;
    }

    public async Task<IChunk> ProcessChunkAsync(IChunk input, CancellationToken cancellationToken = default)
    {
        // Source plugins generate data rather than process existing chunks
        throw new NotSupportedException("DelimitedSource generates data and doesn't process existing chunks");
    }

    public async Task<IArrayRow> ProcessRowAsync(IArrayRow input, CancellationToken cancellationToken = default)
    {
        // Source plugins generate data rather than process existing rows
        throw new NotSupportedException("DelimitedSource generates data and doesn't process existing rows");
    }

    public SchemaCompatibilityResult ValidateInputSchema(ISchema inputSchema)
    {
        // Source plugins don't have input schema requirements
        return SchemaCompatibilityResult.Success();
    }

    public ISchema GetOutputSchema(ISchema inputSchema)
    {
        if (Configuration is not DelimitedSourceConfiguration config)
            throw new InvalidOperationException("Service not properly configured");

        return config.OutputSchema ?? throw new InvalidOperationException("Output schema not available - use InferSchema or provide explicit schema");
    }

    public async Task<IEnumerable<HealthCheck>> HealthCheckAsync(CancellationToken cancellationToken = default)
    {
        return await _plugin.HealthCheckAsync(cancellationToken);
    }

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        return _plugin.StartAsync(cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken = default)
    {
        return _plugin.StopAsync(cancellationToken);
    }

    /// <summary>
    /// Reads the configured delimited file and returns a dataset.
    /// This is the main method for source plugins to generate data.
    /// NOTE: Full implementation requires factories from Core services.
    /// </summary>
    public async Task<IDataset> ReadFileAsync(CancellationToken cancellationToken = default)
    {
        if (Configuration is not DelimitedSourceConfiguration config)
            throw new InvalidOperationException("Service not properly configured");

        _logger.LogInformation("ReadFileAsync: Starting to read delimited file: {FilePath}", config.FilePath);
        
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        
        try
        {
            // For initial implementation, require explicit schema
            _logger.LogDebug("ReadFileAsync: Validating output schema");
            var schema = config.OutputSchema ?? throw new InvalidOperationException("OutputSchema must be provided for this implementation");
            
            // Add timeout wrapper for CSV reading
            _logger.LogDebug("ReadFileAsync: Starting CSV row reading with timeout");
            var rows = await ReadCsvRowsWithTimeoutAsync(config, schema, cancellationToken);
            
            stopwatch.Stop();
            _totalProcessingTime = stopwatch.Elapsed;
            
            _logger.LogInformation("Completed reading {RowCount} rows from {FilePath} in {Duration}ms", 
                rows.Count, config.FilePath, stopwatch.ElapsedMilliseconds);
                
            // Record telemetry for data channel monitoring
            var channelName = $"delimited_source_{Path.GetFileName(config.FilePath)}";
            _channelTelemetry?.RecordRead(channelName, rows.Count, stopwatch.Elapsed);
            
            // Create dataset using factory services
            _logger.LogDebug("ReadFileAsync: Validating factory services");
            if (_datasetFactory == null)
                throw new InvalidOperationException("DatasetFactory is required but was not provided. Ensure plugin is created with proper dependency injection.");
            
            if (_arrayRowFactory == null)
                throw new InvalidOperationException("ArrayRowFactory is required but was not provided. Ensure plugin is created with proper dependency injection.");
            
            if (_chunkFactory == null)
                throw new InvalidOperationException("ChunkFactory is required but was not provided. Ensure plugin is created with proper dependency injection.");

            // Optimized: Process rows directly into chunks without intermediate collections
            _logger.LogDebug("ReadFileAsync: Creating chunks from {RowCount} rows with chunk size {ChunkSize}", rows.Count, config.ChunkSize);
            var chunks = new List<IChunk>();
            var chunkRows = new List<IArrayRow>(config.ChunkSize);
            
            _logger.LogDebug("ReadFileAsync: Processing {RowCount} rows into chunks", rows.Count);
            var processedRowCount = 0;
            
            foreach (var csvRow in rows)
            {
                // Optimized: Pre-allocate array and avoid LINQ Cast operation
                var rowData = new object[csvRow.Length];
                for (int j = 0; j < csvRow.Length; j++)
                {
                    rowData[j] = csvRow[j];
                }
                
                var arrayRow = _arrayRowFactory.CreateRow(schema, rowData);
                chunkRows.Add(arrayRow);
                processedRowCount++;
                
                // Create chunk when we reach the target size
                if (chunkRows.Count >= config.ChunkSize)
                {
                    _logger.LogDebug("ReadFileAsync: Creating chunk {ChunkNumber} with {RowCount} rows", chunks.Count + 1, chunkRows.Count);
                    var chunk = _chunkFactory.CreateChunk(schema, chunkRows);
                    chunks.Add(chunk);
                    
                    // Record telemetry for each chunk written to the data flow
                    _channelTelemetry?.RecordWrite(channelName, chunk.Rows.Length, chunks.Count);
                    _processedChunks++;
                    
                    // Reset for next chunk - reuse the list for better performance
                    chunkRows.Clear();
                }
                
                // Log progress every 1000 rows during chunk processing
                if (processedRowCount % 1000 == 0)
                {
                    _logger.LogDebug("ReadFileAsync: Processed {RowCount} rows into chunks", processedRowCount);
                }
            }
            
            // Handle remaining rows in final chunk
            if (chunkRows.Count > 0)
            {
                _logger.LogDebug("ReadFileAsync: Creating final chunk with {RowCount} rows", chunkRows.Count);
                var finalChunk = _chunkFactory.CreateChunk(schema, chunkRows);
                chunks.Add(finalChunk);
                
                _channelTelemetry?.RecordWrite(channelName, finalChunk.Rows.Length, chunks.Count);
                _processedChunks++;
            }

            // Create dataset from chunks
            _logger.LogDebug("ReadFileAsync: Creating dataset from {ChunkCount} chunks", chunks.Count);
            var dataset = _datasetFactory.CreateDataset(schema, chunks);
            _logger.LogDebug("ReadFileAsync: Dataset creation completed successfully");
            return dataset;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error reading delimited file: {FilePath}", config.FilePath);
            _errorCount++;
            throw;
        }
    }

    private async Task<List<string[]>> ReadCsvRowsWithTimeoutAsync(DelimitedSourceConfiguration config, ISchema schema, CancellationToken cancellationToken)
    {
        // Create a timeout cancellation token (30 seconds timeout)
        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        using var combinedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);
        
        try
        {
            return await ReadCsvRowsAsync(config, schema, combinedCts.Token);
        }
        catch (OperationCanceledException) when (timeoutCts.Token.IsCancellationRequested)
        {
            _logger.LogError("ReadCsvRowsAsync timed out after 30 seconds for file: {FilePath}", config.FilePath);
            throw new TimeoutException($"CSV reading timed out after 30 seconds for file: {config.FilePath}");
        }
    }

    private async Task<List<string[]>> ReadCsvRowsAsync(DelimitedSourceConfiguration config, ISchema schema, CancellationToken cancellationToken)
    {
        var rows = new List<string[]>();
        var encoding = Encoding.GetEncoding(config.Encoding);
        var csvConfig = CreateCsvConfiguration(config);
        
        _logger.LogDebug("ReadCsvRowsAsync: Opening file stream for {FilePath}", config.FilePath);
        using var reader = new StreamReader(config.FilePath, encoding);
        using var csv = new CsvReader(reader, csvConfig);
        
        // Skip header if present
        if (config.HasHeaders)
        {
            _logger.LogDebug("ReadCsvRowsAsync: Reading header row");
            await csv.ReadAsync();
            csv.ReadHeader();
        }
        
        _logger.LogDebug("ReadCsvRowsAsync: Starting main CSV reading loop");
        var rowCount = 0;
        while (await csv.ReadAsync())
        {
            cancellationToken.ThrowIfCancellationRequested();
            
            var values = new string[csv.ColumnCount];
            for (int i = 0; i < csv.ColumnCount; i++)
            {
                values[i] = csv.GetField(i) ?? "";
            }
            rows.Add(values);
            _processedRows++;
            rowCount++;
            
            // Log progress every 1000 rows
            if (rowCount % 1000 == 0)
            {
                _logger.LogDebug("ReadCsvRowsAsync: Read {RowCount} rows so far", rowCount);
            }
        }
        
        _logger.LogDebug("ReadCsvRowsAsync: Completed reading {TotalRows} rows", rowCount);
        return rows;
    }

    private static CsvConfiguration CreateCsvConfiguration(DelimitedSourceConfiguration config)
    {
        return new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            Delimiter = config.Delimiter,
            HasHeaderRecord = config.HasHeaders,
            MissingFieldFound = null, // Don't throw on missing fields
            BadDataFound = null, // Handle bad data gracefully
            TrimOptions = TrimOptions.Trim,
            AllowComments = false,
            BufferSize = 8192
        };
    }

    public void Dispose()
    {
        // No specific disposal needed for service
    }
}