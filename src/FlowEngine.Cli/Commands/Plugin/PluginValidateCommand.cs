using FlowEngine.Abstractions.Plugins;
using FlowEngine.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace FlowEngine.Cli.Commands.Plugin;

/// <summary>
/// Command to validate a plugin and its configuration.
/// </summary>
public class PluginValidateCommand : BaseCommand
{
    /// <inheritdoc />
    public override string Name => "validate";

    /// <inheritdoc />
    public override string Description => "Validate a plugin project and its configuration";

    /// <inheritdoc />
    public override async Task<int> ExecuteAsync(string[] args)
    {
        if (IsHelpRequested(args))
        {
            ShowHelp();
            return 0;
        }

        var verbose = IsVerbose(args);
        var pluginPath = args.Length > 0 ? args[0] : ".";

        try
        {
            WriteInfo($"Validating plugin at: {Path.GetFullPath(pluginPath)}");

            var validationResults = await ValidatePluginAsync(pluginPath, verbose);

            // Display results
            Console.WriteLine();
            Console.WriteLine("=== Validation Results ===");

            var hasErrors = false;
            var hasWarnings = false;

            foreach (var result in validationResults)
            {
                if (result.IsError)
                {
                    WriteError(result.Message);
                    hasErrors = true;
                }
                else if (result.IsWarning)
                {
                    WriteWarning(result.Message);
                    hasWarnings = true;
                }
                else
                {
                    WriteSuccess(result.Message);
                }
            }

            Console.WriteLine();
            if (hasErrors)
            {
                WriteError("Plugin validation failed with errors");
                return 1;
            }
            else if (hasWarnings)
            {
                WriteWarning("Plugin validation completed with warnings");
                return 0;
            }
            else
            {
                WriteSuccess("Plugin validation passed successfully!");
                return 0;
            }
        }
        catch (Exception ex)
        {
            WriteError($"Validation failed: {ex.Message}");
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
        Console.WriteLine("Usage: flowengine plugin validate [path] [options]");
        Console.WriteLine();
        Console.WriteLine("Arguments:");
        Console.WriteLine("  path           Path to plugin directory (defaults to current directory)");
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine("  --verbose, -v  Verbose output");
        Console.WriteLine("  --help, -h     Show this help");
        Console.WriteLine();
        Console.WriteLine("Examples:");
        Console.WriteLine("  flowengine plugin validate");
        Console.WriteLine("  flowengine plugin validate ./MyPlugin");
        Console.WriteLine("  flowengine plugin validate ./MyPlugin --verbose");
    }

    private static async Task<List<ValidationResult>> ValidatePluginAsync(string pluginPath, bool verbose)
    {
        var results = new List<ValidationResult>();

        // 1. Validate directory structure
        results.AddRange(ValidateDirectoryStructure(pluginPath, verbose));

        // 2. Validate project file
        results.AddRange(await ValidateProjectFileAsync(pluginPath, verbose));

        // 3. Validate plugin manifest
        results.AddRange(await ValidatePluginManifestAsync(pluginPath, verbose));

        // 4. Validate plugin implementation (if compiled)
        results.AddRange(await ValidatePluginImplementationAsync(pluginPath, verbose));

        return results;
    }

    private static List<ValidationResult> ValidateDirectoryStructure(string pluginPath, bool verbose)
    {
        var results = new List<ValidationResult>();

        if (!Directory.Exists(pluginPath))
        {
            results.Add(ValidationResult.Error($"Plugin directory does not exist: {pluginPath}"));
            return results;
        }

        results.Add(ValidationResult.Success("Plugin directory exists"));

        // Check for required files
        var projectFiles = Directory.GetFiles(pluginPath, "*.csproj");
        if (projectFiles.Length == 0)
        {
            results.Add(ValidationResult.Error("No .csproj file found"));
        }
        else if (projectFiles.Length > 1)
        {
            results.Add(ValidationResult.Warning($"Multiple .csproj files found: {string.Join(", ", projectFiles.Select(Path.GetFileName))}"));
        }
        else
        {
            results.Add(ValidationResult.Success($"Project file found: {Path.GetFileName(projectFiles[0])}"));
        }

        var manifestPath = Path.Combine(pluginPath, "plugin.json");
        if (!File.Exists(manifestPath))
        {
            results.Add(ValidationResult.Warning("plugin.json manifest not found (recommended for plugin discovery)"));
        }
        else
        {
            results.Add(ValidationResult.Success("plugin.json manifest found"));
        }

        return results;
    }

    private static async Task<List<ValidationResult>> ValidateProjectFileAsync(string pluginPath, bool verbose)
    {
        var results = new List<ValidationResult>();
        var projectFiles = Directory.GetFiles(pluginPath, "*.csproj");

        if (projectFiles.Length == 0)
        {
            return results; // Already reported in directory structure validation
        }

        var projectFile = projectFiles[0];
        try
        {
            var content = await File.ReadAllTextAsync(projectFile);
            results.Add(ValidationResult.Success("Project file is readable"));

            // Check for FlowEngine.Abstractions reference
            if (content.Contains("FlowEngine.Abstractions"))
            {
                results.Add(ValidationResult.Success("FlowEngine.Abstractions dependency found"));
            }
            else
            {
                results.Add(ValidationResult.Error("FlowEngine.Abstractions dependency missing"));
            }

            // Check target framework
            if (content.Contains("net8.0"))
            {
                results.Add(ValidationResult.Success("Target framework is .NET 8.0"));
            }
            else if (content.Contains("<TargetFramework>"))
            {
                results.Add(ValidationResult.Warning("Target framework should be net8.0 for optimal compatibility"));
            }

            // Check for documentation generation
            if (content.Contains("GenerateDocumentationFile"))
            {
                results.Add(ValidationResult.Success("Documentation generation enabled"));
            }
            else
            {
                results.Add(ValidationResult.Warning("Consider enabling documentation generation"));
            }
        }
        catch (Exception ex)
        {
            results.Add(ValidationResult.Error($"Failed to read project file: {ex.Message}"));
        }

        return results;
    }

    private static async Task<List<ValidationResult>> ValidatePluginManifestAsync(string pluginPath, bool verbose)
    {
        var results = new List<ValidationResult>();
        var manifestPath = Path.Combine(pluginPath, "plugin.json");

        if (!File.Exists(manifestPath))
        {
            return results; // Already reported in directory structure validation
        }

        try
        {
            var content = await File.ReadAllTextAsync(manifestPath);
            var manifest = JsonSerializer.Deserialize<JsonElement>(content);

            results.Add(ValidationResult.Success("plugin.json is valid JSON"));

            // Validate required fields
            if (manifest.TryGetProperty("name", out var nameElement) && !string.IsNullOrWhiteSpace(nameElement.GetString()))
            {
                results.Add(ValidationResult.Success($"Plugin name: {nameElement.GetString()}"));
            }
            else
            {
                results.Add(ValidationResult.Error("Plugin name is required in plugin.json"));
            }

            if (manifest.TryGetProperty("version", out var versionElement) && !string.IsNullOrWhiteSpace(versionElement.GetString()))
            {
                results.Add(ValidationResult.Success($"Plugin version: {versionElement.GetString()}"));
            }
            else
            {
                results.Add(ValidationResult.Error("Plugin version is required in plugin.json"));
            }

            if (manifest.TryGetProperty("description", out var descElement) && !string.IsNullOrWhiteSpace(descElement.GetString()))
            {
                results.Add(ValidationResult.Success("Plugin description provided"));
            }
            else
            {
                results.Add(ValidationResult.Warning("Plugin description is recommended"));
            }

            if (manifest.TryGetProperty("category", out var categoryElement) && !string.IsNullOrWhiteSpace(categoryElement.GetString()))
            {
                var category = categoryElement.GetString();
                if (category is "source" or "transform" or "destination")
                {
                    results.Add(ValidationResult.Success($"Plugin category: {category}"));
                }
                else
                {
                    results.Add(ValidationResult.Warning($"Plugin category '{category}' is not standard (source, transform, destination)"));
                }
            }
            else
            {
                results.Add(ValidationResult.Warning("Plugin category is recommended"));
            }

            // Validate dependencies
            if (manifest.TryGetProperty("dependencies", out var depsElement) && depsElement.ValueKind == JsonValueKind.Array)
            {
                var hasFlowEngineCore = false;
                foreach (var dep in depsElement.EnumerateArray())
                {
                    if (dep.TryGetProperty("name", out var depName) && depName.GetString()?.Contains("FlowEngine") == true)
                    {
                        hasFlowEngineCore = true;
                        break;
                    }
                }

                if (hasFlowEngineCore)
                {
                    results.Add(ValidationResult.Success("FlowEngine dependency declared in manifest"));
                }
                else
                {
                    results.Add(ValidationResult.Warning("FlowEngine dependency not declared in manifest"));
                }
            }
        }
        catch (JsonException ex)
        {
            results.Add(ValidationResult.Error($"plugin.json is not valid JSON: {ex.Message}"));
        }
        catch (Exception ex)
        {
            results.Add(ValidationResult.Error($"Failed to validate plugin.json: {ex.Message}"));
        }

        return results;
    }

    private static async Task<List<ValidationResult>> ValidatePluginImplementationAsync(string pluginPath, bool verbose)
    {
        var results = new List<ValidationResult>();

        try
        {
            // Look for compiled assemblies
            var binPath = Path.Combine(pluginPath, "bin");
            if (!Directory.Exists(binPath))
            {
                results.Add(ValidationResult.Warning("No bin directory found - run 'dotnet build' first"));
                return results;
            }

            var dllFiles = Directory.GetFiles(binPath, "*.dll", SearchOption.AllDirectories)
                .Where(f => !Path.GetFileName(f).StartsWith("FlowEngine.") &&
                           !Path.GetFileName(f).StartsWith("Microsoft.") &&
                           !Path.GetFileName(f).StartsWith("System."))
                .ToArray();

            if (dllFiles.Length == 0)
            {
                results.Add(ValidationResult.Warning("No plugin assemblies found - run 'dotnet build' first"));
                return results;
            }

            results.Add(ValidationResult.Success($"Found {dllFiles.Length} plugin assembly(ies)"));

            // Try to load and validate plugin implementation
            var services = new ServiceCollection();
            services.AddLogging(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Warning));
            services.AddFlowEngine();

            await using var serviceProvider = services.BuildServiceProvider();
            var coordinator = serviceProvider.GetRequiredService<FlowEngineCoordinator>();

            var discoveredPlugins = 0;
            foreach (var dllFile in dllFiles)
            {
                try
                {
                    if (verbose)
                    {
                        Console.WriteLine($"Checking assembly: {dllFile}");
                    }

                    // This is a simplified check - in reality we'd use the plugin loader
                    var assembly = System.Reflection.Assembly.LoadFrom(dllFile);
                    var pluginTypes = assembly.GetTypes()
                        .Where(t => t.IsClass && !t.IsAbstract &&
                                   t.GetInterfaces().Any(i => i.Name.Contains("Plugin")))
                        .ToArray();

                    if (pluginTypes.Length > 0)
                    {
                        discoveredPlugins += pluginTypes.Length;
                        results.Add(ValidationResult.Success($"Found {pluginTypes.Length} plugin implementation(s) in {Path.GetFileName(dllFile)}"));

                        foreach (var pluginType in pluginTypes)
                        {
                            if (verbose)
                            {
                                Console.WriteLine($"  - {pluginType.Name} : {string.Join(", ", pluginType.GetInterfaces().Where(i => i.Name.Contains("Plugin")).Select(i => i.Name))}");
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    results.Add(ValidationResult.Warning($"Could not load assembly {Path.GetFileName(dllFile)}: {ex.Message}"));
                }
            }

            if (discoveredPlugins == 0)
            {
                results.Add(ValidationResult.Error("No valid plugin implementations found"));
            }
        }
        catch (Exception ex)
        {
            results.Add(ValidationResult.Error($"Failed to validate plugin implementation: {ex.Message}"));
        }

        return results;
    }

    private class ValidationResult
    {
        public required string Message { get; init; }
        public required bool IsError { get; init; }
        public required bool IsWarning { get; init; }

        public static ValidationResult Success(string message) => new() { Message = message, IsError = false, IsWarning = false };
        public static ValidationResult Warning(string message) => new() { Message = message, IsError = false, IsWarning = true };
        public static ValidationResult Error(string message) => new() { Message = message, IsError = true, IsWarning = false };
    }
}
