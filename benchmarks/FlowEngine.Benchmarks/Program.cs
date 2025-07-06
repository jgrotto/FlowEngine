using BenchmarkDotNet.Running;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Environments;
using BenchmarkDotNet.Exporters;
using BenchmarkDotNet.Loggers;
using FlowEngine.Benchmarks.DataStructures;
using FlowEngine.Benchmarks.Ports;
using FlowEngine.Benchmarks.Memory;
using FlowEngine.Benchmarks.Pipeline;
using FlowEngine.Benchmarks.Integration;

namespace FlowEngine.Benchmarks;

/// <summary>
/// FlowEngine Performance Prototype Benchmark Runner
/// 
/// This application validates core design assumptions through focused performance testing.
/// Goal: Provide data-driven decisions for production implementation.
/// </summary>
public class Program
{
    public static void Main(string[] args)
    {
        var config = ManualConfig.Create(DefaultConfig.Instance)
            .WithOptions(ConfigOptions.DisableOptimizationsValidator)
            .AddJob(Job.Default)
            .AddExporter(MarkdownExporter.GitHub)
            .AddExporter(HtmlExporter.Default)
            .AddLogger(ConsoleLogger.Default);

        if (args.Length == 0)
        {
            Console.WriteLine("FlowEngine Performance Prototype");
            Console.WriteLine("================================");
            Console.WriteLine();
            Console.WriteLine("Available benchmark suites:");
            Console.WriteLine("  simple    - Simple functionality verification (no benchmarking)");
            Console.WriteLine("  quick     - Quick verification tests (fast)");
            Console.WriteLine("  row       - Row implementation performance");
            Console.WriteLine("  ports     - Port communication performance");
            Console.WriteLine("  memory    - Memory management performance");
            Console.WriteLine("  pipeline  - End-to-end pipeline performance");
            Console.WriteLine("  streaming - Streaming pipeline performance");
            Console.WriteLine("  memory-eff - Memory efficiency comparisons");
            Console.WriteLine("  cross-platform - Cross-platform path handling");
            Console.WriteLine("  scaling   - Staged scaling tests (10K → 100K → 1M)");
            Console.WriteLine("  chunked   - Chunked processing benchmarks");
            Console.WriteLine("  plugins   - Plugin data processing performance benchmarks");
            Console.WriteLine("  all       - Run all benchmarks");
            Console.WriteLine();
            Console.WriteLine("Usage: dotnet run -- <suite>");
            Console.WriteLine("Example: dotnet run -- row");
            return;
        }

        var suite = args[0].ToLowerInvariant();
        switch (suite)
        {
            case "simple":
                SimpleTest.RunAll();
                break;
                
            case "quick":
                BenchmarkRunner.Run<QuickVerificationTests>(config);
                break;
                
            case "row":
                BenchmarkRunner.Run<RowBenchmarks>(config);
                break;
                
            case "ports":
                BenchmarkRunner.Run<StreamingBenchmarks>(config);
                break;
                
            case "memory":
                BenchmarkRunner.Run<MemoryPressureBenchmarks>(config);
                BenchmarkRunner.Run<PoolingBenchmarks>(config);
                break;
                
            case "pipeline":
                BenchmarkRunner.Run<EndToEndPipelineBenchmarks>(config);
                break;

            case "streaming":
                BenchmarkRunner.Run<StreamingPipelineBenchmarks>(config);
                break;

            case "memory-eff":
                BenchmarkRunner.Run<MemoryEfficiencyBenchmarks>(config);
                break;

            case "cross-platform":
                BenchmarkRunner.Run<CrossPlatformPathBenchmarks>(config);
                break;

            case "scaling":
                Console.WriteLine("=== FlowEngine Staged Scaling Analysis ===");
                PerformanceAnalyzer.AnalyzeScalingResults();
                PerformanceAnalyzer.PrintDecisionMatrix();
                BenchmarkRunner.Run<StagedScalingBenchmarks>(config);
                break;

            case "chunked":
                BenchmarkRunner.Run<ChunkedProcessingBenchmarks>(config);
                break;

            case "plugins":
                Console.WriteLine("=== Plugin Performance Analysis ===");
                Console.WriteLine("Target: 200K+ rows/sec for plugin processing");
                BenchmarkRunner.Run<PluginPerformanceBenchmarks>(config);
                break;
                
            case "all":
                BenchmarkRunner.Run<RowBenchmarks>(config);
                BenchmarkRunner.Run<StreamingBenchmarks>(config);
                BenchmarkRunner.Run<MemoryPressureBenchmarks>(config);
                BenchmarkRunner.Run<PoolingBenchmarks>(config);
                BenchmarkRunner.Run<EndToEndPipelineBenchmarks>(config);
                BenchmarkRunner.Run<StreamingPipelineBenchmarks>(config);
                BenchmarkRunner.Run<MemoryEfficiencyBenchmarks>(config);
                BenchmarkRunner.Run<CrossPlatformPathBenchmarks>(config);
                BenchmarkRunner.Run<PluginPerformanceBenchmarks>(config);
                break;
                
            default:
                Console.WriteLine($"Unknown benchmark suite: {suite}");
                break;
        }
    }
}
