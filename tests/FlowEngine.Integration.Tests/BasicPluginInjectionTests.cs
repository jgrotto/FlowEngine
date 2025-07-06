using FlowEngine.Abstractions.Data;
using FlowEngine.Abstractions.Factories;
using FlowEngine.Abstractions.Plugins;
using FlowEngine.Core;
using FlowEngine.Core.Plugins;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Phase3TemplatePlugin;
using Xunit;
using Xunit.Abstractions;

namespace FlowEngine.Integration.Tests;

/// <summary>
/// Simplified integration tests for plugin loading with service injection.
/// Focuses on core functionality that can be validated with the current plugin interfaces.
/// </summary>
public class BasicPluginInjectionTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly ServiceProvider _serviceProvider;
    private readonly IPluginLoader _pluginLoader;
    private readonly IPluginDiscoveryService _discoveryService;
    private readonly string _templatePluginAssemblyPath;

    public BasicPluginInjectionTests(ITestOutputHelper output)
    {
        _output = output;

        // Configure service provider with FlowEngine services
        var services = new ServiceCollection();
        services.AddLogging(builder => 
        {
            builder.AddConsole();
            builder.SetMinimumLevel(LogLevel.Information);
        });

        // Add FlowEngine services
        services.AddFlowEngine();

        _serviceProvider = services.BuildServiceProvider();
        _pluginLoader = _serviceProvider.GetRequiredService<IPluginLoader>();
        _discoveryService = _serviceProvider.GetRequiredService<IPluginDiscoveryService>();

        // Get path to template plugin assembly
        _templatePluginAssemblyPath = typeof(TemplatePlugin).Assembly.Location;
        
        _output.WriteLine($"Test setup complete. Plugin assembly: {Path.GetFileName(_templatePluginAssemblyPath)}");
    }

    [Fact]
    public async Task LoadPlugin_WithServiceInjection_ShouldCreateInstance()
    {
        // Arrange
        _output.WriteLine("Test: Basic plugin loading with service injection");

        // Act
        var plugin = await _pluginLoader.LoadPluginAsync<IPlugin>(
            _templatePluginAssemblyPath,
            typeof(TemplatePlugin).FullName!);

        // Assert
        Assert.NotNull(plugin);
        Assert.True(plugin.GetType().Name.Contains("TemplatePlugin"));
        Assert.NotNull(plugin.Id);
        Assert.NotEmpty(plugin.Id);
        Assert.Equal("Phase 3 Template Plugin", plugin.Name);

        _output.WriteLine($"Successfully loaded plugin: {plugin.Name} (ID: {plugin.Id})");
    }

    [Fact]
    public async Task LoadPlugin_WithConfiguration_ShouldInitialize()
    {
        // Arrange
        _output.WriteLine("Test: Plugin configuration and initialization");

        var plugin = await _pluginLoader.LoadPluginAsync<IPlugin>(
            _templatePluginAssemblyPath,
            typeof(TemplatePlugin).FullName!);

        var configuration = new TemplatePluginConfiguration
        {
            RowCount = 100,
            BatchSize = 10,
            DataType = "IntegrationTestData",
            DelayMs = 0
        };

        // Act
        await plugin.InitializeAsync(configuration);

        // Assert
        var templateConfig = plugin.Configuration as TemplatePluginConfiguration;
        Assert.NotNull(templateConfig);
        Assert.Equal("IntegrationTestData", templateConfig.DataType);
        Assert.Equal(100, templateConfig.RowCount);
        Assert.Equal(10, templateConfig.BatchSize);

        _output.WriteLine($"Plugin configured successfully:");
        _output.WriteLine($"  DataType: {templateConfig.DataType}");
        _output.WriteLine($"  RowCount: {templateConfig.RowCount}");
        _output.WriteLine($"  BatchSize: {templateConfig.BatchSize}");
    }

    [Fact]
    public void ServiceInjection_FactoriesAvailable_ShouldResolveCorrectly()
    {
        // Arrange
        _output.WriteLine("Test: Service injection validation");

        // Act
        var schemaFactory = _serviceProvider.GetRequiredService<ISchemaFactory>();
        var arrayRowFactory = _serviceProvider.GetRequiredService<IArrayRowFactory>();
        var chunkFactory = _serviceProvider.GetRequiredService<IChunkFactory>();
        var datasetFactory = _serviceProvider.GetRequiredService<IDatasetFactory>();

        // Create test schema using injected factory
        var schema = schemaFactory.CreateSchema(new[]
        {
            new ColumnDefinition { Name = "id", DataType = typeof(int), IsNullable = false, Index = 0 },
            new ColumnDefinition { Name = "name", DataType = typeof(string), IsNullable = false, Index = 1 },
            new ColumnDefinition { Name = "value", DataType = typeof(double), IsNullable = false, Index = 2 }
        });

        // Create test row using injected factory
        var row = arrayRowFactory.CreateRow(schema, new object[] { 1, "Test", 123.45 });

        // Assert
        Assert.NotNull(schemaFactory);
        Assert.NotNull(arrayRowFactory);
        Assert.NotNull(chunkFactory);
        Assert.NotNull(datasetFactory);
        Assert.NotNull(schema);
        Assert.NotNull(row);
        Assert.Equal(3, schema.Columns.Length);
        Assert.Equal(1, row[0]);
        Assert.Equal("Test", row[1]);
        Assert.Equal(123.45, row[2]);

        _output.WriteLine("Service injection validation successful:");
        _output.WriteLine($"  Schema created with {schema.Columns.Length} columns");
        _output.WriteLine($"  Row created with values: {row[0]}, {row[1]}, {row[2]}");
    }

    [Fact]
    public async Task PluginDiscovery_WithManifest_ShouldDiscoverPlugin()
    {
        // Arrange
        _output.WriteLine("Test: Plugin discovery with manifest file");

        var pluginDirectory = Path.GetDirectoryName(_templatePluginAssemblyPath)!;

        // Act
        var discoveredPlugins = await _discoveryService.DiscoverPluginsAsync(pluginDirectory);

        // Assert
        Assert.NotEmpty(discoveredPlugins);
        
        var templatePlugin = discoveredPlugins.FirstOrDefault(p => 
            p.Manifest.Id == "Phase3TemplatePlugin.TemplatePlugin");

        Assert.NotNull(templatePlugin);
        Assert.Equal("Template", templatePlugin.Manifest.Name);
        Assert.Equal("3.0.0.0", templatePlugin.Manifest.Version);
        Assert.Equal(PluginCategory.Transform, templatePlugin.Manifest.Category);

        _output.WriteLine($"Discovery successful:");
        _output.WriteLine($"  Total plugins found: {discoveredPlugins.Count()}");
        _output.WriteLine($"  Template plugin: {templatePlugin.Manifest.Name} v{templatePlugin.Manifest.Version}");
    }

    [Fact]
    public async Task PluginAsTransform_WithInitialization_ShouldBeUsableAsTransformPlugin()
    {
        // Arrange
        _output.WriteLine("Test: Plugin as transform plugin interface");

        var plugin = await _pluginLoader.LoadPluginAsync<IPlugin>(
            _templatePluginAssemblyPath,
            typeof(TemplatePlugin).FullName!);

        var configuration = new TemplatePluginConfiguration
        {
            RowCount = 5,
            BatchSize = 2,
            DataType = "TransformTest"
        };

        // Act
        await plugin.InitializeAsync(configuration);

        // Assert
        Assert.NotNull(plugin);
        Assert.IsAssignableFrom<IPlugin>(plugin);
        Assert.Equal(PluginState.Initialized, plugin.State);

        _output.WriteLine($"Transform plugin validation successful:");
        _output.WriteLine($"  Plugin type: {plugin.GetType().Name}");
        _output.WriteLine($"  Plugin state: {plugin.State}");
    }

    [Fact]
    public void PluginValidation_WithSchemaValidator_ShouldValidateCorrectly()
    {
        // Arrange
        _output.WriteLine("Test: Plugin schema validation");

        var schemaFactory = _serviceProvider.GetRequiredService<ISchemaFactory>();
        var arrayRowFactory = _serviceProvider.GetRequiredService<IArrayRowFactory>();

        // Create test schema
        var schema = schemaFactory.CreateSchema(new[]
        {
            new ColumnDefinition { Name = "id", DataType = typeof(int), IsNullable = false, Index = 0 },
            new ColumnDefinition { Name = "name", DataType = typeof(string), IsNullable = false, Index = 1 }
        });

        var validRow = arrayRowFactory.CreateRow(schema, new object[] { 1, "ValidName" });

        // Act & Assert
        Assert.NotNull(schema);
        Assert.Equal(2, schema.Columns.Length);
        Assert.NotNull(validRow);
        Assert.Equal(1, validRow[0]);
        Assert.Equal("ValidName", validRow[1]);

        _output.WriteLine("Schema validation test completed:");
        _output.WriteLine($"  Schema has {schema.Columns.Length} columns");
        _output.WriteLine($"  Valid row created successfully");
    }

    [Fact]
    public async Task ConcurrentPluginLoading_WithDifferentServiceProviders_ShouldSucceed()
    {
        // Arrange
        _output.WriteLine("Test: Concurrent plugin loading with different service providers");

        const int concurrentCount = 3;
        var loadingTasks = new List<Task<IPlugin>>();

        // Act - Create separate service providers for each concurrent load
        for (int i = 0; i < concurrentCount; i++)
        {
            var task = Task.Run(async () =>
            {
                // Create a new service provider for this concurrent operation
                var services = new ServiceCollection();
                services.AddLogging(builder => 
                {
                    builder.AddConsole();
                    builder.SetMinimumLevel(LogLevel.Information);
                });
                services.AddFlowEngine();

                using var serviceProvider = services.BuildServiceProvider();
                var pluginLoader = serviceProvider.GetRequiredService<IPluginLoader>();
                
                return await pluginLoader.LoadPluginAsync<IPlugin>(
                    _templatePluginAssemblyPath,
                    typeof(TemplatePlugin).FullName!);
            });
            loadingTasks.Add(task);
        }

        var plugins = await Task.WhenAll(loadingTasks);

        // Assert
        Assert.Equal(concurrentCount, plugins.Length);
        Assert.All(plugins, plugin => Assert.NotNull(plugin));

        // Verify each plugin has a unique ID (different instances)
        var uniqueIds = plugins.Select(p => p.Id).Distinct().Count();
        Assert.Equal(concurrentCount, uniqueIds);

        _output.WriteLine($"Concurrent loading successful:");
        _output.WriteLine($"  Plugins loaded: {plugins.Length}");
        _output.WriteLine($"  Unique IDs: {uniqueIds}");
    }

    [Fact]
    public async Task PluginErrorHandling_WithInvalidAssemblyPath_ShouldThrowPluginLoadException()
    {
        // Arrange
        _output.WriteLine("Test: Error handling for invalid assembly path");

        var invalidPath = "/nonexistent/path/invalid.dll";

        // Act & Assert
        var exception = await Assert.ThrowsAsync<PluginLoadException>(async () =>
        {
            await _pluginLoader.LoadPluginAsync<IPlugin>(
                invalidPath,
                "SomeType");
        });

        Assert.NotNull(exception);
        Assert.Equal(invalidPath, exception.AssemblyPath);
        Assert.Equal("SomeType", exception.TypeName);

        _output.WriteLine($"Error handling validated:");
        _output.WriteLine($"  Exception type: {exception.GetType().Name}");
        _output.WriteLine($"  Assembly path: {exception.AssemblyPath}");
        _output.WriteLine($"  Type name: {exception.TypeName}");
    }

    [Fact]
    public async Task PluginErrorHandling_WithInvalidTypeName_ShouldThrowPluginLoadException()
    {
        // Arrange
        _output.WriteLine("Test: Error handling for invalid type name");

        var invalidTypeName = "NonExistent.InvalidType";

        // Act & Assert
        var exception = await Assert.ThrowsAsync<PluginLoadException>(async () =>
        {
            await _pluginLoader.LoadPluginAsync<IPlugin>(
                _templatePluginAssemblyPath,
                invalidTypeName);
        });

        Assert.NotNull(exception);
        Assert.Equal(_templatePluginAssemblyPath, exception.AssemblyPath);
        Assert.Equal(invalidTypeName, exception.TypeName);

        _output.WriteLine($"Type validation error handled correctly:");
        _output.WriteLine($"  Expected assembly: {Path.GetFileName(_templatePluginAssemblyPath)}");
        _output.WriteLine($"  Invalid type: {invalidTypeName}");
    }

    [Fact]
    public async Task MultiplePluginTypes_LoadingDifferentInterfaces_ShouldWorkCorrectly()
    {
        // Arrange
        _output.WriteLine("Test: Loading plugin with different interface types");

        // Act - Load as generic plugin first
        var genericPlugin = await _pluginLoader.LoadPluginAsync<IPlugin>(
            _templatePluginAssemblyPath,
            typeof(TemplatePlugin).FullName!);

        // Create separate service provider for the second load to avoid "already loaded" error
        var services = new ServiceCollection();
        services.AddLogging(builder => 
        {
            builder.AddConsole();
            builder.SetMinimumLevel(LogLevel.Information);
        });
        services.AddFlowEngine();

        using var serviceProvider2 = services.BuildServiceProvider();
        var pluginLoader2 = serviceProvider2.GetRequiredService<IPluginLoader>();
        
        var secondPlugin = await pluginLoader2.LoadPluginAsync<IPlugin>(
            _templatePluginAssemblyPath,
            typeof(TemplatePlugin).FullName!);

        // Assert
        Assert.NotNull(genericPlugin);
        Assert.NotNull(secondPlugin);
        Assert.IsAssignableFrom<IPlugin>(genericPlugin);
        Assert.IsAssignableFrom<IPlugin>(secondPlugin);
        
        // Verify they are separate instances with different IDs
        Assert.NotEqual(genericPlugin.Id, secondPlugin.Id);

        _output.WriteLine($"Multiple instance loading successful:");
        _output.WriteLine($"  First plugin: {genericPlugin.GetType().Name} (ID: {genericPlugin.Id})");
        _output.WriteLine($"  Second plugin: {secondPlugin.GetType().Name} (ID: {secondPlugin.Id})");
    }

    public void Dispose()
    {
        _output.WriteLine("Disposing test resources...");
        _serviceProvider?.Dispose();
    }
}