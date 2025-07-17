using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FlowEngine.Core.Configuration;
using FlowEngine.Core.Configuration.Yaml;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FlowEngine.Tests.GlobalVariables;

/// <summary>
/// Integration tests for global variables in YAML configuration parsing.
/// </summary>
public class YamlIntegrationTests
{
    [Fact]
    public void ParseYamlContent_WithVariablesSection_LoadsVariables()
    {
        // Arrange
        var yamlContent = @"
variables:
  TestVar: ""TestValue""
  NumericVar: 123

pipeline:
  name: ""Test Pipeline""
  version: ""1.0.0""
  plugins:
    - name: ""test""
      type: ""TestType""
      config:
        Value: ""{TestVar}""
";
        
        var logger = NullLogger<YamlConfigurationParser>.Instance;
        
        // Act
        var result = PipelineConfiguration.LoadFromYaml(yamlContent, logger);
        
        // Assert
        Assert.NotNull(result);
        Assert.Equal("Test Pipeline", result.Name);
        
        // Check that variable substitution occurred
        var plugin = result.Plugins.FirstOrDefault();
        Assert.NotNull(plugin);
        Assert.Equal("TestValue", plugin.Config["Value"]);
    }

    [Fact]
    public void ParseYamlContent_WithVariableSubstitutionInPipelineName_SubstitutesCorrectly()
    {
        // Arrange
        var yamlContent = @"
variables:
  Environment: ""Production""

pipeline:
  name: ""{Environment} Data Pipeline""
  version: ""1.0.0""
  plugins:
    - name: ""test""
      type: ""TestType""
      config:
        TestValue: ""static""
";
        
        var logger = NullLogger<YamlConfigurationParser>.Instance;
        
        // Act
        var result = PipelineConfiguration.LoadFromYaml(yamlContent, logger);
        
        // Assert
        Assert.Equal("Production Data Pipeline", result.Name);
    }

    [Fact]
    public void ParseYamlContent_WithMultipleVariableSubstitutions_SubstitutesAll()
    {
        // Arrange
        var yamlContent = @"
variables:
  OutputPath: ""/data/output""
  FileBase: ""customers""
  ProcessDate: ""2025-01-15""

pipeline:
  name: ""Data Processing Pipeline""
  version: ""1.0.0""
  plugins:
    - name: ""source""
      type: ""DelimitedSource""
      config:
        FilePath: ""/data/input/{FileBase}.csv""
        
    - name: ""sink""
      type: ""DelimitedSink""
      config:
        FilePath: ""{OutputPath}/processed_{FileBase}_{ProcessDate}.csv""
";
        
        var logger = NullLogger<YamlConfigurationParser>.Instance;
        var parser = new YamlConfigurationParser(logger);
        
        // Act
        var result = parser.ParseYamlContent(yamlContent, "test.yaml");
        
        // Assert
        var sourcePlugin = result.Pipeline?.Plugins?.FirstOrDefault(p => p.Name == "source");
        var sinkPlugin = result.Pipeline?.Plugins?.FirstOrDefault(p => p.Name == "sink");
        
        Assert.NotNull(sourcePlugin);
        Assert.NotNull(sinkPlugin);
        
        Assert.Equal("/data/input/customers.csv", sourcePlugin.Config["FilePath"]);
        Assert.Equal("/data/output/processed_customers_2025-01-15.csv", sinkPlugin.Config["FilePath"]);
    }

    [Fact]
    public void ParseYamlContent_WithNestedVariableSubstitution_SubstitutesCorrectly()
    {
        // Arrange
        var yamlContent = @"
variables:
  DatabaseHost: ""localhost""
  DatabasePort: 5432
  DatabaseName: ""production""

pipeline:
  name: ""Database Pipeline""
  version: ""1.0.0""
  plugins:
    - name: ""db-source""
      type: ""DatabaseSource""
      config:
        ConnectionString: ""Host={DatabaseHost};Port={DatabasePort};Database={DatabaseName}""
        Query: ""SELECT * FROM customers""
";
        
        var logger = NullLogger<YamlConfigurationParser>.Instance;
        var parser = new YamlConfigurationParser(logger);
        
        // Act
        var result = parser.ParseYamlContent(yamlContent, "test.yaml");
        
        // Assert
        var plugin = result.Pipeline?.Plugins?.FirstOrDefault();
        Assert.NotNull(plugin);
        Assert.Equal("Host=localhost;Port=5432;Database=production", plugin.Config["ConnectionString"]);
    }

    [Fact]
    public void ParseYamlContent_WithoutVariablesSection_ParsesNormally()
    {
        // Arrange
        var yamlContent = @"
pipeline:
  name: ""Simple Pipeline""
  version: ""1.0.0""
  plugins:
    - name: ""test""
      type: ""TestType""
      config:
        Value: ""static-value""
";
        
        var logger = NullLogger<YamlConfigurationParser>.Instance;
        var parser = new YamlConfigurationParser(logger);
        
        // Act
        var result = parser.ParseYamlContent(yamlContent, "test.yaml");
        
        // Assert
        Assert.NotNull(result);
        Assert.Equal("Simple Pipeline", result.Pipeline?.Name);
        
        var plugin = result.Pipeline?.Plugins?.FirstOrDefault();
        Assert.NotNull(plugin);
        Assert.Equal("static-value", plugin.Config["Value"]);
    }

    [Fact]
    public void ParseYamlContent_WithBuiltInVariables_SubstitutesCorrectly()
    {
        // Arrange
        var yamlContent = @"
variables:
  CustomVar: ""test""

pipeline:
  name: ""Pipeline with Built-ins""
  version: ""1.0.0""
  plugins:
    - name: ""test""
      type: ""TestType""
      config:
        ConfigSource: ""{ConfigFile}""
        CustomValue: ""{CustomVar}""
";
        
        var logger = NullLogger<YamlConfigurationParser>.Instance;
        var parser = new YamlConfigurationParser(logger);
        
        // Act
        var result = parser.ParseYamlContent(yamlContent, "/path/to/config.yaml");
        
        // Assert
        var plugin = result.Pipeline?.Plugins?.FirstOrDefault();
        Assert.NotNull(plugin);
        Assert.Equal("/path/to/config.yaml", plugin.Config["ConfigSource"]);
        Assert.Equal("test", plugin.Config["CustomValue"]);
    }

    [Fact]
    public void ParseYamlContent_WithInvalidVariableReference_ThrowsException()
    {
        // Arrange
        var yamlContent = @"
variables:
  ValidVar: ""valid""

pipeline:
  name: ""Test Pipeline""
  version: ""1.0.0""
  plugins:
    - name: ""test""
      type: ""TestType""
      config:
        Value: ""{InvalidVar}""
";
        
        var logger = NullLogger<YamlConfigurationParser>.Instance;
        var parser = new YamlConfigurationParser(logger);
        
        // Act & Assert
        Assert.Throws<YamlConfigurationException>(() => parser.ParseYamlContent(yamlContent, "test.yaml"));
    }

    [Fact]
    public void ParseYamlContent_WithConnectionSubstitution_SubstitutesCorrectly()
    {
        // Arrange
        var yamlContent = @"
variables:
  SourcePlugin: ""data-source""
  SinkPlugin: ""data-sink""

pipeline:
  name: ""Connection Test Pipeline""
  version: ""1.0.0""
  plugins:
    - name: ""data-source""
      type: ""DelimitedSource""
      config:
        FilePath: ""input.csv""
        
    - name: ""data-sink""
      type: ""DelimitedSink""
      config:
        FilePath: ""output.csv""
        
  connections:
    - from: ""{SourcePlugin}""
      to: ""{SinkPlugin}""
";
        
        var logger = NullLogger<YamlConfigurationParser>.Instance;
        var parser = new YamlConfigurationParser(logger);
        
        // Act
        var result = parser.ParseYamlContent(yamlContent, "test.yaml");
        
        // Assert
        var connection = result.Pipeline?.Connections?.FirstOrDefault();
        Assert.NotNull(connection);
        Assert.Equal("data-source", connection.From);
        Assert.Equal("data-sink", connection.To);
    }

    [Fact]
    public void ParseYamlContent_WithSettingsSubstitution_SubstitutesCorrectly()
    {
        // Arrange
        var yamlContent = @"
variables:
  ExecutionMode: ""Parallel""
  LogLevel: ""Debug""

pipeline:
  name: ""Settings Test Pipeline""
  version: ""1.0.0""
  plugins:
    - name: ""test""
      type: ""TestType""
      config:
        Value: ""test""
        
  settings:
    executionMode: ""{ExecutionMode}""
    monitoring:
      logLevel: ""{LogLevel}""
";
        
        var logger = NullLogger<YamlConfigurationParser>.Instance;
        var parser = new YamlConfigurationParser(logger);
        
        // Act
        var result = parser.ParseYamlContent(yamlContent, "test.yaml");
        
        // Assert
        Assert.NotNull(result.Pipeline?.Settings);
        Assert.Equal("Parallel", result.Pipeline.Settings.ExecutionMode);
        Assert.Equal("Debug", result.Pipeline.Settings.Monitoring?.LogLevel);
    }
}