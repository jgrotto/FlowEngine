using FlowEngine.Abstractions.Plugins;
using Microsoft.Extensions.Logging;
using System.Reflection;
using System.Text.Json;

namespace FlowEngine.Core.Plugins;

/// <summary>
/// Service for discovering plugins with manifest support and advanced directory scanning.
/// </summary>
public sealed class PluginDiscoveryService : IPluginDiscoveryService
{
    private readonly IPluginRegistry _pluginRegistry;
    private readonly ILogger<PluginDiscoveryService> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    /// <summary>
    /// Initializes a new instance of the plugin discovery service.
    /// </summary>
    public PluginDiscoveryService(IPluginRegistry pluginRegistry, ILogger<PluginDiscoveryService> logger)
    {
        _pluginRegistry = pluginRegistry ?? throw new ArgumentNullException(nameof(pluginRegistry));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<IReadOnlyCollection<DiscoveredPlugin>> DiscoverPluginsAsync(string directoryPath, bool includeSubdirectories = true)
    {
        if (string.IsNullOrWhiteSpace(directoryPath))
        {
            throw new ArgumentException("Directory path cannot be null or empty", nameof(directoryPath));
        }

        if (!Directory.Exists(directoryPath))
        {
            _logger.LogWarning("Plugin directory does not exist: {DirectoryPath}", directoryPath);
            return Array.Empty<DiscoveredPlugin>();
        }

        var discoveredPlugins = new List<DiscoveredPlugin>();

        try
        {
            // First, look for plugin.json manifest files
            var searchOption = includeSubdirectories ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
            var manifestFiles = Directory.GetFiles(directoryPath, "plugin.json", searchOption);

            foreach (var manifestFile in manifestFiles)
            {
                try
                {
                    var plugin = await DiscoverPluginFromManifestAsync(manifestFile);
                    if (plugin != null)
                    {
                        discoveredPlugins.Add(plugin);
                        _logger.LogDebug("Discovered plugin from manifest: {PluginName} at {Path}",
                            plugin.Manifest.Name, plugin.DirectoryPath);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to load plugin manifest: {ManifestPath}", manifestFile);
                }
            }

            // Then, scan for assemblies without manifests
            await ScanForAssemblyBasedPluginsAsync(directoryPath, includeSubdirectories, discoveredPlugins);

            // Remove duplicates based on plugin ID
            var uniquePlugins = discoveredPlugins
                .GroupBy(p => p.Manifest.Id)
                .Select(g => g.OrderByDescending(p => p.HasManifest).First())
                .ToList();

            _logger.LogInformation("Discovered {TotalCount} plugins in directory: {DirectoryPath} ({UniqueCount} unique)",
                discoveredPlugins.Count, directoryPath, uniquePlugins.Count);

            return uniquePlugins.AsReadOnly();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during plugin discovery in directory: {DirectoryPath}", directoryPath);
            return Array.Empty<DiscoveredPlugin>();
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyCollection<DiscoveredPlugin>> DiscoverPluginsAsync()
    {
        var allPlugins = new List<DiscoveredPlugin>();
        var directories = GetDefaultPluginDirectories();

        foreach (var directory in directories)
        {
            if (Directory.Exists(directory))
            {
                var plugins = await DiscoverPluginsAsync(directory, includeSubdirectories: true);
                allPlugins.AddRange(plugins);
            }
        }

        // Remove duplicates based on plugin ID
        var uniquePlugins = allPlugins
            .GroupBy(p => p.Manifest.Id)
            .Select(g => g.OrderByDescending(p => p.HasManifest).First())
            .ToList();

        _logger.LogInformation("Discovered {TotalCount} plugins across {DirectoryCount} directories ({UniqueCount} unique)",
            allPlugins.Count, directories.Count, uniquePlugins.Count);

        return uniquePlugins.AsReadOnly();
    }

    /// <inheritdoc />
    public async Task<DiscoveredPlugin?> DiscoverPluginFromManifestAsync(string manifestFilePath)
    {
        if (string.IsNullOrWhiteSpace(manifestFilePath))
        {
            throw new ArgumentException("Manifest file path cannot be null or empty", nameof(manifestFilePath));
        }

        if (!File.Exists(manifestFilePath))
        {
            _logger.LogWarning("Plugin manifest file does not exist: {ManifestPath}", manifestFilePath);
            return null;
        }

        try
        {
            var manifestJson = await File.ReadAllTextAsync(manifestFilePath);
            var manifestData = JsonSerializer.Deserialize<PluginManifestData>(manifestJson, JsonOptions);

            if (manifestData == null)
            {
                _logger.LogWarning("Failed to deserialize plugin manifest: {ManifestPath}", manifestFilePath);
                return null;
            }

            var manifest = CreateManifestFromData(manifestData);
            var pluginDirectory = Path.GetDirectoryName(manifestFilePath)!;
            var assemblyPath = Path.Combine(pluginDirectory, manifest.AssemblyFileName);

            if (!File.Exists(assemblyPath))
            {
                _logger.LogWarning("Plugin assembly not found: {AssemblyPath} (specified in {ManifestPath})",
                    assemblyPath, manifestFilePath);
                return null;
            }

            // Try to get type information from registry
            PluginTypeInfo? typeInfo = null;
            try
            {
                // Scan the assembly to get type information
                _pluginRegistry.ScanAssemblyFile(assemblyPath);
                typeInfo = _pluginRegistry.GetPluginType(manifest.PluginTypeName);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to scan plugin assembly for type info: {AssemblyPath}", assemblyPath);
            }

            return new DiscoveredPlugin
            {
                Manifest = manifest,
                DirectoryPath = pluginDirectory,
                ManifestFilePath = manifestFilePath,
                AssemblyPath = assemblyPath,
                HasManifest = true,
                TypeInfo = typeInfo
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading plugin manifest: {ManifestPath}", manifestFilePath);
            return null;
        }
    }

    /// <inheritdoc />
    public async Task<PluginValidationResult> ValidatePluginAsync(DiscoveredPlugin plugin)
    {
        var errors = new List<string>();
        var warnings = new List<string>();
        var dependencyResults = new List<DependencyValidationResult>();

        // Validate assembly exists and can be loaded
        if (!File.Exists(plugin.AssemblyPath))
        {
            errors.Add($"Plugin assembly not found: {plugin.AssemblyPath}");
        }
        else
        {
            try
            {
                // Try to load assembly metadata without fully loading it
                var assemblyName = AssemblyName.GetAssemblyName(plugin.AssemblyPath);
                _logger.LogDebug("Plugin assembly validation passed: {AssemblyName}", assemblyName.FullName);
            }
            catch (Exception ex)
            {
                errors.Add($"Failed to load plugin assembly: {ex.Message}");
            }
        }

        // Validate plugin type exists
        if (plugin.TypeInfo == null)
        {
            warnings.Add($"Plugin type '{plugin.Manifest.PluginTypeName}' not found in registry. Assembly may not have been scanned.");
        }

        // Validate dependencies
        var dependencyTasks = plugin.Manifest.Dependencies.Select(ValidateDependencyAsync).ToArray();
        var dependencyResultsArray = await Task.WhenAll(dependencyTasks);
        dependencyResults.AddRange(dependencyResultsArray);

        foreach (var dependencyResult in dependencyResultsArray)
        {
            if (!dependencyResult.IsSatisfied && !dependencyResult.Dependency.IsOptional)
            {
                errors.Add($"Required dependency not satisfied: {dependencyResult.Dependency.Name} {dependencyResult.Dependency.Version}");
            }
            else if (!dependencyResult.IsSatisfied && dependencyResult.Dependency.IsOptional)
            {
                warnings.Add($"Optional dependency not satisfied: {dependencyResult.Dependency.Name} {dependencyResult.Dependency.Version}");
            }
        }

        // Validate configuration schema if specified
        if (!string.IsNullOrEmpty(plugin.Manifest.ConfigurationSchemaFile))
        {
            var schemaPath = Path.Combine(plugin.DirectoryPath, plugin.Manifest.ConfigurationSchemaFile);
            if (!File.Exists(schemaPath))
            {
                errors.Add($"Configuration schema file not found: {schemaPath}");
            }
        }

        return new PluginValidationResult
        {
            IsValid = errors.Count == 0,
            Errors = errors.AsReadOnly(),
            Warnings = warnings.AsReadOnly(),
            DependencyResults = dependencyResults.AsReadOnly()
        };
    }

    /// <inheritdoc />
    public IReadOnlyCollection<string> GetDefaultPluginDirectories()
    {
        var appDirectory = AppDomain.CurrentDomain.BaseDirectory;
        var directories = new[]
        {
            Path.Combine(appDirectory, "plugins"),
            Path.Combine(appDirectory, "Plugins"),
            Path.Combine(appDirectory, "..", "plugins"),
            appDirectory // Current directory as fallback
        };

        return directories.ToArray();
    }

    /// <summary>
    /// Scans for assembly-based plugins that don't have manifest files.
    /// </summary>
    private Task ScanForAssemblyBasedPluginsAsync(string directoryPath, bool includeSubdirectories, List<DiscoveredPlugin> existingPlugins)
    {
        var searchOption = includeSubdirectories ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        var assemblyFiles = Directory.GetFiles(directoryPath, "*.dll", searchOption);

        // Get assembly paths that are already covered by manifest-based plugins
        var manifestedAssemblies = new HashSet<string>(
            existingPlugins.Select(p => Path.GetFullPath(p.AssemblyPath)),
            StringComparer.OrdinalIgnoreCase);

        foreach (var assemblyFile in assemblyFiles)
        {
            var fullAssemblyPath = Path.GetFullPath(assemblyFile);
            if (manifestedAssemblies.Contains(fullAssemblyPath))
            {
                continue; // Skip assemblies already discovered via manifest
            }

            try
            {
                // Scan assembly for plugin types
                _pluginRegistry.ScanAssemblyFile(assemblyFile);

                // Get plugin types from this assembly
                var assemblyPluginTypes = _pluginRegistry.GetPluginTypes()
                    .Where(pt => string.Equals(pt.AssemblyPath, fullAssemblyPath, StringComparison.OrdinalIgnoreCase))
                    .ToList();

                foreach (var pluginType in assemblyPluginTypes)
                {
                    // Create a synthetic manifest for assembly-based plugins
                    var manifest = CreateSyntheticManifest(pluginType);
                    var pluginDirectory = Path.GetDirectoryName(assemblyFile)!;

                    var discoveredPlugin = new DiscoveredPlugin
                    {
                        Manifest = manifest,
                        DirectoryPath = pluginDirectory,
                        ManifestFilePath = string.Empty,
                        AssemblyPath = assemblyFile,
                        HasManifest = false,
                        TypeInfo = pluginType
                    };

                    existingPlugins.Add(discoveredPlugin);
                    _logger.LogDebug("Discovered assembly-based plugin: {PluginName} in {AssemblyPath}",
                        pluginType.FriendlyName, assemblyFile);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to scan assembly for plugins: {AssemblyPath}", assemblyFile);
            }
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Creates a manifest from JSON data.
    /// </summary>
    private static PluginManifest CreateManifestFromData(PluginManifestData data)
    {
        return new PluginManifest
        {
            Id = data.Id ?? throw new InvalidOperationException("Plugin ID is required"),
            Name = data.Name ?? throw new InvalidOperationException("Plugin name is required"),
            Version = data.Version ?? "1.0.0",
            Description = data.Description,
            Author = data.Author,
            Category = Enum.TryParse<PluginCategory>(data.Category, true, out var category)
                ? category
                : PluginCategory.Other,
            AssemblyFileName = data.AssemblyFileName ?? throw new InvalidOperationException("Assembly file name is required"),
            PluginTypeName = data.PluginTypeName ?? throw new InvalidOperationException("Plugin type name is required"),
            Dependencies = data.Dependencies?.Select(d => new PluginDependency
            {
                Name = d.Name ?? throw new InvalidOperationException("Dependency name is required"),
                Version = d.Version ?? "*",
                IsOptional = d.IsOptional
            }).ToArray() ?? Array.Empty<PluginDependency>(),
            SupportedInputSchemas = data.SupportedInputSchemas ?? Array.Empty<string>(),
            SupportedOutputSchemas = data.SupportedOutputSchemas ?? Array.Empty<string>(),
            RequiresConfiguration = data.RequiresConfiguration,
            ConfigurationSchemaFile = data.ConfigurationSchemaFile,
            Metadata = data.Metadata ?? new Dictionary<string, object>(),
            MinimumFlowEngineVersion = data.MinimumFlowEngineVersion
        };
    }

    /// <summary>
    /// Creates a synthetic manifest for plugins discovered without manifest files.
    /// </summary>
    private static PluginManifest CreateSyntheticManifest(PluginTypeInfo typeInfo)
    {
        return new PluginManifest
        {
            Id = typeInfo.TypeName,
            Name = typeInfo.FriendlyName,
            Version = typeInfo.Version ?? "1.0.0",
            Description = typeInfo.Description,
            Author = "Unknown",
            Category = typeInfo.Category,
            AssemblyFileName = Path.GetFileName(typeInfo.AssemblyPath),
            PluginTypeName = typeInfo.TypeName,
            Dependencies = Array.Empty<PluginDependency>(),
            SupportedInputSchemas = typeInfo.SupportedSchemas,
            SupportedOutputSchemas = Array.Empty<string>(),
            RequiresConfiguration = typeInfo.RequiresConfiguration,
            ConfigurationSchemaFile = null,
            Metadata = typeInfo.Metadata,
            MinimumFlowEngineVersion = null
        };
    }

    /// <summary>
    /// Validates a single dependency.
    /// </summary>
    private Task<DependencyValidationResult> ValidateDependencyAsync(PluginDependency dependency)
    {
        // For now, just check if the dependency is a known plugin type
        // In a full implementation, this would check version compatibility
        var isKnownPlugin = _pluginRegistry.IsPluginTypeRegistered(dependency.Name);

        if (isKnownPlugin)
        {
            var pluginType = _pluginRegistry.GetPluginType(dependency.Name);
            return Task.FromResult(new DependencyValidationResult
            {
                Dependency = dependency,
                IsSatisfied = true,
                FoundVersion = pluginType?.Version,
                Message = "Dependency satisfied"
            });
        }

        // Check for system assemblies or .NET dependencies
        try
        {
            var assemblyName = new AssemblyName(dependency.Name);
            var assembly = Assembly.Load(assemblyName);

            return Task.FromResult(new DependencyValidationResult
            {
                Dependency = dependency,
                IsSatisfied = true,
                FoundVersion = assembly.GetName().Version?.ToString(),
                Message = "System dependency satisfied"
            });
        }
        catch
        {
            return Task.FromResult(new DependencyValidationResult
            {
                Dependency = dependency,
                IsSatisfied = false,
                FoundVersion = null,
                Message = $"Dependency '{dependency.Name}' not found"
            });
        }
    }

    /// <summary>
    /// Data model for deserializing plugin.json manifest files.
    /// </summary>
    private sealed class PluginManifestData
    {
        public string? Id { get; set; }
        public string? Name { get; set; }
        public string? Version { get; set; }
        public string? Description { get; set; }
        public string? Author { get; set; }
        public string? Category { get; set; }
        public string? AssemblyFileName { get; set; }
        public string? PluginTypeName { get; set; }
        public PluginDependencyData[]? Dependencies { get; set; }
        public string[]? SupportedInputSchemas { get; set; }
        public string[]? SupportedOutputSchemas { get; set; }
        public bool RequiresConfiguration { get; set; }
        public string? ConfigurationSchemaFile { get; set; }
        public Dictionary<string, object>? Metadata { get; set; }
        public string? MinimumFlowEngineVersion { get; set; }
    }

    /// <summary>
    /// Data model for deserializing plugin dependencies.
    /// </summary>
    private sealed class PluginDependencyData
    {
        public string? Name { get; set; }
        public string? Version { get; set; }
        public bool IsOptional { get; set; }
    }
}
