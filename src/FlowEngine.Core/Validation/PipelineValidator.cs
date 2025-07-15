using FlowEngine.Abstractions.Configuration;
using FlowEngine.Abstractions.Data;
using FlowEngine.Abstractions.Plugins;
using FlowEngine.Core.Configuration;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ValidationResult = FlowEngine.Abstractions.ValidationResult;

namespace FlowEngine.Core.Validation;

/// <summary>
/// Validates complete pipeline configurations for compatibility and correctness.
/// Performs comprehensive validation including plugin chain compatibility, schema validation, and structural integrity.
/// </summary>
public class PipelineValidator
{
    private readonly PluginConfigurationProviderRegistry _providerRegistry;
    private readonly ILogger<PipelineValidator> _logger;

    /// <summary>
    /// Initializes a new instance of the PipelineValidator.
    /// </summary>
    /// <param name="providerRegistry">Registry of plugin configuration providers</param>
    /// <param name="logger">Logger for validation operations</param>
    public PipelineValidator(
        PluginConfigurationProviderRegistry providerRegistry,
        ILogger<PipelineValidator> logger)
    {
        _providerRegistry = providerRegistry ?? throw new ArgumentNullException(nameof(providerRegistry));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Validates complete pipeline configuration including plugin chain compatibility.
    /// </summary>
    /// <param name="pipeline">Pipeline configuration to validate</param>
    /// <returns>Comprehensive validation result</returns>
    public async Task<PipelineValidationResult> ValidatePipelineAsync(IPipelineConfiguration pipeline)
    {
        if (pipeline == null)
            throw new ArgumentNullException(nameof(pipeline));

        _logger.LogDebug("Starting pipeline validation for: {PipelineName}", pipeline.Name);

        var errors = new List<string>();
        var warnings = new List<string>();

        try
        {
            // 1. Validate basic pipeline structure
            ValidatePipelineStructure(pipeline, errors);

            // 2. Validate individual plugin configurations
            await ValidateIndividualPluginsAsync(pipeline, errors, warnings);

            // 3. Validate plugin chain compatibility (schema flow)
            await ValidatePluginChainCompatibilityAsync(pipeline, errors, warnings);

            // 4. Validate connections and data flow
            ValidateConnectionConfiguration(pipeline, errors, warnings);

            // 5. Validate resource limits and constraints
            ValidateResourceConfiguration(pipeline, warnings);

            var isValid = errors.Count == 0;
            var result = new PipelineValidationResult
            {
                IsValid = isValid,
                Errors = errors,
                Warnings = warnings,
                PipelineName = pipeline.Name,
                ValidatedPluginCount = pipeline.Plugins.Count,
                ValidatedConnectionCount = pipeline.Connections.Count
            };

            _logger.LogInformation("Pipeline validation completed for {PipelineName}: {Status} ({ErrorCount} errors, {WarningCount} warnings)",
                pipeline.Name, isValid ? "SUCCESS" : "FAILED", errors.Count, warnings.Count);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Pipeline validation failed with exception for {PipelineName}", pipeline.Name);
            errors.Add($"Validation failed with exception: {ex.Message}");

            return new PipelineValidationResult
            {
                IsValid = false,
                Errors = errors,
                Warnings = warnings,
                PipelineName = pipeline.Name,
                ValidatedPluginCount = 0,
                ValidatedConnectionCount = 0
            };
        }
    }

    /// <summary>
    /// Validates the basic structure of the pipeline.
    /// </summary>
    private void ValidatePipelineStructure(IPipelineConfiguration pipeline, List<string> errors)
    {
        _logger.LogDebug("Validating pipeline structure");

        // Check for required pipeline name
        if (string.IsNullOrWhiteSpace(pipeline.Name))
        {
            errors.Add("Pipeline must have a non-empty name");
        }

        // Check for at least one plugin
        if (pipeline.Plugins == null || pipeline.Plugins.Count == 0)
        {
            errors.Add("Pipeline must contain at least one plugin");
            return;
        }

        // Check for unique plugin names
        var pluginNames = pipeline.Plugins.Select(p => p.Name).ToList();
        var duplicateNames = pluginNames.GroupBy(n => n).Where(g => g.Count() > 1).Select(g => g.Key);
        foreach (var duplicateName in duplicateNames)
        {
            errors.Add($"Duplicate plugin name: '{duplicateName}'");
        }

        // Pipeline must have at least one source and one sink
        var pluginTypes = pipeline.Plugins.Select(p => GetPluginCategoryFromType(p.Type)).ToList();
        
        if (!pluginTypes.Contains(PluginCategory.Source))
        {
            errors.Add("Pipeline must contain at least one source plugin");
        }

        if (!pluginTypes.Contains(PluginCategory.Sink))
        {
            errors.Add("Pipeline must contain at least one sink plugin");
        }

        // Validate connections reference existing plugins
        if (pipeline.Connections != null)
        {
            var pluginNameSet = new HashSet<string>(pluginNames, StringComparer.OrdinalIgnoreCase);
            
            foreach (var connection in pipeline.Connections)
            {
                if (!pluginNameSet.Contains(connection.From))
                {
                    errors.Add($"Connection references unknown source plugin: '{connection.From}'");
                }
                
                if (!pluginNameSet.Contains(connection.To))
                {
                    errors.Add($"Connection references unknown destination plugin: '{connection.To}'");
                }
            }
        }
    }

    /// <summary>
    /// Validates individual plugin configurations.
    /// </summary>
    private async Task ValidateIndividualPluginsAsync(IPipelineConfiguration pipeline, List<string> errors, List<string> warnings)
    {
        _logger.LogDebug("Validating individual plugin configurations");

        foreach (var plugin in pipeline.Plugins)
        {
            try
            {
                // Validate using plugin-specific provider if available
                var validationResult = await _providerRegistry.ValidatePluginConfigurationAsync(plugin.Type, plugin);
                
                if (!validationResult.IsValid)
                {
                    foreach (var error in validationResult.Errors)
                    {
                        errors.Add($"Plugin '{plugin.Name}' ({plugin.Type}): {error}");
                    }
                }

                if (validationResult.Warnings != null)
                {
                    foreach (var warning in validationResult.Warnings)
                    {
                        warnings.Add($"Plugin '{plugin.Name}' ({plugin.Type}): {warning}");
                    }
                }

                // Validate plugin type is known
                var provider = _providerRegistry.GetProvider(plugin.Type);
                if (provider == null)
                {
                    warnings.Add($"No configuration provider found for plugin type '{plugin.Type}' in plugin '{plugin.Name}'. Using generic configuration.");
                }
            }
            catch (Exception ex)
            {
                errors.Add($"Failed to validate plugin '{plugin.Name}' ({plugin.Type}): {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Validates plugin chain compatibility by checking schema flow between connected plugins.
    /// </summary>
    private async Task ValidatePluginChainCompatibilityAsync(IPipelineConfiguration pipeline, List<string> errors, List<string> warnings)
    {
        _logger.LogDebug("Validating plugin chain compatibility");

        if (pipeline.Connections == null || pipeline.Connections.Count == 0)
        {
            // If no explicit connections, try to infer simple linear chain
            if (pipeline.Plugins.Count > 1)
            {
                warnings.Add("No explicit connections defined. Consider adding connection configuration for complex pipelines.");
                await ValidateLinearChainCompatibilityAsync(pipeline, errors, warnings);
            }
            return;
        }

        // Build plugin lookup
        var pluginLookup = pipeline.Plugins.ToDictionary(p => p.Name, StringComparer.OrdinalIgnoreCase);

        // Validate each connection
        foreach (var connection in pipeline.Connections)
        {
            try
            {
                if (!pluginLookup.TryGetValue(connection.From, out var sourcePlugin) ||
                    !pluginLookup.TryGetValue(connection.To, out var destPlugin))
                {
                    continue; // Already validated in structure validation
                }

                await ValidateConnectionCompatibilityAsync(sourcePlugin, destPlugin, connection, errors, warnings);
            }
            catch (Exception ex)
            {
                errors.Add($"Failed to validate connection from '{connection.From}' to '{connection.To}': {ex.Message}");
            }
        }

        // Validate no orphaned plugins (except sources and sinks)
        ValidatePluginConnectivity(pipeline, warnings);
    }

    /// <summary>
    /// Validates compatibility between two connected plugins.
    /// </summary>
    private async Task ValidateConnectionCompatibilityAsync(
        IPluginDefinition sourcePlugin, 
        IPluginDefinition destPlugin, 
        IConnectionConfiguration connection,
        List<string> errors, 
        List<string> warnings)
    {
        _logger.LogDebug("Validating connection from {SourcePlugin} to {DestPlugin}", sourcePlugin.Name, destPlugin.Name);

        // Get providers for both plugins
        var sourceProvider = _providerRegistry.GetProvider(sourcePlugin.Type);
        var destProvider = _providerRegistry.GetProvider(destPlugin.Type);

        if (sourceProvider == null || destProvider == null)
        {
            // Can't validate schema compatibility without providers
            warnings.Add($"Cannot validate schema compatibility between '{sourcePlugin.Name}' and '{destPlugin.Name}' - missing configuration providers");
            return;
        }

        try
        {
            // Create configurations to get schemas
            var sourceConfig = await sourceProvider.CreateConfigurationAsync(sourcePlugin);
            var destConfig = await destProvider.CreateConfigurationAsync(destPlugin);

            // Get output schema from source
            var outputSchema = sourceProvider.GetOutputSchema(sourceConfig);
            if (outputSchema == null)
            {
                warnings.Add($"Source plugin '{sourcePlugin.Name}' does not define an output schema");
                return;
            }

            // Get input requirements from destination
            var inputRequirement = destProvider.GetInputSchemaRequirement();
            if (inputRequirement == null)
            {
                // Destination accepts any schema - this is fine
                _logger.LogDebug("Destination plugin '{DestPlugin}' accepts any input schema", destPlugin.Name);
                return;
            }

            // Validate compatibility
            var compatibilityResult = inputRequirement.ValidateSchema(outputSchema);
            if (!compatibilityResult.IsValid)
            {
                foreach (var error in compatibilityResult.Errors)
                {
                    errors.Add($"Schema incompatibility between '{sourcePlugin.Name}' and '{destPlugin.Name}': {error}");
                }
            }

            if (compatibilityResult.Warnings != null)
            {
                foreach (var warning in compatibilityResult.Warnings)
                {
                    warnings.Add($"Schema warning between '{sourcePlugin.Name}' and '{destPlugin.Name}': {warning}");
                }
            }
        }
        catch (Exception ex)
        {
            errors.Add($"Failed to validate schema compatibility between '{sourcePlugin.Name}' and '{destPlugin.Name}': {ex.Message}");
        }
    }

    /// <summary>
    /// Validates linear chain compatibility when no explicit connections are defined.
    /// </summary>
    private async Task ValidateLinearChainCompatibilityAsync(IPipelineConfiguration pipeline, List<string> errors, List<string> warnings)
    {
        var orderedPlugins = pipeline.Plugins.ToList();
        
        for (int i = 0; i < orderedPlugins.Count - 1; i++)
        {
            var sourcePlugin = orderedPlugins[i];
            var destPlugin = orderedPlugins[i + 1];

            // Create a synthetic connection for validation
            var syntheticConnection = new SyntheticConnection
            {
                From = sourcePlugin.Name,
                To = destPlugin.Name
            };

            await ValidateConnectionCompatibilityAsync(sourcePlugin, destPlugin, syntheticConnection, errors, warnings);
        }
    }

    /// <summary>
    /// Validates connection configuration for data flow correctness.
    /// </summary>
    private void ValidateConnectionConfiguration(IPipelineConfiguration pipeline, List<string> errors, List<string> warnings)
    {
        if (pipeline.Connections == null)
            return;

        _logger.LogDebug("Validating connection configuration");

        foreach (var connection in pipeline.Connections)
        {
            // Validate channel configuration if present
            if (connection.Channel != null)
            {
                if (connection.Channel.BufferSize <= 0)
                {
                    errors.Add($"Connection from '{connection.From}' to '{connection.To}' has invalid buffer size: {connection.Channel.BufferSize}");
                }

                if (connection.Channel.BackpressureThreshold < 0 || connection.Channel.BackpressureThreshold > 100)
                {
                    errors.Add($"Connection from '{connection.From}' to '{connection.To}' has invalid backpressure threshold: {connection.Channel.BackpressureThreshold}% (must be 0-100)");
                }

                if (connection.Channel.Timeout <= TimeSpan.Zero)
                {
                    errors.Add($"Connection from '{connection.From}' to '{connection.To}' has invalid timeout: {connection.Channel.Timeout}");
                }
            }
        }
    }

    /// <summary>
    /// Validates resource configuration and limits.
    /// </summary>
    private void ValidateResourceConfiguration(IPipelineConfiguration pipeline, List<string> warnings)
    {
        _logger.LogDebug("Validating resource configuration");

        // Check global resource limits
        if (pipeline.Settings?.DefaultResourceLimits != null)
        {
            ValidateResourceLimits(pipeline.Settings.DefaultResourceLimits, "Global default", warnings);
        }

        // Individual plugin resource limits are validated at the IPluginConfiguration level,
        // not at the IPluginDefinition level during pipeline validation.
        // This validation would need to happen after plugin configurations are created.
    }

    /// <summary>
    /// Validates resource limits for reasonableness.
    /// </summary>
    private void ValidateResourceLimits(IResourceLimits limits, string context, List<string> warnings)
    {
        if (limits.MaxMemoryMB.HasValue)
        {
            if (limits.MaxMemoryMB <= 0)
            {
                warnings.Add($"{context} has invalid memory limit: {limits.MaxMemoryMB}MB");
            }
            else if (limits.MaxMemoryMB > 16384) // 16GB
            {
                warnings.Add($"{context} has very high memory limit: {limits.MaxMemoryMB}MB - consider if this is intended");
            }
        }

        if (limits.TimeoutSeconds.HasValue)
        {
            if (limits.TimeoutSeconds <= 0)
            {
                warnings.Add($"{context} has invalid timeout: {limits.TimeoutSeconds}s");
            }
            else if (limits.TimeoutSeconds > 3600) // 1 hour
            {
                warnings.Add($"{context} has very long timeout: {limits.TimeoutSeconds}s - consider if this is intended");
            }
        }
    }

    /// <summary>
    /// Validates that plugins are properly connected (no orphans except sources and sinks).
    /// </summary>
    private void ValidatePluginConnectivity(IPipelineConfiguration pipeline, List<string> warnings)
    {
        if (pipeline.Connections == null || pipeline.Connections.Count == 0)
            return;

        var connectionSources = new HashSet<string>(pipeline.Connections.Select(c => c.From), StringComparer.OrdinalIgnoreCase);
        var connectionDestinations = new HashSet<string>(pipeline.Connections.Select(c => c.To), StringComparer.OrdinalIgnoreCase);

        foreach (var plugin in pipeline.Plugins)
        {
            var category = GetPluginCategoryFromType(plugin.Type);
            var isConnectedAsSource = connectionSources.Contains(plugin.Name);
            var isConnectedAsDestination = connectionDestinations.Contains(plugin.Name);

            // Check for orphaned plugins
            if (!isConnectedAsSource && !isConnectedAsDestination)
            {
                warnings.Add($"Plugin '{plugin.Name}' is not connected to any other plugins");
            }
            
            // Sources should have outgoing connections
            if (category == PluginCategory.Source && !isConnectedAsSource)
            {
                warnings.Add($"Source plugin '{plugin.Name}' has no outgoing connections");
            }
            
            // Sinks should have incoming connections
            if (category == PluginCategory.Sink && !isConnectedAsDestination)
            {
                warnings.Add($"Sink plugin '{plugin.Name}' has no incoming connections");
            }
        }
    }

    /// <summary>
    /// Determines plugin category from plugin type name.
    /// </summary>
    private PluginCategory GetPluginCategoryFromType(string pluginType)
    {
        var provider = _providerRegistry.GetProvider(pluginType);
        if (provider != null)
        {
            return provider.GetPluginMetadata().Category;
        }

        // Fallback to type name analysis
        var typeLower = pluginType.ToLowerInvariant();
        if (typeLower.Contains("source"))
            return PluginCategory.Source;
        if (typeLower.Contains("sink"))
            return PluginCategory.Sink;
        
        return PluginCategory.Transform; // Default assumption
    }
}

/// <summary>
/// Result of comprehensive pipeline validation.
/// </summary>
public class PipelineValidationResult
{
    /// <summary>
    /// Gets whether the pipeline validation passed without errors.
    /// </summary>
    public bool IsValid { get; init; }

    /// <summary>
    /// Gets validation errors that must be fixed before pipeline execution.
    /// </summary>
    public IReadOnlyList<string> Errors { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Gets validation warnings that should be reviewed but don't prevent execution.
    /// </summary>
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Gets the name of the validated pipeline.
    /// </summary>
    public string PipelineName { get; init; } = string.Empty;

    /// <summary>
    /// Gets the number of plugins that were validated.
    /// </summary>
    public int ValidatedPluginCount { get; init; }

    /// <summary>
    /// Gets the number of connections that were validated.
    /// </summary>
    public int ValidatedConnectionCount { get; init; }

    /// <summary>
    /// Gets additional validation metadata and statistics.
    /// </summary>
    public IReadOnlyDictionary<string, object>? Metadata { get; init; }

    /// <summary>
    /// Creates a successful validation result.
    /// </summary>
    public static PipelineValidationResult Success(string pipelineName, int pluginCount, int connectionCount) =>
        new()
        {
            IsValid = true,
            PipelineName = pipelineName,
            ValidatedPluginCount = pluginCount,
            ValidatedConnectionCount = connectionCount
        };

    /// <summary>
    /// Creates a failed validation result.
    /// </summary>
    public static PipelineValidationResult Failure(string pipelineName, params string[] errors) =>
        new()
        {
            IsValid = false,
            PipelineName = pipelineName,
            Errors = errors
        };
}

/// <summary>
/// Synthetic connection for validation when no explicit connections are defined.
/// </summary>
internal class SyntheticConnection : IConnectionConfiguration
{
    public string From { get; init; } = string.Empty;
    public string To { get; init; } = string.Empty;
    public string? FromPort { get; init; }
    public string? ToPort { get; init; }
    public IChannelConfiguration? Channel { get; init; }
}