using FlowEngine.Abstractions.Plugins;
using Microsoft.Extensions.Logging;
using System.Net.Http;
using System.Net.Sockets;
using System.Security;

namespace FlowEngine.Core.Plugins;

/// <summary>
/// Implements an exponential backoff retry policy for handling transient failures.
/// </summary>
public sealed class ExponentialBackoffRetryPolicy : IRetryPolicy
{
    private readonly RetryPolicyConfiguration _configuration;
    private readonly ILogger<ExponentialBackoffRetryPolicy> _logger;
    private readonly Random _random = new();

    /// <summary>
    /// Initializes a new instance of the exponential backoff retry policy.
    /// </summary>
    public ExponentialBackoffRetryPolicy(
        RetryPolicyConfiguration configuration,
        ILogger<ExponentialBackoffRetryPolicy> logger)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Initializes a new instance with default configuration.
    /// </summary>
    public ExponentialBackoffRetryPolicy(ILogger<ExponentialBackoffRetryPolicy> logger)
        : this(new RetryPolicyConfiguration(), logger)
    {
    }

    /// <inheritdoc />
    public async Task<T> ExecuteAsync<T>(Func<Task<T>> operation, RetryContext? context = null)
    {
        if (operation == null)
            throw new ArgumentNullException(nameof(operation));

        var operationName = context?.OperationName ?? "Unknown Operation";
        var maxAttempts = context?.MaxAttempts ?? _configuration.MaxAttempts;
        var baseDelay = context?.BaseDelay ?? _configuration.BaseDelay;
        var useExponentialBackoff = context?.UseExponentialBackoff ?? _configuration.UseExponentialBackoff;

        var attempts = new List<RetryAttemptResult>();
        Exception lastException = null!;

        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            var attemptStartTime = DateTime.UtcNow;
            
            try
            {
                _logger.LogDebug("Executing operation {OperationName}, attempt {Attempt}/{MaxAttempts}",
                    operationName, attempt, maxAttempts);

                var result = await operation();

                var duration = DateTime.UtcNow - attemptStartTime;
                attempts.Add(new RetryAttemptResult
                {
                    IsSuccess = true,
                    AttemptNumber = attempt,
                    Duration = duration,
                    ShouldRetry = false
                });

                if (attempt > 1)
                {
                    _logger.LogInformation("Operation {OperationName} succeeded on attempt {Attempt} after {Duration}ms",
                        operationName, attempt, duration.TotalMilliseconds);
                }

                return result;
            }
            catch (Exception ex)
            {
                lastException = ex;
                var duration = DateTime.UtcNow - attemptStartTime;
                var shouldRetry = attempt < maxAttempts && ShouldRetry(ex);
                
                TimeSpan? nextRetryDelay = null;
                if (shouldRetry)
                {
                    nextRetryDelay = CalculateDelay(attempt, baseDelay, useExponentialBackoff);
                }

                var attemptResult = new RetryAttemptResult
                {
                    IsSuccess = false,
                    AttemptNumber = attempt,
                    Exception = ex,
                    Duration = duration,
                    ShouldRetry = shouldRetry,
                    NextRetryDelay = nextRetryDelay
                };

                attempts.Add(attemptResult);

                if (shouldRetry)
                {
                    _logger.LogWarning(ex,
                        "Operation {OperationName} failed on attempt {Attempt}/{MaxAttempts}, will retry in {Delay}ms. Error: {ErrorMessage}",
                        operationName, attempt, maxAttempts, nextRetryDelay!.Value.TotalMilliseconds, ex.Message);

                    await Task.Delay(nextRetryDelay.Value);
                }
                else
                {
                    if (attempt == maxAttempts)
                    {
                        _logger.LogError(ex,
                            "Operation {OperationName} failed permanently after {Attempts} attempts over {TotalDuration}ms",
                            operationName, attempt, attempts.Sum(a => a.Duration.TotalMilliseconds));
                    }
                    else
                    {
                        _logger.LogError(ex,
                            "Operation {OperationName} failed with non-retryable error on attempt {Attempt}: {ErrorMessage}",
                            operationName, attempt, ex.Message);
                    }

                    // Wrap the exception with retry context
                    throw CreateRetryExhaustedException(operationName, attempts, lastException);
                }
            }
        }

        // This should never be reached, but included for completeness
        throw CreateRetryExhaustedException(operationName, attempts, lastException);
    }

    /// <inheritdoc />
    public async Task ExecuteAsync(Func<Task> operation, RetryContext? context = null)
    {
        if (operation == null)
            throw new ArgumentNullException(nameof(operation));

        await ExecuteAsync(async () =>
        {
            await operation();
            return true; // Dummy return value
        }, context);
    }

    /// <inheritdoc />
    public bool ShouldRetry(Exception exception)
    {
        if (exception == null)
            return false;

        // Check custom predicate first
        if (_configuration.CustomRetryPredicate != null)
        {
            return _configuration.CustomRetryPredicate(exception);
        }

        // Check against configured retryable exception types
        var exceptionType = exception.GetType();
        if (_configuration.RetryableExceptionTypes.Contains(exceptionType))
        {
            return true;
        }

        // Check for common retryable exceptions
        return exception switch
        {
            IOException => true,
            TimeoutException => true,
            HttpRequestException => true,
            SocketException => true,
            TaskCanceledException when !((TaskCanceledException)exception).CancellationToken.IsCancellationRequested => true,
            OperationCanceledException when !((OperationCanceledException)exception).CancellationToken.IsCancellationRequested => true,
            
            // Plugin-specific retryable exceptions
            PluginLoadException pluginEx when IsRetryablePluginError(pluginEx) => true,
            PluginExecutionException execEx when execEx.IsRecoverable => true,
            
            // Non-retryable exceptions (order matters - most specific first)
            ArgumentNullException => false,
            ArgumentException => false,
            NotSupportedException => false,
            NotImplementedException => false,
            InvalidOperationException => false,
            SecurityException => false,
            
            _ => false
        };
    }

    /// <summary>
    /// Determines if a plugin load exception is retryable.
    /// </summary>
    private static bool IsRetryablePluginError(PluginLoadException pluginException)
    {
        // Retry for temporary I/O issues but not for structural problems
        return pluginException.InnerException switch
        {
            FileLoadException => true,
            FileNotFoundException => false,  // More specific than IOException
            IOException => true,
            UnauthorizedAccessException => false,
            BadImageFormatException => false,
            null => false,
            _ => false
        };
    }

    /// <summary>
    /// Calculates the delay for the next retry attempt.
    /// </summary>
    private TimeSpan CalculateDelay(int attemptNumber, TimeSpan baseDelay, bool useExponentialBackoff)
    {
        TimeSpan delay;

        if (useExponentialBackoff)
        {
            // Exponential backoff: delay = baseDelay * (backoffMultiplier ^ (attemptNumber - 1))
            var exponentialDelay = TimeSpan.FromMilliseconds(
                baseDelay.TotalMilliseconds * Math.Pow(_configuration.BackoffMultiplier, attemptNumber - 1));
            
            delay = exponentialDelay > _configuration.MaxDelay ? _configuration.MaxDelay : exponentialDelay;
        }
        else
        {
            delay = baseDelay;
        }

        // Add jitter to prevent thundering herd
        if (_configuration.UseJitter)
        {
            var jitterRange = delay.TotalMilliseconds * 0.1; // 10% jitter
            var jitter = (_random.NextDouble() - 0.5) * 2 * jitterRange; // -10% to +10%
            delay = TimeSpan.FromMilliseconds(Math.Max(0, delay.TotalMilliseconds + jitter));
        }

        return delay;
    }

    /// <summary>
    /// Creates a comprehensive exception when all retry attempts are exhausted.
    /// </summary>
    private static Exception CreateRetryExhaustedException(
        string operationName, 
        List<RetryAttemptResult> attempts, 
        Exception lastException)
    {
        var totalDuration = TimeSpan.FromMilliseconds(attempts.Sum(a => a.Duration.TotalMilliseconds));
        var errorTypes = attempts.Where(a => a.Exception != null)
                                .Select(a => a.Exception!.GetType().Name)
                                .Distinct()
                                .ToArray();

        var message = $"Operation '{operationName}' failed after {attempts.Count} attempts over {totalDuration.TotalMilliseconds:F1}ms";

        return new OperationRetryExhaustedException(message, lastException)
        {
            OperationName = operationName,
            TotalAttempts = attempts.Count,
            TotalDuration = totalDuration,
            Context = new Dictionary<string, object>
            {
                ["Attempts"] = attempts.Select(a => new
                {
                    AttemptNumber = a.AttemptNumber,
                    IsSuccess = a.IsSuccess,
                    Duration = a.Duration.TotalMilliseconds,
                    ErrorType = a.Exception?.GetType().Name,
                    ErrorMessage = a.Exception?.Message,
                    ShouldRetry = a.ShouldRetry,
                    NextRetryDelay = a.NextRetryDelay?.TotalMilliseconds
                }).ToArray(),
                ["ErrorTypes"] = errorTypes,
                ["FirstError"] = attempts.FirstOrDefault(a => a.Exception != null)?.Exception?.Message ?? "Unknown",
                ["LastError"] = attempts.LastOrDefault(a => a.Exception != null)?.Exception?.Message ?? "Unknown"
            }
        };
    }
}

/// <summary>
/// Exception thrown when all retry attempts for an operation have been exhausted.
/// </summary>
public sealed class OperationRetryExhaustedException : Exception
{
    /// <summary>
    /// Gets the name of the operation that failed.
    /// </summary>
    public string OperationName { get; init; } = string.Empty;

    /// <summary>
    /// Gets the total number of attempts made.
    /// </summary>
    public int TotalAttempts { get; init; }

    /// <summary>
    /// Gets the total duration of all retry attempts.
    /// </summary>
    public TimeSpan TotalDuration { get; init; }

    /// <summary>
    /// Gets additional context about the retry attempts.
    /// </summary>
    public IReadOnlyDictionary<string, object> Context { get; init; } = new Dictionary<string, object>();

    /// <summary>
    /// Initializes a new instance of the retry exhausted exception.
    /// </summary>
    public OperationRetryExhaustedException(string message) : base(message) { }

    /// <summary>
    /// Initializes a new instance of the retry exhausted exception.
    /// </summary>
    public OperationRetryExhaustedException(string message, Exception innerException) 
        : base(message, innerException) { }
}