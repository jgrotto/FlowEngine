using System;
using System.Collections.Generic;

namespace FlowEngine.Abstractions.Plugins;

/// <summary>
/// Rich metadata about a plugin for discovery, documentation, and UI presentation.
/// Used to help users understand plugin capabilities, requirements, and usage.
/// </summary>
public class PluginMetadata
{
    /// <summary>
    /// Human-readable name of the plugin (e.g., "JavaScript Transform").
    /// </summary>
    public string Name { get; init; } = string.Empty;
    
    /// <summary>
    /// Detailed description of what the plugin does and its capabilities.
    /// Should explain the plugin's purpose and typical use cases.
    /// </summary>
    public string Description { get; init; } = string.Empty;
    
    /// <summary>
    /// Plugin version in semantic versioning format (e.g., "1.0.0").
    /// </summary>
    public string Version { get; init; } = string.Empty;
    
    /// <summary>
    /// Author or organization that created the plugin.
    /// </summary>
    public string Author { get; init; } = string.Empty;
    
    /// <summary>
    /// Primary category this plugin belongs to.
    /// Used for grouping and filtering in UI.
    /// </summary>
    public PluginCategory Category { get; init; }
    
    /// <summary>
    /// Tags for searchability and classification (e.g., ["csv", "file", "input"]).
    /// Used for plugin discovery and filtering.
    /// </summary>
    public IEnumerable<string> Tags { get; init; } = [];
    
    /// <summary>
    /// URL to plugin documentation (null if no documentation available).
    /// Should provide comprehensive usage examples and configuration reference.
    /// </summary>
    public Uri? DocumentationUrl { get; init; }
    
    /// <summary>
    /// URL to plugin source code (null if not public).
    /// Used for transparency and community contribution.
    /// </summary>
    public Uri? SourceUrl { get; init; }
    
    /// <summary>
    /// Minimum FlowEngine version required for this plugin.
    /// Used for compatibility checking.
    /// </summary>
    public string? MinimumEngineVersion { get; init; }
    
    /// <summary>
    /// License under which the plugin is distributed.
    /// </summary>
    public string? License { get; init; }
    
    /// <summary>
    /// Whether this plugin is considered stable for production use.
    /// </summary>
    public bool IsStable { get; init; } = true;
    
    /// <summary>
    /// Additional capabilities or features this plugin provides.
    /// Used for advanced filtering and capability-based selection.
    /// </summary>
    public IEnumerable<string> Capabilities { get; init; } = [];
}

