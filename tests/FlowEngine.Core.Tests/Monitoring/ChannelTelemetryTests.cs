using FlowEngine.Abstractions;
using FlowEngine.Core.Data;
using FlowEngine.Core.Monitoring;
using Xunit;

namespace FlowEngine.Core.Tests.Monitoring;

public class ChannelTelemetryTests : IDisposable
{
    private readonly ChannelTelemetry _telemetry;
    private readonly Schema _testSchema;
    
    public ChannelTelemetryTests()
    {
        _telemetry = new ChannelTelemetry();
        _testSchema = Schema.GetOrCreate(new[]
        {
            new ColumnDefinition { Name = "Id", DataType = typeof(int), IsNullable = false },
            new ColumnDefinition { Name = "Name", DataType = typeof(string), IsNullable = false }
        });
    }

    [Fact]
    public void RecordWrite_UpdatesMetricsCorrectly()
    {
        // Arrange
        var channelId = "test-channel";
        var chunk = CreateTestChunk(5);
        var duration = TimeSpan.FromMilliseconds(100);

        // Act
        _telemetry.RecordWrite(channelId, chunk, duration);

        // Assert
        var metrics = _telemetry.GetChannelMetrics(channelId);
        Assert.NotNull(metrics);
        Assert.Equal(1, metrics.TotalChunksWritten);
        Assert.Equal(5, metrics.TotalRowsWritten);
        Assert.Equal(duration, metrics.MaxWriteLatency);
        Assert.True(metrics.TotalBytesWritten > 0);
    }

    [Fact]
    public void RecordRead_UpdatesMetricsCorrectly()
    {
        // Arrange
        var channelId = "test-channel";
        var chunk = CreateTestChunk(3);
        var duration = TimeSpan.FromMilliseconds(50);

        // Act
        _telemetry.RecordRead(channelId, chunk, duration);

        // Assert
        var metrics = _telemetry.GetChannelMetrics(channelId);
        Assert.NotNull(metrics);
        Assert.Equal(1, metrics.TotalChunksRead);
        Assert.Equal(3, metrics.TotalRowsRead);
        Assert.Equal(duration, metrics.MaxReadLatency);
    }

    [Fact]
    public void RecordBackpressure_TracksCorrectly()
    {
        // Arrange
        var channelId = "test-channel";
        var waitTime = TimeSpan.FromMilliseconds(200);

        // Act
        _telemetry.RecordBackpressure(channelId, waitTime);

        // Assert
        var metrics = _telemetry.GetChannelMetrics(channelId);
        Assert.NotNull(metrics);
        Assert.Equal(1, metrics.BackpressureEvents);
        Assert.Equal(waitTime, metrics.TotalBackpressureTime);
        Assert.Equal(waitTime, metrics.MaxBackpressureWait);
    }

    [Fact]
    public void UpdateCapacity_TracksUtilization()
    {
        // Arrange
        var channelId = "test-channel";

        // Act
        _telemetry.UpdateCapacity(channelId, 80, 100);

        // Assert
        var metrics = _telemetry.GetChannelMetrics(channelId);
        Assert.NotNull(metrics);
        Assert.Equal(80, metrics.CurrentCapacity);
        Assert.Equal(100, metrics.MaxCapacity);
        Assert.Equal(80.0, metrics.PeakCapacityUtilization);
    }

    [Fact]
    public void RecordCompletion_MarksChannelCompleted()
    {
        // Arrange
        var channelId = "test-channel";

        // Act
        _telemetry.RecordCompletion(channelId);

        // Assert
        var metrics = _telemetry.GetChannelMetrics(channelId);
        Assert.NotNull(metrics);
        Assert.True(metrics.IsCompleted);
        Assert.NotNull(metrics.CompletedAt);
    }

    [Fact]
    public void GetAllMetrics_ReturnsAllChannels()
    {
        // Arrange
        var chunk = CreateTestChunk(1);
        _telemetry.RecordWrite("channel1", chunk, TimeSpan.FromMilliseconds(10));
        _telemetry.RecordWrite("channel2", chunk, TimeSpan.FromMilliseconds(20));

        // Act
        var allMetrics = _telemetry.GetAllMetrics();

        // Assert
        Assert.Equal(2, allMetrics.Count);
        Assert.Contains("channel1", allMetrics.Keys);
        Assert.Contains("channel2", allMetrics.Keys);
    }

    [Fact]
    public async Task ThroughputCalculation_UpdatesOverTime()
    {
        // Arrange
        var channelId = "throughput-test";
        var chunk = CreateTestChunk(100);

        // Act - Record multiple writes
        for (int i = 0; i < 5; i++)
        {
            _telemetry.RecordWrite(channelId, chunk, TimeSpan.FromMilliseconds(10));
            await Task.Delay(100); // Small delay to allow metrics updates
        }

        // Wait for metrics to update
        await Task.Delay(1100); // Wait longer than the 1-second update interval

        // Assert
        var metrics = _telemetry.GetChannelMetrics(channelId);
        Assert.NotNull(metrics);
        Assert.Equal(500, metrics.TotalRowsWritten);
        Assert.True(metrics.CurrentThroughputRowsPerSecond > 0);
    }

    private Chunk CreateTestChunk(int rowCount)
    {
        var rows = new ArrayRow[rowCount];
        for (int i = 0; i < rowCount; i++)
        {
            rows[i] = new ArrayRow(_testSchema, new object[] { i, $"Name{i}" });
        }
        return new Chunk(_testSchema, rows);
    }

    public void Dispose()
    {
        _telemetry?.Dispose();
    }
}