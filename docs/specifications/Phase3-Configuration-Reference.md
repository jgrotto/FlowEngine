# FlowEngine Phase 3: Five-Component Configuration Reference

**Reading Time**: 4 minutes  
**Purpose**: Complete YAML schema for five-component plugins  

---

## Plugin Configuration Structure

```yaml
plugin:
  name: string              # Required: Plugin identifier
  version: string           # Required: Semantic version
  description: string       # Optional: Plugin description
  
configuration:
  # Component 1: Configuration
  inputSchema:              # Required: Input data schema
    fields: []
  outputSchema:             # Required: Output data schema  
    fields: []
  parameters: {}            # Optional: Plugin-specific settings
  
  # Component 2: Validator
  validation:
    mode: "strict"          # strict|compatible|permissive
    failOnError: true       # true|false
    
  # Component 3: Plugin
  isolation:
    enabled: true           # Enable AssemblyLoadContext isolation
    memoryLimit: "256MB"    # Memory limit for plugin
    
  # Component 4: Processor  
  processing:
    batchSize: 1000         # Rows per batch
    parallelism: 1          # Parallel processing threads
    
  # Component 5: Service
  service:
    lifecycle: "scoped"     # singleton|scoped|transient
    dependencies: []        # Service dependencies
```

---

## Schema Definition

### Field Types
```yaml
fields:
  - name: "id"
    type: "int64"           # int32, int64, float, double
    nullable: false         # Optional: default false
    
  - name: "name"
    type: "string"
    maxLength: 100          # Optional: string constraints
    
  - name: "timestamp" 
    type: "datetime"
    format: "ISO8601"       # Optional: datetime format
    
  - name: "metadata"
    type: "object"          # For complex types
    schema: {}              # Nested schema definition
```

### ArrayRow Optimization
```yaml
# IMPORTANT: Field order affects ArrayRow performance
# Place frequently accessed fields first
fields:
  - name: "id"              # Index 0 - fastest access
    type: "int64"
  - name: "name"            # Index 1 - second fastest  
    type: "string"
  - name: "metadata"        # Index N - slower access
    type: "object"
```

---

## Component Configuration

### 1. Configuration Component
```yaml
configuration:
  parameters:
    connectionString: "${ENV:DATABASE_URL}"
    timeout: 30
    retryCount: 3
```

### 2. Validator Component  
```yaml
validation:
  mode: "strict"            # Exact schema match required
  # mode: "compatible"      # Allow compatible transformations
  # mode: "permissive"      # Best-effort with warnings
  
  rules:
    - field: "age"
      min: 0
      max: 150
    - field: "email"
      pattern: "^[\\w\\.-]+@[\\w\\.-]+\\.[a-zA-Z]{2,}$"
```

### 3. Plugin Component
```yaml
isolation:
  enabled: true
  memoryLimit: "256MB"
  timeoutSeconds: 300
  allowedAssemblies:
    - "System.*"
    - "Microsoft.Extensions.*"
```

### 4. Processor Component
```yaml
processing:
  batchSize: 1000           # Optimize for your data size
  parallelism: 1            # Start with 1, increase if needed
  spilloverThreshold: "128MB"
  spilloverPath: "./temp"
```

### 5. Service Component
```yaml
service:
  lifecycle: "scoped"       # Plugin instance lifecycle
  dependencies:
    - "ILogger<MyPlugin>"
    - "IConfiguration"
  healthCheck:
    enabled: true
    intervalSeconds: 30
```

---

## Environment Variables

```yaml
# Use environment variables for sensitive data
configuration:
  parameters:
    apiKey: "${ENV:API_KEY}"                    # Required env var
    database: "${ENV:DB_CONNECTION:localhost}"  # With default value
    debug: "${ENV:DEBUG:false}"                 # Boolean with default
```

---

## Validation Error Codes

| Code | Message | Solution |
|------|---------|----------|
| **CFG001** | Invalid field type | Check field type spelling |
| **CFG002** | Missing required field | Add field to schema |
| **CFG003** | Schema mismatch | Verify input/output compatibility |
| **CFG004** | Invalid memory limit | Use format like "256MB" or "1GB" |
| **CFG005** | Unknown validation mode | Use strict/compatible/permissive |

---

## Common Patterns

### Transform Plugin
```yaml
plugin:
  name: "DataTransform"
  
configuration:
  inputSchema:
    fields:
      - name: "rawData"
        type: "string"
  outputSchema:  
    fields:
      - name: "processedData"
        type: "object"
```

### Source Plugin
```yaml
plugin:
  name: "CsvSource"
  
configuration:
  outputSchema:
    fields:
      - name: "column1"
        type: "string"
  parameters:
    filePath: "${ENV:INPUT_FILE}"
    delimiter: ","
```

### Sink Plugin
```yaml
plugin:
  name: "DatabaseSink"
  
configuration:
  inputSchema:
    fields:
      - name: "id"
        type: "int64"
      - name: "data"
        type: "string"
  parameters:
    tableName: "output_table"
```