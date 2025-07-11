using FlowEngine.Abstractions.Data;
using FlowEngine.Abstractions.Plugins;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Text.Json;

namespace FlowEngine.Core.Diagnostics;

/// <summary>
/// Collects comprehensive diagnostic information for troubleshooting and performance analysis.
/// Provides detailed system state snapshots for production support.
/// </summary>
public sealed class DiagnosticCollector : IDisposable
{
    private readonly ILogger<DiagnosticCollector> _logger;
    private readonly IPluginManager? _pluginManager;
    private readonly HealthCheckService? _healthCheckService;
    private readonly object _lock = new();
    private readonly Dictionary<string, List<DiagnosticEvent>> _eventHistory = new();
    private readonly int _maxHistorySize;
    private bool _disposed;

    /// <summary>
    /// Gets the configuration for diagnostic collection.
    /// </summary>
    public DiagnosticConfiguration Configuration { get; }

    public DiagnosticCollector(
        ILogger<DiagnosticCollector> logger,
        DiagnosticConfiguration? configuration = null,
        IPluginManager? pluginManager = null,
        HealthCheckService? healthCheckService = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        Configuration = configuration ?? new DiagnosticConfiguration();
        _pluginManager = pluginManager;
        _healthCheckService = healthCheckService;
        _maxHistorySize = Configuration.MaxEventHistorySize;

        _logger.LogInformation("Diagnostic collector initialized with max history size: {MaxSize}", _maxHistorySize);
    }

    /// <summary>
    /// Collects a comprehensive diagnostic snapshot of the system.
    /// </summary>
    public async Task<DiagnosticSnapshot> CollectSnapshotAsync(string? reason = null)
    {
        var stopwatch = Stopwatch.StartNew();

        try
        {
            _logger.LogInformation("Collecting diagnostic snapshot. Reason: {Reason}", reason ?? "Manual");

            var snapshot = new DiagnosticSnapshot
            {
                Id = Guid.NewGuid(),
                Timestamp = DateTimeOffset.UtcNow,
                Reason = reason ?? "Manual collection",
                SystemInfo = await CollectSystemInfoAsync(),
                PerformanceMetrics = await CollectPerformanceMetricsAsync(),
                PluginDiagnostics = await CollectPluginDiagnosticsAsync(),
                ThreadInfo = CollectThreadInfo(),
                MemoryInfo = CollectMemoryInfo(),
                EnvironmentInfo = CollectEnvironmentInfo(),
                EventHistory = GetEventHistory(),
                HealthStatus = await CollectHealthStatusAsync(),
                CollectionDuration = stopwatch.Elapsed
            };

            // Record the snapshot collection as an event
            RecordEvent("DiagnosticSnapshot", "Snapshot collected", new Dictionary<string, object>
            {
                ["SnapshotId"] = snapshot.Id,
                ["Reason"] = snapshot.Reason,
                ["CollectionDuration"] = snapshot.CollectionDuration.TotalMilliseconds
            });

            _logger.LogInformation("Diagnostic snapshot collected in {Duration}ms. ID: {SnapshotId}",
                stopwatch.ElapsedMilliseconds, snapshot.Id);

            return snapshot;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error collecting diagnostic snapshot");

            return new DiagnosticSnapshot
            {
                Id = Guid.NewGuid(),
                Timestamp = DateTimeOffset.UtcNow,
                Reason = $"Error: {ex.Message}",
                SystemInfo = new SystemDiagnosticInfo { ErrorMessage = ex.Message },
                CollectionDuration = stopwatch.Elapsed
            };
        }
    }

    /// <summary>
    /// Records a diagnostic event for later analysis.
    /// </summary>
    public void RecordEvent(string category, string message, Dictionary<string, object>? data = null)
    {
        if (!Configuration.EnableEventRecording)
        {
            return;
        }

        var diagnosticEvent = new DiagnosticEvent
        {
            Id = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            Category = category,
            Message = message,
            Data = data?.AsReadOnly() ?? new Dictionary<string, object>().AsReadOnly(),
            ThreadId = Environment.CurrentManagedThreadId,
            ActivityId = Activity.Current?.Id
        };

        lock (_lock)
        {
            if (!_eventHistory.ContainsKey(category))
            {
                _eventHistory[category] = new List<DiagnosticEvent>();
            }

            var categoryHistory = _eventHistory[category];
            categoryHistory.Add(diagnosticEvent);

            // Trim history if it exceeds max size
            if (categoryHistory.Count > _maxHistorySize)
            {
                categoryHistory.RemoveRange(0, categoryHistory.Count - _maxHistorySize);
            }
        }

        _logger.LogDebug("Recorded diagnostic event: {Category} - {Message}", category, message);
    }

    /// <summary>
    /// Records performance metrics for a specific operation.
    /// </summary>
    public void RecordPerformanceMetrics(string operationName, TimeSpan duration, Dictionary<string, object>? metrics = null)
    {
        var perfData = new Dictionary<string, object>
        {
            ["OperationName"] = operationName,
            ["Duration"] = duration.TotalMilliseconds,
            ["ThreadId"] = Environment.CurrentManagedThreadId
        };

        if (metrics != null)
        {
            foreach (var kvp in metrics)
            {
                perfData[kvp.Key] = kvp.Value;
            }
        }

        RecordEvent("Performance", $"Operation completed: {operationName}", perfData);
    }

    /// <summary>
    /// Exports diagnostic data to a JSON file for external analysis.
    /// </summary>
    public async Task ExportDiagnosticsAsync(string filePath, DiagnosticSnapshot? snapshot = null)
    {
        try
        {
            var exportSnapshot = snapshot ?? await CollectSnapshotAsync("Export");

            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            };

            var json = JsonSerializer.Serialize(exportSnapshot, options);
            await File.WriteAllTextAsync(filePath, json);

            _logger.LogInformation("Diagnostic data exported to: {FilePath}", filePath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error exporting diagnostic data to {FilePath}", filePath);
            throw;
        }
    }

    #region Private Collection Methods

    private Task<SystemDiagnosticInfo> CollectSystemInfoAsync()
    {
        try
        {
            var process = Process.GetCurrentProcess();

            return Task.FromResult(new SystemDiagnosticInfo
            {
                MachineName = Environment.MachineName,
                ProcessorCount = Environment.ProcessorCount,
                OSVersion = Environment.OSVersion.ToString(),
                RuntimeVersion = Environment.Version.ToString(),
                ProcessId = Environment.ProcessId,
                ProcessName = process.ProcessName,
                StartTime = process.StartTime,
                Uptime = DateTime.UtcNow - process.StartTime,
                WorkingSet = Environment.WorkingSet,
                UserName = Environment.UserName,
                CommandLine = Environment.CommandLine,
                CurrentDirectory = Environment.CurrentDirectory
            });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error collecting system info");
            return Task.FromResult(new SystemDiagnosticInfo { ErrorMessage = ex.Message });
        }
    }

    private Task<PerformanceDiagnosticInfo> CollectPerformanceMetricsAsync()
    {
        try
        {
            var process = Process.GetCurrentProcess();

            return Task.FromResult(new PerformanceDiagnosticInfo
            {
                CpuTime = process.TotalProcessorTime,
                WorkingSet = process.WorkingSet64,
                PrivateMemorySize = process.PrivateMemorySize64,
                VirtualMemorySize = process.VirtualMemorySize64,
                ThreadCount = process.Threads.Count,
                HandleCount = process.HandleCount,
                GCTotalMemory = GC.GetTotalMemory(false),
                Gen0Collections = GC.CollectionCount(0),
                Gen1Collections = GC.CollectionCount(1),
                Gen2Collections = GC.CollectionCount(2)
            });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error collecting performance metrics");
            return Task.FromResult(new PerformanceDiagnosticInfo { ErrorMessage = ex.Message });
        }
    }

    private Task<PluginDiagnosticInfo> CollectPluginDiagnosticsAsync()
    {
        try
        {
            if (_pluginManager == null)
            {
                return Task.FromResult(new PluginDiagnosticInfo
                {
                    Message = "Plugin manager not available"
                });
            }

            var loadedPlugins = _pluginManager.GetAllPlugins();
            var pluginInfo = _pluginManager.GetPluginInfo();

            var pluginDetails = pluginInfo.Select(p => new PluginDetail
            {
                Name = p.Id,
                Version = p.Metadata?.GetValueOrDefault("Version")?.ToString() ?? "1.0.0",
                Status = p.Status.ToString(),
                AssemblyPath = p.AssemblyPath,
                TypeName = p.TypeName,
                LoadedAt = p.LoadedAt,
                ErrorMessage = p.Status == PluginStatus.Faulted ? "Plugin is in faulted state" : null
            }).ToArray();

            return Task.FromResult(new PluginDiagnosticInfo
            {
                LoadedPluginCount = loadedPlugins.Count,
                TotalPluginCount = pluginInfo.Count,
                FailedPluginCount = pluginInfo.Count(p => p.Status == PluginStatus.Faulted),
                PluginDetails = pluginDetails
            });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error collecting plugin diagnostics");
            return Task.FromResult(new PluginDiagnosticInfo { ErrorMessage = ex.Message });
        }
    }

    private ThreadDiagnosticInfo CollectThreadInfo()
    {
        try
        {
            var process = Process.GetCurrentProcess();
            var threads = process.Threads.Cast<ProcessThread>().ToArray();

            ThreadPool.GetAvailableThreads(out var availableWorkerThreads, out var availableCompletionPortThreads);
            ThreadPool.GetMaxThreads(out var maxWorkerThreads, out var maxCompletionPortThreads);

            return new ThreadDiagnosticInfo
            {
                TotalThreadCount = threads.Length,
                ManagedThreadId = Environment.CurrentManagedThreadId,
                AvailableWorkerThreads = availableWorkerThreads,
                MaxWorkerThreads = maxWorkerThreads,
                AvailableCompletionPortThreads = availableCompletionPortThreads,
                MaxCompletionPortThreads = maxCompletionPortThreads,
                ThreadDetails = threads.Take(10).Select(t => new ThreadDetail // Limit to first 10 threads
                {
                    Id = t.Id,
                    StartTime = t.StartTime,
                    TotalProcessorTime = t.TotalProcessorTime,
                    ThreadState = t.ThreadState.ToString()
                }).ToArray()
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error collecting thread info");
            return new ThreadDiagnosticInfo { ErrorMessage = ex.Message };
        }
    }

    private MemoryDiagnosticInfo CollectMemoryInfo()
    {
        try
        {
            return new MemoryDiagnosticInfo
            {
                TotalMemory = GC.GetTotalMemory(false),
                WorkingSet = Environment.WorkingSet,
                Gen0Collections = GC.CollectionCount(0),
                Gen1Collections = GC.CollectionCount(1),
                Gen2Collections = GC.CollectionCount(2),
                MemoryMappedFiles = new MemoryMappedFileInfo[0], // Placeholder - would implement in production
                LargeObjectHeapSize = GC.GetTotalMemory(false) // Simplified - would use proper LOH size in production
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error collecting memory info");
            return new MemoryDiagnosticInfo { ErrorMessage = ex.Message };
        }
    }

    private EnvironmentDiagnosticInfo CollectEnvironmentInfo()
    {
        try
        {
            var environmentVariables = Environment.GetEnvironmentVariables()
                .Cast<System.Collections.DictionaryEntry>()
                .Where(kvp => Configuration.IncludeEnvironmentVariables)
                .ToDictionary(kvp => kvp.Key.ToString(), kvp => kvp.Value?.ToString() ?? "");

            return new EnvironmentDiagnosticInfo
            {
                MachineName = Environment.MachineName,
                UserName = Environment.UserName,
                DomainName = Environment.UserDomainName,
                OSVersion = Environment.OSVersion.ToString(),
                CLRVersion = Environment.Version.ToString(),
                CurrentDirectory = Environment.CurrentDirectory,
                SystemDirectory = Environment.SystemDirectory,
                EnvironmentVariables = environmentVariables
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error collecting environment info");
            return new EnvironmentDiagnosticInfo { ErrorMessage = ex.Message };
        }
    }

    private IReadOnlyDictionary<string, IReadOnlyList<DiagnosticEvent>> GetEventHistory()
    {
        lock (_lock)
        {
            return _eventHistory.ToDictionary(
                kvp => kvp.Key,
                kvp => (IReadOnlyList<DiagnosticEvent>)kvp.Value.ToArray()
            );
        }
    }

    private async Task<SystemHealthReport?> CollectHealthStatusAsync()
    {
        try
        {
            if (_healthCheckService == null)
            {
                return null;
            }

            return await _healthCheckService.CheckSystemHealthAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error collecting health status");
            return null;
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
            lock (_lock)
            {
                _eventHistory.Clear();
            }

            _logger.LogInformation("Diagnostic collector disposed");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error during diagnostic collector disposal");
        }
        finally
        {
            _disposed = true;
        }
    }
}

/// <summary>
/// Configuration for diagnostic collection.
/// </summary>
public sealed class DiagnosticConfiguration
{
    /// <summary>
    /// Gets or sets whether to record diagnostic events.
    /// </summary>
    public bool EnableEventRecording { get; set; } = true;

    /// <summary>
    /// Gets or sets the maximum number of events to keep in history per category.
    /// </summary>
    public int MaxEventHistorySize { get; set; } = 1000;

    /// <summary>
    /// Gets or sets whether to include environment variables in diagnostics.
    /// </summary>
    public bool IncludeEnvironmentVariables { get; set; } = false;

    /// <summary>
    /// Gets or sets whether to include sensitive data in diagnostics.
    /// </summary>
    public bool IncludeSensitiveData { get; set; } = false;

    /// <summary>
    /// Gets or sets the timeout for diagnostic collection operations.
    /// </summary>
    public TimeSpan CollectionTimeout { get; set; } = TimeSpan.FromSeconds(30);
}

/// <summary>
/// Comprehensive diagnostic snapshot of the system.
/// </summary>
public sealed class DiagnosticSnapshot
{
    public Guid Id { get; set; }
    public DateTimeOffset Timestamp { get; set; }
    public string Reason { get; set; } = string.Empty;
    public SystemDiagnosticInfo SystemInfo { get; set; } = new();
    public PerformanceDiagnosticInfo PerformanceMetrics { get; set; } = new();
    public PluginDiagnosticInfo PluginDiagnostics { get; set; } = new();
    public ThreadDiagnosticInfo ThreadInfo { get; set; } = new();
    public MemoryDiagnosticInfo MemoryInfo { get; set; } = new();
    public EnvironmentDiagnosticInfo EnvironmentInfo { get; set; } = new();
    public IReadOnlyDictionary<string, IReadOnlyList<DiagnosticEvent>> EventHistory { get; set; } = new Dictionary<string, IReadOnlyList<DiagnosticEvent>>();
    public SystemHealthReport? HealthStatus { get; set; }
    public TimeSpan CollectionDuration { get; set; }
}

/// <summary>
/// Diagnostic event recorded during system operation.
/// </summary>
public sealed class DiagnosticEvent
{
    public Guid Id { get; set; }
    public DateTimeOffset Timestamp { get; set; }
    public string Category { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public IReadOnlyDictionary<string, object> Data { get; set; } = new Dictionary<string, object>();
    public int ThreadId { get; set; }
    public string? ActivityId { get; set; }
}

// Diagnostic info classes for different aspects of the system
public sealed class SystemDiagnosticInfo
{
    public string MachineName { get; set; } = string.Empty;
    public int ProcessorCount { get; set; }
    public string OSVersion { get; set; } = string.Empty;
    public string RuntimeVersion { get; set; } = string.Empty;
    public int ProcessId { get; set; }
    public string ProcessName { get; set; } = string.Empty;
    public DateTime StartTime { get; set; }
    public TimeSpan Uptime { get; set; }
    public long WorkingSet { get; set; }
    public string UserName { get; set; } = string.Empty;
    public string CommandLine { get; set; } = string.Empty;
    public string CurrentDirectory { get; set; } = string.Empty;
    public string? ErrorMessage { get; set; }
}

public sealed class PerformanceDiagnosticInfo
{
    public TimeSpan CpuTime { get; set; }
    public long WorkingSet { get; set; }
    public long PrivateMemorySize { get; set; }
    public long VirtualMemorySize { get; set; }
    public int ThreadCount { get; set; }
    public int HandleCount { get; set; }
    public long GCTotalMemory { get; set; }
    public int Gen0Collections { get; set; }
    public int Gen1Collections { get; set; }
    public int Gen2Collections { get; set; }
    public string? ErrorMessage { get; set; }
}

public sealed class PluginDiagnosticInfo
{
    public int LoadedPluginCount { get; set; }
    public int TotalPluginCount { get; set; }
    public int FailedPluginCount { get; set; }
    public PluginDetail[] PluginDetails { get; set; } = Array.Empty<PluginDetail>();
    public string? Message { get; set; }
    public string? ErrorMessage { get; set; }
}

public sealed class PluginDetail
{
    public string Name { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string AssemblyPath { get; set; } = string.Empty;
    public string TypeName { get; set; } = string.Empty;
    public DateTimeOffset LoadedAt { get; set; }
    public string? ErrorMessage { get; set; }
}

public sealed class ThreadDiagnosticInfo
{
    public int TotalThreadCount { get; set; }
    public int ManagedThreadId { get; set; }
    public int AvailableWorkerThreads { get; set; }
    public int MaxWorkerThreads { get; set; }
    public int AvailableCompletionPortThreads { get; set; }
    public int MaxCompletionPortThreads { get; set; }
    public ThreadDetail[] ThreadDetails { get; set; } = Array.Empty<ThreadDetail>();
    public string? ErrorMessage { get; set; }
}

public sealed class ThreadDetail
{
    public int Id { get; set; }
    public DateTime StartTime { get; set; }
    public TimeSpan TotalProcessorTime { get; set; }
    public string ThreadState { get; set; } = string.Empty;
}

public sealed class MemoryDiagnosticInfo
{
    public long TotalMemory { get; set; }
    public long WorkingSet { get; set; }
    public int Gen0Collections { get; set; }
    public int Gen1Collections { get; set; }
    public int Gen2Collections { get; set; }
    public MemoryMappedFileInfo[] MemoryMappedFiles { get; set; } = Array.Empty<MemoryMappedFileInfo>();
    public long LargeObjectHeapSize { get; set; }
    public string? ErrorMessage { get; set; }
}

public sealed class MemoryMappedFileInfo
{
    public string Name { get; set; } = string.Empty;
    public long Size { get; set; }
}

public sealed class EnvironmentDiagnosticInfo
{
    public string MachineName { get; set; } = string.Empty;
    public string UserName { get; set; } = string.Empty;
    public string DomainName { get; set; } = string.Empty;
    public string OSVersion { get; set; } = string.Empty;
    public string CLRVersion { get; set; } = string.Empty;
    public string CurrentDirectory { get; set; } = string.Empty;
    public string SystemDirectory { get; set; } = string.Empty;
    public Dictionary<string, string> EnvironmentVariables { get; set; } = new();
    public string? ErrorMessage { get; set; }
}
