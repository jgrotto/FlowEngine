# FlowEngine Core Bridge Implementation Plan

**Document Version**: 1.0  
**Date**: January 2025  
**Status**: Implementation Planning  
**Priority**: Critical Path Blocker  

## Executive Summary

The FlowEngine has a mature core implementation and a well-designed plugin architecture, but lacks the critical "bridge layer" that enables external plugins to interact with core functionality. This document outlines the implementation plan to complete this bridge, unblocking plugin development for the next sprints.

## Current State Analysis

### What's Working ✅
- **Core Engine**: ArrayRow optimization, channel architecture, memory management
- **Internal Plugins**: Tightly coupled but functional
- **Template Plugin**: Demonstrates correct five-component architecture
- **Abstractions Layer**: Proper interface definitions for plugin contracts

### What's Missing 🚧
- **Factory Interfaces**: No way for plugins to create Core data types
- **Service Injection**: Plugins can't access Core services (logging, metrics, etc.)
- **Plugin Discovery**: CLI can't properly load external plugins
- **Type Creation Bridge**: Gap between Abstractions interfaces and Core implementations

## Critical Gap: The Missing Bridge

### Problem Statement
External plugins follow the correct pattern of depending only on `FlowEngine.Abstractions`, but they need to create and manipulate Core data types (Schema, ArrayRow, Chunk, Dataset). Without factory interfaces, plugins resort to placeholder implementations, preventing real data processing.

### Architectural Impact
```
Current State:
Plugin → Abstractions → ??? → Core Types

Required State:
Plugin → Abstractions → Bridge Services → Core Types
```

## Core Bridge Implementation Tasks

### Task 1: Implement Factory Interfaces (Priority: Critical)

**Objective**: Enable plugins to create Core data types through Abstractions

**Key Interfaces Needed**:
- `ISchemaFactory` - Create schemas from column definitions
- `IArrayRowFactory` - Create optimized ArrayRow instances
- `IChunkFactory` - Create data chunks with proper memory management
- `IDatasetFactory` - Create complete datasets
- `IDataTypeService` - Type conversion and validation

**Implementation Considerations**:
- Factories should validate inputs to prevent invalid state
- Memory allocation should go through IMemoryManager
- Performance characteristics must match direct instantiation
- Thread safety requirements must be documented

### Task 2: Complete Dependency Injection Pipeline (Priority: High)

**Objective**: Inject Core services into external plugins

**Services to Inject**:
- `ILogger<T>` - Plugin-specific logging
- `IPerformanceCounters` - Metrics collection
- `IMemoryManager` - Memory allocation governance
- `IScriptEngineService` - JavaScript execution (for transform plugins)
- `IValidationService` - Configuration validation

**Implementation Approach**:
- Extend PluginLoader to create plugin-specific service containers
- Bridge Core services through Abstractions interfaces
- Maintain isolation between plugins (separate DI scopes)
- Consider service lifetime management (singleton vs scoped)

### Task 3: Fix Plugin Discovery and Loading (Priority: High)

**Current Issues**:
- PluginLoader attempts to load types before assembly is loaded
- No clear mechanism for external plugin discovery
- Configuration binding for external plugins is incomplete

**Required Fixes**:
- Implement proper assembly loading sequence
- Add plugin manifest/discovery mechanism
- Complete configuration deserialization for plugin-specific configs
- Handle version compatibility checking

### Task 4: Simplify Initial Implementation (Priority: Medium)

**Observations**:
- AssemblyLoadContext isolation may be overengineered for MVP
- Complex type resolution adds brittleness
- Configuration system has too many abstraction layers

**Recommendations**:
- Start with simple assembly loading (add isolation later)
- Use direct type instantiation where possible
- Simplify configuration to basic YAML mapping
- Defer advanced features (hot-swapping, versioning) to later sprints

## Implementation Sequence

### Phase 1: Minimal Bridge (Days 1-2)
1. Create factory interfaces in Abstractions
2. Implement factories in Core
3. Register factories in Core DI container
4. Modify PluginLoader to inject factories

**Success Criteria**: Template plugin creates real ArrayRow and processes data

### Phase 2: Service Integration (Day 2-3)
1. Extend plugin loading to create scoped service containers
2. Bridge Core services through Abstractions
3. Update template plugin to use injected services
4. Verify metrics and logging flow correctly

**Success Criteria**: Plugins report metrics and logs through Core infrastructure

### Phase 3: Loading and Discovery (Day 3)
1. Fix assembly loading sequence
2. Implement plugin discovery (directory scan or manifest)
3. Complete configuration binding
4. Test with external plugin assembly

**Success Criteria**: CLI successfully loads and executes external plugin

## Risk Mitigation

### Risk 1: Performance Degradation
**Mitigation**: Benchmark factory performance vs direct instantiation; optimize if >5% overhead

### Risk 2: Breaking Internal Plugins
**Mitigation**: Keep internal plugin system unchanged; add external loading alongside

### Risk 3: Complex Debug Scenarios
**Mitigation**: Comprehensive logging at bridge points; clear error messages for common failures

### Risk 4: Scope Creep
**Mitigation**: Defer advanced features; focus on minimal working bridge

## Recommendations

### Immediate Actions
1. **Prioritize Factory Implementation**: This is the critical blocker
2. **Keep It Simple**: Avoid overengineering the initial implementation
3. **Test with Template Plugin**: Use it as the validation case throughout

### Architectural Considerations
1. **Document Bridge Contracts**: Clear documentation of bridge interfaces
2. **Performance Benchmarks**: Establish baseline before optimization
3. **Error Handling**: Fail fast with clear messages at bridge points

### Future Enhancements (Post-MVP)
- AssemblyLoadContext isolation for security
- Plugin versioning and compatibility checking
- Hot-swapping support
- Advanced configuration schemas
- Plugin marketplace/repository support

## Success Metrics

### Functional Success
- [ ] Template plugin processes 100K+ rows of real data
- [ ] All three planned plugins (Delimited Source/Sink, JavaScript Transform) implementable
- [ ] CLI executes external plugin pipelines without errors

### Performance Success
- [ ] Factory overhead <5% vs direct instantiation
- [ ] No memory leaks in bridge layer
- [ ] Plugin performance matches internal equivalent

### Developer Experience Success
- [ ] Plugin development requires only Abstractions reference
- [ ] Clear error messages for common failures
- [ ] Simple plugin creation from template

## Conclusion

The Core Bridge Implementation is a focused effort to connect the well-designed plugin architecture with the mature core engine. By implementing factory interfaces and completing the service injection pipeline, we can unblock plugin development and deliver the planned Sprint 2-4 plugins. The key is maintaining simplicity while establishing the foundational bridge that enables the entire plugin ecosystem.