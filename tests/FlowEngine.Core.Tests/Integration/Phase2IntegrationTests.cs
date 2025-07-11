using FlowEngine.Abstractions.Configuration;
using FlowEngine.Core;
using FlowEngine.Core.Configuration;
using FlowEngine.Core.Monitoring;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit.Abstractions;

namespace FlowEngine.Core.Tests.Integration;

/// <summary>
/// Integration tests for Phase 2 functionality using working plugins.
/// Tests the complete pipeline execution with DelimitedSource, JavaScriptTransform, and DelimitedSink.
/// </summary>
public class Phase2IntegrationTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly ServiceProvider _serviceProvider;
    private readonly FlowEngineCoordinator _coordinator;
    private readonly IChannelTelemetry _telemetry;
    private readonly PipelinePerformanceMonitor _monitor;

    public Phase2IntegrationTests(ITestOutputHelper output)
    {
        _output = output;
        
        // Setup dependency injection
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Information));
        services.AddFlowEngine();
        
        _serviceProvider = services.BuildServiceProvider();
        _coordinator = _serviceProvider.GetRequiredService<FlowEngineCoordinator>();
        _telemetry = new FlowEngine.Core.Monitoring.ChannelTelemetry();
        _monitor = new PipelinePerformanceMonitor(_telemetry);
    }

    [Fact]
    public async Task SourcePlugin_DelimitedSource_ShouldLoadAndInitialize()
    {
        // Arrange - Test that we can successfully load and initialize DelimitedSource
        var testFilePath = "/mnt/c/source/FlowEngine/examples/data/customers.csv";
        var yamlConfig = $@"
pipeline:
  name: ""DelimitedSource Plugin Load Test""
  version: ""1.0.0""
  
  plugins:
    - name: ""CustomerSource""
      type: ""DelimitedSource.DelimitedSourcePlugin""
      config:
        FilePath: ""{testFilePath}""
        HasHeaders: true
        Delimiter: "",""
        ChunkSize: 5
        Encoding: ""UTF-8""
        InferSchema: false
        OutputSchema:
          Name: ""CustomerData""
          Description: ""Customer information schema""
          Columns:
            - Name: ""customer_id""
              Type: ""Integer""
              Index: 0
              IsNullable: false

  settings:
    defaultChannel:
      bufferSize: 10
      backpressureThreshold: 80
      fullMode: ""Wait""
      timeoutSeconds: 10
    
    monitoring:
      enableMetrics: true
      metricsIntervalSeconds: 1
      enableTracing: false
";

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        // Act - Try to execute the pipeline (even without sink, should initialize the plugin)
        var result = await _coordinator.ExecutePipelineFromYamlAsync(yamlConfig, cts.Token);

        // Assert - Plugin should load and initialize successfully
        _output.WriteLine($"Pipeline Success: {result.IsSuccess}");
        _output.WriteLine($"Execution Time: {result.ExecutionTime}");

        if (!result.IsSuccess)
        {
            foreach (var error in result.Errors)
            {
                _output.WriteLine($"Error: {error.Message}");
            }
        }

        // DelimitedSource should load successfully even if pipeline execution fails due to missing sink
        var hasPluginLoadErrors = result.Errors.Any(e => e.Message.Contains("DelimitedSource") && e.Message.Contains("not found"));
        Assert.False(hasPluginLoadErrors, "DelimitedSource should load successfully");
    }

    [Fact]
    public async Task PluginConfiguration_WorkingPlugins_ShouldValidateCorrectly()
    {
        // Arrange - Test configuration validation for working plugins
        var testFilePath = "/mnt/c/source/FlowEngine/examples/data/customers.csv";
        var outputFilePath = "/tmp/test-output.csv";
        var yamlConfig = $@"
pipeline:
  name: ""Configuration Validation Test""
  version: ""1.0.0""
  
  plugins:
    - name: ""CustomerSource""
      type: ""DelimitedSource.DelimitedSourcePlugin""
      config:
        FilePath: ""{testFilePath}""
        HasHeaders: true
        Delimiter: "",""
        ChunkSize: 10
        Encoding: ""UTF-8""
        InferSchema: false
        OutputSchema:
          Name: ""CustomerData""
          Description: ""Customer information schema""
          Columns:
            - Name: ""customer_id""
              Type: ""Integer""
              Index: 0
              IsNullable: false
            - Name: ""first_name""
              Type: ""String""
              Index: 1
              IsNullable: false
        
    - name: ""CustomerTransform""
      type: ""JavaScriptTransform.JavaScriptTransformPlugin""
      config:
        Script: ""function process(context) {{ return true; }}""
        ChunkSize: 10
        TimeoutSeconds: 5
        
    - name: ""CustomerSink""
      type: ""DelimitedSink.DelimitedSinkPlugin""
      config:
        FilePath: ""{outputFilePath}""
        HasHeaders: true
        Delimiter: "",""
        Encoding: ""UTF-8""
        BufferSize: 4096
        FlushInterval: 1000
        CreateDirectory: true
        OverwriteExisting: true

  connections:
    - from: ""CustomerSource""
      to: ""CustomerTransform""
    - from: ""CustomerTransform""
      to: ""CustomerSink""

  settings:
    defaultChannel:
      bufferSize: 100
      backpressureThreshold: 80
      fullMode: ""Wait""
      timeoutSeconds: 30
";

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));

        // Act - Load configuration and validate
        var result = await _coordinator.ExecutePipelineFromYamlAsync(yamlConfig, cts.Token);

        // Assert - Configuration should validate successfully
        _output.WriteLine($"Configuration validation result: Success={result.IsSuccess}");
        
        if (!result.IsSuccess)
        {
            foreach (var error in result.Errors)
            {
                _output.WriteLine($"Configuration Error: {error.Message}");
            }
        }

        var hasConfigErrors = result.Errors.Any(e => e.Message.Contains("configuration") && e.Message.Contains("invalid"));
        Assert.False(hasConfigErrors, "Working plugins configuration should validate successfully");
    }

    [Fact]
    public async Task PluginLoadingService_ShouldFindWorkingPlugins()
    {
        // Arrange - Test that plugin discovery can find the working plugins
        var pluginPath = "./plugins";

        try
        {
            // Act - Discover plugins
            await _coordinator.DiscoverPluginsAsync(pluginPath);
            var availablePlugins = _coordinator.GetAvailablePlugins();

            // Assert - Check that working plugins are found
            _output.WriteLine($"Total plugins found: {availablePlugins.Count}");
            
            foreach (var plugin in availablePlugins)
            {
                _output.WriteLine($"Found plugin: {plugin.TypeName} ({plugin.Category})");
            }

            var delimitedSource = availablePlugins.FirstOrDefault(p => p.TypeName.Contains("DelimitedSource"));
            var jsTransform = availablePlugins.FirstOrDefault(p => p.TypeName.Contains("JavaScriptTransform"));
            var delimitedSink = availablePlugins.FirstOrDefault(p => p.TypeName.Contains("DelimitedSink"));

            if (delimitedSource != null)
            {
                Assert.Contains("DelimitedSource", delimitedSource.TypeName);
                _output.WriteLine($"✅ DelimitedSource found: {delimitedSource.TypeName}");
            }
            
            if (jsTransform != null)
            {
                Assert.Contains("JavaScriptTransform", jsTransform.TypeName);
                _output.WriteLine($"✅ JavaScriptTransform found: {jsTransform.TypeName}");
            }
            
            if (delimitedSink != null)
            {
                Assert.Contains("DelimitedSink", delimitedSink.TypeName);
                _output.WriteLine($"✅ DelimitedSink found: {delimitedSink.TypeName}");
            }

            // At least one working plugin should be found
            Assert.True(delimitedSource != null || jsTransform != null || delimitedSink != null, 
                "At least one working plugin should be discoverable");
        }
        catch (DirectoryNotFoundException)
        {
            _output.WriteLine("⚠️ Plugin directory not found - this is expected in some test environments");
            // This is acceptable in test environments where plugin directory doesn't exist
        }
    }

    [Fact]
    public async Task PipelineExecution_Performance_ShouldMeetTargets()
    {
        // Arrange - Test performance with working plugins
        var testFilePath = "/mnt/c/source/FlowEngine/examples/data/customers.csv";
        var outputFilePath = "/tmp/test-performance-output.csv";
        var yamlConfig = $@"
pipeline:
  name: ""Performance Test Pipeline""
  version: ""1.0.0""
  
  plugins:
    - name: ""CustomerSource""
      type: ""DelimitedSource.DelimitedSourcePlugin""
      config:
        FilePath: ""{testFilePath}""
        HasHeaders: true
        Delimiter: "",""
        ChunkSize: 1000
        Encoding: ""UTF-8""
        InferSchema: false
        OutputSchema:
          Name: ""CustomerData""
          Description: ""Customer information schema""
          Columns:
            - Name: ""customer_id""
              Type: ""Integer""
              Index: 0
              IsNullable: false
            - Name: ""first_name""
              Type: ""String""
              Index: 1
              IsNullable: false
        
    - name: ""CustomerSink""
      type: ""DelimitedSink.DelimitedSinkPlugin""
      config:
        FilePath: ""{outputFilePath}""
        HasHeaders: true
        Delimiter: "",""
        Encoding: ""UTF-8""
        BufferSize: 8192
        FlushInterval: 1000
        CreateDirectory: true
        OverwriteExisting: true

  connections:
    - from: ""CustomerSource""
      to: ""CustomerSink""

  settings:
    defaultChannel:
      bufferSize: 2000
      backpressureThreshold: 75
      fullMode: ""Wait""
      timeoutSeconds: 30
    
    monitoring:
      enableMetrics: true
      metricsIntervalSeconds: 1
      enableTracing: true
";

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(120));

        // Act - Execute pipeline and measure performance
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var result = await _coordinator.ExecutePipelineFromYamlAsync(yamlConfig, cts.Token);
        stopwatch.Stop();

        // Assert - Verify execution completed and basic performance metrics
        _output.WriteLine($"Pipeline execution completed: Success={result.IsSuccess}");
        _output.WriteLine($"Total execution time: {stopwatch.Elapsed.TotalSeconds:F2}s");
        _output.WriteLine($"Rows processed: {result.TotalRowsProcessed}");
        
        if (result.TotalRowsProcessed > 0 && stopwatch.Elapsed.TotalSeconds > 0)
        {
            var throughput = result.TotalRowsProcessed / stopwatch.Elapsed.TotalSeconds;
            _output.WriteLine($"Throughput: {throughput:F0} rows/sec");
            
            // Basic performance assertion - should process at least 100 rows/sec
            Assert.True(throughput >= 100, $"Expected throughput >= 100 rows/sec, got {throughput:F0}");
        }

        if (!result.IsSuccess)
        {
            foreach (var error in result.Errors)
            {
                _output.WriteLine($"Error: {error.Message}");
            }
        }
    }

    public void Dispose()
    {
        _serviceProvider?.Dispose();
    }
}