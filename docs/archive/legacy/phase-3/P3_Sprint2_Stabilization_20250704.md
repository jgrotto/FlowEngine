# FlowEngine Phase 3 Stabilization Sprint

**Sprint Duration**: 3-4 days  
**Priority**: High - Complete before plugin development  
**Goal**: Ensure production-ready foundation before implementing DelimitedSource, DelimitedSink, and JavascriptTransform plugins

## Important Clarification: Types of Validation in FlowEngine

**1. YAML Job Structure Validation** (Core's responsibility): Validates overall pipeline YAML structure

**2. Plugin Data Schema Validation** (Plugin's responsibility): Validates the data columns/types the plugin will process

**3. Plugin Configuration Validation** (Plugin's responsibility): Validates plugin-specific parameters like file paths, delimiters, etc.

This sprint focuses on implementing #2 and #3 using JSON Schema for clean, maintainable validation.

## Task 1: Complete Template Plugin Integration (Day 1)

### 1.1 Fix Template Plugin Data Processing
**Current Issue**: Template plugin has service injection but still uses placeholder data generation

**Tasks**:
- Update `TemplatePluginService` to use injected factories to create real data
- Implement actual data processing logic using ArrayRow optimization
- Add performance monitoring using injected `IPerformanceMonitor`
- Validate memory management with `IMemoryManager`

**Success Criteria**:
- Template plugin processes 100K+ rows of generated data
- Performance metrics logged correctly
- Memory usage stays bounded

### 1.2 Add Plugin Data Schema Validation
**Context**: This validates the plugin's data processing schema, NOT the overall YAML job configuration

**Tasks**:
- Implement data schema validation in `TemplatePluginValidator.ValidateSchema()` method
- Validate column types are supported (int32, string, decimal, datetime, bool)
- Verify field indexes are sequential (0, 1, 2, ...) for ArrayRow optimization
- Check required vs optional fields match plugin capabilities
- Add meaningful error messages for schema mismatches

**Example Validation Scenarios**:
```yaml
# Valid schema for TemplatePlugin
outputSchema:
  columns:
    - name: "id"
      type: "int32"      # ✅ Supported type
      index: 0           # ✅ Sequential index
      required: true
    - name: "name"  
      type: "string"
      index: 1           # ✅ Sequential
      required: true

# Invalid schema examples:
    - name: "data"
      type: "binary"     # ❌ Unsupported type for this plugin
      index: 5           # ❌ Non-sequential index
```

**Success Criteria**:
- Plugin rejects unsupported column types with specific error: "TemplatePlugin does not support 'binary' type"
- Non-sequential indexes detected with error: "Field indexes must be sequential starting from 0"
- Schema validation completes in <10ms for typical schemas

### 1.3 Implement JSON Schema-Based Configuration Validation
**Context**: Replace hard-coded validation logic with declarative JSON Schema validation

**Tasks**:
- Create base class `SchemaValidatedPluginValidator` for JSON Schema validation
- Add JSON Schema as embedded resource for TemplatePlugin
- Implement schema-based validation with semantic validation separation
- Add NuGet package reference to `Newtonsoft.Json.Schema` or `NJsonSchema`

**Implementation Pattern**:
```csharp
// TemplatePlugin.Schemas/TemplatePluginConfiguration.json
{
  "$schema": "http://json-schema.org/draft-07/schema#",
  "type": "object",
  "required": ["rowCount", "batchSize"],
  "properties": {
    "rowCount": {
      "type": "integer",
      "minimum": 1,
      "maximum": 1000000,
      "description": "Number of rows to generate"
    },
    "batchSize": {
      "type": "integer", 
      "minimum": 1,
      "maximum": 10000,
      "description": "Rows per batch"
    },
    "dataType": {
      "type": "string",
      "enum": ["TestData", "Random", "Sequential"],
      "default": "TestData"
    }
  }
}
```

**Benefits Achieved**:
- Validation logic becomes declarative and maintainable
- Schema can be reused for documentation generation
- IDE support for YAML editing with schema
- Clear separation between structural and semantic validation

**Success Criteria**:
- Template plugin uses JSON Schema for all configuration validation
- Validation errors match JSON Schema path format: "$.batchSize: Value 10001 exceeds maximum of 10000"
- Pattern established for DelimitedSource/Sink and JavascriptTransform plugins

## Task 2: Plugin Discovery & Loading Improvements (Day 1-2)

### 2.1 Implement Plugin Discovery
**Current Issue**: Plugins must be explicitly configured; no automatic discovery

**Tasks**:
- Add directory scanning for plugin assemblies
- Implement plugin manifest files (plugin.json)
- Create plugin metadata extraction
- Add version compatibility checking

**Implementation Approach**:
```
plugins/
├── DelimitedSource/
│   ├── plugin.json
│   └── DelimitedSource.dll
├── DelimitedSink/
│   ├── plugin.json
│   └── DelimitedSink.dll
└── JavascriptTransform/
    ├── plugin.json
    └── JavascriptTransform.dll
```

### 2.2 Improve Error Handling
**Tasks**:
- Add detailed error messages for common plugin loading failures
- Implement plugin load retry mechanism
- Add plugin health checks before pipeline execution
- Create diagnostic mode for troubleshooting

**Success Criteria**:
- Clear error messages guide developers to solutions
- Failed plugins don't crash the entire pipeline

## Task 3: Performance Baseline & Optimization (Day 2)

### 3.1 Establish Performance Baselines
**Tasks**:
- Create benchmark suite for Template Plugin
- Measure factory overhead vs direct instantiation
- Profile memory allocation patterns
- Document performance characteristics

**Metrics to Capture**:
- Rows/second throughput
- Memory allocation per row
- GC pressure under load
- Factory method overhead

### 3.2 Optimize Critical Paths
**Based on profiling results**:
- Cache frequently used schemas
- Pool ArrayRow instances if beneficial
- Optimize factory method implementations
- Reduce allocations in hot paths

**Success Criteria**:
- Achieve 200K+ rows/sec with Template Plugin
- Factory overhead < 5% vs direct instantiation
- Stable memory usage over 10-minute runs

## Task 4: CLI Enhancement for Plugin Development (Day 2-3)

### 4.1 Plugin Development Commands
**New Commands**:
```bash
# Create new plugin from template
dotnet run -- plugin create --name DelimitedSource --type Source

# Validate plugin implementation
dotnet run -- plugin validate --path ./plugins/DelimitedSource

# Test plugin with sample data
dotnet run -- plugin test --name DelimitedSource --data test.csv

# Benchmark plugin performance
dotnet run -- plugin benchmark --name DelimitedSource
```

### 4.2 Development Workflow Support
**Tasks**:
- Add plugin scaffolding from Template Plugin
- Implement live reload for plugin development
- Add debugging support with detailed logs
- Create plugin packaging command

**Success Criteria**:
- New plugin created and running in < 5 minutes
- Changes to plugin code reflected without restart
- Clear debugging output for troubleshooting

## Task 5: Documentation & Examples (Day 3)

### 5.1 Plugin Developer Guide
**Content**:
- Quick start guide using Template Plugin
- Factory service usage examples
- Performance best practices
- Common patterns and anti-patterns

### 5.2 Example Configurations
**Create working examples for**:
- Simple data generation (Template Plugin)
- CSV processing (preparation for DelimitedSource)
- Data transformation (preparation for JavascriptTransform)
- Performance testing scenarios

### 5.4 JSON Schema Validation Infrastructure
**Tasks**:
- Create reusable `SchemaValidatedPluginValidator` base class
- Document JSON Schema validation pattern for plugin developers
- Create example schemas for common validation scenarios
- Add tooling recommendations for schema development (VS Code extensions, etc.)

**Deliverables**:
- Base validator class in FlowEngine.Abstractions
- JSON Schema examples for DelimitedSource, DelimitedSink, JavascriptTransform
- Developer guide section: "Plugin Validation with JSON Schema"
- Schema versioning strategy documentation

## Task 6: Testing & Quality Assurance (Day 3-4)

### 6.1 Integration Tests
**Focus Areas**:
- Plugin loading with full service injection
- Configuration binding edge cases
- Error handling scenarios
- Performance under load

### 6.2 Cross-Platform Validation
**Platforms**:
- Windows native
- Linux (Ubuntu 20.04+)
- WSL2
- Docker containers

**Success Criteria**:
- All tests pass on all platforms
- Consistent performance across platforms
- No platform-specific bugs

## Definition of Done

### Functional Requirements ✅
- [ ] Template Plugin processes real data using Core factories
- [ ] Plugin discovery works with plugin.json manifests
- [ ] CLI commands support full plugin development lifecycle
- [ ] Performance meets 200K+ rows/sec target
- [ ] All tests pass (unit, integration, performance)

### Documentation Requirements 📚
- [ ] Plugin Developer Guide complete
- [ ] API documentation for all public interfaces
- [ ] Example configurations for common scenarios
- [ ] Architecture diagrams updated

### Quality Requirements 🎯
- [ ] No memory leaks over extended runs
- [ ] Error messages are clear and actionable
- [ ] Cross-platform compatibility verified
- [ ] Performance baselines documented

## Risk Mitigation

### Risk 1: Performance Regression
**Mitigation**: Run benchmarks after each major change; maintain performance test suite

### Risk 2: Breaking Changes
**Mitigation**: Keep existing interfaces stable; add new functionality through extension

### Risk 3: Complex Debugging
**Mitigation**: Add comprehensive logging; create diagnostic mode; document common issues

## Post-Sprint Readiness

After this stabilization sprint, the platform will be ready for:

1. **DelimitedSource Plugin** - CSV/TSV file reading with schema inference
2. **DelimitedSink Plugin** - CSV/TSV file writing with formatting options  
3. **JavascriptTransform Plugin** - Data transformation using Jint engine

Each plugin can follow the Template Plugin pattern with confidence that the foundation is solid, performant, and production-ready.

## Success Metrics

- **Performance**: 200K+ rows/sec baseline achieved
- **Stability**: 24-hour stress test passes without errors
- **Developer Experience**: New plugin running in < 5 minutes
- **Documentation**: 100% public API documented
- **Test Coverage**: >90% for Core bridge services