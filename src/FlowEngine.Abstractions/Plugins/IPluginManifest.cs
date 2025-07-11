namespace FlowEngine.Abstractions.Plugins;

/// <summary>
/// Represents a plugin manifest that provides metadata and configuration for plugin discovery.
/// </summary>
public interface IPluginManifest
{
    /// <summary>
    /// Gets the unique identifier for the plugin.
    /// </summary>
    string Id { get; }

    /// <summary>
    /// Gets the name of the plugin.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Gets the version of the plugin.
    /// </summary>
    string Version { get; }

    /// <summary>
    /// Gets the description of the plugin.
    /// </summary>
    string? Description { get; }

    /// <summary>
    /// Gets the author of the plugin.
    /// </summary>
    string? Author { get; }

    /// <summary>
    /// Gets the category of the plugin.
    /// </summary>
    PluginCategory Category { get; }

    /// <summary>
    /// Gets the main assembly file name for the plugin.
    /// </summary>
    string AssemblyFileName { get; }

    /// <summary>
    /// Gets the main plugin type name.
    /// </summary>
    string PluginTypeName { get; }

    /// <summary>
    /// Gets the plugin dependencies.
    /// </summary>
    IReadOnlyList<PluginDependency> Dependencies { get; }

    /// <summary>
    /// Gets the supported input schemas.
    /// </summary>
    IReadOnlyList<string> SupportedInputSchemas { get; }

    /// <summary>
    /// Gets the supported output schemas.
    /// </summary>
    IReadOnlyList<string> SupportedOutputSchemas { get; }

    /// <summary>
    /// Gets whether this plugin requires configuration.
    /// </summary>
    bool RequiresConfiguration { get; }

    /// <summary>
    /// Gets the configuration schema file path (relative to plugin directory).
    /// </summary>
    string? ConfigurationSchemaFile { get; }

    /// <summary>
    /// Gets additional metadata about the plugin.
    /// </summary>
    IReadOnlyDictionary<string, object> Metadata { get; }

    /// <summary>
    /// Gets the minimum FlowEngine version required.
    /// </summary>
    string? MinimumFlowEngineVersion { get; }
}

/// <summary>
/// Represents a plugin dependency.
/// </summary>
public sealed record PluginDependency
{
    /// <summary>
    /// Gets the name of the dependency.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Gets the version constraint for the dependency.
    /// </summary>
    public required string Version { get; init; }

    /// <summary>
    /// Gets whether this dependency is optional.
    /// </summary>
    public bool IsOptional { get; init; }
}

/// <summary>
/// Default implementation of IPluginManifest.
/// </summary>
public sealed record PluginManifest : IPluginManifest
{
    /// <inheritdoc />
    public required string Id { get; init; }

    /// <inheritdoc />
    public required string Name { get; init; }

    /// <inheritdoc />
    public required string Version { get; init; }

    /// <inheritdoc />
    public string? Description { get; init; }

    /// <inheritdoc />
    public string? Author { get; init; }

    /// <inheritdoc />
    public required PluginCategory Category { get; init; }

    /// <inheritdoc />
    public required string AssemblyFileName { get; init; }

    /// <inheritdoc />
    public required string PluginTypeName { get; init; }

    /// <inheritdoc />
    public IReadOnlyList<PluginDependency> Dependencies { get; init; } = Array.Empty<PluginDependency>();

    /// <inheritdoc />
    public IReadOnlyList<string> SupportedInputSchemas { get; init; } = Array.Empty<string>();

    /// <inheritdoc />
    public IReadOnlyList<string> SupportedOutputSchemas { get; init; } = Array.Empty<string>();

    /// <inheritdoc />
    public bool RequiresConfiguration { get; init; }

    /// <inheritdoc />
    public string? ConfigurationSchemaFile { get; init; }

    /// <inheritdoc />
    public IReadOnlyDictionary<string, object> Metadata { get; init; } = new Dictionary<string, object>();

    /// <inheritdoc />
    public string? MinimumFlowEngineVersion { get; init; }
}
