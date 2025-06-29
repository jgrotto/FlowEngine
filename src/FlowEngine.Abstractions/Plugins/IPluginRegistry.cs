using System.Reflection;

namespace FlowEngine.Abstractions.Plugins;

/// <summary>
/// Interface for plugin type discovery and registration.
/// Provides services for cataloging and querying available plugin types.
/// </summary>
public interface IPluginRegistry
{
    /// <summary>
    /// Scans a directory for plugin assemblies and registers discovered plugin types.
    /// </summary>
    /// <param name="directoryPath">Directory to scan for plugin assemblies</param>
    /// <param name="searchPattern">File pattern to match (default: *.dll)</param>
    /// <returns>Task that completes when scanning is finished</returns>
    Task ScanDirectoryAsync(string directoryPath, string searchPattern = "*.dll");

    /// <summary>
    /// Scans an assembly for plugin types and registers them.
    /// </summary>
    /// <param name="assembly">Assembly to scan</param>
    void ScanAssembly(Assembly assembly);

    /// <summary>
    /// Scans an assembly file for plugin types and registers them.
    /// </summary>
    /// <param name="assemblyPath">Path to assembly file</param>
    void ScanAssemblyFile(string assemblyPath);

    /// <summary>
    /// Gets information about a specific plugin type.
    /// </summary>
    /// <param name="typeName">Full type name of the plugin</param>
    /// <returns>Plugin type information if found, null otherwise</returns>
    PluginTypeInfo? GetPluginType(string typeName);

    /// <summary>
    /// Gets all registered plugin types, optionally filtered by category.
    /// </summary>
    /// <param name="category">Optional category filter</param>
    /// <returns>Collection of plugin type information</returns>
    IReadOnlyCollection<PluginTypeInfo> GetPluginTypes(PluginCategory? category = null);

    /// <summary>
    /// Searches for plugin types matching a name pattern.
    /// Supports wildcards (* and ?).
    /// </summary>
    /// <param name="namePattern">Pattern to match against type names</param>
    /// <returns>Collection of matching plugin type information</returns>
    IReadOnlyCollection<PluginTypeInfo> SearchPluginTypes(string namePattern);

    /// <summary>
    /// Checks if a plugin type is registered.
    /// </summary>
    /// <param name="typeName">Full type name to check</param>
    /// <returns>True if the type is registered</returns>
    bool IsPluginTypeRegistered(string typeName);

    /// <summary>
    /// Manually registers a plugin type.
    /// </summary>
    /// <param name="pluginType">Plugin type to register</param>
    void RegisterPluginType(Type pluginType);

    /// <summary>
    /// Unregisters a plugin type.
    /// </summary>
    /// <param name="typeName">Full type name to unregister</param>
    void UnregisterPluginType(string typeName);

    /// <summary>
    /// Gets the names of all assemblies that have been scanned.
    /// </summary>
    /// <returns>Collection of scanned assembly names</returns>
    IReadOnlyCollection<string> GetScannedAssemblies();
}

/// <summary>
/// Information about a discovered plugin type.
/// </summary>
public sealed record PluginTypeInfo
{
    /// <summary>
    /// Gets the full type name of the plugin.
    /// </summary>
    public required string TypeName { get; init; }

    /// <summary>
    /// Gets the human-readable name of the plugin.
    /// </summary>
    public required string FriendlyName { get; init; }

    /// <summary>
    /// Gets the description of the plugin.
    /// </summary>
    public string? Description { get; init; }

    /// <summary>
    /// Gets the category of the plugin.
    /// </summary>
    public required PluginCategory Category { get; init; }

    /// <summary>
    /// Gets the version of the plugin.
    /// </summary>
    public string? Version { get; init; }

    /// <summary>
    /// Gets the path to the assembly containing the plugin.
    /// </summary>
    public required string AssemblyPath { get; init; }

    /// <summary>
    /// Gets the name of the assembly containing the plugin.
    /// </summary>
    public required string AssemblyName { get; init; }

    /// <summary>
    /// Gets whether this is a built-in FlowEngine plugin.
    /// </summary>
    public required bool IsBuiltIn { get; init; }

    /// <summary>
    /// Gets whether this plugin requires configuration.
    /// </summary>
    public required bool RequiresConfiguration { get; init; }

    /// <summary>
    /// Gets the schemas supported by this plugin.
    /// </summary>
    public IReadOnlyList<string> SupportedSchemas { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Gets additional metadata about the plugin.
    /// </summary>
    public IReadOnlyDictionary<string, object> Metadata { get; init; } = new Dictionary<string, object>();
}

/// <summary>
/// Categories of plugins.
/// </summary>
public enum PluginCategory
{
    /// <summary>
    /// Source plugins that read data from external systems.
    /// </summary>
    Source,

    /// <summary>
    /// Transform plugins that modify or enrich data.
    /// </summary>
    Transform,

    /// <summary>
    /// Sink plugins that write data to external systems.
    /// </summary>
    Sink,

    /// <summary>
    /// Other plugin types.
    /// </summary>
    Other
}