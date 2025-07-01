# FlowEngine Phase 3: CLI Operations Guide

**Reading Time**: 4 minutes  
**Purpose**: Master CLI-driven plugin and pipeline management  

---

## Plugin Lifecycle Management

### Create New Plugin
```bash
# Generate five-component plugin template
dotnet run -- plugin create --name MyPlugin --type Transform
dotnet run -- plugin create --name CsvReader --type Source
dotnet run -- plugin create --name DatabaseWriter --type Sink

# Create with custom template
dotnet run -- plugin create --name CustomPlugin --template ./templates/advanced.json
```

### Validate Plugin
```bash
# Validate plugin structure and configuration
dotnet run -- plugin validate --path ./MyPlugin
dotnet run -- plugin validate --path ./MyPlugin --strict

# Validate specific component
dotnet run -- plugin validate --path ./MyPlugin --component Configuration
dotnet run -- plugin validate --path ./MyPlugin --component Validator
```

### Build and Install Plugin
```bash
# Build plugin with dependencies
dotnet run -- plugin build --path ./MyPlugin --output ./plugins/

# Install plugin to global registry
dotnet run -- plugin install --path ./plugins/MyPlugin.dll
dotnet run -- plugin install --name MyPlugin --version 1.0.0

# List installed plugins
dotnet run -- plugin list
dotnet run -- plugin list --detailed
```

### Test Plugin
```bash
# Unit test single plugin
dotnet run -- plugin test --path ./MyPlugin

# Integration test with sample data
dotnet run -- plugin test --path ./MyPlugin --data ./test-data.csv

# Performance benchmark
dotnet run -- plugin benchmark --path ./MyPlugin --rows 100000
```

---

## Schema Management

### Schema Inference
```bash
# Infer schema from data file
dotnet run -- schema infer --file data.csv --output schema.yaml
dotnet run -- schema infer --file data.json --format json --output schema.json

# Infer from database table
dotnet run -- schema infer --connection "Server=localhost;Database=test" --table users
```

### Schema Validation
```bash
# Validate data against schema
dotnet run -- schema validate --schema schema.yaml --data test.csv
dotnet run -- schema validate --schema schema.yaml --data test.csv --mode strict

# Validate schema compatibility
dotnet run -- schema compatible --input input.yaml --output output.yaml
```

### Schema Evolution
```bash
# Generate migration between schema versions
dotnet run -- schema migrate --from v1.yaml --to v2.yaml --output migration.yaml

# Apply schema migration
dotnet run -- schema apply --schema v2.yaml --migration migration.yaml --data data.csv
```

---

## Pipeline Operations

### Pipeline Configuration
```bash
# Validate pipeline configuration
dotnet run -- pipeline validate --config pipeline.yaml
dotnet run -- pipeline validate --config pipeline.yaml --check-plugins

# Generate pipeline template
dotnet run -- pipeline template --source CsvReader --transform DataCleaner --sink DatabaseWriter
```

### Pipeline Execution
```bash
# Run pipeline with configuration
dotnet run -- pipeline run --config pipeline.yaml

# Run with input data override
dotnet run -- pipeline run --config pipeline.yaml --input ./data/input.csv

# Run with environment variables
ENV=production dotnet run -- pipeline run --config pipeline.yaml

# Dry run (validate without executing)
dotnet run -- pipeline run --config pipeline.yaml --dry-run
```

### Pipeline Monitoring
```bash
# Real-time pipeline monitoring
dotnet run -- pipeline monitor --config pipeline.yaml --live

# Get pipeline status
dotnet run -- pipeline status --id pipeline-123

# View pipeline logs
dotnet run -- pipeline logs --id pipeline-123 --follow
dotnet run -- pipeline logs --id pipeline-123 --component MyPlugin
```

---

## Real-Time Monitoring Commands

### Performance Metrics
```bash
# Show real-time performance dashboard
dotnet run -- monitor dashboard

# Monitor specific pipeline
dotnet run -- monitor pipeline --id pipeline-123

# Monitor plugin performance
dotnet run -- monitor plugin --name MyPlugin --metric throughput
dotnet run -- monitor plugin --name MyPlugin --metric memory
```

### Health Checks
```bash
# Check system health
dotnet run -- health check
dotnet run -- health check --detailed

# Check plugin health
dotnet run -- health plugin --name MyPlugin
dotnet run -- health plugin --all

# Check dependencies
dotnet run -- health dependencies --config pipeline.yaml
```

### Resource Monitoring
```bash
# Monitor memory usage
dotnet run -- monitor memory --threshold 80%

# Monitor CPU usage
dotnet run -- monitor cpu --interval 1s

# Monitor disk I/O for spillover operations
dotnet run -- monitor disk --path ./temp --alert-on-full
```

---

## Development Tools

### Code Generation
```bash
# Generate plugin interfaces
dotnet run -- generate interfaces --plugin MyPlugin --output ./src/

# Generate test scaffolding
dotnet run -- generate tests --plugin MyPlugin --output ./tests/

# Generate documentation
dotnet run -- generate docs --plugin MyPlugin --format markdown
```

### Debugging Support
```bash
# Enable debug mode for plugin
dotnet run -- debug plugin --name MyPlugin --breakpoints

# Trace plugin execution
dotnet run -- trace plugin --name MyPlugin --output trace.log

# Profile plugin performance
dotnet run -- profile plugin --name MyPlugin --duration 60s
```

### Configuration Management
```bash
# Validate configuration files
dotnet run -- config validate --file pipeline.yaml
dotnet run -- config validate --directory ./configs/

# Environment-specific configuration
dotnet run -- config generate --env development --output dev-pipeline.yaml
dotnet run -- config generate --env production --output prod-pipeline.yaml

# Configuration diff
dotnet run -- config diff --file1 dev.yaml --file2 prod.yaml
```

---

## Batch Operations

### Bulk Plugin Operations
```bash
# Install multiple plugins
dotnet run -- plugin bulk-install --directory ./plugins/

# Update all plugins
dotnet run -- plugin bulk-update

# Test all plugins
dotnet run -- plugin bulk-test --parallel 4
```

### Batch Pipeline Management
```bash
# Run multiple pipelines
dotnet run -- pipeline batch-run --directory ./pipelines/ --parallel 2

# Validate multiple configurations
dotnet run -- pipeline batch-validate --directory ./configs/

# Generate reports for multiple runs
dotnet run -- pipeline batch-report --runs pipeline-*.log
```

---

## Advanced Features

### Plugin Hot-Swapping
```bash
# Enable hot-swap mode
dotnet run -- plugin hot-swap --enable

# Swap plugin version without downtime
dotnet run -- plugin swap --name MyPlugin --version 2.0.0 --strategy rolling

# Rollback to previous version
dotnet run -- plugin rollback --name MyPlugin --to-version 1.0.0
```

### Distributed Execution
```bash
# Prepare for distributed execution
dotnet run -- cluster prepare --nodes node1,node2,node3

# Run pipeline across cluster
dotnet run -- pipeline run --config pipeline.yaml --distributed

# Monitor cluster health
dotnet run -- cluster health --show-details
```

### Security and Audit
```bash
# Audit plugin permissions
dotnet run -- security audit --plugin MyPlugin

# Check plugin signatures
dotnet run -- security verify --plugin MyPlugin.dll

# Generate audit report
dotnet run -- audit report --from 2025-01-01 --to 2025-06-30
```

---

## Common Command Patterns

### Development Workflow
```bash
# Full development cycle
dotnet run -- plugin create --name NewPlugin --type Transform
dotnet run -- plugin validate --path ./NewPlugin
dotnet run -- plugin test --path ./NewPlugin
dotnet run -- plugin build --path ./NewPlugin
dotnet run -- plugin install --path ./plugins/NewPlugin.dll
```

### Production Deployment
```bash
# Production validation sequence
dotnet run -- plugin validate --path ./MyPlugin --strict
dotnet run -- schema validate --schema production.yaml --data sample.csv
dotnet run -- pipeline validate --config production-pipeline.yaml --check-plugins
dotnet run -- pipeline run --config production-pipeline.yaml --dry-run
```

### Troubleshooting Sequence
```bash
# Debug failing pipeline
dotnet run -- pipeline status --id failed-pipeline-123
dotnet run -- pipeline logs --id failed-pipeline-123 --error-only
dotnet run -- plugin test --name FailingPlugin --verbose
dotnet run -- health check --detailed
```