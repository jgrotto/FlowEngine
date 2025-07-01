# FlowEngine Phase 3: Component Templates

**Reading Time**: 2 minutes  
**Purpose**: Copy-paste .cs templates for five-component plugins  

---

## Template Overview

These templates provide starting points for each of the five required components. Copy and customize for your specific plugin needs.

---

## 1. PluginConfiguration.cs.template

```csharp
using FlowEngine.Core;
using FlowEngine.Core.Interfaces;
using FlowEngine.Core.Schema;

namespace MyPlugin
{
    /// <summary>
    /// Configuration component - Define plugin parameters and schema
    /// Responsibility: Schema definitions, parameter validation, ArrayRow field index optimization
    /// </summary>
    public class MyPluginConfiguration : IStepConfiguration
    {
        #region Required Properties
        public Schema InputSchema { get; set; } = new Schema();
        public Schema OutputSchema { get; set; } = new Schema();
        #endregion

        #region Plugin-Specific Parameters
        /// <summary>
        /// Add your plugin-specific configuration properties here
        /// </summary>
        public string ConnectionString { get; set; } = string.Empty;
        public int BatchSize { get; set; } = 1000;
        public TimeSpan Timeout { get; set; } = TimeSpan.FromMinutes(5);
        #endregion

        #region ArrayRow Optimization - Pre-calculated Field Indexes
        /// <summary>
        /// CRITICAL: Pre-calculate field indexes for 3.25x performance improvement
        /// Call Initialize() after schema is set
        /// </summary>
        private int _fieldIndex1 = -1;
        private int _fieldIndex2 = -1;
        private int _fieldIndex3 = -1;
        
        /// <summary>
        /// Initialize field indexes for optimal ArrayRow performance
        /// MUST be called after InputSchema and OutputSchema are set
        /// </summary>
        public void Initialize()
        {
            // Input schema field indexes
            _fieldIndex1 = InputSchema.GetFieldIndex("field1Name");
            _fieldIndex2 = InputSchema.GetFieldIndex("field2Name");
            
            // Output schema field indexes  
            _fieldIndex3 = OutputSchema.GetFieldIndex("field3Name");
            
            // Validate required fields exist
            if (_fieldIndex1 == -1) throw new ArgumentException("Required field 'field1Name' not found in input schema");
            if (_fieldIndex2 == -1) throw new ArgumentException("Required field 'field2Name' not found in input schema");
            if (_fieldIndex3 == -1) throw new ArgumentException("Required field 'field3Name' not found in output schema");
        }
        
        // Expose optimized field access methods
        public int GetField1Index() => _fieldIndex1;
        public int GetField2Index() => _fieldIndex2;
        public int GetField3Index() => _fieldIndex3;
        #endregion

        #region Validation
        /// <summary>
        /// Basic configuration validation
        /// Complex validation should be in MyPluginValidator
        /// </summary>
        public bool IsValid(out List<string> errors)
        {
            errors = new List<string>();
            
            if (string.IsNullOrEmpty(ConnectionString))
                errors.Add("ConnectionString is required");
                
            if (BatchSize <= 0)
                errors.Add("BatchSize must be greater than 0");
                
            if (Timeout.TotalSeconds <= 0)
                errors.Add("Timeout must be greater than 0");
            
            return !errors.Any();
        }
        #endregion
    }
}
```

---

## 2. PluginValidator.cs.template

```csharp
using FlowEngine.Core;
using FlowEngine.Core.Interfaces;
using FlowEngine.Core.Validation;

namespace MyPlugin
{
    /// <summary>
    /// Validator component - Validate configuration and data
    /// Responsibility: Configuration validation, data validation, schema compatibility
    /// </summary>
    public class MyPluginValidator : IStepValidator<MyPluginConfiguration>
    {
        private readonly ILogger<MyPluginValidator> _logger;

        public MyPluginValidator(ILogger<MyPluginValidator> logger)
        {
            _logger = logger;
        }

        #region Configuration Validation
        /// <summary>
        /// Validate plugin configuration before processing starts
        /// </summary>
        public ValidationResult ValidateConfiguration(MyPluginConfiguration config)
        {
            var errors = new List<string>();

            // Basic configuration validation
            if (!config.IsValid(out var configErrors))
            {
                errors.AddRange(configErrors);
            }

            // Schema validation
            var schemaValidation = ValidateSchemas(config.InputSchema, config.OutputSchema);
            if (!schemaValidation.IsValid)
            {
                errors.AddRange(schemaValidation.Errors);
            }

            // External dependencies validation
            var dependencyValidation = ValidateExternalDependencies(config);
            if (!dependencyValidation.IsValid)
            {
                errors.AddRange(dependencyValidation.Errors);
            }

            return errors.Any() 
                ? ValidationResult.Failed(errors)
                : ValidationResult.Success();
        }
        #endregion

        #region Data Validation
        /// <summary>
        /// Validate individual data rows using optimized ArrayRow access
        /// </summary>
        public ValidationResult ValidateData(ArrayRow row, MyPluginConfiguration config)
        {
            var errors = new List<string>();

            try
            {
                // Use pre-calculated indexes for high-performance validation
                var field1Index = config.GetField1Index();
                var field2Index = config.GetField2Index();

                // Validate field1 (example: required string field)
                if (field1Index >= 0)
                {
                    var field1Value = row.GetString(field1Index);
                    if (string.IsNullOrEmpty(field1Value))
                    {
                        errors.Add($"Field1 cannot be empty at row {row.RowNumber}");
                    }
                }

                // Validate field2 (example: numeric range validation)
                if (field2Index >= 0)
                {
                    var field2Value = row.GetInt32(field2Index);
                    if (field2Value < 0 || field2Value > 1000)
                    {
                        errors.Add($"Field2 must be between 0 and 1000, got {field2Value} at row {row.RowNumber}");
                    }
                }

                // Add your custom validation logic here
                var customValidation = ValidateCustomBusinessRules(row, config);
                if (!customValidation.IsValid)
                {
                    errors.AddRange(customValidation.Errors);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error validating row {RowNumber}", row.RowNumber);
                errors.Add($"Validation error at row {row.RowNumber}: {ex.Message}");
            }

            return errors.Any() 
                ? ValidationResult.Failed(errors)
                : ValidationResult.Success();
        }
        #endregion

        #region Helper Methods
        private ValidationResult ValidateSchemas(Schema inputSchema, Schema outputSchema)
        {
            var errors = new List<string>();

            // Validate input schema has required fields
            var requiredInputFields = new[] { "field1Name", "field2Name" };
            foreach (var fieldName in requiredInputFields)
            {
                if (!inputSchema.HasField(fieldName))
                {
                    errors.Add($"Required input field '{fieldName}' not found in schema");
                }
            }

            // Validate output schema
            var requiredOutputFields = new[] { "field3Name" };
            foreach (var fieldName in requiredOutputFields)
            {
                if (!outputSchema.HasField(fieldName))
                {
                    errors.Add($"Required output field '{fieldName}' not found in schema");
                }
            }

            // Validate field types
            if (inputSchema.HasField("field1Name"))
            {
                var field1Type = inputSchema.GetField("field1Name").Type;
                if (field1Type != FieldType.String)
                {
                    errors.Add($"Field 'field1Name' must be string type, got {field1Type}");
                }
            }

            return errors.Any() 
                ? ValidationResult.Failed(errors)
                : ValidationResult.Success();
        }

        private ValidationResult ValidateExternalDependencies(MyPluginConfiguration config)
        {
            var errors = new List<string>();

            // Example: Test database connection
            try
            {
                // Add your external dependency validation here
                // e.g., database connectivity, API availability, file system access
                
                // if (!TestConnection(config.ConnectionString))
                // {
                //     errors.Add("Cannot connect to external service");
                // }
            }
            catch (Exception ex)
            {
                errors.Add($"External dependency validation failed: {ex.Message}");
            }

            return errors.Any() 
                ? ValidationResult.Failed(errors)
                : ValidationResult.Success();
        }

        private ValidationResult ValidateCustomBusinessRules(ArrayRow row, MyPluginConfiguration config)
        {
            var errors = new List<string>();

            // Add your custom business rule validation here
            // Example: Cross-field validation, business logic constraints, etc.

            return errors.Any() 
                ? ValidationResult.Failed(errors)
                : ValidationResult.Success();
        }
        #endregion
    }
}
```

---

## 3. Plugin.cs.template

```csharp
using FlowEngine.Core;
using FlowEngine.Core.Interfaces;
using FlowEngine.Core.Processing;

namespace MyPlugin
{
    /// <summary>
    /// Plugin component - Coordinate processing logic
    /// Responsibility: Orchestration, error handling, metrics, component coordination
    /// </summary>
    public class MyPlugin : IPlugin<MyPluginConfiguration>
    {
        private readonly ILogger<MyPlugin> _logger;
        private readonly MyPluginValidator _validator;
        private readonly MyPluginProcessor _processor;
        private readonly MyPluginService _service;
        private readonly IMetricsCollector _metrics;

        public MyPlugin(
            ILogger<MyPlugin> logger,
            MyPluginValidator validator,
            MyPluginProcessor processor,
            MyPluginService service,
            IMetricsCollector metrics)
        {
            _logger = logger;
            _validator = validator;
            _processor = processor;
            _service = service;
            _metrics = metrics;
        }

        #region Main Processing Method
        /// <summary>
        /// Main plugin processing entry point
        /// Coordinates all five components
        /// </summary>
        public async Task<ProcessingResult> ProcessAsync(
            IAsyncEnumerable<Chunk> input,
            MyPluginConfiguration config,
            CancellationToken cancellationToken)
        {
            var startTime = DateTime.UtcNow;
            var processedCount = 0;
            var errorCount = 0;
            var exceptions = new List<Exception>();

            try
            {
                _logger.LogInformation("Starting plugin processing with config: {Config}", 
                    JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true }));

                // 1. Validate configuration (Component 2: Validator)
                var configValidation = _validator.ValidateConfiguration(config);
                if (!configValidation.IsValid)
                {
                    throw new InvalidOperationException($"Configuration validation failed: {string.Join(", ", configValidation.Errors)}");
                }

                // 2. Initialize configuration field indexes (Component 1: Configuration)
                config.Initialize();

                // 3. Initialize service (Component 5: Service)
                await _service.InitializeAsync(config);

                // 4. Process data chunks (Component 4: Processor)
                await foreach (var chunk in input.WithCancellation(cancellationToken))
                {
                    try
                    {
                        var chunkResult = await ProcessChunkWithMetrics(chunk, config, cancellationToken);
                        processedCount += chunkResult.ProcessedRows;
                        errorCount += chunkResult.ErrorRows;

                        if (chunkResult.Exceptions?.Any() == true)
                        {
                            exceptions.AddRange(chunkResult.Exceptions);
                        }

                        // Report progress
                        _logger.LogDebug("Processed chunk: {ProcessedRows} rows, {ErrorRows} errors", 
                            chunkResult.ProcessedRows, chunkResult.ErrorRows);
                    }
                    catch (OperationCanceledException)
                    {
                        _logger.LogWarning("Processing cancelled");
                        throw;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error processing chunk");
                        errorCount += chunk.RowCount;
                        exceptions.Add(ex);
                    }
                }

                var duration = DateTime.UtcNow - startTime;
                var throughput = processedCount / duration.TotalSeconds;

                _logger.LogInformation("Plugin processing completed. Processed: {ProcessedRows}, Errors: {ErrorRows}, Duration: {Duration}, Throughput: {Throughput} rows/sec",
                    processedCount, errorCount, duration, throughput);

                return new ProcessingResult
                {
                    ProcessedRows = processedCount,
                    ErrorRows = errorCount,
                    Duration = duration,
                    Throughput = throughput,
                    Success = errorCount == 0,
                    Exceptions = exceptions
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Fatal error during plugin processing");
                return new ProcessingResult
                {
                    ProcessedRows = processedCount,
                    ErrorRows = errorCount,
                    Duration = DateTime.UtcNow - startTime,
                    Success = false,
                    Exceptions = new List<Exception> { ex }
                };
            }
            finally
            {
                // Cleanup service resources
                await _service.CleanupAsync();
            }
        }
        #endregion

        #region Helper Methods
        private async Task<ChunkResult> ProcessChunkWithMetrics(
            Chunk chunk, 
            MyPluginConfiguration config, 
            CancellationToken cancellationToken)
        {
            using var activity = _metrics.StartActivity("ProcessChunk");
            activity?.SetTag("ChunkSize", chunk.RowCount.ToString());

            var stopwatch = Stopwatch.StartNew();
            
            try
            {
                var result = await _processor.ProcessChunkAsync(chunk, config, cancellationToken);
                
                stopwatch.Stop();
                _metrics.RecordValue("ChunkProcessingTime", stopwatch.ElapsedMilliseconds);
                _metrics.RecordValue("ChunkThroughput", chunk.RowCount / stopwatch.Elapsed.TotalSeconds);
                
                return result;
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                _metrics.RecordValue("ChunkErrors", 1);
                _logger.LogError(ex, "Error processing chunk with {RowCount} rows", chunk.RowCount);
                throw;
            }
        }
        #endregion

        #region IDisposable Implementation
        public void Dispose()
        {
            _service?.Dispose();
        }
        #endregion
    }
}
```

---

## 4. PluginProcessor.cs.template

```csharp
using FlowEngine.Core;
using FlowEngine.Core.Interfaces;
using FlowEngine.Core.Processing;

namespace MyPlugin
{
    /// <summary>
    /// Processor component - Core data processing logic
    /// Responsibility: Data transformation, ArrayRow optimization, business logic
    /// </summary>
    public class MyPluginProcessor : IProcessor<MyPluginConfiguration>
    {
        private readonly ILogger<MyPluginProcessor> _logger;
        private readonly MyPluginValidator _validator;

        public MyPluginProcessor(
            ILogger<MyPluginProcessor> logger,
            MyPluginValidator validator)
        {
            _logger = logger;
            _validator = validator;
        }

        #region Main Processing Method
        /// <summary>
        /// Process a chunk of data with optimized ArrayRow access
        /// </summary>
        public async Task<ChunkResult> ProcessChunkAsync(
            Chunk chunk,
            MyPluginConfiguration config,
            CancellationToken cancellationToken)
        {
            var outputRows = new List<ArrayRow>();
            var errorCount = 0;
            var exceptions = new List<Exception>();

            // Get pre-calculated indexes for optimal performance
            var field1Index = config.GetField1Index();
            var field2Index = config.GetField2Index();
            var field3Index = config.GetField3Index();

            foreach (var row in chunk.Rows)
            {
                try
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    // Optional: Validate individual row data
                    if (config.EnableRowValidation)
                    {
                        var validation = _validator.ValidateData(row, config);
                        if (!validation.IsValid)
                        {
                            _logger.LogWarning("Row validation failed for row {RowNumber}: {Errors}", 
                                row.RowNumber, string.Join(", ", validation.Errors));
                            errorCount++;
                            continue;
                        }
                    }

                    // HIGH-PERFORMANCE: Use pre-calculated indexes for ArrayRow access
                    var field1Value = row.GetString(field1Index);
                    var field2Value = row.GetInt32(field2Index);

                    // YOUR BUSINESS LOGIC HERE
                    var transformedData = await ProcessBusinessLogic(field1Value, field2Value, config, cancellationToken);

                    // Create output row with pre-defined schema
                    var outputRow = new ArrayRow(config.OutputSchema);
                    
                    // HIGH-PERFORMANCE: Direct index access for output
                    outputRow.SetString(field3Index, transformedData);
                    
                    // Add any additional output fields here
                    // outputRow.SetInt32(anotherIndex, someValue);

                    outputRows.Add(outputRow);
                }
                catch (OperationCanceledException)
                {
                    _logger.LogInformation("Processing cancelled at row {RowNumber}", row.RowNumber);
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing row {RowNumber}", row.RowNumber);
                    errorCount++;
                    exceptions.Add(ex);

                    // Decide whether to continue or fail fast
                    if (config.FailFastOnError)
                    {
                        throw;
                    }
                }
            }

            return new ChunkResult
            {
                OutputChunk = new Chunk(outputRows),
                ProcessedRows = chunk.RowCount - errorCount,
                ErrorRows = errorCount,
                Exceptions = exceptions
            };
        }
        #endregion

        #region Business Logic Methods
        /// <summary>
        /// Implement your core business logic here
        /// This is where the actual data transformation happens
        /// </summary>
        private async Task<string> ProcessBusinessLogic(
            string field1Value, 
            int field2Value, 
            MyPluginConfiguration config,
            CancellationToken cancellationToken)
        {
            // EXAMPLE: Simple transformation
            // Replace this with your actual business logic
            
            if (string.IsNullOrEmpty(field1Value))
            {
                return "DEFAULT_VALUE";
            }

            // Example: Transform based on field2Value
            if (field2Value > 100)
            {
                return field1Value.ToUpperInvariant();
            }
            else
            {
                return field1Value.ToLowerInvariant();
            }

            // Example: Async processing (external API call, database lookup, etc.)
            // var result = await ExternalService.ProcessAsync(field1Value, cancellationToken);
            // return result;
        }

        /// <summary>
        /// Example: Batch processing optimization
        /// Process multiple rows together for better performance
        /// </summary>
        private async Task<Dictionary<string, string>> ProcessBatch(
            List<string> inputValues,
            MyPluginConfiguration config,
            CancellationToken cancellationToken)
        {
            // Example: Batch API call instead of individual calls
            // This can significantly improve performance for external service calls
            
            var results = new Dictionary<string, string>();
            
            // YOUR BATCH PROCESSING LOGIC HERE
            foreach (var value in inputValues)
            {
                results[value] = await ProcessBusinessLogic(value, 0, config, cancellationToken);
            }
            
            return results;
        }
        #endregion

        #region Performance Optimization Methods
        /// <summary>
        /// Example: Memory-efficient processing for large datasets
        /// </summary>
        private async Task<ChunkResult> ProcessLargeChunkAsync(
            Chunk chunk,
            MyPluginConfiguration config,
            CancellationToken cancellationToken)
        {
            // For large chunks, consider:
            // 1. Parallel processing
            // 2. Memory pooling
            // 3. Streaming output
            // 4. Batch external calls

            var parallelOptions = new ParallelOptions
            {
                CancellationToken = cancellationToken,
                MaxDegreeOfParallelism = Environment.ProcessorCount
            };

            var outputRows = new ConcurrentBag<ArrayRow>();
            var errorCount = 0;

            await Parallel.ForEachAsync(chunk.Rows, parallelOptions, async (row, ct) =>
            {
                try
                {
                    var processedRow = await ProcessSingleRowAsync(row, config, ct);
                    outputRows.Add(processedRow);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing row {RowNumber}", row.RowNumber);
                    Interlocked.Increment(ref errorCount);
                }
            });

            return new ChunkResult
            {
                OutputChunk = new Chunk(outputRows.ToList()),
                ProcessedRows = chunk.RowCount - errorCount,
                ErrorRows = errorCount
            };
        }

        private async Task<ArrayRow> ProcessSingleRowAsync(
            ArrayRow row,
            MyPluginConfiguration config,
            CancellationToken cancellationToken)
        {
            // Single row processing logic
            var field1Index = config.GetField1Index();
            var field1Value = row.GetString(field1Index);
            
            var transformedValue = await ProcessBusinessLogic(field1Value, 0, config, cancellationToken);
            
            var outputRow = new ArrayRow(config.OutputSchema);
            outputRow.SetString(config.GetField3Index(), transformedValue);
            
            return outputRow;
        }
        #endregion
    }
}
```

---

## 5. PluginService.cs.template

```csharp
using FlowEngine.Core;
using FlowEngine.Core.Interfaces;

namespace MyPlugin
{
    /// <summary>
    /// Service component - External dependencies and lifecycle management
    /// Responsibility: External connections, resource management, initialization/cleanup
    /// </summary>
    public class MyPluginService : IService, IDisposable
    {
        private readonly ILogger<MyPluginService> _logger;
        private readonly IHttpClientFactory _httpClientFactory;
        
        // External service connections
        private HttpClient? _httpClient;
        private DatabaseConnection? _databaseConnection;
        private bool _isInitialized = false;
        private bool _disposed = false;

        public MyPluginService(
            ILogger<MyPluginService> logger,
            IHttpClientFactory httpClientFactory)
        {
            _logger = logger;
            _httpClientFactory = httpClientFactory;
        }

        #region Lifecycle Management
        /// <summary>
        /// Initialize external service connections and dependencies
        /// </summary>
        public async Task InitializeAsync(MyPluginConfiguration config)
        {
            if (_isInitialized)
            {
                _logger.LogWarning("Service already initialized");
                return;
            }

            try
            {
                _logger.LogInformation("Initializing service with connection: {Connection}", 
                    MaskConnectionString(config.ConnectionString));

                // Initialize HTTP client
                await InitializeHttpClientAsync(config);

                // Initialize database connection (if needed)
                await InitializeDatabaseAsync(config);

                // Test connections
                await ValidateConnectionsAsync();

                _isInitialized = true;
                _logger.LogInformation("Service initialization completed successfully");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Service initialization failed");
                await CleanupAsync();
                throw;
            }
        }

        /// <summary>
        /// Cleanup resources and close connections
        /// </summary>
        public async Task CleanupAsync()
        {
            if (!_isInitialized)
                return;

            try
            {
                _logger.LogInformation("Cleaning up service resources");

                // Close database connections
                if (_databaseConnection != null)
                {
                    await _databaseConnection.CloseAsync();
                    _databaseConnection = null;
                }

                // Dispose HTTP client
                _httpClient?.Dispose();
                _httpClient = null;

                _isInitialized = false;
                _logger.LogInformation("Service cleanup completed");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during service cleanup");
            }
        }
        #endregion

        #region External Service Methods
        /// <summary>
        /// Example: Call external REST API
        /// </summary>
        public async Task<TResponse> CallExternalApiAsync<TRequest, TResponse>(
            string endpoint,
            TRequest request,
            CancellationToken cancellationToken)
        {
            if (!_isInitialized)
                throw new InvalidOperationException("Service not initialized");

            try
            {
                var json = JsonSerializer.Serialize(request);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await _httpClient!.PostAsync(endpoint, content, cancellationToken);
                response.EnsureSuccessStatusCode();

                var responseJson = await response.Content.ReadAsStringAsync(cancellationToken);
                return JsonSerializer.Deserialize<TResponse>(responseJson)!;
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex, "HTTP request failed for endpoint: {Endpoint}", endpoint);
                throw;
            }
            catch (JsonException ex)
            {
                _logger.LogError(ex, "JSON serialization/deserialization failed");
                throw;
            }
        }

        /// <summary>
        /// Example: Execute database query
        /// </summary>
        public async Task<List<T>> ExecuteQueryAsync<T>(
            string query,
            object parameters,
            CancellationToken cancellationToken)
        {
            if (!_isInitialized)
                throw new InvalidOperationException("Service not initialized");

            try
            {
                // Example using Dapper (replace with your preferred ORM)
                // return await _databaseConnection.QueryAsync<T>(query, parameters);
                
                // Placeholder implementation
                return new List<T>();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Database query failed: {Query}", query);
                throw;
            }
        }

        /// <summary>
        /// Example: Batch processing for better performance
        /// </summary>
        public async Task<Dictionary<string, TResult>> ProcessBatchAsync<TResult>(
            List<string> inputValues,
            Func<List<string>, Task<Dictionary<string, TResult>>> processor,
            CancellationToken cancellationToken)
        {
            if (!_isInitialized)
                throw new InvalidOperationException("Service not initialized");

            var results = new Dictionary<string, TResult>();
            var batchSize = 100; // Configurable batch size

            for (int i = 0; i < inputValues.Count; i += batchSize)
            {
                var batch = inputValues.Skip(i).Take(batchSize).ToList();
                var batchResults = await processor(batch);
                
                foreach (var kvp in batchResults)
                {
                    results[kvp.Key] = kvp.Value;
                }

                // Optional: Add delay to avoid overwhelming external services
                if (i + batchSize < inputValues.Count)
                {
                    await Task.Delay(100, cancellationToken);
                }
            }

            return results;
        }
        #endregion

        #region Health Check and Monitoring
        /// <summary>
        /// Check if external services are healthy
        /// </summary>
        public async Task<HealthCheckResult> CheckHealthAsync()
        {
            if (!_isInitialized)
                return HealthCheckResult.Unhealthy("Service not initialized");

            try
            {
                var tasks = new List<Task<bool>>();

                // Check HTTP service health
                if (_httpClient != null)
                {
                    tasks.Add(CheckHttpHealthAsync());
                }

                // Check database health
                if (_databaseConnection != null)
                {
                    tasks.Add(CheckDatabaseHealthAsync());
                }

                var results = await Task.WhenAll(tasks);
                
                return results.All(r => r) 
                    ? HealthCheckResult.Healthy("All services healthy")
                    : HealthCheckResult.Unhealthy("One or more services unhealthy");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Health check failed");
                return HealthCheckResult.Unhealthy($"Health check error: {ex.Message}");
            }
        }
        #endregion

        #region Private Helper Methods
        private async Task InitializeHttpClientAsync(MyPluginConfiguration config)
        {
            _httpClient = _httpClientFactory.CreateClient();
            _httpClient.BaseAddress = new Uri(config.ConnectionString);
            _httpClient.Timeout = config.Timeout;
            
            // Add authentication headers if needed
            // _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        private async Task InitializeDatabaseAsync(MyPluginConfiguration config)
        {
            // Example database initialization
            // _databaseConnection = new DatabaseConnection(config.ConnectionString);
            // await _databaseConnection.OpenAsync();
        }

        private async Task ValidateConnectionsAsync()
        {
            if (_httpClient != null)
            {
                var response = await _httpClient.GetAsync("/health");
                if (!response.IsSuccessStatusCode)
                {
                    throw new InvalidOperationException($"HTTP service health check failed: {response.StatusCode}");
                }
            }

            if (_databaseConnection != null)
            {
                // Test database connection
                // await _databaseConnection.TestConnectionAsync();
            }
        }

        private async Task<bool> CheckHttpHealthAsync()
        {
            try
            {
                var response = await _httpClient!.GetAsync("/health");
                return response.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
        }

        private async Task<bool> CheckDatabaseHealthAsync()
        {
            try
            {
                // return await _databaseConnection!.TestConnectionAsync();
                return true; // Placeholder
            }
            catch
            {
                return false;
            }
        }

        private string MaskConnectionString(string connectionString)
        {
            // Mask sensitive information in connection strings for logging
            return connectionString.Length > 20 
                ? connectionString.Substring(0, 20) + "***"
                : "***";
        }
        #endregion

        #region IDisposable Implementation
        public void Dispose()
        {
            if (_disposed)
                return;

            CleanupAsync().GetAwaiter().GetResult();
            _disposed = true;
        }
        #endregion
    }
}
```

---

## Usage Instructions

### 1. **Copy Templates to Your Project**
```bash
# Create new plugin using CLI (recommended)
dotnet run -- plugin create --name MyAwesomePlugin --type Transform

# Or manually copy templates
mkdir MyAwesomePlugin
cp PluginConfiguration.cs.template MyAwesomePlugin/MyAwesomePluginConfiguration.cs
cp PluginValidator.cs.template MyAwesomePlugin/MyAwesomePluginValidator.cs
cp Plugin.cs.template MyAwesomePlugin/MyAwesomePlugin.cs
cp PluginProcessor.cs.template MyAwesomePlugin/MyAwesomePluginProcessor.cs
cp PluginService.cs.template MyAwesomePlugin/MyAwesomePluginService.cs
```

### 2. **Customize for Your Plugin**
- Replace `MyPlugin` with your plugin name throughout all files
- Update `field1Name`, `field2Name`, etc. with your actual field names
- Implement your business logic in the `ProcessBusinessLogic` method
- Add your external service calls in the Service component
- Update validation rules in the Validator component

### 3. **Key Customization Points**

#### **Configuration Component**
- Add plugin-specific properties
- Update field names and indexes
- Define schema requirements

#### **Validator Component**  
- Implement your validation rules
- Add external dependency checks
- Define schema compatibility requirements

#### **Plugin Component**
- Usually minimal changes needed
- Add custom metrics if required
- Update error handling as needed

#### **Processor Component**
- **This is where your main logic goes**
- Implement `ProcessBusinessLogic` method
- Optimize for your data transformation needs
- Add parallel processing if beneficial

#### **Service Component**
- Configure external connections
- Implement external API calls
- Add health checks for your services

### 4. **Performance Optimization Notes**

#### **ArrayRow Field Access**
```csharp
// ❌ SLOW - Field name lookup every time
var value = row.GetString("fieldName");

// ✅ FAST - Pre-calculated index (3.25x faster)
var fieldIndex = config.GetFieldIndex(); 
var value = row.GetString(fieldIndex);
```

#### **Batch Processing**
```csharp
// ❌ SLOW - Individual API calls
foreach (var item in items)
{
    await CallApiAsync(item);
}

// ✅ FAST - Batch API calls
await CallBatchApiAsync(items);
```

### 5. **Testing Your Plugin**
```bash
# Validate plugin structure
dotnet run -- plugin validate --path ./MyAwesomePlugin

# Test with sample data
dotnet run -- plugin test --path ./MyAwesomePlugin --data sample.csv

# Performance benchmark
dotnet run -- plugin benchmark --path ./MyAwesomePlugin --rows 100000
```

---

## Template Features

### **✅ Production Ready**
- Comprehensive error handling
- Logging throughout all components
- Resource cleanup and disposal
- Health checks and monitoring

### **✅ Performance Optimized**
- Pre-calculated ArrayRow field indexes
- Batch processing patterns
- Memory-efficient patterns
- Cancellation token support

### **✅ Enterprise Features**
- Configuration validation
- External service integration
- Metrics and monitoring
- Comprehensive documentation

### **✅ Testing Support**
- Validation at multiple levels
- Health check implementations
- Clean separation of concerns
- Mockable dependencies

Replace the placeholder business logic with your specific requirements and you'll have a production-ready, high-performance plugin following the five-component architecture pattern!