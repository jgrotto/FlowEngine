namespace FlowEngine.Abstractions.Configuration;

/// <summary>
/// Represents a complete pipeline configuration loaded from YAML.
/// Defines the structure, plugins, connections, and settings for a data processing pipeline.
/// </summary>
public interface IPipelineConfiguration
{
    /// <summary>
    /// Gets the unique name of this pipeline.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Gets the version of this pipeline configuration.
    /// </summary>
    string Version { get; }

    /// <summary>
    /// Gets the optional description of this pipeline.
    /// </summary>
    string? Description { get; }

    /// <summary>
    /// Gets the plugin configurations defined in this pipeline.
    /// </summary>
    IReadOnlyList<IPluginConfiguration> Plugins { get; }

    /// <summary>
    /// Gets the connection configurations that define data flow between plugins.
    /// </summary>
    IReadOnlyList<IConnectionConfiguration> Connections { get; }

    /// <summary>
    /// Gets the global settings for this pipeline.
    /// </summary>
    IPipelineSettings Settings { get; }

    /// <summary>
    /// Gets optional metadata associated with this pipeline.
    /// </summary>
    IReadOnlyDictionary<string, object>? Metadata { get; }

    /// <summary>
    /// Validates the pipeline configuration for consistency and completeness.
    /// </summary>
    /// <returns>Validation result with any errors or warnings</returns>
    ConfigurationValidationResult Validate();
}

/// <summary>
/// Configuration for a single plugin instance in the pipeline.
/// </summary>
public interface IPluginConfiguration
{
    /// <summary>
    /// Gets the unique name of this plugin instance within the pipeline.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Gets the type name of the plugin class to instantiate.
    /// </summary>
    string Type { get; }

    /// <summary>
    /// Gets the optional assembly path for custom plugins.
    /// If null, the plugin is assumed to be in the core FlowEngine assemblies.
    /// </summary>
    string? Assembly { get; }

    /// <summary>
    /// Gets the schema configuration for this plugin.
    /// Required for source and transform plugins, optional for sinks.
    /// </summary>
    ISchemaConfiguration? Schema { get; }

    /// <summary>
    /// Gets the plugin-specific configuration settings.
    /// </summary>
    IReadOnlyDictionary<string, object> Config { get; }

    /// <summary>
    /// Gets the resource limits for this plugin instance.
    /// </summary>
    IResourceLimits? ResourceLimits { get; }
}

/// <summary>
/// Configuration for data flow connections between plugins.
/// </summary>
public interface IConnectionConfiguration
{
    /// <summary>
    /// Gets the name of the source plugin.
    /// </summary>
    string From { get; }

    /// <summary>
    /// Gets the name of the destination plugin.
    /// </summary>
    string To { get; }

    /// <summary>
    /// Gets the optional name of the output port on the source plugin.
    /// Default port is used if not specified.
    /// </summary>
    string? FromPort { get; }

    /// <summary>
    /// Gets the optional name of the input port on the destination plugin.
    /// Default port is used if not specified.
    /// </summary>
    string? ToPort { get; }

    /// <summary>
    /// Gets the channel configuration for this connection.
    /// </summary>
    IChannelConfiguration? Channel { get; }
}

/// <summary>
/// Schema configuration from YAML pipeline definitions.
/// </summary>
public interface ISchemaConfiguration
{
    /// <summary>
    /// Gets the name of this schema.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Gets the version of this schema for compatibility checking.
    /// </summary>
    string Version { get; }

    /// <summary>
    /// Gets the field definitions for this schema.
    /// </summary>
    IReadOnlyList<IFieldConfiguration> Fields { get; }

    /// <summary>
    /// Gets optional metadata for this schema.
    /// </summary>
    IReadOnlyDictionary<string, object>? Metadata { get; }

    /// <summary>
    /// Converts this configuration to a runtime schema instance.
    /// </summary>
    /// <returns>Runtime schema</returns>
    ISchema ToSchema();
}

/// <summary>
/// Field definition configuration from YAML.
/// </summary>
public interface IFieldConfiguration
{
    /// <summary>
    /// Gets the name of this field.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Gets the data type of this field.
    /// </summary>
    string Type { get; }

    /// <summary>
    /// Gets whether this field is required (non-nullable).
    /// </summary>
    bool Required { get; }

    /// <summary>
    /// Gets the optional default value for this field.
    /// </summary>
    object? Default { get; }

    /// <summary>
    /// Gets optional field metadata.
    /// </summary>
    IReadOnlyDictionary<string, object>? Metadata { get; }
}

/// <summary>
/// Global pipeline settings configuration.
/// </summary>
public interface IPipelineSettings
{
    /// <summary>
    /// Gets the default channel configuration.
    /// </summary>
    IChannelConfiguration? DefaultChannel { get; }

    /// <summary>
    /// Gets the default resource limits for plugins.
    /// </summary>
    IResourceLimits? DefaultResourceLimits { get; }

    /// <summary>
    /// Gets the monitoring configuration.
    /// </summary>
    IMonitoringConfiguration? Monitoring { get; }

    /// <summary>
    /// Gets custom settings as key-value pairs.
    /// </summary>
    IReadOnlyDictionary<string, object> Custom { get; }
}

/// <summary>
/// Channel configuration for data flow between plugins.
/// </summary>
public interface IChannelConfiguration
{
    /// <summary>
    /// Gets the buffer size for the channel.
    /// </summary>
    int BufferSize { get; }

    /// <summary>
    /// Gets the backpressure threshold as a percentage (0-100).
    /// </summary>
    int BackpressureThreshold { get; }

    /// <summary>
    /// Gets the behavior when the channel is full.
    /// </summary>
    ChannelFullMode FullMode { get; }

    /// <summary>
    /// Gets the timeout for channel operations.
    /// </summary>
    TimeSpan Timeout { get; }
}

/// <summary>
/// Resource limits for plugin execution.
/// </summary>
public interface IResourceLimits
{
    /// <summary>
    /// Gets the maximum memory usage in megabytes.
    /// </summary>
    int? MaxMemoryMB { get; }

    /// <summary>
    /// Gets the execution timeout in seconds.
    /// </summary>
    int? TimeoutSeconds { get; }

    /// <summary>
    /// Gets whether spillover to disk is enabled for memory pressure.
    /// </summary>
    bool? EnableSpillover { get; }

    /// <summary>
    /// Gets the maximum number of file handles.
    /// </summary>
    int? MaxFileHandles { get; }
}

/// <summary>
/// Monitoring and observability configuration.
/// </summary>
public interface IMonitoringConfiguration
{
    /// <summary>
    /// Gets whether performance metrics collection is enabled.
    /// </summary>
    bool EnableMetrics { get; }

    /// <summary>
    /// Gets the metrics collection interval.
    /// </summary>
    TimeSpan MetricsInterval { get; }

    /// <summary>
    /// Gets whether distributed tracing is enabled.
    /// </summary>
    bool EnableTracing { get; }

    /// <summary>
    /// Gets custom monitoring settings.
    /// </summary>
    IReadOnlyDictionary<string, object> Custom { get; }
}

/// <summary>
/// Behavior when a channel buffer is full.
/// </summary>
public enum ChannelFullMode
{
    /// <summary>
    /// Wait for space to become available (applies backpressure).
    /// </summary>
    Wait,

    /// <summary>
    /// Drop the oldest item and add the new one.
    /// </summary>
    DropOldest,

    /// <summary>
    /// Drop the newest item (reject the write).
    /// </summary>
    DropNewest,

    /// <summary>
    /// Throw an exception when the channel is full.
    /// </summary>
    ThrowException
}

/// <summary>
/// Result of configuration validation.
/// </summary>
public sealed record ConfigurationValidationResult
{
    /// <summary>
    /// Gets whether the configuration is valid.
    /// </summary>
    public bool IsValid { get; init; }

    /// <summary>
    /// Gets validation error messages.
    /// </summary>
    public IReadOnlyList<string> Errors { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Gets validation warning messages.
    /// </summary>
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Creates a successful validation result.
    /// </summary>
    public static ConfigurationValidationResult Success() => new() { IsValid = true };

    /// <summary>
    /// Creates a successful validation result with warnings.
    /// </summary>
    public static ConfigurationValidationResult SuccessWithWarnings(params string[] warnings) => 
        new() { IsValid = true, Warnings = warnings };

    /// <summary>
    /// Creates a failed validation result.
    /// </summary>
    public static ConfigurationValidationResult Failure(params string[] errors) => 
        new() { IsValid = false, Errors = errors };
}