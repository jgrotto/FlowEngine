using FlowEngine.Abstractions.Configuration;
using FlowEngine.Abstractions.Data;
using FlowEngine.Abstractions.Plugins;
using FlowEngine.Core.Configuration;
using Microsoft.Extensions.Logging;
using NSubstitute;
using System.Collections.Generic;
using System.Threading.Tasks;
using Xunit;
using ValidationResult = FlowEngine.Abstractions.ValidationResult;

namespace FlowEngine.Core.Tests.Configuration;

/// <summary>
/// Unit tests for the PluginConfigurationProviderRegistry.
/// Tests provider registration, discovery, and validation functionality.
/// </summary>
public class PluginConfigurationProviderRegistryTests
{
    private readonly ILogger<PluginConfigurationProviderRegistry> _mockLogger;
    private readonly JsonSchemaValidator _mockSchemaValidator;
    private readonly PluginConfigurationProviderRegistry _registry;

    public PluginConfigurationProviderRegistryTests()
    {
        _mockLogger = Substitute.For<ILogger<PluginConfigurationProviderRegistry>>();
        var mockSchemaLogger = Substitute.For<ILogger<JsonSchemaValidator>>();
        _mockSchemaValidator = Substitute.For<JsonSchemaValidator>(mockSchemaLogger);
        _registry = new PluginConfigurationProviderRegistry(_mockLogger, _mockSchemaValidator);
    }

    [Fact]
    public void RegisterProvider_ValidProvider_RegistersSuccessfully()
    {
        // Arrange
        var provider = CreateMockProvider("TestPlugin");
        _mockSchemaValidator.ValidateSchemaAsync(Arg.Any<string>())
            .Returns(ValidationResult.Success());

        // Act
        _registry.RegisterProvider(provider);

        // Assert
        var registeredProvider = _registry.GetProvider("TestPlugin");
        Assert.NotNull(registeredProvider);
        Assert.Equal("TestPlugin", registeredProvider.PluginTypeName);
    }

    [Fact]
    public void RegisterProvider_InvalidSchema_ThrowsException()
    {
        // Arrange
        var provider = CreateMockProvider("TestPlugin");
        _mockSchemaValidator.ValidateSchemaAsync(Arg.Any<string>())
            .Returns(ValidationResult.Failure("Invalid schema"));

        // Act & Assert
        var exception = Assert.Throws<InvalidOperationException>(() => _registry.RegisterProvider(provider));
        Assert.Contains("has invalid JSON schema", exception.Message);
    }

    [Fact]
    public void GetProvider_NonExistentPlugin_ReturnsNull()
    {
        // Act
        var provider = _registry.GetProvider("NonExistentPlugin");

        // Assert
        Assert.Null(provider);
    }

    [Fact]
    public async Task ValidatePluginConfigurationAsync_NoProvider_ReturnsFailure()
    {
        // Arrange
        var definition = CreateMockDefinition("NonExistentPlugin");

        // Act
        var result = await _registry.ValidatePluginConfigurationAsync("NonExistentPlugin", definition);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains("No configuration provider found", result.Errors[0]);
    }

    [Fact]
    public async Task ValidatePluginConfigurationAsync_ValidProvider_ReturnsSuccess()
    {
        // Arrange
        var provider = CreateMockProvider("TestPlugin");
        var definition = CreateMockDefinition("TestPlugin");
        
        _mockSchemaValidator.ValidateSchemaAsync(Arg.Any<string>())
            .Returns(ValidationResult.Success());
        _mockSchemaValidator.ValidateAsync(Arg.Any<IDictionary<string, object>>(), Arg.Any<string>(), Arg.Any<string>())
            .Returns(ValidationResult.Success());

        _registry.RegisterProvider(provider);

        // Act
        var result = await _registry.ValidatePluginConfigurationAsync("TestPlugin", definition);

        // Assert
        Assert.True(result.IsValid);
    }

    [Fact]
    public void GetAllProviders_ReturnsRegisteredProviders()
    {
        // Arrange
        var provider1 = CreateMockProvider("Plugin1");
        var provider2 = CreateMockProvider("Plugin2");
        
        _mockSchemaValidator.Setup(x => x.ValidateSchemaAsync(It.IsAny<string>()))
            .ReturnsAsync(ValidationResult.Success());

        _registry.RegisterProvider(provider1);
        _registry.RegisterProvider(provider2);

        // Act
        var providers = _registry.GetAllProviders();

        // Assert
        Assert.Equal(2, providers.Count());
        Assert.Contains(providers, p => p.Provider.PluginTypeName == "Plugin1");
        Assert.Contains(providers, p => p.Provider.PluginTypeName == "Plugin2");
    }

    [Fact]
    public void GetProvidersByCategory_FiltersCorrectly()
    {
        // Arrange
        var sourceProvider = CreateMockProvider("SourcePlugin", PluginCategory.Source);
        var transformProvider = CreateMockProvider("TransformPlugin", PluginCategory.Transform);
        
        _mockSchemaValidator.ValidateSchemaAsync(Arg.Any<string>())
            .Returns(ValidationResult.Success());

        _registry.RegisterProvider(sourceProvider);
        _registry.RegisterProvider(transformProvider);

        // Act
        var sourceProviders = _registry.GetProvidersByCategory(PluginCategory.Source);

        // Assert
        Assert.Single(sourceProviders);
        Assert.Equal("SourcePlugin", sourceProviders.First().Provider.PluginTypeName);
    }

    private IPluginConfigurationProvider CreateMockProvider(string pluginTypeName, PluginCategory category = PluginCategory.Transform)
    {
        var mock = Substitute.For<IPluginConfigurationProvider>();
        mock.PluginTypeName.Returns(pluginTypeName);
        mock.CanHandle(pluginTypeName).Returns(true);
        mock.GetConfigurationJsonSchema().Returns("{}");
        mock.ValidateConfiguration(Arg.Any<IPluginDefinition>())
            .Returns(FlowEngine.Abstractions.Plugins.ValidationResult.Success());
        mock.GetPluginMetadata().Returns(new PluginMetadata
        {
            Name = pluginTypeName,
            Category = category,
            Description = $"Test plugin {pluginTypeName}",
            Version = "1.0.0",
            Author = "Test"
        });
        return mock;
    }

    private IPluginDefinition CreateMockDefinition(string pluginType)
    {
        var mock = Substitute.For<IPluginDefinition>();
        mock.Type.Returns(pluginType);
        mock.Name.Returns($"{pluginType}Instance");
        mock.Configuration.Returns(new Dictionary<string, object>());
        return mock;
    }
}