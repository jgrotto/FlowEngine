namespace FlowEngine.Abstractions.Data;

/// <summary>
/// Represents an immutable data row with schema-aware, high-performance field access.
/// Provides O(1) field access through pre-calculated column indexes for maximum throughput.
/// Target performance: &lt;15ns field access, 200K+ rows/sec processing.
/// </summary>
public interface IArrayRow : IEquatable<IArrayRow>
{
    /// <summary>
    /// Gets the schema that defines the structure of this row.
    /// </summary>
    ISchema Schema { get; }

    /// <summary>
    /// Gets the number of columns in this row.
    /// </summary>
    int ColumnCount { get; }

    /// <summary>
    /// Gets the value of the specified column by name.
    /// Uses O(1) schema index lookup for maximum performance.
    /// </summary>
    /// <param name="columnName">The name of the column</param>
    /// <returns>The value of the column, or null if the column doesn't exist</returns>
    object? this[string columnName] { get; }

    /// <summary>
    /// Gets the value of the specified column by zero-based index.
    /// Provides the fastest possible field access.
    /// </summary>
    /// <param name="index">The zero-based index of the column</param>
    /// <returns>The value of the column</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when index is out of range</exception>
    object? this[int index] { get; }

    /// <summary>
    /// Creates a new row with the specified column value changed.
    /// Implements copy-on-write semantics for efficient immutable updates.
    /// </summary>
    /// <param name="columnName">The name of the column to update</param>
    /// <param name="value">The new value for the column</param>
    /// <returns>A new IArrayRow instance with the updated value</returns>
    /// <exception cref="ArgumentException">Thrown when the column doesn't exist in the schema</exception>
    IArrayRow With(string columnName, object? value);

    /// <summary>
    /// Creates a new row with the specified column value changed by index.
    /// Provides the fastest possible field update operation.
    /// </summary>
    /// <param name="index">The zero-based index of the column to update</param>
    /// <param name="value">The new value for the column</param>
    /// <returns>A new IArrayRow instance with the updated value</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when index is out of range</exception>
    IArrayRow With(int index, object? value);

    /// <summary>
    /// Creates a new row with multiple column values changed.
    /// Optimized for bulk updates to minimize allocations.
    /// </summary>
    /// <param name="updates">Dictionary of column names to new values</param>
    /// <returns>A new IArrayRow instance with the updated values</returns>
    IArrayRow WithMany(IReadOnlyDictionary<string, object?> updates);

    /// <summary>
    /// Gets the value of the specified column with type safety.
    /// </summary>
    /// <typeparam name="T">The expected type of the column value</typeparam>
    /// <param name="columnName">The name of the column</param>
    /// <returns>The typed value of the column</returns>
    /// <exception cref="InvalidCastException">Thrown when the value cannot be cast to the specified type</exception>
    T? GetValue<T>(string columnName);

    /// <summary>
    /// Gets the value of the specified column by index with type safety.
    /// </summary>
    /// <typeparam name="T">The expected type of the column value</typeparam>
    /// <param name="index">The zero-based index of the column</param>
    /// <returns>The typed value of the column</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when index is out of range</exception>
    /// <exception cref="InvalidCastException">Thrown when the value cannot be cast to the specified type</exception>
    T? GetValue<T>(int index);

    /// <summary>
    /// Attempts to get the value of the specified column with type safety.
    /// </summary>
    /// <typeparam name="T">The expected type of the column value</typeparam>
    /// <param name="columnName">The name of the column</param>
    /// <param name="value">When this method returns, contains the typed value if successful</param>
    /// <returns>true if the column exists and the value can be cast to the specified type; otherwise, false</returns>
    bool TryGetValue<T>(string columnName, out T? value);

    /// <summary>
    /// Gets all values in this row as a read-only span for high-performance iteration.
    /// </summary>
    /// <returns>A read-only span containing all column values</returns>
    ReadOnlySpan<object?> AsSpan();

    /// <summary>
    /// Gets all column names in this row.
    /// </summary>
    IReadOnlyList<string> ColumnNames { get; }
}