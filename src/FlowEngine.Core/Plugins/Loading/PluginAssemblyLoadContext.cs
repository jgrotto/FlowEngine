using System.Reflection;
using System.Runtime.Loader;

namespace FlowEngine.Core.Plugins.Loading;

/// <summary>
/// Isolated assembly load context for plugin assemblies.
/// Provides assembly isolation and supports hot-swapping through unloading.
/// </summary>
public sealed class PluginAssemblyLoadContext : AssemblyLoadContext
{
    private readonly AssemblyDependencyResolver _resolver;
    private readonly string _pluginDirectory;
    private readonly HashSet<string> _sharedAssemblies;

    /// <summary>
    /// Gets the plugin identifier associated with this load context.
    /// </summary>
    public string PluginId { get; }

    /// <summary>
    /// Gets the plugin assembly path.
    /// </summary>
    public string AssemblyPath { get; }

    /// <summary>
    /// Gets the timestamp when this context was created.
    /// </summary>
    public DateTimeOffset CreatedAt { get; }

    /// <summary>
    /// Initializes a new instance of the PluginAssemblyLoadContext class.
    /// </summary>
    /// <param name="pluginId">Unique plugin identifier</param>
    /// <param name="assemblyPath">Path to the plugin assembly</param>
    /// <param name="sharedAssemblies">Set of assembly names that should be shared with the host</param>
    public PluginAssemblyLoadContext(string pluginId, string assemblyPath, HashSet<string>? sharedAssemblies = null)
        : base($"Plugin_{pluginId}_{Guid.NewGuid():N}", isCollectible: true)
    {
        PluginId = pluginId ?? throw new ArgumentNullException(nameof(pluginId));
        AssemblyPath = assemblyPath ?? throw new ArgumentNullException(nameof(assemblyPath));
        CreatedAt = DateTimeOffset.UtcNow;

        if (!File.Exists(assemblyPath))
        {
            throw new FileNotFoundException($"Plugin assembly not found: {assemblyPath}");
        }

        _pluginDirectory = Path.GetDirectoryName(assemblyPath) ?? throw new ArgumentException("Invalid assembly path", nameof(assemblyPath));
        _resolver = new AssemblyDependencyResolver(assemblyPath);
        
        // Default shared assemblies that should be loaded from the host context
        _sharedAssemblies = sharedAssemblies ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "FlowEngine.Abstractions",
            "System.Runtime",
            "System.Collections",
            "System.Threading",
            "System.Threading.Tasks",
            "Microsoft.Extensions.Logging",
            "Microsoft.Extensions.DependencyInjection",
            "Microsoft.Extensions.Configuration",
            "System.Text.Json",
            "System.Collections.Immutable"
        };
    }

    /// <summary>
    /// Loads the plugin assembly and returns it.
    /// </summary>
    /// <returns>Loaded plugin assembly</returns>
    public Assembly LoadPluginAssembly()
    {
        return LoadFromAssemblyPath(AssemblyPath);
    }

    /// <summary>
    /// Loads an assembly by name within this plugin context.
    /// </summary>
    /// <param name="assemblyName">Assembly name to load</param>
    /// <returns>Loaded assembly or null if not found</returns>
    protected override Assembly? Load(AssemblyName assemblyName)
    {
        // Check if this assembly should be shared with the host
        if (_sharedAssemblies.Contains(assemblyName.Name!))
        {
            // Load from the default (host) context to ensure shared types
            return Default.LoadFromAssemblyName(assemblyName);
        }

        // Try to resolve the assembly path using the dependency resolver
        var assemblyPath = _resolver.ResolveAssemblyToPath(assemblyName);
        if (assemblyPath != null)
        {
            return LoadFromAssemblyPath(assemblyPath);
        }

        // Check if the assembly exists in the plugin directory
        var pluginAssemblyPath = Path.Combine(_pluginDirectory, $"{assemblyName.Name}.dll");
        if (File.Exists(pluginAssemblyPath))
        {
            return LoadFromAssemblyPath(pluginAssemblyPath);
        }

        // Fallback to the default load context
        return null;
    }

    /// <summary>
    /// Resolves an unmanaged library by name.
    /// </summary>
    /// <param name="unmanagedDllName">Unmanaged library name</param>
    /// <returns>Library handle or IntPtr.Zero if not found</returns>
    protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
    {
        // Try to resolve the unmanaged library path
        var libraryPath = _resolver.ResolveUnmanagedDllToPath(unmanagedDllName);
        if (libraryPath != null)
        {
            return LoadUnmanagedDllFromPath(libraryPath);
        }

        // Check plugin directory for the library
        var pluginLibraryPath = Path.Combine(_pluginDirectory, unmanagedDllName);
        if (File.Exists(pluginLibraryPath))
        {
            return LoadUnmanagedDllFromPath(pluginLibraryPath);
        }

        // Common Windows/Linux library name variations
        var platformSpecificNames = GetPlatformSpecificLibraryNames(unmanagedDllName);
        foreach (var name in platformSpecificNames)
        {
            var platformLibraryPath = Path.Combine(_pluginDirectory, name);
            if (File.Exists(platformLibraryPath))
            {
                return LoadUnmanagedDllFromPath(platformLibraryPath);
            }
        }

        return base.LoadUnmanagedDll(unmanagedDllName);
    }

    /// <summary>
    /// Gets platform-specific variations of a library name.
    /// </summary>
    /// <param name="libraryName">Base library name</param>
    /// <returns>Collection of platform-specific names to try</returns>
    private static IEnumerable<string> GetPlatformSpecificLibraryNames(string libraryName)
    {
        // Return the original name first
        yield return libraryName;

        if (OperatingSystem.IsWindows())
        {
            if (!libraryName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            {
                yield return $"{libraryName}.dll";
            }
        }
        else if (OperatingSystem.IsLinux())
        {
            if (!libraryName.StartsWith("lib"))
            {
                yield return $"lib{libraryName}";
            }
            if (!libraryName.EndsWith(".so"))
            {
                yield return $"{libraryName}.so";
                if (!libraryName.StartsWith("lib"))
                {
                    yield return $"lib{libraryName}.so";
                }
            }
        }
        else if (OperatingSystem.IsMacOS())
        {
            if (!libraryName.StartsWith("lib"))
            {
                yield return $"lib{libraryName}";
            }
            if (!libraryName.EndsWith(".dylib"))
            {
                yield return $"{libraryName}.dylib";
                if (!libraryName.StartsWith("lib"))
                {
                    yield return $"lib{libraryName}.dylib";
                }
            }
        }
    }

    /// <summary>
    /// Adds an assembly name to the shared assemblies list.
    /// Shared assemblies are loaded from the host context to ensure type compatibility.
    /// </summary>
    /// <param name="assemblyName">Assembly name to share</param>
    public void AddSharedAssembly(string assemblyName)
    {
        _sharedAssemblies.Add(assemblyName);
    }

    /// <summary>
    /// Removes an assembly name from the shared assemblies list.
    /// </summary>
    /// <param name="assemblyName">Assembly name to stop sharing</param>
    public void RemoveSharedAssembly(string assemblyName)
    {
        _sharedAssemblies.Remove(assemblyName);
    }

    /// <summary>
    /// Gets the list of shared assembly names.
    /// </summary>
    /// <returns>Read-only collection of shared assembly names</returns>
    public IReadOnlySet<string> GetSharedAssemblies()
    {
        return _sharedAssemblies.ToHashSet();
    }

    /// <summary>
    /// Checks if the context is still alive and collectible.
    /// </summary>
    /// <returns>True if the context can be unloaded</returns>
    public bool IsAlive()
    {
        try
        {
            // Try to access assemblies to check if context is still alive
            _ = Assemblies.Count();
            return true;
        }
        catch (InvalidOperationException)
        {
            // Context has been unloaded
            return false;
        }
    }

    /// <summary>
    /// Gets diagnostic information about this load context.
    /// </summary>
    /// <returns>Diagnostic information</returns>
    public PluginLoadContextDiagnostics GetDiagnostics()
    {
        try
        {
            var assemblies = Assemblies.ToList();
            return new PluginLoadContextDiagnostics
            {
                PluginId = PluginId,
                Name = Name ?? "Unknown",
                IsCollectible = IsCollectible,
                IsAlive = IsAlive(),
                AssemblyCount = assemblies.Count,
                LoadedAssemblies = assemblies.Select(a => a.FullName ?? "Unknown").ToList(),
                CreatedAt = CreatedAt,
                MemoryUsageBytes = GC.GetTotalMemory(false) // Approximate
            };
        }
        catch (Exception ex)
        {
            return new PluginLoadContextDiagnostics
            {
                PluginId = PluginId,
                Name = Name ?? "Unknown",
                IsCollectible = IsCollectible,
                IsAlive = false,
                AssemblyCount = 0,
                LoadedAssemblies = new List<string>(),
                CreatedAt = CreatedAt,
                MemoryUsageBytes = 0,
                Error = ex.Message
            };
        }
    }
}

/// <summary>
/// Diagnostic information about a plugin load context.
/// </summary>
public sealed record PluginLoadContextDiagnostics
{
    /// <summary>
    /// Gets the plugin identifier.
    /// </summary>
    public required string PluginId { get; init; }

    /// <summary>
    /// Gets the context name.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Gets whether the context is collectible.
    /// </summary>
    public required bool IsCollectible { get; init; }

    /// <summary>
    /// Gets whether the context is still alive.
    /// </summary>
    public required bool IsAlive { get; init; }

    /// <summary>
    /// Gets the number of loaded assemblies.
    /// </summary>
    public required int AssemblyCount { get; init; }

    /// <summary>
    /// Gets the list of loaded assembly names.
    /// </summary>
    public required IReadOnlyList<string> LoadedAssemblies { get; init; }

    /// <summary>
    /// Gets the context creation timestamp.
    /// </summary>
    public required DateTimeOffset CreatedAt { get; init; }

    /// <summary>
    /// Gets the approximate memory usage in bytes.
    /// </summary>
    public required long MemoryUsageBytes { get; init; }

    /// <summary>
    /// Gets any error message if diagnostics failed.
    /// </summary>
    public string? Error { get; init; }
}