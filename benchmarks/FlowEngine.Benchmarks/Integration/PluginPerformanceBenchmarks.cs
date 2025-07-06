using BenchmarkDotNet.Attributes;
using FlowEngine.Abstractions.Data;
using FlowEngine.Abstractions.Factories;
using FlowEngine.Core;
using FlowEngine.Core.Data;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace FlowEngine.Benchmarks.Integration;

/// <summary>
/// Simplified plugin performance benchmarks focused on core data processing.
/// Tests ArrayRow performance and data generation scenarios relevant to plugins.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(iterationCount: 5, warmupCount: 2)]
public class PluginPerformanceBenchmarks
{
    private ServiceProvider _serviceProvider = null!;
    private IArrayRowFactory _arrayRowFactory = null!;
    private ISchemaFactory _schemaFactory = null!;
    private ISchema _testSchema = null!;
    private Random _random = null!;

    [Params(1000, 10000, 100000)]
    public int RecordCount { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        // Configure service provider with core dependencies
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.Warning));
        services.AddFlowEngineCore();
        
        _serviceProvider = services.BuildServiceProvider();
        _arrayRowFactory = _serviceProvider.GetRequiredService<IArrayRowFactory>();
        _schemaFactory = _serviceProvider.GetRequiredService<ISchemaFactory>();
        _random = new Random(42); // Fixed seed for consistent benchmarks

        // Create test schema
        _testSchema = _schemaFactory.CreateSchema(new[]
        {
            new ColumnDefinition { Name = "id", DataType = typeof(int), IsNullable = false, Index = 0 },
            new ColumnDefinition { Name = "name", DataType = typeof(string), IsNullable = false, Index = 1 },
            new ColumnDefinition { Name = "value", DataType = typeof(double), IsNullable = false, Index = 2 },
            new ColumnDefinition { Name = "timestamp", DataType = typeof(DateTime), IsNullable = false, Index = 3 },
            new ColumnDefinition { Name = "is_active", DataType = typeof(bool), IsNullable = false, Index = 4 }
        });
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _serviceProvider?.Dispose();
    }

    /// <summary>
    /// Baseline ArrayRow creation performance test.
    /// Measures the core data structure performance that plugins depend on.
    /// Target: 200K+ rows/sec creation rate.
    /// </summary>
    [Benchmark(Baseline = true)]
    public BenchmarkResult ArrayRowCreationPerformance()
    {
        var stopwatch = Stopwatch.StartNew();
        var createdRows = 0;

        for (int i = 0; i < RecordCount; i++)
        {
            var values = new object?[]
            {
                i,
                $"Item {i}",
                _random.NextDouble() * 1000,
                DateTime.UtcNow,
                i % 2 == 0
            };

            var row = _arrayRowFactory.CreateRow(_testSchema, values);
            if (row != null)
            {
                createdRows++;
            }
        }

        stopwatch.Stop();

        return new BenchmarkResult
        {
            TotalProcessed = createdRows,
            ElapsedMs = stopwatch.ElapsedMilliseconds,
            RowsPerSecond = createdRows / (stopwatch.ElapsedMilliseconds / 1000.0)
        };
    }

    /// <summary>
    /// ArrayRow field access performance test.
    /// Measures field access speed which is critical for plugin processing.
    /// Target: <20ns per field access.
    /// </summary>
    [Benchmark]
    public BenchmarkResult ArrayRowFieldAccessPerformance()
    {
        // Pre-create rows for testing
        var rows = new List<IArrayRow>(RecordCount);
        for (int i = 0; i < RecordCount; i++)
        {
            var values = new object?[]
            {
                i,
                $"Item {i}",
                _random.NextDouble() * 1000,
                DateTime.UtcNow,
                i % 2 == 0
            };
            rows.Add(_arrayRowFactory.CreateRow(_testSchema, values));
        }

        var stopwatch = Stopwatch.StartNew();
        var accessCount = 0;
        var totalValue = 0.0;

        // Access each field in each row (5 fields × RecordCount)
        foreach (var row in rows)
        {
            var id = row[0]; // id field
            var name = row[1]; // name field
            var value = row[2]; // value field
            var timestamp = row[3]; // timestamp field
            var isActive = row[4]; // is_active field

            accessCount += 5;
            if (value is double doubleValue)
            {
                totalValue += doubleValue;
            }
        }

        stopwatch.Stop();

        return new BenchmarkResult
        {
            TotalProcessed = accessCount,
            ElapsedMs = stopwatch.ElapsedMilliseconds,
            RowsPerSecond = accessCount / (stopwatch.ElapsedMilliseconds / 1000.0),
            AdditionalData = $"TotalValue: {totalValue:F2}"
        };
    }

    /// <summary>
    /// Batch processing performance test.
    /// Simulates typical plugin batch processing scenarios.
    /// Target: Maintain 200K+ rows/sec with batch processing overhead.
    /// </summary>
    [Benchmark]
    public BenchmarkResult BatchProcessingPerformance()
    {
        const int batchSize = 1000;
        var totalProcessed = 0;
        var stopwatch = Stopwatch.StartNew();

        for (int batch = 0; batch < RecordCount; batch += batchSize)
        {
            var currentBatchSize = Math.Min(batchSize, RecordCount - batch);
            var batchRows = new List<IArrayRow>(currentBatchSize);

            // Create batch
            for (int i = 0; i < currentBatchSize; i++)
            {
                var values = new object?[]
                {
                    batch + i,
                    $"Item {batch + i}",
                    _random.NextDouble() * 1000,
                    DateTime.UtcNow,
                    (batch + i) % 2 == 0
                };
                batchRows.Add(_arrayRowFactory.CreateRow(_testSchema, values));
            }

            // Process batch (simulate plugin processing)
            foreach (var row in batchRows)
            {
                var value = row[2]; // Access value field
                if (value is double doubleValue)
                {
                    // Simulate simple transformation
                    var transformedValue = doubleValue * 1.1;
                    totalProcessed++;
                }
            }
        }

        stopwatch.Stop();

        return new BenchmarkResult
        {
            TotalProcessed = totalProcessed,
            ElapsedMs = stopwatch.ElapsedMilliseconds,
            RowsPerSecond = totalProcessed / (stopwatch.ElapsedMilliseconds / 1000.0)
        };
    }

    /// <summary>
    /// Memory-optimized processing with chunked operations.
    /// Tests memory efficiency under plugin-like processing loads.
    /// </summary>
    [Benchmark]
    public BenchmarkResult MemoryOptimizedProcessing()
    {
        const int chunkSize = 5000;
        var totalProcessed = 0;
        var stopwatch = Stopwatch.StartNew();

        for (int chunk = 0; chunk < RecordCount; chunk += chunkSize)
        {
            var currentChunkSize = Math.Min(chunkSize, RecordCount - chunk);
            
            // Process chunk without accumulating all rows in memory
            for (int i = 0; i < currentChunkSize; i++)
            {
                var values = new object?[]
                {
                    chunk + i,
                    $"Item {chunk + i}",
                    _random.NextDouble() * 1000,
                    DateTime.UtcNow,
                    (chunk + i) % 2 == 0
                };

                var row = _arrayRowFactory.CreateRow(_testSchema, values);
                
                // Immediate processing without storing
                var value = row[2];
                if (value is double doubleValue && doubleValue > 500)
                {
                    totalProcessed++;
                }
            }

            // Force GC between chunks for large datasets
            if (RecordCount >= 50000 && chunk > 0)
            {
                GC.Collect(0, GCCollectionMode.Optimized);
            }
        }

        stopwatch.Stop();

        return new BenchmarkResult
        {
            TotalProcessed = totalProcessed,
            ElapsedMs = stopwatch.ElapsedMilliseconds,
            RowsPerSecond = RecordCount / (stopwatch.ElapsedMilliseconds / 1000.0)
        };
    }

    /// <summary>
    /// Parallel processing performance test.
    /// Tests multi-threaded plugin processing scenarios.
    /// </summary>
    [Benchmark]
    public BenchmarkResult ParallelProcessingPerformance()
    {
        var totalProcessed = 0;
        var stopwatch = Stopwatch.StartNew();

        var parallelOptions = new ParallelOptions
        {
            MaxDegreeOfParallelism = Environment.ProcessorCount
        };

        Parallel.For(0, RecordCount, parallelOptions, i =>
        {
            var values = new object?[]
            {
                i,
                $"Item {i}",
                _random.NextDouble() * 1000,
                DateTime.UtcNow,
                i % 2 == 0
            };

            var row = _arrayRowFactory.CreateRow(_testSchema, values);
            var value = row[2];
            
            if (value is double doubleValue && doubleValue > 250)
            {
                Interlocked.Increment(ref totalProcessed);
            }
        });

        stopwatch.Stop();

        return new BenchmarkResult
        {
            TotalProcessed = totalProcessed,
            ElapsedMs = stopwatch.ElapsedMilliseconds,
            RowsPerSecond = RecordCount / (stopwatch.ElapsedMilliseconds / 1000.0)
        };
    }
}

/// <summary>
/// Result object for plugin benchmark tests.
/// </summary>
public sealed class BenchmarkResult
{
    public int TotalProcessed { get; init; }
    public long ElapsedMs { get; init; }
    public double RowsPerSecond { get; init; }
    public string? AdditionalData { get; init; }

    public override string ToString()
    {
        var result = $"Processed: {TotalProcessed:N0}, Time: {ElapsedMs:N0}ms, Rate: {RowsPerSecond:N0} rows/sec";
        if (!string.IsNullOrEmpty(AdditionalData))
        {
            result += $", {AdditionalData}";
        }
        return result;
    }
}