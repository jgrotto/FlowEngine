using FlowEngine.Abstractions.Execution;
using FlowEngine.Abstractions.Plugins;
using FlowEngine.Core;
using FlowEngine.Core.Configuration;
using Xunit;
using Xunit.Abstractions;

namespace FlowEngine.Integration.Tests;

/// <summary>
/// Integration tests for complete pipeline execution using example plugins.
/// </summary>
public class PipelineExecutionTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly FlowEngineCoordinator _coordinator;

    public PipelineExecutionTests(ITestOutputHelper output)
    {
        _output = output;
        _coordinator = new FlowEngineCoordinator();
    }

    [Fact]
    public async Task ExecutePipeline_WithExamplePlugins_ShouldCompleteSuccessfully()
    {
        // Arrange
        var yamlConfig = @"
pipeline:
  name: ""Test Pipeline""
  version: ""1.0.0""
  description: ""Integration test pipeline""
  
  plugins:
    - name: ""DataSource""
      type: ""FlowEngine.Core.Plugins.Examples.DataGeneratorPlugin""
      assembly: ""FlowEngine.Core.dll""
      config:
        rowCount: 10
        batchSize: 5
        delayMs: 50
      
    - name: ""Enrichment""
      type: ""FlowEngine.Core.Plugins.Examples.DataEnrichmentPlugin""
      assembly: ""FlowEngine.Core.dll""
      config:
        addSalaryBand: true
        addFullName: true
        addEmailDomain: true
        
    - name: ""ConsoleOutput""
      type: ""FlowEngine.Core.Plugins.Examples.ConsoleOutputPlugin""
      assembly: ""FlowEngine.Core.dll""
      config:
        showHeaders: true
        showRowNumbers: true
        maxRows: 10
        showSummary: false
  
  connections:
    - from: ""DataSource""
      to: ""Enrichment""
    - from: ""Enrichment""  
      to: ""ConsoleOutput""

  settings:
    defaultChannel:
      bufferSize: 100
      backpressureThreshold: 80
      fullMode: ""Wait""
      timeoutSeconds: 10
";

        // Act & Assert
        var result = await _coordinator.ExecutePipelineFromYamlAsync(yamlConfig);

        // Assert
        Assert.True(result.IsSuccess, $"Pipeline execution failed: {string.Join(", ", result.Errors.Select(e => e.Message))}");
        Assert.Equal(PipelineExecutionStatus.Completed, result.FinalStatus);
        Assert.True(result.ExecutionTime > TimeSpan.Zero);
        Assert.Equal(10, result.TotalRowsProcessed); // Should process exactly 10 rows
        Assert.True(result.TotalChunksProcessed >= 2); // Should process at least 2 chunks (5 rows each)
        Assert.Empty(result.Errors);

        _output.WriteLine($"Pipeline executed successfully:");
        _output.WriteLine($"  Execution time: {result.ExecutionTime}");
        _output.WriteLine($"  Rows processed: {result.TotalRowsProcessed}");
        _output.WriteLine($"  Chunks processed: {result.TotalChunksProcessed}");
        _output.WriteLine($"  Plugin metrics: {result.PluginMetrics.Count}");
    }

    [Fact]
    public async Task ValidatePipeline_WithValidConfiguration_ShouldReturnValid()
    {
        // Arrange
        var yamlConfig = @"
pipeline:
  name: ""Validation Test Pipeline""
  version: ""1.0.0""
  
  plugins:
    - name: ""Source""
      type: ""FlowEngine.Core.Plugins.Examples.DataGeneratorPlugin""
      assembly: ""FlowEngine.Core.dll""
      config:
        rowCount: 5
      
    - name: ""Transform""
      type: ""FlowEngine.Core.Plugins.Examples.DataEnrichmentPlugin""
      assembly: ""FlowEngine.Core.dll""
        
    - name: ""Sink""
      type: ""FlowEngine.Core.Plugins.Examples.ConsoleOutputPlugin""
      assembly: ""FlowEngine.Core.dll""
  
  connections:
    - from: ""Source""
      to: ""Transform""
    - from: ""Transform""  
      to: ""Sink""
";

        // Act
        var validation = await _coordinator.ValidatePipelineFromYamlAsync(yamlConfig);

        // Assert
        Assert.True(validation.IsValid, $"Pipeline validation failed: {string.Join(", ", validation.Errors)}");
        Assert.Equal(3, validation.ExecutionOrder.Count);
        Assert.Equal("Source", validation.ExecutionOrder[0]);
        Assert.Equal("Transform", validation.ExecutionOrder[1]);
        Assert.Equal("Sink", validation.ExecutionOrder[2]);

        _output.WriteLine($"Pipeline validation successful:");
        _output.WriteLine($"  Execution order: {string.Join(" -> ", validation.ExecutionOrder)}");
        _output.WriteLine($"  Schema flow steps: {validation.SchemaFlow.Count}");
        _output.WriteLine($"  Warnings: {validation.Warnings.Count}");
    }

    [Fact]
    public async Task ValidatePipeline_WithCycle_ShouldReturnInvalid()
    {
        // Arrange - Create a pipeline with a cycle
        var yamlConfig = @"
pipeline:
  name: ""Cyclic Pipeline""
  version: ""1.0.0""
  
  plugins:
    - name: ""Plugin1""
      type: ""FlowEngine.Core.Plugins.Examples.DataGeneratorPlugin""
      assembly: ""FlowEngine.Core.dll""
      
    - name: ""Plugin2""
      type: ""FlowEngine.Core.Plugins.Examples.DataEnrichmentPlugin""
      assembly: ""FlowEngine.Core.dll""
        
    - name: ""Plugin3""
      type: ""FlowEngine.Core.Plugins.Examples.ConsoleOutputPlugin""
      assembly: ""FlowEngine.Core.dll""
  
  connections:
    - from: ""Plugin1""
      to: ""Plugin2""
    - from: ""Plugin2""  
      to: ""Plugin3""
    - from: ""Plugin3""
      to: ""Plugin1""  # This creates a cycle
";

        // Act
        var validation = await _coordinator.ValidatePipelineFromYamlAsync(yamlConfig);

        // Assert
        Assert.False(validation.IsValid, "Pipeline with cycle should be invalid");
        Assert.Contains(validation.Errors, e => e.Contains("cycle") || e.Contains("Cycle"));

        _output.WriteLine($"Correctly detected invalid pipeline:");
        _output.WriteLine($"  Errors: {string.Join(", ", validation.Errors)}");
    }

    [Fact]
    public async Task ExecutePipeline_WithInvalidPluginType_ShouldFail()
    {
        // Arrange
        var yamlConfig = @"
pipeline:
  name: ""Invalid Plugin Pipeline""
  version: ""1.0.0""
  
  plugins:
    - name: ""NonExistentPlugin""
      type: ""NonExistent.Plugin.Type""
  
  connections: []
";

        // Act & Assert
        await Assert.ThrowsAsync<PipelineExecutionException>(async () =>
        {
            await _coordinator.ExecutePipelineFromYamlAsync(yamlConfig);
        });

        _output.WriteLine("Correctly failed with invalid plugin type");
    }

    [Fact]
    public void GetAvailablePlugins_ShouldReturnExamplePlugins()
    {
        // Act
        var plugins = _coordinator.GetAvailablePlugins();

        // Assert
        Assert.NotEmpty(plugins);
        
        var sourcePlugins = plugins.Where(p => p.Category == PluginCategory.Source).ToList();
        var transformPlugins = plugins.Where(p => p.Category == PluginCategory.Transform).ToList();
        var sinkPlugins = plugins.Where(p => p.Category == PluginCategory.Sink).ToList();

        Assert.NotEmpty(sourcePlugins);
        Assert.NotEmpty(transformPlugins);
        Assert.NotEmpty(sinkPlugins);

        _output.WriteLine($"Found {plugins.Count} available plugins:");
        _output.WriteLine($"  Source plugins: {sourcePlugins.Count}");
        _output.WriteLine($"  Transform plugins: {transformPlugins.Count}");
        _output.WriteLine($"  Sink plugins: {sinkPlugins.Count}");

        foreach (var plugin in plugins.Take(5)) // Show first 5 plugins
        {
            _output.WriteLine($"  - {plugin.FriendlyName} ({plugin.TypeName})");
        }
    }

    [Fact]
    public async Task PipelineExecution_ShouldRaiseEvents()
    {
        // Arrange
        var statusChanges = new List<PipelineStatusChangedEventArgs>();
        var completionEvents = new List<PipelineCompletedEventArgs>();

        _coordinator.StatusChanged += (sender, args) => statusChanges.Add(args);
        _coordinator.PipelineCompleted += (sender, args) => completionEvents.Add(args);

        var yamlConfig = @"
pipeline:
  name: ""Event Test Pipeline""
  version: ""1.0.0""
  
  plugins:
    - name: ""Source""
      type: ""FlowEngine.Core.Plugins.Examples.DataGeneratorPlugin""
      assembly: ""FlowEngine.Core.dll""
      config:
        rowCount: 5
        batchSize: 5
      
    - name: ""Sink""
      type: ""FlowEngine.Core.Plugins.Examples.ConsoleOutputPlugin""
      assembly: ""FlowEngine.Core.dll""
      config:
        showSummary: false
  
  connections:
    - from: ""Source""
      to: ""Sink""
";

        // Act
        var result = await _coordinator.ExecutePipelineFromYamlAsync(yamlConfig);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.NotEmpty(statusChanges);
        Assert.Single(completionEvents);

        var finalStatus = statusChanges.LastOrDefault();
        Assert.NotNull(finalStatus);
        Assert.Equal(PipelineExecutionStatus.Completed, finalStatus.NewStatus);

        _output.WriteLine($"Pipeline raised {statusChanges.Count} status change events:");
        foreach (var change in statusChanges)
        {
            _output.WriteLine($"  {change.OldStatus} -> {change.NewStatus}");
        }
    }

    public void Dispose()
    {
        _coordinator?.DisposeAsync().AsTask().Wait(TimeSpan.FromSeconds(5));
    }
}