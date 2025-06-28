using BenchmarkDotNet.Attributes;

namespace FlowEngine.Benchmarks.Ports;

/// <summary>
/// Benchmarks comparing port communication alternatives with fan-out scenarios.
/// Tests critical scalability characteristics:
/// - 1→1 communication (baseline)
/// - 1→10 fan-out (moderate scaling) 
/// - 1→100 fan-out (high scaling stress test)
/// 
/// Goal: Determine which port implementation can handle target fan-out without bottlenecks.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(iterationCount: 3, warmupCount: 1)]
public class StreamingBenchmarks
{
    [Params(1, 10, 100)]
    public int FanOutCount { get; set; }

    [Params(1000, 10000)]
    public int DatasetCount { get; set; }

    // =============================================================================
    // CHANNEL-BASED PORT BENCHMARKS
    // =============================================================================

    [Benchmark(Baseline = true)]
    public async Task ChannelPort_FanOut()
    {
        var (source, sinks) = PortNetworkBuilder.CreateChannelNetwork(FanOutCount);
        
        try
        {
            // Start consumers
            var consumerTasks = sinks.Select(async sink =>
            {
                var count = 0;
                await foreach (var dataset in sink.ReceiveAsync())
                {
                    count++;
                    if (count >= DatasetCount) break;
                }
                return count;
            }).ToArray();

            // Send data
            var sendTask = Task.Run(async () =>
            {
                for (int i = 0; i < DatasetCount; i++)
                {
                    await source.SendAsync(Dataset.Create(1, $"dataset-{i}"));
                }
                source.Complete();
            });

            // Wait for completion
            await sendTask;
            var results = await Task.WhenAll(consumerTasks);
            
            // Verify all consumers received all data
            foreach (var result in results)
            {
                if (result != DatasetCount)
                    throw new InvalidOperationException($"Expected {DatasetCount}, got {result}");
            }
        }
        finally
        {
            source.Dispose();
            foreach (var sink in sinks)
                sink.Dispose();
        }
    }

    // =============================================================================
    // CALLBACK-BASED PORT BENCHMARKS  
    // =============================================================================

    [Benchmark]
    public async Task CallbackPort_FanOut()
    {
        var (source, handlers) = PortNetworkBuilder.CreateCallbackNetwork(FanOutCount);
        var receivedCounts = new int[FanOutCount];

        // Update handlers to track counts
        for (int i = 0; i < FanOutCount; i++)
        {
            var index = i;
            source.Subscribe(async dataset =>
            {
                Interlocked.Increment(ref receivedCounts[index]);
                await Task.Yield();
            });
        }

        // Send data
        for (int i = 0; i < DatasetCount; i++)
        {
            await source.SendAsync(Dataset.Create(1, $"dataset-{i}"));
        }

        // Wait a bit for async processing to complete
        await Task.Delay(100);

        // Verify counts
        foreach (var count in receivedCounts)
        {
            if (count != DatasetCount)
                throw new InvalidOperationException($"Expected {DatasetCount}, got {count}");
        }
    }

    // =============================================================================
    // BATCHED PORT BENCHMARKS
    // =============================================================================

    [Benchmark]
    public async Task BatchedPort_FanOut()
    {
        // Note: BatchedPort doesn't naturally support fan-out, so we'll test single throughput
        using var source = new BatchedPort(batchSize: 50);
        var received = new List<Dataset>();

        // Start consumer
        var consumerTask = Task.Run(async () =>
        {
            await foreach (var dataset in source.ReceiveAsync())
            {
                received.Add(dataset);
                if (received.Count >= DatasetCount * FanOutCount) break;
            }
        });

        // Send data (simulate fan-out by sending multiple copies)
        for (int i = 0; i < DatasetCount; i++)
        {
            var dataset = Dataset.Create(1, $"dataset-{i}");
            for (int j = 0; j < FanOutCount; j++)
            {
                await source.SendAsync(dataset);
            }
        }

        source.Complete();
        await consumerTask;

        // Verify count
        if (received.Count != DatasetCount * FanOutCount)
            throw new InvalidOperationException($"Expected {DatasetCount * FanOutCount}, got {received.Count}");
    }

    // =============================================================================
    // CONCURRENT SENDING BENCHMARKS (Multiple senders to one receiver)
    // =============================================================================

    [Benchmark]
    public async Task ChannelPort_ConcurrentSenders()
    {
        using var sink = new ChannelPort();
        var received = new List<Dataset>();

        // Start consumer
        var consumerTask = Task.Run(async () =>
        {
            await foreach (var dataset in sink.ReceiveAsync())
            {
                received.Add(dataset);
                if (received.Count >= DatasetCount * FanOutCount) break;
            }
        });

        // Start multiple senders (FanOutCount acts as sender count here)
        var senderTasks = Enumerable.Range(0, FanOutCount).Select(async senderId =>
        {
            for (int i = 0; i < DatasetCount; i++)
            {
                await sink.SendAsync(Dataset.Create(1, $"sender-{senderId}-dataset-{i}"));
            }
        }).ToArray();

        await Task.WhenAll(senderTasks);
        sink.Complete();
        await consumerTask;

        // Verify count
        if (received.Count != DatasetCount * FanOutCount)
            throw new InvalidOperationException($"Expected {DatasetCount * FanOutCount}, got {received.Count}");
    }
}

/// <summary>
/// Specialized benchmarks for port overhead and latency measurements.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(iterationCount: 5, warmupCount: 2)]
public class PortLatencyBenchmarks
{
    private ChannelPort _channelPort = null!;
    private CallbackPort _callbackPort = null!;
    private BatchedPort _batchedPort = null!;
    private Dataset _testDataset = null!;

    [GlobalSetup]
    public void Setup()
    {
        _channelPort = new ChannelPort(capacity: 1000);
        _callbackPort = new CallbackPort();
        _batchedPort = new BatchedPort(batchSize: 1); // Force immediate batching
        _testDataset = Dataset.Create(100, "latency-test");

        // Set up callback handler
        _callbackPort.Subscribe(async _ => await Task.Yield());

        // Start background consumer for channel and batched ports
        _ = Task.Run(async () =>
        {
            await foreach (var _ in _channelPort.ReceiveAsync()) { }
        });

        _ = Task.Run(async () =>
        {
            await foreach (var _ in _batchedPort.ReceiveAsync()) { }
        });
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _channelPort?.Dispose();
        _batchedPort?.Dispose();
    }

    [Benchmark(Baseline = true)]
    public async Task ChannelPort_SingleSend()
    {
        await _channelPort.SendAsync(_testDataset);
    }

    [Benchmark]
    public async Task CallbackPort_SingleSend()
    {
        await _callbackPort.SendAsync(_testDataset);
    }

    [Benchmark]
    public async Task BatchedPort_SingleSend()
    {
        await _batchedPort.SendAsync(_testDataset);
    }

    [Benchmark]
    [Arguments(10)]
    [Arguments(100)]
    [Arguments(1000)]
    public async Task ChannelPort_BurstSend(int burstSize)
    {
        var tasks = new Task[burstSize];
        for (int i = 0; i < burstSize; i++)
        {
            tasks[i] = _channelPort.SendAsync(_testDataset).AsTask();
        }
        await Task.WhenAll(tasks);
    }

    [Benchmark]
    [Arguments(10)]
    [Arguments(100)]
    [Arguments(1000)]
    public async Task CallbackPort_BurstSend(int burstSize)
    {
        var tasks = new Task[burstSize];
        for (int i = 0; i < burstSize; i++)
        {
            tasks[i] = _callbackPort.SendAsync(_testDataset).AsTask();
        }
        await Task.WhenAll(tasks);
    }
}

/// <summary>
/// Benchmarks for memory efficiency of different port implementations.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(iterationCount: 3, warmupCount: 1)]
public class PortMemoryBenchmarks
{
    [Params(1000, 10000)]
    public int DatasetCount { get; set; }

    [Benchmark]
    public async Task ChannelPort_MemoryUsage()
    {
        using var port = new ChannelPort(capacity: DatasetCount);
        
        // Fill channel to capacity
        for (int i = 0; i < DatasetCount; i++)
        {
            await port.SendAsync(Dataset.Create(i, $"dataset-{i}"));
        }

        // Consume all data
        var count = 0;
        await foreach (var _ in port.ReceiveAsync())
        {
            count++;
            if (count >= DatasetCount) break;
        }
    }

    [Benchmark]
    public async Task BatchedPort_MemoryUsage()
    {
        using var port = new BatchedPort(batchSize: 100);
        var received = new List<Dataset>();

        // Start consumer
        var consumerTask = Task.Run(async () =>
        {
            await foreach (var dataset in port.ReceiveAsync())
            {
                received.Add(dataset);
                if (received.Count >= DatasetCount) break;
            }
        });

        // Send data
        for (int i = 0; i < DatasetCount; i++)
        {
            await port.SendAsync(Dataset.Create(i, $"dataset-{i}"));
        }

        port.Complete();
        await consumerTask;
    }
}