using FlowEngine.Core;

var coordinator = new FlowEngineCoordinator();

try
{
    Console.WriteLine("Testing simple source->sink pipeline...");
    
    var yamlConfig = @"
pipeline:
  name: ""Simple Test Pipeline""
  version: ""1.0.0""
  
  plugins:
    - name: ""DataSource""
      type: ""FlowEngine.Core.Plugins.Examples.DataGeneratorPlugin""
      assembly: ""FlowEngine.Core.dll""
      config:
        rowCount: 5
        batchSize: 5
        delayMs: 50
        
    - name: ""ConsoleOutput""
      type: ""FlowEngine.Core.Plugins.Examples.ConsoleOutputPlugin""
      assembly: ""FlowEngine.Core.dll""
      config:
        showHeaders: true
        showRowNumbers: true
        maxRows: 10
        showSummary: true
  
  connections:
    - from: ""DataSource""
      to: ""ConsoleOutput""

  settings:
    defaultChannel:
      bufferSize: 100
      backpressureThreshold: 80
      fullMode: ""Wait""
      timeoutSeconds: 10
";

    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
    var result = await coordinator.ExecutePipelineFromYamlAsync(yamlConfig, cts.Token);
    
    Console.WriteLine($"Pipeline result: Success={result.IsSuccess}, Rows={result.TotalRowsProcessed}, Time={result.ExecutionTime}");
    
    if (!result.IsSuccess)
    {
        foreach (var error in result.Errors)
        {
            Console.WriteLine($"Error: {error.Message}");
        }
    }
}
catch (Exception ex)
{
    Console.WriteLine($"Exception: {ex.Message}");
    Console.WriteLine($"Stack trace: {ex.StackTrace}");
}
finally
{
    await coordinator.DisposeAsync();
}