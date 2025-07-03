using FlowEngine.Abstractions.Data;
using FlowEngine.Abstractions.Plugins;
using Microsoft.Extensions.Logging;
using System.Collections.Immutable;

namespace JavaScriptTransform;

/// <summary>
/// Simple test program to verify the working JavaScript plugin functionality.
/// </summary>
public static class TestProgram
{
    public static async Task Main(string[] args)
    {
        Console.WriteLine("=== FlowEngine JavaScriptTransform Plugin Test ===");
        
        // Create a simple logger
        using var loggerFactory = LoggerFactory.Create(builder => 
            builder.AddConsole().SetMinimumLevel(LogLevel.Information));
        var logger = loggerFactory.CreateLogger<WorkingJavaScriptPlugin>();

        try
        {
            // Test 1: Plugin creation and initialization
            Console.WriteLine("\n1. Testing plugin creation and initialization...");
            var plugin = new WorkingJavaScriptPlugin(logger);
            
            var config = new WorkingJavaScriptConfiguration
            {
                Script = "toUpperCase",
                PluginId = "test-plugin",
                Name = "Test Plugin",
                TimeoutMs = 5000,
                ContinueOnError = false
            };

            var initResult = await plugin.InitializeAsync(config);
            Console.WriteLine($"   Initialization: {(initResult.Success ? "SUCCESS" : "FAILED")} - {initResult.Message}");

            // Test 2: Plugin lifecycle
            Console.WriteLine("\n2. Testing plugin lifecycle...");
            await plugin.StartAsync();
            Console.WriteLine($"   Plugin State: {plugin.Status}");

            var health = await plugin.CheckHealthAsync();
            Console.WriteLine($"   Health Check: {(health.IsHealthy ? "HEALTHY" : "UNHEALTHY")} - {health.Message}");

            // Test 3: Data processing
            Console.WriteLine("\n3. Testing data processing...");
            
            // Create a simple schema and test data
            var schema = new TestSchema("TestData", new[]
            {
                new ColumnDefinition { Name = "id", DataType = typeof(int) },
                new ColumnDefinition { Name = "name", DataType = typeof(string) },
                new ColumnDefinition { Name = "email", DataType = typeof(string) }
            });

            var testRow = new WorkingArrayRow(schema, new object?[] { 1, "john doe", "john@example.com" });
            
            Console.WriteLine($"   Input Row: ID={testRow[0]}, Name={testRow[1]}, Email={testRow[2]}");
            
            var transformedRow = await plugin.TransformRowAsync(testRow);
            
            Console.WriteLine($"   Output Row: ID={transformedRow[0]}, Name={transformedRow[1]}, Email={transformedRow[2]}");

            // Test 4: Different transformations
            Console.WriteLine("\n4. Testing different JavaScript transformations...");
            
            // Test toLowerCase
            var lowerConfig = new WorkingJavaScriptConfiguration
            {
                Script = "toLowerCase",
                PluginId = "test-plugin-lower",
                Name = "Test Plugin Lower"
            };
            
            await plugin.HotSwapAsync(lowerConfig);
            var lowerResult = await plugin.TransformRowAsync(testRow);
            Console.WriteLine($"   toLowerCase Result: Name={lowerResult[1]}, Email={lowerResult[2]}");

            // Test trim
            var trimConfig = new WorkingJavaScriptConfiguration
            {
                Script = "trim",
                PluginId = "test-plugin-trim",
                Name = "Test Plugin Trim"
            };
            
            await plugin.HotSwapAsync(trimConfig);
            var testRowWithSpaces = new WorkingArrayRow(schema, new object?[] { 2, "  padded name  ", "  spaced@email.com  " });
            var trimResult = await plugin.TransformRowAsync(testRowWithSpaces);
            Console.WriteLine($"   trim Result: Name='{trimResult[1]}', Email='{trimResult[2]}'");

            // Test 5: Component access
            Console.WriteLine("\n5. Testing five-component architecture...");
            
            var processor = plugin.GetProcessor();
            Console.WriteLine($"   Processor State: {processor.State}");
            
            var service = plugin.GetService();
            var serviceHealth = await service.CheckHealthAsync();
            Console.WriteLine($"   Service Health: {serviceHealth.Status}");
            
            var validator = plugin.GetValidator();
            var validationResult = await validator.ValidateAsync(config);
            Console.WriteLine($"   Validation: {(validationResult.IsValid ? "VALID" : "INVALID")}");

            // Test 6: Metrics
            Console.WriteLine("\n6. Testing metrics collection...");
            var metrics = plugin.GetMetrics();
            Console.WriteLine($"   Processed Rows: {metrics.ProcessedRows}");
            Console.WriteLine($"   Error Count: {metrics.ErrorCount}");
            Console.WriteLine($"   Memory Usage: {metrics.MemoryUsage:N0} bytes");

            // Test 7: Cleanup
            Console.WriteLine("\n7. Testing cleanup...");
            await plugin.StopAsync();
            await plugin.DisposeAsync();
            Console.WriteLine($"   Final State: {plugin.Status}");

            Console.WriteLine("\n=== ALL TESTS COMPLETED SUCCESSFULLY! ===");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\n❌ TEST FAILED: {ex.Message}");
            Console.WriteLine($"Stack Trace: {ex.StackTrace}");
        }
    }
}

/// <summary>
/// Simple test schema implementation.
/// </summary>
internal sealed class TestSchema : ISchema
{
    public string Name { get; }
    public ImmutableArray<ColumnDefinition> Columns { get; }
    public int ColumnCount => Columns.Length;
    public string Signature => $"{Name}({string.Join(",", Columns.Select(c => $"{c.Name}:{c.DataType.Name}"))})";

    public TestSchema(string name, ColumnDefinition[] columns)
    {
        Name = name;
        Columns = columns.ToImmutableArray();
    }

    public int GetIndex(string columnName)
    {
        for (int i = 0; i < Columns.Length; i++)
        {
            if (Columns[i].Name == columnName)
                return i;
        }
        return -1;
    }

    public bool TryGetIndex(string columnName, out int index)
    {
        index = GetIndex(columnName);
        return index >= 0;
    }

    public ColumnDefinition? GetColumn(string columnName)
    {
        return Columns.FirstOrDefault(c => c.Name == columnName);
    }

    public bool HasColumn(string columnName)
    {
        return Columns.Any(c => c.Name == columnName);
    }

    public bool IsCompatibleWith(ISchema other)
    {
        return other != null && ColumnCount == other.ColumnCount;
    }

    public ISchema AddColumns(params ColumnDefinition[] columns)
    {
        return new TestSchema(Name, Columns.Concat(columns).ToArray());
    }

    public ISchema RemoveColumns(params string[] columnNames)
    {
        return new TestSchema(Name, Columns.Where(c => !columnNames.Contains(c.Name)).ToArray());
    }

    public bool Equals(ISchema? other) => other != null && IsCompatibleWith(other);
    public override bool Equals(object? obj) => obj is ISchema other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(Name, ColumnCount);
    public override string ToString() => Signature;
}