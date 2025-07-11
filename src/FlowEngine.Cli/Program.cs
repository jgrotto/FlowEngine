using FlowEngine.Abstractions.Configuration;
using FlowEngine.Abstractions.Plugins;
using FlowEngine.Cli.Commands;
using FlowEngine.Core;
using FlowEngine.Core.Configuration;
using FlowEngine.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace FlowEngine.Cli;

/// <summary>
/// Entry point for the FlowEngine command-line interface.
/// Supports both legacy pipeline execution and new command-based interface.
/// </summary>
internal class Program
{
    private static readonly Dictionary<string, ICommand> Commands = new()
    {
        ["plugin"] = new PluginCommand()
    };

    /// <summary>
    /// Main entry point for the application.
    /// </summary>
    /// <param name="args">Command line arguments</param>
    /// <returns>Exit code: 0=Success, 1=Configuration error, 2=Execution error</returns>
    static async Task<int> Main(string[] args)
    {
        try
        {
            if (args.Length == 0 || args[0] == "--help" || args[0] == "-h")
            {
                ShowUsage();
                return args.Length == 0 ? 1 : 0;
            }

            var firstArg = args[0].ToLowerInvariant();

            // Check for command-based interface
            if (Commands.TryGetValue(firstArg, out var command))
            {
                var commandArgs = args.Skip(1).ToArray();
                return await command.ExecuteAsync(commandArgs);
            }

            // Legacy pipeline execution (backward compatibility)
            var configPath = args[0];

            // Check if it looks like a config file
            if (configPath.EndsWith(".yaml") || configPath.EndsWith(".yml") || File.Exists(configPath))
            {
                var verbose = args.Contains("--verbose") || args.Contains("-v");
                return await ExecutePipelineAsync(configPath, verbose);
            }
            else
            {
                Console.Error.WriteLine($"Unknown command or file: {args[0]}");
                Console.WriteLine();
                ShowUsage();
                return 1;
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Fatal error: {ex.Message}");
            if (args.Contains("--verbose"))
            {
                Console.Error.WriteLine($"Stack trace: {ex.StackTrace}");
            }
            return 1;
        }
    }

    /// <summary>
    /// Shows usage information for the CLI.
    /// </summary>
    private static void ShowUsage()
    {
        Console.WriteLine("FlowEngine CLI v1.0");
        Console.WriteLine("High-performance data processing pipeline executor");
        Console.WriteLine();
        Console.WriteLine("Usage:");
        Console.WriteLine("  flowengine <pipeline.yaml>              Execute a pipeline");
        Console.WriteLine("  flowengine <command> [options]          Run a command");
        Console.WriteLine("  flowengine --help                       Show this help");
        Console.WriteLine();
        Console.WriteLine("Commands:");
        foreach (var command in Commands.Values)
        {
            Console.WriteLine($"  {command.Name,-12} {command.Description}");
        }
        Console.WriteLine();
        Console.WriteLine("Pipeline Execution:");
        Console.WriteLine("  flowengine simple-pipeline.yaml");
        Console.WriteLine("  flowengine production-pipeline.yaml --verbose");
        Console.WriteLine();
        Console.WriteLine("Plugin Development:");
        Console.WriteLine("  flowengine plugin create MySourcePlugin");
        Console.WriteLine("  flowengine plugin validate ./MyPlugin");
        Console.WriteLine("  flowengine plugin test ./MyPlugin");
        Console.WriteLine("  flowengine plugin benchmark ./MyPlugin");
        Console.WriteLine();
        Console.WriteLine("Exit codes:");
        Console.WriteLine("  0  Success");
        Console.WriteLine("  1  Configuration error");
        Console.WriteLine("  2  Execution error");
        Console.WriteLine();
        Console.WriteLine("Use 'flowengine <command> --help' for detailed command help.");
    }

    /// <summary>
    /// Executes a pipeline from the specified configuration file using the new bootstrapper approach.
    /// </summary>
    /// <param name="configPath">Path to the pipeline configuration file</param>
    /// <param name="verbose">Enable verbose output</param>
    /// <returns>Exit code</returns>
    private static async Task<int> ExecutePipelineAsync(string configPath, bool verbose)
    {
        // Step 1: Validate file exists
        if (!File.Exists(configPath))
        {
            Console.Error.WriteLine($"Error: Configuration file not found: {configPath}");
            return 1;
        }

        Console.WriteLine($"FlowEngine CLI - Executing pipeline: {Path.GetFileName(configPath)}");
        if (verbose)
        {
            Console.WriteLine($"Configuration file: {Path.GetFullPath(configPath)}");
        }

        try
        {
            // Step 2: Configure services and create service provider
            Console.WriteLine("Initializing FlowEngine...");
            var services = new ServiceCollection();
            
            // Add logging
            services.AddLogging(builder =>
            {
                builder.AddConsole();
                builder.SetMinimumLevel(verbose ? LogLevel.Debug : LogLevel.Information);
            });
            
            // Add FlowEngine services
            services.AddFlowEngine();
            
            var serviceProvider = services.BuildServiceProvider();
            
            // Step 3: Get core services
            var coordinator = serviceProvider.GetRequiredService<FlowEngineCoordinator>();
            var pluginDiscovery = serviceProvider.GetRequiredService<IPluginDiscoveryService>();
            var configMapper = serviceProvider.GetRequiredService<IPluginConfigurationMapper>();
            
            // Step 4: Discover plugins for bootstrap
            Console.WriteLine("Discovering plugins...");
            var discoveryOptions = new PluginDiscoveryOptions
            {
                IncludeBuiltInPlugins = true,
                ScanRecursively = true,
                PerformValidation = true,
                DiscoveryTimeout = TimeSpan.FromSeconds(30)
            };
            
            var discoveredPlugins = await pluginDiscovery.DiscoverPluginsForBootstrapAsync(discoveryOptions);
            
            if (verbose)
            {
                Console.WriteLine($"Available plugins: {discoveredPlugins.Count}");
                foreach (var plugin in discoveredPlugins.Take(5)) // Show first 5
                {
                    Console.WriteLine($"  - {plugin.Manifest.Name} ({plugin.Capabilities})");
                }
                if (discoveredPlugins.Count > 5)
                {
                    Console.WriteLine($"  ... and {discoveredPlugins.Count - 5} more");
                }
            }

            // Step 5: Load pipeline configuration
            Console.WriteLine("Loading pipeline configuration...");
            var yamlContent = await File.ReadAllTextAsync(configPath);
            var typeResolver = serviceProvider.GetRequiredService<IPluginTypeResolver>();
            var pipelineConfig = PipelineConfiguration.LoadFromYaml(yamlContent, typeResolver);

            if (verbose)
            {
                Console.WriteLine($"Pipeline: {pipelineConfig.Name}");
                Console.WriteLine($"Plugins: {pipelineConfig.Plugins.Count()}");
                Console.WriteLine($"Connections: {pipelineConfig.Connections.Count()}");
            }

            // Step 6: Validate that required plugins are available
            Console.WriteLine("Validating configuration...");
            var requiredPluginTypes = pipelineConfig.Plugins.Select(p => p.Type).Distinct().ToHashSet();
            var availablePluginTypes = discoveredPlugins.Select(p => p.Manifest.PluginTypeName).ToHashSet();
            var missingPlugins = requiredPluginTypes.Except(availablePluginTypes).ToList();
            
            if (missingPlugins.Any())
            {
                Console.Error.WriteLine("Configuration validation failed:");
                Console.Error.WriteLine("Missing plugins:");
                foreach (var missing in missingPlugins)
                {
                    Console.Error.WriteLine($"  - {missing}");
                }
                return 1;
            }

            // Step 7: Execute pipeline
            Console.WriteLine("Executing pipeline...");
            var result = await coordinator.PipelineExecutor.ExecuteAsync(pipelineConfig);

            // Step 8: Report results
            Console.WriteLine();
            Console.WriteLine("=== Execution Results ===");
            Console.WriteLine($"Status: {(result.IsSuccess ? "SUCCESS" : "FAILED")}");
            Console.WriteLine($"Execution Time: {result.ExecutionTime.TotalSeconds:F2}s");
            Console.WriteLine($"Total Rows Processed: {result.TotalRowsProcessed:N0}");
            Console.WriteLine($"Total Chunks Processed: {result.TotalChunksProcessed:N0}");

            if (result.TotalRowsProcessed > 0 && result.ExecutionTime.TotalSeconds > 0)
            {
                var throughput = result.TotalRowsProcessed / result.ExecutionTime.TotalSeconds;
                Console.WriteLine($"Throughput: {throughput:N0} rows/sec");
            }

            if (verbose && result.PluginMetrics.Any())
            {
                Console.WriteLine();
                Console.WriteLine("=== Plugin Metrics ===");
                foreach (var metric in result.PluginMetrics)
                {
                    Console.WriteLine($"{metric.Key}:");
                    Console.WriteLine($"  Execution Time: {metric.Value.ExecutionTime.TotalMilliseconds:F0}ms");
                    Console.WriteLine($"  Rows Processed: {metric.Value.RowsProcessed:N0}");
                    Console.WriteLine($"  Chunks Processed: {metric.Value.ChunksProcessed:N0}");
                    if (metric.Value.ErrorCount > 0)
                    {
                        Console.WriteLine($"  Errors: {metric.Value.ErrorCount}");
                    }
                }
            }

            if (!result.IsSuccess)
            {
                Console.WriteLine();
                Console.WriteLine("=== Errors ===");
                foreach (var error in result.Errors)
                {
                    Console.WriteLine($"Plugin '{error.PluginName}': {error.Message}");
                    if (verbose && error.Exception != null)
                    {
                        Console.WriteLine($"  Exception: {error.Exception}");
                    }
                }
                return 2;
            }

            Console.WriteLine();
            Console.WriteLine("Pipeline executed successfully!");
            return 0;
        }
        catch (FileNotFoundException ex)
        {
            Console.Error.WriteLine($"Configuration file error: {ex.Message}");
            return 1;
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("configuration"))
        {
            Console.Error.WriteLine($"Configuration error: {ex.Message}");
            if (verbose && ex.InnerException != null)
            {
                Console.Error.WriteLine($"Details: {ex.InnerException.Message}");
            }
            return 1;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Execution error: {ex.Message}");
            if (verbose)
            {
                Console.Error.WriteLine($"Details: {ex.StackTrace}");
            }
            return 2;
        }
    }

}
