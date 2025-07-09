# JavaScript Transform Plugin

**Pure Context API for Data Processing**

The JavaScript Transform plugin provides a powerful, flexible way to process data using JavaScript with a clean, pure context API. It leverages FlowEngine's Core services for high-performance script execution while maintaining the thin plugin architecture.

## 🎯 Overview

### Pure Context API Design

The JavaScript Transform plugin implements a **pure context API** approach where JavaScript functions receive only a context object, not raw data. This design provides:

- **Type Safety**: Schema-aware field access and validation
- **Clean API**: Intuitive, discoverable interface
- **Performance**: Optimized execution with engine pooling
- **Flexibility**: Support for routing, validation, and enrichment

### Architecture

```
┌─────────────────────────────────────────────────────────────────────┐
│                        FlowEngine.Core                              │
│                                                                     │
│  ┌─────────────────────┐  ┌─────────────────────────────────────┐  │
│  │ ScriptEngineService │  │ JavaScriptContextService           │  │
│  │                     │  │                                     │  │
│  │ • Jint Integration  │  │ • Context Creation                  │  │
│  │ • Engine Pooling    │  │ • Input/Output Binding             │  │
│  │ • Script Caching    │  │ • Validation Integration           │  │
│  │ • Performance Opt   │  │ • Routing Support                  │  │
│  └─────────────────────┘  └─────────────────────────────────────┘  │
└─────────────────────────────────────────────────────────────────────┘
           ▲                                 ▲
           │                                 │
   ┌───────────────────────────────────────────────────────────────────┐
   │              JavaScriptTransformPlugin                            │
   │                                                                   │
   │  • Configuration Management                                       │
   │  • Core Service Orchestration                                     │
   │  • Data Flow Coordination                                         │
   │  • Result Processing                                              │
   └───────────────────────────────────────────────────────────────────┘
```

## 🚀 Quick Start

### Basic JavaScript Transform

```yaml
- id: transform
  type: javascript-transform
  config:
    script: |
      function process(context) {
        // Access current row data
        const current = context.input.current();
        
        // Validate required fields
        if (!context.validate.required(['customer_id', 'amount'])) {
          return false; // Skip invalid rows
        }
        
        // Transform data
        context.output.setField('customer_id', current.customer_id);
        context.output.setField('full_name', current.first_name + ' ' + current.last_name);
        context.output.setField('amount', current.amount);
        context.output.setField('processed_at', context.utils.now());
        
        return true; // Include row in output
      }
    
    outputSchema:
      fields:
        - {name: customer_id, type: integer, required: true}
        - {name: full_name, type: string, required: true}
        - {name: amount, type: decimal, required: true}
        - {name: processed_at, type: datetime, required: true}
```

### Advanced Example with Routing

```yaml
- id: transform
  type: javascript-transform
  config:
    script: |
      function process(context) {
        const current = context.input.current();
        
        // Business rule validation
        if (!context.validate.required(['customer_id', 'amount', 'email'])) {
          return false;
        }
        
        // Calculate risk score
        const amount = parseFloat(current.amount) || 0;
        let riskScore = 'low';
        let category = 'standard';
        
        if (amount > 10000) {
          riskScore = 'high';
          category = 'premium';
          // Route high-value customers for review
          context.route.sendCopy('high_value_review');
        } else if (amount > 1000) {
          riskScore = 'medium';
          category = 'plus';
        }
        
        // Data enrichment
        context.output.setField('customer_id', current.customer_id);
        context.output.setField('full_name', current.first_name + ' ' + current.last_name);
        context.output.setField('email', current.email);
        context.output.setField('amount', amount);
        context.output.setField('risk_score', riskScore);
        context.output.setField('category', category);
        context.output.setField('processing_fee', amount * 0.025);
        context.output.setField('processed_at', context.utils.now());
        context.output.setField('batch_id', context.utils.newGuid());
        
        return true;
      }
    
    outputSchema:
      fields:
        - {name: customer_id, type: integer, required: true}
        - {name: full_name, type: string, required: true}
        - {name: email, type: string, required: false}
        - {name: amount, type: decimal, required: false}
        - {name: risk_score, type: string, required: false}
        - {name: category, type: string, required: false}
        - {name: processing_fee, type: decimal, required: false}
        - {name: processed_at, type: datetime, required: false}
        - {name: batch_id, type: string, required: false}
```

## 📋 JavaScript Context API

### Context Object Structure

The JavaScript `process` function receives a single `context` parameter with five subsystems:

```javascript
function process(context) {
  // Input subsystem - access current row data
  const current = context.input.current();
  
  // Validation subsystem - validate data and business rules
  if (!context.validate.required(['field1', 'field2'])) {
    return false;
  }
  
  // Output subsystem - set output fields
  context.output.setField('result', 'value');
  
  // Routing subsystem - conditional data flow
  context.route.sendCopy('queue_name');
  
  // Utility subsystem - common operations
  const timestamp = context.utils.now();
  
  return true; // Include row in output
}
```

### 1. Input Context (`context.input`)

Access current row data and schema information:

```javascript
// Get current row as JavaScript object
const current = context.input.current();

// Access fields directly
const customerId = current.customer_id;
const name = current.first_name;
const email = current.email;

// Check if field exists
if (current.hasOwnProperty('optional_field')) {
  // Handle optional field
}
```

**Available Methods**:
- `current()`: Returns current row data as JavaScript object
- `schema()`: Returns schema information

### 2. Validation Context (`context.validate`)

Validate data and business rules:

```javascript
// Validate required fields
if (!context.validate.required(['customer_id', 'amount'])) {
  return false; // Skip invalid rows
}

// Business rule validation
if (!context.validate.businessRule('amount > 0 && amount < 1000000')) {
  context.route.send('validation_errors');
  return false;
}

// Custom validation function
const isValidEmail = (email) => {
  return /^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(email);
};

if (!context.validate.custom(() => isValidEmail(current.email))) {
  return false;
}
```

**Available Methods**:
- `required(fieldNames)`: Validate required fields exist and are not null
- `businessRule(expression)`: Validate business rule expression
- `custom(validatorFunction)`: Custom validation function

### 3. Output Context (`context.output`)

Set output fields and metadata:

```javascript
// Set output fields
context.output.setField('customer_id', current.customer_id);
context.output.setField('full_name', current.first_name + ' ' + current.last_name);
context.output.setField('processed_at', context.utils.now());

// Set computed fields
const riskScore = current.amount > 10000 ? 'high' : 'low';
context.output.setField('risk_score', riskScore);

// Add metadata
context.output.addMetadata('processor_version', '1.0');
context.output.addMetadata('processing_time', new Date().toISOString());
```

**Available Methods**:
- `setField(name, value)`: Set output field value
- `removeField(name)`: Remove field from output
- `addMetadata(key, value)`: Add metadata to output

### 4. Routing Context (`context.route`)

Conditional data flow and routing:

```javascript
// Send row to different queue (removes from main output)
if (current.amount > 50000) {
  context.route.send('high_value_transactions');
  return true; // Don't include in main output
}

// Send copy to queue (keeps in main output)
if (current.customer_tier === 'premium') {
  context.route.sendCopy('premium_processing');
  // Continue with main processing
}

// Multiple routing targets
if (current.risk_score === 'high') {
  context.route.sendCopy('risk_analysis');
  context.route.sendCopy('compliance_review');
}
```

**Available Methods**:
- `send(queueName)`: Send row to queue (removes from main output)
- `sendCopy(queueName)`: Send copy to queue (keeps in main output)

### 5. Utility Context (`context.utils`)

Common utility functions:

```javascript
// Current timestamp
const now = context.utils.now();
context.output.setField('processed_at', now);

// Generate UUID
const id = context.utils.newGuid();
context.output.setField('batch_id', id);

// Logging
context.utils.log('Processing customer: ' + current.customer_id, 'INFO');
context.utils.log('Validation failed for row', 'ERROR');

// Date formatting
const formattedDate = context.utils.formatDate(new Date(), 'yyyy-MM-dd');
```

**Available Methods**:
- `now()`: Current timestamp
- `newGuid()`: Generate UUID
- `log(message, level)`: Log message with level
- `formatDate(date, format)`: Format date string

## ⚙️ Configuration Reference

### Complete Configuration Schema

```yaml
- id: transform
  type: javascript-transform
  config:
    # JavaScript code (required)
    script: |
      function process(context) {
        // Your transformation logic here
        return true;
      }
    
    # Engine configuration (optional)
    engine:
      timeout: 5000                # Script execution timeout (ms)
      memoryLimit: 10485760        # Memory limit (bytes)
      enableCaching: true          # Script compilation caching
      enableDebugging: false       # Debug mode
    
    # Performance configuration (optional)
    performance:
      enableMonitoring: true       # Performance tracking
      targetThroughput: 120000     # Target rows/sec
      logPerformanceWarnings: true # Log performance issues
    
    # Output schema (required)
    outputSchema:
      fields:
        - name: field_name
          type: string|integer|decimal|double|boolean|datetime|date|time|long
          required: true|false
```

### Engine Configuration

```yaml
engine:
  timeout: 5000                    # Maximum script execution time (ms)
  memoryLimit: 10485760           # Memory limit (10MB in bytes)
  enableCaching: true             # Cache compiled scripts
  enableDebugging: false          # Enable debug features
```

### Performance Configuration

```yaml
performance:
  enableMonitoring: true          # Track performance metrics
  targetThroughput: 120000        # Target rows/sec
  logPerformanceWarnings: true    # Log performance issues
```

### Output Schema Configuration

```yaml
outputSchema:
  fields:
    - name: customer_id
      type: integer
      required: true
    - name: full_name
      type: string
      required: true
    - name: amount
      type: decimal
      required: false
    - name: processed_at
      type: datetime
      required: true
```

**Supported Data Types**:
- `string`: Text data
- `integer`: 32-bit integers
- `long`: 64-bit integers
- `decimal`: Decimal numbers
- `double`: Double-precision floating point
- `boolean`: True/false values
- `datetime`: Date and time
- `date`: Date only
- `time`: Time only

## 🚀 Performance Characteristics

### Performance Metrics

Based on comprehensive testing:

| Metric | Value | Notes |
|--------|-------|-------|
| **Simple Transform** | 7.5K rows/sec | Basic field mapping |
| **Complex Transform** | 7.8K rows/sec | Business logic + routing |
| **High Volume** | 13K rows/sec | Streamlined processing |
| **Memory Usage** | 5-16MB | Depends on complexity |
| **Script Compilation** | Cached | Compile-once, execute-many |

### Performance Optimization

**Engine Pooling**: JavaScript engines are pooled for reuse:
```csharp
// Core service handles engine pooling
private readonly ObjectPool<Engine> _enginePool;
```

**Script Caching**: Compiled scripts are cached:
```csharp
// Scripts compiled once and reused
private readonly ConcurrentDictionary<string, CompiledScript> _scriptCache;
```

**Chunk Processing**: Data processed in efficient chunks:
```csharp
// Process data in configurable chunks
const int chunkSize = 5000; // Configurable
```

## 🔧 Advanced Features

### Complex Business Logic

```javascript
function process(context) {
  const current = context.input.current();
  
  // Complex validation rules
  if (!validateCustomer(current)) {
    context.route.send('validation_failures');
    return false;
  }
  
  // Multi-step processing
  const enrichedData = enrichCustomerData(current);
  const riskAssessment = calculateRisk(enrichedData);
  const recommendations = generateRecommendations(riskAssessment);
  
  // Conditional routing based on results
  if (riskAssessment.level === 'high') {
    context.route.sendCopy('high_risk_review');
  }
  
  if (recommendations.length > 0) {
    context.route.sendCopy('recommendation_queue');
  }
  
  // Set output fields
  context.output.setField('customer_id', current.customer_id);
  context.output.setField('risk_level', riskAssessment.level);
  context.output.setField('risk_score', riskAssessment.score);
  context.output.setField('recommendations', recommendations.join(', '));
  
  return true;
}

// Helper functions
function validateCustomer(customer) {
  return customer.customer_id && 
         customer.email && 
         customer.amount > 0;
}

function enrichCustomerData(customer) {
  // Data enrichment logic
  return {
    ...customer,
    processed_at: new Date().toISOString()
  };
}

function calculateRisk(data) {
  const amount = parseFloat(data.amount) || 0;
  if (amount > 50000) return { level: 'high', score: 0.8 };
  if (amount > 10000) return { level: 'medium', score: 0.5 };
  return { level: 'low', score: 0.2 };
}

function generateRecommendations(risk) {
  const recommendations = [];
  if (risk.level === 'high') {
    recommendations.push('Manual review required');
    recommendations.push('Additional documentation needed');
  }
  return recommendations;
}
```

### Error Handling

```javascript
function process(context) {
  try {
    const current = context.input.current();
    
    // Safe field access
    const amount = parseFloat(current.amount) || 0;
    const email = current.email || '';
    
    // Validation with error handling
    if (!context.validate.required(['customer_id'])) {
      context.utils.log('Missing required field: customer_id', 'ERROR');
      return false;
    }
    
    // Business logic with error handling
    if (amount <= 0) {
      context.utils.log('Invalid amount: ' + amount, 'WARN');
      context.route.send('data_quality_issues');
      return false;
    }
    
    // Safe processing
    context.output.setField('customer_id', current.customer_id);
    context.output.setField('validated_amount', amount);
    context.output.setField('processed_at', context.utils.now());
    
    return true;
    
  } catch (error) {
    context.utils.log('Processing error: ' + error.message, 'ERROR');
    context.route.send('processing_errors');
    return false;
  }
}
```

## 🧪 Testing

### Unit Testing JavaScript Logic

```csharp
[Fact]
public async Task JavaScriptTransform_SimpleTransform_ShouldProcessData()
{
    // Arrange
    var config = new JavaScriptTransformConfiguration
    {
        Script = @"
            function process(context) {
                const current = context.input.current();
                context.output.setField('result', current.input_field.toUpperCase());
                return true;
            }",
        OutputSchema = new OutputSchemaConfiguration
        {
            Fields = new List<OutputFieldDefinition>
            {
                new() { Name = "result", Type = "string", Required = true }
            }
        }
    };
    
    var plugin = CreatePlugin();
    await plugin.InitializeAsync(config);
    
    // Act
    var result = await plugin.TransformAsync(inputData);
    
    // Assert
    Assert.True(result.Success);
    Assert.Equal("EXPECTED_VALUE", result.OutputData.Rows[0]["result"]);
}
```

### Performance Testing

```csharp
[Fact]
public async Task JavaScriptTransform_PerformanceTest_ShouldMeetTargets()
{
    const int rowCount = 20000;
    
    var stopwatch = Stopwatch.StartNew();
    var result = await plugin.TransformAsync(CreateTestData(rowCount));
    stopwatch.Stop();
    
    var throughput = rowCount / stopwatch.Elapsed.TotalSeconds;
    
    Assert.True(throughput >= 5000, $"Performance below minimum: {throughput:F0} rows/sec");
}
```

## 📊 Monitoring and Diagnostics

### Performance Monitoring

```yaml
# Enable performance monitoring
performance:
  enableMonitoring: true
  targetThroughput: 120000
  logPerformanceWarnings: true
```

### Performance Logs

```
warn: JavaScriptTransform.JavaScriptTransformPlugin[0]
      Performance below target: 7521 rows/sec < 120000 rows/sec

info: JavaScriptTransform.JavaScriptTransformPlugin[0]
      JavaScript Transform plugin stopped. Processed 20000 rows in 2660ms. Throughput: 7524 rows/sec
```

### Health Checks

The plugin provides comprehensive health checks:

```csharp
// Built-in health monitoring
public async Task<IEnumerable<HealthCheck>> PerformAdditionalHealthChecksAsync()
{
    // Performance health
    if (currentThroughput < targetThroughput * 0.8)
    {
        return new HealthCheck
        {
            Name = "Performance",
            Passed = false,
            Details = $"Performance below 80% of target: {currentThroughput:F0} rows/sec"
        };
    }
    
    // Memory health
    if (memoryUsage > memoryLimit * 0.9)
    {
        return new HealthCheck
        {
            Name = "Memory Usage",
            Passed = false,
            Details = $"Memory usage near limit: {memoryUsage / 1024 / 1024:F1}MB"
        };
    }
}
```

## 🔄 Integration Patterns

### Pipeline Integration

```yaml
pipeline:
  name: "CustomerProcessing"
  
  steps:
    - id: source
      type: delimited-source
      config:
        filePath: "customers.csv"
        hasHeaders: true
        
    - id: transform
      type: javascript-transform
      config:
        script: |
          function process(context) {
            // Your transformation logic
            return true;
          }
        outputSchema:
          fields:
            - {name: customer_id, type: integer}
            - {name: processed_data, type: string}
            
    - id: sink
      type: delimited-sink
      config:
        filePath: "processed_customers.csv"
        
  connections:
    - from: source
      to: transform
    - from: transform
      to: sink
```

### Routing Integration

```yaml
# Define routing targets
routing:
  high_value_review:
    type: delimited-sink
    config:
      filePath: "high_value_customers.csv"
      
  validation_errors:
    type: delimited-sink
    config:
      filePath: "validation_errors.csv"
```

## 🎯 Best Practices

### JavaScript Development

1. **Keep Functions Small**: Break complex logic into helper functions
2. **Handle Errors Gracefully**: Use try-catch and validate data
3. **Use Descriptive Names**: Clear variable and function names
4. **Validate Early**: Check requirements before processing
5. **Log Appropriately**: Use context.utils.log for debugging

### Performance Optimization

1. **Minimize Object Creation**: Reuse objects where possible
2. **Avoid Complex Regex**: Use simple string operations
3. **Cache Calculations**: Store expensive computations
4. **Use Efficient Algorithms**: Choose optimal approaches
5. **Monitor Performance**: Track metrics and optimize bottlenecks

### Error Handling

1. **Validate Input Data**: Check required fields and data types
2. **Handle Missing Data**: Provide defaults or skip processing
3. **Log Errors**: Use structured logging for debugging
4. **Route Errors**: Send problematic data to error queues
5. **Fail Gracefully**: Return false for rows that can't be processed

---

The JavaScript Transform plugin provides a powerful, flexible way to process data with high performance and clean architecture. The pure context API makes it easy to write maintainable transformation logic while the Core services ensure optimal performance.