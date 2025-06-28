using System.Threading.Channels;
using System.Runtime.CompilerServices;

namespace FlowEngine.Benchmarks.Ports;

/// <summary>
/// Simple dataset for port communication benchmarks
/// </summary>
public class Dataset
{
    public required int RowCount { get; init; }
    public required string Name { get; init; }
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    
    public static Dataset Create(int rowCount, string name = "test-dataset")
    {
        return new Dataset { RowCount = rowCount, Name = name };
    }
}

/// <summary>
/// Option 1: Channel-based Port (Current Design)
/// Uses System.Threading.Channels for buffered communication
/// </summary>
public class ChannelPort : IDisposable
{
    private readonly Channel<Dataset> _channel;
    private readonly List<ChannelPort> _connectedPorts = new();
    private readonly object _lock = new();
    private bool _disposed;

    public ChannelPort(int capacity = 100)
    {
        var options = new BoundedChannelOptions(capacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = false,
            SingleWriter = false
        };
        _channel = Channel.CreateBounded<Dataset>(options);
    }

    public async ValueTask SendAsync(Dataset data, CancellationToken cancellationToken = default)
    {
        if (_disposed) return;

        // Send to own channel
        await _channel.Writer.WriteAsync(data, cancellationToken);

        // Fan-out to connected ports
        lock (_lock)
        {
            var tasks = _connectedPorts.Select(port => port.ReceiveFromSourceAsync(data, cancellationToken));
            _ = Task.WhenAll(tasks.Select(t => t.AsTask()));
        }
    }

    private async ValueTask ReceiveFromSourceAsync(Dataset data, CancellationToken cancellationToken)
    {
        if (_disposed) return;
        await _channel.Writer.WriteAsync(data, cancellationToken);
    }

    public async IAsyncEnumerable<Dataset> ReceiveAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var dataset in _channel.Reader.ReadAllAsync(cancellationToken))
        {
            if (_disposed) yield break;
            yield return dataset;
        }
    }

    public void ConnectTo(ChannelPort target)
    {
        lock (_lock)
        {
            _connectedPorts.Add(target);
        }
    }

    public void Complete()
    {
        _channel.Writer.TryComplete();
        lock (_lock)
        {
            foreach (var port in _connectedPorts)
            {
                port._channel.Writer.TryComplete();
            }
        }
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
            Complete();
        }
    }
}

/// <summary>
/// Option 2: Direct Callback Port (Simpler, no buffering)
/// Uses direct function calls for immediate delivery
/// </summary>
public class CallbackPort
{
    private readonly List<Func<Dataset, ValueTask>> _receivers = new();
    private readonly SemaphoreSlim _sendLock = new(1);

    public void Subscribe(Func<Dataset, ValueTask> receiver)
    {
        lock (_receivers)
        {
            _receivers.Add(receiver);
        }
    }

    public async ValueTask SendAsync(Dataset data, CancellationToken cancellationToken = default)
    {
        await _sendLock.WaitAsync(cancellationToken);
        try
        {
            Func<Dataset, ValueTask>[] currentReceivers;
            lock (_receivers)
            {
                currentReceivers = _receivers.ToArray();
            }

            if (currentReceivers.Length == 0) return;

            if (currentReceivers.Length == 1)
            {
                // Optimize single receiver case
                await currentReceivers[0](data);
            }
            else
            {
                // Parallel delivery for multiple receivers
                var tasks = currentReceivers.Select(r => r(data).AsTask());
                await Task.WhenAll(tasks);
            }
        }
        finally
        {
            _sendLock.Release();
        }
    }

    public int SubscriberCount
    {
        get
        {
            lock (_receivers)
            {
                return _receivers.Count;
            }
        }
    }
}

/// <summary>
/// Option 3: Batched Port (Buffer multiple datasets for efficiency)
/// Collects multiple datasets and sends in batches
/// </summary>
public class BatchedPort : IDisposable
{
    private readonly Channel<Dataset[]> _channel;
    private readonly List<Dataset> _buffer = new();
    private readonly int _batchSize;
    private readonly Timer _flushTimer;
    private readonly object _bufferLock = new();
    private bool _disposed;

    public BatchedPort(int batchSize = 100, TimeSpan? flushInterval = null)
    {
        _batchSize = batchSize;
        _channel = Channel.CreateUnbounded<Dataset[]>();
        
        var interval = flushInterval ?? TimeSpan.FromMilliseconds(10);
        _flushTimer = new Timer(FlushBuffer, null, interval, interval);
    }

    public async ValueTask SendAsync(Dataset data, CancellationToken cancellationToken = default)
    {
        if (_disposed) return;

        Dataset[]? batchToSend = null;
        
        lock (_bufferLock)
        {
            _buffer.Add(data);
            if (_buffer.Count >= _batchSize)
            {
                batchToSend = _buffer.ToArray();
                _buffer.Clear();
            }
        }

        if (batchToSend != null)
        {
            await _channel.Writer.WriteAsync(batchToSend, cancellationToken);
        }
    }

    private void FlushBuffer(object? state)
    {
        if (_disposed) return;

        Dataset[]? batchToSend = null;
        
        lock (_bufferLock)
        {
            if (_buffer.Count > 0)
            {
                batchToSend = _buffer.ToArray();
                _buffer.Clear();
            }
        }

        if (batchToSend != null)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await _channel.Writer.WriteAsync(batchToSend);
                }
                catch
                {
                    // Ignore errors during flush
                }
            });
        }
    }

    public async IAsyncEnumerable<Dataset> ReceiveAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var batch in _channel.Reader.ReadAllAsync(cancellationToken))
        {
            if (_disposed) yield break;
            
            foreach (var dataset in batch)
            {
                yield return dataset;
            }
        }
    }

    public void Complete()
    {
        // Flush remaining buffer
        FlushBuffer(null);
        _channel.Writer.TryComplete();
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
            _flushTimer?.Dispose();
            Complete();
        }
    }
}

/// <summary>
/// Helper class for creating port networks for testing
/// </summary>
public static class PortNetworkBuilder
{
    public static (ChannelPort source, ChannelPort[] sinks) CreateChannelNetwork(int sinkCount)
    {
        var source = new ChannelPort();
        var sinks = new ChannelPort[sinkCount];
        
        for (int i = 0; i < sinkCount; i++)
        {
            sinks[i] = new ChannelPort();
            source.ConnectTo(sinks[i]);
        }
        
        return (source, sinks);
    }

    public static (CallbackPort source, Func<Dataset, ValueTask>[] handlers) CreateCallbackNetwork(int handlerCount)
    {
        var source = new CallbackPort();
        var receivedCounts = new int[handlerCount];
        var handlers = new Func<Dataset, ValueTask>[handlerCount];
        
        for (int i = 0; i < handlerCount; i++)
        {
            var index = i; // Capture for closure
            handlers[index] = async dataset =>
            {
                Interlocked.Increment(ref receivedCounts[index]);
                await Task.Yield(); // Simulate some work
            };
            source.Subscribe(handlers[index]);
        }
        
        return (source, handlers);
    }

    public static (BatchedPort source, List<Dataset> received) CreateBatchedNetwork()
    {
        var source = new BatchedPort(batchSize: 50, flushInterval: TimeSpan.FromMilliseconds(5));
        var received = new List<Dataset>();
        
        // Start background task to collect received datasets
        _ = Task.Run(async () =>
        {
            await foreach (var dataset in source.ReceiveAsync())
            {
                lock (received)
                {
                    received.Add(dataset);
                }
            }
        });
        
        return (source, received);
    }
}