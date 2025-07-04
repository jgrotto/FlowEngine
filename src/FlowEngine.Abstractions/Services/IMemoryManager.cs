using FlowEngine.Abstractions.Data;

namespace FlowEngine.Abstractions.Services;

/// <summary>
/// Provides memory management and pooling services for FlowEngine data structures.
/// Helps optimize memory usage and reduce garbage collection pressure.
/// </summary>
public interface IMemoryManager
{
    /// <summary>
    /// Gets the current memory pressure level.
    /// </summary>
    MemoryPressureLevel PressureLevel { get; }

    /// <summary>
    /// Gets the total memory allocated by FlowEngine in bytes.
    /// </summary>
    long TotalAllocatedMemory { get; }

    /// <summary>
    /// Gets the maximum memory threshold in bytes before spillover occurs.
    /// </summary>
    long MaxMemoryThreshold { get; }

    /// <summary>
    /// Rents a pre-allocated object array for ArrayRow creation.
    /// </summary>
    /// <param name="size">The size of the array needed</param>
    /// <returns>A rented array that must be returned via ReturnArray</returns>
    object?[] RentArray(int size);

    /// <summary>
    /// Returns a previously rented array to the pool.
    /// </summary>
    /// <param name="array">The array to return</param>
    /// <param name="clearArray">Whether to clear the array contents</param>
    void ReturnArray(object?[] array, bool clearArray = true);

    /// <summary>
    /// Estimates the memory usage of a given data structure.
    /// </summary>
    /// <param name="data">The data structure to estimate</param>
    /// <returns>Estimated memory usage in bytes</returns>
    long EstimateMemoryUsage(object data);

    /// <summary>
    /// Requests a memory cleanup operation.
    /// </summary>
    /// <param name="force">Whether to force an immediate cleanup</param>
    void RequestCleanup(bool force = false);

    /// <summary>
    /// Checks if the system is under memory pressure.
    /// </summary>
    /// <returns>True if under memory pressure, false otherwise</returns>
    bool IsUnderMemoryPressure();
}

/// <summary>
/// Represents the current memory pressure level.
/// </summary>
public enum MemoryPressureLevel
{
    /// <summary>
    /// Normal memory usage - no pressure.
    /// </summary>
    Normal,

    /// <summary>
    /// Moderate memory usage - should be monitored.
    /// </summary>
    Moderate,

    /// <summary>
    /// High memory usage - should consider cleanup.
    /// </summary>
    High,

    /// <summary>
    /// Critical memory usage - immediate action required.
    /// </summary>
    Critical
}