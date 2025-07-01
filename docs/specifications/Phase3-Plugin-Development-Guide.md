# FlowEngine Phase 3: Plugin Development Guide

**Reading Time**: 6 minutes  
**Purpose**: Master the five-component architecture  

---

## Five-Component Architecture

Every Phase 3 plugin implements exactly five components:

```
Configuration → Validator → Plugin → Processor → Service
```

Each component has a single responsibility and clear interface contracts.

---

## Component 1: Configuration

**Purpose**: Define plugin parameters and schema  
**Interface**: `IStepConfiguration`

```csharp
public class MyPluginConfiguration : IStepConfiguration
{
    public Schema InputSchema { get; set; }
    public Schema OutputSchema { get; set; }
    public string ConnectionString { get; set; }
    public int BatchSize { get; set; } = 1000;
    
    // ArrayRow optimization: Pre-calculate field indexes
    private int _nameIndex = -1;
    private int _ageIndex = -1;
    
    public void Initialize(Schema schema)
    {
        _nameIndex = schema.GetFieldIndex("name");
        _ageIndex = schema.GetFieldIndex("age");
    }
    
    public int GetNameIndex() => _nameIndex;
    public int GetAgeIndex() => _ageIndex;
}
```

---

## Component 2: Validator

**Purpose**: Validate configuration and data  
**Interface**: `IStepValidator<TConfig>`

```csharp
public class MyPluginValidator : IStepValidator<MyPluginConfiguration>
{
    public ValidationResult ValidateConfiguration(MyPluginConfiguration config)
    {
        var errors = new List<string>();
        
        // Validate required fields
        if (string.IsNullOrEmpty(config.ConnectionString))
            errors.Add("ConnectionString is required");
            
        // Validate schema compatibility
        if (!ValidateSchema(config.InputSchema, config.OutputSchema))
            errors.Add("Schema transformation not supported");
            
        return errors.Any() 
            ? ValidationResult.Failed(errors)
            : ValidationResult.Success();
    }
    
    public ValidationResult ValidateData(ArrayRow row, MyPluginConfiguration config)
    {
        // High-performance validation using ArrayRow indexes
        var nameIndex = config.GetNameIndex();
        var ageIndex = config.GetAgeIndex();
        
        if (nameIndex >= 0 && string.IsNullOrEmpty(row.GetString(nameIndex)))
            return ValidationResult.Failed("Name cannot be empty");
            
        if (ageIndex >= 0 && row.GetInt32(ageIndex) < 0)
            return ValidationResult.Failed("Age cannot be negative");
            
        return ValidationResult.Success();
    }
    
    private bool ValidateSchema(Schema input, Schema output)
    {
        // Implement schema compatibility rules
        return input.Fields.All(f => output.HasField(f.Name) || f.IsOptional);
    }
}
```

---

## Component 3: Plugin

**Purpose**: Coordinate processing logic  
**Interface**: `IPlugin<TConfig>`

```csharp
public class MyPlugin : IPlugin<MyPluginConfiguration>
{
    private readonly ILogger<MyPlugin> _logger;
    private readonly MyPluginProcessor _processor;
    private readonly MyPluginService _service;
    
    public MyPlugin(
        ILogger<MyPlugin> logger,
        MyPluginProcessor processor,
        MyPluginService service)
    {
        _logger = logger;
        _processor = processor;
        _service = service;
    }
    
    public async Task<ProcessingResult> ProcessAsync(
        IAsyncEnumerable<Chunk> input,
        MyPluginConfiguration config,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Starting processing with {Config}", config);
        
        var processedCount = 0;
        var errorCount = 0;
        
        await foreach (var chunk in input.WithCancellation(cancellationToken))
        {
            try
            {
                var result = await _processor.ProcessChunkAsync(chunk, config, cancellationToken);
                processedCount += result.ProcessedRows;
                errorCount += result.ErrorRows;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing chunk");
                errorCount += chunk.RowCount;
            }
        }
        
        return new ProcessingResult
        {
            ProcessedRows = processedCount,
            ErrorRows = errorCount,
            Success = errorCount == 0
        };
    }
}
```

---

## Component 4: Processor

**Purpose**: Core data processing logic  
**Interface**: `IProcessor<TConfig>`

```csharp
public class MyPluginProcessor : IProcessor<MyPluginConfiguration>
{
    public async Task<ChunkResult> ProcessChunkAsync(
        Chunk chunk,
        MyPluginConfiguration config,
        CancellationToken cancellationToken)
    {
        var outputRows = new List<ArrayRow>();
        var errorCount = 0;
        
        // Get pre-calculated indexes for optimal performance
        var nameIndex = config.GetNameIndex();
        var ageIndex = config.GetAgeIndex();
        
        foreach (var row in chunk.Rows)
        {
            try
            {
                // High-performance ArrayRow access
                var name = row.GetString(nameIndex);
                var age = row.GetInt32(ageIndex);
                
                // Transform data
                var ageCategory = age < 18 ? "Minor" : age < 65 ? "Adult" : "Senior";
                
                // Create output row with pre-defined schema
                var outputRow = new ArrayRow(config.OutputSchema);
                outputRow.SetString(0, name);        // name field at index 0
                outputRow.SetString(1, ageCategory); // ageCategory field at index 1
                
                outputRows.Add(outputRow);
            }
            catch (Exception ex)
            {
                // Log error but continue processing
                errorCount++;
            }
        }
        
        return new ChunkResult
        {
            OutputChunk = new Chunk(outputRows),
            ProcessedRows = chunk.RowCount - errorCount,
            ErrorRows = errorCount
        };
    }
}
```

---

## Component 5: Service

**Purpose**: External dependencies and lifecycle management  
**Interface**: `IService`

```csharp
public class MyPluginService : IService, IDisposable
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<MyPluginService> _logger;
    private HttpClient? _httpClient;
    
    public MyPluginService(
        IHttpClientFactory httpClientFactory,
        ILogger<MyPluginService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }
    
    public async Task InitializeAsync(MyPluginConfiguration config)
    {
        _httpClient = _httpClientFactory.CreateClient();
        _httpClient.BaseAddress = new Uri(config.ConnectionString);
        
        // Test connection
        var response = await _httpClient.GetAsync("/health");
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException("Service unavailable");
        }
        
        _logger.LogInformation("Service initialized successfully");
    }
    
    public async Task<TResult> CallExternalServiceAsync<TResult>(object request)
    {
        // Implement external service calls
        var json = JsonSerializer.Serialize(request);
        var content = new StringContent(json, Encoding.UTF8, "application/json");
        
        var response = await _httpClient!.PostAsync("/api/process", content);
        response.EnsureSuccessStatusCode();
        
        var responseJson = await response.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<TResult>(responseJson)!;
    }
    
    public void Dispose()
    {
        _httpClient?.Dispose();
    }
}
```

---

## Testing Strategy

### Unit Testing Each Component

```csharp
[Test]
public void Configuration_ShouldValidateRequired_Properties()
{
    var config = new MyPluginConfiguration();
    var validator = new MyPluginValidator();
    
    var result = validator.ValidateConfiguration(config);
    
    Assert.That(result.IsValid, Is.False);
    Assert.That(result.Errors, Contains.Item("ConnectionString is required"));
}

[Test]
public async Task Processor_ShouldTransformData_Correctly()
{
    var config = CreateValidConfiguration();
    var processor = new MyPluginProcessor();
    var chunk = CreateTestChunk();
    
    var result = await processor.ProcessChunkAsync(chunk, config, CancellationToken.None);
    
    Assert.That(result.ProcessedRows, Is.EqualTo(chunk.RowCount));
    Assert.That(result.ErrorRows, Is.EqualTo(0));
}
```

### Integration Testing

```csharp
[Test]
public async Task Plugin_ShouldProcessPipeline_EndToEnd()
{
    var serviceProvider = CreateTestServiceProvider();
    var plugin = serviceProvider.GetRequiredService<MyPlugin>();
    var config = CreateValidConfiguration();
    
    var input = CreateTestInputStream();
    var result = await plugin.ProcessAsync(input, config, CancellationToken.None);
    
    Assert.That(result.Success, Is.True);
    Assert.That(result.ProcessedRows, Is.GreaterThan(0));
}
```

---

## Performance Optimization

### ArrayRow Best Practices

1. **Pre-calculate field indexes** in Configuration component
2. **Access fields by index**, not by name
3. **Reuse ArrayRow instances** when possible
4. **Process in batches** to minimize allocation overhead

### Memory Management

```csharp
// Use memory pooling for large datasets
private readonly ArrayPool<ArrayRow> _rowPool = ArrayPool<ArrayRow>.Shared;

public async Task ProcessLargeDataset(IAsyncEnumerable<Chunk> input)
{
    var buffer = _rowPool.Rent(1000);
    try
    {
        // Process using pooled buffer
    }
    finally
    {
        _rowPool.Return(buffer);
    }
}
```

### Error Handling

```csharp
// Fail fast for configuration errors
// Continue processing for data errors
public async Task<ChunkResult> ProcessChunkAsync(Chunk chunk, MyPluginConfiguration config)
{
    var errors = new List<string>();
    var outputRows = new List<ArrayRow>();
    
    foreach (var row in chunk.Rows)
    {
        try
        {
            var processedRow = ProcessRow(row, config);
            outputRows.Add(processedRow);
        }
        catch (DataException ex)
        {
            // Log and continue - don't fail entire batch
            errors.Add($"Row {row.Index}: {ex.Message}");
        }
        catch (ConfigurationException)
        {
            // Configuration errors should fail fast
            throw;
        }
    }
    
    return new ChunkResult(outputRows, errors);
}
```