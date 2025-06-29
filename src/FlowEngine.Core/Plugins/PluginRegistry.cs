using FlowEngine.Abstractions.Plugins;
using System.Collections.Concurrent;
using System.Reflection;

namespace FlowEngine.Core.Plugins;

/// <summary>
/// Registry for discovering and cataloging available plugin types.
/// Provides plugin discovery from assemblies and type resolution services.
/// </summary>
public sealed class PluginRegistry : IPluginRegistry
{
    private readonly ConcurrentDictionary<string, PluginTypeInfo> _registeredTypes = new();
    private readonly ConcurrentDictionary<string, Assembly> _scannedAssemblies = new();
    private readonly object _scanLock = new();

    /// <summary>
    /// Initializes a new plugin registry.
    /// </summary>
    public PluginRegistry()
    {
        // Register built-in plugin types on startup
        ScanAssembly(typeof(IPlugin).Assembly); // FlowEngine.Abstractions
        ScanAssembly(Assembly.GetExecutingAssembly()); // FlowEngine.Core
    }

    /// <inheritdoc />
    public async Task ScanDirectoryAsync(string directoryPath, string searchPattern = "*.dll")
    {
        if (string.IsNullOrWhiteSpace(directoryPath))
            throw new ArgumentException("Directory path cannot be null or empty", nameof(directoryPath));

        if (!Directory.Exists(directoryPath))
            throw new DirectoryNotFoundException($"Directory not found: {directoryPath}");

        var assemblyFiles = Directory.GetFiles(directoryPath, searchPattern, SearchOption.TopDirectoryOnly);

        var scanTasks = assemblyFiles.Select(async assemblyPath =>
        {
            try
            {
                await Task.Run(() => ScanAssemblyFile(assemblyPath));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to scan assembly {assemblyPath}: {ex.Message}");
            }
        });

        await Task.WhenAll(scanTasks);
    }

    /// <inheritdoc />
    public void ScanAssembly(Assembly assembly)
    {
        if (assembly == null)
            throw new ArgumentNullException(nameof(assembly));

        var assemblyName = assembly.GetName().Name ?? assembly.FullName ?? "Unknown";

        lock (_scanLock)
        {
            if (_scannedAssemblies.ContainsKey(assemblyName))
                return; // Already scanned

            _scannedAssemblies[assemblyName] = assembly;
        }

        try
        {
            var pluginTypes = assembly.GetTypes()
                .Where(IsPluginType)
                .ToArray();

            foreach (var type in pluginTypes)
            {
                RegisterPluginType(type);
            }
        }
        catch (ReflectionTypeLoadException ex)
        {
            // Handle partial assembly loading
            var loadedTypes = ex.Types.Where(t => t != null && IsPluginType(t));
            foreach (var type in loadedTypes)
            {
                RegisterPluginType(type!);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to scan assembly {assemblyName}: {ex.Message}");
        }
    }

    /// <inheritdoc />
    public void ScanAssemblyFile(string assemblyPath)
    {
        if (string.IsNullOrWhiteSpace(assemblyPath))
            throw new ArgumentException("Assembly path cannot be null or empty", nameof(assemblyPath));

        if (!File.Exists(assemblyPath))
            throw new FileNotFoundException($"Assembly file not found: {assemblyPath}");

        try
        {
            // Load assembly for reflection only to avoid loading dependencies
            var assembly = Assembly.LoadFrom(assemblyPath);
            ScanAssembly(assembly);
        }
        catch (Exception ex)
        {
            throw new PluginRegistryException($"Failed to scan assembly file: {assemblyPath}", ex);
        }
    }

    /// <inheritdoc />
    public PluginTypeInfo? GetPluginType(string typeName)
    {
        if (string.IsNullOrWhiteSpace(typeName))
            return null;

        return _registeredTypes.TryGetValue(typeName, out var typeInfo) ? typeInfo : null;
    }

    /// <inheritdoc />
    public IReadOnlyCollection<PluginTypeInfo> GetPluginTypes(PluginCategory? category = null)
    {
        var allTypes = _registeredTypes.Values.ToArray();

        return category.HasValue 
            ? allTypes.Where(t => t.Category == category.Value).ToArray()
            : allTypes;
    }

    /// <inheritdoc />
    public IReadOnlyCollection<PluginTypeInfo> SearchPluginTypes(string namePattern)
    {
        if (string.IsNullOrWhiteSpace(namePattern))
            return Array.Empty<PluginTypeInfo>();

        var pattern = namePattern.Replace("*", ".*").Replace("?", ".");
        var regex = new System.Text.RegularExpressions.Regex(pattern, System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        return _registeredTypes.Values
            .Where(t => regex.IsMatch(t.TypeName) || regex.IsMatch(t.FriendlyName))
            .ToArray();
    }

    /// <inheritdoc />
    public bool IsPluginTypeRegistered(string typeName)
    {
        return !string.IsNullOrWhiteSpace(typeName) && _registeredTypes.ContainsKey(typeName);
    }

    /// <inheritdoc />
    public void RegisterPluginType(Type pluginType)
    {
        if (pluginType == null)
            throw new ArgumentNullException(nameof(pluginType));

        if (!IsPluginType(pluginType))
            throw new ArgumentException($"Type {pluginType.FullName} is not a valid plugin type", nameof(pluginType));

        var typeInfo = CreatePluginTypeInfo(pluginType);
        _registeredTypes[pluginType.FullName ?? pluginType.Name] = typeInfo;
    }

    /// <inheritdoc />
    public void UnregisterPluginType(string typeName)
    {
        if (!string.IsNullOrWhiteSpace(typeName))
        {
            _registeredTypes.TryRemove(typeName, out _);
        }
    }

    /// <inheritdoc />
    public IReadOnlyCollection<string> GetScannedAssemblies()
    {
        lock (_scanLock)
        {
            return _scannedAssemblies.Keys.ToArray();
        }
    }

    private static bool IsPluginType(Type type)
    {
        return type.IsClass &&
               !type.IsAbstract &&
               typeof(IPlugin).IsAssignableFrom(type) &&
               type.GetConstructor(Type.EmptyTypes) != null;
    }

    private static PluginTypeInfo CreatePluginTypeInfo(Type pluginType)
    {
        var category = DeterminePluginCategory(pluginType);
        var friendlyName = ExtractFriendlyName(pluginType);
        var description = ExtractDescription(pluginType);
        var version = ExtractVersion(pluginType);
        var metadata = ExtractMetadata(pluginType);

        return new PluginTypeInfo
        {
            TypeName = pluginType.FullName ?? pluginType.Name,
            FriendlyName = friendlyName,
            Description = description,
            Category = category,
            Version = version,
            AssemblyPath = pluginType.Assembly.Location,
            AssemblyName = pluginType.Assembly.GetName().Name ?? "Unknown",
            IsBuiltIn = IsBuiltInPlugin(pluginType),
            RequiresConfiguration = RequiresConfiguration(pluginType),
            SupportedSchemas = ExtractSupportedSchemas(pluginType),
            Metadata = metadata
        };
    }

    private static PluginCategory DeterminePluginCategory(Type pluginType)
    {
        if (typeof(ISourcePlugin).IsAssignableFrom(pluginType))
            return PluginCategory.Source;

        if (typeof(ITransformPlugin).IsAssignableFrom(pluginType))
            return PluginCategory.Transform;

        if (typeof(ISinkPlugin).IsAssignableFrom(pluginType))
            return PluginCategory.Sink;

        return PluginCategory.Other;
    }

    private static string ExtractFriendlyName(Type pluginType)
    {
        // Check for PluginAttribute or similar
        var attribute = pluginType.GetCustomAttribute<PluginDisplayNameAttribute>();
        if (attribute != null && !string.IsNullOrWhiteSpace(attribute.DisplayName))
            return attribute.DisplayName;

        // Generate friendly name from type name
        var typeName = pluginType.Name;
        if (typeName.EndsWith("Plugin", StringComparison.OrdinalIgnoreCase))
            typeName = typeName[..^6]; // Remove "Plugin" suffix

        // Insert spaces before capital letters
        return System.Text.RegularExpressions.Regex.Replace(typeName, @"(?<!^)(?=[A-Z])", " ");
    }

    private static string? ExtractDescription(Type pluginType)
    {
        var attribute = pluginType.GetCustomAttribute<PluginDescriptionAttribute>();
        return attribute?.Description;
    }

    private static string? ExtractVersion(Type pluginType)
    {
        var attribute = pluginType.GetCustomAttribute<PluginVersionAttribute>();
        if (attribute != null)
            return attribute.Version;

        // Try to get version from assembly
        var assemblyVersion = pluginType.Assembly.GetName().Version;
        return assemblyVersion?.ToString();
    }

    private static bool IsBuiltInPlugin(Type pluginType)
    {
        var assemblyName = pluginType.Assembly.GetName().Name;
        return assemblyName != null && assemblyName.StartsWith("FlowEngine.", StringComparison.OrdinalIgnoreCase);
    }

    private static bool RequiresConfiguration(Type pluginType)
    {
        return typeof(IConfigurablePlugin).IsAssignableFrom(pluginType);
    }

    private static IReadOnlyList<string> ExtractSupportedSchemas(Type pluginType)
    {
        var attributes = pluginType.GetCustomAttributes<PluginSupportedSchemaAttribute>();
        return attributes.Select(a => a.SchemaName).ToArray();
    }

    private static IReadOnlyDictionary<string, object> ExtractMetadata(Type pluginType)
    {
        var metadata = new Dictionary<string, object>();

        // Add basic metadata
        metadata["FullTypeName"] = pluginType.FullName ?? pluginType.Name;
        metadata["Namespace"] = pluginType.Namespace ?? "Unknown";
        metadata["AssemblyQualifiedName"] = pluginType.AssemblyQualifiedName ?? "Unknown";

        // Add interface information
        var interfaces = pluginType.GetInterfaces()
            .Where(i => typeof(IPlugin).IsAssignableFrom(i) && i != typeof(IPlugin))
            .Select(i => i.Name)
            .ToArray();
        metadata["Interfaces"] = interfaces;

        // Add custom attributes
        var customAttributes = pluginType.GetCustomAttributes()
            .Where(a => a.GetType().Namespace?.StartsWith("FlowEngine") == true)
            .ToDictionary(a => a.GetType().Name, a => a.ToString() ?? "");
        
        if (customAttributes.Count > 0)
            metadata["Attributes"] = customAttributes;

        return metadata;
    }
}

/// <summary>
/// Exception thrown when plugin registry operations fail.
/// </summary>
public sealed class PluginRegistryException : Exception
{
    /// <summary>
    /// Initializes a new plugin registry exception.
    /// </summary>
    /// <param name="message">Error message</param>
    public PluginRegistryException(string message) : base(message)
    {
    }

    /// <summary>
    /// Initializes a new plugin registry exception with inner exception.
    /// </summary>
    /// <param name="message">Error message</param>
    /// <param name="innerException">Inner exception</param>
    public PluginRegistryException(string message, Exception innerException) : base(message, innerException)
    {
    }
}

/// <summary>
/// Attribute to specify a plugin's display name.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
public sealed class PluginDisplayNameAttribute : Attribute
{
    /// <summary>
    /// Initializes a new plugin display name attribute.
    /// </summary>
    /// <param name="displayName">The display name for the plugin</param>
    public PluginDisplayNameAttribute(string displayName)
    {
        DisplayName = displayName ?? throw new ArgumentNullException(nameof(displayName));
    }

    /// <summary>
    /// Gets the display name for the plugin.
    /// </summary>
    public string DisplayName { get; }
}

/// <summary>
/// Attribute to specify a plugin's description.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
public sealed class PluginDescriptionAttribute : Attribute
{
    /// <summary>
    /// Initializes a new plugin description attribute.
    /// </summary>
    /// <param name="description">The description for the plugin</param>
    public PluginDescriptionAttribute(string description)
    {
        Description = description ?? throw new ArgumentNullException(nameof(description));
    }

    /// <summary>
    /// Gets the description for the plugin.
    /// </summary>
    public string Description { get; }
}

/// <summary>
/// Attribute to specify a plugin's version.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
public sealed class PluginVersionAttribute : Attribute
{
    /// <summary>
    /// Initializes a new plugin version attribute.
    /// </summary>
    /// <param name="version">The version for the plugin</param>
    public PluginVersionAttribute(string version)
    {
        Version = version ?? throw new ArgumentNullException(nameof(version));
    }

    /// <summary>
    /// Gets the version for the plugin.
    /// </summary>
    public string Version { get; }
}

/// <summary>
/// Attribute to specify schemas supported by a plugin.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
public sealed class PluginSupportedSchemaAttribute : Attribute
{
    /// <summary>
    /// Initializes a new plugin supported schema attribute.
    /// </summary>
    /// <param name="schemaName">The name of the supported schema</param>
    public PluginSupportedSchemaAttribute(string schemaName)
    {
        SchemaName = schemaName ?? throw new ArgumentNullException(nameof(schemaName));
    }

    /// <summary>
    /// Gets the name of the supported schema.
    /// </summary>
    public string SchemaName { get; }
}