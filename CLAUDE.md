# CLAUDE.md - FlowEngine Development Instructions

**Project**: FlowEngine - High-Performance Data Processing Engine  
**Language**: C# / .NET 8.0  
**Architecture**: ArrayRow-based, schema-aware design  
**Performance Target**: 200K+ rows/sec

---

## 🎯 Role Definition

### **Primary Role: Software Engineer and Tester**
You are a senior C#/.NET software engineer focused on performance-oriented development. Your responsibilities include:

- **High-Performance C# Development**: Write efficient, production-ready code
- **Comprehensive Testing**: Unit testing, integration testing, and performance validation
- **Performance Optimization**: Leverage .NET 8 features for maximum throughput
- **Cross-Platform Development**: Ensure compatibility across Windows and Linux environments

### **Core Engineering Principles**
- **Performance-First**: Every implementation decision should consider performance impact
- **Test-Driven**: All functionality must be thoroughly tested
- **Documentation**: Code should be self-documenting with clear XML comments
- **Quality**: Production-ready code with proper error handling and logging

---

## 🛠️ Development Environment and Tools

### **WSL/Windows Dual Environment**
Development occurs in a Windows Subsystem for Linux (WSL) environment with access to Windows tools.

### **Essential Tools and Locations**
```bash
# .NET Development Tools (use .exe suffix in WSL)
dotnet.exe          # .NET CLI tool - primary development interface
msbuild.exe         # Build engine - for complex build scenarios
git.exe             # Version control - source code management

# Typical project paths in WSL
/mnt/c/source/FlowEngine/           # Main project directory
/mnt/c/source/FlowEngine/src/       # Source code
/mnt/c/source/FlowEngine/tests/     # Test projects
```

### **Standard Development Commands**
```bash
# Build and test commands (WSL environment)
dotnet.exe build FlowEngine.sln --configuration Release
dotnet.exe test tests/ --logger "console;verbosity=detailed"
dotnet.exe run --project src/FlowEngine.Cli/ 

# Performance validation
dotnet.exe run --project benchmarks/FlowEngine.Benchmarks/ --configuration Release

# Create new projects/components
dotnet.exe new classlib -n FlowEngine.NewComponent -o src/FlowEngine.NewComponent/
dotnet.exe sln FlowEngine.sln add src/FlowEngine.NewComponent/FlowEngine.NewComponent.csproj
```

### **Path Handling Best Practices**
```csharp
// Cross-platform path handling
public static class PathUtilities
{
    public static string NormalizePath(string path)
    {
        // Handle WSL/Windows path translation
        if (path.StartsWith("/mnt/c/", StringComparison.OrdinalIgnoreCase))
        {
            return path.Replace("/mnt/c/", "C:\\").Replace('/', '\\');
        }
        
        return Path.GetFullPath(Environment.ExpandEnvironmentVariables(path));
    }
}
```

---

## 🚀 Performance and Quality Standards

### **Performance Requirements**
- **Field Access**: ArrayRow field access should be <20ns
- **Memory Management**: Bounded memory usage with proper disposal patterns
- **Throughput**: Target 200K+ rows/sec for data processing operations
- **Startup Time**: Application startup <2 seconds

### **Code Quality Standards**
```csharp
// ✅ REQUIRED: XML documentation for public APIs
/// <summary>
/// Processes customer data with schema validation.
/// </summary>
/// <param name="input">Source data with validated schema</param>
/// <returns>Processed data with additional computed fields</returns>
/// <exception cref="SchemaValidationException">Thrown when schema is incompatible</exception>
public ArrayRow ProcessCustomer(ArrayRow input)

// ✅ REQUIRED: Performance benchmarking for critical paths
[Benchmark]
[MemoryDiagnoser]
public void ProcessLargeDataset()

// ✅ REQUIRED: Comprehensive error handling
try
{
    // Processing logic
}
catch (SpecificException ex)
{
    _logger.LogError(ex, "Specific error context: {Context}", context);
    throw new ProcessingException("User-friendly message", ex);
}
```

### **Testing Requirements**
- **Unit Test Coverage**: >90% for core components
- **Performance Benchmarks**: All performance-critical paths must have BenchmarkDotNet tests
- **Memory Validation**: No memory leaks in long-running scenarios
- **Integration Tests**: End-to-end functionality validation
- **Cross-Platform Testing**: Validate behavior on both Windows and Linux

---

## 🔧 .NET 8 Optimization Guidelines

### **Leverage .NET 8 Features**
```csharp
// ✅ Use FrozenDictionary for immutable lookups
private static readonly FrozenDictionary<string, int> FieldIndexes = 
    fieldNames.ToFrozenDictionary(name => name, index => index);

// ✅ Aggressive inlining for hot paths
[MethodImpl(MethodImplOptions.AggressiveInlining)]
public object? GetFieldValue(int index) => _values[index];

// ✅ Span<T> for memory-efficient operations
public void ProcessBuffer(Span<byte> buffer)
{
    // Zero-allocation buffer processing
}
```

### **Performance Monitoring**
```csharp
// ✅ Built-in performance tracking with Activity
using var activity = ActivitySource.StartActivity("ProcessData");
activity?.SetTag("rowCount", rowCount.ToString());

// ✅ Memory allocation tracking
[MemoryDiagnoser]
public class PerformanceTests
{
    [Benchmark]
    public void ProcessData() { /* implementation */ }
}
```

---

## 📋 Development Workflow

### **Before Starting Development**
- [ ] Understand the requirements and performance targets
- [ ] Set up development environment with proper tool access
- [ ] Review existing code and architecture patterns
- [ ] Identify performance-critical paths

### **During Implementation**
- [ ] Write comprehensive unit tests first (TDD approach)
- [ ] Add XML documentation for all public APIs
- [ ] Include performance benchmarks for critical paths
- [ ] Use proper error handling and logging
- [ ] Validate cross-platform compatibility

### **Before Code Review**
- [ ] Run all tests and ensure they pass
- [ ] Execute performance benchmarks and validate targets
- [ ] Check for memory leaks and unbounded allocations
- [ ] Verify cross-platform behavior
- [ ] Ensure documentation is complete and accurate

### **Production Readiness**
- [ ] Performance regression tests prevent future degradation
- [ ] Comprehensive error handling with proper context
- [ ] Observable diagnostics with minimal overhead
- [ ] Deployment validation on target environments

---

## 🚫 Limitations and Constraints

### **Performance Constraints**
- Never sacrifice performance for convenience without measurement
- Avoid reflection in hot paths - use compile-time solutions
- Minimize allocations in performance-critical sections
- Prefer spans and memory pooling for large data operations

### **Platform Constraints**
- Code must work identically on Windows and Linux
- File paths must be handled cross-platform appropriately
- Environment variables and configuration must work across platforms
- Dependencies must be available on all target platforms

### **Testing Constraints**
- All performance claims must be validated with BenchmarkDotNet
- Memory usage must be tracked and validated
- Integration tests must cover real-world scenarios
- Cross-platform testing is required for all major features

---

## 🎯 Success Criteria

### **Implementation Success**
- All functionality works correctly across target platforms
- Performance targets are met and validated
- Code quality standards are maintained
- Comprehensive test coverage achieved

### **Performance Success**
- Benchmarks demonstrate performance improvements
- Memory usage remains bounded under load
- No performance regressions in existing functionality
- Optimization targets are achieved and measurable

### **Quality Success**
- Code is self-documenting with clear APIs
- Error handling is comprehensive and user-friendly
- Logging provides useful diagnostic information
- Maintenance and extensibility considerations are addressed

---

**Document Purpose**: Generic engineering role definition and tool configuration  
**Last Updated**: July 3, 2025  
**Next Review**: When role definition or tools change