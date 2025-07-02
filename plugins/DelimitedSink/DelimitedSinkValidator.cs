using FlowEngine.Abstractions.Data;
using FlowEngine.Abstractions.Plugins;
using FlowEngine.Abstractions.Schema;
using Microsoft.Extensions.Logging;
using System.Collections.Immutable;
using System.Text;

namespace DelimitedSink;

/// <summary>
/// Validator for DelimitedSink plugin configuration and implementation.
/// Performs comprehensive validation including file paths, schema compatibility, and performance optimization checks.
/// </summary>
public sealed class DelimitedSinkValidator : IPluginValidator<DelimitedSinkConfiguration>
{
    private readonly ILogger<DelimitedSinkValidator> _logger;
    private readonly IJsonSchemaValidator _jsonSchemaValidator;

    /// <summary>
    /// Initializes a new instance of the DelimitedSinkValidator class.
    /// </summary>
    /// <param name="logger">Logger for validation operations</param>
    /// <param name="jsonSchemaValidator">JSON schema validator for configuration validation</param>
    public DelimitedSinkValidator(
        ILogger<DelimitedSinkValidator> logger,
        IJsonSchemaValidator? jsonSchemaValidator = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _jsonSchemaValidator = jsonSchemaValidator ?? new FlowEngine.Core.Schema.JsonSchemaValidator(
            logger as ILogger<FlowEngine.Core.Schema.JsonSchemaValidator> ?? 
            throw new InvalidOperationException("Logger not available for JSON schema validator"));
    }

    /// <summary>
    /// Validates the DelimitedSink configuration asynchronously.
    /// </summary>
    /// <param name="configuration">Configuration to validate</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Validation result with detailed error information</returns>
    public async Task<ValidationResult> ValidateAsync(DelimitedSinkConfiguration configuration, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Validating DelimitedSink configuration");

        var errors = new List<ValidationError>();
        var warnings = new List<ValidationWarning>();
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            // First perform JSON schema validation based on attributes
            await ValidateJsonSchemaAsync(configuration, errors, warnings, cancellationToken);

            // Validate file path configuration
            ValidateFilePathConfiguration(configuration, errors, warnings);

            // Validate format configuration
            ValidateFormatConfiguration(configuration, errors, warnings);

            // Validate performance configuration
            ValidatePerformanceConfiguration(configuration, errors, warnings);

            // Validate schema configuration
            ValidateSchemaConfiguration(configuration, errors, warnings);

            // Validate write permissions
            await ValidateWritePermissionsAsync(configuration, errors, warnings, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error during DelimitedSink validation");
            errors.Add(new ValidationError
            {
                Code = "VALIDATION_EXCEPTION",
                Message = $"Validation failed with exception: {ex.Message}",
                Severity = ErrorSeverity.Critical
            });
        }
        finally
        {
            stopwatch.Stop();
        }

        return new ValidationResult
        {
            IsValid = errors.Count == 0,
            Errors = errors.ToImmutableArray(),
            Warnings = warnings.ToImmutableArray(),
            ValidationTime = stopwatch.Elapsed
        };
    }

    /// <summary>
    /// Validates the DelimitedSink configuration synchronously.
    /// </summary>
    /// <param name="configuration">Configuration to validate</param>
    /// <returns>Validation result</returns>
    public ValidationResult Validate(DelimitedSinkConfiguration configuration)
    {
        return ValidateAsync(configuration).GetAwaiter().GetResult();
    }

    /// <summary>
    /// Validates schema compatibility between configurations.
    /// </summary>
    /// <param name="sourceConfiguration">Source configuration</param>
    /// <param name="targetSchema">Target schema</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Schema compatibility result</returns>
    public async Task<SchemaCompatibilityResult> ValidateCompatibilityAsync(
        DelimitedSinkConfiguration sourceConfiguration, 
        ISchema targetSchema, 
        CancellationToken cancellationToken = default)
    {
        var errors = new List<ValidationError>();
        var warnings = new List<ValidationWarning>();
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            if (sourceConfiguration.InputSchema == null)
            {
                warnings.Add(new ValidationWarning
                {
                    Code = "NO_INPUT_SCHEMA",
                    Message = "Sink configuration has no input schema defined - compatibility cannot be verified"
                });
            }
            else
            {
                // Validate that target schema contains all required fields from sink input schema
                foreach (var sinkColumn in sourceConfiguration.InputSchema.Columns)
                {
                    var targetColumn = targetSchema.Columns
                        .FirstOrDefault(c => c.Name.Equals(sinkColumn.Name, StringComparison.OrdinalIgnoreCase));
                    
                    if (targetColumn == null)
                    {
                        if (!sinkColumn.IsNullable)
                        {
                            errors.Add(new ValidationError
                            {
                                Code = "MISSING_REQUIRED_FIELD",
                                Message = $"Required field '{sinkColumn.Name}' from sink input schema not found in target schema",
                                Severity = ErrorSeverity.Error
                            });
                        }
                        else
                        {
                            warnings.Add(new ValidationWarning
                            {
                                Code = "MISSING_OPTIONAL_FIELD",
                                Message = $"Optional field '{sinkColumn.Name}' from sink input schema not found in target schema"
                            });
                        }
                        continue;
                    }

                    // Check type compatibility
                    if (!AreTypesCompatible(targetColumn.DataType, sinkColumn.DataType))
                    {
                        errors.Add(new ValidationError
                        {
                            Code = "TYPE_MISMATCH",
                            Message = $"Field '{sinkColumn.Name}' type mismatch: target provides {targetColumn.DataType.Name}, sink expects {sinkColumn.DataType.Name}",
                            Severity = ErrorSeverity.Error
                        });
                    }
                }
            }
        }
        finally
        {
            stopwatch.Stop();
        }

        await Task.CompletedTask;

        return new SchemaCompatibilityResult
        {
            IsCompatible = errors.Count == 0,
            Errors = errors.ToImmutableArray(),
            Warnings = warnings.ToImmutableArray(),
            ValidationTime = stopwatch.Elapsed
        };
    }

    /// <summary>
    /// Validates ArrayRow optimization for the configuration.
    /// </summary>
    /// <param name="configuration">Configuration to validate</param>
    /// <returns>ArrayRow optimization result</returns>
    public ArrayRowOptimizationResult ValidateArrayRowOptimization(DelimitedSinkConfiguration configuration)
    {
        var errors = new List<ValidationError>();
        var warnings = new List<ValidationWarning>();
        var optimizations = new List<string>();

        // Validate input schema optimization
        if (configuration.InputSchema != null)
        {
            var inputColumns = configuration.InputSchema.Columns.ToList();
            for (int i = 0; i < inputColumns.Count; i++)
            {
                optimizations.Add($"Input column '{inputColumns[i].Name}' at index {i} - optimized for ArrayRow access");
            }

            if (inputColumns.Count > 50)
            {
                warnings.Add(new ValidationWarning
                {
                    Code = "MANY_INPUT_COLUMNS",
                    Message = $"Input schema has {inputColumns.Count} columns. Consider field reduction for optimal writing performance."
                });
            }
        }

        // Delimited writing optimizations
        if (configuration.BatchSize >= 1000)
        {
            optimizations.Add($"Batch size {configuration.BatchSize} optimized for I/O efficiency");
        }

        if (configuration.BufferSize >= 8192)
        {
            optimizations.Add($"Buffer size {configuration.BufferSize} bytes optimized for file I/O");
        }

        if (configuration.EnableParallelProcessing && configuration.MaxDegreeOfParallelism <= 4)
        {
            optimizations.Add($"Parallel processing with {configuration.MaxDegreeOfParallelism} threads - good for file I/O");
        }

        if (!configuration.FlushAfterBatch)
        {
            optimizations.Add("Buffered writing enabled - better I/O performance");
        }

        return new ArrayRowOptimizationResult
        {
            IsOptimized = errors.Count == 0,
            Errors = errors.ToImmutableArray(),
            Warnings = warnings.ToImmutableArray(),
            Optimizations = optimizations.ToImmutableArray(),
            ValidationTime = TimeSpan.FromMilliseconds(1)
        };
    }

    /// <summary>
    /// Validates plugin-specific business rules.
    /// </summary>
    /// <param name="configuration">Configuration to validate</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Plugin-specific validation result</returns>
    public async Task<ValidationResult> ValidatePluginSpecificAsync(DelimitedSinkConfiguration configuration, CancellationToken cancellationToken = default)
    {
        var errors = new List<ValidationError>();
        var warnings = new List<ValidationWarning>();

        // Validate file rotation settings
        if (configuration.MaxFileSizeBytes > 0 && configuration.MaxFileSizeBytes < 1024 * 1024) // 1MB
        {
            warnings.Add(new ValidationWarning
            {
                Code = "SMALL_FILE_ROTATION_SIZE",
                Message = "File rotation size is very small (< 1MB). This may create many small files."
            });
        }

        // Validate compression settings
        if (configuration.EnableCompression)
        {
            if (configuration.CompressionLevel < 1 || configuration.CompressionLevel > 9)
            {
                errors.Add(new ValidationError
                {
                    Code = "INVALID_COMPRESSION_LEVEL",
                    Message = "Compression level must be between 1 and 9",
                    PropertyPath = nameof(configuration.CompressionLevel),
                    Severity = ErrorSeverity.Error
                });
            }

            if (configuration.CompressionLevel > 7)
            {
                warnings.Add(new ValidationWarning
                {
                    Code = "HIGH_COMPRESSION_LEVEL",
                    Message = "High compression level may significantly impact write performance."
                });
            }
        }

        // Validate encoding
        try
        {
            Encoding.GetEncoding(configuration.Encoding);
        }
        catch (ArgumentException)
        {
            errors.Add(new ValidationError
            {
                Code = "INVALID_ENCODING",
                Message = $"Encoding '{configuration.Encoding}' is not supported",
                PropertyPath = nameof(configuration.Encoding),
                Severity = ErrorSeverity.Error
            });
        }

        await Task.CompletedTask;

        return new ValidationResult
        {
            IsValid = errors.Count == 0,
            Errors = errors.ToImmutableArray(),
            Warnings = warnings.ToImmutableArray(),
            ValidationTime = TimeSpan.FromMilliseconds(1)
        };
    }

    /// <summary>
    /// Validates the configuration using JSON schema validation based on attributes.
    /// </summary>
    private async Task ValidateJsonSchemaAsync(
        DelimitedSinkConfiguration configuration,
        List<ValidationError> errors,
        List<ValidationWarning> warnings,
        CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogDebug("Performing JSON schema validation for DelimitedSink configuration");

            // Generate schema from the configuration type using attributes
            var schemaDefinition = _jsonSchemaValidator.CreateSchemaFromType<DelimitedSinkConfiguration>();

            // Validate the configuration object against the generated schema
            var schemaResult = await _jsonSchemaValidator.ValidateObjectAsync(
                configuration, 
                schemaDefinition, 
                cancellationToken: cancellationToken);

            if (!schemaResult.IsValid)
            {
                _logger.LogWarning("JSON schema validation found {ErrorCount} errors", schemaResult.Errors.Length);

                // Convert JSON schema errors to validation errors
                foreach (var schemaError in schemaResult.Errors)
                {
                    errors.Add(new ValidationError
                    {
                        Code = $"SCHEMA_{schemaError.Code}",
                        Message = schemaError.Message,
                        PropertyPath = schemaError.Path.Replace("$.", "").Replace("$", ""),
                        Severity = GetErrorSeverityFromSchemaError(schemaError)
                    });
                }

                // Convert JSON schema warnings to validation warnings
                foreach (var schemaWarning in schemaResult.Warnings)
                {
                    warnings.Add(new ValidationWarning
                    {
                        Code = $"SCHEMA_{schemaWarning.Code}",
                        Message = schemaWarning.Message,
                        PropertyPath = schemaWarning.Path.Replace("$.", "").Replace("$", "")
                    });
                }
            }

            _logger.LogDebug("JSON schema validation completed with {ErrorCount} errors and {WarningCount} warnings",
                errors.Count, warnings.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "JSON schema validation failed");
            warnings.Add(new ValidationWarning
            {
                Code = "JSON_SCHEMA_VALIDATION_FAILED",
                Message = $"JSON schema validation could not be completed: {ex.Message}"
            });
        }
    }

    /// <summary>
    /// Validates the file path configuration aspects.
    /// </summary>
    private void ValidateFilePathConfiguration(DelimitedSinkConfiguration configuration, List<ValidationError> errors, List<ValidationWarning> warnings)
    {
        // Validate output file path
        if (string.IsNullOrWhiteSpace(configuration.FilePath))
        {
            errors.Add(new ValidationError
            {
                Code = "EMPTY_FILE_PATH",
                Message = "FilePath cannot be empty",
                PropertyPath = nameof(configuration.FilePath),
                Severity = ErrorSeverity.Error
            });
            return;
        }

        try
        {
            var directoryPath = Path.GetDirectoryName(configuration.FilePath);
            if (!string.IsNullOrEmpty(directoryPath) && !Directory.Exists(directoryPath))
            {
                warnings.Add(new ValidationWarning
                {
                    Code = "OUTPUT_DIRECTORY_NOT_EXISTS",
                    Message = $"Output directory does not exist: {directoryPath}. It will be created during execution.",
                    PropertyPath = nameof(configuration.FilePath)
                });
            }

            // Check if file already exists and append mode
            if (File.Exists(configuration.FilePath) && !configuration.AppendToFile)
            {
                warnings.Add(new ValidationWarning
                {
                    Code = "FILE_WILL_BE_OVERWRITTEN",
                    Message = $"File '{configuration.FilePath}' already exists and will be overwritten",
                    PropertyPath = nameof(configuration.FilePath)
                });
            }
        }
        catch (Exception ex)
        {
            errors.Add(new ValidationError
            {
                Code = "INVALID_FILE_PATH",
                Message = $"Invalid file path: {ex.Message}",
                PropertyPath = nameof(configuration.FilePath),
                Severity = ErrorSeverity.Error
            });
        }
    }

    /// <summary>
    /// Validates the format configuration aspects.
    /// </summary>
    private void ValidateFormatConfiguration(DelimitedSinkConfiguration configuration, List<ValidationError> errors, List<ValidationWarning> warnings)
    {
        // Validate delimiter
        if (string.IsNullOrEmpty(configuration.Delimiter))
        {
            errors.Add(new ValidationError
            {
                Code = "EMPTY_DELIMITER",
                Message = "Delimiter cannot be empty",
                PropertyPath = nameof(configuration.Delimiter),
                Severity = ErrorSeverity.Error
            });
        }
        else if (configuration.Delimiter.Length > 5)
        {
            warnings.Add(new ValidationWarning
            {
                Code = "LONG_DELIMITER",
                Message = "Delimiter is unusually long. This may impact performance or compatibility.",
                PropertyPath = nameof(configuration.Delimiter)
            });
        }

        // Validate quote and escape characters
        if (configuration.QuoteCharacter == configuration.EscapeCharacter && configuration.QuoteCharacter.Length > 1)
        {
            warnings.Add(new ValidationWarning
            {
                Code = "SAME_QUOTE_ESCAPE_CHARS",
                Message = "Quote and escape characters are the same and multi-character. This may cause parsing issues."
            });
        }

        // Validate line ending
        var validLineEndings = new[] { "CRLF", "LF", "CR" };
        if (!validLineEndings.Contains(configuration.LineEnding.ToUpperInvariant()))
        {
            errors.Add(new ValidationError
            {
                Code = "INVALID_LINE_ENDING",
                Message = $"Line ending '{configuration.LineEnding}' is not valid. Must be one of: {string.Join(", ", validLineEndings)}",
                PropertyPath = nameof(configuration.LineEnding),
                Severity = ErrorSeverity.Error
            });
        }
    }

    /// <summary>
    /// Validates the performance configuration aspects.
    /// </summary>
    private void ValidatePerformanceConfiguration(DelimitedSinkConfiguration configuration, List<ValidationError> errors, List<ValidationWarning> warnings)
    {
        // Validate batch size
        if (configuration.BatchSize <= 0)
        {
            errors.Add(new ValidationError
            {
                Code = "INVALID_BATCH_SIZE",
                Message = "BatchSize must be greater than 0",
                PropertyPath = nameof(configuration.BatchSize),
                Severity = ErrorSeverity.Error
            });
        }
        else if (configuration.BatchSize < 100)
        {
            warnings.Add(new ValidationWarning
            {
                Code = "SMALL_BATCH_SIZE",
                Message = "Small batch size may reduce I/O efficiency. Consider increasing for better performance."
            });
        }

        // Validate buffer size
        if (configuration.BufferSize <= 0)
        {
            errors.Add(new ValidationError
            {
                Code = "INVALID_BUFFER_SIZE",
                Message = "BufferSize must be greater than 0",
                PropertyPath = nameof(configuration.BufferSize),
                Severity = ErrorSeverity.Error
            });
        }
        else if (configuration.BufferSize < 1024)
        {
            warnings.Add(new ValidationWarning
            {
                Code = "SMALL_BUFFER_SIZE",
                Message = "Small buffer size may reduce I/O performance."
            });
        }

        // Validate parallelism settings
        if (configuration.MaxDegreeOfParallelism <= 0)
        {
            errors.Add(new ValidationError
            {
                Code = "INVALID_PARALLELISM",
                Message = "MaxDegreeOfParallelism must be greater than 0",
                PropertyPath = nameof(configuration.MaxDegreeOfParallelism),
                Severity = ErrorSeverity.Error
            });
        }
        else if (configuration.MaxDegreeOfParallelism > 8)
        {
            warnings.Add(new ValidationWarning
            {
                Code = "HIGH_PARALLELISM",
                Message = "High parallelism may not improve file I/O performance and could cause contention."
            });
        }

        // Validate error threshold
        if (configuration.MaxErrorThreshold < 0)
        {
            errors.Add(new ValidationError
            {
                Code = "INVALID_ERROR_THRESHOLD",
                Message = "MaxErrorThreshold cannot be negative",
                PropertyPath = nameof(configuration.MaxErrorThreshold),
                Severity = ErrorSeverity.Error
            });
        }
    }

    /// <summary>
    /// Validates the schema configuration aspects.
    /// </summary>
    private void ValidateSchemaConfiguration(DelimitedSinkConfiguration configuration, List<ValidationError> errors, List<ValidationWarning> warnings)
    {
        // Validate that input schema is defined for sink plugins
        if (configuration.InputSchema == null)
        {
            warnings.Add(new ValidationWarning
            {
                Code = "NO_INPUT_SCHEMA",
                Message = "Sink plugins should define an input schema for better type safety",
                PropertyPath = nameof(configuration.InputSchema)
            });
        }
        else
        {
            if (configuration.InputSchema.ColumnCount == 0)
            {
                warnings.Add(new ValidationWarning
                {
                    Code = "EMPTY_INPUT_SCHEMA",
                    Message = "Input schema has no columns defined"
                });
            }
        }
    }

    /// <summary>
    /// Validates write permissions and disk space.
    /// </summary>
    private async Task ValidateWritePermissionsAsync(DelimitedSinkConfiguration configuration, List<ValidationError> errors, List<ValidationWarning> warnings, CancellationToken cancellationToken)
    {
        try
        {
            var directoryPath = Path.GetDirectoryName(configuration.FilePath);
            if (string.IsNullOrEmpty(directoryPath))
                directoryPath = Directory.GetCurrentDirectory();

            // Check if directory exists and is writable
            if (Directory.Exists(directoryPath))
            {
                try
                {
                    var testFile = Path.Combine(directoryPath, $"flowengine_write_test_{Guid.NewGuid():N}.tmp");
                    await File.WriteAllTextAsync(testFile, "test", cancellationToken);
                    File.Delete(testFile);
                }
                catch (UnauthorizedAccessException)
                {
                    errors.Add(new ValidationError
                    {
                        Code = "NO_WRITE_PERMISSION",
                        Message = $"No write permission for directory: {directoryPath}",
                        PropertyPath = nameof(configuration.FilePath),
                        Severity = ErrorSeverity.Error
                    });
                }
                catch (Exception ex)
                {
                    warnings.Add(new ValidationWarning
                    {
                        Code = "WRITE_PERMISSION_CHECK_FAILED",
                        Message = $"Could not verify write permissions: {ex.Message}"
                    });
                }

                // Check available disk space
                try
                {
                    var drive = new DriveInfo(directoryPath);
                    if (drive.AvailableFreeSpace < 100 * 1024 * 1024) // 100MB
                    {
                        warnings.Add(new ValidationWarning
                        {
                            Code = "LOW_DISK_SPACE",
                            Message = $"Low disk space available: {drive.AvailableFreeSpace / (1024 * 1024)}MB"
                        });
                    }
                }
                catch (Exception ex)
                {
                    warnings.Add(new ValidationWarning
                    {
                        Code = "DISK_SPACE_CHECK_FAILED",
                        Message = $"Could not check disk space: {ex.Message}"
                    });
                }
            }
        }
        catch (Exception ex)
        {
            warnings.Add(new ValidationWarning
            {
                Code = "PERMISSION_VALIDATION_FAILED",
                Message = $"Could not validate write permissions: {ex.Message}"
            });
        }
    }

    /// <summary>
    /// Checks if two data types are compatible for writing.
    /// </summary>
    private static bool AreTypesCompatible(Type sourceType, Type targetType)
    {
        if (sourceType == targetType) return true;

        // All types can be written as strings in delimited format
        if (targetType == typeof(string)) return true;

        // Object can contain anything
        if (sourceType == typeof(object)) return true;

        // Numeric type compatibility
        if (IsNumericType(sourceType) && IsNumericType(targetType))
        {
            return true; // Numeric types can be converted
        }

        // Nullable compatibility
        var underlyingSourceType = Nullable.GetUnderlyingType(sourceType);
        var underlyingTargetType = Nullable.GetUnderlyingType(targetType);
        
        if (underlyingSourceType != null || underlyingTargetType != null)
        {
            return AreTypesCompatible(
                underlyingSourceType ?? sourceType,
                underlyingTargetType ?? targetType
            );
        }

        return false;
    }

    /// <summary>
    /// Checks if a type is numeric.
    /// </summary>
    private static bool IsNumericType(Type type)
    {
        return type == typeof(byte) || type == typeof(sbyte) ||
               type == typeof(short) || type == typeof(ushort) ||
               type == typeof(int) || type == typeof(uint) ||
               type == typeof(long) || type == typeof(ulong) ||
               type == typeof(float) || type == typeof(double) ||
               type == typeof(decimal);
    }

    /// <summary>
    /// Converts JSON schema error severity to ValidationError severity.
    /// </summary>
    private static ErrorSeverity GetErrorSeverityFromSchemaError(JsonSchemaValidationError schemaError)
    {
        return schemaError.Code switch
        {
            "TYPE_MISMATCH" => ErrorSeverity.Error,
            "MISSING_REQUIRED_PROPERTY" => ErrorSeverity.Error,
            "ENUM_VIOLATION" => ErrorSeverity.Error,
            "PATTERN_MISMATCH" => ErrorSeverity.Error,
            "STRING_TOO_SHORT" => ErrorSeverity.Error,
            "STRING_TOO_LONG" => ErrorSeverity.Warning,
            "NUMBER_TOO_SMALL" => ErrorSeverity.Error,
            "NUMBER_TOO_LARGE" => ErrorSeverity.Warning,
            _ => ErrorSeverity.Warning
        };
    }
}