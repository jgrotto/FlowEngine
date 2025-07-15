using FlowEngine.Abstractions.Configuration;
using FlowEngine.Core.Configuration;
using FlowEngine.Core.Validation;
using Microsoft.Extensions.Logging;
using Xunit;

namespace FlowEngine.Core.Tests.Validation;

/// <summary>
/// Simple tests for PipelineValidator functionality focusing on basic validation logic.
/// </summary>
public class PipelineValidatorSimpleTests
{
    private readonly PipelineValidator _validator;
    private readonly PluginConfigurationProviderRegistry _providerRegistry;
    private readonly ILogger<PipelineValidator> _logger;

    public PipelineValidatorSimpleTests()
    {
        var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        _logger = loggerFactory.CreateLogger<PipelineValidator>();
        var registryLogger = loggerFactory.CreateLogger<PluginConfigurationProviderRegistry>();
        var schemaLogger = loggerFactory.CreateLogger<JsonSchemaValidator>();
        
        var schemaValidator = new JsonSchemaValidator(schemaLogger);
        _providerRegistry = new PluginConfigurationProviderRegistry(registryLogger, schemaValidator);
        _validator = new PipelineValidator(_providerRegistry, _logger);
    }

    [Fact]
    public async Task ValidatePipelineAsync_WithSimplePipelineYaml_ShouldLoadAndValidate()
    {
        // Arrange
        var yamlContent = await File.ReadAllTextAsync("/mnt/c/source/FlowEngine/examples/simple-pipeline.yaml");
        var pipeline = PipelineConfiguration.LoadFromYaml(yamlContent);

        // Act
        var result = await _validator.ValidatePipelineAsync(pipeline);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("Simple Pipeline Example", result.PipelineName);
        Assert.Equal(2, result.ValidatedPluginCount);
        Assert.Equal(1, result.ValidatedConnectionCount);
        
        // Since no providers are registered, should have warnings but basic structure should be valid
        _logger.LogInformation("Validation completed: IsValid={IsValid}, Errors={ErrorCount}, Warnings={WarningCount}", 
            result.IsValid, result.Errors.Count, result.Warnings.Count);
        
        // Log all results for inspection
        foreach (var error in result.Errors)
        {
            _logger.LogError("Error: {Error}", error);
        }
        
        foreach (var warning in result.Warnings)
        {
            _logger.LogWarning("Warning: {Warning}", warning);
        }
    }

    [Fact]
    public async Task ValidatePipelineAsync_WithNullPipeline_ShouldThrow()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(() => _validator.ValidatePipelineAsync(null!));
    }

    [Fact]
    public async Task ValidatePipelineAsync_WithEmptyPipeline_ShouldReturnErrors()
    {
        // Arrange
        var emptyPipeline = CreateEmptyPipeline();

        // Act
        var result = await _validator.ValidatePipelineAsync(emptyPipeline);

        // Assert
        Assert.NotNull(result);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("must contain at least one plugin"));
    }

    [Fact]
    public async Task ValidatePipelineAsync_Performance_ShouldCompleteQuickly()
    {
        // Arrange
        var yamlContent = await File.ReadAllTextAsync("/mnt/c/source/FlowEngine/examples/simple-pipeline.yaml");
        var pipeline = PipelineConfiguration.LoadFromYaml(yamlContent);
        
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        // Act
        var result = await _validator.ValidatePipelineAsync(pipeline);

        // Assert
        stopwatch.Stop();
        
        Assert.True(stopwatch.ElapsedMilliseconds < 100, 
            $"Pipeline validation took {stopwatch.ElapsedMilliseconds}ms, should be <100ms");
        
        Assert.NotNull(result);
        _logger.LogInformation("Pipeline validation completed in {ElapsedMs}ms", stopwatch.ElapsedMilliseconds);
    }

    [Fact]
    public async Task ValidatePipelineAsync_ValidatesBasicStructure()
    {
        // Arrange
        var yamlContent = await File.ReadAllTextAsync("/mnt/c/source/FlowEngine/examples/simple-pipeline.yaml");
        var pipeline = PipelineConfiguration.LoadFromYaml(yamlContent);

        // Act
        var result = await _validator.ValidatePipelineAsync(pipeline);

        // Assert
        Assert.NotNull(result);
        
        // Should validate that pipeline has source and sink
        var hasSourceSinkValidation = result.Errors.Count == 0 || 
            !result.Errors.Any(e => e.Contains("must contain at least one source plugin") || 
                                   e.Contains("must contain at least one sink plugin"));
                                   
        Assert.True(hasSourceSinkValidation, "Pipeline should pass basic source/sink structure validation");
        
        // Should validate plugin name uniqueness (simple-pipeline.yaml has unique names)
        var hasDuplicateNameErrors = result.Errors.Any(e => e.Contains("Duplicate plugin name"));
        Assert.False(hasDuplicateNameErrors, "Pipeline should not have duplicate plugin name errors");
        
        // Should validate connections reference existing plugins  
        var hasInvalidConnectionErrors = result.Errors.Any(e => e.Contains("Connection references unknown"));
        Assert.False(hasInvalidConnectionErrors, "Pipeline should not have invalid connection errors");
    }

    private IPipelineConfiguration CreateEmptyPipeline()
    {
        return new TestPipelineConfiguration
        {
            Name = "Empty Pipeline",
            Plugins = Array.Empty<IPluginDefinition>(),
            Connections = Array.Empty<IConnectionConfiguration>()
        };
    }
}

// Minimal test helper classes
internal class TestPipelineConfiguration : IPipelineConfiguration
{
    public string Name { get; init; } = string.Empty;
    public string Version { get; init; } = "1.0.0";
    public string? Description { get; init; }
    public IReadOnlyList<IPluginDefinition> Plugins { get; init; } = Array.Empty<IPluginDefinition>();
    public IReadOnlyList<IConnectionConfiguration> Connections { get; init; } = Array.Empty<IConnectionConfiguration>();
    public IPipelineSettings Settings { get; init; } = new TestPipelineSettings();
    public IReadOnlyDictionary<string, object>? Metadata { get; init; }

    public ConfigurationValidationResult Validate()
    {
        return ConfigurationValidationResult.Success();
    }
}

internal class TestPipelineSettings : IPipelineSettings
{
    public IChannelConfiguration? DefaultChannel { get; init; }
    public IResourceLimits? DefaultResourceLimits { get; init; }
    public IMonitoringConfiguration? Monitoring { get; init; }
    public IReadOnlyDictionary<string, object> Custom { get; init; } = new Dictionary<string, object>();
}