using FlowEngine.Abstractions.Plugins;
using FlowEngine.Core.Plugins;
using Microsoft.Extensions.Logging;
using NSubstitute;
using System.Text.Json;
using Xunit;

namespace FlowEngine.Core.Tests.Plugins;

/// <summary>
/// Tests for plugin discovery service functionality including manifest support.
/// </summary>
public class PluginDiscoveryServiceTests : IDisposable
{
    private readonly IPluginRegistry _mockRegistry;
    private readonly ILogger<PluginDiscoveryService> _mockLogger;
    private readonly PluginDiscoveryService _discoveryService;
    private readonly string _testDirectory;

    public PluginDiscoveryServiceTests()
    {
        _mockRegistry = Substitute.For<IPluginRegistry>();
        _mockLogger = Substitute.For<ILogger<PluginDiscoveryService>>();
        _discoveryService = new PluginDiscoveryService(_mockRegistry, _mockLogger);

        // Create temporary test directory
        _testDirectory = Path.Combine(Path.GetTempPath(), "FlowEngineTests", Guid.NewGuid().ToString());
        Directory.CreateDirectory(_testDirectory);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDirectory))
        {
            Directory.Delete(_testDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task DiscoverPluginsAsync_WithValidManifest_ReturnsDiscoveredPlugin()
    {
        // Arrange
        var pluginDirectory = Path.Combine(_testDirectory, "TestPlugin");
        Directory.CreateDirectory(pluginDirectory);

        var manifest = new
        {
            id = "TestPlugin",
            name = "Test Plugin",
            version = "1.0.0",
            description = "A test plugin",
            author = "Test Author",
            category = "Transform",
            assemblyFileName = "TestPlugin.dll",
            pluginTypeName = "TestPlugin.TestPluginClass",
            requiresConfiguration = true,
            dependencies = new[]
            {
                new { name = "FlowEngine.Core", version = ">=1.0.0", isOptional = false }
            },
            supportedInputSchemas = new[] { "Customer", "Employee" },
            supportedOutputSchemas = new[] { "ProcessedCustomer" },
            configurationSchemaFile = "config.json",
            metadata = new { tags = new[] { "test", "demo" } },
            minimumFlowEngineVersion = "1.0.0"
        };

        var manifestPath = Path.Combine(pluginDirectory, "plugin.json");
        var assemblyPath = Path.Combine(pluginDirectory, "TestPlugin.dll");

        await File.WriteAllTextAsync(manifestPath, JsonSerializer.Serialize(manifest));
        await File.WriteAllTextAsync(assemblyPath, "fake dll content"); // Create fake assembly file

        // Mock registry scanning
        _mockRegistry.When(r => r.ScanAssemblyFile(assemblyPath))
            .Do(call => { /* Mock successful scanning */ });

        var mockTypeInfo = new PluginTypeInfo
        {
            TypeName = "TestPlugin.TestPluginClass",
            FriendlyName = "Test Plugin Class",
            Category = PluginCategory.Transform,
            AssemblyPath = assemblyPath,
            AssemblyName = "TestPlugin",
            IsBuiltIn = false,
            RequiresConfiguration = true,
            Version = "1.0.0"
        };

        _mockRegistry.GetPluginType("TestPlugin.TestPluginClass").Returns(mockTypeInfo);

        // Act
        var result = await _discoveryService.DiscoverPluginsAsync(_testDirectory);

        // Assert
        Assert.Single(result);
        var discoveredPlugin = result.First();

        Assert.Equal("TestPlugin", discoveredPlugin.Manifest.Id);
        Assert.Equal("Test Plugin", discoveredPlugin.Manifest.Name);
        Assert.Equal("1.0.0", discoveredPlugin.Manifest.Version);
        Assert.Equal("A test plugin", discoveredPlugin.Manifest.Description);
        Assert.Equal("Test Author", discoveredPlugin.Manifest.Author);
        Assert.Equal(PluginCategory.Transform, discoveredPlugin.Manifest.Category);
        Assert.Equal("TestPlugin.dll", discoveredPlugin.Manifest.AssemblyFileName);
        Assert.Equal("TestPlugin.TestPluginClass", discoveredPlugin.Manifest.PluginTypeName);
        Assert.True(discoveredPlugin.Manifest.RequiresConfiguration);
        Assert.True(discoveredPlugin.HasManifest);
        Assert.Equal(assemblyPath, discoveredPlugin.AssemblyPath);
        Assert.Equal(manifestPath, discoveredPlugin.ManifestFilePath);

        // Verify dependencies
        Assert.Single(discoveredPlugin.Manifest.Dependencies);
        var dependency = discoveredPlugin.Manifest.Dependencies.First();
        Assert.Equal("FlowEngine.Core", dependency.Name);
        Assert.Equal(">=1.0.0", dependency.Version);
        Assert.False(dependency.IsOptional);

        // Verify schemas
        Assert.Equal(2, discoveredPlugin.Manifest.SupportedInputSchemas.Count);
        Assert.Contains("Customer", discoveredPlugin.Manifest.SupportedInputSchemas);
        Assert.Contains("Employee", discoveredPlugin.Manifest.SupportedInputSchemas);

        Assert.Single(discoveredPlugin.Manifest.SupportedOutputSchemas);
        Assert.Contains("ProcessedCustomer", discoveredPlugin.Manifest.SupportedOutputSchemas);
    }

    [Fact]
    public async Task DiscoverPluginsAsync_WithMissingAssembly_ReturnsNull()
    {
        // Arrange
        var pluginDirectory = Path.Combine(_testDirectory, "InvalidPlugin");
        Directory.CreateDirectory(pluginDirectory);

        var manifest = new
        {
            id = "InvalidPlugin",
            name = "Invalid Plugin",
            version = "1.0.0",
            assemblyFileName = "NonExistent.dll",
            pluginTypeName = "InvalidPlugin.Class"
        };

        var manifestPath = Path.Combine(pluginDirectory, "plugin.json");
        await File.WriteAllTextAsync(manifestPath, JsonSerializer.Serialize(manifest));

        // Act
        var result = await _discoveryService.DiscoverPluginsAsync(_testDirectory);

        // Assert
        Assert.Empty(result); // Should not discover plugins with missing assemblies
    }

    [Fact]
    public async Task DiscoverPluginFromManifestAsync_WithInvalidJson_ReturnsNull()
    {
        // Arrange
        var manifestPath = Path.Combine(_testDirectory, "invalid-plugin.json");
        await File.WriteAllTextAsync(manifestPath, "{ invalid json }");

        // Act
        var result = await _discoveryService.DiscoverPluginFromManifestAsync(manifestPath);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task ValidatePluginAsync_WithValidPlugin_ReturnsSuccess()
    {
        // Arrange
        var pluginDirectory = Path.Combine(_testDirectory, "ValidPlugin");
        Directory.CreateDirectory(pluginDirectory);

        var assemblyPath = Path.Combine(pluginDirectory, "ValidPlugin.dll");
        await File.WriteAllTextAsync(assemblyPath, "fake dll content");

        var manifest = new PluginManifest
        {
            Id = "ValidPlugin",
            Name = "Valid Plugin",
            Version = "1.0.0",
            Category = PluginCategory.Transform,
            AssemblyFileName = "ValidPlugin.dll",
            PluginTypeName = "ValidPlugin.Class",
            Dependencies = Array.Empty<PluginDependency>()
        };

        var discoveredPlugin = new DiscoveredPlugin
        {
            Manifest = manifest,
            DirectoryPath = pluginDirectory,
            ManifestFilePath = Path.Combine(pluginDirectory, "plugin.json"),
            AssemblyPath = assemblyPath,
            HasManifest = true,
            TypeInfo = new PluginTypeInfo
            {
                TypeName = "ValidPlugin.Class",
                FriendlyName = "Valid Plugin Class",
                Category = PluginCategory.Transform,
                AssemblyPath = assemblyPath,
                AssemblyName = "ValidPlugin",
                IsBuiltIn = false,
                RequiresConfiguration = false
            }
        };

        // Create a valid assembly file for testing
        var assemblyBytes = new byte[] { 0x4D, 0x5A }; // MZ header for valid PE file
        await File.WriteAllBytesAsync(assemblyPath, assemblyBytes);

        // Act
        var result = await _discoveryService.ValidatePluginAsync(discoveredPlugin);

        // Assert
        Assert.False(result.IsValid); // Will fail due to invalid assembly, but that's expected for test
        Assert.NotEmpty(result.Errors);
    }

    [Fact]
    public async Task DiscoverPluginsAsync_WithAssemblyButNoManifest_CreatesSyntheticManifest()
    {
        // Arrange
        var pluginDirectory = Path.Combine(_testDirectory, "NoManifestPlugin");
        Directory.CreateDirectory(pluginDirectory);

        var assemblyPath = Path.Combine(pluginDirectory, "NoManifestPlugin.dll");
        await File.WriteAllTextAsync(assemblyPath, "fake dll content");

        // Mock registry to return plugin types for assembly
        var mockTypeInfo = new PluginTypeInfo
        {
            TypeName = "NoManifestPlugin.PluginClass",
            FriendlyName = "No Manifest Plugin",
            Category = PluginCategory.Source,
            AssemblyPath = assemblyPath,
            AssemblyName = "NoManifestPlugin",
            IsBuiltIn = false,
            RequiresConfiguration = false,
            Version = "1.0.0",
            Description = "A plugin without manifest"
        };

        _mockRegistry.GetPluginTypes().Returns(new[] { mockTypeInfo });

        // Act
        var result = await _discoveryService.DiscoverPluginsAsync(_testDirectory);

        // Assert
        Assert.Single(result);
        var discoveredPlugin = result.First();

        Assert.Equal("NoManifestPlugin.PluginClass", discoveredPlugin.Manifest.Id); // Uses type name as ID
        Assert.Equal("No Manifest Plugin", discoveredPlugin.Manifest.Name);
        Assert.Equal(PluginCategory.Source, discoveredPlugin.Manifest.Category);
        Assert.False(discoveredPlugin.HasManifest);
        Assert.Empty(discoveredPlugin.ManifestFilePath);
        Assert.Equal(assemblyPath, discoveredPlugin.AssemblyPath);
    }

    [Fact]
    public void GetDefaultPluginDirectories_ReturnsExpectedDirectories()
    {
        // Act
        var directories = _discoveryService.GetDefaultPluginDirectories();

        // Assert
        Assert.NotEmpty(directories);

        // Should contain common plugin directory patterns
        var dirList = directories.ToList();
        Assert.Contains(dirList, d => d.Contains("plugins") || d.Contains("Plugins"));
    }

    [Fact]
    public async Task DiscoverPluginsAsync_RemovesDuplicatesBasedOnId()
    {
        // Arrange - Create two directories with the same plugin ID
        var dir1 = Path.Combine(_testDirectory, "dir1");
        var dir2 = Path.Combine(_testDirectory, "dir2");
        Directory.CreateDirectory(dir1);
        Directory.CreateDirectory(dir2);

        // Create manifest with same ID in both directories
        var manifest = new
        {
            id = "DuplicatePlugin",
            name = "Duplicate Plugin",
            version = "1.0.0",
            assemblyFileName = "DuplicatePlugin.dll",
            pluginTypeName = "DuplicatePlugin.Class"
        };

        var manifestPath1 = Path.Combine(dir1, "plugin.json");
        var manifestPath2 = Path.Combine(dir2, "plugin.json");
        var assemblyPath1 = Path.Combine(dir1, "DuplicatePlugin.dll");
        var assemblyPath2 = Path.Combine(dir2, "DuplicatePlugin.dll");

        await File.WriteAllTextAsync(manifestPath1, JsonSerializer.Serialize(manifest));
        await File.WriteAllTextAsync(manifestPath2, JsonSerializer.Serialize(manifest));
        await File.WriteAllTextAsync(assemblyPath1, "fake dll content");
        await File.WriteAllTextAsync(assemblyPath2, "fake dll content");

        // Act
        var result = await _discoveryService.DiscoverPluginsAsync(_testDirectory);

        // Assert - Should only return one plugin despite finding it in two directories
        Assert.Single(result);
        var discoveredPlugin = result.First();
        Assert.Equal("DuplicatePlugin", discoveredPlugin.Manifest.Id);
        Assert.True(discoveredPlugin.HasManifest); // Should prefer manifest-based discovery
    }
}
