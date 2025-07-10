# Sprint A: Pure Context API Foundation (Week 1)

## 🎯 **Sprint Objective**
Implement Mode 1 JavaScript Transform with pure context API - no templates, just inline scripts with rich context functionality.

## 📋 **Sprint Tasks**

### **Day 1-2: Core Context API Implementation**

#### **Context Service Architecture**
```csharp
// Core service that creates context for JavaScript execution
public class JavaScriptContextService : IJavaScriptContextService
{
    private readonly IArrayRowFactory _arrayRowFactory;
    private readonly IValidationService _validationService;
    private readonly IEnrichmentService _enrichmentService;
    private readonly IOutputService _outputService;
    private readonly IRoutingService _routingService;

    public IJavaScriptContext CreateContext(
        IArrayRow currentRow,
        ISchema inputSchema,
        ISchema outputSchema,
        Dictionary<string, object> metadata)
    {
        return new JavaScriptContext
        {
            Input = new InputContext(currentRow, inputSchema),
            Validate = new ValidationContext(_validationService, currentRow),
            Enrich = new EnrichmentContext(_enrichmentService, currentRow),
            Output = new OutputContext(_outputService, outputSchema),
            Route = new RoutingContext(_routingService, metadata),
            Utils = new UtilityContext()
        };
    }
}
```

#### **JavaScript Context API Implementation**
```javascript
// Available to JavaScript process function
context = {
    // Input subsystem (Mode 1: simple current row access)
    input: {
        current: () => currentRowData,
        schema: () => schemaInfo
    },
    
    // Validation subsystem
    validate: {
        required: (fieldNames) => boolean,
        businessRule: (expression) => boolean,
        custom: (validatorFunction) => boolean
    },
    
    // Output subsystem
    output: {
        setField: (name, value) => void,
        removeField: (name) => void,
        addMetadata: (key, value) => void
    },
    
    // Routing subsystem
    route: {
        send: (queueName) => void,
        sendCopy: (queueName) => void
    },
    
    // Utilities
    utils: {
        now: () => Date,
        uuid: () => string,
        log: (message, level) => void
    }
};
```

**Success Criteria Day 1-2:**
- [ ] Context service creates functional JavaScript context
- [ ] Basic context API (input.current, validate.required, output.setField) working
- [ ] JavaScript can access current row through context
- [ ] Process function returns boolean correctly

### **Day 3: Plugin Integration and Schema Handling**

#### **JavaScriptTransformPlugin Implementation**
```csharp
public class JavaScriptTransformPlugin : PluginBase<JavaScriptTransformConfiguration>
{
    private readonly IJavaScriptContextService _contextService;
    private readonly IScriptEngineService _scriptEngine;

    public override async Task<ProcessResult> ProcessAsync(
        IDataset dataset,
        JavaScriptTransformConfiguration config,
        CancellationToken cancellationToken)
    {
        // Compile script once
        var compiledScript = await _scriptEngine.CompileAsync(config.Script);
        
        var outputChunks = new List<IChunk>();
        
        foreach (var chunk in dataset.Chunks)
        {
            var processedChunk = await ProcessChunkAsync(chunk, compiledScript, config);
            outputChunks.Add(processedChunk);
        }
        
        return ProcessResult.Success(_datasetFactory.CreateDataset(outputSchema, outputChunks));
    }

    private async Task<IChunk> ProcessChunkAsync(IChunk chunk, CompiledScript script, config)
    {
        var outputRows = new List<IArrayRow>();
        
        foreach (var row in chunk.Rows)
        {
            // Create pure context for this row
            var context = _contextService.CreateContext(row, _inputSchema, _outputSchema, metadata);
            
            // Execute pure context function
            var success = await ExecuteScriptAsync(script, context);
            
            if (success && !context.Route.HasRoutingTargets)
            {
                // Create output row from context
                var outputRow = context.Output.BuildOutputRow();
                outputRows.Add(outputRow);
            }
            // Handle routing and failures through context
        }
        
        return _chunkFactory.CreateChunk(_outputSchema, outputRows);
    }
}
```

#### **Configuration Schema (Mode 1)**
```yaml
transform:
  type: javascript-transform
  config:
    # Mode 1: Inline script only
    script: |
      function process(context) {
        const row = context.input.current();
        
        // Validation
        if (!context.validate.required(['customer_id', 'amount'])) {
          return false;
        }
        
        // Business logic
        if (row.amount > 10000) {
          context.route.send('high_value_review');
          return true;
        }
        
        // Enrichment
        context.output.setField('customer_id', row.customer_id);
        context.output.setField('amount', row.amount);
        context.output.setField('processed_at', context.utils.now());
        context.output.setField('risk_level', row.amount > 1000 ? 'medium' : 'low');
        
        return true;
      }
      
    # Engine configuration
    engine:
      timeout: 5000
      memoryLimit: 10485760
      
    # Schema handling
    outputSchema:
      inferFromScript: false  # Mode 1: explicit schema required
      fields:
        - name: customer_id
          type: integer
        - name: amount
          type: decimal
        - name: processed_at
          type: datetime
        - name: risk_level
          type: string
```

**Success Criteria Day 3:**
- [ ] Complete JavaScriptTransformPlugin working with five-component architecture
- [ ] Configuration binding working with inline scripts
- [ ] Output schema correctly defined and enforced
- [ ] Plugin processes real data through context API

### **Day 4: Routing and Advanced Context Features**

#### **Routing Implementation**
```csharp
public class RoutingContext : IRoutingContext
{
    private readonly List<RoutingTarget> _routingTargets = new();
    
    public void Send(string queueName)
    {
        _routingTargets.Add(new RoutingTarget(queueName, RoutingMode.Move));
    }
    
    public void SendCopy(string queueName)
    {
        _routingTargets.Add(new RoutingTarget(queueName, RoutingMode.Copy));
    }
    
    public bool HasRoutingTargets => _routingTargets.Any();
    public IReadOnlyList<RoutingTarget> Targets => _routingTargets.AsReadOnly();
}
```

#### **Advanced Context Features**
```javascript
// Enhanced context API for Day 4
function process(context) {
    const row = context.input.current();
    
    // Advanced validation
    if (!context.validate.businessRule('amount > 0 && amount < 1000000')) {
        context.utils.log('Invalid amount: ' + row.amount, 'ERROR');
        return false;
    }
    
    // Conditional routing
    if (row.customer_tier === 'platinum') {
        context.route.sendCopy('vip_processing');  // Copy to VIP queue
        // Continue main processing
    }
    
    // Complex enrichment
    const riskScore = calculateRiskScore(row);
    context.output.setField('risk_score', riskScore);
    context.output.addMetadata('processing_version', '1.0');
    
    return true;
}
```

**Success Criteria Day 4:**
- [ ] Routing system working (send, sendCopy)
- [ ] Advanced validation (businessRule, custom validators)
- [ ] Metadata handling in output
- [ ] Logging integration through context.utils.log

### **Day 5: Performance Testing and Integration**

#### **Performance Validation**
- [ ] Benchmark Mode 1 JavaScript Transform against 150K rows/sec target
- [ ] Memory usage testing with chunked processing
- [ ] Error handling and recovery testing
- [ ] Integration with existing DelimitedSource → JavaScriptTransform → DelimitedSink pipeline

#### **End-to-End Testing**
```yaml
# Test pipeline: CSV → JavaScript Transform → CSV
pipeline:
  steps:
    - id: source
      type: delimited-source
      config:
        filePath: "test-data.csv"
        hasHeaders: true
        
    - id: transform
      type: javascript-transform
      config:
        script: |
          function process(context) {
            const row = context.input.current();
            
            // Simple transformation
            context.output.setField('customer_id', row.id);
            context.output.setField('full_name', row.first_name + ' ' + row.last_name);
            context.output.setField('processed_at', context.utils.now());
            
            return true;
          }
        outputSchema:
          fields:
            - {name: customer_id, type: integer}
            - {name: full_name, type: string}
            - {name: processed_at, type: datetime}
            
    - id: sink
      type: delimited-sink
      config:
        filePath: "output.csv"
        
  connections:
    - from: source
      to: transform
    - from: transform
      to: sink
```

**Success Criteria Day 5:**
- [ ] Complete end-to-end pipeline working
- [ ] Performance baseline established
- [ ] Error scenarios handled gracefully
- [ ] Ready for Mode 2 template implementation

## 🎯 **Sprint Success Criteria**

### **Functional Requirements**
- [ ] JavaScript Transform plugin processes real data using pure context API
- [ ] Process function receives only context parameter (no row parameter)
- [ ] Context API provides all necessary data access and manipulation
- [ ] Boolean return value correctly indicates success/failure
- [ ] Routing system works for conditional data flow

### **Performance Requirements**
- [ ] 120K+ rows/sec processing (good baseline for Mode 1)
- [ ] Memory usage bounded and predictable
- [ ] Script compilation and caching working efficiently

### **Quality Requirements**
- [ ] 100% test coverage on context API
- [ ] Error messages clear and actionable
- [ ] Integration with existing plugin ecosystem seamless

## 🔄 **Post-Sprint A: Ready for Mode 2**

After Sprint A completion, you'll have:
- ✅ **Proven Context Architecture**: Pure context API validated
- ✅ **Performance Baseline**: Real-world performance characteristics
- ✅ **User Validation**: Context API usability confirmed
- ✅ **Foundation for Templates**: All building blocks for Mode 2 ready

**Sprint B** can then focus on template system without architectural risk!