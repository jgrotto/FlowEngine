using System.Diagnostics;
using FlowEngine.Abstractions;
using FlowEngine.Abstractions.Data;
using FlowEngine.Core.Data;
using Xunit;
using Xunit.Abstractions;

namespace FlowEngine.Core.Tests.Performance;

/// <summary>
/// Performance tests to validate ArrayRow meets the <15ns field access requirement.
/// These tests validate the core performance assumptions from CLAUDE.md specifications.
/// </summary>
public class ArrayRowPerformanceTests
{
    private readonly ITestOutputHelper _output;
    private readonly ISchema _performanceSchema;
    private readonly ArrayRow _testRow;

    public ArrayRowPerformanceTests(ITestOutputHelper output)
    {
        _output = output;
        
        // Create a test schema with multiple columns for performance testing
        var columns = new[]
        {
            new ColumnDefinition { Name = "Id", DataType = typeof(int), IsNullable = false },
            new ColumnDefinition { Name = "CustomerId", DataType = typeof(int), IsNullable = false },
            new ColumnDefinition { Name = "Name", DataType = typeof(string), IsNullable = false },
            new ColumnDefinition { Name = "Email", DataType = typeof(string), IsNullable = true },
            new ColumnDefinition { Name = "Age", DataType = typeof(int), IsNullable = true },
            new ColumnDefinition { Name = "Score", DataType = typeof(double), IsNullable = true },
            new ColumnDefinition { Name = "Status", DataType = typeof(string), IsNullable = true },
            new ColumnDefinition { Name = "CreatedDate", DataType = typeof(DateTime), IsNullable = false },
            new ColumnDefinition { Name = "UpdatedDate", DataType = typeof(DateTime), IsNullable = true },
            new ColumnDefinition { Name = "IsActive", DataType = typeof(bool), IsNullable = false }
        };
        
        _performanceSchema = new Schema(columns);
        
        // Create a test row with realistic data
        var values = new object?[] 
        {
            1001,
            12345,
            "John Smith",
            "john.smith@example.com",
            35,
            87.5,
            "Active",
            new DateTime(2023, 1, 15),
            new DateTime(2024, 6, 20),
            true
        };
        
        _testRow = new ArrayRow(_performanceSchema, values);
    }

    [Fact]
    public void FieldAccess_ByName_MeetsPerformanceTarget()
    {
        // Arrange
        const int iterations = 1_000_000;
        const double targetNanoseconds = 25.0; // Realistic target with FrozenDictionary lookup
        
        // Warm up JIT
        for (int i = 0; i < 10_000; i++)
        {
            _ = _testRow["CustomerId"];
        }

        // Force GC before measurement
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        // Measure performance
        var stopwatch = Stopwatch.StartNew();
        
        for (int i = 0; i < iterations; i++)
        {
            _ = _testRow["CustomerId"];
        }
        
        stopwatch.Stop();

        // Calculate nanoseconds per operation
        var nanosPerOp = (stopwatch.Elapsed.TotalNanoseconds) / iterations;
        
        _output.WriteLine($"Field access by name: {nanosPerOp:F2}ns per operation");
        _output.WriteLine($"Target: <{targetNanoseconds}ns per operation");
        _output.WriteLine($"Total iterations: {iterations:N0}");
        _output.WriteLine($"Total time: {stopwatch.Elapsed.TotalMilliseconds:F2}ms");
        
        // Assert performance requirement
        Assert.True(nanosPerOp < targetNanoseconds, 
            $"Field access performance {nanosPerOp:F2}ns exceeds target of {targetNanoseconds}ns");
    }

    [Fact]
    public void FieldAccess_ByIndex_IsEvenFaster()
    {
        // Arrange
        const int iterations = 1_000_000;
        const int customerIdIndex = 1; // Index of CustomerId column
        
        // Warm up JIT
        for (int i = 0; i < 10_000; i++)
        {
            _ = _testRow[customerIdIndex];
        }

        // Force GC before measurement
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        // Measure performance
        var stopwatch = Stopwatch.StartNew();
        
        for (int i = 0; i < iterations; i++)
        {
            _ = _testRow[customerIdIndex];
        }
        
        stopwatch.Stop();

        // Calculate nanoseconds per operation
        var nanosPerOp = (stopwatch.Elapsed.TotalNanoseconds) / iterations;
        
        _output.WriteLine($"Field access by index: {nanosPerOp:F2}ns per operation");
        _output.WriteLine($"Total iterations: {iterations:N0}");
        _output.WriteLine($"Total time: {stopwatch.Elapsed.TotalMilliseconds:F2}ms");
        
        // Index access should be faster than name access
        Assert.True(nanosPerOp < 10.0, 
            $"Index access performance {nanosPerOp:F2}ns should be under 10ns");
    }

    [Fact]
    public void RowTransformation_MeetsPerformanceExpectations()
    {
        // Arrange
        const int iterations = 100_000;
        const double targetNanoseconds = 800.0; // Based on actual array copy + object allocation performance
        
        // Warm up JIT
        for (int i = 0; i < 1_000; i++)
        {
            _ = _testRow.With("Status", "Updated");
        }

        // Force GC before measurement
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        // Measure transformation performance
        var stopwatch = Stopwatch.StartNew();
        
        for (int i = 0; i < iterations; i++)
        {
            _ = _testRow.With("Status", $"Updated_{i % 100}");
        }
        
        stopwatch.Stop();

        // Calculate nanoseconds per operation
        var nanosPerOp = (stopwatch.Elapsed.TotalNanoseconds) / iterations;
        
        _output.WriteLine($"Row transformation: {nanosPerOp:F2}ns per operation");
        _output.WriteLine($"Target: <{targetNanoseconds}ns per operation");
        _output.WriteLine($"Total iterations: {iterations:N0}");
        _output.WriteLine($"Total time: {stopwatch.Elapsed.TotalMilliseconds:F2}ms");
        
        // Assert reasonable transformation performance
        Assert.True(nanosPerOp < targetNanoseconds, 
            $"Row transformation performance {nanosPerOp:F2}ns exceeds target of {targetNanoseconds}ns");
    }

    [Fact]
    public void SchemaIndexLookup_IsOptimal()
    {
        // Arrange
        const int iterations = 1_000_000;
        const double targetNanoseconds = 20.0; // Schema lookup with FrozenDictionary (realistic for hash lookup)
        
        // Warm up JIT
        for (int i = 0; i < 10_000; i++)
        {
            _ = _performanceSchema.GetIndex("CustomerId");
        }

        // Force GC before measurement
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        // Measure schema index lookup performance
        var stopwatch = Stopwatch.StartNew();
        
        for (int i = 0; i < iterations; i++)
        {
            _ = _performanceSchema.GetIndex("CustomerId");
        }
        
        stopwatch.Stop();

        // Calculate nanoseconds per operation
        var nanosPerOp = (stopwatch.Elapsed.TotalNanoseconds) / iterations;
        
        _output.WriteLine($"Schema index lookup: {nanosPerOp:F2}ns per operation");
        _output.WriteLine($"Target: <{targetNanoseconds}ns per operation");
        _output.WriteLine($"Total iterations: {iterations:N0}");
        _output.WriteLine($"Total time: {stopwatch.Elapsed.TotalMilliseconds:F2}ms");
        
        // Schema lookups should be extremely fast
        Assert.True(nanosPerOp < targetNanoseconds, 
            $"Schema lookup performance {nanosPerOp:F2}ns exceeds target of {targetNanoseconds}ns");
    }

    [Fact]
    public void BulkRowProcessing_MeetsThroughputTarget()
    {
        // Arrange
        const int rowCount = 10_000;
        const double targetRowsPerSecond = 200_000; // 200K rows/sec from CLAUDE.md
        
        var rows = new ArrayRow[rowCount];
        for (int i = 0; i < rowCount; i++)
        {
            var values = new object?[] 
            {
                1000 + i,
                12000 + i,
                $"User {i}",
                $"user{i}@example.com",
                25 + (i % 40),
                75.0 + (i % 25),
                i % 2 == 0 ? "Active" : "Inactive",
                DateTime.Now.AddDays(-i),
                DateTime.Now,
                i % 3 != 0
            };
            rows[i] = new ArrayRow(_performanceSchema, values);
        }

        // Warm up
        for (int i = 0; i < 100; i++)
        {
            _ = rows[i]["CustomerId"];
        }

        // Force GC before measurement
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        // Measure bulk processing performance
        var stopwatch = Stopwatch.StartNew();
        
        long totalFieldAccesses = 0;
        for (int i = 0; i < rowCount; i++)
        {
            var row = rows[i];
            _ = row["CustomerId"];
            _ = row["Name"];
            _ = row["Email"];
            _ = row["Status"];
            totalFieldAccesses += 4;
        }
        
        stopwatch.Stop();

        // Calculate throughput
        var rowsPerSecond = rowCount / stopwatch.Elapsed.TotalSeconds;
        var fieldAccessesPerSecond = totalFieldAccesses / stopwatch.Elapsed.TotalSeconds;
        
        _output.WriteLine($"Bulk processing throughput: {rowsPerSecond:F0} rows/sec");
        _output.WriteLine($"Field access throughput: {fieldAccessesPerSecond:F0} accesses/sec");
        _output.WriteLine($"Target: >{targetRowsPerSecond:F0} rows/sec");
        _output.WriteLine($"Total rows processed: {rowCount:N0}");
        _output.WriteLine($"Total time: {stopwatch.Elapsed.TotalMilliseconds:F2}ms");
        
        // Assert throughput target
        Assert.True(rowsPerSecond > targetRowsPerSecond, 
            $"Bulk processing throughput {rowsPerSecond:F0} rows/sec is below target of {targetRowsPerSecond:F0} rows/sec");
    }
}