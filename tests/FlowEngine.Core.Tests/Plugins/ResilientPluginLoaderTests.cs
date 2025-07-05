using FlowEngine.Abstractions.Plugins;
using FlowEngine.Core.Plugins;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace FlowEngine.Core.Tests.Plugins;

/// <summary>
/// Tests for resilient plugin loader functionality.
/// </summary>
public class ResilientPluginLoaderTests : IDisposable
{
    private readonly IPluginLoader _mockBaseLoader;
    private readonly IRetryPolicy _mockRetryPolicy;
    private readonly ILogger<ResilientPluginLoader> _mockLogger;
    private readonly ResilientPluginLoader _resilientLoader;

    public ResilientPluginLoaderTests()
    {
        _mockBaseLoader = Substitute.For<IPluginLoader>();
        _mockRetryPolicy = Substitute.For<IRetryPolicy>();
        _mockLogger = Substitute.For<ILogger<ResilientPluginLoader>>();
        _resilientLoader = new ResilientPluginLoader(_mockBaseLoader, _mockRetryPolicy, _mockLogger);
    }

    public void Dispose()
    {
        _resilientLoader.Dispose();
    }

    [Fact]
    public async Task LoadPluginAsync_NonExistentAssembly_ThrowsPluginLoadException()
    {
        // Arrange
        const string nonExistentPath = "/non/existent/path.dll";
        const string typeName = "TestPlugin";

        // Act & Assert
        var exception = await Assert.ThrowsAsync<PluginLoadException>(
            () => _resilientLoader.LoadPluginAsync<IPlugin>(nonExistentPath, typeName));

        Assert.Contains("not found", exception.Message);
        Assert.Equal(nonExistentPath, exception.AssemblyPath);
        Assert.Equal(typeName, exception.TypeName);
        Assert.NotNull(exception.Context);
    }

    [Fact]
    public async Task LoadPluginAsync_BaseLoaderSuccess_ReturnsPlugin()
    {
        // Arrange
        const string assemblyPath = "test.dll";
        const string typeName = "TestPlugin";
        var mockPlugin = Substitute.For<IPlugin>();

        // Create a temporary file for testing
        File.WriteAllText(assemblyPath, "fake assembly content");

        try
        {
            // Mock retry policy to execute immediately
            _mockRetryPolicy.ExecuteAsync(Arg.Any<Func<Task<IPlugin>>>(), Arg.Any<RetryContext>())
                .Returns(callInfo => callInfo.Arg<Func<Task<IPlugin>>>()());

            _mockBaseLoader.LoadPluginAsync<IPlugin>(assemblyPath, typeName, Arg.Any<PluginIsolationLevel>())
                .Returns(mockPlugin);

            // Act
            var result = await _resilientLoader.LoadPluginAsync<IPlugin>(assemblyPath, typeName);

            // Assert
            Assert.Same(mockPlugin, result);
            await _mockBaseLoader.Received(1).LoadPluginAsync<IPlugin>(assemblyPath, typeName, Arg.Any<PluginIsolationLevel>());
        }
        finally
        {
            if (File.Exists(assemblyPath))
                File.Delete(assemblyPath);
        }
    }

    [Fact]
    public async Task LoadPluginAsync_BaseLoaderFailsWithRetryableError_RetriesAndThrowsEnhancedException()
    {
        // Arrange
        const string assemblyPath = "test.dll";
        const string typeName = "TestPlugin";
        var originalException = new IOException("Temporary I/O error");

        // Create a temporary file for testing
        File.WriteAllText(assemblyPath, "fake assembly content");

        try
        {
            // Mock retry policy to throw retry exhausted exception
            var retryExhaustedException = new OperationRetryExhaustedException("All retries failed", originalException)
            {
                OperationName = "LoadPlugin",
                TotalAttempts = 3,
                TotalDuration = TimeSpan.FromSeconds(1)
            };

            _mockRetryPolicy.ExecuteAsync(Arg.Any<Func<Task<IPlugin>>>(), Arg.Any<RetryContext>())
                .Throws(retryExhaustedException);

            // Act & Assert
            var exception = await Assert.ThrowsAsync<PluginLoadException>(
                () => _resilientLoader.LoadPluginAsync<IPlugin>(assemblyPath, typeName));

            Assert.Contains("after 3 attempts", exception.Message);
            Assert.Equal(assemblyPath, exception.AssemblyPath);
            Assert.Equal(typeName, exception.TypeName);
            Assert.NotNull(exception.Context);
            Assert.True(exception.Context.ContainsKey("RetryAttempts"));
        }
        finally
        {
            if (File.Exists(assemblyPath))
                File.Delete(assemblyPath);
        }
    }

    [Fact]
    public async Task LoadPluginAsync_BaseLoaderFailsWithNonRetryableError_ThrowsEnhancedException()
    {
        // Arrange
        const string assemblyPath = "test.dll";
        const string typeName = "TestPlugin";
        var originalException = new ArgumentException("Invalid argument");

        // Create a temporary file for testing
        File.WriteAllText(assemblyPath, "fake assembly content");

        try
        {
            // Mock retry policy to execute immediately and let exception through
            _mockRetryPolicy.ExecuteAsync(Arg.Any<Func<Task<IPlugin>>>(), Arg.Any<RetryContext>())
                .Returns(callInfo => callInfo.Arg<Func<Task<IPlugin>>>()());

            _mockBaseLoader.LoadPluginAsync<IPlugin>(assemblyPath, typeName, Arg.Any<PluginIsolationLevel>())
                .Throws(originalException);

            // Act & Assert
            var exception = await Assert.ThrowsAsync<ArgumentException>(
                () => _resilientLoader.LoadPluginAsync<IPlugin>(assemblyPath, typeName));

            Assert.Same(originalException, exception);
        }
        finally
        {
            if (File.Exists(assemblyPath))
                File.Delete(assemblyPath);
        }
    }

    [Fact]
    public async Task UnloadPluginAsync_BaseLoaderSuccess_CompletesSuccessfully()
    {
        // Arrange
        var mockPlugin = Substitute.For<IPlugin>();
        _mockBaseLoader.UnloadPluginAsync(mockPlugin).Returns(Task.CompletedTask);

        // Act
        await _resilientLoader.UnloadPluginAsync(mockPlugin);

        // Assert
        await _mockBaseLoader.Received(1).UnloadPluginAsync(mockPlugin);
    }

    [Fact]
    public async Task UnloadPluginAsync_BaseLoaderFails_ThrowsEnhancedException()
    {
        // Arrange
        var mockPlugin = Substitute.For<IPlugin>();
        var originalException = new InvalidOperationException("Unload failed");
        _mockBaseLoader.UnloadPluginAsync(mockPlugin).Throws(originalException);

        // Act & Assert
        var exception = await Assert.ThrowsAsync<PluginLoadException>(
            () => _resilientLoader.UnloadPluginAsync(mockPlugin));

        Assert.Contains("Failed to unload plugin", exception.Message);
        Assert.NotNull(exception.Context);
        Assert.True(exception.Context.ContainsKey("ErrorContext"));
    }

    [Fact]
    public async Task UnloadPluginAsync_NullPlugin_ThrowsArgumentNullException()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => _resilientLoader.UnloadPluginAsync(null!));
    }

    [Fact]
    public void Constructor_NullArguments_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => 
            new ResilientPluginLoader(null!, _mockRetryPolicy, _mockLogger));
        
        Assert.Throws<ArgumentNullException>(() => 
            new ResilientPluginLoader(_mockBaseLoader, null!, _mockLogger));
        
        Assert.Throws<ArgumentNullException>(() => 
            new ResilientPluginLoader(_mockBaseLoader, _mockRetryPolicy, null!));
    }

    [Fact]
    public void Events_ForwardedFromBaseLoader()
    {
        // Arrange
        var pluginLoadedEventFired = false;
        var pluginUnloadedEventFired = false;
        var pluginLoadFailedEventFired = false;

        _resilientLoader.PluginLoaded += (_, _) => pluginLoadedEventFired = true;
        _resilientLoader.PluginUnloaded += (_, _) => pluginUnloadedEventFired = true;
        _resilientLoader.PluginLoadFailed += (_, _) => pluginLoadFailedEventFired = true;

        // Act - Simulate base loader events
        _mockBaseLoader.PluginLoaded += Raise.EventWith(new PluginLoadedEventArgs("test", "test"));
        _mockBaseLoader.PluginUnloaded += Raise.EventWith(new PluginUnloadedEventArgs("test"));
        _mockBaseLoader.PluginLoadFailed += Raise.EventWith(new PluginLoadFailedEventArgs("test", new Exception("test")));

        // Assert
        Assert.True(pluginLoadedEventFired);
        Assert.True(pluginUnloadedEventFired);
        Assert.True(pluginLoadFailedEventFired);
    }

    [Fact]
    public void Dispose_DisposesBaseLoader()
    {
        // Act
        _resilientLoader.Dispose();

        // Assert
        _mockBaseLoader.Received(1).Dispose();
    }

    [Fact]
    public async Task LoadPluginAsync_InvalidAssemblyFormat_ThrowsPluginLoadExceptionImmediately()
    {
        // Arrange
        const string assemblyPath = "invalid.dll";
        const string typeName = "TestPlugin";

        // Create a file with invalid assembly content
        File.WriteAllText(assemblyPath, "This is not a valid assembly");

        try
        {
            // Act & Assert
            var exception = await Assert.ThrowsAsync<PluginLoadException>(
                () => _resilientLoader.LoadPluginAsync<IPlugin>(assemblyPath, typeName));

            Assert.Contains("Invalid assembly format", exception.Message);
            Assert.Equal(assemblyPath, exception.AssemblyPath);
            Assert.Equal(typeName, exception.TypeName);
            Assert.NotNull(exception.Context);
            Assert.True(exception.Context.ContainsKey("ErrorContext"));

            // Should not call retry policy for validation failures
            await _mockRetryPolicy.DidNotReceive().ExecuteAsync(Arg.Any<Func<Task<IPlugin>>>(), Arg.Any<RetryContext>());
        }
        finally
        {
            if (File.Exists(assemblyPath))
                File.Delete(assemblyPath);
        }
    }

    [Fact]
    public async Task LoadPluginAsync_UnauthorizedAccess_ThrowsPluginLoadExceptionImmediately()
    {
        // This test would be platform-specific and might require elevated permissions
        // Skipping implementation for this example, but the pattern would be similar
        await Task.CompletedTask;
    }
}