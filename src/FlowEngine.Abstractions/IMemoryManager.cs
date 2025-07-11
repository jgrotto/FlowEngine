using FlowEngine.Abstractions.Data;

namespace FlowEngine.Abstractions;

/// <summary>
/// Manages memory allocation and pooling for high-performance ArrayRow processing.
/// Implements memory pressure detection and spillover capabilities for unbounded datasets.
/// </summary>
public interface IMemoryManager : IDisposable
{
    /// <summary>
    /// Allocates a managed array of ArrayRow instances with automatic pooling.
    /// </summary>
    /// <param name="count">The number of rows to allocate</param>
    /// <param name="schema">The schema for the rows</param>
    /// <returns>A memory allocation that manages the lifecycle of the allocated rows</returns>
    ValueTask<MemoryAllocation<IArrayRow>> AllocateRowsAsync(int count, ISchema schema);

    /// <summary>
    /// Allocates a chunk with the specified capacity and schema.
    /// </summary>
    /// <param name="capacity">The initial capacity of the chunk</param>
    /// <param name="schema">The schema for the chunk</param>
    /// <returns>A memory allocation that manages the lifecycle of the allocated chunk</returns>
    ValueTask<MemoryAllocation<IChunk>> AllocateChunkAsync(int capacity, ISchema schema);

    /// <summary>
    /// Gets the current memory pressure level (0.0 to 1.0).
    /// </summary>
    double MemoryPressure { get; }

    /// <summary>
    /// Gets the total memory currently allocated by this manager in bytes.
    /// </summary>
    long TotalAllocatedMemory { get; }

    /// <summary>
    /// Gets the number of active allocations being managed.
    /// </summary>
    int ActiveAllocations { get; }

    /// <summary>
    /// Forces a cleanup of unused memory and returns resources to pools.
    /// </summary>
    /// <param name="aggressive">Whether to perform aggressive cleanup including GC</param>
    /// <returns>The amount of memory freed in bytes</returns>
    ValueTask<long> CleanupAsync(bool aggressive = false);

    /// <summary>
    /// Event raised when memory pressure reaches critical levels.
    /// </summary>
    event EventHandler<MemoryPressureEventArgs>? MemoryPressureChanged;
}

/// <summary>
/// Represents a managed memory allocation that automatically returns resources to pools when disposed.
/// </summary>
/// <typeparam name="T">The type of allocated resource</typeparam>
public sealed class MemoryAllocation<T> : IDisposable where T : class
{
    private readonly Action? _disposeAction;
    private T? _resource;
    private bool _disposed;

    /// <summary>
    /// Initializes a new memory allocation.
    /// </summary>
    /// <param name="resource">The allocated resource</param>
    /// <param name="disposeAction">Optional action to perform when disposing</param>
    public MemoryAllocation(T resource, Action? disposeAction = null)
    {
        _resource = resource ?? throw new ArgumentNullException(nameof(resource));
        _disposeAction = disposeAction;
    }

    /// <summary>
    /// Gets the allocated resource.
    /// </summary>
    /// <exception cref="ObjectDisposedException">Thrown when the allocation has been disposed</exception>
    public T Resource
    {
        get
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(MemoryAllocation<T>));
            }

            return _resource!;
        }
    }

    /// <summary>
    /// Gets whether this allocation has been disposed.
    /// </summary>
    public bool IsDisposed => _disposed;

    /// <summary>
    /// Disposes this allocation and returns the resource to the pool.
    /// </summary>
    public void Dispose()
    {
        if (!_disposed)
        {
            _disposeAction?.Invoke();
            _resource = null;
            _disposed = true;
        }
    }
}

/// <summary>
/// Event arguments for memory pressure change notifications.
/// </summary>
public sealed class MemoryPressureEventArgs : EventArgs
{
    /// <summary>
    /// Initializes a new instance of the MemoryPressureEventArgs class.
    /// </summary>
    /// <param name="currentPressure">The current memory pressure level (0.0 to 1.0)</param>
    /// <param name="previousPressure">The previous memory pressure level</param>
    /// <param name="totalMemory">The total memory allocated in bytes</param>
    public MemoryPressureEventArgs(double currentPressure, double previousPressure, long totalMemory)
    {
        CurrentPressure = currentPressure;
        PreviousPressure = previousPressure;
        TotalMemory = totalMemory;
    }

    /// <summary>
    /// Gets the current memory pressure level (0.0 to 1.0).
    /// </summary>
    public double CurrentPressure { get; }

    /// <summary>
    /// Gets the previous memory pressure level (0.0 to 1.0).
    /// </summary>
    public double PreviousPressure { get; }

    /// <summary>
    /// Gets the total memory allocated in bytes.
    /// </summary>
    public long TotalMemory { get; }

    /// <summary>
    /// Gets whether the pressure level indicates a critical state (>0.8).
    /// </summary>
    public bool IsCritical => CurrentPressure > 0.8;
}
