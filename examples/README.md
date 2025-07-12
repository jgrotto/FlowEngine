# FlowEngine Pipeline Examples

This directory contains working pipeline examples that demonstrate FlowEngine capabilities.

## Available Examples

### 1. Simple Pipeline (`simple-pipeline.yaml`)
**Basic CSV read and write with explicit schema definition**

- **Purpose**: Demonstrates basic FlowEngine usage
- **Pipeline**: CSV Source → CSV Sink (direct passthrough)
- **Features**:
  - Explicit schema definition (production best practice)
  - Basic error handling and monitoring
  - Simple configuration with clear comments
- **Performance**: ~10K records in ~5 seconds
- **Use Case**: Data validation, format conversion, basic ETL

**Run Example:**
```bash
dotnet run --project src/FlowEngine.Cli/ -- examples/simple-pipeline.yaml
```

### 2. Complex Pipeline (`complex-pipeline.yaml`)
**Full 3-stage pipeline with JavaScript transformation**

- **Purpose**: Demonstrates advanced FlowEngine capabilities
- **Pipeline**: CSV Source → JavaScript Transform → CSV Sink
- **Features**:
  - JavaScript-based data transformation
  - Computed fields and business logic
  - Advanced error handling and retry logic
  - Performance monitoring and metrics
  - Complex schema transformations (6 → 12 columns)
- **Performance**: ~10K records in ~7 seconds
- **Use Case**: Real-world ETL with business logic, data enrichment

**Transformations Applied:**
- Creates full name from first/last name
- Categorizes age groups (Young Adult, Adult, Middle Age, Senior)
- Extracts email domains
- Calculates account value categories (Standard, Silver, Gold, Premium)
- Generates priority scores based on multiple factors
- Adds processing timestamps

**Run Example:**
```bash
dotnet run --project src/FlowEngine.Cli/ -- examples/complex-pipeline.yaml
```

## Data Files

### Input Data (`data/customers.csv`)
Sample customer data with the following schema:
- `customer_id` (Integer): Unique customer identifier
- `first_name` (String): Customer first name
- `last_name` (String): Customer last name  
- `age` (Integer): Customer age
- `email` (String): Customer email address
- `account_value` (Decimal): Account value in dollars

### Output Data (`output/`)
Processed results are written to the output directory:
- `simple-output.csv`: Direct copy of input data (schema validation)
- `processed-customers-simplified.csv`: Enriched data with computed fields

## Key Architecture Principles Demonstrated

### 1. Explicit Schema Definition
Both examples use explicit schema definitions rather than runtime inference:
```yaml
InferSchema: false
OutputSchema:
  Name: "CustomerData"
  Columns:
    - Name: "customer_id"
      Type: "Integer"
      Index: 0
      IsNullable: false
```

**Why Explicit Schemas?**
- ✅ **Production Stability**: Predictable data contracts
- ✅ **Early Validation**: "Your data doesn't match expected schema" upfront  
- ✅ **Error Prevention**: Prevents silent failures from data changes
- ✅ **Documentation**: Clear data expectations

### 2. Plugin Type Resolution
Both examples use short plugin names that are automatically resolved:
```yaml
type: "DelimitedSource"  # Resolves to DelimitedSource.DelimitedSourcePlugin
type: "DelimitedSink"    # Resolves to DelimitedSink.DelimitedSinkPlugin
type: "JavaScriptTransform"  # Resolves to JavaScriptTransform.JavaScriptTransformPlugin
```

### 3. Performance Configuration
Examples include appropriate performance settings:
- Buffer sizes optimized for data volume
- Memory limits and GC pressure thresholds
- Timeout and retry configurations
- Monitoring and metrics collection

## Development Guidelines

When creating new pipeline examples:

1. **Always use explicit schemas** - No `InferSchema: true`
2. **Include comprehensive error handling** - Timeouts, retries, error limits
3. **Add clear documentation** - Comments explaining each section
4. **Test thoroughly** - Ensure examples work end-to-end
5. **Follow naming conventions** - Descriptive plugin and connection names
6. **Include performance settings** - Appropriate for data volume

## Performance Expectations

| Pipeline Type | Record Count | Execution Time | Throughput |
|---------------|--------------|----------------|------------|
| Simple        | 10,000       | ~5 seconds     | ~2,000 rows/sec |
| Complex       | 10,000       | ~7 seconds     | ~1,400 rows/sec |

*Performance may vary based on hardware and data complexity*

## File Structure
```
examples/
├── README.md              # This documentation
├── simple-pipeline.yaml   # Basic CSV read/write example
├── complex-pipeline.yaml  # Advanced transformation example
├── data/
│   ├── customers.csv      # Sample input data (10K records)
│   └── customers-50k.csv  # Large dataset for performance testing
└── output/
    ├── simple-output.csv             # Simple pipeline output
    └── processed-customers-simplified.csv  # Complex pipeline output
```

## Creating Your Own Pipeline

1. **Choose Your Plugins**: Select source, transform, and sink plugins
2. **Define Explicit Schema**: Specify exact input/output data structures
3. **Configure Connections**: Set up data flow between plugins
4. **Tune Performance**: Adjust chunk sizes, buffers, and timeouts
5. **Add Monitoring**: Enable metrics and error handling
6. **Test Thoroughly**: Validate with representative data

For more information, see the FlowEngine documentation.