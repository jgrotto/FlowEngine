using FlowEngine.Abstractions.Data;
using FlowEngine.Abstractions.Plugins;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using System.Collections.Immutable;

namespace FlowEngine.Core.Plugins.Sources;

/// <summary>
/// Main plugin class for DelimitedSource that manages lifecycle and metadata.
/// Implements the Plugin component of the five-component architecture pattern.
/// </summary>
public sealed class DelimitedSourcePlugin : IPlugin
{
    private readonly ILogger<DelimitedSourcePlugin> _logger;
    private DelimitedSourceConfiguration? _configuration;
    private DelimitedSourceProcessor? _processor;
    private DelimitedSourceService? _service;
    private bool _isInitialized;

    /// <summary>
    /// Gets the plugin metadata.
    /// </summary>
    public IPluginMetadata Metadata { get; } = new PluginMetadata
    {
        Id = "delimited-source",
        Name = "DelimitedSource",
        Version = "3.0.0",
        Author = "FlowEngine Team",
        Description = "High-performance delimited file source plugin with ArrayRow optimization",
        Category = PluginCategory.Source,
        Tags = ImmutableList.Create("source", "csv", "delimited", "file", "performance"),
        MinimumFrameworkVersion = "3.0.0",
        Dependencies = ImmutableList<string>.Empty
    };

    /// <summary>
    /// Gets the current plugin state.
    /// </summary>
    public PluginState State { get; private set; } = PluginState.Stopped;

    /// <summary>
    /// Gets whether this plugin supports hot-swapping configuration updates.
    /// </summary>
    public bool SupportsHotSwapping => true;

    /// <summary>
    /// Gets the current health status of the plugin.
    /// </summary>
    public PluginHealth Health { get; private set; } = PluginHealth.Unknown;

    /// <summary>
    /// Initializes a new instance of the DelimitedSourcePlugin class.
    /// </summary>
    /// <param name="logger">Logger for plugin operations</param>
    public DelimitedSourcePlugin(ILogger<DelimitedSourcePlugin> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Initializes the plugin with configuration.
    /// </summary>
    /// <param name="configuration">Plugin configuration</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Task representing the initialization operation</returns>
    public async Task InitializeAsync(IPluginConfiguration configuration, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Initializing DelimitedSource plugin");

        try
        {
            // Validate configuration type
            if (configuration is not DelimitedSourceConfiguration delimitedConfig)
            {
                throw new ArgumentException("Invalid configuration type. Expected DelimitedSourceConfiguration.");
            }

            // Validate configuration
            var validator = new DelimitedSourceValidator(_logger);
            var validationResult = await validator.ValidateAsync(delimitedConfig, cancellationToken);
            
            if (!validationResult.IsValid)
            {
                var errors = string.Join("; ", validationResult.Errors.Select(e => e.Message));
                throw new InvalidOperationException($"Configuration validation failed: {errors}");
            }

            // Store configuration
            _configuration = delimitedConfig;

            // Create service and processor
            _service = new DelimitedSourceService(_logger);
            _processor = new DelimitedSourceProcessor(_configuration, _service, _logger);

            await _processor.InitializeAsync(cancellationToken);

            _isInitialized = true;
            Health = PluginHealth.Healthy;

            _logger.LogInformation("DelimitedSource plugin initialized successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize DelimitedSource plugin");
            Health = PluginHealth.Unhealthy;
            throw;
        }
    }

    /// <summary>
    /// Starts the plugin and begins data processing.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Task representing the start operation</returns>
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Starting DelimitedSource plugin");

        if (!_isInitialized || _processor == null)
        {
            throw new InvalidOperationException("Plugin not initialized");
        }

        State = PluginState.Starting;
        
        try
        {
            await _processor.StartAsync(cancellationToken);
            State = PluginState.Running;
            Health = PluginHealth.Healthy;

            _logger.LogInformation("DelimitedSource plugin started successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start DelimitedSource plugin");
            State = PluginState.Faulted;
            Health = PluginHealth.Unhealthy;
            throw;
        }
    }

    /// <summary>
    /// Stops the plugin and halts data processing.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Task representing the stop operation</returns>
    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Stopping DelimitedSource plugin");

        if (_processor == null)
        {
            State = PluginState.Stopped;
            return;
        }

        State = PluginState.Stopping;

        try
        {
            await _processor.StopAsync(cancellationToken);
            State = PluginState.Stopped;
            Health = PluginHealth.Healthy;

            _logger.LogInformation("DelimitedSource plugin stopped successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error stopping DelimitedSource plugin");
            State = PluginState.Faulted;
            Health = PluginHealth.Unhealthy;
            throw;
        }
    }

    /// <summary>
    /// Updates the plugin configuration with hot-swapping support.
    /// </summary>
    /// <param name="configuration">New configuration</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Task representing the update operation</returns>
    public async Task UpdateConfigurationAsync(IPluginConfiguration configuration, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Updating DelimitedSource plugin configuration");

        try
        {
            if (configuration is not DelimitedSourceConfiguration newConfig)
            {
                throw new ArgumentException("Invalid configuration type");
            }

            // Validate new configuration
            var validator = new DelimitedSourceValidator(_logger);
            var validationResult = await validator.ValidateAsync(newConfig, cancellationToken);
            
            if (!validationResult.IsValid)
            {
                var errors = string.Join("; ", validationResult.Errors.Select(e => e.Message));
                throw new InvalidOperationException($"Configuration validation failed: {errors}");
            }

            // Check if schema-affecting changes require restart
            bool requiresRestart = _configuration != null && (
                _configuration.FilePath != newConfig.FilePath ||
                _configuration.Delimiter != newConfig.Delimiter ||
                _configuration.HasHeader != newConfig.HasHeader ||
                _configuration.Encoding != newConfig.Encoding
            );

            if (requiresRestart && State == PluginState.Running)
            {
                _logger.LogInformation("Configuration changes require restart");
                await StopAsync(cancellationToken);
                _configuration = newConfig;
                await InitializeAsync(newConfig, cancellationToken);
                await StartAsync(cancellationToken);
            }
            else
            {
                // Hot-swap compatible changes
                _configuration = newConfig;
                _logger.LogInformation("Configuration updated via hot-swap");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update DelimitedSource plugin configuration");
            Health = PluginHealth.Unhealthy;
            throw;
        }
    }

    /// <summary>
    /// Validates compatibility with another plugin.
    /// </summary>
    /// <param name="other">Other plugin to check compatibility</param>
    /// <returns>Task representing the compatibility validation</returns>
    public async Task<bool> ValidateCompatibilityAsync(IPlugin other)
    {
        // Source plugins are typically compatible with transform and sink plugins
        if (other.Metadata.Category == PluginCategory.Transform || 
            other.Metadata.Category == PluginCategory.Sink)
        {
            return true;
        }

        return false;
    }

    /// <summary>
    /// Checks the current health status of the plugin.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Task representing the health check</returns>
    public async Task<PluginHealth> CheckHealthAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            // Basic health checks
            if (!_isInitialized)
            {
                Health = PluginHealth.Unhealthy;
                return Health;
            }

            // Check file accessibility
            if (_configuration != null)
            {
                if (!File.Exists(_configuration.FilePath))
                {
                    Health = PluginHealth.Unhealthy;
                    return Health;
                }

                // Test file access
                using var stream = File.OpenRead(_configuration.FilePath);
                Health = PluginHealth.Healthy;
            }

            return Health;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during health check");
            Health = PluginHealth.Unhealthy;
            return Health;
        }
    }

    /// <summary>
    /// Gets the current plugin configuration.
    /// </summary>
    /// <returns>Current configuration</returns>
    public IPluginConfiguration GetConfiguration()
    {
        return _configuration ?? throw new InvalidOperationException("Plugin not initialized");
    }

    /// <summary>
    /// Disposes the plugin and releases resources.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        _logger.LogInformation("Disposing DelimitedSource plugin");

        if (State == PluginState.Running)
        {
            await StopAsync();
        }

        if (_processor != null)
        {
            await _processor.DisposeAsync();
        }

        _service?.Dispose();
        _isInitialized = false;
        State = PluginState.Disposed;

        GC.SuppressFinalize(this);
    }
}