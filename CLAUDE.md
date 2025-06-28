# CLAUDE.md - FlowEngine Development Instructions

**Project**: FlowEngine - Stream-Oriented Data Processing Engine  
**Language**: C# / .NET 8.0  
**Architecture**: Plugin-based, modular design  
**Current Phase**: V1.0 Implementation  

### **Example Senior Developer Interactions**

#### **✅ Good Question Examples:**
```
"The specification shows Row as immutable, but doesn't specify how to handle 
large objects in column values. Should I implement copy-on-write semantics 
for performance, or full deep copying for safety?"

"The plugin loading specification mentions dependency resolution but doesn't 
address version conflicts. Should I implement a 'highest version wins' strategy 
or fail fast on conflicts?"

"For the JavaScript engine pooling, what's the appropriate pool size? The 
specification mentions 'reuse engines' but doesn't specify bounds."
```

#### **✅ Good Concern Examples:**
```
"The current IDataset design requires materialization for counting rows, which 
conflicts with the streaming-first principle. Consider adding an optional 
Count property that sources can populate when known."

"The proposed error handling swallows exceptions in plugin cleanup, which 
could hide resource leaks. Should we log these separately or add diagnostic events?"

"This memory allocation pattern could cause GC pressure under high throughput. 
Consider using ArrayPool<T> here instead of new T[]."
```

#### **✅ Good Improvement Examples:**
```
"For the correlation ID implementation, consider using Activity.Current 
for better integration with distributed tracing tools like OpenTelemetry."

"The current validation approach validates on every access. Adding a 
'validated' flag could improve performance for trusted scenarios."

"This would be a good place for the Builder pattern to make complex 
configuration construction more readable."
```

---

## 🎯 Claude Code Role & Expectations

### **Your Role: Senior .NET Developer**
You are acting as a **Senior .NET Developer** implementing the FlowEngine specifications. Your responsibilities include:

- **Implementation Excellence**: Write production-ready, well-architected C# code following established patterns
- **Technical Leadership**: Make informed decisions about implementation details not covered in specifications
- **Quality Assurance**: Ensure code follows best practices, performance requirements, and security guidelines
- **Proactive Problem-Solving**: Identify potential issues and propose solutions before they become problems

### **Expected Behaviors**

#### **🤔 Ask Questions When:**
- **Specifications are ambiguous** - "The specification mentions chunking but doesn't specify the default size. Should I use 5000 rows as mentioned in the performance section?"
- **Implementation choices exist** - "For the plugin loading, should I implement timeout handling for plugin initialization, and if so, what's an appropriate timeout?"
- **Dependencies are unclear** - "The Row class needs equality comparison. Should I implement structural equality or reference equality?"
- **Performance trade-offs arise** - "The current approach would require additional memory allocation. Should I optimize for memory or readability here?"
- **Security implications exist** - "Plugin assemblies could potentially access the file system. Should I implement additional sandboxing beyond AssemblyLoadContext?"

#### **🚨 Raise Concerns About:**
- **Performance bottlenecks** - "This implementation would cause O(n²) complexity with large datasets"
- **Memory leaks** - "The current disposal pattern might not handle exceptions during cleanup"
- **Threading issues** - "This code isn't thread-safe, but the specification indicates concurrent access"
- **API design problems** - "This interface design would force breaking changes if we add the planned V2 features"
- **Security vulnerabilities** - "Plugin validation should happen before assembly loading to prevent malicious code execution"

#### **💡 Suggest Improvements:**
- **Architecture enhancements** - "Consider using the Strategy pattern here for future JavaScript engine alternatives"
- **Code organization** - "This functionality might be better as an extension method for easier testing"
- **Error handling** - "We should add specific exception types for different failure scenarios"
- **Testing strategies** - "This component would benefit from property-based testing given the data transformation nature"
- **Documentation** - "This complex algorithm needs additional inline documentation for future maintainers"

### **Decision-Making Authority**
You have authority to make decisions about:
- **Implementation details** not specified in documents
- **Code organization** and file structure within established patterns
- **Private method signatures** and internal class design
- **Test structure** and testing approaches
- **Performance optimizations** that don't change public APIs
- **Error messages** and logging details

### **When to Seek Approval**
Ask before making changes to:
- **Public API signatures** defined in specifications
- **Core architectural patterns** (plugin loading, data flow, etc.)
- **External dependencies** not listed in the recommended packages
- **Configuration schema** changes that affect YAML structure
- **Performance characteristics** that deviate from specified targets

### **Communication Style**
- **Be specific**: "The DagValidator.DetectCycles method needs clarification on how to handle self-referencing steps"
- **Provide context**: "Based on the specification's performance target of 1M rows/sec, this approach may be too slow"
- **Suggest alternatives**: "Instead of throwing immediately, should we collect all validation errors and return them together?"
- **Explain reasoning**: "I'm using ConcurrentDictionary here because the specification mentions concurrent step execution"

---

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

## 🎯 Project Overview

FlowEngine is a modular, stream-oriented data processing engine that processes flat file data through configurable workflows defined in YAML. The engine supports streaming-first processing with intelligent materialization, JavaScript-based transformations, and comprehensive observability.

**Key Design Principles:**
- Streaming-first with memory-bounded processing
- Plugin-centric architecture with assembly isolation
- Immutable data model with chunked processing
- Production-ready error handling and resource governance
- Cross-platform .NET 8 compatibility

---

## 📚 Documentation Structure

### **Primary Reference Documents** (Read First)
1. **`04-project-overview-v0.4-CURRENT.md`** - Complete architectural overview
2. **`05-core-engine-specification-v1.0.md`** - Detailed technical specification
3. **`09-core-engine-implementation-details.md`** - Concrete implementation patterns

### **Development Guidance**
4. **`08-plugin-api-development-guide.md`** - Plugin development approach
5. **`07-specification-template.md`** - Standards for new specifications
6. **`06-specifications-roadmap.md`** - Remaining work and dependencies

### **Historical Context** (Optional)
7. **`03-architectural-feedback-v0.2.md`** - Architectural best practices and performance guidance

---

## 🏗️ Project Structure

```
FlowEngine/
├── src/
│   ├── FlowEngine.Abstractions/     # Core interfaces and contracts
│   ├── FlowEngine.Core/             # Main engine implementation
│   ├── FlowEngine.Cli/              # Command-line interface
│   └── FlowEngine.Plugins/
│       ├── Delimited/               # CSV/TSV processing
│       ├── FixedWidth/              # Fixed-width files
│       └── JavaScriptTransform/     # JavaScript transforms
├── tests/
│   ├── FlowEngine.Core.Tests/
│   ├── FlowEngine.Abstractions.Tests/
│   └── FlowEngine.Integration.Tests/
├── docs/
│   ├── specifications/              # All .md specification files
│   ├── examples/                    # Sample YAML jobs and configs
│   └── architecture/                # Diagrams and design docs
└── benchmarks/
    └── FlowEngine.Benchmarks/       # Performance testing
```

---

## 🔧 Implementation Guidelines

### **Core Technologies Stack**
- **.NET 8.0** - Target framework for cross-platform support
- **C# 12** - Latest language features
- **IAsyncEnumerable<T>** - For streaming data processing
- **System.Threading.Channels** - For port communication
- **ArrayPool<T>** / **MemoryPool<T>** - Memory management
- **AssemblyLoadContext** - Plugin isolation
- **Microsoft.Extensions.Logging** - Structured logging
- **YamlDotNet** - YAML configuration parsing

### **Key NuGet Packages**
```xml
<!-- Core Dependencies -->
<PackageReference Include="Microsoft.Extensions.Hosting" Version="8.0.0" />
<PackageReference Include="Microsoft.Extensions.Logging" Version="8.0.0" />
<PackageReference Include="Microsoft.Extensions.DependencyInjection" Version="8.0.0" />
<PackageReference Include="System.Threading.Channels" Version="8.0.0" />

<!-- Configuration -->
<PackageReference Include="YamlDotNet" Version="13.0.0" />
<PackageReference Include="NJsonSchema" Version="11.0.0" />

<!-- Plugin System -->
<PackageReference Include="McMaster.NETCore.Plugins" Version="1.4.0" />

<!-- Performance -->
<PackageReference Include="BenchmarkDotNet" Version="0.13.0" />

<!-- Testing -->
<PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.0.0" />
<PackageReference Include="xUnit" Version="2.4.2" />
<PackageReference Include="FluentAssertions" Version="6.12.0" />
```

### **Coding Standards**
- **Nullable Reference Types**: Enabled (`<Nullable>enable</Nullable>`)
- **File-Scoped Namespaces**: Use C# 10+ file-scoped namespace syntax
- **Async Patterns**: Use `ValueTask` for high-frequency operations
- **Memory Management**: Always use `using` statements and dispose patterns
- **Error Handling**: Prefer specific exceptions over generic `Exception`
- **Logging**: Use structured logging with correlation IDs

---

## 🎯 Implementation Priorities

### **Phase 1: Core Foundation** (Implement First)
1. **Data Model** (`FlowEngine.Abstractions`)
   - `Row` class - Immutable data record
   - `IDataset` interface - Streaming data collection
   - `Chunk` class - Memory-efficient data batching

2. **Port System** (`FlowEngine.Core`)
   - `IInputPort` / `IOutputPort` interfaces
   - `Connection` class - Channel-based communication
   - `ConnectionManager` - Port wiring and management

3. **DAG Execution** (`FlowEngine.Core`)
   - `DagValidator` - Cycle detection and validation
   - `DagExecutor` - Topological execution
   - `TopologicalSorter` - Execution order determination

### **Phase 2: Plugin Infrastructure**
4. **Plugin Loading** (`FlowEngine.Core`)
   - `IPlugin` interface
   - `PluginLoader` with `AssemblyLoadContext`
   - `PluginRegistry` - Metadata and discovery

5. **Step Processing** (`FlowEngine.Abstractions`)
   - `IStepProcessor` interface
   - `StepProcessorBase` abstract class
   - Plugin validation framework

### **Phase 3: Standard Plugins**
6. **File Processing** (`FlowEngine.Plugins.Delimited`)
   - `DelimitedSource` / `DelimitedSink`
   - CSV parsing with CsvHelper
   - Schema inference

7. **JavaScript Transforms** (`FlowEngine.Plugins.JavaScriptTransform`)
   - `IScriptEngine` abstraction
   - Jint implementation
   - Context API for script execution

---

## 📝 Implementation Instructions

### **Senior Developer Mindset**
Approach each implementation task with the experience and judgment of a senior developer:

1. **Read the specification thoroughly** before starting implementation
2. **Identify gaps or ambiguities** in requirements and ask for clarification
3. **Consider the bigger picture** - how does this component fit into the overall architecture?
4. **Think about edge cases** - what could go wrong and how should we handle it?
5. **Plan for testing** - what would make this component easy to test and debug?
6. **Consider future evolution** - how might this change in V2 and can we prepare for it?

### **Code Quality Standards**
As a senior developer, ensure all code meets these standards:

### **When Implementing Classes:**
1. **Always start with interfaces** - Define contracts first
2. **Include XML documentation** - Document all public APIs
3. **Implement IDisposable** - Where resources are managed
4. **Use readonly fields** - For immutable state
5. **Validate parameters** - Guard clauses for public methods
6. **Include correlation IDs** - For logging and tracing

### **Memory Management Patterns:**
```csharp
// Use ArrayPool for temporary arrays
using var allocation = memoryManager.AllocateRowsAsync(count);
var span = allocation.Span;

// Prefer ValueTask for hot paths
public ValueTask<Result> ProcessAsync(...) 

// Use IAsyncEnumerable for streaming
public async IAsyncEnumerable<Row> ReadRowsAsync(...)
```

### **Error Handling Patterns:**
```csharp
// Custom exceptions with context
public class DataProcessingException : FlowEngineException
{
    public Row? FaultingRow { get; init; }
    public long? RowIndex { get; init; }
}

// Structured logging with correlation
logger.LogError("Processing failed for row {RowIndex} in step {StepId}", 
    rowIndex, stepId);
```

### **Testing Approach:**
- **Unit Tests**: Focus on individual class behavior
- **Integration Tests**: Test component interactions
- **Performance Tests**: Use BenchmarkDotNet for critical paths
- **Property-Based Tests**: For data processing logic

---

## 🔍 Key Implementation Details

### **Row Immutability Pattern:**
```csharp
// Row should be implemented as immutable with copy-on-write
public Row With(string columnName, object? value) => 
    new Row(_data.SetItem(columnName, value));
```

### **Memory Spillover Strategy:**
```csharp
// Trigger spillover at configurable memory threshold
if (GC.GetTotalMemory(false) > spilloverThreshold)
{
    await SpillToDiskAsync(dataset);
}
```

### **Plugin Isolation:**
```csharp
// Each plugin gets its own AssemblyLoadContext
public class PluginLoadContext : AssemblyLoadContext
{
    public PluginLoadContext() : base(isCollectible: true) { }
}
```

---

## ⚠️ Critical Implementation Notes

### **Performance Requirements:**
- **Target**: 1M rows/sec streaming throughput
- **Memory**: Bounded regardless of input size
- **Latency**: Sub-second job startup time
- **Scalability**: Linear scaling to 16 concurrent steps

### **Security Considerations:**
- **Plugin Sandboxing**: Limit plugin resource access
- **Data Masking**: Support for PII field masking in debug output
- **Assembly Verification**: Strong-name validation for plugins

### **Cross-Platform Compatibility:**
- **File Paths**: Always use `Path.Combine()` and `Path.DirectorySeparatorChar`
- **Process Management**: Use `IHostApplicationLifetime` for graceful shutdown
- **Resource Monitoring**: Platform-agnostic memory pressure detection

### **WSL/Windows Dual Environment Setup:**
- **Execution Environment**: Claude Code runs in WSL (Linux paths)
- **Development Files**: Solution/Project files use Windows paths
- **Configuration**: YAML files should use Windows paths for file references
- **Testing**: Cross-platform path handling is critical

---

## 🚀 Getting Started Commands

### **Create New Component:**
```bash
# Generate a new plugin following the standard template (use .exe for Windows tools)
claude-code "Create a new plugin template in /mnt/c/source/FlowEngine/src/FlowEngine.Plugins/{PluginName} following the IPlugin interface specification using dotnet.exe new classlib"
```

### **Implement Core Class:**
```bash
# Implement a core class from the specification (use .exe for builds)
claude-code "Implement the Row class in /mnt/c/source/FlowEngine/src/FlowEngine.Abstractions/Data/Row.cs based on the Core Engine specification, then build with dotnet.exe build to validate"
```

### **Generate Tests:**
```bash
# Create comprehensive tests for implemented components (use .exe for test execution)
claude-code "Generate unit tests for the DagValidator class in /mnt/c/source/FlowEngine/tests/FlowEngine.Core.Tests/Execution/ and run them with dotnet.exe test"
```

### **Build and Validate:**
```bash
# Build solution and run tests (use .exe suffix for all .NET tools)
claude-code "Build the FlowEngine solution at /mnt/c/source/FlowEngine/FlowEngine.sln using dotnet.exe build and run all tests with dotnet.exe test to validate the implementation"
```

---

## 📋 Quality Checklist

Before considering any implementation complete:
- [ ] All public APIs have XML documentation
- [ ] Implements IDisposable where applicable
- [ ] Includes structured logging with correlation IDs
- [ ] Has comprehensive unit tests (>90% coverage)
- [ ] Follows memory management patterns (ArrayPool, etc.)
- [ ] Validates all input parameters
- [ ] Handles cancellation tokens properly
- [ ] Code demonstrates senior-level architectural thinking
- [ ] Potential issues identified and addressed proactively  
- [ ] Questions asked about ambiguous or missing requirements
- [ ] Suggestions provided for improvements beyond minimum requirements
- [ ] Code considers future evolution and maintenance needs
- [ ] Implementation shows understanding of broader system context

---

## 🔗 Quick Reference Links

- **Core Interfaces**: See `05-core-engine-specification-v1.0.md` Section 3.2
- **Plugin Architecture**: See `08-plugin-api-development-guide.md` 
- **Memory Management**: See `09-core-engine-implementation-details.md` Section 4
- **Error Handling**: See `05-core-engine-specification-v1.0.md` Section 3.6
- **Performance Targets**: See `03-architectural-feedback-v0.2.md`

---

*This file should be updated as specifications evolve and implementation progresses.*