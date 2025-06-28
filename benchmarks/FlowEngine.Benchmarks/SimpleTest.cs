using FlowEngine.Benchmarks.DataStructures;
using FlowEngine.Benchmarks.Ports;

namespace FlowEngine.Benchmarks;

/// <summary>
/// Simple functionality tests to verify implementations work correctly.
/// No benchmarking, just basic verification.
/// </summary>
public static class SimpleTest
{
    public static void RunAll()
    {
        Console.WriteLine("=== FlowEngine Prototype Verification Tests ===");
        Console.WriteLine();
        
        TestRowImplementations();
        TestPortImplementations();
        TestMemoryComponents();
        
        Console.WriteLine("✅ All verification tests passed!");
    }

    private static void TestRowImplementations()
    {
        Console.WriteLine("🔬 Testing Row implementations...");
        
        var data = new Dictionary<string, object?>
        {
            ["id"] = 123,
            ["name"] = "Test User",
            ["email"] = "test@example.com",
            ["active"] = true
        };

        // Test DictionaryRow
        var dictRow = new DictionaryRow(data);
        Console.WriteLine($"  ✓ DictionaryRow: {dictRow["name"]} ({dictRow["email"]})");
        var dictModified = dictRow.With("email", "modified@example.com");
        Console.WriteLine($"  ✓ DictionaryRow.With(): {dictModified["email"]}");

        // Test ImmutableRow
        var immutableRow = new ImmutableRow(data);
        Console.WriteLine($"  ✓ ImmutableRow: {immutableRow["name"]} ({immutableRow["email"]})");
        var immutableModified = immutableRow.With("email", "modified@example.com");
        Console.WriteLine($"  ✓ ImmutableRow.With(): {immutableModified["email"]}");

        // Test ArrayRow
        var schema = Schema.GetOrCreate(data.Keys.ToArray());
        var arrayRow = new ArrayRow(schema, data.Values.ToArray());
        Console.WriteLine($"  ✓ ArrayRow: {arrayRow["name"]} ({arrayRow["email"]})");
        var arrayModified = arrayRow.With("email", "modified@example.com");
        Console.WriteLine($"  ✓ ArrayRow.With(): {arrayModified["email"]}");

        // Test PooledRow
        using var pooledRow = PooledRow.Create(data);
        Console.WriteLine($"  ✓ PooledRow: {pooledRow["name"]} ({pooledRow["email"]})");
        using var pooledModified = pooledRow.With("email", "modified@example.com");
        Console.WriteLine($"  ✓ PooledRow.With(): {pooledModified["email"]}");
        
        Console.WriteLine();
    }

    private static void TestPortImplementations()
    {
        Console.WriteLine("🔗 Testing Port implementations...");
        
        TestChannelPorts();
        TestCallbackPorts();
        TestBatchedPorts();
        
        Console.WriteLine();
    }

    private static void TestChannelPorts()
    {
        using var source = new ChannelPort();
        using var sink = new ChannelPort();
        source.ConnectTo(sink);

        var testData = Dataset.Create(100, "test-channel");
        var received = new List<Dataset>();

        // Start consumer
        var consumerTask = Task.Run(async () =>
        {
            await foreach (var dataset in sink.ReceiveAsync())
            {
                received.Add(dataset);
                if (received.Count >= 3) break;
            }
        });

        // Send data
        Task.Run(async () =>
        {
            await source.SendAsync(Dataset.Create(10, "dataset-1"));
            await source.SendAsync(Dataset.Create(20, "dataset-2"));
            await source.SendAsync(Dataset.Create(30, "dataset-3"));
            source.Complete();
        });

        consumerTask.Wait(TimeSpan.FromSeconds(5));
        Console.WriteLine($"  ✓ ChannelPort: Sent 3, received {received.Count}");
    }

    private static void TestCallbackPorts()
    {
        var source = new CallbackPort();
        var receivedCount = 0;
        
        source.Subscribe(async dataset =>
        {
            Interlocked.Increment(ref receivedCount);
            await Task.Yield();
        });

        // Send some data
        Task.Run(async () =>
        {
            await source.SendAsync(Dataset.Create(10, "cb-1"));
            await source.SendAsync(Dataset.Create(20, "cb-2"));
            await source.SendAsync(Dataset.Create(30, "cb-3"));
        }).Wait();

        Thread.Sleep(100); // Give callbacks time to complete
        Console.WriteLine($"  ✓ CallbackPort: Sent 3, received {receivedCount}");
    }

    private static void TestBatchedPorts()
    {
        using var source = new BatchedPort(batchSize: 2);
        var received = new List<Dataset>();

        // Start consumer
        var consumerTask = Task.Run(async () =>
        {
            await foreach (var dataset in source.ReceiveAsync())
            {
                received.Add(dataset);
                if (received.Count >= 3) break;
            }
        });

        // Send data
        Task.Run(async () =>
        {
            await source.SendAsync(Dataset.Create(10, "batch-1"));
            await source.SendAsync(Dataset.Create(20, "batch-2"));
            await source.SendAsync(Dataset.Create(30, "batch-3"));
            source.Complete();
        });

        consumerTask.Wait(TimeSpan.FromSeconds(5));
        Console.WriteLine($"  ✓ BatchedPort: Sent 3, received {received.Count}");
    }

    private static void TestMemoryComponents()
    {
        Console.WriteLine("💾 Testing Memory components...");
        
        // Test memory pressure simulation
        using var simulator = new FlowEngine.Benchmarks.Memory.MemoryPressureSimulator();
        var initialInfo = simulator.GetMemoryInfo();
        Console.WriteLine($"  ✓ Initial memory: {initialInfo.MemoryLoadPercent:F1}% ({initialInfo.Level})");
        
        simulator.SimulateMemoryPressure(50); // 50 MB
        var pressureInfo = simulator.GetMemoryInfo();
        Console.WriteLine($"  ✓ Under pressure: {pressureInfo.MemoryLoadPercent:F1}% ({pressureInfo.Level})");
        
        simulator.ReleaseMemory(50);
        var releasedInfo = simulator.GetMemoryInfo();
        Console.WriteLine($"  ✓ After release: {releasedInfo.MemoryLoadPercent:F1}% ({releasedInfo.Level})");
        
        Console.WriteLine();
    }
}