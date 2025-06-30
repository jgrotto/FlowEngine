using FlowEngine.Core.Plugins.Examples;
using FlowEngine.Abstractions.Plugins;
using System;
using System.Threading;

Console.WriteLine("Testing schema flow debug...");

// 1. Create and initialize source plugin
var sourcePlugin = new DataGeneratorPlugin();
var sourceConfig = new Microsoft.Extensions.Configuration.ConfigurationBuilder()
    .AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["RowCount"] = "2",
        ["BatchSize"] = "2", 
        ["DelayMs"] = "10"
    })
    .Build();

await sourcePlugin.InitializeAsync(sourceConfig);
Console.WriteLine($"Source plugin output schema: {sourcePlugin.OutputSchema?.ColumnCount} columns");

// 2. Create and initialize transform plugin
var transformPlugin = new DataEnrichmentPlugin();
var transformConfig = new Microsoft.Extensions.Configuration.ConfigurationBuilder()
    .AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["AddSalaryBand"] = "true",
        ["AddFullName"] = "false",
        ["AddEmailDomain"] = "false", 
        ["AddAgeCategory"] = "false"
    })
    .Build();

await transformPlugin.InitializeAsync(transformConfig);
Console.WriteLine($"Transform plugin output schema before SetSchemaAsync: {transformPlugin.OutputSchema?.ColumnCount}");

// 3. Set schema on transform plugin
if (sourcePlugin.OutputSchema != null)
{
    Console.WriteLine($"Setting input schema on transform plugin with {sourcePlugin.OutputSchema.ColumnCount} columns");
    await transformPlugin.SetSchemaAsync(sourcePlugin.OutputSchema);
    Console.WriteLine($"Transform plugin output schema after SetSchemaAsync: {transformPlugin.OutputSchema?.ColumnCount}");
}
else
{
    Console.WriteLine("ERROR: Source plugin output schema is null!");
}

// 4. Test data flow
if (transformPlugin.OutputSchema != null)
{
    Console.WriteLine("Schema setup successful - would proceed with data flow");
}
else
{
    Console.WriteLine("ERROR: Transform plugin output schema is still null!");
}