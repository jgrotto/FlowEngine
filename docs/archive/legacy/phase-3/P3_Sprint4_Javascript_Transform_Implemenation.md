# JavaScript Transform: Pure Context-Driven Workflow Engine

## 🎯 Executive Summary

**Revolutionary Vision**: JavaScript Transform with **pure context API** enables complete workflow orchestration through a single, consistent interface. The process function receives **only context** - all data access, manipulation, and routing flows through the context API. This enables **multi-source processing, dynamic data access, and template-driven input/output definitions**.

## 🧠 **The Pure Context Architecture**

### **Ultimate Process Function Simplicity**
```javascript
/**
 * Pure Context Process Function:
 * @param {object} context - The ONLY parameter - all data and operations through context
 * @returns {boolean} - true = success, false = failure
 */
function process(context) {
    // All data access through context
    const row = context.input.current();
    const customerData = context.input.lookup('customer_master', row.customer_id);
    
    // All validation through context
    if (!context.validate.required(['customer_id', 'transaction_amount'])) {
        return false;
    }
    
    // All business logic through context
    const riskScore = context.enrich.calculate('risk_score', {
        amount: row.transaction_amount,
        history: customerData.transaction_history,
        location: context.input.external('geo_service', row.ip_address)
    });
    
    // All routing through context
    if (riskScore > 0.8) {
        context.route.send('high_risk_review');
        return true; // Success, but routed elsewhere
    }
    
    // All output through context
    context.output.setField('risk_score', riskScore);
    context.output.setField('processed_at', context.utils.now());
    
    return true;
}
```

### **Rich Context API for All Data Operations**
```javascript
context = {
    // === INPUT SUBSYSTEM (Multiple Sources) ===
    input: {
        current: () => row,                           // Current row being processed
        previous: () => row,                          // Previous row (for delta processing)
        lookup: (source, key) => data,               // Lookup from configured sources
        external: (service, params) => data,         // External API/service calls
        batch: (count) => [rows],                    // Batch processing helper
        aggregate: (operation, field) => value       // Aggregate operations
    },
    
    // === VALIDATION SUBSYSTEM ===
    validate: {
        required: (fieldNames) => boolean,
        businessRule: (expression) => boolean,
        crossField: (validationFunction) => boolean,
        external: (service, data) => boolean
    },
    
    // === ENRICHMENT SUBSYSTEM ===
    enrich: {
        setCalculated: (fieldName, value) => void,
        calculate: (formula, params) => value,
        lookup: (table, key, returnField) => value,
        transform: (fieldName, transformFunction) => void
    },
    
    // === OUTPUT SUBSYSTEM (Multiple Targets) ===
    output: {
        setField: (name, value) => void,
        removeField: (name) => void,
        addRow: (data) => void,                       // Create additional output rows
        addToDataset: (datasetName, data) => void,   // Send to named output dataset
        transformField: (name, transformer) => void
    },
    
    // === ROUTING SUBSYSTEM ===
    route: {
        send: (queueName) => void,
        sendCopy: (queueName) => void,               // Send copy, continue main flow
        split: (conditions) => void,                 // Split to multiple queues
        batch: (queueName, batchSize) => void       // Batch routing
    },
    
    // === METADATA & UTILITIES ===
    meta: {
        schema: () => schemaInfo,                     // Current schema information
        jobId: () => string,                         // Current job execution ID
        stepId: () => string,                        // Current step ID
        correlationId: () => string                  // Correlation tracking
    },
    
    utils: {
        now: () => Date,
        uuid: () => string,
        hash: (value) => string,
        featureFlag: (flagName) => boolean
    }
};
```

## 🎨 **Template-Driven Input/Output Definition**

### **Templates Define Complete Data Flow**
```yaml
# Template defines everything - inputs, outputs, processing logic
template: "multi-source-customer-enrichment"
metadata:
  description: "Enrich customer data from multiple sources"
  version: "2.1"
  author: "data-team"
  
# Template declares its data requirements
input_sources:
  primary:
    type: "stream"
    schema:
      - name: "customer_id"
        type: "integer"
        required: true
      - name: "transaction_amount"
        type: "decimal"
        required: true
        
  lookup_sources:
    customer_master:
      type: "database"
      connection: "${CUSTOMER_DB}"
      query: "SELECT * FROM customers WHERE id = ?"
      key_field: "customer_id"
      
    geo_service:
      type: "api"
      endpoint: "${GEO_API}/location"
      method: "POST"
      cache_ttl: 3600
      
# Template declares its outputs
output_targets:
  main:
    schema:
      - name: "customer_id"
        type: "integer"
      - name: "risk_score"
        type: "decimal"
      - name: "processed_at"
        type: "datetime"
        
  routing_targets:
    high_risk_review:
      condition: "risk_score > 0.8"
      schema:
        - name: "customer_id"
          type: "integer"
        - name: "risk_score"
          type: "decimal"
        - name: "alert_reason"
          type: "string"

# The JavaScript logic (now pure context)
script: |
  function process(context) {
    // Access current row from primary input
    const row = context.input.current();
    
    // Access lookup data
    const customer = context.input.lookup('customer_master', row.customer_id);
    const geoData = context.input.external('geo_service', { ip: row.ip_address });
    
    // Business logic
    const riskScore = calculateRisk(row, customer, geoData);
    
    if (riskScore > 0.8) {
      context.output.addToDataset('high_risk_review', {
        customer_id: row.customer_id,
        risk_score: riskScore,
        alert_reason: 'High transaction amount + location anomaly'
      });
      return true;
    }
    
    // Main output
    context.output.setField('customer_id', row.customer_id);
    context.output.setField('risk_score', riskScore);
    context.output.setField('processed_at', context.utils.now());
    
    return true;
  }
```

### **Advanced Multi-Source Processing**
```yaml
# Template for complex multi-input scenarios
template: "order-fulfillment-orchestration"

input_sources:
  orders:
    type: "stream"
    schema: [/* order schema */]
    
  inventory:
    type: "database"
    refresh_interval: 300  # 5 minutes
    
  shipping_rates:
    type: "api"
    cache_ttl: 1800  # 30 minutes
    
  customer_preferences:
    type: "cache"
    ttl: 3600  # 1 hour

# The process function accesses all sources through context
script: |
  function process(context) {
    const order = context.input.current();
    
    // Check inventory across multiple warehouses
    const inventory = context.input.aggregate('inventory', 'sum_by_location');
    
    // Get dynamic shipping rates
    const shipping = context.input.external('shipping_rates', {
      origin: inventory.best_warehouse,
      destination: order.shipping_address
    });
    
    // Access customer preferences
    const preferences = context.input.lookup('customer_preferences', order.customer_id);
    
    // Orchestrate fulfillment
    if (inventory.available >= order.quantity) {
      context.output.addToDataset('fulfillment_queue', {
        order_id: order.id,
        warehouse: inventory.best_warehouse,
        shipping_method: preferences.preferred_shipping,
        estimated_cost: shipping.cost
      });
    } else {
      context.route.send('backorder_processing');
    }
    
    return true;
  }
```

## 🏗️ **Core Implementation Architecture**

### **Context Factory Service**
```csharp
public class JavaScriptContextFactory : IJavaScriptContextFactory
{
    private readonly IMultiSourceDataService _dataService;
    private readonly IValidationService _validationService;
    private readonly IEnrichmentService _enrichmentService;
    private readonly IRoutingService _routingService;
    private readonly IOutputService _outputService;

    public IJavaScriptContext CreateContext(
        ProcessingState state,
        TemplateDefinition template,
        Dictionary<string, object> metadata)
    {
        return new JavaScriptContext
        {
            Input = new InputContext(_dataService, state, template.InputSources),
            Validate = new ValidationContext(_validationService, state.CurrentRow),
            Enrich = new EnrichmentContext(_enrichmentService, state.CurrentRow),
            Output = new OutputContext(_outputService, template.OutputTargets),
            Route = new RoutingContext(_routingService, template.RoutingTargets),
            Meta = new MetadataContext(state, metadata),
            Utils = new UtilityContext()
        };
    }
}
```

### **Multi-Source Input Context**
```csharp
public class InputContext : IInputContext
{
    private readonly IMultiSourceDataService _dataService;
    private readonly ProcessingState _state;
    private readonly Dictionary<string, InputSourceDefinition> _sources;

    public IArrayRow Current() 
    {
        return _state.CurrentRow;
    }

    public IArrayRow Previous() 
    {
        return _state.PreviousRow;
    }

    public async Task<object> LookupAsync(string sourceName, object key)
    {
        if (!_sources.TryGetValue(sourceName, out var sourceDefinition))
            throw new InvalidOperationException($"Source '{sourceName}' not defined in template");

        return await _dataService.LookupAsync(sourceDefinition, key);
    }

    public async Task<object> ExternalAsync(string serviceName, object parameters)
    {
        if (!_sources.TryGetValue(serviceName, out var serviceDefinition))
            throw new InvalidOperationException($"External service '{serviceName}' not defined");

        return await _dataService.CallExternalServiceAsync(serviceDefinition, parameters);
    }

    public async Task<IEnumerable<IArrayRow>> BatchAsync(int count)
    {
        return await _dataService.GetBatchAsync(_state.InputSource, _state.CurrentPosition, count);
    }
}
```

### **Template-Driven Schema Validation**
```csharp
public class TemplateProcessor : ITemplateProcessor
{
    public async Task<ProcessResult> ProcessAsync(
        IDataChunk inputChunk,
        TemplateDefinition template,
        CancellationToken cancellationToken)
    {
        // Validate input schema matches template requirements
        ValidateInputSchema(inputChunk.Schema, template.InputSources);
        
        // Prepare output schemas based on template definitions
        var outputSchemas = PrepareOutputSchemas(template.OutputTargets);
        
        // Process each row through the template
        var results = new Dictionary<string, List<IArrayRow>>();
        
        foreach (var row in inputChunk.Rows)
        {
            var state = new ProcessingState(row, inputChunk);
            var context = _contextFactory.CreateContext(state, template, metadata);
            
            // Execute the pure context function
            var success = await ExecuteTemplateAsync(template.Script, context);
            
            // Process context results
            if (success)
            {
                ProcessContextOutputs(context, results, outputSchemas);
            }
            else
            {
                HandleProcessingFailure(context, results);
            }
        }
        
        return CreateProcessResult(results, outputSchemas);
    }
}
```

## 🚀 **Advanced Scenarios Enabled**

### **1. Multi-Input Data Correlation**
```javascript
function process(context) {
    const order = context.input.current();
    
    // Access multiple data sources through context
    const customer = context.input.lookup('customer_db', order.customer_id);
    const inventory = context.input.lookup('inventory_cache', order.product_id);
    const pricing = context.input.external('pricing_api', {
        product: order.product_id,
        customer_tier: customer.tier,
        quantity: order.quantity
    });
    
    // Complex business logic with multi-source data
    if (inventory.quantity < order.quantity) {
        context.route.send('backorder_queue');
        context.output.addToDataset('inventory_alerts', {
            product_id: order.product_id,
            requested: order.quantity,
            available: inventory.quantity
        });
        return true;
    }
    
    // Calculate dynamic pricing
    const finalPrice = pricing.base_price * pricing.tier_multiplier * pricing.volume_discount;
    
    context.output.setField('calculated_price', finalPrice);
    context.output.setField('pricing_version', pricing.version);
    
    return true;
}
```

### **2. Batch Processing and Aggregation**
```javascript
function process(context) {
    // Process in batches for performance
    const currentBatch = context.input.batch(100);
    
    // Aggregate operations across batch
    const totalValue = context.input.aggregate('sum', 'transaction_amount');
    const avgRisk = context.input.aggregate('average', 'risk_score');
    
    if (totalValue > 1000000) { // $1M batch threshold
        // Route entire batch for special processing
        context.route.sendBatch('high_value_batch', currentBatch);
        
        // Create batch summary
        context.output.addToDataset('batch_summaries', {
            batch_id: context.meta.batchId(),
            total_value: totalValue,
            average_risk: avgRisk,
            record_count: currentBatch.length,
            processing_time: context.meta.elapsedTime()
        });
    }
    
    return true;
}
```

### **3. Dynamic Schema Evolution**
```javascript
function process(context) {
    const row = context.input.current();
    const schema = context.meta.schema();
    
    // Adapt to schema changes dynamically
    if (schema.hasField('new_field_v2')) {
        // New schema version - use enhanced logic
        const enhancedData = processWithNewLogic(row);
        context.output.setField('processing_version', '2.0');
    } else {
        // Legacy schema - maintain compatibility
        const legacyData = processWithLegacyLogic(row);
        context.output.setField('processing_version', '1.0');
    }
    
    // Schema-aware output routing
    if (schema.version >= '2.0') {
        context.route.send('v2_processing_queue');
    } else {
        context.route.send('legacy_processing_queue');
    }
    
    return true;
}
```

### **4. Real-Time Feature Flag Integration**
```javascript
function process(context) {
    const row = context.input.current();
    
    // Dynamic feature activation through context
    if (context.utils.featureFlag('new_fraud_detection')) {
        const mlScore = context.input.external('ml_service', {
            transaction: row,
            model: 'fraud_detection_v3'
        });
        
        context.output.setField('ml_fraud_score', mlScore);
        
        if (mlScore > 0.85) {
            context.route.send('ai_fraud_review');
            return true;
        }
    }
    
    // Fallback to rule-based detection
    if (row.amount > 10000 && row.merchant_country !== row.card_country) {
        context.route.send('manual_review');
    }
    
    return true;
}
```

## 🚀 **Implementation Roadmap**

### **Phase 1: Context API Foundation (Week 1)**

#### **Core Context Service Implementation**
```csharp
public class JavaScriptContextService : IJavaScriptContextService
{
    private readonly IArrayRowFactory _arrayRowFactory;
    private readonly IDataValidationService _validationService;
    private readonly IRoutingService _routingService;
    private readonly IEnrichmentService _enrichmentService;
    private readonly IAuditService _auditService;

    public JavaScriptContext CreateContext(
        IArrayRow currentRow, 
        ISchema inputSchema, 
        ISchema outputSchema,
        Dictionary<string, object> metadata)
    {
        return new JavaScriptContext
        {
            Validate = new ValidationContext(_validationService, currentRow, inputSchema),
            Enrich = new EnrichmentContext(_enrichmentService, currentRow),
            Route = new RoutingContext(_routingService, metadata),
            Output = new OutputContext(_arrayRowFactory, outputSchema),
            Audit = new AuditContext(_auditService, metadata),
            Utils = new UtilityContext()
        };
    }
}
```

#### **Process Function Integration**
```csharp
public async Task<ProcessResult> ProcessRowAsync(IArrayRow row, string script)
{
    // Create rich context
    var context = _contextService.CreateContext(row, _inputSchema, _outputSchema, metadata);
    
    // Create schema-aware row proxy
    var rowProxy = CreateRowProxy(row, _inputSchema);
    
    // Execute with boolean return contract
    var success = await _scriptEngine.ExecuteAsync<bool>(script, new {
        row = rowProxy,
        context = context
    });
    
    // Process context results
    if (context.Route.HasRoutingTargets)
    {
        return ProcessResult.Routed(context.Route.Targets);
    }
    
    if (!success)
    {
        return ProcessResult.Failed(context.Audit.ErrorMessages);
    }
    
    // Create output row from context
    var outputRow = context.Output.BuildOutputRow();
    return ProcessResult.Success(outputRow);
}
```

### **Phase 2: Template Engine Implementation (Week 2)**

#### **Template Loading and Compilation**
```csharp
public class JavaScriptTemplateEngine : IJavaScriptTemplateEngine
{
    private readonly ConcurrentDictionary<string, CompiledTemplate> _templateCache;
    
    public async Task<CompiledTemplate> LoadTemplateAsync(string templateName, Dictionary<string, object> config)
    {
        var cacheKey = $"{templateName}:{ComputeConfigHash(config)}";
        
        return await _templateCache.GetOrAddAsync(cacheKey, async _ =>
        {
            var template = await LoadTemplateFromStore(templateName);
            var script = await CompileTemplateWithConfig(template, config);
            return new CompiledTemplate(templateName, script, config);
        });
    }
    
    private async Task<string> CompileTemplateWithConfig(Template template, Dictionary<string, object> config)
    {
        // Template parameter substitution
        var compiledScript = template.BaseScript;
        
        // Replace template parameters with actual configuration
        foreach (var parameter in template.Parameters)
        {
            var value = config.GetValueOrDefault(parameter.Name, parameter.DefaultValue);
            compiledScript = compiledScript.Replace($"${{{parameter.Name}}}", JsonSerializer.Serialize(value));
        }
        
        return compiledScript;
    }
}
```

### **Phase 3: Standard Template Library (Week 3-4)**

#### **Template Development Strategy**
1. **Core Templates** (Week 3):
   - `data-validation-standard`
   - `field-mapping-simple`
   - `business-routing-basic`
   - `audit-logging-standard`

2. **Advanced Templates** (Week 4):
   - `customer-lifecycle-processing`
   - `data-quality-complete`
   - `api-integration-standard`
   - `multi-output-routing`

3. **Industry Templates** (Future):
   - `financial-compliance-processing`
   - `healthcare-hipaa-compliant`
   - `retail-customer-segmentation`
   - `manufacturing-quality-control`

## 🎯 **Strategic Benefits**

### **For Less Experienced Users**
- **Template-Driven Workflow**: Complete end-to-end processing with configuration, not coding
- **Progressive Complexity**: Start with simple templates, grow into custom JavaScript
- **Built-in Best Practices**: Templates embody enterprise-grade patterns
- **Reduced Learning Curve**: Focus on business logic, not technical implementation

### **For Advanced Users**
- **Ultimate Flexibility**: Full JavaScript access for complex scenarios
- **Performance**: Specialized transforms only needed for ultimate speed requirements
- **Extensibility**: Create custom templates for organization-specific patterns
- **Integration**: Rich context API enables sophisticated external system integration

### **For Development Team**
- **Reduced Plugin Maintenance**: One flexible plugin vs. dozens of specialized ones
- **Faster Feature Delivery**: New capabilities through templates, not new plugins
- **Consistent Architecture**: All transform logic follows same patterns
- **Performance Optimization**: Focus optimization efforts on one critical plugin

## 🏆 **Success Criteria**

### **Week 1 Goals: Context API Foundation**
- ✅ Rich context API implemented with all subsystems
- ✅ Boolean-returning process function contract working
- ✅ Schema-aware field access in JavaScript
- ✅ Basic routing and validation working

### **Week 2 Goals: Template Engine**
- ✅ Template loading and compilation system
- ✅ Parameter substitution and configuration binding
- ✅ Template caching and performance optimization
- ✅ Error handling and validation for templates

### **Week 3-4 Goals: Standard Templates**
- ✅ 8-10 production-ready templates covering common scenarios
- ✅ Template documentation and examples
- ✅ Template composition and inheritance working
- ✅ Performance validation: 150K+ rows/sec with templates

### **Performance Targets**
- **Mode 1 (Custom Scripts)**: 120-150K rows/sec
- **Mode 2 (Simple Templates)**: 150-180K rows/sec  
- **Mode 2 (Complex Templates)**: 100-130K rows/sec
- **Memory Usage**: Bounded processing with chunking

## 💡 **Key Implementation Insights**

### **1. Context API as Service Gateway**
The context API becomes the **primary interface** between user logic and FlowEngine Core services. This abstraction enables:
- **Consistent Performance**: Optimized service access patterns
- **Rich Functionality**: Access to validation, routing, enrichment without plugin complexity
- **Future Evolution**: New capabilities added through context extensions

### **2. Template-First Development**
Focus on **template quality over quantity**:
- Each template should solve **complete business scenarios**
- Templates should be **extensively tested and documented**
- **Template marketplace potential** for community-contributed workflows

### **3. Progressive Disclosure**
Enable users to **start simple and grow complex**:
- Begin with pre-built templates
- Customize through configuration parameters
- Add custom JavaScript sections as needed
- Graduate to full custom scripts when required

This approach makes FlowEngine accessible to business users while maintaining the power needed for complex enterprise scenarios.