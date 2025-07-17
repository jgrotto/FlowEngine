using System;
using System.Collections.Generic;
using FlowEngine.Core.Configuration;
using Xunit;

namespace FlowEngine.Tests.GlobalVariables;

/// <summary>
/// Comprehensive tests for the GlobalVariables system.
/// </summary>
public class GlobalVariablesTests
{
    [Fact]
    public void SetVariable_WithValidInput_StoresValue()
    {
        // Arrange
        var globals = new GlobalVariables();
        
        // Act
        globals.SetVariable("TestVar", "TestValue");
        
        // Assert
        Assert.Equal("TestValue", globals.GetVariable<string>("TestVar"));
    }

    [Fact]
    public void SetVariable_WithNullName_ThrowsArgumentNullException()
    {
        // Arrange
        var globals = new GlobalVariables();
        
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => globals.SetVariable(null!, "value"));
    }

    [Fact]
    public void SetVariable_WithEmptyName_ThrowsArgumentException()
    {
        // Arrange
        var globals = new GlobalVariables();
        
        // Act & Assert
        Assert.Throws<ArgumentException>(() => globals.SetVariable("", "value"));
    }

    [Fact]
    public void SetVariable_WithInvalidName_ThrowsArgumentException()
    {
        // Arrange
        var globals = new GlobalVariables();
        
        // Act & Assert
        Assert.Throws<ArgumentException>(() => globals.SetVariable("123invalid", "value"));
    }

    [Fact]
    public void GetVariable_WithNonExistentVariable_ThrowsKeyNotFoundException()
    {
        // Arrange
        var globals = new GlobalVariables();
        
        // Act & Assert
        Assert.Throws<KeyNotFoundException>(() => globals.GetVariable<string>("NonExistent"));
    }

    [Fact]
    public void HasVariable_WithExistingVariable_ReturnsTrue()
    {
        // Arrange
        var globals = new GlobalVariables();
        globals.SetVariable("TestVar", "TestValue");
        
        // Act & Assert
        Assert.True(globals.HasVariable("TestVar"));
    }

    [Fact]
    public void HasVariable_WithNonExistentVariable_ReturnsFalse()
    {
        // Arrange
        var globals = new GlobalVariables();
        
        // Act & Assert
        Assert.False(globals.HasVariable("NonExistent"));
    }

    [Fact]
    public void SubstituteVariables_WithBasicPattern_ReplacesVariable()
    {
        // Arrange
        var globals = new GlobalVariables();
        globals.SetVariable("Name", "World");
        
        // Act
        var result = globals.SubstituteVariables("Hello {Name}!");
        
        // Assert
        Assert.Equal("Hello World!", result);
    }

    [Fact]
    public void SubstituteVariables_WithMultipleVariables_ReplacesAll()
    {
        // Arrange
        var globals = new GlobalVariables();
        globals.SetVariable("First", "Hello");
        globals.SetVariable("Second", "World");
        
        // Act
        var result = globals.SubstituteVariables("{First} {Second}!");
        
        // Assert
        Assert.Equal("Hello World!", result);
    }

    [Fact]
    public void SubstituteVariables_WithMissingVariable_ThrowsException()
    {
        // Arrange
        var globals = new GlobalVariables();
        
        // Act & Assert
        Assert.Throws<InvalidOperationException>(() => globals.SubstituteVariables("Hello {Missing}!"));
    }

    [Fact]
    public void SubstituteVariables_WithNoVariables_ReturnsOriginalString()
    {
        // Arrange
        var globals = new GlobalVariables();
        var original = "Hello World!";
        
        // Act
        var result = globals.SubstituteVariables(original);
        
        // Assert
        Assert.Equal(original, result);
    }

    [Fact]
    public void SubstituteVariables_WithNullInput_ReturnsNull()
    {
        // Arrange
        var globals = new GlobalVariables();
        
        // Act
        var result = globals.SubstituteVariables(null!);
        
        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void SubstituteVariables_WithEmptyInput_ReturnsEmpty()
    {
        // Arrange
        var globals = new GlobalVariables();
        
        // Act
        var result = globals.SubstituteVariables("");
        
        // Assert
        Assert.Equal("", result);
    }

    [Fact]
    public void SetReadOnly_AfterCalled_PreventsNewVariables()
    {
        // Arrange
        var globals = new GlobalVariables();
        globals.SetVariable("Existing", "value");
        
        // Act
        globals.SetReadOnly();
        
        // Assert
        Assert.Throws<InvalidOperationException>(() => globals.SetVariable("New", "value"));
    }

    [Fact]
    public void SetReadOnly_AfterCalled_AllowsReadingExistingVariables()
    {
        // Arrange
        var globals = new GlobalVariables();
        globals.SetVariable("Test", "value");
        
        // Act
        globals.SetReadOnly();
        
        // Assert
        Assert.Equal("value", globals.GetVariable<string>("Test"));
    }

    [Fact]
    public void IsReadOnly_InitiallyFalse_ReturnsFalse()
    {
        // Arrange
        var globals = new GlobalVariables();
        
        // Act & Assert
        Assert.False(globals.IsReadOnly);
    }

    [Fact]
    public void IsReadOnly_AfterSetReadOnly_ReturnsTrue()
    {
        // Arrange
        var globals = new GlobalVariables();
        
        // Act
        globals.SetReadOnly();
        
        // Assert
        Assert.True(globals.IsReadOnly);
    }

    [Fact]
    public void BuiltInVariables_RunDate_IsAvailable()
    {
        // Arrange
        var globals = new GlobalVariables();
        globals.SetReadOnly();
        
        // Act
        var runDate = globals.GetVariable<DateTime>("RunDate");
        
        // Assert
        Assert.True(runDate > DateTime.MinValue);
        Assert.True(runDate <= DateTime.Now);
    }

    [Fact]
    public void BuiltInVariables_ConfigFile_IsAvailable()
    {
        // Arrange
        var globals = new GlobalVariables();
        globals.SetConfigurationFilePath("/test/config.yaml");
        globals.SetReadOnly();
        
        // Act
        var configFile = globals.GetVariable<string>("ConfigFile");
        
        // Assert
        Assert.Equal("/test/config.yaml", configFile);
    }

    [Fact]
    public void SubstituteVariables_WithDateTimeFormat_FormatsCorrectly()
    {
        // Arrange
        var globals = new GlobalVariables();
        globals.SetVariable("Date", new DateTime(2025, 1, 15));
        
        // Act
        var result = globals.SubstituteVariables("Date: {Date:yyyy-MM-dd}");
        
        // Assert
        Assert.Equal("Date: 2025-01-15", result);
    }

    [Fact]
    public void SubstituteVariables_WithNumericFormat_FormatsCorrectly()
    {
        // Arrange
        var globals = new GlobalVariables();
        globals.SetVariable("Number", 1234.56);
        
        // Act
        var result = globals.SubstituteVariables("Value: {Number:F2}");
        
        // Assert
        Assert.Equal("Value: 1234.56", result);
    }

    [Fact]
    public void GetAllVariables_ReturnsAllStaticAndComputedVariables()
    {
        // Arrange
        var globals = new GlobalVariables();
        globals.SetVariable("Static", "value");
        globals.SetReadOnly();
        
        // Act
        var allVariables = globals.GetAllVariables();
        
        // Assert
        Assert.Contains("Static", allVariables.Keys);
        Assert.Contains("RunDate", allVariables.Keys);
        Assert.Contains("ConfigFile", allVariables.Keys);
        Assert.Equal("value", allVariables["Static"]);
    }

    [Fact]
    public void SetComputedVariable_WithValidFactory_StoresAndEvaluates()
    {
        // Arrange
        var globals = new GlobalVariables();
        var counter = 0;
        
        // Act
        globals.SetComputedVariable("Counter", () => ++counter);
        
        // Assert
        Assert.Equal(1, globals.GetVariable<int>("Counter"));
        Assert.Equal(2, globals.GetVariable<int>("Counter")); // Should increment each time
    }

    [Fact]
    public void FileBase_WithInputFile_ReturnsCorrectBaseName()
    {
        // Arrange
        var globals = new GlobalVariables();
        globals.SetVariable("InputFile", "/path/to/customers.csv");
        globals.SetReadOnly();
        
        // Act
        var fileBase = globals.GetVariable<string>("FileBase");
        
        // Assert
        Assert.Equal("customers", fileBase);
    }

    [Fact]
    public void ComplexVariableSubstitution_WithMultipleFormats_WorksCorrectly()
    {
        // Arrange
        var globals = new GlobalVariables();
        globals.SetVariable("OutputPath", "/data/output");
        globals.SetVariable("FileBase", "customers");
        globals.SetVariable("ProcessDate", new DateTime(2025, 1, 15));
        
        // Act
        var result = globals.SubstituteVariables("{OutputPath}/processed_{FileBase}_{ProcessDate:yyyy-MM-dd}.csv");
        
        // Assert
        Assert.Equal("/data/output/processed_customers_2025-01-15.csv", result);
    }
}