using FlowEngine.Abstractions.Plugins;
using Microsoft.Extensions.Logging;

namespace FlowEngine.Core.Plugins.Loading;

/// <summary>
/// Example demonstrating how to use the plugin loader with the DelimitedSource plugin.
/// This is for demonstration and testing purposes.
/// </summary>
public static class PluginLoadingExample
{
    /// <summary>
    /// Demonstrates loading and using the DelimitedSource plugin.
    /// </summary>
    /// <param name="pluginPath">Path to the DelimitedSource plugin assembly</param>
    /// <param name="logger">Logger instance</param>
    /// <returns>Task representing the example execution</returns>
    public static async Task<bool> DemonstrateDelimitedSourceLoading(string pluginPath, ILogger logger)
    {
        // Create plugin loader
        using var pluginLoader = new PluginLoader(logger);

        try
        {
            logger.LogInformation("=== FlowEngine Plugin Loading Demo ===");
            
            // Step 1: Validate the plugin
            logger.LogInformation("Step 1: Validating plugin at {PluginPath}", pluginPath);
            var validationResult = await pluginLoader.ValidatePluginAsync(pluginPath);
            
            if (!validationResult.IsValid)
            {
                logger.LogError("Plugin validation failed: {Errors}", string.Join(", ", validationResult.Errors));
                return false;
            }
            
            logger.LogInformation("✓ Plugin validation successful");

            // Step 2: Load the plugin
            logger.LogInformation("Step 2: Loading plugin");
            var pluginDescriptor = await pluginLoader.LoadPluginAsync(pluginPath);
            
            logger.LogInformation("✓ Plugin loaded successfully:");
            logger.LogInformation("  - ID: {PluginId}", pluginDescriptor.Id);
            logger.LogInformation("  - Name: {PluginName}", pluginDescriptor.Name);
            logger.LogInformation("  - Version: {PluginVersion}", pluginDescriptor.Version);
            logger.LogInformation("  - Type: {PluginType}", pluginDescriptor.Type);
            logger.LogInformation("  - Assembly: {AssemblyPath}", pluginDescriptor.AssemblyPath);
            logger.LogInformation("  - Loaded At: {LoadedAt}", pluginDescriptor.LoadedAt);

            // Step 3: Create a simple configuration
            logger.LogInformation("Step 3: Creating plugin configuration");
            var configuration = CreateSampleConfiguration();
            
            // Step 4: Instantiate the plugin with configuration
            logger.LogInformation("Step 4: Creating plugin instance");
            var plugin = await pluginLoader.LoadPluginWithConfigurationAsync(pluginPath, configuration);
            
            logger.LogInformation("✓ Plugin instance created:");
            logger.LogInformation("  - Plugin ID: {PluginId}", plugin.Id);
            logger.LogInformation("  - Plugin Name: {PluginName}", plugin.Name);
            logger.LogInformation("  - Plugin Version: {PluginVersion}", plugin.Version);
            logger.LogInformation("  - Plugin Type: {PluginType}", plugin.Type);
            logger.LogInformation("  - Plugin Author: {PluginAuthor}", plugin.Author);
            logger.LogInformation("  - Supports Hot-Swapping: {SupportsHotSwapping}", plugin.SupportsHotSwapping);

            // Step 5: Check plugin health
            logger.LogInformation("Step 5: Checking plugin health");
            var health = await plugin.CheckHealthAsync();
            
            logger.LogInformation("✓ Plugin health check completed:");
            logger.LogInformation("  - Is Healthy: {IsHealthy}", health.IsHealthy);
            logger.LogInformation("  - State: {State}", health.State);
            logger.LogInformation("  - Health Score: {HealthScore}", health.HealthScore);
            logger.LogInformation("  - Checks Count: {ChecksCount}", health.Checks.Length);

            // Step 6: Get plugin metrics
            logger.LogInformation("Step 6: Getting plugin metrics");
            var metrics = await plugin.GetMetricsAsync();
            
            logger.LogInformation("✓ Plugin metrics retrieved:");
            logger.LogInformation("  - Plugin ID: {PluginId}", metrics.PluginId);
            logger.LogInformation("  - State: {State}", metrics.State);
            logger.LogInformation("  - Total Processed: {TotalProcessed}", metrics.TotalProcessed);
            logger.LogInformation("  - Error Count: {ErrorCount}", metrics.ErrorCount);
            logger.LogInformation("  - Throughput: {Throughput:F2} items/sec", metrics.ThroughputPerSecond);

            // Step 7: List all loaded plugins
            logger.LogInformation("Step 7: Listing all loaded plugins");
            var loadedPlugins = pluginLoader.GetLoadedPlugins();
            
            logger.LogInformation("✓ Found {PluginCount} loaded plugins:", loadedPlugins.Count);
            foreach (var loadedPlugin in loadedPlugins)
            {
                logger.LogInformation("  - {PluginId}: {PluginName} v{Version} ({Type})", 
                    loadedPlugin.Id, loadedPlugin.Name, loadedPlugin.Version, loadedPlugin.Type);
            }

            // Step 8: Cleanup - Dispose plugin
            logger.LogInformation("Step 8: Disposing plugin");
            plugin.Dispose();
            logger.LogInformation("✓ Plugin disposed");

            // Step 9: Unload plugin
            logger.LogInformation("Step 9: Unloading plugin");
            var unloadResult = await pluginLoader.UnloadPluginAsync(pluginDescriptor.Id);
            
            if (unloadResult)
            {
                logger.LogInformation("✓ Plugin unloaded successfully");
            }
            else
            {
                logger.LogWarning("⚠ Plugin unload returned false");
            }

            logger.LogInformation("=== Plugin Loading Demo Completed Successfully ===");
            return true;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Plugin loading demo failed");
            return false;
        }
    }

    /// <summary>
    /// Creates a sample configuration for the DelimitedSource plugin.
    /// </summary>
    /// <returns>Sample plugin configuration</returns>
    private static IPluginConfiguration CreateSampleConfiguration()
    {
        // This is a simplified configuration that matches what DelimitedSource expects
        // In a real implementation, this would be properly typed
        return new SamplePluginConfiguration
        {
            PluginId = "delimited-source",
            Name = "DelimitedSource",
            Version = "3.0.0",
            PluginType = "Source"
        };
    }

    /// <summary>
    /// Sample plugin configuration implementation for demonstration.
    /// </summary>
    private sealed class SamplePluginConfiguration : IPluginConfiguration
    {
        public required string PluginId { get; init; }
        public required string Name { get; init; }
        public required string Version { get; init; }
        public required string PluginType { get; init; }
        
        public FlowEngine.Abstractions.Data.ISchema? InputSchema => null;
        public FlowEngine.Abstractions.Data.ISchema? OutputSchema => null;
        public bool SupportsHotSwapping => true;
        
        public System.Collections.Immutable.ImmutableDictionary<string, int> InputFieldIndexes => 
            System.Collections.Immutable.ImmutableDictionary<string, int>.Empty;
        
        public System.Collections.Immutable.ImmutableDictionary<string, int> OutputFieldIndexes => 
            System.Collections.Immutable.ImmutableDictionary<string, int>.Empty;
        
        public System.Collections.Immutable.ImmutableDictionary<string, object> Properties => 
            System.Collections.Immutable.ImmutableDictionary<string, object>.Empty;

        public int GetInputFieldIndex(string fieldName) => throw new NotSupportedException("Source plugins don't have input schemas");
        public int GetOutputFieldIndex(string fieldName) => throw new ArgumentException($"Field not found: {fieldName}");
        public bool IsCompatibleWith(FlowEngine.Abstractions.Data.ISchema inputSchema) => true;
        public T GetProperty<T>(string key) => throw new KeyNotFoundException($"Property not found: {key}");
        public bool TryGetProperty<T>(string key, out T? value) { value = default; return false; }
    }
}