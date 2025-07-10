using FlowEngine.Abstractions.Configuration;
using FlowEngine.Abstractions.ErrorHandling;
using Microsoft.Extensions.Logging;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;

namespace FlowEngine.Core.Configuration;

/// <summary>
/// Validates FlowEngine configurations and provides default values for production deployment.
/// Ensures configuration integrity and provides helpful error messages for misconfiguration.
/// </summary>
public sealed class ConfigurationValidator : IDisposable
{
    private readonly ILogger<ConfigurationValidator> _logger;
    private readonly Dictionary<Type, IConfigurationValidator> _validators = new();
    private readonly object _lock = new();
    private bool _disposed;

    /// <summary>
    /// Configuration for the validator behavior.
    /// </summary>
    public ValidationConfiguration Configuration { get; }

    public ConfigurationValidator(ILogger<ConfigurationValidator> logger, ValidationConfiguration? configuration = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        Configuration = configuration ?? new ValidationConfiguration();
        
        RegisterDefaultValidators();
        _logger.LogInformation("Configuration validator initialized with {ValidatorCount} validators", _validators.Count);
    }

    /// <summary>
    /// Validates a configuration object and returns detailed validation results.
    /// </summary>
    public async Task<ValidatorResult> ValidateAsync<T>(T configuration) where T : class
    {
        if (configuration == null)
        {
            return ValidatorResult.CreateError("Configuration cannot be null");
        }

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var validationContext = new ValidationContext(configuration);
        var validationResults = new List<ValidationResult>();
        var warnings = new List<ConfigurationWarning>();
        
        try
        {
            _logger.LogDebug("Validating configuration of type {ConfigurationType}", typeof(T).Name);

            // Step 1: Data annotations validation
            var isValid = Validator.TryValidateObject(configuration, validationContext, validationResults, true);

            // Step 2: Custom validator if available
            if (_validators.TryGetValue(typeof(T), out var customValidator))
            {
                var customResult = await customValidator.ValidateAsync(configuration);
                validationResults.AddRange(customResult.Errors.Select(e => new ValidationResult(e.Message, e.MemberNames)));
                warnings.AddRange(customResult.Warnings);
                isValid = isValid && customResult.IsValid;
            }

            // Step 3: Cross-field validation for complex scenarios
            var crossFieldResults = await ValidateCrossFieldRulesAsync(configuration);
            validationResults.AddRange(crossFieldResults.Errors);
            warnings.AddRange(crossFieldResults.Warnings);
            isValid = isValid && crossFieldResults.IsValid;

            // Step 4: Environmental validation (file paths, network connectivity, etc.)
            if (Configuration.ValidateEnvironmentalRequirements)
            {
                var envResults = await ValidateEnvironmentalRequirementsAsync(configuration);
                validationResults.AddRange(envResults.Errors);
                warnings.AddRange(envResults.Warnings);
                isValid = isValid && envResults.IsValid;
            }

            var result = new ValidatorResult
            {
                IsValid = isValid,
                ConfigurationType = typeof(T).Name,
                Errors = validationResults.Select(vr => new ConfigurationError
                {
                    Message = vr.ErrorMessage ?? "Unknown validation error",
                    MemberNames = vr.MemberNames?.ToArray() ?? Array.Empty<string>(),
                    ErrorCode = DetermineErrorCode(vr)
                }).ToArray(),
                Warnings = warnings.ToArray(),
                ValidationDuration = stopwatch.Elapsed,
                ValidationTimestamp = DateTimeOffset.UtcNow
            };

            _logger.LogInformation("Configuration validation completed for {ConfigurationType} in {Duration}ms. Valid: {IsValid}, Errors: {ErrorCount}, Warnings: {WarningCount}",
                typeof(T).Name, stopwatch.ElapsedMilliseconds, isValid, result.Errors.Length, result.Warnings.Length);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during configuration validation for type {ConfigurationType}", typeof(T).Name);
            
            return ValidatorResult.CreateError($"Validation failed with exception: {ex.Message}", typeof(T).Name);
        }
    }

    /// <summary>
    /// Applies default values to a configuration object where properties are null or invalid.
    /// </summary>
    public T ApplyDefaults<T>(T configuration) where T : class, new()
    {
        if (configuration == null)
        {
            _logger.LogInformation("Configuration is null, creating new instance with defaults for type {ConfigurationType}", typeof(T).Name);
            return CreateWithDefaults<T>();
        }

        try
        {
            _logger.LogDebug("Applying default values to configuration of type {ConfigurationType}", typeof(T).Name);

            var properties = typeof(T).GetProperties().Where(p => p.CanWrite);
            var defaultsApplied = 0;

            foreach (var property in properties)
            {
                var currentValue = property.GetValue(configuration);
                var defaultValue = GetDefaultValue(property, currentValue);

                if (defaultValue != null && !Equals(currentValue, defaultValue))
                {
                    property.SetValue(configuration, defaultValue);
                    defaultsApplied++;
                    
                    _logger.LogDebug("Applied default value for property {PropertyName}: {DefaultValue}", 
                        property.Name, defaultValue);
                }
            }

            _logger.LogInformation("Applied {DefaultsCount} default values to configuration of type {ConfigurationType}", 
                defaultsApplied, typeof(T).Name);

            return configuration;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error applying defaults to configuration of type {ConfigurationType}", typeof(T).Name);
            throw new InvalidOperationException($"Failed to apply defaults to configuration: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Creates a new configuration instance with all default values applied.
    /// </summary>
    public T CreateWithDefaults<T>() where T : class, new()
    {
        var instance = new T();
        return ApplyDefaults(instance);
    }

    /// <summary>
    /// Validates configuration from a JSON string and applies defaults if needed.
    /// </summary>
    public async Task<ConfigurationLoadResult<T>> LoadAndValidateFromJsonAsync<T>(string json, bool applyDefaults = true) where T : class, new()
    {
        try
        {
            _logger.LogDebug("Loading and validating configuration from JSON for type {ConfigurationType}", typeof(T).Name);

            // Parse JSON
            T? configuration;
            try
            {
                configuration = JsonSerializer.Deserialize<T>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    AllowTrailingCommas = true
                });
            }
            catch (JsonException jsonEx)
            {
                return ConfigurationLoadResult<T>.CreateError($"JSON parsing failed: {jsonEx.Message}");
            }

            if (configuration == null)
            {
                if (applyDefaults)
                {
                    configuration = CreateWithDefaults<T>();
                    _logger.LogInformation("JSON deserialization returned null, created instance with defaults");
                }
                else
                {
                    return ConfigurationLoadResult<T>.CreateError("JSON deserialization returned null");
                }
            }

            // Apply defaults if requested
            if (applyDefaults)
            {
                configuration = ApplyDefaults(configuration);
            }

            // Validate
            var validationResult = await ValidateAsync(configuration);

            return new ConfigurationLoadResult<T>
            {
                Configuration = configuration,
                ValidatorResult = validationResult,
                IsSuccess = validationResult.IsValid,
                LoadedFromDefaults = applyDefaults
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading and validating configuration from JSON for type {ConfigurationType}", typeof(T).Name);
            return ConfigurationLoadResult<T>.CreateError($"Configuration load failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Validates configuration from a file and applies defaults if needed.
    /// </summary>
    public async Task<ConfigurationLoadResult<T>> LoadAndValidateFromFileAsync<T>(string filePath, bool applyDefaults = true) where T : class, new()
    {
        try
        {
            if (!File.Exists(filePath))
            {
                if (applyDefaults)
                {
                    _logger.LogInformation("Configuration file {FilePath} not found, creating with defaults", filePath);
                    var defaultConfig = CreateWithDefaults<T>();
                    var validationResult = await ValidateAsync(defaultConfig);
                    
                    return new ConfigurationLoadResult<T>
                    {
                        Configuration = defaultConfig,
                        ValidatorResult = validationResult,
                        IsSuccess = validationResult.IsValid,
                        LoadedFromDefaults = true
                    };
                }
                else
                {
                    return ConfigurationLoadResult<T>.CreateError($"Configuration file not found: {filePath}");
                }
            }

            var json = await File.ReadAllTextAsync(filePath);
            var result = await LoadAndValidateFromJsonAsync<T>(json, applyDefaults);
            
            if (result.IsSuccess)
            {
                _logger.LogInformation("Successfully loaded and validated configuration from file {FilePath}", filePath);
            }
            else
            {
                _logger.LogWarning("Configuration loaded from file {FilePath} but validation failed", filePath);
            }

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading configuration file {FilePath}", filePath);
            return ConfigurationLoadResult<T>.CreateError($"Failed to load configuration file: {ex.Message}");
        }
    }

    /// <summary>
    /// Registers a custom validator for a specific configuration type.
    /// </summary>
    public void RegisterValidator<T>(IConfigurationValidator<T> validator) where T : class
    {
        if (validator == null)
            throw new ArgumentNullException(nameof(validator));

        lock (_lock)
        {
            _validators[typeof(T)] = new ConfigurationValidatorAdapter<T>(validator);
        }

        _logger.LogInformation("Registered custom validator for configuration type {ConfigurationType}", typeof(T).Name);
    }

    private void RegisterDefaultValidators()
    {
        // Register built-in validators for common configuration types
        // These would be implemented based on specific FlowEngine configuration types
        
        _logger.LogDebug("Registered default configuration validators");
    }

    private async Task<InternalValidatorResult> ValidateCrossFieldRulesAsync<T>(T configuration) where T : class
    {
        var errors = new List<ValidationResult>();
        var warnings = new List<ConfigurationWarning>();

        // Example cross-field validation rules
        if (configuration is IHasMemoryLimits memoryConfig)
        {
            if (memoryConfig.MaxMemoryMB > 0 && memoryConfig.MaxMemoryMB < memoryConfig.InitialMemoryMB)
            {
                errors.Add(new ValidationResult("MaxMemoryMB cannot be less than InitialMemoryMB", 
                    new[] { nameof(memoryConfig.MaxMemoryMB), nameof(memoryConfig.InitialMemoryMB) }));
            }
        }

        if (configuration is IHasTimeouts timeoutConfig)
        {
            if (timeoutConfig.ConnectionTimeout > timeoutConfig.OperationTimeout)
            {
                warnings.Add(new ConfigurationWarning
                {
                    Message = "Connection timeout is greater than operation timeout, which may cause unexpected behavior",
                    MemberNames = new[] { nameof(timeoutConfig.ConnectionTimeout), nameof(timeoutConfig.OperationTimeout) },
                    Severity = WarningSeverity.Medium
                });
            }
        }

        return new InternalValidatorResult
        {
            IsValid = !errors.Any(),
            Errors = errors,
            Warnings = warnings
        };
    }

    private async Task<InternalValidatorResult> ValidateEnvironmentalRequirementsAsync<T>(T configuration) where T : class
    {
        var errors = new List<ValidationResult>();
        var warnings = new List<ConfigurationWarning>();

        // Validate file paths
        if (configuration is IHasFilePaths filePathConfig)
        {
            foreach (var property in typeof(T).GetProperties())
            {
                if (property.PropertyType == typeof(string) && property.Name.Contains("Path"))
                {
                    var path = property.GetValue(configuration) as string;
                    if (!string.IsNullOrEmpty(path))
                    {
                        var pathValidation = ValidateFilePath(path, property.Name);
                        if (pathValidation.Error != null)
                        {
                            errors.Add(pathValidation.Error);
                        }
                        if (pathValidation.Warning != null)
                        {
                            warnings.Add(pathValidation.Warning);
                        }
                    }
                }
            }
        }

        // Validate network endpoints
        if (configuration is IHasNetworkEndpoints networkConfig)
        {
            // Validate network connectivity, DNS resolution, etc.
            // Implementation would depend on specific network requirements
        }

        return new InternalValidatorResult
        {
            IsValid = !errors.Any(),
            Errors = errors,
            Warnings = warnings
        };
    }

    private (System.ComponentModel.DataAnnotations.ValidationResult? Error, ConfigurationWarning? Warning) ValidateFilePath(string path, string propertyName)
    {
        try
        {
            var fullPath = Path.GetFullPath(path);
            var directory = Path.GetDirectoryName(fullPath);

            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                return (new System.ComponentModel.DataAnnotations.ValidationResult(
                    $"Directory does not exist for {propertyName}: {directory}", 
                    new[] { propertyName }), null);
            }

            if (File.Exists(fullPath))
            {
                var fileInfo = new FileInfo(fullPath);
                if (fileInfo.IsReadOnly)
                {
                    return (null, new ConfigurationWarning
                    {
                        Message = $"File is read-only: {fullPath}",
                        MemberNames = new[] { propertyName },
                        Severity = WarningSeverity.Low
                    });
                }
            }

            return (null, null);
        }
        catch (Exception ex)
        {
            return (new System.ComponentModel.DataAnnotations.ValidationResult(
                $"Invalid file path for {propertyName}: {ex.Message}", 
                new[] { propertyName }), null);
        }
    }

    private object? GetDefaultValue(System.Reflection.PropertyInfo property, object? currentValue)
    {
        // Check for DefaultValue attribute
        var defaultValueAttr = property.GetCustomAttributes(typeof(System.ComponentModel.DefaultValueAttribute), false)
                                      .Cast<System.ComponentModel.DefaultValueAttribute>()
                                      .FirstOrDefault();
        
        if (defaultValueAttr != null)
        {
            return defaultValueAttr.Value;
        }

        // Apply type-specific defaults for null or invalid values
        if (currentValue == null || IsInvalidValue(currentValue))
        {
            return property.PropertyType switch
            {
                Type t when t == typeof(string) => string.Empty,
                Type t when t == typeof(int) => 0,
                Type t when t == typeof(long) => 0L,
                Type t when t == typeof(double) => 0.0,
                Type t when t == typeof(bool) => false,
                Type t when t == typeof(TimeSpan) => TimeSpan.Zero,
                Type t when t == typeof(TimeSpan?) => TimeSpan.Zero,
                Type t when t.IsGenericType && t.GetGenericTypeDefinition() == typeof(List<>) => Activator.CreateInstance(t),
                Type t when t.IsArray => Array.CreateInstance(t.GetElementType()!, 0),
                _ => null
            };
        }

        return currentValue;
    }

    private bool IsInvalidValue(object value)
    {
        return value switch
        {
            string s => string.IsNullOrWhiteSpace(s),
            int i => i < 0,
            long l => l < 0,
            double d => d < 0 || double.IsNaN(d) || double.IsInfinity(d),
            TimeSpan ts => ts < TimeSpan.Zero,
            _ => false
        };
    }

    private string DetermineErrorCode(System.ComponentModel.DataAnnotations.ValidationResult validationResult)
    {
        var message = validationResult.ErrorMessage ?? "";
        
        return message.ToLowerInvariant() switch
        {
            var m when m.Contains("required") => "REQUIRED_FIELD",
            var m when m.Contains("range") => "VALUE_OUT_OF_RANGE",
            var m when m.Contains("length") => "INVALID_LENGTH",
            var m when m.Contains("format") => "INVALID_FORMAT",
            var m when m.Contains("path") => "INVALID_PATH",
            var m when m.Contains("url") || m.Contains("uri") => "INVALID_URL",
            var m when m.Contains("email") => "INVALID_EMAIL",
            _ => "VALIDATION_ERROR"
        };
    }

    public void Dispose()
    {
        if (_disposed) return;

        try
        {
            lock (_lock)
            {
                foreach (var validator in _validators.Values.OfType<IDisposable>())
                {
                    validator.Dispose();
                }
                _validators.Clear();
            }
            
            _logger.LogInformation("Configuration validator disposed");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error during configuration validator disposal");
        }
        finally
        {
            _disposed = true;
        }
    }

    private sealed class InternalValidatorResult
    {
        public bool IsValid { get; set; }
        public List<System.ComponentModel.DataAnnotations.ValidationResult> Errors { get; set; } = new();
        public List<ConfigurationWarning> Warnings { get; set; } = new();
    }
}

/// <summary>
/// Configuration for the configuration validator.
/// </summary>
public sealed class ValidationConfiguration
{
    /// <summary>
    /// Gets or sets whether to validate environmental requirements (file paths, network connectivity, etc.).
    /// </summary>
    public bool ValidateEnvironmentalRequirements { get; set; } = true;
    
    /// <summary>
    /// Gets or sets whether to automatically apply defaults for missing or invalid values.
    /// </summary>
    public bool AutoApplyDefaults { get; set; } = true;
    
    /// <summary>
    /// Gets or sets the maximum validation timeout.
    /// </summary>
    public TimeSpan ValidationTimeout { get; set; } = TimeSpan.FromSeconds(30);
    
    /// <summary>
    /// Gets or sets whether to validate cross-field dependencies.
    /// </summary>
    public bool ValidateCrossFieldRules { get; set; } = true;
}

/// <summary>
/// Result of configuration validation.
/// </summary>
public sealed class ValidatorResult
{
    /// <summary>
    /// Gets whether the configuration is valid.
    /// </summary>
    public bool IsValid { get; set; }
    
    /// <summary>
    /// Gets the type of configuration that was validated.
    /// </summary>
    public string ConfigurationType { get; set; } = string.Empty;
    
    /// <summary>
    /// Gets the validation errors.
    /// </summary>
    public ConfigurationError[] Errors { get; set; } = Array.Empty<ConfigurationError>();
    
    /// <summary>
    /// Gets the validation warnings.
    /// </summary>
    public ConfigurationWarning[] Warnings { get; set; } = Array.Empty<ConfigurationWarning>();
    
    /// <summary>
    /// Gets the duration of the validation process.
    /// </summary>
    public TimeSpan ValidationDuration { get; set; }
    
    /// <summary>
    /// Gets the timestamp when validation was performed.
    /// </summary>
    public DateTimeOffset ValidationTimestamp { get; set; }
    
    /// <summary>
    /// Gets whether there are any warnings.
    /// </summary>
    public bool HasWarnings => Warnings.Any();
    
    /// <summary>
    /// Gets a summary of the validation result.
    /// </summary>
    public string Summary => $"Validation {(IsValid ? "passed" : "failed")} for {ConfigurationType} with {Errors.Length} errors and {Warnings.Length} warnings";

    public static ValidatorResult CreateError(string errorMessage, string configurationType = "Unknown")
    {
        return new ValidatorResult
        {
            IsValid = false,
            ConfigurationType = configurationType,
            Errors = new[]
            {
                new ConfigurationError
                {
                    Message = errorMessage,
                    ErrorCode = "VALIDATION_FAILED",
                    MemberNames = Array.Empty<string>()
                }
            },
            ValidationTimestamp = DateTimeOffset.UtcNow
        };
    }
}

/// <summary>
/// Result of configuration loading and validation.
/// </summary>
public sealed class ConfigurationLoadResult<T> where T : class
{
    /// <summary>
    /// Gets the loaded configuration instance.
    /// </summary>
    public T? Configuration { get; set; }
    
    /// <summary>
    /// Gets the validation result.
    /// </summary>
    public ValidatorResult? ValidatorResult { get; set; }
    
    /// <summary>
    /// Gets whether the load operation was successful.
    /// </summary>
    public bool IsSuccess { get; set; }
    
    /// <summary>
    /// Gets whether defaults were applied during loading.
    /// </summary>
    public bool LoadedFromDefaults { get; set; }
    
    /// <summary>
    /// Gets the error message if loading failed.
    /// </summary>
    public string? ErrorMessage { get; set; }

    public static ConfigurationLoadResult<T> CreateError(string errorMessage)
    {
        return new ConfigurationLoadResult<T>
        {
            IsSuccess = false,
            ErrorMessage = errorMessage
        };
    }
}

/// <summary>
/// Represents a configuration validation error.
/// </summary>
public sealed class ConfigurationError
{
    /// <summary>
    /// Gets or sets the error message.
    /// </summary>
    public string Message { get; set; } = string.Empty;
    
    /// <summary>
    /// Gets or sets the error code for programmatic handling.
    /// </summary>
    public string ErrorCode { get; set; } = string.Empty;
    
    /// <summary>
    /// Gets or sets the names of the members that caused the error.
    /// </summary>
    public string[] MemberNames { get; set; } = Array.Empty<string>();
}

/// <summary>
/// Represents a configuration validation warning.
/// </summary>
public sealed class ConfigurationWarning
{
    /// <summary>
    /// Gets or sets the warning message.
    /// </summary>
    public string Message { get; set; } = string.Empty;
    
    /// <summary>
    /// Gets or sets the severity of the warning.
    /// </summary>
    public WarningSeverity Severity { get; set; } = WarningSeverity.Medium;
    
    /// <summary>
    /// Gets or sets the names of the members that caused the warning.
    /// </summary>
    public string[] MemberNames { get; set; } = Array.Empty<string>();
}

/// <summary>
/// Severity levels for configuration warnings.
/// </summary>
public enum WarningSeverity
{
    Low = 0,
    Medium = 1,
    High = 2
}

// Marker interfaces for configuration types
public interface IHasMemoryLimits
{
    int MaxMemoryMB { get; }
    int InitialMemoryMB { get; }
}

public interface IHasTimeouts
{
    TimeSpan ConnectionTimeout { get; }
    TimeSpan OperationTimeout { get; }
}

public interface IHasFilePaths
{
    // Marker interface for configurations with file paths
}

public interface IHasNetworkEndpoints
{
    // Marker interface for configurations with network endpoints
}

// Validator interfaces
public interface IConfigurationValidator
{
    Task<ValidatorResult> ValidateAsync(object configuration);
}

public interface IConfigurationValidator<T> where T : class
{
    Task<ValidatorResult> ValidateAsync(T configuration);
}

// Adapter for generic validators
internal sealed class ConfigurationValidatorAdapter<T> : IConfigurationValidator where T : class
{
    private readonly IConfigurationValidator<T> _validator;

    public ConfigurationValidatorAdapter(IConfigurationValidator<T> validator)
    {
        _validator = validator;
    }

    public async Task<ValidatorResult> ValidateAsync(object configuration)
    {
        if (configuration is T typedConfig)
        {
            return await _validator.ValidateAsync(typedConfig);
        }
        
        return ValidatorResult.CreateError($"Invalid configuration type. Expected {typeof(T).Name}, got {configuration?.GetType().Name ?? "null"}");
    }
}