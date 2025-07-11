using System.Net.Http;
using System.Net.Sockets;

namespace FlowEngine.Abstractions.Plugins;

/// <summary>
/// Interface for retry policy implementations that handle transient failures.
/// </summary>
public interface IRetryPolicy
{
    /// <summary>
    /// Executes an operation with retry logic for transient failures.
    /// </summary>
    /// <typeparam name="T">The return type of the operation</typeparam>
    /// <param name="operation">The operation to execute</param>
    /// <param name="context">Optional context for the retry operation</param>
    /// <returns>The result of the operation</returns>
    Task<T> ExecuteAsync<T>(Func<Task<T>> operation, RetryContext? context = null);

    /// <summary>
    /// Executes an operation with retry logic for transient failures.
    /// </summary>
    /// <param name="operation">The operation to execute</param>
    /// <param name="context">Optional context for the retry operation</param>
    /// <returns>Task that completes when the operation succeeds</returns>
    Task ExecuteAsync(Func<Task> operation, RetryContext? context = null);

    /// <summary>
    /// Determines if an exception should trigger a retry attempt.
    /// </summary>
    /// <param name="exception">The exception to evaluate</param>
    /// <returns>True if the operation should be retried</returns>
    bool ShouldRetry(Exception exception);
}

/// <summary>
/// Context information for retry operations.
/// </summary>
public sealed class RetryContext
{
    /// <summary>
    /// Gets or sets the operation name for logging and diagnostics.
    /// </summary>
    public required string OperationName { get; init; }

    /// <summary>
    /// Gets or sets additional context data.
    /// </summary>
    public IReadOnlyDictionary<string, object> Properties { get; init; } = new Dictionary<string, object>();

    /// <summary>
    /// Gets or sets the maximum number of retry attempts.
    /// </summary>
    public int? MaxAttempts { get; init; }

    /// <summary>
    /// Gets or sets the base delay between retry attempts.
    /// </summary>
    public TimeSpan? BaseDelay { get; init; }

    /// <summary>
    /// Gets or sets whether to use exponential backoff.
    /// </summary>
    public bool UseExponentialBackoff { get; init; } = true;
}

/// <summary>
/// Result of a retry operation attempt.
/// </summary>
public sealed class RetryAttemptResult
{
    /// <summary>
    /// Gets whether the attempt was successful.
    /// </summary>
    public required bool IsSuccess { get; init; }

    /// <summary>
    /// Gets the attempt number (1-based).
    /// </summary>
    public required int AttemptNumber { get; init; }

    /// <summary>
    /// Gets the exception that occurred during the attempt (if any).
    /// </summary>
    public Exception? Exception { get; init; }

    /// <summary>
    /// Gets the duration of the attempt.
    /// </summary>
    public required TimeSpan Duration { get; init; }

    /// <summary>
    /// Gets whether another retry should be attempted.
    /// </summary>
    public required bool ShouldRetry { get; init; }

    /// <summary>
    /// Gets the delay before the next retry attempt.
    /// </summary>
    public TimeSpan? NextRetryDelay { get; init; }
}

/// <summary>
/// Configuration for retry policy behavior.
/// </summary>
public sealed record RetryPolicyConfiguration
{
    /// <summary>
    /// Gets the maximum number of retry attempts.
    /// </summary>
    public int MaxAttempts { get; init; } = 3;

    /// <summary>
    /// Gets the base delay between retry attempts.
    /// </summary>
    public TimeSpan BaseDelay { get; init; } = TimeSpan.FromMilliseconds(100);

    /// <summary>
    /// Gets the maximum delay between retry attempts.
    /// </summary>
    public TimeSpan MaxDelay { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Gets whether to use exponential backoff.
    /// </summary>
    public bool UseExponentialBackoff { get; init; } = true;

    /// <summary>
    /// Gets the exponential backoff multiplier.
    /// </summary>
    public double BackoffMultiplier { get; init; } = 2.0;

    /// <summary>
    /// Gets whether to add jitter to retry delays.
    /// </summary>
    public bool UseJitter { get; init; } = true;

    /// <summary>
    /// Gets the types of exceptions that should trigger retries.
    /// </summary>
    public IReadOnlySet<Type> RetryableExceptionTypes { get; init; } = new HashSet<Type>
    {
        typeof(IOException),
        typeof(TimeoutException),
        typeof(HttpRequestException),
        typeof(SocketException),
        typeof(TaskCanceledException)
    };

    /// <summary>
    /// Gets custom logic for determining if an exception should trigger a retry.
    /// </summary>
    public Func<Exception, bool>? CustomRetryPredicate { get; init; }
}
