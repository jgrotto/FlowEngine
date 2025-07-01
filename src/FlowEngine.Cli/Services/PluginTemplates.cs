namespace FlowEngine.Cli.Services;

/// <summary>
/// Contains template strings for generating plugin scaffolding.
/// Supports both five-component and minimal plugin architectures.
/// </summary>
internal static class PluginTemplates
{
    /// <summary>
    /// Gets the project file template for five-component plugins.
    /// </summary>
    /// <param name="pluginName">Name of the plugin</param>
    /// <returns>Project file content</returns>
    public static string GetFiveComponentProjectTemplate(string pluginName) => $@"<Project Sdk=""Microsoft.NET.Sdk"">

  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <AssemblyName>{pluginName}</AssemblyName>
    <RootNamespace>{pluginName}</RootNamespace>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include=""FlowEngine.Abstractions"" Version=""3.0.0"" />
    <PackageReference Include=""FlowEngine.Core"" Version=""3.0.0"" />
    <PackageReference Include=""Microsoft.Extensions.Logging"" Version=""8.0.1"" />
    <PackageReference Include=""Microsoft.Extensions.DependencyInjection"" Version=""8.0.1"" />
  </ItemGroup>

</Project>";

    /// <summary>
    /// Gets the project file template for minimal plugins.
    /// </summary>
    /// <param name="pluginName">Name of the plugin</param>
    /// <returns>Project file content</returns>
    public static string GetMinimalProjectTemplate(string pluginName) => $@"<Project Sdk=""Microsoft.NET.Sdk"">

  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <AssemblyName>{pluginName}</AssemblyName>
    <RootNamespace>{pluginName}</RootNamespace>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include=""FlowEngine.Abstractions"" Version=""3.0.0"" />
    <PackageReference Include=""Microsoft.Extensions.Logging"" Version=""8.0.1"" />
  </ItemGroup>

</Project>";

    /// <summary>
    /// Gets the configuration template for five-component plugins.
    /// </summary>
    /// <param name="pluginName">Name of the plugin</param>
    /// <param name="pluginType">Type of plugin (Source, Transform, Sink)</param>
    /// <returns>Configuration class content</returns>
    public static string GetConfigurationTemplate(string pluginName, string pluginType) => $@"using FlowEngine.Abstractions.Data;
using FlowEngine.Abstractions.Plugins;
using FlowEngine.Core.Plugins.Base;
using System.Collections.Immutable;

namespace {pluginName};

/// <summary>
/// Configuration for {pluginName} {pluginType.ToLower()} plugin.
/// Implements ArrayRow optimization with pre-calculated field indexes.
/// </summary>
public sealed class {pluginName}Configuration : PluginConfigurationBase
{{
    /// <summary>
    /// Gets the unique plugin identifier.
    /// </summary>
    public override string PluginId {{ get; }} = ""{pluginName.ToLower()}"";

    /// <summary>
    /// Gets the plugin name.
    /// </summary>
    public override string Name {{ get; }} = ""{pluginName}"";

    /// <summary>
    /// Gets the plugin version.
    /// </summary>
    public override string Version {{ get; }} = ""1.0.0"";

    /// <summary>
    /// Gets whether this plugin supports hot-swapping configuration updates.
    /// </summary>
    public override bool SupportsHotSwapping {{ get; }} = true;

    /// <summary>
    /// Gets the input data schema definition.
    /// Override this property to define your plugin's expected input schema.
    /// </summary>
    public override ISchema InputSchema {{ get; }} = CreateInputSchema();

    /// <summary>
    /// Gets the output data schema definition.
    /// Override this property to define your plugin's output schema.
    /// </summary>
    public override ISchema OutputSchema {{ get; }} = CreateOutputSchema();

    /// <summary>
    /// Gets plugin-specific configuration properties.
    /// Add your custom configuration properties here.
    /// </summary>
    public override ImmutableDictionary<string, object> Properties {{ get; }} = CreateProperties();

    // Plugin-specific configuration properties
    
    /// <summary>
    /// Gets or sets example configuration property.
    /// Replace this with your actual configuration properties.
    /// </summary>
    public string ExampleProperty {{ get; set; }} = ""DefaultValue"";

    /// <summary>
    /// Creates the input schema definition.
    /// </summary>
    /// <returns>Input schema with optimized field ordering</returns>
    private static ISchema CreateInputSchema()
    {{
        // TODO: Define your input schema with sequential field indexes
        // Example schema definition:
        /*
        return SchemaBuilder.Create()
            .AddField(""id"", typeof(int), index: 0, required: true)
            .AddField(""name"", typeof(string), index: 1, required: true)
            .AddField(""timestamp"", typeof(DateTime), index: 2, required: false)
            .Build();
        */
        
        throw new NotImplementedException(""Define your input schema in CreateInputSchema()"");
    }}

    /// <summary>
    /// Creates the output schema definition.
    /// </summary>
    /// <returns>Output schema with optimized field ordering</returns>
    private static ISchema CreateOutputSchema()
    {{
        // TODO: Define your output schema with sequential field indexes
        // Example schema definition:
        /*
        return SchemaBuilder.Create()
            .AddField(""id"", typeof(int), index: 0, required: true)
            .AddField(""processed_name"", typeof(string), index: 1, required: true)
            .AddField(""processed_at"", typeof(DateTime), index: 2, required: true)
            .Build();
        */
        
        throw new NotImplementedException(""Define your output schema in CreateOutputSchema()"");
    }}

    /// <summary>
    /// Creates the plugin properties dictionary.
    /// </summary>
    /// <returns>Plugin properties</returns>
    private static ImmutableDictionary<string, object> CreateProperties()
    {{
        return ImmutableDictionary<string, object>.Empty
            .Add(""ExampleProperty"", ""DefaultValue"")
            .Add(""SupportsBatching"", true)
            .Add(""MaxBatchSize"", 10000);
    }}
}}";

    /// <summary>
    /// Gets the validator template for five-component plugins.
    /// </summary>
    /// <param name="pluginName">Name of the plugin</param>
    /// <param name="pluginType">Type of plugin (Source, Transform, Sink)</param>
    /// <returns>Validator class content</returns>
    public static string GetValidatorTemplate(string pluginName, string pluginType) => $@"using FlowEngine.Abstractions.Data;
using FlowEngine.Abstractions.Plugins;
using FlowEngine.Core.Plugins.Base;
using Microsoft.Extensions.Logging;

namespace {pluginName};

/// <summary>
/// Validator for {pluginName} {pluginType.ToLower()} plugin.
/// Provides comprehensive validation including ArrayRow optimization checks.
/// </summary>
public sealed class {pluginName}Validator : PluginValidatorBase<{pluginName}Configuration>
{{
    /// <summary>
    /// Initializes a new instance of the {pluginName}Validator class.
    /// </summary>
    /// <param name=""logger"">Logger for validation operations</param>
    public {pluginName}Validator(ILogger<{pluginName}Validator> logger) : base(logger)
    {{
    }}

    /// <summary>
    /// Validates plugin-specific business rules and constraints.
    /// </summary>
    /// <param name=""configuration"">Configuration to validate</param>
    /// <param name=""cancellationToken"">Cancellation token for validation timeout</param>
    /// <returns>Plugin-specific validation result</returns>
    protected override async Task<ValidationResult> ValidatePluginSpecificAsync(
        {pluginName}Configuration configuration, 
        CancellationToken cancellationToken = default)
    {{
        var errors = new List<ValidationError>();
        var warnings = new List<ValidationWarning>();

        // TODO: Add your plugin-specific validation logic here
        
        // Example validation:
        if (string.IsNullOrWhiteSpace(configuration.ExampleProperty))
        {{
            errors.Add(new ValidationError
            {{
                Code = ""EXAMPLE_PROPERTY_REQUIRED"",
                Message = ""ExampleProperty cannot be null or empty"",
                PropertyPath = nameof(configuration.ExampleProperty),
                Severity = ValidationSeverity.Error,
                Remediation = ""Provide a valid value for ExampleProperty""
            }});
        }}

        // Performance validation example
        if (configuration.Properties.TryGetValue(""MaxBatchSize"", out var batchSizeObj) && 
            batchSizeObj is int batchSize && batchSize > 50000)
        {{
            warnings.Add(new ValidationWarning
            {{
                Code = ""LARGE_BATCH_SIZE"",
                Message = ""Large batch sizes may impact performance"",
                PropertyPath = ""Properties.MaxBatchSize"",
                SuggestedAction = ""Consider reducing batch size to improve memory usage""
            }});
        }}

        var isValid = !errors.Any();
        var validationTime = TimeSpan.FromMilliseconds(1); // Placeholder

        return isValid 
            ? ValidationResult.Success(validationTime, warnings)
            : ValidationResult.Failure(errors, validationTime, warnings);
    }}
}}";

    /// <summary>
    /// Gets the plugin template for five-component plugins.
    /// </summary>
    /// <param name="pluginName">Name of the plugin</param>
    /// <param name="pluginType">Type of plugin (Source, Transform, Sink)</param>
    /// <returns>Plugin class content</returns>
    public static string GetPluginTemplate(string pluginName, string pluginType) => $@"using FlowEngine.Abstractions.Plugins;
using FlowEngine.Core.Plugins.Base;
using Microsoft.Extensions.Logging;

namespace {pluginName};

/// <summary>
/// {pluginName} {pluginType.ToLower()} plugin implementing the five-component architecture.
/// Provides lifecycle management and component coordination.
/// </summary>
public sealed class {pluginName}Plugin : PluginBase<
    {pluginName}Configuration, 
    {pluginName}Validator, 
    {pluginName}Processor, 
    {pluginName}Service>
{{
    /// <summary>
    /// Gets the plugin metadata and information.
    /// </summary>
    public override PluginMetadata Metadata {{ get; }} = new()
    {{
        Id = ""{pluginName.ToLower()}"",
        Name = ""{pluginName}"",
        Type = ""{pluginType}"",
        Version = ""1.0.0"",
        Description = ""A {pluginType.ToLower()} plugin for FlowEngine"",
        Author = ""Your Name"",
        Tags = [""{pluginType.ToLower()}"", ""custom"", ""generated""],
        Capabilities = [""ArrayRowOptimized"", ""HotSwappable"", ""HighPerformance""],
        MinimumEngineVersion = ""3.0.0""
    }};

    /// <summary>
    /// Gets the current plugin health status.
    /// </summary>
    public override PluginHealth Health => CreateHealthStatus();

    /// <summary>
    /// Initializes a new instance of the {pluginName}Plugin class.
    /// </summary>
    /// <param name=""configuration"">Plugin configuration</param>
    /// <param name=""validator"">Plugin validator</param>
    /// <param name=""processor"">Plugin processor</param>
    /// <param name=""service"">Plugin service</param>
    /// <param name=""logger"">Logger for plugin operations</param>
    public {pluginName}Plugin(
        {pluginName}Configuration configuration,
        {pluginName}Validator validator,
        {pluginName}Processor processor,
        {pluginName}Service service,
        ILogger<{pluginName}Plugin> logger) 
        : base(configuration, validator, processor, service, logger)
    {{
    }}

    /// <summary>
    /// Performs plugin-specific initialization logic.
    /// </summary>
    /// <param name=""cancellationToken"">Cancellation token for initialization</param>
    /// <returns>Task representing the plugin-specific initialization</returns>
    protected override async Task InitializePluginSpecificAsync(CancellationToken cancellationToken)
    {{
        Logger.LogInformation(""Initializing {{PluginName}} plugin..."", Metadata.Name);
        
        // TODO: Add your plugin-specific initialization logic here
        // Examples:
        // - Initialize external connections
        // - Validate external dependencies
        // - Pre-load reference data
        // - Set up performance counters
        
        await Task.CompletedTask;
        
        Logger.LogInformation(""{{PluginName}} plugin initialized successfully"", Metadata.Name);
    }}

    /// <summary>
    /// Performs plugin-specific start logic.
    /// </summary>
    /// <param name=""cancellationToken"">Cancellation token for start operation</param>
    /// <returns>Task representing the plugin-specific start</returns>
    protected override async Task StartPluginSpecificAsync(CancellationToken cancellationToken)
    {{
        Logger.LogInformation(""Starting {{PluginName}} plugin..."", Metadata.Name);
        
        // TODO: Add your plugin-specific start logic here
        // Examples:
        // - Start background services
        // - Open network connections
        // - Begin monitoring threads
        
        await Task.CompletedTask;
        
        Logger.LogInformation(""{{PluginName}} plugin started successfully"", Metadata.Name);
    }}

    /// <summary>
    /// Performs plugin-specific stop logic.
    /// </summary>
    /// <param name=""cancellationToken"">Cancellation token for stop operation</param>
    /// <returns>Task representing the plugin-specific stop</returns>
    protected override async Task StopPluginSpecificAsync(CancellationToken cancellationToken)
    {{
        Logger.LogInformation(""Stopping {{PluginName}} plugin..."", Metadata.Name);
        
        // TODO: Add your plugin-specific stop logic here
        // Examples:
        // - Stop background services gracefully
        // - Close network connections
        // - Flush pending data
        
        await Task.CompletedTask;
        
        Logger.LogInformation(""{{PluginName}} plugin stopped successfully"", Metadata.Name);
    }}

    /// <summary>
    /// Creates the current health status for the plugin.
    /// </summary>
    /// <returns>Plugin health status</returns>
    private PluginHealth CreateHealthStatus()
    {{
        var checks = new List<HealthCheck>();
        
        // TODO: Add your health checks here
        checks.Add(new HealthCheck
        {{
            Name = ""Configuration"",
            Passed = Configuration != null,
            Details = Configuration != null ? ""Configuration loaded"" : ""Configuration not loaded"",
            ExecutionTime = TimeSpan.FromMilliseconds(1),
            Severity = HealthCheckSeverity.Critical
        }});
        
        checks.Add(new HealthCheck
        {{
            Name = ""ComponentIntegration"",
            Passed = State == PluginState.Running,
            Details = $""Plugin state: {{State}}"",
            ExecutionTime = TimeSpan.FromMilliseconds(1),
            Severity = HealthCheckSeverity.Error
        }});

        var allHealthy = checks.All(c => c.Passed);
        var healthScore = allHealthy ? 100 : (int)(checks.Count(c => c.Passed) * 100.0 / checks.Count);

        return new PluginHealth
        {{
            IsHealthy = allHealthy,
            State = State,
            Checks = checks.ToImmutableArray(),
            LastChecked = DateTimeOffset.UtcNow,
            HealthScore = healthScore,
            Recommendations = allHealthy 
                ? ImmutableArray<string>.Empty 
                : ImmutableArray.Create(""Check failed health checks and resolve issues"")
        }};
    }}
}}";

    /// <summary>
    /// Gets the processor template for five-component plugins.
    /// </summary>
    /// <param name="pluginName">Name of the plugin</param>
    /// <param name="pluginType">Type of plugin (Source, Transform, Sink)</param>
    /// <returns>Processor class content</returns>
    public static string GetProcessorTemplate(string pluginName, string pluginType) => $@"using FlowEngine.Abstractions.Data;
using FlowEngine.Abstractions.Plugins;
using FlowEngine.Core.Plugins.Base;
using Microsoft.Extensions.Logging;

namespace {pluginName};

/// <summary>
/// Processor for {pluginName} {pluginType.ToLower()} plugin.
/// Orchestrates data flow and coordinates with the service component.
/// </summary>
public sealed class {pluginName}Processor : PluginProcessorBase<{pluginName}Configuration, {pluginName}Service>
{{
    /// <summary>
    /// Gets whether this processor is compatible with the specified service.
    /// </summary>
    /// <param name=""service"">Service to check compatibility</param>
    /// <returns>True if processor is compatible with the service</returns>
    public override bool IsServiceCompatible(IPluginService service)
    {{
        return service is {pluginName}Service;
    }}

    /// <summary>
    /// Gets current processor performance metrics and statistics.
    /// </summary>
    /// <returns>Current processor performance metrics</returns>
    public override ProcessorMetrics GetMetrics()
    {{
        // TODO: Implement actual metrics collection
        return new ProcessorMetrics
        {{
            TotalChunksProcessed = 0,
            TotalRowsProcessed = 0,
            ErrorCount = 0,
            AverageProcessingTimeMs = 0.0,
            MemoryUsageBytes = GC.GetTotalMemory(false),
            ThroughputPerSecond = 0.0,
            LastProcessedAt = DateTimeOffset.UtcNow,
            BackpressureLevel = 0,
            AverageChunkSize = 0.0
        }};
    }}

    /// <summary>
    /// Initializes a new instance of the {pluginName}Processor class.
    /// </summary>
    /// <param name=""configuration"">Plugin configuration</param>
    /// <param name=""service"">Service component for data processing</param>
    /// <param name=""logger"">Logger for processor operations</param>
    public {pluginName}Processor(
        {pluginName}Configuration configuration,
        {pluginName}Service service,
        ILogger<{pluginName}Processor> logger) 
        : base(configuration, service, logger)
    {{
    }}

    /// <summary>
    /// Performs processor-specific initialization logic.
    /// </summary>
    /// <param name=""cancellationToken"">Cancellation token for initialization</param>
    /// <returns>Task representing the processor-specific initialization</returns>
    protected override async Task InitializeProcessorSpecificAsync(CancellationToken cancellationToken)
    {{
        Logger.LogInformation(""Initializing {{ProcessorName}} processor..."", nameof({pluginName}Processor));
        
        // TODO: Add your processor-specific initialization logic here
        // Examples:
        // - Configure data flow channels
        // - Set up batching parameters
        // - Initialize performance counters
        
        await Task.CompletedTask;
        
        Logger.LogInformation(""{{ProcessorName}} processor initialized successfully"", nameof({pluginName}Processor));
    }}

    /// <summary>
    /// Performs processor-specific start logic.
    /// </summary>
    /// <param name=""cancellationToken"">Cancellation token for start operation</param>
    /// <returns>Task representing the processor-specific start</returns>
    protected override async Task StartProcessorSpecificAsync(CancellationToken cancellationToken)
    {{
        Logger.LogInformation(""Starting {{ProcessorName}} processor..."", nameof({pluginName}Processor));
        
        // TODO: Add your processor-specific start logic here
        // Examples:
        // - Start data flow monitoring
        // - Begin performance tracking
        // - Initialize flow control mechanisms
        
        await Task.CompletedTask;
        
        Logger.LogInformation(""{{ProcessorName}} processor started successfully"", nameof({pluginName}Processor));
    }}

    /// <summary>
    /// Performs processor-specific stop logic.
    /// </summary>
    /// <param name=""cancellationToken"">Cancellation token for stop operation</param>
    /// <returns>Task representing the processor-specific stop</returns>
    protected override async Task StopProcessorSpecificAsync(CancellationToken cancellationToken)
    {{
        Logger.LogInformation(""Stopping {{ProcessorName}} processor..."", nameof({pluginName}Processor));
        
        // TODO: Add your processor-specific stop logic here
        // Examples:
        // - Flush pending data chunks
        // - Stop monitoring threads
        // - Clean up flow control resources
        
        await Task.CompletedTask;
        
        Logger.LogInformation(""{{ProcessorName}} processor stopped successfully"", nameof({pluginName}Processor));
    }}
}}";

    /// <summary>
    /// Gets the service template for five-component plugins.
    /// </summary>
    /// <param name="pluginName">Name of the plugin</param>
    /// <param name="pluginType">Type of plugin (Source, Transform, Sink)</param>
    /// <returns>Service class content</returns>
    public static string GetServiceTemplate(string pluginName, string pluginType) => $@"using FlowEngine.Abstractions.Data;
using FlowEngine.Abstractions.Plugins;
using FlowEngine.Core.Plugins.Base;
using Microsoft.Extensions.Logging;

namespace {pluginName};

/// <summary>
/// Service for {pluginName} {pluginType.ToLower()} plugin.
/// Implements core business logic with ArrayRow optimization.
/// </summary>
public sealed class {pluginName}Service : PluginServiceBase<{pluginName}Configuration>
{{
    /// <summary>
    /// Gets whether this service can process the specified schema.
    /// </summary>
    /// <param name=""schema"">Schema to check compatibility</param>
    /// <returns>True if service can process the schema</returns>
    public override bool CanProcess(ISchema schema)
    {{
        // TODO: Implement schema compatibility checking
        // Example:
        /*
        var requiredFields = new[] {{ ""id"", ""name"" }};
        return requiredFields.All(field => 
            schema.Fields.Any(f => f.Name.Equals(field, StringComparison.OrdinalIgnoreCase)));
        */
        
        return true; // Placeholder - implement actual logic
    }}

    /// <summary>
    /// Gets current service performance metrics and statistics.
    /// </summary>
    /// <returns>Current service performance metrics</returns>
    public override ServiceMetrics GetMetrics()
    {{
        // TODO: Implement actual metrics collection
        return new ServiceMetrics
        {{
            TotalChunksProcessed = 0,
            TotalRowsProcessed = 0,
            ErrorCount = 0,
            AverageProcessingTimeMs = GetAverageProcessingTime(),
            MemoryUsageBytes = GC.GetTotalMemory(false),
            ThroughputPerSecond = 0.0,
            LastProcessedAt = DateTimeOffset.UtcNow,
            AverageFieldAccessTimeNs = 10.0, // Target <15ns
            OptimizationEfficiency = 95 // Percentage
        }};
    }}

    /// <summary>
    /// Processes a single data chunk with ArrayRow optimization.
    /// </summary>
    /// <param name=""chunk"">Data chunk to process</param>
    /// <param name=""cancellationToken"">Cancellation token for processing</param>
    /// <returns>Processed data chunk</returns>
    public override async Task<IChunk> ProcessChunkAsync(IChunk chunk, CancellationToken cancellationToken = default)
    {{
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        
        try
        {{
            Logger.LogTrace(""Processing chunk with {{RowCount}} rows"", chunk.RowCount);
            
            // Pre-calculate field indexes for O(1) access
            var inputIndexes = Configuration.InputFieldIndexes;
            var outputIndexes = Configuration.OutputFieldIndexes;
            
            var processedRows = new List<IArrayRow>();
            
            foreach (var row in chunk.Rows)
            {{
                var processedRow = await ProcessRowAsync(row, cancellationToken);
                processedRows.Add(processedRow);
            }}
            
            // Create output chunk with processed rows
            var outputChunk = CreateOutputChunk(processedRows, chunk.Schema);
            
            stopwatch.Stop();
            UpdateMetrics(stopwatch.ElapsedMilliseconds, chunk.RowCount);
            
            Logger.LogTrace(""Processed chunk in {{Duration}}ms"", stopwatch.ElapsedMilliseconds);
            
            return outputChunk;
        }}
        catch (Exception ex)
        {{
            stopwatch.Stop();
            UpdateMetrics(stopwatch.ElapsedMilliseconds, chunk.RowCount, hasError: true);
            
            Logger.LogError(ex, ""Error processing chunk with {{RowCount}} rows"", chunk.RowCount);
            throw;
        }}
    }}

    /// <summary>
    /// Processes a single ArrayRow with optimized field access.
    /// </summary>
    /// <param name=""row"">ArrayRow to process</param>
    /// <param name=""cancellationToken"">Cancellation token for processing</param>
    /// <returns>Processed ArrayRow</returns>
    protected override async Task<IArrayRow> ProcessRowAsync(IArrayRow row, CancellationToken cancellationToken = default)
    {{
        // TODO: Implement your row processing logic here
        // Use pre-calculated field indexes for optimal performance
        
        // Example ArrayRow optimization pattern:
        /*
        var idIndex = Configuration.GetInputFieldIndex(""id"");
        var nameIndex = Configuration.GetInputFieldIndex(""name"");
        
        var id = row.GetValue<int>(idIndex);
        var name = row.GetValue<string>(nameIndex);
        
        // Process the data
        var processedName = $""Processed: {{name}}"";
        var processedAt = DateTime.UtcNow;
        
        // Create output row with optimized field access
        var outputIdIndex = Configuration.GetOutputFieldIndex(""id"");
        var outputNameIndex = Configuration.GetOutputFieldIndex(""processed_name"");
        var outputTimestampIndex = Configuration.GetOutputFieldIndex(""processed_at"");
        
        return row.With(outputIdIndex, id)
                  .With(outputNameIndex, processedName)
                  .With(outputTimestampIndex, processedAt);
        */
        
        await Task.CompletedTask;
        return row; // Placeholder - implement actual processing
    }}

    /// <summary>
    /// Initializes a new instance of the {pluginName}Service class.
    /// </summary>
    /// <param name=""configuration"">Plugin configuration</param>
    /// <param name=""logger"">Logger for service operations</param>
    public {pluginName}Service(
        {pluginName}Configuration configuration,
        ILogger<{pluginName}Service> logger) 
        : base(configuration, logger)
    {{
    }}

    /// <summary>
    /// Performs service-specific initialization logic.
    /// </summary>
    /// <param name=""cancellationToken"">Cancellation token for initialization</param>
    /// <returns>Task representing the service-specific initialization</returns>
    protected override async Task InitializeServiceSpecificAsync(CancellationToken cancellationToken)
    {{
        Logger.LogInformation(""Initializing {{ServiceName}} service..."", nameof({pluginName}Service));
        
        // TODO: Add your service-specific initialization logic here
        // Examples:
        // - Initialize external connections
        // - Load reference data
        // - Prepare lookup tables
        // - Set up caches
        
        await Task.CompletedTask;
        
        Logger.LogInformation(""{{ServiceName}} service initialized successfully"", nameof({pluginName}Service));
    }}

    /// <summary>
    /// Performs service-specific start logic.
    /// </summary>
    /// <param name=""cancellationToken"">Cancellation token for start operation</param>
    /// <returns>Task representing the service-specific start</returns>
    protected override async Task StartServiceSpecificAsync(CancellationToken cancellationToken)
    {{
        Logger.LogInformation(""Starting {{ServiceName}} service..."", nameof({pluginName}Service));
        
        // TODO: Add your service-specific start logic here
        // Examples:
        // - Open database connections
        // - Start monitoring threads
        // - Begin cache warming
        
        await Task.CompletedTask;
        
        Logger.LogInformation(""{{ServiceName}} service started successfully"", nameof({pluginName}Service));
    }}

    /// <summary>
    /// Performs service-specific stop logic.
    /// </summary>
    /// <param name=""cancellationToken"">Cancellation token for stop operation</param>
    /// <returns>Task representing the service-specific stop</returns>
    protected override async Task StopServiceSpecificAsync(CancellationToken cancellationToken)
    {{
        Logger.LogInformation(""Stopping {{ServiceName}} service..."", nameof({pluginName}Service));
        
        // TODO: Add your service-specific stop logic here
        // Examples:
        // - Close database connections
        // - Flush caches
        // - Stop background threads
        
        await Task.CompletedTask;
        
        Logger.LogInformation(""{{ServiceName}} service stopped successfully"", nameof({pluginName}Service));
    }}

    /// <summary>
    /// Creates an output chunk from processed rows.
    /// </summary>
    /// <param name=""processedRows"">Processed rows</param>
    /// <param name=""inputSchema"">Input schema</param>
    /// <returns>Output chunk</returns>
    private IChunk CreateOutputChunk(List<IArrayRow> processedRows, ISchema inputSchema)
    {{
        // TODO: Implement chunk creation logic
        // This is a placeholder - implement actual chunk creation
        throw new NotImplementedException(""Implement CreateOutputChunk method"");
    }}
}}";

    /// <summary>
    /// Gets the configuration YAML template.
    /// </summary>
    /// <param name="pluginName">Name of the plugin</param>
    /// <param name="pluginType">Type of plugin (Source, Transform, Sink)</param>
    /// <returns>Configuration YAML content</returns>
    public static string GetConfigurationYamlTemplate(string pluginName, string pluginType) => $@"# {pluginName} Plugin Configuration
# Supports schema-first design with ArrayRow optimization

plugin:
  name: ""{pluginName}""
  type: ""{pluginType}""
  version: ""1.0.0""

# Input schema definition with explicit field indexing
input_schema:
  fields:
    - name: ""id""
      type: ""int32""
      index: 0
      required: true
      description: ""Unique identifier""
    - name: ""name""
      type: ""string""
      index: 1
      required: true
      description: ""Name field""
    # Add more fields as needed with sequential indexes

# Output schema definition with explicit field indexing
output_schema:
  fields:
    - name: ""id""
      type: ""int32""
      index: 0
      required: true
      description: ""Unique identifier""
    - name: ""processed_name""
      type: ""string""
      index: 1
      required: true
      description: ""Processed name field""
    - name: ""processed_at""
      type: ""datetime""
      index: 2
      required: true
      description: ""Processing timestamp""
    # Add more fields as needed with sequential indexes

# Plugin-specific configuration
configuration:
  example_property: ""DefaultValue""
  max_batch_size: 10000
  enable_caching: true
  timeout_seconds: 30

# Performance settings
performance:
  enable_arrayrow_optimization: true
  target_throughput: 200000  # rows per second
  memory_limit_mb: 500
  parallel_processing: true

# Monitoring and health checks
monitoring:
  enable_metrics: true
  health_check_interval: 30  # seconds
  log_performance: true
  track_memory_usage: true";

    /// <summary>
    /// Gets the minimal plugin template.
    /// </summary>
    /// <param name="pluginName">Name of the plugin</param>
    /// <param name="pluginType">Type of plugin (Source, Transform, Sink)</param>
    /// <returns>Minimal plugin class content</returns>
    public static string GetMinimalPluginTemplate(string pluginName, string pluginType) => $@"using FlowEngine.Abstractions.Data;
using FlowEngine.Abstractions.Plugins;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Collections.Immutable;

namespace {pluginName};

/// <summary>
/// {pluginName} {pluginType.ToLower()} plugin with minimal dependencies.
/// Implements plugin interfaces directly using only FlowEngine.Abstractions.
/// </summary>
public sealed class {pluginName}Plugin : IPlugin, IPluginService, IPluginProcessor
{{
    private readonly ILogger<{pluginName}Plugin> _logger;
    private IPluginConfiguration? _configuration;
    private PluginState _state = PluginState.Created;

    /// <summary>
    /// Gets the unique name of this plugin instance.
    /// </summary>
    public string Name {{ get; }} = ""{pluginName}"";

    /// <summary>
    /// Gets the output schema produced by this plugin.
    /// </summary>
    public ISchema? OutputSchema {{ get; private set; }}

    /// <summary>
    /// Gets the plugin version for compatibility checking.
    /// </summary>
    public string Version {{ get; }} = ""1.0.0"";

    /// <summary>
    /// Gets optional metadata about this plugin.
    /// </summary>
    public IReadOnlyDictionary<string, object>? Metadata {{ get; }} = new Dictionary<string, object>
    {{
        [""Type""] = ""{pluginType}"",
        [""Description""] = ""A minimal {pluginType.ToLower()} plugin"",
        [""Author""] = ""Your Name"",
        [""Framework""] = ""FlowEngine"",
        [""Architecture""] = ""Minimal""
    }};

    /// <summary>
    /// Gets the current plugin state.
    /// </summary>
    public PluginState State => _state;

    /// <summary>
    /// Gets whether this plugin supports hot-swapping configuration updates.
    /// </summary>
    public bool SupportsHotSwapping => false;

    /// <summary>
    /// Gets the current plugin health status.
    /// </summary>
    public PluginHealth Health => new()
    {{
        IsHealthy = _state == PluginState.Running,
        State = _state,
        Checks = ImmutableArray.Create(new HealthCheck
        {{
            Name = ""BasicHealth"",
            Passed = _state == PluginState.Running,
            Details = $""Plugin state: {{_state}}"",
            ExecutionTime = TimeSpan.FromMilliseconds(1),
            Severity = HealthCheckSeverity.Info
        }}),
        LastChecked = DateTimeOffset.UtcNow,
        HealthScore = _state == PluginState.Running ? 100 : 0
    }};

    // IPluginService implementation
    public ServiceState ServiceState {{ get; private set; }} = ServiceState.Created;

    // IPluginProcessor implementation  
    public ProcessorState ProcessorState {{ get; private set; }} = ProcessorState.Created;

    /// <summary>
    /// Initializes a new instance of the {pluginName}Plugin class.
    /// </summary>
    /// <param name=""logger"">Logger for plugin operations</param>
    public {pluginName}Plugin(ILogger<{pluginName}Plugin> logger)
    {{
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }}

    /// <summary>
    /// Initializes the plugin with configuration.
    /// </summary>
    /// <param name=""config"">Plugin-specific configuration</param>
    /// <param name=""cancellationToken"">Cancellation token</param>
    /// <returns>Task representing the initialization</returns>
    public async Task InitializeAsync(IConfiguration config, CancellationToken cancellationToken = default)
    {{
        _logger.LogInformation(""Initializing {{PluginName}} plugin..."", Name);
        
        _state = PluginState.Initializing;
        
        try
        {{
            // TODO: Initialize your plugin here
            // Example:
            // - Parse configuration
            // - Set up schemas
            // - Initialize resources
            
            // Create sample schemas (replace with your actual schemas)
            OutputSchema = CreateOutputSchema();
            
            _state = PluginState.Initialized;
            ServiceState = ServiceState.Initialized;
            ProcessorState = ProcessorState.Initialized;
            
            _logger.LogInformation(""{{PluginName}} plugin initialized successfully"", Name);
        }}
        catch (Exception ex)
        {{
            _state = PluginState.Failed;
            _logger.LogError(ex, ""Failed to initialize {{PluginName}} plugin"", Name);
            throw;
        }}
        
        await Task.CompletedTask;
    }}

    /// <summary>
    /// Starts the plugin and begins processing operations.
    /// </summary>
    /// <param name=""cancellationToken"">Cancellation token</param>
    /// <returns>Task representing the start operation</returns>
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {{
        if (_state != PluginState.Initialized)
        {{
            throw new InvalidOperationException($""Cannot start plugin from state {{_state}}"");
        }}

        _logger.LogInformation(""Starting {{PluginName}} plugin..."", Name);
        
        _state = PluginState.Starting;
        
        try
        {{
            // TODO: Start your plugin here
            
            _state = PluginState.Running;
            ServiceState = ServiceState.Running;
            ProcessorState = ProcessorState.Running;
            
            _logger.LogInformation(""{{PluginName}} plugin started successfully"", Name);
        }}
        catch (Exception ex)
        {{
            _state = PluginState.Failed;
            _logger.LogError(ex, ""Failed to start {{PluginName}} plugin"", Name);
            throw;
        }}
        
        await Task.CompletedTask;
    }}

    /// <summary>
    /// Stops the plugin and suspends processing operations.
    /// </summary>
    /// <param name=""cancellationToken"">Cancellation token</param>
    /// <returns>Task representing the stop operation</returns>
    public async Task StopAsync(CancellationToken cancellationToken = default)
    {{
        _logger.LogInformation(""Stopping {{PluginName}} plugin..."", Name);
        
        _state = PluginState.Stopping;
        
        try
        {{
            // TODO: Stop your plugin here
            
            _state = PluginState.Stopped;
            ServiceState = ServiceState.Stopped;
            ProcessorState = ProcessorState.Stopped;
            
            _logger.LogInformation(""{{PluginName}} plugin stopped successfully"", Name);
        }}
        catch (Exception ex)
        {{
            _state = PluginState.Failed;
            _logger.LogError(ex, ""Failed to stop {{PluginName}} plugin"", Name);
            throw;
        }}
        
        await Task.CompletedTask;
    }}

    /// <summary>
    /// Validates that this plugin can process the given input schema.
    /// </summary>
    /// <param name=""inputSchema"">Input schema to validate</param>
    /// <returns>Validation result</returns>
    public PluginValidationResult ValidateInputSchema(ISchema? inputSchema)
    {{
        // TODO: Implement schema validation logic
        return PluginValidationResult.Success();
    }}

    /// <summary>
    /// Gets performance metrics for monitoring.
    /// </summary>
    /// <returns>Current plugin metrics</returns>
    public PluginMetrics GetMetrics()
    {{
        return new PluginMetrics
        {{
            PluginId = Name,
            State = _state,
            TotalProcessed = 0,
            ErrorCount = 0,
            AverageProcessingTimeMs = 0.0,
            MemoryUsageBytes = GC.GetTotalMemory(false),
            LastActivityAt = DateTimeOffset.UtcNow,
            ThroughputPerSecond = 0.0
        }};
    }}

    // Additional interface implementations would go here...
    // TODO: Implement remaining interface members based on your plugin type

    /// <summary>
    /// Creates the output schema for this plugin.
    /// </summary>
    /// <returns>Output schema</returns>
    private static ISchema CreateOutputSchema()
    {{
        // TODO: Implement actual schema creation
        throw new NotImplementedException(""Implement CreateOutputSchema method"");
    }}

    /// <summary>
    /// Disposes the plugin and releases resources.
    /// </summary>
    /// <returns>Task representing disposal</returns>
    public async ValueTask DisposeAsync()
    {{
        if (_state == PluginState.Running)
        {{
            await StopAsync();
        }}
        
        _state = PluginState.Disposed;
        _logger.LogInformation(""{{PluginName}} plugin disposed"", Name);
    }}

    // Additional interface implementations...
    // (Implement remaining methods from IPluginService and IPluginProcessor as needed)
}}";

    /// <summary>
    /// Gets the unit test template.
    /// </summary>
    /// <param name="pluginName">Name of the plugin</param>
    /// <param name="pluginType">Type of plugin (Source, Transform, Sink)</param>
    /// <returns>Unit test class content</returns>
    public static string GetUnitTestTemplate(string pluginName, string pluginType) => $@"using FlowEngine.Abstractions.Data;
using FlowEngine.Abstractions.Plugins;
using Microsoft.Extensions.Logging;
using Xunit;

namespace {pluginName}.Tests;

/// <summary>
/// Unit tests for {pluginName} plugin.
/// Tests plugin functionality, performance, and error handling.
/// </summary>
public class {pluginName}PluginTests
{{
    private readonly ILogger<{pluginName}Plugin> _logger;

    public {pluginName}PluginTests()
    {{
        var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        _logger = loggerFactory.CreateLogger<{pluginName}Plugin>();
    }}

    [Fact]
    public async Task InitializeAsync_WithValidConfiguration_ShouldSucceed()
    {{
        // Arrange
        var plugin = CreatePlugin();

        // Act & Assert
        var exception = await Record.ExceptionAsync(() => plugin.InitializeAsync(CreateConfiguration()));
        Assert.Null(exception);
        Assert.Equal(PluginState.Initialized, plugin.State);
    }}

    [Fact]
    public async Task StartAsync_AfterInitialization_ShouldSucceed()
    {{
        // Arrange
        var plugin = CreatePlugin();
        await plugin.InitializeAsync(CreateConfiguration());

        // Act & Assert
        var exception = await Record.ExceptionAsync(() => plugin.StartAsync());
        Assert.Null(exception);
        Assert.Equal(PluginState.Running, plugin.State);
    }}

    [Fact]
    public async Task ProcessChunkAsync_WithValidData_ShouldProcessSuccessfully()
    {{
        // Arrange
        var plugin = CreatePlugin();
        await plugin.InitializeAsync(CreateConfiguration());
        await plugin.StartAsync();
        
        var testChunk = CreateTestChunk();

        // Act
        // TODO: Implement processing test
        // var result = await plugin.ProcessChunkAsync(testChunk);

        // Assert
        // TODO: Add assertions for processed data
        Assert.True(true); // Placeholder
    }}

    [Fact]
    public void ValidateInputSchema_WithCompatibleSchema_ShouldReturnValid()
    {{
        // Arrange
        var plugin = CreatePlugin();
        var schema = CreateCompatibleSchema();

        // Act
        var result = plugin.ValidateInputSchema(schema);

        // Assert
        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }}

    [Fact]
    public void GetMetrics_WhenCalled_ShouldReturnValidMetrics()
    {{
        // Arrange
        var plugin = CreatePlugin();

        // Act
        var metrics = plugin.GetMetrics();

        // Assert
        Assert.NotNull(metrics);
        Assert.Equal(plugin.Name, metrics.PluginId);
        Assert.True(metrics.MemoryUsageBytes > 0);
    }}

    [Fact]
    public async Task Performance_ProcessingLargeDataset_ShouldMeetThroughputTarget()
    {{
        // Arrange
        var plugin = CreatePlugin();
        await plugin.InitializeAsync(CreateConfiguration());
        await plugin.StartAsync();
        
        var largeDataset = CreateLargeTestDataset(100000); // 100K rows
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        // Act
        // TODO: Process large dataset
        // foreach (var chunk in largeDataset)
        // {{
        //     await plugin.ProcessChunkAsync(chunk);
        // }}
        
        stopwatch.Stop();

        // Assert
        var throughput = 100000 / stopwatch.Elapsed.TotalSeconds;
        Assert.True(throughput > 200000, $""Throughput {{throughput:N0}} rows/sec is below target (200K rows/sec)"");
    }}

    [Fact]
    public async Task ErrorHandling_WithInvalidData_ShouldHandleGracefully()
    {{
        // Arrange
        var plugin = CreatePlugin();
        await plugin.InitializeAsync(CreateConfiguration());
        await plugin.StartAsync();
        
        var invalidChunk = CreateInvalidTestChunk();

        // Act & Assert
        // TODO: Test error handling
        // var exception = await Record.ExceptionAsync(() => plugin.ProcessChunkAsync(invalidChunk));
        // Assert specific error handling behavior
        Assert.True(true); // Placeholder
    }}

    private {pluginName}Plugin CreatePlugin()
    {{
        // TODO: Create plugin instance with proper dependencies
        // For five-component plugins:
        /*
        var configuration = new {pluginName}Configuration();
        var validator = new {pluginName}Validator(_logger);
        var service = new {pluginName}Service(configuration, _logger);
        var processor = new {pluginName}Processor(configuration, service, _logger);
        
        return new {pluginName}Plugin(configuration, validator, processor, service, _logger);
        */
        
        // For minimal plugins:
        return new {pluginName}Plugin(_logger);
    }}

    private IConfiguration CreateConfiguration()
    {{
        // TODO: Create test configuration
        var configDict = new Dictionary<string, string>
        {{
            [""TestProperty""] = ""TestValue""
        }};
        
        return new ConfigurationBuilder()
            .AddInMemoryCollection(configDict)
            .Build();
    }}

    private IChunk CreateTestChunk()
    {{
        // TODO: Create test data chunk
        throw new NotImplementedException(""Implement CreateTestChunk method"");
    }}

    private ISchema CreateCompatibleSchema()
    {{
        // TODO: Create compatible test schema
        throw new NotImplementedException(""Implement CreateCompatibleSchema method"");
    }}

    private IEnumerable<IChunk> CreateLargeTestDataset(int rowCount)
    {{
        // TODO: Create large test dataset for performance testing
        throw new NotImplementedException(""Implement CreateLargeTestDataset method"");
    }}

    private IChunk CreateInvalidTestChunk()
    {{
        // TODO: Create invalid test data for error handling tests
        throw new NotImplementedException(""Implement CreateInvalidTestChunk method"");
    }}
}}";

    /// <summary>
    /// Gets the test project file template.
    /// </summary>
    /// <param name="pluginName">Name of the plugin</param>
    /// <returns>Test project file content</returns>
    public static string GetTestProjectTemplate(string pluginName) => $@"<Project Sdk=""Microsoft.NET.Sdk"">

  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <IsPackable>false</IsPackable>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include=""Microsoft.NET.Test.Sdk"" Version=""17.9.0"" />
    <PackageReference Include=""xunit"" Version=""2.6.6"" />
    <PackageReference Include=""xunit.runner.visualstudio"" Version=""2.5.6"">
      <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
      <PrivateAssets>all</PrivateAssets>
    </PackageReference>
    <PackageReference Include=""Microsoft.Extensions.Logging"" Version=""8.0.1"" />
    <PackageReference Include=""Microsoft.Extensions.Configuration"" Version=""8.0.1"" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include=""..\{pluginName}.csproj"" />
  </ItemGroup>

</Project>";

    /// <summary>
    /// Gets the README template.
    /// </summary>
    /// <param name="pluginName">Name of the plugin</param>
    /// <param name="pluginType">Type of plugin (Source, Transform, Sink)</param>
    /// <param name="isMinimal">Whether this is a minimal plugin</param>
    /// <returns>README content</returns>
    public static string GetReadmeTemplate(string pluginName, string pluginType, bool isMinimal) => $@"# {pluginName} Plugin

A {pluginType.ToLower()} plugin for FlowEngine built with the **{(isMinimal ? "minimal" : "five-component")} architecture**.

## Overview

{pluginName} is a {pluginType.ToLower()} plugin that processes data using FlowEngine's high-performance ArrayRow optimization. The plugin is designed to achieve >200K rows/sec throughput with <15ns field access times.

## Architecture

This plugin uses the **{(isMinimal ? "minimal" : "five-component")} architecture pattern**:

{(isMinimal ? @"- **Minimal**: Implements plugin interfaces directly using only FlowEngine.Abstractions
- Lightweight with minimal dependencies
- Full control over implementation details" : @"- **Configuration**: Schema-first configuration with ArrayRow optimization
- **Validator**: Comprehensive validation including performance checks  
- **Plugin**: Central coordination and lifecycle management
- **Processor**: Data flow orchestration with chunked processing
- **Service**: Core business logic with ArrayRow optimization")}

## Getting Started

### Prerequisites

- .NET 8.0 or later
- FlowEngine {(isMinimal ? "Abstractions" : "Core")} package

### Building

```bash
dotnet build
```

### Testing

```bash
dotnet test
```

### Using the Plugin

1. **Configure the plugin** in your pipeline YAML:
```yaml
plugins:
  - name: ""{pluginName.ToLower()}""
    type: ""{pluginType}""
    configuration:
      # Add your configuration here
```

2. **Validate the plugin**:
```bash
flowengine plugin validate --path .
```

3. **Test with sample data**:
```bash
flowengine plugin test --path . --data sample.csv
```

4. **Benchmark performance**:
```bash
flowengine plugin benchmark --path . --rows 1000000
```

## Configuration

The plugin supports the following configuration options:

- `example_property`: Description of the property
- `max_batch_size`: Maximum number of rows to process in a batch (default: 10000)
- `enable_caching`: Whether to enable caching (default: true)

See `config.yaml` for a complete configuration example.

## Performance

This plugin is optimized for high-performance data processing:

- **ArrayRow Optimization**: Pre-calculated field indexes for O(1) access
- **Target Throughput**: >200K rows/sec
- **Memory Efficiency**: Bounded memory usage regardless of dataset size
- **Field Access Time**: <15ns per field access

## Schema

### Input Schema
- `id` (int32): Unique identifier
- `name` (string): Name field
- Add your input fields here

### Output Schema  
- `id` (int32): Unique identifier
- `processed_name` (string): Processed name field
- `processed_at` (datetime): Processing timestamp
- Add your output fields here

## Development

### Project Structure

```
{pluginName}/
├── {(isMinimal ? $"{pluginName}Plugin.cs" : $@"{pluginName}Configuration.cs
├── {pluginName}Validator.cs
├── {pluginName}Plugin.cs
├── {pluginName}Processor.cs
├── {pluginName}Service.cs")}
├── config.yaml
├── README.md
└── Tests/
    └── {pluginName}PluginTests.cs
```

### Extending the Plugin

{(isMinimal ? @"To extend this minimal plugin:

1. **Add configuration properties** to the plugin class
2. **Implement schema definitions** in the CreateOutputSchema method
3. **Add processing logic** in the appropriate interface methods
4. **Update validation** in ValidateInputSchema method" : @"To extend this five-component plugin:

1. **Add configuration properties** to {pluginName}Configuration
2. **Update schema definitions** in CreateInputSchema/CreateOutputSchema methods  
3. **Add validation rules** to {pluginName}Validator
4. **Implement processing logic** in {pluginName}Service.ProcessRowAsync
5. **Add monitoring** in {pluginName}Plugin health checks")}

### Testing

The plugin includes comprehensive tests:

- Unit tests for each component
- Integration tests for data flow
- Performance tests for throughput validation
- Error handling tests

### Contributing

1. Follow FlowEngine coding standards
2. Maintain >95% test coverage
3. Ensure performance targets are met
4. Update documentation for any changes

## License

[Your License Here]

## Support

For support and questions, please refer to the FlowEngine documentation or contact [your contact information].
";
}