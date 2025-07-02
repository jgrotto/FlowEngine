using FlowEngine.Abstractions.Data;
using FlowEngine.Core.Data;
using FlowEngine.Core.Plugins.Loading;
using FlowEngine.Core.Services.Scripting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace FlowEngine.Core.Extensions;

/// <summary>
/// Extension methods for registering FlowEngine services in the dependency injection container.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds FlowEngine core services to the service collection.
    /// </summary>
    /// <param name="services">Service collection</param>
    /// <returns>Service collection for method chaining</returns>
    public static IServiceCollection AddFlowEngineCore(this IServiceCollection services)
    {
        // Add data services
        services.TryAddSingleton<IArrayRowFactory, ArrayRowFactory>();
        
        // Add plugin loading services
        services.TryAddSingleton<IPluginLoader, PluginLoader>();
        
        // Add script engine services
        services.TryAddSingleton<IScriptEngineService, ScriptEngineService>();
        
        return services;
    }

    /// <summary>
    /// Adds FlowEngine plugin loading services to the service collection.
    /// </summary>
    /// <param name="services">Service collection</param>
    /// <returns>Service collection for method chaining</returns>
    public static IServiceCollection AddFlowEnginePluginLoading(this IServiceCollection services)
    {
        services.TryAddSingleton<IArrayRowFactory, ArrayRowFactory>();
        services.TryAddSingleton<IPluginLoader, PluginLoader>();
        return services;
    }

    /// <summary>
    /// Adds FlowEngine script engine services to the service collection.
    /// </summary>
    /// <param name="services">Service collection</param>
    /// <returns>Service collection for method chaining</returns>
    public static IServiceCollection AddFlowEngineScriptEngine(this IServiceCollection services)
    {
        services.TryAddSingleton<IScriptEngineService, ScriptEngineService>();
        return services;
    }
}