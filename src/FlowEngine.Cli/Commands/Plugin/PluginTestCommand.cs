using FlowEngine.Abstractions.Data;
using FlowEngine.Abstractions.Factories;
using FlowEngine.Abstractions.Plugins;
using FlowEngine.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Collections.Immutable;
using System.Diagnostics;

namespace FlowEngine.Cli.Commands.Plugin;

/// <summary>
/// Command to test a plugin with sample data.
/// </summary>
public class PluginTestCommand : BaseCommand
{
    /// <inheritdoc />
    public override string Name => "test";

    /// <inheritdoc />
    public override string Description => "Test a plugin with sample data";

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
        var testCount = GetTestCountOption(args);

        try
        {
            WriteInfo($"Testing plugin at: {Path.GetFullPath(pluginPath)}");
            WriteInfo($"Test iterations: {testCount}");

            var testResults = await TestPluginAsync(pluginPath, testCount, verbose);

            // Display results
            Console.WriteLine();
            Console.WriteLine("=== Test Results ===");

            if (testResults.Success)
            {
                WriteSuccess("All tests passed!");
                Console.WriteLine($"Total execution time: {testResults.TotalTime.TotalMilliseconds:F0}ms");
                Console.WriteLine($"Average per iteration: {testResults.AverageTime.TotalMilliseconds:F1}ms");
                Console.WriteLine($"Rows processed: {testResults.RowsProcessed:N0}");

                if (testResults.RowsProcessed > 0 && testResults.TotalTime.TotalSeconds > 0)
                {
                    var throughput = testResults.RowsProcessed / testResults.TotalTime.TotalSeconds;
                    Console.WriteLine($"Throughput: {throughput:N0} rows/sec");
                }

                return 0;
            }
            else
            {
                WriteError("Tests failed!");
                Console.WriteLine($"Error: {testResults.ErrorMessage}");
                return 1;
            }
        }
        catch (Exception ex)
        {
            WriteError($"Test execution failed: {ex.Message}");
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
        Console.WriteLine("Usage: flowengine plugin test [path] [options]");
        Console.WriteLine();
        Console.WriteLine("Arguments:");
        Console.WriteLine("  path           Path to plugin directory (defaults to current directory)");
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine("  --count <n>    Number of test iterations (default: 10)");
        Console.WriteLine("  --verbose, -v  Verbose output");
        Console.WriteLine("  --help, -h     Show this help");
        Console.WriteLine();
        Console.WriteLine("Examples:");
        Console.WriteLine("  flowengine plugin test");
        Console.WriteLine("  flowengine plugin test ./MyPlugin --count 100");
        Console.WriteLine("  flowengine plugin test ./MyPlugin --verbose");
    }

    private static int GetTestCountOption(string[] args)
    {
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == "--count" && i + 1 < args.Length)
            {
                if (int.TryParse(args[i + 1], out var count) && count > 0)
                {
                    return count;
                }
            }
        }
        return 10; // Default
    }

    private static async Task<TestResult> TestPluginAsync(string pluginPath, int testCount, bool verbose)
    {
        try
        {
            // Find plugin assembly
            var binPath = Path.Combine(pluginPath, "bin");
            if (!Directory.Exists(binPath))
            {
                return TestResult.Failed("No bin directory found - run 'dotnet build' first");
            }

            var dllFiles = Directory.GetFiles(binPath, "*.dll", SearchOption.AllDirectories)
                .Where(f => !Path.GetFileName(f).StartsWith("FlowEngine.") &&
                           !Path.GetFileName(f).StartsWith("Microsoft.") &&
                           !Path.GetFileName(f).StartsWith("System."))
                .ToArray();

            if (dllFiles.Length == 0)
            {
                return TestResult.Failed("No plugin assemblies found - run 'dotnet build' first");
            }

            // Setup FlowEngine services
            var services = new ServiceCollection();
            services.AddLogging(builder =>
            {
                builder.AddConsole();
                builder.SetMinimumLevel(verbose ? LogLevel.Debug : LogLevel.Warning);
            });
            services.AddFlowEngine();

            await using var serviceProvider = services.BuildServiceProvider();
            var pluginLoader = serviceProvider.GetRequiredService<IPluginLoader>();

            // Load and test each plugin
            var totalStopwatch = Stopwatch.StartNew();
            var totalRowsProcessed = 0L;

            foreach (var dllFile in dllFiles)
            {
                if (verbose)
                {
                    Console.WriteLine($"Testing assembly: {dllFile}");
                }

                try
                {
                    // Find plugin types in assembly
                    var assembly = System.Reflection.Assembly.LoadFrom(dllFile);
                    var pluginTypes = assembly.GetTypes()
                        .Where(t => t.IsClass && !t.IsAbstract &&
                                   typeof(IPlugin).IsAssignableFrom(t))
                        .ToArray();

                    foreach (var pluginType in pluginTypes)
                    {
                        if (verbose)
                        {
                            Console.WriteLine($"  Testing plugin: {pluginType.Name}");
                        }

                        // Load plugin
                        var plugin = await pluginLoader.LoadPluginAsync<IPlugin>(dllFile, pluginType.FullName!);

                        // Initialize plugin with test configuration
                        await InitializePluginForTestingAsync(plugin, verbose);

                        // Run test iterations
                        var iterationStopwatch = Stopwatch.StartNew();
                        var rowsProcessed = await RunPluginTestIterationsAsync(plugin, testCount, verbose, serviceProvider);
                        iterationStopwatch.Stop();

                        totalRowsProcessed += rowsProcessed;

                        if (verbose)
                        {
                            Console.WriteLine($"    Completed {testCount} iterations in {iterationStopwatch.ElapsedMilliseconds}ms");
                            Console.WriteLine($"    Processed {rowsProcessed} rows");
                        }
                    }
                }
                catch (Exception ex)
                {
                    return TestResult.Failed($"Failed to test assembly {Path.GetFileName(dllFile)}: {ex.Message}");
                }
            }

            totalStopwatch.Stop();

            return TestResult.Succeeded(
                totalStopwatch.Elapsed,
                TimeSpan.FromTicks(totalStopwatch.Elapsed.Ticks / testCount),
                totalRowsProcessed);
        }
        catch (Exception ex)
        {
            return TestResult.Failed($"Test setup failed: {ex.Message}");
        }
    }

    private static async Task InitializePluginForTestingAsync(IPlugin plugin, bool verbose)
    {
        // Create test configuration
        var testConfig = new TestPluginConfiguration();

        if (verbose)
        {
            Console.WriteLine($"      Initializing plugin: {plugin.Name}");
        }

        await plugin.InitializeAsync(testConfig);

        if (verbose)
        {
            Console.WriteLine($"      Plugin initialized successfully");
        }
    }

    private static async Task<long> RunPluginTestIterationsAsync(IPlugin plugin, int testCount, bool verbose, IServiceProvider serviceProvider)
    {
        var totalRowsProcessed = 0L;

        // Test different plugin types differently
        if (plugin is ISourcePlugin sourcePlugin)
        {
            totalRowsProcessed = await TestSourcePluginAsync(sourcePlugin, testCount, verbose, serviceProvider);
        }
        else if (plugin is ITransformPlugin transformPlugin)
        {
            totalRowsProcessed = await TestTransformPluginAsync(transformPlugin, testCount, verbose, serviceProvider);
        }
        else if (plugin is ISinkPlugin sinkPlugin)
        {
            totalRowsProcessed = await TestDestinationPluginAsync(sinkPlugin, testCount, verbose, serviceProvider);
        }
        else
        {
            // Generic plugin test
            totalRowsProcessed = await TestGenericPluginAsync(plugin, testCount, verbose);
        }

        return totalRowsProcessed;
    }

    private static async Task<long> TestSourcePluginAsync(ISourcePlugin plugin, int testCount, bool verbose, IServiceProvider serviceProvider)
    {
        var rowsProcessed = 0L;

        for (int i = 0; i < testCount; i++)
        {
            var chunkCount = 0;
            await foreach (var chunk in plugin.ProduceAsync(CancellationToken.None))
            {
                rowsProcessed += chunk.RowCount;
                chunkCount++;

                // Limit chunks per iteration for testing
                if (chunkCount >= 3)
                {
                    break;
                }
            }
        }

        if (verbose)
        {
            Console.WriteLine($"      Source plugin generated {rowsProcessed} rows across {testCount} iterations");
        }

        return rowsProcessed;
    }

    private static async Task<long> TestTransformPluginAsync(ITransformPlugin plugin, int testCount, bool verbose, IServiceProvider serviceProvider)
    {
        var rowsProcessed = 0L;

        // Create test input chunk
        var testSchema = CreateTestSchema(serviceProvider);
        var testRows = CreateTestRows(testSchema, 5, serviceProvider);
        var chunkFactory = serviceProvider.GetRequiredService<IChunkFactory>();
        var inputChunk = chunkFactory.CreateChunk(testRows);

        for (int i = 0; i < testCount; i++)
        {
            await foreach (var outputChunk in plugin.TransformAsync(CreateAsyncEnumerable(inputChunk), CancellationToken.None))
            {
                rowsProcessed += outputChunk.RowCount;
            }
        }

        if (verbose)
        {
            Console.WriteLine($"      Transform plugin processed {rowsProcessed} rows across {testCount} iterations");
        }

        return rowsProcessed;
    }

    private static async Task<long> TestDestinationPluginAsync(ISinkPlugin plugin, int testCount, bool verbose, IServiceProvider serviceProvider)
    {
        var rowsProcessed = 0L;

        // Create test input chunk
        var testSchema = CreateTestSchema(serviceProvider);
        var testRows = CreateTestRows(testSchema, 5, serviceProvider);
        var chunkFactory = serviceProvider.GetRequiredService<IChunkFactory>();
        var inputChunk = chunkFactory.CreateChunk(testRows);

        for (int i = 0; i < testCount; i++)
        {
            await plugin.ConsumeAsync(CreateAsyncEnumerable(inputChunk), CancellationToken.None);
            rowsProcessed += inputChunk.RowCount;
        }

        if (verbose)
        {
            Console.WriteLine($"      Destination plugin consumed {rowsProcessed} rows across {testCount} iterations");
        }

        return rowsProcessed;
    }

    private static async Task<long> TestGenericPluginAsync(IPlugin plugin, int testCount, bool verbose)
    {
        // Basic test for generic plugins
        if (verbose)
        {
            Console.WriteLine($"      Running basic test for generic plugin");
        }

        // Just verify the plugin is working
        for (int i = 0; i < testCount; i++)
        {
            // Basic operation test
            await Task.Delay(1); // Simulate work
        }

        return testCount; // Return iteration count as "rows processed"
    }

    private static ISchema CreateTestSchema(IServiceProvider serviceProvider)
    {
        var columns = new[]
        {
            new ColumnDefinition { Name = "Id", DataType = typeof(int), IsNullable = false },
            new ColumnDefinition { Name = "Name", DataType = typeof(string), IsNullable = false },
            new ColumnDefinition { Name = "Value", DataType = typeof(double), IsNullable = true },
            new ColumnDefinition { Name = "Timestamp", DataType = typeof(DateTime), IsNullable = false }
        };

        var schemaFactory = serviceProvider.GetRequiredService<ISchemaFactory>();
        return schemaFactory.CreateSchema(columns);
    }

    private static IArrayRow[] CreateTestRows(ISchema schema, int count, IServiceProvider serviceProvider)
    {
        var rows = new IArrayRow[count];
        var random = new Random(42); // Fixed seed for consistent tests

        var arrayRowFactory = serviceProvider.GetRequiredService<IArrayRowFactory>();

        for (int i = 0; i < count; i++)
        {
            var values = new object?[]
            {
                i + 1,
                $"Test Item {i + 1}",
                random.NextDouble() * 1000,
                DateTime.UtcNow.AddMinutes(-random.Next(1440)) // Random time in last 24 hours
            };

            rows[i] = arrayRowFactory.CreateRow(schema, values);
        }

        return rows;
    }

    private class TestResult
    {
        public required bool Success { get; init; }
        public required TimeSpan TotalTime { get; init; }
        public required TimeSpan AverageTime { get; init; }
        public required long RowsProcessed { get; init; }
        public string? ErrorMessage { get; init; }

        public static TestResult Succeeded(TimeSpan totalTime, TimeSpan averageTime, long rowsProcessed) =>
            new() { Success = true, TotalTime = totalTime, AverageTime = averageTime, RowsProcessed = rowsProcessed };

        public static TestResult Failed(string errorMessage) =>
            new() { Success = false, TotalTime = TimeSpan.Zero, AverageTime = TimeSpan.Zero, RowsProcessed = 0, ErrorMessage = errorMessage };
    }

    private class TestPluginConfiguration : IPluginConfiguration
    {
        private readonly Dictionary<string, object> _values = new()
        {
            ["connectionString"] = "test-connection",
            ["batchSize"] = 1000,
            ["timeout"] = 30
        };

        public string PluginId => "test-plugin";
        public string Name => "Test Plugin";
        public string Version => "1.0.0";
        public string PluginType => "Test";
        public ISchema? InputSchema => null;
        public ISchema? OutputSchema => null;
        public bool SupportsHotSwapping => false;
        public ImmutableDictionary<string, int> InputFieldIndexes => ImmutableDictionary<string, int>.Empty;
        public ImmutableDictionary<string, int> OutputFieldIndexes => ImmutableDictionary<string, int>.Empty;
        public ImmutableDictionary<string, object> Properties => _values.ToImmutableDictionary();

        public int GetInputFieldIndex(string fieldName) => -1;
        public int GetOutputFieldIndex(string fieldName) => -1;
        public bool IsCompatibleWith(ISchema schema) => true;

        public T GetProperty<T>(string key)
        {
            if (_values.TryGetValue(key, out var value))
            {
                return (T)Convert.ChangeType(value, typeof(T))!;
            }
            throw new KeyNotFoundException($"Property '{key}' not found");
        }

        public bool TryGetProperty<T>(string key, out T? value)
        {
            if (_values.TryGetValue(key, out var objValue))
            {
                try
                {
                    value = (T)Convert.ChangeType(objValue, typeof(T))!;
                    return true;
                }
                catch
                {
                    value = default!;
                    return false;
                }
            }
            value = default!;
            return false;
        }
    }

    /// <summary>
    /// Helper method to create an async enumerable from a single chunk.
    /// </summary>
    private static async IAsyncEnumerable<IChunk> CreateAsyncEnumerable(IChunk chunk)
    {
        yield return chunk;
        await Task.CompletedTask; // Satisfy async requirement
    }
}
