using FlowEngine.Core;
using Xunit;
using Xunit.Abstractions;

namespace FlowEngine.Integration.Tests;

public class TransformDebugTest : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly FlowEngineCoordinator _coordinator;

    public TransformDebugTest(ITestOutputHelper output)
    {
        _output = output;
        _coordinator = new FlowEngineCoordinator();
    }

    [Fact]
    public async Task SourceTransformSink_ShouldWork()
    {
        var yamlConfig = @"
pipeline:
  name: ""Transform Debug Pipeline""
  version: ""1.0.0""
  
  plugins:
    - name: ""Source""
      type: ""FlowEngine.Core.Plugins.Examples.DataGeneratorPlugin""
      assembly: ""FlowEngine.Core.dll""
      config:
        rowCount: 2
        batchSize: 2
        delayMs: 10
        
    - name: ""Transform""
      type: ""FlowEngine.Core.Plugins.Examples.DataEnrichmentPlugin""
      assembly: ""FlowEngine.Core.dll""
      config:
        addSalaryBand: true
        addFullName: false
        addEmailDomain: false
        addAgeCategory: false
        
    - name: ""Sink""
      type: ""FlowEngine.Core.Plugins.Examples.ConsoleOutputPlugin""
      assembly: ""FlowEngine.Core.dll""
      config:
        showHeaders: false
        showRowNumbers: false
        maxRows: 5
        showSummary: false
  
  connections:
    - from: ""Source""
      to: ""Transform""
    - from: ""Transform""
      to: ""Sink""

  settings:
    defaultChannel:
      bufferSize: 10
      backpressureThreshold: 80
      fullMode: ""Wait""
      timeoutSeconds: 5
";

        _output.WriteLine("Starting source->transform->sink test...");
        
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        
        try
        {
            var result = await _coordinator.ExecutePipelineFromYamlAsync(yamlConfig, cts.Token);
            
            _output.WriteLine($"Result: Success={result.IsSuccess}");
            _output.WriteLine($"Rows: {result.TotalRowsProcessed}");
            _output.WriteLine($"Chunks: {result.TotalChunksProcessed}");
            _output.WriteLine($"Time: {result.ExecutionTime}");
            
            if (!result.IsSuccess)
            {
                foreach (var error in result.Errors)
                {
                    _output.WriteLine($"Error: {error.Message}");
                }
            }
            
            Assert.True(result.IsSuccess, "Pipeline should succeed");
            Assert.True(result.TotalRowsProcessed > 0, "Should process some rows");
        }
        catch (OperationCanceledException)
        {
            _output.WriteLine("Pipeline timed out - transform plugin issue");
            throw new Exception("Pipeline execution timed out");
        }
    }

    public void Dispose()
    {
        _coordinator?.DisposeAsync().AsTask().Wait(TimeSpan.FromSeconds(2));
    }
}