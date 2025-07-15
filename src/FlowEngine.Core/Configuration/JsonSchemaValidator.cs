using NJsonSchema;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using ValidationResult = FlowEngine.Abstractions.ValidationResult;

namespace FlowEngine.Core.Configuration;

/// <summary>
/// Provides JSON schema validation for plugin configurations.
/// Uses NJsonSchema library for comprehensive schema validation with detailed error reporting.
/// </summary>
public class JsonSchemaValidator
{
    private readonly ILogger<JsonSchemaValidator> _logger;
    private readonly Dictionary<string, JsonSchema> _compiledSchemas = new();
    private readonly object _cacheLock = new();

    /// <summary>
    /// Initializes a new instance of the JsonSchemaValidator.
    /// </summary>
    /// <param name="logger">Logger for diagnostic information</param>
    public JsonSchemaValidator(ILogger<JsonSchemaValidator> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Validates configuration data against a JSON schema.
    /// Caches compiled schemas for performance.
    /// </summary>
    /// <param name="configurationData">The configuration data to validate (as dictionary)</param>
    /// <param name="jsonSchemaString">The JSON schema to validate against</param>
    /// <param name="pluginTypeName">Plugin type name for logging and error context</param>
    /// <returns>Validation result with success status and detailed error messages</returns>
    public async Task<ValidationResult> ValidateAsync(
        IDictionary<string, object> configurationData, 
        string jsonSchemaString, 
        string pluginTypeName)
    {
        try
        {
            // Get or compile schema with caching
            var schema = await GetOrCompileSchemaAsync(jsonSchemaString, pluginTypeName);
            
            // Convert configuration data to JSON for validation
            var configJson = ConvertToJson(configurationData);
            
            // Validate against schema
            var validationErrors = schema.Validate(configJson);
            
            if (!validationErrors.Any())
            {
                _logger.LogDebug("Configuration validation passed for plugin type: {PluginType}", pluginTypeName);
                return ValidationResult.Success();
            }

            // Convert schema validation errors to our format
            var errors = validationErrors.Select(error => FormatValidationError(error)).ToList();
            
            _logger.LogWarning("Configuration validation failed for plugin type {PluginType}: {ErrorCount} errors", 
                pluginTypeName, errors.Count);
            
            return ValidationResult.Failure(errors.ToArray());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "JSON schema validation failed for plugin type: {PluginType}", pluginTypeName);
            return ValidationResult.Failure(new[] { $"Schema validation error: {ex.Message}" });
        }
    }

    /// <summary>
    /// Validates that a JSON schema string is valid and well-formed.
    /// Used to validate plugin-provided schemas during provider registration.
    /// </summary>
    /// <param name="jsonSchemaString">The JSON schema string to validate</param>
    /// <returns>Validation result indicating if the schema is valid</returns>
    public async Task<ValidationResult> ValidateSchemaAsync(string jsonSchemaString)
    {
        try
        {
            await JsonSchema.FromJsonAsync(jsonSchemaString);
            return ValidationResult.Success();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Invalid JSON schema provided");
            return ValidationResult.Failure(new[] { $"Invalid JSON schema: {ex.Message}" });
        }
    }

    /// <summary>
    /// Gets or compiles a JSON schema with caching for performance.
    /// </summary>
    private async Task<JsonSchema> GetOrCompileSchemaAsync(string jsonSchemaString, string pluginTypeName)
    {
        var cacheKey = $"{pluginTypeName}:{jsonSchemaString.GetHashCode()}";
        
        lock (_cacheLock)
        {
            if (_compiledSchemas.TryGetValue(cacheKey, out var cachedSchema))
            {
                return cachedSchema;
            }
        }

        // Compile schema outside of lock
        var schema = await JsonSchema.FromJsonAsync(jsonSchemaString);
        
        lock (_cacheLock)
        {
            // Double-check pattern to avoid race conditions
            if (!_compiledSchemas.ContainsKey(cacheKey))
            {
                _compiledSchemas[cacheKey] = schema;
                _logger.LogDebug("Compiled and cached JSON schema for plugin type: {PluginType}", pluginTypeName);
            }
            return _compiledSchemas[cacheKey];
        }
    }

    /// <summary>
    /// Converts configuration dictionary to JSON string for validation.
    /// </summary>
    private static string ConvertToJson(IDictionary<string, object> configurationData)
    {
        try
        {
            return JsonConvert.SerializeObject(configurationData, Formatting.None);
        }
        catch (Exception ex)
        {
            throw new ArgumentException($"Failed to serialize configuration data to JSON: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Formats NJsonSchema validation errors into user-friendly messages.
    /// </summary>
    private static string FormatValidationError(NJsonSchema.Validation.ValidationError error)
    {
        var path = string.IsNullOrEmpty(error.Path) ? "root" : error.Path;
        
        // Simplified error formatting that works with actual NJsonSchema API
        if (!string.IsNullOrEmpty(error.Property))
        {
            return $"Validation error for property '{error.Property}' at {path}: {error.Kind}";
        }
        
        return $"Validation error at {path}: {error.Kind}";
    }

    /// <summary>
    /// Clears the schema cache. Useful for testing or memory management.
    /// </summary>
    public void ClearCache()
    {
        lock (_cacheLock)
        {
            _compiledSchemas.Clear();
            _logger.LogDebug("JSON schema cache cleared");
        }
    }

    /// <summary>
    /// Gets cache statistics for monitoring and diagnostics.
    /// </summary>
    public (int CachedSchemas, double CacheHitRate) GetCacheStatistics()
    {
        lock (_cacheLock)
        {
            return (_compiledSchemas.Count, 0.0); // TODO: Implement hit rate tracking if needed
        }
    }
}