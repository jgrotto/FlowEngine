using FlowEngine.Abstractions.Data;
using FlowEngine.Abstractions.Plugins;
using FlowEngine.Abstractions.Schema;
using Microsoft.Extensions.Logging;
using System.Collections.Immutable;
using System.Text.RegularExpressions;

namespace JavaScriptTransform;

/// <summary>
/// Validator for JavaScriptTransform plugin configuration and implementation.
/// Performs comprehensive validation including script syntax, security, schema compatibility, and performance optimization checks.
/// </summary>
public sealed class JavaScriptTransformValidator : IPluginValidator<JavaScriptTransformConfiguration>
{
    private readonly ILogger<JavaScriptTransformValidator> _logger;
    private readonly IJsonSchemaValidator _jsonSchemaValidator;

    // Security patterns to detect potentially dangerous JavaScript
    private static readonly Regex[] SecurityPatterns = new[]
    {
        new Regex(@"\b(eval|Function|setTimeout|setInterval)\s*\(", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new Regex(@"\b(require|import|module|exports)\s*[\(\.]", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new Regex(@"\b(process|global|window|document)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new Regex(@"__\w+__", RegexOptions.Compiled), // Dunder methods
        new Regex(@"\bdelete\s+\w+", RegexOptions.IgnoreCase | RegexOptions.Compiled)
    };

    // Performance anti-patterns that could cause issues
    private static readonly Regex[] PerformancePatterns = new[]
    {
        new Regex(@"while\s*\(\s*true\s*\)", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new Regex(@"for\s*\(\s*;\s*;\s*\)", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new Regex(@"\bsetTimeout\s*\(.*,\s*0\s*\)", RegexOptions.IgnoreCase | RegexOptions.Compiled)
    };

    /// <summary>
    /// Initializes a new instance of the JavaScriptTransformValidator class.
    /// </summary>
    /// <param name="logger">Logger for validation operations</param>
    /// <param name="jsonSchemaValidator">JSON schema validator for configuration validation</param>
    public JavaScriptTransformValidator(
        ILogger<JavaScriptTransformValidator> logger,
        IJsonSchemaValidator? jsonSchemaValidator = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _jsonSchemaValidator = jsonSchemaValidator ?? new FlowEngine.Core.Schema.JsonSchemaValidator(
            logger as ILogger<FlowEngine.Core.Schema.JsonSchemaValidator> ?? 
            throw new InvalidOperationException("Logger not available for JSON schema validator"));
    }

    /// <summary>
    /// Validates the JavaScriptTransform configuration asynchronously.
    /// </summary>
    /// <param name="configuration">Configuration to validate</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Validation result with detailed error information</returns>
    public async Task<ValidationResult> ValidateAsync(JavaScriptTransformConfiguration configuration, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Validating JavaScriptTransform configuration");

        var errors = new List<ValidationError>();
        var warnings = new List<ValidationWarning>();
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            // First perform JSON schema validation based on attributes
            await ValidateJsonSchemaAsync(configuration, errors, warnings, cancellationToken);

            // Validate script configuration
            ValidateScriptConfiguration(configuration, errors, warnings);

            // Validate schema configuration
            ValidateSchemaConfiguration(configuration, errors, warnings);

            // Validate performance configuration
            ValidatePerformanceConfiguration(configuration, errors, warnings);

            // Validate security aspects
            await ValidateScriptSecurityAsync(configuration, errors, warnings, cancellationToken);

            // Validate script syntax (if script is available)
            await ValidateScriptSyntaxAsync(configuration, errors, warnings, cancellationToken);

        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error during JavaScriptTransform validation");
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
    /// Validates the JavaScriptTransform configuration synchronously.
    /// </summary>
    /// <param name="configuration">Configuration to validate</param>
    /// <returns>Validation result</returns>
    public ValidationResult Validate(JavaScriptTransformConfiguration configuration)
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
        JavaScriptTransformConfiguration sourceConfiguration, 
        ISchema targetSchema, 
        CancellationToken cancellationToken = default)
    {
        var errors = new List<ValidationError>();
        var warnings = new List<ValidationWarning>();
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            if (sourceConfiguration.OutputSchema == null)
            {
                warnings.Add(new ValidationWarning
                {
                    Code = "NO_OUTPUT_SCHEMA",
                    Message = "Transform configuration has no output schema defined - compatibility cannot be verified"
                });
            }
            else
            {
                // Validate field compatibility
                foreach (var targetColumn in targetSchema.Columns)
                {
                    var sourceColumn = sourceConfiguration.OutputSchema.Columns
                        .FirstOrDefault(c => c.Name.Equals(targetColumn.Name, StringComparison.OrdinalIgnoreCase));
                    
                    if (sourceColumn == null)
                    {
                        if (!targetColumn.IsNullable)
                        {
                            errors.Add(new ValidationError
                            {
                                Code = "MISSING_REQUIRED_FIELD",
                                Message = $"Required field '{targetColumn.Name}' not found in transform output schema",
                                Severity = ErrorSeverity.Error
                            });
                        }
                        else
                        {
                            warnings.Add(new ValidationWarning
                            {
                                Code = "MISSING_OPTIONAL_FIELD",
                                Message = $"Optional field '{targetColumn.Name}' not found in transform output schema"
                            });
                        }
                        continue;
                    }

                    // Check type compatibility
                    if (!AreTypesCompatible(sourceColumn.DataType, targetColumn.DataType))
                    {
                        errors.Add(new ValidationError
                        {
                            Code = "TYPE_MISMATCH",
                            Message = $"Field '{targetColumn.Name}' type mismatch: transform outputs {sourceColumn.DataType.Name}, target expects {targetColumn.DataType.Name}",
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
    public ArrayRowOptimizationResult ValidateArrayRowOptimization(JavaScriptTransformConfiguration configuration)
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
                    Message = $"Input schema has {inputColumns.Count} columns. Consider field reduction for optimal JavaScript performance."
                });
            }
        }

        // Validate output schema optimization
        if (configuration.OutputSchema != null)
        {
            var outputColumns = configuration.OutputSchema.Columns.ToList();
            for (int i = 0; i < outputColumns.Count; i++)
            {
                optimizations.Add($"Output column '{outputColumns[i].Name}' at index {i} - optimized for ArrayRow creation");
            }

            if (outputColumns.Count > 50)
            {
                warnings.Add(new ValidationWarning
                {
                    Code = "MANY_OUTPUT_COLUMNS",
                    Message = $"Output schema has {outputColumns.Count} columns. Consider field reduction for optimal JavaScript performance."
                });
            }
        }

        // JavaScript-specific optimizations
        if (configuration.EnableScriptCaching)
        {
            optimizations.Add("Script caching enabled - compiled scripts will be reused for better performance");
        }

        if (configuration.EnableParallelProcessing && configuration.BatchSize > 100)
        {
            optimizations.Add($"Parallel processing enabled with batch size {configuration.BatchSize} - good for CPU-intensive transformations");
        }

        return new ArrayRowOptimizationResult
        {
            IsOptimized = errors.Count == 0,
            Errors = errors.ToImmutableArray(),
            Warnings = warnings.ToImmutableArray(),
            Optimizations = optimizations.ToImmutableArray(),
            ValidationTime = TimeSpan.FromMilliseconds(2)
        };
    }

    /// <summary>
    /// Validates plugin-specific business rules.
    /// </summary>
    /// <param name="configuration">Configuration to validate</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Plugin-specific validation result</returns>
    public async Task<ValidationResult> ValidatePluginSpecificAsync(JavaScriptTransformConfiguration configuration, CancellationToken cancellationToken = default)
    {
        var errors = new List<ValidationError>();
        var warnings = new List<ValidationWarning>();

        // Validate JavaScript-specific configurations
        if (configuration.ScriptTimeoutMs < 100)
        {
            warnings.Add(new ValidationWarning
            {
                Code = "LOW_SCRIPT_TIMEOUT",
                Message = "Script timeout is very low (< 100ms). Complex transformations may fail."
            });
        }
        else if (configuration.ScriptTimeoutMs > 30000)
        {
            warnings.Add(new ValidationWarning
            {
                Code = "HIGH_SCRIPT_TIMEOUT",
                Message = "Script timeout is very high (> 30s). This may impact throughput in case of script errors."
            });
        }

        if (configuration.MaxMemoryBytes < 1024 * 1024) // 1MB
        {
            warnings.Add(new ValidationWarning
            {
                Code = "LOW_MEMORY_LIMIT",
                Message = "Memory limit is very low (< 1MB). Complex transformations may fail."
            });
        }

        if (configuration.BatchSize < 10)
        {
            warnings.Add(new ValidationWarning
            {
                Code = "SMALL_BATCH_SIZE",
                Message = "Small batch size may reduce throughput. Consider increasing for better performance."
            });
        }
        else if (configuration.BatchSize > 10000)
        {
            warnings.Add(new ValidationWarning
            {
                Code = "LARGE_BATCH_SIZE",
                Message = "Large batch size may increase memory usage. Monitor memory consumption."
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
    /// Validates the script configuration aspects.
    /// </summary>
    private void ValidateScriptConfiguration(JavaScriptTransformConfiguration configuration, List<ValidationError> errors, List<ValidationWarning> warnings)
    {
        // Validate script source
        if (string.IsNullOrWhiteSpace(configuration.ScriptCode) && string.IsNullOrWhiteSpace(configuration.ScriptFilePath))
        {
            errors.Add(new ValidationError
            {
                Code = "NO_SCRIPT_SOURCE",
                Message = "Either ScriptCode or ScriptFilePath must be provided",
                Severity = ErrorSeverity.Error
            });
        }

        // Validate script file if specified
        if (!string.IsNullOrWhiteSpace(configuration.ScriptFilePath))
        {
            if (!File.Exists(configuration.ScriptFilePath))
            {
                errors.Add(new ValidationError
                {
                    Code = "SCRIPT_FILE_NOT_FOUND",
                    Message = $"Script file not found: {configuration.ScriptFilePath}",
                    PropertyPath = nameof(configuration.ScriptFilePath),
                    Severity = ErrorSeverity.Error
                });
            }
            else
            {
                try
                {
                    var fileInfo = new FileInfo(configuration.ScriptFilePath);
                    if (fileInfo.Length > 1024 * 1024) // 1MB
                    {
                        warnings.Add(new ValidationWarning
                        {
                            Code = "LARGE_SCRIPT_FILE",
                            Message = $"Script file is large ({fileInfo.Length / 1024}KB). Consider optimization for better performance.",
                            PropertyPath = nameof(configuration.ScriptFilePath)
                        });
                    }
                }
                catch (Exception ex)
                {
                    errors.Add(new ValidationError
                    {
                        Code = "SCRIPT_FILE_ACCESS_ERROR",
                        Message = $"Cannot access script file: {ex.Message}",
                        PropertyPath = nameof(configuration.ScriptFilePath),
                        Severity = ErrorSeverity.Error
                    });
                }
            }
        }

        // Validate function name
        if (string.IsNullOrWhiteSpace(configuration.FunctionName))
        {
            errors.Add(new ValidationError
            {
                Code = "EMPTY_FUNCTION_NAME",
                Message = "FunctionName cannot be empty",
                PropertyPath = nameof(configuration.FunctionName),
                Severity = ErrorSeverity.Error
            });
        }
        else if (!IsValidJavaScriptIdentifier(configuration.FunctionName))
        {
            errors.Add(new ValidationError
            {
                Code = "INVALID_FUNCTION_NAME",
                Message = $"FunctionName '{configuration.FunctionName}' is not a valid JavaScript identifier",
                PropertyPath = nameof(configuration.FunctionName),
                Severity = ErrorSeverity.Error
            });
        }
    }

    /// <summary>
    /// Validates the schema configuration aspects.
    /// </summary>
    private void ValidateSchemaConfiguration(JavaScriptTransformConfiguration configuration, List<ValidationError> errors, List<ValidationWarning> warnings)
    {
        // Validate that at least output schema is defined for transform plugins
        if (configuration.OutputSchema == null)
        {
            warnings.Add(new ValidationWarning
            {
                Code = "NO_OUTPUT_SCHEMA",
                Message = "Transform plugins should define an output schema for better type safety",
                PropertyPath = nameof(configuration.OutputSchema)
            });
        }

        // Validate schema compatibility if both input and output are defined
        if (configuration.InputSchema != null && configuration.OutputSchema != null)
        {
            if (configuration.InputSchema.ColumnCount == 0)
            {
                warnings.Add(new ValidationWarning
                {
                    Code = "EMPTY_INPUT_SCHEMA",
                    Message = "Input schema has no columns defined"
                });
            }

            if (configuration.OutputSchema.ColumnCount == 0)
            {
                warnings.Add(new ValidationWarning
                {
                    Code = "EMPTY_OUTPUT_SCHEMA",
                    Message = "Output schema has no columns defined"
                });
            }
        }
    }

    /// <summary>
    /// Validates the performance configuration aspects.
    /// </summary>
    private void ValidatePerformanceConfiguration(JavaScriptTransformConfiguration configuration, List<ValidationError> errors, List<ValidationWarning> warnings)
    {
        // Validate timeout values
        if (configuration.ScriptTimeoutMs <= 0)
        {
            errors.Add(new ValidationError
            {
                Code = "INVALID_TIMEOUT",
                Message = "ScriptTimeoutMs must be greater than 0",
                PropertyPath = nameof(configuration.ScriptTimeoutMs),
                Severity = ErrorSeverity.Error
            });
        }

        // Validate memory limits
        if (configuration.MaxMemoryBytes <= 0)
        {
            errors.Add(new ValidationError
            {
                Code = "INVALID_MEMORY_LIMIT",
                Message = "MaxMemoryBytes must be greater than 0",
                PropertyPath = nameof(configuration.MaxMemoryBytes),
                Severity = ErrorSeverity.Error
            });
        }

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
    /// Validates security aspects of the JavaScript code.
    /// </summary>
    private async Task ValidateScriptSecurityAsync(JavaScriptTransformConfiguration configuration, List<ValidationError> errors, List<ValidationWarning> warnings, CancellationToken cancellationToken)
    {
        try
        {
            var scriptCode = configuration.GetEffectiveScriptCode();

            // Check for security anti-patterns
            foreach (var pattern in SecurityPatterns)
            {
                if (pattern.IsMatch(scriptCode))
                {
                    warnings.Add(new ValidationWarning
                    {
                        Code = "SECURITY_PATTERN_DETECTED",
                        Message = $"Potentially unsafe JavaScript pattern detected: {pattern}. Review for security implications."
                    });
                }
            }

            // Check for performance anti-patterns
            foreach (var pattern in PerformancePatterns)
            {
                if (pattern.IsMatch(scriptCode))
                {
                    warnings.Add(new ValidationWarning
                    {
                        Code = "PERFORMANCE_ANTI_PATTERN",
                        Message = $"Performance anti-pattern detected: {pattern}. This may cause poor performance."
                    });
                }
            }
        }
        catch (Exception ex)
        {
            errors.Add(new ValidationError
            {
                Code = "SCRIPT_SECURITY_CHECK_FAILED",
                Message = $"Failed to perform security validation: {ex.Message}",
                Severity = ErrorSeverity.Warning
            });
        }

        await Task.CompletedTask;
    }

    /// <summary>
    /// Validates JavaScript syntax (basic validation).
    /// </summary>
    private async Task ValidateScriptSyntaxAsync(JavaScriptTransformConfiguration configuration, List<ValidationError> errors, List<ValidationWarning> warnings, CancellationToken cancellationToken)
    {
        try
        {
            var scriptCode = configuration.GetEffectiveScriptCode();

            // Basic syntax checks
            if (scriptCode.Length > 100000) // 100KB
            {
                warnings.Add(new ValidationWarning
                {
                    Code = "LARGE_SCRIPT",
                    Message = "Script is very large. Consider breaking into smaller modules for better maintainability."
                });
            }

            // Check for balanced braces, brackets, and parentheses
            if (!AreBalanced(scriptCode, '{', '}') || !AreBalanced(scriptCode, '[', ']') || !AreBalanced(scriptCode, '(', ')'))
            {
                errors.Add(new ValidationError
                {
                    Code = "SYNTAX_ERROR_UNBALANCED",
                    Message = "Syntax error: Unbalanced braces, brackets, or parentheses detected",
                    Severity = ErrorSeverity.Error
                });
            }

            // Check that the required function exists
            var functionPattern = new Regex($@"\bfunction\s+{Regex.Escape(configuration.FunctionName)}\s*\(", RegexOptions.IgnoreCase);
            var arrowFunctionPattern = new Regex($@"\b{Regex.Escape(configuration.FunctionName)}\s*=\s*\(", RegexOptions.IgnoreCase);
            
            if (!functionPattern.IsMatch(scriptCode) && !arrowFunctionPattern.IsMatch(scriptCode))
            {
                warnings.Add(new ValidationWarning
                {
                    Code = "FUNCTION_NOT_FOUND",
                    Message = $"Function '{configuration.FunctionName}' not found in script. Ensure the function is properly defined."
                });
            }
        }
        catch (Exception ex)
        {
            warnings.Add(new ValidationWarning
            {
                Code = "SYNTAX_VALIDATION_FAILED",
                Message = $"Could not validate script syntax: {ex.Message}"
            });
        }

        await Task.CompletedTask;
    }

    /// <summary>
    /// Checks if two data types are compatible for transformation.
    /// </summary>
    private static bool AreTypesCompatible(Type sourceType, Type targetType)
    {
        if (sourceType == targetType) return true;

        // String can be converted to most types
        if (sourceType == typeof(string)) return true;

        // Object can contain anything
        if (targetType == typeof(object)) return true;

        // Numeric type compatibility
        if (IsNumericType(sourceType) && IsNumericType(targetType))
        {
            return true; // JavaScript can handle numeric conversions
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
    /// Checks if a string is a valid JavaScript identifier.
    /// </summary>
    private static bool IsValidJavaScriptIdentifier(string identifier)
    {
        if (string.IsNullOrWhiteSpace(identifier))
            return false;

        // Basic check for valid JavaScript identifier
        var pattern = new Regex(@"^[a-zA-Z_$][a-zA-Z0-9_$]*$");
        return pattern.IsMatch(identifier);
    }

    /// <summary>
    /// Checks if opening and closing characters are balanced in the text.
    /// </summary>
    private static bool AreBalanced(string text, char open, char close)
    {
        int count = 0;
        bool inString = false;
        char stringChar = '\0';

        for (int i = 0; i < text.Length; i++)
        {
            var ch = text[i];

            // Handle string literals
            if (!inString && (ch == '"' || ch == '\'' || ch == '`'))
            {
                inString = true;
                stringChar = ch;
                continue;
            }

            if (inString)
            {
                if (ch == stringChar && (i == 0 || text[i - 1] != '\\'))
                {
                    inString = false;
                }
                continue;
            }

            // Count opening and closing characters outside strings
            if (ch == open)
                count++;
            else if (ch == close)
                count--;

            if (count < 0)
                return false; // More closing than opening
        }

        return count == 0; // Should be balanced
    }

    /// <summary>
    /// Validates the configuration using JSON schema validation based on attributes.
    /// </summary>
    /// <param name="configuration">Configuration to validate</param>
    /// <param name="errors">List to add validation errors to</param>
    /// <param name="warnings">List to add validation warnings to</param>
    /// <param name="cancellationToken">Cancellation token</param>
    private async Task ValidateJsonSchemaAsync(
        JavaScriptTransformConfiguration configuration,
        List<ValidationError> errors,
        List<ValidationWarning> warnings,
        CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogDebug("Performing JSON schema validation for JavaScriptTransform configuration");

            // Generate schema from the configuration type using attributes
            var schemaDefinition = _jsonSchemaValidator.CreateSchemaFromType<JavaScriptTransformConfiguration>();

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

            // Additional file path validation for FilePathSchema attributes
            await ValidateFilePathSchemaAsync(configuration, errors, warnings, cancellationToken);

            // Additional performance validation for PerformanceSchema attributes
            ValidatePerformanceSchemaAsync(configuration, errors, warnings);

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
    /// Validates file path properties with FilePathSchema attributes.
    /// </summary>
    private async Task ValidateFilePathSchemaAsync(
        JavaScriptTransformConfiguration configuration,
        List<ValidationError> errors,
        List<ValidationWarning> warnings,
        CancellationToken cancellationToken)
    {
        // Validate ScriptFilePath if specified
        if (!string.IsNullOrWhiteSpace(configuration.ScriptFilePath))
        {
            if (!File.Exists(configuration.ScriptFilePath))
            {
                errors.Add(new ValidationError
                {
                    Code = "SCRIPT_FILE_NOT_FOUND",
                    Message = $"Script file not found: {configuration.ScriptFilePath}",
                    PropertyPath = nameof(configuration.ScriptFilePath),
                    Severity = ErrorSeverity.Error
                });
            }
            else
            {
                var fileInfo = new FileInfo(configuration.ScriptFilePath);
                var extension = fileInfo.Extension.ToLowerInvariant();
                
                if (extension != ".js" && extension != ".mjs")
                {
                    warnings.Add(new ValidationWarning
                    {
                        Code = "UNEXPECTED_FILE_EXTENSION",
                        Message = $"Script file has unexpected extension '{extension}'. Expected .js or .mjs",
                        PropertyPath = nameof(configuration.ScriptFilePath)
                    });
                }

                if (fileInfo.Length > 1024 * 1024) // 1MB
                {
                    warnings.Add(new ValidationWarning
                    {
                        Code = "LARGE_SCRIPT_FILE",
                        Message = $"Script file is large ({fileInfo.Length / 1024}KB). This may impact performance",
                        PropertyPath = nameof(configuration.ScriptFilePath)
                    });
                }
            }
        }

        await Task.CompletedTask;
    }

    /// <summary>
    /// Validates performance properties with PerformanceSchema attributes.
    /// </summary>
    private void ValidatePerformanceSchemaAsync(
        JavaScriptTransformConfiguration configuration,
        List<ValidationError> errors,
        List<ValidationWarning> warnings)
    {
        // Validate ScriptTimeoutMs against recommended range
        if (configuration.ScriptTimeoutMs < 100)
        {
            warnings.Add(new ValidationWarning
            {
                Code = "SCRIPT_TIMEOUT_TOO_LOW",
                Message = "Script timeout is below recommended minimum (100ms). Complex scripts may fail",
                PropertyPath = nameof(configuration.ScriptTimeoutMs)
            });
        }
        else if (configuration.ScriptTimeoutMs > 5000)
        {
            warnings.Add(new ValidationWarning
            {
                Code = "SCRIPT_TIMEOUT_TOO_HIGH",
                Message = "Script timeout is above recommended maximum (5000ms). This may impact throughput",
                PropertyPath = nameof(configuration.ScriptTimeoutMs)
            });
        }

        // Validate MaxMemoryBytes against recommended range
        if (configuration.MaxMemoryBytes < 1048576) // 1MB
        {
            warnings.Add(new ValidationWarning
            {
                Code = "MEMORY_LIMIT_TOO_LOW",
                Message = "Memory limit is below recommended minimum (1MB). Complex scripts may fail",
                PropertyPath = nameof(configuration.MaxMemoryBytes)
            });
        }
        else if (configuration.MaxMemoryBytes > 104857600) // 100MB
        {
            warnings.Add(new ValidationWarning
            {
                Code = "MEMORY_LIMIT_TOO_HIGH",
                Message = "Memory limit is above recommended maximum (100MB). This may cause memory pressure",
                PropertyPath = nameof(configuration.MaxMemoryBytes)
            });
        }

        // Validate BatchSize against recommended range
        if (configuration.BatchSize < 100)
        {
            warnings.Add(new ValidationWarning
            {
                Code = "BATCH_SIZE_TOO_SMALL",
                Message = "Batch size is below recommended minimum (100). This may reduce throughput",
                PropertyPath = nameof(configuration.BatchSize)
            });
        }
        else if (configuration.BatchSize > 10000)
        {
            warnings.Add(new ValidationWarning
            {
                Code = "BATCH_SIZE_TOO_LARGE",
                Message = "Batch size is above recommended maximum (10000). This may increase memory usage",
                PropertyPath = nameof(configuration.BatchSize)
            });
        }

        // Validate MaxDegreeOfParallelism against system capabilities
        if (configuration.MaxDegreeOfParallelism > Environment.ProcessorCount * 2)
        {
            warnings.Add(new ValidationWarning
            {
                Code = "PARALLELISM_TOO_HIGH",
                Message = $"Maximum degree of parallelism ({configuration.MaxDegreeOfParallelism}) exceeds recommended maximum (2x CPU cores: {Environment.ProcessorCount * 2})",
                PropertyPath = nameof(configuration.MaxDegreeOfParallelism)
            });
        }
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

// Example usage demonstrating JSON schema validation integration:
/*
var configuration = new JavaScriptTransformConfiguration
{
    ScriptCode = "function transform(row) { return row; }",  // Valid
    FunctionName = "transform",                              // Valid pattern
    ScriptTimeoutMs = 50,                                   // Below recommended min (100ms) - warning
    MaxMemoryBytes = 512 * 1024,                           // Below recommended min (1MB) - warning  
    BatchSize = 50,                                         // Below recommended min (100) - warning
    ScriptFilePath = "/nonexistent/script.js"              // File doesn't exist - error
};

var validator = new JavaScriptTransformValidator(logger, jsonSchemaValidator);
var result = await validator.ValidateAsync(configuration);

// Result will contain:
// - Errors: Script file not found
// - Warnings: ScriptTimeoutMs too low, MaxMemoryBytes too low, BatchSize too small
// - Schema validation errors are automatically integrated via attributes

Example validation output:
{
  "IsValid": false,
  "Errors": [
    {
      "Code": "SCRIPT_FILE_NOT_FOUND",
      "Message": "Script file not found: /nonexistent/script.js",
      "PropertyPath": "ScriptFilePath",
      "Severity": "Error"
    }
  ],
  "Warnings": [
    {
      "Code": "SCRIPT_TIMEOUT_TOO_LOW", 
      "Message": "Script timeout is below recommended minimum (100ms). Complex scripts may fail",
      "PropertyPath": "ScriptTimeoutMs"
    },
    {
      "Code": "MEMORY_LIMIT_TOO_LOW",
      "Message": "Memory limit is below recommended minimum (1MB). Complex scripts may fail", 
      "PropertyPath": "MaxMemoryBytes"
    },
    {
      "Code": "BATCH_SIZE_TOO_SMALL",
      "Message": "Batch size is below recommended minimum (100). This may reduce throughput",
      "PropertyPath": "BatchSize"
    }
  ]
}
*/