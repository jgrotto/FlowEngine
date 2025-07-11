namespace FlowEngine.Abstractions.Plugins;

/// <summary>
/// Service for discovering plugins with manifest support, advanced directory scanning, and bootstrap scenarios.
/// Supports both manifest-based discovery and assembly-based discovery for different use cases.
/// </summary>
public interface IPluginDiscoveryService
{
    // === Manifest-Based Discovery (Existing) ===
    
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

    // === Bootstrap-Focused Discovery (New) ===
    
    /// <summary>
    /// Discovers plugins for bootstrap scenarios with assembly-based scanning and metadata extraction.
    /// This method is optimized for application startup and doesn't require manifest files.
    /// </summary>
    /// <param name="options">Bootstrap discovery options and filters</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Collection of discovered plugin information</returns>
    Task<IReadOnlyCollection<DiscoveredPlugin>> DiscoverPluginsForBootstrapAsync(
        PluginDiscoveryOptions? options = null,
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Gets standard plugin directories optimized for bootstrap scenarios.
    /// Includes development build paths and production deployment directories.
    /// </summary>
    /// <returns>Collection of standard plugin directory paths</returns>
    IReadOnlyCollection<string> GetStandardPluginDirectories();
    
    /// <summary>
    /// Validates a plugin assembly without loading it (optimized for bootstrap).
    /// </summary>
    /// <param name="assemblyPath">Path to the plugin assembly</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Plugin validation result</returns>
    Task<PluginValidationResult> ValidatePluginAssemblyAsync(string assemblyPath, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Scans a specific directory for plugin assemblies (bootstrap-optimized).
    /// </summary>
    /// <param name="directory">Directory to scan</param>
    /// <param name="recursive">Whether to scan subdirectories</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Collection of discovered plugin information</returns>
    Task<IReadOnlyCollection<DiscoveredPlugin>> ScanDirectoryForAssembliesAsync(
        string directory, 
        bool recursive = false, 
        CancellationToken cancellationToken = default);
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

    /// <summary>
    /// Gets plugin capabilities discovered during bootstrap scanning.
    /// </summary>
    public PluginCapabilities Capabilities { get; init; } = PluginCapabilities.None;

    /// <summary>
    /// Gets the plugin version information.
    /// </summary>
    public Version? Version { get; init; }

    /// <summary>
    /// Gets the plugin author information.
    /// </summary>
    public string? Author { get; init; }

    /// <summary>
    /// Gets the plugin description.
    /// </summary>
    public string? Description { get; init; }

    /// <summary>
    /// Gets whether this plugin was discovered during bootstrap (assembly scanning).
    /// </summary>
    public bool IsBootstrapDiscovered { get; init; } = false;
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

/// <summary>
/// Options for configuring plugin discovery behavior during bootstrap scenarios.
/// </summary>
public record PluginDiscoveryOptions
{
    /// <summary>
    /// Additional directories to search for plugins.
    /// </summary>
    public IReadOnlyCollection<string>? AdditionalDirectories { get; init; }
    
    /// <summary>
    /// Whether to include built-in plugins in discovery results.
    /// </summary>
    public bool IncludeBuiltInPlugins { get; init; } = true;
    
    /// <summary>
    /// Whether to scan subdirectories recursively.
    /// </summary>
    public bool ScanRecursively { get; init; } = false;
    
    /// <summary>
    /// Maximum time to spend on plugin discovery.
    /// </summary>
    public TimeSpan? DiscoveryTimeout { get; init; } = TimeSpan.FromSeconds(30);
    
    /// <summary>
    /// Filter plugins by specific capabilities.
    /// </summary>
    public PluginCapabilities? RequiredCapabilities { get; init; }
    
    /// <summary>
    /// Whether to perform detailed validation during discovery.
    /// </summary>
    public bool PerformValidation { get; init; } = true;
}

/// <summary>
/// Plugin capabilities flags.
/// </summary>
[Flags]
public enum PluginCapabilities
{
    /// <summary>
    /// No specific capabilities.
    /// </summary>
    None = 0,
    
    /// <summary>
    /// Plugin can act as a data source.
    /// </summary>
    Source = 1 << 0,
    
    /// <summary>
    /// Plugin can transform data.
    /// </summary>
    Transform = 1 << 1,
    
    /// <summary>
    /// Plugin can act as a data sink.
    /// </summary>
    Sink = 1 << 2,
    
    /// <summary>
    /// Plugin supports hot-swapping.
    /// </summary>
    HotSwap = 1 << 3,
    
    /// <summary>
    /// Plugin supports schema inference.
    /// </summary>
    SchemaInference = 1 << 4,
    
    /// <summary>
    /// Plugin supports streaming operations.
    /// </summary>
    Streaming = 1 << 5,
    
    /// <summary>
    /// Plugin supports batch operations.
    /// </summary>
    Batch = 1 << 6,
    
    /// <summary>
    /// Plugin has built-in validation.
    /// </summary>
    Validation = 1 << 7,
    
    /// <summary>
    /// Plugin supports custom configuration.
    /// </summary>
    CustomConfiguration = 1 << 8,
    
    /// <summary>
    /// All plugin types (source, transform, sink).
    /// </summary>
    All = Source | Transform | Sink
}
