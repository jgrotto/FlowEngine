using FlowEngine.Abstractions.Data;
using FlowEngine.Abstractions.Plugins;
using Microsoft.Extensions.Logging;
using System.Collections.Immutable;
using System.Diagnostics;
using System.IO.Compression;
using System.Text;

namespace DelimitedSink;

/// <summary>
/// Core service for DelimitedSink plugin implementing high-performance file writing with ArrayRow optimization.
/// Provides the business logic for writing delimited data to files with optimal performance characteristics.
/// </summary>
public sealed class DelimitedSinkService : IPluginService
{
    private readonly ILogger<DelimitedSinkService> _logger;
    private readonly object _writerLock = new();
    private readonly object _metricsLock = new();

    private DelimitedSinkConfiguration? _configuration;
    private StreamWriter? _writer;
    private FileStream? _fileStream;
    private GZipStream? _compressionStream;
    private string? _currentFilePath;
    private int _currentFileIndex;
    private long _currentFileSize;
    private bool _headerWritten;
    private bool _isInitialized;
    private bool _disposed;

    // Pre-calculated field indexes for ArrayRow optimization
    private ImmutableDictionary<string, int> _fieldIndexes = ImmutableDictionary<string, int>.Empty;
    private string[] _fieldNames = Array.Empty<string>();

    // Performance metrics
    private long _totalRowsWritten;
    private long _totalChunksProcessed;
    private long _totalBytesWritten;
    private long _totalErrors;
    private DateTime? _lastWriteTime;

    /// <summary>
    /// Initializes a new instance of the DelimitedSinkService class.
    /// </summary>
    /// <param name="logger">Logger instance</param>
    public DelimitedSinkService(ILogger<DelimitedSinkService> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Initializes the service asynchronously.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Task representing the initialization operation</returns>
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Initializing DelimitedSinkService");
        
        // Service initialization without configuration
        // Configuration will be provided later through specific methods
        
        await Task.CompletedTask;
    }

    /// <summary>
    /// Starts the service asynchronously.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Task representing the start operation</returns>
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Starting DelimitedSinkService");
        
        // Service is ready to process data
        
        await Task.CompletedTask;
    }

    /// <summary>
    /// Stops the service asynchronously.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Task representing the stop operation</returns>
    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Stopping DelimitedSinkService");
        
        // Ensure any pending writes are completed
        await FlushAndCloseAsync();
        
        await Task.CompletedTask;
    }

    /// <summary>
    /// Processes a single row asynchronously.
    /// </summary>
    /// <param name="row">Input row data</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Processed row (for sink plugins, typically the same row)</returns>
    public async Task<IArrayRow> ProcessRowAsync(IArrayRow row, CancellationToken cancellationToken = default)
    {
        if (_configuration == null)
            throw new InvalidOperationException("Service must be initialized with configuration before processing rows");

        await WriteRowAsync(row, cancellationToken);
        
        // For sink plugins, we typically return the same row as it's the end of the pipeline
        return row;
    }

    /// <summary>
    /// Initializes the service with the specified configuration.
    /// </summary>
    /// <param name="configuration">Sink configuration</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Completion task</returns>
    public async Task InitializeAsync(DelimitedSinkConfiguration configuration, CancellationToken cancellationToken = default)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(DelimitedSinkService));

        _logger.LogDebug("Initializing DelimitedSinkService");

        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));

        // Pre-calculate field indexes for ArrayRow optimization
        if (_configuration.InputSchema != null)
        {
            _fieldIndexes = _configuration.InputFieldIndexes;
            _fieldNames = _configuration.InputSchema.Columns.Select(c => c.Name).ToArray();
            
            _logger.LogDebug("Pre-calculated {FieldCount} field indexes for ArrayRow optimization", _fieldNames.Length);
        }

        // Initialize file writing
        await InitializeFileWriterAsync(cancellationToken);

        _isInitialized = true;
        _logger.LogDebug("DelimitedSinkService initialized successfully");
    }

    /// <summary>
    /// Writes a data chunk to the output file with ArrayRow optimization.
    /// </summary>
    /// <param name="chunk">Data chunk to write</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Write result with statistics</returns>
    public async Task<WriteResult> WriteChunkAsync(IChunk chunk, CancellationToken cancellationToken = default)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(DelimitedSinkService));

        if (!_isInitialized || _configuration == null)
            throw new InvalidOperationException("Service must be initialized before writing");

        var stopwatch = Stopwatch.StartNew();
        var errors = new List<string>();
        long rowsProcessed = 0;
        long rowsFailed = 0;

        try
        {
            lock (_writerLock)
            {
                // Write header if needed and not already written
                if (_configuration.IncludeHeader && !_headerWritten && _fieldNames.Length > 0)
                {
                    WriteHeader();
                    _headerWritten = true;
                }

                // Process each row with ArrayRow optimization
                foreach (var row in chunk.Rows)
                {
                    try
                    {
                        WriteRow(row);
                        rowsProcessed++;

                        // Check for file rotation
                        if (_configuration.MaxFileSizeBytes > 0 && 
                            _currentFileSize >= _configuration.MaxFileSizeBytes)
                        {
                            RotateFile();
                        }
                    }
                    catch (Exception ex)
                    {
                        rowsFailed++;
                        errors.Add($"Row {rowsProcessed + rowsFailed}: {ex.Message}");
                        
                        if (!_configuration.ContinueOnError)
                        {
                            throw;
                        }
                    }
                }

                // Flush if configured
                if (_configuration.FlushAfterBatch)
                {
                    _writer?.Flush();
                }
            }

            // Update metrics
            lock (_metricsLock)
            {
                _totalRowsWritten += rowsProcessed;
                _totalChunksProcessed++;
                _totalErrors += rowsFailed;
                _lastWriteTime = DateTime.UtcNow;
            }

            stopwatch.Stop();

            _logger.LogDebug("Wrote chunk: {RowsProcessed} rows, {RowsFailed} failed in {ElapsedMs}ms",
                rowsProcessed, rowsFailed, stopwatch.ElapsedMilliseconds);

            return new WriteResult
            {
                RowsProcessed = rowsProcessed,
                RowsFailed = rowsFailed,
                ProcessingTime = stopwatch.Elapsed,
                Errors = errors
            };
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            
            lock (_metricsLock)
            {
                _totalErrors++;
            }

            _logger.LogError(ex, "Failed to write chunk after {ElapsedMs}ms", stopwatch.ElapsedMilliseconds);
            
            return new WriteResult
            {
                RowsProcessed = rowsProcessed,
                RowsFailed = chunk.RowCount - rowsProcessed,
                ProcessingTime = stopwatch.Elapsed,
                Errors = errors.Concat(new[] { ex.Message }).ToList()
            };
        }
    }

    /// <summary>
    /// Finalizes writing operations (flushes buffers, closes files).
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Completion task</returns>
    public async Task FinalizeAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed)
            return;

        _logger.LogDebug("Finalizing DelimitedSinkService");

        try
        {
            lock (_writerLock)
            {
                _writer?.Flush();
                _writer?.Close();
                
                _compressionStream?.Close();
                _fileStream?.Close();
            }

            _logger.LogInformation("Finalized writing: {TotalRows} rows written to {FilePath}",
                _totalRowsWritten, _currentFilePath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during finalization");
            throw;
        }
    }

    /// <summary>
    /// Checks the health of the service.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Health status</returns>
    public async Task<ServiceHealth> CheckHealthAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed)
        {
            return ServiceHealth.CreateUnhealthy(
                "SERVICE_DISPOSED",
                "Service has been disposed",
                ImmutableDictionary<string, object>.Empty);
        }

        if (!_isInitialized)
        {
            return ServiceHealth.CreateUnhealthy(
                "SERVICE_NOT_INITIALIZED",
                "Service has not been initialized",
                ImmutableDictionary<string, object>.Empty);
        }

        try
        {
            // Check if writer is still functional
            lock (_writerLock)
            {
                if (_writer == null || _fileStream == null)
                {
                    return ServiceHealth.CreateUnhealthy(
                        "NO_ACTIVE_WRITER",
                        "No active file writer available",
                        ImmutableDictionary<string, object>.Empty);
                }

                if (!_fileStream.CanWrite)
                {
                    return ServiceHealth.CreateUnhealthy(
                        "WRITER_NOT_WRITABLE",
                        "File stream is not writable",
                        ImmutableDictionary<string, object>.Empty);
                }
            }

            // Check disk space
            if (_currentFilePath != null)
            {
                var directoryPath = Path.GetDirectoryName(_currentFilePath) ?? Directory.GetCurrentDirectory();
                var drive = new DriveInfo(directoryPath);
                var availableSpaceMB = drive.AvailableFreeSpace / (1024 * 1024);

                if (availableSpaceMB < 50) // Less than 50MB
                {
                    return ServiceHealth.CreateDegraded(
                        "LOW_DISK_SPACE",
                        $"Low disk space: {availableSpaceMB}MB available",
                        ImmutableDictionary<string, object>.Empty
                            .Add("AvailableSpaceMB", availableSpaceMB));
                }
            }

            lock (_metricsLock)
            {
                var errorRate = _totalChunksProcessed > 0 ? (double)_totalErrors / _totalChunksProcessed : 0;
                
                if (errorRate > 0.05) // More than 5% error rate
                {
                    return ServiceHealth.CreateDegraded(
                        "HIGH_ERROR_RATE",
                        $"High error rate detected: {errorRate:P1}",
                        ImmutableDictionary<string, object>.Empty
                            .Add("ErrorRate", errorRate)
                            .Add("TotalChunks", _totalChunksProcessed)
                            .Add("TotalErrors", _totalErrors));
                }
            }

            return ServiceHealth.CreateHealthy(
                "Service is healthy and ready to write data",
                ImmutableDictionary<string, object>.Empty
                    .Add("IsInitialized", _isInitialized)
                    .Add("TotalRowsWritten", _totalRowsWritten)
                    .Add("TotalChunksProcessed", _totalChunksProcessed)
                    .Add("CurrentFilePath", _currentFilePath ?? "None")
                    .Add("LastWriteTime", _lastWriteTime?.ToString("O") ?? "Never"));
        }
        catch (Exception ex)
        {
            return ServiceHealth.CreateUnhealthy(
                "HEALTH_CHECK_EXCEPTION",
                $"Health check failed: {ex.Message}",
                ImmutableDictionary<string, object>.Empty);
        }
    }

    /// <summary>
    /// Gets current service metrics.
    /// </summary>
    /// <returns>Service metrics</returns>
    public ServiceMetrics GetMetrics()
    {
        if (_disposed)
            return ServiceMetrics.Empty;

        lock (_metricsLock)
        {
            return new ServiceMetrics
            {
                RequestCount = _totalChunksProcessed,
                ErrorCount = _totalErrors,
                AverageResponseTime = TimeSpan.Zero, // Calculated at processor level
                LastRequestTime = _lastWriteTime,
                Metadata = ImmutableDictionary<string, object>.Empty
                    .Add("TotalRowsWritten", _totalRowsWritten)
                    .Add("TotalBytesWritten", _totalBytesWritten)
                    .Add("CurrentFileIndex", _currentFileIndex)
                    .Add("CurrentFileSize", _currentFileSize)
                    .Add("HeaderWritten", _headerWritten)
                    .Add("IsInitialized", _isInitialized)
            };
        }
    }

    /// <summary>
    /// Performs hot-swapping of configuration.
    /// </summary>
    /// <param name="newConfiguration">New configuration</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Hot-swap result</returns>
    public async Task<HotSwapResult> HotSwapAsync(DelimitedSinkConfiguration newConfiguration, CancellationToken cancellationToken = default)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(DelimitedSinkService));

        _logger.LogDebug("Performing hot-swap for DelimitedSinkService");

        try
        {
            var oldConfiguration = _configuration;
            
            // If the file path changes, we need to finalize current writing and start a new file
            if (oldConfiguration?.FilePath != newConfiguration.FilePath)
            {
                await FinalizeAsync(cancellationToken);
                _configuration = newConfiguration;
                await InitializeFileWriterAsync(cancellationToken);
            }
            else
            {
                // Configuration change doesn't affect the file, just update settings
                _configuration = newConfiguration;
                
                // Update field indexes if schema changed
                if (_configuration.InputSchema != null)
                {
                    _fieldIndexes = _configuration.InputFieldIndexes;
                    _fieldNames = _configuration.InputSchema.Columns.Select(c => c.Name).ToArray();
                }
            }

            _logger.LogDebug("DelimitedSinkService hot-swap completed successfully");

            return HotSwapResult.Success(
                TimeSpan.FromMilliseconds(1),
                ImmutableArray<ValidationWarning>.Empty,
                ImmutableDictionary<string, object>.Empty
                    .Add("ServiceHotSwapped", true)
                    .Add("FilePathChanged", oldConfiguration?.FilePath != newConfiguration.FilePath)
                    .Add("NewFilePath", newConfiguration.FilePath));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Service hot-swap failed");
            return HotSwapResult.Failure(
                "SERVICE_HOT_SWAP_FAILED",
                $"Service hot-swap failed: {ex.Message}",
                TimeSpan.Zero);
        }
    }

    /// <summary>
    /// Initializes the file writer based on configuration.
    /// </summary>
    private async Task InitializeFileWriterAsync(CancellationToken cancellationToken)
    {
        if (_configuration == null)
            throw new InvalidOperationException("Configuration required for file writer initialization");

        _currentFilePath = _configuration.GetEffectiveFilePath(_currentFileIndex);
        
        // Ensure directory exists
        var directoryPath = Path.GetDirectoryName(_currentFilePath);
        if (!string.IsNullOrEmpty(directoryPath) && !Directory.Exists(directoryPath))
        {
            Directory.CreateDirectory(directoryPath);
            _logger.LogDebug("Created output directory: {DirectoryPath}", directoryPath);
        }

        // Create file stream
        var fileMode = _configuration.AppendToFile && File.Exists(_currentFilePath) 
            ? FileMode.Append 
            : FileMode.Create;
        
        _fileStream = new FileStream(
            _currentFilePath,
            fileMode,
            FileAccess.Write,
            FileShare.Read,
            _configuration.BufferSize);

        // Add compression if enabled
        if (_configuration.EnableCompression)
        {
            _compressionStream = new GZipStream(_fileStream, CompressionLevel.Optimal);
            var encoding = Encoding.GetEncoding(_configuration.Encoding);
            _writer = new StreamWriter(_compressionStream, encoding, _configuration.BufferSize);
        }
        else
        {
            var encoding = Encoding.GetEncoding(_configuration.Encoding);
            _writer = new StreamWriter(_fileStream, encoding, _configuration.BufferSize);
        }

        _currentFileSize = _fileStream.Length;
        _headerWritten = _configuration.AppendToFile && _currentFileSize > 0; // Don't write header if appending to existing file

        _logger.LogDebug("Initialized file writer for: {FilePath} (Mode: {FileMode}, Compression: {Compression})",
            _currentFilePath, fileMode, _configuration.EnableCompression);
    }

    /// <summary>
    /// Writes the header row to the file.
    /// </summary>
    private void WriteHeader()
    {
        if (_writer == null || _fieldNames.Length == 0)
            return;

        var headerLine = string.Join(_configuration.Delimiter, _fieldNames.Select(name => 
            QuoteFieldIfNeeded(name, _configuration.Delimiter, _configuration.QuoteCharacter, _configuration.EscapeCharacter)));
        
        _writer.WriteLine(headerLine);
        
        var headerBytes = Encoding.GetEncoding(_configuration.Encoding).GetByteCount(headerLine + _configuration.GetLineEnding());
        _currentFileSize += headerBytes;
        _totalBytesWritten += headerBytes;

        _logger.LogDebug("Wrote header with {FieldCount} fields", _fieldNames.Length);
    }

    /// <summary>
    /// Writes a single row using ArrayRow optimization for O(1) field access.
    /// </summary>
    private void WriteRow(IArrayRow row)
    {
        if (_writer == null)
            throw new InvalidOperationException("Writer not initialized");

        var values = new string[_fieldNames.Length];
        
        // Use pre-calculated indexes for O(1) ArrayRow access
        for (int i = 0; i < _fieldNames.Length; i++)
        {
            var fieldName = _fieldNames[i];
            if (_fieldIndexes.TryGetValue(fieldName, out var fieldIndex))
            {
                var value = row.GetValue(fieldIndex);
                values[i] = value?.ToString() ?? string.Empty;
            }
            else
            {
                values[i] = string.Empty; // Field not found
            }
        }

        // Quote fields that need quoting
        var quotedValues = values.Select(value => 
            QuoteFieldIfNeeded(value, _configuration.Delimiter, _configuration.QuoteCharacter, _configuration.EscapeCharacter));

        var line = string.Join(_configuration.Delimiter, quotedValues);
        _writer.WriteLine(line);

        // Update file size tracking
        var lineBytes = Encoding.GetEncoding(_configuration.Encoding).GetByteCount(line + _configuration.GetLineEnding());
        _currentFileSize += lineBytes;
        _totalBytesWritten += lineBytes;
    }

    /// <summary>
    /// Rotates to a new file when size limit is reached.
    /// </summary>
    private void RotateFile()
    {
        _logger.LogDebug("Rotating file: current size {CurrentSize} bytes", _currentFileSize);

        // Close current writer
        _writer?.Flush();
        _writer?.Close();
        _compressionStream?.Close();
        _fileStream?.Close();

        // Increment file index and create new writer
        _currentFileIndex++;
        _currentFileSize = 0;
        _headerWritten = false;

        Task.Run(async () => await InitializeFileWriterAsync(CancellationToken.None));

        _logger.LogDebug("Rotated to new file: {FilePath}", _configuration?.GetEffectiveFilePath(_currentFileIndex));
    }

    /// <summary>
    /// Quotes a field value if it contains special characters.
    /// </summary>
    private static string QuoteFieldIfNeeded(string value, string delimiter, string quoteChar, string escapeChar)
    {
        if (string.IsNullOrEmpty(value))
            return value;

        // Check if quoting is needed
        if (value.Contains(delimiter) || value.Contains(quoteChar) || value.Contains('\n') || value.Contains('\r'))
        {
            // Escape existing quotes
            var escapedValue = value.Replace(quoteChar, escapeChar + quoteChar);
            return quoteChar + escapedValue + quoteChar;
        }

        return value;
    }

    /// <summary>
    /// Disposes the service asynchronously.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;

        _logger.LogDebug("Disposing DelimitedSinkService");

        try
        {
            await FinalizeAsync(CancellationToken.None);

            lock (_writerLock)
            {
                _writer?.Dispose();
                _compressionStream?.Dispose();
                _fileStream?.Dispose();
            }

            _disposed = true;
            _logger.LogDebug("DelimitedSinkService disposed successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during service disposal");
            throw;
        }
    }

    /// <summary>
    /// Disposes the service synchronously.
    /// </summary>
    public void Dispose()
    {
        DisposeAsync().AsTask().GetAwaiter().GetResult();
    }
}

/// <summary>
/// Result of a write operation for a data chunk.
/// </summary>
public sealed class WriteResult
{
    /// <summary>
    /// Gets the number of rows successfully processed.
    /// </summary>
    public long RowsProcessed { get; init; }

    /// <summary>
    /// Gets the number of rows that failed to process.
    /// </summary>
    public long RowsFailed { get; init; }

    /// <summary>
    /// Gets the time taken to process the chunk.
    /// </summary>
    public TimeSpan ProcessingTime { get; init; }

    /// <summary>
    /// Gets any errors that occurred during processing.
    /// </summary>
    public IReadOnlyList<string> Errors { get; init; } = Array.Empty<string>();
}