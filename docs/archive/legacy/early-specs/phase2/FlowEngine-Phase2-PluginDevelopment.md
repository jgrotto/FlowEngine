# FlowEngine Phase 2 Plugin Development Guide

**Reading Time**: 6 minutes  
**Goal**: Create your first Phase 2 plugin in 20 minutes  

---

## Plugin Interface Contracts

### Core Interfaces
```csharp
public interface IPlugin
{
    string Name { get; }
    ISchema OutputSchema { get; }
    Task InitializeAsync(IConfiguration config, CancellationToken cancellationToken);
    Task DisposeAsync();
}

public interface ISourcePlugin : IPlugin
{
    IAsyncEnumerable<Chunk> ProduceAsync(CancellationToken cancellationToken);
}

public interface ITransformPlugin : IPlugin
{
    IAsyncEnumerable<Chunk> TransformAsync(
        IAsyncEnumerable<Chunk> input, 
        CancellationToken cancellationToken);
}

public interface ISinkPlugin : IPlugin
{
    Task ConsumeAsync(
        IAsyncEnumerable<Chunk> input, 
        CancellationToken cancellationToken);
}
```

## Plugin Lifecycle

### 1. Assembly Loading
- Plugin loaded in isolated AssemblyLoadContext
- Dependency resolution within plugin boundaries
- Memory and timeout limits enforced

### 2. Initialization Phase
```csharp
public async Task InitializeAsync(IConfiguration config, CancellationToken cancellationToken)
{
    // Parse plugin-specific configuration
    _connectionString = config.GetConnectionString("Database");
    _batchSize = config.GetValue<int>("BatchSize", 1000);
    
    // Initialize resources (database connections, file handles, etc.)
    _dbConnection = new SqlConnection(_connectionString);
    await _dbConnection.OpenAsync(cancellationToken);
    
    // Validate configuration and external dependencies
    await ValidateConfigurationAsync();
}
```

### 3. Processing Phase
- Receive chunks through channels
- Process data with schema validation
- Emit transformed chunks to next stage

### 4. Disposal Phase
```csharp
public async Task DisposeAsync()
{
    // Clean up resources
    await _dbConnection?.DisposeAsync();
    _fileStream?.Dispose();
    
    // Report final metrics
    _logger.LogInformation("Processed {Count} total rows", _totalRows);
}
```

## Creating a Transform Plugin

### Step 1: Create Plugin Class
```csharp
[Plugin("UppercaseTransform", "Converts string fields to uppercase")]
public class UppercaseTransformPlugin : ITransformPlugin
{
    private readonly ILogger<UppercaseTransformPlugin> _logger;
    private ISchema _outputSchema;
    private string[] _targetFields;
    
    public UppercaseTransformPlugin(ILogger<UppercaseTransformPlugin> logger)
    {
        _logger = logger;
    }
    
    public string Name => "UppercaseTransform";
    public ISchema OutputSchema => _outputSchema;
}
```

### Step 2: Implement Initialization
```csharp
public async Task InitializeAsync(IConfiguration config, CancellationToken cancellationToken)
{
    // Read configuration
    _targetFields = config.GetSection("TargetFields").Get<string[]>() ?? Array.Empty<string>();
    
    // Create output schema (same as input for transforms)
    _outputSchema = config.GetRequiredSection("Schema").Get<SchemaDefinition>().ToSchema();
    
    // Validate target fields exist in schema
    foreach (var field in _targetFields)
    {
        if (!_outputSchema.HasField(field))
            throw new ConfigurationException($"Target field '{field}' not found in schema");
    }
    
    _logger.LogInformation("Initialized with {Count} target fields", _targetFields.Length);
}
```

### Step 3: Implement Processing
```csharp
public async IAsyncEnumerable<Chunk> TransformAsync(
    IAsyncEnumerable<Chunk> input, 
    [EnumeratorCancellation] CancellationToken cancellationToken)
{
    await foreach (var chunk in input)
    {
        var transformedRows = new List<ArrayRow>(chunk.Count);
        
        foreach (var row in chunk.Rows)
        {
            var newRow = TransformRow(row);
            transformedRows.Add(newRow);
        }
        
        yield return new Chunk(transformedRows, _outputSchema);
        
        // Yield periodically for cancellation
        if (cancellationToken.IsCancellationRequested)
            yield break;
    }
}

private ArrayRow TransformRow(ArrayRow sourceRow)
{
    var values = new object[_outputSchema.FieldCount];
    
    for (int i = 0; i < _outputSchema.FieldCount; i++)
    {
        var field = _outputSchema.GetField(i);
        var sourceValue = sourceRow.GetValue(i);
        
        if (_targetFields.Contains(field.Name) && sourceValue is string str)
        {
            values[i] = str.ToUpperInvariant();
        }
        else
        {
            values[i] = sourceValue;
        }
    }
    
    return ArrayRow.Create(_outputSchema, values);
}
```

## Configuration Schema

### Plugin Configuration Template
```yaml
plugins:
  - name: "myTransform"
    type: "UppercaseTransformPlugin"
    config:
      targetFields: ["name", "description"]
    schema:
      name: "customer"
      version: "1.0.0"
      fields:
        - name: "id"
          type: "int32"
          required: true
        - name: "name"
          type: "string"
          required: true
        - name: "description"
          type: "string"
          required: false
```

## Testing Your Plugin

### Unit Testing Template
```csharp
[Test]
public async Task TransformAsync_ConvertsTargetFieldsToUppercase()
{
    // Arrange
    var config = new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string>
        {
            ["TargetFields:0"] = "name",
            ["Schema:Name"] = "test",
            ["Schema:Version"] = "1.0.0",
            ["Schema:Fields:0:Name"] = "name",
            ["Schema:Fields:0:Type"] = "string",
            ["Schema:Fields:0:Required"] = "true"
        })
        .Build();
    
    var plugin = new UppercaseTransformPlugin(NullLogger<UppercaseTransformPlugin>.Instance);
    await plugin.InitializeAsync(config, CancellationToken.None);
    
    var schema = SchemaBuilder.Create("test", "1.0.0")
        .AddField("name", typeof(string), required: true)
        .Build();
    
    var inputRow = ArrayRow.Create(schema, new object[] { "john doe" });
    var inputChunk = new Chunk(new[] { inputRow }, schema);
    
    // Act
    var result = await plugin.TransformAsync(new[] { inputChunk }.ToAsyncEnumerable())
        .FirstAsync();
    
    // Assert
    Assert.That(result.Rows[0].GetValue<string>("name"), Is.EqualTo("JOHN DOE"));
}
```

### Integration Testing
```csharp
[Test]
public async Task EndToEndPipelineTest()
{
    var pipelineConfig = """
        pipeline:
          plugins:
            - name: "source"
              type: "ArraySourcePlugin"
              config:
                data: [["john"], ["jane"]]
            - name: "transform"
              type: "UppercaseTransformPlugin"
              config:
                targetFields: ["name"]
            - name: "sink"
              type: "ListSinkPlugin"
          connections:
            - from: "source"
              to: "transform"
            - from: "transform"
              to: "sink"
        """;
    
    var pipeline = await PipelineBuilder.FromYamlAsync(pipelineConfig);
    await pipeline.ExecuteAsync();
    
    var sink = pipeline.GetPlugin<ListSinkPlugin>("sink");
    Assert.That(sink.Results[0].GetValue<string>("name"), Is.EqualTo("JOHN"));
}
```

## Performance Considerations

### Memory Management
- Avoid large object allocations in hot paths
- Reuse ArrayRow instances when possible
- Monitor memory usage through plugin telemetry

### Async Patterns
- Use `IAsyncEnumerable` for streaming data
- Implement proper cancellation support
- Avoid blocking calls in async methods

### Error Handling
- Use structured logging for debugging
- Implement graceful degradation for recoverable errors
- Throw meaningful exceptions with context

## Common Patterns

### Database Source Plugin
- Use connection pooling
- Implement proper retry logic
- Handle connection failures gracefully

### File Transform Plugin
- Stream large files instead of loading into memory
- Implement progress reporting
- Handle encoding and format variations

### API Sink Plugin
- Batch API calls for efficiency
- Implement exponential backoff
- Handle rate limiting appropriately

---

**Next Steps**: See [Plugin Testing Guide](plugin-testing.md) and [Performance Optimization](performance-guide.md)