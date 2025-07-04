using FlowEngine.Abstractions.Data;
using FlowEngine.Abstractions.Plugins;
using Microsoft.Extensions.Logging;
using System.Collections.Immutable;

namespace Phase3TemplatePlugin;

/// <summary>
/// COMPONENT 3: Plugin
/// 
/// Demonstrates proper Phase 3 plugin lifecycle management using PluginBase:
/// - Plugin metadata and versioning
/// - Lifecycle state management (Created → Initialized → Running → Stopped → Disposed)
/// - Hot-swapping configuration support
/// - Health monitoring and metrics
/// - Simplified implementation using base class
/// </summary>
public sealed class TemplatePlugin : PluginBase
{
    private TemplatePluginConfiguration? _configuration;

    /// <summary>
    /// Constructor demonstrating proper dependency injection with PluginBase
    /// </summary>
    /// <param name="logger">Logger injected from Core framework</param>
    public TemplatePlugin(ILogger<TemplatePlugin> logger) : base(logger)
    {
    }

    // === Plugin Metadata (PluginBase Overrides) ===

    /// <inheritdoc />
    public override string Name => "Phase 3 Template Plugin";

    /// <inheritdoc />
    public override string Version => "3.0.0";

    /// <inheritdoc />
    public override string Description => "Reference implementation demonstrating correct Phase 3 five-component plugin architecture";

    /// <inheritdoc />
    public override string Author => "FlowEngine Team";

    /// <inheritdoc />
    public override string PluginType => "Source";

    /// <inheritdoc />
    public override bool SupportsHotSwapping => true;

    /// <inheritdoc />
    public override IPluginConfiguration Configuration 
    { 
        get => _configuration ?? throw new InvalidOperationException("Plugin not initialized");
        protected set => _configuration = (TemplatePluginConfiguration)value;
    }

    /// <inheritdoc />
    public override ISchema? OutputSchema
    {
        get
        {
            // Return the output schema defined in configuration, or null if not initialized
            return _configuration?.OutputSchema;
        }
    }

    // Additional plugin capabilities metadata
    public ImmutableArray<string> SupportedCapabilities => ImmutableArray.Create(
        "DataGeneration",
        "ConfigurableOutput",
        "BatchProcessing",
        "ArrayRowOptimization",
        "PerformanceMonitoring"
    );

    // === Base Class Overrides ===

    /// <inheritdoc />
    protected override async Task<PluginInitializationResult> InitializeInternalAsync(
        IPluginConfiguration configuration,
        CancellationToken cancellationToken)
    {
        Logger.LogInformation("Initializing Template Plugin v{Version}", Version);

        if (configuration is not TemplatePluginConfiguration templateConfig)
        {
            return new PluginInitializationResult
            {
                Success = false,
                Message = $"Expected TemplatePluginConfiguration, got {configuration?.GetType().Name ?? "null"}",
                InitializationTime = TimeSpan.Zero,
                Errors = ImmutableArray.Create("Invalid configuration type")
            };
        }

        try
        {
            // Validate configuration using validator
            var validator = new TemplatePluginValidator();
            var validationResult = validator.ValidateConfiguration(templateConfig);

            if (!validationResult.IsValid)
            {
                var errors = validationResult.Errors.Select(e => e.Message).ToArray();
                return new PluginInitializationResult
                {
                    Success = false,
                    Message = "Configuration validation failed",
                    InitializationTime = TimeSpan.Zero,
                    Errors = ImmutableArray.Create(errors)
                };
            }

            // Store configuration
            _configuration = templateConfig;

            Logger.LogInformation(
                "Template Plugin initialized successfully. RowCount: {RowCount}, BatchSize: {BatchSize}, DataType: {DataType}",
                templateConfig.RowCount,
                templateConfig.BatchSize,
                templateConfig.DataType);

            return new PluginInitializationResult
            {
                Success = true,
                Message = "Plugin initialized successfully",
                InitializationTime = TimeSpan.Zero
            };
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to initialize Template Plugin");

            return new PluginInitializationResult
            {
                Success = false,
                Message = $"Initialization failed: {ex.Message}",
                InitializationTime = TimeSpan.Zero,
                Errors = ImmutableArray.Create(ex.Message)
            };
        }
    }

    /// <inheritdoc />
    protected override async Task StartInternalAsync(CancellationToken cancellationToken)
    {
        Logger.LogInformation("Starting Template Plugin");
        // Plugin-specific start logic here
        Logger.LogInformation("Template Plugin started successfully");
        await Task.CompletedTask;
    }

    /// <inheritdoc />
    protected override async Task StopInternalAsync(CancellationToken cancellationToken)
    {
        Logger.LogInformation("Stopping Template Plugin");
        // Plugin-specific stop logic here
        Logger.LogInformation("Template Plugin stopped successfully");
        await Task.CompletedTask;
    }

    // Health checks and metrics handled by base class

    /// <inheritdoc />
    protected override async Task<HotSwapResult> HotSwapInternalAsync(
        IPluginConfiguration newConfiguration,
        CancellationToken cancellationToken)
    {
        Logger.LogInformation("Performing hot-swap for Template Plugin");

        if (newConfiguration is not TemplatePluginConfiguration newTemplateConfig)
        {
            return new HotSwapResult
            {
                Success = false,
                Message = $"Expected TemplatePluginConfiguration, got {newConfiguration?.GetType().Name ?? "null"}",
                SwapTime = TimeSpan.Zero
            };
        }

        try
        {
            // Validate new configuration
            var validator = new TemplatePluginValidator();
            var validationResult = validator.ValidateConfiguration(newTemplateConfig);

            if (!validationResult.IsValid)
            {
                return new HotSwapResult
                {
                    Success = false,
                    Message = "Hot-swap validation failed",
                    SwapTime = TimeSpan.Zero,
                    Errors = validationResult.Errors.Select(e => e.Message).ToImmutableArray()
                };
            }

            // Update configuration atomically
            var oldConfig = _configuration;
            _configuration = newTemplateConfig;

            Logger.LogInformation(
                "Hot-swap completed successfully. RowCount: {OldRowCount} → {NewRowCount}, BatchSize: {OldBatchSize} → {NewBatchSize}",
                oldConfig?.RowCount ?? 0,
                newTemplateConfig.RowCount,
                oldConfig?.BatchSize ?? 0,
                newTemplateConfig.BatchSize);

            return new HotSwapResult
            {
                Success = true,
                Message = "Configuration updated successfully",
                SwapTime = TimeSpan.Zero
            };
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Hot-swap failed for Template Plugin");
            
            return new HotSwapResult
            {
                Success = false,
                Message = $"Hot-swap failed: {ex.Message}",
                SwapTime = TimeSpan.Zero,
                Errors = ImmutableArray.Create(ex.Message)
            };
        }
    }

    // === Component Factory Methods (for framework integration) ===

    /// <summary>
    /// Creates the processor component for this plugin
    /// </summary>
    public IPluginProcessor GetProcessor()
    {
        return new TemplatePluginProcessor();
    }

    /// <summary>
    /// Creates the service component for this plugin
    /// </summary>
    public IPluginService GetService()
    {
        return new TemplatePluginService();
    }

    /// <summary>
    /// Creates the validator component for this plugin
    /// </summary>
    public IPluginValidator GetValidator()
    {
        return new TemplatePluginValidator();
    }

    // Disposal handled by PluginBase
}