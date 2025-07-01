# FlowEngine Phase 3: Troubleshooting Guide

**Reading Time**: 3 minutes  
**Purpose**: Solve common problems quickly  

---

## Plugin Isolation Issues

### Problem: Plugin fails to load with assembly conflicts
```
Error: Could not load file or assembly 'Newtonsoft.Json, Version=13.0.0.0'
```

**Solution**: Check AssemblyLoadContext isolation
```bash
# Verify plugin isolation is enabled
dotnet run -- plugin validate --path ./MyPlugin --check-isolation

# View loaded assemblies
dotnet run -- debug assemblies --plugin MyPlugin

# Force isolation
# In plugin.yaml:
isolation:
  enabled: true
  allowedAssemblies:
    - "System.*"
    - "Microsoft.Extensions.*"
```

### Problem: Plugin components can't find dependencies
```
Error: Unable to resolve service for type 'IMyService'
```

**Solution**: Check service registration
```csharp
// In Plugin.cs constructor, verify all dependencies are registered
public MyPlugin(
    ILogger<MyPlugin> logger,          // ✅ Auto-registered
    MyPluginProcessor processor,       // ❌ Manual registration needed
    MyPluginService service)           // ❌ Manual registration needed

// Register in service configuration:
services.AddScoped<MyPluginProcessor>();
services.AddScoped<MyPluginService>();
```

---

## Five-Component Architecture Issues

### Problem: Configuration component validation fails
```
Error: CFG001 - Invalid field type 'int23'
```

**Solution**: Check field type definitions
```yaml
# ❌ Wrong
fields:
  - name: "age"
    type: "int23"  # Invalid type

# ✅ Correct  
fields:
  - name: "age"
    type: "int32"  # Valid type
```

Valid types: `string`, `int32`, `int64`, `float`, `double`, `bool`, `datetime`, `object`

### Problem: Validator component throws exceptions
```
Error: Schema validation failed - field 'name' not found
```

**Solution**: Verify schema field mapping
```csharp
// ❌ Wrong - accessing by name (slow + error-prone)
var name = row.GetString("name");

// ✅ Correct - use pre-calculated indexes
private int _nameIndex = -1;

public void Initialize(Schema schema)
{
    _nameIndex = schema.GetFieldIndex("name");
    if (_nameIndex == -1)
        throw new ArgumentException("Required field 'name' not found");
}

public string GetName(ArrayRow row) => row.GetString(_nameIndex);
```

### Problem: Processor component performance issues
```
Warning: Processing speed 1K rows/sec (expected >200K rows/sec)
```

**Solution**: Optimize ArrayRow access patterns
```csharp
// ❌ Slow - repeated field lookups
foreach (var row in chunk.Rows)
{
    var name = row.GetString("name");        // Lookup each time
    var age = row.GetInt32("age");           // Lookup each time
}

// ✅ Fast - pre-calculated indexes
var nameIndex = config.GetNameIndex();
var ageIndex = config.GetAgeIndex();
foreach (var row in chunk.Rows)
{
    var name = row.GetString(nameIndex);     // Direct access
    var age = row.GetInt32(ageIndex);        // Direct access
}
```

### Problem: Service component connection failures
```
Error: Service initialization failed - connection timeout
```

**Solution**: Implement retry logic and health checks
```csharp
public async Task InitializeAsync(MyPluginConfiguration config)
{
    var retryPolicy = Policy
        .Handle<HttpRequestException>()
        .WaitAndRetryAsync(3, retryAttempt => 
            TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)));
    
    await retryPolicy.ExecuteAsync(async () =>
    {
        var response = await _httpClient.GetAsync("/health");
        response.EnsureSuccessStatusCode();
    });
}
```

---

## Performance Bottleneck Identification

### Problem: Pipeline runs slower than expected
```bash
# First: Identify bottleneck component
dotnet run -- monitor pipeline --id pipeline-123 --breakdown

# Common bottlenecks:
# 1. Schema validation (CPU-bound)
# 2. Memory spillover (I/O-bound)  
# 3. External service calls (Network-bound)
# 4. ArrayRow field access (Memory-bound)
```

**CPU-Bound Issues**
```bash
# Symptoms: High CPU usage, low I/O
dotnet run -- monitor cpu --plugin MyPlugin

# Solutions:
# 1. Reduce validation strictness
validation:
  mode: "compatible"  # Instead of "strict"

# 2. Increase batch size
processing:
  batchSize: 5000     # Instead of 1000

# 3. Enable parallel processing  
processing:
  parallelism: 4      # Use multiple threads
```

**Memory-Bound Issues**
```bash
# Symptoms: Frequent spillover, high memory usage
dotnet run -- monitor memory --plugin MyPlugin --alert-threshold 80%

# Solutions:
# 1. Optimize ArrayRow field ordering
fields:
  - name: "frequently_used"  # Put first
    type: "string"
  - name: "rarely_used"      # Put last
    type: "object"

# 2. Increase memory limit
isolation:
  memoryLimit: "512MB"       # Instead of 256MB

# 3. Tune spillover threshold
processing:
  spilloverThreshold: "200MB" # Delay spillover
```

**I/O-Bound Issues**
```bash
# Symptoms: High disk usage, slow processing
dotnet run -- monitor disk --path ./temp

# Solutions:
# 1. Use faster storage for spillover
processing:
  spilloverPath: "/fast-ssd/temp"

# 2. Reduce spillover frequency
processing:
  batchSize: 2000            # Larger batches
  spilloverThreshold: "400MB" # Higher threshold

# 3. Enable compression
processing:
  spilloverCompression: true
```

---

## Schema Compatibility Problems

### Problem: Schema evolution breaks existing pipelines
```
Error: Incompatible schema change - field 'oldField' removed
```

**Solution**: Use schema versioning and migration
```yaml
# Version your schemas
schema:
  version: "2.0.0"
  compatibility: "backward"  # backward|forward|full
  
  fields:
    - name: "newField"
      type: "string"
      since: "2.0.0"         # Field introduction version
      
    - name: "oldField" 
      type: "string"
      deprecated: "2.0.0"     # Mark as deprecated
      removedIn: "3.0.0"      # When it will be removed
```

### Problem: Field type mismatches between plugins
```
Error: Cannot convert 'string' to 'int32' for field 'age'
```

**Solution**: Configure type coercion
```yaml
validation:
  mode: "compatible"
  typeCoercion:
    enabled: true
    rules:
      - from: "string"
        to: "int32"
        pattern: "^\\d+$"      # Only numeric strings
      - from: "string"  
        to: "datetime"
        format: "yyyy-MM-dd"   # Specific date format
```

---

## Memory and Resource Issues

### Problem: Out of memory errors during large file processing
```
Error: OutOfMemoryException - Unable to allocate chunk
```

**Solution**: Configure memory management
```bash
# Check current memory usage
dotnet run -- monitor memory --detailed

# Enable spillover before OOM
# In plugin.yaml:
processing:
  memoryLimit: "1GB"
  spilloverThreshold: "800MB"
  spilloverPath: "./temp"
  
isolation:
  memoryLimit: "1.2GB"       # Higher than processing limit
```

### Problem: Temporary files not cleaned up
```
Warning: Disk space low - temp files accumulating
```

**Solution**: Configure cleanup policies
```yaml
processing:
  spillover:
    cleanup: "immediate"      # immediate|batch|manual
    maxAge: "1h"             # Auto-cleanup after 1 hour
    maxSize: "10GB"          # Max total spillover size
```

---

## CLI and Development Issues

### Problem: CLI commands not found or fail
```bash
$ dotnet run -- plugin create --name Test
Command 'plugin' not found
```

**Solution**: Verify FlowEngine installation
```bash
# Check if FlowEngine is built
dotnet build --configuration Release

# Verify CLI is working
dotnet run -- --help
dotnet run -- --version

# If still failing, check PATH and working directory
which dotnet
pwd
```

### Problem: Plugin validation passes but runtime fails
```
Validation: ✅ All checks passed
Runtime: ❌ Plugin initialization failed
```

**Solution**: Use comprehensive validation
```bash
# Basic validation (structure only)
dotnet run -- plugin validate --path ./MyPlugin

# Comprehensive validation (includes runtime checks)
dotnet run -- plugin validate --path ./MyPlugin --comprehensive

# Test with actual data
dotnet run -- plugin test --path ./MyPlugin --data sample.csv --verbose
```

---

## Common Error Messages

| Error Code | Message | Quick Fix |
|------------|---------|-----------|
| **CFG001** | Invalid field type | Check spelling: `int32`, `string`, `datetime` |
| **CFG002** | Missing required field | Add field to schema or mark as optional |
| **CFG003** | Schema mismatch | Verify input/output compatibility |
| **ISO001** | Assembly load failed | Enable isolation, check dependencies |
| **ISO002** | Memory limit exceeded | Increase `memoryLimit` or enable spillover |
| **VAL001** | Validation rule failed | Check data format matches schema |
| **VAL002** | Required field missing | Ensure all required fields are present |
| **PROC001** | Processing timeout | Increase timeout or optimize processing |
| **PROC002** | Batch size too large | Reduce `batchSize` or increase memory |
| **SVC001** | Service unavailable | Check external service connectivity |
| **SVC002** | Authentication failed | Verify credentials and permissions |

---

## Emergency Debugging

### When Everything Fails
```bash
# 1. Enable maximum logging
export FLOWENGINE_LOG_LEVEL=Trace

# 2. Run with full diagnostics
dotnet run -- pipeline run --config pipeline.yaml --debug --verbose

# 3. Generate diagnostic report
dotnet run -- debug report --output debug-report.zip

# 4. Check system resources
dotnet run -- health check --detailed --include-system

# 5. Reset to known good state
dotnet run -- plugin uninstall --name ProblematicPlugin
dotnet run -- config restore --backup last-known-good.yaml
```

### Contact Support Information
- **Documentation**: Check [Plugin Development Guide](./Phase3-Plugin-Development-Guide.md)
- **Configuration**: Review [Configuration Reference](./Phase3-Configuration-Reference.md)  
- **Performance**: See [CLI Operations Guide](./Phase3-CLI-Operations-Guide.md)
- **GitHub Issues**: Create issue with diagnostic report
- **Support Email**: Include error codes and configuration files