# JSON Schema Validation in FlowEngine

**Version**: 1.0.0  
**Framework**: NJsonSchema 11.0.0  
**Last Updated**: July 6, 2025

---

## 🎯 Overview

FlowEngine uses JSON Schema for comprehensive configuration validation, ensuring type safety, business rule enforcement, and developer-friendly error messages. This guide covers everything about implementing and using JSON Schema validation in FlowEngine plugins.

## 📋 Table of Contents

- [Architecture Overview](#architecture-overview)
- [Schema Definition](#schema-definition)
- [Validation Implementation](#validation-implementation)
- [Advanced Patterns](#advanced-patterns)
- [Error Handling](#error-handling)
- [Best Practices](#best-practices)
- [Examples](#examples)

---

## 🏗️ Architecture Overview

### Validation Pipeline

FlowEngine's validation system operates at multiple levels:

```
Plugin Configuration
        ↓
JSON Schema Validation (NJsonSchema)
        ↓
Business Rule Validation
        ↓
Runtime Type Safety
        ↓
Configuration Applied
```

### Core Components

1. **SchemaValidatedPluginValidator**: Base class for schema-aware validators
2. **ValidationError**: Structured error reporting
3. **PluginManifest**: Schema definitions in plugin.json
4. **IPluginConfiguration**: Strongly-typed configuration access

### Integration Points

```csharp
// Base validator with JSON Schema support
public abstract class SchemaValidatedPluginValidator<T> : PluginValidatorBase<T> where T : class, IPlugin
{
    protected abstract string GetEmbeddedSchemaResourceName();
    
    public override async Task<ValidationResult> ValidateConfigurationAsync(
        IPluginConfiguration configuration)
    {
        // 1. JSON Schema validation
        var schemaResult = await ValidateAgainstJsonSchemaAsync(configuration);
        if (!schemaResult.IsValid)
            return schemaResult;
            
        // 2. Business rule validation
        return await ValidateBusinessRulesAsync(configuration);
    }
}
```

---

## 📝 Schema Definition

### Basic Schema Structure

Define schemas in your `plugin.json` manifest:

```json
{
  "name": "MyDataProcessor",
  "version": "1.0.0",
  "configuration": {
    "schema": {
      "$schema": "https://json-schema.org/draft/2020-12/schema",
      "type": "object",
      "title": "MyDataProcessor Configuration",
      "description": "Configuration schema for MyDataProcessor plugin",
      "properties": {
        "connectionString": {
          "type": "string",
          "pattern": "^[^;]+=[^;]+;.*$",
          "description": "Database connection string",
          "examples": [
            "Server=localhost;Database=test;Integrated Security=true"
          ]
        },
        "batchSize": {
          "type": "integer",
          "minimum": 1,
          "maximum": 10000,
          "default": 1000,
          "description": "Number of rows to process in each batch"
        },
        "timeout": {
          "type": "number",
          "minimum": 0.1,
          "maximum": 300.0,
          "default": 30.0,
          "description": "Timeout in seconds for operations"
        },
        "enableRetries": {
          "type": "boolean",
          "default": true,
          "description": "Enable automatic retry on transient failures"
        }
      },
      "required": ["connectionString"],
      "additionalProperties": false
    }
  }
}
```

### Advanced Schema Features

#### Complex Data Types

```json
{
  "properties": {
    "retryPolicy": {
      "type": "object",
      "properties": {
        "maxAttempts": {
          "type": "integer",
          "minimum": 1,
          "maximum": 10,
          "default": 3
        },
        "baseDelay": {
          "type": "string",
          "pattern": "^\\d+(\\.\\d+)?[ms|s|m|h]$",
          "description": "Base delay for exponential backoff (e.g., '1s', '500ms')",
          "default": "1s"
        },
        "maxDelay": {
          "type": "string", 
          "pattern": "^\\d+(\\.\\d+)?[ms|s|m|h]$",
          "default": "30s"
        }
      },
      "required": ["maxAttempts"],
      "additionalProperties": false
    },
    "fieldMappings": {
      "type": "array",
      "items": {
        "type": "object",
        "properties": {
          "source": {
            "type": "string",
            "minLength": 1,
            "description": "Source field name"
          },
          "target": {
            "type": "string", 
            "minLength": 1,
            "description": "Target field name"
          },
          "transform": {
            "type": "string",
            "enum": ["none", "uppercase", "lowercase", "trim", "custom"],
            "default": "none"
          }
        },
        "required": ["source", "target"],
        "additionalProperties": false
      },
      "minItems": 1,
      "uniqueItems": true
    }
  }
}
```

#### Conditional Schemas

```json
{
  "properties": {
    "mode": {
      "type": "string",
      "enum": ["batch", "streaming", "realtime"]
    },
    "batchConfig": {
      "type": "object",
      "properties": {
        "size": { "type": "integer", "minimum": 1 },
        "timeout": { "type": "number", "minimum": 0 }
      }
    },
    "streamingConfig": {
      "type": "object", 
      "properties": {
        "bufferSize": { "type": "integer", "minimum": 1 },
        "flushInterval": { "type": "number", "minimum": 0 }
      }
    }
  },
  "if": {
    "properties": { "mode": { "const": "batch" } }
  },
  "then": {
    "required": ["batchConfig"],
    "properties": {
      "streamingConfig": false
    }
  },
  "else": {
    "if": {
      "properties": { "mode": { "const": "streaming" } }
    },
    "then": {
      "required": ["streamingConfig"],
      "properties": {
        "batchConfig": false  
      }
    }
  }
}
```

#### Custom Formats

```json
{
  "properties": {
    "cronExpression": {
      "type": "string",
      "format": "cron",
      "description": "Cron expression for scheduling",
      "examples": ["0 */5 * * *", "0 0 12 * * ?"]
    },
    "emailAddress": {
      "type": "string",
      "format": "email",
      "description": "Notification email address"
    },
    "ipAddress": {
      "type": "string",
      "format": "ipv4",
      "description": "Server IP address"
    },
    "timestamp": {
      "type": "string",
      "format": "date-time",
      "description": "ISO 8601 timestamp"
    }
  }
}
```

---

## ⚙️ Validation Implementation

### Plugin Validator Implementation

```csharp
using FlowEngine.Abstractions.Plugins;
using FlowEngine.Core.Validation;
using NJsonSchema;

public class MyDataProcessorValidator : SchemaValidatedPluginValidator<MyDataProcessor>
{
    protected override string GetEmbeddedSchemaResourceName()
    {
        return "MyDataProcessor.Configuration.schema.json";
    }

    protected override async Task<ValidationResult> ValidateBusinessRulesAsync(
        IPluginConfiguration configuration)
    {
        var errors = new List<ValidationError>();
        
        // Custom business rule: batch size must be compatible with timeout
        if (configuration.TryGetProperty<int>("batchSize", out var batchSize) &&
            configuration.TryGetProperty<double>("timeout", out var timeout))
        {
            var expectedProcessingTime = batchSize * 0.001; // 1ms per row estimate
            if (expectedProcessingTime > timeout)
            {
                errors.Add(new ValidationError
                {
                    PropertyPath = "batchSize",
                    ErrorMessage = $"Batch size {batchSize} may exceed timeout {timeout}s. " +
                                 $"Estimated processing time: {expectedProcessingTime:F2}s",
                    ErrorCode = "BATCH_SIZE_TIMEOUT_MISMATCH",
                    Severity = ValidationSeverity.Warning
                });
            }
        }
        
        // Validate connection string format
        if (configuration.TryGetProperty<string>("connectionString", out var connStr))
        {
            var connectionValidation = await ValidateConnectionStringAsync(connStr);
            if (!connectionValidation.IsValid)
            {
                errors.Add(new ValidationError
                {
                    PropertyPath = "connectionString",
                    ErrorMessage = connectionValidation.ErrorMessage,
                    ErrorCode = "INVALID_CONNECTION_STRING",
                    Severity = ValidationSeverity.Error
                });
            }
        }
        
        return new ValidationResult
        {
            IsValid = !errors.Any(e => e.Severity == ValidationSeverity.Error),
            Errors = errors
        };
    }
    
    private async Task<(bool IsValid, string? ErrorMessage)> ValidateConnectionStringAsync(string connectionString)
    {
        try
        {
            // Implement actual connection validation
            using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync();
            return (true, null);
        }
        catch (Exception ex)
        {
            return (false, $"Connection failed: {ex.Message}");
        }
    }
}
```

### Embedded Schema Resources

Add schema files as embedded resources:

```xml
<!-- In your .csproj file -->
<ItemGroup>
  <EmbeddedResource Include="Configuration\schema.json" />
</ItemGroup>
```

```csharp
// Access embedded schema
private async Task<JsonSchema> LoadEmbeddedSchemaAsync()
{
    var resourceName = GetEmbeddedSchemaResourceName();
    var assembly = GetType().Assembly;
    
    await using var stream = assembly.GetManifestResourceStream(resourceName);
    if (stream == null)
        throw new InvalidOperationException($"Schema resource '{resourceName}' not found");
        
    using var reader = new StreamReader(stream);
    var schemaJson = await reader.ReadToEndAsync();
    
    return await JsonSchema.FromJsonAsync(schemaJson);
}
```

### Runtime Validation

```csharp
public class MyDataProcessor : PluginBase, ITransformPlugin
{
    private MyDataProcessorConfig? _config;
    
    public override async Task InitializeAsync(IPluginConfiguration configuration)
    {
        await base.InitializeAsync(configuration);
        
        // Validate and parse configuration
        _config = await ParseAndValidateConfigurationAsync(configuration);
    }
    
    private async Task<MyDataProcessorConfig> ParseAndValidateConfigurationAsync(
        IPluginConfiguration configuration)
    {
        // JSON Schema validation happens automatically via validator
        // Here we focus on strongly-typed parsing
        
        var config = new MyDataProcessorConfig
        {
            ConnectionString = configuration.GetProperty<string>("connectionString"),
            BatchSize = configuration.TryGetProperty<int>("batchSize", out var batchSize) ? batchSize : 1000,
            Timeout = TimeSpan.FromSeconds(
                configuration.TryGetProperty<double>("timeout", out var timeout) ? timeout : 30.0),
            EnableRetries = configuration.TryGetProperty<bool>("enableRetries", out var retries) ? retries : true
        };
        
        // Additional validation
        if (config.BatchSize <= 0)
            throw new PluginConfigurationException("batchSize must be positive");
            
        if (config.Timeout <= TimeSpan.Zero)
            throw new PluginConfigurationException("timeout must be positive");
        
        return config;
    }
}

// Strongly-typed configuration class
public class MyDataProcessorConfig
{
    public required string ConnectionString { get; init; }
    public int BatchSize { get; init; } = 1000;
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(30);
    public bool EnableRetries { get; init; } = true;
}
```

---

## 🔧 Advanced Patterns

### Schema Composition

```json
{
  "$defs": {
    "connection": {
      "type": "object",
      "properties": {
        "server": { "type": "string" },
        "port": { "type": "integer", "minimum": 1, "maximum": 65535 },
        "database": { "type": "string" },
        "credentials": {
          "$ref": "#/$defs/credentials"
        }
      },
      "required": ["server", "database"]
    },
    "credentials": {
      "type": "object",
      "oneOf": [
        {
          "properties": {
            "type": { "const": "integrated" }
          },
          "required": ["type"],
          "additionalProperties": false
        },
        {
          "properties": {
            "type": { "const": "username_password" },
            "username": { "type": "string", "minLength": 1 },
            "password": { "type": "string", "minLength": 1 }
          },
          "required": ["type", "username", "password"],
          "additionalProperties": false
        }
      ]
    }
  },
  "properties": {
    "primaryConnection": {
      "$ref": "#/$defs/connection"
    },
    "fallbackConnection": {
      "$ref": "#/$defs/connection"
    }
  }
}
```

### Dynamic Schema Generation

```csharp
public class DynamicSchemaGenerator
{
    public static JsonSchema GeneratePluginConfigurationSchema(Type pluginType)
    {
        var schema = new JsonSchema
        {
            Type = JsonObjectType.Object,
            Title = $"{pluginType.Name} Configuration",
            AdditionalPropertiesSchema = null // Strict validation
        };
        
        // Analyze plugin type for configuration attributes
        var configProperties = GetConfigurationProperties(pluginType);
        
        foreach (var prop in configProperties)
        {
            var propSchema = GeneratePropertySchema(prop);
            schema.Properties[prop.Name.ToCamelCase()] = propSchema;
            
            if (prop.IsRequired)
                schema.RequiredProperties.Add(prop.Name.ToCamelCase());
        }
        
        return schema;
    }
    
    private static JsonSchemaProperty GeneratePropertySchema(ConfigurationProperty prop)
    {
        var schema = new JsonSchemaProperty();
        
        // Map .NET types to JSON Schema types
        schema.Type = prop.Type switch
        {
            Type t when t == typeof(string) => JsonObjectType.String,
            Type t when t == typeof(int) => JsonObjectType.Integer,
            Type t when t == typeof(double) => JsonObjectType.Number,
            Type t when t == typeof(bool) => JsonObjectType.Boolean,
            Type t when t.IsArray => JsonObjectType.Array,
            Type t when typeof(IDictionary).IsAssignableFrom(t) => JsonObjectType.Object,
            _ => JsonObjectType.Object
        };
        
        // Apply validation attributes
        if (prop.Validation != null)
        {
            ApplyValidationRules(schema, prop.Validation);
        }
        
        return schema;
    }
}
```

### Schema Versioning

```json
{
  "$schema": "https://json-schema.org/draft/2020-12/schema",
  "$id": "https://flowengine.dev/schemas/plugins/mydataprocessor/v1.0.0.json",
  "version": "1.0.0",
  "type": "object",
  "properties": {
    "schemaVersion": {
      "type": "string",
      "const": "1.0.0",
      "description": "Configuration schema version"
    }
  },
  "required": ["schemaVersion"]
}
```

```csharp
public class VersionedSchemaValidator
{
    private readonly Dictionary<string, JsonSchema> _schemas = new();
    
    public async Task<ValidationResult> ValidateAsync(
        IPluginConfiguration configuration, 
        string requestedVersion)
    {
        var schema = await GetSchemaForVersionAsync(requestedVersion);
        return await ValidateAgainstSchemaAsync(configuration, schema);
    }
    
    private async Task<JsonSchema> GetSchemaForVersionAsync(string version)
    {
        if (_schemas.TryGetValue(version, out var schema))
            return schema;
            
        // Load schema for specific version
        var schemaPath = $"Schemas/v{version}/configuration.json";
        schema = await LoadSchemaFromResourceAsync(schemaPath);
        
        _schemas[version] = schema;
        return schema;
    }
}
```

---

## 🚨 Error Handling

### Structured Error Reporting

```csharp
public class ValidationError
{
    public required string PropertyPath { get; init; }
    public required string ErrorMessage { get; init; }
    public required string ErrorCode { get; init; }
    public ValidationSeverity Severity { get; init; } = ValidationSeverity.Error;
    public object? AttemptedValue { get; init; }
    public string[]? AllowedValues { get; init; }
    public Dictionary<string, object>? Context { get; init; }
}

public enum ValidationSeverity
{
    Info,
    Warning, 
    Error,
    Critical
}
```

### User-Friendly Error Messages

```csharp
private ValidationError CreateUserFriendlyError(ValidationError schemaError)
{
    return schemaError.ErrorCode switch
    {
        "type" => new ValidationError
        {
            PropertyPath = schemaError.PropertyPath,
            ErrorCode = "INVALID_TYPE",
            ErrorMessage = $"The value '{schemaError.AttemptedValue}' is not valid for {schemaError.PropertyPath}. " +
                          $"Expected a {GetExpectedTypeDescription(schemaError)}.",
            Severity = ValidationSeverity.Error,
            Context = new Dictionary<string, object>
            {
                ["expectedType"] = GetExpectedType(schemaError),
                ["providedType"] = schemaError.AttemptedValue?.GetType().Name ?? "null"
            }
        },
        
        "minimum" => new ValidationError
        {
            PropertyPath = schemaError.PropertyPath,
            ErrorCode = "VALUE_TOO_SMALL", 
            ErrorMessage = $"The value {schemaError.AttemptedValue} for {schemaError.PropertyPath} " +
                          $"is too small. Minimum allowed value is {GetMinimumValue(schemaError)}.",
            Severity = ValidationSeverity.Error
        },
        
        "required" => new ValidationError
        {
            PropertyPath = schemaError.PropertyPath,
            ErrorCode = "REQUIRED_PROPERTY_MISSING",
            ErrorMessage = $"The required property '{schemaError.PropertyPath}' is missing. " +
                          $"This property is required for proper plugin operation.",
            Severity = ValidationSeverity.Error
        },
        
        _ => schemaError
    };
}
```

### Error Aggregation and Reporting

```csharp
public class ValidationResultFormatter
{
    public string FormatValidationResult(ValidationResult result)
    {
        if (result.IsValid)
            return "✅ Configuration is valid";
            
        var sb = new StringBuilder();
        sb.AppendLine("❌ Configuration validation failed:");
        sb.AppendLine();
        
        // Group errors by severity
        var errorGroups = result.Errors.GroupBy(e => e.Severity);
        
        foreach (var group in errorGroups.OrderByDescending(g => g.Key))
        {
            var icon = group.Key switch
            {
                ValidationSeverity.Critical => "🔴",
                ValidationSeverity.Error => "❌", 
                ValidationSeverity.Warning => "⚠️",
                ValidationSeverity.Info => "ℹ️",
                _ => "•"
            };
            
            sb.AppendLine($"{icon} {group.Key} ({group.Count()}):");
            
            foreach (var error in group)
            {
                sb.AppendLine($"  • {error.PropertyPath}: {error.ErrorMessage}");
                
                if (error.AllowedValues?.Any() == true)
                {
                    sb.AppendLine($"    Allowed values: {string.Join(", ", error.AllowedValues)}");
                }
            }
            
            sb.AppendLine();
        }
        
        return sb.ToString();
    }
}
```

---

## ✅ Best Practices

### Schema Design

1. **Be Explicit and Strict**:
```json
{
  "additionalProperties": false,  // Prevent unknown properties
  "required": ["connectionString"], // Clearly specify requirements
  "properties": {
    "batchSize": {
      "type": "integer",
      "minimum": 1,
      "maximum": 10000,  // Set realistic bounds
      "default": 1000
    }
  }
}
```

2. **Provide Clear Descriptions**:
```json
{
  "properties": {
    "retryCount": {
      "type": "integer",
      "minimum": 0,
      "maximum": 10,
      "default": 3,
      "description": "Number of retry attempts for transient failures. Set to 0 to disable retries.",
      "examples": [0, 3, 5]
    }
  }
}
```

3. **Use Semantic Validation**:
```json
{
  "properties": {
    "connectionString": {
      "type": "string",
      "pattern": "^Server=.+;Database=.+;.+$",
      "description": "SQL Server connection string"
    },
    "timeout": {
      "type": "string",
      "pattern": "^\\d+(\\.\\d+)?[ms|s|m|h]$",
      "description": "Timeout duration (e.g., '30s', '5m', '1h')"
    }
  }
}
```

### Validation Implementation

1. **Separate Schema and Business Logic**:
```csharp
// JSON Schema handles structure and types
// Business logic validation handles domain rules
protected override async Task<ValidationResult> ValidateBusinessRulesAsync(
    IPluginConfiguration configuration)
{
    var errors = new List<ValidationError>();
    
    // Domain-specific validation
    if (await IsConnectionStringReachableAsync(configuration.GetProperty<string>("connectionString")))
    {
        errors.Add(CreateConnectionError());
    }
    
    return new ValidationResult { IsValid = !errors.Any(), Errors = errors };
}
```

2. **Fail Fast with Clear Messages**:
```csharp
public override async Task InitializeAsync(IPluginConfiguration configuration)
{
    // Validate configuration before any processing
    var validationResult = await ValidateConfigurationAsync(configuration);
    if (!validationResult.IsValid)
    {
        var errorMessage = FormatValidationErrors(validationResult.Errors);
        throw new PluginConfigurationException(errorMessage);
    }
    
    await base.InitializeAsync(configuration);
}
```

3. **Cache Expensive Validations**:
```csharp
private readonly ConcurrentDictionary<string, bool> _connectionValidityCache = new();

private async Task<bool> IsConnectionValidAsync(string connectionString)
{
    return await _connectionValidityCache.GetOrAddAsync(
        connectionString,
        async connStr => await TestConnectionAsync(connStr));
}
```

### Performance Considerations

1. **Lazy Schema Loading**:
```csharp
private JsonSchema? _schema;
private readonly SemaphoreSlim _schemaLoadSemaphore = new(1, 1);

private async Task<JsonSchema> GetSchemaAsync()
{
    if (_schema != null)
        return _schema;
        
    await _schemaLoadSemaphore.WaitAsync();
    try
    {
        return _schema ??= await LoadSchemaFromResourceAsync();
    }
    finally
    {
        _schemaLoadSemaphore.Release();
    }
}
```

2. **Validation Result Caching**:
```csharp
private readonly MemoryCache _validationCache = new(new MemoryCacheOptions
{
    SizeLimit = 1000,
    CompactionPercentage = 0.2
});

public async Task<ValidationResult> ValidateAsync(IPluginConfiguration configuration)
{
    var cacheKey = GenerateConfigurationHash(configuration);
    
    if (_validationCache.TryGetValue(cacheKey, out ValidationResult? cached))
        return cached;
        
    var result = await PerformValidationAsync(configuration);
    
    _validationCache.Set(cacheKey, result, TimeSpan.FromMinutes(5));
    return result;
}
```

---

## 📚 Examples

### Complete Plugin with Validation

```csharp
// Plugin implementation
public class DatabaseSourcePlugin : PluginBase, ISourcePlugin
{
    private DatabaseSourceConfig? _config;
    
    public override async Task InitializeAsync(IPluginConfiguration configuration)
    {
        await base.InitializeAsync(configuration);
        _config = ParseConfiguration(configuration);
    }
    
    private static DatabaseSourceConfig ParseConfiguration(IPluginConfiguration config)
    {
        return new DatabaseSourceConfig
        {
            ConnectionString = config.GetProperty<string>("connectionString"),
            Query = config.GetProperty<string>("query"),
            BatchSize = config.TryGetProperty<int>("batchSize", out var size) ? size : 1000,
            Timeout = TimeSpan.FromSeconds(
                config.TryGetProperty<double>("timeoutSeconds", out var timeout) ? timeout : 30),
            Parameters = config.TryGetProperty<Dictionary<string, object>>("parameters", out var p) ? 
                        p : new Dictionary<string, object>()
        };
    }
    
    // Implementation...
}

// Configuration class
public class DatabaseSourceConfig
{
    public required string ConnectionString { get; init; }
    public required string Query { get; init; }
    public int BatchSize { get; init; } = 1000;
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(30);
    public Dictionary<string, object> Parameters { get; init; } = new();
}

// Validator
public class DatabaseSourceValidator : SchemaValidatedPluginValidator<DatabaseSourcePlugin>
{
    protected override string GetEmbeddedSchemaResourceName() =>
        "DatabaseSourcePlugin.Configuration.schema.json";
        
    protected override async Task<ValidationResult> ValidateBusinessRulesAsync(
        IPluginConfiguration configuration)
    {
        var errors = new List<ValidationError>();
        
        // Validate connection string
        var connectionString = configuration.GetProperty<string>("connectionString");
        if (!await CanConnectToDatabaseAsync(connectionString))
        {
            errors.Add(new ValidationError
            {
                PropertyPath = "connectionString",
                ErrorMessage = "Unable to connect to database with provided connection string",
                ErrorCode = "DATABASE_CONNECTION_FAILED",
                Severity = ValidationSeverity.Error
            });
        }
        
        // Validate SQL query
        var query = configuration.GetProperty<string>("query");
        var queryValidation = ValidateSqlQuery(query);
        if (!queryValidation.IsValid)
        {
            errors.Add(new ValidationError
            {
                PropertyPath = "query",
                ErrorMessage = queryValidation.ErrorMessage,
                ErrorCode = "INVALID_SQL_QUERY",
                Severity = ValidationSeverity.Error
            });
        }
        
        return new ValidationResult 
        { 
            IsValid = !errors.Any(e => e.Severity == ValidationSeverity.Error), 
            Errors = errors 
        };
    }
}
```

### Schema Definition

```json
{
  "$schema": "https://json-schema.org/draft/2020-12/schema",
  "type": "object",
  "title": "Database Source Plugin Configuration",
  "description": "Configuration for reading data from SQL databases",
  "properties": {
    "connectionString": {
      "type": "string",
      "pattern": "^.*(Server|Data Source)=.+;.*(Database|Initial Catalog)=.+;.*$",
      "description": "SQL Server connection string",
      "examples": [
        "Server=localhost;Database=MyDB;Integrated Security=true",
        "Server=localhost;Database=MyDB;User Id=user;Password=pass;"
      ]
    },
    "query": {
      "type": "string",
      "minLength": 10,
      "pattern": "^\\s*SELECT\\s+.+\\s+FROM\\s+.+$",
      "description": "SQL SELECT query to execute",
      "examples": [
        "SELECT * FROM Users WHERE CreatedDate >= @StartDate",
        "SELECT Id, Name, Email FROM Customers ORDER BY Id"
      ]
    },
    "batchSize": {
      "type": "integer",
      "minimum": 1,
      "maximum": 10000,
      "default": 1000,
      "description": "Number of rows to fetch in each batch"
    },
    "timeoutSeconds": {
      "type": "number",
      "minimum": 1,
      "maximum": 300,
      "default": 30,
      "description": "Query timeout in seconds"
    },
    "parameters": {
      "type": "object",
      "patternProperties": {
        "^@[a-zA-Z][a-zA-Z0-9_]*$": {
          "oneOf": [
            { "type": "string" },
            { "type": "number" },
            { "type": "boolean" },
            { "type": "null" }
          ]
        }
      },
      "additionalProperties": false,
      "description": "SQL parameters for the query",
      "examples": [
        { "@StartDate": "2024-01-01", "@MaxRecords": 1000 }
      ]
    }
  },
  "required": ["connectionString", "query"],
  "additionalProperties": false
}
```

---

## 🔗 References

- [JSON Schema Specification](https://json-schema.org/)
- [NJsonSchema Documentation](https://github.com/RicoSuter/NJsonSchema)
- [FlowEngine Plugin Developer Guide](./Plugin-Developer-Guide.md)
- [FlowEngine Core Architecture](./Core-Architecture.md)

---

**Validation Made Simple and Powerful!** ✨