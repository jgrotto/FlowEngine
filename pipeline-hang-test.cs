using System;
using System.Threading.Tasks;
using DelimitedSource;
using FlowEngine.Abstractions.Data;
using FlowEngine.Abstractions.Plugins;
using FlowEngine.Core.Data;
using Microsoft.Extensions.Logging;

// Minimal test to isolate the hanging issue
public class PipelineHangTest
{
    public static async Task Main()
    {
        var loggerFactory = LoggerFactory.Create(builder => 
            builder.AddConsole().SetMinimumLevel(LogLevel.Debug));
        var logger = loggerFactory.CreateLogger<DelimitedSourcePlugin>();
        
        Console.WriteLine("Creating DelimitedSourcePlugin...");
        var plugin = new DelimitedSourcePlugin(logger);
        
        // Create test schema
        var schema = Schema.GetOrCreate(new[]
        {
            new ColumnDefinition { Name = "customer_id", DataType = typeof(string), Index = 0 },
            new ColumnDefinition { Name = "first_name", DataType = typeof(string), Index = 1 },
            new ColumnDefinition { Name = "last_name", DataType = typeof(string), Index = 2 }
        });
        
        var config = new DelimitedSourceConfiguration
        {
            FilePath = "C:\\source\\FlowEngine\\examples\\data\\customers.csv",
            Delimiter = ",",
            HasHeaders = true,
            Encoding = "UTF-8",
            ChunkSize = 50,
            OutputSchema = schema
        };
        
        Console.WriteLine("Initializing plugin...");
        var initResult = await plugin.InitializeAsync(config);
        Console.WriteLine($"Init result: {initResult.Success} - {initResult.Message}");
        
        Console.WriteLine("Starting plugin...");
        await plugin.StartAsync();
        Console.WriteLine("Plugin started successfully");
        
        Console.WriteLine("Testing ProduceAsync...");
        var chunkCount = 0;
        try
        {
            await foreach (var chunk in plugin.ProduceAsync())
            {
                chunkCount++;
                Console.WriteLine($"Received chunk {chunkCount} with {chunk.RowCount} rows");
                
                // Process only a few chunks for testing
                if (chunkCount >= 3)
                {
                    Console.WriteLine("Stopping after 3 chunks for test");
                    break;
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error in ProduceAsync: {ex.Message}");
            Console.WriteLine($"Stack trace: {ex.StackTrace}");
        }
        
        Console.WriteLine($"Test completed. Processed {chunkCount} chunks.");
    }
}