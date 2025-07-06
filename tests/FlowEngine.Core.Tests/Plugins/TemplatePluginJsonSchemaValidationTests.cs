using FlowEngine.Abstractions.Data;
using FlowEngine.Core.Data;
using TemplatePlugin;
using Xunit;

namespace FlowEngine.Core.Tests.Plugins;

/// <summary>
/// Tests for Template Plugin JSON Schema-based validation functionality.
/// </summary>
public class TemplatePluginJsonSchemaValidationTests
{
    private readonly TemplatePluginValidator _validator = new();

    [Fact]
    public void ValidateConfiguration_WithValidConfiguration_ShouldSucceed()
    {
        // Arrange
        var outputSchema = CreateValidSchema();
        var config = new TemplatePluginConfiguration
        {
            RowCount = 1000,
            BatchSize = 100,
            DataType = "TestData",
            DelayMs = 10,
            OutputSchema = outputSchema
        };

        // Act
        var result = _validator.ValidateConfiguration(config);

        // Assert
        Assert.True(result.IsValid, $"Configuration validation should succeed. Errors: {string.Join(", ", result.Errors.Select(e => e.Message))}");
    }

    [Fact]
    public void ValidateConfiguration_WithInvalidRowCount_ShouldFail()
    {
        // Arrange
        var outputSchema = CreateValidSchema();
        var config = new TemplatePluginConfiguration
        {
            RowCount = 0, // Invalid: must be > 0
            BatchSize = 100,
            DataType = "TestData",
            DelayMs = 10,
            OutputSchema = outputSchema
        };

        // Act
        var result = _validator.ValidateConfiguration(config);

        // Assert
        Assert.False(result.IsValid, "Configuration validation should fail for invalid RowCount");
        Assert.Contains(result.Errors, e => e.Code == "SCHEMA_VALIDATION_ERROR");
    }

    [Fact]
    public void ValidateConfiguration_WithInvalidBatchSize_ShouldFail()
    {
        // Arrange
        var outputSchema = CreateValidSchema();
        var config = new TemplatePluginConfiguration
        {
            RowCount = 1000,
            BatchSize = 0, // Invalid: must be > 0
            DataType = "TestData",
            DelayMs = 10,
            OutputSchema = outputSchema
        };

        // Act
        var result = _validator.ValidateConfiguration(config);

        // Assert
        Assert.False(result.IsValid, "Configuration validation should fail for invalid BatchSize");
        Assert.Contains(result.Errors, e => e.Code == "SCHEMA_VALIDATION_ERROR");
    }

    [Fact]
    public void ValidateConfiguration_WithInvalidDataType_ShouldFail()
    {
        // Arrange
        var outputSchema = CreateValidSchema();
        var config = new TemplatePluginConfiguration
        {
            RowCount = 1000,
            BatchSize = 100,
            DataType = "InvalidType", // Invalid: not in enum
            DelayMs = 10,
            OutputSchema = outputSchema
        };

        // Act
        var result = _validator.ValidateConfiguration(config);

        // Assert
        Assert.False(result.IsValid, "Configuration validation should fail for invalid DataType");
        Assert.Contains(result.Errors, e => e.Code == "SCHEMA_VALIDATION_ERROR");
    }

    [Fact]
    public void ValidateConfiguration_WithSemanticErrors_ShouldWarn()
    {
        // Arrange
        var outputSchema = CreateValidSchema();
        var config = new TemplatePluginConfiguration
        {
            RowCount = 1000,
            BatchSize = 1500, // Warning: BatchSize > RowCount
            DataType = "TestData",
            DelayMs = 0,
            OutputSchema = outputSchema
        };

        // Act
        var result = _validator.ValidateConfiguration(config);

        // Assert
        Assert.False(result.IsValid, "Configuration validation should warn for semantic issues");
        Assert.Contains(result.Errors, e => e.Code == "BATCH_SIZE_EXCEEDS_ROW_COUNT");
    }

    [Fact]
    public void ValidateConfiguration_WithLargeDatasetAndSmallBatch_ShouldWarn()
    {
        // Arrange
        var outputSchema = CreateValidSchema();
        var config = new TemplatePluginConfiguration
        {
            RowCount = 150000, // Large dataset
            BatchSize = 50,    // Small batch
            DataType = "TestData",
            DelayMs = 0,
            OutputSchema = outputSchema
        };

        // Act
        var result = _validator.ValidateConfiguration(config);

        // Assert
        Assert.False(result.IsValid, "Configuration validation should warn for suboptimal batch size");
        Assert.Contains(result.Errors, e => e.Code == "SUBOPTIMAL_BATCH_SIZE");
    }

    [Fact]
    public void ValidateConfiguration_WithSequentialAndLargeDataset_ShouldWarn()
    {
        // Arrange
        var outputSchema = CreateValidSchema();
        var config = new TemplatePluginConfiguration
        {
            RowCount = 1500000, // Very large dataset
            BatchSize = 1000,
            DataType = "Sequential", // Sequential with large dataset
            DelayMs = 0,
            OutputSchema = outputSchema
        };

        // Act
        var result = _validator.ValidateConfiguration(config);

        // Assert
        Assert.False(result.IsValid, "Configuration validation should warn for sequential generation with large dataset");
        Assert.Contains(result.Errors, e => e.Code == "SEQUENTIAL_PERFORMANCE_WARNING");
    }

    [Fact]
    public void ValidateConfiguration_WithDelayAndLargeDataset_ShouldWarn()
    {
        // Arrange
        var outputSchema = CreateValidSchema();
        var config = new TemplatePluginConfiguration
        {
            RowCount = 15000, // Large dataset
            BatchSize = 1000,
            DataType = "TestData",
            DelayMs = 100, // Delay with large dataset
            OutputSchema = outputSchema
        };

        // Act
        var result = _validator.ValidateConfiguration(config);

        // Assert
        Assert.False(result.IsValid, "Configuration validation should warn for delay with large dataset");
        Assert.Contains(result.Errors, e => e.Code == "DELAY_WITH_LARGE_DATASET");
    }

    [Fact]
    public void ValidateSchemaCompatibility_WithValidOutputSchema_ShouldSucceed()
    {
        // Arrange
        var outputSchema = CreateValidSchema();

        // Act
        var result = _validator.ValidateSchemaCompatibility(null, outputSchema);

        // Assert
        Assert.True(result.IsCompatible, $"Schema compatibility validation should succeed. Issues: {string.Join(", ", result.Issues.Select(i => i.Description))}");
    }

    [Fact]
    public void ValidateSchemaCompatibility_WithInvalidOutputSchema_ShouldFail()
    {
        // Arrange - Create schema with unsupported type
        var columns = new[]
        {
            new ColumnDefinition { Name = "Id", DataType = typeof(int), IsNullable = false, Index = 0 },
            new ColumnDefinition { Name = "Data", DataType = typeof(byte[]), IsNullable = false, Index = 1 } // Unsupported type
        };
        var invalidSchema = Schema.GetOrCreate(columns);

        // Act
        var result = _validator.ValidateSchemaCompatibility(null, invalidSchema);

        // Assert
        Assert.False(result.IsCompatible, "Schema compatibility validation should fail for invalid schema");
        Assert.True(result.Issues.Length > 0, "Should have compatibility issues");
    }

    [Fact]
    public void ComprehensiveValidation_WithValidConfiguration_ShouldSucceed()
    {
        // Arrange
        var outputSchema = CreateValidSchema();
        var config = new TemplatePluginConfiguration
        {
            RowCount = 1000,
            BatchSize = 100,
            DataType = "TestData",
            DelayMs = 0,
            OutputSchema = outputSchema
        };

        // Act
        var result = _validator.ValidateComprehensive(config);

        // Assert
        Assert.True(result.IsValid, "Comprehensive validation should succeed for valid configuration");
        Assert.Equal(100, result.ValidationScore);
    }

    private static ISchema CreateValidSchema()
    {
        var columns = new[]
        {
            new ColumnDefinition { Name = "Id", DataType = typeof(int), IsNullable = false, Index = 0 },
            new ColumnDefinition { Name = "Name", DataType = typeof(string), IsNullable = false, Index = 1 },
            new ColumnDefinition { Name = "Category", DataType = typeof(string), IsNullable = false, Index = 2 },
            new ColumnDefinition { Name = "Price", DataType = typeof(decimal), IsNullable = true, Index = 3 },
            new ColumnDefinition { Name = "Timestamp", DataType = typeof(DateTimeOffset), IsNullable = false, Index = 4 }
        };
        return Schema.GetOrCreate(columns);
    }
}