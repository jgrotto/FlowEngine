# FlowEngine Pipeline Examples

This directory contains example pipeline configurations demonstrating FlowEngine's capabilities with real-world scenarios.

## Customer Processing Pipeline

**File**: `customer-processing-pipeline.yaml`

A comprehensive example showing a complete ETL pipeline:

### Pipeline Flow
```
CSV Source → JavaScript Transform → CSV Sink
```

### What it does:
1. **Source**: Reads customer data from `data/customers.csv`
2. **Transform**: Enriches data with JavaScript logic:
   - Creates full names from first/last names
   - Categorizes customers by age groups
   - Extracts email domains
   - Calculates account value categories (Standard/Silver/Gold/Premium)
   - Generates priority scores based on multiple factors
   - Adds processing timestamps
3. **Sink**: Outputs processed data to `output/processed-customers.csv`

### Key Features Demonstrated:
- **Delimited Source Plugin**: CSV reading with schema inference
- **JavaScript Transform Plugin**: Complex data transformation logic
- **Delimited Sink Plugin**: CSV writing with headers
- **Error Handling**: Graceful handling of malformed data
- **Performance Tuning**: Chunk sizes, buffer settings, timeouts
- **Monitoring**: Metrics and tracing configuration

### Running the Example:

```bash
# Create output directory
mkdir -p examples/output

# Run the pipeline (assuming FlowEngine CLI is available)
dotnet run --project src/FlowEngine.Cli pipeline run examples/customer-processing-pipeline.yaml
```

### Input Data Format:
```csv
customer_id,first_name,last_name,age,email,account_value
1,John,Smith,32,john.smith@gmail.com,75000.50
2,Sarah,Johnson,28,sarah.j@yahoo.com,125000.00
...
```

### Output Data Format:
```csv
customer_id,full_name,first_name,last_name,age,age_category,email,email_domain,account_value,value_category,priority_score,processed_date
1,John Smith,John,Smith,32,Adult,john.smith@gmail.com,gmail.com,75000.50,Silver,60,2025-01-09T...
2,Sarah Johnson,Sarah,Johnson,28,Adult,sarah.j@yahoo.com,yahoo.com,125000.00,Premium,70,2025-01-09T...
...
```

### Configuration Highlights:

#### JavaScript Transform Logic:
- **Age Categorization**: Young Adult (<25), Adult (25-44), Middle Age (45-64), Senior (65+)
- **Account Value Tiers**: Standard (<$10K), Silver ($10K-$50K), Gold ($50K-$100K), Premium ($100K+)
- **Priority Scoring**: Algorithm considering account value, age category, and email provider
- **Data Validation**: Checks for required fields, skips invalid rows
- **Error Handling**: Comprehensive try-catch with logging

#### Performance Settings:
- **Chunk Size**: 1000 rows for optimal memory usage
- **Buffer Sizes**: Tuned for different pipeline stages
- **Timeouts**: Configured for each connection
- **Backpressure**: Prevents memory overflow

#### Monitoring:
- **Metrics**: 5-second interval performance tracking
- **Tracing**: Full pipeline execution visibility
- **Error Logging**: Detailed error context and recovery

### Expected Performance:
- **Throughput**: ~7,000-8,000 rows/sec (JavaScript Transform)
- **Memory Usage**: <512MB for datasets up to 100K rows
- **Latency**: <50ms per chunk processing

### File Structure:
```
examples/
├── README.md                           # This file
├── customer-processing-pipeline.yaml   # Main pipeline configuration
├── data/
│   └── customers.csv                   # Sample input data
└── output/
    └── processed-customers.csv         # Generated output (after running)
```

## Creating Your Own Pipeline:

1. **Choose Your Plugins**: Select source, transform, and sink plugins
2. **Define Schema**: Specify input/output data structures
3. **Configure Connections**: Set up data flow between plugins
4. **Tune Performance**: Adjust chunk sizes, buffers, and timeouts
5. **Add Monitoring**: Enable metrics and error handling

For more information, see the [Configuration Guide](../docs/configuration.md) and [Plugin Development Guide](../docs/plugin-development.md).