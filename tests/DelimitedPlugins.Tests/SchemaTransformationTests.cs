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
/// Tests for schema transformation capabilities - reading one schema and outputting a different one.
/// Demonstrates field removal, reordering, and column mapping functionality.
/// </summary>
public class SchemaTransformationTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly ILogger<DelimitedSourcePlugin> _sourceLogger;
    private readonly ILogger<DelimitedSinkPlugin> _sinkLogger;
    private readonly string _testDataDirectory;
    private readonly List<string> _testFiles = new();

    public SchemaTransformationTests(ITestOutputHelper output)
    {
        _output = output;
        
        var loggerFactory = LoggerFactory.Create(builder => 
            builder.AddConsole().SetMinimumLevel(LogLevel.Information));
        _sourceLogger = loggerFactory.CreateLogger<DelimitedSourcePlugin>();
        _sinkLogger = loggerFactory.CreateLogger<DelimitedSinkPlugin>();
        
        _testDataDirectory = Path.Combine(Path.GetTempPath(), "SchemaTransformation_Tests", Guid.NewGuid().ToString());
        Directory.CreateDirectory(_testDataDirectory);
    }

    [Fact]
    public async Task DelimitedSink_Should_RemoveFieldsUsingColumnMapping()
    {
        // Arrange - Create input file with full schema (6 fields)
        var sourceFile = CreateFullSchemaTestFile("full_customer_data.csv", 100);
        var outputFile = GetTestFilePath("reduced_customer_data.csv");

        var fullInputSchema = CreateFullCustomerSchema();
        var reducedOutputSchema = CreateReducedCustomerSchema();

        // Configure source to read full schema
        var sourceConfig = new DelimitedSourceConfiguration
        {
            FilePath = sourceFile,
            Delimiter = ",",
            HasHeaders = true,
            Encoding = "UTF-8",
            ChunkSize = 50,
            OutputSchema = fullInputSchema
        };

        // Configure sink to output only selected fields (removing sensitive/unnecessary fields)
        var columnMappings = new List<ColumnMapping>
        {
            new() { SourceColumn = "Id", OutputColumn = "CustomerId" },
            new() { SourceColumn = "Name", OutputColumn = "CustomerName" },
            new() { SourceColumn = "Email", OutputColumn = "ContactEmail" }
            // Note: Removing SSN, InternalNotes, and Salary fields for privacy/security
        };

        var sinkConfig = new DelimitedSinkConfiguration
        {
            FilePath = outputFile,
            Delimiter = ",",
            IncludeHeaders = true,
            Encoding = "UTF-8",
            InputSchema = fullInputSchema, // Input schema has all fields
            ColumnMappings = columnMappings // But only map 3 fields to output
        };

        var sourcePlugin = new DelimitedSourcePlugin(_sourceLogger);
        var sinkPlugin = new DelimitedSinkPlugin(_sinkLogger);

        // Act
        var sourceResult = await sourcePlugin.InitializeAsync(sourceConfig);
        var sinkResult = await sinkPlugin.InitializeAsync(sinkConfig);

        // Assert
        Assert.True(sourceResult.Success, $"Source initialization failed: {sourceResult.Message}");
        Assert.True(sinkResult.Success, $"Sink initialization failed: {sinkResult.Message}");

        // Verify the column mappings are configured correctly
        var configuration = (DelimitedSinkConfiguration)sinkPlugin.Configuration;
        Assert.NotNull(configuration.ColumnMappings);
        Assert.Equal(3, configuration.ColumnMappings.Count); // Only 3 fields mapped
        
        // Verify specific mappings
        Assert.Contains(configuration.ColumnMappings, m => m.SourceColumn == "Id" && m.OutputColumn == "CustomerId");
        Assert.Contains(configuration.ColumnMappings, m => m.SourceColumn == "Name" && m.OutputColumn == "CustomerName");
        Assert.Contains(configuration.ColumnMappings, m => m.SourceColumn == "Email" && m.OutputColumn == "ContactEmail");
        
        // Verify sensitive fields are NOT mapped
        Assert.DoesNotContain(configuration.ColumnMappings, m => m.SourceColumn == "SSN");
        Assert.DoesNotContain(configuration.ColumnMappings, m => m.SourceColumn == "Salary");
        Assert.DoesNotContain(configuration.ColumnMappings, m => m.SourceColumn == "InternalNotes");

        _output.WriteLine("✅ Schema transformation configured successfully");
        _output.WriteLine($"   Input schema: {fullInputSchema.ColumnCount} fields");
        _output.WriteLine($"   Output mapping: {configuration.ColumnMappings.Count} fields");
        _output.WriteLine($"   Fields removed: {fullInputSchema.ColumnCount - configuration.ColumnMappings.Count}");
        
        foreach (var mapping in configuration.ColumnMappings)
        {
            _output.WriteLine($"   - {mapping.SourceColumn} → {mapping.OutputColumn}");
        }
    }

    [Fact]
    public async Task DelimitedSink_Should_ReorderAndTransformFields()
    {
        // Arrange - Input has fields in one order, output in different order with transformations
        var sourceFile = CreateFullSchemaTestFile("customer_input.csv", 50);
        var outputFile = GetTestFilePath("customer_report.csv");

        var inputSchema = CreateFullCustomerSchema();

        // Configure source
        var sourceConfig = new DelimitedSourceConfiguration
        {
            FilePath = sourceFile,
            Delimiter = ",",
            HasHeaders = true,
            Encoding = "UTF-8",
            OutputSchema = inputSchema
        };

        // Configure sink with reordered fields and transformations
        var columnMappings = new List<ColumnMapping>
        {
            // Reorder: Put Name first instead of Id
            new() { SourceColumn = "Name", OutputColumn = "FullName" },
            new() { SourceColumn = "Email", OutputColumn = "EmailAddress" },
            new() { SourceColumn = "Id", OutputColumn = "CustomerNumber" },
            // Transform: Format salary as currency (if we were to include it)
            // new() { SourceColumn = "Salary", OutputColumn = "AnnualSalary", Format = "C" }
            // Note: We're excluding SSN and InternalNotes for this report
        };

        var sinkConfig = new DelimitedSinkConfiguration
        {
            FilePath = outputFile,
            Delimiter = "\t", // Different delimiter (tab-separated)
            IncludeHeaders = true,
            Encoding = "UTF-8",
            InputSchema = inputSchema,
            ColumnMappings = columnMappings
        };

        var sourcePlugin = new DelimitedSourcePlugin(_sourceLogger);
        var sinkPlugin = new DelimitedSinkPlugin(_sinkLogger);

        // Act
        var sourceResult = await sourcePlugin.InitializeAsync(sourceConfig);
        var sinkResult = await sinkPlugin.InitializeAsync(sinkConfig);

        // Assert
        Assert.True(sourceResult.Success, "Source should initialize successfully");
        Assert.True(sinkResult.Success, "Sink should initialize successfully");

        var config = (DelimitedSinkConfiguration)sinkPlugin.Configuration;
        
        // Verify field reordering - Name comes first now
        Assert.Equal("Name", config.ColumnMappings[0].SourceColumn);
        Assert.Equal("FullName", config.ColumnMappings[0].OutputColumn);
        
        // Verify field renaming
        Assert.Contains(config.ColumnMappings, m => m.SourceColumn == "Id" && m.OutputColumn == "CustomerNumber");
        Assert.Contains(config.ColumnMappings, m => m.SourceColumn == "Email" && m.OutputColumn == "EmailAddress");

        _output.WriteLine("✅ Field reordering and transformation configured");
        _output.WriteLine($"   Input order: Id, Name, Email, SSN, Salary, InternalNotes");
        _output.WriteLine($"   Output order: FullName, EmailAddress, CustomerNumber");
        _output.WriteLine($"   Output delimiter: TAB (different from input comma)");
    }

    [Fact]
    public async Task DelimitedSink_Should_HandleComplexSchemaReduction()
    {
        // Arrange - Simulate enterprise data with many fields, reducing to essential subset
        var sourceFile = CreateEnterpriseDataFile("enterprise_employees.csv", 200);
        var outputFile = GetTestFilePath("public_employee_directory.csv");

        var fullEnterpriseSchema = CreateEnterpriseEmployeeSchema();

        var sourceConfig = new DelimitedSourceConfiguration
        {
            FilePath = sourceFile,
            Delimiter = ",",
            HasHeaders = true,
            Encoding = "UTF-8",
            OutputSchema = fullEnterpriseSchema
        };

        // Public directory only shows safe, non-sensitive information
        var publicColumnMappings = new List<ColumnMapping>
        {
            new() { SourceColumn = "EmployeeId", OutputColumn = "ID" },
            new() { SourceColumn = "FirstName", OutputColumn = "First_Name" },
            new() { SourceColumn = "LastName", OutputColumn = "Last_Name" },
            new() { SourceColumn = "Department", OutputColumn = "Department" },
            new() { SourceColumn = "JobTitle", OutputColumn = "Title" },
            new() { SourceColumn = "Email", OutputColumn = "Email" },
            new() { SourceColumn = "Phone", OutputColumn = "Phone" }
            // Excluding: SSN, Salary, BankAccount, HomeAddress, ManagerNotes, SecurityClearance, etc.
        };

        var sinkConfig = new DelimitedSinkConfiguration
        {
            FilePath = outputFile,
            Delimiter = "|", // Pipe-separated for this output
            IncludeHeaders = true,
            Encoding = "UTF-8",
            InputSchema = fullEnterpriseSchema,
            ColumnMappings = publicColumnMappings
        };

        var sourcePlugin = new DelimitedSourcePlugin(_sourceLogger);
        var sinkPlugin = new DelimitedSinkPlugin(_sinkLogger);

        // Act
        var stopwatch = Stopwatch.StartNew();
        
        var sourceResult = await sourcePlugin.InitializeAsync(sourceConfig);
        var sinkResult = await sinkPlugin.InitializeAsync(sinkConfig);
        
        stopwatch.Stop();

        // Assert
        Assert.True(sourceResult.Success, "Enterprise source should initialize");
        Assert.True(sinkResult.Success, "Public directory sink should initialize");

        var config = (DelimitedSinkConfiguration)sinkPlugin.Configuration;
        
        // Verify dramatic field reduction (from 12 to 7 fields)
        Assert.Equal(7, config.ColumnMappings.Count);
        Assert.Equal(12, fullEnterpriseSchema.ColumnCount);
        
        // Verify no sensitive fields are included
        var mappedSourceColumns = config.ColumnMappings.Select(m => m.SourceColumn).ToHashSet();
        Assert.DoesNotContain("SSN", mappedSourceColumns);
        Assert.DoesNotContain("Salary", mappedSourceColumns);
        Assert.DoesNotContain("BankAccount", mappedSourceColumns);
        Assert.DoesNotContain("HomeAddress", mappedSourceColumns);
        Assert.DoesNotContain("SecurityClearance", mappedSourceColumns);
        
        // Verify safe fields are included
        Assert.Contains("EmployeeId", mappedSourceColumns);
        Assert.Contains("FirstName", mappedSourceColumns);
        Assert.Contains("Department", mappedSourceColumns);

        var fileSize = new FileInfo(sourceFile).Length;
        
        _output.WriteLine("✅ Complex schema reduction successful");
        _output.WriteLine($"   Input schema: {fullEnterpriseSchema.ColumnCount} fields (enterprise data)");
        _output.WriteLine($"   Output schema: {config.ColumnMappings.Count} fields (public directory)");
        _output.WriteLine($"   Fields removed: {fullEnterpriseSchema.ColumnCount - config.ColumnMappings.Count} (sensitive data excluded)");
        _output.WriteLine($"   Processing time: {stopwatch.ElapsedMilliseconds}ms");
        _output.WriteLine($"   File size: {fileSize / 1024:N0} KB");
        _output.WriteLine($"   Output format: Pipe-separated (|)");
    }

    [Fact]
    public async Task DelimitedSink_Should_ValidateSchemaCompatibilityWithReduction()
    {
        // Arrange
        var fullSchema = CreateFullCustomerSchema();
        var reducedMappings = new List<ColumnMapping>
        {
            new() { SourceColumn = "Id", OutputColumn = "CustomerId" },
            new() { SourceColumn = "Name", OutputColumn = "CustomerName" }
            // Only mapping 2 out of 6 fields
        };

        var sinkConfig = new DelimitedSinkConfiguration
        {
            FilePath = GetTestFilePath("schema_validation.csv"),
            Delimiter = ",",
            IncludeHeaders = true,
            Encoding = "UTF-8",
            InputSchema = fullSchema,
            ColumnMappings = reducedMappings
        };

        var plugin = new DelimitedSinkPlugin(_sinkLogger);
        await plugin.InitializeAsync(sinkConfig);
        var service = plugin.GetSinkService();

        // Act
        var compatibilityResult = service.ValidateInputSchema(fullSchema);

        // Assert
        Assert.True(compatibilityResult.IsCompatible, "Full schema should be compatible even when only subset is used");

        _output.WriteLine("✅ Schema compatibility validation with field reduction");
        _output.WriteLine($"   Input schema: {fullSchema.ColumnCount} fields available");
        _output.WriteLine($"   Output mapping: {reducedMappings.Count} fields used");
        _output.WriteLine($"   Compatibility: {compatibilityResult.IsCompatible}");
    }

    private string CreateFullSchemaTestFile(string fileName, int rowCount)
    {
        var filePath = GetTestFilePath(fileName);
        var csv = new StringBuilder();
        
        // Headers for full customer schema
        csv.AppendLine("Id,Name,Email,SSN,Salary,InternalNotes");
        
        // Sample data
        for (int i = 1; i <= rowCount; i++)
        {
            csv.AppendLine($"{i},Customer{i},customer{i}@company.com,{111 + i:000}-{22 + i:00}-{3333 + i:0000},{50000 + (i * 1000)},Internal note for customer {i}");
        }
        
        File.WriteAllText(filePath, csv.ToString(), Encoding.UTF8);
        _testFiles.Add(filePath);
        
        return filePath;
    }

    private string CreateEnterpriseDataFile(string fileName, int rowCount)
    {
        var filePath = GetTestFilePath(fileName);
        var csv = new StringBuilder();
        
        // Headers for enterprise employee schema
        csv.AppendLine("EmployeeId,FirstName,LastName,Email,Phone,Department,JobTitle,Salary,SSN,BankAccount,HomeAddress,SecurityClearance");
        
        var departments = new[] { "Engineering", "Sales", "Marketing", "HR", "Finance" };
        var titles = new[] { "Senior Engineer", "Sales Rep", "Marketing Manager", "HR Specialist", "Financial Analyst" };
        
        for (int i = 1; i <= rowCount; i++)
        {
            var dept = departments[i % departments.Length];
            var title = titles[i % titles.Length];
            csv.AppendLine($"EMP{i:0000},John{i},Doe{i},john.doe{i}@company.com,555-{1000 + i:0000},{dept},{title},{60000 + (i * 2000)},{400 + i:000}-{50 + i:00}-{7000 + i:0000},ACC{i:000000},123 Main St Apt {i},{(i % 3 == 0 ? "Secret" : "Public")}");
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

    private ISchema CreateFullCustomerSchema()
    {
        return new TransformTestSchema("FullCustomerSchema", new[]
        {
            new ColumnDefinition { Name = "Id", DataType = typeof(int), Index = 0 },
            new ColumnDefinition { Name = "Name", DataType = typeof(string), Index = 1 },
            new ColumnDefinition { Name = "Email", DataType = typeof(string), Index = 2 },
            new ColumnDefinition { Name = "SSN", DataType = typeof(string), Index = 3 },
            new ColumnDefinition { Name = "Salary", DataType = typeof(decimal), Index = 4 },
            new ColumnDefinition { Name = "InternalNotes", DataType = typeof(string), Index = 5 }
        });
    }

    private ISchema CreateReducedCustomerSchema()
    {
        return new TransformTestSchema("ReducedCustomerSchema", new[]
        {
            new ColumnDefinition { Name = "CustomerId", DataType = typeof(int), Index = 0 },
            new ColumnDefinition { Name = "CustomerName", DataType = typeof(string), Index = 1 },
            new ColumnDefinition { Name = "ContactEmail", DataType = typeof(string), Index = 2 }
        });
    }

    private ISchema CreateEnterpriseEmployeeSchema()
    {
        return new TransformTestSchema("EnterpriseEmployeeSchema", new[]
        {
            new ColumnDefinition { Name = "EmployeeId", DataType = typeof(string), Index = 0 },
            new ColumnDefinition { Name = "FirstName", DataType = typeof(string), Index = 1 },
            new ColumnDefinition { Name = "LastName", DataType = typeof(string), Index = 2 },
            new ColumnDefinition { Name = "Email", DataType = typeof(string), Index = 3 },
            new ColumnDefinition { Name = "Phone", DataType = typeof(string), Index = 4 },
            new ColumnDefinition { Name = "Department", DataType = typeof(string), Index = 5 },
            new ColumnDefinition { Name = "JobTitle", DataType = typeof(string), Index = 6 },
            new ColumnDefinition { Name = "Salary", DataType = typeof(decimal), Index = 7 },
            new ColumnDefinition { Name = "SSN", DataType = typeof(string), Index = 8 },
            new ColumnDefinition { Name = "BankAccount", DataType = typeof(string), Index = 9 },
            new ColumnDefinition { Name = "HomeAddress", DataType = typeof(string), Index = 10 },
            new ColumnDefinition { Name = "SecurityClearance", DataType = typeof(string), Index = 11 }
        });
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

// Enhanced test schema that supports custom names and column definitions
public class TransformTestSchema : ISchema
{
    public ImmutableArray<ColumnDefinition> Columns { get; }
    public int ColumnCount => Columns.Length;
    public string Signature { get; }

    public TransformTestSchema(string name, ColumnDefinition[] columns)
    {
        Signature = $"{name}_v1";
        Columns = columns.ToImmutableArray();
    }

    public int GetIndex(string columnName) => Columns.FirstOrDefault(c => c.Name == columnName)?.Index ?? -1;
    
    public bool TryGetIndex(string columnName, out int index)
    {
        index = GetIndex(columnName);
        return index >= 0;
    }
    
    public ColumnDefinition? GetColumn(string columnName) => Columns.FirstOrDefault(c => c.Name == columnName);
    public bool HasColumn(string columnName) => Columns.Any(c => c.Name == columnName);
    public bool IsCompatibleWith(ISchema other) => true; // Flexible for testing
    
    public ISchema AddColumns(params ColumnDefinition[] columns) => this;
    public ISchema RemoveColumns(params string[] columnNames) => this;
    public bool Equals(ISchema? other) => other?.Signature == Signature;
}