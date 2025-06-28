using BenchmarkDotNet.Attributes;
using FlowEngine.Benchmarks.DataStructures;

namespace FlowEngine.Benchmarks.Pipeline;

/// <summary>
/// Staged scaling benchmarks: 10K → 100K → 1M records
/// Tests performance curve and validates 1M rows/sec target
/// </summary>
[MemoryDiagnoser]
[SimpleJob(iterationCount: 3, warmupCount: 1)]
public class StagedScalingBenchmarks
{
    private MockCsvSource _csvSource = null!;
    private EmployeeTransformStep _transformStep = null!;
    private DepartmentAggregationStep _aggregationStep = null!;
    
    [Params(10000, 100000, 1000000)]  // 10K → 100K → 1M progression
    public int RecordCount { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _csvSource = new MockCsvSource();
        _transformStep = new EmployeeTransformStep();
        _aggregationStep = new DepartmentAggregationStep();
        
        Console.WriteLine($"=== Starting Staged Scaling Test: {RecordCount:N0} records ===");
        Console.WriteLine($"Target: {RecordCount / 1000.0:F1}ms for 1M rows/sec equivalent");
    }

    [Benchmark(Baseline = true)]
    public Dictionary<string, object> DictionaryRowPipeline()
    {
        var sourceData = _csvSource.GenerateEmployeeData(RecordCount);
        
        // Convert to DictionaryRow and process through pipeline
        var processedRows = sourceData
            .Select(data => new DictionaryRow(data))
            .Cast<IRow>()
            .Select(row => _transformStep.ProcessEmployee(row))
            .ToList();
        
        return _aggregationStep.AggregateByDepartment(processedRows);
    }

    [Benchmark]
    public Dictionary<string, object> ArrayRowPipeline()
    {
        var sourceData = _csvSource.GenerateEmployeeData(RecordCount);
        
        // Create schema and convert to ArrayRow
        var firstRecord = sourceData.First();
        var schema = Schema.GetOrCreate(firstRecord.Keys.ToArray());
        
        var processedRows = sourceData
            .Select(data => new ArrayRow(schema, data.Values.ToArray()))
            .Cast<IRow>()
            .Select(row => _transformStep.ProcessEmployee(row))
            .ToList();
        
        return _aggregationStep.AggregateByDepartment(processedRows);
    }

    // Only test ImmutableRow for smaller datasets due to expected poor performance
    [Benchmark]
    public Dictionary<string, object> ImmutableRowPipeline()
    {
        if (RecordCount > 100000)
        {
            // Skip ImmutableRow for 1M test to save time - we expect it to be slowest
            return new Dictionary<string, object> { ["skipped"] = true };
        }

        var sourceData = _csvSource.GenerateEmployeeData(RecordCount);
        
        var processedRows = sourceData
            .Select(data => new ImmutableRow(data))
            .Cast<IRow>()
            .Select(row => _transformStep.ProcessEmployee(row))
            .ToList();
        
        return _aggregationStep.AggregateByDepartment(processedRows);
    }

    // Only test PooledRow for smaller datasets initially
    [Benchmark]
    public Dictionary<string, object> PooledRowPipeline()
    {
        if (RecordCount > 100000)
        {
            // Skip PooledRow for 1M test initially - can re-enable if it performs well at 100K
            return new Dictionary<string, object> { ["skipped"] = true };
        }

        var sourceData = _csvSource.GenerateEmployeeData(RecordCount);
        var processedRows = new List<IRow>();
        
        foreach (var data in sourceData)
        {
            using var row = PooledRow.Create(data);
            var transformed = _transformStep.ProcessEmployee(row);
            processedRows.Add(transformed);
        }
        
        return _aggregationStep.AggregateByDepartment(processedRows);
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        Console.WriteLine($"=== Completed {RecordCount:N0} records test ===");
        
        // Force GC to get accurate memory readings
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        
        var memoryAfter = GC.GetTotalMemory(false);
        Console.WriteLine($"Memory after cleanup: {memoryAfter / 1024 / 1024:F1} MB");
    }
}

/// <summary>
/// Streaming chunked processing benchmarks for large datasets
/// Tests memory-bounded processing approach
/// </summary>
[MemoryDiagnoser]
[SimpleJob(iterationCount: 3, warmupCount: 1)]
public class ChunkedProcessingBenchmarks
{
    private MockCsvSource _csvSource = null!;
    private EmployeeTransformStep _transformStep = null!;
    private DepartmentAggregationStep _aggregationStep = null!;
    
    [Params(100000, 1000000)]  // Test chunking at scale
    public int RecordCount { get; set; }
    
    [Params(1000, 5000, 10000)]  // Different chunk sizes
    public int ChunkSize { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _csvSource = new MockCsvSource();
        _transformStep = new EmployeeTransformStep();
        _aggregationStep = new DepartmentAggregationStep();
        
        Console.WriteLine($"=== Chunked Processing Test: {RecordCount:N0} records, {ChunkSize:N0} chunk size ===");
    }

    [Benchmark(Baseline = true)]
    public Dictionary<string, object> MaterializedProcessing()
    {
        // Load all data into memory at once (baseline - memory intensive)
        var allData = _csvSource.GenerateEmployeeData(RecordCount).ToList();
        var allRows = allData.Select(data => new DictionaryRow(data)).ToList();
        
        // Process all rows
        var transformedRows = allRows.Select(row => _transformStep.ProcessEmployee(row)).ToList();
        
        // Aggregate
        return _aggregationStep.AggregateByDepartment(transformedRows);
    }

    [Benchmark]
    public Dictionary<string, object> ChunkedDictionaryProcessing()
    {
        // Process data in chunks to control memory usage
        var allProcessedRows = new List<IRow>();
        
        var sourceData = _csvSource.GenerateEmployeeData(RecordCount);
        var chunks = sourceData.Chunk(ChunkSize);
        
        foreach (var chunk in chunks)
        {
            var chunkRows = chunk.Select(data => new DictionaryRow(data)).ToList();
            var transformedChunk = chunkRows.Select(row => _transformStep.ProcessEmployee(row)).ToList();
            allProcessedRows.AddRange(transformedChunk);
            
            // Optional: Force GC between chunks to measure peak memory
            if (allProcessedRows.Count % (ChunkSize * 10) == 0)
            {
                GC.Collect(0, GCCollectionMode.Optimized);
            }
        }
        
        return _aggregationStep.AggregateByDepartment(allProcessedRows);
    }

    [Benchmark]
    public Dictionary<string, object> ChunkedArrayProcessing()
    {
        // Process data in chunks using ArrayRow
        var allProcessedRows = new List<IRow>();
        Schema? schema = null;
        
        var sourceData = _csvSource.GenerateEmployeeData(RecordCount);
        var chunks = sourceData.Chunk(ChunkSize);
        
        foreach (var chunk in chunks)
        {
            // Create schema from first chunk
            if (schema == null)
            {
                var firstRecord = chunk.First();
                schema = Schema.GetOrCreate(firstRecord.Keys.ToArray());
            }

            var chunkRows = chunk.Select(data => new ArrayRow(schema, data.Values.ToArray())).ToList();
            var transformedChunk = chunkRows.Select(row => _transformStep.ProcessEmployee(row)).ToList();
            allProcessedRows.AddRange(transformedChunk);
            
            // Optional: Force GC between chunks
            if (allProcessedRows.Count % (ChunkSize * 10) == 0)
            {
                GC.Collect(0, GCCollectionMode.Optimized);
            }
        }
        
        return _aggregationStep.AggregateByDepartment(allProcessedRows);
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        Console.WriteLine($"=== Completed chunked processing: {RecordCount:N0} records ===");
    }
}

/// <summary>
/// Performance analysis helper - run after benchmarks to analyze results
/// </summary>
public static class PerformanceAnalyzer
{
    public static void AnalyzeScalingResults()
    {
        Console.WriteLine("=== Performance Analysis ===");
        Console.WriteLine();
        Console.WriteLine("Target Analysis:");
        Console.WriteLine("- 1M rows/sec = 1 millisecond per 1K records");
        Console.WriteLine("- 10K records should complete in ~10ms");
        Console.WriteLine("- 100K records should complete in ~100ms"); 
        Console.WriteLine("- 1M records should complete in ~1000ms (1 second)");
        Console.WriteLine();
        Console.WriteLine("Key Metrics to Review:");
        Console.WriteLine("1. Linear scaling: Does time increase proportionally?");
        Console.WriteLine("2. Memory efficiency: GC collections and memory allocation");
        Console.WriteLine("3. Implementation comparison: ArrayRow vs DictionaryRow performance gap");
        Console.WriteLine("4. Chunking effectiveness: Memory-bounded vs materialized processing");
        Console.WriteLine();
    }
    
    public static void PrintDecisionMatrix()
    {
        Console.WriteLine("=== Implementation Decision Matrix ===");
        Console.WriteLine();
        Console.WriteLine("Criteria              | DictionaryRow | ArrayRow | ImmutableRow | PooledRow");
        Console.WriteLine("---------------------|---------------|----------|--------------|----------");
        Console.WriteLine("Performance           | Baseline      | ?        | ?            | ?");
        Console.WriteLine("Memory Efficiency     | ?             | ?        | ?            | ?");
        Console.WriteLine("Ease of Use          | High          | Medium   | High         | Low");
        Console.WriteLine("Type Safety          | Low           | High     | Low          | Low");
        Console.WriteLine("Schema Flexibility   | High          | Low      | High         | High");
        Console.WriteLine();
        Console.WriteLine("Fill in the '?' based on benchmark results!");
    }
}