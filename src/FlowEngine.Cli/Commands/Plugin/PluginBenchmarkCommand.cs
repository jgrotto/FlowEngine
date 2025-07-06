// Simplified benchmarking without BenchmarkDotNet dependency
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
/// Command to run performance benchmarks on a plugin.
/// </summary>
public class PluginBenchmarkCommand : BaseCommand
{
    /// <inheritdoc />
    public override string Name => "benchmark";

    /// <inheritdoc />
    public override string Description => "Run performance benchmarks on a plugin";

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
        var quick = args.Contains("--quick");

        try
        {
            WriteInfo($"Benchmarking plugin at: {Path.GetFullPath(pluginPath)}");
            if (quick)
            {
                WriteInfo("Running quick benchmark (reduced iterations)");
            }

            var benchmarkResults = await BenchmarkPluginAsync(pluginPath, quick, verbose);

            // Display results
            Console.WriteLine();
            Console.WriteLine("=== Benchmark Results ===");

            if (benchmarkResults.Success)
            {
                WriteSuccess("Benchmark completed successfully!");
                
                Console.WriteLine();
                Console.WriteLine($"Plugin: {benchmarkResults.PluginName}");
                Console.WriteLine($"Total iterations: {benchmarkResults.TotalIterations:N0}");
                Console.WriteLine($"Total execution time: {benchmarkResults.TotalTime.TotalSeconds:F2}s");
                Console.WriteLine($"Average per iteration: {benchmarkResults.AverageIterationTime.TotalMilliseconds:F2}ms");
                Console.WriteLine($"Rows processed: {benchmarkResults.RowsProcessed:N0}");
                Console.WriteLine($"Memory allocated: {benchmarkResults.MemoryAllocated:N0} bytes");
                
                if (benchmarkResults.RowsProcessed > 0 && benchmarkResults.TotalTime.TotalSeconds > 0)
                {
                    var throughput = benchmarkResults.RowsProcessed / benchmarkResults.TotalTime.TotalSeconds;
                    Console.WriteLine($"Throughput: {throughput:N0} rows/sec");
                    
                    // Performance assessment
                    Console.WriteLine();
                    if (throughput >= 200000)
                    {
                        WriteSuccess($"🚀 Excellent performance! Exceeds 200K rows/sec target");
                    }
                    else if (throughput >= 100000)
                    {
                        WriteSuccess($"👍 Good performance! Above 100K rows/sec");
                    }
                    else if (throughput >= 50000)
                    {
                        WriteWarning($"⚡ Moderate performance. Consider optimization for high-throughput scenarios");
                    }
                    else
                    {
                        WriteWarning($"🐌 Performance below 50K rows/sec. Optimization recommended");
                    }
                }

                Console.WriteLine();
                Console.WriteLine("Memory efficiency:");
                var memoryPerRow = benchmarkResults.RowsProcessed > 0 ? 
                    (double)benchmarkResults.MemoryAllocated / benchmarkResults.RowsProcessed : 0;
                Console.WriteLine($"Memory per row: {memoryPerRow:F0} bytes");
                
                if (memoryPerRow < 100)
                {
                    WriteSuccess("🔋 Excellent memory efficiency");
                }
                else if (memoryPerRow < 500)
                {
                    WriteSuccess("✅ Good memory efficiency");
                }
                else if (memoryPerRow < 1000)
                {
                    WriteWarning("⚠️ Moderate memory usage");
                }
                else
                {
                    WriteWarning("🔴 High memory usage - consider optimization");
                }

                return 0;
            }
            else
            {
                WriteError("Benchmark failed!");
                Console.WriteLine($"Error: {benchmarkResults.ErrorMessage}");
                return 1;
            }
        }
        catch (Exception ex)
        {
            WriteError($"Benchmark execution failed: {ex.Message}");
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
        Console.WriteLine("Usage: flowengine plugin benchmark [path] [options]");
        Console.WriteLine();
        Console.WriteLine("Arguments:");
        Console.WriteLine("  path           Path to plugin directory (defaults to current directory)");
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine("  --quick        Run quick benchmark with reduced iterations");
        Console.WriteLine("  --verbose, -v  Verbose output");
        Console.WriteLine("  --help, -h     Show this help");
        Console.WriteLine();
        Console.WriteLine("Examples:");
        Console.WriteLine("  flowengine plugin benchmark");
        Console.WriteLine("  flowengine plugin benchmark ./MyPlugin --quick");
        Console.WriteLine("  flowengine plugin benchmark ./MyPlugin --verbose");
    }

    private static async Task<BenchmarkResult> BenchmarkPluginAsync(string pluginPath, bool quick, bool verbose)
    {
        try
        {
            // Find plugin assembly
            var binPath = Path.Combine(pluginPath, "bin");
            if (!Directory.Exists(binPath))
            {
                return BenchmarkResult.Failed("No bin directory found - run 'dotnet build' first");
            }

            var dllFiles = Directory.GetFiles(binPath, "*.dll", SearchOption.AllDirectories)
                .Where(f => !Path.GetFileName(f).StartsWith("FlowEngine.") && 
                           !Path.GetFileName(f).StartsWith("Microsoft.") &&
                           !Path.GetFileName(f).StartsWith("System."))
                .ToArray();

            if (dllFiles.Length == 0)
            {
                return BenchmarkResult.Failed("No plugin assemblies found - run 'dotnet build' first");
            }

            // Setup FlowEngine services
            var services = new ServiceCollection();
            services.AddLogging(builder => 
            {
                builder.AddConsole();
                builder.SetMinimumLevel(LogLevel.Error); // Minimize logging during benchmarks
            });
            services.AddFlowEngine();

            await using var serviceProvider = services.BuildServiceProvider();
            var pluginLoader = serviceProvider.GetRequiredService<IPluginLoader>();

            // Benchmark each plugin
            var totalIterations = quick ? 100 : 1000;
            var warmupIterations = quick ? 10 : 100;

            foreach (var dllFile in dllFiles)
            {
                if (verbose)
                {
                    Console.WriteLine($"Benchmarking assembly: {dllFile}");
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
                            Console.WriteLine($"  Benchmarking plugin: {pluginType.Name}");
                        }

                        // Load plugin
                        var plugin = await pluginLoader.LoadPluginAsync<IPlugin>(dllFile, pluginType.FullName!);
                        
                        // Initialize plugin
                        await InitializePluginForBenchmarkAsync(plugin, verbose);

                        // Run benchmark
                        return await RunPluginBenchmarkAsync(plugin, totalIterations, warmupIterations, verbose, serviceProvider);
                    }
                }
                catch (Exception ex)
                {
                    return BenchmarkResult.Failed($"Failed to benchmark assembly {Path.GetFileName(dllFile)}: {ex.Message}");
                }
            }

            return BenchmarkResult.Failed("No plugins found to benchmark");
        }
        catch (Exception ex)
        {
            return BenchmarkResult.Failed($"Benchmark setup failed: {ex.Message}");
        }
    }

    private static async Task InitializePluginForBenchmarkAsync(IPlugin plugin, bool verbose)
    {
        var benchmarkConfig = new BenchmarkPluginConfiguration();

        if (verbose)
        {
            Console.WriteLine($"      Initializing plugin for benchmark: {plugin.Name}");
        }

        await plugin.InitializeAsync(benchmarkConfig);
    }

    private static async Task<BenchmarkResult> RunPluginBenchmarkAsync(IPlugin plugin, int totalIterations, int warmupIterations, bool verbose, IServiceProvider serviceProvider)
    {
        try
        {
            if (verbose)
            {
                Console.WriteLine($"      Running warmup ({warmupIterations} iterations)...");
            }

            // Warmup phase
            await RunBenchmarkIterations(plugin, warmupIterations, false, serviceProvider);

            if (verbose)
            {
                Console.WriteLine($"      Running benchmark ({totalIterations} iterations)...");
            }

            // Force garbage collection before benchmark
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            var initialMemory = GC.GetTotalMemory(false);
            var stopwatch = Stopwatch.StartNew();

            // Actual benchmark
            var rowsProcessed = await RunBenchmarkIterations(plugin, totalIterations, verbose, serviceProvider);

            stopwatch.Stop();
            var finalMemory = GC.GetTotalMemory(false);

            var memoryAllocated = Math.Max(0, finalMemory - initialMemory);

            return BenchmarkResult.Succeeded(
                plugin.Name ?? plugin.GetType().Name,
                totalIterations,
                stopwatch.Elapsed,
                TimeSpan.FromTicks(stopwatch.Elapsed.Ticks / totalIterations),
                rowsProcessed,
                memoryAllocated);
        }
        catch (Exception ex)
        {
            return BenchmarkResult.Failed($"Benchmark execution failed: {ex.Message}");
        }
    }

    private static async Task<long> RunBenchmarkIterations(IPlugin plugin, int iterations, bool verbose, IServiceProvider serviceProvider)
    {
        var totalRowsProcessed = 0L;

        // Benchmark different plugin types differently
        if (plugin is ISourcePlugin sourcePlugin)
        {
            totalRowsProcessed = await BenchmarkSourcePluginAsync(sourcePlugin, iterations, verbose, serviceProvider);
        }
        else if (plugin is ITransformPlugin transformPlugin)
        {
            totalRowsProcessed = await BenchmarkTransformPluginAsync(transformPlugin, iterations, verbose, serviceProvider);
        }
        else if (plugin is ISinkPlugin sinkPlugin)
        {
            totalRowsProcessed = await BenchmarkDestinationPluginAsync(sinkPlugin, iterations, verbose, serviceProvider);
        }
        else
        {
            // Generic plugin benchmark
            totalRowsProcessed = await BenchmarkGenericPluginAsync(plugin, iterations, verbose);
        }

        return totalRowsProcessed;
    }

    private static async Task<long> BenchmarkSourcePluginAsync(ISourcePlugin plugin, int iterations, bool verbose, IServiceProvider serviceProvider)
    {
        var rowsProcessed = 0L;

        for (int i = 0; i < iterations; i++)
        {
            var chunkCount = 0;
            await foreach (var chunk in plugin.ProduceAsync(CancellationToken.None))
            {
                rowsProcessed += chunk.RowCount;
                chunkCount++;
                
                // Limit chunks per iteration for consistent benchmarking
                if (chunkCount >= 5) break;
            }
        }

        return rowsProcessed;
    }

    private static async Task<long> BenchmarkTransformPluginAsync(ITransformPlugin plugin, int iterations, bool verbose, IServiceProvider serviceProvider)
    {
        var rowsProcessed = 0L;

        // Create benchmark input chunk with more realistic size
        var testSchema = CreateBenchmarkSchema(serviceProvider);
        var testRows = CreateBenchmarkRows(testSchema, 1000, serviceProvider); // 1K rows per chunk
        var chunkFactory = serviceProvider.GetRequiredService<IChunkFactory>();
        var inputChunk = chunkFactory.CreateChunk(testRows);

        for (int i = 0; i < iterations; i++)
        {
            await foreach (var outputChunk in plugin.TransformAsync(CreateAsyncEnumerable(inputChunk), CancellationToken.None))
            {
                rowsProcessed += outputChunk.RowCount;
            }
        }

        return rowsProcessed;
    }

    private static async Task<long> BenchmarkDestinationPluginAsync(ISinkPlugin plugin, int iterations, bool verbose, IServiceProvider serviceProvider)
    {
        var rowsProcessed = 0L;

        // Create benchmark input chunk
        var testSchema = CreateBenchmarkSchema(serviceProvider);
        var testRows = CreateBenchmarkRows(testSchema, 1000, serviceProvider); // 1K rows per chunk
        var chunkFactory = serviceProvider.GetRequiredService<IChunkFactory>();
        var inputChunk = chunkFactory.CreateChunk(testRows);

        for (int i = 0; i < iterations; i++)
        {
            await plugin.ConsumeAsync(CreateAsyncEnumerable(inputChunk), CancellationToken.None);
            rowsProcessed += inputChunk.RowCount;
        }

        return rowsProcessed;
    }

    private static async Task<long> BenchmarkGenericPluginAsync(IPlugin plugin, int iterations, bool verbose)
    {
        // Basic benchmark for generic plugins
        for (int i = 0; i < iterations; i++)
        {
            // Simulate some work
            await Task.Delay(1, CancellationToken.None);
        }

        return iterations;
    }

    private static ISchema CreateBenchmarkSchema(IServiceProvider serviceProvider)
    {
        var columns = new[]
        {
            new ColumnDefinition { Name = "Id", DataType = typeof(long), IsNullable = false },
            new ColumnDefinition { Name = "Name", DataType = typeof(string), IsNullable = false },
            new ColumnDefinition { Name = "Email", DataType = typeof(string), IsNullable = true },
            new ColumnDefinition { Name = "Age", DataType = typeof(int), IsNullable = true },
            new ColumnDefinition { Name = "Balance", DataType = typeof(decimal), IsNullable = false },
            new ColumnDefinition { Name = "IsActive", DataType = typeof(bool), IsNullable = false },
            new ColumnDefinition { Name = "CreatedAt", DataType = typeof(DateTime), IsNullable = false },
            new ColumnDefinition { Name = "LastLoginAt", DataType = typeof(DateTime), IsNullable = true },
            new ColumnDefinition { Name = "Tags", DataType = typeof(string), IsNullable = true },
            new ColumnDefinition { Name = "Score", DataType = typeof(double), IsNullable = true }
        };
        
        var schemaFactory = serviceProvider.GetRequiredService<ISchemaFactory>();
        return schemaFactory.CreateSchema(columns);
    }

    private static IArrayRow[] CreateBenchmarkRows(ISchema schema, int count, IServiceProvider serviceProvider)
    {
        var rows = new IArrayRow[count];
        var random = new Random(42); // Fixed seed for consistent benchmarks
        var names = new[] { "Alice", "Bob", "Charlie", "Diana", "Eve", "Frank", "Grace", "Henry", "Ivy", "Jack" };
        var domains = new[] { "example.com", "test.org", "sample.net", "demo.co" };
        var tags = new[] { "premium", "standard", "vip", "trial", "beta" };

        var arrayRowFactory = serviceProvider.GetRequiredService<IArrayRowFactory>();
        
        for (int i = 0; i < count; i++)
        {
            var name = names[i % names.Length];
            var email = random.NextDouble() > 0.1 ? $"{name.ToLower()}{i}@{domains[i % domains.Length]}" : null;
            var age = random.NextDouble() > 0.2 ? random.Next(18, 80) : (int?)null;
            var tag = random.NextDouble() > 0.3 ? tags[i % tags.Length] : null;
            var lastLogin = random.NextDouble() > 0.4 ? DateTime.UtcNow.AddDays(-random.Next(365)) : (DateTime?)null;

            var values = new object?[]
            {
                (long)(i + 1),
                $"{name} {i + 1}",
                email,
                age,
                (decimal)(random.NextDouble() * 10000),
                random.NextDouble() > 0.2,
                DateTime.UtcNow.AddDays(-random.Next(1000)),
                lastLogin,
                tag,
                random.NextDouble() * 100
            };

            rows[i] = arrayRowFactory.CreateRow(schema, values);
        }

        return rows;
    }

    private class BenchmarkResult
    {
        public required bool Success { get; init; }
        public required string PluginName { get; init; }
        public required int TotalIterations { get; init; }
        public required TimeSpan TotalTime { get; init; }
        public required TimeSpan AverageIterationTime { get; init; }
        public required long RowsProcessed { get; init; }
        public required long MemoryAllocated { get; init; }
        public string? ErrorMessage { get; init; }

        public static BenchmarkResult Succeeded(string pluginName, int totalIterations, TimeSpan totalTime, TimeSpan averageTime, long rowsProcessed, long memoryAllocated) =>
            new() 
            { 
                Success = true, 
                PluginName = pluginName,
                TotalIterations = totalIterations,
                TotalTime = totalTime, 
                AverageIterationTime = averageTime, 
                RowsProcessed = rowsProcessed,
                MemoryAllocated = memoryAllocated
            };

        public static BenchmarkResult Failed(string errorMessage) =>
            new() 
            { 
                Success = false, 
                PluginName = string.Empty,
                TotalIterations = 0,
                TotalTime = TimeSpan.Zero, 
                AverageIterationTime = TimeSpan.Zero, 
                RowsProcessed = 0,
                MemoryAllocated = 0,
                ErrorMessage = errorMessage 
            };
    }

    private class BenchmarkPluginConfiguration : IPluginConfiguration
    {
        private readonly Dictionary<string, object> _values = new()
        {
            ["connectionString"] = "benchmark-connection",
            ["batchSize"] = 1000,
            ["timeout"] = 60,
            ["cacheSize"] = 10000,
            ["enableOptimizations"] = true
        };

        public string PluginId => "benchmark-plugin";
        public string Name => "Benchmark Plugin";
        public string Version => "1.0.0";
        public string PluginType => "Benchmark";
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