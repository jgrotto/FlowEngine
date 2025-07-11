using FlowEngine.Abstractions.Plugins;
using FlowEngine.Core.Plugins;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace FlowEngine.Core.Tests.Plugins;

/// <summary>
/// Tests for exponential backoff retry policy functionality.
/// </summary>
public class ExponentialBackoffRetryPolicyTests : IDisposable
{
    private readonly ILogger<ExponentialBackoffRetryPolicy> _mockLogger;
    private readonly ExponentialBackoffRetryPolicy _retryPolicy;

    public ExponentialBackoffRetryPolicyTests()
    {
        _mockLogger = Substitute.For<ILogger<ExponentialBackoffRetryPolicy>>();
        _retryPolicy = new ExponentialBackoffRetryPolicy(_mockLogger);
    }

    public void Dispose()
    {
        // Cleanup if needed
    }

    [Fact]
    public async Task ExecuteAsync_SuccessOnFirstAttempt_ReturnsResult()
    {
        // Arrange
        const string expectedResult = "success";
        var operation = new Func<Task<string>>(() => Task.FromResult(expectedResult));

        // Act
        var result = await _retryPolicy.ExecuteAsync(operation);

        // Assert
        Assert.Equal(expectedResult, result);
    }

    [Fact]
    public async Task ExecuteAsync_SuccessOnSecondAttempt_RetriesAndReturnsResult()
    {
        // Arrange
        const string expectedResult = "success";
        var attemptCount = 0;
        var operation = new Func<Task<string>>(() =>
        {
            attemptCount++;
            if (attemptCount == 1)
            {
                throw new IOException("Temporary I/O error");
            }
            return Task.FromResult(expectedResult);
        });

        // Act
        var result = await _retryPolicy.ExecuteAsync(operation);

        // Assert
        Assert.Equal(expectedResult, result);
        Assert.Equal(2, attemptCount);
    }

    [Fact]
    public async Task ExecuteAsync_AllAttemptsFail_ThrowsRetryExhaustedException()
    {
        // Arrange
        var operation = new Func<Task<string>>(() => throw new IOException("Persistent I/O error"));
        var context = new RetryContext
        {
            OperationName = "TestOperation",
            MaxAttempts = 2
        };

        // Act & Assert
        var exception = await Assert.ThrowsAsync<OperationRetryExhaustedException>(
            () => _retryPolicy.ExecuteAsync(operation, context));

        Assert.Equal("TestOperation", exception.OperationName);
        Assert.Equal(2, exception.TotalAttempts);
        Assert.IsType<IOException>(exception.InnerException);
    }

    [Fact]
    public async Task ExecuteAsync_NonRetryableException_ThrowsImmediately()
    {
        // Arrange
        var operation = new Func<Task<string>>(() => throw new ArgumentException("Invalid argument"));

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() => _retryPolicy.ExecuteAsync(operation));
    }

    [Theory]
    [InlineData(typeof(IOException), true)]
    [InlineData(typeof(TimeoutException), true)]
    [InlineData(typeof(TaskCanceledException), true)]
    [InlineData(typeof(ArgumentException), false)]
    [InlineData(typeof(NotSupportedException), false)]
    [InlineData(typeof(UnauthorizedAccessException), false)]
    public void ShouldRetry_VariousExceptionTypes_ReturnsExpectedResult(Type exceptionType, bool expectedResult)
    {
        // Arrange
        var exception = (Exception)Activator.CreateInstance(exceptionType, "Test message")!;

        // Act
        var result = _retryPolicy.ShouldRetry(exception);

        // Assert
        Assert.Equal(expectedResult, result);
    }

    [Fact]
    public void ShouldRetry_PluginLoadExceptionWithIOError_ReturnsTrue()
    {
        // Arrange
        var ioException = new IOException("File temporarily locked");
        var pluginException = new PluginLoadException("Plugin load failed", ioException)
        {
            AssemblyPath = "/path/to/plugin.dll",
            TypeName = "TestPlugin"
        };

        // Act
        var result = _retryPolicy.ShouldRetry(pluginException);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void ShouldRetry_PluginLoadExceptionWithBadImageFormat_ReturnsFalse()
    {
        // Arrange
        var badImageException = new BadImageFormatException("Corrupted assembly");
        var pluginException = new PluginLoadException("Plugin load failed", badImageException)
        {
            AssemblyPath = "/path/to/plugin.dll",
            TypeName = "TestPlugin"
        };

        // Act
        var result = _retryPolicy.ShouldRetry(pluginException);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void ShouldRetry_RecoverablePluginExecutionException_ReturnsTrue()
    {
        // Arrange
        var pluginException = new PluginExecutionException("Execution failed")
        {
            PluginName = "TestPlugin",
            IsRecoverable = true
        };

        // Act
        var result = _retryPolicy.ShouldRetry(pluginException);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void ShouldRetry_NonRecoverablePluginExecutionException_ReturnsFalse()
    {
        // Arrange
        var pluginException = new PluginExecutionException("Execution failed")
        {
            PluginName = "TestPlugin",
            IsRecoverable = false
        };

        // Act
        var result = _retryPolicy.ShouldRetry(pluginException);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task ExecuteAsync_WithCustomRetryPredicate_UsesCustomLogic()
    {
        // Arrange
        var customConfig = new RetryPolicyConfiguration
        {
            MaxAttempts = 2,
            CustomRetryPredicate = ex => ex.Message.Contains("RETRY")
        };
        var customRetryPolicy = new ExponentialBackoffRetryPolicy(customConfig, _mockLogger);

        var attemptCount = 0;
        var operation = new Func<Task<string>>(() =>
        {
            attemptCount++;
            if (attemptCount == 1)
            {
                throw new InvalidOperationException("RETRY this operation");
            }
            return Task.FromResult("success");
        });

        // Act
        var result = await customRetryPolicy.ExecuteAsync(operation);

        // Assert
        Assert.Equal("success", result);
        Assert.Equal(2, attemptCount);
    }

    [Fact]
    public async Task ExecuteAsync_WithExponentialBackoff_IncreaseDelay()
    {
        // Arrange
        var config = new RetryPolicyConfiguration
        {
            MaxAttempts = 3,
            BaseDelay = TimeSpan.FromMilliseconds(10),
            UseExponentialBackoff = true,
            BackoffMultiplier = 2.0,
            UseJitter = false // Disable jitter for predictable testing
        };
        var retryPolicy = new ExponentialBackoffRetryPolicy(config, _mockLogger);

        var attemptTimes = new List<DateTime>();
        var operation = new Func<Task<string>>(() =>
        {
            attemptTimes.Add(DateTime.UtcNow);
            throw new IOException("Always fails");
        });

        // Act & Assert
        await Assert.ThrowsAsync<OperationRetryExhaustedException>(
            () => retryPolicy.ExecuteAsync(operation));

        // Verify exponential backoff (allowing for timing variations)
        Assert.Equal(3, attemptTimes.Count);
        if (attemptTimes.Count >= 2)
        {
            var firstDelay = attemptTimes[1] - attemptTimes[0];
            // First delay should be close to base delay (10ms)
            Assert.True(firstDelay.TotalMilliseconds >= 8 && firstDelay.TotalMilliseconds <= 50);
        }
        if (attemptTimes.Count >= 3)
        {
            var secondDelay = attemptTimes[2] - attemptTimes[1];
            // Second delay should be approximately double the first (20ms)
            Assert.True(secondDelay.TotalMilliseconds >= 15 && secondDelay.TotalMilliseconds <= 100);
        }
    }

    [Fact]
    public async Task ExecuteAsync_VoidOperation_ExecutesSuccessfully()
    {
        // Arrange
        var executed = false;
        var operation = new Func<Task>(() =>
        {
            executed = true;
            return Task.CompletedTask;
        });

        // Act
        await _retryPolicy.ExecuteAsync(operation);

        // Assert
        Assert.True(executed);
    }

    [Fact]
    public async Task ExecuteAsync_VoidOperationWithRetry_RetriesAndSucceeds()
    {
        // Arrange
        var attemptCount = 0;
        var operation = new Func<Task>(() =>
        {
            attemptCount++;
            if (attemptCount == 1)
            {
                throw new IOException("Temporary error");
            }
            return Task.CompletedTask;
        });

        // Act
        await _retryPolicy.ExecuteAsync(operation);

        // Assert
        Assert.Equal(2, attemptCount);
    }

    [Fact]
    public async Task ExecuteAsync_ContextWithCustomMaxAttempts_UseCustomValue()
    {
        // Arrange
        var attemptCount = 0;
        var operation = new Func<Task<string>>(() =>
        {
            attemptCount++;
            throw new IOException("Always fails");
        });

        var context = new RetryContext
        {
            OperationName = "CustomAttemptsTest",
            MaxAttempts = 5 // Override default 3
        };

        // Act & Assert
        var exception = await Assert.ThrowsAsync<OperationRetryExhaustedException>(
            () => _retryPolicy.ExecuteAsync(operation, context));

        Assert.Equal(5, exception.TotalAttempts);
        Assert.Equal(5, attemptCount);
    }
}
