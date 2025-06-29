using FlowEngine.Core.Plugins.Examples;
using System;
using System.Threading;

Console.WriteLine("Testing DataGeneratorPlugin directly...");

var plugin = new DataGeneratorPlugin();

try
{
    // Initialize plugin
    var config = new Microsoft.Extensions.Configuration.ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["RowCount"] = "3",
            ["BatchSize"] = "3",
            ["DelayMs"] = "10"
        })
        .Build();
    
    await plugin.InitializeAsync(config);
    
    Console.WriteLine($"Plugin initialized. Output schema: {plugin.OutputSchema?.ColumnCount} columns");
    
    // Test data production
    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
    
    int chunkCount = 0;
    int totalRows = 0;
    
    await foreach (var chunk in plugin.ProduceAsync(cts.Token))
    {
        chunkCount++;
        totalRows += chunk.RowCount;
        
        Console.WriteLine($"Chunk {chunkCount}: {chunk.RowCount} rows");
        
        // Show first row of first chunk
        if (chunkCount == 1 && chunk.RowCount > 0)
        {
            var firstRow = chunk.Rows[0];
            Console.WriteLine($"Sample row: {firstRow[0]}, {firstRow[1]}, {firstRow[2]}");
        }
        
        using (chunk) { } // Dispose chunk
        
        if (totalRows >= 3) break; // Safety
    }
    
    Console.WriteLine($"Total: {chunkCount} chunks, {totalRows} rows");
}
catch (Exception ex)
{
    Console.WriteLine($"Error: {ex.Message}");
    Console.WriteLine($"Stack: {ex.StackTrace}");
}
finally
{
    await plugin.DisposeAsync();
}