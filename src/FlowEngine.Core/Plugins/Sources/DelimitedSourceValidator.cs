using FlowEngine.Abstractions.Data;
using FlowEngine.Abstractions.Plugins;
using FlowEngine.Core.Plugins.Base;
using Microsoft.Extensions.Logging;
using System.Collections.Immutable;

namespace FlowEngine.Core.Plugins.Sources;

/// <summary>
/// Validator for DelimitedSource plugin configuration and implementation.
/// Performs comprehensive validation including file accessibility, schema inference, and performance optimization checks.
/// </summary>
public sealed class DelimitedSourceValidator : PluginValidatorBase<DelimitedSourceConfiguration>
{
    /// <summary>
    /// Initializes a new instance of the DelimitedSourceValidator class.
    /// </summary>
    /// <param name="logger">Logger for validation operations</param>
    public DelimitedSourceValidator(ILogger<DelimitedSourceValidator> logger) : base(logger)
    {
    }

    /// <summary>
    /// Validates plugin-specific constraints for DelimitedSource.
    /// </summary>
    /// <param name="configuration">Configuration to validate</param>
    /// <param name="errors">Error collection</param>
    /// <param name="warnings">Warning collection</param>
    /// <param name="metrics">Validation metrics</param>
    /// <returns>Task representing the validation operation</returns>
    protected override async Task ValidatePluginSpecific(
        DelimitedSourceConfiguration configuration,
        List<ValidationError> errors,
        List<ValidationWarning> warnings,
        ValidationMetrics metrics)
    {
        Logger.LogDebug("Validating DelimitedSource-specific configuration for file: {FilePath}", configuration.FilePath);

        // Validate file path
        if (string.IsNullOrWhiteSpace(configuration.FilePath))
        {
            errors.Add(new ValidationError
            {
                Code = "MISSING_FILE_PATH",
                Message = "FilePath is required",
                Severity = ValidationSeverity.Error,
                PropertyPath = nameof(configuration.FilePath)
            });
        }
        else if (!File.Exists(configuration.FilePath))
        {
            errors.Add(new ValidationError
            {
                Code = "FILE_NOT_FOUND", 
                Message = $"File not found: {configuration.FilePath}",
                Severity = ValidationSeverity.Error,
                PropertyPath = nameof(configuration.FilePath)
            });
        }

        // Validate delimiter
        if (string.IsNullOrEmpty(configuration.Delimiter))
        {
            errors.Add(new ValidationError
            {
                Code = "EMPTY_DELIMITER",
                Message = "Delimiter cannot be empty",
                Severity = ValidationSeverity.Error,
                PropertyPath = nameof(configuration.Delimiter)
            });
        }

        // Validate batch size
        if (configuration.BatchSize <= 0)
        {
            errors.Add(new ValidationError
            {
                Code = "INVALID_BATCH_SIZE",
                Message = "BatchSize must be greater than 0",
                Severity = ValidationSeverity.Error,
                PropertyPath = nameof(configuration.BatchSize)
            });
        }
        else if (configuration.BatchSize > 100000)
        {
            warnings.Add(new ValidationWarning
            {
                Code = "LARGE_BATCH_SIZE",
                Message = "BatchSize should not exceed 100,000 for optimal memory usage",
                PropertyPath = nameof(configuration.BatchSize)
            });
        }

        // Validate encoding
        try
        {
            System.Text.Encoding.GetEncoding(configuration.Encoding);
        }
        catch (ArgumentException)
        {
            errors.Add(new ValidationError
            {
                Code = "INVALID_ENCODING",
                Message = $"Invalid encoding: {configuration.Encoding}",
                Severity = ValidationSeverity.Error,
                PropertyPath = nameof(configuration.Encoding)
            });
        }

        await Task.CompletedTask;
    }
}