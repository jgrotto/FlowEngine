using System;
using System.Collections.Generic;
using FlowEngine.Core.Configuration;
using Xunit;

namespace FlowEngine.Tests.GlobalVariables;

/// <summary>
/// Tests for the VariableSubstitutionEngine static class.
/// </summary>
public class VariableSubstitutionEngineTests
{
    [Fact]
    public void Substitute_WithBasicVariable_ReplacesCorrectly()
    {
        // Arrange
        var variables = new GlobalVariables();
        variables.SetVariable("Name", "World");
        
        // Act
        var result = VariableSubstitutionEngine.Substitute("Hello {Name}!", variables);
        
        // Assert
        Assert.Equal("Hello World!", result);
    }

    [Fact]
    public void Substitute_WithMultipleVariables_ReplacesAll()
    {
        // Arrange
        var variables = new GlobalVariables();
        variables.SetVariable("Greeting", "Hello");
        variables.SetVariable("Name", "World");
        
        // Act
        var result = VariableSubstitutionEngine.Substitute("{Greeting} {Name}!", variables);
        
        // Assert
        Assert.Equal("Hello World!", result);
    }

    [Fact]
    public void Substitute_WithFormatString_FormatsCorrectly()
    {
        // Arrange
        var variables = new GlobalVariables();
        variables.SetVariable("Date", new DateTime(2025, 1, 15));
        
        // Act
        var result = VariableSubstitutionEngine.Substitute("Date: {Date:yyyy-MM-dd}", variables);
        
        // Assert
        Assert.Equal("Date: 2025-01-15", result);
    }

    [Fact]
    public void Substitute_WithNumericFormat_FormatsCorrectly()
    {
        // Arrange
        var variables = new GlobalVariables();
        variables.SetVariable("Value", 1234.567);
        
        // Act
        var result = VariableSubstitutionEngine.Substitute("Value: {Value:F2}", variables);
        
        // Assert
        Assert.Equal("Value: 1234.57", result);
    }

    [Fact]
    public void Substitute_WithIntegerFormat_FormatsCorrectly()
    {
        // Arrange
        var variables = new GlobalVariables();
        variables.SetVariable("Count", 42);
        
        // Act
        var result = VariableSubstitutionEngine.Substitute("Count: {Count:D3}", variables);
        
        // Assert
        Assert.Equal("Count: 042", result);
    }

    [Fact]
    public void Substitute_WithMissingVariable_ThrowsException()
    {
        // Arrange
        var variables = new GlobalVariables();
        
        // Act & Assert
        var exception = Assert.Throws<InvalidOperationException>(() => 
            VariableSubstitutionEngine.Substitute("Hello {Missing}!", variables));
        
        Assert.Contains("Variable substitution failed", exception.Message);
    }

    [Fact]
    public void Substitute_WithNullInput_ReturnsNull()
    {
        // Arrange
        var variables = new GlobalVariables();
        
        // Act
        var result = VariableSubstitutionEngine.Substitute(null!, variables);
        
        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void Substitute_WithEmptyInput_ReturnsEmpty()
    {
        // Arrange
        var variables = new GlobalVariables();
        
        // Act
        var result = VariableSubstitutionEngine.Substitute("", variables);
        
        // Assert
        Assert.Equal("", result);
    }

    [Fact]
    public void Substitute_WithNoVariables_ReturnsOriginal()
    {
        // Arrange
        var variables = new GlobalVariables();
        var original = "Hello World!";
        
        // Act
        var result = VariableSubstitutionEngine.Substitute(original, variables);
        
        // Assert
        Assert.Equal(original, result);
    }

    [Fact]
    public void Substitute_WithComplexPattern_ReplacesCorrectly()
    {
        // Arrange
        var variables = new GlobalVariables();
        variables.SetVariable("OutputPath", "/data/output");
        variables.SetVariable("FileBase", "customers");
        variables.SetVariable("Date", new DateTime(2025, 1, 15));
        
        // Act
        var result = VariableSubstitutionEngine.Substitute(
            "{OutputPath}/processed_{FileBase}_{Date:yyyy-MM-dd}.csv", 
            variables);
        
        // Assert
        Assert.Equal("/data/output/processed_customers_2025-01-15.csv", result);
    }

    [Fact]
    public void Substitute_WithVariableAtDifferentPositions_ReplacesAll()
    {
        // Arrange
        var variables = new GlobalVariables();
        variables.SetVariable("Name", "Test");
        
        // Act
        var result = VariableSubstitutionEngine.Substitute(
            "{Name} at start, {Name} in middle, and {Name} at end", 
            variables);
        
        // Assert
        Assert.Equal("Test at start, Test in middle, and Test at end", result);
    }

    [Fact]
    public void Substitute_WithNestedBraces_HandlesCorrectly()
    {
        // Arrange
        var variables = new GlobalVariables();
        variables.SetVariable("Config", "{ \"key\": \"value\" }");
        
        // Act
        var result = VariableSubstitutionEngine.Substitute("Config: {Config}", variables);
        
        // Assert
        Assert.Equal("Config: { \"key\": \"value\" }", result);
    }

    [Fact]
    public void Substitute_WithBooleanValue_ConvertsToString()
    {
        // Arrange
        var variables = new GlobalVariables();
        variables.SetVariable("IsEnabled", true);
        
        // Act
        var result = VariableSubstitutionEngine.Substitute("Enabled: {IsEnabled}", variables);
        
        // Assert
        Assert.Equal("Enabled: True", result);
    }

    [Fact]
    public void Substitute_WithNullValue_ReturnsEmptyString()
    {
        // Arrange
        var variables = new GlobalVariables();
        variables.SetVariable("NullValue", null);
        
        // Act
        var result = VariableSubstitutionEngine.Substitute("Value: {NullValue}", variables);
        
        // Assert
        Assert.Equal("Value: ", result);
    }

    [Fact]
    public void Substitute_WithInvalidFormatString_ThrowsException()
    {
        // Arrange
        var variables = new GlobalVariables();
        variables.SetVariable("Date", new DateTime(2025, 1, 15));
        
        // Act & Assert
        var exception = Assert.Throws<InvalidOperationException>(() => 
            VariableSubstitutionEngine.Substitute("Date: {Date:INVALID}", variables));
        
        Assert.Contains("Variable substitution failed", exception.Message);
    }

    [Fact]
    public void Substitute_WithDateTimeOffsetFormat_FormatsCorrectly()
    {
        // Arrange
        var variables = new GlobalVariables();
        variables.SetVariable("Timestamp", new DateTimeOffset(2025, 1, 15, 14, 30, 0, TimeSpan.Zero));
        
        // Act
        var result = VariableSubstitutionEngine.Substitute("Timestamp: {Timestamp:yyyy-MM-dd HH:mm:ss}", variables);
        
        // Assert
        Assert.Equal("Timestamp: 2025-01-15 14:30:00", result);
    }

    [Fact]
    public void Substitute_WithLongValue_FormatsCorrectly()
    {
        // Arrange
        var variables = new GlobalVariables();
        variables.SetVariable("BigNumber", 9876543210L);
        
        // Act
        var result = VariableSubstitutionEngine.Substitute("Big: {BigNumber:N0}", variables);
        
        // Assert
        Assert.Equal("Big: 9,876,543,210", result);
    }

    [Fact]
    public void Substitute_WithFloatValue_FormatsCorrectly()
    {
        // Arrange
        var variables = new GlobalVariables();
        variables.SetVariable("FloatValue", 3.14159f);
        
        // Act
        var result = VariableSubstitutionEngine.Substitute("Pi: {FloatValue:F3}", variables);
        
        // Assert
        Assert.Equal("Pi: 3.142", result);
    }

    [Fact]
    public void Substitute_WithDecimalValue_FormatsCorrectly()
    {
        // Arrange
        var variables = new GlobalVariables();
        variables.SetVariable("Price", 19.99m);
        
        // Act
        var result = VariableSubstitutionEngine.Substitute("Price: ${Price:F2}", variables);
        
        // Assert
        Assert.Equal("Price: $19.99", result);
    }
}