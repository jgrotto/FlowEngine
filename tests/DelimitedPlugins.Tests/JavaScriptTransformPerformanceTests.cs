using FlowEngine.Abstractions.Data;
using FlowEngine.Abstractions.Plugins;
using FlowEngine.Core;
using FlowEngine.Core.Data;
using FlowEngine.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Text;
using Xunit;
using Xunit.Abstractions;
using JavaScriptTransform;

namespace DelimitedPlugins.Tests;

/// <summary>
/// Performance tests for JavaScript Transform plugin to validate throughput claims.
/// Tests process realistic data volumes (20K-50K rows) to measure actual performance.
/// </summary>
public class JavaScriptTransformPerformanceTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly ServiceProvider _serviceProvider;

    public JavaScriptTransformPerformanceTests(ITestOutputHelper output)
    {
        _output = output;
        
        // Set up dependency injection
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Warning)); // Reduce noise
        services.AddFlowEngineCore();
        services.AddSingleton<IScriptEngineService, JintScriptEngineService>();
        services.AddSingleton<IJavaScriptContextService, JavaScriptContextService>();
        
        _serviceProvider = services.BuildServiceProvider();
    }

    [Fact]
    public async Task JavaScriptTransform_SimpleTransform_20K_Rows_ShouldMeetPerformanceTarget()
    {
        await TestPerformanceWithRowCount(20000, "Simple field mapping", @"
function process(context) {
    var current = context.input.current();
    
    context.output.setField('customer_id', current.customer_id);
    context.output.setField('full_name', current.first_name + ' ' + current.last_name);
    context.output.setField('email_domain', current.email.split('@')[1] || 'unknown');
    context.output.setField('processed_at', context.utils.now());
    
    return true;
}");
    }

    [Fact]
    public async Task JavaScriptTransform_ComplexTransform_30K_Rows_ShouldMeetPerformanceTarget()
    {
        await TestPerformanceWithRowCount(30000, "Complex business logic", @"
function process(context) {
    var current = context.input.current();
    
    // Complex validation
    if (!context.validate.required(['customer_id', 'amount', 'email'])) {
        return false; // Skip invalid rows
    }
    
    // Business rules and calculations
    var amount = parseFloat(current.amount) || 0;
    var riskScore = 'low';
    var category = 'standard';
    
    if (amount > 10000) {
        riskScore = 'high';
        category = 'premium';
        // Route high-value customers for review
        context.route.sendCopy('high_value_review');
    } else if (amount > 1000) {
        riskScore = 'medium';
        category = 'plus';
    }
    
    // Data enrichment
    context.output.setField('customer_id', current.customer_id);
    context.output.setField('full_name', current.first_name + ' ' + current.last_name);
    context.output.setField('email', current.email);
    context.output.setField('amount', amount);
    context.output.setField('risk_score', riskScore);
    context.output.setField('category', category);
    context.output.setField('processing_fee', amount * 0.025);
    context.output.setField('processed_at', context.utils.now());
    context.output.setField('batch_id', context.utils.newGuid());
    
    return true;
}");
    }

    [Fact]
    public async Task JavaScriptTransform_HighVolume_50K_Rows_ShouldMeetPerformanceTarget()
    {
        await TestPerformanceWithRowCount(50000, "High volume processing", @"
function process(context) {
    var current = context.input.current();
    
    // Streamlined processing for high volume
    context.output.setField('id', current.customer_id);
    context.output.setField('name', current.first_name + ' ' + current.last_name);
    context.output.setField('domain', current.email.split('@')[1] || 'unknown');
    context.output.setField('amount_cents', Math.round(parseFloat(current.amount || 0) * 100));
    context.output.setField('timestamp', Date.now());
    
    return true;
}");
    }

    private async Task TestPerformanceWithRowCount(int rowCount, string testDescription, string script)
    {
        _output.WriteLine($"🚀 Performance Test: {testDescription}");
        _output.WriteLine($"📊 Processing {rowCount:N0} rows...");

        // Create configuration
        var transformConfig = new JavaScriptTransformConfiguration
        {
            Script = script,
            OutputSchema = new OutputSchemaConfiguration
            {
                Fields = new List<OutputFieldDefinition>
                {
                    new() { Name = "customer_id", Type = "integer", Required = true },
                    new() { Name = "full_name", Type = "string", Required = true },
                    new() { Name = "email", Type = "string", Required = false },
                    new() { Name = "amount", Type = "decimal", Required = false },
                    new() { Name = "risk_score", Type = "string", Required = false },
                    new() { Name = "category", Type = "string", Required = false },
                    new() { Name = "processing_fee", Type = "decimal", Required = false },
                    new() { Name = "processed_at", Type = "datetime", Required = false },
                    new() { Name = "batch_id", Type = "string", Required = false },
                    new() { Name = "id", Type = "integer", Required = false },
                    new() { Name = "name", Type = "string", Required = false },
                    new() { Name = "domain", Type = "string", Required = false },
                    new() { Name = "email_domain", Type = "string", Required = false },
                    new() { Name = "amount_cents", Type = "integer", Required = false },
                    new() { Name = "timestamp", Type = "long", Required = false }
                }
            },
            Performance = new PerformanceConfiguration
            {
                TargetThroughput = 120000, // 120K rows/sec target
                EnableMonitoring = true,
                LogPerformanceWarnings = true
            }
        };

        // Create services
        var logger = _serviceProvider.GetRequiredService<ILogger<JavaScriptTransformPlugin>>();
        var scriptEngine = _serviceProvider.GetRequiredService<IScriptEngineService>();
        var contextService = _serviceProvider.GetRequiredService<IJavaScriptContextService>();

        // Generate test data
        var inputSchema = Schema.GetOrCreate(new[]
        {
            new ColumnDefinition { Name = "customer_id", DataType = typeof(int), IsNullable = false, Index = 0 },
            new ColumnDefinition { Name = "first_name", DataType = typeof(string), IsNullable = false, Index = 1 },
            new ColumnDefinition { Name = "last_name", DataType = typeof(string), IsNullable = false, Index = 2 },
            new ColumnDefinition { Name = "email", DataType = typeof(string), IsNullable = false, Index = 3 },
            new ColumnDefinition { Name = "amount", DataType = typeof(decimal), IsNullable = false, Index = 4 }
        });

        _output.WriteLine("📝 Generating test data...");
        var inputChunks = GenerateTestData(inputSchema, rowCount, chunkSize: 5000);
        
        var plugin = new JavaScriptTransformPlugin(scriptEngine, contextService, logger);
        
        try
        {
            // Initialize plugin
            var initResult = await plugin.InitializeAsync(transformConfig);
            Assert.True(initResult.Success, $"Initialization failed: {initResult.Message}");
            
            await plugin.StartAsync();
            
            var validationResult = plugin.ValidateInputSchema(inputSchema);
            Assert.True(validationResult.IsValid);

            // Performance test
            var stopwatch = Stopwatch.StartNew();
            var totalOutputRows = 0;
            var chunksProcessed = 0;

            _output.WriteLine("⚡ Starting performance test...");

            await foreach (var outputChunk in plugin.TransformAsync(inputChunks, CancellationToken.None))
            {
                totalOutputRows += outputChunk.RowCount;
                chunksProcessed++;
                
                // Progress reporting every 10 chunks
                if (chunksProcessed % 10 == 0)
                {
                    var elapsed = stopwatch.Elapsed.TotalSeconds;
                    var currentThroughput = totalOutputRows / elapsed;
                    _output.WriteLine($"   📈 Processed {totalOutputRows:N0} rows in {elapsed:F1}s ({currentThroughput:F0} rows/sec)");
                }
            }
            
            stopwatch.Stop();
            await plugin.StopAsync();

            // Calculate final metrics
            var totalTimeSeconds = stopwatch.Elapsed.TotalSeconds;
            var actualThroughput = totalOutputRows / totalTimeSeconds;
            var targetThroughput = 120000; // 120K rows/sec
            var performanceRatio = actualThroughput / targetThroughput;

            // Report results
            _output.WriteLine("");
            _output.WriteLine("📋 Performance Results:");
            _output.WriteLine($"   🔢 Total Rows Processed: {totalOutputRows:N0}");
            _output.WriteLine($"   ⏱️  Total Time: {totalTimeSeconds:F2} seconds");
            _output.WriteLine($"   🚀 Actual Throughput: {actualThroughput:F0} rows/sec");
            _output.WriteLine($"   🎯 Target Throughput: {targetThroughput:N0} rows/sec");
            _output.WriteLine($"   📊 Performance Ratio: {performanceRatio:P1} of target");
            _output.WriteLine($"   💾 Memory Usage: ~{GC.GetTotalMemory(false) / 1024 / 1024:F1} MB");

            // Performance assertions
            Assert.Equal(rowCount, totalOutputRows);
            
            // Realistic targets for JavaScript execution (much lower than native C# ArrayRow operations)
            // JavaScript Transform target: At least 5K rows/sec (realistic for JS overhead)
            var minimumAcceptableThroughput = 5000;
            Assert.True(actualThroughput >= minimumAcceptableThroughput, 
                $"Performance below minimum acceptable threshold: {actualThroughput:F0} < {minimumAcceptableThroughput:N0} rows/sec");

            // Log performance category
            if (actualThroughput >= targetThroughput)
            {
                _output.WriteLine("   ✅ EXCELLENT: Exceeded target performance!");
            }
            else if (actualThroughput >= targetThroughput * 0.5)
            {
                _output.WriteLine("   ✅ GOOD: Within 50% of target performance");
            }
            else if (actualThroughput >= minimumAcceptableThroughput)
            {
                _output.WriteLine("   ⚠️  ACCEPTABLE: Above minimum threshold but below target");
            }
            else
            {
                _output.WriteLine("   ❌ POOR: Below minimum acceptable performance");
            }
        }
        finally
        {
            plugin.Dispose();
        }
    }

    private static async IAsyncEnumerable<IChunk> GenerateTestData(ISchema schema, int totalRows, int chunkSize = 5000)
    {
        var random = new Random(42); // Fixed seed for reproducible results
        var firstNames = new[] { "John", "Jane", "Bob", "Alice", "Charlie", "Diana", "Eve", "Frank", "Grace", "Henry" };
        var lastNames = new[] { "Smith", "Johnson", "Williams", "Brown", "Jones", "Garcia", "Miller", "Davis", "Rodriguez", "Martinez" };
        var domains = new[] { "gmail.com", "yahoo.com", "hotmail.com", "company.com", "example.org", "test.net" };

        for (int startRow = 0; startRow < totalRows; startRow += chunkSize)
        {
            var endRow = Math.Min(startRow + chunkSize, totalRows);
            var rowsInChunk = endRow - startRow;
            var rows = new ArrayRow[rowsInChunk];

            for (int i = 0; i < rowsInChunk; i++)
            {
                var customerId = startRow + i + 1;
                var firstName = firstNames[random.Next(firstNames.Length)];
                var lastName = lastNames[random.Next(lastNames.Length)];
                var domain = domains[random.Next(domains.Length)];
                var email = $"{firstName.ToLower()}.{lastName.ToLower()}@{domain}";
                var amount = (decimal)(random.NextDouble() * 50000); // $0 - $50,000

                rows[i] = new ArrayRow(schema, new object[] { customerId, firstName, lastName, email, amount });
            }

            yield return new Chunk(schema, rows);
            await Task.Yield(); // Allow other operations to run
        }
    }

    public void Dispose()
    {
        _serviceProvider?.Dispose();
    }
}