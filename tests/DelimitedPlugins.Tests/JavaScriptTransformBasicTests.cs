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
/// Basic unit tests for JavaScript Transform plugin functionality.
/// Tests core plugin initialization, configuration, and simple transformations.
/// </summary>
public class JavaScriptTransformBasicTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly ServiceProvider _serviceProvider;

    public JavaScriptTransformBasicTests(ITestOutputHelper output)
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
    public async Task JavaScriptTransformPlugin_Initialization_ShouldSucceed()
    {
        // Arrange
        var transformConfig = new JavaScriptTransformConfiguration
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

        var logger = _serviceProvider.GetRequiredService<ILogger<JavaScriptTransformPlugin>>();
        var scriptEngine = _serviceProvider.GetRequiredService<IScriptEngineService>();
        var contextService = _serviceProvider.GetRequiredService<IJavaScriptContextService>();

        // Act
        var plugin = new JavaScriptTransformPlugin(scriptEngine, contextService, logger);
        var result = await plugin.InitializeAsync(transformConfig);

        // Assert
        Assert.True(result.Success);
        Assert.NotNull(plugin.OutputSchema);
        Assert.Equal("JavaScript Transform", plugin.Name);
        Assert.Equal("1.0.0", plugin.Version);
        Assert.Equal("Transform", plugin.PluginType);
        Assert.False(plugin.IsStateful);
        Assert.Equal(TransformMode.OneToOne, plugin.Mode);

        _output.WriteLine("✅ JavaScript Transform plugin initialization test passed!");
        
        // Cleanup
        plugin.Dispose();
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

    [Fact]
    public async Task JavaScriptTransformPlugin_SimpleTransformation_ShouldProcessData()
    {
        // Arrange
        var transformConfig = new JavaScriptTransformConfiguration
        {
            Script = @"
function process(context) {
    var input = context.input.getValue('Name');
    return { 
        UpperName: input ? input.toUpperCase() : 'NULL',
        Timestamp: new Date().toISOString()
    };
}",
            OutputSchema = new OutputSchemaConfiguration
            {
                Fields = new List<OutputFieldDefinition>
                {
                    new() { Name = "UpperName", Type = "string", Required = true },
                    new() { Name = "Timestamp", Type = "string", Required = true }
                }
            }
        };

        var logger = _serviceProvider.GetRequiredService<ILogger<JavaScriptTransformPlugin>>();
        var scriptEngine = _serviceProvider.GetRequiredService<IScriptEngineService>();
        var contextService = _serviceProvider.GetRequiredService<IJavaScriptContextService>();

        // Create input schema and data
        var inputSchema = Schema.GetOrCreate(new[]
        {
            new ColumnDefinition { Name = "Name", DataType = typeof(string), IsNullable = false, Index = 0 }
        });

        var inputRow = new ArrayRow(inputSchema, new object?[] { "TestName" });
        var inputChunk = new Chunk(inputSchema, new[] { inputRow });

        // Act
        var plugin = new JavaScriptTransformPlugin(scriptEngine, contextService, logger);
        
        try
        {
            var initResult = await plugin.InitializeAsync(transformConfig);
            Assert.True(initResult.Success, $"Initialization failed: {initResult.Message}");
            
            await plugin.StartAsync();
            
            var validationResult = plugin.ValidateInputSchema(inputSchema);
            Assert.True(validationResult.IsValid);

            // Transform the data
            var transformedChunks = plugin.TransformAsync(
                CreateAsyncEnumerable(inputChunk), 
                CancellationToken.None);

            var outputChunks = new List<IChunk>();
            await foreach (var chunk in transformedChunks)
            {
                outputChunks.Add(chunk);
            }
            
            await plugin.StopAsync();

            // Assert
            Assert.Single(outputChunks);
            var outputChunk = outputChunks[0];
            Assert.Equal(1, outputChunk.RowCount);
            
            var outputRow = outputChunk.Rows[0];
            Assert.Equal("TESTNAME", outputRow[0]); // UpperName
            Assert.NotNull(outputRow[1]); // Timestamp
            
            _output.WriteLine($"✅ Transformation successful:");
            _output.WriteLine($"  Input: {inputRow[0]}");
            _output.WriteLine($"  Output: {outputRow[0]}, {outputRow[1]}");
        }
        finally
        {
            plugin.Dispose();
        }
    }

    [Fact]
    public async Task JavaScriptTransformPlugin_PluginProperties_ShouldReturnCorrectValues()
    {
        // Arrange
        var transformConfig = new JavaScriptTransformConfiguration
        {
            Script = @"function process(context) { return { test: 'value' }; }",
            OutputSchema = new OutputSchemaConfiguration
            {
                Fields = new List<OutputFieldDefinition>
                {
                    new() { Name = "test", Type = "string", Required = true }
                }
            }
        };

        var logger = _serviceProvider.GetRequiredService<ILogger<JavaScriptTransformPlugin>>();
        var scriptEngine = _serviceProvider.GetRequiredService<IScriptEngineService>();
        var contextService = _serviceProvider.GetRequiredService<IJavaScriptContextService>();

        // Act
        var plugin = new JavaScriptTransformPlugin(scriptEngine, contextService, logger);
        
        try
        {
            await plugin.InitializeAsync(transformConfig);
            await plugin.StartAsync();
            
            // Assert plugin properties
            Assert.Equal("JavaScript Transform", plugin.Name);
            Assert.Equal("1.0.0", plugin.Version);
            Assert.Equal("Transform", plugin.PluginType);
            Assert.Equal("FlowEngine Team", plugin.Author);
            Assert.False(plugin.SupportsHotSwapping);
            Assert.False(plugin.IsStateful);
            Assert.Equal(TransformMode.OneToOne, plugin.Mode);
            
            await plugin.StopAsync();
            
            _output.WriteLine("✅ Plugin properties test passed!");
        }
        finally
        {
            plugin.Dispose();
        }
    }

    private static async IAsyncEnumerable<IChunk> CreateAsyncEnumerable(IChunk chunk)
    {
        yield return chunk;
        await Task.CompletedTask;
    }

    public void Dispose()
    {
        _serviceProvider?.Dispose();
    }
}