using FlowEngine.Abstractions;
using FlowEngine.Abstractions.Data;
using FlowEngine.Abstractions.Plugins;
using FlowEngine.Core.Data;
using Microsoft.Extensions.Configuration;
using System.Text;

namespace FlowEngine.Core.Plugins.Examples;

/// <summary>
/// Example sink plugin that outputs data to the console.
/// Provides configurable formatting and filtering options for testing.
/// </summary>
[PluginDisplayName("Console Output")]
[PluginDescription("Outputs processed data to the console")]
[PluginVersion("1.0.0")]
public sealed class ConsoleOutputPlugin : ISinkPlugin, IConfigurablePlugin
{
    private bool _showHeaders = true;
    private bool _showRowNumbers = true;
    private int _maxRows = int.MaxValue;
    private string _delimiter = " | ";
    private int _maxColumnWidth = 50;
    private bool _showSummary = true;
    private int _rowsProcessed = 0;
    private long _totalRowsProcessed = 0;
    private readonly object _outputLock = new();
    private ISchema? _inputSchema;

    /// <inheritdoc />
    public string Name { get; private set; } = "ConsoleOutput";

    /// <inheritdoc />
    public ISchema? OutputSchema => null; // Sink plugins don't produce output

    /// <inheritdoc />
    public ISchema InputSchema => _inputSchema ?? throw new InvalidOperationException("Input schema not set");

    /// <inheritdoc />
    public bool SupportsTransactions => false;

    /// <inheritdoc />
    public WriteMode Mode => WriteMode.Streaming;

    /// <inheritdoc />
    public TransactionState? CurrentTransaction => null;

    /// <inheritdoc />
    public Task<string> BeginTransactionAsync(CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("Console output plugin does not support transactions");
    }

    /// <inheritdoc />
    public Task CommitTransactionAsync(string transactionId, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("Console output plugin does not support transactions");
    }

    /// <inheritdoc />
    public Task RollbackTransactionAsync(string transactionId, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("Console output plugin does not support transactions");
    }

    /// <inheritdoc />
    public Task FlushAsync(CancellationToken cancellationToken = default)
    {
        // Console output is already flushed, nothing to do
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public string Version => "1.0.0";

    /// <inheritdoc />
    public IReadOnlyDictionary<string, object>? Metadata => new Dictionary<string, object>
    {
        ["Type"] = "Sink",
        ["Category"] = "Output",
        ["Description"] = "Outputs data to console for testing and debugging"
    };

    /// <inheritdoc />
    public async Task InitializeAsync(IConfiguration config, CancellationToken cancellationToken = default)
    {
        // Extract configuration
        if (config["Name"] != null)
            Name = config["Name"]!;

        if (bool.TryParse(config["ShowHeaders"], out var showHeaders))
            _showHeaders = showHeaders;

        if (bool.TryParse(config["ShowRowNumbers"], out var showRowNumbers))
            _showRowNumbers = showRowNumbers;

        if (int.TryParse(config["MaxRows"], out var maxRows))
            _maxRows = Math.Max(0, maxRows);

        if (config["Delimiter"] != null)
            _delimiter = config["Delimiter"]!;

        if (int.TryParse(config["MaxColumnWidth"], out var maxColumnWidth))
            _maxColumnWidth = Math.Max(5, maxColumnWidth);

        if (bool.TryParse(config["ShowSummary"], out var showSummary))
            _showSummary = showSummary;

        _rowsProcessed = 0;
        _totalRowsProcessed = 0;

        lock (_outputLock)
        {
            Console.WriteLine($"=== {Name} Plugin Initialized ===");
            Console.WriteLine($"Configuration: Headers={_showHeaders}, RowNumbers={_showRowNumbers}, MaxRows={_maxRows}");
            Console.WriteLine($"Delimiter='{_delimiter}', MaxColumnWidth={_maxColumnWidth}, ShowSummary={_showSummary}");
            Console.WriteLine();
        }

        await Task.CompletedTask;
    }

    /// <inheritdoc />
    public PluginValidationResult ValidateInputSchema(ISchema? inputSchema)
    {
        if (inputSchema == null)
            return PluginValidationResult.Failure("Sink plugins require an input schema");

        _inputSchema = inputSchema;
        var warnings = new List<string>();

        if (inputSchema.ColumnCount == 0)
            warnings.Add("Input schema has no columns - nothing will be output");

        if (inputSchema.ColumnCount > 20)
            warnings.Add($"Input schema has {inputSchema.ColumnCount} columns - output may be difficult to read");

        return PluginValidationResult.SuccessWithWarnings(warnings.ToArray());
    }

    /// <inheritdoc />
    public PluginMetrics GetMetrics()
    {
        return new PluginMetrics
        {
            TotalChunksProcessed = 0, // Would be tracked during execution
            TotalRowsProcessed = _totalRowsProcessed,
            TotalProcessingTime = TimeSpan.Zero,
            CurrentMemoryUsage = GC.GetTotalMemory(false),
            PeakMemoryUsage = GC.GetTotalMemory(false),
            ErrorCount = 0,
            Timestamp = DateTimeOffset.UtcNow,
            CustomMetrics = new Dictionary<string, object>
            {
                ["RowsOutput"] = _totalRowsProcessed,
                ["MaxRowsLimit"] = _maxRows
            }
        };
    }

    /// <inheritdoc />
    public async Task ConsumeAsync(IAsyncEnumerable<IChunk> inputChunks, CancellationToken cancellationToken = default)
    {
        var chunkCount = 0;
        var firstChunk = true;

        await foreach (var chunk in inputChunks.WithCancellation(cancellationToken))
        {
            using (chunk) // Ensure proper disposal
            {
                chunkCount++;

                lock (_outputLock)
                {
                    // Show headers for first chunk
                    if (firstChunk && _showHeaders && chunk.Schema.ColumnCount > 0)
                    {
                        OutputHeaders(chunk.Schema);
                        firstChunk = false;
                    }

                    // Process rows in this chunk
                    for (int i = 0; i < chunk.RowCount && _rowsProcessed < _maxRows; i++)
                    {
                        var row = (ArrayRow)chunk.Rows[i];
                        OutputRow(row, chunk.Schema);
                        _rowsProcessed++;
                        _totalRowsProcessed++;
                    }

                    // Show chunk summary
                    Console.WriteLine($"--- Chunk {chunkCount} processed: {chunk.RowCount} rows ---");

                    // Check if we've reached the row limit
                    if (_rowsProcessed >= _maxRows)
                    {
                        Console.WriteLine($"--- Reached maximum row limit ({_maxRows}) ---");
                        break;
                    }
                }
            }
        }

        // Show final summary
        if (_showSummary)
        {
            lock (_outputLock)
            {
                Console.WriteLine();
                Console.WriteLine($"=== {Name} Summary ===");
                Console.WriteLine($"Total chunks processed: {chunkCount}");
                Console.WriteLine($"Total rows processed: {_totalRowsProcessed}");
                Console.WriteLine($"Processing completed at: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC");
                Console.WriteLine();
            }
        }
    }

    /// <inheritdoc />
    public async Task ConfigureAsync(IReadOnlyDictionary<string, object> config)
    {
        if (config.TryGetValue("ShowHeaders", out var headersObj) && bool.TryParse(headersObj?.ToString(), out var showHeaders))
            _showHeaders = showHeaders;

        if (config.TryGetValue("ShowRowNumbers", out var rowNumbersObj) && bool.TryParse(rowNumbersObj?.ToString(), out var showRowNumbers))
            _showRowNumbers = showRowNumbers;

        if (config.TryGetValue("MaxRows", out var maxRowsObj) && int.TryParse(maxRowsObj?.ToString(), out var maxRows))
            _maxRows = Math.Max(0, maxRows);

        if (config.TryGetValue("Delimiter", out var delimiterObj) && delimiterObj?.ToString() != null)
            _delimiter = delimiterObj.ToString()!;

        if (config.TryGetValue("MaxColumnWidth", out var maxWidthObj) && int.TryParse(maxWidthObj?.ToString(), out var maxColumnWidth))
            _maxColumnWidth = Math.Max(5, maxColumnWidth);

        if (config.TryGetValue("ShowSummary", out var summaryObj) && bool.TryParse(summaryObj?.ToString(), out var showSummary))
            _showSummary = showSummary;

        await Task.CompletedTask;
    }

    private void OutputHeaders(ISchema schema)
    {
        var headerBuilder = new StringBuilder();

        if (_showRowNumbers)
        {
            headerBuilder.Append("Row#".PadRight(6));
            headerBuilder.Append(_delimiter);
        }

        for (int i = 0; i < schema.ColumnCount; i++)
        {
            var column = schema.Columns[i];
            var header = TruncateString(column.Name, _maxColumnWidth);
            headerBuilder.Append(header.PadRight(_maxColumnWidth));

            if (i < schema.ColumnCount - 1)
                headerBuilder.Append(_delimiter);
        }

        Console.WriteLine(headerBuilder.ToString());

        // Output separator line
        var separatorBuilder = new StringBuilder();
        if (_showRowNumbers)
        {
            separatorBuilder.Append(new string('-', 6));
            separatorBuilder.Append(_delimiter);
        }

        for (int i = 0; i < schema.ColumnCount; i++)
        {
            separatorBuilder.Append(new string('-', _maxColumnWidth));
            if (i < schema.ColumnCount - 1)
                separatorBuilder.Append(_delimiter);
        }

        Console.WriteLine(separatorBuilder.ToString());
    }

    private void OutputRow(ArrayRow row, ISchema schema)
    {
        var rowBuilder = new StringBuilder();

        if (_showRowNumbers)
        {
            rowBuilder.Append((_rowsProcessed + 1).ToString().PadRight(6));
            rowBuilder.Append(_delimiter);
        }

        for (int i = 0; i < schema.ColumnCount; i++)
        {
            var value = row[i];
            var valueString = FormatValue(value);
            var truncatedValue = TruncateString(valueString, _maxColumnWidth);
            rowBuilder.Append(truncatedValue.PadRight(_maxColumnWidth));

            if (i < schema.ColumnCount - 1)
                rowBuilder.Append(_delimiter);
        }

        Console.WriteLine(rowBuilder.ToString());
    }

    private static string FormatValue(object? value)
    {
        return value switch
        {
            null => "<null>",
            DateTime dt => dt.ToString("yyyy-MM-dd HH:mm:ss"),
            DateTimeOffset dto => dto.ToString("yyyy-MM-dd HH:mm:ss"),
            decimal d => d.ToString("F2"),
            double d => d.ToString("F2"),
            float f => f.ToString("F2"),
            _ => value.ToString() ?? "<null>"
        };
    }

    private static string TruncateString(string input, int maxLength)
    {
        if (string.IsNullOrEmpty(input))
            return string.Empty;

        if (input.Length <= maxLength)
            return input;

        return input.Substring(0, maxLength - 3) + "...";
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        lock (_outputLock)
        {
            Console.WriteLine($"=== {Name} Plugin Disposed ===");
        }

        await ValueTask.CompletedTask;
    }
}