using FlowEngine.Abstractions;
using FlowEngine.Abstractions.Configuration;
using FlowEngine.Abstractions.Data;
using FlowEngine.Abstractions.Plugins;
using FlowEngine.Core.Data;
using System.Collections.Immutable;

namespace FlowEngine.Core.Configuration;

/// <summary>
/// Implementation of plugin definition from YAML data.
/// Represents the parsed plugin definition before conversion to runtime configuration.
/// </summary>
internal sealed class PluginConfiguration : IPluginDefinition
{
    private readonly PluginData _data;

    public PluginConfiguration(PluginData data)
    {
        _data = data ?? throw new ArgumentNullException(nameof(data));
    }

    /// <inheritdoc />
    public string Name => _data.Name ?? throw new InvalidOperationException("Plugin name is required");

    /// <inheritdoc />
    public string Type => _data.Type ?? throw new InvalidOperationException("Plugin type is required");

    /// <inheritdoc />
    public string? AssemblyPath => _data.Assembly;

    /// <inheritdoc />
    public ISchemaDefinition? InputSchema => null; // TODO: Implement schema parsing

    /// <inheritdoc />
    public ISchemaDefinition? OutputSchema => null; // TODO: Implement schema parsing

    /// <inheritdoc />
    public IReadOnlyDictionary<string, object> Configuration => 
        _data.Config ?? (IReadOnlyDictionary<string, object>)new Dictionary<string, object>();

    /// <inheritdoc />
    public bool SupportsHotSwapping => false; // TODO: Parse from YAML

    /// <inheritdoc />
    public IReadOnlyDictionary<string, object>? Metadata => null; // TODO: Parse from YAML

    // Legacy properties for backward compatibility
    public string? Assembly => _data.Assembly;
    public ISchemaConfiguration? Schema => _data.Schema != null ? new SchemaConfiguration(_data.Schema) : null;
    public IReadOnlyDictionary<string, object> Config => Configuration;
    public IResourceLimits? ResourceLimits => 
        _data.ResourceLimits != null ? new ResourceLimits(_data.ResourceLimits) : null;
}

/// <summary>
/// Implementation of schema configuration from YAML data.
/// </summary>
internal sealed class SchemaConfiguration : ISchemaConfiguration
{
    private readonly SchemaData _data;

    public SchemaConfiguration(SchemaData data)
    {
        _data = data ?? throw new ArgumentNullException(nameof(data));
    }

    /// <inheritdoc />
    public string Name => _data.Name ?? throw new InvalidOperationException("Schema name is required");

    /// <inheritdoc />
    public string Version => _data.Version ?? "1.0.0";

    /// <inheritdoc />
    public IReadOnlyList<IFieldConfiguration> Fields =>
        _data.Fields?.Select(f => new FieldConfiguration(f)).ToArray() ?? Array.Empty<IFieldConfiguration>();

    /// <inheritdoc />
    public IReadOnlyDictionary<string, object>? Metadata => _data.Metadata;

    /// <inheritdoc />
    public ISchema ToSchema()
    {
        var columns = Fields.Select(field => new ColumnDefinition
        {
            Name = field.Name,
            DataType = ParseDataType(field.Type),
            IsNullable = !field.Required,
            Description = field.Metadata?.TryGetValue("description", out var desc) == true ? desc?.ToString() : null,
            Metadata = field.Metadata?.ToImmutableDictionary() ?? ImmutableDictionary<string, object>.Empty
        }).ToArray();

        return Schema.GetOrCreate(columns);
    }

    /// <summary>
    /// Validates this schema configuration.
    /// </summary>
    public ConfigurationValidationResult Validate()
    {
        var errors = new List<string>();
        var warnings = new List<string>();

        if (string.IsNullOrWhiteSpace(Name))
            errors.Add("Schema name is required");

        if (Fields.Count == 0)
            errors.Add("Schema must contain at least one field");

        // Validate field names are unique
        var fieldNames = Fields.Select(f => f.Name).ToList();
        var duplicateNames = fieldNames.GroupBy(n => n).Where(g => g.Count() > 1).Select(g => g.Key);
        foreach (var name in duplicateNames)
            errors.Add($"Duplicate field name: {name}");

        // Validate field types
        foreach (var field in Fields)
        {
            if (string.IsNullOrWhiteSpace(field.Name))
                errors.Add("Field name is required");

            try
            {
                ParseDataType(field.Type);
            }
            catch (ArgumentException ex)
            {
                errors.Add($"Invalid field type '{field.Type}' for field '{field.Name}': {ex.Message}");
            }
        }

        return errors.Count > 0 
            ? ConfigurationValidationResult.Failure(errors.ToArray())
            : warnings.Count > 0 
                ? ConfigurationValidationResult.SuccessWithWarnings(warnings.ToArray())
                : ConfigurationValidationResult.Success();
    }

    private static Type ParseDataType(string typeName)
    {
        return typeName?.ToLowerInvariant() switch
        {
            "int32" or "int" => typeof(int),
            "int64" or "long" => typeof(long),
            "float32" or "float" => typeof(float),
            "float64" or "double" => typeof(double),
            "decimal" => typeof(decimal),
            "bool" or "boolean" => typeof(bool),
            "string" => typeof(string),
            "datetime" => typeof(DateTime),
            "datetimeoffset" => typeof(DateTimeOffset),
            "timespan" => typeof(TimeSpan),
            "guid" => typeof(Guid),
            "byte" => typeof(byte),
            "sbyte" => typeof(sbyte),
            "int16" or "short" => typeof(short),
            "uint16" or "ushort" => typeof(ushort),
            "uint32" or "uint" => typeof(uint),
            "uint64" or "ulong" => typeof(ulong),
            _ => throw new ArgumentException($"Unknown data type: {typeName}")
        };
    }
}

/// <summary>
/// Implementation of field configuration from YAML data.
/// </summary>
internal sealed class FieldConfiguration : IFieldConfiguration
{
    private readonly FieldData _data;

    public FieldConfiguration(FieldData data)
    {
        _data = data ?? throw new ArgumentNullException(nameof(data));
    }

    /// <inheritdoc />
    public string Name => _data.Name ?? throw new InvalidOperationException("Field name is required");

    /// <inheritdoc />
    public string Type => _data.Type ?? throw new InvalidOperationException("Field type is required");

    /// <inheritdoc />
    public bool Required => _data.Required;

    /// <inheritdoc />
    public object? Default => _data.Default;

    /// <inheritdoc />
    public IReadOnlyDictionary<string, object>? Metadata => _data.Metadata;
}

/// <summary>
/// Implementation of connection configuration from YAML data.
/// </summary>
internal sealed class ConnectionConfiguration : IConnectionConfiguration
{
    private readonly ConnectionData _data;

    public ConnectionConfiguration(ConnectionData data)
    {
        _data = data ?? throw new ArgumentNullException(nameof(data));
    }

    /// <inheritdoc />
    public string From => _data.From ?? throw new InvalidOperationException("Connection source is required");

    /// <inheritdoc />
    public string To => _data.To ?? throw new InvalidOperationException("Connection destination is required");

    /// <inheritdoc />
    public string? FromPort => _data.FromPort;

    /// <inheritdoc />
    public string? ToPort => _data.ToPort;

    /// <inheritdoc />
    public IChannelConfiguration? Channel => _data.Channel != null ? new ChannelConfiguration(_data.Channel) : null;
}

/// <summary>
/// Implementation of pipeline settings from YAML data.
/// </summary>
internal sealed class PipelineSettings : IPipelineSettings
{
    private readonly SettingsData? _data;

    public PipelineSettings(SettingsData? data)
    {
        _data = data;
    }

    /// <inheritdoc />
    public IChannelConfiguration? DefaultChannel => 
        _data?.DefaultChannel != null ? new ChannelConfiguration(_data.DefaultChannel) : null;

    /// <inheritdoc />
    public IResourceLimits? DefaultResourceLimits => 
        _data?.DefaultResourceLimits != null ? new ResourceLimits(_data.DefaultResourceLimits) : null;

    /// <inheritdoc />
    public IMonitoringConfiguration? Monitoring => 
        _data?.Monitoring != null ? new MonitoringConfiguration(_data.Monitoring) : null;

    /// <inheritdoc />
    public IReadOnlyDictionary<string, object> Custom => 
        _data?.Custom ?? (IReadOnlyDictionary<string, object>)new Dictionary<string, object>();
}

/// <summary>
/// Implementation of channel configuration from YAML data.
/// </summary>
internal sealed class ChannelConfiguration : IChannelConfiguration
{
    private readonly ChannelData _data;

    public ChannelConfiguration(ChannelData data)
    {
        _data = data ?? throw new ArgumentNullException(nameof(data));
    }

    /// <inheritdoc />
    public int BufferSize => _data.BufferSize;

    /// <inheritdoc />
    public int BackpressureThreshold => _data.BackpressureThreshold;

    /// <inheritdoc />
    public ChannelFullMode FullMode => ParseFullMode(_data.FullMode);

    /// <inheritdoc />
    public TimeSpan Timeout => TimeSpan.FromSeconds(_data.TimeoutSeconds);

    private static ChannelFullMode ParseFullMode(string? mode)
    {
        return mode?.ToLowerInvariant() switch
        {
            "wait" => ChannelFullMode.Wait,
            "dropoldest" => ChannelFullMode.DropOldest,
            "dropnewest" => ChannelFullMode.DropNewest,
            "throwexception" => ChannelFullMode.ThrowException,
            _ => ChannelFullMode.Wait
        };
    }
}

/// <summary>
/// Implementation of resource limits from YAML data.
/// </summary>
internal sealed class ResourceLimits : IResourceLimits
{
    private readonly ResourceLimitsData _data;

    public ResourceLimits(ResourceLimitsData data)
    {
        _data = data ?? throw new ArgumentNullException(nameof(data));
    }

    /// <inheritdoc />
    public int? MaxMemoryMB => _data.MaxMemoryMB;

    /// <inheritdoc />
    public int? TimeoutSeconds => _data.TimeoutSeconds;

    /// <inheritdoc />
    public bool? EnableSpillover => _data.EnableSpillover;

    /// <inheritdoc />
    public int? MaxFileHandles => _data.MaxFileHandles;
}

/// <summary>
/// Implementation of monitoring configuration from YAML data.
/// </summary>
internal sealed class MonitoringConfiguration : IMonitoringConfiguration
{
    private readonly MonitoringData _data;

    public MonitoringConfiguration(MonitoringData data)
    {
        _data = data ?? throw new ArgumentNullException(nameof(data));
    }

    /// <inheritdoc />
    public bool EnableMetrics => _data.EnableMetrics;

    /// <inheritdoc />
    public TimeSpan MetricsInterval => TimeSpan.FromSeconds(_data.MetricsIntervalSeconds);

    /// <inheritdoc />
    public bool EnableTracing => _data.EnableTracing;

    /// <inheritdoc />
    public IReadOnlyDictionary<string, object> Custom => 
        _data.Custom ?? (IReadOnlyDictionary<string, object>)new Dictionary<string, object>();
}