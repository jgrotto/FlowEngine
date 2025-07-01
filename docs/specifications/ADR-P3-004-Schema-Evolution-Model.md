# ADR-P3-004: Schema Evolution Model

**Status**: ✅ Accepted  
**Date**: June 30, 2025  
**Decision**: Implement semantic versioning with backward compatibility for schema evolution

---

## Context

Production data pipelines require schema changes over time as business requirements evolve, but these changes must not break existing pipelines or corrupt data. Phase 2's schema-first approach needs a robust evolution strategy for:

- Adding new fields to existing schemas
- Changing field types (e.g., string to datetime)
- Removing deprecated fields safely
- Handling data from multiple schema versions
- Migrating existing data to new schema versions

Without proper schema evolution, organizations face:
- Breaking changes that halt production pipelines
- Data corruption from incompatible type changes
- Manual migration processes that are error-prone
- Inability to roll back problematic schema changes
- Complex coordination across multiple teams and systems

## Decision

**Implement semantic versioning with three compatibility modes and automatic migration:**

```yaml
schema:
  version: "2.1.0"              # Semantic versioning (major.minor.patch)
  compatibility: "backward"     # backward|forward|full|breaking
  
evolution:
  strategy: "gradual"           # gradual|immediate|manual
  migration: "automatic"        # automatic|manual|none
  rollback: "enabled"           # enabled|disabled
```

**Compatibility Levels:**
- **Backward Compatible**: New version can read old data
- **Forward Compatible**: Old version can read new data  
- **Full Compatible**: Both directions supported
- **Breaking**: Incompatible change requiring migration

## Schema Change Types

### **Patch Changes (x.x.+1)** - Always Backward Compatible
```yaml
# Adding optional fields
fields:
  - name: "existingField"
    type: "string"
  - name: "newOptionalField"    # ✅ Safe addition
    type: "string"
    optional: true
    since: "2.0.1"

# Adding field constraints
  - name: "email"
    type: "string"
    pattern: "^[\\w\\.-]+@.*"   # ✅ Safe validation addition
    since: "2.0.1"
```

### **Minor Changes (x.+1.0)** - Backward Compatible
```yaml
# Widening field types  
  - name: "age"
    type: "int64"               # ✅ Was int32, now int64
    migration:
      from: "int32"
      strategy: "auto-widen"
    since: "2.1.0"

# Adding enum values
  - name: "status"
    type: "string"
    enum: ["active", "inactive", "pending", "archived"]  # ✅ Added "archived"
    since: "2.1.0"
```

### **Major Changes (+1.0.0)** - Breaking Changes
```yaml
# Removing fields
  - name: "deprecatedField"
    type: "string"
    deprecated: "2.0.0"         # ⚠️ Marked deprecated
    removedIn: "3.0.0"          # ❌ Will be removed

# Narrowing field types
  - name: "precision"
    type: "float"               # ❌ Was double, now float
    migration:
      from: "double"
      strategy: "lossy-conversion"
      validation: "range-check"
    since: "3.0.0"
```

## Consequences

### ✅ Positive Outcomes
- **Safe Evolution**: Clear rules for compatible vs. breaking changes
- **Automatic Migration**: Most changes handled without manual intervention
- **Rollback Capability**: Ability to revert problematic schema changes
- **Data Integrity**: Validation prevents data corruption during migration
- **Team Coordination**: Clear versioning communicates impact of changes
- **Production Stability**: Gradual rollout minimizes risk of widespread failures

### ❌ Negative Outcomes
- **Complexity**: Schema versioning adds conceptual and implementation overhead
- **Storage Overhead**: May need to store multiple schema versions temporarily
- **Migration Time**: Large datasets may require extended migration windows
- **Validation Cost**: Runtime schema validation adds processing overhead

### 🔄 Mitigation Strategies
- **Schema Registry**: Centralized management of schema versions and compatibility
- **Lazy Migration**: Migrate data on-demand rather than batch processing
- **Caching**: Cache schema compatibility decisions to reduce validation overhead
- **Monitoring**: Track migration progress and performance impact

## Technical Implementation

### **Schema Registry Structure**
```csharp
public class SchemaRegistry
{
    public async Task<Schema> GetSchemaAsync(string name, string version)
    {
        // Return specific schema version
    }
    
    public async Task<CompatibilityResult> CheckCompatibilityAsync(
        Schema oldSchema, Schema newSchema)
    {
        // Determine compatibility level and required migrations
    }
    
    public async Task<MigrationPlan> GenerateMigrationAsync(
        Schema from, Schema to)
    {
        // Create step-by-step migration plan
    }
}
```

### **Automatic Migration Engine**
```csharp
public class SchemaTransformer
{
    public ArrayRow Transform(ArrayRow input, Schema fromSchema, Schema toSchema)
    {
        var output = new ArrayRow(toSchema);
        var migrationPlan = GetCachedMigrationPlan(fromSchema, toSchema);
        
        foreach (var step in migrationPlan.Steps)
        {
            switch (step.Type)
            {
                case MigrationType.CopyField:
                    output.SetValue(step.TargetIndex, input.GetValue(step.SourceIndex));
                    break;
                    
                case MigrationType.TypeConversion:
                    var converted = ConvertType(
                        input.GetValue(step.SourceIndex), 
                        step.FromType, 
                        step.ToType);
                    output.SetValue(step.TargetIndex, converted);
                    break;
                    
                case MigrationType.SetDefault:
                    output.SetValue(step.TargetIndex, step.DefaultValue);
                    break;
                    
                case MigrationType.ComputeField:
                    var computed = ExecuteExpression(step.Expression, input);
                    output.SetValue(step.TargetIndex, computed);
                    break;
            }
        }
        
        return output;
    }
}
```

### **Gradual Migration Strategy**
```csharp
public class GradualMigrationService
{
    public async Task BeginMigrationAsync(string schemaName, string targetVersion)
    {
        // 1. Register new schema version
        await _registry.RegisterSchemaAsync(schemaName, targetVersion);
        
        // 2. Start accepting data in both formats
        await _processor.EnableDualFormat(schemaName, targetVersion);
        
        // 3. Begin background migration of stored data
        await _migrator.StartBackgroundMigrationAsync(schemaName, targetVersion);
        
        // 4. Monitor migration progress
        await _monitor.TrackMigrationProgressAsync(schemaName, targetVersion);
    }
}
```

## Migration Strategies

### **Field-Level Migrations**
```yaml
migrations:
  - field: "timestamp"
    from: "string"
    to: "datetime"
    strategy: "parse"
    format: "yyyy-MM-dd HH:mm:ss"
    onError: "default"          # default|skip|fail
    
  - field: "fullName"
    from: ["firstName", "lastName"]
    to: "fullName"
    strategy: "concatenate"
    separator: " "
    
  - field: "age"
    from: "birthDate"
    to: "age"
    strategy: "compute"
    expression: "DATEDIFF(YEAR, birthDate, NOW())"
```

### **Schema-Level Migrations**
```yaml
schema:
  version: "2.0.0"
  migrations:
    from: "1.0.0"
    steps:
      - type: "add_field"
        field: "createdAt"
        type: "datetime"
        default: "NOW()"
        
      - type: "rename_field"
        from: "old_name"
        to: "new_name"
        
      - type: "remove_field"
        field: "deprecated_field"
        backup: true              # Keep in backup schema
        
      - type: "split_field"
        from: "address"
        to: ["street", "city", "zipCode"]
        strategy: "regex"
        pattern: "^(.*), (.*), (\\d{5})$"
```

## Rollback Strategy

### **Safe Rollback Conditions**
```yaml
rollback:
  enabled: true
  conditions:
    - errorRate: "> 5%"         # Rollback if error rate exceeds 5%
    - dataLoss: "> 0%"          # Rollback if any data loss detected
    - performance: "> 200%"     # Rollback if processing time doubles
    
  strategy: "immediate"         # immediate|gradual|manual
  preserveNewData: true         # Keep data created during migration
```

### **Rollback Implementation**
```csharp
public async Task<RollbackResult> RollbackSchemaAsync(
    string schemaName, 
    string fromVersion, 
    string toVersion)
{
    // 1. Stop accepting new schema format
    await _processor.DisableNewFormat(schemaName, fromVersion);
    
    // 2. Migrate new data back to old format (if possible)
    if (IsBackwardMigrationPossible(fromVersion, toVersion))
    {
        await _migrator.StartReverseMigrationAsync(schemaName, toVersion);
    }
    
    // 3. Restore old schema as current
    await _registry.ActivateSchemaAsync(schemaName, toVersion);
    
    // 4. Clean up migration artifacts
    await _migrator.CleanupMigrationAsync(schemaName, fromVersion);
    
    return RollbackResult.Success();
}
```

## Alternatives Considered

### **Schema-less/Dynamic Approach** (Rejected)
- **Pro**: Ultimate flexibility, no migration needed
- **Con**: Poor performance (no ArrayRow optimization), runtime errors, no type safety

### **Immutable Schemas Only** (Rejected)
- **Pro**: Simple, no migration complexity
- **Con**: Inflexible, requires new pipelines for schema changes

### **Database-Style Migrations** (Rejected)
- **Pro**: Well-understood pattern
- **Con**: Too heavyweight for data processing, requires central coordination

## Configuration Examples

### **Production Configuration**
```yaml
schemaEvolution:
  registry:
    type: "file"              # file|database|redis
    path: "./schemas"
    backup: true
    
  migration:
    strategy: "gradual"
    batchSize: 10000
    parallelism: 4
    timeout: "30m"
    
  validation:
    strict: true              # Fail on validation errors
    logWarnings: true
    metrics: true
    
  rollback:
    enabled: true
    autoTrigger: true
    preserveData: true
```

### **Development Configuration**
```yaml
schemaEvolution:
  registry:
    type: "memory"            # Fast for testing
    
  migration:
    strategy: "immediate"     # No gradual rollout needed
    
  validation:
    strict: false             # Allow warnings
    logWarnings: true
    
  rollback:
    enabled: true
    autoTrigger: false        # Manual rollback for debugging
```

## CLI Commands

### **Schema Management**
```bash
# Create new schema version
dotnet run -- schema create --name UserProfile --version 2.0.0 --from 1.0.0

# Check compatibility
dotnet run -- schema compatible --schema UserProfile --from 1.0.0 --to 2.0.0

# Generate migration plan
dotnet run -- schema migration --schema UserProfile --from 1.0.0 --to 2.0.0 --output migration.yaml

# Apply migration
dotnet run -- schema migrate --schema UserProfile --to 2.0.0 --strategy gradual

# Rollback migration
dotnet run -- schema rollback --schema UserProfile --to 1.0.0
```

### **Migration Monitoring**
```bash
# Monitor migration progress
dotnet run -- migration status --schema UserProfile

# View migration logs
dotnet run -- migration logs --schema UserProfile --follow

# Generate migration report
dotnet run -- migration report --schema UserProfile --output report.html
```

## Success Metrics

- **Zero Data Loss**: No data corruption or loss during schema migrations
- **Backward Compatibility**: 95% of schema changes should be backward compatible
- **Migration Speed**: Large datasets (>1M rows) migrate within acceptable time windows
- **Rollback Time**: Failed migrations rollback within 5 minutes
- **Developer Experience**: Schema changes deployable with single CLI command

## Validation Strategy

### **Pre-Migration Validation**
- Schema compatibility analysis
- Data sampling to test migration rules
- Performance impact estimation
- Rollback plan verification

### **During Migration Validation**
- Real-time error rate monitoring
- Data integrity checksum validation
- Performance metrics tracking
- Automatic rollback trigger evaluation

### **Post-Migration Validation**
- End-to-end pipeline testing
- Data quality verification
- Performance baseline comparison
- Rollback capability testing

## Future Enhancements

- **Machine Learning**: Automatic schema inference from data patterns
- **Visual Tools**: GUI for schema design and migration planning
- **Advanced Analytics**: Schema usage patterns and optimization recommendations
- **Cross-System**: Schema evolution coordination across multiple systems