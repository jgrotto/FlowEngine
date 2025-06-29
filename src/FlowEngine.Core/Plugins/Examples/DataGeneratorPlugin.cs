using FlowEngine.Abstractions;
using FlowEngine.Abstractions.Plugins;
using FlowEngine.Core.Data;
using Microsoft.Extensions.Configuration;
using System.Collections.Immutable;

namespace FlowEngine.Core.Plugins.Examples;

/// <summary>
/// Example source plugin that generates test data for pipeline testing.
/// Produces configurable number of rows with sample customer data.
/// </summary>
[PluginDisplayName("Data Generator")]
[PluginDescription("Generates sample data for testing pipelines")]
[PluginVersion("1.0.0")]
public sealed class DataGeneratorPlugin : ISourcePlugin, IConfigurablePlugin
{
    private ISchema? _outputSchema;
    private int _rowCount = 1000;
    private int _batchSize = 100;
    private TimeSpan _delayBetweenBatches = TimeSpan.FromMilliseconds(100);
    private readonly Random _random = new();
    private int _currentPosition = 0;

    private static readonly string[] FirstNames = 
    {
        "John", "Jane", "Bob", "Alice", "Charlie", "Diana", "Frank", "Grace",
        "Henry", "Ivy", "Jack", "Karen", "Leo", "Mia", "Nathan", "Olivia"
    };

    private static readonly string[] LastNames = 
    {
        "Smith", "Johnson", "Williams", "Brown", "Jones", "Garcia", "Miller",
        "Davis", "Rodriguez", "Martinez", "Hernandez", "Lopez", "Gonzalez"
    };

    private static readonly string[] Departments = 
    {
        "Engineering", "Sales", "Marketing", "HR", "Finance", "Operations"
    };

    /// <inheritdoc />
    public string Name { get; private set; } = "DataGenerator";

    /// <inheritdoc />
    public ISchema? OutputSchema => _outputSchema;

    /// <inheritdoc />
    public long? EstimatedRowCount => _rowCount > 0 ? _rowCount : null;

    /// <inheritdoc />
    public bool IsSeekable => true;

    /// <inheritdoc />
    public SourcePosition? CurrentPosition => 
        _currentPosition > 0 ? SourcePosition.FromValue(_currentPosition) : null;

    /// <inheritdoc />
    public Task SeekAsync(SourcePosition position, CancellationToken cancellationToken = default)
    {
        if (position.Value is int pos && pos >= 0 && pos <= _rowCount)
        {
            _currentPosition = pos;
            return Task.CompletedTask;
        }
        
        throw new PluginExecutionException($"Invalid seek position: {position.Value}");
    }

    /// <inheritdoc />
    public string Version => "1.0.0";

    /// <inheritdoc />
    public IReadOnlyDictionary<string, object>? Metadata => new Dictionary<string, object>
    {
        ["Type"] = "Source",
        ["Category"] = "Testing",
        ["Description"] = "Generates sample customer data for testing"
    };

    /// <inheritdoc />
    public async Task InitializeAsync(IConfiguration config, CancellationToken cancellationToken = default)
    {
        // Extract configuration
        if (config["Name"] != null)
            Name = config["Name"]!;

        if (int.TryParse(config["RowCount"], out var rowCount))
            _rowCount = rowCount;

        if (int.TryParse(config["BatchSize"], out var batchSize))
            _batchSize = Math.Max(1, batchSize);

        if (int.TryParse(config["DelayMs"], out var delayMs))
            _delayBetweenBatches = TimeSpan.FromMilliseconds(Math.Max(0, delayMs));

        // Create output schema
        var columns = new ColumnDefinition[]
        {
            new() { Name = "Id", DataType = typeof(int), IsNullable = false, Description = "Customer ID" },
            new() { Name = "FirstName", DataType = typeof(string), IsNullable = false, Description = "Customer first name" },
            new() { Name = "LastName", DataType = typeof(string), IsNullable = false, Description = "Customer last name" },
            new() { Name = "Email", DataType = typeof(string), IsNullable = false, Description = "Customer email address" },
            new() { Name = "Department", DataType = typeof(string), IsNullable = false, Description = "Customer department" },
            new() { Name = "Salary", DataType = typeof(decimal), IsNullable = false, Description = "Customer salary" },
            new() { Name = "CreatedAt", DataType = typeof(DateTime), IsNullable = false, Description = "Record creation time" }
        };

        _outputSchema = Schema.GetOrCreate(columns);

        await Task.CompletedTask;
    }

    /// <inheritdoc />
    public PluginValidationResult ValidateInputSchema(ISchema? inputSchema)
    {
        // Source plugins don't have input schemas
        if (inputSchema != null)
            return PluginValidationResult.Failure("Source plugins should not have input schemas");

        return PluginValidationResult.Success();
    }

    /// <inheritdoc />
    public PluginMetrics GetMetrics()
    {
        return new PluginMetrics
        {
            TotalChunksProcessed = 0, // Would be tracked during execution
            TotalRowsProcessed = 0,
            TotalProcessingTime = TimeSpan.Zero,
            CurrentMemoryUsage = GC.GetTotalMemory(false),
            PeakMemoryUsage = GC.GetTotalMemory(false),
            ErrorCount = 0,
            Timestamp = DateTimeOffset.UtcNow
        };
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<IChunk> ProduceAsync([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (_outputSchema == null)
            throw new PluginInitializationException("Plugin not initialized - output schema is null");

        var rowsGenerated = 0;
        var chunkId = 0;

        while (rowsGenerated < _rowCount && !cancellationToken.IsCancellationRequested)
        {
            var currentBatchSize = Math.Min(_batchSize, _rowCount - rowsGenerated);
            var rows = new ArrayRow[currentBatchSize];

            for (int i = 0; i < currentBatchSize; i++)
            {
                var customerId = _currentPosition + i + 1;
                var firstName = FirstNames[_random.Next(FirstNames.Length)];
                var lastName = LastNames[_random.Next(LastNames.Length)];
                var email = $"{firstName.ToLower()}.{lastName.ToLower()}@company.com";
                var department = Departments[_random.Next(Departments.Length)];
                var salary = (decimal)(_random.NextDouble() * 100000 + 30000); // $30K - $130K
                var createdAt = DateTime.UtcNow.AddDays(-_random.Next(365)); // Random date within last year

                var values = new object?[]
                {
                    customerId,
                    firstName,
                    lastName,
                    email,
                    department,
                    Math.Round(salary, 2),
                    createdAt
                };

                rows[i] = new ArrayRow(_outputSchema, values);
            }

            var metadata = ImmutableDictionary<string, object>.Empty
                .Add("ChunkId", chunkId++)
                .Add("Source", Name)
                .Add("GeneratedAt", DateTimeOffset.UtcNow);

            var chunk = new Chunk(_outputSchema, rows, metadata);
            
            yield return chunk;

            rowsGenerated += currentBatchSize;
            _currentPosition += currentBatchSize;

            // Add delay between batches if configured
            if (_delayBetweenBatches > TimeSpan.Zero && rowsGenerated < _rowCount)
            {
                await Task.Delay(_delayBetweenBatches, cancellationToken);
            }
        }
    }

    /// <inheritdoc />
    public async Task ConfigureAsync(IReadOnlyDictionary<string, object> config)
    {
        if (config.TryGetValue("RowCount", out var rowCountObj) && int.TryParse(rowCountObj?.ToString(), out var rowCount))
            _rowCount = rowCount;

        if (config.TryGetValue("BatchSize", out var batchSizeObj) && int.TryParse(batchSizeObj?.ToString(), out var batchSize))
            _batchSize = Math.Max(1, batchSize);

        if (config.TryGetValue("DelayMs", out var delayObj) && int.TryParse(delayObj?.ToString(), out var delayMs))
            _delayBetweenBatches = TimeSpan.FromMilliseconds(Math.Max(0, delayMs));

        await Task.CompletedTask;
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        // Nothing to dispose
        await ValueTask.CompletedTask;
    }
}