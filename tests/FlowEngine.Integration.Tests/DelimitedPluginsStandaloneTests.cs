using FlowEngine.Abstractions.Data;
using FlowEngine.Abstractions.Plugins;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Text;
using Xunit;
using Xunit.Abstractions;
using DelimitedSource;
using DelimitedSink;

namespace FlowEngine.Integration.Tests;

/// <summary>
/// Standalone integration tests for DelimitedSource and DelimitedSink plugins.
/// These tests focus on plugin functionality without requiring full Core integration.
/// </summary>
public class DelimitedPluginsStandaloneTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly ILogger<DelimitedPluginsStandaloneTests> _logger;
    private readonly string _testDataDirectory;
    private readonly List<string> _testFiles = new();

    public DelimitedPluginsStandaloneTests(ITestOutputHelper output)
    {
        _output = output;
        
        var loggerFactory = LoggerFactory.Create(builder => 
            builder.AddConsole().SetMinimumLevel(LogLevel.Information));
        _logger = loggerFactory.CreateLogger<DelimitedPluginsStandaloneTests>();
        
        _testDataDirectory = Path.Combine(Path.GetTempPath(), "FlowEngine_DelimitedPlugins_Standalone", Guid.NewGuid().ToString());
        Directory.CreateDirectory(_testDataDirectory);
    }

    [Fact]
    public async Task DelimitedSource_Should_InitializeSuccessfully()
    {
        // Arrange
        var sourceFile = CreateTestCsvFile("test_source.csv", 100);
        var config = new DelimitedSourceConfiguration
        {
            FilePath = sourceFile,
            Delimiter = ",",
            HasHeaders = true,
            Encoding = "UTF-8",
            ChunkSize = 50,
            OutputSchema = CreateSimpleSchema()
        };

        var plugin = new DelimitedSourcePlugin(_logger);

        // Act
        var stopwatch = Stopwatch.StartNew();
        var result = await plugin.InitializeAsync(config);
        stopwatch.Stop();

        // Assert
        Assert.True(result.Success, $"Initialization failed: {result.Message}");
        Assert.Equal(PluginState.Initialized, plugin.State);
        Assert.Equal("delimited-source", plugin.Id);
        
        _output.WriteLine($"✅ DelimitedSource initialized in {stopwatch.ElapsedMilliseconds}ms");
        _output.WriteLine($"   File: {sourceFile}");
        _output.WriteLine($"   State: {plugin.State}");
    }

    [Fact]
    public async Task DelimitedSink_Should_InitializeSuccessfully()
    {
        // Arrange
        var outputFile = GetTestFilePath("test_output.csv");
        var config = new DelimitedSinkConfiguration
        {
            FilePath = outputFile,
            Delimiter = ",",
            IncludeHeaders = true,
            Encoding = "UTF-8",
            BufferSize = 8192,
            FlushInterval = 100,
            CreateDirectories = true,
            OverwriteExisting = true,
            InputSchema = CreateSimpleSchema()
        };

        var plugin = new DelimitedSinkPlugin(_logger);

        // Act
        var stopwatch = Stopwatch.StartNew();
        var result = await plugin.InitializeAsync(config);
        stopwatch.Stop();

        // Assert
        Assert.True(result.Success, $"Initialization failed: {result.Message}");
        Assert.Equal(PluginState.Initialized, plugin.State);
        Assert.Equal("delimited-sink", plugin.Id);
        
        _output.WriteLine($"✅ DelimitedSink initialized in {stopwatch.ElapsedMilliseconds}ms");
        _output.WriteLine($"   Output: {outputFile}");
        _output.WriteLine($"   State: {plugin.State}");
    }

    [Fact]
    public async Task DelimitedPlugins_Should_StartAndStopSuccessfully()
    {
        // Arrange
        var sourceFile = CreateTestCsvFile("lifecycle_test.csv", 50);
        var outputFile = GetTestFilePath("lifecycle_output.csv");

        var sourceConfig = new DelimitedSourceConfiguration
        {
            FilePath = sourceFile,
            Delimiter = ",",
            HasHeaders = true,
            Encoding = "UTF-8",
            ChunkSize = 25,
            OutputSchema = CreateSimpleSchema()
        };

        var sinkConfig = new DelimitedSinkConfiguration
        {
            FilePath = outputFile,
            Delimiter = ",",
            IncludeHeaders = true,
            Encoding = "UTF-8",
            InputSchema = CreateSimpleSchema()
        };

        var sourcePlugin = new DelimitedSourcePlugin(_logger);
        var sinkPlugin = new DelimitedSinkPlugin(_logger);

        // Act & Assert - Initialize
        await sourcePlugin.InitializeAsync(sourceConfig);
        await sinkPlugin.InitializeAsync(sinkConfig);
        
        Assert.Equal(PluginState.Initialized, sourcePlugin.State);
        Assert.Equal(PluginState.Initialized, sinkPlugin.State);

        // Act & Assert - Start
        await sourcePlugin.StartAsync();
        await sinkPlugin.StartAsync();
        
        Assert.Equal(PluginState.Running, sourcePlugin.State);
        Assert.Equal(PluginState.Running, sinkPlugin.State);

        // Act & Assert - Stop
        await sourcePlugin.StopAsync();
        await sinkPlugin.StopAsync();
        
        Assert.Equal(PluginState.Stopped, sourcePlugin.State);
        Assert.Equal(PluginState.Stopped, sinkPlugin.State);

        _output.WriteLine("✅ Plugin lifecycle (Initialize → Start → Stop) completed successfully");
    }

    [Fact]
    public async Task DelimitedPlugins_Should_ValidateConfigurationCorrectly()
    {
        // Arrange - Valid configuration
        var validConfig = new DelimitedSourceConfiguration
        {
            FilePath = CreateTestCsvFile("valid_config.csv", 10),
            Delimiter = ",",
            HasHeaders = true,
            Encoding = "UTF-8",
            ChunkSize = 5,
            OutputSchema = CreateSimpleSchema()
        };

        // Arrange - Invalid configuration (non-existent file)
        var invalidConfig = new DelimitedSourceConfiguration
        {
            FilePath = "/non/existent/path/file.csv",
            Delimiter = ",",
            HasHeaders = true,
            Encoding = "UTF-8",
            ChunkSize = 5,
            OutputSchema = CreateSimpleSchema()
        };

        var plugin = new DelimitedSourcePlugin(_logger);

        // Act & Assert - Valid configuration
        var validResult = await plugin.InitializeAsync(validConfig);
        Assert.True(validResult.Success, "Valid configuration should succeed");

        // Reset plugin for second test
        plugin.Dispose();
        plugin = new DelimitedSourcePlugin(_logger);

        // Act & Assert - Invalid configuration
        var invalidResult = await plugin.InitializeAsync(invalidConfig);
        Assert.False(invalidResult.Success, "Invalid configuration should fail");
        Assert.Contains("not found", invalidResult.Message, StringComparison.OrdinalIgnoreCase);

        _output.WriteLine("✅ Configuration validation working correctly");
        _output.WriteLine($"   Valid config: {validResult.Success}");
        _output.WriteLine($"   Invalid config: {invalidResult.Success} (expected false)");
    }

    [Fact]
    public async Task DelimitedSink_Should_HandleColumnMappingConfiguration()
    {
        // Arrange
        var outputFile = GetTestFilePath("column_mapping_test.csv");
        var columnMappings = new List<ColumnMapping>
        {
            new() { SourceColumn = "Id", OutputColumn = "RecordId" },
            new() { SourceColumn = "Name", OutputColumn = "FullName" },
            new() { SourceColumn = "CreatedAt", OutputColumn = "DateCreated", Format = "yyyy-MM-dd" }
        };

        var config = new DelimitedSinkConfiguration
        {
            FilePath = outputFile,
            Delimiter = "\t", // Tab-separated
            IncludeHeaders = true,
            Encoding = "UTF-8",
            InputSchema = CreateSimpleSchema(),
            ColumnMappings = columnMappings
        };

        var plugin = new DelimitedSinkPlugin(_logger);

        // Act
        var result = await plugin.InitializeAsync(config);

        // Assert
        Assert.True(result.Success, $"Initialization with column mapping failed: {result.Message}");
        
        var configuration = (DelimitedSinkConfiguration)plugin.Configuration;
        Assert.NotNull(configuration.ColumnMappings);
        Assert.Equal(3, configuration.ColumnMappings.Count);
        Assert.Contains(configuration.ColumnMappings, m => m.SourceColumn == "Id" && m.OutputColumn == "RecordId");
        Assert.Contains(configuration.ColumnMappings, m => m.Format == "yyyy-MM-dd");

        _output.WriteLine("✅ Column mapping configuration validated successfully");
        _output.WriteLine($"   Mappings count: {configuration.ColumnMappings.Count}");
        _output.WriteLine($"   Delimiter: {configuration.Delimiter} (tab)");
    }

    [Fact]
    public async Task DelimitedPlugins_Should_HandleLargeFileConfiguration()
    {
        // Arrange - Simulate large file configuration
        var largeFileConfig = CreateTestCsvFile("large_file_test.csv", 10000); // 10K rows
        var outputFile = GetTestFilePath("large_output.csv");

        var sourceConfig = new DelimitedSourceConfiguration
        {
            FilePath = largeFileConfig,
            Delimiter = ",",
            HasHeaders = true,
            Encoding = "UTF-8",
            ChunkSize = 2000, // Large chunks for performance
            OutputSchema = CreateSimpleSchema()
        };

        var sinkConfig = new DelimitedSinkConfiguration
        {
            FilePath = outputFile,
            Delimiter = ",",
            IncludeHeaders = true,
            Encoding = "UTF-8",
            BufferSize = 65536, // 64KB buffer
            FlushInterval = 1000, // Flush every 1000 rows
            InputSchema = CreateSimpleSchema()
        };

        var sourcePlugin = new DelimitedSourcePlugin(_logger);
        var sinkPlugin = new DelimitedSinkPlugin(_logger);

        // Act
        var stopwatch = Stopwatch.StartNew();
        
        var sourceResult = await sourcePlugin.InitializeAsync(sourceConfig);
        var sinkResult = await sinkPlugin.InitializeAsync(sinkConfig);
        
        stopwatch.Stop();

        // Assert
        Assert.True(sourceResult.Success, "Large file source initialization should succeed");
        Assert.True(sinkResult.Success, "Large file sink initialization should succeed");
        
        var sourceFileSize = new FileInfo(largeFileConfig).Length;
        
        _output.WriteLine("✅ Large file configuration handled successfully");
        _output.WriteLine($"   Source file size: {sourceFileSize / 1024:N0} KB");
        _output.WriteLine($"   Initialization time: {stopwatch.ElapsedMilliseconds}ms");
        _output.WriteLine($"   Source chunk size: {sourceConfig.ChunkSize:N0} rows");
        _output.WriteLine($"   Sink buffer size: {sinkConfig.BufferSize / 1024:N0} KB");

        // Performance assertion
        Assert.True(stopwatch.ElapsedMilliseconds < 2000, 
            $"Large file initialization took {stopwatch.ElapsedMilliseconds}ms, should be under 2000ms");
    }

    [Fact]
    public async Task DelimitedPlugins_Should_HandleHealthChecks()
    {
        // Arrange
        var sourceFile = CreateTestCsvFile("health_check.csv", 100);
        var outputFile = GetTestFilePath("health_output.csv");

        var sourceConfig = new DelimitedSourceConfiguration
        {
            FilePath = sourceFile,
            Delimiter = ",",
            HasHeaders = true,
            Encoding = "UTF-8",
            OutputSchema = CreateSimpleSchema()
        };

        var sinkConfig = new DelimitedSinkConfiguration
        {
            FilePath = outputFile,
            Delimiter = ",",
            IncludeHeaders = true,
            Encoding = "UTF-8",
            InputSchema = CreateSimpleSchema()
        };

        var sourcePlugin = new DelimitedSourcePlugin(_logger);
        var sinkPlugin = new DelimitedSinkPlugin(_logger);

        await sourcePlugin.InitializeAsync(sourceConfig);
        await sinkPlugin.InitializeAsync(sinkConfig);
        await sourcePlugin.StartAsync();
        await sinkPlugin.StartAsync();

        // Act
        var sourceHealthChecks = await sourcePlugin.HealthCheckAsync();
        var sinkHealthChecks = await sinkPlugin.HealthCheckAsync();

        // Assert
        Assert.NotEmpty(sourceHealthChecks);
        Assert.NotEmpty(sinkHealthChecks);
        
        var sourceHealthList = sourceHealthChecks.ToList();
        var sinkHealthList = sinkHealthChecks.ToList();

        Assert.All(sourceHealthList, check => Assert.True(check.Passed, $"Source health check failed: {check.Details}"));
        Assert.All(sinkHealthList, check => Assert.True(check.Passed, $"Sink health check failed: {check.Details}"));

        _output.WriteLine("✅ Health checks completed successfully");
        _output.WriteLine($"   Source checks: {sourceHealthList.Count} (all passed)");
        _output.WriteLine($"   Sink checks: {sinkHealthList.Count} (all passed)");
    }

    private string CreateTestCsvFile(string fileName, int rowCount)
    {
        var filePath = GetTestFilePath(fileName);
        var csv = new StringBuilder();
        
        // Headers
        csv.AppendLine("Id,Name,Email,CreatedAt");
        
        // Data rows
        var baseDate = new DateTime(2024, 1, 1);
        for (int i = 1; i <= rowCount; i++)
        {
            var date = baseDate.AddDays(i % 365);
            csv.AppendLine($"{i},TestUser{i},user{i}@test.com,{date:yyyy-MM-ddTHH:mm:ssZ}");
        }
        
        File.WriteAllText(filePath, csv.ToString(), Encoding.UTF8);
        _testFiles.Add(filePath);
        
        return filePath;
    }

    private string GetTestFilePath(string fileName)
    {
        var filePath = Path.Combine(_testDataDirectory, fileName);
        _testFiles.Add(filePath);
        return filePath;
    }

    private ISchema CreateSimpleSchema()
    {
        return new TestSchema();
    }

    public void Dispose()
    {
        try
        {
            // Clean up test files
            foreach (var file in _testFiles)
            {
                if (File.Exists(file))
                    File.Delete(file);
            }

            if (Directory.Exists(_testDataDirectory))
                Directory.Delete(_testDataDirectory, true);
        }
        catch (Exception ex)
        {
            _output?.WriteLine($"Cleanup error: {ex.Message}");
        }
    }
}

/// <summary>
/// Simple test schema implementation for testing purposes.
/// </summary>
public class TestSchema : ISchema
{
    public string Name => "TestSchema";
    public int Version => 1;
    
    public IColumn[] Columns { get; } = new IColumn[]
    {
        new TestColumn { Name = "Id", DataType = typeof(int), Index = 0 },
        new TestColumn { Name = "Name", DataType = typeof(string), Index = 1 },
        new TestColumn { Name = "Email", DataType = typeof(string), Index = 2 },
        new TestColumn { Name = "CreatedAt", DataType = typeof(DateTime), Index = 3 }
    };

    public int GetIndex(string columnName)
    {
        var column = Columns.FirstOrDefault(c => c.Name == columnName);
        return column?.Index ?? -1;
    }

    public IColumn? GetColumn(string columnName)
    {
        return Columns.FirstOrDefault(c => c.Name == columnName);
    }

    public IColumn? GetColumn(int index)
    {
        return Columns.FirstOrDefault(c => c.Index == index);
    }

    public bool IsCompatibleWith(ISchema other)
    {
        return other.Name == Name && other.Version <= Version;
    }
}

/// <summary>
/// Simple test column implementation for testing purposes.
/// </summary>
public class TestColumn : IColumn
{
    public required string Name { get; init; }
    public required Type DataType { get; init; }
    public required int Index { get; init; }
    public bool IsNullable { get; init; } = true;
    public object? DefaultValue { get; init; } = null;
    public string? Description { get; init; } = null;
}