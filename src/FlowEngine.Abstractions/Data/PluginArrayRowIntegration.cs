using FlowEngine.Abstractions.Plugins;
using Microsoft.Extensions.DependencyInjection;

namespace FlowEngine.Abstractions.Data;

/// <summary>
/// Demonstrates how plugins can integrate with ArrayRow functionality
/// through dependency injection and abstractions only.
/// </summary>
public static class PluginArrayRowIntegration
{
    /// <summary>
    /// Example of how a plugin service should be constructed to receive ArrayRow factory.
    /// This pattern allows plugins to create and manipulate ArrayRow instances without
    /// directly referencing FlowEngine.Core.
    /// </summary>
    public class ExamplePluginService : IPluginService
    {
        private readonly IArrayRowFactory _arrayRowFactory;
        private readonly ILogger _logger;

        // Constructor showing proper dependency injection for plugins
        public ExamplePluginService(IArrayRowFactory arrayRowFactory, ILogger logger)
        {
            _arrayRowFactory = arrayRowFactory ?? throw new ArgumentNullException(nameof(arrayRowFactory));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task InitializeAsync(CancellationToken cancellationToken = default)
        {
            _logger.LogInformation("Plugin service initialized with ArrayRow factory support");
            await Task.CompletedTask;
        }

        public async Task StartAsync(CancellationToken cancellationToken = default)
        {
            _logger.LogInformation("Plugin service started");
            await Task.CompletedTask;
        }

        public async Task StopAsync(CancellationToken cancellationToken = default)
        {
            _logger.LogInformation("Plugin service stopped");
            await Task.CompletedTask;
        }

        public async Task<IArrayRow> ProcessRowAsync(IArrayRow row, CancellationToken cancellationToken = default)
        {
            // Example: Transform a row by adding a computed field
            var modifiedRow = row.With("processed_at", DateTimeOffset.UtcNow);
            
            await Task.CompletedTask;
            return modifiedRow;
        }

        public async Task<ServiceHealth> CheckHealthAsync(CancellationToken cancellationToken = default)
        {
            return new ServiceHealth
            {
                IsHealthy = true,
                Status = HealthStatus.Healthy,
                Message = "Plugin service is healthy",
                CheckedAt = DateTimeOffset.UtcNow
            };
        }

        /// <summary>
        /// Example method showing how to create ArrayRow instances in plugins.
        /// </summary>
        public IEnumerable<IArrayRow> CreateSampleData(ISchema schema)
        {
            // Method 1: Create from arrays (fastest for known data)
            yield return _arrayRowFactory.Create(schema, new object?[] { 1, "Sample", 100 });

            // Method 2: Create from dictionary (convenient for dynamic data)
            yield return _arrayRowFactory.CreateFromDictionary(schema, new Dictionary<string, object?>
            {
                ["id"] = 2,
                ["name"] = "Dynamic",
                ["value"] = 200
            });

            // Method 3: Create using builder (best for partial data)
            yield return _arrayRowFactory.CreateBuilder(schema)
                .Set("id", 3)
                .Set("name", "Builder")
                .Set("value", 300)
                .Build();
        }

        /// <summary>
        /// Example of high-performance data processing with pre-calculated indexes.
        /// This is the pattern plugins should use for optimal performance.
        /// </summary>
        public async Task<ProcessingMetrics> ProcessDataOptimized(
            IAsyncEnumerable<IArrayRow> inputRows,
            ISchema schema,
            CancellationToken cancellationToken = default)
        {
            // PRE-CALCULATE INDEXES - This is critical for performance!
            var idIndex = schema.GetIndex("id");
            var nameIndex = schema.GetIndex("name");
            var valueIndex = schema.GetIndex("value");

            var processedCount = 0L;
            var totalValue = 0L;
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();

            await foreach (var row in inputRows.WithCancellation(cancellationToken))
            {
                // Use index-based access for O(1) performance
                var id = row.GetInt32(idIndex);
                var name = row.GetString(nameIndex);
                var value = row.GetInt32(valueIndex);

                // Process the data (example business logic)
                var processedValue = value * 2;
                
                // Create output row with computed fields
                var outputRow = _arrayRowFactory.CreateBuilder(schema)
                    .Set(idIndex, id)
                    .Set(nameIndex, $"Processed_{name}")
                    .Set(valueIndex, processedValue)
                    .Build();

                processedCount++;
                totalValue += processedValue;

                // Example: yield return outputRow; (in a real transform plugin)
            }

            stopwatch.Stop();

            return new ProcessingMetrics
            {
                ProcessedRows = processedCount,
                TotalValue = totalValue,
                ProcessingTime = stopwatch.Elapsed,
                RowsPerSecond = processedCount / stopwatch.Elapsed.TotalSeconds
            };
        }

        public void Dispose()
        {
            _logger.LogInformation("Plugin service disposed");
        }
    }

    /// <summary>
    /// Example of how to properly configure a plugin for ArrayRow access.
    /// This would typically be in the plugin's startup configuration.
    /// </summary>
    public static class PluginServiceConfiguration
    {
        /// <summary>
        /// Configures services for a plugin that needs ArrayRow access.
        /// This is the pattern plugin assemblies should follow.
        /// </summary>
        /// <param name="services">Service collection</param>
        /// <returns>Configured service collection</returns>
        public static IServiceCollection ConfigurePluginServices(IServiceCollection services)
        {
            // Register plugin services
            services.AddSingleton<ExamplePluginService>();
            
            // Note: IArrayRowFactory will be provided by the host application
            // Plugins should NOT register it themselves
            
            return services;
        }
    }

    /// <summary>
    /// Example of ArrayRow patterns for different plugin types.
    /// </summary>
    public static class PluginPatterns
    {
        /// <summary>
        /// Source Plugin Pattern: Generate ArrayRow instances from external data.
        /// </summary>
        public static async IAsyncEnumerable<IArrayRow> SourcePluginPattern(
            IArrayRowFactory factory,
            ISchema outputSchema,
            string dataSource)
        {
            // Pre-calculate field indexes for output schema
            var fieldIndexes = outputSchema.Columns
                .Select((col, index) => new { col.Name, Index = index })
                .ToDictionary(x => x.Name, x => x.Index);

            // Simulate reading from external source
            var lines = await File.ReadAllLinesAsync(dataSource);
            
            foreach (var line in lines)
            {
                var fields = line.Split(',');
                
                // Create ArrayRow with optimized field assignment
                var builder = factory.CreateBuilder(outputSchema);
                
                for (int i = 0; i < Math.Min(fields.Length, outputSchema.ColumnCount); i++)
                {
                    builder.Set(i, fields[i].Trim());
                }
                
                yield return builder.Build();
            }
        }

        /// <summary>
        /// Transform Plugin Pattern: Modify existing ArrayRow instances.
        /// </summary>
        public static IArrayRow TransformPluginPattern(
            IArrayRow inputRow,
            ISchema outputSchema,
            IArrayRowFactory factory)
        {
            // Pre-calculate indexes for both schemas
            var inputIdIndex = inputRow.Schema.GetIndex("id");
            var inputNameIndex = inputRow.Schema.GetIndex("name");
            
            // Extract input data with O(1) access
            var id = inputRow.GetInt32(inputIdIndex);
            var name = inputRow.GetString(inputNameIndex);
            
            // Apply transformation logic
            var transformedName = name.ToUpper();
            var timestamp = DateTimeOffset.UtcNow;
            
            // Create output row with transformed data
            return factory.CreateBuilder(outputSchema)
                .Set("id", id)
                .Set("transformed_name", transformedName)
                .Set("processed_at", timestamp)
                .Build();
        }

        /// <summary>
        /// Sink Plugin Pattern: Extract data from ArrayRow instances for output.
        /// </summary>
        public static async Task<bool> SinkPluginPattern(
            IAsyncEnumerable<IArrayRow> inputRows,
            string outputDestination,
            CancellationToken cancellationToken = default)
        {
            var processedCount = 0;
            
            await using var writer = new StreamWriter(outputDestination);
            
            await foreach (var row in inputRows.WithCancellation(cancellationToken))
            {
                // Extract data using optimized access patterns
                var values = new string[row.ColumnCount];
                var span = row.AsSpan();
                
                for (int i = 0; i < values.Length; i++)
                {
                    values[i] = span[i]?.ToString() ?? string.Empty;
                }
                
                // Write to output destination
                await writer.WriteLineAsync(string.Join(",", values));
                processedCount++;
            }
            
            return processedCount > 0;
        }
    }
}

/// <summary>
/// Example processing metrics for demonstration.
/// </summary>
public sealed record ProcessingMetrics
{
    public required long ProcessedRows { get; init; }
    public required long TotalValue { get; init; }
    public required TimeSpan ProcessingTime { get; init; }
    public required double RowsPerSecond { get; init; }
}

/// <summary>
/// Placeholder logger interface for example (plugins would use Microsoft.Extensions.Logging).
/// </summary>
public interface ILogger
{
    void LogInformation(string message);
}

/// <summary>
/// Extension methods for working with async enumerables and cancellation.
/// </summary>
public static class AsyncEnumerableExtensions
{
    public static IAsyncEnumerable<T> WithCancellation<T>(this IAsyncEnumerable<T> source, CancellationToken cancellationToken)
    {
        return source; // Simplified for example
    }
}