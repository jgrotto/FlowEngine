using FlowEngine.Abstractions.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.ObjectPool;
using System.Collections.Concurrent;

namespace FlowEngine.Core.Services;

/// <summary>
/// Provides memory management and pooling services for FlowEngine data structures.
/// </summary>
public sealed class MemoryManager : IMemoryManager, IDisposable
{
    private readonly ILogger<MemoryManager> _logger;
    private readonly ObjectPool<object?[]> _arrayPool;
    private readonly ConcurrentDictionary<int, ObjectPool<object?[]>> _sizedPools = new();
    private readonly object _lock = new();

    private long _totalAllocatedMemory;
    private long _maxMemoryThreshold = 1024 * 1024 * 1024; // 1GB default
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the MemoryManager.
    /// </summary>
    /// <param name="logger">Logger for memory management operations</param>
    public MemoryManager(ILogger<MemoryManager> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        // Create default pool for most common array sizes
        var provider = new DefaultObjectPoolProvider();
        _arrayPool = provider.Create(new ArrayPoolPolicy(64)); // Default size for most ArrayRows

        _logger.LogInformation("MemoryManager initialized with {MaxMemory} bytes threshold", _maxMemoryThreshold);
    }

    /// <inheritdoc />
    public MemoryPressureLevel PressureLevel
    {
        get
        {
            var ratio = (double)_totalAllocatedMemory / _maxMemoryThreshold;
            return ratio switch
            {
                < 0.5 => MemoryPressureLevel.Normal,
                < 0.75 => MemoryPressureLevel.Moderate,
                < 0.9 => MemoryPressureLevel.High,
                _ => MemoryPressureLevel.Critical
            };
        }
    }

    /// <inheritdoc />
    public long TotalAllocatedMemory => _totalAllocatedMemory;

    /// <inheritdoc />
    public long MaxMemoryThreshold => _maxMemoryThreshold;

    /// <inheritdoc />
    public object?[] RentArray(int size)
    {
        ThrowIfDisposed();

        var pool = GetPoolForSize(size);
        var array = pool.Get();

        // Track memory allocation
        var memorySize = size * IntPtr.Size; // Rough estimate
        Interlocked.Add(ref _totalAllocatedMemory, memorySize);

        _logger.LogDebug("Rented array of size {Size}, total memory: {TotalMemory} bytes",
            size, _totalAllocatedMemory);

        return array;
    }

    /// <inheritdoc />
    public void ReturnArray(object?[] array, bool clearArray = true)
    {
        ThrowIfDisposed();

        if (array == null)
        {
            return;
        }

        if (clearArray)
        {
            Array.Clear(array);
        }

        var pool = GetPoolForSize(array.Length);
        pool.Return(array);

        // Track memory deallocation
        var memorySize = array.Length * IntPtr.Size;
        Interlocked.Add(ref _totalAllocatedMemory, -memorySize);

        _logger.LogDebug("Returned array of size {Size}, total memory: {TotalMemory} bytes",
            array.Length, _totalAllocatedMemory);
    }

    /// <inheritdoc />
    public long EstimateMemoryUsage(object data)
    {
        return data switch
        {
            object?[] array => array.Length * IntPtr.Size,
            string str => str.Length * sizeof(char),
            int => sizeof(int),
            long => sizeof(long),
            double => sizeof(double),
            DateTime => sizeof(long), // DateTime is stored as ticks
            Guid => 16, // 128 bits
            _ => IntPtr.Size // Default pointer size
        };
    }

    /// <inheritdoc />
    public void RequestCleanup(bool force = false)
    {
        ThrowIfDisposed();

        if (force || PressureLevel >= MemoryPressureLevel.High)
        {
            _logger.LogInformation("Performing memory cleanup, pressure level: {PressureLevel}", PressureLevel);

            // Request garbage collection
            GC.Collect(2, GCCollectionMode.Optimized);
            GC.WaitForPendingFinalizers();

            _logger.LogInformation("Memory cleanup completed, current memory: {TotalMemory} bytes",
                _totalAllocatedMemory);
        }
    }

    /// <inheritdoc />
    public bool IsUnderMemoryPressure()
    {
        return PressureLevel >= MemoryPressureLevel.High;
    }

    /// <summary>
    /// Sets the maximum memory threshold.
    /// </summary>
    /// <param name="threshold">The maximum memory threshold in bytes</param>
    public void SetMaxMemoryThreshold(long threshold)
    {
        if (threshold <= 0)
        {
            throw new ArgumentException("Threshold must be positive", nameof(threshold));
        }

        _maxMemoryThreshold = threshold;
        _logger.LogInformation("Memory threshold updated to {Threshold} bytes", threshold);
    }

    private ObjectPool<object?[]> GetPoolForSize(int size)
    {
        // Use default pool for common sizes
        if (size <= 64)
        {
            return _arrayPool;
        }

        // Create size-specific pools for larger arrays
        return _sizedPools.GetOrAdd(size, s =>
        {
            var provider = new DefaultObjectPoolProvider();
            return provider.Create(new ArrayPoolPolicy(s));
        });
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(MemoryManager));
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (!_disposed)
        {
            _logger.LogInformation("Disposing MemoryManager, final memory usage: {TotalMemory} bytes",
                _totalAllocatedMemory);
            _disposed = true;
        }
    }

    /// <summary>
    /// Object pool policy for object arrays.
    /// </summary>
    private sealed class ArrayPoolPolicy : IPooledObjectPolicy<object?[]>
    {
        private readonly int _size;

        public ArrayPoolPolicy(int size)
        {
            _size = size;
        }

        public object?[] Create()
        {
            return new object?[_size];
        }

        public bool Return(object?[] obj)
        {
            // Always accept returns
            return true;
        }
    }
}
