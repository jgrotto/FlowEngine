# 🔧 Cycle 2 Architectural Foundation Sprint

**Sprint Name**: Plugin Architecture Self-Registration & Schema Validation  
**Sprint Period**: July 15 - August 2, 2025 (3 weeks)  
**Sprint Type**: Architectural Foundation  
**Priority**: Critical Infrastructure  

---

## 🎯 Sprint Objectives

### **Primary Goal: Enable True Plugin Extensibility with Schema Validation**
Fix the fundamental architectural flaw where 3rd party developers cannot create plugins without modifying Core. Implement plugin self-registration pattern with comprehensive schema exposure that enables zero-Core-modification plugin development with full validation support.

### **Success Criteria:**
1. **✅ 3rd Party Plugin Creation**: External developers can create plugins without touching Core
2. **✅ Backward Compatibility**: All existing plugins continue working unchanged
3. **✅ Self-Registration Working**: Plugins automatically register their configuration providers
4. **✅ Schema Validation**: Plugins expose configuration and data schemas for validation
5. **✅ Pipeline Validation**: End-to-end pipeline compatibility validation
6. **✅ Documentation Updated**: Complete plugin development guide with schema examples
7. **✅ Foundation Cleanup**: NuGet packages standardized, dead code removed

### **Target Performance:**
- **Plugin Registration Time**: <100ms for discovery and registration
- **Configuration Creation**: <10ms per plugin configuration
- **Schema Validation**: <5ms per configuration validation
- **Pipeline Validation**: <50ms for complete pipeline validation
- **Zero Core Dependencies**: New plugins depend only on Abstractions

---

## 🚨 Current State Analysis

### **Critical Architectural Flaws:**
```csharp
// Problem 1: Core contains plugin-specific configurations
/src/FlowEngine.Core/Configuration/JavaScriptTransformConfiguration.cs
/src/FlowEngine.Core/Configuration/DelimitedSourceConfiguration.cs
/src/FlowEngine.Core/Configuration/DelimitedSinkConfiguration.cs

// Problem 2: Hard-coded plugin mappings in Core
RegisterMapping("JavaScriptTransform.JavaScriptTransformPlugin", new PluginConfigurationMapping
{
    CreateConfigurationAsync = async (definition) => await CreateJavaScriptTransformConfigurationAsync(definition)
});

// Problem 3: No schema validation or pipeline compatibility checking
// YAML errors discovered at runtime, not configuration time
```

### **Impact Assessment:**
- **Current**: No 3rd party plugins possible
- **Risk**: Every new plugin requires Core modification
- **Technical Debt**: Coupling increases with each plugin
- **Extensibility**: Fundamentally broken plugin ecosystem
- **Validation**: No early error detection for configuration or pipeline issues

---

## 📋 Implementation Strategy

### **Phase 1: Self-Registration Architecture with Schema Support (Week 1)**

#### **1.1 Create Enhanced Plugin Configuration Provider Interface**
```csharp
// New in FlowEngine.Abstractions
namespace FlowEngine.Abstractions.Plugins
{
    /// <summary>
    /// Allows plugins to provide their own configuration creation logic with schema validation.
    /// </summary>
    public interface IPluginConfigurationProvider
    {
        /// <summary>
        /// Determines if this provider can handle the specified plugin type.
        /// </summary>
        bool CanHandle(string pluginTypeName);
        
        /// <summary>
        /// Creates configuration for the plugin from YAML definition.
        /// </summary>
        Task<IPluginConfiguration> CreateConfigurationAsync(IPluginDefinition definition);
        
        /// <summary>
        /// Plugin type name this provider handles.
        /// </summary>
        string PluginTypeName { get; }
        
        /// <summary>
        /// Configuration type this provider creates.
        /// </summary>
        Type ConfigurationType { get; }
        
        // === NEW SCHEMA VALIDATION METHODS ===
        
        /// <summary>
        /// Gets the JSON schema for validating this plugin's configuration.
        /// Used by UI and pipeline validation.
        /// </summary>
        string GetConfigurationJsonSchema();
        
        /// <summary>
        /// Validates a configuration definition before creating the plugin configuration.
        /// Provides early error detection and clear error messages.
        /// </summary>
        ValidationResult ValidateConfiguration(IPluginDefinition definition);
        
        /// <summary>
        /// Gets expected input schema requirements (null if accepts any).
        /// Used for pipeline compatibility validation.
        /// </summary>
        ISchemaRequirement? GetInputSchemaRequirement();
        
        /// <summary>
        /// Determines output schema from configuration.
        /// Used for downstream plugin compatibility validation.
        /// </summary>
        ISchema GetOutputSchema(IPluginConfiguration configuration);
        
        /// <summary>
        /// Gets plugin metadata for discovery and documentation.
        /// </summary>
        PluginMetadata GetPluginMetadata();
    }
}
```

#### **1.2 Create Schema Requirement Interface**
```csharp
// New in FlowEngine.Abstractions
namespace FlowEngine.Abstractions.Data
{
    /// <summary>
    /// Defines schema requirements for plugin input validation.
    /// </summary>
    public interface ISchemaRequirement
    {
        /// <summary>
        /// Validates if the provided schema meets plugin requirements.
        /// </summary>
        ValidationResult ValidateSchema(ISchema schema);
        
        /// <summary>
        /// Gets required fields for this plugin.
        /// </summary>
        IEnumerable<FieldRequirement> RequiredFields { get; }
        
        /// <summary>
        /// Indicates if plugin can accept any schema (flexible plugins).
        /// </summary>
        bool AcceptsAnySchema { get; }
        
        /// <summary>
        /// Gets human-readable description of requirements.
        /// </summary>
        string RequirementDescription { get; }
    }
    
    /// <summary>
    /// Defines a required field for plugin input.
    /// </summary>
    public class FieldRequirement
    {
        public string FieldName { get; init; } = string.Empty;
        public Type ExpectedType { get; init; } = typeof(object);
        public bool IsRequired { get; init; } = true;
        public string? Description { get; init; }
        public IEnumerable<string>? AllowedValues { get; init; }
    }
    
    /// <summary>
    /// Plugin metadata for discovery and documentation.
    /// </summary>
    public class PluginMetadata
    {
        public string Name { get; init; } = string.Empty;
        public string Description { get; init; } = string.Empty;
        public string Version { get; init; } = string.Empty;
        public string Author { get; init; } = string.Empty;
        public PluginCategory Category { get; init; }
        public IEnumerable<string> Tags { get; init; } = [];
        public Uri? DocumentationUrl { get; init; }
        public Uri? SourceUrl { get; init; }
    }
    
    public enum PluginCategory
    {
        Source,
        Transform,
        Sink,
        Utility
    }
}
```

#### **1.3 Create Provider Discovery Service**
```csharp
// New in FlowEngine.Core
namespace FlowEngine.Core.Configuration
{
    /// <summary>
    /// Discovers and manages plugin configuration providers with schema validation.
    /// </summary>
    public class PluginConfigurationProviderRegistry
    {
        private readonly Dictionary<string, IPluginConfigurationProvider> _providers = new();
        private readonly ILogger<PluginConfigurationProviderRegistry> _logger;
        private readonly JsonSchemaValidator _schemaValidator;
        
        /// <summary>
        /// Registers a configuration provider.
        /// </summary>
        public void RegisterProvider(IPluginConfigurationProvider provider);
        
        /// <summary>
        /// Gets provider for specific plugin type.
        /// </summary>
        public IPluginConfigurationProvider? GetProvider(string pluginTypeName);
        
        /// <summary>
        /// Discovers providers from assemblies using attribute scanning.
        /// </summary>
        public Task DiscoverProvidersAsync(IEnumerable<Assembly> assemblies);
        
        /// <summary>
        /// Validates configuration against plugin's JSON schema.
        /// </summary>
        public ValidationResult ValidatePluginConfiguration(string pluginTypeName, IPluginDefinition definition);
        
        /// <summary>
        /// Gets all registered providers with metadata.
        /// </summary>
        public IEnumerable<(IPluginConfigurationProvider Provider, PluginMetadata Metadata)> GetAllProviders();
    }
}
```

#### **1.4 Update PluginConfigurationMapper with Schema Validation**
```csharp
// Modified FlowEngine.Core.Configuration.PluginConfigurationMapper
public class PluginConfigurationMapper : IPluginConfigurationMapper
{
    private readonly PluginConfigurationProviderRegistry _providerRegistry;
    private readonly ILogger<PluginConfigurationMapper> _logger;
    
    public async Task<IPluginConfiguration> CreateConfigurationAsync(IPluginDefinition definition)
    {
        // Validate configuration against schema first
        var validationResult = _providerRegistry.ValidatePluginConfiguration(definition.Type, definition);
        if (!validationResult.IsValid)
        {
            throw new ConfigurationException($"Plugin configuration validation failed for {definition.Type}: {string.Join(", ", validationResult.Errors)}");
        }
        
        // Try plugin-specific provider first
        var provider = _providerRegistry.GetProvider(definition.Type);
        if (provider != null)
        {
            return await provider.CreateConfigurationAsync(definition);
        }
        
        // Fallback to generic configuration
        _logger.LogWarning("No specific provider found for {PluginType}, using generic configuration", definition.Type);
        return await CreateGenericConfigurationAsync(definition);
    }
}
```

### **Phase 2: Plugin Migration with Schema Implementation (Week 2)**

#### **2.1 Create JavaScriptTransform Provider with Schema**
```csharp
// New in plugins/JavaScriptTransform/
namespace JavaScriptTransform
{
    [PluginConfigurationProvider] // Attribute for auto-discovery
    public class JavaScriptTransformConfigurationProvider : IPluginConfigurationProvider
    {
        public string PluginTypeName => "JavaScriptTransform.JavaScriptTransformPlugin";
        public Type ConfigurationType => typeof(JavaScriptTransformConfiguration);
        
        public bool CanHandle(string pluginTypeName) 
            => pluginTypeName == PluginTypeName;
        
        public string GetConfigurationJsonSchema()
        {
            return """
            {
              "$schema": "http://json-schema.org/draft-07/schema#",
              "type": "object",
              "title": "JavaScript Transform Configuration",
              "description": "Configuration for JavaScript-based data transformation",
              "properties": {
                "Script": {
                  "type": "string",
                  "minLength": 1,
                  "description": "JavaScript code containing process(context) function",
                  "examples": ["function process(context) { context.output.setField('result', 'value'); return true; }"]
                },
                "OutputSchema": {
                  "type": "object",
                  "description": "Defines the output schema for transformed data",
                  "properties": {
                    "Fields": {
                      "type": "array",
                      "minItems": 1,
                      "items": {
                        "type": "object",
                        "properties": {
                          "Name": { "type": "string", "minLength": 1 },
                          "Type": { 
                            "type": "string", 
                            "enum": ["string", "integer", "decimal", "double", "boolean", "datetime", "date", "time"],
                            "description": "Data type for the output field"
                          },
                          "Required": { "type": "boolean", "default": false },
                          "Description": { "type": "string" }
                        },
                        "required": ["Name", "Type"],
                        "additionalProperties": false
                      }
                    }
                  },
                  "required": ["Fields"],
                  "additionalProperties": false
                },
                "Engine": {
                  "type": "object",
                  "description": "JavaScript engine configuration",
                  "properties": {
                    "Timeout": { "type": "integer", "minimum": 100, "maximum": 30000, "default": 5000 },
                    "MemoryLimit": { "type": "integer", "minimum": 1048576, "maximum": 104857600, "default": 10485760 },
                    "EnableCaching": { "type": "boolean", "default": true },
                    "EnableDebugging": { "type": "boolean", "default": false }
                  },
                  "additionalProperties": false
                },
                "Performance": {
                  "type": "object",
                  "description": "Performance monitoring configuration",
                  "properties": {
                    "TargetThroughput": { "type": "integer", "minimum": 1, "default": 120000 },
                    "EnableMonitoring": { "type": "boolean", "default": true },
                    "LogPerformanceWarnings": { "type": "boolean", "default": true }
                  },
                  "additionalProperties": false
                }
              },
              "required": ["Script", "OutputSchema"],
              "additionalProperties": false
            }
            """;
        }
        
        public ValidationResult ValidateConfiguration(IPluginDefinition definition)
        {
            var errors = new List<string>();
            var config = definition.Configuration ?? new Dictionary<string, object>();
            
            // Validate script exists and contains process function
            if (!config.TryGetValue("Script", out var scriptObj) || string.IsNullOrWhiteSpace(scriptObj?.ToString()))
            {
                errors.Add("Script is required and cannot be empty");
            }
            else
            {
                var script = scriptObj.ToString()!;
                if (!script.Contains("function process(context)") && !script.Contains("process = function(context)"))
                {
                    errors.Add("Script must contain a process(context) function");
                }
            }
            
            // Validate output schema exists and has fields
            if (!config.TryGetValue("OutputSchema", out var schemaObj))
            {
                errors.Add("OutputSchema is required");
            }
            else if (schemaObj is Dictionary<string, object> schemaDict)
            {
                if (!schemaDict.TryGetValue("Fields", out var fieldsObj) || 
                    fieldsObj is not IEnumerable<object> fields || 
                    !fields.Any())
                {
                    errors.Add("OutputSchema.Fields is required and must contain at least one field");
                }
            }
            
            return errors.Count == 0 
                ? ValidationResult.Success() 
                : ValidationResult.Failure(errors);
        }
        
        public ISchemaRequirement? GetInputSchemaRequirement()
        {
            // JavaScript transform can accept any input schema
            return new FlexibleSchemaRequirement
            {
                AcceptsAnySchema = true,
                RequirementDescription = "JavaScript Transform can process any input schema. Access fields using context.input.getValue('fieldName')."
            };
        }
        
        public ISchema GetOutputSchema(IPluginConfiguration configuration)
        {
            var jsConfig = (JavaScriptTransformConfiguration)configuration;
            return CreateSchemaFromOutputDefinition(jsConfig.OutputSchema);
        }
        
        public PluginMetadata GetPluginMetadata()
        {
            return new PluginMetadata
            {
                Name = "JavaScript Transform",
                Description = "Transform data using custom JavaScript code with full context API support",
                Version = "1.0.0",
                Author = "FlowEngine Team",
                Category = PluginCategory.Transform,
                Tags = ["javascript", "transform", "scripting", "custom"],
                DocumentationUrl = new Uri("https://flowengine.dev/docs/plugins/javascript-transform"),
                SourceUrl = new Uri("https://github.com/flowengine/plugins/javascript-transform")
            };
        }
        
        public async Task<IPluginConfiguration> CreateConfigurationAsync(IPluginDefinition definition)
        {
            var config = definition.Configuration ?? new Dictionary<string, object>();
            
            return new JavaScriptTransformConfiguration
            {
                Name = definition.Name,
                Script = GetRequiredValue<string>(config, "Script"),
                OutputSchema = await CreateOutputSchemaAsync(config),
                Engine = CreateEngineConfiguration(config),
                Performance = CreatePerformanceConfiguration(config)
            };
        }
        
        // Helper methods...
    }
}
```

#### **2.2 Create Schema Requirement Implementations**
```csharp
// New in FlowEngine.Abstractions or Core
public class FlexibleSchemaRequirement : ISchemaRequirement
{
    public bool AcceptsAnySchema { get; init; } = true;
    public string RequirementDescription { get; init; } = "Accepts any input schema";
    public IEnumerable<FieldRequirement> RequiredFields => [];
    
    public ValidationResult ValidateSchema(ISchema schema)
    {
        return ValidationResult.Success();
    }
}

public class SpecificSchemaRequirement : ISchemaRequirement
{
    public bool AcceptsAnySchema => false;
    public string RequirementDescription { get; init; } = string.Empty;
    public IEnumerable<FieldRequirement> RequiredFields { get; init; } = [];
    
    public ValidationResult ValidateSchema(ISchema schema)
    {
        var errors = new List<string>();
        
        foreach (var requirement in RequiredFields.Where(r => r.IsRequired))
        {
            if (!schema.TryGetIndex(requirement.FieldName, out _))
            {
                errors.Add($"Required field '{requirement.FieldName}' not found in schema");
            }
            else
            {
                var column = schema.Columns.First(c => c.Name == requirement.FieldName);
                if (!requirement.ExpectedType.IsAssignableFrom(column.DataType))
                {
                    errors.Add($"Field '{requirement.FieldName}' has type {column.DataType.Name}, expected {requirement.ExpectedType.Name}");
                }
            }
        }
        
        return errors.Count == 0 
            ? ValidationResult.Success() 
            : ValidationResult.Failure(errors);
    }
}
```

#### **2.3 Migrate Delimited Plugins**
- Create `DelimitedSourceConfigurationProvider` with CSV format schema
- Create `DelimitedSinkConfigurationProvider` with output format schema
- Move configuration logic from Core to respective plugins
- Add comprehensive JSON schemas for file format configurations

#### **2.4 Remove Core Plugin Configurations**
- Delete `JavaScriptTransformConfiguration.cs` from Core
- Delete `DelimitedSourceConfiguration.cs` from Core  
- Delete `DelimitedSinkConfiguration.cs` from Core
- Remove hard-coded mappings from `PluginConfigurationMapper`

### **Phase 3: Pipeline Validation & Foundation Cleanup (Week 3)**

#### **3.1 Create Pipeline Validator**
```csharp
// New in FlowEngine.Core
namespace FlowEngine.Core.Validation
{
    /// <summary>
    /// Validates complete pipeline configurations for compatibility and correctness.
    /// </summary>
    public class PipelineValidator
    {
        private readonly PluginConfigurationProviderRegistry _providerRegistry;
        private readonly ILogger<PipelineValidator> _logger;
        
        /// <summary>
        /// Validates complete pipeline configuration.
        /// </summary>
        public ValidationResult ValidatePipeline(PipelineConfiguration pipeline)
        {
            var errors = new List<string>();
            
            // 1. Validate individual plugin configurations
            foreach (var plugin in pipeline.Plugins)
            {
                var pluginValidation = _providerRegistry.ValidatePluginConfiguration(plugin.Type, plugin);
                if (!pluginValidation.IsValid)
                {
                    errors.AddRange(pluginValidation.Errors.Select(e => $"Plugin '{plugin.Name}': {e}"));
                }
            }
            
            // 2. Validate plugin chain compatibility
            var compatibilityErrors = ValidatePluginChainCompatibility(pipeline.Plugins);
            errors.AddRange(compatibilityErrors);
            
            // 3. Validate pipeline structure
            var structureErrors = ValidatePipelineStructure(pipeline);
            errors.AddRange(structureErrors);
            
            return errors.Count == 0 
                ? ValidationResult.Success() 
                : ValidationResult.Failure(errors);
        }
        
        /// <summary>
        /// Validates that plugin outputs are compatible with downstream plugin inputs.
        /// </summary>
        private IEnumerable<string> ValidatePluginChainCompatibility(IEnumerable<IPluginDefinition> plugins)
        {
            var errors = new List<string>();
            var pluginList = plugins.ToList();
            
            for (int i = 0; i < pluginList.Count - 1; i++)
            {
                var currentPlugin = pluginList[i];
                var nextPlugin = pluginList[i + 1];
                
                var currentProvider = _providerRegistry.GetProvider(currentPlugin.Type);
                var nextProvider = _providerRegistry.GetProvider(nextPlugin.Type);
                
                if (currentProvider != null && nextProvider != null)
                {
                    // Get output schema from current plugin
                    var currentConfig = await currentProvider.CreateConfigurationAsync(currentPlugin);
                    var outputSchema = currentProvider.GetOutputSchema(currentConfig);
                    
                    // Check if next plugin can accept this schema
                    var inputRequirement = nextProvider.GetInputSchemaRequirement();
                    if (inputRequirement != null && !inputRequirement.AcceptsAnySchema)
                    {
                        var compatibility = inputRequirement.ValidateSchema(outputSchema);
                        if (!compatibility.IsValid)
                        {
                            errors.AddRange(compatibility.Errors.Select(e => 
                                $"Incompatible connection between '{currentPlugin.Name}' and '{nextPlugin.Name}': {e}"));
                        }
                    }
                }
            }
            
            return errors;
        }
        
        private IEnumerable<string> ValidatePipelineStructure(PipelineConfiguration pipeline)
        {
            var errors = new List<string>();
            
            // Must have at least one plugin
            if (!pipeline.Plugins.Any())
            {
                errors.Add("Pipeline must contain at least one plugin");
                return errors;
            }
            
            // First plugin should be a source
            var firstPlugin = pipeline.Plugins.First();
            var firstProvider = _providerRegistry.GetProvider(firstPlugin.Type);
            if (firstProvider?.GetPluginMetadata().Category != PluginCategory.Source)
            {
                errors.Add("Pipeline must start with a Source plugin");
            }
            
            // Last plugin should be a sink  
            var lastPlugin = pipeline.Plugins.Last();
            var lastProvider = _providerRegistry.GetProvider(lastPlugin.Type);
            if (lastProvider?.GetPluginMetadata().Category != PluginCategory.Sink)
            {
                errors.Add("Pipeline must end with a Sink plugin");
            }
            
            return errors;
        }
    }
}
```

#### **3.2 NuGet Package Standardization**
```xml
<!-- New file: Directory.Packages.props -->
<Project>
  <PropertyGroup>
    <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>
  </PropertyGroup>
  
  <ItemGroup>
    <!-- Core Framework -->
    <PackageVersion Include="Microsoft.Extensions.Logging" Version="8.0.1" />
    <PackageVersion Include="Microsoft.Extensions.Logging.Abstractions" Version="8.0.1" />
    <PackageVersion Include="Microsoft.Extensions.DependencyInjection" Version="8.0.0" />
    <PackageVersion Include="Microsoft.Extensions.DependencyInjection.Abstractions" Version="8.0.2" />
    <PackageVersion Include="Microsoft.Extensions.Configuration" Version="8.0.0" />
    <PackageVersion Include="Microsoft.Extensions.ObjectPool" Version="8.0.10" />
    <PackageVersion Include="Microsoft.Extensions.Primitives" Version="8.0.0" />
    
    <!-- Data Processing -->
    <PackageVersion Include="CsvHelper" Version="31.0.2" />
    <PackageVersion Include="System.Collections.Immutable" Version="8.0.0" />
    <PackageVersion Include="System.Threading.Channels" Version="8.0.0" />
    <PackageVersion Include="System.Memory" Version="4.5.5" />
    
    <!-- JavaScript Engines -->
    <PackageVersion Include="Jint" Version="4.3.0" />
    <PackageVersion Include="Microsoft.ClearScript.V8" Version="7.5.0" />
    <PackageVersion Include="Microsoft.ClearScript.V8.Native.win-x64" Version="7.5.0" />
    
    <!-- Schema Validation -->
    <PackageVersion Include="NJsonSchema" Version="11.0.2" />
    <PackageVersion Include="NJsonSchema.Annotations" Version="11.0.2" />
    <PackageVersion Include="YamlDotNet" Version="15.1.2" />
    
    <!-- Testing -->
    <PackageVersion Include="BenchmarkDotNet" Version="0.15.2" />
    <PackageVersion Include="Microsoft.NET.Test.Sdk" Version="17.8.0" />
    <PackageVersion Include="xunit" Version="2.6.2" />
    <PackageVersion Include="xunit.runner.visualstudio" Version="2.5.3" />
    
    <!-- Reflection and JSON -->
    <PackageVersion Include="Namotion.Reflection" Version="3.1.2" />
    <PackageVersion Include="Newtonsoft.Json" Version="13.0.3" />
  </ItemGroup>
</Project>
```

#### **3.3 Dead Code Removal & Cleanup**
- Remove all `.bak` files from tests/
- Remove abandoned `Phase3TemplatePlugin` directory
- Clean up duplicate `IMemoryManager` interface
- Consolidate scattered configuration files
- Update project references to use PackageReference instead of direct dependencies

#### **3.4 Documentation Updates**
- Update Plugin Developer Guide with self-registration pattern and schema examples
- Create "Creating Your First Plugin with Schema Validation" tutorial
- Document configuration provider pattern with JSON schema examples
- Update architecture documentation with validation flow
- Create schema validation best practices guide

---

## 🧪 Testing Strategy

### **Unit Tests (New & Enhanced):**
```csharp
// Schema validation tests
[Test]
public void JavaScriptTransformProvider_ValidateConfiguration_ShouldReturnErrors_ForInvalidScript()

[Test]
public void JavaScriptTransformProvider_GetConfigurationJsonSchema_ShouldReturnValidJsonSchema()

[Test]
public void SpecificSchemaRequirement_ValidateSchema_ShouldReturnErrors_ForMissingRequiredFields()

// Provider registry tests
[Test]
public async Task PluginConfigurationProviderRegistry_ShouldDiscoverProvidersFromAssembly()

[Test]
public void PluginConfigurationProviderRegistry_ValidatePluginConfiguration_ShouldUseProviderSchema()

// Pipeline validation tests
[Test]
public void PipelineValidator_ValidatePipeline_ShouldDetectIncompatiblePluginChain()

[Test]
public void PipelineValidator_ValidatePipeline_ShouldRequireSourceAndSinkPlugins()
```

### **Integration Tests (Updated):**
```csharp
// End-to-end plugin loading with schema validation
[Test]
public async Task PluginManager_ShouldLoadPlugin_WithSchemaValidatedConfiguration()

// Backward compatibility with existing YAML
[Test]
public async Task ExistingYamlConfigs_ShouldPassSchemaValidation()

// Pipeline compatibility validation
[Test]
public async Task PipelineValidator_ShouldValidateCompleteWorkflow_WithRealPlugins()
```

### **3rd Party Plugin Test:**
```csharp
// Create a test plugin in separate assembly to validate no Core dependencies
public class TestExternalPlugin : PluginBase, ITransformPlugin
{
    // Validate external plugin can be created without Core references
}

[PluginConfigurationProvider]
public class TestExternalPluginProvider : IPluginConfigurationProvider
{
    // Validate external configuration provider works with schema validation
}
```

### **Schema Validation Tests:**
```csharp
// JSON schema validation tests
[Test]
public void JsonSchemaValidator_ShouldValidateConfiguration_AgainstPluginSchema()

// Schema requirement tests
[Test]
public void SchemaRequirement_ShouldValidateCompatibility_BetweenPlugins()

// Pipeline validation integration tests
[Test]
public async Task PipelineValidation_ShouldCatchConfigurationErrors_BeforeExecution()
```

---

## 📊 Risk Assessment & Mitigation

### **High Risks:**
1. **Breaking Existing Plugins**
   - *Mitigation*: Comprehensive backward compatibility testing with existing YAML configs
   - *Fallback*: Keep old configuration mapper as backup during transition

2. **Schema Validation Performance Impact**
   - *Mitigation*: Benchmark validation performance, cache compiled schemas
   - *Target*: <5ms per configuration validation
   - *Monitoring*: Add performance metrics to validation pipeline

3. **Complex Migration Path**
   - *Mitigation*: Phase-by-phase approach with validation at each step
   - *Fallback*: Revert to previous architecture if needed
   - *Testing*: Extensive integration tests for each migration phase

### **Medium Risks:**
1. **JSON Schema Complexity**
   - *Mitigation*: Start with simple schemas, iterate based on feedback
   - *Validation*: Test schemas with real-world configurations
   - *Documentation*: Provide clear schema authoring guidelines

2. **Pipeline Validation Overhead**
   - *Mitigation*: Cache validation results, validate only on configuration changes
   - *Optimization*: Parallel validation for independent plugins
   - *Monitoring*: Track validation time in telemetry

3. **Provider Discovery Performance**
   - *Mitigation*: Cache providers after discovery, lazy loading
   - *Optimization*: Assembly scanning only at startup
   - *Monitoring*: Track discovery time in startup metrics

---

## 📈 Success Metrics

### **Technical Metrics:**
- **✅ Plugin Creation Time**: External plugin from scratch to working <2 hours
- **✅ Core Dependencies**: Zero Core references in plugin projects
- **✅ Configuration Performance**: <10ms per configuration creation
- **✅ Schema Validation Performance**: <5ms per configuration validation
- **✅ Pipeline Validation Performance**: <50ms for complete pipeline validation
- **✅ Discovery Performance**: <100ms for full provider discovery

### **Quality Metrics:**
- **✅ Test Coverage**: 90%+ coverage for new provider and validation systems
- **✅ Backward Compatibility**: 100% existing functionality preserved
- **✅ Schema Coverage**: All plugins have complete JSON schemas
- **✅ Documentation**: Complete plugin development guide with schema examples
- **✅ Code Quality**: Zero plugin-specific code remaining in Core

### **Developer Experience Metrics:**
- **✅ Plugin Template**: Working external plugin template project with schema
- **✅ Clear Validation Errors**: Meaningful error messages for configuration issues
- **✅ Schema Documentation**: Auto-generated documentation from JSON schemas
- **✅ Pipeline Validation**: Early error detection before pipeline execution

### **Validation Quality Metrics:**
- **✅ Configuration Error Detection**: 95%+ of configuration errors caught before runtime
- **✅ Pipeline Compatibility**: 100% plugin chain compatibility issues detected
- **✅ Schema Accuracy**: JSON schemas match actual plugin requirements
- **✅ Error Message Quality**: Error messages provide actionable feedback

---

## 🔄 Dependencies & Prerequisites

### **Required Before Starting:**
- [ ] Complete current Cycle 1 Performance Sprint documentation
- [ ] Backup current working plugin configurations for testing
- [ ] Set up dedicated test environment for architectural changes
- [ ] Create test data sets for validation scenarios

### **Sprint Dependencies:**
- **Week 1 → Week 2**: Provider interface and schema system must be complete before plugin migration
- **Week 2 → Week 3**: Plugin migration must be working before pipeline validation implementation
- **Parallel Work**: NuGet standardization can run parallel to provider work
- **Testing**: Schema validation tests run continuously throughout sprint

---

## 📋 Detailed Implementation Checklist

### **Week 1: Self-Registration Architecture with Schema Support**
- [ ] Create `IPluginConfigurationProvider` interface with schema methods in Abstractions
- [ ] Create `ISchemaRequirement` interface and implementations in Abstractions  
- [ ] Create `PluginMetadata` class and `PluginCategory` enum
- [ ] Implement `PluginConfigurationProviderRegistry` in Core
- [ ] Add JSON schema validation infrastructure using NJsonSchema
- [ ] Create provider discovery mechanism with assembly scanning and attributes
- [ ] Add configuration provider attribute for auto-discovery
- [ ] Update `PluginConfigurationMapper` to use providers with validation
- [ ] Write unit tests for provider registry, discovery, and schema validation
- [ ] Test provider registration and configuration creation with validation
- [ ] Create schema requirement implementations (Flexible and Specific)
- [ ] Benchmark schema validation performance

### **Week 2: Plugin Migration with Comprehensive Schema Implementation**
- [ ] Create `JavaScriptTransformConfigurationProvider` with complete JSON schema
- [ ] Implement input/output schema requirements for JavaScript Transform
- [ ] Add plugin metadata and validation logic to JavaScript Transform provider
- [ ] Create `DelimitedSourceConfigurationProvider` with CSV format schema
- [ ] Implement file format validation and schema requirements for Delimited Source
- [ ] Create `DelimitedSinkConfigurationProvider` with output format schema  
- [ ] Implement output format validation for Delimited Sink
- [ ] Move all configuration logic from Core to respective plugin providers
- [ ] Test each plugin with new provider architecture and schema validation
- [ ] Validate backward compatibility with existing YAML configs using schema validation
- [ ] Remove plugin-specific configurations from Core (JavaScriptTransform, DelimitedSource, DelimitedSink)
- [ ] Update service registration to use provider discovery with schema support
- [ ] Create plugin metadata for all migrated plugins
- [ ] Test plugin chain compatibility validation

### **Week 3: Pipeline Validation & Foundation Cleanup**
- [ ] Create `Directory.Packages.props` with standardized package versions
- [ ] Update all project files to use central package management
- [ ] Remove all `.bak` files and dead code from repository
- [ ] Clean up duplicate interfaces (`IMemoryManager` duplication)
- [ ] Remove abandoned projects and unused files
- [ ] Implement `PipelineValidator` class with comprehensive validation logic
- [ ] Add plugin chain compatibility validation using schema requirements
- [ ] Implement pipeline structure validation (source → transforms → sink)
- [ ] Create validation error aggregation and reporting
- [ ] Add pipeline validation to configuration pipeline
- [ ] Update Plugin Developer Guide with self-registration pattern and schema examples
- [ ] Create external plugin template project with schema validation example
- [ ] Write comprehensive plugin development tutorial with schema best practices
- [ ] Update architecture documentation to reflect new plugin model and validation flow
- [ ] Create schema validation best practices and troubleshooting guide
- [ ] Run full integration test suite with schema validation
- [ ] Performance validation and benchmarking for all validation components
- [ ] Create plugin compatibility matrix documentation

---

## 🎯 Post-Sprint Deliverables

### **Code Deliverables:**
1. **Self-Registration Architecture**: Complete provider pattern implementation with schema validation
2. **Migrated Plugins**: All existing plugins using self-registration with comprehensive schemas
3. **Pipeline Validation System**: Complete validation framework for configurations and compatibility
4. **Clean Foundation**: Standardized packages, no dead code, organized project structure
5. **External Plugin Template**: Working template for 3rd party development with schema examples

### **Schema Deliverables:**
1. **JSON Schemas**: Complete configuration schemas for all plugins
2. **Schema Requirements**: Input/output requirements for all plugins
3. **Validation Framework**: Comprehensive validation system for configurations and pipelines
4. **Plugin Metadata**: Complete metadata system for plugin discovery and documentation

### **Documentation Deliverables:**
1. **Updated Plugin Developer Guide**: Complete self-registration and schema documentation
2. **Plugin Creation Tutorial**: Step-by-step external plugin development with schema validation
3. **Schema Authoring Guide**: Best practices for creating JSON schemas and requirements
4. **Architecture Documentation**: Updated to reflect new plugin model and validation architecture
5. **Migration Guide**: How to convert old plugins to new architecture with schema support
6. **Validation Troubleshooting**: Common validation errors and solutions

### **Testing Deliverables:**
1. **Comprehensive Test Suite**: Unit and integration tests for provider and validation systems
2. **External Plugin Test**: Validated 3rd party plugin development with schema validation
3. **Performance Benchmarks**: Configuration creation and validation performance validation
4. **Backward Compatibility Validation**: All existing functionality preserved with schema validation
5. **Pipeline Validation Tests**: End-to-end pipeline compatibility testing

---

## 🚀 Next Sprint Preparation

### **Enabled by This Sprint:**
- **Performance Optimization**: Clean architecture with validation accelerates performance work
- **Usability Improvements**: Plugin development becomes accessible with clear schemas and validation
- **Stability Enhancements**: Reduced coupling and early validation improve system stability
- **Feature Development**: New features can be built as external plugins with schema support
- **UI Development**: JSON schemas enable automatic form generation and validation
- **Documentation Generation**: Plugin metadata enables automatic documentation generation

### **Recommended Next Sprint:**
**Cycle 3: "Performance & Usability Enhancement"** (Post-Foundation)
- Complete Jint optimization to achieve 2x performance target with clean architecture
- V8 scaling validation using new plugin architecture
- Plugin development tooling and templates with schema support
- Configuration UI prototyping using JSON schemas for form generation
- Performance monitoring enhancements with plugin-specific metrics
- Error message and debugging experience improvements

---

## 📊 Sprint Success Definition

**This sprint is successful when:**
1. A developer can create a new plugin in a separate project/assembly with comprehensive schema validation
2. The plugin requires zero Core project references
3. The plugin automatically registers its configuration provider with schema support
4. All existing plugins continue working without modification using schema validation
5. Plugin development documentation is complete and validated with schema examples
6. Pipeline validation catches configuration and compatibility errors before execution
7. JSON schemas provide clear configuration structure and validation
8. Plugin metadata enables automatic documentation and discovery

**Sprint failure criteria:**
1. Existing plugins break during migration
2. Performance degrades significantly (>10ms config creation, >5ms validation)
3. 3rd party plugin development still requires Core modifications
4. Schema validation introduces more complexity than value
5. Pipeline validation has false positives or misses real issues
6. Architecture becomes more complex instead of simpler

---

**Document Version**: 1.0  
**Created**: July 14, 2025  
**Sprint Lead**: Architecture Team  
**Review Date**: Weekly sprint reviews every Friday  
**Success Measurement**: Technical metrics + external plugin validation + schema validation effectiveness

---

## 🎯 Summary of Key Enhancements

This enhanced sprint plan incorporates **comprehensive schema validation** throughout the plugin architecture:

1. **Configuration Schemas**: JSON schemas for all plugin configurations
2. **Input/Output Requirements**: Schema compatibility validation between plugins  
3. **Pipeline Validation**: End-to-end pipeline compatibility checking
4. **Plugin Metadata**: Rich metadata for discovery and documentation
5. **Early Error Detection**: Validation happens at configuration time, not runtime
6. **Developer Experience**: Clear schemas enable better tooling and documentation
7. **Future UI Support**: JSON schemas enable automatic form generation

The result is a **self-documenting, self-validating plugin architecture** that provides excellent developer experience while maintaining production-grade reliability.