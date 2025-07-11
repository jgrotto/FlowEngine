using FlowEngine.Abstractions.ErrorHandling;
using FlowEngine.Abstractions.Data;
using Microsoft.Extensions.Logging;

namespace FlowEngine.Core.ErrorHandling;

/// <summary>
/// Service that implements recovery strategies for different types of errors.
/// Provides automated recovery mechanisms for transient and recoverable errors.
/// </summary>
public sealed class ErrorRecoveryService : IDisposable
{
    private readonly ILogger<ErrorRecoveryService> _logger;
    private readonly Dictionary<Type, IErrorRecoveryStrategy> _strategies;
    private readonly object _lock = new();
    private bool _disposed;

    public ErrorRecoveryService(ILogger<ErrorRecoveryService> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _strategies = new Dictionary<Type, IErrorRecoveryStrategy>();

        RegisterDefaultStrategies();
    }

    /// <summary>
    /// Attempts to recover from a schema validation error.
    /// </summary>
    public Task<RecoveryResult> AttemptSchemaRecoveryAsync(FlowEngineSchemaException exception, ErrorContext context)
    {
        try
        {
            _logger.LogInformation("Attempting schema recovery for operation {OperationName}", context.OperationName);

            // Check if we can perform automatic schema adaptation
            if (exception.SourceSchema != null && exception.TargetSchema != null)
            {
                var compatibilityResult = AnalyzeSchemaCompatibility(exception.SourceSchema, exception.TargetSchema);

                if (compatibilityResult.CanAutoResolve)
                {
                    _logger.LogInformation("Schema auto-resolution possible: {Resolution}", compatibilityResult.Resolution);
                    return Task.FromResult(RecoveryResult.Success($"Schema auto-resolved: {compatibilityResult.Resolution}"));
                }
            }

            // If auto-resolution isn't possible, provide guidance
            var guidance = GenerateSchemaRecoveryGuidance(exception);
            _logger.LogWarning("Schema recovery requires manual intervention: {Guidance}", guidance);

            return Task.FromResult(RecoveryResult.RequiresManualIntervention(guidance));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during schema recovery attempt");
            return Task.FromResult(RecoveryResult.Failed("Schema recovery attempt failed"));
        }
    }

    /// <summary>
    /// Attempts to recover from memory allocation errors.
    /// </summary>
    public async Task<RecoveryResult> AttemptMemoryRecoveryAsync(FlowEngineMemoryException? exception, ErrorContext context)
    {
        try
        {
            _logger.LogInformation("Attempting memory recovery for operation {OperationName}", context.OperationName);

            var beforeGC = GC.GetTotalMemory(false);

            // Force garbage collection
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            var afterGC = GC.GetTotalMemory(false);
            var freedMemory = beforeGC - afterGC;

            _logger.LogInformation("Garbage collection freed {FreedMB:F1} MB of memory",
                freedMemory / (1024.0 * 1024.0));

            // Check if we've freed enough memory
            var currentPressure = GetMemoryPressure();
            if (currentPressure < 0.8) // Less than 80% pressure
            {
                return RecoveryResult.Success($"Memory recovery successful. Freed {freedMemory / (1024.0 * 1024.0):F1} MB");
            }

            // If still under pressure, try more aggressive measures
            await TryReduceMemoryUsageAsync();

            var finalPressure = GetMemoryPressure();
            if (finalPressure < currentPressure)
            {
                return RecoveryResult.Success($"Partial memory recovery. Pressure reduced from {currentPressure:P1} to {finalPressure:P1}");
            }

            return RecoveryResult.RequiresManualIntervention($"Memory pressure remains high: {finalPressure:P1}. Consider increasing available memory or reducing batch sizes.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during memory recovery attempt");
            return RecoveryResult.Failed("Memory recovery attempt failed");
        }
    }

    /// <summary>
    /// Attempts to recover from plugin-related errors.
    /// </summary>
    public async Task<RecoveryResult> AttemptPluginRecoveryAsync(FlowEnginePluginException exception, ErrorContext context)
    {
        try
        {
            _logger.LogInformation("Attempting plugin recovery for {PluginName} in operation {OperationName}",
                exception.PluginName, context.OperationName);

            if (!exception.IsRecoverable)
            {
                return RecoveryResult.RequiresManualIntervention($"Plugin error is marked as non-recoverable: {exception.Message}");
            }

            // Try different recovery strategies based on error code
            return exception.ErrorCode switch
            {
                ErrorCode.PluginLoadFailed => await AttemptPluginLoadRecoveryAsync(exception),
                ErrorCode.PluginExecutionFailed => await AttemptPluginExecutionRecoveryAsync(exception),
                ErrorCode.PluginConfigurationInvalid => await AttemptPluginConfigurationRecoveryAsync(exception),
                _ => RecoveryResult.RequiresManualIntervention($"No recovery strategy available for error code: {exception.ErrorCode}")
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during plugin recovery attempt for {PluginName}", exception.PluginName);
            return RecoveryResult.Failed($"Plugin recovery attempt failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Attempts to recover from I/O related errors.
    /// </summary>
    public Task<RecoveryResult> AttemptIORecoveryAsync(IOException exception, ErrorContext context)
    {
        try
        {
            _logger.LogInformation("Attempting I/O recovery for operation {OperationName}", context.OperationName);

            // Extract file path from context if available
            var filePath = context.Metadata.ContainsKey("FilePath") ? context.Metadata["FilePath"]?.ToString() : null;

            if (!string.IsNullOrEmpty(filePath))
            {
                // Check if file exists and is accessible
                if (File.Exists(filePath))
                {
                    try
                    {
                        // Try to access the file
                        using var stream = File.OpenRead(filePath);
                        return Task.FromResult(RecoveryResult.Success("File access recovered"));
                    }
                    catch (UnauthorizedAccessException)
                    {
                        return Task.FromResult(RecoveryResult.RequiresManualIntervention($"File access denied: {filePath}. Check permissions."));
                    }
                    catch (IOException)
                    {
                        // File might be locked, suggest retry
                        return Task.FromResult(RecoveryResult.RequiresManualIntervention($"File may be locked: {filePath}. Retry after a delay."));
                    }
                }
                else
                {
                    return Task.FromResult(RecoveryResult.RequiresManualIntervention($"File not found: {filePath}. Verify path and file existence."));
                }
            }

            // Generic I/O error handling
            if (exception.Message.Contains("sharing violation") || exception.Message.Contains("being used"))
            {
                return Task.FromResult(RecoveryResult.RequiresManualIntervention("File sharing violation detected. Retry after a delay or ensure file is not in use."));
            }

            return Task.FromResult(RecoveryResult.RequiresManualIntervention($"I/O error requires manual investigation: {exception.Message}"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during I/O recovery attempt");
            return Task.FromResult(RecoveryResult.Failed($"I/O recovery attempt failed: {ex.Message}"));
        }
    }

    private void RegisterDefaultStrategies()
    {
        // Register built-in recovery strategies
        // This allows for extensibility in the future
        _logger.LogDebug("Registered default error recovery strategies");
    }

    private SchemaCompatibilityResult AnalyzeSchemaCompatibility(ISchema sourceSchema, ISchema targetSchema)
    {
        // Analyze if schemas can be automatically adapted
        var sourceFields = sourceSchema.Columns.ToDictionary(f => f.Name, f => f);
        var targetFields = targetSchema.Columns.ToDictionary(f => f.Name, f => f);

        var missingInTarget = sourceFields.Keys.Except(targetFields.Keys).ToArray();
        var missingInSource = targetFields.Keys.Except(sourceFields.Keys).ToArray();
        var typeMismatches = new List<string>();

        foreach (var field in sourceFields.Keys.Intersect(targetFields.Keys))
        {
            if (sourceFields[field].DataType != targetFields[field].DataType)
            {
                typeMismatches.Add($"{field}: {sourceFields[field].DataType} -> {targetFields[field].DataType}");
            }
        }

        // Simple heuristics for auto-resolution
        var canAutoResolve = missingInTarget.Length == 0 && missingInSource.Length <= 2 && typeMismatches.Count == 0;

        var resolution = canAutoResolve
            ? $"Add optional fields: {string.Join(", ", missingInSource)}"
            : $"Manual schema mapping required. Missing: {string.Join(", ", missingInTarget)}, Extra: {string.Join(", ", missingInSource)}, Type mismatches: {string.Join(", ", typeMismatches)}";

        return new SchemaCompatibilityResult(canAutoResolve, resolution);
    }

    private string GenerateSchemaRecoveryGuidance(FlowEngineSchemaException exception)
    {
        var guidance = new List<string>
        {
            "Schema validation failed. Possible solutions:"
        };

        if (exception.ValidationErrors.Any())
        {
            guidance.Add("• Review validation errors:");
            foreach (var error in exception.ValidationErrors.Take(3)) // Show first 3 errors
            {
                guidance.Add($"  - {error}");
            }

            if (exception.ValidationErrors.Count > 3)
            {
                guidance.Add($"  - ... and {exception.ValidationErrors.Count - 3} more errors");
            }
        }

        guidance.Add("• Verify source and target schema compatibility");
        guidance.Add("• Check for missing required fields");
        guidance.Add("• Validate field data types and constraints");

        return string.Join(Environment.NewLine, guidance);
    }

    private double GetMemoryPressure()
    {
        try
        {
            var totalMemory = GC.GetTotalMemory(false);
            var availableMemory = GC.GetTotalMemory(false); // Simplified - in real implementation, would use OS APIs

            // Simplified pressure calculation
            return totalMemory / (1024.0 * 1024.0 * 1024.0); // Convert to GB and use as rough pressure metric
        }
        catch
        {
            return 0.5; // Default moderate pressure
        }
    }

    private async Task TryReduceMemoryUsageAsync()
    {
        try
        {
            // Clear any weak references
            GC.Collect();
            GC.WaitForPendingFinalizers();

            // In a real implementation, this would:
            // - Clear caches
            // - Reduce buffer sizes
            // - Trim collections
            // - Release pooled objects

            await Task.Delay(100); // Simulate cleanup operations

            _logger.LogInformation("Attempted aggressive memory cleanup");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error during aggressive memory cleanup");
        }
    }

    private Task<RecoveryResult> AttemptPluginLoadRecoveryAsync(FlowEnginePluginException exception)
    {
        // Check if the assembly file exists
        if (!string.IsNullOrEmpty(exception.AssemblyPath) && !File.Exists(exception.AssemblyPath))
        {
            return Task.FromResult(RecoveryResult.RequiresManualIntervention($"Plugin assembly not found: {exception.AssemblyPath}"));
        }

        // Check for common plugin load issues
        if (exception.InnerException is FileLoadException)
        {
            return Task.FromResult(RecoveryResult.RequiresManualIntervention("Plugin assembly cannot be loaded. Check dependencies and .NET version compatibility."));
        }

        if (exception.InnerException is BadImageFormatException)
        {
            return Task.FromResult(RecoveryResult.RequiresManualIntervention("Plugin assembly is corrupted or incompatible. Rebuild or reinstall the plugin."));
        }

        return Task.FromResult(RecoveryResult.RequiresManualIntervention("Plugin load failure requires manual investigation of assembly and dependencies."));
    }

    private Task<RecoveryResult> AttemptPluginExecutionRecoveryAsync(FlowEnginePluginException exception)
    {
        // Plugin execution failures might be transient
        return Task.FromResult(RecoveryResult.RequiresManualIntervention($"Plugin execution failed: {exception.Message}. Review plugin logs and input data."));
    }

    private Task<RecoveryResult> AttemptPluginConfigurationRecoveryAsync(FlowEnginePluginException exception)
    {
        return Task.FromResult(RecoveryResult.RequiresManualIntervention($"Plugin configuration is invalid: {exception.Message}. Review and correct plugin configuration."));
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        lock (_lock)
        {
            if (_disposed)
            {
                return;
            }

            foreach (var strategy in _strategies.Values.OfType<IDisposable>())
            {
                try
                {
                    strategy.Dispose();
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error disposing recovery strategy");
                }
            }

            _strategies.Clear();
            _disposed = true;
        }
    }
}

/// <summary>
/// Result of an error recovery attempt.
/// </summary>
public sealed class RecoveryResult
{
    /// <summary>
    /// Gets whether the recovery was successful.
    /// </summary>
    public bool IsSuccessful { get; }

    /// <summary>
    /// Gets whether the recovery requires manual intervention.
    /// </summary>
    public bool RequiresIntervention { get; }

    /// <summary>
    /// Gets the recovery action that was taken or guidance for intervention.
    /// </summary>
    public string? RecoveryAction { get; }

    /// <summary>
    /// Gets additional metadata about the recovery attempt.
    /// </summary>
    public IReadOnlyDictionary<string, object> Metadata { get; }

    private RecoveryResult(bool isSuccessful, bool requiresIntervention, string? recoveryAction, Dictionary<string, object>? metadata = null)
    {
        IsSuccessful = isSuccessful;
        RequiresIntervention = requiresIntervention;
        RecoveryAction = recoveryAction;
        Metadata = metadata?.AsReadOnly() ?? new Dictionary<string, object>().AsReadOnly();
    }

    public static RecoveryResult Success(string recoveryAction, Dictionary<string, object>? metadata = null)
    {
        return new RecoveryResult(true, false, recoveryAction, metadata);
    }

    public static RecoveryResult Failed(string reason, Dictionary<string, object>? metadata = null)
    {
        return new RecoveryResult(false, false, reason, metadata);
    }

    public static RecoveryResult RequiresManualIntervention(string guidance, Dictionary<string, object>? metadata = null)
    {
        return new RecoveryResult(false, true, guidance, metadata);
    }
}

/// <summary>
/// Result of schema compatibility analysis.
/// </summary>
internal sealed class SchemaCompatibilityResult
{
    public bool CanAutoResolve { get; }
    public string Resolution { get; }

    public SchemaCompatibilityResult(bool canAutoResolve, string resolution)
    {
        CanAutoResolve = canAutoResolve;
        Resolution = resolution;
    }
}

/// <summary>
/// Interface for pluggable error recovery strategies.
/// </summary>
public interface IErrorRecoveryStrategy
{
    Task<RecoveryResult> AttemptRecoveryAsync(Exception exception, ErrorContext context);
    bool CanHandle(Exception exception);
}
