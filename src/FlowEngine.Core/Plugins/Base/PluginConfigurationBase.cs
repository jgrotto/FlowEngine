using FlowEngine.Abstractions.Data;
using System.Collections.Immutable;

namespace FlowEngine.Core.Plugins.Base;

/// <summary>
/// Base class for all plugin configurations in the five-component architecture.
/// Provides schema-first design with explicit field indexing for ArrayRow optimization.
/// </summary>
/// <remarks>
/// This base class implements the Configuration component of the five-component pattern:
/// Configuration → Validator → Plugin → Processor → Service
/// 
/// Key Responsibilities:
/// - Define input/output schemas with explicit field indexes
/// - Provide ArrayRow optimization through pre-calculated field access
/// - Enable schema validation and compatibility checking
/// - Support plugin-specific configuration parameters
/// 
/// Performance Requirements:
/// - Field access must be O(1) through pre-calculated indexes
/// - Schema validation must complete in &lt;1ms for typical schemas
/// - Configuration loading must support hot-swapping scenarios
/// 
/// ArrayRow Optimization:
/// The Configuration component is responsible for defining explicit field indexes
/// that enable O(1) field access. This is critical for achieving the target
/// performance of 200K+ rows/sec throughput.
/// </remarks>
public abstract class PluginConfigurationBase
{
    /// <summary>
    /// Gets the input schema definition with explicit field indexing.
    /// </summary>
    /// <remarks>
    /// The input schema defines the structure of data expected by this plugin.
    /// Field indexes must be sequential (0, 1, 2...) and most frequently
    /// accessed fields should have the lowest indexes for optimal performance.
    /// 
    /// Example:
    /// - Index 0: Primary key or identifier (most accessed)
    /// - Index 1: Primary data field
    /// - Index 2: Secondary data field
    /// - Index N: Metadata or optional fields (least accessed)
    /// </remarks>
    public abstract ISchema InputSchema { get; }

    /// <summary>
    /// Gets the output schema definition with explicit field indexing.
    /// </summary>
    /// <remarks>
    /// The output schema defines the structure of data produced by this plugin.
    /// Must be compatible with downstream plugin input schemas.
    /// Field indexing should optimize for downstream plugin access patterns.
    /// </remarks>
    public abstract ISchema OutputSchema { get; }

    /// <summary>
    /// Gets the plugin-specific configuration parameters.
    /// </summary>
    /// <remarks>
    /// Contains all configuration parameters specific to this plugin type.
    /// These parameters control plugin behavior but do not affect schema structure.
    /// Examples: file paths, connection strings, transformation rules, etc.
    /// </remarks>
    public abstract ImmutableDictionary<string, object> Parameters { get; }

    /// <summary>
    /// Gets the pre-calculated field indexes for input schema optimization.
    /// </summary>
    /// <remarks>
    /// This dictionary provides O(1) field name to index mapping for the input schema.
    /// Used by the Processor and Service components to achieve optimal ArrayRow performance.
    /// 
    /// Performance Critical: This mapping is calculated once during configuration
    /// loading and reused throughout plugin execution.
    /// </remarks>
    public virtual ImmutableDictionary<string, int> InputFieldIndexes => 
        CalculateFieldIndexes(InputSchema);

    /// <summary>
    /// Gets the pre-calculated field indexes for output schema optimization.
    /// </summary>
    /// <remarks>
    /// This dictionary provides O(1) field name to index mapping for the output schema.
    /// Used by the Service component to efficiently construct output ArrayRow instances.
    /// </remarks>
    public virtual ImmutableDictionary<string, int> OutputFieldIndexes => 
        CalculateFieldIndexes(OutputSchema);

    /// <summary>
    /// Gets the plugin type identifier.
    /// </summary>
    /// <remarks>
    /// Identifies the type of plugin (Source, Transform, Sink) for runtime
    /// type checking and pipeline validation.
    /// </remarks>
    public abstract string PluginType { get; }

    /// <summary>
    /// Gets the unique plugin identifier.
    /// </summary>
    /// <remarks>
    /// Unique identifier for this plugin instance within a pipeline.
    /// Used for error reporting, monitoring, and plugin management.
    /// </remarks>
    public abstract string PluginId { get; }

    /// <summary>
    /// Gets the plugin version for compatibility checking.
    /// </summary>
    /// <remarks>
    /// Semantic version string used for plugin compatibility validation
    /// and hot-swapping decisions.
    /// </remarks>
    public abstract string Version { get; }

    /// <summary>
    /// Gets whether this plugin supports hot-swapping.
    /// </summary>
    /// <remarks>
    /// Indicates if this plugin can be updated without stopping the pipeline.
    /// Hot-swappable plugins must maintain state compatibility across versions.
    /// </remarks>
    public virtual bool SupportsHotSwapping => false;

    /// <summary>
    /// Gets the memory usage estimate for this plugin configuration.
    /// </summary>
    /// <remarks>
    /// Estimated memory usage in bytes for resource planning and monitoring.
    /// Includes schema overhead, parameter storage, and estimated processing memory.
    /// </remarks>
    public virtual long EstimatedMemoryUsage => 
        CalculateEstimatedMemoryUsage();

    /// <summary>
    /// Gets schema compatibility constraints.
    /// </summary>
    /// <remarks>
    /// Defines rules for schema compatibility checking with other plugins.
    /// Used during pipeline validation to ensure data can flow correctly.
    /// </remarks>
    public virtual ImmutableArray<string> CompatibilityConstraints => 
        ImmutableArray<string>.Empty;

    /// <summary>
    /// Validates the configuration for correctness and performance.
    /// </summary>
    /// <returns>Validation result with any errors or warnings</returns>
    /// <remarks>
    /// Performs comprehensive validation including:
    /// - Schema structure validation
    /// - Field index sequential validation
    /// - Parameter value validation
    /// - Performance constraint checking
    /// 
    /// This method is called during plugin initialization and configuration changes.
    /// </remarks>
    public virtual ValidationResult ValidateConfiguration()
    {
        var errors = new List<string>();
        var warnings = new List<string>();

        // Validate schema structure
        ValidateSchema(InputSchema, "InputSchema", errors, warnings);
        ValidateSchema(OutputSchema, "OutputSchema", errors, warnings);

        // Validate field indexes are sequential
        ValidateFieldIndexes(InputSchema, "InputSchema", errors);
        ValidateFieldIndexes(OutputSchema, "OutputSchema", errors);

        // Validate plugin-specific parameters
        ValidateParameters(errors, warnings);

        return new ValidationResult
        {
            IsValid = errors.Count == 0,
            Errors = errors.ToImmutableArray(),
            Warnings = warnings.ToImmutableArray()
        };
    }

    /// <summary>
    /// Gets the field index for a specific field name in the input schema.
    /// </summary>
    /// <param name="fieldName">Name of the field to lookup</param>
    /// <returns>Field index for O(1) ArrayRow access</returns>
    /// <exception cref="ArgumentException">Field name not found in input schema</exception>
    /// <remarks>
    /// This method provides the critical performance optimization for ArrayRow access.
    /// It should be called once during initialization and the result cached for
    /// use in processing loops.
    /// 
    /// Performance: O(1) lookup from pre-calculated dictionary.
    /// </remarks>
    public int GetInputFieldIndex(string fieldName)
    {
        if (InputFieldIndexes.TryGetValue(fieldName, out var index))
        {
            return index;
        }
        
        throw new ArgumentException($"Field '{fieldName}' not found in input schema", nameof(fieldName));
    }

    /// <summary>
    /// Gets the field index for a specific field name in the output schema.
    /// </summary>
    /// <param name="fieldName">Name of the field to lookup</param>
    /// <returns>Field index for O(1) ArrayRow construction</returns>
    /// <exception cref="ArgumentException">Field name not found in output schema</exception>
    /// <remarks>
    /// This method enables efficient output ArrayRow construction with O(1) field setting.
    /// Used by the Service component to build output data efficiently.
    /// </remarks>
    public int GetOutputFieldIndex(string fieldName)
    {
        if (OutputFieldIndexes.TryGetValue(fieldName, out var index))
        {
            return index;
        }
        
        throw new ArgumentException($"Field '{fieldName}' not found in output schema", nameof(fieldName));
    }

    /// <summary>
    /// Checks if a schema is compatible with this plugin's input schema.
    /// </summary>
    /// <param name="sourceSchema">Source schema to check compatibility</param>
    /// <returns>True if schemas are compatible</returns>
    /// <remarks>
    /// Schema compatibility ensures that data can flow from one plugin to another
    /// without type errors or missing fields.
    /// 
    /// Compatibility Rules:
    /// - All required input fields must be present in source schema
    /// - Field types must be compatible (same type or safe conversion)
    /// - Optional fields may be missing from source
    /// </remarks>
    public virtual bool IsCompatibleWith(ISchema sourceSchema)
    {
        if (sourceSchema == null)
            return false;

        return InputSchema.IsCompatibleWith(sourceSchema);
    }

    /// <summary>
    /// Creates a clone of this configuration with modified parameters.
    /// </summary>
    /// <param name="parameterUpdates">Parameters to update</param>
    /// <returns>New configuration instance with updated parameters</returns>
    /// <remarks>
    /// Enables configuration updates for hot-swapping scenarios.
    /// The new configuration maintains schema compatibility while updating
    /// behavioral parameters.
    /// </remarks>
    public abstract PluginConfigurationBase WithParameters(ImmutableDictionary<string, object> parameterUpdates);

    /// <summary>
    /// Validates plugin-specific parameters.
    /// </summary>
    /// <param name="errors">List to add validation errors</param>
    /// <param name="warnings">List to add validation warnings</param>
    /// <remarks>
    /// Override this method to implement plugin-specific parameter validation.
    /// Common validations include: required parameters, value ranges, file existence, etc.
    /// </remarks>
    protected virtual void ValidateParameters(List<string> errors, List<string> warnings)
    {
        // Base implementation - override in derived classes
    }

    /// <summary>
    /// Calculates field name to index mapping for a schema.
    /// </summary>
    /// <param name="schema">Schema to calculate indexes for</param>
    /// <returns>Immutable dictionary mapping field names to indexes</returns>
    private static ImmutableDictionary<string, int> CalculateFieldIndexes(ISchema schema)
    {
        if (schema == null)
            return ImmutableDictionary<string, int>.Empty;

        var builder = ImmutableDictionary.CreateBuilder<string, int>();
        
        for (int i = 0; i < schema.Columns.Length; i++)
        {
            builder[schema.Columns[i].Name] = i;
        }
        
        return builder.ToImmutable();
    }

    /// <summary>
    /// Validates a schema for structural correctness.
    /// </summary>
    /// <param name="schema">Schema to validate</param>
    /// <param name="schemaName">Name of schema for error reporting</param>
    /// <param name="errors">List to add validation errors</param>
    /// <param name="warnings">List to add validation warnings</param>
    private static void ValidateSchema(ISchema? schema, string schemaName, List<string> errors, List<string> warnings)
    {
        if (schema == null)
        {
            errors.Add($"{schemaName} cannot be null");
            return;
        }

        if (schema.ColumnCount == 0)
        {
            warnings.Add($"{schemaName} has no columns defined");
            return;
        }

        // Check for duplicate column names
        var columnNames = new HashSet<string>();
        foreach (var column in schema.Columns)
        {
            if (!columnNames.Add(column.Name))
            {
                errors.Add($"{schemaName} contains duplicate column name: {column.Name}");
            }
        }

        // Check for performance-critical field ordering
        if (schema.ColumnCount > 10)
        {
            warnings.Add($"{schemaName} has {schema.ColumnCount} columns. Consider placing most frequently accessed fields first for optimal performance.");
        }
    }

    /// <summary>
    /// Validates that field indexes are sequential for ArrayRow optimization.
    /// </summary>
    /// <param name="schema">Schema to validate</param>
    /// <param name="schemaName">Name of schema for error reporting</param>
    /// <param name="errors">List to add validation errors</param>
    private static void ValidateFieldIndexes(ISchema schema, string schemaName, List<string> errors)
    {
        if (schema == null || schema.ColumnCount == 0)
            return;

        // Verify indexes are sequential starting from 0
        for (int i = 0; i < schema.ColumnCount; i++)
        {
            var expectedIndex = i;
            var actualIndex = schema.GetIndex(schema.Columns[i].Name);
            
            if (actualIndex != expectedIndex)
            {
                errors.Add($"{schemaName} field '{schema.Columns[i].Name}' has index {actualIndex}, expected {expectedIndex}. Indexes must be sequential for ArrayRow optimization.");
            }
        }
    }

    /// <summary>
    /// Calculates estimated memory usage for this configuration.
    /// </summary>
    /// <returns>Estimated memory usage in bytes</returns>
    private long CalculateEstimatedMemoryUsage()
    {
        var baseSize = 1024; // Base plugin overhead
        
        // Schema memory overhead
        var schemaSize = (InputSchema?.ColumnCount ?? 0) * 128 + 
                        (OutputSchema?.ColumnCount ?? 0) * 128;
        
        // Parameter storage overhead
        var parameterSize = Parameters.Count * 64;
        
        return baseSize + schemaSize + parameterSize;
    }
}

/// <summary>
/// Configuration validation result.
/// </summary>
public sealed record ValidationResult
{
    /// <summary>
    /// Gets whether the configuration is valid.
    /// </summary>
    public required bool IsValid { get; init; }
    
    /// <summary>
    /// Gets validation errors that prevent plugin execution.
    /// </summary>
    public required ImmutableArray<string> Errors { get; init; }
    
    /// <summary>
    /// Gets validation warnings that may affect performance or reliability.
    /// </summary>
    public required ImmutableArray<string> Warnings { get; init; }
}