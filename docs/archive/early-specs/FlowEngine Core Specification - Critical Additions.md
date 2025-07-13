# FlowEngine Core Specification - Critical Additions
**Version**: 1.0  
**Status**: Draft  
**Purpose**: Complete missing implementation details from Core Engine Specification  
**Date**: June 27, 2025

---

## 1. Concrete Port Implementations

### 1.1 OutputPort Implementation

```csharp
/// <summary>
/// Concrete implementation of an output port that can send data to multiple connected input ports.
/// </summary>
public sealed class OutputPort : IOutputPort
{
    private readonly List<Connection> _connections = new();
    private readonly object _connectionLock = new();
    private readonly ILogger<OutputPort> _logger;
    
    public OutputPort(string name, PortMetadata metadata, ILogger<OutputPort> logger)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
        Metadata = metadata ?? throw new ArgumentNullException(nameof(metadata));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

---

## 7. Additional Implementation Details

### 7.1 Topological Sort Implementation

```csharp
/// <summary>
/// Performs topological sort on the job DAG to determine execution order.
/// </summary>
public sealed class TopologicalSorter
{
    private readonly ILogger<TopologicalSorter> _logger;
    
    public TopologicalSorter(ILogger<TopologicalSorter> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }
    
    /// <summary>
    /// Sorts steps into execution stages based on dependencies.
    /// </summary>
    public IReadOnlyList<ExecutionStage> Sort(JobDefinition job)
    {
        if (job == null)
            throw new ArgumentNullException(nameof(job));
        
        // Build dependency graph
        var dependencies = BuildDependencyGraph(job);
        var inDegree = CalculateInDegrees(job.Steps, dependencies);
        
        // Kahn's algorithm for topological sort
        var stages = new List<ExecutionStage>();
        var currentStage = new Queue<StepDefinition>();
        var processedCount = 0;
        var stageIndex = 0;
        
        // Find all nodes with no incoming edges
        foreach (var step in job.Steps)
        {
            if (inDegree[step.Id] == 0)
            {
                currentStage.Enqueue(step);
            }
        }
        
        while (currentStage.Count > 0)
        {
            var stageSteps = currentStage.ToList();
            currentStage.Clear();
            
            stages.Add(new ExecutionStage
            {
                StageIndex = stageIndex++,
                Steps = stageSteps
            });
            
            // Process all steps in current stage
            foreach (var step in stageSteps)
            {
                processedCount++;
                
                // Reduce in-degree for all dependent steps
                if (dependencies.TryGetValue(step.Id, out var dependents))
                {
                    foreach (var dependent in dependents)
                    {
                        inDegree[dependent]--;
                        
                        if (inDegree[dependent] == 0)
                        {
                            var dependentStep = job.Steps.First(s => s.Id == dependent);
                            currentStage.Enqueue(dependentStep);
                        }
                    }
                }
            }
        }
        
        if (processedCount != job.Steps.Count)
        {
            throw new InvalidOperationException(
                "Failed to create execution plan. The job DAG may contain cycles.");
        }
        
        _logger.LogInformation(
            "Created execution plan with {StageCount} stages for {StepCount} steps",
            stages.Count, job.Steps.Count);
        
        return stages;
    }
    
    private Dictionary<string, List<string>> BuildDependencyGraph(JobDefinition job)
    {
        var dependencies = new Dictionary<string, List<string>>();
        
        foreach (var connection in job.Connections)
        {
            var fromStep = ExtractStepId(connection.From);
            var toStep = ExtractStepId(connection.To);
            
            if (fromStep != null && toStep != null)
            {
                if (!dependencies.ContainsKey(fromStep))
                {
                    dependencies[fromStep] = new List<string>();
                }
                
                dependencies[fromStep].Add(toStep);
            }
        }
        
        return dependencies;
    }
    
    private Dictionary<string, int> CalculateInDegrees(
        IEnumerable<StepDefinition> steps,
        Dictionary<string, List<string>> dependencies)
    {
        var inDegree = new Dictionary<string, int>();
        
        // Initialize all steps with 0 in-degree
        foreach (var step in steps)
        {
            inDegree[step.Id] = 0;
        }
        
        // Calculate in-degrees
        foreach (var dependentsList in dependencies.Values)
        {
            foreach (var dependent in dependentsList)
            {
                if (inDegree.ContainsKey(dependent))
                {
                    inDegree[dependent]++;
                }
            }
        }
        
        return inDegree;
    }
    
    private string? ExtractStepId(string portReference)
    {
        var dotIndex = portReference.IndexOf('.');
        return dotIndex > 0 ? portReference.Substring(0, dotIndex) : null;
    }
}
```

### 7.2 Step Processor Interface for Port Access

```csharp
/// <summary>
/// Extended interface for step processors to expose their ports.
/// </summary>
public interface IStepProcessor
{
    /// <summary>
    /// Executes the step processing logic.
    /// </summary>
    Task ExecuteAsync(StepExecutionContext context);
    
    /// <summary>
    /// Gets all input ports for this step.
    /// </summary>
    IEnumerable<IInputPort> GetInputPorts();
    
    /// <summary>
    /// Gets all output ports for this step.
    /// </summary>
    IEnumerable<IOutputPort> GetOutputPorts();
    
    /// <summary>
    /// Initializes the processor before execution.
    /// </summary>
    Task InitializeAsync(CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Cleans up resources after execution.
    /// </summary>
    Task CleanupAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Base class for step processors providing common functionality.
/// </summary>
public abstract class StepProcessorBase : IStepProcessor
{
    private readonly Dictionary<string, IInputPort> _inputPorts = new();
    private readonly Dictionary<string, IOutputPort> _outputPorts = new();
    protected readonly ILogger Logger;
    
    protected StepProcessorBase(ILogger logger)
    {
        Logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }
    
    public abstract Task ExecuteAsync(StepExecutionContext context);
    
    public virtual Task InitializeAsync(CancellationToken cancellationToken = default)
        => Task.CompletedTask;
    
    public virtual Task CleanupAsync(CancellationToken cancellationToken = default)
        => Task.CompletedTask;
    
    public IEnumerable<IInputPort> GetInputPorts() => _inputPorts.Values;
    public IEnumerable<IOutputPort> GetOutputPorts() => _outputPorts.Values;
    
    protected void RegisterInputPort(string name, PortMetadata metadata)
    {
        var port = new InputPort(name, metadata, Logger as ILogger<InputPort>);
        _inputPorts[name] = port;
    }
    
    protected void RegisterOutputPort(string name, PortMetadata metadata)
    {
        var port = new OutputPort(name, metadata, Logger as ILogger<OutputPort>);
        _outputPorts[name] = port;
    }
    
    protected IInputPort GetInputPort(string name)
    {
        if (_inputPorts.TryGetValue(name, out var port))
            return port;
        
        throw new InvalidOperationException($"Input port '{name}' not found");
    }
    
    protected IOutputPort GetOutputPort(string name)
    {
        if (_outputPorts.TryGetValue(name, out var port))
            return port;
        
        throw new InvalidOperationException($"Output port '{name}' not found");
    }
}
```

### 7.3 Complete Error Context Management

```csharp
/// <summary>
/// Extended ErrorHandler with full context management.
/// </summary>
public sealed class ErrorHandler
{
    private readonly ErrorPolicy _policy;
    private readonly ILogger<ErrorHandler> _logger;
    private readonly IDeadLetterQueue? _deadLetterQueue;
    
    // Error tracking
    private int _consecutiveErrors;
    private long _totalErrors;
    private long _totalProcessed;
    
    // Context information
    private string? _jobExecutionId;
    private string? _stepExecutionId;
    private string? _currentStepId;
    private long? _currentRowIndex;
    
    public ErrorHandler(
        ErrorPolicy policy,
        ILogger<ErrorHandler> logger,
        IDeadLetterQueue? deadLetterQueue = null)
    {
        _policy = policy ?? throw new ArgumentNullException(nameof(policy));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _deadLetterQueue = deadLetterQueue;
    }
    
    /// <summary>
    /// Sets the current execution context.
    /// </summary>
    public void SetContext(string jobExecutionId, string stepExecutionId, string stepId)
    {
        _jobExecutionId = jobExecutionId;
        _stepExecutionId = stepExecutionId;
        _currentStepId = stepId;
    }
    
    /// <summary>
    /// Sets the current row index being processed.
    /// </summary>
    public void SetRowIndex(long rowIndex)
    {
        _currentRowIndex = rowIndex;
    }
    
    /// <summary>
    /// Handles an error according to the configured policy.
    /// </summary>
    public async ValueTask<bool> HandleErrorAsync(
        Exception error,
        ErrorContext context,
        CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _totalErrors);
        Interlocked.Increment(ref _consecutiveErrors);
        
        // Enrich context with current state
        context = context with
        {
            JobExecutionId = context.JobExecutionId ?? _jobExecutionId,
            StepExecutionId = context.StepExecutionId ?? _stepExecutionId,
            StepId = context.StepId ?? _currentStepId ?? "Unknown",
            RowIndex = context.RowIndex ?? _currentRowIndex
        };
        
        // Log the error with full context
        _logger.LogError(error,
            "Error in job {JobExecutionId}, step {StepId} at row {RowIndex}: {ErrorMessage}",
            context.JobExecutionId, context.StepId, context.RowIndex, error.Message);
        
        // Apply retry policy if configured
        if (_policy.RetryPolicy != null && context.RetryCount < _policy.RetryPolicy.MaxAttempts)
        {
            var delay = CalculateRetryDelay(context.RetryCount);
            _logger.LogInformation(
                "Retrying after {Delay}ms (attempt {Attempt} of {MaxAttempts})",
                delay.TotalMilliseconds, context.RetryCount + 1, _policy.RetryPolicy.MaxAttempts);
            
            await Task.Delay(delay, cancellationToken);
            return false; // Signal retry
        }
        
        // Send to dead letter queue if configured
        if (_deadLetterQueue != null && context.FaultingRow != null)
        {
            await SendToDeadLetterQueueAsync(context, error, cancellationToken);
        }
        
        // Check error policy thresholds
        if (_policy.StopOnError)
        {
            throw new ExecutionException("Execution stopped due to error policy", error)
            {
                JobExecutionId = context.JobExecutionId,
                StepExecutionId = context.StepExecutionId,
                StepId = context.StepId,
                RowIndex = context.RowIndex
            };
        }
        
        if (_consecutiveErrors > _policy.MaxConsecutiveErrors)
        {
            throw new ExecutionException(
                $"Exceeded maximum consecutive errors ({_policy.MaxConsecutiveErrors})", error)
            {
                JobExecutionId = context.JobExecutionId,
                StepExecutionId = context.StepExecutionId,
                StepId = context.StepId
            };
        }
        
        var errorRate = (double)_totalErrors / Math.Max(1, _totalProcessed);
        if (errorRate > _policy.MaxErrorRate)
        {
            throw new ExecutionException(
                $"Exceeded maximum error rate ({_policy.MaxErrorRate:P})", error)
            {
                JobExecutionId = context.JobExecutionId,
                StepExecutionId = context.StepExecutionId,
                StepId = context.StepId
            };
        }
        
        return true; // Continue processing
    }
    
    /// <summary>
    /// Records a successful row processing.
    /// </summary>
    public void RecordSuccess()
    {
        Interlocked.Exchange(ref _consecutiveErrors, 0);
        Interlocked.Increment(ref _totalProcessed);
    }
    
    /// <summary>
    /// Gets current error statistics.
    /// </summary>
    public ErrorStatistics GetStatistics()
    {
        return new ErrorStatistics
        {
            TotalErrors = Interlocked.Read(ref _totalErrors),
            TotalProcessed = Interlocked.Read(ref _totalProcessed),
            ConsecutiveErrors = _consecutiveErrors,
            ErrorRate = (double)_totalErrors / Math.Max(1, _totalProcessed)
        };
    }
    
    private TimeSpan CalculateRetryDelay(int retryCount)
    {
        if (_policy.RetryPolicy == null)
            return TimeSpan.Zero;
        
        var baseDelay = _policy.RetryPolicy.InitialDelay;
        var multiplier = Math.Pow(_policy.RetryPolicy.BackoffMultiplier, retryCount);
        var delay = TimeSpan.FromMilliseconds(baseDelay.TotalMilliseconds * multiplier);
        
        // Cap at maximum delay if specified
        if (_policy.RetryPolicy.MaxDelay.HasValue && delay > _policy.RetryPolicy.MaxDelay.Value)
        {
            delay = _policy.RetryPolicy.MaxDelay.Value;
        }
        
        return delay;
    }
    
    private async ValueTask SendToDeadLetterQueueAsync(
        ErrorContext context,
        Exception error,
        CancellationToken cancellationToken)
    {
        if (_deadLetterQueue == null || context.FaultingRow == null)
            return;
        
        var entry = new DeadLetterEntry
        {
            FailedRow = context.FaultingRow,
            ErrorMessage = error.Message,
            StepId = context.StepId ?? "Unknown",
            Timestamp = DateTimeOffset.UtcNow,
            JobExecutionId = context.JobExecutionId,
            StepExecutionId = context.StepExecutionId,
            OriginalRowIndex = context.RowIndex,
            Exception = error,
            AdditionalContext = new Dictionary<string, object>
            {
                ["PortName"] = context.PortName ?? "Unknown",
                ["RetryCount"] = context.RetryCount,
                ["ConsecutiveErrors"] = _consecutiveErrors
            }
        };
        
        try
        {
            await _deadLetterQueue.SendAsync(entry, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send row to dead letter queue");
            // Don't throw - dead letter queue failures shouldn't stop processing
        }
    }
}

/// <summary>
/// Enhanced error context with execution information.
/// </summary>
public sealed record ErrorContext
{
    public string? JobExecutionId { get; init; }
    public string? StepExecutionId { get; init; }
    public required string StepId { get; init; }
    public long? RowIndex { get; init; }
    public Row? FaultingRow { get; init; }
    public string? PortName { get; init; }
    public int RetryCount { get; init; }
}

/// <summary>
/// Error handling statistics.
/// </summary>
public sealed class ErrorStatistics
{
    public long TotalErrors { get; init; }
    public long TotalProcessed { get; init; }
    public int ConsecutiveErrors { get; init; }
    public double ErrorRate { get; init; }
}

/// <summary>
/// Retry policy configuration.
/// </summary>
public sealed class RetryPolicy
{
    public int MaxAttempts { get; init; } = 3;
    public TimeSpan InitialDelay { get; init; } = TimeSpan.FromSeconds(1);
    public double BackoffMultiplier { get; init; } = 2.0;
    public TimeSpan? MaxDelay { get; init; } = TimeSpan.FromMinutes(1);
}
```

---

## Summary

These critical additions complete the Core Engine specification with:

1. **Concrete Port Implementations**: Full `OutputPort` and `InputPort` classes with connection management
2. **DAG Cycle Detection**: Complete depth-first search algorithm implementation
3. **Dead Letter Queue**: File-based implementation with proper error context preservation
4. **Memory Allocation Pattern**: Clear disposal semantics with pooling support
5. **Connection Wiring**: Complete `ConnectionManager` with port registration and wiring
6. **Additional Support**: Topological sorting, base processor class, and enhanced error handling

With these additions, developers have all the necessary implementation details to begin building the FlowEngine core engine.
    
    public string Name { get; }
    public PortMetadata Metadata { get; }
    public bool IsConnected => ConnectionCount > 0;
    public int ConnectionCount => _connections.Count;
    
    public event EventHandler<ConnectionChangedEventArgs>? ConnectionChanged;
    
    /// <summary>
    /// Sends a dataset to all connected input ports.
    /// </summary>
    public async ValueTask SendAsync(IDataset dataset, CancellationToken cancellationToken = default)
    {
        if (dataset == null)
            throw new ArgumentNullException(nameof(dataset));
        
        Connection[] connections;
        lock (_connectionLock)
        {
            connections = _connections.ToArray();
        }
        
        if (connections.Length == 0)
        {
            _logger.LogDebug("Output port {PortName} has no connections, data will be discarded", Name);
            return;
        }
        
        // Send to all connections in parallel
        var sendTasks = connections.Select(conn => 
            SendToConnectionAsync(conn, dataset, cancellationToken)
        ).ToArray();
        
        await Task.WhenAll(sendTasks);
    }
    
    /// <summary>
    /// Adds a connection from this output port to an input port.
    /// </summary>
    internal void AddConnection(Connection connection)
    {
        if (connection == null)
            throw new ArgumentNullException(nameof(connection));
        
        lock (_connectionLock)
        {
            _connections.Add(connection);
        }
        
        ConnectionChanged?.Invoke(this, new ConnectionChangedEventArgs(
            ConnectionChangeType.Added, connection));
    }
    
    /// <summary>
    /// Removes a connection from this output port.
    /// </summary>
    internal void RemoveConnection(Connection connection)
    {
        if (connection == null)
            throw new ArgumentNullException(nameof(connection));
        
        bool removed;
        lock (_connectionLock)
        {
            removed = _connections.Remove(connection);
        }
        
        if (removed)
        {
            ConnectionChanged?.Invoke(this, new ConnectionChangedEventArgs(
                ConnectionChangeType.Removed, connection));
        }
    }
    
    /// <summary>
    /// Completes all connections, signaling no more data will be sent.
    /// </summary>
    internal void Complete()
    {
        Connection[] connections;
        lock (_connectionLock)
        {
            connections = _connections.ToArray();
        }
        
        foreach (var connection in connections)
        {
            connection.Complete();
        }
    }
    
    private async ValueTask SendToConnectionAsync(
        Connection connection, 
        IDataset dataset, 
        CancellationToken cancellationToken)
    {
        try
        {
            await connection.SendAsync(dataset, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, 
                "Failed to send dataset through connection on port {PortName}", Name);
            throw;
        }
    }
}
```

### 1.2 InputPort Implementation

```csharp
/// <summary>
/// Concrete implementation of an input port that receives data from connected output ports.
/// </summary>
public sealed class InputPort : IInputPort
{
    private readonly List<Connection> _connections = new();
    private readonly object _connectionLock = new();
    private readonly ILogger<InputPort> _logger;
    private int _completedConnections = 0;
    
    public InputPort(string name, PortMetadata metadata, ILogger<InputPort> logger)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
        Metadata = metadata ?? throw new ArgumentNullException(nameof(metadata));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }
    
    public string Name { get; }
    public PortMetadata Metadata { get; }
    public bool IsConnected => _connections.Count > 0;
    public bool IsCompleted => _completedConnections == _connections.Count && _connections.Count > 0;
    
    /// <summary>
    /// Receives datasets from all connected output ports.
    /// </summary>
    public async IAsyncEnumerable<IDataset> ReceiveAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        Connection[] connections;
        lock (_connectionLock)
        {
            connections = _connections.ToArray();
        }
        
        if (connections.Length == 0)
        {
            _logger.LogWarning("Input port {PortName} has no connections", Name);
            yield break;
        }
        
        // Create a channel to merge all incoming data
        var mergeChannel = Channel.CreateUnbounded<IDataset>();
        
        // Start tasks to read from all connections
        var readTasks = connections.Select(conn => 
            ReadFromConnectionAsync(conn, mergeChannel.Writer, cancellationToken)
        ).ToArray();
        
        // Start a task to complete the channel when all reads are done
        _ = Task.WhenAll(readTasks).ContinueWith(_ => mergeChannel.Writer.TryComplete());
        
        // Yield datasets as they arrive from any connection
        await foreach (var dataset in mergeChannel.Reader.ReadAllAsync(cancellationToken))
        {
            yield return dataset;
        }
    }
    
    /// <summary>
    /// Adds a connection from an output port to this input port.
    /// </summary>
    internal void AddConnection(Connection connection)
    {
        if (connection == null)
            throw new ArgumentNullException(nameof(connection));
        
        lock (_connectionLock)
        {
            _connections.Add(connection);
        }
    }
    
    private async Task ReadFromConnectionAsync(
        Connection connection, 
        ChannelWriter<IDataset> writer,
        CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var dataset in connection.ReceiveAsync(cancellationToken))
            {
                await writer.WriteAsync(dataset, cancellationToken);
            }
            
            Interlocked.Increment(ref _completedConnections);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, 
                "Failed to receive dataset through connection on port {PortName}", Name);
            throw;
        }
    }
}
```

### 1.3 Connection State Events

```csharp
public enum ConnectionChangeType
{
    Added,
    Removed
}

public sealed class ConnectionChangedEventArgs : EventArgs
{
    public ConnectionChangedEventArgs(ConnectionChangeType changeType, Connection connection)
    {
        ChangeType = changeType;
        Connection = connection ?? throw new ArgumentNullException(nameof(connection));
    }
    
    public ConnectionChangeType ChangeType { get; }
    public Connection Connection { get; }
}
```

---

## 2. DAG Cycle Detection Algorithm

```csharp
/// <summary>
/// Result of cycle detection in a job DAG.
/// </summary>
public sealed class CycleDetectionResult
{
    public bool HasCycle { get; init; }
    public IReadOnlyList<string>? Cycle { get; init; }
    
    public static CycleDetectionResult NoCycle() => new() { HasCycle = false };
    
    public static CycleDetectionResult WithCycle(IEnumerable<string> cyclePath) => new()
    {
        HasCycle = true,
        Cycle = cyclePath.ToList()
    };
}

/// <summary>
/// Implementation of cycle detection for DagValidator.
/// </summary>
public partial class DagValidator
{
    private CycleDetectionResult DetectCycles(JobDefinition job)
    {
        // Build adjacency list representation of the DAG
        var adjacencyList = BuildAdjacencyList(job);
        
        // Track visit states: 0 = unvisited, 1 = visiting, 2 = visited
        var visitStates = new Dictionary<string, int>();
        var currentPath = new Stack<string>();
        
        // Initialize all nodes as unvisited
        foreach (var step in job.Steps)
        {
            visitStates[step.Id] = 0;
        }
        
        // Perform DFS from each unvisited node
        foreach (var step in job.Steps)
        {
            if (visitStates[step.Id] == 0)
            {
                var result = DetectCycleDFS(step.Id, adjacencyList, visitStates, currentPath);
                if (result.HasCycle)
                {
                    return result;
                }
            }
        }
        
        return CycleDetectionResult.NoCycle();
    }
    
    private Dictionary<string, List<string>> BuildAdjacencyList(JobDefinition job)
    {
        var adjacencyList = new Dictionary<string, List<string>>();
        
        // Initialize empty lists for all steps
        foreach (var step in job.Steps)
        {
            adjacencyList[step.Id] = new List<string>();
        }
        
        // Build edges from connections
        foreach (var connection in job.Connections)
        {
            var fromStep = ExtractStepId(connection.From);
            var toStep = ExtractStepId(connection.To);
            
            if (fromStep != null && toStep != null && adjacencyList.ContainsKey(fromStep))
            {
                adjacencyList[fromStep].Add(toStep);
            }
        }
        
        return adjacencyList;
    }
    
    private CycleDetectionResult DetectCycleDFS(
        string nodeId,
        Dictionary<string, List<string>> adjacencyList,
        Dictionary<string, int> visitStates,
        Stack<string> currentPath)
    {
        // Mark node as visiting
        visitStates[nodeId] = 1;
        currentPath.Push(nodeId);
        
        // Visit all neighbors
        if (adjacencyList.TryGetValue(nodeId, out var neighbors))
        {
            foreach (var neighbor in neighbors)
            {
                if (visitStates.TryGetValue(neighbor, out var state))
                {
                    if (state == 1) // Currently visiting = cycle detected
                    {
                        // Build cycle path
                        var cyclePath = new List<string>();
                        var foundStart = false;
                        
                        foreach (var node in currentPath.Reverse())
                        {
                            if (node == neighbor)
                                foundStart = true;
                            
                            if (foundStart)
                                cyclePath.Add(node);
                        }
                        
                        cyclePath.Add(neighbor); // Close the cycle
                        return CycleDetectionResult.WithCycle(cyclePath);
                    }
                    else if (state == 0) // Unvisited
                    {
                        var result = DetectCycleDFS(neighbor, adjacencyList, visitStates, currentPath);
                        if (result.HasCycle)
                        {
                            return result;
                        }
                    }
                }
            }
        }
        
        // Mark node as visited
        visitStates[nodeId] = 2;
        currentPath.Pop();
        
        return CycleDetectionResult.NoCycle();
    }
    
    private string? ExtractStepId(string portReference)
    {
        // Port reference format: "stepId.portName"
        var dotIndex = portReference.IndexOf('.');
        return dotIndex > 0 ? portReference.Substring(0, dotIndex) : null;
    }
}
```

---

## 3. Dead Letter Queue Implementation

```csharp
/// <summary>
/// Interface for dead letter queue to handle failed rows.
/// </summary>
public interface IDeadLetterQueue
{
    /// <summary>
    /// Sends a failed row to the dead letter queue.
    /// </summary>
    ValueTask SendAsync(DeadLetterEntry entry, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Retrieves entries from the dead letter queue for inspection or reprocessing.
    /// </summary>
    IAsyncEnumerable<DeadLetterEntry> ReadAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Entry in the dead letter queue containing failed row and error context.
/// </summary>
public sealed class DeadLetterEntry
{
    public required Row FailedRow { get; init; }
    public required string ErrorMessage { get; init; }
    public required string StepId { get; init; }
    public required DateTimeOffset Timestamp { get; init; }
    public string? JobExecutionId { get; init; }
    public string? StepExecutionId { get; init; }
    public long? OriginalRowIndex { get; init; }
    public Exception? Exception { get; init; }
    public IReadOnlyDictionary<string, object>? AdditionalContext { get; init; }
}

/// <summary>
/// File-based implementation of dead letter queue.
/// </summary>
public sealed class FileDeadLetterQueue : IDeadLetterQueue, IDisposable
{
    private readonly string _filePath;
    private readonly SemaphoreSlim _writeLock = new(1);
    private readonly ILogger<FileDeadLetterQueue> _logger;
    private StreamWriter? _writer;
    private bool _disposed;
    
    public FileDeadLetterQueue(string filePath, ILogger<FileDeadLetterQueue> logger)
    {
        _filePath = filePath ?? throw new ArgumentNullException(nameof(filePath));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        
        // Ensure directory exists
        var directory = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }
    
    public async ValueTask SendAsync(DeadLetterEntry entry, CancellationToken cancellationToken = default)
    {
        if (entry == null)
            throw new ArgumentNullException(nameof(entry));
        
        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            if (_writer == null)
            {
                var stream = new FileStream(_filePath, FileMode.Append, FileAccess.Write, 
                    FileShare.Read, bufferSize: 4096, useAsync: true);
                _writer = new StreamWriter(stream);
            }
            
            var json = JsonSerializer.Serialize(entry, new JsonSerializerOptions
            {
                WriteIndented = false,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
            });
            
            await _writer.WriteLineAsync(json);
            await _writer.FlushAsync();
            
            _logger.LogDebug("Wrote dead letter entry for step {StepId}", entry.StepId);
        }
        finally
        {
            _writeLock.Release();
        }
    }
    
    public async IAsyncEnumerable<DeadLetterEntry> ReadAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_filePath))
            yield break;
        
        using var stream = new FileStream(_filePath, FileMode.Open, FileAccess.Read, 
            FileShare.ReadWrite, bufferSize: 4096, useAsync: true);
        using var reader = new StreamReader(stream);
        
        string? line;
        while ((line = await reader.ReadLineAsync()) != null)
        {
            if (cancellationToken.IsCancellationRequested)
                yield break;
            
            if (string.IsNullOrWhiteSpace(line))
                continue;
            
            DeadLetterEntry? entry = null;
            try
            {
                entry = JsonSerializer.Deserialize<DeadLetterEntry>(line);
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "Failed to deserialize dead letter entry");
            }
            
            if (entry != null)
                yield return entry;
        }
    }
    
    public void Dispose()
    {
        if (!_disposed)
        {
            _writer?.Dispose();
            _writeLock?.Dispose();
            _disposed = true;
        }
    }
}

/// <summary>
/// Extension of ErrorHandler to integrate dead letter queue.
/// </summary>
public partial class ErrorHandler
{
    private readonly IDeadLetterQueue? _deadLetterQueue;
    
    public ErrorHandler(
        ErrorPolicy policy, 
        ILogger<ErrorHandler> logger,
        IDeadLetterQueue? deadLetterQueue = null)
    {
        _policy = policy ?? throw new ArgumentNullException(nameof(policy));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _deadLetterQueue = deadLetterQueue;
    }
    
    private async ValueTask SendToDeadLetterAsync(
        Row faultingRow, 
        Exception error, 
        CancellationToken cancellationToken)
    {
        if (_deadLetterQueue == null)
            return;
        
        var entry = new DeadLetterEntry
        {
            FailedRow = faultingRow,
            ErrorMessage = error.Message,
            StepId = _currentStepId ?? "Unknown",
            Timestamp = DateTimeOffset.UtcNow,
            JobExecutionId = _jobExecutionId,
            StepExecutionId = _stepExecutionId,
            OriginalRowIndex = _currentRowIndex,
            Exception = error
        };
        
        try
        {
            await _deadLetterQueue.SendAsync(entry, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send row to dead letter queue");
            // Don't throw - dead letter queue failures shouldn't stop processing
        }
    }
}
```

---

## 4. Memory Allocation Disposal Pattern

```csharp
/// <summary>
/// Represents a memory allocation that must be returned to a pool when disposed.
/// </summary>
public sealed class MemoryAllocation<T> : IDisposable
{
    private readonly T[] _array;
    private readonly ArrayPool<T> _pool;
    private readonly Action? _onDispose;
    private int _disposed;
    
    public MemoryAllocation(T[] array, int length, ArrayPool<T> pool, Action? onDispose = null)
    {
        if (array == null)
            throw new ArgumentNullException(nameof(array));
        if (pool == null)
            throw new ArgumentNullException(nameof(pool));
        if (length < 0 || length > array.Length)
            throw new ArgumentOutOfRangeException(nameof(length));
        
        _array = array;
        Length = length;
        _pool = pool;
        _onDispose = onDispose;
    }
    
    /// <summary>
    /// Gets the allocated memory as a span.
    /// </summary>
    public Span<T> Span => _disposed == 0 ? _array.AsSpan(0, Length) : Span<T>.Empty;
    
    /// <summary>
    /// Gets the allocated memory as memory.
    /// </summary>
    public Memory<T> Memory => _disposed == 0 ? _array.AsMemory(0, Length) : Memory<T>.Empty;
    
    /// <summary>
    /// Gets the number of elements in the allocation.
    /// </summary>
    public int Length { get; }
    
    /// <summary>
    /// Returns the allocation to the pool.
    /// </summary>
    public void Dispose()
    {
        if (Interlocked.CompareExchange(ref _disposed, 1, 0) == 0)
        {
            try
            {
                _onDispose?.Invoke();
            }
            finally
            {
                _pool.Return(_array, clearArray: true);
            }
        }
    }
}

/// <summary>
/// Enhanced chunk implementation with proper disposal tracking.
/// </summary>
public sealed class Chunk : IDisposable
{
    private readonly MemoryAllocation<Row> _allocation;
    private readonly ChunkMetadata _metadata;
    private int _disposed;
    
    public Chunk(MemoryAllocation<Row> allocation, ChunkMetadata metadata)
    {
        _allocation = allocation ?? throw new ArgumentNullException(nameof(allocation));
        _metadata = metadata ?? throw new ArgumentNullException(nameof(metadata));
    }
    
    public ChunkMetadata Metadata => _metadata;
    
    public ReadOnlySpan<Row> Rows => 
        _disposed == 0 ? _allocation.Span.Slice(0, _metadata.RowCount) : ReadOnlySpan<Row>.Empty;
    
    public void Dispose()
    {
        if (Interlocked.CompareExchange(ref _disposed, 1, 0) == 0)
        {
            _allocation.Dispose();
        }
    }
    
    /// <summary>
    /// Creates a chunk from rows, handling the allocation internally.
    /// </summary>
    public static async ValueTask<Chunk> CreateAsync(
        IMemoryManager memoryManager,
        IEnumerable<Row> rows,
        int chunkIndex,
        long startingRowIndex,
        Guid datasetId,
        CancellationToken cancellationToken = default)
    {
        var rowList = rows.ToList();
        var allocation = await memoryManager.AllocateRowsAsync(rowList.Count, cancellationToken);
        
        // Copy rows into allocated memory
        var span = allocation.Span;
        for (int i = 0; i < rowList.Count; i++)
        {
            span[i] = rowList[i];
        }
        
        var metadata = new ChunkMetadata
        {
            ChunkIndex = chunkIndex,
            RowCount = rowList.Count,
            StartingRowIndex = startingRowIndex,
            DatasetId = datasetId
        };
        
        return new Chunk(allocation, metadata);
    }
}
```

---

## 5. Connection Wiring Mechanism

```csharp
/// <summary>
/// Manages the wiring of connections between ports in a job.
/// </summary>
public sealed class ConnectionManager
{
    private readonly Dictionary<string, IOutputPort> _outputPorts = new();
    private readonly Dictionary<string, IInputPort> _inputPorts = new();
    private readonly List<Connection> _connections = new();
    private readonly ILogger<ConnectionManager> _logger;
    
    public ConnectionManager(ILogger<ConnectionManager> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }
    
    /// <summary>
    /// Registers an output port with the connection manager.
    /// </summary>
    public void RegisterOutputPort(string stepId, IOutputPort port)
    {
        if (string.IsNullOrEmpty(stepId))
            throw new ArgumentException("Step ID cannot be null or empty", nameof(stepId));
        if (port == null)
            throw new ArgumentNullException(nameof(port));
        
        var key = $"{stepId}.{port.Name}";
        _outputPorts[key] = port;
        
        _logger.LogDebug("Registered output port {PortKey}", key);
    }
    
    /// <summary>
    /// Registers an input port with the connection manager.
    /// </summary>
    public void RegisterInputPort(string stepId, IInputPort port)
    {
        if (string.IsNullOrEmpty(stepId))
            throw new ArgumentException("Step ID cannot be null or empty", nameof(stepId));
        if (port == null)
            throw new ArgumentNullException(nameof(port));
        
        var key = $"{stepId}.{port.Name}";
        _inputPorts[key] = port;
        
        _logger.LogDebug("Registered input port {PortKey}", key);
    }
    
    /// <summary>
    /// Creates connections based on job configuration.
    /// </summary>
    public void WireConnections(IEnumerable<ConnectionDefinition> connectionDefinitions)
    {
        if (connectionDefinitions == null)
            throw new ArgumentNullException(nameof(connectionDefinitions));
        
        foreach (var definition in connectionDefinitions)
        {
            CreateConnection(definition);
        }
    }
    
    /// <summary>
    /// Completes all output ports, signaling end of data flow.
    /// </summary>
    public void CompleteAllOutputs()
    {
        foreach (var outputPort in _outputPorts.Values)
        {
            if (outputPort is OutputPort port)
            {
                port.Complete();
            }
        }
        
        _logger.LogDebug("Completed all output ports");
    }
    
    /// <summary>
    /// Gets all active connections.
    /// </summary>
    public IReadOnlyList<Connection> GetConnections() => _connections.AsReadOnly();
    
    private void CreateConnection(ConnectionDefinition definition)
    {
        // Validate source port exists
        if (!_outputPorts.TryGetValue(definition.From, out var outputPort))
        {
            throw new InvalidOperationException(
                $"Output port '{definition.From}' not found. Ensure the step is registered.");
        }
        
        // Validate target port exists
        if (!_inputPorts.TryGetValue(definition.To, out var inputPort))
        {
            throw new InvalidOperationException(
                $"Input port '{definition.To}' not found. Ensure the step is registered.");
        }
        
        // Create connection with options from definition
        var options = new ConnectionOptions
        {
            BufferSize = definition.BufferSize ?? 10,
            MaxPendingDatasets = definition.MaxPendingDatasets ?? 5,
            BackpressureStrategy = definition.BackpressureStrategy ?? BackpressureStrategy.Wait
        };
        
        var connection = new Connection(options);
        
        // Wire up the connection
        if (outputPort is OutputPort output && inputPort is InputPort input)
        {
            output.AddConnection(connection);
            input.AddConnection(connection);
            _connections.Add(connection);
            
            _logger.LogInformation(
                "Created connection from {FromPort} to {ToPort} with buffer size {BufferSize}",
                definition.From, definition.To, options.BufferSize);
        }
        else
        {
            throw new InvalidOperationException(
                "Invalid port types. Ensure ports are properly initialized.");
        }
    }
}

/// <summary>
/// Definition of a connection from configuration.
/// </summary>
public sealed class ConnectionDefinition
{
    public required string From { get; init; }
    public required string To { get; init; }
    public int? BufferSize { get; init; }
    public int? MaxPendingDatasets { get; init; }
    public BackpressureStrategy? BackpressureStrategy { get; init; }
}

/// <summary>
/// Extension to DagExecutor for connection management.
/// </summary>
public partial class DagExecutor
{
    private ConnectionManager CreateConnectionManager(ExecutionPlan plan, ExecutionContext context)
    {
        var connectionManager = new ConnectionManager(
            _loggerFactory.CreateLogger<ConnectionManager>());
        
        // Register all ports from step processors
        foreach (var stage in plan.ExecutionStages)
        {
            foreach (var step in stage.Steps)
            {
                var processor = GetStepProcessor(step.Id);
                
                // Register output ports
                foreach (var outputPort in processor.GetOutputPorts())
                {
                    connectionManager.RegisterOutputPort(step.Id, outputPort);
                }
                
                // Register input ports
                foreach (var inputPort in processor.GetInputPorts())
                {
                    connectionManager.RegisterInputPort(step.Id, inputPort);
                }
            }
        }
        
        // Wire connections
        connectionManager.WireConnections(plan.ConnectionDefinitions);
        
        return connectionManager;
    }
}
```

---

## 6. Usage Example: Putting It All Together

```csharp
/// <summary>
/// Example showing how the components work together.
/// </summary>
public class CoreEngineExample
{
    public async Task RunSimpleJobAsync()
    {
        var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        var serviceProvider = new ServiceCollection()
            .AddSingleton(loggerFactory)
            .AddSingleton<IMemoryManager, DefaultMemoryManager>()
            .BuildServiceProvider();
        
        // Create a simple job definition
        var job = new JobDefinition
        {
            Name = "simple-job",
            Steps = new[]
            {
                new StepDefinition 
                { 
                    Id = "source",
                    Type = "TestSource",
                    Ports = new[]
                    {
                        new PortDefinition 
                        { 
                            Name = "output",
                            Direction = PortDirection.Output
                        }
                    }
                },
                new StepDefinition 
                { 
                    Id = "processor",
                    Type = "TestProcessor",
                    Ports = new[]
                    {
                        new PortDefinition 
                        { 
                            Name = "input",
                            Direction = PortDirection.Input,
                            IsRequired = true
                        },
                        new PortDefinition 
                        { 
                            Name = "output",
                            Direction = PortDirection.Output
                        }
                    }
                },
                new StepDefinition 
                { 
                    Id = "sink",
                    Type = "TestSink",
                    Ports = new[]
                    {
                        new PortDefinition 
                        { 
                            Name = "input",
                            Direction = PortDirection.Input,
                            IsRequired = true
                        }
                    }
                }
            },
            Connections = new[]
            {
                new ConnectionDefinition 
                { 
                    From = "source.output", 
                    To = "processor.input",
                    BufferSize = 5
                },
                new ConnectionDefinition 
                { 
                    From = "processor.output", 
                    To = "sink.input",
                    BufferSize = 5
                }
            }
        };
        
        // Validate the DAG
        var validator = new DagValidator();
        var validationResult = validator.Validate(job);
        
        if (validationResult.HasErrors)
        {
            throw new ValidationException(validationResult);
        }
        
        // Create execution plan
        var plan = CreateExecutionPlan(job);
        
        // Execute the job
        var executor = new DagExecutor(
            serviceProvider.GetRequiredService<IMemoryManager>(),
            loggerFactory,
            serviceProvider);
        
        var result = await executor.ExecuteAsync(plan);
        
        if (result.Status == ExecutionStatus.Failed)
        {
            throw new ExecutionException($"Job execution failed: {result.Error?.Message}", result.Error);
        }
    }
    
    private ExecutionPlan CreateExecutionPlan(JobDefinition job)
    {
        // Topological sort to determine execution stages
        var stages = TopologicalSort(job);
        
        return new ExecutionPlan
        {
            ExecutionStages = stages,
            ConnectionDefinitions = job.Connections
        };
    }
    
    private IReadOnlyList<ExecutionStage> TopologicalSort(JobDefinition job)
    {
        // Simple implementation - in reality this would be more sophisticated
        // For this example, we know source -> processor -> sink
        return new[]
        {
            new ExecutionStage 
            { 
                StageIndex = 0,
                Steps = new[] { job.Steps.First(s => s.Id == "source") }
            },
            new ExecutionStage 
            { 
                StageIndex = 1,
                Steps = new[] { job.Steps.First(s => s.Id == "processor") }
            },
            new ExecutionStage 
            { 
                StageIndex = 2,
                Steps = new[] { job.Steps.First(s => s.Id == "sink") }
            }
        };
    }
}