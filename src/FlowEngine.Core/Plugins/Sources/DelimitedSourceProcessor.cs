using FlowEngine.Abstractions.Data;
using FlowEngine.Abstractions.Plugins;
using Microsoft.Extensions.Logging;
using System.Collections.Immutable;
using System.Diagnostics;

namespace FlowEngine.Core.Plugins.Sources;

/// <summary>
/// Processor for DelimitedSource plugin that orchestrates data flow between framework and business logic.
/// Implements the Processor component of the five-component architecture pattern.
/// </summary>
public sealed class DelimitedSourceProcessor : IPluginProcessor
{
    private readonly ILogger<DelimitedSourceProcessor> _logger;
    private readonly DelimitedSourceConfiguration _configuration;
    private readonly DelimitedSourceService _service;

    /// <summary>
    /// Gets the current processor state.
    /// </summary>
    public ProcessorState State { get; private set; } = ProcessorState.Stopped;

    /// <summary>
    /// Initializes a new instance of the DelimitedSourceProcessor class.
    /// </summary>
    /// <param name="configuration">Plugin configuration</param>
    /// <param name="service">Plugin service instance</param>
    /// <param name="logger">Logger for processor operations</param>
    public DelimitedSourceProcessor(
        DelimitedSourceConfiguration configuration,
        DelimitedSourceService service,
        ILogger<DelimitedSourceProcessor> logger)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _service = service ?? throw new ArgumentNullException(nameof(service));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Gets whether this processor is compatible with the specified service.
    /// </summary>
    /// <param name="service">Service to check compatibility</param>
    /// <returns>True if processor is compatible with the service</returns>
    public bool IsServiceCompatible(IPluginService service)
    {
        return service is DelimitedSourceService;
    }

    /// <summary>
    /// Initializes the processor with channel setup and validation.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token for initialization</param>
    /// <returns>Task representing the initialization operation</returns>
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Initializing DelimitedSource processor for file: {FilePath}", _configuration.FilePath);
        
        State = ProcessorState.Initializing;
        
        // Validate configuration
        if (string.IsNullOrWhiteSpace(_configuration.FilePath))
        {
            throw new InvalidOperationException("FilePath is required for DelimitedSource processor");
        }

        if (!File.Exists(_configuration.FilePath))
        {
            throw new FileNotFoundException($"File not found: {_configuration.FilePath}");
        }

        State = ProcessorState.Ready;
        
        _logger.LogInformation("DelimitedSource processor initialized successfully");
        await Task.CompletedTask;
    }

    /// <summary>
    /// Starts the processor and begins data processing.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token for start operation</param>
    /// <returns>Task representing the start operation</returns>
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Starting DelimitedSource processor");
        
        if (State != ProcessorState.Ready)
        {
            throw new InvalidOperationException($"Cannot start processor in state: {State}");
        }

        State = ProcessorState.Running;
        
        // Source processors start reading data immediately
        await ProcessFileAsync(cancellationToken);
    }

    /// <summary>
    /// Stops the processor and halts data processing.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token for stop operation</param>
    /// <returns>Task representing the stop operation</returns>
    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Stopping DelimitedSource processor");
        
        State = ProcessorState.Stopping;
        
        // Cleanup any resources
        State = ProcessorState.Stopped;
        
        await Task.CompletedTask;
    }

    /// <summary>
    /// Processes a single chunk of data.
    /// </summary>
    /// <param name="inputChunk">Input data chunk</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Processed data chunk</returns>
    public async Task<IChunk> ProcessChunkAsync(IChunk inputChunk, CancellationToken cancellationToken = default)
    {
        // For source processors, this method is typically not used as they generate data
        // instead of processing input chunks
        _logger.LogDebug("ProcessChunkAsync called on source processor - delegating to service");
        
        return await _service.ProcessChunkAsync(inputChunk, cancellationToken);
    }

    /// <summary>
    /// Processes the delimited file and outputs data chunks.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Task representing the processing operation</returns>
    private async Task ProcessFileAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Processing delimited file: {FilePath}", _configuration.FilePath);

        var stopwatch = Stopwatch.StartNew();
        var processedRows = 0L;

        try
        {
            using var reader = new StreamReader(_configuration.FilePath, 
                System.Text.Encoding.GetEncoding(_configuration.Encoding));

            // Skip header if present
            if (_configuration.HasHeader)
            {
                await reader.ReadLineAsync();
            }

            var batchRows = new List<string>();
            string? line;

            while ((line = await reader.ReadLineAsync()) != null)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (_configuration.SkipEmptyLines && string.IsNullOrWhiteSpace(line))
                    continue;

                batchRows.Add(line);
                processedRows++;

                // Process batch when it reaches the configured size
                if (batchRows.Count >= _configuration.BatchSize)
                {
                    await ProcessBatch(batchRows, cancellationToken);
                    batchRows.Clear();
                }
            }

            // Process remaining rows
            if (batchRows.Count > 0)
            {
                await ProcessBatch(batchRows, cancellationToken);
            }

            stopwatch.Stop();

            var throughput = processedRows / stopwatch.Elapsed.TotalSeconds;
            _logger.LogInformation("Completed processing {ProcessedRows:N0} rows in {ElapsedMs:N0}ms. Throughput: {Throughput:N0} rows/sec",
                processedRows, stopwatch.ElapsedMilliseconds, throughput);

            // Log performance warning if below target
            if (throughput < 200_000)
            {
                _logger.LogWarning("Performance below target: {Throughput:N0} rows/sec (target: 200K+ rows/sec)", throughput);
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("File processing was cancelled after {ProcessedRows:N0} rows", processedRows);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing delimited file");
            throw;
        }
    }

    /// <summary>
    /// Processes a batch of rows.
    /// </summary>
    /// <param name="batchRows">Batch of raw text rows</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Task representing the batch processing operation</returns>
    private async Task ProcessBatch(List<string> batchRows, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Processing batch of {RowCount} rows", batchRows.Count);

        // Convert raw text rows to structured data
        var arrayRows = new List<IArrayRow>();

        foreach (var rawRow in batchRows)
        {
            try
            {
                var fields = rawRow.Split(_configuration.Delimiter, StringSplitOptions.None);
                
                // Create values array
                var values = new object[fields.Length];
                for (int i = 0; i < fields.Length; i++)
                {
                    values[i] = _configuration.TrimValues ? fields[i].Trim() : fields[i];
                }

                // Create ArrayRow (simplified - would use proper schema in full implementation)
                var arrayRow = new FlowEngine.Core.Data.ArrayRow(values, _configuration.OutputSchema);
                arrayRows.Add(arrayRow);
            }
            catch (Exception ex) when (_configuration.ContinueOnError)
            {
                _logger.LogWarning("Error parsing row, skipping: {Error}", ex.Message);
            }
        }

        // Process the structured data through the service
        if (arrayRows.Count > 0)
        {
            var chunk = new FlowEngine.Core.Data.Chunk(_configuration.OutputSchema, arrayRows);
            await _service.ProcessChunkAsync(chunk, cancellationToken);
        }
    }

    /// <summary>
    /// Disposes the processor and releases resources.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (State == ProcessorState.Running)
        {
            await StopAsync();
        }

        _logger.LogDebug("DelimitedSource processor disposed");
        GC.SuppressFinalize(this);
    }
}