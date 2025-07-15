using FlowEngine.Abstractions.Data;
using FlowEngine.Core.Data;
using NSubstitute;
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using ValidationResult = FlowEngine.Abstractions.ValidationResult;

namespace FlowEngine.Core.Tests.Data;

/// <summary>
/// Unit tests for FlexibleSchemaRequirement.
/// Tests schema validation logic for flexible plugin requirements.
/// </summary>
public class FlexibleSchemaRequirementTests
{
    [Fact]
    public void Constructor_ValidRequirements_InitializesCorrectly()
    {
        // Arrange
        var requirements = new[]
        {
            new FieldRequirement("Field1", typeof(string), true),
            new FieldRequirement("Field2", typeof(int), false)
        };

        // Act
        var schemaRequirement = new FlexibleSchemaRequirement(requirements, "Test requirement");

        // Assert
        Assert.Equal(2, schemaRequirement.RequiredFields.Count());
        Assert.False(schemaRequirement.AcceptsAnySchema);
        Assert.Equal("Test requirement", schemaRequirement.RequirementDescription);
    }

    [Fact]
    public void ValidateSchema_NullSchema_ReturnsFailure()
    {
        // Arrange
        var requirements = new[] { new FieldRequirement("Field1", typeof(string), true) };
        var schemaRequirement = new FlexibleSchemaRequirement(requirements);

        // Act
        var result = schemaRequirement.ValidateSchema(null);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains("Schema cannot be null", result.Errors[0]);
    }

    [Fact]
    public void ValidateSchema_AllRequiredFieldsPresent_ReturnsSuccess()
    {
        // Arrange
        var requirements = new[]
        {
            new FieldRequirement("Name", typeof(string), true),
            new FieldRequirement("Age", typeof(int), true)
        };
        var schemaRequirement = new FlexibleSchemaRequirement(requirements);
        var schema = CreateMockSchema(
            ("Name", typeof(string)),
            ("Age", typeof(int)),
            ("Extra", typeof(bool)) // Additional field should be allowed
        );

        // Act
        var result = schemaRequirement.ValidateSchema(schema);

        // Assert
        Assert.True(result.IsValid);
    }

    [Fact]
    public void ValidateSchema_MissingRequiredField_ReturnsFailure()
    {
        // Arrange
        var requirements = new[]
        {
            new FieldRequirement("Name", typeof(string), true),
            new FieldRequirement("Age", typeof(int), true)
        };
        var schemaRequirement = new FlexibleSchemaRequirement(requirements);
        var schema = CreateMockSchema(
            ("Name", typeof(string))
            // Missing "Age" field
        );

        // Act
        var result = schemaRequirement.ValidateSchema(schema);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains("Required field 'Age' is missing", result.Errors[0]);
    }

    [Fact]
    public void ForFields_CreatesFlexibleRequirement()
    {
        // Arrange
        var fieldNames = new[] { "Field1", "Field2" };

        // Act
        var requirement = FlexibleSchemaRequirement.ForFields(fieldNames, "Test description");

        // Assert
        Assert.Equal(2, requirement.RequiredFields.Count());
        Assert.Equal("Test description", requirement.RequirementDescription);
        Assert.All(requirement.RequiredFields, f => Assert.Equal(typeof(object), f.ExpectedType));
    }

    [Fact]
    public void ForTypedFields_CreatesTypedRequirement()
    {
        // Arrange
        var typedFields = new Dictionary<string, Type>
        {
            ["Name"] = typeof(string),
            ["Age"] = typeof(int)
        };

        // Act
        var requirement = FlexibleSchemaRequirement.ForTypedFields(typedFields);

        // Assert
        var requirements = requirement.RequiredFields.ToList();
        Assert.Equal(2, requirements.Count);
        
        var nameReq = requirements.First(r => r.FieldName == "Name");
        Assert.Equal(typeof(string), nameReq.ExpectedType);
        
        var ageReq = requirements.First(r => r.FieldName == "Age");
        Assert.Equal(typeof(int), ageReq.ExpectedType);
    }

    [Fact]
    public void RequirementDescription_GeneratesCorrectDescription()
    {
        // Arrange & Act
        var singleField = FlexibleSchemaRequirement.ForFields(new[] { "Field1" });
        var multipleFields = FlexibleSchemaRequirement.ForFields(new[] { "Field1", "Field2", "Field3" });
        var manyFields = FlexibleSchemaRequirement.ForFields(new[] { "F1", "F2", "F3", "F4", "F5" });

        // Assert
        Assert.Equal("Requires field: Field1", singleField.RequirementDescription);
        Assert.Equal("Requires fields: Field1, Field2, Field3", multipleFields.RequirementDescription);
        Assert.Contains("Requires 5 fields: F1, F2, and 3 more", manyFields.RequirementDescription);
    }

    private ISchema CreateMockSchema(params (string name, Type type)[] columns)
    {
        var columnDefinitions = columns.Select((col, index) => new ColumnDefinition
        {
            Name = col.name,
            DataType = col.type,
            Index = index,
            IsNullable = false
        }).ToArray();

        var mock = Substitute.For<ISchema>();
        mock.Columns.Returns(columnDefinitions);
        return mock;
    }
}