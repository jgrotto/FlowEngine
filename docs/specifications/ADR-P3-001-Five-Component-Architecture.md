# ADR-P3-001: Five-Component Architecture

**Status**: ✅ Accepted  
**Date**: June 30, 2025  
**Decision**: Standardize all Phase 3 plugins using the five-component pattern

---

## Context

Phase 2 plugins showed inconsistent architecture patterns, making them difficult to test, debug, and maintain. Different developers implemented plugins with varying numbers of components and responsibilities, leading to:

- Inconsistent error handling across plugins
- Difficult unit testing due to mixed responsibilities  
- Poor separation of concerns
- Hard to optimize individual components
- Complex debugging when issues occurred

## Decision

**Adopt a standardized five-component architecture for all Phase 3 plugins:**

```
Configuration → Validator → Plugin → Processor → Service
```

**Component Responsibilities:**
- **Configuration**: Schema definitions and plugin parameters
- **Validator**: Data and configuration validation logic
- **Plugin**: Coordination and orchestration of processing
- **Processor**: Core data transformation logic
- **Service**: External dependencies and lifecycle management

## Consequences

### ✅ Positive Outcomes
- **Testability**: Each component can be unit tested independently
- **Maintainability**: Clear separation of concerns reduces complexity
- **Performance**: Components can be optimized individually
- **Standardization**: Consistent patterns across all plugins
- **Debugging**: Issues can be isolated to specific components
- **ArrayRow Optimization**: Configuration component pre-calculates field indexes

### ❌ Negative Outcomes  
- **Initial Complexity**: More files and interfaces per plugin
- **Learning Curve**: Developers must understand five-component pattern
- **Over-Engineering**: Simple plugins may seem unnecessarily complex

### 🔄 Mitigation Strategies
- **Code Generation**: CLI tools generate five-component templates automatically
- **Clear Documentation**: Comprehensive examples and patterns provided
- **Base Classes**: Shared base implementations reduce boilerplate code
- **Training**: Team training on five-component architecture principles

## Alternatives Considered

### **Single-Class Plugin** (Rejected)
- **Pro**: Simple, minimal code
- **Con**: Mixed responsibilities, hard to test, poor maintainability

### **Three-Component Plugin** (Rejected)  
- **Pro**: Simpler than five components
- **Con**: Still mixed responsibilities, insufficient separation

### **Microservice-Style Plugin** (Rejected)
- **Pro**: Maximum isolation
- **Con**: Excessive overhead, complex deployment, network latency

## Implementation Notes

- Use dependency injection for component wiring
- Pre-calculate ArrayRow field indexes in Configuration component
- Implement comprehensive validation in Validator component
- Keep Service component stateless when possible
- Provide base classes for common patterns

## Validation Criteria

- All new plugins must implement five-component pattern
- Plugin validation CLI command enforces component structure
- Code review checklist includes five-component verification
- Performance benchmarks validate ArrayRow optimization in Configuration