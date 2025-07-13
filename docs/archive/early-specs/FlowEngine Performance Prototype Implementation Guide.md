# FlowEngine Performance Prototype Implementation Guide

**Purpose**: Validate core architectural decisions through focused performance testing  
**Duration**: 5-7 days  
**Goal**: Data-driven decisions for production implementation

---

## 🎯 Executive Summary

This guide provides a structured approach to building performance prototypes that will validate FlowEngine's core design assumptions. We'll focus on answering critical questions while avoiding premature optimization.

### Developer Concerns Assessment

**Valid Concerns** ✅:
- Row immutability performance impact
- Memory pooling strategy for reference types  
- Channel scalability for fan-out scenarios
- Cross-platform path handling complexity

**Potential Overreach** ⚠️:
- Plugin assembly loading complexity (can be simplified for V1)
- Full resource governance implementation (can start with monitoring only)
- JavaScript engine pooling optimization (defer until transform performance is proven)
- Schema evolution handling (V2 feature, not needed for V1)

---

## 📋 Prototype Task List

### **Day 1-2: Core Data Structure Benchmarks**

#### Task 1.1: Create Benchmark Project
```bash
# Create benchmark project structure
dotnet new console -n FlowEngine.Benchmarks
cd FlowEngine.Benchmarks
dotnet add package BenchmarkDotNet
dotnet add package System.Collections.Immutable
dotnet add package Microsoft.Extensions.ObjectPool
```

#### Task 1.2: Implement Row Alternatives
```csharp
// File: RowImplementations.cs
namespace FlowEngine.Benchmarks.DataStructures;

// Option 1: Current Design (Baseline)
public sealed class DictionaryRow
{
    private readonly Dictionary<string, object?> _data;
    
    public DictionaryRow(Dictionary<string, object?> data)
    {
        _data = new Dictionary<string, object?>(data);
    }
    
    public object? this[string key] => _data.GetValueOrDefault(key);
    
    public DictionaryRow With(string key, object? value)
    {
        var newData = new Dictionary<string, object?>(_data) { [key] = value };
        return new DictionaryRow(newData);
    }
}

// Option 2: Immutable Dictionary with Sharing
public sealed class ImmutableRow
{
    private readonly ImmutableDictionary<string, object?> _data;
    
    public ImmutableRow(ImmutableDictionary<string, object?> data)
    {
        _data = data;
    }
    
    public object? this[string key] => _data.GetValueOrDefault(key);
    
    public ImmutableRow With(string key, object? value)
    {
        return new ImmutableRow(_data.SetItem(key, value));
    }
}

// Option 3: Array-based with Schema (Fastest)
public sealed class ArrayRow
{
    private readonly object?[] _values;
    private readonly Schema _schema;
    
    public ArrayRow(Schema schema, object?[] values)
    {
        _schema = schema;
        _values = (object?[])values.Clone();
    }
    
    public object? this[string key]
    {
        get
        {
            var index = _schema.GetIndex(key);
            return index >= 0 ? _values[index] : null;
        }
    }
    
    public ArrayRow With(string key, object? value)
    {
        var newValues = (object?[])_values.Clone();
        var index = _schema.GetIndex(key);
        if (index >= 0) newValues[index] = value;
        return new ArrayRow(_schema, newValues);
    }
}

// Option 4: Pooled Mutable Row with Immutable Interface
public sealed class PooledRow : IDisposable
{
    private static readonly ObjectPool<PooledRow> Pool = 
        new DefaultObjectPoolProvider().Create<PooledRow>();
    
    private Dictionary<string, object?> _data = new();
    private bool _disposed;
    
    public static PooledRow Create(IEnumerable<KeyValuePair<string, object?>> data)
    {
        var row = Pool.Get();
        row._data.Clear();
        foreach (var kvp in data)
            row._data[kvp.Key] = kvp.Value;
        return row;
    }
    
    public object? this[string key] => _data.GetValueOrDefault(key);
    
    public PooledRow Clone()
    {
        return Create(_data);
    }
    
    public void Dispose()
    {
        if (!_disposed)
        {
            Pool.Return(this);
            _disposed = true;
        }
    }
}
```

#### Task 1.3: Create Row Benchmarks
```csharp
// File: RowBenchmarks.cs
[MemoryDiagnoser]
[SimpleJob(RuntimeMoniker.Net80)]
public class RowBenchmarks
{
    private DictionaryRow _dictRow;
    private ImmutableRow _immutableRow;
    private ArrayRow _arrayRow;
    private Schema _schema;
    
    [GlobalSetup]
    public void Setup()
    {
        var data = new Dictionary<string, object?>
        {
            ["id"] = 12345,
            ["name"] = "John Doe",
            ["email"] = "john@example.com",
            ["age"] = 30,
            ["balance"] = 1234.56m
        };
        
        _dictRow = new DictionaryRow(data);
        _immutableRow = new ImmutableRow(data.ToImmutableDictionary());
        _schema = new Schema(data.Keys.ToArray());
        _arrayRow = new ArrayRow(_schema, data.Values.ToArray());
    }
    
    [Benchmark(Baseline = true)]
    public object? DictionaryRead()
    {
        return _dictRow["email"];
    }
    
    [Benchmark]
    public DictionaryRow DictionaryTransform()
    {
        return _dictRow.With("email", "newemail@example.com");
    }
    
    [Benchmark]
    public object? ImmutableRead()
    {
        return _immutableRow["email"];
    }
    
    [Benchmark]
    public ImmutableRow ImmutableTransform()
    {
        return _immutableRow.With("email", "newemail@example.com");
    }
    
    [Benchmark]
    public object? ArrayRead()
    {
        return _arrayRow["email"];
    }
    
    [Benchmark]
    public ArrayRow ArrayTransform()
    {
        return _arrayRow.With("email", "newemail@example.com");
    }
    
    [Benchmark]
    public void PooledLifecycle()
    {
        using var row = PooledRow.Create(new[] 
        { 
            new KeyValuePair<string, object?>("email", "test@example.com") 
        });
        var value = row["email"];
    }
}
```

### **Day 2-3: Port Communication & Streaming**

#### Task 2.1: Port Implementation Alternatives
```csharp
// File: PortImplementations.cs

// Option 1: Channel-based (Current Design)
public class ChannelPort
{
    private readonly Channel<Dataset> _channel;
    
    public ChannelPort(int capacity = 100)
    {
        _channel = Channel.CreateBounded<Dataset>(new BoundedChannelOptions(capacity)
        {
            FullMode = BoundedChannelFullMode.Wait
        });
    }
    
    public async ValueTask SendAsync(Dataset data)
    {
        await _channel.Writer.WriteAsync(data);
    }
    
    public IAsyncEnumerable<Dataset> ReceiveAsync()
    {
        return _channel.Reader.ReadAllAsync();
    }
}

// Option 2: Direct Callback (Simpler)
public class CallbackPort
{
    private readonly List<Func<Dataset, ValueTask>> _receivers = new();
    
    public void Subscribe(Func<Dataset, ValueTask> receiver)
    {
        _receivers.Add(receiver);
    }
    
    public async ValueTask SendAsync(Dataset data)
    {
        var tasks = _receivers.Select(r => r(data).AsTask());
        await Task.WhenAll(tasks);
    }
}

// Option 3: Batched Delivery
public class BatchedPort
{
    private readonly Channel<Dataset[]> _channel;
    private readonly List<Dataset> _buffer = new();
    private readonly int _batchSize;
    
    public BatchedPort(int batchSize = 100)
    {
        _batchSize = batchSize;
        _channel = Channel.CreateUnbounded<Dataset[]>();
    }
    
    public async ValueTask SendAsync(Dataset data)
    {
        _buffer.Add(data);
        if (_buffer.Count >= _batchSize)
        {
            await _channel.Writer.WriteAsync(_buffer.ToArray());
            _buffer.Clear();
        }
    }
}
```

#### Task 2.2: Streaming Performance Tests
```csharp
// File: StreamingBenchmarks.cs
[MemoryDiagnoser]
public class StreamingBenchmarks
{
    [Params(1, 10, 100)]
    public int FanOutCount { get; set; }
    
    [Params(1000, 10000)]
    public int RowCount { get; set; }
    
    [Benchmark]
    public async Task ChannelFanOut()
    {
        var source = new ChannelPort();
        var sinks = Enumerable.Range(0, FanOutCount)
            .Select(_ => new ChannelPort())
            .ToList();
        
        // Wire up fan-out
        var fanOutTask = Task.Run(async () =>
        {
            await foreach (var data in source.ReceiveAsync())
            {
                await Task.WhenAll(sinks.Select(s => s.SendAsync(data).AsTask()));
            }
        });
        
        // Send data
        for (int i = 0; i < RowCount; i++)
        {
            await source.SendAsync(new Dataset { RowCount = 1 });
        }
    }
    
    [Benchmark]
    public async Task DirectCallbackFanOut()
    {
        var source = new CallbackPort();
        var sinkCount = 0;
        
        // Wire up receivers
        for (int i = 0; i < FanOutCount; i++)
        {
            source.Subscribe(async data =>
            {
                Interlocked.Increment(ref sinkCount);
                await Task.Yield(); // Simulate work
            });
        }
        
        // Send data
        for (int i = 0; i < RowCount; i++)
        {
            await source.SendAsync(new Dataset { RowCount = 1 });
        }
    }
}
```

### **Day 3-4: Memory Management & Spillover**

#### Task 3.1: Memory Pressure Simulation
```csharp
// File: MemoryManagementTests.cs
public class MemoryPressureSimulator
{
    private readonly List<byte[]> _memoryHog = new();
    
    public void SimulateMemoryPressure(int targetMB)
    {
        // Allocate memory to simulate pressure
        var bytesPerMB = 1024 * 1024;
        for (int i = 0; i < targetMB; i++)
        {
            _memoryHog.Add(new byte[bytesPerMB]);
        }
    }
    
    public MemoryPressureInfo GetMemoryInfo()
    {
        var gcInfo = GC.GetGCMemoryInfo();
        var totalMemory = GC.GetTotalMemory(false);
        
        return new MemoryPressureInfo
        {
            TotalMemoryBytes = totalMemory,
            HeapSizeBytes = gcInfo.HeapSizeBytes,
            HighMemoryLoadThresholdBytes = gcInfo.HighMemoryLoadThresholdBytes,
            MemoryLoadPercent = (double)totalMemory / gcInfo.HighMemoryLoadThresholdBytes * 100
        };
    }
}

[Fact]
public async Task TestSpilloverTrigger()
{
    var simulator = new MemoryPressureSimulator();
    var memoryManager = new MemoryManager(spilloverThreshold: 0.7); // 70%
    
    // Process data until spillover
    var spilledCount = 0;
    memoryManager.OnSpillover += () => spilledCount++;
    
    while (spilledCount == 0)
    {
        // Allocate rows
        var rows = Enumerable.Range(0, 1000)
            .Select(i => new DictionaryRow(new Dictionary<string, object?> 
            { 
                ["id"] = i,
                ["data"] = new string('x', 1000) // 1KB per row
            }))
            .ToList();
        
        // Check if should spill
        if (memoryManager.ShouldSpillToDisk())
        {
            await memoryManager.SpillToFileAsync(rows);
        }
        
        // Simulate memory pressure
        simulator.SimulateMemoryPressure(10); // Add 10MB
    }
    
    Assert.True(spilledCount > 0, "Spillover should have been triggered");
}
```

#### Task 3.2: Pooling Efficiency Tests
```csharp
// File: PoolingBenchmarks.cs
[MemoryDiagnoser]
[GcServer(true)]
public class PoolingBenchmarks
{
    private ObjectPool<Row> _objectPool;
    private ArrayPool<Row> _arrayPool; // Note: This is incorrect usage
    private ConcurrentBag<Row> _customPool;
    
    [GlobalSetup]
    public void Setup()
    {
        _objectPool = new DefaultObjectPoolProvider()
            .Create(new RowPooledObjectPolicy());
        _customPool = new ConcurrentBag<Row>();
    }
    
    [Benchmark(Baseline = true)]
    public void NoPooling()
    {
        for (int i = 0; i < 1000; i++)
        {
            var row = new Row(); // Direct allocation
            // Simulate usage
            row.Process();
        }
    }
    
    [Benchmark]
    public void WithObjectPool()
    {
        for (int i = 0; i < 1000; i++)
        {
            var row = _objectPool.Get();
            try
            {
                row.Process();
            }
            finally
            {
                _objectPool.Return(row);
            }
        }
    }
    
    [Benchmark]
    public void WithCustomPool()
    {
        for (int i = 0; i < 1000; i++)
        {
            if (!_customPool.TryTake(out var row))
            {
                row = new Row();
            }
            
            try
            {
                row.Process();
            }
            finally
            {
                _customPool.Add(row);
            }
        }
    }
}
```

### **Day 4-5: Integration & Real-World Scenarios**

#### Task 4.1: End-to-End Pipeline Test
```csharp
// File: PipelinePrototype.cs
public class PipelinePrototype
{
    public async Task<PipelineResult> RunPipelineAsync(PipelineConfig config)
    {
        var stopwatch = Stopwatch.StartNew();
        var stats = new PipelineStatistics();
        
        // Source -> Transform -> Sink
        var source = new CsvSource(config.InputFile);
        var transform = new SimpleTransform(row => 
        {
            // Simulate transformation
            return row.With("processed", true);
        });
        var sink = new CsvSink(config.OutputFile);
        
        // Wire up pipeline
        var channel = Channel.CreateBounded<Row>(config.BufferSize);
        
        // Source task
        var sourceTask = Task.Run(async () =>
        {
            await foreach (var row in source.ReadAsync())
            {
                await channel.Writer.WriteAsync(row);
                stats.RowsRead++;
            }
            channel.Writer.Complete();
        });
        
        // Transform & sink task
        var sinkTask = Task.Run(async () =>
        {
            await foreach (var row in channel.Reader.ReadAllAsync())
            {
                var transformed = transform.Process(row);
                await sink.WriteAsync(transformed);
                stats.RowsWritten++;
            }
        });
        
        await Task.WhenAll(sourceTask, sinkTask);
        
        stopwatch.Stop();
        stats.Duration = stopwatch.Elapsed;
        stats.ThroughputRowsPerSec = stats.RowsRead / stopwatch.Elapsed.TotalSeconds;
        
        return new PipelineResult { Statistics = stats };
    }
}

[Fact]
public async Task MeasureRealisticThroughput()
{
    // Create test file with realistic data
    var testFile = await CreateTestCsvFile(
        rows: 100_000,
        columns: 20,
        avgCellSize: 50
    );
    
    var config = new PipelineConfig
    {
        InputFile = testFile,
        OutputFile = "output.csv",
        BufferSize = 1000
    };
    
    var result = await new PipelinePrototype().RunPipelineAsync(config);
    
    Console.WriteLine($"Throughput: {result.Statistics.ThroughputRowsPerSec:N0} rows/sec");
    Console.WriteLine($"Memory used: {result.Statistics.PeakMemoryMB:N2} MB");
    
    // Establish baseline for this hardware
    Assert.True(result.Statistics.ThroughputRowsPerSec > 10_000, 
        "Should process at least 10K rows/sec on laptop");
}
```

#### Task 4.2: Cross-Platform Path Handling
```csharp
// File: PathHandlingTests.cs
public class PathHandlingPrototype
{
    public string NormalizePath(string path)
    {
        // Handle various path formats
        if (path.StartsWith("/mnt/c/", StringComparison.OrdinalIgnoreCase))
        {
            // WSL path
            return path.Replace("/mnt/c/", "C:\\").Replace('/', '\\');
        }
        
        // Expand environment variables
        path = Environment.ExpandEnvironmentVariables(path);
        
        // Normalize separators
        return Path.GetFullPath(path);
    }
    
    [Theory]
    [InlineData("C:\\Data\\file.csv")]
    [InlineData("/mnt/c/Data/file.csv")]
    [InlineData("${TEMP}/file.csv")]
    [InlineData("../data/file.csv")]
    public void TestPathNormalization(string input)
    {
        var normalized = NormalizePath(input);
        Assert.True(Path.IsPathRooted(normalized));
    }
}
```

### **Day 5: Analysis & Recommendations**

#### Task 5.1: Create Performance Report
```csharp
// File: PerformanceReportGenerator.cs
public class PerformanceReportGenerator
{
    public void GenerateReport(BenchmarkResults results)
    {
        var report = new StringBuilder();
        report.AppendLine("# FlowEngine Performance Analysis");
        report.AppendLine($"Date: {DateTime.Now:yyyy-MM-dd}");
        report.AppendLine($"Hardware: {GetHardwareInfo()}");
        report.AppendLine();
        
        report.AppendLine("## Key Findings");
        report.AppendLine();
        
        // Row implementation comparison
        var rowResults = results.GetBenchmarks("RowBenchmarks");
        report.AppendLine("### Row Implementation Performance");
        report.AppendLine("| Implementation | Read (ns) | Transform (ns) | Memory (bytes) |");
        report.AppendLine("|----------------|-----------|----------------|----------------|");
        foreach (var result in rowResults)
        {
            report.AppendLine($"| {result.Method} | {result.Mean:F2} | ... | {result.AllocatedBytes} |");
        }
        
        // Recommendations
        report.AppendLine();
        report.AppendLine("## Recommendations");
        
        if (rowResults.GetResult("ArrayRow").Mean < rowResults.GetResult("DictionaryRow").Mean * 0.5)
        {
            report.AppendLine("- ✅ Use ArrayRow for 2x+ performance improvement");
        }
        else
        {
            report.AppendLine("- ⚠️ Performance gain from ArrayRow is marginal");
        }
        
        File.WriteAllText("performance-report.md", report.ToString());
    }
}
```

---

## 🔍 Key Questions to Answer

### Through Benchmarking:
1. **Row Performance**: Which implementation can handle target throughput?
2. **Memory Pressure**: At what point does GC become a bottleneck?
3. **Fan-out Scaling**: How many connections can we support efficiently?
4. **Spillover Trigger**: What's the optimal memory threshold?
5. **Pooling Benefit**: Is pooling worth the complexity?

### Through Prototyping:
1. **Real-world Performance**: What throughput on actual CSV files?
2. **Memory Footprint**: Memory usage for 1M rows?
3. **Thermal Throttling**: Performance degradation over time?
4. **Cross-platform Issues**: Any WSL-specific bottlenecks?

---

## 📊 Success Criteria

### Minimum Viable Performance:
- Process 100K rows/sec for simple pass-through
- Memory usage < 2x raw data size
- No thermal throttling in 30-minute runs
- Linear scaling to 4 cores

### Decision Points:
- If ArrayRow is >50% faster → Use it despite flexibility loss
- If channels scale poorly → Consider alternative communication
- If pooling saves >30% allocations → Implement it
- If spillover works smoothly → Keep current design

---

## 🚫 What NOT to Prototype

### Avoid Premature Optimization:
- ❌ Complex plugin loading mechanisms
- ❌ Full error handling policies  
- ❌ JavaScript engine pooling optimization
- ❌ Debug tap implementation
- ❌ Configuration validation
- ❌ Resource governance enforcement

### Focus on Core Questions:
- ✅ Can we process data fast enough?
- ✅ Is memory usage bounded?
- ✅ Does the design scale?
- ✅ Are there platform-specific issues?

---

## 📝 Deliverables

### After 5 Days:
1. **Benchmark Results** - BenchmarkDotNet reports
2. **Performance Report** - Recommendations based on data
3. **Decision Matrix** - Which design alternatives to use
4. **Risk Assessment** - Remaining performance risks
5. **Implementation Plan** - Updated based on findings

### Sample Decision Matrix:
```markdown
| Component | Current Design | Alternative | Decision | Rationale |
|-----------|---------------|-------------|----------|-----------|
| Row | Dictionary | ArrayRow | ArrayRow | 2.5x faster, acceptable trade-off |
| Ports | Channels | Keep Current | Channels | Scales adequately to 20 connections |
| Memory | ArrayPool | ObjectPool | ObjectPool | Better for reference types |
| Spillover | 80% threshold | 70% threshold | 70% | More headroom on laptops |
```

This prototype phase will give us concrete data to make informed decisions and avoid both premature optimization and performance pitfalls.