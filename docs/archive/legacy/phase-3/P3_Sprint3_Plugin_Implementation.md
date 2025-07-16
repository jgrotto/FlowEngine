# FlowEngine Sprint 3: Core Plugin Implementation

**Sprint Duration**: 5-6 days  
**Priority**: Critical - Prove end-to-end functionality  
**Goal**: Implement DelimitedSource and DelimitedSink plugins to enable CSV→CSV pipeline processing

## Sprint Objectives

This sprint focuses on implementing the first two production plugins that will:
1. Resolve the configuration binding issue through real implementation
2. Validate 200K+ rows/sec performance target
3. Enable end-to-end testing with actual file I/O
4. Establish patterns for JavaScriptTransform in Sprint 4

## Critical Pre-Sprint Fix

### Fix Plugin Manifest Dependencies
**Issue**: Template plugin incorrectly declares Core dependency
```json
// WRONG - in plugin.json
"dependencies": [
    {
        "name": "FlowEngine.Core",
        "version": ">=1.0.0",
        "isOptional": false
    }
]
```

**Fix**: Remove Core dependency from ALL plugin manifests
```json
// CORRECT - plugins should ONLY depend on Abstractions
"dependencies": [
    {
        "name": "FlowEngine.Abstractions",
        "version": ">=1.0.0",
        "isOptional": false
    }
]
```

## Day 1: DelimitedSource Plugin Foundation

### 1.1 Project Setup and Configuration
**Tasks**:
- Create `plugins/DelimitedSource/` project structure
- Copy five-component pattern from TemplatePlugin
- Update namespaces and class names
- Fix manifest dependencies (Abstractions only)

**File Structure**:
```
plugins/DelimitedSource/
├── DelimitedSource.csproj
├── plugin.json
├── Schemas/
│   └── DelimitedSourceConfiguration.json
├── DelimitedSourceConfiguration.cs
├── DelimitedSourceValidator.cs
├── DelimitedSourcePlugin.cs
├── DelimitedSourceProcessor.cs
└── DelimitedSourceService.cs
```

### 1.2 Configuration Implementation
```csharp
public sealed class DelimitedSourceConfiguration : IPluginConfiguration
{
    // File settings
    public string FilePath { get; init; } = "";
    public string Delimiter { get; init; } = ",";
    public bool HasHeaders { get; init; } = true;
    public string Encoding { get; init; } = "UTF-8";
    public int ChunkSize { get; init; } = 5000;
    
    // Schema definition (REQUIRED for ArrayRow)
    public ISchema? OutputSchema { get; init; }
    
    // Schema inference settings
    public bool InferSchema { get; init; } = false;
    public int InferenceSampleRows { get; init; } = 1000;
    
    // Error handling
    public bool SkipMalformedRows { get; init; } = false;
    public int MaxErrors { get; init; } = 100;
    
    // ArrayRow optimization
    public ImmutableDictionary<string, int> OutputFieldIndexes { get; init; } = 
        ImmutableDictionary<string, int>.Empty;
}
```

### 1.3 JSON Schema Definition
```json
{
  "$schema": "http://json-schema.org/draft-07/schema#",
  "type": "object",
  "required": ["filePath"],
  "properties": {
    "filePath": {
      "type": "string",
      "description": "Path to the delimited file"
    },
    "delimiter": {
      "type": "string",
      "default": ",",
      "maxLength": 1
    },
    "hasHeaders": {
      "type": "boolean",
      "default": true
    },
    "outputSchema": {
      "type": "object",
      "properties": {
        "columns": {
          "type": "array",
          "items": {
            "type": "object",
            "required": ["name", "type", "index"],
            "properties": {
              "name": { "type": "string" },
              "type": { "enum": ["string", "int32", "decimal", "datetime", "bool"] },
              "index": { "type": "integer", "minimum": 0 }
            }
          }
        }
      }
    }
  }
}
```

## Day 2: DelimitedSource Core Implementation

### 2.1 Service Implementation (Business Logic)
```csharp
public sealed class DelimitedSourceService : IPluginService
{
    private readonly ISchemaFactory _schemaFactory;
    private readonly IArrayRowFactory _arrayRowFactory;
    private readonly IChunkFactory _chunkFactory;
    private readonly ILogger<DelimitedSourceService> _logger;
    
    public async Task<IDataset> ReadFileAsync(
        DelimitedSourceConfiguration config,
        CancellationToken cancellationToken)
    {
        // Schema determination
        var schema = config.OutputSchema ?? 
            await InferSchemaAsync(config, cancellationToken);
        
        // Create chunks while reading file
        var chunks = new List<IChunk>();
        using var reader = new StreamReader(config.FilePath);
        
        // Skip headers if needed
        if (config.HasHeaders)
            await reader.ReadLineAsync();
        
        var rowBuffer = new List<IArrayRow>(config.ChunkSize);
        string? line;
        var rowCount = 0;
        
        while ((line = await reader.ReadLineAsync()) != null)
        {
            if (cancellationToken.IsCancellationRequested)
                break;
                
            try
            {
                var values = ParseLine(line, config.Delimiter);
                var row = _arrayRowFactory.CreateRow(schema, values);
                rowBuffer.Add(row);
                
                if (rowBuffer.Count >= config.ChunkSize)
                {
                    chunks.Add(_chunkFactory.CreateChunk(rowBuffer.ToArray()));
                    rowBuffer.Clear();
                }
                
                rowCount++;
            }
            catch (Exception ex) when (config.SkipMalformedRows)
            {
                _logger.LogWarning("Skipping malformed row {RowNumber}: {Error}", 
                    rowCount, ex.Message);
            }
        }
        
        // Final chunk
        if (rowBuffer.Count > 0)
            chunks.Add(_chunkFactory.CreateChunk(rowBuffer.ToArray()));
        
        return _datasetFactory.CreateDataset(schema, chunks);
    }
}
```

### 2.2 PluginManager Configuration Support
**Add to `PluginManager.CreatePluginConfigurationAsync`**:
```csharp
if (definition.Type.Contains("DelimitedSource"))
{
    return await CreateDelimitedSourceConfigurationAsync(definition);
}
```

## Day 3: DelimitedSource Testing & Validation

### 3.1 Performance Benchmark
```csharp
[Benchmark]
public async Task BenchmarkDelimitedSource()
{
    var config = new DelimitedSourceConfiguration
    {
        FilePath = "test-data/customers-1M.csv",
        ChunkSize = 10000,
        OutputSchema = _testSchema
    };
    
    var dataset = await _service.ReadFileAsync(config, CancellationToken.None);
    var rowCount = 0;
    
    await foreach (var chunk in dataset.GetChunksAsync())
    {
        rowCount += chunk.RowCount;
    }
    
    // Should achieve 200K+ rows/sec
}
```

### 3.2 Integration Tests
- Test with various delimiters (comma, tab, pipe)
- Test with/without headers
- Test malformed row handling
- Test schema inference
- Test encoding support

## Day 4: DelimitedSink Plugin

### 4.1 Configuration
```csharp
public sealed class DelimitedSinkConfiguration : IPluginConfiguration
{
    public string FilePath { get; init; } = "";
    public string Delimiter { get; init; } = ",";
    public bool IncludeHeaders { get; init; } = true;
    public string Encoding { get; init; } = "UTF-8";
    public string LineEnding { get; init; } = Environment.NewLine;
    public bool AppendMode { get; init; } = false;
    
    // Performance
    public int BufferSize { get; init; } = 65536;
    public int FlushInterval { get; init; } = 1000;
    
    // Input schema acceptance
    public ISchema? InputSchema { get; init; }
}
```

### 4.2 Service Implementation
```csharp
public sealed class DelimitedSinkService : IPluginService
{
    public async Task WriteDatasetAsync(
        IDataset dataset,
        DelimitedSinkConfiguration config,
        CancellationToken cancellationToken)
    {
        using var writer = new StreamWriter(
            config.FilePath, 
            config.AppendMode,
            Encoding.GetEncoding(config.Encoding),
            config.BufferSize);
        
        // Write headers
        if (config.IncludeHeaders && !config.AppendMode)
        {
            await WriteHeadersAsync(writer, dataset.Schema, config);
        }
        
        var rowCount = 0;
        await foreach (var chunk in dataset.GetChunksAsync(cancellationToken))
        {
            foreach (var row in chunk.Rows)
            {
                await WriteRowAsync(writer, row, config);
                rowCount++;
                
                if (rowCount % config.FlushInterval == 0)
                    await writer.FlushAsync();
            }
        }
        
        await writer.FlushAsync();
        _logger.LogInformation("Wrote {RowCount} rows to {FilePath}", 
            rowCount, config.FilePath);
    }
}
```

## Day 5-6: End-to-End Testing & Performance

### 5.1 Complete Pipeline Test
```yaml
# test-csv-pipeline.yaml
pipeline:
  name: "CSV Processing Pipeline"
  plugins:
    - name: "source"
      type: "DelimitedSource"
      config:
        filePath: "input/customers.csv"
        delimiter: ","
        hasHeaders: true
        outputSchema:
          columns:
            - name: "id"
              type: "int32"
              index: 0
            - name: "name"
              type: "string"
              index: 1
            - name: "email"
              type: "string"
              index: 2
              
    - name: "sink"
      type: "DelimitedSink"
      config:
        filePath: "output/customers-processed.csv"
        delimiter: ","
        includeHeaders: true
        
  connections:
    - from: "source"
      to: "sink"
```

### 5.2 Performance Validation
- Process 1M row CSV file
- Measure end-to-end throughput
- Validate memory usage stays bounded
- Confirm 200K+ rows/sec target

### 5.3 Fix Remaining Issues
- Address 9 failing unit tests
- Resolve any configuration binding issues
- Update documentation with real examples

## Definition of Done

### Functional Requirements ✅
- [ ] DelimitedSource reads CSV files correctly
- [ ] DelimitedSink writes CSV files correctly
- [ ] End-to-end pipeline processes data successfully
- [ ] Performance meets 200K+ rows/sec target
- [ ] Configuration binding works for both plugins

### Quality Requirements 🎯
- [ ] All unit tests pass (100%)
- [ ] Integration tests cover main scenarios
- [ ] No memory leaks during large file processing
- [ ] Cross-platform compatibility verified

### Documentation Requirements 📚
- [ ] Plugin implementation documented
- [ ] Configuration examples provided
- [ ] Performance benchmarks recorded
- [ ] Known limitations documented

## Risk Mitigation

### Risk 1: File I/O Performance
**Mitigation**: Use buffered streams, optimize chunk sizes, consider memory-mapped files for large datasets

### Risk 2: Schema Inference Complexity
**Mitigation**: Start with basic type detection, enhance incrementally

### Risk 3: Cross-Platform Path Issues
**Mitigation**: Use Path.Combine, test on both Windows and Linux early

## Sprint 4 Preview: JavaScriptTransform

With DelimitedSource and Sink complete, Sprint 4 will focus on:
- JavaScriptTransform plugin using Jint
- Schema transformation capabilities
- Performance optimization for script execution
- Complete CSV→Transform→CSV pipeline

## Success Metrics

- **Throughput**: 200K+ rows/sec for 1M row file
- **Memory**: <500MB for 1M row processing
- **Reliability**: Zero data loss or corruption
- **Compatibility**: Works on Windows, Linux, WSL2
- **Developer Experience**: Plugin creation time <2 hours using template