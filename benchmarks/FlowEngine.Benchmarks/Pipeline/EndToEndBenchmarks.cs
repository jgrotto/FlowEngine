using BenchmarkDotNet.Attributes;
using FlowEngine.Benchmarks.DataStructures;
using FlowEngine.Benchmarks.Ports;
using System.Text;

namespace FlowEngine.Benchmarks.Pipeline;

/// <summary>
/// Mock CSV data source for generating realistic test data
/// </summary>
public class MockCsvSource
{
    private readonly Random _random = new Random(42); // Fixed seed for consistent benchmarks
    private readonly string[] _firstNames = { "John", "Jane", "Bob", "Alice", "Charlie", "Diana", "Frank", "Grace" };
    private readonly string[] _lastNames = { "Smith", "Johnson", "Williams", "Brown", "Jones", "Garcia", "Miller", "Davis" };
    private readonly string[] _departments = { "Engineering", "Sales", "Marketing", "HR", "Finance", "Operations" };
    private readonly string[] _cities = { "New York", "Los Angeles", "Chicago", "Houston", "Phoenix", "Philadelphia" };

    public IEnumerable<Dictionary<string, object?>> GenerateEmployeeData(int count)
    {
        for (int i = 0; i < count; i++)
        {
            yield return new Dictionary<string, object?>
            {
                ["id"] = i + 1,
                ["first_name"] = _firstNames[_random.Next(_firstNames.Length)],
                ["last_name"] = _lastNames[_random.Next(_lastNames.Length)],
                ["email"] = $"employee{i + 1}@company.com",
                ["department"] = _departments[_random.Next(_departments.Length)],
                ["salary"] = _random.Next(40000, 150000),
                ["hire_date"] = DateTime.Now.AddDays(-_random.Next(1, 3650)).ToString("yyyy-MM-dd"),
                ["city"] = _cities[_random.Next(_cities.Length)],
                ["is_active"] = _random.NextDouble() > 0.1, // 90% active
                ["performance_score"] = Math.Round(_random.NextDouble() * 5, 2) // 0-5 score
            };
        }
    }

    public IEnumerable<Dictionary<string, object?>> GenerateTransactionData(int count)
    {
        for (int i = 0; i < count; i++)
        {
            yield return new Dictionary<string, object?>
            {
                ["transaction_id"] = Guid.NewGuid().ToString(),
                ["customer_id"] = _random.Next(1, 10000),
                ["product_id"] = _random.Next(1, 1000),
                ["quantity"] = _random.Next(1, 10),
                ["unit_price"] = Math.Round(_random.NextDouble() * 100 + 5, 2),
                ["transaction_date"] = DateTime.Now.AddDays(-_random.Next(1, 365)).ToString("yyyy-MM-dd HH:mm:ss"),
                ["payment_method"] = new[] { "Credit Card", "Debit Card", "Cash", "PayPal" }[_random.Next(4)],
                ["discount_percent"] = _random.NextDouble() > 0.7 ? Math.Round(_random.NextDouble() * 20, 2) : 0,
                ["tax_amount"] = Math.Round(_random.NextDouble() * 10, 2),
                ["shipping_cost"] = _random.NextDouble() > 0.3 ? Math.Round(_random.NextDouble() * 15 + 5, 2) : 0
            };
        }
    }
}

/// <summary>
/// Simulates a data transformation step that processes employee data
/// </summary>
public class EmployeeTransformStep
{
    public IRow ProcessEmployee(IRow employee)
    {
        // Simulate business logic transformations
        var salary = Convert.ToDouble(employee["salary"]);
        var hireDate = DateTime.Parse(employee["hire_date"]!.ToString()!);
        var performanceScore = Convert.ToDouble(employee["performance_score"]);
        
        // Calculate derived fields
        var yearsOfService = (DateTime.Now - hireDate).TotalDays / 365.25;
        var salaryGrade = salary switch
        {
            < 50000 => "Junior",
            < 80000 => "Mid-level",
            < 120000 => "Senior",
            _ => "Executive"
        };
        
        var bonusEligible = performanceScore >= 3.5 && yearsOfService >= 1.0;
        
        // Return transformed row with additional fields
        return employee
            .With("years_of_service", Math.Round(yearsOfService, 1))
            .With("salary_grade", salaryGrade)
            .With("bonus_eligible", bonusEligible)
            .With("full_name", $"{employee["first_name"]} {employee["last_name"]}")
            .With("processed_date", DateTime.UtcNow.ToString("yyyy-MM-dd"));
    }
}

/// <summary>
/// Simulates a data aggregation step that groups and summarizes data
/// </summary>
public class DepartmentAggregationStep
{
    public Dictionary<string, object> AggregateByDepartment(IEnumerable<IRow> employees)
    {
        var departmentStats = employees
            .GroupBy(emp => emp["department"]?.ToString())
            .ToDictionary(
                group => group.Key ?? "Unknown",
                group => new
                {
                    Count = group.Count(),
                    AverageSalary = group.Average(emp => Convert.ToDouble(emp["salary"])),
                    AveragePerformance = group.Average(emp => Convert.ToDouble(emp["performance_score"])),
                    BonusEligibleCount = group.Count(emp => Convert.ToBoolean(emp["bonus_eligible"]))
                }
            );

        return new Dictionary<string, object>
        {
            ["total_employees"] = employees.Count(),
            ["department_stats"] = departmentStats,
            ["processing_timestamp"] = DateTime.UtcNow
        };
    }
}

/// <summary>
/// End-to-end pipeline benchmarks testing realistic data processing scenarios
/// </summary>
[MemoryDiagnoser]
[SimpleJob(iterationCount: 3, warmupCount: 1)]
public class EndToEndPipelineBenchmarks
{
    private MockCsvSource _csvSource = null!;
    private EmployeeTransformStep _transformStep = null!;
    private DepartmentAggregationStep _aggregationStep = null!;
    
    [Params(1000, 5000, 10000)]
    public int RecordCount { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _csvSource = new MockCsvSource();
        _transformStep = new EmployeeTransformStep();
        _aggregationStep = new DepartmentAggregationStep();
    }

    [Benchmark(Baseline = true)]
    public Dictionary<string, object> DictionaryRowPipeline()
    {
        var sourceData = _csvSource.GenerateEmployeeData(RecordCount);
        
        // Convert to DictionaryRow and process through pipeline
        var processedRows = sourceData
            .Select(data => new DictionaryRow(data))
            .Cast<IRow>()
            .Select(row => _transformStep.ProcessEmployee(row))
            .ToList();
        
        return _aggregationStep.AggregateByDepartment(processedRows);
    }

    [Benchmark]
    public Dictionary<string, object> ArrayRowPipeline()
    {
        var sourceData = _csvSource.GenerateEmployeeData(RecordCount);
        
        // Create schema and convert to ArrayRow
        var firstRecord = sourceData.First();
        var schema = Schema.GetOrCreate(firstRecord.Keys.ToArray());
        
        var processedRows = sourceData
            .Select(data => new ArrayRow(schema, data.Values.ToArray()))
            .Cast<IRow>()
            .Select(row => _transformStep.ProcessEmployee(row))
            .ToList();
        
        return _aggregationStep.AggregateByDepartment(processedRows);
    }

    [Benchmark]
    public Dictionary<string, object> ImmutableRowPipeline()
    {
        var sourceData = _csvSource.GenerateEmployeeData(RecordCount);
        
        var processedRows = sourceData
            .Select(data => new ImmutableRow(data))
            .Cast<IRow>()
            .Select(row => _transformStep.ProcessEmployee(row))
            .ToList();
        
        return _aggregationStep.AggregateByDepartment(processedRows);
    }

    [Benchmark]
    public Dictionary<string, object> PooledRowPipeline()
    {
        var sourceData = _csvSource.GenerateEmployeeData(RecordCount);
        var processedRows = new List<IRow>();
        
        foreach (var data in sourceData)
        {
            using var row = PooledRow.Create(data);
            var transformed = _transformStep.ProcessEmployee(row);
            processedRows.Add(transformed);
        }
        
        return _aggregationStep.AggregateByDepartment(processedRows);
    }
}

/// <summary>
/// Streaming pipeline benchmarks that test port communication under load
/// </summary>
[MemoryDiagnoser]
[SimpleJob(iterationCount: 3, warmupCount: 1)]
public class StreamingPipelineBenchmarks
{
    private MockCsvSource _csvSource = null!;
    
    [Params(1000, 5000)]
    public int RecordCount { get; set; }
    
    [Params(100, 500)]
    public int ChunkSize { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _csvSource = new MockCsvSource();
    }

    [Benchmark(Baseline = true)]
    public async Task<int> ChannelPortStreaming()
    {
        using var sourcePort = new ChannelPort();
        using var sinkPort = new ChannelPort();
        sourcePort.ConnectTo(sinkPort);

        var processedCount = 0;
        
        // Start consumer task
        var consumerTask = Task.Run(async () =>
        {
            await foreach (var dataset in sinkPort.ReceiveAsync())
            {
                processedCount += dataset.RowCount;
            }
        });

        // Start producer task
        var producerTask = Task.Run(async () =>
        {
            var sourceData = _csvSource.GenerateEmployeeData(RecordCount);
            var chunks = sourceData.Chunk(ChunkSize);
            
            foreach (var chunk in chunks)
            {
                var dataset = Dataset.Create(chunk.Length, $"chunk-{processedCount}");
                await sourcePort.SendAsync(dataset);
            }
            
            sourcePort.Complete();
        });

        await Task.WhenAll(producerTask, consumerTask);
        return processedCount;
    }

    [Benchmark]
    public async Task<int> CallbackPortStreaming()
    {
        var sourcePort = new CallbackPort();
        var processedCount = 0;
        
        sourcePort.Subscribe(async dataset =>
        {
            Interlocked.Add(ref processedCount, dataset.RowCount);
            await Task.Yield(); // Simulate async processing
        });

        var sourceData = _csvSource.GenerateEmployeeData(RecordCount);
        var chunks = sourceData.Chunk(ChunkSize);
        
        var tasks = chunks.Select(async chunk =>
        {
            var dataset = Dataset.Create(chunk.Length, $"chunk-{processedCount}");
            await sourcePort.SendAsync(dataset);
        });

        await Task.WhenAll(tasks);
        
        // Wait a bit for all callbacks to complete
        await Task.Delay(100);
        
        return processedCount;
    }

    [Benchmark]
    public async Task<int> BatchedPortStreaming()
    {
        using var sourcePort = new BatchedPort(batchSize: ChunkSize / 10);
        var processedCount = 0;
        
        // Start consumer task
        var consumerTask = Task.Run(async () =>
        {
            await foreach (var dataset in sourcePort.ReceiveAsync())
            {
                processedCount += dataset.RowCount;
            }
        });

        // Start producer task
        var producerTask = Task.Run(async () =>
        {
            var sourceData = _csvSource.GenerateEmployeeData(RecordCount);
            var chunks = sourceData.Chunk(ChunkSize);
            
            foreach (var chunk in chunks)
            {
                var dataset = Dataset.Create(chunk.Length, $"chunk-{processedCount}");
                await sourcePort.SendAsync(dataset);
            }
            
            sourcePort.Complete();
        });

        await Task.WhenAll(producerTask, consumerTask);
        return processedCount;
    }
}

/// <summary>
/// Memory efficiency benchmarks for large dataset processing
/// </summary>
[MemoryDiagnoser]
[SimpleJob(iterationCount: 3, warmupCount: 1)]
public class MemoryEfficiencyBenchmarks
{
    private MockCsvSource _csvSource = null!;
    
    [Params(10000, 50000)]
    public int RecordCount { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _csvSource = new MockCsvSource();
    }

    [Benchmark(Baseline = true)]
    public long MaterializedProcessing()
    {
        // Load all data into memory at once (non-streaming)
        var allData = _csvSource.GenerateEmployeeData(RecordCount).ToList();
        var allRows = allData.Select(data => new DictionaryRow(data)).ToList();
        
        // Process all rows
        var transformStep = new EmployeeTransformStep();
        var transformedRows = allRows.Select(row => transformStep.ProcessEmployee(row)).ToList();
        
        // Aggregate
        var aggregationStep = new DepartmentAggregationStep();
        var result = aggregationStep.AggregateByDepartment(transformedRows);
        
        return GC.GetTotalMemory(false);
    }

    [Benchmark]
    public long StreamingProcessing()
    {
        // Process data in streaming fashion (one at a time)
        var transformStep = new EmployeeTransformStep();
        var processedRows = new List<IRow>();
        
        foreach (var data in _csvSource.GenerateEmployeeData(RecordCount))
        {
            var row = new DictionaryRow(data);
            var transformed = transformStep.ProcessEmployee(row);
            processedRows.Add(transformed);
        }
        
        // Aggregate at the end
        var aggregationStep = new DepartmentAggregationStep();
        var result = aggregationStep.AggregateByDepartment(processedRows);
        
        return GC.GetTotalMemory(false);
    }

    [Benchmark]
    public long ChunkedProcessing()
    {
        // Process data in chunks to balance memory usage and efficiency
        const int chunkSize = 1000;
        var transformStep = new EmployeeTransformStep();
        var allProcessedRows = new List<IRow>();
        
        var sourceData = _csvSource.GenerateEmployeeData(RecordCount);
        var chunks = sourceData.Chunk(chunkSize);
        
        foreach (var chunk in chunks)
        {
            var chunkRows = chunk.Select(data => new DictionaryRow(data)).ToList();
            var transformedChunk = chunkRows.Select(row => transformStep.ProcessEmployee(row)).ToList();
            allProcessedRows.AddRange(transformedChunk);
            
            // Force GC between chunks to measure peak memory
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }
        
        // Final aggregation
        var aggregationStep = new DepartmentAggregationStep();
        var result = aggregationStep.AggregateByDepartment(allProcessedRows);
        
        return GC.GetTotalMemory(false);
    }

    [Benchmark]
    public long PooledChunkedProcessing()
    {
        // Use pooled rows with chunked processing
        const int chunkSize = 1000;
        var transformStep = new EmployeeTransformStep();
        var allProcessedRows = new List<IRow>();
        
        var sourceData = _csvSource.GenerateEmployeeData(RecordCount);
        var chunks = sourceData.Chunk(chunkSize);
        
        foreach (var chunk in chunks)
        {
            var transformedChunk = new List<IRow>();
            
            foreach (var data in chunk)
            {
                using var row = PooledRow.Create(data);
                var transformed = transformStep.ProcessEmployee(row);
                transformedChunk.Add(transformed);
            }
            
            allProcessedRows.AddRange(transformedChunk);
            
            // Force GC between chunks
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }
        
        // Final aggregation
        var aggregationStep = new DepartmentAggregationStep();
        var result = aggregationStep.AggregateByDepartment(allProcessedRows);
        
        return GC.GetTotalMemory(false);
    }
}

/// <summary>
/// Cross-platform file path handling benchmarks
/// </summary>
[MemoryDiagnoser]
[SimpleJob(iterationCount: 3, warmupCount: 1)]
public class CrossPlatformPathBenchmarks
{
    private string[] _windowsPaths = null!;
    private string[] _linuxPaths = null!;
    private string[] _mixedPaths = null!;

    [GlobalSetup]
    public void Setup()
    {
        _windowsPaths = new[]
        {
            @"C:\Data\input\customers.csv",
            @"C:\Reports\output\summary.xlsx",
            @"\\server\share\archive\data.zip",
            @"D:\Temp\processing\intermediate.json"
        };

        _linuxPaths = new[]
        {
            "/home/user/data/customers.csv",
            "/var/log/application/events.log",
            "/tmp/processing/intermediate.json",
            "/opt/flowengine/config/settings.yaml"
        };

        _mixedPaths = new[]
        {
            @"C:\Data/input\customers.csv",
            @"/home/user\data/customers.csv",
            @"${HOME}\data/file.csv",
            @"${OUTPUT_PATH}/results\report.pdf"
        };
    }

    [Benchmark(Baseline = true)]
    public string[] NaivePathHandling()
    {
        // Simulates naive path handling without proper cross-platform consideration
        var results = new List<string>();
        
        foreach (var path in _windowsPaths.Concat(_linuxPaths).Concat(_mixedPaths))
        {
            // Just use the path as-is (problematic)
            results.Add(path);
        }
        
        return results.ToArray();
    }

    [Benchmark]
    public string[] CrossPlatformPathHandling()
    {
        // Proper cross-platform path handling using .NET APIs
        var results = new List<string>();
        
        foreach (var path in _windowsPaths.Concat(_linuxPaths).Concat(_mixedPaths))
        {
            try
            {
                // Expand environment variables
                var expandedPath = Environment.ExpandEnvironmentVariables(path);
                
                // Normalize path separators
                var normalizedPath = expandedPath.Replace('\\', Path.DirectorySeparatorChar)
                                                 .Replace('/', Path.DirectorySeparatorChar);
                
                // Get full path (handles relative paths too)
                var fullPath = Path.GetFullPath(normalizedPath);
                
                results.Add(fullPath);
            }
            catch
            {
                // Handle invalid paths gracefully
                results.Add(path);
            }
        }
        
        return results.ToArray();
    }

    [Benchmark]
    public string[] WSLPathConversion()
    {
        // Simulate WSL path conversion scenarios
        var results = new List<string>();
        
        foreach (var path in _windowsPaths.Concat(_linuxPaths).Concat(_mixedPaths))
        {
            var convertedPath = ConvertWSLPath(path);
            results.Add(convertedPath);
        }
        
        return results.ToArray();
    }

    private string ConvertWSLPath(string path)
    {
        // Simulate WSL path conversion logic
        if (path.StartsWith("/mnt/c/", StringComparison.OrdinalIgnoreCase))
        {
            return path.Replace("/mnt/c/", "C:\\").Replace('/', '\\');
        }
        
        if (path.Length >= 3 && path[1] == ':' && path[2] == '\\')
        {
            // Windows path - convert to WSL
            var drive = path[0].ToString().ToLower();
            return $"/mnt/{drive}/" + path.Substring(3).Replace('\\', '/');
        }
        
        return path;
    }
}