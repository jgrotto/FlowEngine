using FlowEngine.Abstractions.Data;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace FlowEngine.Abstractions.Plugins;

/// <summary>
/// Abstract base class for plugin processors implementing orchestration and performance monitoring.
/// Provides common functionality for data processing coordination between framework and services.
/// </summary>
public abstract class PluginProcessorBase : IPluginProcessor
{
    private readonly ILogger _logger;
    private PluginState _state = PluginState.Created;
    private readonly object _stateLock = new();
    private bool _disposed;
    private ProcessorMetrics _metrics = new();

    /// <summary>
    /// Initializes a new instance of the PluginProcessorBase class.
    /// </summary>
    /// <param name="logger">Logger instance for this processor</param>
    protected PluginProcessorBase(ILogger logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        Id = Guid.NewGuid().ToString();
    }

    /// <inheritdoc />
    public virtual string Id { get; protected set; }

    /// <inheritdoc />
    public PluginState State
    {
        get
        {
            lock (_stateLock)
            {
                return _state;
            }
        }
        protected set
        {
            PluginState previousState;
            lock (_stateLock)
            {
                previousState = _state;
                _state = value;
            }

            if (previousState != value)
            {
                OnStateChanged(previousState, value);
            }
        }
    }

    /// <inheritdoc />
    public abstract IPluginConfiguration Configuration { get; protected set; }

    /// <inheritdoc />
    public ProcessorMetrics Metrics
    {
        get
        {
            lock (_stateLock)
            {
                return _metrics;
            }
        }
        protected set
        {
            lock (_stateLock)
            {
                _metrics = value;
            }
        }
    }

    /// <summary>
    /// Gets the logger instance for this processor.
    /// </summary>
    protected ILogger Logger => _logger;

    /// <inheritdoc />
    public virtual async Task InitializeAsync(IPluginConfiguration configuration, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        if (State != PluginState.Created)
        {
            throw new InvalidOperationException($"Processor is in {State} state and cannot be initialized");
        }

        try
        {
            _logger.LogDebug("Initializing processor {ProcessorId} with configuration", Id);

            await InitializeInternalAsync(configuration, cancellationToken);

            Configuration = configuration;
            State = PluginState.Initialized;
            _logger.LogInformation("Processor {ProcessorId} initialized successfully", Id);
        }
        catch (Exception ex)
        {
            State = PluginState.Failed;
            _logger.LogError(ex, "Processor {ProcessorId} initialization failed", Id);
            throw;
        }
    }

    /// <inheritdoc />
    public virtual async Task<ProcessResult> ProcessAsync(IChunk input, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        if (State != PluginState.Running)
        {
            return ProcessResult.CreateFailure($"Processor is in {State} state and cannot process data");
        }

        var stopwatch = Stopwatch.StartNew();
        try
        {
            _logger.LogDebug("Processing chunk with {RowCount} rows", input.RowCount);

            var result = await ProcessInternalAsync(input, cancellationToken);

            stopwatch.Stop();
            UpdateMetrics(input.RowCount, stopwatch.Elapsed, result.Success);

            if (result.Success)
            {
                _logger.LogDebug("Processed chunk successfully in {ElapsedMs}ms", stopwatch.ElapsedMilliseconds);
            }
            else
            {
                _logger.LogWarning("Chunk processing failed: {Errors}", string.Join(", ", result.Errors));
            }

            return result with { ProcessingTime = stopwatch.Elapsed };
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            UpdateMetrics(input.RowCount, stopwatch.Elapsed, false);

            _logger.LogError(ex, "Chunk processing failed with exception");

            return ProcessResult.CreateFailure(ex.Message);
        }
    }

    /// <inheritdoc />
    public virtual async Task<IDataset> ProcessDatasetAsync(IDataset input, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        if (State != PluginState.Running)
        {
            throw new InvalidOperationException($"Processor is in {State} state and cannot process data");
        }

        try
        {
            _logger.LogDebug("Processing dataset with streaming approach");

            var result = await ProcessDatasetInternalAsync(input, cancellationToken);

            _logger.LogDebug("Dataset processing completed");

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Dataset processing failed");
            throw;
        }
    }

    /// <inheritdoc />
    public virtual async Task StartAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        if (State != PluginState.Initialized)
        {
            throw new InvalidOperationException($"Processor must be initialized before starting. Current state: {State}");
        }

        try
        {
            _logger.LogDebug("Starting processor {ProcessorId}", Id);

            await StartInternalAsync(cancellationToken);

            State = PluginState.Running;
            _logger.LogInformation("Processor {ProcessorId} started successfully", Id);
        }
        catch (Exception ex)
        {
            State = PluginState.Failed;
            _logger.LogError(ex, "Processor {ProcessorId} start failed", Id);
            throw;
        }
    }

    /// <inheritdoc />
    public virtual async Task StopAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        if (State != PluginState.Running)
        {
            _logger.LogWarning("Processor {ProcessorId} is not running (state: {State})", Id, State);
            return;
        }

        try
        {
            _logger.LogDebug("Stopping processor {ProcessorId}", Id);

            await StopInternalAsync(cancellationToken);

            State = PluginState.Stopped;
            _logger.LogInformation("Processor {ProcessorId} stopped successfully", Id);
        }
        catch (Exception ex)
        {
            State = PluginState.Failed;
            _logger.LogError(ex, "Processor {ProcessorId} stop failed", Id);
            throw;
        }
    }

    /// <inheritdoc />
    public virtual async Task<IEnumerable<HealthCheck>> HealthCheckAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        var healthChecks = new List<HealthCheck>
        {
            new HealthCheck
            {
                Name = "Processor State",
                Passed = State == PluginState.Running || State == PluginState.Initialized,
                Details = $"Current state: {State}",
                ExecutionTime = TimeSpan.Zero,
                Severity = State == PluginState.Failed ? HealthCheckSeverity.Critical : HealthCheckSeverity.Info
            },
            new HealthCheck
            {
                Name = "Processing Performance",
                Passed = Metrics.RowsPerSecond > 1000, // Threshold for acceptable performance
                Details = $"Current throughput: {Metrics.RowsPerSecond:F0} rows/sec",
                ExecutionTime = TimeSpan.Zero,
                Severity = Metrics.RowsPerSecond < 100 ? HealthCheckSeverity.Warning : HealthCheckSeverity.Info
            }
        };

        var additionalChecks = await PerformAdditionalHealthChecksAsync(cancellationToken);
        healthChecks.AddRange(additionalChecks);

        return healthChecks;
    }

    /// <inheritdoc />
    public event EventHandler<PluginStateChangedEventArgs>? StateChanged;

    /// <summary>
    /// Performs processor-specific initialization logic.
    /// </summary>
    /// <param name="configuration">Plugin configuration</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Task representing the initialization operation</returns>
    protected abstract Task InitializeInternalAsync(IPluginConfiguration configuration, CancellationToken cancellationToken);

    /// <summary>
    /// Performs processor-specific chunk processing logic.
    /// </summary>
    /// <param name="input">Input chunk</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Processing result</returns>
    protected abstract Task<ProcessResult> ProcessInternalAsync(IChunk input, CancellationToken cancellationToken);

    /// <summary>
    /// Performs processor-specific dataset processing logic.
    /// </summary>
    /// <param name="input">Input dataset</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Processed dataset</returns>
    protected abstract Task<IDataset> ProcessDatasetInternalAsync(IDataset input, CancellationToken cancellationToken);

    /// <summary>
    /// Performs processor-specific start logic.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Task representing the start operation</returns>
    protected abstract Task StartInternalAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Performs processor-specific stop logic.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Task representing the stop operation</returns>
    protected abstract Task StopInternalAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Performs additional processor-specific health checks.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Additional health check results</returns>
    protected virtual Task<IEnumerable<HealthCheck>> PerformAdditionalHealthChecksAsync(CancellationToken cancellationToken)
    {
        return Task.FromResult(Enumerable.Empty<HealthCheck>());
    }

    /// <summary>
    /// Updates the processor metrics.
    /// </summary>
    /// <param name="rowCount">Number of rows processed</param>
    /// <param name="processingTime">Time taken for processing</param>
    /// <param name="success">Whether processing was successful</param>
    protected void UpdateMetrics(int rowCount, TimeSpan processingTime, bool success)
    {
        lock (_stateLock)
        {
            var currentMemory = GC.GetTotalMemory(false);

            _metrics = _metrics with
            {
                TotalChunks = _metrics.TotalChunks + 1,
                TotalRows = _metrics.TotalRows + rowCount,
                TotalProcessingTime = _metrics.TotalProcessingTime.Add(processingTime),
                CurrentMemoryUsage = currentMemory,
                PeakMemoryUsage = Math.Max(_metrics.PeakMemoryUsage, currentMemory),
                ErrorCount = success ? _metrics.ErrorCount : _metrics.ErrorCount + 1,
                Timestamp = DateTimeOffset.UtcNow
            };
        }
    }

    /// <summary>
    /// Called when the processor state changes.
    /// </summary>
    /// <param name="previousState">Previous state</param>
    /// <param name="currentState">Current state</param>
    protected virtual void OnStateChanged(PluginState previousState, PluginState currentState)
    {
        StateChanged?.Invoke(this, new PluginStateChangedEventArgs
        {
            PreviousState = previousState,
            CurrentState = currentState
        });
    }

    /// <summary>
    /// Throws an exception if the processor has been disposed.
    /// </summary>
    protected void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(GetType().Name);
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Disposes the processor resources.
    /// </summary>
    /// <param name="disposing">Whether disposing is being called from Dispose method</param>
    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed && disposing)
        {
            try
            {
                if (State == PluginState.Running)
                {
                    StopAsync().GetAwaiter().GetResult();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error stopping processor {ProcessorId} during disposal", Id);
            }
            finally
            {
                State = PluginState.Disposed;
                _disposed = true;
            }
        }
    }
}
