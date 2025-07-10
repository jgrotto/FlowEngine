using FlowEngine.Abstractions.Plugins;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using Jint;
using Jint.Native;

namespace FlowEngine.Core.Diagnostics;

/// <summary>
/// Service that monitors FlowEngine system health and provides diagnostic information.
/// Implements comprehensive health checks for production monitoring and alerting.
/// </summary>
public sealed class HealthCheckService : IDisposable
{
    private readonly ILogger<HealthCheckService> _logger;
    private readonly IPluginManager? _pluginManager;
    private readonly Timer _healthCheckTimer;
    private readonly object _lock = new();
    private readonly Dictionary<string, HealthCheckResult> _lastResults = new();
    private bool _disposed;

    /// <summary>
    /// Event raised when health status changes.
    /// </summary>
    public event EventHandler<HealthStatusChangedEventArgs>? HealthStatusChanged;

    /// <summary>
    /// Gets the current overall health status.
    /// </summary>
    public HealthStatus CurrentHealthStatus { get; private set; } = HealthStatus.Unknown;

    /// <summary>
    /// Gets the configuration for health checks.
    /// </summary>
    public HealthCheckConfiguration Configuration { get; }

    public HealthCheckService(
        ILogger<HealthCheckService> logger, 
        HealthCheckConfiguration? configuration = null,
        IPluginManager? pluginManager = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _pluginManager = pluginManager;
        Configuration = configuration ?? new HealthCheckConfiguration();
        
        // Start periodic health checks
        _healthCheckTimer = new Timer(PerformHealthCheckAsync, null, 
            Configuration.InitialDelay, Configuration.CheckInterval);
        
        _logger.LogInformation("Health check service started with interval {Interval}ms", 
            Configuration.CheckInterval.TotalMilliseconds);
    }

    /// <summary>
    /// Performs a comprehensive health check of the FlowEngine system.
    /// </summary>
    public async Task<SystemHealthReport> CheckSystemHealthAsync()
    {
        var stopwatch = Stopwatch.StartNew();
        var checks = new List<HealthCheckResult>();
        
        try
        {
            _logger.LogDebug("Starting system health check");
            
            // Perform individual health checks
            checks.Add(await CheckMemoryHealthAsync());
            checks.Add(await CheckCpuHealthAsync());
            checks.Add(await CheckPluginHealthAsync());
            checks.Add(await CheckJavaScriptEngineHealthAsync());
            checks.Add(await CheckFileSystemHealthAsync());
            checks.Add(await CheckThreadPoolHealthAsync());
            checks.Add(await CheckGarbageCollectionHealthAsync());

            // Determine overall health status
            var overallStatus = DetermineOverallStatus(checks);
            var previousStatus = CurrentHealthStatus;
            CurrentHealthStatus = overallStatus;

            // Create health report
            var report = new SystemHealthReport
            {
                OverallStatus = overallStatus,
                CheckResults = checks.AsReadOnly(),
                CheckDuration = stopwatch.Elapsed,
                Timestamp = DateTimeOffset.UtcNow,
                SystemInfo = GatherSystemInfo()
            };

            // Update last results and raise events if status changed
            lock (_lock)
            {
                foreach (var result in checks)
                {
                    _lastResults[result.CheckName] = result;
                }
            }

            if (previousStatus != overallStatus)
            {
                RaiseHealthStatusChanged(previousStatus, overallStatus, report);
            }

            _logger.LogInformation("System health check completed in {Duration}ms. Status: {Status}", 
                stopwatch.ElapsedMilliseconds, overallStatus);

            return report;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during system health check");
            
            var errorResult = new HealthCheckResult("SystemHealthCheck", HealthStatus.Unhealthy, 
                $"Health check failed: {ex.Message}", stopwatch.Elapsed, new Dictionary<string, object>
                {
                    ["Exception"] = ex.GetType().Name,
                    ["ErrorMessage"] = ex.Message
                });

            return new SystemHealthReport
            {
                OverallStatus = HealthStatus.Unhealthy,
                CheckResults = new[] { errorResult },
                CheckDuration = stopwatch.Elapsed,
                Timestamp = DateTimeOffset.UtcNow,
                SystemInfo = GatherSystemInfo()
            };
        }
    }

    /// <summary>
    /// Gets the last health check results for monitoring systems.
    /// </summary>
    public IReadOnlyDictionary<string, HealthCheckResult> GetLastResults()
    {
        lock (_lock)
        {
            return _lastResults.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
        }
    }

    /// <summary>
    /// Checks if a specific component is healthy.
    /// </summary>
    public async Task<HealthCheckResult> CheckComponentHealthAsync(string componentName)
    {
        return componentName.ToLowerInvariant() switch
        {
            "memory" => await CheckMemoryHealthAsync(),
            "cpu" => await CheckCpuHealthAsync(),
            "plugins" => await CheckPluginHealthAsync(),
            "javascript" => await CheckJavaScriptEngineHealthAsync(),
            "filesystem" => await CheckFileSystemHealthAsync(),
            "threadpool" => await CheckThreadPoolHealthAsync(),
            "gc" => await CheckGarbageCollectionHealthAsync(),
            _ => new HealthCheckResult(componentName, HealthStatus.Unknown, 
                $"Unknown component: {componentName}", TimeSpan.Zero)
        };
    }

    #region Individual Health Checks

    private async Task<HealthCheckResult> CheckMemoryHealthAsync()
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var totalMemory = GC.GetTotalMemory(false);
            var workingSet = Environment.WorkingSet;
            var gen2Collections = GC.CollectionCount(2);
            
            // Calculate memory pressure (simplified)
            var pressureRatio = totalMemory / (double)workingSet;
            
            var status = pressureRatio switch
            {
                > 0.9 => HealthStatus.Unhealthy,
                > 0.7 => HealthStatus.Degraded,
                _ => HealthStatus.Healthy
            };

            var message = status switch
            {
                HealthStatus.Unhealthy => $"High memory pressure: {pressureRatio:P1}",
                HealthStatus.Degraded => $"Moderate memory pressure: {pressureRatio:P1}",
                _ => $"Memory usage normal: {pressureRatio:P1}"
            };

            return new HealthCheckResult("Memory", status, message, stopwatch.Elapsed, new Dictionary<string, object>
            {
                ["TotalMemoryMB"] = totalMemory / (1024.0 * 1024.0),
                ["WorkingSetMB"] = workingSet / (1024.0 * 1024.0),
                ["PressureRatio"] = pressureRatio,
                ["Gen2Collections"] = gen2Collections
            });
        }
        catch (Exception ex)
        {
            return new HealthCheckResult("Memory", HealthStatus.Unhealthy, 
                $"Memory check failed: {ex.Message}", stopwatch.Elapsed);
        }
    }

    private async Task<HealthCheckResult> CheckCpuHealthAsync()
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            // Get CPU usage (simplified - in production would use performance counters)
            var process = Process.GetCurrentProcess();
            var cpuTime = process.TotalProcessorTime;
            
            // For this example, we'll check thread count as a CPU health proxy
            var threadCount = process.Threads.Count;
            var maxThreads = Environment.ProcessorCount * 4; // Rough heuristic
            
            var status = threadCount switch
            {
                var count when count > maxThreads => HealthStatus.Unhealthy,
                var count when count > maxThreads * 0.7 => HealthStatus.Degraded,
                _ => HealthStatus.Healthy
            };

            var message = $"Thread count: {threadCount}/{maxThreads}";

            return new HealthCheckResult("CPU", status, message, stopwatch.Elapsed, new Dictionary<string, object>
            {
                ["ThreadCount"] = threadCount,
                ["MaxThreads"] = maxThreads,
                ["ProcessorCount"] = Environment.ProcessorCount,
                ["TotalProcessorTime"] = cpuTime.TotalMilliseconds
            });
        }
        catch (Exception ex)
        {
            return new HealthCheckResult("CPU", HealthStatus.Unhealthy, 
                $"CPU check failed: {ex.Message}", stopwatch.Elapsed);
        }
    }

    private async Task<HealthCheckResult> CheckPluginHealthAsync()
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            if (_pluginManager == null)
            {
                return new HealthCheckResult("Plugins", HealthStatus.Unknown, 
                    "Plugin manager not available", stopwatch.Elapsed);
            }

            var loadedPlugins = _pluginManager.GetAllPlugins();
            var pluginInfo = _pluginManager.GetPluginInfo();
            
            var failedPlugins = pluginInfo.Where(p => p.Status == PluginStatus.Faulted).ToArray();
            
            var status = failedPlugins.Any() ? HealthStatus.Degraded : HealthStatus.Healthy;
            var message = failedPlugins.Any() 
                ? $"{failedPlugins.Length} plugins failed out of {pluginInfo.Count}"
                : $"{loadedPlugins.Count} plugins loaded successfully";

            return new HealthCheckResult("Plugins", status, message, stopwatch.Elapsed, new Dictionary<string, object>
            {
                ["LoadedPlugins"] = loadedPlugins.Count,
                ["TotalPlugins"] = pluginInfo.Count,
                ["FailedPlugins"] = failedPlugins.Length,
                ["FailedPluginNames"] = failedPlugins.Select(p => p.Id).ToArray()
            });
        }
        catch (Exception ex)
        {
            return new HealthCheckResult("Plugins", HealthStatus.Unhealthy, 
                $"Plugin check failed: {ex.Message}", stopwatch.Elapsed);
        }
    }

    private async Task<HealthCheckResult> CheckJavaScriptEngineHealthAsync()
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            // Simple JavaScript engine health test
            var testScript = "2 + 2";
            var engine = new Jint.Engine();
            var result = engine.Evaluate(testScript);
            
            var isHealthy = result.AsNumber() == 4;
            var status = isHealthy ? HealthStatus.Healthy : HealthStatus.Unhealthy;
            var message = isHealthy ? "JavaScript engine functional" : "JavaScript engine test failed";

            return new HealthCheckResult("JavaScript", status, message, stopwatch.Elapsed, new Dictionary<string, object>
            {
                ["TestScript"] = testScript,
                ["TestResult"] = result?.ToString() ?? "null",
                ["ExecutionTime"] = stopwatch.ElapsedMilliseconds
            });
        }
        catch (Exception ex)
        {
            return new HealthCheckResult("JavaScript", HealthStatus.Unhealthy, 
                $"JavaScript engine check failed: {ex.Message}", stopwatch.Elapsed);
        }
    }

    private async Task<HealthCheckResult> CheckFileSystemHealthAsync()
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var tempPath = Path.GetTempPath();
            var testFile = Path.Combine(tempPath, $"flowengine_health_{Guid.NewGuid():N}.tmp");
            
            // Test file operations
            await File.WriteAllTextAsync(testFile, "health check");
            var content = await File.ReadAllTextAsync(testFile);
            File.Delete(testFile);
            
            var isHealthy = content == "health check";
            var status = isHealthy ? HealthStatus.Healthy : HealthStatus.Unhealthy;
            var message = isHealthy ? "File system operations functional" : "File system test failed";

            // Check disk space
            var drive = new DriveInfo(Path.GetPathRoot(tempPath)!);
            var freeSpaceGB = drive.AvailableFreeSpace / (1024.0 * 1024.0 * 1024.0);
            
            if (freeSpaceGB < 1.0) // Less than 1GB free
            {
                status = HealthStatus.Unhealthy;
                message = $"Low disk space: {freeSpaceGB:F1} GB free";
            }
            else if (freeSpaceGB < 5.0) // Less than 5GB free
            {
                status = HealthStatus.Degraded;
                message = $"Moderate disk space: {freeSpaceGB:F1} GB free";
            }

            return new HealthCheckResult("FileSystem", status, message, stopwatch.Elapsed, new Dictionary<string, object>
            {
                ["TempPath"] = tempPath,
                ["FreeSpaceGB"] = freeSpaceGB,
                ["TotalSpaceGB"] = drive.TotalSize / (1024.0 * 1024.0 * 1024.0),
                ["UsagePercent"] = (1 - (double)drive.AvailableFreeSpace / drive.TotalSize) * 100
            });
        }
        catch (Exception ex)
        {
            return new HealthCheckResult("FileSystem", HealthStatus.Unhealthy, 
                $"File system check failed: {ex.Message}", stopwatch.Elapsed);
        }
    }

    private async Task<HealthCheckResult> CheckThreadPoolHealthAsync()
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            ThreadPool.GetAvailableThreads(out var availableWorker, out var availableCompletion);
            ThreadPool.GetMaxThreads(out var maxWorker, out var maxCompletion);
            
            var workerUsage = (maxWorker - availableWorker) / (double)maxWorker;
            var completionUsage = (maxCompletion - availableCompletion) / (double)maxCompletion;
            
            var maxUsage = Math.Max(workerUsage, completionUsage);
            
            var status = maxUsage switch
            {
                > 0.9 => HealthStatus.Unhealthy,
                > 0.7 => HealthStatus.Degraded,
                _ => HealthStatus.Healthy
            };

            var message = $"Thread pool usage: {maxUsage:P1}";

            return new HealthCheckResult("ThreadPool", status, message, stopwatch.Elapsed, new Dictionary<string, object>
            {
                ["AvailableWorkerThreads"] = availableWorker,
                ["MaxWorkerThreads"] = maxWorker,
                ["WorkerUsagePercent"] = workerUsage * 100,
                ["AvailableCompletionThreads"] = availableCompletion,
                ["MaxCompletionThreads"] = maxCompletion,
                ["CompletionUsagePercent"] = completionUsage * 100
            });
        }
        catch (Exception ex)
        {
            return new HealthCheckResult("ThreadPool", HealthStatus.Unhealthy, 
                $"Thread pool check failed: {ex.Message}", stopwatch.Elapsed);
        }
    }

    private async Task<HealthCheckResult> CheckGarbageCollectionHealthAsync()
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var gen0 = GC.CollectionCount(0);
            var gen1 = GC.CollectionCount(1);
            var gen2 = GC.CollectionCount(2);
            
            // Check for excessive Gen2 collections (simplified heuristic)
            var totalMemory = GC.GetTotalMemory(false);
            var gcPressure = gen2 / Math.Max(1.0, totalMemory / (1024.0 * 1024.0)); // Collections per MB
            
            var status = gcPressure switch
            {
                > 0.1 => HealthStatus.Unhealthy,
                > 0.05 => HealthStatus.Degraded,
                _ => HealthStatus.Healthy
            };

            var message = $"GC pressure: {gcPressure:F4} collections/MB";

            return new HealthCheckResult("GarbageCollection", status, message, stopwatch.Elapsed, new Dictionary<string, object>
            {
                ["Gen0Collections"] = gen0,
                ["Gen1Collections"] = gen1,
                ["Gen2Collections"] = gen2,
                ["TotalMemoryMB"] = totalMemory / (1024.0 * 1024.0),
                ["GCPressure"] = gcPressure
            });
        }
        catch (Exception ex)
        {
            return new HealthCheckResult("GarbageCollection", HealthStatus.Unhealthy, 
                $"GC check failed: {ex.Message}", stopwatch.Elapsed);
        }
    }

    #endregion

    private HealthStatus DetermineOverallStatus(IEnumerable<HealthCheckResult> results)
    {
        var resultArray = results.ToArray();
        
        if (resultArray.Any(r => r.Status == HealthStatus.Unhealthy))
            return HealthStatus.Unhealthy;
            
        if (resultArray.Any(r => r.Status == HealthStatus.Degraded))
            return HealthStatus.Degraded;
            
        if (resultArray.All(r => r.Status == HealthStatus.Healthy))
            return HealthStatus.Healthy;
            
        return HealthStatus.Unknown;
    }

    private SystemInfo GatherSystemInfo()
    {
        return new SystemInfo
        {
            MachineName = Environment.MachineName,
            ProcessorCount = Environment.ProcessorCount,
            OSVersion = Environment.OSVersion.ToString(),
            RuntimeVersion = Environment.Version.ToString(),
            WorkingSet = Environment.WorkingSet,
            ProcessId = Environment.ProcessId,
            UserName = Environment.UserName,
            Uptime = DateTimeOffset.UtcNow - Process.GetCurrentProcess().StartTime
        };
    }

    private void RaiseHealthStatusChanged(HealthStatus previousStatus, HealthStatus newStatus, SystemHealthReport report)
    {
        try
        {
            _logger.LogInformation("Health status changed from {PreviousStatus} to {NewStatus}", 
                previousStatus, newStatus);
                
            HealthStatusChanged?.Invoke(this, new HealthStatusChangedEventArgs(previousStatus, newStatus, report));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error raising health status changed event");
        }
    }

    private async void PerformHealthCheckAsync(object? state)
    {
        try
        {
            await CheckSystemHealthAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during periodic health check");
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        
        try
        {
            _healthCheckTimer?.Dispose();
            _logger.LogInformation("Health check service disposed");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error during health check service disposal");
        }
        finally
        {
            _disposed = true;
        }
    }
}

/// <summary>
/// Represents the health status of a component or system.
/// </summary>
public enum HealthStatus
{
    /// <summary>
    /// Health status is unknown or cannot be determined.
    /// </summary>
    Unknown = 0,
    
    /// <summary>
    /// Component is functioning normally.
    /// </summary>
    Healthy = 1,
    
    /// <summary>
    /// Component is functioning but with reduced performance or capabilities.
    /// </summary>
    Degraded = 2,
    
    /// <summary>
    /// Component is not functioning properly and requires attention.
    /// </summary>
    Unhealthy = 3
}

/// <summary>
/// Configuration for health check behavior.
/// </summary>
public sealed class HealthCheckConfiguration
{
    /// <summary>
    /// Gets or sets the interval between health checks.
    /// </summary>
    public TimeSpan CheckInterval { get; set; } = TimeSpan.FromMinutes(1);
    
    /// <summary>
    /// Gets or sets the initial delay before the first health check.
    /// </summary>
    public TimeSpan InitialDelay { get; set; } = TimeSpan.FromSeconds(10);
    
    /// <summary>
    /// Gets or sets the timeout for individual health checks.
    /// </summary>
    public TimeSpan CheckTimeout { get; set; } = TimeSpan.FromSeconds(30);
    
    /// <summary>
    /// Gets or sets whether to include detailed system information.
    /// </summary>
    public bool IncludeSystemInfo { get; set; } = true;
    
    /// <summary>
    /// Gets or sets the memory pressure threshold for unhealthy status.
    /// </summary>
    public double MemoryPressureThreshold { get; set; } = 0.9;
    
    /// <summary>
    /// Gets or sets the minimum free disk space in GB before marking as unhealthy.
    /// </summary>
    public double MinimumFreeDiskSpaceGB { get; set; } = 1.0;
}

/// <summary>
/// Result of an individual health check.
/// </summary>
public sealed class HealthCheckResult
{
    /// <summary>
    /// Gets the name of the health check.
    /// </summary>
    public string CheckName { get; }
    
    /// <summary>
    /// Gets the health status result.
    /// </summary>
    public HealthStatus Status { get; }
    
    /// <summary>
    /// Gets the descriptive message about the health check result.
    /// </summary>
    public string Message { get; }
    
    /// <summary>
    /// Gets the duration of the health check.
    /// </summary>
    public TimeSpan Duration { get; }
    
    /// <summary>
    /// Gets additional data collected during the health check.
    /// </summary>
    public IReadOnlyDictionary<string, object> Data { get; }
    
    /// <summary>
    /// Gets the timestamp when the health check was performed.
    /// </summary>
    public DateTimeOffset Timestamp { get; }

    public HealthCheckResult(string checkName, HealthStatus status, string message, TimeSpan duration, Dictionary<string, object>? data = null)
    {
        CheckName = checkName ?? throw new ArgumentNullException(nameof(checkName));
        Status = status;
        Message = message ?? throw new ArgumentNullException(nameof(message));
        Duration = duration;
        Data = data?.AsReadOnly() ?? new Dictionary<string, object>().AsReadOnly();
        Timestamp = DateTimeOffset.UtcNow;
    }
}

/// <summary>
/// Comprehensive system health report.
/// </summary>
public sealed class SystemHealthReport
{
    /// <summary>
    /// Gets or sets the overall system health status.
    /// </summary>
    public HealthStatus OverallStatus { get; set; }
    
    /// <summary>
    /// Gets or sets the individual health check results.
    /// </summary>
    public IReadOnlyList<HealthCheckResult> CheckResults { get; set; } = Array.Empty<HealthCheckResult>();
    
    /// <summary>
    /// Gets or sets the total duration of all health checks.
    /// </summary>
    public TimeSpan CheckDuration { get; set; }
    
    /// <summary>
    /// Gets or sets the timestamp when the health check was performed.
    /// </summary>
    public DateTimeOffset Timestamp { get; set; }
    
    /// <summary>
    /// Gets or sets system information.
    /// </summary>
    public SystemInfo SystemInfo { get; set; } = new();
}

/// <summary>
/// System information for health reports.
/// </summary>
public sealed class SystemInfo
{
    public string MachineName { get; set; } = string.Empty;
    public int ProcessorCount { get; set; }
    public string OSVersion { get; set; } = string.Empty;
    public string RuntimeVersion { get; set; } = string.Empty;
    public long WorkingSet { get; set; }
    public int ProcessId { get; set; }
    public string UserName { get; set; } = string.Empty;
    public TimeSpan Uptime { get; set; }
}

/// <summary>
/// Event arguments for health status change notifications.
/// </summary>
public sealed class HealthStatusChangedEventArgs : EventArgs
{
    public HealthStatus PreviousStatus { get; }
    public HealthStatus NewStatus { get; }
    public SystemHealthReport Report { get; }
    
    public HealthStatusChangedEventArgs(HealthStatus previousStatus, HealthStatus newStatus, SystemHealthReport report)
    {
        PreviousStatus = previousStatus;
        NewStatus = newStatus;
        Report = report;
    }
}