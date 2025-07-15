using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace FlowEngine.Core.Configuration.Yaml;

/// <summary>
/// Root configuration class for YAML pipeline definitions.
/// Provides strongly-typed mapping without requiring post-processing normalization.
/// </summary>
public class YamlPipelineConfiguration
{
    /// <summary>
    /// Gets or sets the pipeline definition.
    /// </summary>
    [Required]
    public YamlPipelineDefinition Pipeline { get; set; } = new();
}

/// <summary>
/// Strongly-typed pipeline definition for YAML parsing.
/// Maps directly to YAML pipeline structure without requiring post-processing.
/// </summary>
public class YamlPipelineDefinition
{
    /// <summary>
    /// Gets or sets the pipeline name.
    /// </summary>
    [Required]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the pipeline version.
    /// </summary>
    public string Version { get; set; } = "1.0.0";

    /// <summary>
    /// Gets or sets the pipeline description.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Gets or sets the list of plugins in the pipeline.
    /// </summary>
    [Required]
    public List<YamlPluginDefinition> Plugins { get; set; } = new();

    /// <summary>
    /// Gets or sets the list of connections between plugins.
    /// </summary>
    public List<YamlConnectionDefinition> Connections { get; set; } = new();

    /// <summary>
    /// Gets or sets the pipeline settings.
    /// </summary>
    public YamlPipelineSettings? Settings { get; set; }

    /// <summary>
    /// Gets or sets additional pipeline metadata.
    /// </summary>
    public Dictionary<string, object>? Metadata { get; set; }
}

/// <summary>
/// Strongly-typed plugin definition for YAML parsing.
/// Uses polymorphic deserialization based on plugin type.
/// </summary>
public class YamlPluginDefinition
{
    /// <summary>
    /// Gets or sets the plugin name.
    /// </summary>
    [Required]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the plugin type.
    /// </summary>
    [Required]
    public string Type { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the plugin assembly path (optional).
    /// </summary>
    public string? Assembly { get; set; }

    /// <summary>
    /// Gets or sets the plugin configuration as a generic dictionary.
    /// This will be converted to strongly-typed configuration during processing.
    /// </summary>
    [Required]
    public Dictionary<string, object> Config { get; set; } = new();

    /// <summary>
    /// Gets or sets additional plugin metadata.
    /// </summary>
    public Dictionary<string, object>? Metadata { get; set; }
}

/// <summary>
/// Strongly-typed connection definition for YAML parsing.
/// Maps directly to YAML connection structure without requiring post-processing.
/// </summary>
public class YamlConnectionDefinition
{
    /// <summary>
    /// Gets or sets the source plugin name.
    /// </summary>
    [Required]
    public string From { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the destination plugin name.
    /// </summary>
    [Required]
    public string To { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the connection type (optional).
    /// </summary>
    public string? Type { get; set; }

    /// <summary>
    /// Gets or sets connection-specific configuration.
    /// </summary>
    public Dictionary<string, object>? Config { get; set; }

    /// <summary>
    /// Gets or sets additional connection metadata.
    /// </summary>
    public Dictionary<string, object>? Metadata { get; set; }
}

/// <summary>
/// Strongly-typed pipeline settings for YAML parsing.
/// Maps directly to YAML settings structure without requiring post-processing.
/// </summary>
public class YamlPipelineSettings
{
    /// <summary>
    /// Gets or sets the execution mode for the pipeline.
    /// </summary>
    public string ExecutionMode { get; set; } = "Sequential";

    /// <summary>
    /// Gets or sets the maximum degree of parallelism.
    /// </summary>
    [Range(1, 100)]
    public int MaxDegreeOfParallelism { get; set; } = 1;

    /// <summary>
    /// Gets or sets the pipeline timeout in milliseconds.
    /// </summary>
    [Range(1000, int.MaxValue)]
    public int TimeoutMs { get; set; } = 300000; // 5 minutes

    /// <summary>
    /// Gets or sets the retry configuration.
    /// </summary>
    public YamlRetryConfiguration? Retry { get; set; }

    /// <summary>
    /// Gets or sets the monitoring configuration.
    /// </summary>
    public YamlMonitoringConfiguration? Monitoring { get; set; }

    /// <summary>
    /// Gets or sets custom pipeline settings.
    /// </summary>
    public Dictionary<string, object>? Custom { get; set; }
}

/// <summary>
/// Strongly-typed retry configuration for YAML parsing.
/// Maps directly to YAML retry structure without requiring post-processing.
/// </summary>
public class YamlRetryConfiguration
{
    /// <summary>
    /// Gets or sets whether retry is enabled.
    /// </summary>
    public bool Enabled { get; set; } = false;

    /// <summary>
    /// Gets or sets the maximum number of retry attempts.
    /// </summary>
    [Range(0, 10)]
    public int MaxAttempts { get; set; } = 3;

    /// <summary>
    /// Gets or sets the base delay between retries in milliseconds.
    /// </summary>
    [Range(100, 60000)]
    public int BaseDelayMs { get; set; } = 1000;

    /// <summary>
    /// Gets or sets the maximum delay between retries in milliseconds.
    /// </summary>
    [Range(1000, 300000)]
    public int MaxDelayMs { get; set; } = 30000;

    /// <summary>
    /// Gets or sets the backoff strategy.
    /// </summary>
    public string BackoffStrategy { get; set; } = "Exponential";
}

/// <summary>
/// Strongly-typed monitoring configuration for YAML parsing.
/// Maps directly to YAML monitoring structure without requiring post-processing.
/// </summary>
public class YamlMonitoringConfiguration
{
    /// <summary>
    /// Gets or sets whether monitoring is enabled.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Gets or sets the metrics collection interval in milliseconds.
    /// </summary>
    [Range(100, 60000)]
    public int MetricsIntervalMs { get; set; } = 1000;

    /// <summary>
    /// Gets or sets whether to collect performance metrics.
    /// </summary>
    public bool CollectPerformanceMetrics { get; set; } = true;

    /// <summary>
    /// Gets or sets whether to collect memory metrics.
    /// </summary>
    public bool CollectMemoryMetrics { get; set; } = true;

    /// <summary>
    /// Gets or sets whether to collect error metrics.
    /// </summary>
    public bool CollectErrorMetrics { get; set; } = true;

    /// <summary>
    /// Gets or sets the log level for monitoring.
    /// </summary>
    public string LogLevel { get; set; } = "Information";
}