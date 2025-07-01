using Microsoft.Extensions.Logging;

namespace FlowEngine.Cli.Services;

/// <summary>
/// Service for generating plugin scaffolding and templates.
/// Supports both five-component and minimal plugin architectures.
/// </summary>
public class PluginScaffoldingService
{
    private readonly ILogger<PluginScaffoldingService> _logger;

    /// <summary>
    /// Initializes a new instance of the PluginScaffoldingService class.
    /// </summary>
    /// <param name="logger">Logger for scaffolding operations</param>
    public PluginScaffoldingService(ILogger<PluginScaffoldingService> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Generates a five-component plugin template.
    /// </summary>
    /// <param name="pluginName">Name of the plugin</param>
    /// <param name="pluginType">Type of plugin (Source, Transform, Sink)</param>
    /// <param name="outputDirectory">Output directory for generated files</param>
    /// <returns>Task representing the generation operation</returns>
    public async Task GenerateFiveComponentPluginAsync(string pluginName, string pluginType, string outputDirectory)
    {
        _logger.LogInformation("Generating five-component plugin '{PluginName}' of type '{PluginType}'", pluginName, pluginType);

        var pluginDir = Path.Combine(outputDirectory, pluginName);
        var testDir = Path.Combine(pluginDir, "Tests");

        // Create directories
        Directory.CreateDirectory(pluginDir);
        Directory.CreateDirectory(testDir);

        var files = new Dictionary<string, string>
        {
            // Main plugin files
            [Path.Combine(pluginDir, $"{pluginName}.csproj")] = PluginTemplates.GetFiveComponentProjectTemplate(pluginName),
            [Path.Combine(pluginDir, $"{pluginName}Configuration.cs")] = PluginTemplates.GetConfigurationTemplate(pluginName, pluginType),
            [Path.Combine(pluginDir, $"{pluginName}Validator.cs")] = PluginTemplates.GetValidatorTemplate(pluginName, pluginType),
            [Path.Combine(pluginDir, $"{pluginName}Plugin.cs")] = PluginTemplates.GetPluginTemplate(pluginName, pluginType),
            [Path.Combine(pluginDir, $"{pluginName}Processor.cs")] = PluginTemplates.GetProcessorTemplate(pluginName, pluginType),
            [Path.Combine(pluginDir, $"{pluginName}Service.cs")] = PluginTemplates.GetServiceTemplate(pluginName, pluginType),
            [Path.Combine(pluginDir, "config.yaml")] = PluginTemplates.GetConfigurationYamlTemplate(pluginName, pluginType),
            [Path.Combine(pluginDir, "README.md")] = PluginTemplates.GetReadmeTemplate(pluginName, pluginType, isMinimal: false),

            // Test project
            [Path.Combine(testDir, $"{pluginName}.Tests.csproj")] = PluginTemplates.GetTestProjectTemplate(pluginName),
            [Path.Combine(testDir, $"{pluginName}PluginTests.cs")] = PluginTemplates.GetUnitTestTemplate(pluginName, pluginType)
        };

        await WriteFilesAsync(files);

        _logger.LogInformation("Five-component plugin '{PluginName}' generated successfully in '{OutputDirectory}'", 
            pluginName, pluginDir);
    }

    /// <summary>
    /// Generates a minimal plugin template using only Abstractions.
    /// </summary>
    /// <param name="pluginName">Name of the plugin</param>
    /// <param name="pluginType">Type of plugin (Source, Transform, Sink)</param>
    /// <param name="outputDirectory">Output directory for generated files</param>
    /// <returns>Task representing the generation operation</returns>
    public async Task GenerateMinimalPluginAsync(string pluginName, string pluginType, string outputDirectory)
    {
        _logger.LogInformation("Generating minimal plugin '{PluginName}' of type '{PluginType}'", pluginName, pluginType);

        var pluginDir = Path.Combine(outputDirectory, pluginName);
        var testDir = Path.Combine(pluginDir, "Tests");

        // Create directories
        Directory.CreateDirectory(pluginDir);
        Directory.CreateDirectory(testDir);

        var files = new Dictionary<string, string>
        {
            // Main plugin files
            [Path.Combine(pluginDir, $"{pluginName}.csproj")] = PluginTemplates.GetMinimalProjectTemplate(pluginName),
            [Path.Combine(pluginDir, $"{pluginName}Plugin.cs")] = PluginTemplates.GetMinimalPluginTemplate(pluginName, pluginType),
            [Path.Combine(pluginDir, "config.yaml")] = PluginTemplates.GetConfigurationYamlTemplate(pluginName, pluginType),
            [Path.Combine(pluginDir, "README.md")] = PluginTemplates.GetReadmeTemplate(pluginName, pluginType, isMinimal: true),

            // Test project
            [Path.Combine(testDir, $"{pluginName}.Tests.csproj")] = PluginTemplates.GetTestProjectTemplate(pluginName),
            [Path.Combine(testDir, $"{pluginName}PluginTests.cs")] = PluginTemplates.GetUnitTestTemplate(pluginName, pluginType)
        };

        await WriteFilesAsync(files);

        _logger.LogInformation("Minimal plugin '{PluginName}' generated successfully in '{OutputDirectory}'", 
            pluginName, pluginDir);
    }

    /// <summary>
    /// Generates a plugin from a custom template.
    /// </summary>
    /// <param name="templateName">Name of the template to use</param>
    /// <param name="pluginName">Name of the plugin</param>
    /// <param name="pluginType">Type of plugin (Source, Transform, Sink)</param>
    /// <param name="outputDirectory">Output directory for generated files</param>
    /// <returns>Task representing the generation operation</returns>
    public async Task GenerateFromTemplateAsync(string templateName, string pluginName, string pluginType, string outputDirectory)
    {
        _logger.LogInformation("Generating plugin '{PluginName}' from template '{TemplateName}'", pluginName, templateName);

        // Template-specific generation logic
        switch (templateName.ToLowerInvariant())
        {
            case "five-component":
            case "full":
                await GenerateFiveComponentPluginAsync(pluginName, pluginType, outputDirectory);
                break;

            case "minimal":
            case "simple":
                await GenerateMinimalPluginAsync(pluginName, pluginType, outputDirectory);
                break;

            case "source":
                await GenerateSourcePluginTemplateAsync(pluginName, outputDirectory);
                break;

            case "transform":
                await GenerateTransformPluginTemplateAsync(pluginName, outputDirectory);
                break;

            case "sink":
                await GenerateSinkPluginTemplateAsync(pluginName, outputDirectory);
                break;

            default:
                throw new ArgumentException($"Unknown template: {templateName}");
        }

        _logger.LogInformation("Plugin '{PluginName}' generated successfully from template '{TemplateName}'", 
            pluginName, templateName);
    }

    /// <summary>
    /// Generates a specialized source plugin template.
    /// </summary>
    /// <param name="pluginName">Name of the plugin</param>
    /// <param name="outputDirectory">Output directory for generated files</param>
    /// <returns>Task representing the generation operation</returns>
    private async Task GenerateSourcePluginTemplateAsync(string pluginName, string outputDirectory)
    {
        // Generate five-component plugin optimized for source operations
        await GenerateFiveComponentPluginAsync(pluginName, "Source", outputDirectory);

        // Add source-specific enhancements
        var pluginDir = Path.Combine(outputDirectory, pluginName);
        
        // Add source-specific configuration examples
        var sourceConfigExample = @"# Source Plugin Specific Configuration
source:
  connection_string: ""your-connection-string""
  batch_size: 10000
  poll_interval_seconds: 30
  enable_change_detection: true
  parallel_readers: 4

# Data generation settings
data_generation:
  enable_synthetic_data: false
  records_per_batch: 1000
  generation_rate_per_second: 50000";

        var sourceConfigPath = Path.Combine(pluginDir, "source-config-example.yaml");
        await File.WriteAllTextAsync(sourceConfigPath, sourceConfigExample);

        _logger.LogInformation("Source plugin template enhancements added");
    }

    /// <summary>
    /// Generates a specialized transform plugin template.
    /// </summary>
    /// <param name="pluginName">Name of the plugin</param>
    /// <param name="outputDirectory">Output directory for generated files</param>
    /// <returns>Task representing the generation operation</returns>
    private async Task GenerateTransformPluginTemplateAsync(string pluginName, string outputDirectory)
    {
        // Generate five-component plugin optimized for transform operations
        await GenerateFiveComponentPluginAsync(pluginName, "Transform", outputDirectory);

        // Add transform-specific enhancements
        var pluginDir = Path.Combine(outputDirectory, pluginName);
        
        // Add transform-specific configuration examples
        var transformConfigExample = @"# Transform Plugin Specific Configuration
transform:
  enable_field_mapping: true
  enable_data_validation: true
  enable_enrichment: true
  transformation_mode: ""batch""  # batch, streaming, hybrid

# Field mapping configuration
field_mapping:
  - source: ""old_field_name""
    target: ""new_field_name""
    transform: ""uppercase""
  - source: ""timestamp""
    target: ""processed_timestamp""
    transform: ""add_timezone""

# Validation rules
validation:
  required_fields: [""id"", ""name""]
  field_length_limits:
    name: 100
    description: 500
  
# Performance settings for transforms
performance:
  enable_parallel_transforms: true
  max_parallel_degree: 8
  enable_result_caching: true
  cache_size_mb: 100";

        var transformConfigPath = Path.Combine(pluginDir, "transform-config-example.yaml");
        await File.WriteAllTextAsync(transformConfigPath, transformConfigExample);

        _logger.LogInformation("Transform plugin template enhancements added");
    }

    /// <summary>
    /// Generates a specialized sink plugin template.
    /// </summary>
    /// <param name="pluginName">Name of the plugin</param>
    /// <param name="outputDirectory">Output directory for generated files</param>
    /// <returns>Task representing the generation operation</returns>
    private async Task GenerateSinkPluginTemplateAsync(string pluginName, string outputDirectory)
    {
        // Generate five-component plugin optimized for sink operations
        await GenerateFiveComponentPluginAsync(pluginName, "Sink", outputDirectory);

        // Add sink-specific enhancements
        var pluginDir = Path.Combine(outputDirectory, pluginName);
        
        // Add sink-specific configuration examples
        var sinkConfigExample = @"# Sink Plugin Specific Configuration
sink:
  connection_string: ""your-output-connection-string""
  batch_size: 5000
  flush_interval_seconds: 10
  enable_transactions: true
  retry_policy:
    max_retries: 3
    retry_delay_seconds: 5
    exponential_backoff: true

# Output format settings
output:
  format: ""json""  # json, csv, parquet, avro
  compression: ""gzip""
  include_metadata: true
  
# Error handling
error_handling:
  continue_on_error: true
  error_threshold_percentage: 5
  dead_letter_queue: ""errors/""
  
# Performance tuning for sinks
performance:
  enable_bulk_operations: true
  connection_pool_size: 10
  enable_write_caching: true
  write_cache_size_mb: 50";

        var sinkConfigPath = Path.Combine(pluginDir, "sink-config-example.yaml");
        await File.WriteAllTextAsync(sinkConfigPath, sinkConfigExample);

        _logger.LogInformation("Sink plugin template enhancements added");
    }

    /// <summary>
    /// Writes all files to disk asynchronously.
    /// </summary>
    /// <param name="files">Dictionary of file paths and contents</param>
    /// <returns>Task representing the write operation</returns>
    private async Task WriteFilesAsync(Dictionary<string, string> files)
    {
        var tasks = files.Select(async kvp =>
        {
            var directory = Path.GetDirectoryName(kvp.Key);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            await File.WriteAllTextAsync(kvp.Key, kvp.Value);
            _logger.LogDebug("Generated file: {FilePath}", kvp.Key);
        });

        await Task.WhenAll(tasks);
    }
}