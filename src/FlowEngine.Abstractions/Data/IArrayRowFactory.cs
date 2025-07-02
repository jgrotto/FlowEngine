namespace FlowEngine.Abstractions.Data;

/// <summary>
/// Factory interface for creating ArrayRow instances.
/// Provides a way for plugins to create ArrayRow instances without directly referencing FlowEngine.Core.
/// </summary>
public interface IArrayRowFactory
{
    /// <summary>
    /// Creates a new ArrayRow with the specified schema and values.
    /// </summary>
    /// <param name="schema">The schema defining the structure of the row</param>
    /// <param name="values">The values for each column</param>
    /// <returns>A new ArrayRow instance</returns>
    IArrayRow Create(ISchema schema, object?[] values);

    /// <summary>
    /// Creates a new ArrayRow from a dictionary of column names and values.
    /// </summary>
    /// <param name="schema">The schema for the row</param>
    /// <param name="data">The column data</param>
    /// <returns>A new ArrayRow instance</returns>
    IArrayRow CreateFromDictionary(ISchema schema, IReadOnlyDictionary<string, object?> data);

    /// <summary>
    /// Creates a new ArrayRow with all null values for the given schema.
    /// </summary>
    /// <param name="schema">The schema for the row</param>
    /// <returns>A new ArrayRow instance with null values</returns>
    IArrayRow CreateEmpty(ISchema schema);

    /// <summary>
    /// Creates a new ArrayRow with default values based on column types.
    /// </summary>
    /// <param name="schema">The schema for the row</param>
    /// <returns>A new ArrayRow instance with default values</returns>
    IArrayRow CreateWithDefaults(ISchema schema);

    /// <summary>
    /// Creates a builder for constructing ArrayRows with fluent syntax.
    /// </summary>
    /// <param name="schema">The schema for the row</param>
    /// <returns>An ArrayRow builder instance</returns>
    IArrayRowBuilder CreateBuilder(ISchema schema);
}

/// <summary>
/// Builder interface for constructing ArrayRows with fluent syntax.
/// Provides an efficient way to build rows with partial data.
/// </summary>
public interface IArrayRowBuilder
{
    /// <summary>
    /// Gets the schema associated with this builder.
    /// </summary>
    ISchema Schema { get; }

    /// <summary>
    /// Sets the value for the specified column by name.
    /// </summary>
    /// <param name="columnName">The name of the column</param>
    /// <param name="value">The value to set</param>
    /// <returns>This builder instance for method chaining</returns>
    IArrayRowBuilder Set(string columnName, object? value);

    /// <summary>
    /// Sets the value for the specified column by index.
    /// </summary>
    /// <param name="index">The zero-based index of the column</param>
    /// <param name="value">The value to set</param>
    /// <returns>This builder instance for method chaining</returns>
    IArrayRowBuilder Set(int index, object? value);

    /// <summary>
    /// Sets multiple values from a dictionary.
    /// </summary>
    /// <param name="values">Dictionary of column names to values</param>
    /// <returns>This builder instance for method chaining</returns>
    IArrayRowBuilder SetMany(IReadOnlyDictionary<string, object?> values);

    /// <summary>
    /// Gets the current value for the specified column by name.
    /// </summary>
    /// <param name="columnName">The name of the column</param>
    /// <returns>The current value of the column</returns>
    object? Get(string columnName);

    /// <summary>
    /// Gets the current value for the specified column by index.
    /// </summary>
    /// <param name="index">The zero-based index of the column</param>
    /// <returns>The current value of the column</returns>
    object? Get(int index);

    /// <summary>
    /// Tries to get the current value for the specified column.
    /// </summary>
    /// <param name="columnName">The name of the column</param>
    /// <param name="value">The current value if found</param>
    /// <returns>True if the column exists, false otherwise</returns>
    bool TryGet(string columnName, out object? value);

    /// <summary>
    /// Checks if a value has been set for the specified column.
    /// </summary>
    /// <param name="columnName">The name of the column</param>
    /// <returns>True if a value has been set, false otherwise</returns>
    bool HasValue(string columnName);

    /// <summary>
    /// Checks if a value has been set for the specified column by index.
    /// </summary>
    /// <param name="index">The zero-based index of the column</param>
    /// <returns>True if a value has been set, false otherwise</returns>
    bool HasValue(int index);

    /// <summary>
    /// Clears the value for the specified column (sets it to null).
    /// </summary>
    /// <param name="columnName">The name of the column</param>
    /// <returns>This builder instance for method chaining</returns>
    IArrayRowBuilder Clear(string columnName);

    /// <summary>
    /// Clears all values in the builder.
    /// </summary>
    /// <returns>This builder instance for method chaining</returns>
    IArrayRowBuilder ClearAll();

    /// <summary>
    /// Validates that all required columns have been set.
    /// </summary>
    /// <returns>List of validation errors, empty if valid</returns>
    IReadOnlyList<string> Validate();

    /// <summary>
    /// Builds the final ArrayRow instance.
    /// </summary>
    /// <returns>A new immutable ArrayRow instance</returns>
    IArrayRow Build();

    /// <summary>
    /// Builds the final ArrayRow instance with validation.
    /// </summary>
    /// <param name="validateRequired">Whether to validate that all required columns are set</param>
    /// <returns>A new immutable ArrayRow instance</returns>
    /// <exception cref="InvalidOperationException">Thrown when validation fails</exception>
    IArrayRow Build(bool validateRequired);
}