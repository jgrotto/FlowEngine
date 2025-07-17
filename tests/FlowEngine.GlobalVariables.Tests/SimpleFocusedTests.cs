using System;
using System.Linq;
using FlowEngine.Core.Configuration;
using FlowEngine.Core.Configuration.Yaml;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FlowEngine.Tests.GlobalVariables;

/// <summary>
/// Simple focused tests to verify our global variables implementation works.
/// </summary>
public class SimpleFocusedTests
{
    [Fact]
    public void GlobalVariables_BasicFunctionality_Works()
    {
        // Arrange
        var globals = new Core.Configuration.GlobalVariables();
        
        // Act
        globals.SetVariable("TestVar", "TestValue");
        var result = globals.GetVariable<string>("TestVar");
        
        // Assert
        Assert.Equal("TestValue", result);
    }

    [Fact]
    public void GlobalVariables_VariableSubstitution_Works()
    {
        // Arrange
        var globals = new Core.Configuration.GlobalVariables();
        globals.SetVariable("Name", "World");
        
        // Act
        var result = globals.SubstituteVariables("Hello {Name}!");
        
        // Assert
        Assert.Equal("Hello World!", result);
    }

    [Fact]
    public void GlobalVariables_ReadOnlyEnforcement_Works()
    {
        // Arrange
        var globals = new Core.Configuration.GlobalVariables();
        globals.SetVariable("Test", "value");
        
        // Act
        globals.SetReadOnly();
        
        // Assert
        Assert.Throws<InvalidOperationException>(() => globals.SetVariable("New", "value"));
    }

    [Fact]
    public void GlobalVariables_BuiltInVariables_Work()
    {
        // Arrange
        var globals = new Core.Configuration.GlobalVariables();
        globals.SetReadOnly();
        
        // Act
        var runDate = globals.GetVariable<DateTime>("RunDate");
        
        // Assert
        Assert.True(runDate > DateTime.MinValue);
        Assert.True(runDate <= DateTime.Now);
    }

    [Fact]
    public void GlobalVariables_ComplexSubstitution_Works()
    {
        // Arrange
        var globals = new Core.Configuration.GlobalVariables();
        globals.SetVariable("OutputPath", "/data/output");
        globals.SetVariable("FileBase", "customers");
        globals.SetVariable("Date", new DateTime(2025, 1, 15));
        
        // Act
        var result = globals.SubstituteVariables("{OutputPath}/processed_{FileBase}_{Date:yyyy-MM-dd}.csv");
        
        // Assert
        Assert.Equal("/data/output/processed_customers_2025-01-15.csv", result);
    }

    [Fact]
    public void PipelineConfiguration_LoadFromYaml_WithVariables_Works()
    {
        // Arrange
        var yamlContent = @"
variables:
  TestVar: ""Hello World""

pipeline:
  name: ""Test {TestVar} Pipeline""
  version: ""1.0.0""
  plugins:
    - name: ""test-plugin""
      type: ""TestType""
      config:
        TestValue: ""{TestVar}""
";
        
        var logger = NullLogger<YamlConfigurationParser>.Instance;
        
        // Act
        var result = PipelineConfiguration.LoadFromYaml(yamlContent, logger);
        
        // Assert
        Assert.Equal("Test Hello World Pipeline", result.Name);
        
        var plugin = result.Plugins.FirstOrDefault();
        Assert.NotNull(plugin);
        Assert.Equal("Hello World", plugin.Config["TestValue"]);
    }

    [Fact]
    public void VariableSubstitutionEngine_BasicSubstitution_Works()
    {
        // Arrange
        var globals = new Core.Configuration.GlobalVariables();
        globals.SetVariable("Name", "Test");
        
        // Act
        var result = Core.Configuration.VariableSubstitutionEngine.Substitute("Hello {Name}!", globals);
        
        // Assert
        Assert.Equal("Hello Test!", result);
    }

    [Fact]
    public void VariableSubstitutionEngine_WithFormat_Works()
    {
        // Arrange
        var globals = new Core.Configuration.GlobalVariables();
        globals.SetVariable("Date", new DateTime(2025, 1, 15));
        
        // Act
        var result = Core.Configuration.VariableSubstitutionEngine.Substitute("Date: {Date:yyyy-MM-dd}", globals);
        
        // Assert
        Assert.Equal("Date: 2025-01-15", result);
    }
}