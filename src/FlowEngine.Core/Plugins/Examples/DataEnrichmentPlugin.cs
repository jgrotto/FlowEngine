using FlowEngine.Abstractions;
using FlowEngine.Abstractions.Plugins;
using FlowEngine.Core.Data;
using Microsoft.Extensions.Configuration;
using System.Collections.Immutable;
using System.Linq;

namespace FlowEngine.Core.Plugins.Examples;

/// <summary>
/// Example transform plugin that enriches data with calculated fields.
/// Adds salary bands, full names, and email domains to customer data.
/// </summary>
[PluginDisplayName("Data Enrichment")]
[PluginDescription("Enriches customer data with calculated fields")]
[PluginVersion("1.0.0")]
public sealed class DataEnrichmentPlugin : ITransformPlugin, IConfigurablePlugin, ISchemaAwarePlugin
{
    private ISchema? _inputSchema;
    private ISchema? _outputSchema;
    private bool _addSalaryBand = true;
    private bool _addFullName = true;
    private bool _addEmailDomain = true;
    private bool _addAgeCategory = false;

    /// <inheritdoc />
    public string Name { get; private set; } = "DataEnrichment";

    /// <inheritdoc />
    public ISchema? OutputSchema => _outputSchema;

    /// <inheritdoc />
    public ISchema InputSchema => _inputSchema ?? throw new InvalidOperationException("Input schema not set");

    /// <inheritdoc />
    public bool IsStateful => false;

    /// <inheritdoc />
    public TransformMode Mode => TransformMode.OneToOne;

    /// <inheritdoc />
#pragma warning disable CS1998 // Async method lacks 'await' operators
    public async IAsyncEnumerable<IChunk> FlushAsync([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        // This transform is stateless, so no flushing is needed
        yield break;
    }
#pragma warning restore CS1998

    /// <inheritdoc />
    public string Version => "1.0.0";

    /// <inheritdoc />
    public IReadOnlyDictionary<string, object>? Metadata => new Dictionary<string, object>
    {
        ["Type"] = "Transform",
        ["Category"] = "Data Enhancement",
        ["Description"] = "Adds calculated fields to enrich customer data"
    };

    /// <inheritdoc />
    public Task InitializeAsync(IConfiguration config, CancellationToken cancellationToken = default)
    {
        // Extract configuration
        if (config["Name"] != null)
            Name = config["Name"]!;

        if (bool.TryParse(config["AddSalaryBand"], out var addSalaryBand))
            _addSalaryBand = addSalaryBand;

        if (bool.TryParse(config["AddFullName"], out var addFullName))
            _addFullName = addFullName;

        if (bool.TryParse(config["AddEmailDomain"], out var addEmailDomain))
            _addEmailDomain = addEmailDomain;

        if (bool.TryParse(config["AddAgeCategory"], out var addAgeCategory))
            _addAgeCategory = addAgeCategory;

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public PluginValidationResult ValidateInputSchema(ISchema? inputSchema)
    {
        if (inputSchema == null)
            return PluginValidationResult.Failure("Transform plugins require an input schema");

        var errors = new List<string>();
        var warnings = new List<string>();

        // Check for required columns
        var requiredColumns = new[]
        {
            ("FirstName", typeof(string)),
            ("LastName", typeof(string)),
            ("Email", typeof(string)),
            ("Salary", typeof(decimal))
        };

        foreach (var (columnName, expectedType) in requiredColumns)
        {
            var column = inputSchema.GetColumn(columnName);
            if (column == null)
            {
                errors.Add($"Required column '{columnName}' not found in input schema");
                continue;
            }

            if (column.DataType != expectedType)
            {
                warnings.Add($"Column '{columnName}' has type {column.DataType.Name}, expected {expectedType.Name}");
            }
        }

        return errors.Count > 0
            ? PluginValidationResult.Failure(errors.ToArray())
            : PluginValidationResult.SuccessWithWarnings(warnings.ToArray());
    }

    /// <inheritdoc />
    public PluginMetrics GetMetrics()
    {
        return new PluginMetrics
        {
            TotalChunksProcessed = 0, // Would be tracked during execution
            TotalRowsProcessed = 0,
            TotalProcessingTime = TimeSpan.Zero,
            CurrentMemoryUsage = GC.GetTotalMemory(false),
            PeakMemoryUsage = GC.GetTotalMemory(false),
            ErrorCount = 0,
            Timestamp = DateTimeOffset.UtcNow
        };
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<IChunk> TransformAsync(IAsyncEnumerable<IChunk> inputChunks, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (_outputSchema == null)
            throw new PluginInitializationException("Plugin not initialized - output schema is null");

        await foreach (var inputChunk in inputChunks.WithCancellation(cancellationToken))
        {
            using (inputChunk) // Ensure proper disposal
            {
                var transformedRows = new List<ArrayRow>();

                for (int i = 0; i < inputChunk.RowCount; i++)
                {
                    var inputRow = (ArrayRow)inputChunk.Rows[i];
                    var transformedRow = TransformRow(inputRow);
                    transformedRows.Add(transformedRow);
                }

                var metadata = (inputChunk.Metadata ?? new Dictionary<string, object>())
                    .Concat(new Dictionary<string, object>
                    {
                        ["TransformedBy"] = Name,
                        ["TransformedAt"] = DateTimeOffset.UtcNow
                    })
                    .ToImmutableDictionary(kvp => kvp.Key, kvp => kvp.Value);

                var outputChunk = new Chunk(_outputSchema, transformedRows.ToArray(), metadata);
                yield return outputChunk;
            }
        }
    }

    /// <inheritdoc />
    public async Task ConfigureAsync(IReadOnlyDictionary<string, object> config)
    {
        if (config.TryGetValue("AddSalaryBand", out var salaryBandObj) && bool.TryParse(salaryBandObj?.ToString(), out var addSalaryBand))
            _addSalaryBand = addSalaryBand;

        if (config.TryGetValue("AddFullName", out var fullNameObj) && bool.TryParse(fullNameObj?.ToString(), out var addFullName))
            _addFullName = addFullName;

        if (config.TryGetValue("AddEmailDomain", out var emailDomainObj) && bool.TryParse(emailDomainObj?.ToString(), out var addEmailDomain))
            _addEmailDomain = addEmailDomain;

        if (config.TryGetValue("AddAgeCategory", out var ageCategoryObj) && bool.TryParse(ageCategoryObj?.ToString(), out var addAgeCategory))
            _addAgeCategory = addAgeCategory;

        await Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task SetSchemaAsync(ISchema schema, CancellationToken cancellationToken = default)
    {
        _inputSchema = schema ?? throw new ArgumentNullException(nameof(schema));

        // Build output schema by adding calculated columns
        var outputColumns = new List<ColumnDefinition>();

        // Copy all input columns
        foreach (var column in _inputSchema.Columns)
        {
            outputColumns.Add(column);
        }

        // Add calculated columns based on configuration
        if (_addFullName)
        {
            outputColumns.Add(new ColumnDefinition
            {
                Name = "FullName",
                DataType = typeof(string),
                IsNullable = false,
                Description = "Concatenated first and last name"
            });
        }

        if (_addSalaryBand)
        {
            outputColumns.Add(new ColumnDefinition
            {
                Name = "SalaryBand",
                DataType = typeof(string),
                IsNullable = false,
                Description = "Salary band category (Low/Medium/High/Executive)"
            });
        }

        if (_addEmailDomain)
        {
            outputColumns.Add(new ColumnDefinition
            {
                Name = "EmailDomain",
                DataType = typeof(string),
                IsNullable = false,
                Description = "Domain extracted from email address"
            });
        }

        if (_addAgeCategory)
        {
            outputColumns.Add(new ColumnDefinition
            {
                Name = "AgeCategory",
                DataType = typeof(string),
                IsNullable = false,
                Description = "Age category based on creation date"
            });
        }

        _outputSchema = Schema.GetOrCreate(outputColumns.ToArray());

        await Task.CompletedTask;
    }

    private ArrayRow TransformRow(ArrayRow inputRow)
    {
        if (_inputSchema == null || _outputSchema == null)
            throw new InvalidOperationException("Schemas not initialized");

        // Copy all input values
        var outputValues = new object?[_outputSchema.ColumnCount];
        for (int i = 0; i < _inputSchema.ColumnCount; i++)
        {
            outputValues[i] = inputRow[i];
        }

        var nextIndex = _inputSchema.ColumnCount;

        // Add calculated fields
        if (_addFullName)
        {
            var firstName = (string?)inputRow["FirstName"] ?? "";
            var lastName = (string?)inputRow["LastName"] ?? "";
            outputValues[nextIndex++] = $"{firstName} {lastName}".Trim();
        }

        if (_addSalaryBand)
        {
            var salary = inputRow["Salary"] as decimal? ?? 0m;
            var salaryBand = salary switch
            {
                < 40000 => "Low",
                < 70000 => "Medium", 
                < 100000 => "High",
                _ => "Executive"
            };
            outputValues[nextIndex++] = salaryBand;
        }

        if (_addEmailDomain)
        {
            var email = (string?)inputRow["Email"] ?? "";
            var atIndex = email.IndexOf('@');
            var domain = atIndex >= 0 ? email.Substring(atIndex + 1) : "";
            outputValues[nextIndex++] = domain;
        }

        if (_addAgeCategory)
        {
            var createdAt = inputRow["CreatedAt"] as DateTime? ?? DateTime.UtcNow;
            var daysSince = (DateTime.UtcNow - createdAt).TotalDays;
            var ageCategory = daysSince switch
            {
                < 30 => "New",
                < 90 => "Recent",
                < 365 => "Established",
                _ => "Legacy"
            };
            outputValues[nextIndex++] = ageCategory;
        }

        return new ArrayRow(_outputSchema, outputValues);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        // Nothing to dispose
        await ValueTask.CompletedTask;
    }
}