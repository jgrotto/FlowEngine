using BenchmarkDotNet.Attributes;
using System.Collections.Immutable;

namespace FlowEngine.Benchmarks.DataStructures;

/// <summary>
/// Comprehensive benchmarks comparing Row implementation alternatives.
/// 
/// Tests the critical path operations:
/// - Property access (single field read)
/// - Row transformation (creating new row with modified field)
/// - Row creation from data
/// - Memory allocation patterns
/// 
/// Goal: Determine which Row implementation can meet 1M rows/sec target.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(iterationCount: 3, warmupCount: 1)]
public class RowBenchmarks
{
    private DictionaryRow _dictRow = null!;
    private ImmutableRow _immutableRow = null!;
    private ArrayRow _arrayRow = null!;
    private Schema _schema = null!;
    private Dictionary<string, object?> _sampleData = null!;
    private PooledRow _pooledRow = null!;

    [GlobalSetup]
    public void Setup()
    {
        // Create realistic customer data sample
        _sampleData = new Dictionary<string, object?>
        {
            ["customerId"] = 12345,
            ["firstName"] = "John",
            ["lastName"] = "Doe",
            ["email"] = "john.doe@example.com",
            ["age"] = 30,
            ["balance"] = 1234.56m,
            ["isActive"] = true,
            ["lastLogin"] = DateTime.Now,
            ["address"] = "123 Main St, Anytown, USA",
            ["phoneNumber"] = "+1-555-123-4567"
        };

        // Initialize all Row implementations
        _dictRow = new DictionaryRow(_sampleData);
        _immutableRow = new ImmutableRow(_sampleData);
        _schema = Schema.GetOrCreate(_sampleData.Keys.ToArray());
        _arrayRow = new ArrayRow(_schema, _sampleData.Values.ToArray());
        _pooledRow = PooledRow.Create(_sampleData);
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _pooledRow?.Dispose();
    }

    // =============================================================================
    // PROPERTY ACCESS BENCHMARKS (Critical for read-heavy operations)
    // =============================================================================

    [Benchmark(Baseline = true)]
    public object? Dictionary_Read()
    {
        return _dictRow["email"];
    }

    [Benchmark]
    public object? Immutable_Read()
    {
        return _immutableRow["email"];
    }

    [Benchmark]
    public object? Array_Read()
    {
        return _arrayRow["email"];
    }

    [Benchmark]
    public object? Pooled_Read()
    {
        return _pooledRow["email"];
    }

    // =============================================================================
    // TRANSFORMATION BENCHMARKS (Critical for transform operations)
    // =============================================================================

    [Benchmark]
    public DictionaryRow Dictionary_Transform()
    {
        return _dictRow.With("email", "new.email@example.com");
    }

    [Benchmark]
    public ImmutableRow Immutable_Transform()
    {
        return _immutableRow.With("email", "new.email@example.com");
    }

    [Benchmark]
    public ArrayRow Array_Transform()
    {
        return _arrayRow.With("email", "new.email@example.com");
    }

    [Benchmark]
    public PooledRow Pooled_Transform()
    {
        var result = _pooledRow.With("email", "new.email@example.com");
        result.Dispose(); // Cleanup for benchmarking
        return result;
    }

    // =============================================================================
    // CREATION BENCHMARKS (Critical for source operations)
    // =============================================================================

    [Benchmark]
    public DictionaryRow Dictionary_Create()
    {
        return new DictionaryRow(_sampleData);
    }

    [Benchmark]
    public ImmutableRow Immutable_Create()
    {
        return new ImmutableRow(_sampleData);
    }

    [Benchmark]
    public ArrayRow Array_Create()
    {
        return new ArrayRow(_schema, _sampleData.Values.ToArray());
    }

    [Benchmark]
    public PooledRow Pooled_Create()
    {
        var result = PooledRow.Create(_sampleData);
        result.Dispose(); // Cleanup for benchmarking
        return result;
    }

    // =============================================================================
    // BULK OPERATION BENCHMARKS (Simulates high-throughput scenarios)
    // =============================================================================

    [Benchmark]
    [Arguments(1000)]
    [Arguments(10000)]
    public long Dictionary_BulkTransform(int count)
    {
        long total = 0;
        for (int i = 0; i < count; i++)
        {
            var transformed = _dictRow.With("customerId", i);
            total += (int)transformed["customerId"]!;
        }
        return total;
    }

    [Benchmark]
    [Arguments(1000)]
    [Arguments(10000)]
    public long Immutable_BulkTransform(int count)
    {
        long total = 0;
        for (int i = 0; i < count; i++)
        {
            var transformed = _immutableRow.With("customerId", i);
            total += (int)transformed["customerId"]!;
        }
        return total;
    }

    [Benchmark]
    [Arguments(1000)]
    [Arguments(10000)]
    public long Array_BulkTransform(int count)
    {
        long total = 0;
        for (int i = 0; i < count; i++)
        {
            var transformed = _arrayRow.With("customerId", i);
            total += (int)transformed["customerId"]!;
        }
        return total;
    }

    [Benchmark]
    [Arguments(1000)]
    [Arguments(10000)]
    public long Pooled_BulkTransform(int count)
    {
        long total = 0;
        for (int i = 0; i < count; i++)
        {
            using var transformed = _pooledRow.With("customerId", i);
            total += (int)transformed["customerId"]!;
        }
        return total;
    }

    // =============================================================================
    // EQUALITY COMPARISON BENCHMARKS (For fan-out scenarios)
    // =============================================================================

    [Benchmark]
    public bool Dictionary_Equality()
    {
        var copy = new DictionaryRow(_sampleData);
        return _dictRow.Equals(copy);
    }

    [Benchmark]
    public bool Immutable_Equality()
    {
        var copy = new ImmutableRow(_sampleData);
        return _immutableRow.Equals(copy);
    }

    [Benchmark]
    public bool Array_Equality()
    {
        var copy = new ArrayRow(_schema, _sampleData.Values.ToArray());
        return _arrayRow.Equals(copy);
    }

    // =============================================================================
    // CHAIN OPERATION BENCHMARKS (Multiple transformations)
    // =============================================================================

    [Benchmark]
    public DictionaryRow Dictionary_ChainedTransforms()
    {
        return _dictRow
            .With("email", "updated@example.com")
            .With("age", 31)
            .With("isActive", false)
            .With("balance", 2000.00m);
    }

    [Benchmark]
    public ImmutableRow Immutable_ChainedTransforms()
    {
        return _immutableRow
            .With("email", "updated@example.com")
            .With("age", 31)
            .With("isActive", false)
            .With("balance", 2000.00m);
    }

    [Benchmark]
    public ArrayRow Array_ChainedTransforms()
    {
        return _arrayRow
            .With("email", "updated@example.com")
            .With("age", 31)
            .With("isActive", false)
            .With("balance", 2000.00m);
    }
}

/// <summary>
/// Specialized benchmarks for different data sizes and schemas.
/// Tests performance characteristics across different row complexities.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(iterationCount: 3, warmupCount: 1)]
public class RowSizeBenchmarks
{
    private Dictionary<string, object?>[] _testData = null!;
    private Schema[] _schemas = null!;

    [Params(5, 20, 50)]
    public int ColumnCount { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _testData = new Dictionary<string, object?>[3];
        _schemas = new Schema[3];

        // Create test data with different column counts
        var columnCounts = new[] { 5, 20, 50 };
        for (int i = 0; i < columnCounts.Length; i++)
        {
            var data = new Dictionary<string, object?>();
            var columns = new string[columnCounts[i]];
            
            for (int j = 0; j < columnCounts[i]; j++)
            {
                var colName = $"column_{j}";
                columns[j] = colName;
                data[colName] = $"value_{j}";
            }
            
            _testData[i] = data;
            _schemas[i] = Schema.GetOrCreate(columns);
        }
    }

    [Benchmark]
    public DictionaryRow Dictionary_ByColumnCount()
    {
        var index = ColumnCount == 5 ? 0 : ColumnCount == 20 ? 1 : 2;
        return new DictionaryRow(_testData[index]);
    }

    [Benchmark]
    public ArrayRow Array_ByColumnCount()
    {
        var index = ColumnCount == 5 ? 0 : ColumnCount == 20 ? 1 : 2;
        return new ArrayRow(_schemas[index], _testData[index].Values.ToArray());
    }
}