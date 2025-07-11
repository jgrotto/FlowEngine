using FlowEngine.Abstractions.Plugins;
using Microsoft.Extensions.Logging;
using System.Text.RegularExpressions;

namespace FlowEngine.Core.Configuration;

/// <summary>
/// Service for resolving short plugin type names to full type names and assembly paths.
/// Enables YAML configuration to use simple names like "DelimitedSource" instead of full qualified names.
/// </summary>
public interface IPluginTypeResolver
{
    /// <summary>
    /// Resolves a short plugin type name to full type information.
    /// </summary>
    /// <param name="shortTypeName">Short type name like "DelimitedSource"</param>
    /// <returns>Resolved plugin type information or null if not found</returns>
    Task<PluginTypeResolution?> ResolveTypeAsync(string shortTypeName);

    /// <summary>
    /// Scans available plugins to build the resolution cache.
    /// </summary>
    /// <returns>Task that completes when scanning is finished</returns>
    Task ScanAvailablePluginsAsync();
}

/// <summary>
/// Resolved plugin type information with full qualified names and assembly path.
/// </summary>
public sealed record PluginTypeResolution
{
    /// <summary>
    /// Gets the full qualified type name (e.g., "DelimitedSource.DelimitedSourcePlugin").
    /// </summary>
    public required string FullTypeName { get; init; }

    /// <summary>
    /// Gets the assembly path where the plugin is located.
    /// </summary>
    public required string AssemblyPath { get; init; }

    /// <summary>
    /// Gets the original plugin type information.
    /// </summary>
    public required PluginTypeInfo TypeInfo { get; init; }
}

/// <summary>
/// Implementation of plugin type resolver using the plugin registry.
/// </summary>
public sealed class PluginTypeResolver : IPluginTypeResolver
{
    private readonly IPluginRegistry _pluginRegistry;
    private readonly ILogger<PluginTypeResolver> _logger;
    private readonly Dictionary<string, PluginTypeResolution> _resolutionCache = new();
    private readonly object _cacheLock = new();
    private bool _isScanned = false;

    /// <summary>
    /// Initializes a new instance of the plugin type resolver.
    /// </summary>
    /// <param name="pluginRegistry">Plugin registry for discovering available plugins</param>
    /// <param name="logger">Logger for resolution operations</param>
    public PluginTypeResolver(IPluginRegistry pluginRegistry, ILogger<PluginTypeResolver> logger)
    {
        _pluginRegistry = pluginRegistry ?? throw new ArgumentNullException(nameof(pluginRegistry));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<PluginTypeResolution?> ResolveTypeAsync(string shortTypeName)
    {
        if (string.IsNullOrWhiteSpace(shortTypeName))
        {
            return null;
        }

        // Ensure we've scanned for available plugins
        if (!_isScanned)
        {
            await ScanAvailablePluginsAsync();
        }

        lock (_cacheLock)
        {
            // Try exact match first
            if (_resolutionCache.TryGetValue(shortTypeName, out var exactMatch))
            {
                _logger.LogDebug("Resolved '{ShortName}' to '{FullName}' via exact match",
                    shortTypeName, exactMatch.FullTypeName);
                return exactMatch;
            }

            // Try case-insensitive match
            var caseInsensitiveKey = _resolutionCache.Keys
                .FirstOrDefault(key => string.Equals(key, shortTypeName, StringComparison.OrdinalIgnoreCase));

            if (caseInsensitiveKey != null)
            {
                var match = _resolutionCache[caseInsensitiveKey];
                _logger.LogDebug("Resolved '{ShortName}' to '{FullName}' via case-insensitive match",
                    shortTypeName, match.FullTypeName);
                return match;
            }

            // Try pattern matching for common variations
            var patterns = GenerateTypeNamePatterns(shortTypeName);
            foreach (var pattern in patterns)
            {
                var matchingKey = _resolutionCache.Keys
                    .FirstOrDefault(key => Regex.IsMatch(key, pattern, RegexOptions.IgnoreCase));

                if (matchingKey != null)
                {
                    var match = _resolutionCache[matchingKey];
                    _logger.LogDebug("Resolved '{ShortName}' to '{FullName}' via pattern match '{Pattern}'",
                        shortTypeName, match.FullTypeName, pattern);
                    return match;
                }
            }
        }

        _logger.LogWarning("Could not resolve plugin type '{ShortName}'. Available types: {AvailableTypes}",
            shortTypeName, string.Join(", ", _resolutionCache.Keys));

        return null;
    }

    /// <inheritdoc />
    public async Task ScanAvailablePluginsAsync()
    {
        try
        {
            _logger.LogInformation("Scanning for available plugins to build type resolution cache");

            // Scan common plugin directories
            var pluginDirectories = GetPluginDirectories();
            foreach (var directory in pluginDirectories)
            {
                if (Directory.Exists(directory))
                {
                    _logger.LogDebug("Scanning plugin directory: {Directory}", directory);
                    await _pluginRegistry.ScanDirectoryAsync(directory);
                }
            }

            // Build resolution cache from discovered plugins
            lock (_cacheLock)
            {
                _resolutionCache.Clear();
                var allPluginTypes = _pluginRegistry.GetPluginTypes();

                foreach (var pluginType in allPluginTypes)
                {
                    var shortNames = GenerateShortNames(pluginType);
                    foreach (var shortName in shortNames)
                    {
                        if (!_resolutionCache.ContainsKey(shortName))
                        {
                            var resolution = new PluginTypeResolution
                            {
                                FullTypeName = pluginType.TypeName,
                                AssemblyPath = pluginType.AssemblyPath,
                                TypeInfo = pluginType
                            };

                            _resolutionCache[shortName] = resolution;
                            _logger.LogDebug("Registered resolution: '{ShortName}' -> '{FullName}'",
                                shortName, pluginType.TypeName);
                        }
                    }
                }
            }

            _isScanned = true;
            _logger.LogInformation("Plugin type resolution cache built with {Count} entries", _resolutionCache.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to scan available plugins for type resolution");
            throw;
        }
    }

    /// <summary>
    /// Generates possible short names for a plugin type.
    /// </summary>
    /// <param name="pluginType">Plugin type information</param>
    /// <returns>Collection of possible short names</returns>
    private static List<string> GenerateShortNames(PluginTypeInfo pluginType)
    {
        var shortNames = new List<string>();
        var typeName = pluginType.TypeName;

        // Extract the last part of the namespace (e.g., "DelimitedSource" from "DelimitedSource.DelimitedSourcePlugin")
        var parts = typeName.Split('.');
        if (parts.Length >= 2)
        {
            var namespacePart = parts[^2]; // Second to last part (namespace)
            var classNamePart = parts[^1]; // Last part (class name)

            // Add namespace part as short name (e.g., "DelimitedSource")
            shortNames.Add(namespacePart);

            // Add class name without common suffixes
            var baseClassName = classNamePart
                .Replace("Plugin", "")
                .Replace("Service", "")
                .Replace("Processor", "");

            if (!string.IsNullOrEmpty(baseClassName) && baseClassName != namespacePart)
            {
                shortNames.Add(baseClassName);
            }
        }

        // Add friendly name if available
        if (!string.IsNullOrEmpty(pluginType.FriendlyName))
        {
            shortNames.Add(pluginType.FriendlyName);
        }

        // Add full type name for backward compatibility
        shortNames.Add(typeName);

        return shortNames.Distinct().ToList();
    }

    /// <summary>
    /// Generates regex patterns for matching plugin type names.
    /// </summary>
    /// <param name="shortTypeName">Short type name to generate patterns for</param>
    /// <returns>Collection of regex patterns</returns>
    private static List<string> GenerateTypeNamePatterns(string shortTypeName)
    {
        var patterns = new List<string>();
        var escapedName = Regex.Escape(shortTypeName);

        // Exact match
        patterns.Add($"^{escapedName}$");

        // With common suffixes
        patterns.Add($"^{escapedName}(Plugin|Service|Processor)$");

        // As part of namespace
        patterns.Add($"^{escapedName}\\.");

        // As class name part
        patterns.Add($"\\.{escapedName}(Plugin|Service|Processor)?$");

        return patterns;
    }

    /// <summary>
    /// Gets the list of directories to scan for plugins.
    /// </summary>
    /// <returns>Array of plugin directories</returns>
    private static string[] GetPluginDirectories()
    {
        var appDirectory = AppDomain.CurrentDomain.BaseDirectory;
        var directories = new List<string>();

        // Standard plugin directories
        directories.AddRange(new[]
        {
            Path.Combine(appDirectory, "plugins"),
            Path.Combine(appDirectory, "Plugins"),
            Path.Combine(appDirectory, "..", "plugins"),
            Path.Combine(appDirectory, "..", "..", "plugins"), // For development environment
            appDirectory // Current directory as fallback
        });

        // Development environment - specific plugin build directories
        var baseProjectPath = Path.Combine(appDirectory, "..", "..", "..", "..", "..");
        var pluginsPath = Path.Combine(baseProjectPath, "plugins");
        if (Directory.Exists(pluginsPath))
        {
            var pluginProjects = Directory.GetDirectories(pluginsPath);
            foreach (var pluginProject in pluginProjects)
            {
                // Add both Debug and Release directories
                var debugPath = Path.Combine(pluginProject, "bin", "Debug", "net8.0");
                var releasePath = Path.Combine(pluginProject, "bin", "Release", "net8.0");

                if (Directory.Exists(debugPath))
                {
                    directories.Add(debugPath);
                }

                if (Directory.Exists(releasePath))
                {
                    directories.Add(releasePath);
                }
            }
        }

        return directories.ToArray();
    }
}
