using FlowEngine.Abstractions;
using FlowEngine.Abstractions.Data;
using FlowEngine.Core.Data;
using Xunit;

namespace FlowEngine.Core.Tests.Data;

/// <summary>
/// Tests for Schema implementation to validate performance and correctness.
/// </summary>
public class SchemaTests
{
    [Fact]
    public void Constructor_WithValidColumns_CreatesSchema()
    {
        // Arrange
        var columns = new[]
        {
            new ColumnDefinition { Name = "Id", DataType = typeof(int), IsNullable = false },
            new ColumnDefinition { Name = "Name", DataType = typeof(string), IsNullable = false }
        };

        // Act
        var schema = new Schema(columns);

        // Assert
        Assert.NotNull(schema);
        Assert.Equal(2, schema.ColumnCount);
        Assert.NotEmpty(schema.Signature);
    }

    [Fact]
    public void Constructor_WithEmptyColumns_ThrowsException()
    {
        // Arrange
        var columns = Array.Empty<ColumnDefinition>();

        // Act & Assert
        Assert.Throws<ArgumentException>(() => new Schema(columns));
    }

    [Fact]
    public void Constructor_WithDuplicateColumns_ThrowsException()
    {
        // Arrange
        var columns = new[]
        {
            new ColumnDefinition { Name = "Id", DataType = typeof(int), IsNullable = false },
            new ColumnDefinition { Name = "id", DataType = typeof(string), IsNullable = false } // Case insensitive duplicate
        };

        // Act & Assert
        Assert.Throws<ArgumentException>(() => new Schema(columns));
    }

    [Fact]
    public void GetIndex_WithValidColumn_ReturnsCorrectIndex()
    {
        // Arrange
        var columns = new[]
        {
            new ColumnDefinition { Name = "Id", DataType = typeof(int), IsNullable = false },
            new ColumnDefinition { Name = "Name", DataType = typeof(string), IsNullable = false },
            new ColumnDefinition { Name = "Email", DataType = typeof(string), IsNullable = true }
        };
        var schema = new Schema(columns);

        // Act & Assert
        Assert.Equal(0, schema.GetIndex("Id"));
        Assert.Equal(1, schema.GetIndex("Name"));
        Assert.Equal(2, schema.GetIndex("Email"));
    }

    [Fact]
    public void GetIndex_WithInvalidColumn_ReturnsMinusOne()
    {
        // Arrange
        var columns = new[]
        {
            new ColumnDefinition { Name = "Id", DataType = typeof(int), IsNullable = false }
        };
        var schema = new Schema(columns);

        // Act
        var index = schema.GetIndex("NonExistentColumn");

        // Assert
        Assert.Equal(-1, index);
    }

    [Fact]
    public void GetIndex_IsCaseInsensitive()
    {
        // Arrange
        var columns = new[]
        {
            new ColumnDefinition { Name = "Id", DataType = typeof(int), IsNullable = false },
            new ColumnDefinition { Name = "Name", DataType = typeof(string), IsNullable = false }
        };
        var schema = new Schema(columns);

        // Act & Assert
        Assert.Equal(0, schema.GetIndex("ID"));
        Assert.Equal(0, schema.GetIndex("id"));
        Assert.Equal(1, schema.GetIndex("NAME"));
        Assert.Equal(1, schema.GetIndex("name"));
    }

    [Fact]
    public void TryGetIndex_WithValidColumn_ReturnsTrueAndIndex()
    {
        // Arrange
        var columns = new[]
        {
            new ColumnDefinition { Name = "Id", DataType = typeof(int), IsNullable = false },
            new ColumnDefinition { Name = "Name", DataType = typeof(string), IsNullable = false }
        };
        var schema = new Schema(columns);

        // Act
        var success = schema.TryGetIndex("Name", out var index);

        // Assert
        Assert.True(success);
        Assert.Equal(1, index);
    }

    [Fact]
    public void TryGetIndex_WithInvalidColumn_ReturnsFalse()
    {
        // Arrange
        var columns = new[]
        {
            new ColumnDefinition { Name = "Id", DataType = typeof(int), IsNullable = false }
        };
        var schema = new Schema(columns);

        // Act
        var success = schema.TryGetIndex("NonExistentColumn", out var index);

        // Assert
        Assert.False(success);
        Assert.Equal(-1, index);
    }

    [Fact]
    public void GetColumn_WithValidName_ReturnsColumn()
    {
        // Arrange
        var idColumn = new ColumnDefinition { Name = "Id", DataType = typeof(int), IsNullable = false };
        var nameColumn = new ColumnDefinition { Name = "Name", DataType = typeof(string), IsNullable = false };
        var columns = new[] { idColumn, nameColumn };
        var schema = new Schema(columns);

        // Act
        var retrievedColumn = schema.GetColumn("Name");

        // Assert
        Assert.NotNull(retrievedColumn);
        Assert.Equal(nameColumn.Name, retrievedColumn.Name);
        Assert.Equal(nameColumn.DataType, retrievedColumn.DataType);
        Assert.Equal(nameColumn.IsNullable, retrievedColumn.IsNullable);
    }

    [Fact]
    public void GetColumn_WithInvalidName_ReturnsNull()
    {
        // Arrange
        var columns = new[]
        {
            new ColumnDefinition { Name = "Id", DataType = typeof(int), IsNullable = false }
        };
        var schema = new Schema(columns);

        // Act
        var column = schema.GetColumn("NonExistentColumn");

        // Assert
        Assert.Null(column);
    }

    [Fact]
    public void HasColumn_WithValidName_ReturnsTrue()
    {
        // Arrange
        var columns = new[]
        {
            new ColumnDefinition { Name = "Id", DataType = typeof(int), IsNullable = false },
            new ColumnDefinition { Name = "Name", DataType = typeof(string), IsNullable = false }
        };
        var schema = new Schema(columns);

        // Act & Assert
        Assert.True(schema.HasColumn("Id"));
        Assert.True(schema.HasColumn("Name"));
        Assert.True(schema.HasColumn("id")); // Case insensitive
    }

    [Fact]
    public void HasColumn_WithInvalidName_ReturnsFalse()
    {
        // Arrange
        var columns = new[]
        {
            new ColumnDefinition { Name = "Id", DataType = typeof(int), IsNullable = false }
        };
        var schema = new Schema(columns);

        // Act
        var hasColumn = schema.HasColumn("NonExistentColumn");

        // Assert
        Assert.False(hasColumn);
    }

    [Fact]
    public void IsCompatibleWith_WithIdenticalSchemas_ReturnsTrue()
    {
        // Arrange
        var columns = new[]
        {
            new ColumnDefinition { Name = "Id", DataType = typeof(int), IsNullable = false },
            new ColumnDefinition { Name = "Name", DataType = typeof(string), IsNullable = false }
        };
        var schema1 = new Schema(columns);
        var schema2 = new Schema(columns);

        // Act
        var isCompatible = schema1.IsCompatibleWith(schema2);

        // Assert
        Assert.True(isCompatible);
    }

    [Fact]
    public void IsCompatibleWith_WithCompatibleTypes_ReturnsTrue()
    {
        // Arrange
        var sourceColumns = new[]
        {
            new ColumnDefinition { Name = "Id", DataType = typeof(int), IsNullable = false },
            new ColumnDefinition { Name = "Score", DataType = typeof(float), IsNullable = true }
        };
        var targetColumns = new[]
        {
            new ColumnDefinition { Name = "Id", DataType = typeof(long), IsNullable = false }, // int -> long is compatible
            new ColumnDefinition { Name = "Score", DataType = typeof(double), IsNullable = true } // float -> double is compatible
        };
        var sourceSchema = new Schema(sourceColumns);
        var targetSchema = new Schema(targetColumns);

        // Act
        var isCompatible = sourceSchema.IsCompatibleWith(targetSchema);

        // Assert
        Assert.True(isCompatible);
    }

    [Fact]
    public void IsCompatibleWith_WithMissingColumn_ReturnsFalse()
    {
        // Arrange
        var sourceColumns = new[]
        {
            new ColumnDefinition { Name = "Id", DataType = typeof(int), IsNullable = false }
        };
        var targetColumns = new[]
        {
            new ColumnDefinition { Name = "Id", DataType = typeof(int), IsNullable = false },
            new ColumnDefinition { Name = "Name", DataType = typeof(string), IsNullable = false }
        };
        var sourceSchema = new Schema(sourceColumns);
        var targetSchema = new Schema(targetColumns);

        // Act
        var isCompatible = sourceSchema.IsCompatibleWith(targetSchema);

        // Assert
        Assert.False(isCompatible);
    }

    [Fact]
    public void AddColumns_WithNewColumns_ReturnsNewSchema()
    {
        // Arrange
        var originalColumns = new[]
        {
            new ColumnDefinition { Name = "Id", DataType = typeof(int), IsNullable = false }
        };
        var schema = new Schema(originalColumns);
        var additionalColumns = new[]
        {
            new ColumnDefinition { Name = "Name", DataType = typeof(string), IsNullable = false },
            new ColumnDefinition { Name = "Email", DataType = typeof(string), IsNullable = true }
        };

        // Act
        var newSchema = schema.AddColumns(additionalColumns);

        // Assert
        Assert.NotSame(schema, newSchema);
        Assert.Equal(1, schema.ColumnCount); // Original unchanged
        Assert.Equal(3, newSchema.ColumnCount); // New schema has all columns
        Assert.True(newSchema.HasColumn("Id"));
        Assert.True(newSchema.HasColumn("Name"));
        Assert.True(newSchema.HasColumn("Email"));
    }

    [Fact]
    public void RemoveColumns_WithExistingColumns_ReturnsNewSchema()
    {
        // Arrange
        var columns = new[]
        {
            new ColumnDefinition { Name = "Id", DataType = typeof(int), IsNullable = false },
            new ColumnDefinition { Name = "Name", DataType = typeof(string), IsNullable = false },
            new ColumnDefinition { Name = "Email", DataType = typeof(string), IsNullable = true }
        };
        var schema = new Schema(columns);

        // Act
        var newSchema = schema.RemoveColumns("Email");

        // Assert
        Assert.NotSame(schema, newSchema);
        Assert.Equal(3, schema.ColumnCount); // Original unchanged
        Assert.Equal(2, newSchema.ColumnCount); // New schema has column removed
        Assert.True(newSchema.HasColumn("Id"));
        Assert.True(newSchema.HasColumn("Name"));
        Assert.False(newSchema.HasColumn("Email"));
    }

    [Fact]
    public void Equals_WithIdenticalSchemas_ReturnsTrue()
    {
        // Arrange
        var columns = new[]
        {
            new ColumnDefinition { Name = "Id", DataType = typeof(int), IsNullable = false },
            new ColumnDefinition { Name = "Name", DataType = typeof(string), IsNullable = false }
        };
        var schema1 = new Schema(columns);
        var schema2 = new Schema(columns);

        // Act & Assert
        Assert.True(schema1.Equals(schema2));
        Assert.True(schema2.Equals(schema1));
        Assert.Equal(schema1.GetHashCode(), schema2.GetHashCode());
    }

    [Fact]
    public void Equals_WithDifferentSchemas_ReturnsFalse()
    {
        // Arrange
        var columns1 = new[]
        {
            new ColumnDefinition { Name = "Id", DataType = typeof(int), IsNullable = false }
        };
        var columns2 = new[]
        {
            new ColumnDefinition { Name = "Name", DataType = typeof(string), IsNullable = false }
        };
        var schema1 = new Schema(columns1);
        var schema2 = new Schema(columns2);

        // Act & Assert
        Assert.False(schema1.Equals(schema2));
        Assert.False(schema2.Equals(schema1));
    }

    [Fact]
    public void GetOrCreate_WithSameColumns_ReturnsSameInstance()
    {
        // Arrange
        var columns = new[]
        {
            new ColumnDefinition { Name = "Id", DataType = typeof(int), IsNullable = false },
            new ColumnDefinition { Name = "Name", DataType = typeof(string), IsNullable = false }
        };

        // Act
        var schema1 = Schema.GetOrCreate(columns);
        var schema2 = Schema.GetOrCreate(columns);

        // Assert
        Assert.Same(schema1, schema2); // Should be same instance due to caching
    }

    [Fact]
    public void Signature_IsConsistentAcrossIdenticalSchemas()
    {
        // Arrange
        var columns = new[]
        {
            new ColumnDefinition { Name = "Id", DataType = typeof(int), IsNullable = false },
            new ColumnDefinition { Name = "Name", DataType = typeof(string), IsNullable = false }
        };
        var schema1 = new Schema(columns);
        var schema2 = new Schema(columns);

        // Act & Assert
        Assert.Equal(schema1.Signature, schema2.Signature);
        Assert.NotEmpty(schema1.Signature);
    }
}