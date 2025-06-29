# FlowEngine Phase 2 Configuration Reference

**Reading Time**: 4 minutes  
**Purpose**: Complete YAML configuration schema and common patterns  

---

## Core Configuration Schema

### Pipeline Structure
```yaml
pipeline:
  name: "string"                    # Required: Pipeline identifier
  version: "1.0.0"                  # Required: Pipeline version
  description: "string"             # Optional: Documentation
  
  plugins: []                       # Required: Plugin definitions
  connections: []                   # Required: Data flow connections
  
  # Optional: Global settings
  settings:
    channels: {}                    # Channel configuration
    isolation: {}                   # Plugin isolation settings
    monitoring: {}                  # Observability settings
```

### Plugin Definition
```yaml
plugins:
  - name: "uniqueName"              # Required: Plugin instance name
    type: "PluginTypeName"          # Required: Plugin class name
    assembly: "path/to/assembly"    # Optional: Custom assembly path
    
    # Schema definition (required for sources/transforms)
    schema:
      name: "schemaName"            # Required: Schema identifier
      version: "1.0.0"              # Required: Schema version
      fields:                       # Required: Field definitions
        - name: "fieldName"
          type: "int32|string|bool|float64|datetime"
          required: true|false
          default: value            # Optional: Default value
    
    # Plugin-specific configuration
    config:
      key: "value"                  # Plugin-specific settings
```

### Connection Definition
```yaml
connections:
  - from: "sourcePlugin"            # Required: Source plugin name
    to: "targetPlugin"              # Required: Target plugin name
    channel: "channelName"          # Optional: Named channel config
    schema:                         # Optional: Schema transformation
      mode: "strict|compatible|permissive"
      target: "1.1.0"              # Target schema version
```

## Common Configuration Patterns

### High-Throughput Batch Processing
```yaml
pipeline:
  settings:
    channels:
      default:
        bufferSize: 1000
        backpressureThreshold: 90
        allowOverflow: true
        fullMode: "DropOldest"
    
    isolation:
      defaultMemoryLimitMB: 500
      defaultTimeoutSeconds: 120
```

### Low-Latency Stream Processing
```yaml
pipeline:
  settings:
    channels:
      default:
        bufferSize: 10
        backpressureThreshold: 50
        allowOverflow: false
        fullMode: "Wait"
    
    isolation:
      defaultMemoryLimitMB: 100
      defaultTimeoutSeconds: 5
```

### Memory-Constrained Environment
```yaml
pipeline:
  settings:
    channels:
      default:
        bufferSize: 50
        backpressureThreshold: 70
        enableMemoryPressureAdaptation: true
    
    isolation:
      defaultMemoryLimitMB: 50
      enableSpillover: true
```

## Schema Evolution Examples

### Adding Optional Field (Compatible)
```yaml
# Original schema v1.0.0
schema:
  name: "customer"
  version: "1.0.0"
  fields:
    - name: "id"
      type: "int32"
      required: true
    - name: "name"
      type: "string"
      required: true

# Updated schema v1.1.0
schema:
  name: "customer"
  version: "1.1.0"
  fields:
    - name: "id"
      type: "int32"
      required: true
    - name: "name"
      type: "string"
      required: true
    - name: "email"          # New optional field
      type: "string"
      required: false
      default: ""
  
  # Automatic migration from v1.0.0
  transformations:
    - from: "1.0.0"
      type: "add_field"
      field: "email"
      default: ""
```

### Type Widening (Compatible)
```yaml
schema:
  name: "metrics"
  version: "1.1.0"
  fields:
    - name: "count"
      type: "int64"            # Was int32 in v1.0.0
      required: true
  
  transformations:
    - from: "1.0.0"
      type: "widen_field"
      field: "count"
      fromType: "int32"
      toType: "int64"
```

## Validation Rules

### Schema Validation
- **Field names**: Must be valid C# identifiers
- **Types**: Must be supported primitive types
- **Versions**: Must follow semantic versioning (major.minor.patch)

### Connection Validation
- **Source/Target**: Must reference existing plugin names
- **Schema compatibility**: Source schema must be compatible with target
- **Circular dependencies**: Not allowed in connection graph

### Resource Validation
- **Memory limits**: Must be positive integers
- **Timeouts**: Must be positive time spans
- **Buffer sizes**: Must be between 1 and 10000

## Environment Variables

```bash
FLOWENGINE_LOG_LEVEL=Debug|Info|Warning|Error
FLOWENGINE_PLUGIN_PATH=/path/to/plugins
FLOWENGINE_SCHEMA_REGISTRY_PATH=/path/to/schemas
FLOWENGINE_ENABLE_TELEMETRY=true|false
FLOWENGINE_MAX_MEMORY_MB=1024
```

## Error Messages

**Common Configuration Errors**:
- `Schema version X.Y.Z is not compatible with A.B.C`
- `Plugin 'name' references unknown schema 'schemaName'`
- `Circular dependency detected: plugin1 → plugin2 → plugin1`
- `Buffer size must be between 1 and 10000, got: {value}`

**Troubleshooting**:
- Validate YAML syntax with online validators
- Check plugin assembly references and paths
- Verify schema compatibility with migration tools
- Monitor resource usage with built-in telemetry