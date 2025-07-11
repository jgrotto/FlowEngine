using FlowEngine.Abstractions.Data;
using FlowEngine.Abstractions.Plugins;
using System.Collections.Immutable;
using System.ComponentModel.DataAnnotations;

namespace TemplatePlugin;

/// <summary>
/// COMPONENT 1: Configuration
/// 
/// Demonstrates proper Phase 3 configuration with:
/// - Schema-mapped YAML configuration
/// - Embedded field definitions with explicit indexing
/// - Configuration validation attributes
/// - ArrayRow optimization field mappings
/// </summary>
public sealed class TemplatePluginConfiguration : IPluginConfiguration
{
    /// <summary>
    /// Template-specific configuration: Number of rows to generate
    /// </summary>
    [Required]
    [Range(1, 1_000_000, ErrorMessage = "RowCount must be between 1 and 1,000,000")]
    public int RowCount { get; init; } = 1000;

    /// <summary>
    /// Template-specific configuration: Batch size for processing
    /// </summary>
    [Required]
    [Range(1, 10_000, ErrorMessage = "BatchSize must be between 1 and 10,000")]
    public int BatchSize { get; init; } = 100;

    /// <summary>
    /// Template-specific configuration: Data type to generate
    /// </summary>
    [Required]
    public string DataType { get; init; } = "TestData";

    /// <summary>
    /// Template-specific configuration: Delay in milliseconds between batch processing
    /// </summary>
    [Range(0, 5000, ErrorMessage = "DelayMs must be between 0 and 5,000")]
    public int DelayMs { get; init; } = 0;

    // === IPluginConfiguration Implementation ===

    /// <inheritdoc />
    public string PluginId { get; init; } = "phase3-template-plugin";

    /// <inheritdoc />
    public string Name { get; init; } = "Phase 3 Template Plugin";

    /// <inheritdoc />
    public string Version { get; init; } = "3.0.0";

    /// <inheritdoc />
    public string PluginType { get; init; } = "Source";

    /// <inheritdoc />
    public ISchema? InputSchema { get; init; }

    /// <inheritdoc />
    public ISchema? OutputSchema { get; init; }

    /// <inheritdoc />
    public bool SupportsHotSwapping { get; init; } = true;

    // === ArrayRow Optimization Fields ===

    private readonly ImmutableDictionary<string, int> _inputFieldIndexes;
    private readonly ImmutableDictionary<string, int> _outputFieldIndexes;
    private readonly ImmutableDictionary<string, object> _properties;

    /// <summary>
    /// Constructor for creating configuration with proper field index calculation
    /// </summary>
    public TemplatePluginConfiguration()
    {
        _inputFieldIndexes = CalculateFieldIndexes(InputSchema);
        _outputFieldIndexes = CalculateFieldIndexes(OutputSchema);
        _properties = CreateDefaultProperties();
    }

    /// <inheritdoc />
    public ImmutableDictionary<string, int> InputFieldIndexes => _inputFieldIndexes;

    /// <inheritdoc />
    public ImmutableDictionary<string, int> OutputFieldIndexes => _outputFieldIndexes;

    /// <inheritdoc />
    public ImmutableDictionary<string, object> Properties => _properties;

    /// <inheritdoc />
    public int GetInputFieldIndex(string fieldName)
    {
        if (_inputFieldIndexes.TryGetValue(fieldName, out var index))
        {
            return index;
        }

        throw new ArgumentException($"Field '{fieldName}' not found in input schema", nameof(fieldName));
    }

    /// <inheritdoc />
    public int GetOutputFieldIndex(string fieldName)
    {
        if (_outputFieldIndexes.TryGetValue(fieldName, out var index))
        {
            return index;
        }

        throw new ArgumentException($"Field '{fieldName}' not found in output schema", nameof(fieldName));
    }

    /// <inheritdoc />
    public bool IsCompatibleWith(ISchema inputSchema)
    {
        // For source plugins, input schema should be null
        return inputSchema == null;
    }

    /// <inheritdoc />
    public T GetProperty<T>(string key)
    {
        if (!_properties.TryGetValue(key, out var value))
        {
            throw new KeyNotFoundException($"Property '{key}' not found");
        }

        if (value is T typedValue)
        {
            return typedValue;
        }

        return (T)Convert.ChangeType(value, typeof(T));
    }

    /// <inheritdoc />
    public bool TryGetProperty<T>(string key, out T? value)
    {
        value = default;
        if (!_properties.TryGetValue(key, out var objectValue))
        {
            return false;
        }

        try
        {
            if (objectValue is T typedValue)
            {
                value = typedValue;
                return true;
            }
            value = (T)Convert.ChangeType(objectValue, typeof(T));
            return true;
        }
        catch
        {
            return false;
        }
    }

    // === Helper Methods ===

    /// <summary>
    /// Calculates field indexes for ArrayRow optimization.
    /// CRITICAL: Must produce sequential indexes (0, 1, 2, ...) for ArrayRow performance.
    /// </summary>
    private static ImmutableDictionary<string, int> CalculateFieldIndexes(ISchema? schema)
    {
        if (schema == null)
        {
            return ImmutableDictionary<string, int>.Empty;
        }

        var builder = ImmutableDictionary.CreateBuilder<string, int>();

        // IMPORTANT: Sequential indexing is required for ArrayRow optimization
        for (int i = 0; i < schema.Columns.Length; i++)
        {
            builder[schema.Columns[i].Name] = i;
        }

        return builder.ToImmutable();
    }

    /// <summary>
    /// Creates default properties for the plugin
    /// </summary>
    private ImmutableDictionary<string, object> CreateDefaultProperties()
    {
        return ImmutableDictionary<string, object>.Empty
            .Add("PerformanceTarget", "200K+ rows/sec")
            .Add("MemoryOptimized", true)
            .Add("ArrayRowOptimized", true)
            .Add("SupportsBatching", true)
            .Add("ConfigurableFields", new[] { "RowCount", "BatchSize", "DataType" });
    }

    /// <summary>
    /// Creates the output schema for this template plugin.
    /// Demonstrates proper schema creation with explicit field indexing.
    /// </summary>
    public static TemplatePluginConfiguration CreateWithOutputSchema()
    {
        // Define output schema with explicit field ordering
        var columns = new[]
        {
            new ColumnDefinition
            {
                Name = "Id",
                DataType = typeof(int),
                IsNullable = false,
                Description = "Sequential ID",
                Index = 0  // Explicit index for ArrayRow optimization
            },
            new ColumnDefinition
            {
                Name = "Name",
                DataType = typeof(string),
                IsNullable = false,
                Description = "Generated name",
                Index = 1  // Must be sequential
            },
            new ColumnDefinition
            {
                Name = "Value",
                DataType = typeof(decimal),
                IsNullable = false,
                Description = "Generated value",
                Index = 2  // Must be sequential
            },
            new ColumnDefinition
            {
                Name = "Timestamp",
                DataType = typeof(DateTime),
                IsNullable = false,
                Description = "Creation timestamp",
                Index = 3  // Must be sequential
            }
        };

        // TODO: Need to resolve ISchema creation - this is where we'll need Core bridge
        // var outputSchema = Schema.Create(columns); 

        return new TemplatePluginConfiguration
        {
            // OutputSchema = outputSchema  // Will be set once we have Core bridge
        };
    }
}
