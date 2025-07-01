# ADR-P3-002: CLI-First Development

**Status**: ✅ Accepted  
**Date**: June 30, 2025  
**Decision**: Design all Phase 3 functionality through command-line interface first

---

## Context

Phase 2 development suffered from inconsistent developer experience where some operations required manual configuration file editing, others needed direct API calls, and debugging required diving into log files. This created:

- Inconsistent developer workflows
- High learning curve for new team members  
- Difficult automation and CI/CD integration
- Poor discoverability of available operations
- Time-consuming debugging and troubleshooting

## Decision

**Implement CLI-first development approach where every operation is accessible through command-line interface:**

```bash
# Plugin lifecycle
dotnet run -- plugin create|validate|test|build|install

# Schema management  
dotnet run -- schema infer|validate|migrate|apply

# Pipeline operations
dotnet run -- pipeline run|validate|monitor|logs

# Development tools
dotnet run -- debug|trace|profile|health
```

**Design Principles:**
- Every feature must have a CLI command
- CLI commands should be discoverable and self-documenting
- Complex operations should be composable from simple commands
- CLI should provide immediate feedback and clear error messages

## Consequences

### ✅ Positive Outcomes
- **Developer Experience**: Consistent, discoverable interface for all operations
- **Automation**: Easy integration with CI/CD pipelines and scripts
- **Documentation**: CLI help serves as living documentation
- **Learning Curve**: New developers can explore features through help commands
- **Debugging**: Dedicated CLI commands for troubleshooting and profiling
- **Remote Operations**: CLI enables easy remote server management

### ❌ Negative Outcomes
- **Initial Investment**: Significant effort to build comprehensive CLI
- **Maintenance Overhead**: CLI commands must be kept in sync with core features  
- **Complex Parameters**: Some operations may have many CLI parameters
- **Platform Dependencies**: CLI experience may vary across operating systems

### 🔄 Mitigation Strategies
- **Auto-Generation**: Generate CLI commands from core interfaces where possible
- **Configuration Files**: Support both CLI parameters and configuration files
- **Interactive Mode**: Provide guided prompts for complex operations
- **Rich Help**: Comprehensive help with examples and common patterns

## Alternatives Considered

### **GUI-First Development** (Rejected)
- **Pro**: Visual interface, potentially easier for non-technical users
- **Con**: Difficult to automate, slower development cycle, platform dependencies

### **API-First Development** (Rejected)
- **Pro**: Language agnostic, programmatic access
- **Con**: Poor developer experience, requires additional tooling

### **Configuration-File-Only** (Rejected)
- **Pro**: Declarative, version-controllable
- **Con**: Poor discoverability, difficult debugging, manual file editing

## Implementation Strategy

### **Phase 1: Core Commands** (Week 1)
```bash
plugin create|validate|build|install
schema infer|validate  
pipeline run|validate
```

### **Phase 2: Development Tools** (Week 2)
```bash
plugin test|benchmark
schema migrate|apply
pipeline monitor|logs|status
```

### **Phase 3: Advanced Operations** (Week 3)
```bash
debug|trace|profile
health check
monitor dashboard
cluster operations
```

## Technical Implementation

### **Command Structure**
```bash
dotnet run -- <noun> <verb> [options]

# Examples:
dotnet run -- plugin create --name MyPlugin --type Transform
dotnet run -- schema validate --file schema.yaml --data test.csv
dotnet run -- pipeline run --config pipeline.yaml --dry-run
```

### **Error Handling**
- Return appropriate exit codes (0 = success, >0 = error)
- Provide clear, actionable error messages
- Include suggestions for fixing common problems
- Support verbose mode for detailed diagnostics

### **Progress Reporting**
- Show progress bars for long-running operations
- Provide real-time feedback during processing
- Support quiet mode for automation scenarios
- Include estimated time remaining for large operations

## Success Metrics

- **Discoverability**: New developers can complete basic tasks using only CLI help
- **Automation**: CI/CD pipelines can perform all necessary operations via CLI
- **Performance**: CLI operations complete within acceptable time limits
- **Reliability**: CLI commands provide consistent behavior across platforms

## Future Considerations

- **Shell Completion**: Bash/PowerShell completion for commands and parameters
- **Interactive Mode**: Guided workflows for complex operations
- **Remote Management**: CLI commands that work with remote FlowEngine instances
- **Plugin Extensions**: Allow plugins to register their own CLI commands