using FlowEngine.Benchmarks.DataStructures;
using BenchmarkDotNet.Attributes;

namespace FlowEngine.Benchmarks.Memory;

/// <summary>
/// Information about current memory pressure state
/// </summary>
public class MemoryPressureInfo
{
    public long TotalMemoryBytes { get; init; }
    public long HeapSizeBytes { get; init; }
    public long HighMemoryLoadThresholdBytes { get; init; }
    public double MemoryLoadPercent { get; init; }
    
    public MemoryPressureLevel Level => MemoryLoadPercent switch
    {
        < 50 => MemoryPressureLevel.Low,
        < 70 => MemoryPressureLevel.Medium, 
        < 90 => MemoryPressureLevel.High,
        _ => MemoryPressureLevel.Critical
    };
}

public enum MemoryPressureLevel
{
    Low,
    Medium,
    High,
    Critical
}

/// <summary>
/// Simulates memory pressure for testing spillover behavior
/// </summary>
public class MemoryPressureSimulator : IDisposable
{
    private readonly List<byte[]> _memoryHog = new();
    private bool _disposed;

    public void SimulateMemoryPressure(int targetMB)
    {
        if (_disposed) return;
        
        var bytesPerMB = 1024 * 1024;
        for (int i = 0; i < targetMB; i++)
        {
            // Allocate in smaller chunks to avoid LOH immediately
            for (int j = 0; j < 4; j++)
            {
                _memoryHog.Add(new byte[bytesPerMB / 4]);
            }
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

    public void ReleaseMemory(int amountMB)
    {
        if (_disposed) return;
        
        var itemsToRemove = Math.Min(amountMB * 4, _memoryHog.Count); // 4 items per MB
        for (int i = 0; i < itemsToRemove; i++)
        {
            _memoryHog.RemoveAt(_memoryHog.Count - 1);
        }
        
        // Force GC to reclaim memory
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _memoryHog.Clear();
            _disposed = true;
            GC.Collect();
        }
    }
}

/// <summary>
/// Mock memory manager for testing spillover behavior
/// </summary>
public class MockMemoryManager
{
    private readonly double _spilloverThreshold;
    private readonly MemoryPressureSimulator _simulator;
    
    public event Action? OnSpillover;
    
    public MockMemoryManager(double spilloverThreshold = 0.7)
    {
        _spilloverThreshold = spilloverThreshold;
        _simulator = new MemoryPressureSimulator();
    }

    public bool ShouldSpillToDisk()
    {
        var info = _simulator.GetMemoryInfo();
        return info.MemoryLoadPercent / 100.0 > _spilloverThreshold;
    }

    public async Task<string> SpillToFileAsync<T>(IEnumerable<T> data)
    {
        var tempFile = Path.GetTempFileName();
        
        await using var stream = new FileStream(tempFile, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize: 65536, useAsync: true);
        await using var writer = new StreamWriter(stream);
        
        // Simulate spillover by writing data as JSON lines
        foreach (var item in data)
        {
            await writer.WriteLineAsync(System.Text.Json.JsonSerializer.Serialize(item));
        }
        
        OnSpillover?.Invoke();
        return tempFile;
    }

    public async Task<List<T>> ReadFromFileAsync<T>(string fileName)
    {
        var results = new List<T>();
        
        using var stream = new FileStream(fileName, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 65536, useAsync: true);
        using var reader = new StreamReader(stream);
        
        string? line;
        while ((line = await reader.ReadLineAsync()) != null)
        {
            if (!string.IsNullOrWhiteSpace(line))
            {
                var item = System.Text.Json.JsonSerializer.Deserialize<T>(line);
                if (item != null)
                    results.Add(item);
            }
        }
        
        return results;
    }

    public MemoryPressureInfo GetCurrentMemoryInfo()
    {
        return _simulator.GetMemoryInfo();
    }

    public void SimulateMemoryPressure(int mb)
    {
        _simulator.SimulateMemoryPressure(mb);
    }

    public void ReleaseMemory(int mb)
    {
        _simulator.ReleaseMemory(mb);
    }
}

/// <summary>
/// Tests memory pressure detection and spillover behavior
/// </summary>
[MemoryDiagnoser]
[SimpleJob(iterationCount: 3, warmupCount: 1)]
public class MemoryPressureBenchmarks
{
    private MockMemoryManager _memoryManager = null!;
    private List<DictionaryRow> _testData = null!;

    [GlobalSetup]
    public void Setup()
    {
        _memoryManager = new MockMemoryManager(spilloverThreshold: 0.6); // 60% threshold
        
        // Create test data - realistic customer rows
        _testData = new List<DictionaryRow>();
        for (int i = 0; i < 10000; i++)
        {
            var data = new Dictionary<string, object?>
            {
                ["id"] = i,
                ["name"] = $"Customer {i}",
                ["email"] = $"customer{i}@example.com",
                ["address"] = $"{i} Main Street, City {i % 100}, State {i % 50}",
                ["phone"] = $"+1-555-{i:D3}-{(i * 7) % 10000:D4}",
                ["data"] = new string('x', 500) // Add some bulk to each row
            };
            _testData.Add(new DictionaryRow(data));
        }
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _memoryManager?.ReleaseMemory(1000); // Release all memory
    }

    [Benchmark]
    [Arguments(10)]
    [Arguments(50)]
    [Arguments(100)]
    public async Task TestSpilloverTrigger(int pressureMB)
    {
        // Start with low memory pressure
        var initialInfo = _memoryManager.GetCurrentMemoryInfo();
        
        // Gradually increase memory pressure
        _memoryManager.SimulateMemoryPressure(pressureMB);
        
        // Process data and check for spillover
        var spilledFiles = new List<string>();
        var spilledCount = 0;
        
        _memoryManager.OnSpillover += () => spilledCount++;
        
        // Process data in chunks, checking for spillover
        const int chunkSize = 1000;
        for (int i = 0; i < _testData.Count; i += chunkSize)
        {
            var chunk = _testData.Skip(i).Take(chunkSize);
            
            if (_memoryManager.ShouldSpillToDisk())
            {
                var spillFile = await _memoryManager.SpillToFileAsync(chunk);
                spilledFiles.Add(spillFile);
            }
        }
        
        // Cleanup spilled files
        foreach (var file in spilledFiles)
        {
            if (File.Exists(file))
                File.Delete(file);
        }
        
        // Release pressure for next iteration
        _memoryManager.ReleaseMemory(pressureMB);
        
        var finalInfo = _memoryManager.GetCurrentMemoryInfo();
    }

    [Benchmark]
    public async Task SpilloverWritePerformance()
    {
        // Test the performance of spillover write operations
        const int testDataCount = 5000;
        var testData = _testData.Take(testDataCount);
        
        var spillFile = await _memoryManager.SpillToFileAsync(testData);
        
        // Verify we can read it back
        var readBack = await _memoryManager.ReadFromFileAsync<DictionaryRow>(spillFile);
        
        if (readBack.Count != testDataCount)
            throw new InvalidOperationException($"Expected {testDataCount}, got {readBack.Count}");
        
        // Cleanup
        if (File.Exists(spillFile))
            File.Delete(spillFile);
    }

    [Benchmark]
    [Arguments(0.5)]  // 50% threshold
    [Arguments(0.7)]  // 70% threshold  
    [Arguments(0.9)]  // 90% threshold
    public void MemoryPressureDetection(double threshold)
    {
        var manager = new MockMemoryManager(threshold);
        
        // Test pressure detection at different levels
        for (int mb = 10; mb <= 200; mb += 20)
        {
            manager.SimulateMemoryPressure(20);
            var shouldSpill = manager.ShouldSpillToDisk();
            var info = manager.GetCurrentMemoryInfo();
            
            // Record the detection result (not throwing to avoid benchmark interference)
            var detected = shouldSpill;
        }
        
        manager.ReleaseMemory(200);
    }
}

/// <summary>
/// Tests different memory allocation patterns and their GC impact
/// </summary>
[MemoryDiagnoser]
[GcServer(true)]
[SimpleJob(iterationCount: 3, warmupCount: 1)]
public class MemoryAllocationBenchmarks
{
    [Params(1000, 10000, 100000)]
    public int AllocationCount { get; set; }

    [Benchmark(Baseline = true)]
    public void DirectAllocation()
    {
        var objects = new List<DictionaryRow>(AllocationCount);
        
        for (int i = 0; i < AllocationCount; i++)
        {
            var data = new Dictionary<string, object?>
            {
                ["id"] = i,
                ["value"] = $"test-{i}"
            };
            objects.Add(new DictionaryRow(data));
        }
        
        // Simulate some work with the objects
        var sum = 0;
        foreach (var obj in objects)
        {
            sum += (int)obj["id"]!;
        }
    }

    [Benchmark]
    public void ArrayRowAllocation()
    {
        var schema = Schema.GetOrCreate(new[] { "id", "value" });
        var objects = new List<ArrayRow>(AllocationCount);
        
        for (int i = 0; i < AllocationCount; i++)
        {
            var values = new object[] { i, $"test-{i}" };
            objects.Add(new ArrayRow(schema, values));
        }
        
        // Simulate some work with the objects
        var sum = 0;
        foreach (var obj in objects)
        {
            sum += (int)obj["id"]!;
        }
    }

    [Benchmark]
    public void PooledRowAllocation()
    {
        var objects = new List<PooledRow>(AllocationCount);
        
        try
        {
            for (int i = 0; i < AllocationCount; i++)
            {
                var data = new[]
                {
                    new KeyValuePair<string, object?>("id", i),
                    new KeyValuePair<string, object?>("value", $"test-{i}")
                };
                objects.Add(PooledRow.Create(data));
            }
            
            // Simulate some work with the objects
            var sum = 0;
            foreach (var obj in objects)
            {
                sum += (int)obj["id"]!;
            }
        }
        finally
        {
            // Cleanup pooled objects
            foreach (var obj in objects)
            {
                obj.Dispose();
            }
        }
    }

    [Benchmark]
    public void LargeObjectHeapPressure()
    {
        // Test allocation patterns that might trigger LOH
        var largeObjects = new List<byte[]>();
        
        for (int i = 0; i < AllocationCount / 100; i++) // Fewer iterations for large objects
        {
            // Allocate objects > 85KB to trigger LOH
            largeObjects.Add(new byte[100_000]); 
        }
        
        // Simulate some work
        long sum = 0;
        foreach (var obj in largeObjects)
        {
            sum += obj.Length;
        }
    }
}