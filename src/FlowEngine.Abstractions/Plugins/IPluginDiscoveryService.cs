namespace FlowEngine.Abstractions.Plugins;

/// <summary>
/// Service for discovering plugins with manifest support and advanced directory scanning.
/// </summary>
public interface IPluginDiscoveryService
{
    /// <summary>
    /// Discovers plugins in the specified directory with manifest support.
    /// </summary>
    /// <param name="directoryPath">Directory to scan for plugins</param>
    /// <param name="includeSubdirectories">Whether to scan subdirectories</param>
    /// <returns>Collection of discovered plugin information</returns>
    Task<IReadOnlyCollection<DiscoveredPlugin>> DiscoverPluginsAsync(string directoryPath, bool includeSubdirectories = true);

    /// <summary>
    /// Discovers plugins in the default plugin directories.
    /// </summary>
    /// <returns>Collection of discovered plugin information</returns>
    Task<IReadOnlyCollection<DiscoveredPlugin>> DiscoverPluginsAsync();

    /// <summary>
    /// Discovers a single plugin from a manifest file.
    /// </summary>
    /// <param name="manifestFilePath">Path to plugin.json manifest file</param>
    /// <returns>Discovered plugin information</returns>
    Task<DiscoveredPlugin?> DiscoverPluginFromManifestAsync(string manifestFilePath);

    /// <summary>
    /// Validates plugin dependencies and compatibility.
    /// </summary>
    /// <param name="plugin">Plugin to validate</param>
    /// <returns>Validation result with dependency check results</returns>
    Task<PluginValidationResult> ValidatePluginAsync(DiscoveredPlugin plugin);

    /// <summary>
    /// Gets the default plugin directories that are automatically scanned.
    /// </summary>
    /// <returns>Collection of default plugin directory paths</returns>
    IReadOnlyCollection<string> GetDefaultPluginDirectories();
}

/// <summary>
/// Represents a discovered plugin with manifest and assembly information.
/// </summary>
public sealed record DiscoveredPlugin
{
    /// <summary>
    /// Gets the plugin manifest information.
    /// </summary>
    public required IPluginManifest Manifest { get; init; }

    /// <summary>
    /// Gets the path to the plugin directory.
    /// </summary>
    public required string DirectoryPath { get; init; }

    /// <summary>
    /// Gets the path to the manifest file.
    /// </summary>
    public required string ManifestFilePath { get; init; }

    /// <summary>
    /// Gets the path to the plugin assembly.
    /// </summary>
    public required string AssemblyPath { get; init; }

    /// <summary>
    /// Gets whether the plugin was discovered from a manifest file.
    /// </summary>
    public required bool HasManifest { get; init; }

    /// <summary>
    /// Gets the plugin type information if assembly scanning was performed.
    /// </summary>
    public PluginTypeInfo? TypeInfo { get; init; }

    /// <summary>
    /// Gets the discovery timestamp.
    /// </summary>
    public DateTime DiscoveredAt { get; init; } = DateTime.UtcNow;
}

/// <summary>
/// Result of plugin validation including dependency checks.
/// </summary>
public sealed record PluginValidationResult
{
    /// <summary>
    /// Gets whether the plugin is valid.
    /// </summary>
    public required bool IsValid { get; init; }

    /// <summary>
    /// Gets validation error messages.
    /// </summary>
    public IReadOnlyList<string> Errors { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Gets validation warning messages.
    /// </summary>
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Gets dependency validation results.
    /// </summary>
    public IReadOnlyList<DependencyValidationResult> DependencyResults { get; init; } = Array.Empty<DependencyValidationResult>();

    /// <summary>
    /// Creates a successful validation result.
    /// </summary>
    public static PluginValidationResult Success() => new() { IsValid = true };

    /// <summary>
    /// Creates a failed validation result.
    /// </summary>
    public static PluginValidationResult Failure(params string[] errors) => new()
    {
        IsValid = false,
        Errors = errors.ToArray()
    };
}

/// <summary>
/// Result of dependency validation.
/// </summary>
public sealed record DependencyValidationResult
{
    /// <summary>
    /// Gets the dependency information.
    /// </summary>
    public required PluginDependency Dependency { get; init; }

    /// <summary>
    /// Gets whether the dependency is satisfied.
    /// </summary>
    public required bool IsSatisfied { get; init; }

    /// <summary>
    /// Gets the found version of the dependency (if any).
    /// </summary>
    public string? FoundVersion { get; init; }

    /// <summary>
    /// Gets the validation message.
    /// </summary>
    public string? Message { get; init; }
}
