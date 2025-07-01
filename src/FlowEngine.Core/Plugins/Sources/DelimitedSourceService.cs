using FlowEngine.Abstractions.Data;
using FlowEngine.Abstractions.Plugins;
using FlowEngine.Core.Plugins.Base;
using Microsoft.Extensions.Logging;
using System.Collections.Immutable;
using System.Diagnostics;

namespace FlowEngine.Core.Plugins.Sources;

/// <summary>
/// Core business logic service for DelimitedSource plugin.
/// Implements the Service component of the five-component architecture pattern with ArrayRow optimization.
/// </summary>
public sealed class DelimitedSourceService : PluginServiceBase<DelimitedSourceConfiguration>, IPluginService
{
    private readonly ILogger<DelimitedSourceService> _logger;

    /// <summary>
    /// Initializes a new instance of the DelimitedSourceService class.
    /// </summary>
    /// <param name="logger">Logger for service operations</param>
    public DelimitedSourceService(ILogger<DelimitedSourceService> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Processes a chunk of data with ArrayRow optimization for O(1) field access.
    /// </summary>
    /// <param name="chunk">Input data chunk</param>
    /// <param name="cancellationToken">Cancellation token for cooperative cancellation</param>
    /// <returns>Processed data chunk with performance metrics</returns>
    public async Task<IChunk> ProcessChunkAsync(IChunk chunk, CancellationToken cancellationToken = default)
    {
        if (chunk == null) return chunk;

        _logger.LogDebug("Processing chunk with {RowCount} rows using ArrayRow optimization", chunk.RowCount);

        var stopwatch = Stopwatch.StartNew();

        try
        {
            // For source plugins, we typically don't transform the data extensively
            // but we ensure ArrayRow optimization and validate data integrity
            var processedRows = new List<IArrayRow>();

            foreach (var row in chunk.GetRows())
            {
                cancellationToken.ThrowIfCancellationRequested();

                // Process individual row with ArrayRow optimization
                var processedRow = await ProcessRowWithArrayRowOptimizationAsync(row, cancellationToken);
                
                if (processedRow != null)
                {
                    processedRows.Add(processedRow);
                }
            }

            stopwatch.Stop();

            // Calculate performance metrics
            var rowsPerSecond = chunk.RowCount / stopwatch.Elapsed.TotalSeconds;
            var avgProcessingTimePerRowNs = stopwatch.Elapsed.TotalNanoseconds / chunk.RowCount;

            _logger.LogDebug("Chunk processed: {ProcessedRows}/{InputRows} rows in {ElapsedMs:F2}ms. " +
                "Throughput: {RowsPerSecond:N0} rows/sec, Avg per row: {AvgNs:F1}ns",
                processedRows.Count, chunk.RowCount, stopwatch.ElapsedMilliseconds,
                rowsPerSecond, avgProcessingTimePerRowNs);

            // Validate performance target (<15ns per field access)
            var estimatedFieldAccessCount = chunk.RowCount * chunk.Schema.ColumnCount;
            var avgFieldAccessTimeNs = stopwatch.Elapsed.TotalNanoseconds / estimatedFieldAccessCount;
            
            if (avgFieldAccessTimeNs > 15.0)
            {
                _logger.LogWarning("Field access time above target: {AvgFieldAccessNs:F1}ns (target: <15ns)", avgFieldAccessTimeNs);
            }

            // Create processed chunk
            var metadata = new Dictionary<string, object>
            {
                ["ProcessingTimeMs"] = stopwatch.ElapsedMilliseconds,
                ["ThroughputRowsPerSec"] = rowsPerSecond,
                ["AvgRowProcessingTimeNs"] = avgProcessingTimePerRowNs,
                ["AvgFieldAccessTimeNs"] = avgFieldAccessTimeNs,
                ["ProcessedTimestamp"] = DateTimeOffset.UtcNow,
                ["ProcessorType"] = "DelimitedSourceService"
            };

            return chunk.WithRows(processedRows).WithMetadata(metadata);
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug("Chunk processing was cancelled");
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing chunk");
            throw;
        }
    }

    /// <summary>
    /// Processes a single row with ArrayRow optimization demonstrating O(1) field access.
    /// </summary>
    /// <param name="row">Input row</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Processed row</returns>
    private async Task<IArrayRow?> ProcessRowWithArrayRowOptimizationAsync(
        IArrayRow row, 
        CancellationToken cancellationToken = default)
    {
        if (row == null) return null;

        try
        {
            // Demonstrate ArrayRow optimization with direct index access
            // This achieves O(1) field access instead of O(n) string-based lookups
            
            // Example: Access fields using direct index access
            var values = new object[row.Schema.ColumnCount];
            
            // Copy all values using direct index access (O(1) operation)
            for (int i = 0; i < row.Schema.ColumnCount; i++)
            {
                // Direct array access - this is the key to >200K rows/sec performance
                values[i] = row[i]; // O(1) access vs row.GetValue("fieldName") which is O(n)
            }

            // Example of field-specific processing using indexes
            // Only if we have known fields in the schema
            if (row.Schema.ColumnCount > 0)
            {
                // Validate first field (typically an ID or key field)
                var firstFieldValue = values[0];
                if (firstFieldValue == null || (firstFieldValue is string str && string.IsNullOrWhiteSpace(str)))
                {
                    _logger.LogWarning("First field is null or empty - potential data quality issue");
                }
            }

            // Create new ArrayRow with the same schema and values
            // For source plugins, we typically don't transform the data structure
            return new FlowEngine.Core.Data.ArrayRow(values, row.Schema);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error processing individual row");
            return null; // Return null to skip problematic rows
        }
    }

    /// <summary>
    /// Disposes the service and releases resources.
    /// </summary>
    /// <param name="disposing">Whether disposing managed resources</param>
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _logger.LogDebug("Disposing DelimitedSourceService");
        }
        
        base.Dispose(disposing);
    }
}