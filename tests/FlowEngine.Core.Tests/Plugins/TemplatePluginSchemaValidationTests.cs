using FlowEngine.Abstractions.Data;
using FlowEngine.Core.Data;
using TemplatePlugin;
using Xunit;

namespace FlowEngine.Core.Tests.Plugins;

/// <summary>
/// Tests for Template Plugin schema validation functionality.
/// </summary>
public class TemplatePluginSchemaValidationTests
{
    private readonly TemplatePluginValidator _validator = new();

    [Fact]
    public void ValidateSchema_WithSupportedTypes_ShouldSucceed()
    {
        // Arrange
        var columns = new[]
        {
            new ColumnDefinition { Name = "Id", DataType = typeof(int), IsNullable = false, Index = 0 },
            new ColumnDefinition { Name = "Name", DataType = typeof(string), IsNullable = false, Index = 1 },
            new ColumnDefinition { Name = "Price", DataType = typeof(decimal), IsNullable = true, Index = 2 },
            new ColumnDefinition { Name = "Timestamp", DataType = typeof(DateTimeOffset), IsNullable = false, Index = 3 },
            new ColumnDefinition { Name = "IsActive", DataType = typeof(bool), IsNullable = true, Index = 4 }
        };
        var schema = Schema.GetOrCreate(columns);

        // Act
        var result = _validator.ValidateSchema(schema);

        // Assert
        Assert.True(result.IsValid, $"Schema validation should succeed. Errors: {string.Join(", ", result.Errors.Select(e => e.Message))}");
    }

    [Fact]
    public void ValidateSchema_WithUnsupportedType_ShouldFail()
    {
        // Arrange
        var columns = new[]
        {
            new ColumnDefinition { Name = "Id", DataType = typeof(int), IsNullable = false, Index = 0 },
            new ColumnDefinition { Name = "Data", DataType = typeof(byte[]), IsNullable = false, Index = 1 } // Unsupported binary type
        };
        var schema = Schema.GetOrCreate(columns);

        // Act
        var result = _validator.ValidateSchema(schema);

        // Assert
        Assert.False(result.IsValid, "Schema validation should fail for unsupported types");
        Assert.Contains(result.Errors, e => e.Code == "UNSUPPORTED_COLUMN_TYPE");
        Assert.Contains(result.Errors, e => e.Message.Contains("TemplatePlugin does not support 'Byte[]' type"));
    }

    [Fact]
    public void ValidateSchema_WithNonSequentialIndexes_ShouldFail()
    {
        // Arrange
        var columns = new[]
        {
            new ColumnDefinition { Name = "Id", DataType = typeof(int), IsNullable = false, Index = 0 },
            new ColumnDefinition { Name = "Name", DataType = typeof(string), IsNullable = false, Index = 5 } // Non-sequential index
        };
        var schema = Schema.GetOrCreate(columns);

        // Act
        var result = _validator.ValidateSchema(schema);

        // Assert
        Assert.False(result.IsValid, "Schema validation should fail for non-sequential indexes");
        Assert.Contains(result.Errors, e => e.Code == "NON_SEQUENTIAL_INDEX");
        Assert.Contains(result.Errors, e => e.Message.Contains("expected 1"));
    }

    [Fact]
    public void ValidateSchema_WithAllNullableColumns_ShouldWarn()
    {
        // Arrange
        var columns = new[]
        {
            new ColumnDefinition { Name = "Id", DataType = typeof(int), IsNullable = true, Index = 0 },
            new ColumnDefinition { Name = "Name", DataType = typeof(string), IsNullable = true, Index = 1 }
        };
        var schema = Schema.GetOrCreate(columns);

        // Act
        var result = _validator.ValidateSchema(schema);

        // Assert
        Assert.False(result.IsValid, "Schema validation should warn about all nullable columns");
        Assert.Contains(result.Errors, e => e.Code == "NO_REQUIRED_COLUMNS");
        Assert.Contains(result.Errors, e => e.Severity == FlowEngine.Abstractions.Plugins.ValidationSeverity.Warning);
    }

    [Fact]
    public void ValidateSchema_WithTooManyColumns_ShouldWarn()
    {
        // Arrange - Create 25 columns (more than 20)
        var columns = Enumerable.Range(0, 25)
            .Select(i => new ColumnDefinition 
            { 
                Name = $"Column{i}", 
                DataType = typeof(string), 
                IsNullable = false, 
                Index = i 
            })
            .ToArray();
        var schema = Schema.GetOrCreate(columns);

        // Act
        var result = _validator.ValidateSchema(schema);

        // Assert
        Assert.False(result.IsValid, "Schema validation should warn about too many columns");
        Assert.Contains(result.Errors, e => e.Code == "TOO_MANY_COLUMNS");
        Assert.Contains(result.Errors, e => e.Severity == FlowEngine.Abstractions.Plugins.ValidationSeverity.Warning);
    }

    [Fact]
    public void ValidateSchema_WithValidComplexSchema_ShouldSucceed()
    {
        // Arrange - Realistic schema for data generation
        var columns = new[]
        {
            new ColumnDefinition { Name = "Id", DataType = typeof(int), IsNullable = false, Index = 0 },
            new ColumnDefinition { Name = "Name", DataType = typeof(string), IsNullable = false, Index = 1 },
            new ColumnDefinition { Name = "Category", DataType = typeof(string), IsNullable = false, Index = 2 },
            new ColumnDefinition { Name = "Price", DataType = typeof(decimal), IsNullable = true, Index = 3 },
            new ColumnDefinition { Name = "CreatedAt", DataType = typeof(DateTime), IsNullable = false, Index = 4 },
            new ColumnDefinition { Name = "LastModified", DataType = typeof(DateTimeOffset), IsNullable = true, Index = 5 },
            new ColumnDefinition { Name = "IsActive", DataType = typeof(bool), IsNullable = false, Index = 6 }
        };
        var schema = Schema.GetOrCreate(columns);

        // Act
        var result = _validator.ValidateSchema(schema);

        // Assert
        Assert.True(result.IsValid, $"Complex schema validation should succeed. Errors: {string.Join(", ", result.Errors.Select(e => e.Message))}");
    }
}