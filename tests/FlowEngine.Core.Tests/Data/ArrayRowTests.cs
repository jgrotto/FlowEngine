using FlowEngine.Abstractions;
using FlowEngine.Abstractions.Data;
using FlowEngine.Core.Data;
using Xunit;

namespace FlowEngine.Core.Tests.Data;

/// <summary>
/// Tests for ArrayRow implementation to validate performance and correctness.
/// </summary>
public class ArrayRowTests
{
    private readonly ISchema _testSchema;
    private readonly ColumnDefinition[] _testColumns;

    public ArrayRowTests()
    {
        _testColumns = new[]
        {
            new ColumnDefinition { Name = "Id", DataType = typeof(int), IsNullable = false },
            new ColumnDefinition { Name = "Name", DataType = typeof(string), IsNullable = false },
            new ColumnDefinition { Name = "Email", DataType = typeof(string), IsNullable = true },
            new ColumnDefinition { Name = "Age", DataType = typeof(int), IsNullable = true },
            new ColumnDefinition { Name = "Score", DataType = typeof(double), IsNullable = true }
        };
        _testSchema = new Schema(_testColumns);
    }

    [Fact]
    public void Constructor_WithValidData_CreatesArrayRow()
    {
        // Arrange
        var values = new object?[] { 1, "John Doe", "john@example.com", 30, 95.5 };

        // Act
        var row = new ArrayRow(_testSchema, values);

        // Assert
        Assert.NotNull(row);
        Assert.Equal(_testSchema, row.Schema);
        Assert.Equal(5, row.ColumnCount);
    }

    [Fact]
    public void Constructor_WithMismatchedValueCount_ThrowsException()
    {
        // Arrange
        var values = new object?[] { 1, "John Doe" }; // Only 2 values for 5 columns

        // Act & Assert
        Assert.Throws<ArgumentException>(() => new ArrayRow(_testSchema, values));
    }

    [Fact]
    public void IndexerByName_WithValidColumn_ReturnsCorrectValue()
    {
        // Arrange
        var values = new object?[] { 1, "John Doe", "john@example.com", 30, 95.5 };
        var row = new ArrayRow(_testSchema, values);

        // Act & Assert
        Assert.Equal(1, row["Id"]);
        Assert.Equal("John Doe", row["Name"]);
        Assert.Equal("john@example.com", row["Email"]);
        Assert.Equal(30, row["Age"]);
        Assert.Equal(95.5, row["Score"]);
    }

    [Fact]
    public void IndexerByName_WithInvalidColumn_ReturnsNull()
    {
        // Arrange
        var values = new object?[] { 1, "John Doe", "john@example.com", 30, 95.5 };
        var row = new ArrayRow(_testSchema, values);

        // Act
        var result = row["NonExistentColumn"];

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void IndexerByIndex_WithValidIndex_ReturnsCorrectValue()
    {
        // Arrange
        var values = new object?[] { 1, "John Doe", "john@example.com", 30, 95.5 };
        var row = new ArrayRow(_testSchema, values);

        // Act & Assert
        Assert.Equal(1, row[0]);
        Assert.Equal("John Doe", row[1]);
        Assert.Equal("john@example.com", row[2]);
        Assert.Equal(30, row[3]);
        Assert.Equal(95.5, row[4]);
    }

    [Fact]
    public void IndexerByIndex_WithInvalidIndex_ThrowsException()
    {
        // Arrange
        var values = new object?[] { 1, "John Doe", "john@example.com", 30, 95.5 };
        var row = new ArrayRow(_testSchema, values);

        // Act & Assert
        Assert.Throws<ArgumentOutOfRangeException>(() => row[10]);
        Assert.Throws<ArgumentOutOfRangeException>(() => row[-1]);
    }

    [Fact]
    public void With_ByName_CreatesNewRowWithUpdatedValue()
    {
        // Arrange
        var values = new object?[] { 1, "John Doe", "john@example.com", 30, 95.5 };
        var row = new ArrayRow(_testSchema, values);

        // Act
        var updatedRow = row.With("Name", "Jane Smith");

        // Assert
        Assert.NotSame(row, updatedRow);
        Assert.Equal("John Doe", row["Name"]); // Original unchanged
        Assert.Equal("Jane Smith", updatedRow["Name"]); // New row updated
        Assert.Equal(1, updatedRow["Id"]); // Other values preserved
    }

    [Fact]
    public void With_ByIndex_CreatesNewRowWithUpdatedValue()
    {
        // Arrange
        var values = new object?[] { 1, "John Doe", "john@example.com", 30, 95.5 };
        var row = new ArrayRow(_testSchema, values);

        // Act
        var updatedRow = row.With(1, "Jane Smith");

        // Assert
        Assert.NotSame(row, updatedRow);
        Assert.Equal("John Doe", row[1]); // Original unchanged
        Assert.Equal("Jane Smith", updatedRow[1]); // New row updated
        Assert.Equal(1, updatedRow[0]); // Other values preserved
    }

    [Fact]
    public void With_SameValue_ReturnsSameInstance()
    {
        // Arrange
        var values = new object?[] { 1, "John Doe", "john@example.com", 30, 95.5 };
        var row = new ArrayRow(_testSchema, values);

        // Act
        var updatedRow = row.With("Name", "John Doe");

        // Assert
        Assert.Same(row, updatedRow); // Should return same instance for same value
    }

    [Fact]
    public void WithMany_WithMultipleUpdates_CreatesNewRowWithAllUpdates()
    {
        // Arrange
        var values = new object?[] { 1, "John Doe", "john@example.com", 30, 95.5 };
        var row = new ArrayRow(_testSchema, values);
        var updates = new Dictionary<string, object?>
        {
            { "Name", "Jane Smith" },
            { "Age", 25 },
            { "Score", 98.0 }
        };

        // Act
        var updatedRow = row.WithMany(updates);

        // Assert
        Assert.NotSame(row, updatedRow);
        Assert.Equal("Jane Smith", updatedRow["Name"]);
        Assert.Equal(25, updatedRow["Age"]);
        Assert.Equal(98.0, updatedRow["Score"]);
        Assert.Equal(1, updatedRow["Id"]); // Unchanged value preserved
        Assert.Equal("john@example.com", updatedRow["Email"]); // Unchanged value preserved
    }

    [Fact]
    public void GetValue_WithValidType_ReturnsTypedValue()
    {
        // Arrange
        var values = new object?[] { 1, "John Doe", "john@example.com", 30, 95.5 };
        var row = new ArrayRow(_testSchema, values);

        // Act & Assert
        Assert.Equal(1, row.GetValue<int>("Id"));
        Assert.Equal("John Doe", row.GetValue<string>("Name"));
        Assert.Equal(30, row.GetValue<int>("Age"));
        Assert.Equal(95.5, row.GetValue<double>("Score"));
    }

    [Fact]
    public void TryGetValue_WithValidType_ReturnsTrue()
    {
        // Arrange
        var values = new object?[] { 1, "John Doe", "john@example.com", 30, 95.5 };
        var row = new ArrayRow(_testSchema, values);

        // Act
        var success = row.TryGetValue<string>("Name", out var name);

        // Assert
        Assert.True(success);
        Assert.Equal("John Doe", name);
    }

    [Fact]
    public void TryGetValue_WithInvalidColumn_ReturnsFalse()
    {
        // Arrange
        var values = new object?[] { 1, "John Doe", "john@example.com", 30, 95.5 };
        var row = new ArrayRow(_testSchema, values);

        // Act
        var success = row.TryGetValue<string>("NonExistentColumn", out var value);

        // Assert
        Assert.False(success);
        Assert.Null(value);
    }

    [Fact]
    public void AsSpan_ReturnsReadOnlySpanOfValues()
    {
        // Arrange
        var values = new object?[] { 1, "John Doe", "john@example.com", 30, 95.5 };
        var row = new ArrayRow(_testSchema, values);

        // Act
        var span = row.AsSpan();

        // Assert
        Assert.Equal(5, span.Length);
        Assert.Equal(1, span[0]);
        Assert.Equal("John Doe", span[1]);
        Assert.Equal("john@example.com", span[2]);
        Assert.Equal(30, span[3]);
        Assert.Equal(95.5, span[4]);
    }

    [Fact]
    public void Equals_WithIdenticalRows_ReturnsTrue()
    {
        // Arrange
        var values1 = new object?[] { 1, "John Doe", "john@example.com", 30, 95.5 };
        var values2 = new object?[] { 1, "John Doe", "john@example.com", 30, 95.5 };
        var row1 = new ArrayRow(_testSchema, values1);
        var row2 = new ArrayRow(_testSchema, values2);

        // Act & Assert
        Assert.True(row1.Equals(row2));
        Assert.True(row2.Equals(row1));
        Assert.Equal(row1.GetHashCode(), row2.GetHashCode());
    }

    [Fact]
    public void Equals_WithDifferentValues_ReturnsFalse()
    {
        // Arrange
        var values1 = new object?[] { 1, "John Doe", "john@example.com", 30, 95.5 };
        var values2 = new object?[] { 2, "Jane Smith", "jane@example.com", 25, 88.0 };
        var row1 = new ArrayRow(_testSchema, values1);
        var row2 = new ArrayRow(_testSchema, values2);

        // Act & Assert
        Assert.False(row1.Equals(row2));
        Assert.False(row2.Equals(row1));
    }

    [Fact]
    public void FromDictionary_CreatesCorrectArrayRow()
    {
        // Arrange
        var data = new Dictionary<string, object?>
        {
            { "Id", 1 },
            { "Name", "John Doe" },
            { "Email", "john@example.com" },
            { "Age", 30 },
            { "Score", 95.5 }
        };

        // Act
        var row = ArrayRow.FromDictionary(_testSchema, data);

        // Assert
        Assert.Equal(1, row["Id"]);
        Assert.Equal("John Doe", row["Name"]);
        Assert.Equal("john@example.com", row["Email"]);
        Assert.Equal(30, row["Age"]);
        Assert.Equal(95.5, row["Score"]);
    }

    [Fact]
    public void Empty_CreatesRowWithNullValues()
    {
        // Act
        var row = ArrayRow.Empty(_testSchema);

        // Assert
        Assert.Equal(_testSchema, row.Schema);
        Assert.Equal(5, row.ColumnCount);
        Assert.Null(row["Id"]);
        Assert.Null(row["Name"]);
        Assert.Null(row["Email"]);
        Assert.Null(row["Age"]);
        Assert.Null(row["Score"]);
    }
}