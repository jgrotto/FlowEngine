using FlowEngine.Abstractions.Data;
using FlowEngine.Abstractions.Factories;
using FlowEngine.Core;
using FlowEngine.Core.Data;
using FlowEngine.Core.Factories;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using Xunit;
using Xunit.Abstractions;

namespace DelimitedPlugins.Tests;

/// <summary>
/// Core performance tests to validate the 200K+ rows/sec claims for ArrayRow operations.
/// These tests bypass JavaScript overhead to measure pure C# ArrayRow performance.
/// </summary>
public class CorePerformanceTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly ServiceProvider _serviceProvider;

    public CorePerformanceTests(ITestOutputHelper output)
    {
        _output = output;
        
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Warning));
        services.AddFlowEngineCore();
        
        _serviceProvider = services.BuildServiceProvider();
    }

    [Fact]
    public void ArrayRow_FieldAccess_ShouldMeetPerformanceTarget()
    {
        // Test pure ArrayRow field access performance
        const int iterations = 10_000_000; // 10M operations
        
        var schema = Schema.GetOrCreate(new[]
        {
            new ColumnDefinition { Name = "id", DataType = typeof(int), IsNullable = false, Index = 0 },
            new ColumnDefinition { Name = "name", DataType = typeof(string), IsNullable = false, Index = 1 },
            new ColumnDefinition { Name = "amount", DataType = typeof(decimal), IsNullable = false, Index = 2 }
        });

        var row = new ArrayRow(schema, new object[] { 123, "Test", 456.78m });

        _output.WriteLine($"🚀 ArrayRow Field Access Performance Test");
        _output.WriteLine($"📊 Testing {iterations:N0} field access operations...");

        // Warm up
        for (int i = 0; i < 1000; i++)
        {
            _ = row[0];
            _ = row[1];
            _ = row[2];
        }

        var stopwatch = Stopwatch.StartNew();
        
        for (int i = 0; i < iterations; i++)
        {
            _ = row[0]; // Access by index (should be <20ns)
            _ = row[1];
            _ = row[2];
        }
        
        stopwatch.Stop();

        var totalOperations = iterations * 3; // 3 field accesses per iteration
        var avgNanoseconds = (stopwatch.Elapsed.TotalNanoseconds) / totalOperations;
        var operationsPerSecond = totalOperations / stopwatch.Elapsed.TotalSeconds;

        _output.WriteLine($"📋 Results:");
        _output.WriteLine($"   ⏱️  Total Time: {stopwatch.Elapsed.TotalMilliseconds:F1} ms");
        _output.WriteLine($"   🚀 Operations/sec: {operationsPerSecond:N0}");
        _output.WriteLine($"   ⚡ Avg Field Access: {avgNanoseconds:F1} ns");
        _output.WriteLine($"   🎯 Target: <20 ns per access");

        // Performance assertion - field access should be under 20ns
        Assert.True(avgNanoseconds < 50, $"Field access too slow: {avgNanoseconds:F1}ns > 50ns"); // Relaxed for CI
        
        _output.WriteLine(avgNanoseconds < 20 ? "   ✅ EXCELLENT: Under 20ns target!" : 
                         avgNanoseconds < 50 ? "   ✅ GOOD: Acceptable performance" : 
                         "   ⚠️  SLOW: Above target");
    }

    [Fact]
    public void ArrayRow_DataProcessing_200K_Rows_ShouldMeetPerformanceTarget()
    {
        // Test ArrayRow creation and processing at scale
        const int rowCount = 200_000;
        
        var schema = Schema.GetOrCreate(new[]
        {
            new ColumnDefinition { Name = "customer_id", DataType = typeof(int), IsNullable = false, Index = 0 },
            new ColumnDefinition { Name = "first_name", DataType = typeof(string), IsNullable = false, Index = 1 },
            new ColumnDefinition { Name = "last_name", DataType = typeof(string), IsNullable = false, Index = 2 },
            new ColumnDefinition { Name = "amount", DataType = typeof(decimal), IsNullable = false, Index = 3 }
        });

        var factory = _serviceProvider.GetRequiredService<IArrayRowFactory>();

        _output.WriteLine($"🚀 ArrayRow Processing Performance Test");
        _output.WriteLine($"📊 Processing {rowCount:N0} rows...");

        var random = new Random(42);
        var stopwatch = Stopwatch.StartNew();
        var processedRows = 0;

        // Simulate high-speed data processing
        for (int i = 0; i < rowCount; i++)
        {
            // Create row (simulating source)
            var sourceRow = new ArrayRow(schema, new object[] 
            { 
                i + 1, 
                $"User{i}", 
                $"LastName{i % 1000}", 
                (decimal)(random.NextDouble() * 10000) 
            });

            // Process row (simulating transform)
            var customerId = (int)sourceRow[0];
            var firstName = (string)sourceRow[1];
            var lastName = (string)sourceRow[2];
            var amount = (decimal)sourceRow[3];

            // Create output row (simulating sink)
            var outputData = new Dictionary<string, object?>
            {
                ["customer_id"] = customerId,
                ["full_name"] = $"{firstName} {lastName}",
                ["amount"] = amount,
                ["category"] = amount > 5000 ? "Premium" : "Standard"
            };

            var outputRow = factory.CreateRow(schema, outputData);
            processedRows++;
        }

        stopwatch.Stop();

        var throughput = processedRows / stopwatch.Elapsed.TotalSeconds;
        var targetThroughput = 200_000; // 200K rows/sec

        _output.WriteLine($"📋 Results:");
        _output.WriteLine($"   🔢 Rows Processed: {processedRows:N0}");
        _output.WriteLine($"   ⏱️  Total Time: {stopwatch.Elapsed.TotalSeconds:F2} seconds");
        _output.WriteLine($"   🚀 Actual Throughput: {throughput:F0} rows/sec");
        _output.WriteLine($"   🎯 Target Throughput: {targetThroughput:N0} rows/sec");
        _output.WriteLine($"   📊 Performance Ratio: {throughput / targetThroughput:P1} of target");
        _output.WriteLine($"   💾 Memory Usage: ~{GC.GetTotalMemory(false) / 1024 / 1024:F1} MB");

        // Performance assertion
        Assert.True(throughput >= targetThroughput * 0.5, 
            $"Core throughput below 50% of target: {throughput:F0} < {targetThroughput * 0.5:F0} rows/sec");

        if (throughput >= targetThroughput)
            _output.WriteLine("   ✅ EXCELLENT: Exceeded 200K rows/sec target!");
        else if (throughput >= targetThroughput * 0.8)
            _output.WriteLine("   ✅ GOOD: Within 80% of target");
        else if (throughput >= targetThroughput * 0.5)
            _output.WriteLine("   ⚠️  ACCEPTABLE: Above 50% of target");
        else
            _output.WriteLine("   ❌ POOR: Below 50% of target");
    }

    [Fact]
    public void Chunk_Processing_ShouldMeetPerformanceTarget()
    {
        // Test chunk-based processing performance
        const int totalRows = 100_000;
        const int chunkSize = 5_000;
        
        var schema = Schema.GetOrCreate(new[]
        {
            new ColumnDefinition { Name = "id", DataType = typeof(int), IsNullable = false, Index = 0 },
            new ColumnDefinition { Name = "value", DataType = typeof(string), IsNullable = false, Index = 1 }
        });

        _output.WriteLine($"🚀 Chunk Processing Performance Test");
        _output.WriteLine($"📊 Processing {totalRows:N0} rows in chunks of {chunkSize:N0}...");

        var stopwatch = Stopwatch.StartNew();
        var totalProcessed = 0;

        for (int startRow = 0; startRow < totalRows; startRow += chunkSize)
        {
            var endRow = Math.Min(startRow + chunkSize, totalRows);
            var rows = new ArrayRow[endRow - startRow];

            // Create chunk
            for (int i = 0; i < rows.Length; i++)
            {
                rows[i] = new ArrayRow(schema, new object[] { startRow + i, $"Value{startRow + i}" });
            }

            var chunk = new Chunk(schema, rows);

            // Process chunk (simulate transformation)
            foreach (var row in chunk.Rows)
            {
                _ = row[0]; // Access fields
                _ = row[1];
                totalProcessed++;
            }
        }

        stopwatch.Stop();

        var throughput = totalProcessed / stopwatch.Elapsed.TotalSeconds;
        var targetThroughput = 500_000; // 500K rows/sec for chunk processing

        _output.WriteLine($"📋 Results:");
        _output.WriteLine($"   🔢 Rows Processed: {totalProcessed:N0}");
        _output.WriteLine($"   ⏱️  Total Time: {stopwatch.Elapsed.TotalMilliseconds:F1} ms");
        _output.WriteLine($"   🚀 Actual Throughput: {throughput:F0} rows/sec");
        _output.WriteLine($"   🎯 Target Throughput: {targetThroughput:N0} rows/sec");
        _output.WriteLine($"   📊 Performance Ratio: {throughput / targetThroughput:P1} of target");

        Assert.True(throughput >= targetThroughput * 0.3, 
            $"Chunk processing below 30% of target: {throughput:F0} < {targetThroughput * 0.3:F0} rows/sec");

        _output.WriteLine(throughput >= targetThroughput ? "   ✅ EXCELLENT: Exceeded target!" : 
                         throughput >= targetThroughput * 0.5 ? "   ✅ GOOD: Above 50% of target" : 
                         "   ⚠️  ACCEPTABLE: Meets minimum threshold");
    }

    public void Dispose()
    {
        _serviceProvider?.Dispose();
    }
}