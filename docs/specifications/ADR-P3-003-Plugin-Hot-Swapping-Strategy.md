# ADR-P3-003: Plugin Hot-Swapping Strategy

**Status**: ✅ Accepted  
**Date**: June 30, 2025  
**Decision**: Implement zero-downtime plugin updates using AssemblyLoadContext isolation

---

## Context

Enterprise deployments require the ability to update plugins without stopping running pipelines. Phase 2's AssemblyLoadContext isolation provides the foundation, but Phase 3 needs a comprehensive hot-swapping strategy for:

- Critical production pipelines that cannot be interrupted
- Plugin bug fixes that need immediate deployment
- Feature updates that should be available without downtime
- A/B testing of plugin versions in production
- Gradual rollouts of new plugin versions

Current Phase 2 approach requires full pipeline restart for plugin updates, causing:
- Service interruption during updates
- Lost processing state and progress
- Complex coordination during deployments
- Risk of data loss during transitions

## Decision

**Implement hot-swapping using versioned AssemblyLoadContext with graceful transition:**

```bash
# Enable hot-swap capability
dotnet run -- plugin hot-swap --enable

# Swap plugin version without downtime  
dotnet run -- plugin swap --name MyPlugin --version 2.0.0 --strategy rolling

# Rollback if issues detected
dotnet run -- plugin rollback --name MyPlugin --to-version 1.0.0
```

**Hot-Swap Strategy:**
1. **Load New Version**: Create new AssemblyLoadContext for updated plugin
2. **Parallel Processing**: Route new requests to new version, continue existing with old
3. **Graceful Drain**: Allow in-flight operations to complete on old version
4. **Health Monitoring**: Monitor new version for errors or performance issues
5. **Automatic Rollback**: Revert to old version if issues detected
6. **Cleanup**: Unload old AssemblyLoadContext after successful transition

## Consequences

### ✅ Positive Outcomes
- **Zero Downtime**: Plugin updates without service interruption
- **Risk Mitigation**: Automatic rollback on failure detection
- **Gradual Rollout**: Control percentage of traffic to new version
- **State Preservation**: In-flight operations complete successfully
- **Enterprise Ready**: Meets production deployment requirements
- **A/B Testing**: Compare plugin versions in live environment

### ❌ Negative Outcomes
- **Memory Overhead**: Temporary increase during transition (2x plugin memory)
- **Complexity**: Sophisticated state management and monitoring required
- **Resource Usage**: Additional CPU/memory for parallel processing
- **Debugging Difficulty**: Multiple plugin versions running simultaneously

### 🔄 Mitigation Strategies
- **Memory Monitoring**: Automatic abort if memory usage exceeds thresholds
- **Health Checks**: Comprehensive monitoring of new plugin version
- **Timeout Controls**: Configurable timeouts for graceful draining
- **Rollback Automation**: Automatic reversion based on error rates or performance

## Technical Implementation

### **Hot-Swap Process**
```csharp
public async Task<SwapResult> SwapPluginAsync(string pluginName, string newVersion)
{
    // 1. Load new plugin version
    var newContext = new AssemblyLoadContext($"{pluginName}-v{newVersion}", true);
    var newPlugin = LoadPlugin(newContext, pluginName, newVersion);
    
    // 2. Initialize and validate new plugin
    await newPlugin.InitializeAsync();
    var healthCheck = await newPlugin.HealthCheckAsync();
    if (!healthCheck.IsHealthy)
        return SwapResult.Failed("New plugin failed health check");
    
    // 3. Begin gradual traffic routing
    await RouterService.BeginGradualSwap(pluginName, newPlugin, percentage: 10);
    
    // 4. Monitor and gradually increase traffic
    while (await MonitorNewVersion(newPlugin))
    {
        await RouterService.IncreaseTraffic(pluginName, percentage: +10);
        await Task.Delay(TimeSpan.FromMinutes(1));
    }
    
    // 5. Complete swap and cleanup old version
    await RouterService.CompleteSwap(pluginName);
    await DrainOldVersion(pluginName, timeout: TimeSpan.FromMinutes(5));
    UnloadOldContext(pluginName);
    
    return SwapResult.Success();
}
```

### **Rollback Mechanism**
```csharp
public async Task<RollbackResult> RollbackPluginAsync(string pluginName)
{
    // 1. Stop routing to new version
    await RouterService.StopNewVersion(pluginName);
    
    // 2. Route all traffic back to old version
    await RouterService.RouteToOldVersion(pluginName);
    
    // 3. Allow new version to drain
    await DrainNewVersion(pluginName, timeout: TimeSpan.FromMinutes(2));
    
    // 4. Unload failed version
    UnloadNewContext(pluginName);
    
    return RollbackResult.Success();
}
```

## Alternatives Considered

### **Stop-and-Start Deployment** (Rejected)
- **Pro**: Simple implementation, no memory overhead
- **Con**: Service downtime, lost state, poor enterprise experience

### **Blue-Green Plugin Deployment** (Rejected)  
- **Pro**: Clean separation between versions
- **Con**: Requires duplicate infrastructure, complex state synchronization

### **Plugin Containers/Microservices** (Rejected)
- **Pro**: Complete isolation, independent scaling
- **Con**: Network latency, deployment complexity, resource overhead

## Configuration Options

### **Hot-Swap Settings**
```yaml
hotSwap:
  enabled: true
  strategy: "rolling"           # rolling|immediate|canary
  
  rolling:
    initialPercentage: 10       # Start with 10% traffic
    incrementPercentage: 10     # Increase by 10% each step
    incrementInterval: "1m"     # Wait 1 minute between increases
    
  monitoring:
    errorThreshold: 5.0         # Rollback if error rate > 5%
    latencyThreshold: "2s"      # Rollback if latency > 2 seconds
    memoryThreshold: "512MB"    # Abort if memory > 512MB
    
  gracefulDrain:
    timeout: "5m"               # Max time to wait for drain
    pollInterval: "10s"         # Check completion every 10 seconds
```

### **Safety Controls**
```yaml
safety:
  autoRollback: true            # Automatic rollback on failure
  requireHealthCheck: true      # New plugin must pass health check
  maxConcurrentSwaps: 1         # Only one swap at a time
  preserveState: true           # Maintain processing state during swap
```

## Success Criteria

- **Zero Data Loss**: No processing state lost during plugin swaps
- **Automatic Recovery**: Failed swaps automatically rollback within 2 minutes  
- **Memory Efficiency**: Memory overhead <2x during transition
- **Performance**: <5% performance impact during hot-swap process
- **Monitoring**: Full visibility into swap progress and health metrics

## Rollout Plan

### **Phase 1: Basic Hot-Swap** (Week 1)
- Implement basic load/unload mechanism
- Add graceful draining capability
- Create health check framework

### **Phase 2: Advanced Features** (Week 2)  
- Add gradual traffic routing
- Implement automatic rollback
- Create monitoring and metrics

### **Phase 3: Production Hardening** (Week 3)
- Add comprehensive safety controls
- Implement advanced rollout strategies
- Create CLI management commands