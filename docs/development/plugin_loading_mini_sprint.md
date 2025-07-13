# Plugin Loading Encapsulation Mini-Sprint Plan

**Duration**: 2-3 days  
**Priority**: High (architectural cleanup)  
**Scope**: Refactor plugin loading responsibilities from CLI to Core

## 🎯 Objective

Move plugin loading and discovery logic from `Program.cs` to a dedicated service in FlowEngine.Core, improving separation of concerns and enabling reuse across multiple frontends.

## 📋 Current State Analysis

### What CLI Currently Does
```csharp
// In Program.cs - Should NOT be here
private static string[] GetPluginDirectories()
{
    var appDirectory = AppDomain.CurrentDomain.BaseDirectory;
    return new[]
    {
        Path.Combine(appDirectory, "plugins"),
        Path.Combine(appDirectory, "Plugins"), 
        Path.Combine(appDirectory, "..", "plugins"),
        Path.Combine(Directory.GetCurrentDirectory(), "plugins"),
        appDirectory
    };
}

// Manual service setup - Should be encapsulated
services.AddFlowEngine();
var serviceProvider = services.BuildServiceProvider();
var pluginLoader = serviceProvider.GetRequiredService<IPluginLoader>();
```

### Problems with Current Approach
- ❌ CLI responsible for infrastructure concerns
- ❌ Plugin discovery logic not reusable
- ❌ Manual service provider management
- ❌ Difficult to unit test plugin loading in isolation
- ❌ Web frontend would duplicate this logic

## 🏗️ Proposed Architecture

### New Service: `IPluginDiscoveryService`
```csharp
namespace FlowEngine.Abstractions.Services;

public interface IPluginDiscoveryService
{
    /// <summary>
    /// Discovers plugins in standard locations and returns metadata
    /// </summary>
    Task<IReadOnlyCollection<PluginDiscoveryResult>> DiscoverPluginsAsync(
        PluginDiscoveryOptions? options = null);
    
    /// <summary>
    /// Gets standard plugin directories for the current environment
    /// </summary>
    IReadOnlyCollection<string> GetStandardPluginDirectories();
    
    /// <summary>
    /// Validates a plugin assembly without loading it
    /// </summary>
    Task<PluginValidationResult> ValidatePluginAsync(string assemblyPath);
}

public record PluginDiscoveryOptions
{
    public IReadOnlyCollection<string>? AdditionalDirectories { get; init; }
    public bool IncludeBuiltInPlugins { get; init; } = true;
    public bool ScanRecursively { get; init; } = false;
    public TimeSpan? DiscoveryTimeout { get; init; } = TimeSpan.FromSeconds(30);
}

public record PluginDiscoveryResult
{
    public required string AssemblyPath { get; init; }
    public required string TypeName { get; init; }
    public required string PluginName { get; init; }
    public string? Description { get; init; }
    public Version? Version { get; init; }
    public PluginCapabilities Capabilities { get; init; }
}
```

### New Service: `IFlowEngineBootstrapper`
```csharp
namespace FlowEngine.Abstractions.Services;

public interface IFlowEngineBootstrapper
{
    /// <summary>
    /// Initializes FlowEngine with plugin discovery and returns configured coordinator
    /// </summary>
    Task<FlowEngineCoordinator> InitializeAsync(FlowEngineBootstrapOptions? options = null);
    
    /// <summary>
    /// Loads a pipeline configuration and discovers required plugins
    /// </summary>
    Task<IPipelineConfiguration> LoadConfigurationAsync(
        string configPath, 
        IDictionary<string, string>? variables = null);
}

public record FlowEngineBootstrapOptions
{
    public PluginDiscoveryOptions? PluginDiscovery { get; init; }
    public LogLevel MinimumLogLevel { get; init; } = LogLevel.Information;
    public bool EnableVerboseOutput { get; init; } = false;
    public IServiceCollection? Services { get; init; }
}
```

## 🔧 Implementation Tasks

### Day 1: Core Services Implementation
**Duration**: 4-6 hours

1. **Create Plugin Discovery Service**
   - `src/FlowEngine.Abstractions/Services/IPluginDiscoveryService.cs`
   - `src/FlowEngine.Core/Services/PluginDiscoveryService.cs`
   - Move plugin directory discovery logic from CLI
   - Add plugin metadata extraction
   - Implement async discovery with timeout handling

2. **Create FlowEngine Bootstrapper**
   - `src/FlowEngine.Abstractions/Services/IFlowEngineBootstrapper.cs`
   - `src/FlowEngine.Core/Services/FlowEngineBootstrapper.cs`
   - Encapsulate service container setup
   - Handle plugin type resolver initialization
   - Provide simplified initialization API

3. **Update Service Registration**
   - Add new services to `ServiceCollectionExtensions.cs`
   - Ensure proper dependency injection setup

### Day 2: CLI Refactoring
**Duration**: 3-4 hours

1. **Simplify Program.cs**
   ```csharp
   // New simplified CLI approach
   public static async Task<int> Main(string[] args)
   {
       try
       {
           var options = ParseArguments(args);
           
           var bootstrapper = new FlowEngineBootstrapper();
           var coordinator = await bootstrapper.InitializeAsync(options.Bootstrap);
           
           var config = await bootstrapper.LoadConfigurationAsync(
               options.ConfigPath, 
               options.Variables);
           
           return await coordinator.ExecuteAsync(config);
       }
       catch (Exception ex)
       {
           Console.Error.WriteLine($"Error: {ex.Message}");
           return 1;
       }
   }
   ```

2. **Remove Plugin Loading Logic**
   - Delete `GetPluginDirectories()` method
   - Remove manual service provider setup
   - Remove plugin type resolver initialization
   - Simplify configuration loading

3. **Update Command Implementation**
   - Use bootstrapper instead of manual setup
   - Focus CLI on argument parsing and user experience

### Day 3: Testing and Validation
**Duration**: 2-3 hours

1. **Unit Tests**
   - Test plugin discovery service with mock file system
   - Test bootstrapper initialization scenarios
   - Test error handling and edge cases

2. **Integration Tests**
   - Verify CLI still works with new architecture
   - Test plugin loading across different scenarios
   - Validate performance hasn't degraded

3. **Documentation Updates**
   - Update plugin development guide
   - Document new bootstrapper API
   - Update CLI usage documentation

## ✅ Success Criteria

### Functional Requirements
- [ ] CLI functionality unchanged from user perspective
- [ ] Plugin discovery works in all supported scenarios
- [ ] Configuration loading maintains compatibility
- [ ] Error handling provides clear messages

### Architectural Requirements
- [ ] Plugin loading logic moved to Core
- [ ] CLI reduced to <100 lines in Main method
- [ ] New services properly registered in DI container
- [ ] Bootstrapper API simple and intuitive

### Quality Requirements
- [ ] Unit test coverage >90% for new services
- [ ] Integration tests pass without modification
- [ ] Performance impact <5% (startup time)
- [ ] Memory usage unchanged

## 🎁 Benefits

### Immediate Benefits
- **Cleaner CLI**: Focus on user interaction, not infrastructure
- **Reusability**: Web frontend can use same bootstrapper
- **Testability**: Plugin loading logic isolated and testable
- **Maintainability**: Clear separation of concerns

### Future Benefits
- **Service Bus Integration**: Easy to add message queue frontends
- **Microservice Architecture**: Core services ready for distribution
- **Plugin Marketplace**: Discovery service foundation for plugin repositories
- **Development Tools**: Plugin development CLI commands easier to implement

## 🚨 Risks and Mitigations

### Risk 1: Breaking CLI Functionality
**Mitigation**: Comprehensive integration testing before and after changes

### Risk 2: Performance Regression
**Mitigation**: Benchmark startup time and memory usage

### Risk 3: Complex Migration
**Mitigation**: Keep existing CLI as fallback during development

### Risk 4: Service Dependencies
**Mitigation**: Clear dependency injection setup with validation

## 🔄 Next Steps After Completion

1. **Web Frontend Development**: Use bootstrapper for ASP.NET Core integration
2. **Plugin CLI Commands**: Add `plugin create`, `plugin test`, `plugin publish`
3. **Configuration Validation**: Enhanced YAML validation with plugin discovery
4. **Performance Monitoring**: Add startup telemetry and optimization
5. **Plugin Repository**: Foundation for centralized plugin distribution

## 📝 Implementation Notes

### Design Principles
- **Keep CLI Simple**: Focus on argument parsing and user experience
- **Service-Oriented**: Each concern gets its own service
- **Async-First**: All I/O operations should be async
- **Error-First**: Comprehensive error handling with clear messages

### Code Quality Standards
- **XML Documentation**: All public APIs documented
- **Unit Testing**: >90% coverage for new services
- **Performance**: Startup time <2 seconds for typical scenarios
- **Memory**: No memory leaks in service initialization

This mini-sprint will significantly improve the architecture and make FlowEngine more suitable for enterprise deployment scenarios.