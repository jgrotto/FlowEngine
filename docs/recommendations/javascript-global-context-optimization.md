# JavaScript Global Context Optimization

**Document Type**: Technical Recommendation & Sprint Planning  
**Date**: July 12, 2025  
**Status**: Ready for Sprint 2 Implementation  
**Estimated Effort**: 2-3 week sprint (higher complexity)  
**Prerequisites**: JavaScript AST Pre-compilation (Sprint 1)

---

## Executive Summary

FlowEngine's JavaScript Transform has a **significant context management performance issue** beyond AST compilation. Current implementation recreates the entire JavaScript execution context for every row, including global variables, schema information, and utility objects.

**Key Findings:**
- ❌ **Complete context recreation per row** - 10K rows = 10K unnecessary context allocations
- ❌ **No global variable architecture** - Missing read-only global state support
- ❌ **Schema translation waste** - Same schema converted to JavaScript objects repeatedly
- 📈 **Expected improvement** - Additional 2-3x performance boost (5-10x combined with AST)
- ⚠️ **Higher complexity** - Requires new architecture for state management and thread safety

---

## Current State Analysis

### How JavaScript Context Management Works Today

**Per-Row Context Recreation Pattern:**

1. **Plugin Execution** (JavaScriptTransformPlugin.cs:346)
   ```csharp
   // For EVERY ROW in 10K dataset:
   var context = _contextService.CreateContext(row, _inputSchema!, _outputSchema, processingState);
   ```

2. **Engine Context Setup** (JintScriptEngineService.cs:129)
   ```csharp
   // For EVERY ROW:
   engine.SetValue("context", context);  // JavaScript object marshalling
   ```

3. **Context Object Creation** (JavaScriptContextService.cs:37-44)
   ```csharp
   return new JavaScriptContext
   {
       Input = new InputContext(currentRow, inputSchema),     // NEW ALLOCATION
       Output = new OutputContext(outputSchema, _arrayRowFactory), // NEW ALLOCATION
       Validate = new ValidationContext(currentRow, inputSchema),  // NEW ALLOCATION
       Route = new RoutingContext(state),                    // NEW ALLOCATION
       Utils = new UtilityContext()                         // NEW ALLOCATION
   };
   ```

### The Global Context Recreation Problem

**Schema Information Recreation** (JavaScriptContextService.cs:95-116):
```csharp
public object Schema()
{
    var fields = new List<object>();  // NEW ALLOCATION EVERY ROW

    for (int i = 0; i < _schema.ColumnCount; i++)
    {
        var column = _schema.Columns[i];
        fields.Add(new                // NEW OBJECT EVERY ROW
        {
            Name = column.Name,
            Type = column.DataType.Name,
            IsNullable = column.IsNullable,
            Index = i
        });
    }

    return new                        // NEW OBJECT EVERY ROW
    {
        Fields = fields,
        FieldCount = _schema.ColumnCount
    };
}
```

**Utility Context Recreation** (JavaScriptContextService.cs:284-324):
```csharp
internal class UtilityContext : IUtilityContext
{
    // Same utility methods created 10K+ times
    public DateTime Now() => DateTime.UtcNow;
    public string NewGuid() => Guid.NewGuid().ToString();
    public string Format(object value, string format) { /* ... */ }
}
```

### Performance Impact Analysis

**For 10,000 Customer Records Processing:**

| **Current Overhead** | **Operations** | **Impact** |
|---------------------|---------------|------------|
| Context Object Creation | 10,000x | Complete object graph allocation |
| Schema JavaScript Translation | 10,000x | Dictionary and object creation |
| Utility Object Instantiation | 10,000x | Method binding and marshalling |
| JavaScript Object Marshalling | 10,000x | CLR → Jint type conversion |
| Memory Allocation Pressure | 50,000+ allocations | GC pressure and fragmentation |

**Measured Current Performance:**
- **Throughput**: 3,870-3,997 rows/sec with JavaScript transforms
- **Context overhead**: Estimated 30-50% of total execution time
- **Memory allocations**: 5-7 objects per row context creation

---

## Performance Problem Statement

### The Context Recreation Inefficiency

**Real-World JavaScript Transform Example:**
```yaml
transform:
  type: JavaScriptTransform
  config:
    script: |
      function process(context) {
        // Access schema info - RECREATED 10K TIMES
        var schema = context.input.Schema();
        
        // Use utilities - RECREATED 10K TIMES  
        var timestamp = context.utils.Now();
        var guid = context.utils.NewGuid();
        
        // Access customer data - ONLY THIS SHOULD CHANGE
        var customer = context.input.Current();
        
        // Business logic
        context.output.SetField("processed_at", timestamp);
        return true;
      }
```

**Optimization Opportunity:**
- **Schema information**: Same for all 10K rows - should be global
- **Utility methods**: Identical for all rows - should be global  
- **Configuration data**: Read-only constants - should be global
- **Only customer data**: Should be per-row

### Missing Global Variable Architecture

**Current Limitations:**
- ❌ No way to define global variables in pipeline configuration
- ❌ No persistent state between row executions
- ❌ No read-only data caching
- ❌ No global utility object reuse
- ❌ No schema information persistence

**Example of Desired Global Context:**
```yaml
transform:
  type: JavaScriptTransform  
  config:
    globals:
      # Read-only global variables
      COMPANY_NAME: "Acme Corp"
      TAX_RATE: 0.08
      BUSINESS_RULES:
        minimum_age: 18
        maximum_credit: 50000
    script: |
      function process(context) {
        // Globals available without recreation
        var companyName = COMPANY_NAME;  // From global context
        var taxRate = TAX_RATE;          // From global context
        
        // Per-row data
        var customer = context.input.Current();
        
        // Business logic using globals
        if (customer.age < BUSINESS_RULES.minimum_age) {
          return false;
        }
        
        context.output.SetField("tax_amount", customer.amount * taxRate);
        return true;
      }
```

---

## Proposed Global Context Architecture

### Design Principles

**1. Hybrid Context Model**
- **Global Context**: Read-only data shared across all row executions
- **Per-Row Context**: Dynamic data specific to current row
- **Engine Context**: Persistent JavaScript environment with global state

**2. Context Lifecycle Management**
- **Initialization**: Create global context once during plugin startup
- **Execution**: Merge global + per-row context for each row
- **Cleanup**: Dispose global context during plugin shutdown

**3. Thread Safety Architecture**
- **Immutable Global State**: Read-only globals prevent race conditions  
- **Engine Pool Isolation**: Each pooled engine has isolated global state
- **Per-Row State Separation**: Row-specific data remains isolated

### Implementation Strategy

#### **Phase 1: Global Context Foundation**

**1. Global Context Service** 
```csharp
public interface IGlobalContextService
{
    /// <summary>
    /// Creates persistent global context for JavaScript execution
    /// </summary>
    Task<IGlobalContext> CreateGlobalContextAsync(
        GlobalContextConfiguration config,
        ISchema inputSchema,
        ISchema outputSchema);
        
    /// <summary>
    /// Merges global context with per-row data
    /// </summary>
    IJavaScriptContext MergeWithRowContext(
        IGlobalContext globalContext,
        IArrayRow currentRow,
        ProcessingState state);
}

public interface IGlobalContext : IDisposable
{
    /// <summary>
    /// Gets persistent schema information as JavaScript object
    /// </summary>
    object SchemaInfo { get; }
    
    /// <summary>
    /// Gets persistent utility functions
    /// </summary>
    object Utils { get; }
    
    /// <summary>
    /// Gets read-only global variables
    /// </summary>
    IReadOnlyDictionary<string, object> GlobalVariables { get; }
}
```

**2. Enhanced JavaScript Engine Service**
```csharp
public interface IScriptEngineService : IDisposable
{
    // Existing methods...
    
    /// <summary>
    /// Compiles script with global context support
    /// </summary>
    Task<CompiledScript> CompileWithGlobalsAsync(
        string script, 
        ScriptOptions options,
        IGlobalContext globalContext);
        
    /// <summary>
    /// Executes script with merged global + row context
    /// </summary>
    Task<ScriptResult> ExecuteWithGlobalsAsync(
        CompiledScript script,
        IJavaScriptContext mergedContext);
}
```

#### **Phase 2: Configuration Integration**

**3. Global Variable Configuration**
```csharp
public class GlobalContextConfiguration
{
    /// <summary>
    /// Read-only global variables available in JavaScript
    /// </summary>
    public IReadOnlyDictionary<string, object>? GlobalVariables { get; init; }
    
    /// <summary>
    /// Whether to cache schema information globally
    /// </summary>
    public bool CacheSchemaInfo { get; init; } = true;
    
    /// <summary>
    /// Whether to reuse utility objects globally
    /// </summary>
    public bool ReuseUtilities { get; init; } = true;
}

public class JavaScriptTransformConfiguration : IPluginConfiguration
{
    // Existing properties...
    
    /// <summary>
    /// Global context configuration for performance optimization
    /// </summary>
    public GlobalContextConfiguration? GlobalContext { get; init; }
}
```

#### **Phase 3: Engine Pool Integration**

**4. Enhanced Engine Pool with Global State**
```csharp
internal class JintEnginePooledObjectPolicy : PooledObjectPolicy<Engine>
{
    private readonly IGlobalContext? _globalContext;
    
    public override Engine Create()
    {
        var engine = new Engine(options);
        
        // Set up persistent global state
        if (_globalContext != null)
        {
            SetupGlobalContext(engine, _globalContext);
        }
        
        return engine;
    }
    
    private void SetupGlobalContext(Engine engine, IGlobalContext globalContext)
    {
        // Set global variables
        foreach (var globalVar in globalContext.GlobalVariables)
        {
            engine.SetValue(globalVar.Key, globalVar.Value);
        }
        
        // Set persistent schema and utilities
        engine.SetValue("__global_schema", globalContext.SchemaInfo);
        engine.SetValue("__global_utils", globalContext.Utils);
    }
}
```

---

## Technical Implementation Plan

### Sprint Task Breakdown

#### **Week 1: Global Context Foundation (5-6 days)**
- [ ] Implement `IGlobalContextService` and `IGlobalContext` interfaces
- [ ] Create `GlobalContextConfiguration` with YAML mapping support
- [ ] Add global variable storage and marshalling to JavaScript
- [ ] Update `JavaScriptTransformConfiguration` with global context support

#### **Week 2: Engine Integration & Context Merging (5-6 days)**
- [ ] Modify `JintScriptEngineService` for global context support
- [ ] Implement context merging strategy (global + per-row)
- [ ] Update engine pool policy for persistent global state
- [ ] Add thread safety validation and testing

#### **Week 3: Testing & Performance Validation (4-5 days)**
- [ ] Create comprehensive unit tests for global context isolation
- [ ] Add performance benchmarks comparing context recreation vs reuse
- [ ] Validate thread safety under concurrent execution
- [ ] Test memory usage impact and global context lifecycle

### Implementation Dependencies

**Required from Sprint 1 (AST Optimization):**
- ✅ **Enhanced `CompiledScript`** - AST storage foundation
- ✅ **Improved engine pooling** - Performance-optimized engine reuse
- ✅ **Enhanced performance monitoring** - Baseline metrics for comparison

**New Architecture Components:**
- ⚙️ **Global context service** - New service for persistent state management
- ⚙️ **Context merging strategy** - Hybrid global + per-row context model
- ⚙️ **Configuration integration** - YAML support for global variables
- ⚙️ **Thread safety mechanisms** - Concurrent access to global state

---

## Expected Performance Impact

### Performance Projections

**Conservative Estimate:**
- **Current with AST**: 8,000 rows/sec (2x from Sprint 1)
- **With Global Context**: 16,000 rows/sec (additional 2x improvement)
- **Combined improvement**: 4x total performance increase

**Optimistic Estimate:**
- **With Global Context**: 24,000+ rows/sec (additional 3x improvement)
- **Combined improvement**: 6x total performance increase
- **Memory allocation reduction**: 70% fewer allocations per row

### Specific Optimizations

**Eliminated Overhead:**
- ✅ **Schema translation**: Once per pipeline vs 10K times
- ✅ **Utility object creation**: Once per engine vs 10K times  
- ✅ **Global variable marshalling**: Once per engine vs 10K times
- ✅ **Context object allocation**: Reuse vs recreation
- ✅ **JavaScript object setup**: Persistent vs per-row

**Performance Test Scenarios:**
1. **Global-heavy workload**: Scripts using lots of schema info and utilities
2. **Mixed workload**: Combination of global and per-row data access
3. **Memory stress test**: Extended processing with global context reuse
4. **Concurrency test**: Multiple engines with shared global state

---

## Complexity Assessment

### Why This Is More Complex Than AST Optimization

**AST Optimization Complexity: LOW**
- ✅ **Single responsibility**: Pre-compile JavaScript to AST
- ✅ **No state management**: Stateless compilation optimization
- ✅ **No threading concerns**: Read-only AST objects
- ✅ **No architectural changes**: Uses existing `PreparedScript` property

**Global Context Optimization Complexity: MEDIUM-HIGH**
- ⚠️ **Multi-component architecture**: Global context service, configuration, engine integration
- ⚠️ **State management**: Persistent global state with lifecycle management
- ⚠️ **Thread safety concerns**: Concurrent access to shared global context
- ⚠️ **Configuration integration**: New YAML schema and validation requirements
- ⚠️ **Engine pool modifications**: Complex changes to engine initialization

### Technical Risks

**MEDIUM RISK: Thread Safety**
- **Risk**: Race conditions in global context access
- **Mitigation**: Immutable global state, engine pool isolation
- **Validation**: Concurrent execution stress testing

**MEDIUM RISK: Memory Management**
- **Risk**: Global context memory leaks or unbounded growth
- **Mitigation**: Proper lifecycle management and disposal patterns
- **Validation**: Extended execution memory monitoring

**LOW RISK: Configuration Complexity**
- **Risk**: Complex global variable configuration in YAML
- **Mitigation**: Simple key-value global variable model
- **Validation**: Configuration validation and error handling testing

**LOW RISK: Compatibility**
- **Risk**: Breaking changes to existing JavaScript transforms
- **Mitigation**: Backward compatibility via optional global context
- **Validation**: All existing integration tests must pass

---

## Sprint Planning

### Sprint Definition

**Sprint Name**: JavaScript Global Context Optimization  
**Sprint Type**: Hardening Sprint (2-3 weeks)  
**Goal**: Eliminate JavaScript context recreation overhead through persistent global state  
**Prerequisites**: JavaScript AST Pre-compilation (Sprint 1) completed

### Sprint Objectives

#### **Primary Goals**
1. **Implement global context architecture** - Persistent global variables and utilities
2. **Maintain thread safety** - Concurrent execution with shared global state
3. **Achieve 2-3x additional performance improvement** - Beyond AST optimization gains
4. **Preserve backward compatibility** - Optional global context with graceful fallback

#### **Success Criteria**
- [ ] Global variable support in YAML pipeline configuration
- [ ] ≥2x additional performance improvement over AST optimization baseline
- [ ] Thread safety validation under concurrent execution
- [ ] Memory usage remains bounded with global context reuse
- [ ] 100% compatibility with existing JavaScript transforms (global context optional)

### Deliverables

1. **Global Context Architecture** - Complete persistent global state system
2. **Enhanced Configuration Support** - YAML global variable configuration
3. **Performance Benchmarks** - Measured improvement combining AST + Global Context
4. **Thread Safety Validation** - Concurrent execution testing and validation
5. **Updated Feature Matrix** - Combined JavaScript performance characteristics

---

## Integration with Sprint 1 (AST Optimization)

### Combined Architecture Benefits

**Sprint 1 Foundation:**
- ✅ **AST Pre-compilation** - Eliminates JavaScript string parsing overhead
- ✅ **Enhanced Engine Pooling** - Optimized engine reuse patterns
- ✅ **Performance Monitoring** - Baseline metrics and tracking

**Sprint 2 Building Upon Sprint 1:**
- 🔄 **Global Context + AST** - Pre-compiled scripts with persistent global state
- 🔄 **Enhanced Engine Pool** - Engines with both AST and global context optimization
- 🔄 **Comprehensive Performance** - Combined 5-10x improvement potential

### Example Combined Optimization

**Before (Current State):**
```
Row Processing: String Parse → Context Create → Execute → Dispose
Performance: 4,000 rows/sec
Operations per 10K rows: 40,000+ allocations
```

**After Sprint 1 (AST Only):**
```
Row Processing: AST Execute → Context Create → Execute → Dispose  
Performance: 8,000 rows/sec (2x improvement)
Operations per 10K rows: 30,000 allocations (25% reduction)
```

**After Sprint 2 (AST + Global Context):**
```
Row Processing: AST Execute → Context Merge → Execute
Performance: 16,000-24,000 rows/sec (4-6x total improvement)
Operations per 10K rows: 10,000 allocations (75% reduction)
```

---

## Alignment with Strategic Goals

### Production Hardening Focus

This optimization aligns with FlowEngine's **production hardening strategy**:

✅ **Polish existing foundation** - Optimize core JavaScript performance architecture  
✅ **Make it bulletproof** - Add proper global state management and thread safety  
✅ **Evidence-based improvement** - Measurable 2-3x additional performance increase  
✅ **No scope creep** - Focused on existing JavaScript transform optimization

### Implementation Strategy Alignment

Per `implementation-strategy.md` priorities:
- ✅ **Phase 1: Foundation Stability** - Advanced performance optimization for existing capabilities
- ✅ **Sprint-lite methodology** - 2-3 week hardening sprint with complex deliverables
- ✅ **Quality over quantity** - Deep optimization of JavaScript transforms vs new features

---

## Next Steps

### Sprint 2 Prerequisites

1. **Complete Sprint 1** - JavaScript AST pre-compilation implementation
2. **Validate AST performance gains** - Establish baseline for global context improvements
3. **Review global context design** - Technical architecture review and approval

### Sprint 2 Initiation

1. **Create sprint document** in `docs/sprints/active/javascript-global-context-sprint.md`
2. **Update ROADMAP.md** with Sprint 2 timeline and dependencies
3. **Begin global context service implementation** with thread safety design

### Future Sprint Considerations

**Potential Sprint 3: JavaScript Performance Polish**
- **JavaScript debugging enhancements** - Source maps and error context
- **Memory optimization** - Advanced pooling and allocation reduction
- **Configuration simplification** - Streamlined global variable setup

---

**Recommendation Status**: ✅ **READY FOR SPRINT 2 IMPLEMENTATION**  
**Dependencies**: Requires Sprint 1 (AST optimization) completion  
**Performance Impact**: Expected 2-3x additional improvement (4-6x combined)  
**Complexity**: Medium-High (state management, thread safety, architecture changes)