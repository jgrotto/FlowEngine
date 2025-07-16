# Sprint A: Pure Context API Foundation (Week 1) - REVISED

## 🎯 **Sprint Objective**
Implement Mode 1 JavaScript Transform with pure context API - maintaining architectural consistency where Core provides script execution services and the plugin remains thin.

## 🏗️ **Architectural Alignment**

### **Core Responsibilities**
- **IScriptEngineService**: Jint engine pooling, script compilation, caching
- **IJavaScriptContextService**: Context creation and management
- **Script Execution**: Actual JavaScript execution and sandboxing
- **Performance Optimization**: Engine pooling, compilation caching

### **Plugin Responsibilities**
- **Configuration Handling**: Parse and validate JavaScript configuration
- **Context Setup**: Call Core services to create context
- **Data Flow**: Pass chunks to Core for processing
- **Result Handling**: Route outputs based on execution results

## 📋 **Sprint Tasks**

### **Day 1-2: Core Script Engine Service**

#### **Core Service Implementation**
```csharp
// In FlowEngine.Core/Services/
public interface IScriptEngineService
{
    Task<CompiledScript> CompileAsync(string script, ScriptOptions options);
    Task<ScriptResult> ExecuteAsync(CompiledScript script, IJavaScriptContext context);
    void ReturnEngine(Engine engine); // For pooling
}

public class JintScriptEngineService : IScriptEngineService
{
    private readonly ObjectPool<Engine> _enginePool;
    private readonly ConcurrentDictionary<string, CompiledScript> _scriptCache;
    private readonly ILogger<JintScriptEngineService> _logger;
    
    public JintScriptEngineService(
        ILogger<JintScriptEngineService> logger,
        IMemoryManager memoryManager)
    {
        _logger = logger;
        _enginePool = CreateEnginePool();
        _scriptCache = new ConcurrentDictionary<string, CompiledScript>();
    }
    
    private ObjectPool<Engine> CreateEnginePool()
    {
        var policy = new JintEnginePooledObjectPolicy();
        var provider = new DefaultObjectPoolProvider();
        return provider.Create(policy);
    }
}
```

#### **Core Context Service**
```csharp
// In FlowEngine.Core/Services/
public interface IJavaScriptContextService
{
    IJavaScriptContext CreateContext(
        IArrayRow currentRow,
        ISchema inputSchema,
        ISchema outputSchema,
        ProcessingState state);
}

public class JavaScriptContextService : IJavaScriptContextService
{
    private readonly IArrayRowFactory _arrayRowFactory;
    private readonly IValidationService _validationService;
    private readonly IEnrichmentService _enrichmentService;
    
    public IJavaScriptContext CreateContext(
        IArrayRow currentRow,
        ISchema inputSchema,
        ISchema outputSchema,
        ProcessingState state)
    {
        return new JavaScriptContext
        {
            Input = new InputContext(currentRow, inputSchema),
            Output = new OutputContext(outputSchema, _arrayRowFactory),
            Validate = new ValidationContext(_validationService),
            Route = new RoutingContext(state),
            Utils = new UtilityContext()
        };
    }
}
```

**Success Criteria Day 1-2:**
- [ ] Core script engine service with Jint integration
- [ ] Engine pooling for performance
- [ ] Script compilation and caching
- [ ] Context service creating proper contexts

### **Day 3: Thin Plugin Implementation**

#### **JavaScriptTransformPlugin - Thin Wrapper**
```csharp
// In plugins/JavaScriptTransform/
[Plugin("javascript-transform", "1.0.0")]
public class JavaScriptTransformPlugin : PluginBase<JavaScriptTransformConfiguration>
{
    private readonly IScriptEngineService _scriptEngine;
    private readonly IJavaScriptContextService _contextService;
    private readonly IDatasetFactory _datasetFactory;
    private readonly ILogger<JavaScriptTransformPlugin> _logger;
    
    public JavaScriptTransformPlugin(
        IScriptEngineService scriptEngine,
        IJavaScriptContextService contextService,
        IDatasetFactory datasetFactory,
        ILogger<JavaScriptTransformPlugin> logger)
    {
        _scriptEngine = scriptEngine ?? throw new ArgumentNullException(nameof(scriptEngine));
        _contextService = contextService ?? throw new ArgumentNullException(nameof(contextService));
        _datasetFactory = datasetFactory ?? throw new ArgumentNullException(nameof(datasetFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }
    
    public override async Task<ProcessResult> ProcessAsync(
        IDataset dataset,
        CancellationToken cancellationToken)
    {
        // Compile script using Core service
        var compiledScript = await _scriptEngine.CompileAsync(
            Configuration.Script,
            new ScriptOptions 
            { 
                Timeout = TimeSpan.FromMilliseconds(Configuration.Engine.Timeout),
                MemoryLimit = Configuration.Engine.MemoryLimit
            });
        
        var outputChunks = new List<IChunk>();
        var routingResults = new Dictionary<string, List<IArrayRow>>();
        
        foreach (var chunk in dataset.Chunks)
        {
            // Process chunk using Core services
            var result = await ProcessChunkWithCoreServices(
                chunk, 
                compiledScript, 
                dataset.Schema,
                Configuration.OutputSchema,
                routingResults,
                cancellationToken);
                
            if (result.Success)
                outputChunks.Add(result.OutputChunk);
        }
        
        // Create output dataset
        var outputDataset = _datasetFactory.CreateDataset(
            Configuration.OutputSchema,
            outputChunks);
            
        return ProcessResult.Success(outputDataset, routingResults);
    }
    
    private async Task<ChunkResult> ProcessChunkWithCoreServices(
        IChunk chunk,
        CompiledScript script,
        ISchema inputSchema,
        ISchema outputSchema,
        Dictionary<string, List<IArrayRow>> routingResults,
        CancellationToken cancellationToken)
    {
        var outputRows = new List<IArrayRow>();
        
        foreach (var row in chunk.Rows)
        {
            // Create context using Core service
            var context = _contextService.CreateContext(
                row, 
                inputSchema, 
                outputSchema,
                new ProcessingState { RoutingResults = routingResults });
            
            // Execute script using Core service
            var result = await _scriptEngine.ExecuteAsync(script, context);
            
            if (result.Success && result.ReturnValue)
            {
                outputRows.Add(context.Output.GetResultRow());
            }
            
            // Handle routing targets
            foreach (var target in context.Route.GetTargets())
            {
                if (!routingResults.ContainsKey(target.QueueName))
                    routingResults[target.QueueName] = new List<IArrayRow>();
                    
                routingResults[target.QueueName].Add(
                    target.Mode == RoutingMode.Copy ? row.Clone() : row);
            }
        }
        
        return new ChunkResult 
        { 
            Success = true, 
            OutputChunk = _chunkFactory.CreateChunk(outputSchema, outputRows) 
        };
    }
}
```

#### **Configuration Schema**
```yaml
transform:
  type: javascript-transform
  config:
    # Inline script
    script: |
      // Pure context API - no row parameter
      const current = context.input.current();
      
      if (!context.validate.required(['customer_id', 'amount'])) {
        return false;
      }
      
      if (current.amount > 10000) {
        context.route.send('high_value_transactions');
      }
      
      context.output.setField('processed_at', context.utils.now());
      context.output.setField('risk_score', current.amount > 5000 ? 'high' : 'low');
      
      return true;
      
    # Engine configuration
    engine:
      timeout: 5000
      memoryLimit: 10485760
      
    # Output schema
    outputSchema:
      fields:
        - name: customer_id
          type: integer
        - name: amount
          type: decimal
        - name: processed_at
          type: datetime
        - name: risk_score
          type: string
```

**Success Criteria Day 3:**
- [ ] Thin plugin using Core services
- [ ] Configuration properly bound
- [ ] Integration with existing plugin patterns
- [ ] No script engine logic in plugin

### **Day 4: Context API Features**

#### **Enhanced Context Implementation (in Core)**
```csharp
public class JavaScriptContext : IJavaScriptContext
{
    public IInputContext Input { get; set; }
    public IOutputContext Output { get; set; }
    public IValidationContext Validate { get; set; }
    public IRoutingContext Route { get; set; }
    public IUtilityContext Utils { get; set; }
}

// Input context for data access
public class InputContext : IInputContext
{
    private readonly IArrayRow _currentRow;
    private readonly ISchema _schema;
    
    public object Current() => ConvertToJavaScriptObject(_currentRow);
    public object Schema() => ConvertSchemaToJavaScript(_schema);
}

// Output context for modifications
public class OutputContext : IOutputContext
{
    private readonly Dictionary<string, object> _modifications = new();
    private readonly ISchema _outputSchema;
    private readonly IArrayRowFactory _arrayRowFactory;
    
    public void SetField(string name, object value)
    {
        if (!_outputSchema.HasField(name))
            throw new InvalidOperationException($"Field '{name}' not in output schema");
            
        _modifications[name] = value;
    }
    
    public IArrayRow GetResultRow()
    {
        // Create new ArrayRow with modifications applied
        return _arrayRowFactory.CreateRow(_outputSchema, _modifications);
    }
}

// Routing context
public class RoutingContext : IRoutingContext
{
    private readonly List<RoutingTarget> _targets = new();
    
    public void Send(string queueName) => 
        _targets.Add(new RoutingTarget(queueName, RoutingMode.Move));
        
    public void SendCopy(string queueName) => 
        _targets.Add(new RoutingTarget(queueName, RoutingMode.Copy));
        
    public IReadOnlyList<RoutingTarget> GetTargets() => _targets.AsReadOnly();
}
```

**Success Criteria Day 4:**
- [ ] Full context API working
- [ ] Routing system functional
- [ ] Validation subsystem integrated
- [ ] Utility functions available

### **Day 5: End-to-End Testing**

#### **Integration Test Pipeline**
```yaml
pipeline:
  name: test-javascript-transform
  
  steps:
    - id: source
      type: delimited-source
      config:
        filePath: "input.csv"
        hasHeaders: true
        
    - id: transform
      type: javascript-transform
      config:
        script: |
          const row = context.input.current();
          
          // Business logic validation
          if (!context.validate.businessRule('amount > 0 && amount < 1000000')) {
            context.route.send('validation_errors');
            return false;
          }
          
          // Data enrichment
          const riskScore = row.amount > 50000 ? 'high' : 
                           row.amount > 10000 ? 'medium' : 'low';
          
          // Set output fields
          context.output.setField('customer_id', row.customer_id);
          context.output.setField('full_name', `${row.first_name} ${row.last_name}`);
          context.output.setField('risk_score', riskScore);
          context.output.setField('processed_at', context.utils.now());
          
          // Conditional routing
          if (riskScore === 'high') {
            context.route.sendCopy('high_risk_review');
          }
          
          return true;
          
        outputSchema:
          fields:
            - {name: customer_id, type: integer}
            - {name: full_name, type: string}
            - {name: risk_score, type: string}
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
- [ ] Complete pipeline executes successfully
- [ ] Performance baseline established (120K+ rows/sec)
- [ ] Error scenarios handled gracefully
- [ ] Memory usage bounded
- [ ] Routing targets working correctly

## 🎯 **Sprint Success Criteria**

### **Functional Requirements**
- [ ] Core provides script engine service with Jint integration
- [ ] Core provides context service for JavaScript execution
- [ ] Plugin remains thin, calling Core services
- [ ] Pure context API (no row parameter)
- [ ] Routing system enables conditional data flow

### **Performance Requirements**
- [ ] 120K+ rows/sec processing baseline
- [ ] Engine pooling reduces overhead
- [ ] Script compilation cached effectively
- [ ] Memory usage bounded and predictable

### **Quality Requirements**
- [ ] Clean separation: Core handles complexity, plugin stays simple
- [ ] 100% test coverage on Core services
- [ ] Integration tests validate end-to-end flow
- [ ] Error messages clear and actionable

## 🔄 **Key Architectural Benefits**

1. **Consistency**: Follows same pattern as DelimitedSource/Sink using Core services
2. **Reusability**: Script engine service available for future scripting needs
3. **Maintainability**: Complex Jint logic isolated in Core
4. **Testability**: Core services can be unit tested independently
5. **Performance**: Centralized optimization in Core benefits all script execution

## 📝 **Post-Sprint A**

After Sprint A completion:
- ✅ **Core Script Services**: Reusable for any future scripting needs
- ✅ **Proven Architecture**: Thin plugin pattern validated
- ✅ **Performance Baseline**: Real JavaScript execution metrics
- ✅ **Ready for Templates**: Mode 2 can build on solid Core services

This approach maintains the architectural integrity established in previous sprints while delivering the innovative JavaScript Transform capabilities!