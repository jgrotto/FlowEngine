using FlowEngine.Abstractions.Configuration;
using FlowEngine.Abstractions.Factories;
using FlowEngine.Abstractions.Services;
using FlowEngine.Core.Configuration;
using FlowEngine.Core.Factories;
using FlowEngine.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace FlowEngine.Core;

/// <summary>
/// Extension methods for registering FlowEngine Core services with dependency injection.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds FlowEngine Core services to the service collection.
    /// This includes factories, data services, and core infrastructure.
    /// </summary>
    /// <param name="services">The service collection to add services to</param>
    /// <returns>The service collection for chaining</returns>
    public static IServiceCollection AddFlowEngineCore(this IServiceCollection services)
    {
        // Register factory interfaces with their implementations
        services.TryAddSingleton<IDataTypeService, DataTypeService>();
        services.TryAddSingleton<ISchemaFactory, SchemaFactory>();
        services.TryAddSingleton<IArrayRowFactory, ArrayRowFactory>();
        services.TryAddSingleton<IChunkFactory, ChunkFactory>();
        services.TryAddSingleton<IDatasetFactory, DatasetFactory>();

        // Register monitoring and performance services
        services.TryAddSingleton<IMemoryManager, MemoryManager>();
        services.TryAddSingleton<IPerformanceMonitor, PerformanceMonitor>();
        services.TryAddSingleton<IChannelTelemetry, ChannelTelemetry>();

        // Register Core infrastructure services
        services.TryAddSingleton<Abstractions.Execution.IDagAnalyzer, Execution.DagAnalyzer>();
        services.TryAddSingleton<FlowEngineCoordinator>();
        
        // Register configuration services
        services.TryAddSingleton<IPluginConfigurationMapper, PluginConfigurationMapper>();

        // Register JavaScript script engine services
        services.TryAddSingleton<IScriptEngineService, JintScriptEngineService>();
        services.TryAddSingleton<IJavaScriptContextService, JavaScriptContextService>();

        return services;
    }

    /// <summary>
    /// Adds FlowEngine factories only (minimal subset for plugin scenarios).
    /// Useful when you only need the factory services without the full Core infrastructure.
    /// </summary>
    /// <param name="services">The service collection to add services to</param>
    /// <returns>The service collection for chaining</returns>
    public static IServiceCollection AddFlowEngineFactories(this IServiceCollection services)
    {
        // Register factory interfaces with their implementations
        services.TryAddSingleton<IDataTypeService, DataTypeService>();
        services.TryAddSingleton<ISchemaFactory, SchemaFactory>();
        services.TryAddSingleton<IArrayRowFactory, ArrayRowFactory>();
        services.TryAddSingleton<IChunkFactory, ChunkFactory>();
        services.TryAddSingleton<IDatasetFactory, DatasetFactory>();

        return services;
    }

    /// <summary>
    /// Adds FlowEngine monitoring services to the service collection.
    /// Includes performance monitoring, memory management, and telemetry services.
    /// </summary>
    /// <param name="services">The service collection to add services to</param>
    /// <returns>The service collection for chaining</returns>
    public static IServiceCollection AddFlowEngineMonitoring(this IServiceCollection services)
    {
        // Register monitoring and telemetry services
        services.TryAddSingleton<IMemoryManager, MemoryManager>();
        services.TryAddSingleton<IPerformanceMonitor, PerformanceMonitor>();
        services.TryAddSingleton<IChannelTelemetry, ChannelTelemetry>();

        return services;
    }

    /// <summary>
    /// Adds FlowEngine plugin infrastructure services.
    /// Includes plugin loading, management, and execution services.
    /// </summary>
    /// <param name="services">The service collection to add services to</param>
    /// <returns>The service collection for chaining</returns>
    public static IServiceCollection AddFlowEnginePlugins(this IServiceCollection services)
    {
        // Ensure factories are registered first (plugins depend on them)
        services.AddFlowEngineFactories();

        // Register plugin infrastructure
        services.TryAddSingleton<Abstractions.Plugins.IPluginRegistry, Plugins.PluginRegistry>();
        services.TryAddSingleton<Abstractions.Plugins.IPluginLoader, Plugins.PluginLoader>();
        services.TryAddSingleton<Abstractions.Plugins.IPluginManager, Plugins.PluginManager>();
        services.TryAddSingleton<Abstractions.Plugins.IPluginDiscoveryService, Plugins.PluginDiscoveryService>();

        // Register execution infrastructure
        services.TryAddSingleton<Abstractions.Execution.IPipelineExecutor, Execution.PipelineExecutor>();

        return services;
    }

    /// <summary>
    /// Adds all FlowEngine services (full registration).
    /// Includes Core infrastructure, factories, plugins, and execution services.
    /// </summary>
    /// <param name="services">The service collection to add services to</param>
    /// <returns>The service collection for chaining</returns>
    public static IServiceCollection AddFlowEngine(this IServiceCollection services)
    {
        // Add all service groups
        services.AddFlowEngineCore();
        services.AddFlowEnginePlugins();

        return services;
    }
}