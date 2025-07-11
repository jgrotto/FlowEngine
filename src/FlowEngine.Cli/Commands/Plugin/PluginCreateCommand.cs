using System.Text;

namespace FlowEngine.Cli.Commands.Plugin;

/// <summary>
/// Command to create a new plugin project from a template.
/// </summary>
public class PluginCreateCommand : BaseCommand
{
    /// <inheritdoc />
    public override string Name => "create";

    /// <inheritdoc />
    public override string Description => "Create a new plugin project from a template";

    /// <inheritdoc />
    public override async Task<int> ExecuteAsync(string[] args)
    {
        if (IsHelpRequested(args))
        {
            ShowHelp();
            return 0;
        }

        var verbose = IsVerbose(args);

        // Parse arguments
        if (args.Length < 1)
        {
            WriteError("Plugin name is required");
            ShowHelp();
            return 1;
        }

        var pluginName = args[0];
        var outputDir = args.Length > 1 ? args[1] : pluginName;
        var template = GetTemplateOption(args);

        if (!IsValidPluginName(pluginName))
        {
            WriteError("Plugin name must be a valid C# identifier (letters, digits, and underscores only, starting with a letter)");
            return 1;
        }

        try
        {
            WriteInfo($"Creating new plugin: {pluginName}");
            if (verbose)
            {
                Console.WriteLine($"Template: {template}");
                Console.WriteLine($"Output directory: {Path.GetFullPath(outputDir)}");
            }

            var success = await CreatePluginAsync(pluginName, outputDir, template, verbose);
            if (success)
            {
                WriteSuccess($"Plugin '{pluginName}' created successfully!");
                Console.WriteLine();
                Console.WriteLine("Next steps:");
                Console.WriteLine($"  cd {outputDir}");
                Console.WriteLine("  dotnet build");
                Console.WriteLine($"  flowengine plugin validate {outputDir}");
                return 0;
            }
            else
            {
                WriteError("Failed to create plugin");
                return 1;
            }
        }
        catch (Exception ex)
        {
            WriteError($"Failed to create plugin: {ex.Message}");
            if (verbose)
            {
                Console.WriteLine($"Stack trace: {ex.StackTrace}");
            }
            return 1;
        }
    }

    /// <inheritdoc />
    public override void ShowHelp()
    {
        Console.WriteLine("Usage: flowengine plugin create <name> [output-dir] [options]");
        Console.WriteLine();
        Console.WriteLine("Arguments:");
        Console.WriteLine("  name        Plugin name (must be valid C# identifier)");
        Console.WriteLine("  output-dir  Output directory (defaults to plugin name)");
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine("  --template <type>   Template type (source, transform, destination) [default: source]");
        Console.WriteLine("  --verbose, -v       Verbose output");
        Console.WriteLine("  --help, -h          Show this help");
        Console.WriteLine();
        Console.WriteLine("Examples:");
        Console.WriteLine("  flowengine plugin create MySourcePlugin");
        Console.WriteLine("  flowengine plugin create MyTransform ./plugins/MyTransform --template transform");
        Console.WriteLine("  flowengine plugin create MyDestination --template destination --verbose");
    }

    private static string GetTemplateOption(string[] args)
    {
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == "--template" && i + 1 < args.Length)
            {
                var template = args[i + 1].ToLowerInvariant();
                return template switch
                {
                    "source" or "src" => "source",
                    "transform" or "trans" => "transform",
                    "destination" or "dest" or "sink" => "destination",
                    _ => "source"
                };
            }
        }
        return "source";
    }

    private static bool IsValidPluginName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        if (!char.IsLetter(name[0]) && name[0] != '_')
        {
            return false;
        }

        return name.All(c => char.IsLetterOrDigit(c) || c == '_');
    }

    private static async Task<bool> CreatePluginAsync(string pluginName, string outputDir, string template, bool verbose)
    {
        try
        {
            // Create output directory
            if (Directory.Exists(outputDir))
            {
                if (Directory.GetFiles(outputDir).Length > 0 || Directory.GetDirectories(outputDir).Length > 0)
                {
                    Console.WriteLine($"Directory '{outputDir}' already exists and is not empty. Continue? (y/N)");
                    var response = Console.ReadLine()?.Trim().ToLowerInvariant();
                    if (response != "y" && response != "yes")
                    {
                        Console.WriteLine("Operation cancelled.");
                        return false;
                    }
                }
            }
            else
            {
                Directory.CreateDirectory(outputDir);
                if (verbose)
                {
                    Console.WriteLine($"Created directory: {outputDir}");
                }
            }

            // Create project file
            var projectContent = GenerateProjectFile(pluginName);
            var projectPath = Path.Combine(outputDir, $"{pluginName}.csproj");
            await File.WriteAllTextAsync(projectPath, projectContent);
            if (verbose)
            {
                Console.WriteLine($"Created: {projectPath}");
            }

            // Create plugin class
            var pluginContent = GeneratePluginClass(pluginName, template);
            var pluginPath = Path.Combine(outputDir, $"{pluginName}.cs");
            await File.WriteAllTextAsync(pluginPath, pluginContent);
            if (verbose)
            {
                Console.WriteLine($"Created: {pluginPath}");
            }

            // Create plugin.json manifest
            var manifestContent = GeneratePluginManifest(pluginName, template);
            var manifestPath = Path.Combine(outputDir, "plugin.json");
            await File.WriteAllTextAsync(manifestPath, manifestContent);
            if (verbose)
            {
                Console.WriteLine($"Created: {manifestPath}");
            }

            // Create README
            var readmeContent = GenerateReadme(pluginName, template);
            var readmePath = Path.Combine(outputDir, "README.md");
            await File.WriteAllTextAsync(readmePath, readmeContent);
            if (verbose)
            {
                Console.WriteLine($"Created: {readmePath}");
            }

            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error creating plugin: {ex.Message}");
            return false;
        }
    }

    private static string GenerateProjectFile(string pluginName)
    {
        return $"""
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <GenerateDocumentationFile>true</GenerateDocumentationFile>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="FlowEngine.Abstractions" Version="1.0.0" />
  </ItemGroup>

  <ItemGroup>
    <None Include="plugin.json">
      <CopyToOutputDirectory>Always</CopyToOutputDirectory>
    </None>
  </ItemGroup>

</Project>
""";
    }

    private static string GeneratePluginClass(string pluginName, string template)
    {
        var baseInterface = template switch
        {
            "source" => "ISourcePlugin",
            "transform" => "ITransformPlugin",
            "destination" => "ISinkPlugin",
            _ => "ISourcePlugin"
        };

        var baseClass = "PluginBase";

        return $$"""
using FlowEngine.Abstractions.Data;
using FlowEngine.Abstractions.Plugins;

namespace {{pluginName}};

/// <summary>
/// {{template.Substring(0, 1).ToUpper() + template.Substring(1)}} plugin implementation.
/// </summary>
public sealed class {{pluginName}} : {{baseClass}}, {{baseInterface}}
{
    private ISchema? _outputSchema;

    /// <summary>
    /// Initializes a new instance of the {{pluginName}}.
    /// </summary>
    public {{pluginName}}()
    {
        Id = "{{pluginName.ToLowerInvariant()}}";
        Name = "{{pluginName}}";
        Version = "1.0.0";
        Description = "Generated {{template}} plugin";
        Category = "{{template}}";
    }

    /// <inheritdoc />
    public override async Task InitializeAsync(IPluginConfiguration configuration)
    {
        await base.InitializeAsync(configuration);
        
        // TODO: Initialize your plugin here
        // Access configuration values: configuration.GetValue<string>("key")
        
        // Define output schema for this plugin
        _outputSchema = CreateOutputSchema();
    }

    /// <inheritdoc />
    public override ISchema GetOutputSchema()
    {
        return _outputSchema ?? throw new InvalidOperationException("Plugin not initialized");
    }

{{(template == "source" ? GenerateSourceMethods() :
  template == "transform" ? GenerateTransformMethods() :
  GenerateDestinationMethods())}}

    /// <summary>
    /// Creates the output schema for this plugin.
    /// </summary>
    /// <returns>Output schema definition</returns>
    private static ISchema CreateOutputSchema()
    {
        // TODO: Define your output schema here
        var columns = new[]
        {
            new ColumnDefinition { Name = "Id", DataType = typeof(int), IsNullable = false },
            new ColumnDefinition { Name = "Name", DataType = typeof(string), IsNullable = false },
            new ColumnDefinition { Name = "Timestamp", DataType = typeof(DateTime), IsNullable = false }
        };
        
        return Schema.GetOrCreate(columns);
    }
}
""";
    }

    private static string GenerateSourceMethods()
    {
        return """
    /// <inheritdoc />
    public override async IAsyncEnumerable<IChunk> GenerateDataAsync(CancellationToken cancellationToken = default)
    {
        // TODO: Implement your data generation logic here
        // This is where you read from your data source (files, APIs, databases, etc.)
        
        var schema = GetOutputSchema();
        var sampleData = new object?[][]
        {
            new object?[] { 1, "Sample Item 1", DateTime.UtcNow },
            new object?[] { 2, "Sample Item 2", DateTime.UtcNow },
            new object?[] { 3, "Sample Item 3", DateTime.UtcNow }
        };
        
        foreach (var rowData in sampleData)
        {
            cancellationToken.ThrowIfCancellationRequested();
            
            var row = ArrayRowFactory.CreateRow(schema, rowData);
            var chunk = ChunkFactory.CreateChunk(new[] { row });
            
            yield return chunk;
            
            // Add small delay to simulate real data generation
            await Task.Delay(100, cancellationToken);
        }
    }
""";
    }

    private static string GenerateTransformMethods()
    {
        return """
    /// <inheritdoc />
    public override async Task<IChunk> TransformAsync(IChunk inputChunk, CancellationToken cancellationToken = default)
    {
        // TODO: Implement your transformation logic here
        // This is where you modify, filter, or enrich the data
        
        var outputSchema = GetOutputSchema();
        var transformedRows = new List<IArrayRow>();
        
        foreach (var row in inputChunk.Rows)
        {
            cancellationToken.ThrowIfCancellationRequested();
            
            // Example transformation: add a timestamp field
            var transformedData = new object?[row.FieldCount + 1];
            for (int i = 0; i < row.FieldCount; i++)
            {
                transformedData[i] = row.GetFieldValue(i);
            }
            transformedData[row.FieldCount] = DateTime.UtcNow; // Add timestamp
            
            var transformedRow = ArrayRowFactory.CreateRow(outputSchema, transformedData);
            transformedRows.Add(transformedRow);
        }
        
        return ChunkFactory.CreateChunk(transformedRows);
    }
""";
    }

    private static string GenerateDestinationMethods()
    {
        return """
    /// <inheritdoc />
    public override async Task ConsumeAsync(IChunk chunk, CancellationToken cancellationToken = default)
    {
        // TODO: Implement your data consumption logic here
        // This is where you write data to your destination (files, APIs, databases, etc.)
        
        foreach (var row in chunk.Rows)
        {
            cancellationToken.ThrowIfCancellationRequested();
            
            // Example: Write to console (replace with your destination logic)
            var values = new object?[row.FieldCount];
            for (int i = 0; i < row.FieldCount; i++)
            {
                values[i] = row.GetFieldValue(i);
            }
            
            Console.WriteLine($"Writing row: {string.Join(", ", values)}");
            
            // Add small delay to simulate real data writing
            await Task.Delay(10, cancellationToken);
        }
    }
""";
    }

    private static string GeneratePluginManifest(string pluginName, string template)
    {
        return $$"""
{
  "name": "{{pluginName}}",
  "version": "1.0.0",
  "description": "Generated {{template}} plugin",
  "category": "{{template}}",
  "author": "Your Name",
  "dependencies": [
    {
      "name": "FlowEngine.Abstractions",
      "version": ">=1.0.0"
    }
  ],
  "configuration": {
    "schema": {
      "type": "object",
      "properties": {
        "connectionString": {
          "type": "string",
          "description": "Connection string for data source"
        },
        "batchSize": {
          "type": "integer",
          "default": 1000,
          "minimum": 1,
          "maximum": 10000,
          "description": "Number of rows to process in each batch"
        }
      }
    }
  },
  "performance": {
    "targetThroughput": "1000 rows/sec",
    "memoryUsage": "low"
  }
}
""";
    }

    private static string GenerateReadme(string pluginName, string template)
    {
        return $$"""
# {{pluginName}}

A FlowEngine {{template}} plugin.

## Description

This plugin provides {{template}} functionality for FlowEngine data processing pipelines.

## Configuration

The plugin supports the following configuration options:

- `connectionString` (string): Connection string for data source
- `batchSize` (integer): Number of rows to process in each batch (default: 1000)

## Example Usage

```yaml
pipeline:
  name: "Sample Pipeline"
  plugins:
    - name: "{{pluginName.ToLowerInvariant()}}"
      type: "{{pluginName}}"
      assembly: "./{{pluginName}}.dll"
      config:
        connectionString: "your-connection-string"
        batchSize: 1000
```

## Development

### Building

```bash
dotnet build
```

### Testing

```bash
flowengine plugin validate .
flowengine plugin test .
```

### Benchmarking

```bash
flowengine plugin benchmark .
```

## License

[Your License Here]
""";
    }
}
