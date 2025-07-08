using FlowEngine.Abstractions.Data;
using FlowEngine.Abstractions.Plugins;
using FlowEngine.Core;
using FlowEngine.Core.Data;
using FlowEngine.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Collections.Immutable;
using System.Text;
using Xunit;
using Xunit.Abstractions;
using JavaScriptTransform;

namespace DelimitedPlugins.Tests;

/// <summary>
/// Tests for JavaScript Transform plugin structure and configuration without V8 runtime dependencies.
/// These tests validate the plugin architecture without requiring native V8 binaries.
/// </summary>
public class JavaScriptTransformStructureTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly ServiceProvider _serviceProvider;

    public JavaScriptTransformStructureTests(ITestOutputHelper output)
    {
        _output = output;
        
        // Set up dependency injection
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Information));
        services.AddFlowEngineCore();
        services.AddSingleton<IScriptEngineService, JintScriptEngineService>();
        services.AddSingleton<IJavaScriptContextService, JavaScriptContextService>();
        
        _serviceProvider = services.BuildServiceProvider();
    }

    [Fact]
    public void JavaScriptTransformPlugin_Construction_ShouldSucceed()
    {
        // Arrange
        var logger = _serviceProvider.GetRequiredService<ILogger<JavaScriptTransformPlugin>>();
        var scriptEngine = _serviceProvider.GetRequiredService<IScriptEngineService>();
        var contextService = _serviceProvider.GetRequiredService<IJavaScriptContextService>();

        // Act
        var plugin = new JavaScriptTransformPlugin(scriptEngine, contextService, logger);

        // Assert
        Assert.NotNull(plugin);
        Assert.Equal("JavaScript Transform", plugin.Name);
        Assert.Equal("1.0.0", plugin.Version);
        Assert.Equal("Transform", plugin.PluginType);
        Assert.Equal("FlowEngine Team", plugin.Author);
        Assert.False(plugin.SupportsHotSwapping);
        Assert.False(plugin.IsStateful);
        Assert.Equal(TransformMode.OneToOne, plugin.Mode);

        _output.WriteLine("✅ JavaScript Transform plugin construction test passed!");
        
        // Cleanup
        plugin.Dispose();
    }

    [Fact]
    public void JavaScriptTransformConfiguration_Validation_ShouldWork()
    {
        // Arrange
        var validConfig = new JavaScriptTransformConfiguration
        {
            Script = @"
function process(context) {
    return { result: 'test' };
}",
            OutputSchema = new OutputSchemaConfiguration
            {
                Fields = new List<OutputFieldDefinition>
                {
                    new() { Name = "result", Type = "string", Required = true }
                }
            }
        };

        var invalidConfig = new JavaScriptTransformConfiguration
        {
            Script = "invalid script without process function",
            OutputSchema = new OutputSchemaConfiguration
            {
                Fields = new List<OutputFieldDefinition>
                {
                    new() { Name = "result", Type = "string", Required = true }
                }
            }
        };

        // Act & Assert
        Assert.True(validConfig.IsValid());
        Assert.False(invalidConfig.IsValid());
        
        var validationErrors = invalidConfig.GetValidationErrors().ToList();
        Assert.Contains("function process(context)", validationErrors.First());

        _output.WriteLine("✅ JavaScript Transform configuration validation test passed!");
    }

    [Fact]
    public void JavaScriptTransformConfiguration_Properties_ShouldImplementInterface()
    {
        // Arrange
        var config = new JavaScriptTransformConfiguration
        {
            Script = @"function process(context) { return {}; }",
            OutputSchema = new OutputSchemaConfiguration
            {
                Fields = new List<OutputFieldDefinition>
                {
                    new() { Name = "test", Type = "string", Required = true }
                }
            }
        };

        // Act & Assert - Test IPluginConfiguration interface
        Assert.Equal("JavaScript Transform", config.PluginId);
        Assert.Equal("JavaScript Transform", config.Name);
        Assert.Equal("1.0.0", config.Version);
        Assert.Equal("Transform", config.PluginType);
        Assert.False(config.SupportsHotSwapping);
        Assert.NotNull(config.InputFieldIndexes);
        Assert.NotNull(config.OutputFieldIndexes);
        Assert.NotNull(config.Properties);

        _output.WriteLine("✅ JavaScript Transform configuration interface test passed!");
    }

    [Fact]
    public void JavaScriptContextService_Creation_ShouldSucceed()
    {
        // Arrange
        var contextService = _serviceProvider.GetRequiredService<IJavaScriptContextService>();
        
        // Create test schema and row
        var schema = Schema.GetOrCreate(new[]
        {
            new ColumnDefinition { Name = "TestField", DataType = typeof(string), IsNullable = false, Index = 0 }
        });
        
        var row = new ArrayRow(schema, new object?[] { "TestValue" });

        // Act
        var context = contextService.CreateContext(row, schema, schema, new ProcessingState());

        // Assert
        Assert.NotNull(context);
        // We can't test the actual JavaScript context without V8, but we can verify the service was created
        
        _output.WriteLine("✅ JavaScript context service creation test passed!");
    }

    [Fact]
    public void OutputSchemaConfiguration_Fields_ShouldValidateCorrectly()
    {
        // Arrange
        var validOutputSchema = new OutputSchemaConfiguration
        {
            Fields = new List<OutputFieldDefinition>
            {
                new() { Name = "StringField", Type = "string", Required = true },
                new() { Name = "IntField", Type = "integer", Required = false },
                new() { Name = "DateField", Type = "datetime", Required = true }
            }
        };

        var invalidOutputSchema = new OutputSchemaConfiguration
        {
            Fields = new List<OutputFieldDefinition>()
        };

        // Act & Assert
        Assert.True(validOutputSchema.Fields.Count == 3);
        Assert.True(validOutputSchema.Fields.All(f => !string.IsNullOrEmpty(f.Name)));
        Assert.True(validOutputSchema.Fields.All(f => !string.IsNullOrEmpty(f.Type)));
        
        Assert.Empty(invalidOutputSchema.Fields);

        _output.WriteLine("✅ Output schema configuration validation test passed!");
    }

    [Fact]
    public void EngineConfiguration_Properties_ShouldHaveValidDefaults()
    {
        // Arrange & Act
        var engineConfig = new EngineConfiguration();

        // Assert
        Assert.Equal(5000, engineConfig.Timeout);
        Assert.Equal(10485760, engineConfig.MemoryLimit); // 10MB
        Assert.True(engineConfig.EnableCaching);
        Assert.False(engineConfig.EnableDebugging);

        _output.WriteLine("✅ Engine configuration defaults test passed!");
    }

    [Fact]
    public void PerformanceConfiguration_Properties_ShouldHaveValidDefaults()
    {
        // Arrange & Act
        var perfConfig = new PerformanceConfiguration();

        // Assert
        Assert.Equal(120000, perfConfig.TargetThroughput);
        Assert.True(perfConfig.EnableMonitoring);
        Assert.True(perfConfig.LogPerformanceWarnings);

        _output.WriteLine("✅ Performance configuration defaults test passed!");
    }

    [Fact]
    public async Task JavaScriptTransformPlugin_InvalidScript_ShouldFailInitialization()
    {
        // Arrange
        var transformConfig = new JavaScriptTransformConfiguration
        {
            Script = "invalid script without process function",
            OutputSchema = new OutputSchemaConfiguration
            {
                Fields = new List<OutputFieldDefinition>
                {
                    new() { Name = "result", Type = "string", Required = true }
                }
            }
        };

        var logger = _serviceProvider.GetRequiredService<ILogger<JavaScriptTransformPlugin>>();
        var scriptEngine = _serviceProvider.GetRequiredService<IScriptEngineService>();
        var contextService = _serviceProvider.GetRequiredService<IJavaScriptContextService>();

        // Act
        var plugin = new JavaScriptTransformPlugin(scriptEngine, contextService, logger);
        var result = await plugin.InitializeAsync(transformConfig);

        // Assert
        Assert.False(result.Success);
        Assert.Contains("process(context)", result.Message);

        _output.WriteLine("✅ JavaScript Transform invalid script test passed!");
        
        // Cleanup
        plugin.Dispose();
    }

    public void Dispose()
    {
        _serviceProvider?.Dispose();
    }
}