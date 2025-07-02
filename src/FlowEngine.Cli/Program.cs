using System.CommandLine;
using System.CommandLine.Builder;
using System.CommandLine.Parsing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using FlowEngine.Cli.Commands;
using FlowEngine.Cli.Services;

namespace FlowEngine.Cli;

/// <summary>
/// Entry point for the FlowEngine command-line interface.
/// Implements Phase 3 CLI-first development with comprehensive plugin management.
/// </summary>
/// <remarks>
/// FlowEngine CLI provides:
/// - Plugin operations (create, validate, test, benchmark)
/// - Schema operations (infer, validate, migrate)
/// - Pipeline operations (validate, run, export)
/// - Performance monitoring and diagnostics
/// - Hot-swapping and configuration management
/// 
/// All FlowEngine operations are available through the CLI for automation,
/// CI/CD integration, and developer productivity.
/// </remarks>
internal class Program
{
    /// <summary>
    /// Main entry point for the FlowEngine CLI application.
    /// </summary>
    /// <param name="args">Command line arguments</param>
    /// <returns>Exit code (0 = success, non-zero = error)</returns>
    static async Task<int> Main(string[] args)
    {
        // Create root command with description
        var rootCommand = new RootCommand("FlowEngine Phase 3 CLI - Plugin Ecosystem & Enterprise Features")
        {
            Description = "FlowEngine CLI provides comprehensive plugin management, pipeline operations, and performance monitoring.\n" +
                         "Supports the five-component plugin architecture with ArrayRow optimization for >200K rows/sec throughput."
        };

        // Build command hierarchy
        rootCommand.Add(BuildPluginCommands());
        rootCommand.Add(BuildSchemaCommands());
        rootCommand.Add(BuildPipelineCommands());
        rootCommand.Add(BuildMonitoringCommands());
        rootCommand.Add(BuildDevelopmentCommands());

        // Configure command line parser with advanced features
        var parser = new CommandLineBuilder(rootCommand)
            .UseDefaults() // Adds help, version, error handling
            .UseExceptionHandler((ex, context) =>
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.Error.WriteLine($"Error: {ex.Message}");
                Console.ResetColor();
                
                if (ex is AggregateException aggregateEx)
                {
                    foreach (var innerEx in aggregateEx.InnerExceptions)
                    {
                        Console.Error.WriteLine($"  - {innerEx.Message}");
                    }
                }
                
                context.ExitCode = 1;
            })
            .Build();

        // Execute command
        return await parser.InvokeAsync(args);
    }

    /// <summary>
    /// Builds plugin management commands.
    /// </summary>
    /// <returns>Plugin command with subcommands</returns>
    private static Command BuildPluginCommands()
    {
        var pluginCommand = new Command("plugin", "Plugin management operations");

        // Plugin create command
        var createCommand = new Command("create", "Create a new plugin from template");
        var nameOption = new Option<string>("--name", "Plugin name") { IsRequired = true };
        var typeOption = new Option<string>("--type", "Plugin type (Source, Transform, Sink)") { IsRequired = true };
        var outputOption = new Option<string?>("--output", "Output directory (default: current directory)");
        var fiveComponentOption = new Option<bool>("--five-component", () => true, "Use five-component architecture pattern");
        var minimalOption = new Option<bool>("--minimal", "Create minimal plugin with Abstractions-only dependencies");
        
        createCommand.AddOption(nameOption);
        createCommand.AddOption(typeOption);
        createCommand.AddOption(outputOption);
        createCommand.AddOption(fiveComponentOption);
        createCommand.AddOption(minimalOption);
        createCommand.SetHandler(async (string name, string type, string? output, bool fiveComponent, bool minimal) =>
        {
            await PluginCommands.CreateAsync(name, type, output, fiveComponent, minimal);
        }, nameOption, typeOption, outputOption, fiveComponentOption, minimalOption);

        // Plugin validate command
        var validateCommand = new Command("validate", "Validate plugin configuration and implementation");
        validateCommand.AddOption(new Option<string>("--path", "Path to plugin directory or assembly") { IsRequired = true });
        validateCommand.AddOption(new Option<bool>("--comprehensive", "Perform comprehensive validation including performance checks"));
        validateCommand.AddOption(new Option<string?>("--config", "Plugin configuration file to validate"));
        validateCommand.SetHandler(PluginCommands.ValidateAsync);

        // Plugin test command
        var testCommand = new Command("test", "Test plugin with sample data");
        testCommand.AddOption(new Option<string>("--path", "Path to plugin directory or assembly") { IsRequired = true });
        testCommand.AddOption(new Option<string>("--data", "Path to test data file") { IsRequired = true });
        testCommand.AddOption(new Option<string?>("--config", "Plugin configuration file"));
        testCommand.AddOption(new Option<int>("--rows", () => 1000, "Number of rows to process"));
        testCommand.AddOption(new Option<bool>("--performance", "Include performance measurements"));
        testCommand.SetHandler(PluginCommands.TestAsync);

        // Plugin benchmark command
        var benchmarkCommand = new Command("benchmark", "Benchmark plugin performance");
        benchmarkCommand.AddOption(new Option<string>("--path", "Path to plugin directory or assembly") { IsRequired = true });
        benchmarkCommand.AddOption(new Option<int>("--rows", () => 1000000, "Number of rows for benchmark"));
        benchmarkCommand.AddOption(new Option<int>("--iterations", () => 5, "Number of benchmark iterations"));
        benchmarkCommand.AddOption(new Option<bool>("--memory", "Include memory usage analysis"));
        benchmarkCommand.AddOption(new Option<string?>("--output", "Output benchmark results to file"));
        benchmarkCommand.SetHandler(PluginCommands.BenchmarkAsync);

        // Plugin list command
        var listCommand = new Command("list", "List available plugins");
        listCommand.AddOption(new Option<string?>("--type", "Filter by plugin type"));
        listCommand.AddOption(new Option<bool>("--detailed", "Show detailed plugin information"));
        listCommand.SetHandler(PluginCommands.ListAsync);

        pluginCommand.Add(createCommand);
        pluginCommand.Add(validateCommand);
        pluginCommand.Add(testCommand);
        pluginCommand.Add(benchmarkCommand);
        pluginCommand.Add(listCommand);

        return pluginCommand;
    }

    /// <summary>
    /// Builds schema management commands.
    /// </summary>
    /// <returns>Schema command with subcommands</returns>
    private static Command BuildSchemaCommands()
    {
        var schemaCommand = new Command("schema", "Schema inference and validation operations");

        // Schema infer command
        var inferCommand = new Command("infer", "Infer schema from data file");
        inferCommand.AddOption(new Option<string>("--input", "Input data file") { IsRequired = true });
        inferCommand.AddOption(new Option<string>("--output", "Output schema file") { IsRequired = true });
        inferCommand.AddOption(new Option<string>("--format", () => "yaml", "Output format (yaml, json)"));
        inferCommand.AddOption(new Option<int>("--sample-size", () => 10000, "Number of rows to sample"));
        inferCommand.AddOption(new Option<bool>("--optimize-arrayrow", () => true, "Include ArrayRow optimization hints"));
        inferCommand.SetHandler(SchemaCommands.InferAsync);

        // Schema validate command
        var validateCommand = new Command("validate", "Validate data against schema");
        validateCommand.AddOption(new Option<string>("--schema", "Schema file") { IsRequired = true });
        validateCommand.AddOption(new Option<string>("--data", "Data file to validate") { IsRequired = true });
        validateCommand.AddOption(new Option<bool>("--detailed", "Show detailed validation results"));
        validateCommand.AddOption(new Option<int>("--max-errors", () => 100, "Maximum errors to report"));
        validateCommand.SetHandler(SchemaCommands.ValidateAsync);

        // Schema migrate command
        var migrateCommand = new Command("migrate", "Migrate schema to new version");
        migrateCommand.AddOption(new Option<string>("--from", "Source schema file") { IsRequired = true });
        migrateCommand.AddOption(new Option<string>("--to", "Target schema file") { IsRequired = true });
        migrateCommand.AddOption(new Option<string>("--output", "Migration plan output file") { IsRequired = true });
        migrateCommand.AddOption(new Option<bool>("--check-compatibility", () => true, "Check backward compatibility"));
        migrateCommand.SetHandler(SchemaCommands.MigrateAsync);

        schemaCommand.Add(inferCommand);
        schemaCommand.Add(validateCommand);
        schemaCommand.Add(migrateCommand);

        return schemaCommand;
    }

    /// <summary>
    /// Builds pipeline management commands.
    /// </summary>
    /// <returns>Pipeline command with subcommands</returns>
    private static Command BuildPipelineCommands()
    {
        var pipelineCommand = new Command("pipeline", "Pipeline execution and management operations");

        // Pipeline validate command
        var validateCommand = new Command("validate", "Validate pipeline configuration");
        validateCommand.AddOption(new Option<string>("--config", "Pipeline configuration file") { IsRequired = true });
        validateCommand.AddOption(new Option<bool>("--check-plugins", () => true, "Validate plugin availability"));
        validateCommand.AddOption(new Option<bool>("--check-schemas", () => true, "Validate schema compatibility"));
        validateCommand.AddOption(new Option<bool>("--performance", "Include performance projections"));
        validateCommand.SetHandler(PipelineCommands.ValidateAsync);

        // Pipeline run command
        var runCommand = new Command("run", "Execute pipeline");
        runCommand.AddOption(new Option<string>("--config", "Pipeline configuration file") { IsRequired = true });
        runCommand.AddOption(new Option<bool>("--monitor", "Enable real-time monitoring"));
        runCommand.AddOption(new Option<string?>("--output", "Output directory for results"));
        runCommand.AddOption(new Option<int>("--parallelism", () => Environment.ProcessorCount, "Degree of parallelism"));
        runCommand.AddOption(new Option<bool>("--dry-run", "Validate and plan without execution"));
        runCommand.SetHandler(PipelineCommands.RunAsync);

        // Pipeline export command
        var exportCommand = new Command("export", "Export pipeline for deployment");
        exportCommand.AddOption(new Option<string>("--config", "Pipeline configuration file") { IsRequired = true });
        exportCommand.AddOption(new Option<string>("--format", () => "docker", "Export format (docker, kubernetes, standalone)"));
        exportCommand.AddOption(new Option<string>("--output", "Output directory") { IsRequired = true });
        exportCommand.AddOption(new Option<bool>("--include-data", "Include sample data"));
        exportCommand.SetHandler(PipelineCommands.ExportAsync);

        pipelineCommand.Add(validateCommand);
        pipelineCommand.Add(runCommand);
        pipelineCommand.Add(exportCommand);

        return pipelineCommand;
    }

    /// <summary>
    /// Builds monitoring and diagnostics commands.
    /// </summary>
    /// <returns>Monitor command with subcommands</returns>
    private static Command BuildMonitoringCommands()
    {
        var monitorCommand = new Command("monitor", "Monitoring and diagnostics operations");

        // Monitor performance command
        var performanceCommand = new Command("performance", "Monitor pipeline performance");
        performanceCommand.AddOption(new Option<string?>("--pipeline", "Pipeline to monitor (if not specified, monitors all)"));
        performanceCommand.AddOption(new Option<int>("--interval", () => 5, "Monitoring interval in seconds"));
        performanceCommand.AddOption(new Option<bool>("--real-time", () => true, "Real-time performance display"));
        performanceCommand.SetHandler(MonitorCommands.PerformanceAsync);

        // Monitor health command
        var healthCommand = new Command("health", "Check system and plugin health");
        healthCommand.AddOption(new Option<bool>("--comprehensive", "Comprehensive health check"));
        healthCommand.AddOption(new Option<string?>("--plugin", "Check specific plugin health"));
        healthCommand.SetHandler(MonitorCommands.HealthAsync);

        monitorCommand.Add(performanceCommand);
        monitorCommand.Add(healthCommand);

        return monitorCommand;
    }

    /// <summary>
    /// Builds development and debugging commands.
    /// </summary>
    /// <returns>Dev command with subcommands</returns>
    private static Command BuildDevelopmentCommands()
    {
        var devCommand = new Command("dev", "Development and debugging tools");

        // Dev generate command
        var generateCommand = new Command("generate", "Generate plugin scaffolding and templates");
        generateCommand.AddOption(new Option<string>("--type", "Generation type (plugin, schema, pipeline)") { IsRequired = true });
        generateCommand.AddOption(new Option<string>("--name", "Name for generated item") { IsRequired = true });
        generateCommand.AddOption(new Option<string?>("--template", "Template to use"));
        generateCommand.AddOption(new Option<string?>("--output", "Output directory"));
        generateCommand.SetHandler(DevelopmentCommands.GenerateAsync);

        // Dev debug command
        var debugCommand = new Command("debug", "Debug plugin or pipeline issues");
        debugCommand.AddOption(new Option<string>("--target", "Plugin or pipeline to debug") { IsRequired = true });
        debugCommand.AddOption(new Option<bool>("--verbose", "Verbose debugging output"));
        debugCommand.AddOption(new Option<string?>("--log-level", "Log level (trace, debug, info, warn, error)"));
        debugCommand.SetHandler(DevelopmentCommands.DebugAsync);

        devCommand.Add(generateCommand);
        devCommand.Add(debugCommand);

        return devCommand;
    }
}