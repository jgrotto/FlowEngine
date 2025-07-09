# FlowEngine Configuration Reference

**Complete Guide to Pipeline and Plugin Configuration**

FlowEngine uses YAML-based configuration for defining data processing pipelines. This guide provides comprehensive reference for all configuration options.

## 🏗️ Pipeline Configuration Structure

### Basic Pipeline Structure

```yaml
pipeline:
  name: "MyPipeline"                    # Required: Pipeline identifier
  version: "1.0.0"                      # Optional: Version for tracking
  description: "Data processing pipeline" # Optional: Pipeline description
  
  settings:                             # Optional: Global pipeline settings
    maxMemoryMB: 2048                   # Memory limit for pipeline
    chunkSize: 5000                     # Default chunk size
    parallelism: 4                      # Parallel execution threads
    timeout: 300                        # Pipeline timeout (seconds)
    
  steps:                                # Required: Processing steps
    - id: step1                         # Required: Unique step identifier
      type: plugin-type                 # Required: Plugin type name
      config:                           # Plugin-specific configuration
        # Plugin configuration here
        
  connections:                          # Required: Data flow connections
    - from: step1                       # Source step
      to: step2                         # Target step
      
  routing:                              # Optional: Routing targets
    queue_name:                         # Custom queue name
      type: plugin-type                 # Target plugin type
      config:
        # Target configuration
```

### Complete Example

```yaml
pipeline:
  name: "CustomerProcessing"
  version: "1.0.0"
  description: "Process customer data with validation and enrichment"
  
  settings:
    maxMemoryMB: 4096
    chunkSize: 10000
    parallelism: 8
    timeout: 600
    
  steps:
    - id: source
      type: delimited-source
      config:
        filePath: "data/customers.csv"
        delimiter: ","
        hasHeaders: true
        encoding: "UTF-8"
        chunkSize: 5000
        outputSchema:
          fields:
            - {name: customer_id, type: integer, required: true}
            - {name: first_name, type: string, required: true}
            - {name: last_name, type: string, required: true}
            - {name: email, type: string, required: false}
            - {name: amount, type: decimal, required: true}
        
    - id: transform
      type: javascript-transform
      config:
        script: |
          function process(context) {
            const current = context.input.current();
            
            // Validation
            if (!context.validate.required(['customer_id', 'amount'])) {
              return false;
            }
            
            // Business logic
            const amount = parseFloat(current.amount) || 0;
            const riskScore = amount > 10000 ? 'high' : 
                             amount > 1000 ? 'medium' : 'low';
            
            // Routing
            if (amount > 50000) {
              context.route.sendCopy('high_value_review');
            }
            
            // Output
            context.output.setField('customer_id', current.customer_id);
            context.output.setField('full_name', `${current.first_name} ${current.last_name}`);
            context.output.setField('email', current.email || 'unknown@example.com');
            context.output.setField('amount', amount);
            context.output.setField('risk_score', riskScore);
            context.output.setField('processed_at', context.utils.now());
            
            return true;
          }
          
        engine:
          timeout: 5000
          memoryLimit: 10485760
          enableCaching: true
          
        performance:
          enableMonitoring: true
          targetThroughput: 120000
          logPerformanceWarnings: true
          
        outputSchema:
          fields:
            - {name: customer_id, type: integer, required: true}
            - {name: full_name, type: string, required: true}
            - {name: email, type: string, required: false}
            - {name: amount, type: decimal, required: true}
            - {name: risk_score, type: string, required: true}
            - {name: processed_at, type: datetime, required: true}
        
    - id: sink
      type: delimited-sink
      config:
        filePath: "output/processed_customers.csv"
        delimiter: ","
        includeHeaders: true
        encoding: "UTF-8"
        bufferSize: 131072
        flushInterval: 1000
        overwriteExisting: true
        
  connections:
    - from: source
      to: transform
    - from: transform
      to: sink
      
  routing:
    high_value_review:
      type: delimited-sink
      config:
        filePath: "output/high_value_customers.csv"
        includeHeaders: true
        delimiter: ","
```

## ⚙️ Global Settings

### Pipeline Settings

```yaml
settings:
  # Memory management
  maxMemoryMB: 2048                     # Maximum memory usage (MB)
  memoryWarningThreshold: 80            # Warning threshold (% of max)
  enableMemoryMonitoring: true          # Enable memory monitoring
  
  # Processing configuration
  chunkSize: 5000                       # Default chunk size
  parallelism: 4                        # Parallel processing threads
  timeout: 300                          # Pipeline timeout (seconds)
  
  # Performance settings
  enablePerformanceMonitoring: true     # Enable performance tracking
  performanceLogInterval: 30            # Performance log interval (seconds)
  
  # Error handling
  continueOnError: false                # Continue processing on errors
  maxErrorCount: 100                    # Maximum error count before stopping
  errorRetryCount: 3                    # Number of retry attempts
  
  # Logging
  logLevel: "Information"               # Log level (Debug, Information, Warning, Error)
  enableVerboseLogging: false           # Enable verbose logging
  
  # Monitoring
  enableTelemetry: true                 # Enable telemetry
  telemetryInterval: 60                 # Telemetry interval (seconds)
```

### Environment Variables

```bash
# Pipeline settings
export FLOWENGINE_MAX_MEMORY=4096
export FLOWENGINE_CHUNK_SIZE=10000
export FLOWENGINE_PARALLELISM=8
export FLOWENGINE_TIMEOUT=600

# Performance settings
export FLOWENGINE_ENABLE_MONITORING=true
export FLOWENGINE_LOG_INTERVAL=30

# Logging
export FLOWENGINE_LOG_LEVEL=Information
export FLOWENGINE_ENABLE_VERBOSE=false
```

## 🔌 Plugin Configurations

### Delimited Source Plugin

```yaml
- id: source
  type: delimited-source
  config:
    # File configuration
    filePath: "data/input.csv"           # Required: Input file path
    delimiter: ","                       # Field delimiter (default: ",")
    hasHeaders: true                     # File has header row (default: true)
    encoding: "UTF-8"                    # File encoding (default: "UTF-8")
    
    # Processing configuration
    chunkSize: 5000                      # Rows per chunk (default: 5000)
    bufferSize: 131072                   # Read buffer size (default: 128KB)
    skipEmptyLines: true                 # Skip empty lines (default: true)
    trimFields: true                     # Trim whitespace (default: true)
    
    # Quote handling
    quoteChar: '"'                       # Quote character (default: ")
    escapeChar: '"'                      # Escape character (default: ")
    allowQuotedNewlines: true            # Allow newlines in quoted fields
    
    # Error handling
    continueOnError: false               # Continue on parsing errors
    maxErrorCount: 100                   # Maximum error count
    
    # Schema configuration
    outputSchema:
      fields:
        - name: customer_id
          type: integer
          required: true
        - name: first_name
          type: string
          required: true
        - name: last_name
          type: string
          required: true
        - name: email
          type: string
          required: false
        - name: amount
          type: decimal
          required: true
        - name: created_at
          type: datetime
          required: false
```

### JavaScript Transform Plugin

```yaml
- id: transform
  type: javascript-transform
  config:
    # Script configuration
    script: |                           # Required: JavaScript code
      function process(context) {
        const current = context.input.current();
        
        // Validation
        if (!context.validate.required(['customer_id'])) {
          return false;
        }
        
        // Business logic
        context.output.setField('processed_id', current.customer_id);
        context.output.setField('processed_at', context.utils.now());
        
        return true;
      }
      
    # Engine configuration
    engine:
      timeout: 5000                     # Script timeout (ms) (default: 5000)
      memoryLimit: 10485760             # Memory limit (bytes) (default: 10MB)
      enableCaching: true               # Enable script caching (default: true)
      enableDebugging: false            # Enable debug mode (default: false)
      
    # Performance configuration
    performance:
      enableMonitoring: true            # Enable performance monitoring
      targetThroughput: 120000          # Target throughput (rows/sec)
      logPerformanceWarnings: true      # Log performance warnings
      
    # Output schema
    outputSchema:
      fields:
        - name: processed_id
          type: integer
          required: true
        - name: processed_at
          type: datetime
          required: true
```

### Delimited Sink Plugin

```yaml
- id: sink
  type: delimited-sink
  config:
    # File configuration
    filePath: "output/result.csv"        # Required: Output file path
    delimiter: ","                       # Field delimiter (default: ",")
    includeHeaders: true                 # Include header row (default: true)
    encoding: "UTF-8"                    # File encoding (default: "UTF-8")
    
    # Write configuration
    bufferSize: 131072                   # Write buffer size (default: 128KB)
    flushInterval: 1000                  # Flush interval (rows) (default: 1000)
    createDirectories: true              # Create output directories (default: true)
    overwriteExisting: true              # Overwrite existing files (default: false)
    
    # Quote handling
    quoteChar: '"'                       # Quote character (default: ")
    escapeChar: '"'                      # Escape character (default: ")
    quoteAllFields: false                # Quote all fields (default: false)
    
    # Error handling
    continueOnError: false               # Continue on write errors
    maxErrorCount: 100                   # Maximum error count
    
    # Performance configuration
    enableCompression: false             # Enable file compression
    compressionLevel: 6                  # Compression level (1-9)
```

## 📋 Schema Configuration

### Field Types

```yaml
outputSchema:
  fields:
    # Basic types
    - name: string_field
      type: string
      required: true
      maxLength: 100
      
    - name: integer_field
      type: integer
      required: true
      minimum: 0
      maximum: 1000000
      
    - name: long_field
      type: long
      required: false
      
    - name: decimal_field
      type: decimal
      required: true
      precision: 10
      scale: 2
      
    - name: double_field
      type: double
      required: false
      
    - name: boolean_field
      type: boolean
      required: true
      
    # Date/time types
    - name: datetime_field
      type: datetime
      required: true
      format: "yyyy-MM-dd HH:mm:ss"
      
    - name: date_field
      type: date
      required: false
      format: "yyyy-MM-dd"
      
    - name: time_field
      type: time
      required: false
      format: "HH:mm:ss"
```

### Schema Validation

```yaml
outputSchema:
  strict: true                          # Strict schema validation
  allowExtraFields: false               # Allow extra fields in data
  validateFieldTypes: true              # Validate field types
  convertTypes: true                    # Auto-convert compatible types
  
  fields:
    - name: customer_id
      type: integer
      required: true
      nullable: false
      
    - name: email
      type: string
      required: false
      nullable: true
      pattern: "^[^@]+@[^@]+\.[^@]+$"   # Email validation pattern
      
    - name: amount
      type: decimal
      required: true
      nullable: false
      minimum: 0
      maximum: 1000000
      
    - name: status
      type: string
      required: true
      nullable: false
      enum: ["active", "inactive", "pending"]
```

## 🔗 Connections and Routing

### Basic Connections

```yaml
connections:
  # Simple connection
  - from: source
    to: transform
    
  # Connection with filtering
  - from: transform
    to: sink
    filter:
      field: "status"
      value: "active"
      
  # Connection with transformation
  - from: transform
    to: sink
    transform:
      - field: "amount"
        operation: "multiply"
        value: 1.1
```

### Routing Configuration

```yaml
routing:
  # Basic routing target
  error_queue:
    type: delimited-sink
    config:
      filePath: "output/errors.csv"
      includeHeaders: true
      
  # Conditional routing
  high_value_queue:
    type: delimited-sink
    config:
      filePath: "output/high_value.csv"
      includeHeaders: true
    condition:
      field: "amount"
      operator: ">"
      value: 10000
      
  # Multiple routing targets
  audit_queue:
    type: delimited-sink
    config:
      filePath: "output/audit.csv"
      includeHeaders: true
    mode: "copy"                        # copy | move (default: move)
```

## 🔧 Advanced Configuration

### Performance Tuning

```yaml
settings:
  # Memory optimization
  maxMemoryMB: 8192
  memoryWarningThreshold: 85
  enableMemoryPressureHandling: true
  gcSettings:
    mode: "server"                      # server | workstation
    concurrent: true
    
  # Processing optimization
  chunkSize: 10000                      # Larger chunks for better throughput
  parallelism: 16                       # Match CPU cores
  processingMode: "parallel"            # parallel | sequential
  
  # I/O optimization
  ioThreads: 8                          # I/O thread pool size
  bufferSize: 262144                    # 256KB buffers
  enableIOCompression: true
  
  # Monitoring optimization
  enableDetailedMetrics: false          # Disable for production
  metricsInterval: 60                   # Reduce frequency
```

### Error Handling

```yaml
settings:
  # Error handling strategy
  errorHandling:
    strategy: "continue"                # continue | stop | retry
    maxRetries: 3
    retryDelay: 1000                    # Retry delay (ms)
    
  # Error routing
  errorRouting:
    enabled: true
    target: "error_queue"
    includeOriginalData: true
    includeErrorDetails: true
    
  # Error thresholds
  errorThresholds:
    maxErrorRate: 0.05                  # 5% error rate
    maxErrorCount: 1000
    timeWindow: 300                     # 5 minutes
```

### Monitoring and Logging

```yaml
settings:
  # Monitoring configuration
  monitoring:
    enabled: true
    interval: 30                        # Monitoring interval (seconds)
    includeSystemMetrics: true
    includeCustomMetrics: true
    
    # Performance metrics
    trackThroughput: true
    trackLatency: true
    trackMemoryUsage: true
    trackErrorRates: true
    
  # Logging configuration
  logging:
    level: "Information"                # Debug | Information | Warning | Error
    includeTimestamp: true
    includeThreadId: false
    includeScopes: true
    
    # Structured logging
    enableStructuredLogging: true
    logFormat: "json"                   # json | text
    
    # Log destinations
    destinations:
      - type: "console"
        enabled: true
        level: "Information"
      - type: "file"
        enabled: true
        level: "Warning"
        path: "logs/flowengine.log"
        maxSize: "100MB"
        maxFiles: 10
```

## 🔒 Security Configuration

### Authentication

```yaml
settings:
  security:
    authentication:
      enabled: true
      provider: "jwt"                   # jwt | apikey | oauth2
      
      jwt:
        issuer: "flowengine"
        audience: "api"
        secretKey: "${JWT_SECRET_KEY}"
        expiration: 3600                # 1 hour
        
    authorization:
      enabled: true
      defaultPolicy: "authenticated"
      
      policies:
        - name: "admin"
          requirements:
            - role: "admin"
        - name: "user"
          requirements:
            - role: "user"
```

### Data Protection

```yaml
settings:
  security:
    dataProtection:
      enabled: true
      
      # Field-level encryption
      encryptedFields:
        - "customer_id"
        - "email"
        - "phone"
        
      # PII handling
      piiHandling:
        enabled: true
        maskFields:
          - field: "email"
            strategy: "partial"          # partial | full | hash
          - field: "phone"
            strategy: "hash"
            
      # Audit logging
      auditLogging:
        enabled: true
        includeDataAccess: true
        includeDataModification: true
        destination: "audit_log"
```

## 🌍 Environment-Specific Configuration

### Development Environment

```yaml
# development.yaml
pipeline:
  settings:
    logLevel: "Debug"
    enableVerboseLogging: true
    enableDetailedMetrics: true
    chunkSize: 1000                     # Smaller chunks for debugging
    
  steps:
    - id: source
      type: delimited-source
      config:
        filePath: "test-data/sample.csv"
        chunkSize: 100
        
    - id: sink
      type: delimited-sink
      config:
        filePath: "output/dev-output.csv"
        overwriteExisting: true
```

### Production Environment

```yaml
# production.yaml
pipeline:
  settings:
    logLevel: "Information"
    enableVerboseLogging: false
    enableDetailedMetrics: false
    maxMemoryMB: 16384
    chunkSize: 20000
    parallelism: 32
    
  steps:
    - id: source
      type: delimited-source
      config:
        filePath: "/data/production/input.csv"
        chunkSize: 10000
        bufferSize: 524288              # 512KB
        
    - id: sink
      type: delimited-sink
      config:
        filePath: "/data/production/output.csv"
        bufferSize: 1048576             # 1MB
        flushInterval: 5000
        enableCompression: true
```

## 🔧 Configuration Validation

### Schema Validation

FlowEngine validates all configuration using JSON Schema:

```json
{
  "$schema": "http://json-schema.org/draft-07/schema#",
  "type": "object",
  "properties": {
    "pipeline": {
      "type": "object",
      "properties": {
        "name": {
          "type": "string",
          "minLength": 1
        },
        "version": {
          "type": "string",
          "pattern": "^\\d+\\.\\d+\\.\\d+$"
        },
        "settings": {
          "type": "object",
          "properties": {
            "maxMemoryMB": {
              "type": "integer",
              "minimum": 256,
              "maximum": 65536
            },
            "chunkSize": {
              "type": "integer",
              "minimum": 100,
              "maximum": 100000
            }
          }
        }
      },
      "required": ["name", "steps", "connections"]
    }
  },
  "required": ["pipeline"]
}
```

### Configuration Testing

```bash
# Validate configuration
flowengine config validate pipeline.yaml

# Test configuration with dry run
flowengine config test pipeline.yaml --dry-run

# Generate configuration schema
flowengine config schema > pipeline-schema.json
```

## 🚀 Performance Configuration

### Optimization Settings

```yaml
settings:
  performance:
    # CPU optimization
    cpuOptimization:
      enabled: true
      affinityMask: "0xFFFF"           # CPU affinity mask
      threadPriority: "Normal"         # Thread priority
      
    # Memory optimization
    memoryOptimization:
      enabled: true
      largeObjectHeap: true
      serverGC: true
      concurrentGC: true
      
    # I/O optimization
    ioOptimization:
      enabled: true
      asyncIO: true
      directIO: false
      readAhead: true
      
    # Network optimization (if applicable)
    networkOptimization:
      enabled: true
      tcpNoDelay: true
      socketBuffer: 65536
```

### Monitoring Configuration

```yaml
settings:
  monitoring:
    # Performance counters
    counters:
      - name: "rows_processed"
        type: "counter"
        unit: "rows"
      - name: "processing_time"
        type: "histogram"
        unit: "milliseconds"
      - name: "memory_usage"
        type: "gauge"
        unit: "bytes"
        
    # Alerts
    alerts:
      - name: "high_memory_usage"
        condition: "memory_usage > 80%"
        action: "log_warning"
      - name: "low_throughput"
        condition: "rows_per_second < 1000"
        action: "send_notification"
```

---

This configuration reference provides comprehensive guidance for setting up and tuning FlowEngine pipelines. Use these examples as templates and adjust settings based on your specific requirements and performance targets.