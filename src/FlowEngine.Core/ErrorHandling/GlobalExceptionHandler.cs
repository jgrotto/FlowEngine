using FlowEngine.Abstractions.ErrorHandling;
using FlowEngine.Abstractions;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace FlowEngine.Core.ErrorHandling;

/// <summary>
/// Global exception handler that provides centralized error handling and recovery strategies.
/// Implements consistent error processing across all FlowEngine components.
/// </summary>
public sealed class GlobalExceptionHandler : IDisposable
{
    private readonly ILogger<GlobalExceptionHandler> _logger;
    private readonly ErrorRecoveryService _recoveryService;
    private readonly Dictionary<Type, Func<Exception, ErrorContext, Task<ErrorHandlingResult>>> _handlers;
    private readonly object _lock = new();
    private bool _disposed;

    /// <summary>
    /// Event raised when an error is handled.
    /// </summary>
    public event EventHandler<ErrorHandledEventArgs>? ErrorHandled;

    /// <summary>
    /// Event raised when an error requires escalation.
    /// </summary>
    public event EventHandler<ErrorEscalationEventArgs>? ErrorEscalation;

    public GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger, ErrorRecoveryService recoveryService)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _recoveryService = recoveryService ?? throw new ArgumentNullException(nameof(recoveryService));
        _handlers = new Dictionary<Type, Func<Exception, ErrorContext, Task<ErrorHandlingResult>>>();

        RegisterDefaultHandlers();
        SetupGlobalHandlers();
    }

    /// <summary>
    /// Handles an exception with the appropriate strategy based on exception type and context.
    /// </summary>
    public async Task<ErrorHandlingResult> HandleExceptionAsync(Exception exception, ErrorContext? context = null)
    {
        if (exception == null)
        {
            throw new ArgumentNullException(nameof(exception));
        }

        var effectiveContext = context ?? ErrorContext.FromCurrentContext("UnknownOperation", "GlobalHandler");
        var activity = Activity.Current;

        try
        {
            using var handlingActivity = Activity.Current?.Source.StartActivity("ErrorHandling");
            handlingActivity?.SetTag("error.type", exception.GetType().Name);
            handlingActivity?.SetTag("error.message", exception.Message);
            handlingActivity?.SetTag("operation.name", effectiveContext.OperationName);

            _logger.LogError(exception, "Handling exception in operation {OperationName} from component {Component}",
                effectiveContext.OperationName, effectiveContext.Component);

            // Find the most specific handler
            var handler = FindHandler(exception.GetType());
            if (handler != null)
            {
                var result = await handler(exception, effectiveContext);

                // Log the handling result
                LogHandlingResult(exception, effectiveContext, result);

                // Raise events
                RaiseHandledEvent(exception, effectiveContext, result);

                if (result.RequiresEscalation)
                {
                    RaiseEscalationEvent(exception, effectiveContext, result);
                }

                return result;
            }

            // No specific handler found, use default handling
            var defaultResult = await HandleUnknownExceptionAsync(exception, effectiveContext);
            LogHandlingResult(exception, effectiveContext, defaultResult);
            RaiseHandledEvent(exception, effectiveContext, defaultResult);

            return defaultResult;
        }
        catch (Exception handlingException)
        {
            _logger.LogCritical(handlingException,
                "Critical error in exception handler while processing {ExceptionType}: {OriginalMessage}",
                exception.GetType().Name, exception.Message);

            // Return a safe fallback result
            return ErrorHandlingResult.CreateCriticalFailure(
                "Exception handler failed",
                new Dictionary<string, object>
                {
                    ["OriginalException"] = exception.GetType().Name,
                    ["OriginalMessage"] = exception.Message,
                    ["HandlingException"] = handlingException.GetType().Name,
                    ["HandlingMessage"] = handlingException.Message
                });
        }
    }

    /// <summary>
    /// Registers a custom exception handler for a specific exception type.
    /// </summary>
    public void RegisterHandler<T>(Func<T, ErrorContext, Task<ErrorHandlingResult>> handler) where T : Exception
    {
        if (handler == null)
        {
            throw new ArgumentNullException(nameof(handler));
        }

        lock (_lock)
        {
            _handlers[typeof(T)] = (ex, ctx) => handler((T)ex, ctx);
        }
    }

    /// <summary>
    /// Removes a registered exception handler.
    /// </summary>
    public void UnregisterHandler<T>() where T : Exception
    {
        lock (_lock)
        {
            _handlers.Remove(typeof(T));
        }
    }

    private void RegisterDefaultHandlers()
    {
        // FlowEngine-specific exceptions
        RegisterHandler<FlowEngineSchemaException>(HandleSchemaExceptionAsync);
        RegisterHandler<FlowEngineMemoryException>(HandleMemoryExceptionAsync);
        RegisterHandler<FlowEnginePluginException>(HandlePluginExceptionAsync);

        // Legacy exceptions (for backwards compatibility)
        RegisterHandler<SchemaValidationException>(HandleLegacySchemaExceptionAsync);
        RegisterHandler<MemoryAllocationException>(HandleLegacyMemoryExceptionAsync);
        RegisterHandler<ChunkProcessingException>(HandleChunkProcessingExceptionAsync);

        // System exceptions
        RegisterHandler<OutOfMemoryException>(HandleOutOfMemoryExceptionAsync);
        RegisterHandler<UnauthorizedAccessException>(HandleUnauthorizedAccessExceptionAsync);
        RegisterHandler<IOException>(HandleIOExceptionAsync);
        RegisterHandler<TimeoutException>(HandleTimeoutExceptionAsync);
        RegisterHandler<OperationCanceledException>(HandleCancellationExceptionAsync);
    }

    private void SetupGlobalHandlers()
    {
        // Set up global unhandled exception handlers
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
    }

    private Func<Exception, ErrorContext, Task<ErrorHandlingResult>>? FindHandler(Type exceptionType)
    {
        lock (_lock)
        {
            // Try exact match first
            if (_handlers.TryGetValue(exceptionType, out var handler))
            {
                return handler;
            }

            // Try inheritance hierarchy
            var currentType = exceptionType.BaseType;
            while (currentType != null)
            {
                if (_handlers.TryGetValue(currentType, out handler))
                {
                    return handler;
                }
                currentType = currentType.BaseType;
            }

            return null;
        }
    }

    #region Exception Handlers

    private async Task<ErrorHandlingResult> HandleSchemaExceptionAsync(FlowEngineSchemaException exception, ErrorContext context)
    {
        var recovery = await _recoveryService.AttemptSchemaRecoveryAsync(exception, context);

        return ErrorHandlingResult.Create(
            isRecovered: recovery.IsSuccessful,
            requiresEscalation: !recovery.IsSuccessful && exception.Severity >= ErrorSeverity.High,
            message: recovery.IsSuccessful ? "Schema validation recovered" : "Schema validation failed permanently",
            recoveryAction: recovery.RecoveryAction,
            metadata: new Dictionary<string, object>
            {
                ["SchemaErrors"] = exception.ValidationErrors.Count,
                ["RecoveryAttempted"] = recovery.IsSuccessful,
                ["RecoveryStrategy"] = recovery.RecoveryAction ?? "None"
            });
    }

    private async Task<ErrorHandlingResult> HandleMemoryExceptionAsync(FlowEngineMemoryException exception, ErrorContext context)
    {
        var recovery = await _recoveryService.AttemptMemoryRecoveryAsync(exception, context);

        return ErrorHandlingResult.Create(
            isRecovered: recovery.IsSuccessful,
            requiresEscalation: !recovery.IsSuccessful, // Memory issues always escalate if not recovered
            message: recovery.IsSuccessful ? "Memory pressure resolved" : "Memory allocation failed permanently",
            recoveryAction: recovery.RecoveryAction,
            metadata: new Dictionary<string, object>
            {
                ["RequestedSizeMB"] = exception.RequestedSize / (1024.0 * 1024.0),
                ["MemoryPressure"] = exception.MemoryPressure,
                ["RecoveryAttempted"] = recovery.IsSuccessful
            });
    }

    private async Task<ErrorHandlingResult> HandlePluginExceptionAsync(FlowEnginePluginException exception, ErrorContext context)
    {
        var recovery = await _recoveryService.AttemptPluginRecoveryAsync(exception, context);

        return ErrorHandlingResult.Create(
            isRecovered: recovery.IsSuccessful,
            requiresEscalation: !recovery.IsSuccessful && !exception.IsRecoverable,
            message: recovery.IsSuccessful ? "Plugin operation recovered" : "Plugin operation failed permanently",
            recoveryAction: recovery.RecoveryAction,
            metadata: new Dictionary<string, object>
            {
                ["PluginName"] = exception.PluginName ?? "Unknown",
                ["IsRecoverable"] = exception.IsRecoverable,
                ["RecoveryAttempted"] = recovery.IsSuccessful
            });
    }

    private async Task<ErrorHandlingResult> HandleLegacySchemaExceptionAsync(SchemaValidationException exception, ErrorContext context)
    {
        // Convert legacy exception to new format for consistent handling
        var modernException = new FlowEngineSchemaException(
            exception.Message,
            exception.SourceSchema,
            exception.TargetSchema,
            exception.ValidationErrors,
            context.OperationName,
            exception.InnerException);

        return await HandleSchemaExceptionAsync(modernException, context);
    }

    private async Task<ErrorHandlingResult> HandleLegacyMemoryExceptionAsync(MemoryAllocationException exception, ErrorContext context)
    {
        // Convert legacy exception to new format
        var modernException = new FlowEngineMemoryException(
            exception.Message,
            exception.RequestedSize,
            exception.AvailableMemory,
            exception.MemoryPressure,
            context.OperationName,
            exception.InnerException);

        return await HandleMemoryExceptionAsync(modernException, context);
    }

    private Task<ErrorHandlingResult> HandleChunkProcessingExceptionAsync(ChunkProcessingException exception, ErrorContext context)
    {
        // Attempt chunk recovery
        var canRetry = exception.Chunk != null && exception.RowIndex.HasValue;

        if (canRetry)
        {
            // Log the problematic row/chunk for analysis
            _logger.LogWarning("Chunk processing failed at row {RowIndex} in column {ColumnName}. Chunk can be retried.",
                exception.RowIndex, exception.ColumnName ?? "Unknown");
        }

        return Task.FromResult(ErrorHandlingResult.Create(
            isRecovered: false, // Chunk processing failures usually need manual intervention
            requiresEscalation: true,
            message: canRetry ? "Chunk processing failed but can be retried" : "Chunk processing failed permanently",
            recoveryAction: canRetry ? "Skip problematic row and continue" : null,
            metadata: new Dictionary<string, object>
            {
                ["RowIndex"] = exception.RowIndex ?? -1,
                ["ColumnName"] = exception.ColumnName ?? "Unknown",
                ["HasChunk"] = exception.Chunk != null,
                ["CanRetry"] = canRetry
            }));
    }

    private async Task<ErrorHandlingResult> HandleOutOfMemoryExceptionAsync(OutOfMemoryException exception, ErrorContext context)
    {
        // Force garbage collection and attempt recovery
        var recovery = await _recoveryService.AttemptMemoryRecoveryAsync(null, context);

        return ErrorHandlingResult.Create(
            isRecovered: false, // OOM usually means serious issues
            requiresEscalation: true,
            message: "Out of memory condition detected",
            recoveryAction: "Force garbage collection and reduce memory usage",
            metadata: new Dictionary<string, object>
            {
                ["MemoryAfterGC"] = GC.GetTotalMemory(true),
                ["Generation2Collections"] = GC.CollectionCount(2)
            });
    }

    private Task<ErrorHandlingResult> HandleUnauthorizedAccessExceptionAsync(UnauthorizedAccessException exception, ErrorContext context)
    {
        return Task.FromResult(ErrorHandlingResult.Create(
            isRecovered: false,
            requiresEscalation: true,
            message: "Access denied - insufficient permissions",
            recoveryAction: null, // Security issues can't be auto-recovered
            metadata: new Dictionary<string, object>
            {
                ["SecurityIssue"] = true,
                ["RequiresManualIntervention"] = true
            }));
    }

    private async Task<ErrorHandlingResult> HandleIOExceptionAsync(IOException exception, ErrorContext context)
    {
        var recovery = await _recoveryService.AttemptIORecoveryAsync(exception, context);

        return ErrorHandlingResult.Create(
            isRecovered: recovery.IsSuccessful,
            requiresEscalation: !recovery.IsSuccessful,
            message: recovery.IsSuccessful ? "I/O operation recovered" : "I/O operation failed",
            recoveryAction: recovery.RecoveryAction,
            metadata: new Dictionary<string, object>
            {
                ["IOErrorType"] = exception.GetType().Name,
                ["RecoveryAttempted"] = recovery.IsSuccessful
            });
    }

    private Task<ErrorHandlingResult> HandleTimeoutExceptionAsync(TimeoutException exception, ErrorContext context)
    {
        return Task.FromResult(ErrorHandlingResult.Create(
            isRecovered: false, // Timeouts usually indicate the operation should be retried
            requiresEscalation: false, // But not necessarily escalated immediately
            message: "Operation timed out",
            recoveryAction: "Retry with increased timeout",
            metadata: new Dictionary<string, object>
            {
                ["IsRetryable"] = true,
                ["SuggestedRetryDelay"] = TimeSpan.FromSeconds(5).TotalMilliseconds
            }));
    }

    private Task<ErrorHandlingResult> HandleCancellationExceptionAsync(OperationCanceledException exception, ErrorContext context)
    {
        var isUserCancellation = exception.CancellationToken.IsCancellationRequested;

        return Task.FromResult(ErrorHandlingResult.Create(
            isRecovered: isUserCancellation, // User cancellations are "normal"
            requiresEscalation: !isUserCancellation, // Unexpected cancellations should escalate
            message: isUserCancellation ? "Operation cancelled by user" : "Operation cancelled unexpectedly",
            recoveryAction: isUserCancellation ? null : "Investigate cancellation source",
            metadata: new Dictionary<string, object>
            {
                ["IsUserCancellation"] = isUserCancellation,
                ["CancellationRequested"] = exception.CancellationToken.IsCancellationRequested
            }));
    }

    private Task<ErrorHandlingResult> HandleUnknownExceptionAsync(Exception exception, ErrorContext context)
    {
        return Task.FromResult(ErrorHandlingResult.Create(
            isRecovered: false,
            requiresEscalation: true,
            message: $"Unhandled exception: {exception.GetType().Name}",
            recoveryAction: null,
            metadata: new Dictionary<string, object>
            {
                ["ExceptionType"] = exception.GetType().FullName ?? "Unknown",
                ["UnhandledException"] = true
            }));
    }

    #endregion

    #region Event Handling

    private void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception exception)
        {
            var context = ErrorContext.Create("UnhandledException", "AppDomain")
                .WithMetadata("IsTerminating", e.IsTerminating)
                .Build();

            _logger.LogCritical(exception, "Unhandled exception in AppDomain. Terminating: {IsTerminating}", e.IsTerminating);

            // Attempt to handle the exception even if terminating
            _ = Task.Run(async () =>
            {
                try
                {
                    await HandleExceptionAsync(exception, context);
                }
                catch
                {
                    // Nothing we can do at this point
                }
            });
        }
    }

    private void OnUnobservedTaskException(object sender, UnobservedTaskExceptionEventArgs e)
    {
        var context = ErrorContext.Create("UnobservedTaskException", "TaskScheduler")
            .WithMetadata("TaskId", Task.CurrentId ?? -1)
            .Build();

        _logger.LogError(e.Exception, "Unobserved task exception detected");

        // Mark as observed to prevent process termination
        e.SetObserved();

        // Handle the exception
        _ = Task.Run(async () =>
        {
            try
            {
                await HandleExceptionAsync(e.Exception, context);
            }
            catch
            {
                // Log but don't throw
            }
        });
    }

    private void LogHandlingResult(Exception exception, ErrorContext context, ErrorHandlingResult result)
    {
        var logLevel = result.RequiresEscalation ? LogLevel.Error :
                      result.IsRecovered ? LogLevel.Information : LogLevel.Warning;

        using var scope = _logger.BeginScope(context.ToLogDictionary());
        _logger.Log(logLevel,
            "Exception handling completed. Type: {ExceptionType}, Recovered: {IsRecovered}, Escalation: {RequiresEscalation}, Action: {RecoveryAction}",
            exception.GetType().Name, result.IsRecovered, result.RequiresEscalation, result.RecoveryAction ?? "None");
    }

    private void RaiseHandledEvent(Exception exception, ErrorContext context, ErrorHandlingResult result)
    {
        try
        {
            ErrorHandled?.Invoke(this, new ErrorHandledEventArgs(exception, context, result));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error in ErrorHandled event handler");
        }
    }

    private void RaiseEscalationEvent(Exception exception, ErrorContext context, ErrorHandlingResult result)
    {
        try
        {
            ErrorEscalation?.Invoke(this, new ErrorEscalationEventArgs(exception, context, result));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error in ErrorEscalation event handler");
        }
    }

    #endregion

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        try
        {
            AppDomain.CurrentDomain.UnhandledException -= OnUnhandledException;
            TaskScheduler.UnobservedTaskException -= OnUnobservedTaskException;

            _recoveryService?.Dispose();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error during GlobalExceptionHandler disposal");
        }
        finally
        {
            _disposed = true;
        }
    }
}

/// <summary>
/// Result of exception handling operation.
/// </summary>
public sealed class ErrorHandlingResult
{
    /// <summary>
    /// Gets whether the error was successfully recovered.
    /// </summary>
    public bool IsRecovered { get; }

    /// <summary>
    /// Gets whether the error requires escalation to higher-level handlers.
    /// </summary>
    public bool RequiresEscalation { get; }

    /// <summary>
    /// Gets the human-readable result message.
    /// </summary>
    public string Message { get; }

    /// <summary>
    /// Gets the recovery action that was taken or should be taken.
    /// </summary>
    public string? RecoveryAction { get; }

    /// <summary>
    /// Gets additional metadata about the handling result.
    /// </summary>
    public IReadOnlyDictionary<string, object> Metadata { get; }

    /// <summary>
    /// Gets the timestamp when the handling was completed.
    /// </summary>
    public DateTimeOffset Timestamp { get; }

    private ErrorHandlingResult(bool isRecovered, bool requiresEscalation, string message, string? recoveryAction, Dictionary<string, object> metadata)
    {
        IsRecovered = isRecovered;
        RequiresEscalation = requiresEscalation;
        Message = message;
        RecoveryAction = recoveryAction;
        Metadata = metadata.AsReadOnly();
        Timestamp = DateTimeOffset.UtcNow;
    }

    public static ErrorHandlingResult Create(bool isRecovered, bool requiresEscalation, string message, string? recoveryAction = null, Dictionary<string, object>? metadata = null)
    {
        return new ErrorHandlingResult(isRecovered, requiresEscalation, message, recoveryAction, metadata ?? new Dictionary<string, object>());
    }

    public static ErrorHandlingResult CreateSuccess(string message, string? recoveryAction = null, Dictionary<string, object>? metadata = null)
    {
        return Create(true, false, message, recoveryAction, metadata);
    }

    public static ErrorHandlingResult CreateFailure(string message, bool requiresEscalation = true, Dictionary<string, object>? metadata = null)
    {
        return Create(false, requiresEscalation, message, null, metadata);
    }

    public static ErrorHandlingResult CreateCriticalFailure(string message, Dictionary<string, object>? metadata = null)
    {
        return Create(false, true, message, "Immediate attention required", metadata);
    }
}

/// <summary>
/// Event arguments for error handled events.
/// </summary>
public sealed class ErrorHandledEventArgs : EventArgs
{
    public Exception Exception { get; }
    public ErrorContext Context { get; }
    public ErrorHandlingResult Result { get; }

    public ErrorHandledEventArgs(Exception exception, ErrorContext context, ErrorHandlingResult result)
    {
        Exception = exception;
        Context = context;
        Result = result;
    }
}

/// <summary>
/// Event arguments for error escalation events.
/// </summary>
public sealed class ErrorEscalationEventArgs : EventArgs
{
    public Exception Exception { get; }
    public ErrorContext Context { get; }
    public ErrorHandlingResult Result { get; }

    public ErrorEscalationEventArgs(Exception exception, ErrorContext context, ErrorHandlingResult result)
    {
        Exception = exception;
        Context = context;
        Result = result;
    }
}
