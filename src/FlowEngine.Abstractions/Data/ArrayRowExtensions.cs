using System.Runtime.CompilerServices;

namespace FlowEngine.Abstractions.Data;

/// <summary>
/// Extension methods for ArrayRow that provide high-performance access patterns
/// optimized for plugin development scenarios.
/// </summary>
public static class ArrayRowExtensions
{
    /// <summary>
    /// Gets a strongly-typed value from the row by column name with optimized performance.
    /// Uses the fastest path available for the specified type.
    /// </summary>
    /// <typeparam name="T">The expected type of the value</typeparam>
    /// <param name="row">The ArrayRow instance</param>
    /// <param name="columnName">The name of the column</param>
    /// <returns>The strongly-typed value</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static T GetTypedValue<T>(this IArrayRow row, string columnName)
    {
        return row.GetValue<T>(columnName)!;
    }

    /// <summary>
    /// Gets a strongly-typed value from the row by index with optimized performance.
    /// </summary>
    /// <typeparam name="T">The expected type of the value</typeparam>
    /// <param name="row">The ArrayRow instance</param>
    /// <param name="index">The zero-based column index</param>
    /// <returns>The strongly-typed value</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static T GetTypedValue<T>(this IArrayRow row, int index)
    {
        return row.GetValue<T>(index)!;
    }

    /// <summary>
    /// Gets a string value from the row with null-safe handling.
    /// </summary>
    /// <param name="row">The ArrayRow instance</param>
    /// <param name="columnName">The name of the column</param>
    /// <returns>The string value or empty string if null</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static string GetString(this IArrayRow row, string columnName)
    {
        return row.GetValue<string>(columnName) ?? string.Empty;
    }

    /// <summary>
    /// Gets a string value from the row by index with null-safe handling.
    /// </summary>
    /// <param name="row">The ArrayRow instance</param>
    /// <param name="index">The zero-based column index</param>
    /// <returns>The string value or empty string if null</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static string GetString(this IArrayRow row, int index)
    {
        return row.GetValue<string>(index) ?? string.Empty;
    }

    /// <summary>
    /// Gets an int32 value from the row with optimized performance.
    /// </summary>
    /// <param name="row">The ArrayRow instance</param>
    /// <param name="columnName">The name of the column</param>
    /// <returns>The int32 value</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int GetInt32(this IArrayRow row, string columnName)
    {
        return row.GetValue<int>(columnName);
    }

    /// <summary>
    /// Gets an int32 value from the row by index with optimized performance.
    /// </summary>
    /// <param name="row">The ArrayRow instance</param>
    /// <param name="index">The zero-based column index</param>
    /// <returns>The int32 value</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int GetInt32(this IArrayRow row, int index)
    {
        return row.GetValue<int>(index);
    }

    /// <summary>
    /// Gets an int64 value from the row with optimized performance.
    /// </summary>
    /// <param name="row">The ArrayRow instance</param>
    /// <param name="columnName">The name of the column</param>
    /// <returns>The int64 value</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static long GetInt64(this IArrayRow row, string columnName)
    {
        return row.GetValue<long>(columnName);
    }

    /// <summary>
    /// Gets an int64 value from the row by index with optimized performance.
    /// </summary>
    /// <param name="row">The ArrayRow instance</param>
    /// <param name="index">The zero-based column index</param>
    /// <returns>The int64 value</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static long GetInt64(this IArrayRow row, int index)
    {
        return row.GetValue<long>(index);
    }

    /// <summary>
    /// Gets a double value from the row with optimized performance.
    /// </summary>
    /// <param name="row">The ArrayRow instance</param>
    /// <param name="columnName">The name of the column</param>
    /// <returns>The double value</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static double GetDouble(this IArrayRow row, string columnName)
    {
        return row.GetValue<double>(columnName);
    }

    /// <summary>
    /// Gets a double value from the row by index with optimized performance.
    /// </summary>
    /// <param name="row">The ArrayRow instance</param>
    /// <param name="index">The zero-based column index</param>
    /// <returns>The double value</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static double GetDouble(this IArrayRow row, int index)
    {
        return row.GetValue<double>(index);
    }

    /// <summary>
    /// Gets a decimal value from the row with optimized performance.
    /// </summary>
    /// <param name="row">The ArrayRow instance</param>
    /// <param name="columnName">The name of the column</param>
    /// <returns>The decimal value</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static decimal GetDecimal(this IArrayRow row, string columnName)
    {
        return row.GetValue<decimal>(columnName);
    }

    /// <summary>
    /// Gets a decimal value from the row by index with optimized performance.
    /// </summary>
    /// <param name="row">The ArrayRow instance</param>
    /// <param name="index">The zero-based column index</param>
    /// <returns>The decimal value</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static decimal GetDecimal(this IArrayRow row, int index)
    {
        return row.GetValue<decimal>(index);
    }

    /// <summary>
    /// Gets a DateTime value from the row with optimized performance.
    /// </summary>
    /// <param name="row">The ArrayRow instance</param>
    /// <param name="columnName">The name of the column</param>
    /// <returns>The DateTime value</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static DateTime GetDateTime(this IArrayRow row, string columnName)
    {
        return row.GetValue<DateTime>(columnName);
    }

    /// <summary>
    /// Gets a DateTime value from the row by index with optimized performance.
    /// </summary>
    /// <param name="row">The ArrayRow instance</param>
    /// <param name="index">The zero-based column index</param>
    /// <returns>The DateTime value</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static DateTime GetDateTime(this IArrayRow row, int index)
    {
        return row.GetValue<DateTime>(index);
    }

    /// <summary>
    /// Gets a bool value from the row with optimized performance.
    /// </summary>
    /// <param name="row">The ArrayRow instance</param>
    /// <param name="columnName">The name of the column</param>
    /// <returns>The bool value</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool GetBoolean(this IArrayRow row, string columnName)
    {
        return row.GetValue<bool>(columnName);
    }

    /// <summary>
    /// Gets a bool value from the row by index with optimized performance.
    /// </summary>
    /// <param name="row">The ArrayRow instance</param>
    /// <param name="index">The zero-based column index</param>
    /// <returns>The bool value</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool GetBoolean(this IArrayRow row, int index)
    {
        return row.GetValue<bool>(index);
    }

    /// <summary>
    /// Gets a Guid value from the row with optimized performance.
    /// </summary>
    /// <param name="row">The ArrayRow instance</param>
    /// <param name="columnName">The name of the column</param>
    /// <returns>The Guid value</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Guid GetGuid(this IArrayRow row, string columnName)
    {
        return row.GetValue<Guid>(columnName);
    }

    /// <summary>
    /// Gets a Guid value from the row by index with optimized performance.
    /// </summary>
    /// <param name="row">The ArrayRow instance</param>
    /// <param name="index">The zero-based column index</param>
    /// <returns>The Guid value</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Guid GetGuid(this IArrayRow row, int index)
    {
        return row.GetValue<Guid>(index);
    }

    /// <summary>
    /// Safely gets a nullable value from the row with type conversion.
    /// </summary>
    /// <typeparam name="T">The expected nullable type</typeparam>
    /// <param name="row">The ArrayRow instance</param>
    /// <param name="columnName">The name of the column</param>
    /// <returns>The nullable value</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static T? GetNullable<T>(this IArrayRow row, string columnName) where T : struct
    {
        return row.TryGetValue<T?>(columnName, out var value) ? value : null;
    }

    /// <summary>
    /// Safely gets a nullable value from the row by index with type conversion.
    /// </summary>
    /// <typeparam name="T">The expected nullable type</typeparam>
    /// <param name="row">The ArrayRow instance</param>
    /// <param name="index">The zero-based column index</param>
    /// <returns>The nullable value</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static T? GetNullable<T>(this IArrayRow row, int index) where T : struct
    {
        if (index < 0 || index >= row.ColumnCount)
            return null;
        
        return row.GetValue<T?>(index);
    }

    /// <summary>
    /// Gets a value with a default if the column doesn't exist or is null.
    /// </summary>
    /// <typeparam name="T">The expected type</typeparam>
    /// <param name="row">The ArrayRow instance</param>
    /// <param name="columnName">The name of the column</param>
    /// <param name="defaultValue">The default value to return</param>
    /// <returns>The value or default</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static T GetValueOrDefault<T>(this IArrayRow row, string columnName, T defaultValue)
    {
        return row.TryGetValue<T>(columnName, out var value) ? value! : defaultValue;
    }

    /// <summary>
    /// Creates a dictionary representation of the row for debugging or serialization.
    /// </summary>
    /// <param name="row">The ArrayRow instance</param>
    /// <returns>Dictionary mapping column names to values</returns>
    public static IReadOnlyDictionary<string, object?> ToDictionary(this IArrayRow row)
    {
        var result = new Dictionary<string, object?>(row.ColumnCount);
        var columnNames = row.ColumnNames;
        
        for (int i = 0; i < row.ColumnCount; i++)
        {
            result[columnNames[i]] = row[i];
        }
        
        return result;
    }

    /// <summary>
    /// Creates a copy of the row with optimized field access patterns.
    /// Useful for performance-critical scenarios where you need to ensure optimal access.
    /// </summary>
    /// <param name="row">The ArrayRow instance</param>
    /// <param name="factory">Factory for creating new ArrayRow instances</param>
    /// <returns>A new optimized ArrayRow instance</returns>
    public static IArrayRow Copy(this IArrayRow row, IArrayRowFactory factory)
    {
        var values = new object?[row.ColumnCount];
        var span = row.AsSpan();
        
        for (int i = 0; i < values.Length; i++)
        {
            values[i] = span[i];
        }
        
        return factory.Create(row.Schema, values);
    }

    /// <summary>
    /// Compares two ArrayRows for value equality across all columns.
    /// </summary>
    /// <param name="row">The first ArrayRow</param>
    /// <param name="other">The second ArrayRow</param>
    /// <returns>True if all column values are equal</returns>
    public static bool ValuesEqual(this IArrayRow row, IArrayRow other)
    {
        if (ReferenceEquals(row, other))
            return true;
        
        if (row.ColumnCount != other.ColumnCount)
            return false;
        
        var thisSpan = row.AsSpan();
        var otherSpan = other.AsSpan();
        
        for (int i = 0; i < thisSpan.Length; i++)
        {
            var thisValue = thisSpan[i];
            var otherValue = otherSpan[i];
            
            if (!Equals(thisValue, otherValue))
                return false;
        }
        
        return true;
    }

    /// <summary>
    /// Projects the row to a new schema by mapping column names.
    /// Useful for schema transformations in transform plugins.
    /// </summary>
    /// <param name="row">The source ArrayRow</param>
    /// <param name="targetSchema">The target schema</param>
    /// <param name="factory">Factory for creating new ArrayRow instances</param>
    /// <param name="columnMapping">Optional mapping from target column names to source column names</param>
    /// <returns>A new ArrayRow with the target schema</returns>
    public static IArrayRow ProjectTo(this IArrayRow row, ISchema targetSchema, IArrayRowFactory factory, 
        IReadOnlyDictionary<string, string>? columnMapping = null)
    {
        var builder = factory.CreateBuilder(targetSchema);
        
        foreach (var targetColumn in targetSchema.Columns)
        {
            var sourceColumnName = columnMapping?.TryGetValue(targetColumn.Name, out var mapped) == true 
                ? mapped 
                : targetColumn.Name;
            
            if (row.TryGetValue<object>(sourceColumnName, out var value))
            {
                builder.Set(targetColumn.Name, value);
            }
        }
        
        return builder.Build();
    }
}