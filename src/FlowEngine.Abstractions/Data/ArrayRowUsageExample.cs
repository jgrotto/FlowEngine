namespace FlowEngine.Abstractions.Data;

/// <summary>
/// Example demonstrating how plugins can work with ArrayRow instances
/// using only the Abstractions layer (no direct FlowEngine.Core dependency).
/// </summary>
public static class ArrayRowUsageExample
{
    /// <summary>
    /// Example of creating ArrayRow instances in a plugin.
    /// </summary>
    /// <param name="factory">Injected ArrayRow factory</param>
    /// <param name="schema">Schema for the data</param>
    /// <returns>Example ArrayRow instances</returns>
    public static IEnumerable<IArrayRow> CreateExampleRows(IArrayRowFactory factory, ISchema schema)
    {
        // Method 1: Create from array
        var row1 = factory.Create(schema, new object?[] { 1, "John", 25, true });
        yield return row1;

        // Method 2: Create from dictionary
        var row2 = factory.CreateFromDictionary(schema, new Dictionary<string, object?>
        {
            ["id"] = 2,
            ["name"] = "Jane",
            ["age"] = 30,
            ["active"] = false
        });
        yield return row2;

        // Method 3: Create using builder (fluent API)
        var row3 = factory.CreateBuilder(schema)
            .Set("id", 3)
            .Set("name", "Bob")
            .Set("age", 35)
            .Set("active", true)
            .Build();
        yield return row3;

        // Method 4: Create empty and populate
        var row4 = factory.CreateEmpty(schema)
            .With("id", 4)
            .With("name", "Alice")
            .With("age", 28)
            .With("active", true);
        yield return row4;
    }

    /// <summary>
    /// Example of reading data from ArrayRow instances with optimized access patterns.
    /// </summary>
    /// <param name="rows">ArrayRow instances to read</param>
    /// <param name="schema">Schema with pre-calculated field indexes</param>
    /// <returns>Processed data summary</returns>
    public static ProcessingSummary ProcessRowsOptimized(IEnumerable<IArrayRow> rows, ISchema schema)
    {
        // Pre-calculate field indexes for O(1) access (critical for performance)
        var idIndex = schema.GetIndex("id");
        var nameIndex = schema.GetIndex("name");
        var ageIndex = schema.GetIndex("age");
        var activeIndex = schema.GetIndex("active");

        var totalRows = 0;
        var activeCount = 0;
        var totalAge = 0;
        var names = new List<string>();

        foreach (var row in rows)
        {
            totalRows++;

            // Use index-based access for maximum performance (<15ns per field)
            var id = row.GetInt32(idIndex);
            var name = row.GetString(nameIndex);
            var age = row.GetInt32(ageIndex);
            var active = row.GetBoolean(activeIndex);

            if (active)
                activeCount++;

            totalAge += age;
            names.Add(name);
        }

        return new ProcessingSummary
        {
            TotalRows = totalRows,
            ActiveCount = activeCount,
            AverageAge = totalRows > 0 ? (double)totalAge / totalRows : 0,
            Names = names
        };
    }

    /// <summary>
    /// Example of transforming ArrayRow instances (useful for Transform plugins).
    /// </summary>
    /// <param name="sourceRows">Source rows to transform</param>
    /// <param name="sourceSchema">Source schema</param>
    /// <param name="targetSchema">Target schema</param>
    /// <param name="factory">ArrayRow factory</param>
    /// <returns>Transformed rows</returns>
    public static IEnumerable<IArrayRow> TransformRows(
        IEnumerable<IArrayRow> sourceRows,
        ISchema sourceSchema,
        ISchema targetSchema,
        IArrayRowFactory factory)
    {
        // Pre-calculate source field indexes
        var sourceIdIndex = sourceSchema.GetIndex("id");
        var sourceNameIndex = sourceSchema.GetIndex("name");
        var sourceAgeIndex = sourceSchema.GetIndex("age");

        foreach (var sourceRow in sourceRows)
        {
            // Extract data with optimized access
            var id = sourceRow.GetInt32(sourceIdIndex);
            var name = sourceRow.GetString(sourceNameIndex);
            var age = sourceRow.GetInt32(sourceAgeIndex);

            // Transform data
            var upperName = name.ToUpper();
            var isAdult = age >= 18;
            var category = age switch
            {
                < 18 => "Child",
                < 65 => "Adult",
                _ => "Senior"
            };

            // Create transformed row using builder
            var transformedRow = factory.CreateBuilder(targetSchema)
                .Set("id", id)
                .Set("upper_name", upperName)
                .Set("is_adult", isAdult)
                .Set("age_category", category)
                .Set("processed_at", DateTimeOffset.UtcNow)
                .Build();

            yield return transformedRow;
        }
    }

    /// <summary>
    /// Example of validating and filtering ArrayRow instances.
    /// </summary>
    /// <param name="rows">Rows to validate and filter</param>
    /// <param name="schema">Schema for validation</param>
    /// <returns>Valid rows only</returns>
    public static IEnumerable<IArrayRow> ValidateAndFilter(IEnumerable<IArrayRow> rows, ISchema schema)
    {
        var nameIndex = schema.GetIndex("name");
        var ageIndex = schema.GetIndex("age");

        foreach (var row in rows)
        {
            // Validate required fields using direct index access for performance
            var name = row.GetString(nameIndex);
            if (string.IsNullOrWhiteSpace(name))
                continue; // Skip invalid rows

            var age = row.GetInt32(ageIndex);
            if (age < 0 || age > 150)
                continue; // Skip invalid rows

            yield return row;
        }
    }

    /// <summary>
    /// Example of batch processing with chunking for memory efficiency.
    /// </summary>
    /// <param name="rows">Rows to process in batches</param>
    /// <param name="batchSize">Size of each batch</param>
    /// <param name="processor">Function to process each batch</param>
    /// <returns>Batch processing results</returns>
    public static async IAsyncEnumerable<BatchResult> ProcessInBatches(
        IAsyncEnumerable<IArrayRow> rows,
        int batchSize,
        Func<IReadOnlyList<IArrayRow>, Task<BatchResult>> processor)
    {
        var batch = new List<IArrayRow>(batchSize);

        await foreach (var row in rows)
        {
            batch.Add(row);

            if (batch.Count >= batchSize)
            {
                var result = await processor(batch);
                yield return result;
                batch.Clear();
            }
        }

        // Process remaining rows
        if (batch.Count > 0)
        {
            var result = await processor(batch);
            yield return result;
        }
    }
}

/// <summary>
/// Example processing summary result.
/// </summary>
public sealed record ProcessingSummary
{
    public required int TotalRows { get; init; }
    public required int ActiveCount { get; init; }
    public required double AverageAge { get; init; }
    public required List<string> Names { get; init; }
}

/// <summary>
/// Example batch processing result.
/// </summary>
public sealed record BatchResult
{
    public required int ProcessedCount { get; init; }
    public required TimeSpan ProcessingTime { get; init; }
    public required long MemoryUsed { get; init; }
    public IReadOnlyList<string> Errors { get; init; } = Array.Empty<string>();
}