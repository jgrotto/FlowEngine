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

    // === Bootstrap-Focused Discovery Methods ===

    /// <inheritdoc />
    public async Task<IReadOnlyCollection<DiscoveredPlugin>> DiscoverPluginsForBootstrapAsync(
        PluginDiscoveryOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new PluginDiscoveryOptions();
        
        _logger.LogInformation("Starting bootstrap plugin discovery with timeout {Timeout}", options.DiscoveryTimeout);

        try
        {
            using var timeoutCts = options.DiscoveryTimeout.HasValue 
                ? CancellationTokenSource.CreateLinkedTokenSource(cancellationToken)
                : null;

            if (timeoutCts != null)
            {
                timeoutCts.CancelAfter(options.DiscoveryTimeout.Value);
            }

            var effectiveCancellationToken = timeoutCts?.Token ?? cancellationToken;
            var discoveredPlugins = new List<DiscoveredPlugin>();

            // Get all directories to scan (bootstrap standard directories + additional)
            var directoriesToScan = GetStandardPluginDirectories().ToList();
            if (options.AdditionalDirectories != null)
            {
                directoriesToScan.AddRange(options.AdditionalDirectories);
            }

            _logger.LogDebug("Scanning {DirectoryCount} directories for plugins", directoriesToScan.Count);

            // Track processed assembly names globally to avoid duplicates across directories
            var processedAssemblyNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // Scan each directory sequentially to properly track duplicates
            var allDiscoveredPlugins = new List<DiscoveredPlugin>();
            foreach (var directory in directoriesToScan)
            {
                try
                {
                    var directoryPlugins = await ScanDirectoryForAssembliesWithDuplicateTrackingAsync(
                        directory, options.ScanRecursively, processedAssemblyNames, effectiveCancellationToken);
                    allDiscoveredPlugins.AddRange(directoryPlugins);
                }
                catch (OperationCanceledException)
                {
                    _logger.LogWarning("Plugin discovery timed out while scanning {Directory}", directory);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to scan directory {Directory}", directory);
                }
            }

            // Convert to scan tasks format for compatibility
            var scanTasks = new[] { Task.FromResult<IEnumerable<DiscoveredPlugin>>(allDiscoveredPlugins) };

            var scanResults = await Task.WhenAll(scanTasks);
            
            // Flatten results and remove duplicates
            var allPlugins = scanResults
                .SelectMany(results => results)
                .GroupBy(p => $"{p.AssemblyPath}::{p.Manifest.PluginTypeName}")
                .Select(g => g.First())
                .ToList();

            // Filter by capabilities if specified
            if (options.RequiredCapabilities.HasValue)
            {
                allPlugins = allPlugins
                    .Where(p => (p.Capabilities & options.RequiredCapabilities.Value) != 0)
                    .ToList();
            }

            // Filter built-in plugins if requested
            if (!options.IncludeBuiltInPlugins)
            {
                allPlugins = allPlugins
                    .Where(p => !IsBuiltInPlugin(p))
                    .ToList();
            }

            // Perform validation if requested
            if (options.PerformValidation)
            {
                await ValidateDiscoveredPluginsAsync(allPlugins, effectiveCancellationToken);
            }

            discoveredPlugins.AddRange(allPlugins);

            _logger.LogInformation("Bootstrap plugin discovery completed. Found {PluginCount} plugins",
                discoveredPlugins.Count);

            return discoveredPlugins.AsReadOnly();
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Bootstrap plugin discovery was cancelled or timed out");
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Bootstrap plugin discovery failed");
            throw;
        }
    }

    /// <inheritdoc />
    public IReadOnlyCollection<string> GetStandardPluginDirectories()
    {
        var appDirectory = AppDomain.CurrentDomain.BaseDirectory;
        var currentDirectory = Directory.GetCurrentDirectory();
        
        var directories = new List<string>
        {
            // Standard plugin directories
            Path.Combine(appDirectory, "plugins"),
            Path.Combine(appDirectory, "Plugins"),
            Path.Combine(appDirectory, "..", "plugins"),
            Path.Combine(currentDirectory, "plugins"),
            
            // Current directory as fallback
            appDirectory
        };
        
        // Add development plugin directories
        directories.AddRange(GetDevelopmentPluginDirectories(appDirectory));

        // Flatten and filter existing directories
        var existingDirectories = directories
            .SelectMany(dir => dir.Contains('*') ? ExpandGlob(dir) : new[] { dir })
            .Where(Directory.Exists)
            .Distinct()
            .ToList();

        _logger.LogDebug("Standard plugin directories: {Directories}", string.Join(", ", existingDirectories));
        
        return existingDirectories.AsReadOnly();
    }

    /// <inheritdoc />
    public async Task<PluginValidationResult> ValidatePluginAssemblyAsync(string assemblyPath, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug("Validating plugin assembly: {AssemblyPath}", assemblyPath);

            if (!File.Exists(assemblyPath))
            {
                return PluginValidationResult.Failure($"Assembly file not found: {assemblyPath}");
            }

            // Try to load assembly metadata without loading into current domain
            var assemblyName = AssemblyName.GetAssemblyName(assemblyPath);
            var assembly = Assembly.LoadFrom(assemblyPath);
            
            var errors = new List<string>();
            var warnings = new List<string>();
            
            // Scan assembly for plugin types and get them from registry
            _pluginRegistry.ScanAssembly(assembly);
            var assemblyLocation = assembly.Location;
            var pluginTypes = _pluginRegistry.GetPluginTypes()
                .Where(pt => string.Equals(pt.AssemblyPath, assemblyLocation, StringComparison.OrdinalIgnoreCase));
            
            if (!pluginTypes.Any())
            {
                errors.Add("No plugin types found in assembly");
                return PluginValidationResult.Failure(errors.ToArray());
            }

            // Additional validation can be added here
            await Task.CompletedTask;

            _logger.LogDebug("Plugin assembly validation passed: {AssemblyPath}", assemblyPath);
            return PluginValidationResult.Success();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Plugin assembly validation failed for {AssemblyPath}", assemblyPath);
            return PluginValidationResult.Failure($"Validation failed: {ex.Message}");
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyCollection<DiscoveredPlugin>> ScanDirectoryForAssembliesAsync(
        string directory, 
        bool recursive = false, 
        CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(directory))
        {
            _logger.LogDebug("Directory does not exist: {Directory}", directory);
            return Array.Empty<DiscoveredPlugin>();
        }

        _logger.LogDebug("Scanning directory for assemblies: {Directory} (recursive: {Recursive})", directory, recursive);

        var discoveredPlugins = new List<DiscoveredPlugin>();
        var searchOption = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;

        try
        {
            var assemblyFiles = Directory.GetFiles(directory, "*.dll", searchOption);
            _logger.LogDebug("Found {FileCount} DLL files in {Directory}", assemblyFiles.Length, directory);

            var scanTasks = assemblyFiles.Select(async assemblyPath =>
            {
                try
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    return await ScanAssemblyForPluginsAsync(assemblyPath);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Failed to process assembly: {AssemblyPath}", assemblyPath);
                    return Array.Empty<DiscoveredPlugin>();
                }
            });

            var scanResults = await Task.WhenAll(scanTasks);
            discoveredPlugins.AddRange(scanResults.SelectMany(r => r));

            _logger.LogDebug("Discovered {PluginCount} plugins in {Directory}", discoveredPlugins.Count, directory);
            return discoveredPlugins.AsReadOnly();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to scan directory: {Directory}", directory);
            return Array.Empty<DiscoveredPlugin>();
        }
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

        // Track scanned assembly names to avoid duplicates
        var scannedAssemblyNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var assemblyFile in assemblyFiles)
        {
            var fullAssemblyPath = Path.GetFullPath(assemblyFile);
            if (manifestedAssemblies.Contains(fullAssemblyPath))
            {
                continue; // Skip assemblies already discovered via manifest
            }

            // Check if we've already scanned an assembly with this name
            var assemblyName = Path.GetFileNameWithoutExtension(assemblyFile);
            if (scannedAssemblyNames.Contains(assemblyName))
            {
                _logger.LogDebug("Skipping duplicate assembly {AssemblyName} at {AssemblyPath}", 
                    assemblyName, assemblyFile);
                continue;
            }

            try
            {
                // Scan assembly for plugin types
                _pluginRegistry.ScanAssemblyFile(assemblyFile);
                scannedAssemblyNames.Add(assemblyName);

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
            catch (PluginRegistryException ex) when (ex.InnerException is FileLoadException fle && fle.Message.Contains("Assembly with same name is already loaded"))
            {
                _logger.LogDebug("Skipping duplicate assembly: {AssemblyPath} (already loaded)", assemblyFile);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to scan assembly for plugins: {AssemblyPath}", assemblyFile);
            }
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Gets development plugin directories for the current environment.
    /// Only scans directories matching the current build configuration to avoid assembly conflicts.
    /// </summary>
    private static IEnumerable<string> GetDevelopmentPluginDirectories(string appDirectory)
    {
        var developmentPaths = new List<string>();

#if DEBUG
        // In Debug builds, only scan Debug assemblies
        developmentPaths.AddRange(new[]
        {
            Path.Combine(appDirectory, "..", "..", "..", "plugins", "*", "bin", "Debug", "net8.0"),
            Path.Combine(appDirectory, "..", "plugins", "*", "bin", "Debug", "net8.0")
        });
#else
        // In Release builds, only scan Release assemblies
        developmentPaths.AddRange(new[]
        {
            Path.Combine(appDirectory, "..", "..", "..", "plugins", "*", "bin", "Release", "net8.0"),
            Path.Combine(appDirectory, "..", "plugins", "*", "bin", "Release", "net8.0")
        });
#endif

        return developmentPaths;
    }

    /// <summary>
    /// Expands glob patterns to actual directory paths.
    /// </summary>
    private static IEnumerable<string> ExpandGlob(string pattern)
    {
        try
        {
            var directory = Path.GetDirectoryName(pattern);
            var fileName = Path.GetFileName(pattern);
            
            if (string.IsNullOrEmpty(directory) || !fileName.Contains('*'))
            {
                return new[] { pattern };
            }

            if (!Directory.Exists(directory))
            {
                return Array.Empty<string>();
            }

            var searchPattern = fileName.Replace('*', '*'); // Ensure valid pattern
            return Directory.GetDirectories(directory, searchPattern, SearchOption.TopDirectoryOnly);
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    /// <summary>
    /// Determines if a plugin is a built-in FlowEngine plugin.
    /// </summary>
    private static bool IsBuiltInPlugin(DiscoveredPlugin plugin)
    {
        var assemblyName = Path.GetFileNameWithoutExtension(plugin.AssemblyPath);
        
        // Built-in plugins typically start with "FlowEngine." or are in standard plugin assemblies
        return assemblyName.StartsWith("FlowEngine.", StringComparison.OrdinalIgnoreCase) ||
               assemblyName.Equals("DelimitedSource", StringComparison.OrdinalIgnoreCase) ||
               assemblyName.Equals("JavaScriptTransform", StringComparison.OrdinalIgnoreCase) ||
               assemblyName.Equals("ConsoleSink", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Validates a collection of discovered plugins.
    /// </summary>
    private async Task ValidateDiscoveredPluginsAsync(List<DiscoveredPlugin> plugins, CancellationToken cancellationToken)
    {
        var validationTasks = plugins.Select(async plugin =>
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                var result = await ValidatePluginAssemblyAsync(plugin.AssemblyPath, cancellationToken);
                
                if (!result.IsValid)
                {
                    _logger.LogWarning("Plugin validation failed for {PluginName}: {Errors}", 
                        plugin.Manifest.Name, string.Join(", ", result.Errors));
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to validate plugin {PluginName}", plugin.Manifest.Name);
            }
        });

        await Task.WhenAll(validationTasks);
    }

    /// <summary>
    /// Scans an assembly for plugin types and creates discovered plugin entries.
    /// </summary>
    private async Task<IReadOnlyCollection<DiscoveredPlugin>> ScanAssemblyForPluginsAsync(string assemblyPath)
    {
        var discoveredPlugins = new List<DiscoveredPlugin>();

        try
        {
            _logger.LogDebug("Scanning assembly for plugins: {AssemblyPath}", assemblyPath);

            // Scan assembly for plugin types
            _pluginRegistry.ScanAssemblyFile(assemblyPath);

            // Get plugin types from this assembly
            var assemblyPluginTypes = _pluginRegistry.GetPluginTypes()
                .Where(pt => string.Equals(pt.AssemblyPath, Path.GetFullPath(assemblyPath), StringComparison.OrdinalIgnoreCase))
                .ToList();

            foreach (var pluginType in assemblyPluginTypes)
            {
                // Create a synthetic manifest for assembly-based plugins
                var manifest = CreateSyntheticManifest(pluginType);
                var pluginDirectory = Path.GetDirectoryName(assemblyPath)!;

                // Determine plugin capabilities based on type information
                var capabilities = DeterminePluginCapabilities(pluginType);

                var discoveredPlugin = new DiscoveredPlugin
                {
                    Manifest = manifest,
                    DirectoryPath = pluginDirectory,
                    ManifestFilePath = string.Empty,
                    AssemblyPath = assemblyPath,
                    HasManifest = false,
                    TypeInfo = pluginType,
                    Capabilities = capabilities,
                    Version = Version.TryParse(pluginType.Version, out var version) ? version : null,
                    Author = pluginType.Metadata.TryGetValue("Author", out var author) ? author?.ToString() : null,
                    Description = pluginType.Description,
                    IsBootstrapDiscovered = true
                };

                discoveredPlugins.Add(discoveredPlugin);
                _logger.LogDebug("Discovered bootstrap plugin: {PluginName} in {AssemblyPath}",
                    pluginType.FriendlyName, assemblyPath);
            }

            await Task.CompletedTask;
            return discoveredPlugins.AsReadOnly();
        }
        catch (PluginRegistryException ex) when (ex.InnerException is FileLoadException fle && fle.Message.Contains("Assembly with same name is already loaded"))
        {
            _logger.LogDebug("Skipping duplicate assembly: {AssemblyPath} (already loaded)", assemblyPath);
            return Array.Empty<DiscoveredPlugin>();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to scan assembly for plugins: {AssemblyPath}", assemblyPath);
            return Array.Empty<DiscoveredPlugin>();
        }
    }

    /// <summary>
    /// Scans a directory for plugin assemblies with global duplicate tracking.
    /// </summary>
    private async Task<IReadOnlyCollection<DiscoveredPlugin>> ScanDirectoryForAssembliesWithDuplicateTrackingAsync(
        string directory, 
        bool recursive, 
        HashSet<string> processedAssemblyNames,
        CancellationToken cancellationToken)
    {
        var discoveredPlugins = new List<DiscoveredPlugin>();

        if (!Directory.Exists(directory))
        {
            _logger.LogDebug("Directory does not exist, skipping: {Directory}", directory);
            return discoveredPlugins.AsReadOnly();
        }

        _logger.LogDebug("Scanning directory for assemblies: {Directory} (recursive: {Recursive})", directory, recursive);

        // Get manifest-based plugins first
        var existingPlugins = await DiscoverPluginsAsync();

        // Get all DLL files in directory
        var searchOption = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        var allAssemblyFiles = Directory.GetFiles(directory, "*.dll", searchOption);
        
        // Filter out problematic directories that cause assembly conflicts
        var assemblyFiles = allAssemblyFiles
            .Where(f => !f.Contains("\\obj\\", StringComparison.OrdinalIgnoreCase))  // Skip obj directories
            .Where(f => !f.Contains("\\ref\\", StringComparison.OrdinalIgnoreCase))  // Skip reference assemblies
            .Where(f => !f.Contains("\\refint\\", StringComparison.OrdinalIgnoreCase)) // Skip refint assemblies
            .ToArray();
        
        _logger.LogDebug("Found {FileCount} DLL files in {Directory} (filtered from {TotalCount})", 
            assemblyFiles.Length, directory, allAssemblyFiles.Length);

        // Get assembly paths that are already covered by manifest-based plugins
        var manifestedAssemblies = new HashSet<string>(
            existingPlugins.Select(p => Path.GetFullPath(p.AssemblyPath)),
            StringComparer.OrdinalIgnoreCase);

        // Group assemblies by name and prefer Debug over Release builds
        var assemblyGroups = assemblyFiles
            .Select(f => new { Path = f, Name = Path.GetFileNameWithoutExtension(f), FullPath = Path.GetFullPath(f) })
            .Where(a => !manifestedAssemblies.Contains(a.FullPath))
            .GroupBy(a => a.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var assemblyGroup in assemblyGroups)
        {
            // Prefer current build configuration, then prefer bin over obj
            var selectedAssembly = assemblyGroup
#if DEBUG
                .OrderByDescending(a => a.Path.Contains("Debug", StringComparison.OrdinalIgnoreCase) ? 1 : 0) // Prefer Debug in Debug builds
#else
                .OrderByDescending(a => a.Path.Contains("Release", StringComparison.OrdinalIgnoreCase) ? 1 : 0) // Prefer Release in Release builds
#endif
                .ThenByDescending(a => a.Path.Contains("\\bin\\", StringComparison.OrdinalIgnoreCase) ? 1 : 0) // Prefer bin over obj
                .First();

            var assemblyFile = selectedAssembly.Path;
            var fullAssemblyPath = selectedAssembly.FullPath;
            var assemblyName = assemblyGroup.Key;

            // Check if we've already processed this assembly name globally
            if (processedAssemblyNames.Contains(assemblyName))
            {
                _logger.LogDebug("Skipping duplicate assembly {AssemblyName} at {AssemblyPath}", 
                    assemblyName, assemblyFile);
                continue;
            }

            // Log skipped assemblies in this group
            var skippedAssemblies = assemblyGroup.Where(a => a != selectedAssembly).ToList();
            foreach (var skipped in skippedAssemblies)
            {
                _logger.LogDebug("Skipping duplicate assembly {AssemblyName} at {AssemblyPath} (using {SelectedPath} instead)", 
                    assemblyName, skipped.Path, assemblyFile);
            }

            try
            {
                // Scan assembly for plugin types
                _pluginRegistry.ScanAssemblyFile(assemblyFile);
                processedAssemblyNames.Add(assemblyName);

                // Get plugin types from this assembly
                var assemblyPluginTypes = _pluginRegistry.GetPluginTypes()
                    .Where(pt => string.Equals(pt.AssemblyPath, fullAssemblyPath, StringComparison.OrdinalIgnoreCase))
                    .ToList();

                foreach (var pluginType in assemblyPluginTypes)
                {
                    // Create a synthetic manifest for assembly-based plugins
                    var manifest = CreateSyntheticManifest(pluginType);
                    var pluginDirectory = Path.GetDirectoryName(assemblyFile)!;

                    // Determine plugin capabilities based on type information
                    var capabilities = DeterminePluginCapabilities(pluginType);

                    var discoveredPlugin = new DiscoveredPlugin
                    {
                        Manifest = manifest,
                        DirectoryPath = pluginDirectory,
                        ManifestFilePath = string.Empty,
                        AssemblyPath = assemblyFile,
                        HasManifest = false,
                        TypeInfo = pluginType,
                        Capabilities = capabilities,
                        Version = Version.TryParse(pluginType.Version, out var version) ? version : null,
                        Author = pluginType.Metadata.TryGetValue("Author", out var author) ? author?.ToString() : null,
                        Description = pluginType.Description,
                        IsBootstrapDiscovered = true
                    };

                    discoveredPlugins.Add(discoveredPlugin);
                    _logger.LogDebug("Discovered bootstrap plugin: {PluginName} in {AssemblyPath}",
                        pluginType.FriendlyName, assemblyFile);
                }
            }
            catch (PluginRegistryException ex) when (ex.InnerException is FileLoadException fle && fle.Message.Contains("Assembly with same name is already loaded"))
            {
                _logger.LogDebug("Skipping duplicate assembly: {AssemblyPath} (already loaded)", assemblyFile);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to scan assembly for plugins: {AssemblyPath}", assemblyFile);
            }
        }

        await Task.CompletedTask;
        return discoveredPlugins.AsReadOnly();
    }

    /// <summary>
    /// Determines plugin capabilities based on type information.
    /// </summary>
    private static PluginCapabilities DeterminePluginCapabilities(PluginTypeInfo typeInfo)
    {
        var capabilities = PluginCapabilities.None;

        // Determine capabilities based on category and type information
        switch (typeInfo.Category)
        {
            case PluginCategory.Source:
                capabilities |= PluginCapabilities.Source;
                break;
            case PluginCategory.Transform:
                capabilities |= PluginCapabilities.Transform;
                break;
            case PluginCategory.Sink:
                capabilities |= PluginCapabilities.Sink;
                break;
        }

        // Check for additional capabilities based on metadata or interfaces
        if (typeInfo.Metadata.ContainsKey("SupportsStreaming") && 
            typeInfo.Metadata["SupportsStreaming"]?.ToString()?.Equals("true", StringComparison.OrdinalIgnoreCase) == true)
        {
            capabilities |= PluginCapabilities.Streaming;
        }

        if (typeInfo.RequiresConfiguration)
        {
            capabilities |= PluginCapabilities.CustomConfiguration;
        }

        // Default to batch processing if no streaming specified
        if ((capabilities & PluginCapabilities.Streaming) == 0)
        {
            capabilities |= PluginCapabilities.Batch;
        }

        return capabilities;
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
