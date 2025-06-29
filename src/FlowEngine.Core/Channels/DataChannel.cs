using System.Threading.Channels;
using FlowEngine.Abstractions.Channels;
using FlowEngine.Abstractions.Configuration;
using ChannelConfiguration = FlowEngine.Abstractions.Configuration.IChannelConfiguration;

namespace FlowEngine.Core.Channels;

/// <summary>
/// High-performance data channel implementation using System.Threading.Channels.
/// Provides configurable buffering, backpressure, and flow control for plugin communication.
/// </summary>
/// <typeparam name="T">Type of data flowing through the channel</typeparam>
public sealed class DataChannel<T> : IDataChannel<T>
{
    private readonly Channel<T> _channel;
    private readonly ChannelConfiguration _configuration;
    private readonly DataChannelMetrics _metrics;
    private readonly object _statusLock = new();

    private ChannelStatus _status = ChannelStatus.Active;
    private bool _disposed;

    /// <summary>
    /// Initializes a new data channel with the specified configuration.
    /// </summary>
    /// <param name="id">Unique identifier for this channel</param>
    /// <param name="configuration">Channel configuration</param>
    public DataChannel(string id, ChannelConfiguration configuration)
    {
        Id = id ?? throw new ArgumentNullException(nameof(id));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _metrics = new DataChannelMetrics();

        // Create the underlying channel based on configuration
        var options = CreateChannelOptions(configuration);
        _channel = Channel.CreateBounded<T>(options);

        // Start monitoring backpressure
        _ = Task.Run(MonitorBackpressureAsync);
    }

    /// <inheritdoc />
    public string Id { get; }

    /// <inheritdoc />
    public Abstractions.Configuration.IChannelConfiguration Configuration => _configuration;

    /// <inheritdoc />
    public ChannelStatus Status
    {
        get
        {
            lock (_statusLock)
            {
                return _status;
            }
        }
    }

    /// <inheritdoc />
    public IChannelMetrics Metrics => _metrics;

    /// <inheritdoc />
    public event EventHandler<ChannelStatusChangedEventArgs>? StatusChanged;

    /// <inheritdoc />
    public async ValueTask<bool> WriteAsync(T item, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        try
        {
            var written = await _channel.Writer.WaitToWriteAsync(cancellationToken);
            if (!written)
            {
                // Channel is completed
                return false;
            }

            var success = _channel.Writer.TryWrite(item);
            if (success)
            {
                _metrics.RecordWrite();
            }
            else
            {
                // Handle backpressure based on configuration
                success = await HandleBackpressureAsync(item, cancellationToken);
            }

            return success;
        }
        catch (InvalidOperationException)
        {
            // Channel is completed
            return false;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            SetStatus(ChannelStatus.Faulted, ex);
            throw new ChannelException($"Failed to write to channel {Id}", ex) { ChannelId = Id, ChannelStatus = Status };
        }
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<T> ReadAllAsync([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        IAsyncEnumerator<T>? enumerator = null;
        try
        {
            enumerator = _channel.Reader.ReadAllAsync(cancellationToken).GetAsyncEnumerator(cancellationToken);
            
            while (true)
            {
                try
                {
                    if (!await enumerator.MoveNextAsync())
                        break;
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    SetStatus(ChannelStatus.Faulted, ex);
                    throw new ChannelException($"Failed to read from channel {Id}", ex) { ChannelId = Id, ChannelStatus = Status };
                }

                _metrics.RecordRead();
                yield return enumerator.Current;
            }
        }
        finally
        {
            if (enumerator != null)
            {
                await enumerator.DisposeAsync();
            }
        }
    }

    /// <inheritdoc />
    public void Complete(Exception? exception = null)
    {
        ThrowIfDisposed();

        try
        {
            if (exception != null)
            {
                _channel.Writer.Complete(exception);
                SetStatus(ChannelStatus.Faulted, exception);
            }
            else
            {
                _channel.Writer.Complete();
                SetStatus(ChannelStatus.Completed);
            }
        }
        catch (InvalidOperationException)
        {
            // Channel was already completed
        }
    }

    /// <inheritdoc />
    public Task WaitForCompletionAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        return _channel.Reader.Completion.WaitAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;

        try
        {
            Complete();
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await WaitForCompletionAsync(cts.Token);
        }
        catch
        {
            // Ignore cleanup errors
        }
        finally
        {
            SetStatus(ChannelStatus.Disposed);
            _disposed = true;
        }
    }

    private static BoundedChannelOptions CreateChannelOptions(ChannelConfiguration configuration)
    {
        var options = new BoundedChannelOptions(configuration.BufferSize)
        {
            SingleReader = false,
            SingleWriter = false,
            AllowSynchronousContinuations = false
        };

        // Set the full mode based on configuration
        options.FullMode = configuration.FullMode switch
        {
            ChannelFullMode.Wait => BoundedChannelFullMode.Wait,
            ChannelFullMode.DropOldest => BoundedChannelFullMode.DropOldest,
            ChannelFullMode.DropNewest => BoundedChannelFullMode.DropNewest,
            ChannelFullMode.ThrowException => BoundedChannelFullMode.Wait, // We'll handle this manually
            _ => BoundedChannelFullMode.Wait
        };

        return options;
    }

    private async Task<bool> HandleBackpressureAsync(T item, CancellationToken cancellationToken)
    {
        _metrics.RecordBlockedWrite();

        switch (_configuration.FullMode)
        {
            case ChannelFullMode.Wait:
                try
                {
                    using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    cts.CancelAfter(_configuration.Timeout);
                    
                    await _channel.Writer.WaitToWriteAsync(cts.Token);
                    var success = _channel.Writer.TryWrite(item);
                    if (success)
                    {
                        _metrics.RecordWrite();
                    }
                    return success;
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    // Timeout occurred
                    _metrics.RecordDroppedItem();
                    return false;
                }

            case ChannelFullMode.ThrowException:
                throw new ChannelException($"Channel {Id} is full and cannot accept more items") 
                { 
                    ChannelId = Id, 
                    ChannelStatus = Status 
                };

            case ChannelFullMode.DropOldest:
            case ChannelFullMode.DropNewest:
                // These are handled by the underlying channel
                _metrics.RecordDroppedItem();
                return false;

            default:
                _metrics.RecordDroppedItem();
                return false;
        }
    }

    private async Task MonitorBackpressureAsync()
    {
        try
        {
            while (!_disposed && Status == ChannelStatus.Active)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(100));

                var utilization = _metrics.BufferUtilization;
                var threshold = _configuration.BackpressureThreshold;

                lock (_statusLock)
                {
                    if (_status == ChannelStatus.Active && utilization >= threshold)
                    {
                        SetStatus(ChannelStatus.Backpressure);
                    }
                    else if (_status == ChannelStatus.Backpressure && utilization < threshold * 0.8) // Hysteresis
                    {
                        SetStatus(ChannelStatus.Active);
                    }
                }
            }
        }
        catch
        {
            // Ignore monitoring errors
        }
    }

    private void SetStatus(ChannelStatus newStatus, Exception? exception = null)
    {
        ChannelStatus oldStatus;

        lock (_statusLock)
        {
            if (_status == newStatus)
                return;

            oldStatus = _status;
            _status = newStatus;
        }

        try
        {
            StatusChanged?.Invoke(this, new ChannelStatusChangedEventArgs(oldStatus, newStatus, exception));
        }
        catch
        {
            // Ignore event handler errors
        }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(DataChannel<T>));
    }
}

/// <summary>
/// Implementation of channel metrics for monitoring and performance tracking.
/// </summary>
internal sealed class DataChannelMetrics : IChannelMetrics
{
    private long _totalItemsWritten;
    private long _totalItemsRead;
    private long _droppedItems;
    private long _blockedWrites;
    private readonly object _metricsLock = new();

    /// <inheritdoc />
    public long TotalItemsWritten
    {
        get
        {
            lock (_metricsLock)
            {
                return _totalItemsWritten;
            }
        }
    }

    /// <inheritdoc />
    public long TotalItemsRead
    {
        get
        {
            lock (_metricsLock)
            {
                return _totalItemsRead;
            }
        }
    }

    /// <inheritdoc />
    public int CurrentBufferCount { get; private set; }

    /// <inheritdoc />
    public double BufferUtilization => CurrentBufferCount / (double)Math.Max(1, BufferCapacity) * 100.0;

    /// <inheritdoc />
    public long DroppedItems
    {
        get
        {
            lock (_metricsLock)
            {
                return _droppedItems;
            }
        }
    }

    /// <inheritdoc />
    public long BlockedWrites
    {
        get
        {
            lock (_metricsLock)
            {
                return _blockedWrites;
            }
        }
    }

    /// <inheritdoc />
    public TimeSpan AverageLatency { get; private set; } = TimeSpan.Zero;

    /// <inheritdoc />
    public double ThroughputPerSecond { get; private set; }

    /// <inheritdoc />
    public DateTimeOffset LastUpdated { get; private set; } = DateTimeOffset.UtcNow;

    private int BufferCapacity { get; set; } = 1000; // Set during channel creation

    internal void RecordWrite()
    {
        lock (_metricsLock)
        {
            _totalItemsWritten++;
            CurrentBufferCount++;
            LastUpdated = DateTimeOffset.UtcNow;
        }
    }

    internal void RecordRead()
    {
        lock (_metricsLock)
        {
            _totalItemsRead++;
            CurrentBufferCount = Math.Max(0, CurrentBufferCount - 1);
            LastUpdated = DateTimeOffset.UtcNow;
        }
    }

    internal void RecordDroppedItem()
    {
        lock (_metricsLock)
        {
            _droppedItems++;
            LastUpdated = DateTimeOffset.UtcNow;
        }
    }

    internal void RecordBlockedWrite()
    {
        lock (_metricsLock)
        {
            _blockedWrites++;
            LastUpdated = DateTimeOffset.UtcNow;
        }
    }

    internal void SetBufferCapacity(int capacity)
    {
        BufferCapacity = capacity;
    }
}