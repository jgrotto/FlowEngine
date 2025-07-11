using FlowEngine.Core.Monitoring;
using Xunit;

namespace FlowEngine.Core.Tests.Monitoring;

public class PipelinePerformanceMonitorTests : IDisposable
{
    private readonly ChannelTelemetry _channelTelemetry;
    private readonly PipelinePerformanceMonitor _monitor;

    public PipelinePerformanceMonitorTests()
    {
        _channelTelemetry = new ChannelTelemetry();
        _monitor = new PipelinePerformanceMonitor(_channelTelemetry);
    }

    [Fact]
    public void RecordPluginExecution_UpdatesMetrics()
    {
        // Arrange
        var pluginName = "TestPlugin";
        var duration = TimeSpan.FromMilliseconds(150);
        var rowsProcessed = 1000L;
        var memoryUsed = 50_000_000L;

        // Act
        _monitor.RecordPluginExecution(pluginName, duration, rowsProcessed, memoryUsed);

        // Assert
        var metrics = _monitor.GetPluginMetrics(pluginName);
        Assert.NotNull(metrics);
        Assert.Equal(1, metrics.TotalExecutions);
        Assert.Equal(duration, metrics.TotalProcessingTime);
        Assert.Equal(duration, metrics.MaxExecutionTime);
        Assert.Equal(rowsProcessed, metrics.TotalRowsProcessed);
        Assert.Equal(memoryUsed, metrics.CurrentMemoryUsage);
        Assert.Equal(memoryUsed, metrics.PeakMemoryUsage);
    }

    [Fact]
    public void RecordPluginError_TracksErrors()
    {
        // Arrange
        var pluginName = "ErrorPlugin";
        var exception = new InvalidOperationException("Test error");

        // Act
        _monitor.RecordPluginError(pluginName, exception);

        // Assert
        var metrics = _monitor.GetPluginMetrics(pluginName);
        Assert.NotNull(metrics);
        Assert.Equal(1, metrics.ErrorCount);
        Assert.Equal("Test error", metrics.LastError);
        Assert.NotNull(metrics.LastErrorTime);
    }

    [Fact]
    public void GetPerformanceReport_ReturnsComprehensiveData()
    {
        // Arrange
        _monitor.RecordPluginExecution("Plugin1", TimeSpan.FromMilliseconds(100), 500, 25_000_000);
        _monitor.RecordPluginExecution("Plugin2", TimeSpan.FromMilliseconds(200), 250, 15_000_000);

        // Act
        var report = _monitor.GetPerformanceReport();

        // Assert
        Assert.NotNull(report);
        Assert.True(report.Timestamp <= DateTimeOffset.UtcNow);
        Assert.NotNull(report.ChannelMetrics);
        Assert.NotNull(report.PluginMetrics);
        Assert.NotNull(report.BottleneckAnalysis);
        Assert.NotNull(report.ResourceUsage);
        Assert.NotNull(report.Recommendations);

        Assert.Equal(2, report.PluginMetrics.Count);
        Assert.Contains("Plugin1", report.PluginMetrics.Keys);
        Assert.Contains("Plugin2", report.PluginMetrics.Keys);
    }

    [Fact]
    public void PerformanceAlert_RaisedForHighMemoryUsage()
    {
        // Arrange
        var alertsRaised = new List<PerformanceAlertEventArgs>();
        _monitor.PerformanceAlert += (sender, args) => alertsRaised.Add(args);

        // Act - Record high memory usage
        _monitor.RecordPluginExecution("MemoryHog", TimeSpan.FromMilliseconds(50), 100, 600_000_000); // 600MB

        // Wait for analysis timer to trigger
        Thread.Sleep(6000); // Wait longer than 5-second analysis interval

        // Assert
        Assert.True(alertsRaised.Count > 0, "Expected performance alerts to be raised");
        var memoryAlerts = alertsRaised.SelectMany(args => args.Alerts)
            .Where(alert => alert.Metric == "MemoryUsage")
            .ToList();
        Assert.True(memoryAlerts.Count > 0, "Expected memory usage alert");
    }

    [Fact]
    public void CalculateAverageTimes_WorksCorrectly()
    {
        // Arrange
        var pluginName = "AverageTestPlugin";

        // Act - Record multiple executions
        _monitor.RecordPluginExecution(pluginName, TimeSpan.FromMilliseconds(100), 100, 10_000_000);
        _monitor.RecordPluginExecution(pluginName, TimeSpan.FromMilliseconds(200), 100, 10_000_000);
        _monitor.RecordPluginExecution(pluginName, TimeSpan.FromMilliseconds(300), 100, 10_000_000);

        // Wait for metrics to be calculated
        Thread.Sleep(6000);

        // Assert
        var metrics = _monitor.GetPluginMetrics(pluginName);
        Assert.NotNull(metrics);
        Assert.Equal(3, metrics.TotalExecutions);
        Assert.Equal(TimeSpan.FromMilliseconds(600), metrics.TotalProcessingTime);
        Assert.Equal(TimeSpan.FromMilliseconds(300), metrics.MaxExecutionTime);

        // Average should be calculated (200ms)
        Assert.True(metrics.AverageExecutionTime.TotalMilliseconds > 150);
        Assert.True(metrics.AverageExecutionTime.TotalMilliseconds < 250);
    }

    [Fact]
    public void BottleneckAnalysis_IdentifiesSlowestPlugin()
    {
        // Arrange
        _monitor.RecordPluginExecution("FastPlugin", TimeSpan.FromMilliseconds(50), 1000, 10_000_000);
        _monitor.RecordPluginExecution("SlowPlugin", TimeSpan.FromMilliseconds(500), 100, 10_000_000);

        // Wait for average calculations
        Thread.Sleep(6000);

        // Act
        var report = _monitor.GetPerformanceReport();

        // Assert
        Assert.NotNull(report.BottleneckAnalysis);
        Assert.Contains("SlowPlugin", report.BottleneckAnalysis.SlowestComponent);
        Assert.True(report.BottleneckAnalysis.IdentifiedBottlenecks.Count > 0);
    }

    [Fact]
    public void ResourceUsage_ReturnsValidData()
    {
        // Act
        var report = _monitor.GetPerformanceReport();

        // Assert
        Assert.NotNull(report.ResourceUsage);
        Assert.True(report.ResourceUsage.MemoryUsageMB > 0);
        Assert.True(report.ResourceUsage.ThreadCount > 0);
        Assert.True(report.ResourceUsage.HandleCount > 0);
    }

    [Fact]
    public void GetPluginMetrics_ReturnsNullForUnknownPlugin()
    {
        // Act
        var metrics = _monitor.GetPluginMetrics("NonExistentPlugin");

        // Assert
        Assert.Null(metrics);
    }

    [Fact]
    public void ThroughputCalculation_WorksForPlugins()
    {
        // Arrange
        var pluginName = "ThroughputPlugin";

        // Act
        _monitor.RecordPluginExecution(pluginName, TimeSpan.FromSeconds(1), 1000, 10_000_000); // 1000 rows/sec
        _monitor.RecordPluginExecution(pluginName, TimeSpan.FromSeconds(2), 4000, 10_000_000); // 2000 rows/sec

        // Assert
        var metrics = _monitor.GetPluginMetrics(pluginName);
        Assert.NotNull(metrics);
        Assert.Equal(2000.0, metrics.PeakThroughput); // Should capture the higher throughput
    }

    public void Dispose()
    {
        _monitor?.Dispose();
        _channelTelemetry?.Dispose();
    }
}
