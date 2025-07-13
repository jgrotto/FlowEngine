# JavaScript Context Identification Strategy

**Document Type**: Technical Architecture Recommendation  
**Date**: July 12, 2025  
**Status**: Ready for Architecture Review  
**Scope**: JavaScript AST Pre-compilation & Global Context Optimization  
**Audience**: Architecture Team

---

## Executive Summary

Both JavaScript AST pre-compilation and global context optimization require **robust unique identification strategies** for caching and recall purposes. Current FlowEngine identification approaches are insufficient for advanced caching scenarios where multiple JavaScript transforms may exist in the same pipeline.

**Key Decision Required:**
Choose between **Runtime GUID assignment** vs **Path-based naming** vs **Hybrid approach** for identifying JavaScript contexts in cache systems.

**Recommendation Preview:**
**Hybrid Strategy** combining human-readable path-based names with guaranteed uniqueness through configuration validation and automatic disambiguation.

---

## Current State Analysis

### Existing Identification Approaches

FlowEngine currently uses **three different identification strategies** across different components:

#### **1. Script Content Hashing** (JintScriptEngineService.cs)
```csharp
private static string ComputeScriptHash(string script)
{
    using var sha256 = SHA256.Create();
    var hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(script));
    return Convert.ToHexString(hash)[..16]; // First 16 characters
}
```

**Usage**: Script compilation caching  
**Pros**: Content-based, automatic deduplication  
**Cons**: Not human-readable, doesn't account for configuration differences  

#### **2. Static Plugin Type IDs** (Configuration Classes)
```csharp
public string PluginId { get; init; } = "JavaScript Transform";  // JavaScriptTransformConfiguration
public string PluginId { get; init; } = "delimited-source";     // DelimitedSourceConfiguration  
public string PluginId { get; init; } = "delimited-sink";       // DelimitedSinkConfiguration
```

**Usage**: Plugin type identification  
**Pros**: Human-readable, consistent  
**Cons**: Not unique per instance, conflicts with multiple instances  

#### **3. Assembly-Based IDs** (PluginLoader.cs)
```csharp
private static string GeneratePluginId(string assemblyPath, string typeName)
{
    return $"{Path.GetFileName(assemblyPath)}::{typeName}";
}
```

**Usage**: Plugin loading and isolation  
**Pros**: Unique per assembly/type combination  
**Cons**: Not suitable for runtime instance differentiation  

### Current Limitations

**❌ No Pipeline Step Identity**: Multiple JavaScript transforms in same pipeline use identical IDs  
**❌ No Instance Differentiation**: Static IDs prevent multiple instances of same plugin type  
**❌ No Configuration Context**: Same script with different global variables considered identical  
**❌ No Human-Readable Caching**: SHA256 hashes provide no operational visibility  

---

## Identification Requirements

### For AST Pre-compilation Optimization

**AST Cache Requirements:**
```csharp
// Cache Key Requirements for AST storage
public interface IAstCacheKey
{
    string ScriptContent { get; }      // JavaScript source code
    string StepIdentifier { get; }     // Unique step ID in pipeline
    string ConfigurationHash { get; }  // Global variables, engine options
}
```

**Cache Scenarios:**
- **Same script, different steps**: `CustomerTransform.script` vs `OrderTransform.script`
- **Same script, different config**: Different global variables or engine options
- **Pipeline reloads**: Same logical step should reuse cached AST
- **Multi-pipeline**: Different pipelines with same step names should not conflict

### For Global Context Optimization  

**Global Context Cache Requirements:**
```csharp
// Cache Key Requirements for Global Context storage
public interface IGlobalContextCacheKey
{
    string StepIdentifier { get; }        // Unique step ID in pipeline
    string GlobalVariablesHash { get; }   // Hash of global variable configuration
    string SchemaHash { get; }            // Input/output schema fingerprint
    string PipelineVersion { get; }       // Pipeline version for invalidation
}
```

**Context Scenarios:**
- **Global variable sharing**: Steps with identical globals can share context
- **Schema compatibility**: Steps with compatible schemas can share schema objects
- **Pipeline isolation**: Different pipeline instances should not share contexts
- **Configuration changes**: Global variable changes should invalidate cached contexts

### Cross-Cutting Requirements

**Operational Requirements:**
- ✅ **Human-readable**: Operations teams can understand cache contents
- ✅ **Debuggable**: Clear mapping between YAML config and cache entries
- ✅ **Performant**: ID generation must be fast (sub-millisecond)
- ✅ **Collision-resistant**: Guaranteed uniqueness across all scenarios

**Development Requirements:**
- ✅ **Configuration-driven**: IDs defined in YAML pipeline configuration
- ✅ **Validation-enforced**: Duplicate detection during pipeline validation
- ✅ **Migration-friendly**: Existing pipelines continue working
- ✅ **Tooling-friendly**: CLI tools can inspect and manage cached contexts

---

## Strategy Options Comparison

### Option 1: Runtime GUID Assignment

**Implementation:**
```yaml
# YAML Configuration (User-Defined)
transform:
  type: JavaScriptTransform
  name: CustomerTransform  # Human-readable name
  config:
    script: |
      function process(context) { /* ... */ }

# Runtime Assignment (Auto-Generated)  
# StepId: "CustomerTransform-a7f3e8d2-4b91-4c5e-8f7a-1e2d3c4b5a6f"
```

**Cache Key Generation:**
```csharp
public string GenerateStepId(string userDefinedName)
{
    return $"{userDefinedName}-{Guid.NewGuid()}";
}
```

#### **Pros:**
- ✅ **Guaranteed uniqueness**: No collision risk across any scenario
- ✅ **Zero configuration overhead**: No duplicate name validation required
- ✅ **Simple implementation**: Straightforward GUID generation
- ✅ **Migration compatibility**: Existing configs work without changes

#### **Cons:**
- ❌ **Not human-readable**: `a7f3e8d2-4b91-4c5e-8f7a-1e2d3c4b5a6f` meaningless to operators
- ❌ **Cache invalidation complexity**: New GUID every restart invalidates caches
- ❌ **Debugging difficulty**: No clear mapping between config and cached objects
- ❌ **Operational overhead**: Cannot predict or reference cache entries

**Architecture Impact:**
- **Cache Persistence**: Cannot survive pipeline restarts without additional mapping
- **Monitoring**: Cache metrics become opaque (GUID-based)
- **Troubleshooting**: Difficult to correlate cache entries with pipeline steps

---

### Option 2: Path-Based Naming

**Implementation:**
```yaml
# YAML Configuration with Hierarchical Naming
pipeline:
  name: CustomerProcessing
  
  source:
    name: CustomerSource    # Step ID: "CustomerProcessing.CustomerSource"
    type: DelimitedSource
    
  transforms:
    - name: CustomerTransform   # Step ID: "CustomerProcessing.CustomerTransform"
      type: JavaScriptTransform
      config:
        script: |
          function process(context) { /* ... */ }
          
    - name: OrderTransform      # Step ID: "CustomerProcessing.OrderTransform"  
      type: JavaScriptTransform
      config:
        script: |
          function process(context) { /* ... */ }
          
  sink:
    name: CustomerSink      # Step ID: "CustomerProcessing.CustomerSink"
    type: DelimitedSink
```

**Cache Key Generation:**
```csharp
public string GenerateStepId(string pipelineName, string stepName)
{
    return $"{pipelineName}.{stepName}";
}
```

#### **Pros:**
- ✅ **Human-readable**: `CustomerProcessing.CustomerTransform` clearly identifies purpose
- ✅ **Predictable**: Operations teams can anticipate cache entry names
- ✅ **Debuggable**: Direct mapping between YAML config and cache entries  
- ✅ **Tooling-friendly**: CLI tools can reference steps by meaningful names
- ✅ **Cache persistence**: Same logical step reuses cache across restarts

#### **Cons:**
- ❌ **Duplicate name risk**: Multiple steps with same name cause conflicts
- ❌ **Validation complexity**: Must enforce uniqueness during pipeline validation
- ❌ **Cross-pipeline conflicts**: Different pipelines with same step names collide
- ❌ **Migration risk**: Existing configs might have naming conflicts

**Architecture Impact:**
- **Validation Required**: Pipeline validation must check for duplicate step names
- **Global Namespace**: Need strategy for cross-pipeline step name conflicts  
- **Configuration Constraints**: YAML schema must enforce naming rules

---

### Option 3: Hybrid Strategy (Recommended)

**Implementation:**
```yaml
# YAML Configuration with Smart Disambiguation
pipeline:
  name: CustomerProcessing
  version: "1.2.0"        # For cache invalidation
  
  transforms:
    - name: CustomerTransform     # User-defined name
      type: JavaScriptTransform
      config:
        script: |
          function process(context) { /* ... */ }
          
    - name: CustomerTransform     # Duplicate name - auto-disambiguated
      type: JavaScriptTransform  
      config:
        script: |
          function process(context) { /* different script */ }
```

**Cache Key Generation with Auto-Disambiguation:**
```csharp
public class HybridStepIdGenerator
{
    public string GenerateStepId(string pipelineName, string pipelineVersion, string stepName, int sequenceNumber)
    {
        var baseId = $"{pipelineName}.{stepName}";
        
        // Add sequence number only if duplicates exist
        if (sequenceNumber > 1)
        {
            baseId += $".{sequenceNumber}";
        }
        
        // Add pipeline version hash for cache invalidation
        var versionHash = ComputeVersionHash(pipelineVersion);
        return $"{baseId}@{versionHash}";
    }
    
    private string ComputeVersionHash(string version)
    {
        using var sha256 = SHA256.Create();
        var hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(version));
        return Convert.ToHexString(hash)[..8]; // 8 characters for readability
    }
}
```

**Generated Step IDs:**
```
CustomerProcessing.CustomerTransform@a1b2c3d4           # First instance
CustomerProcessing.CustomerTransform.2@a1b2c3d4        # Second instance (auto-disambiguated)
CustomerProcessing.OrderTransform@a1b2c3d4             # Different step name
```

#### **Pros:**
- ✅ **Human-readable**: Clear base names with minimal disambiguation
- ✅ **Guaranteed uniqueness**: Automatic sequence numbers prevent collisions
- ✅ **Cache persistence**: Same logical step reuses cache across restarts
- ✅ **Version awareness**: Pipeline version changes invalidate stale caches
- ✅ **Migration-friendly**: Existing configs work with automatic disambiguation
- ✅ **Operational clarity**: Easy to understand and troubleshoot

#### **Cons:**
- ⚠️ **Implementation complexity**: Requires duplicate detection and disambiguation logic
- ⚠️ **Sequence number dependency**: Step order affects cache keys
- ⚠️ **Version management**: Requires pipeline version discipline

**Architecture Impact:**
- **Enhanced Validation**: Pipeline validation detects and auto-resolves duplicates
- **Version Management**: Pipeline configuration should include version for cache invalidation
- **Tooling Enhancement**: CLI tools can display step IDs with disambiguation context

---

## Detailed Hybrid Strategy Design

### Implementation Architecture

#### **1. Pipeline Configuration Enhancement**
```yaml
# Enhanced YAML Schema with Version and Naming
pipeline:
  name: CustomerProcessing
  version: "1.2.0"                    # Required for cache invalidation
  description: "Customer data processing pipeline"
  
  plugins:
    - step_name: CustomerSource       # Explicit step naming
      plugin_type: DelimitedSource
      config:
        path: "customers.csv"
        
    - step_name: CustomerTransform    # JavaScript transform step
      plugin_type: JavaScriptTransform
      config:
        globals:                      # Global variables affect cache key
          COMPANY_NAME: "Acme Corp"
          TAX_RATE: 0.08
        script: |
          function process(context) {
            // Transform logic using globals
            var companyName = COMPANY_NAME;
            var customer = context.input.Current();
            context.output.SetField("company", companyName);
            return true;
          }
          
    - step_name: CustomerTransform    # Duplicate name - will be auto-disambiguated
      plugin_type: JavaScriptTransform
      config:
        globals:
          COMPANY_NAME: "Beta Corp"   # Different globals = different cache key
        script: |
          function process(context) {
            // Different transform logic
            var customer = context.input.Current();
            context.output.SetField("processed_by", "Beta");
            return true;
          }
          
    - step_name: CustomerSink
      plugin_type: DelimitedSink
      config:
        path: "processed-customers.csv"
```

#### **2. Step ID Generation Service**
```csharp
public interface IStepIdGenerationService
{
    /// <summary>
    /// Generates unique step IDs for pipeline configuration
    /// </summary>
    StepIdGenerationResult GenerateStepIds(IPipelineConfiguration pipeline);
}

public class StepIdGenerationResult
{
    public bool Success { get; init; }
    public IReadOnlyList<StepIdMapping> StepMappings { get; init; } = Array.Empty<StepIdMapping>();
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
}

public record StepIdMapping(
    string UserDefinedName,
    string GeneratedStepId,
    int SequenceNumber,
    string ConfigurationHash);
```

#### **3. Cache Key Generation**
```csharp
public class JavaScriptCacheKeyGenerator
{
    public string GenerateAstCacheKey(StepIdMapping stepMapping, string scriptContent, ScriptOptions options)
    {
        var components = new[]
        {
            stepMapping.GeneratedStepId,
            "ast",
            ComputeScriptHash(scriptContent),
            ComputeOptionsHash(options)
        };
        
        return string.Join(":", components);
        // Example: "CustomerProcessing.CustomerTransform@a1b2c3d4:ast:f7e8d9a2:opt1b2c3"
    }
    
    public string GenerateGlobalContextCacheKey(StepIdMapping stepMapping, GlobalContextConfiguration globalConfig, ISchema inputSchema, ISchema outputSchema)
    {
        var components = new[]
        {
            stepMapping.GeneratedStepId,
            "globals",
            ComputeGlobalsHash(globalConfig),
            ComputeSchemaHash(inputSchema, outputSchema)
        };
        
        return string.Join(":", components);
        // Example: "CustomerProcessing.CustomerTransform@a1b2c3d4:globals:g3f4e5d6:sch7a8b9"
    }
}
```

### Validation and Disambiguation Logic

#### **Pipeline Validation with Auto-Disambiguation**
```csharp
public class EnhancedPipelineValidator
{
    public ValidationResult ValidateAndDisambiguate(IPipelineConfiguration pipeline)
    {
        var stepNames = new Dictionary<string, int>();
        var stepMappings = new List<StepIdMapping>();
        var warnings = new List<string>();
        
        foreach (var pluginConfig in pipeline.Plugins)
        {
            var stepName = pluginConfig.StepName ?? pluginConfig.PluginType;
            
            // Track duplicate names and assign sequence numbers
            if (!stepNames.ContainsKey(stepName))
            {
                stepNames[stepName] = 1;
            }
            else
            {
                stepNames[stepName]++;
                warnings.Add($"Duplicate step name '{stepName}' auto-disambiguated as '{stepName}.{stepNames[stepName]}'");
            }
            
            var sequenceNumber = stepNames[stepName];
            var generatedStepId = _stepIdGenerator.GenerateStepId(
                pipeline.Name, 
                pipeline.Version, 
                stepName, 
                sequenceNumber);
            
            var configHash = ComputeConfigurationHash(pluginConfig);
            
            stepMappings.Add(new StepIdMapping(
                stepName, 
                generatedStepId, 
                sequenceNumber, 
                configHash));
        }
        
        return new ValidationResult
        {
            Success = true,
            StepMappings = stepMappings,
            Warnings = warnings
        };
    }
}
```

### Configuration Migration Strategy

#### **Backward Compatibility Approach**
```csharp
public class ConfigurationMigrationService
{
    public IPipelineConfiguration MigrateToV2(IPipelineConfiguration legacyConfig)
    {
        // Handle legacy configurations without explicit step names
        var migratedConfig = legacyConfig.DeepClone();
        
        if (string.IsNullOrEmpty(migratedConfig.Version))
        {
            migratedConfig.Version = "1.0.0";  // Default version for legacy configs
        }
        
        for (int i = 0; i < migratedConfig.Plugins.Count; i++)
        {
            var plugin = migratedConfig.Plugins[i];
            if (string.IsNullOrEmpty(plugin.StepName))
            {
                // Generate step names based on plugin type and sequence
                plugin.StepName = $"{plugin.PluginType}Step{i + 1}";
            }
        }
        
        return migratedConfig;
    }
}
```

---

## Implementation Guidance

### Pipeline Configuration Standards

#### **Required YAML Schema Changes**
```yaml
# New Required Fields for V2 Pipeline Configuration
pipeline:
  name: string              # Required - Pipeline identifier
  version: string           # Required - Semantic version for cache invalidation
  description: string       # Optional - Human-readable description
  
  plugins:
    - step_name: string     # Required - Unique step identifier within pipeline
      plugin_type: string   # Required - Plugin type (existing)
      config: object        # Required - Plugin configuration (existing)
```

#### **Step Naming Conventions**
```yaml
# Recommended Naming Patterns

# Source Steps
- step_name: CustomerSource
- step_name: OrderSource  
- step_name: ProductCatalogSource

# Transform Steps  
- step_name: CustomerValidation
- step_name: CustomerEnrichment
- step_name: OrderProcessing
- step_name: TaxCalculation

# Sink Steps
- step_name: CustomerSink
- step_name: OrderSink
- step_name: ErrorSink
```

### Validation Rules

#### **Step Name Validation**
```csharp
public static class StepNameValidator
{
    private static readonly Regex ValidStepNamePattern = new Regex(
        @"^[A-Za-z][A-Za-z0-9_]{2,49}$", 
        RegexOptions.Compiled);
    
    public static ValidationResult ValidateStepName(string stepName)
    {
        if (string.IsNullOrWhiteSpace(stepName))
            return ValidationResult.Error("Step name cannot be empty");
            
        if (stepName.Length < 3 || stepName.Length > 50)
            return ValidationResult.Error("Step name must be 3-50 characters");
            
        if (!ValidStepNamePattern.IsMatch(stepName))
            return ValidationResult.Error("Step name must start with letter, contain only letters, numbers, and underscores");
            
        return ValidationResult.Success();
    }
}
```

#### **Pipeline Version Validation**
```csharp
public static class PipelineVersionValidator
{
    public static ValidationResult ValidateVersion(string version)
    {
        if (string.IsNullOrWhiteSpace(version))
            return ValidationResult.Error("Pipeline version is required");
            
        if (!SemanticVersion.TryParse(version, out var semVer))
            return ValidationResult.Error("Pipeline version must be valid semantic version (e.g., '1.0.0')");
            
        return ValidationResult.Success();
    }
}
```

### Cache Management

#### **Cache Key Structure**
```
Format: {StepId}:{CacheType}:{ContentHash}:{ConfigHash}

Examples:
CustomerProcessing.CustomerTransform@a1b2c3d4:ast:f7e8d9a2:opt1b2c3
CustomerProcessing.CustomerTransform@a1b2c3d4:globals:g3f4e5d6:sch7a8b9
CustomerProcessing.OrderTransform@a1b2c3d4:ast:a3b4c5d6:opt1b2c3

Components:
- StepId: Generated step identifier with version hash
- CacheType: "ast" for AST cache, "globals" for global context cache  
- ContentHash: Hash of JavaScript content or global variable content
- ConfigHash: Hash of engine options or schema configuration
```

#### **Cache Invalidation Strategy**
```csharp
public class CacheInvalidationService
{
    public async Task InvalidatePipelineCache(string pipelineName, string version)
    {
        // Invalidate all cache entries for specific pipeline version
        var pattern = $"{pipelineName}.*@{ComputeVersionHash(version)}:*";
        await _cacheService.InvalidateByPattern(pattern);
    }
    
    public async Task InvalidateStepCache(string stepId)
    {
        // Invalidate all cache entries for specific step
        var pattern = $"{stepId}:*";
        await _cacheService.InvalidateByPattern(pattern);
    }
}
```

---

## Risk Assessment

### Technical Risks

**LOW RISK: Implementation Complexity**
- **Risk**: Hybrid strategy more complex than GUID-only approach
- **Mitigation**: Incremental implementation, comprehensive testing
- **Validation**: Unit tests for all disambiguation scenarios

**LOW RISK: Performance Impact**  
- **Risk**: Step ID generation adds processing overhead
- **Mitigation**: Cache generated IDs, optimize hash algorithms
- **Validation**: Performance benchmarks for ID generation

**MEDIUM RISK: Migration Compatibility**
- **Risk**: Existing pipelines may not follow new naming requirements
- **Mitigation**: Automatic migration with backward compatibility
- **Validation**: Migration testing with legacy configurations

### Operational Risks

**LOW RISK: Configuration Complexity**
- **Risk**: Users must provide step names and versions
- **Mitigation**: Clear documentation, validation with helpful error messages
- **Validation**: User experience testing with sample configurations

**VERY LOW RISK: Cache Key Collisions**
- **Risk**: Different configurations generate identical cache keys
- **Mitigation**: Comprehensive hash algorithms, collision detection
- **Validation**: Collision testing with diverse configuration scenarios

---

## Recommended Implementation Plan

### Phase 1: Core Infrastructure (Sprint 1 Foundation)
1. **Step ID Generation Service** - Implement hybrid ID generation with disambiguation
2. **Enhanced Pipeline Validation** - Add step name and version validation
3. **Cache Key Generation** - Implement structured cache key generation
4. **Configuration Migration** - Support for legacy configurations

### Phase 2: AST Integration (Sprint 1)
1. **AST Cache Integration** - Use hybrid step IDs for AST cache keys
2. **Performance Validation** - Ensure ID generation doesn't impact performance
3. **Testing** - Comprehensive testing with multiple JavaScript steps

### Phase 3: Global Context Integration (Sprint 2)  
1. **Global Context Cache Integration** - Use hybrid step IDs for global context keys
2. **Cross-Context Optimization** - Share global contexts with identical configurations
3. **Monitoring** - Cache hit/miss metrics with meaningful step identifiers

### Success Criteria
- [ ] **Human-readable cache keys** for operational visibility
- [ ] **Zero cache key collisions** across all test scenarios
- [ ] **Automatic disambiguation** of duplicate step names  
- [ ] **Performance impact <1ms** for step ID generation
- [ ] **100% backward compatibility** with existing pipeline configurations

---

## Architecture Team Decisions Required

### **Decision 1: Identification Strategy**
**Question**: Approve hybrid strategy with auto-disambiguation vs simpler GUID-only approach?  
**Recommendation**: **Hybrid strategy** for operational clarity and debugging capability  
**Trade-off**: Implementation complexity vs operational visibility

### **Decision 2: Configuration Schema Changes**
**Question**: Require `step_name` and `version` fields in pipeline configuration?  
**Recommendation**: **Yes, with automatic migration** for backward compatibility  
**Trade-off**: Configuration complexity vs cache management capability

### **Decision 3: Duplicate Name Handling**
**Question**: Auto-disambiguate duplicate step names vs reject pipeline with error?  
**Recommendation**: **Auto-disambiguate with warnings** for user-friendly experience  
**Trade-off**: Automatic resolution vs explicit user control

### **Decision 4: Cache Key Structure**
**Question**: Approve structured cache key format with multiple hash components?  
**Recommendation**: **Approve structured format** for granular cache management  
**Trade-off**: Cache key complexity vs cache invalidation precision

---

**Document Status**: ✅ **READY FOR ARCHITECTURE REVIEW**  
**Next Action**: Architecture team review and decision on recommended hybrid strategy  
**Implementation Impact**: Foundation for both AST pre-compilation and global context optimization  
**Timeline**: Architecture decision required before Sprint 1 implementation begins