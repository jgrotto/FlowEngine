using FlowEngine.Abstractions.Data;
using FlowEngine.Abstractions.Plugins;
using Microsoft.Extensions.Logging;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Text;
using Xunit;
using Xunit.Abstractions;
using DelimitedSource;
using DelimitedSink;

namespace DelimitedPlugins.Tests;

/// <summary>
/// Comprehensive tests for DelimitedSource and DelimitedSink plugins.
/// Tests plugin lifecycle, configuration validation, and performance characteristics.
/// </summary>
public class DelimitedPluginsTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly ILogger<DelimitedPluginsTests> _logger;
    private readonly ILogger<DelimitedSourcePlugin> _sourceLogger;
    private readonly ILogger<DelimitedSinkPlugin> _sinkLogger;
    private readonly string _testDataDirectory;
    private readonly List<string> _testFiles = new();

    public DelimitedPluginsTests(ITestOutputHelper output)
    {
        _output = output;
        
        var loggerFactory = LoggerFactory.Create(builder => 
            builder.AddConsole().SetMinimumLevel(LogLevel.Information));
        _logger = loggerFactory.CreateLogger<DelimitedPluginsTests>();
        _sourceLogger = loggerFactory.CreateLogger<DelimitedSourcePlugin>();
        _sinkLogger = loggerFactory.CreateLogger<DelimitedSinkPlugin>();
        
        _testDataDirectory = Path.Combine(Path.GetTempPath(), "DelimitedPlugins_Tests", Guid.NewGuid().ToString());
        Directory.CreateDirectory(_testDataDirectory);
    }

    [Fact]
    public async Task DelimitedSource_Should_InitializeWithValidConfiguration()
    {
        // Arrange
        var sourceFile = CreateTestCsvFile("init_test.csv", 100);
        var config = new DelimitedSourceConfiguration
        {
            FilePath = sourceFile,
            Delimiter = ",",
            HasHeaders = true,
            Encoding = "UTF-8",
            ChunkSize = 50,
            OutputSchema = CreateTestSchema()
        };

        var plugin = new DelimitedSourcePlugin(_sourceLogger);

        // Act
        var stopwatch = Stopwatch.StartNew();
        var result = await plugin.InitializeAsync(config);
        stopwatch.Stop();

        // Assert
        Assert.True(result.Success, $"Initialization failed: {result.Message}");
        Assert.Equal(PluginState.Initialized, plugin.State);
        Assert.Equal("delimited-source", plugin.Id);
        Assert.True(stopwatch.ElapsedMilliseconds < 1000, $"Initialization took {stopwatch.ElapsedMilliseconds}ms, should be under 1000ms");
        
        _output.WriteLine($"✅ DelimitedSource initialization: {stopwatch.ElapsedMilliseconds}ms");
    }

    [Fact]
    public async Task DelimitedSink_Should_InitializeWithValidConfiguration()
    {
        // Arrange
        var outputFile = GetTestFilePath("sink_init_test.csv");
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
            InputSchema = CreateTestSchema()
        };

        var plugin = new DelimitedSinkPlugin(_sinkLogger);

        // Act
        var stopwatch = Stopwatch.StartNew();
        var result = await plugin.InitializeAsync(config);
        stopwatch.Stop();

        // Assert
        Assert.True(result.Success, $"Initialization failed: {result.Message}");
        Assert.Equal(PluginState.Initialized, plugin.State);
        Assert.Equal("delimited-sink", plugin.Id);
        Assert.True(stopwatch.ElapsedMilliseconds < 1000, $"Initialization took {stopwatch.ElapsedMilliseconds}ms, should be under 1000ms");
        
        _output.WriteLine($"✅ DelimitedSink initialization: {stopwatch.ElapsedMilliseconds}ms");
    }

    [Fact]
    public async Task DelimitedSource_Should_RejectInvalidConfiguration()
    {
        // Arrange
        var invalidConfig = new DelimitedSourceConfiguration
        {
            FilePath = "/nonexistent/path/file.csv",
            Delimiter = ",",
            HasHeaders = true,
            Encoding = "UTF-8",
            ChunkSize = 50,
            OutputSchema = CreateTestSchema()
        };

        var plugin = new DelimitedSourcePlugin(_sourceLogger);

        // Act
        var result = await plugin.InitializeAsync(invalidConfig);

        // Assert
        Assert.False(result.Success, "Invalid configuration should be rejected");
        Assert.Contains("not found", result.Message, StringComparison.OrdinalIgnoreCase);
        
        _output.WriteLine($"✅ Invalid configuration properly rejected: {result.Message}");
    }

    [Fact]
    public async Task DelimitedSink_Should_CreateDirectoriesWhenConfigured()
    {
        // Arrange
        var outputDir = Path.Combine(_testDataDirectory, "nested", "directories");
        var outputFile = Path.Combine(outputDir, "auto_created.csv");
        
        var config = new DelimitedSinkConfiguration
        {
            FilePath = outputFile,
            Delimiter = ",",
            IncludeHeaders = true,
            Encoding = "UTF-8",
            CreateDirectories = true,
            OverwriteExisting = true,
            InputSchema = CreateTestSchema()
        };

        var plugin = new DelimitedSinkPlugin(_sinkLogger);

        // Act
        var result = await plugin.InitializeAsync(config);

        // Assert
        Assert.True(result.Success, $"Directory creation failed: {result.Message}");
        Assert.True(Directory.Exists(outputDir), "Output directory should be created");
        
        _testFiles.Add(outputFile);
        _output.WriteLine($"✅ Directories auto-created: {outputDir}");
    }

    [Fact]
    public async Task DelimitedPlugins_Should_CompleteFullLifecycle()
    {
        // Arrange
        var sourceFile = CreateTestCsvFile("lifecycle.csv", 50);
        var outputFile = GetTestFilePath("lifecycle_output.csv");

        var sourceConfig = new DelimitedSourceConfiguration
        {
            FilePath = sourceFile,
            Delimiter = ",",
            HasHeaders = true,
            Encoding = "UTF-8",
            ChunkSize = 25,
            OutputSchema = CreateTestSchema()
        };

        var sinkConfig = new DelimitedSinkConfiguration
        {
            FilePath = outputFile,
            Delimiter = ",",
            IncludeHeaders = true,
            Encoding = "UTF-8",
            InputSchema = CreateTestSchema()
        };

        var sourcePlugin = new DelimitedSourcePlugin(_sourceLogger);
        var sinkPlugin = new DelimitedSinkPlugin(_sinkLogger);

        // Act & Assert - Initialize
        var sourceInit = await sourcePlugin.InitializeAsync(sourceConfig);
        var sinkInit = await sinkPlugin.InitializeAsync(sinkConfig);
        
        Assert.True(sourceInit.Success && sinkInit.Success, "Both plugins should initialize");
        Assert.Equal(PluginState.Initialized, sourcePlugin.State);
        Assert.Equal(PluginState.Initialized, sinkPlugin.State);

        // Act & Assert - Start
        await sourcePlugin.StartAsync();
        await sinkPlugin.StartAsync();
        
        Assert.Equal(PluginState.Running, sourcePlugin.State);
        Assert.Equal(PluginState.Running, sinkPlugin.State);

        // Act & Assert - Health checks
        var sourceHealth = await sourcePlugin.HealthCheckAsync();
        var sinkHealth = await sinkPlugin.HealthCheckAsync();
        
        Assert.NotEmpty(sourceHealth);
        Assert.NotEmpty(sinkHealth);
        Assert.All(sourceHealth, hc => Assert.True(hc.Passed, $"Source health check failed: {hc.Details}"));
        Assert.All(sinkHealth, hc => Assert.True(hc.Passed, $"Sink health check failed: {hc.Details}"));

        // Act & Assert - Stop
        await sourcePlugin.StopAsync();
        await sinkPlugin.StopAsync();
        
        Assert.Equal(PluginState.Stopped, sourcePlugin.State);
        Assert.Equal(PluginState.Stopped, sinkPlugin.State);

        _output.WriteLine("✅ Full plugin lifecycle (Initialize → Start → Health Check → Stop) completed");
    }

    [Fact]
    public async Task DelimitedSink_Should_HandleColumnMappings()
    {
        // Arrange
        var outputFile = GetTestFilePath("column_mapping.tsv");
        var columnMappings = new List<ColumnMapping>
        {
            new() { SourceColumn = "Id", OutputColumn = "RecordId" },
            new() { SourceColumn = "Name", OutputColumn = "FullName" },
            new() { SourceColumn = "Email", OutputColumn = "EmailAddress" },
            new() { SourceColumn = "CreatedAt", OutputColumn = "DateCreated", Format = "yyyy-MM-dd" }
        };

        var config = new DelimitedSinkConfiguration
        {
            FilePath = outputFile,
            Delimiter = "\t",
            IncludeHeaders = true,
            Encoding = "UTF-8",
            InputSchema = CreateTestSchema(),
            ColumnMappings = columnMappings
        };

        var plugin = new DelimitedSinkPlugin(_sinkLogger);

        // Act
        var result = await plugin.InitializeAsync(config);

        // Assert
        Assert.True(result.Success, "Column mapping configuration should succeed");
        
        var configuration = (DelimitedSinkConfiguration)plugin.Configuration;
        Assert.NotNull(configuration.ColumnMappings);
        Assert.Equal(4, configuration.ColumnMappings.Count);
        
        var idMapping = configuration.ColumnMappings.FirstOrDefault(m => m.SourceColumn == "Id");
        Assert.NotNull(idMapping);
        Assert.Equal("RecordId", idMapping.OutputColumn);
        
        var dateMapping = configuration.ColumnMappings.FirstOrDefault(m => m.SourceColumn == "CreatedAt");
        Assert.NotNull(dateMapping);
        Assert.Equal("yyyy-MM-dd", dateMapping.Format);

        _output.WriteLine($"✅ Column mappings configured: {configuration.ColumnMappings.Count} mappings");
    }

    [Fact]
    public async Task DelimitedPlugins_Should_HandleLargeDatasetConfiguration()
    {
        // Arrange - Large dataset simulation
        var rowCount = 25000; // 25K rows for performance testing
        var sourceFile = CreateTestCsvFile("large_dataset.csv", rowCount);
        var outputFile = GetTestFilePath("large_output.csv");

        var sourceConfig = new DelimitedSourceConfiguration
        {
            FilePath = sourceFile,
            Delimiter = ",",
            HasHeaders = true,
            Encoding = "UTF-8",
            ChunkSize = 5000, // Large chunks
            OutputSchema = CreateTestSchema()
        };

        var sinkConfig = new DelimitedSinkConfiguration
        {
            FilePath = outputFile,
            Delimiter = ",",
            IncludeHeaders = true,
            Encoding = "UTF-8",
            BufferSize = 65536, // 64KB buffer
            FlushInterval = 2500, // Flush every 2500 rows
            InputSchema = CreateTestSchema()
        };

        var sourcePlugin = new DelimitedSourcePlugin(_sourceLogger);
        var sinkPlugin = new DelimitedSinkPlugin(_sinkLogger);

        // Act
        var stopwatch = Stopwatch.StartNew();
        
        var sourceResult = await sourcePlugin.InitializeAsync(sourceConfig);
        var sinkResult = await sinkPlugin.InitializeAsync(sinkConfig);
        
        stopwatch.Stop();

        // Assert
        Assert.True(sourceResult.Success, "Large dataset source should initialize");
        Assert.True(sinkResult.Success, "Large dataset sink should initialize");
        
        var sourceFileSize = new FileInfo(sourceFile).Length;
        var initTime = stopwatch.ElapsedMilliseconds;
        
        // Performance assertions for large datasets
        Assert.True(initTime < 3000, $"Large dataset initialization took {initTime}ms, should be under 3000ms");
        
        _output.WriteLine($"✅ Large dataset handling validated");
        _output.WriteLine($"   Rows: {rowCount:N0}");
        _output.WriteLine($"   File size: {sourceFileSize / 1024:N0} KB");
        _output.WriteLine($"   Initialization: {initTime}ms");
        _output.WriteLine($"   Source chunk size: {sourceConfig.ChunkSize:N0}");
        _output.WriteLine($"   Sink buffer: {sinkConfig.BufferSize / 1024:N0} KB");
    }

    [Fact]
    public async Task DelimitedSink_Should_ValidateSchemaCompatibility()
    {
        // Arrange
        var compatibleSchema = CreateTestSchema();
        var incompatibleSchema = CreateIncompatibleSchema();

        var config = new DelimitedSinkConfiguration
        {
            FilePath = GetTestFilePath("schema_compat.csv"),
            Delimiter = ",",
            IncludeHeaders = true,
            Encoding = "UTF-8",
            InputSchema = compatibleSchema
        };

        var plugin = new DelimitedSinkPlugin(_sinkLogger);
        await plugin.InitializeAsync(config);
        var service = plugin.GetSinkService();

        // Act & Assert - Compatible schema
        var compatibleResult = service.ValidateInputSchema(compatibleSchema);
        Assert.True(compatibleResult.IsCompatible, "Compatible schema should pass validation");

        // Act & Assert - Incompatible schema
        var incompatibleResult = service.ValidateInputSchema(incompatibleSchema);
        Assert.False(incompatibleResult.IsCompatible, "Incompatible schema should fail validation");
        
        _output.WriteLine("✅ Schema compatibility validation working correctly");
        _output.WriteLine($"   Compatible: {compatibleResult.IsCompatible}");
        _output.WriteLine($"   Incompatible: {incompatibleResult.IsCompatible} (expected false)");
    }

    [Fact]
    public async Task DelimitedPlugins_Should_HandleHotSwapping()
    {
        // Arrange
        var sourceFile1 = CreateTestCsvFile("hotswap1.csv", 50);
        var sourceFile2 = CreateTestCsvFile("hotswap2.csv", 75);
        
        var initialConfig = new DelimitedSourceConfiguration
        {
            FilePath = sourceFile1,
            Delimiter = ",",
            HasHeaders = true,
            Encoding = "UTF-8",
            ChunkSize = 25,
            OutputSchema = CreateTestSchema()
        };

        var newConfig = new DelimitedSourceConfiguration
        {
            FilePath = sourceFile2,
            Delimiter = ",",
            HasHeaders = true,
            Encoding = "UTF-8",
            ChunkSize = 50,
            OutputSchema = CreateTestSchema()
        };

        var plugin = new DelimitedSourcePlugin(_sourceLogger);

        // Act
        await plugin.InitializeAsync(initialConfig);
        await plugin.StartAsync();
        
        var hotSwapResult = await plugin.HotSwapAsync(newConfig);

        // Assert
        Assert.True(hotSwapResult.Success, $"Hot swap failed: {hotSwapResult.Message}");
        Assert.True(hotSwapResult.SwapTime.TotalMilliseconds < 500, "Hot swap should be fast");
        
        var currentConfig = (DelimitedSourceConfiguration)plugin.Configuration;
        Assert.Equal(sourceFile2, currentConfig.FilePath);
        Assert.Equal(50, currentConfig.ChunkSize);

        _output.WriteLine($"✅ Hot swapping completed in {hotSwapResult.SwapTime.TotalMilliseconds:F2}ms");
    }

    [Fact]
    public async Task DelimitedPlugins_Should_ProvideDetailedHealthChecks()
    {
        // Arrange
        var sourceFile = CreateTestCsvFile("health_detailed.csv", 100);
        var outputFile = GetTestFilePath("health_output.csv");

        var sourceConfig = new DelimitedSourceConfiguration
        {
            FilePath = sourceFile,
            Delimiter = ",",
            HasHeaders = true,
            Encoding = "UTF-8",
            OutputSchema = CreateTestSchema()
        };

        var sinkConfig = new DelimitedSinkConfiguration
        {
            FilePath = outputFile,
            Delimiter = ",",
            IncludeHeaders = true,
            Encoding = "UTF-8",
            InputSchema = CreateTestSchema()
        };

        var sourcePlugin = new DelimitedSourcePlugin(_sourceLogger);
        var sinkPlugin = new DelimitedSinkPlugin(_sinkLogger);

        await sourcePlugin.InitializeAsync(sourceConfig);
        await sinkPlugin.InitializeAsync(sinkConfig);
        await sourcePlugin.StartAsync();
        await sinkPlugin.StartAsync();

        // Act
        var sourceHealthChecks = await sourcePlugin.HealthCheckAsync();
        var sinkHealthChecks = await sinkPlugin.HealthCheckAsync();

        // Assert
        var sourceChecks = sourceHealthChecks.ToList();
        var sinkChecks = sinkHealthChecks.ToList();

        Assert.NotEmpty(sourceChecks);
        Assert.NotEmpty(sinkChecks);
        
        // Verify all health checks pass
        Assert.All(sourceChecks, check => Assert.True(check.Passed, $"Source health check '{check.Name}' failed: {check.Details}"));
        Assert.All(sinkChecks, check => Assert.True(check.Passed, $"Sink health check '{check.Name}' failed: {check.Details}"));
        
        // Verify health check performance
        Assert.All(sourceChecks, check => Assert.True(check.ExecutionTime.TotalMilliseconds < 100, $"Health check '{check.Name}' took too long: {check.ExecutionTime.TotalMilliseconds}ms"));
        Assert.All(sinkChecks, check => Assert.True(check.ExecutionTime.TotalMilliseconds < 100, $"Health check '{check.Name}' took too long: {check.ExecutionTime.TotalMilliseconds}ms"));

        _output.WriteLine($"✅ Health checks completed successfully");
        _output.WriteLine($"   Source checks: {sourceChecks.Count} (all passed)");
        _output.WriteLine($"   Sink checks: {sinkChecks.Count} (all passed)");
        
        foreach (var check in sourceChecks.Concat(sinkChecks))
        {
            _output.WriteLine($"   - {check.Name}: {check.Details} ({check.ExecutionTime.TotalMilliseconds:F1}ms)");
        }
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

    private ISchema CreateTestSchema()
    {
        return new TestSchema();
    }

    private ISchema CreateIncompatibleSchema()
    {
        return new IncompatibleTestSchema();
    }

    public void Dispose()
    {
        try
        {
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

// Test schema implementations
public class TestSchema : ISchema
{
    public ImmutableArray<ColumnDefinition> Columns { get; }
    public int ColumnCount => Columns.Length;
    public string Signature => "TestSchema_v1";

    public TestSchema()
    {
        Columns = ImmutableArray.Create(
            new ColumnDefinition { Name = "Id", DataType = typeof(int), Index = 0 },
            new ColumnDefinition { Name = "Name", DataType = typeof(string), Index = 1 },
            new ColumnDefinition { Name = "Email", DataType = typeof(string), Index = 2 },
            new ColumnDefinition { Name = "CreatedAt", DataType = typeof(DateTime), Index = 3 }
        );
    }

    public int GetIndex(string columnName) => Columns.FirstOrDefault(c => c.Name == columnName)?.Index ?? -1;
    
    public bool TryGetIndex(string columnName, out int index)
    {
        index = GetIndex(columnName);
        return index >= 0;
    }
    
    public ColumnDefinition? GetColumn(string columnName) => Columns.FirstOrDefault(c => c.Name == columnName);
    public bool HasColumn(string columnName) => Columns.Any(c => c.Name == columnName);
    public bool IsCompatibleWith(ISchema other) => other.Signature == Signature;
    
    public ISchema AddColumns(params ColumnDefinition[] columns)
    {
        var newColumns = Columns.AddRange(columns);
        return new TestSchema(); // Simplified for test
    }
    
    public ISchema RemoveColumns(params string[] columnNames)
    {
        var filtered = Columns.Where(c => !columnNames.Contains(c.Name));
        return new TestSchema(); // Simplified for test
    }
    
    public bool Equals(ISchema? other) => other?.Signature == Signature;
}

public class IncompatibleTestSchema : ISchema
{
    public ImmutableArray<ColumnDefinition> Columns { get; }
    public int ColumnCount => Columns.Length;
    public string Signature => "IncompatibleSchema_v1";

    public IncompatibleTestSchema()
    {
        Columns = ImmutableArray.Create(
            new ColumnDefinition { Name = "ComplexData", DataType = typeof(object), Index = 0 },
            new ColumnDefinition { Name = "BinaryData", DataType = typeof(byte[]), Index = 1 }
        );
    }

    public int GetIndex(string columnName) => Columns.FirstOrDefault(c => c.Name == columnName)?.Index ?? -1;
    
    public bool TryGetIndex(string columnName, out int index)
    {
        index = GetIndex(columnName);
        return index >= 0;
    }
    
    public ColumnDefinition? GetColumn(string columnName) => Columns.FirstOrDefault(c => c.Name == columnName);
    public bool HasColumn(string columnName) => Columns.Any(c => c.Name == columnName);
    public bool IsCompatibleWith(ISchema other) => false; // Always incompatible
    
    public ISchema AddColumns(params ColumnDefinition[] columns) => this;
    public ISchema RemoveColumns(params string[] columnNames) => this;
    public bool Equals(ISchema? other) => other?.Signature == Signature;
}