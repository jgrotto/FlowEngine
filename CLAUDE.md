# CLAUDE.md - FlowEngine Phase 3 Development Instructions

**Version**: 3.0  
**Phase**: Phase 3 - Plugin Ecosystem & Enterprise Features  
**Last Updated**: June 30, 2025  
**Purpose**: AI Assistant instructions for FlowEngine Phase 3 development

---

## Role and Context

You are an expert C#/.NET developer assisting with FlowEngine Phase 3 implementation. Phase 3 focuses on building a robust plugin ecosystem using the **five-component architecture pattern** while maintaining the exceptional performance characteristics (14.3M rows/sec capability) achieved in Phases 1 and 2.

### Current Project State
- **Phase 1**: ✅ Complete - ArrayRow architecture delivering 3.25x performance improvement
- **Phase 2**: ✅ Complete - Plugin system, DAG execution, monitoring all operational
- **Phase 3**: 🚧 Active - Plugin ecosystem expansion and enterprise features

---

## Phase 3 Architecture Overview

### Five-Component Plugin Pattern (MANDATORY)
Every plugin MUST implement these five components:
```
1. Configuration → Schema-mapped YAML configuration with embedded field definitions
2. Validator     → Configuration and schema validation with detailed errors
3. Plugin        → Lifecycle management and metadata provider
4. Processor     → Orchestration between framework and business logic
5. Service       → Core business logic with ArrayRow optimization
```

### Critical ArrayRow Requirements
- **Explicit Field Indexes**: All fields must use sequential indexing (0, 1, 2, ...)
- **Pre-calculated Access**: Use `GetFieldIndex()` once, then direct array access
- **No String Lookups**: Never use field names in processing loops
- **Schema Validation**: Validate sequential indexing before processing

---

## Development Guidelines

### Code Generation Rules

1. **Always Use Five Components**
   - Even simple plugins need all five components
   - Use provided templates as starting point
   - Maintain clear separation of concerns

2. **ArrayRow Performance Patterns**
   ```csharp
   // ✅ CORRECT - Pre-calculate indexes
   var nameIndex = schema.GetFieldIndex("name");
   var ageIndex = schema.GetFieldIndex("age");
   
   foreach (var row in chunk.Rows)
   {
       var name = row.GetString(nameIndex);  // O(1) access
       var age = row.GetInt32(ageIndex);     // O(1) access
   }
   
   // ❌ WRONG - String lookups in loop
   foreach (var row in chunk.Rows)
   {
       var name = row.GetValue("name");  // O(n) lookup
       var age = row.GetValue("age");    // O(n) lookup
   }
   ```

3. **Schema Definition Requirements**
   ```yaml
   # Every plugin configuration MUST define explicit schemas
   configuration:
     schema:
       fields:
         - name: "id"
           type: "int32"
           index: 0        # REQUIRED: Sequential indexing
         - name: "name"
           type: "string"
           index: 1        # REQUIRED: Must be index + 1
   ```

### Response Patterns

1. **Component Implementation Queries**
   - Always provide all five components
   - Include extensive comments in templates
   - Show ArrayRow optimization patterns
   - Add performance considerations

2. **Configuration Examples**
   - Always include schema with explicit indexes
   - Show both simple and complex scenarios
   - Include validation rules
   - Demonstrate schema evolution

3. **Performance Optimization**
   - Emphasize pre-calculated field indexes
   - Show memory pooling patterns
   - Include chunking strategies
   - Demonstrate batch processing

4. **Error Handling**
   - Validation at configuration time
   - Graceful degradation patterns
   - Detailed error context
   - Recovery strategies

---

## CLI-First Development

All operations must be available through CLI:
```bash
# Plugin operations
flowengine plugin create --name MyPlugin --type Transform
flowengine plugin validate --path ./MyPlugin
flowengine plugin test --path ./MyPlugin --data sample.csv
flowengine plugin benchmark --path ./MyPlugin --rows 1000000

# Schema operations
flowengine schema infer --input data.csv --output schema.yaml
flowengine schema validate --schema schema.yaml --data test.csv
flowengine schema migrate --from v1.yaml --to v2.yaml

# Pipeline operations
flowengine pipeline validate --config pipeline.yaml
flowengine pipeline run --config pipeline.yaml --monitor
flowengine pipeline export --config pipeline.yaml --format docker
```

---

## Phase 3 Specific Patterns

### Plugin Categories
1. **Source Plugins**: DelimitedSource, JsonSource, DatabaseSource
2. **Transform Plugins**: DataEnrichment, JavaScriptTransform, SchemaMapper
3. **Sink Plugins**: DatabaseSink, ApiSink, FileSink
4. **Utility Plugins**: Validator, Router, ErrorHandler

### Enterprise Features
1. **Hot-Swapping**: Zero-downtime plugin updates
2. **Schema Evolution**: Backward-compatible migrations
3. **Script Engine**: Core-managed, not plugin-managed
4. **Monitoring**: Real-time metrics and health checks

---

## Script Engine Architecture

### Core Service Pattern (IMPORTANT)
JavaScript/Script engines are managed by **core services**, NOT individual plugins:

```csharp
// ✅ CORRECT - Plugin uses injected service
public class JavaScriptTransformService
{
    private readonly IScriptEngineService _scriptEngine; // From core
    
    public JavaScriptTransformService(IScriptEngineService scriptEngine)
    {
        _scriptEngine = scriptEngine;
    }
}

// ❌ WRONG - Plugin manages its own engine
public class JavaScriptTransformService
{
    private readonly ObjectPool<ScriptEngine> _enginePool; // Don't do this
}
```

### Rationale for Core Management
- **Resource Efficiency**: Single engine pool for entire application
- **Script Caching**: Compiled scripts shared across plugins
- **Security**: Centralized script validation and sandboxing
- **Performance**: Unified monitoring and optimization
- **Configuration**: Single point of engine configuration

---

## Testing Patterns

### Unit Testing Plugins
```csharp
// Test each component independently
[TestClass]
public class MyPluginValidatorTests
{
    [TestMethod]
    public void Validate_WithInvalidSchema_ReturnsErrors()
    {
        // Arrange
        var validator = new MyPluginValidator();
        var config = new MyPluginConfiguration { /* invalid */ };
        
        // Act
        var result = validator.ValidateConfiguration(config);
        
        // Assert
        Assert.IsFalse(result.IsValid);
        Assert.Contains("Sequential indexing required", result.Errors);
    }
}
```

### Integration Testing
```csharp
// Test complete plugin flow
[TestMethod]
public async Task Plugin_ProcessesData_MaintainsPerformance()
{
    // Arrange
    var plugin = CreatePlugin();
    var testData = GenerateTestData(10000);
    var stopwatch = Stopwatch.StartNew();
    
    // Act
    var result = await plugin.ProcessAsync(testData);
    stopwatch.Stop();
    
    // Assert
    Assert.IsTrue(result.Success);
    Assert.IsTrue(stopwatch.ElapsedMilliseconds < 50); // <50ms for 10K rows
    Assert.AreEqual(10000, result.ProcessedRows);
}
```

### Performance Benchmarking
```csharp
[Benchmark]
public async Task BenchmarkArrayRowAccess()
{
    var schema = CreateSchema();
    var nameIndex = schema.GetFieldIndex("name"); // Pre-calculated
    
    foreach (var row in _testChunk.Rows)
    {
        var name = row.GetString(nameIndex); // O(1) access
        // Process...
    }
}
```

---

## Common Anti-Patterns to Avoid

### 1. **String-Based Field Access in Loops**
```csharp
// ❌ NEVER DO THIS
foreach (var row in chunk.Rows)
{
    var value = row["fieldName"]; // String lookup each time
}
```

### 2. **Creating Services in Processor**
```csharp
// ❌ WRONG - Creates new service each time
public async Task<ProcessResult> ProcessAsync(IDataset input)
{
    var service = new MyPluginService(); // Don't create here
}

// ✅ CORRECT - Inject service
public MyPluginProcessor(MyPluginService service)
{
    _service = service; // Injected
}
```

### 3. **Synchronous I/O in Async Methods**
```csharp
// ❌ BLOCKS THREAD
public async Task ProcessAsync()
{
    var data = File.ReadAllText(path); // Sync I/O
}

// ✅ CORRECT
public async Task ProcessAsync()
{
    var data = await File.ReadAllTextAsync(path); // Async I/O
}
```

### 4. **Ignoring Cancellation Tokens**
```csharp
// ❌ Can't be cancelled
public async Task LongRunningOperation()
{
    await Task.Delay(5000);
}

// ✅ CORRECT - Respects cancellation
public async Task LongRunningOperation(CancellationToken cancellationToken)
{
    await Task.Delay(5000, cancellationToken);
}
```

---

## Documentation Standards

### Code Comments
```csharp
/// <summary>
/// Processes input data using ArrayRow optimization for O(1) field access.
/// </summary>
/// <param name="input">Input dataset with pre-validated schema</param>
/// <param name="cancellationToken">Cancellation token for cooperative cancellation</param>
/// <returns>ProcessResult with success/failure status and processed row count</returns>
/// <remarks>
/// Performance: Processes 200K+ rows/second on standard hardware.
/// Memory: Uses bounded memory regardless of input size.
/// </remarks>
public async Task<ProcessResult> ProcessAsync(
    IDataset input, 
    CancellationToken cancellationToken)
```

### YAML Configuration Comments
```yaml
# Plugin configuration with embedded schema definitions
configuration:
  # REQUIRED: Explicit schema for ArrayRow optimization
  schema:
    fields:
      - name: "customerId"
        type: "int32"
        index: 0          # MUST be sequential: 0, 1, 2...
        required: true
        description: "Unique customer identifier"
```

---

## Phase 3 Success Criteria

When generating code or reviewing implementations, ensure:

1. ✅ **Five Components**: All plugins have Configuration, Validator, Plugin, Processor, Service
2. ✅ **ArrayRow Optimization**: Pre-calculated indexes, no string lookups in loops
3. ✅ **CLI Integration**: All operations available through command-line
4. ✅ **Performance**: Maintains 200K+ rows/sec throughput
5. ✅ **Error Handling**: Comprehensive validation and graceful degradation
6. ✅ **Documentation**: XML comments and usage examples
7. ✅ **Testing**: Unit tests for each component, integration tests for flow
8. ✅ **Monitoring**: Metrics and health checks implemented

---

## Quick Reference

### Create New Plugin
```bash
flowengine plugin create --name MyPlugin --type Transform
```

### Validate Plugin
```bash
flowengine plugin validate --path ./MyPlugin --comprehensive
```

### Test Performance
```bash
flowengine plugin benchmark --path ./MyPlugin --rows 1000000
```

## 🐧 WSL Development Environment

### **Environment Context**
Claude Code is running in **WSL (Windows Subsystem for Linux)** with a mixed Windows/Linux development setup:

- **Execution Environment**: WSL Ubuntu/Linux (use Linux paths for tools)
- **File System**: Windows NTFS accessed via WSL (use Windows paths in configs)
- **Development Tools**: .NET CLI, editors run in WSL
- **Project Files**: Solution/project files reference Windows paths

### **Path Handling Strategy**

#### **For Tool Execution (WSL Context):**
```bash
# Use .exe suffix for Windows tools executed from WSL
dotnet.exe build /mnt/c/source/FlowEngine/FlowEngine.sln
dotnet.exe test /mnt/c/source/FlowEngine/tests/FlowEngine.Core.Tests/
dotnet.exe run --project /mnt/c/source/FlowEngine/src/FlowEngine.Cli/

# MSBuild for advanced scenarios
msbuild.exe /mnt/c/source/FlowEngine/FlowEngine.sln /p:Configuration=Release

# File operations in code generation (Linux tools)
touch /mnt/c/source/FlowEngine/src/FlowEngine.Core/NewClass.cs
mkdir -p /mnt/c/source/FlowEngine/src/FlowEngine.Plugins/NewPlugin/
```

#### **For Project Files (Windows Context):**
```xml
<!-- .csproj files should use Windows-style paths -->
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputPath>..\..\bin\$(Configuration)\</OutputPath>
  </PropertyGroup>
  
  <ItemGroup>
    <ProjectReference Include="..\FlowEngine.Abstractions\FlowEngine.Abstractions.csproj" />
    <Content Include="templates\*.yaml">
      <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
    </Content>
  </ItemGroup>
</Project>
```

#### **For YAML Configuration Files (Windows Context):**
```yaml
# Configuration files should reference Windows paths
job:
  steps:
    - id: read-data
      type: DelimitedSource
      config:
        path: "C:\\Data\\input\\customers.csv"  # Windows path
        
    - id: write-output
      type: DelimitedSink
      config:
        path: "${OUTPUT_PATH}\\processed\\customers.csv"  # Windows path with variables
```

### **Code Implementation Guidelines for Dual Environment:**

#### **Always Use .NET Path APIs:**
```csharp
// CORRECT: Cross-platform path handling
string configPath = Path.Combine(basePath, "config", "job.yaml");
string outputDir = Path.GetDirectoryName(outputPath);

// CORRECT: Platform-agnostic directory separators
string[] pathParts = filePath.Split(Path.DirectorySeparatorChar);

// AVOID: Hard-coded path separators
string badPath = basePath + "\\config\\job.yaml";  // ❌ Windows-only
string alsoBad = basePath + "/config/job.yaml";    // ❌ Linux-only
```

#### **Environment Variable Handling:**
```csharp
// Handle both Windows and WSL environment variables
public static string ResolvePath(string path)
{
    // Expand environment variables first
    path = Environment.ExpandEnvironmentVariables(path);
    
    // Handle WSL path translation if needed
    if (path.StartsWith("/mnt/c/", StringComparison.OrdinalIgnoreCase))
    {
        // Running in WSL, but config might have Windows paths
        return path.Replace("/mnt/c/", "C:\\").Replace('/', '\\');
    }
    
    return Path.GetFullPath(path);
}
```

#### **File Existence Checks:**
```csharp
// Robust file checking for WSL/Windows
public static bool FileExistsAnyCase(string filePath)
{
    if (File.Exists(filePath)) return true;
    
    // Try case variations for Linux file systems
    var directory = Path.GetDirectoryName(filePath);
    var fileName = Path.GetFileName(filePath);
    
    if (Directory.Exists(directory))
    {
        return Directory.GetFiles(directory, fileName, SearchOption.TopDirectoryOnly)
                       .Any(f => string.Equals(Path.GetFileName(f), fileName, 
                                              StringComparison.OrdinalIgnoreCase));
    }
    
    return false;
}
```

### **Development Workflow Commands:**

#### **Solution/Project Management:**
```bash
# Create new projects (use .exe suffix for Windows tools in WSL)
dotnet.exe new classlib -n FlowEngine.Plugins.NewPlugin -o /mnt/c/source/FlowEngine/src/FlowEngine.Plugins/NewPlugin/
dotnet.exe sln /mnt/c/source/FlowEngine/FlowEngine.sln add /mnt/c/source/FlowEngine/src/FlowEngine.Plugins/NewPlugin/

# Build and test
dotnet.exe build /mnt/c/source/FlowEngine/FlowEngine.sln
dotnet.exe test /mnt/c/source/FlowEngine/tests/ --logger "console;verbosity=detailed"

# Package and publish
dotnet.exe pack /mnt/c/source/FlowEngine/src/FlowEngine.Core/ --configuration Release
dotnet.exe publish /mnt/c/source/FlowEngine/src/FlowEngine.Cli/ --configuration Release --runtime win-x64
```

#### **File Operations:**
```bash
# Create directory structures
mkdir -p /mnt/c/source/FlowEngine/src/FlowEngine.Plugins/NewPlugin/{Models,Services,Validators}

# Generate files
cat > /mnt/c/source/FlowEngine/src/FlowEngine.Core/NewInterface.cs << 'EOF'
namespace FlowEngine.Core;

public interface INewInterface
{
    // Interface definition
}
EOF
```

### **Testing Path Scenarios:**
When implementing file handling code, always test these scenarios:
1. **Windows absolute paths**: `C:\Data\file.csv`
2. **Linux absolute paths**: `/home/user/data/file.csv`
3. **Relative paths**: `../data/file.csv`
4. **UNC paths**: `\\server\share\file.csv`
5. **Environment variables**: `${HOME}/data/file.csv`
6. **Mixed separators**: `C:\Data/subfolder\file.csv`

### **WSL-Specific Debugging:**
```bash
# Check if running in WSL
if grep -qi microsoft /proc/version; then
    echo "Running in WSL"
    # Use .exe suffix for Windows tools
    DOTNET_CMD="dotnet.exe"
    MSBUILD_CMD="msbuild.exe"
else
    echo "Running in native Linux"
    # Use native tools
    DOTNET_CMD="dotnet"
    MSBUILD_CMD="msbuild"
fi

# Convert between Windows and WSL paths
wslpath -w /mnt/c/source/FlowEngine/  # → C:\source\FlowEngine\
wslpath -u "C:\\source\\FlowEngine\\" # → /mnt/c/source/FlowEngine/

# Verify tool availability
which dotnet.exe || echo "dotnet.exe not found in PATH"
which msbuild.exe || echo "msbuild.exe not found in PATH"
```
---

### Common Issues
- **Performance degradation**: Check for string-based field access
- **Memory leaks**: Ensure proper disposal patterns
- **Schema errors**: Validate sequential field indexing
- **Service failures**: Check dependency injection configuration

---

**Remember**: FlowEngine Phase 3 is about building a robust plugin ecosystem while maintaining the exceptional performance achieved in earlier phases. Every decision should support both goals.