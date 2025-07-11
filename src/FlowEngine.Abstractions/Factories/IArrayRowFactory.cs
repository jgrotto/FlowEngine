using FlowEngine.Abstractions.Data;

namespace FlowEngine.Abstractions.Factories;

/// <summary>
/// Factory interface for creating ArrayRow instances.
/// Provides high-performance row creation with proper validation and optimization.
/// </summary>
public interface IArrayRowFactory
{
    /// <summary>
    /// Creates an ArrayRow with the specified schema and values.
    /// </summary>
    /// <param name="schema">Schema defining the structure</param>
    /// <param name="values">Values for each column (must match schema column count)</param>
    /// <returns>New ArrayRow instance</returns>
    /// <exception cref="ArgumentNullException">Thrown when schema or values is null</exception>
    /// <exception cref="ArgumentException">Thrown when values length doesn't match schema</exception>
    IArrayRow CreateRow(ISchema schema, object?[] values);

    /// <summary>
    /// Creates an ArrayRow with typed value initialization.
    /// </summary>
    /// <param name="schema">Schema defining the structure</param>
    /// <param name="valueInitializer">Function to initialize each field by index</param>
    /// <returns>New ArrayRow instance</returns>
    IArrayRow CreateRow(ISchema schema, Func<int, object?> valueInitializer);

    /// <summary>
    /// Creates an ArrayRow from key-value pairs with field name mapping.
    /// </summary>
    /// <param name="schema">Schema defining the structure</param>
    /// <param name="fieldValues">Dictionary of field names to values</param>
    /// <returns>New ArrayRow instance</returns>
    /// <exception cref="ArgumentException">Thrown when field names don't match schema</exception>
    IArrayRow CreateRow(ISchema schema, IReadOnlyDictionary<string, object?> fieldValues);

    /// <summary>
    /// Creates an ArrayRow with default values for all fields.
    /// Uses column default values where specified, null for nullable fields, 
    /// and appropriate default values for non-nullable types.
    /// </summary>
    /// <param name="schema">Schema defining the structure</param>
    /// <returns>New ArrayRow instance with default values</returns>
    IArrayRow CreateRowWithDefaults(ISchema schema);

    /// <summary>
    /// Creates a copy of an existing ArrayRow with modified values.
    /// </summary>
    /// <param name="source">Source row to copy</param>
    /// <param name="fieldUpdates">Dictionary of field names to new values</param>
    /// <returns>New ArrayRow instance with updated values</returns>
    IArrayRow CreateRowWithUpdates(IArrayRow source, IReadOnlyDictionary<string, object?> fieldUpdates);

    /// <summary>
    /// Creates a projected ArrayRow containing only specified fields.
    /// </summary>
    /// <param name="source">Source row to project from</param>
    /// <param name="targetSchema">Schema for the projected row</param>
    /// <returns>New ArrayRow instance with projected fields</returns>
    IArrayRow ProjectRow(IArrayRow source, ISchema targetSchema);

    /// <summary>
    /// Validates values against schema without creating a row.
    /// </summary>
    /// <param name="schema">Schema to validate against</param>
    /// <param name="values">Values to validate</param>
    /// <returns>Validation result indicating success or failure with details</returns>
    ValidationResult ValidateValues(ISchema schema, object?[] values);

    /// <summary>
    /// Creates multiple ArrayRows efficiently in batch.
    /// Optimized for bulk operations with minimal per-row overhead.
    /// </summary>
    /// <param name="schema">Schema for all rows</param>
    /// <param name="valueArrays">Array of value arrays, one per row</param>
    /// <returns>Array of ArrayRow instances</returns>
    IArrayRow[] CreateRows(ISchema schema, object?[][] valueArrays);
}
