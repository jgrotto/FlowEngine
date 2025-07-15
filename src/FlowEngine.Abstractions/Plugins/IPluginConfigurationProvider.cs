using FlowEngine.Abstractions.Configuration;
using FlowEngine.Abstractions.Data;
using System;
using System.Threading.Tasks;
using ValidationResult = FlowEngine.Abstractions.ValidationResult;

namespace FlowEngine.Abstractions.Plugins;

/// <summary>
/// Allows plugins to provide their own configuration creation logic with comprehensive schema validation.
/// This enables true plugin extensibility where 3rd party developers can create plugins without 
/// modifying Core infrastructure.
/// </summary>
public interface IPluginConfigurationProvider
{
    /// <summary>
    /// Determines if this provider can handle the specified plugin type.
    /// </summary>
    /// <param name="pluginTypeName">The full type name of the plugin (e.g., "MyNamespace.MyPlugin")</param>
    /// <returns>True if this provider can create configuration for the plugin type</returns>
    bool CanHandle(string pluginTypeName);
    
    /// <summary>
    /// Creates configuration for the plugin from YAML definition.
    /// This method is called after configuration validation passes.
    /// </summary>
    /// <param name="definition">The plugin definition from YAML configuration</param>
    /// <returns>A fully configured plugin configuration instance</returns>
    Task<IPluginConfiguration> CreateConfigurationAsync(IPluginDefinition definition);
    
    /// <summary>
    /// Plugin type name this provider handles (e.g., "MyNamespace.MyPlugin").
    /// Used for provider registration and lookup.
    /// </summary>
    string PluginTypeName { get; }
    
    /// <summary>
    /// Configuration type this provider creates (e.g., typeof(MyPluginConfiguration)).
    /// Used for type validation and metadata.
    /// </summary>
    Type ConfigurationType { get; }
    
    // === SCHEMA VALIDATION METHODS ===
    
    /// <summary>
    /// Gets the JSON schema for validating this plugin's configuration.
    /// This schema is used by:
    /// - Configuration validation before plugin creation
    /// - UI form generation for plugin configuration
    /// - Documentation generation
    /// - IDE/tooling support for YAML editing
    /// </summary>
    /// <returns>A valid JSON Schema (draft-07 or later) as string</returns>
    string GetConfigurationJsonSchema();
    
    /// <summary>
    /// Validates a configuration definition before creating the plugin configuration.
    /// This provides early error detection with specific, actionable error messages.
    /// Called before CreateConfigurationAsync to fail fast on invalid configurations.
    /// </summary>
    /// <param name="definition">The plugin definition to validate</param>
    /// <returns>Validation result with success status and any error messages</returns>
    ValidationResult ValidateConfiguration(IPluginDefinition definition);
    
    /// <summary>
    /// Gets expected input schema requirements for this plugin (null if accepts any).
    /// Used for pipeline compatibility validation to ensure plugin chains are compatible.
    /// </summary>
    /// <returns>Schema requirements for input data, or null if plugin accepts any schema</returns>
    ISchemaRequirement? GetInputSchemaRequirement();
    
    /// <summary>
    /// Determines output schema from the provided configuration.
    /// Used for downstream plugin compatibility validation and pipeline planning.
    /// </summary>
    /// <param name="configuration">The plugin configuration instance</param>
    /// <returns>The schema this plugin will output when configured with the provided configuration</returns>
    ISchema GetOutputSchema(IPluginConfiguration configuration);
    
    /// <summary>
    /// Gets plugin metadata for discovery, documentation, and UI presentation.
    /// This metadata helps users understand plugin capabilities and requirements.
    /// </summary>
    /// <returns>Rich metadata about the plugin including description, version, author, etc.</returns>
    PluginMetadata GetPluginMetadata();
}