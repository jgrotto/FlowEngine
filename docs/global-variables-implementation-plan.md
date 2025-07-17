# FlowEngine Global Variables & Variable Substitution System
## Implementation Document - Sprint 1

**Status**: Implementation Ready  
**Authors**: Development Team  
**Date**: January 17, 2025  
**Version**: 1.0  
**Sprint Duration**: 2 weeks

---

## Executive Summary

This sprint implements a comprehensive global variables and variable substitution system for FlowEngine, enabling parameterized pipeline configurations with runtime overrides. This enhancement transforms static YAML configurations into flexible, reusable templates suitable for multi-environment deployments and CI/CD automation.

**Key Deliverables:**
- Global variable definition in YAML configurations
- Variable substitution engine with format support
- Enhanced CLI with modern command-line parsing
- Runtime variable override capabilities
- Built-in computed variables system

**Business Value**: Enables pipeline reusability, environment-specific deployments, and automation-ready configurations.

---

## Current State Analysis

### ✅ **Foundation Strengths**
- **Simplified YAML System**: Clean configuration architecture ready for extension
- **Modern Dependency Stack**: Can easily accommodate System.CommandLine
- **Performance Baseline**: 3,894 rows/sec maintained through recent optimizations
- **Clean Plugin Architecture**: Variables can be seamlessly passed to plugins

### 🎯 **User Pain Points Addressed**
- **Static Configurations**: Currently requires separate YAML files per environment
- **Hardcoded Paths**: File paths and settings embedded in configuration
- **Limited Automation**: No runtime parameter override capability
- **Repetitive Configuration**: Similar pipelines duplicated for different environments

---

## Technical Requirements

### Core Functionality Requirements

1. **Global Variable Definition**
   ```yaml
   variables:
     InputFile: "customers-50k.csv"
     OutputPath: "./output"
     Environment: "dev"
     BatchSize: 1000
   ```

2. **Variable Substitution**
   ```yaml
   stages:
     - plugin: DelimitedSource
       configuration:
         filePath: "{DataPath}/{InputFile}"
     - plugin: DelimitedSink
       configuration:
         filePath: "{OutputPath}/processed_{FileBase}_{RunDate:yyyy-MM-dd}.csv"
   ```

3. **Runtime Override**
   ```bash
   FlowEngine pipeline.yaml --var InputFile=newdata.csv --var Environment=prod
   ```

4. **Built-in Variables**
   - RunDate, RunDateUtc, Timestamp
   - ConfigFile, ConfigDir, WorkingDir
   - MachineName, UserName, ProcessId
   - Computed variables (FileBase, etc.)

### Performance Requirements
- **Substitution Performance**: <1ms per variable substitution
- **Pipeline Performance**: Maintain 3,894 rows/sec baseline
- **Memory Usage**: <10MB additional memory for variable system
- **Startup Impact**: <100ms additional startup time

### Compatibility Requirements
- **Existing Configurations**: All current YAML files work unchanged
- **Plugin Compatibility**: Variables available to all plugins via configuration
- **Cross-Platform**: Windows and Linux CLI support identical

---

## Technical Architecture

### Component Design

#### 1. GlobalVariables Class
```csharp
/// <summary>
/// Manages global variables and provides variable substitution capabilities.
/// </summary>
public sealed class GlobalVariables
{
    private readonly Dictionary<string, object> _variables = new();
    private readonly Dictionary<string, Func<object>> _computedVariables = new();
    
    /// <summary>
    /// Sets a static variable value.
    /// </summary>
    public void SetVariable(string name, object value);
    
    /// <summary>
    /// Gets a variable value with type conversion.
    /// </summary>
    public T GetVariable<T>(string name);
    
    /// <summary>
    /// Sets a computed variable that is evaluated on-demand.
    /// </summary>
    public void SetComputedVariable(string name, Func<object> valueFactory);
    
    /// <summary>
    /// Performs variable substitution on input string.
    /// Supports: {VariableName} and {VariableName:Format}
    /// </summary>
    public string SubstituteVariables(string input);
    
    /// <summary>
    /// Loads variables from command-line format "name=value".
    /// </summary>
    public void SetVariableFromString(string variableAssignment);
    
    /// <summary>
    /// Loads variable from environment variable.
    /// </summary>
    public void SetVariableFromEnvironment(string environmentVariableName);
    
    /// <summary>
    /// Loads variables from JSON file.
    /// </summary>
    public void LoadFromFile(string filePath);
}
```

#### 2. Variable Substitution Engine
```csharp
/// <summary>
/// High-performance variable substitution with regex-based pattern matching.
/// </summary>
public static class VariableSubstitutionEngine
{
    // Pattern: {VariableName} or {VariableName:Format}
    private static readonly Regex SubstitutionPattern = new(
        @"\{(?<name>[A-Za-z_][A-Za-z0-9_]*?)(?::(?<format>[^}]+))?\}",
        RegexOptions.Compiled | RegexOptions.CultureInvariant
    );
    
    public static string Substitute(string input, GlobalVariables variables);
    private static string FormatValue(object value, string format);
}
```

#### 3. Enhanced YAML Configuration
```csharp
public class YamlPipelineConfiguration
{
    /// <summary>
    /// Global variables defined in YAML configuration.
    /// </summary>
    public Dictionary<string, object>? Variables { get; set; }
    
    public List<YamlStageConfiguration> Stages { get; set; } = new();
}

public class YamlConfigurationAdapter
{
    /// <summary>
    /// Loads configuration with variable substitution.
    /// </summary>
    public PipelineConfigurationData LoadFromFile(
        string filePath, 
        GlobalVariables? runtimeVariables = null);
        
    private void PerformVariableSubstitution(
        YamlPipelineConfiguration config, 
        GlobalVariables variables);
}
```

#### 4. Enhanced CLI with System.CommandLine
```csharp
public class Program
{
    public static async Task<int> Main(string[] args)
    {
        var rootCommand = CreateRootCommand();
        return await rootCommand.InvokeAsync(args);
    }
    
    private static RootCommand CreateRootCommand()
    {
        var configFileArgument = new Argument<string>("config-file");
        var verboseOption = new Option<bool>("--verbose", "-v");
        var variableOption = new Option<string[]>("--var");
        var varFileOption = new Option<string>("--var-file");
        var varEnvOption = new Option<string[]>("--var-env");
        
        // Configure command structure
    }
}
```

#### 5. FlowEngineOptions Extension
```csharp
public class FlowEngineOptions
{
    public bool EnableConsoleLogging { get; set; } = true;
    public LogLevel LogLevel { get; set; } = LogLevel.Information;
    public string? PluginDirectory { get; set; } = null;
    
    /// <summary>
    /// Global variables for variable substitution.
    /// </summary>
    public GlobalVariables? GlobalVariables { get; set; }
}
```

---

## Implementation Plan

### **Week 1: Core Variable System**

#### **Day 1-2: GlobalVariables Foundation**
- [ ] Implement `GlobalVariables` class with core functionality
- [ ] Create `VariableSubstitutionEngine` with regex-based substitution
- [ ] Add comprehensive unit tests for variable operations
- [ ] Implement format string support for DateTime and numeric values

**Deliverables:**
```csharp
// Core functionality working
var variables = new GlobalVariables();
variables.SetVariable("Name", "test");
variables.SetVariable("Date", DateTime.Now);

var result = variables.SubstituteVariables("Hello {Name}, today is {Date:yyyy-MM-dd}");
// Result: "Hello test, today is 2025-01-17"
```

#### **Day 3: Built-in Variables System**
- [ ] Implement built-in variable registration
- [ ] Add computed variables for file operations
- [ ] Create variable context setup for configuration files
- [ ] Add error handling for undefined variables

**Built-in Variables:**
```csharp
// Automatically available
RunDate, RunDateUtc, Timestamp
ConfigFile, ConfigDir, WorkingDir  
MachineName, UserName, ProcessId
FileBase (computed from InputFile)
```

#### **Day 4-5: YAML Integration**
- [ ] Extend `YamlPipelineConfiguration` with `Variables` property
- [ ] Update `YamlConfigurationAdapter` to handle variable substitution
- [ ] Implement recursive substitution for nested configuration objects
- [ ] Add validation for variable references and circular dependencies

**Integration Result:**
```yaml
variables:
  InputFile: "data.csv"
  OutputPath: "./output"

stages:
  - plugin: DelimitedSource
    configuration:
      filePath: "{ConfigDir}/{InputFile}"
  - plugin: DelimitedSink
    configuration:
      filePath: "{OutputPath}/processed_{FileBase}_{RunDate:yyyy-MM-dd}.csv"
```

### **Week 2: Enhanced CLI & Advanced Features**

#### **Day 1-2: System.CommandLine Integration**
- [ ] Add `System.CommandLine` NuGet package to CLI project
- [ ] Implement modern command-line parsing with options
- [ ] Add `--var`, `--var-file`, and `--var-env` options
- [ ] Update argument validation and help text

**Enhanced CLI:**
```bash
FlowEngine pipeline.yaml --var InputFile=newdata.csv --var Environment=prod
FlowEngine pipeline.yaml --var-file prod-settings.json --verbose
FlowEngine pipeline.yaml --var-env DATABASE_URL --var-env API_KEY
```

#### **Day 3: Runtime Variable Override**
- [ ] Implement command-line variable parsing ("name=value" format)
- [ ] Add environment variable loading capability
- [ ] Create JSON variable file loading
- [ ] Implement variable precedence: CLI > File > YAML > Built-in

**Variable Loading Priority:**
1. Command-line `--var` options (highest priority)
2. Variable files loaded with `--var-file`
3. Environment variables loaded with `--var-env`
4. YAML-defined variables
5. Built-in variables (lowest priority)

#### **Day 4: Advanced Features**
- [ ] Implement nested object variable substitution
- [ ] Add array/list variable support
- [ ] Create variable validation and type checking
- [ ] Implement variable dependency resolution

**Advanced Substitution:**
```yaml
variables:
  Database:
    Host: "localhost"
    Port: 5432
    Name: "analytics_{Environment}"

stages:
  - plugin: DatabaseSource
    configuration:
      connectionString: "Server={Database.Host}:{Database.Port};Database={Database.Name}"
```

#### **Day 5: Integration Testing & Documentation**
- [ ] Comprehensive end-to-end testing with all features
- [ ] Performance validation (maintain 3,894 rows/sec baseline)
- [ ] Cross-platform testing (Windows/Linux)
- [ ] Update CLI help text and examples
- [ ] Create user documentation with examples

---

## Testing Strategy

### Unit Tests (Target: 95% Coverage)

#### **GlobalVariables Tests**
```csharp
[TestClass]
public class GlobalVariablesTests
{
    [TestMethod]
    public void SetVariable_WithValidInput_StoresValue()
    
    [TestMethod]
    public void SubstituteVariables_WithSimpleVariable_ReturnsSubstituted()
    
    [TestMethod]
    public void SubstituteVariables_WithFormat_AppliesFormatCorrectly()
    
    [TestMethod]
    public void SubstituteVariables_WithUndefinedVariable_ThrowsException()
    
    [TestMethod]
    public void SetComputedVariable_LazilyEvaluates_WhenAccessed()
}
```

#### **Variable Substitution Engine Tests**
```csharp
[TestClass]
public class VariableSubstitutionEngineTests
{
    [TestMethod]
    public void Substitute_WithNestedBraces_HandlesCorrectly()
    
    [TestMethod]
    public void Substitute_WithDateTimeFormat_FormatsCorrectly()
    
    [TestMethod]
    public void Substitute_WithNumericFormat_FormatsCorrectly()
    
    [TestMethod]
    public void Substitute_WithComplexString_SubstitutesAll()
}
```

#### **YAML Integration Tests**
```csharp
[TestClass]
public class YamlVariableIntegrationTests
{
    [TestMethod]
    public void LoadFromFile_WithVariables_SubstitutesInConfiguration()
    
    [TestMethod]
    public void LoadFromFile_WithRuntimeOverrides_PrioritizesCorrectly()
    
    [TestMethod]
    public void LoadFromFile_WithBuiltInVariables_ProvidesAutomatically()
}
```

### Integration Tests

#### **End-to-End Pipeline Tests**
```csharp
[TestMethod]
public async Task ExecutePipeline_WithVariableSubstitution_ProcessesCorrectly()
{
    // Test complete pipeline with variable substitution
    var variables = new GlobalVariables();
    variables.SetVariable("InputFile", "test-data.csv");
    variables.SetVariable("OutputPath", "./test-output");
    
    var options = new FlowEngineOptions { GlobalVariables = variables };
    var result = await FlowEngineCoordinator.ExecuteFromFileAsync("test-pipeline.yaml", options);
    
    Assert.IsTrue(result.IsSuccess);
    Assert.IsTrue(File.Exists("./test-output/processed_test-data_2025-01-17.csv"));
}
```

#### **CLI Integration Tests**
```csharp
[TestMethod]
public async Task CLI_WithVariableOverrides_ExecutesCorrectly()
{
    var args = new[] 
    { 
        "test-pipeline.yaml", 
        "--var", "InputFile=cli-test.csv",
        "--var", "Environment=test"
    };
    
    var exitCode = await Program.Main(args);
    Assert.AreEqual(0, exitCode);
}
```

### Performance Tests

#### **Variable Substitution Performance**
```csharp
[TestMethod]
public void SubstituteVariables_PerformanceBenchmark()
{
    // Ensure substitution completes in <1ms per operation
    var variables = new GlobalVariables();
    // ... setup test data
    
    var stopwatch = Stopwatch.StartNew();
    for (int i = 0; i < 1000; i++)
    {
        variables.SubstituteVariables(testString);
    }
    stopwatch.Stop();
    
    Assert.IsTrue(stopwatch.ElapsedMilliseconds < 1000); // <1ms per operation
}
```

#### **Pipeline Performance Validation**
```csharp
[TestMethod]
public async Task ExecutePipeline_WithVariables_MaintainsPerformance()
{
    // Validate 3,894 rows/sec baseline maintained
    var result = await ExecutePerformanceTest();
    Assert.IsTrue(result.RowsPerSecond >= 3894);
}
```

---

## Example Use Cases

### Use Case 1: Environment-Specific Deployment
```yaml
# pipeline-template.yaml
variables:
  Environment: "dev"
  DataPath: "./data"
  OutputPath: "./output"
  DatabaseHost: "localhost"
  LogLevel: "Information"

stages:
  - plugin: DelimitedSource
    configuration:
      filePath: "{DataPath}/customers_{Environment}.csv"
      
  - plugin: JavaScriptTransform
    configuration:
      script: |
        row.environment = '{Environment}';
        row.processed_date = '{RunDate:yyyy-MM-dd}';
        
  - plugin: DelimitedSink
    configuration:
      filePath: "{OutputPath}/processed_customers_{Environment}_{RunDate:yyyy-MM-dd}.csv"
```

**Usage:**
```bash
# Development environment
FlowEngine pipeline-template.yaml

# Production environment  
FlowEngine pipeline-template.yaml \
  --var Environment=prod \
  --var DataPath=/prod/data \
  --var OutputPath=/prod/output \
  --var DatabaseHost=prod-db.company.com \
  --var LogLevel=Warning
```

### Use Case 2: CI/CD Integration
```bash
#!/bin/bash
# Jenkins/GitHub Actions script

# Set build-specific variables
BUILD_DATE=$(date +%Y-%m-%d)
BUILD_NUMBER=${GITHUB_RUN_NUMBER:-"local"}

# Execute pipeline with runtime parameters
FlowEngine etl-pipeline.yaml \
  --var BuildDate="${BUILD_DATE}" \
  --var BuildNumber="${BUILD_NUMBER}" \
  --var-env DATABASE_CONNECTION_STRING \
  --var-env API_KEY \
  --var-file "${ENVIRONMENT_CONFIG_FILE}" \
  --verbose
```

### Use Case 3: Batch Processing with Dynamic Names
```yaml
variables:
  BatchId: "batch001"
  ProcessingDate: "{RunDate:yyyy-MM-dd}"
  InputPattern: "*.csv"
  
stages:
  - plugin: DelimitedSource
    configuration:
      filePath: "{InputPath}/{BatchId}_raw.csv"
      
  - plugin: JavaScriptTransform
    configuration:
      script: |
        row.batch_id = '{BatchId}';
        row.processing_timestamp = '{Timestamp}';
        
  - plugin: DelimitedSink
    configuration:
      filePath: "{OutputPath}/{BatchId}_processed_{ProcessingDate}_{Timestamp}.csv"
```

**Result:** `batch001_processed_2025-01-17_1737148800.csv`

### Use Case 4: Configuration Templating
```yaml
# base-template.yaml
variables:
  # Override these in specific configurations
  SourceType: "delimited"
  TransformScript: "// Default: no transformation"
  SinkType: "delimited"
  
stages:
  - plugin: "{SourceType}Source"
    configuration:
      filePath: "{InputFile}"
      
  - plugin: JavaScriptTransform
    configuration:
      script: "{TransformScript}"
      
  - plugin: "{SinkType}Sink"
    configuration:
      filePath: "{OutputFile}"
```

**Specialization:**
```bash
# Customer processing pipeline
FlowEngine base-template.yaml \
  --var InputFile=customers.csv \
  --var OutputFile=processed_customers.csv \
  --var TransformScript="row.customer_type = 'premium';"
```

---

## Documentation Updates

### CLI Help Text
```
FlowEngine CLI - High-performance data processing engine

USAGE:
    FlowEngine <config-file> [OPTIONS]

ARGUMENTS:
    <config-file>    Pipeline configuration file (YAML)

OPTIONS:
    -v, --verbose                Enable verbose logging
    --var <name=value>          Set variable (can be used multiple times)
    --var-file <file>           Load variables from JSON file
    --var-env <name>            Load variable from environment (can be used multiple times)
    -h, --help                  Show help information

EXAMPLES:
    FlowEngine pipeline.yaml
    FlowEngine pipeline.yaml --verbose
    FlowEngine pipeline.yaml --var InputFile=data.csv --var Environment=prod
    FlowEngine pipeline.yaml --var-file prod-config.json
    FlowEngine pipeline.yaml --var-env DATABASE_URL --var-env API_KEY

VARIABLE SUBSTITUTION:
    Variables can be used in configuration files using {VariableName} syntax.
    Format strings are supported: {RunDate:yyyy-MM-dd}
    
    Built-in variables:
        RunDate, RunDateUtc, Timestamp
        ConfigFile, ConfigDir, WorkingDir
        MachineName, UserName, ProcessId
        FileBase (derived from InputFile variable)
```

### YAML Configuration Guide
```markdown
## Global Variables in FlowEngine

### Defining Variables
```yaml
variables:
  InputFile: "data.csv"
  OutputPath: "./output"
  BatchSize: 1000
  Environment: "dev"
```

### Variable Substitution
Use `{VariableName}` syntax in any configuration value:
```yaml
stages:
  - plugin: DelimitedSource
    configuration:
      filePath: "{DataPath}/{InputFile}"
```

### Format Strings
Apply formatting to variables:
```yaml
variables:
  ProcessedFile: "output_{RunDate:yyyy-MM-dd_HH-mm-ss}.csv"
```

### Built-in Variables
FlowEngine provides these variables automatically:
- `RunDate`: Current date/time
- `ConfigFile`: Path to configuration file
- `FileBase`: Filename without extension (derived from InputFile)
```

---

## Dependencies

### New Package Dependencies
```xml
<!-- CLI Project -->
<PackageReference Include="System.CommandLine" Version="2.0.0-beta4.22272.1" />

<!-- Core Project (if needed for JSON variable files) -->
<PackageReference Include="System.Text.Json" Version="9.0.0" />
```

### Internal Dependencies
- **FlowEngine.Core**: Extended for variable support in configuration
- **FlowEngine.Abstractions**: May need IVariableProvider interface for plugins
- **Existing YAML System**: Enhanced with variable substitution

---

## Risk Assessment & Mitigation

### Technical Risks

#### **Performance Impact**
- **Risk**: Variable substitution adds processing overhead
- **Mitigation**: 
  - Compile regex patterns for performance
  - Cache substitution results where possible
  - Benchmark against 3,894 rows/sec baseline
  - Profile substitution performance in hot paths

#### **Complexity Inflation**
- **Risk**: Feature complexity violates our simplification principles
- **Mitigation**:
  - Keep variable system focused and minimal
  - Avoid advanced templating features (conditionals, loops)
  - Maintain clear separation of concerns
  - Regular architecture review against complexity goals

#### **Breaking Changes**
- **Risk**: YAML changes break existing configurations
- **Mitigation**:
  - Variables section is optional
  - Existing configurations work unchanged
  - Comprehensive backward compatibility testing
  - Clear migration documentation

### Implementation Risks

#### **Scope Creep**
- **Risk**: Feature requests for advanced templating
- **Mitigation**:
  - Define clear scope boundaries in this document
  - Focus on variable substitution, not templating language
  - Defer advanced features to future sprints
  - Maintain simplicity discipline

#### **Security Considerations**
- **Risk**: Variable injection or code execution
- **Mitigation**:
  - No code evaluation in variable substitution
  - Input validation for variable names and values
  - Sanitization of format strings
  - Clear security guidelines in documentation

#### **Cross-Platform Compatibility**
- **Risk**: Path and environment variable differences
- **Mitigation**:
  - Use Path.Combine for all path operations
  - Test on both Windows and Linux
  - Handle environment variable case sensitivity
  - Document platform-specific considerations

---

## Success Criteria

### Functional Success
- [ ] **Variable Definition**: YAML configurations can define global variables
- [ ] **Variable Substitution**: String values support {VariableName} and {VariableName:Format} syntax
- [ ] **CLI Override**: Command-line options can override any variable
- [ ] **Built-in Variables**: System provides useful automatic variables
- [ ] **Error Handling**: Clear error messages for undefined variables
- [ ] **Backward Compatibility**: All existing configurations work unchanged

### Performance Success
- [ ] **Processing Speed**: Maintain 3,894 rows/sec baseline throughput
- [ ] **Substitution Speed**: Variable substitution <1ms per operation
- [ ] **Memory Usage**: <10MB additional memory for variable system
- [ ] **Startup Time**: <100ms additional startup overhead

### Quality Success
- [ ] **Test Coverage**: 95% unit test coverage for variable system
- [ ] **Integration Tests**: Complete end-to-end validation
- [ ] **Cross-Platform**: Identical behavior on Windows and Linux
- [ ] **Documentation**: Complete user and developer documentation
- [ ] **Examples**: Real-world use case demonstrations

### User Experience Success
- [ ] **Intuitive Syntax**: Variable substitution feels natural in YAML
- [ ] **Clear CLI**: Command-line options are discoverable and well-documented
- [ ] **Error Messages**: Helpful guidance when variables are misconfigured
- [ ] **Performance**: No noticeable impact on pipeline execution time

---

## Post-Sprint Deliverables

### Code Deliverables
- [ ] `GlobalVariables` class with comprehensive functionality
- [ ] `VariableSubstitutionEngine` with regex-based substitution
- [ ] Enhanced `YamlConfigurationAdapter` with variable support
- [ ] Modernized CLI using System.CommandLine
- [ ] Comprehensive test suite with 95% coverage

### Documentation Deliverables
- [ ] Updated CLI help text with variable examples
- [ ] YAML configuration guide with variable syntax
- [ ] User documentation with real-world use cases
- [ ] Developer documentation for extending variable system
- [ ] Migration guide for advanced users

### Validation Deliverables
- [ ] Performance benchmark results
- [ ] Cross-platform compatibility report
- [ ] Backward compatibility validation
- [ ] Security review results
- [ ] User acceptance test results

---

## Next Sprint Preparation

This sprint creates the foundation for the next two practical enhancements you mentioned. The global variables system will likely enable:

1. **Enhanced configuration management** with more sophisticated override mechanisms
2. **Template and scripting capabilities** building on the variable substitution foundation
3. **Advanced pipeline orchestration** using variable-driven configurations

The clean, extensible architecture delivered in this sprint ensures these future enhancements can be implemented efficiently without compromising our simplification principles.

---

**Implementation Start Date**: January 20, 2025  
**Target Completion**: February 3, 2025  
**Next Review**: January 27, 2025 (mid-sprint checkpoint)  
**Sprint Goal**: Deliver production-ready global variables system that enhances FlowEngine's flexibility while maintaining performance and simplicity.