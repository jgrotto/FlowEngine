using BenchmarkDotNet.Attributes;
using FlowEngine.Benchmarks.DataStructures;

namespace FlowEngine.Benchmarks;

/// <summary>
/// Quick verification tests to ensure benchmarks work correctly.
/// Uses minimal iterations for fast feedback.
/// </summary>
[SimpleJob(iterationCount: 1, warmupCount: 1, invocationCount: 1000)]
[MemoryDiagnoser]
public class QuickVerificationTests
{
    private DictionaryRow _dictRow = null!;
    private ImmutableRow _immutableRow = null!;
    private ArrayRow _arrayRow = null!;
    private Schema _schema = null!;

    [GlobalSetup]
    public void Setup()
    {
        var data = new Dictionary<string, object?>
        {
            ["id"] = 123,
            ["name"] = "Test",
            ["email"] = "test@example.com"
        };

        _dictRow = new DictionaryRow(data);
        _immutableRow = new ImmutableRow(data);
        _schema = Schema.GetOrCreate(data.Keys.ToArray());
        _arrayRow = new ArrayRow(_schema, data.Values.ToArray());
    }

    [Benchmark(Baseline = true)]
    public object? DictRead() => _dictRow["email"];

    [Benchmark]
    public object? ArrayRead() => _arrayRow["email"];

    [Benchmark]
    public DictionaryRow DictTransform() => _dictRow.With("email", "new@example.com");

    [Benchmark]
    public ArrayRow ArrayTransform() => _arrayRow.With("email", "new@example.com");
}