# FlowEngine Technical Specification: Core Engine
**Version**: 1.0  
**Status**: Draft  
**Authors**: FlowEngine Team  
**Reviewers**: TBD  
> **Note**: This specification is supplemented by "FlowEngine Core Specification - Critical Additions" 
> which provides concrete implementations for key interfaces. Both documents should be read together.

## 1. Overview

### 1.1 Purpose and Scope
This specification defines the core execution engine of FlowEngine, including:
- Fundamental data structures (Row, Dataset, Chunk)
- Port system for inter-step communication
- DAG validation and execution model
- Memory management with intelligent spillover
- Core error handling and propagation

### 1.2 Dependencies
- **External**: .NET 8.0 runtime, C# 12.0 language features
- **Internal**: None (this is the foundational specification)

### 1.3 Out of Scope
- Plugin loading mechanisms (see Plugin API Spec)
- Configuration parsing (see Configuration Spec)
- Debug taps and observability (see Observability Spec)
- Specific step implementations (see Standard Plugins Spec)

## 2. Design Goals

### 2.1 Primary Objectives
1. **Streaming-First**: Process arbitrarily large datasets without loading into memory
2. **Memory-Bounded**: Predictable memory usage regardless of input size
3. **Immutable Data**: Prevent corruption in fan-out scenarios
4. **High Performance**: Minimize allocations and maximize throughput
5. **Type Safety**: Leverage C# type system for compile-time guarantees

### 2.2 Non-Goals
- Distributed processing (V2 consideration)
- Schema evolution within running job
- Transaction support across steps
- Real-time/sub-millisecond latency

### 2.3 Trade-offs Accepted
- Memory copying for immutability over in-place mutations
- Synchronous processing in V1 over async complexity
- Simplified error handling over complex recovery mechanisms

## 3. Technical Design

### 3.1 Architecture

#### Component Overview
```
┌─────────────────────────────────────────────────────────────┐
│                      Job Executor                            │
├─────────────────────────────────────────────────────────────┤
│  ┌──────────────┐  ┌──────────────┐  ┌──────────────────┐  │
│  │ DAG Validator │  │ DAG Executor │  │ Memory Manager   │  │
│  └──────────────┘  └──────────────┘  └──────────────────┘  │
├─────────────────────────────────────────────────────────────┤
│                    Port System                               │
│  ┌────────────┐  ┌────────────┐  ┌─────────────────────┐   │
│  │ Input Port │──│ Connection │──│ Output Port         │   │
│  └────────────┘  └────────────┘  └─────────────────────┘   │
├─────────────────────────────────────────────────────────────┤
│                    Data Layer                                │
│  ┌──────────┐  ┌─────────────┐  ┌──────────────────────┐   │
│  │   Row    │  │   Dataset   │  │ Chunk Management    │   │
│  └──────────┘  └─────────────┘  └──────────────────────┘   │
└─────────────────────────────────────────────────────────────┘
```

### 3.2 Data Model

#### 3.2.1 Row Structure
```csharp
/// <summary>
/// Immutable representation of a single data record.
/// </summary>
public sealed class Row : IEquatable<Row>
{
    private readonly Dictionary<string, object?> _data;
    private readonly int _hashCode;
    
    public Row(IEnumerable<KeyValuePair<string, object?>> data)
    {
        _data = new Dictionary<string, object?>(data, StringComparer.OrdinalIgnoreCase);
        _hashCode = CalculateHashCode();
    }
    
    // Property access
    public object? this[string columnName] => _data.GetValueOrDefault(columnName);
    
    // Schema information
    public IReadOnlyCollection<string> ColumnNames => _data.Keys;
    public int ColumnCount => _data.Count;
    
    // Immutable operations
    public Row With(string columnName, object? value)
    {
        var newData = new Dictionary<string, object?>(_data, StringComparer.OrdinalIgnoreCase)
        {
            [columnName] = value
        };
        return new Row(newData);
    }
    
    public Row Without(string columnName)
    {
        var newData = new Dictionary<string, object?>(_data, StringComparer.OrdinalIgnoreCase);
        newData.Remove(columnName);
        return new Row(newData);
    }
    
    // Value access helpers
    public T? GetValue<T>(string columnName) where T : struct
    {
        var value = this[columnName];
        return value switch
        {
            null => null,
            T typed => typed,
            IConvertible convertible => (T)Convert.ChangeType(convertible, typeof(T)),
            _ => throw new InvalidCastException($"Cannot convert column '{columnName}' to {typeof(T).Name}")
        };
    }
    
    public string? GetString(string columnName) => this[columnName]?.ToString();
    
    // Equality
    public bool Equals(Row? other) => other != null && _hashCode == other._hashCode && DataEquals(other);
    public override bool Equals(object? obj) => obj is Row other && Equals(other);
    public override int GetHashCode() => _hashCode;
    
    private bool DataEquals(Row other) => _data.Count == other._data.Count && 
        _data.All(kvp => other._data.TryGetValue(kvp.Key, out var value) && Equals(kvp.Value, value));
        
    private int CalculateHashCode()
    {
        unchecked
        {
            int hash = 17;
            foreach (var kvp in _data.OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase))
            {
                hash = hash * 31 + kvp.Key.GetHashCode();
                hash = hash * 31 + (kvp.Value?.GetHashCode() ?? 0);
            }
            return hash;
        }
    }
}
```

#### 3.2.2 Dataset Structure
```csharp
/// <summary>
/// Represents an immutable collection of rows with metadata.
/// </summary>
public interface IDataset : IAsyncEnumerable<Row>
{
    /// <summary>
    /// Metadata about the dataset (schema, statistics, etc.)
    /// </summary>
    DatasetMetadata Metadata { get; }
    
    /// <summary>
    /// Gets an estimated row count if available (may return null for streaming sources).
    /// </summary>
    ValueTask<long?> GetEstimatedRowCountAsync();
    
    /// <summary>
    /// Returns the dataset as chunks for efficient processing.
    /// </summary>
    IAsyncEnumerable<Chunk> GetChunksAsync(ChunkingOptions options);
}

/// <summary>
/// Metadata describing a dataset.
/// </summary>
public sealed class DatasetMetadata
{
    public Guid DatasetId { get; init; } = Guid.NewGuid();
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public string? SourceStepId { get; init; }
    public string? SourcePortName { get; init; }
    public Schema? Schema { get; init; }
    public IReadOnlyDictionary<string, object> Properties { get; init; } = new Dictionary<string, object>();
}

/// <summary>
/// Describes the structure of data in a dataset.
/// </summary>
public sealed class Schema
{
    public IReadOnlyList<ColumnDefinition> Columns { get; init; } = Array.Empty<ColumnDefinition>();
    
    public ColumnDefinition? GetColumn(string name) => 
        Columns.FirstOrDefault(c => c.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
}

public sealed class ColumnDefinition
{
    public required string Name { get; init; }
    public Type DataType { get; init; } = typeof(string);
    public bool IsNullable { get; init; } = true;
    public IReadOnlyDictionary<string, object> Properties { get; init; } = new Dictionary<string, object>();
}
```

#### 3.2.3 Chunk Management
```csharp
/// <summary>
/// Represents a subset of rows for memory-efficient processing.
/// </summary>
public sealed class Chunk : IDisposable
{
    private readonly Row[] _rows;
    private readonly ArrayPool<Row> _pool;
    private bool _disposed;
    
    public Chunk(Row[] rows, ArrayPool<Row> pool, ChunkMetadata metadata)
    {
        _rows = rows;
        _pool = pool;
        Metadata = metadata;
    }
    
    public ChunkMetadata Metadata { get; }
    public ReadOnlySpan<Row> Rows => _disposed ? ReadOnlySpan<Row>.Empty : _rows.AsSpan(0, Metadata.RowCount);
    
    public void Dispose()
    {
        if (!_disposed)
        {
            _pool.Return(_rows, clearArray: true);
            _disposed = true;
        }
    }
}

public sealed class ChunkMetadata
{
    public int ChunkIndex { get; init; }
    public int RowCount { get; init; }
    public long StartingRowIndex { get; init; }
    public Guid DatasetId { get; init; }
}

public sealed class ChunkingOptions
{
    public int TargetChunkSize { get; init; } = 5000;
    public int MaxChunkSize { get; init; } = 10000;
    public MemoryPressureStrategy PressureStrategy { get; init; } = MemoryPressureStrategy.Adaptive;
}

public enum MemoryPressureStrategy
{
    /// <summary>Use fixed chunk sizes regardless of memory pressure.</summary>
    Fixed,
    /// <summary>Reduce chunk size under memory pressure.</summary>
    Adaptive,
    /// <summary>Spill to disk under memory pressure.</summary>
    SpillToDisk
}
```

### 3.3 Port System

#### 3.3.1 Port Interfaces
```csharp
/// <summary>
/// Base interface for all ports.
/// </summary>
public interface IPort
{
    string Name { get; }
    PortMetadata Metadata { get; }
    bool IsConnected { get; }
}

/// <summary>
/// Output port that produces data.
/// </summary>
public interface IOutputPort : IPort
{
    /// <summary>
    /// Sends a dataset through this port to all connected input ports.
    /// </summary>
    ValueTask SendAsync(IDataset dataset, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Gets the number of connected input ports.
    /// </summary>
    int ConnectionCount { get; }
    
    /// <summary>
    /// Event raised when a connection is added or removed.
    /// </summary>
    event EventHandler<ConnectionChangedEventArgs>? ConnectionChanged;
}

/// <summary>
/// Input port that receives data.
/// </summary>
public interface IInputPort : IPort
{
    /// <summary>
    /// Receives datasets from connected output ports.
    /// </summary>
    IAsyncEnumerable<IDataset> ReceiveAsync(CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Gets whether this port has received all data (upstream completed).
    /// </summary>
    bool IsCompleted { get; }
}

public sealed class PortMetadata
{
    public required string StepId { get; init; }
    public required PortDirection Direction { get; init; }
    public string? Description { get; init; }
    public Schema? ExpectedSchema { get; init; }
    public bool IsRequired { get; init; } = true;
}

public enum PortDirection
{
    Input,
    Output
}
```

#### 3.3.2 Connection Management
```csharp
/// <summary>
/// Represents a connection between an output port and an input port.
/// </summary>
public sealed class Connection
{
    private readonly Channel<IDataset> _channel;
    private readonly SemaphoreSlim _backpressure;
    
    public Connection(ConnectionOptions options)
    {
        Options = options;
        
        var channelOptions = new BoundedChannelOptions(options.BufferSize)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = true
        };
        
        _channel = Channel.CreateBounded<IDataset>(channelOptions);
        _backpressure = new SemaphoreSlim(options.MaxPendingDatasets);
    }
    
    public ConnectionOptions Options { get; }
    public ConnectionStatistics Statistics { get; } = new();
    
    public async ValueTask SendAsync(IDataset dataset, CancellationToken cancellationToken)
    {
        await _backpressure.WaitAsync(cancellationToken);
        try
        {
            await _channel.Writer.WriteAsync(dataset, cancellationToken);
            Statistics.IncrementDatasetsSent();
        }
        catch
        {
            _backpressure.Release();
            throw;
        }
    }
    
    public async IAsyncEnumerable<IDataset> ReceiveAsync([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var dataset in _channel.Reader.ReadAllAsync(cancellationToken))
        {
            Statistics.IncrementDatasetsReceived();
            yield return dataset;
            _backpressure.Release();
        }
    }
    
    public void Complete() => _channel.Writer.TryComplete();
}

public sealed class ConnectionOptions
{
    public int BufferSize { get; init; } = 10;
    public int MaxPendingDatasets { get; init; } = 5;
    public BackpressureStrategy BackpressureStrategy { get; init; } = BackpressureStrategy.Wait;
}

public sealed class ConnectionStatistics
{
    private long _datasetsSent;
    private long _datasetsReceived;
    
    public long DatasetsSent => Interlocked.Read(ref _datasetsSent);
    public long DatasetsReceived => Interlocked.Read(ref _datasetsReceived);
    
    internal void IncrementDatasetsSent() => Interlocked.Increment(ref _datasetsSent);
    internal void IncrementDatasetsReceived() => Interlocked.Increment(ref _datasetsReceived);
}
```

### 3.4 DAG Execution

#### 3.4.1 DAG Validation
```csharp
/// <summary>
/// Validates and prepares a job DAG for execution.
/// </summary>
public sealed class DagValidator
{
    public ValidationResult Validate(JobDefinition job)
    {
        var errors = new List<ValidationError>();
        
        // Check for cycles
        var cycleResult = DetectCycles(job);
        if (cycleResult.HasCycle)
        {
            errors.Add(new ValidationError(
                ValidationErrorCode.CycleDetected,
                $"Cycle detected: {string.Join(" -> ", cycleResult.Cycle!)}"
            ));
        }
        
        // Validate connections
        foreach (var connection in job.Connections)
        {
            // Check source port exists
            if (!TryGetPort(job, connection.From, out var sourcePort))
            {
                errors.Add(new ValidationError(
                    ValidationErrorCode.PortNotFound,
                    $"Source port not found: {connection.From}"
                ));
                continue;
            }
            
            // Check target port exists
            if (!TryGetPort(job, connection.To, out var targetPort))
            {
                errors.Add(new ValidationError(
                    ValidationErrorCode.PortNotFound,
                    $"Target port not found: {connection.To}"
                ));
                continue;
            }
            
            // Validate port directions
            if (sourcePort.Direction != PortDirection.Output)
            {
                errors.Add(new ValidationError(
                    ValidationErrorCode.InvalidPortDirection,
                    $"Port {connection.From} is not an output port"
                ));
            }
            
            if (targetPort.Direction != PortDirection.Input)
            {
                errors.Add(new ValidationError(
                    ValidationErrorCode.InvalidPortDirection,
                    $"Port {connection.To} is not an input port"
                ));
            }
        }
        
        // Check for unconnected required input ports
        foreach (var step in job.Steps)
        {
            foreach (var port in step.Ports.Where(p => p.Direction == PortDirection.Input && p.IsRequired))
            {
                var portId = $"{step.Id}.{port.Name}";
                if (!job.Connections.Any(c => c.To == portId))
                {
                    errors.Add(new ValidationError(
                        ValidationErrorCode.UnconnectedRequiredPort,
                        $"Required input port not connected: {portId}",
                        ValidationErrorSeverity.Warning
                    ));
                }
            }
        }
        
        return new ValidationResult(errors);
    }
    
    private CycleDetectionResult DetectCycles(JobDefinition job)
    {
        // Implementation: Depth-first search with path tracking
        // Returns cycle path if found
        throw new NotImplementedException();
    }
}
```

#### 3.4.2 DAG Executor
```csharp
/// <summary>
/// Executes a validated job DAG.
/// </summary>
public sealed class DagExecutor
{
    private readonly IMemoryManager _memoryManager;
    private readonly ILoggerFactory _loggerFactory;
    private readonly IServiceProvider _serviceProvider;
    
    public DagExecutor(
        IMemoryManager memoryManager,
        ILoggerFactory loggerFactory,
        IServiceProvider serviceProvider)
    {
        _memoryManager = memoryManager;
        _loggerFactory = loggerFactory;
        _serviceProvider = serviceProvider;
    }
    
    public async Task<ExecutionResult> ExecuteAsync(
        ExecutionPlan plan,
        CancellationToken cancellationToken = default)
    {
        var context = new ExecutionContext
        {
            JobExecutionId = Guid.NewGuid().ToString("N"),
            StartTime = DateTimeOffset.UtcNow,
            CancellationToken = cancellationToken
        };
        
        try
        {
            // Initialize all step processors
            var processors = await InitializeProcessorsAsync(plan, context);
            
            // Execute in topological order
            foreach (var stage in plan.ExecutionStages)
            {
                var stageTasks = stage.Steps.Select(step =>
                    ExecuteStepAsync(processors[step.Id], context)
                ).ToArray();
                
                await Task.WhenAll(stageTasks);
            }
            
            // Cleanup
            await CleanupProcessorsAsync(processors);
            
            return new ExecutionResult
            {
                JobExecutionId = context.JobExecutionId,
                Status = ExecutionStatus.Succeeded,
                StartTime = context.StartTime,
                EndTime = DateTimeOffset.UtcNow
            };
        }
        catch (Exception ex)
        {
            return new ExecutionResult
            {
                JobExecutionId = context.JobExecutionId,
                Status = ExecutionStatus.Failed,
                StartTime = context.StartTime,
                EndTime = DateTimeOffset.UtcNow,
                Error = ex
            };
        }
    }
    
    private async Task ExecuteStepAsync(IStepProcessor processor, ExecutionContext context)
    {
        var stepContext = new StepExecutionContext
        {
            StepExecutionId = Guid.NewGuid().ToString("N"),
            ParentContext = context,
            MemoryManager = _memoryManager
        };
        
        await processor.ExecuteAsync(stepContext);
    }
}

/// <summary>
/// Represents the execution plan for a job.
/// </summary>
public sealed class ExecutionPlan
{
    /// <summary>
    /// Steps grouped by execution stage (can run in parallel within stage).
    /// </summary>
    public required IReadOnlyList<ExecutionStage> ExecutionStages { get; init; }
    
    /// <summary>
    /// All connections in the job.
    /// </summary>
    public required IReadOnlyList<Connection> Connections { get; init; }
}

public sealed class ExecutionStage
{
    public required int StageIndex { get; init; }
    public required IReadOnlyList<StepDefinition> Steps { get; init; }
}
```

### 3.5 Memory Management

#### 3.5.1 Memory Manager
```csharp
/// <summary>
/// Manages memory allocation and spillover for the engine.
/// </summary>
public interface IMemoryManager
{
    /// <summary>
    /// Current memory pressure level.
    /// </summary>
    MemoryPressureLevel CurrentPressure { get; }
    
    /// <summary>
    /// Allocates memory for a chunk of rows.
    /// </summary>
    ValueTask<MemoryAllocation<Row>> AllocateRowsAsync(int count, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Checks if an operation requiring full materialization should spill to disk.
    /// </summary>
    bool ShouldSpillToDisk(long estimatedBytes);
    
    /// <summary>
    /// Creates a spillover file for large datasets.
    /// </summary>
    ValueTask<ISpilloverDataset> CreateSpilloverAsync(IAsyncEnumerable<Row> rows, CancellationToken cancellationToken = default);
}

public sealed class DefaultMemoryManager : IMemoryManager
{
    private readonly MemoryManagerOptions _options;
    private readonly ArrayPool<Row> _rowPool;
    private long _currentAllocation;
    
    public DefaultMemoryManager(IOptions<MemoryManagerOptions> options)
    {
        _options = options.Value;
        _rowPool = ArrayPool<Row>.Create();
    }
    
    public MemoryPressureLevel CurrentPressure => CalculatePressureLevel();
    
    public async ValueTask<MemoryAllocation<Row>> AllocateRowsAsync(int count, CancellationToken cancellationToken)
    {
        var pressure = CurrentPressure;
        
        // Reduce allocation size under pressure
        if (pressure >= MemoryPressureLevel.High)
        {
            count = Math.Min(count, _options.MinChunkSize);
        }
        
        var array = _rowPool.Rent(count);
        Interlocked.Add(ref _currentAllocation, array.Length * EstimatedRowSize);
        
        return new MemoryAllocation<Row>(array, count, _rowPool, () =>
        {
            Interlocked.Add(ref _currentAllocation, -array.Length * EstimatedRowSize);
        });
    }
    
    public bool ShouldSpillToDisk(long estimatedBytes)
    {
        var totalMemory = GC.GetTotalMemory(false);
        var threshold = _options.SpilloverThresholdBytes;
        
        return totalMemory + estimatedBytes > threshold;
    }
    
    public async ValueTask<ISpilloverDataset> CreateSpilloverAsync(
        IAsyncEnumerable<Row> rows, 
        CancellationToken cancellationToken)
    {
        var tempFile = Path.GetTempFileName();
        
        await using var stream = new FileStream(
            tempFile, 
            FileMode.Create, 
            FileAccess.Write, 
            FileShare.None,
            bufferSize: 65536,
            useAsync: true);
            
        await using var compressor = new BrotliStream(stream, CompressionLevel.Fastest);
        await using var writer = new StreamWriter(compressor);
        
        // Write rows as JSON lines for simplicity (V1)
        await foreach (var row in rows.WithCancellation(cancellationToken))
        {
            await writer.WriteLineAsync(JsonSerializer.Serialize(row));
        }
        
        return new SpilloverDataset(tempFile, deleteOnDispose: true);
    }
    
    private MemoryPressureLevel CalculatePressureLevel()
    {
        var totalMemory = GC.GetTotalMemory(false);
        var gcInfo = GC.GetGCMemoryInfo();
        var heapSize = gcInfo.HeapSizeBytes;
        var highMemoryThreshold = gcInfo.HighMemoryLoadThresholdBytes;
        
        var usage = (double)totalMemory / highMemoryThreshold;
        
        return usage switch
        {
            < 0.5 => MemoryPressureLevel.Low,
            < 0.8 => MemoryPressureLevel.Medium,
            < 0.95 => MemoryPressureLevel.High,
            _ => MemoryPressureLevel.Critical
        };
    }
    
    private const int EstimatedRowSize = 1024; // Conservative estimate
}

public enum MemoryPressureLevel
{
    Low,
    Medium,
    High,
    Critical
}

public sealed class MemoryManagerOptions
{
    public long SpilloverThresholdBytes { get; init; } = 1_073_741_824; // 1GB
    public int MinChunkSize { get; init; } = 100;
    public int MaxChunkSize { get; init; } = 10_000;
    public string SpilloverPath { get; init; } = Path.GetTempPath();
}
```

### 3.6 Error Handling

#### 3.6.1 Error Types
```csharp
/// <summary>
/// Base exception for all FlowEngine errors.
/// </summary>
public abstract class FlowEngineException : Exception
{
    protected FlowEngineException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
    
    public string? JobExecutionId { get; init; }
    public string? StepExecutionId { get; init; }
    public string? StepId { get; init; }
    public long? RowIndex { get; init; }
}

/// <summary>
/// Configuration or validation errors.
/// </summary>
public sealed class ValidationException : FlowEngineException
{
    public ValidationException(ValidationResult result)
        : base($"Validation failed with {result.Errors.Count} errors")
    {
        ValidationResult = result;
    }
    
    public ValidationResult ValidationResult { get; }
}

/// <summary>
/// Runtime execution errors.
/// </summary>
public sealed class ExecutionException : FlowEngineException
{
    public ExecutionException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
    
    public ExecutionStage? Stage { get; init; }
}

/// <summary>
/// Data processing errors.
/// </summary>
public sealed class DataProcessingException : FlowEngineException
{
    public DataProcessingException(string message, Row? faultingRow = null, Exception? innerException = null)
        : base(message, innerException)
    {
        FaultingRow = faultingRow;
    }
    
    public Row? FaultingRow { get; }
}
```

#### 3.6.2 Error Propagation
```csharp
/// <summary>
/// Handles error policy enforcement during execution.
/// </summary>
public sealed class ErrorHandler
{
    private readonly ErrorPolicy _policy;
    private readonly ILogger<ErrorHandler> _logger;
    private int _consecutiveErrors;
    private long _totalErrors;
    private long _totalProcessed;
    
    public ErrorHandler(ErrorPolicy policy, ILogger<ErrorHandler> logger)
    {
        _policy = policy;
        _logger = logger;
    }
    
    public async ValueTask<bool> HandleErrorAsync(
        Exception error,
        ErrorContext context,
        CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _totalErrors);
        Interlocked.Increment(ref _consecutiveErrors);
        
        // Log the error with context
        _logger.LogError(error, 
            "Error in step {StepId} at row {RowIndex}: {ErrorMessage}",
            context.StepId, context.RowIndex, error.Message);
        
        // Send to dead letter queue if configured
        if (_policy.DeadLetterQueue != null && context.FaultingRow != null)
        {
            await SendToDeadLetterAsync(context.FaultingRow, error, cancellationToken);
        }
        
        // Check if we should stop
        if (_policy.StopOnError)
        {
            throw new ExecutionException("Execution stopped due to error policy", error)
            {
                StepId = context.StepId,
                RowIndex = context.RowIndex
            };
        }
        
        // Check consecutive error limit
        if (_consecutiveErrors > _policy.MaxConsecutiveErrors)
        {
            throw new ExecutionException(
                $"Exceeded maximum consecutive errors ({_policy.MaxConsecutiveErrors})", 
                error);
        }
        
        // Check error rate
        var errorRate = (double)_totalErrors / Math.Max(1, _totalProcessed);
        if (errorRate > _policy.MaxErrorRate)
        {
            throw new ExecutionException(
                $"Exceeded maximum error rate ({_policy.MaxErrorRate:P})", 
                error);
        }
        
        return true; // Continue processing
    }
    
    public void RecordSuccess()
    {
        Interlocked.Exchange(ref _consecutiveErrors, 0);
        Interlocked.Increment(ref _totalProcessed);
    }
}

public sealed class ErrorPolicy
{
    public bool StopOnError { get; init; }
    public int MaxConsecutiveErrors { get; init; } = 10;
    public double MaxErrorRate { get; init; } = 0.05;
    public string? DeadLetterQueue { get; init; }
    public RetryPolicy? RetryPolicy { get; init; }
}

public sealed class ErrorContext
{
    public required string StepId { get; init; }
    public long? RowIndex { get; init; }
    public Row? FaultingRow { get; init; }
    public string? PortName { get; init; }
}
```

## 4. Implementation Notes

### 4.1 .NET Specific Considerations
1. **Memory Pooling**: Use `ArrayPool<T>` and `MemoryPool<T>` for chunk allocations
2. **Span/Memory**: Leverage `Span<T>` and `Memory<T>` for zero-copy operations where possible
3. **ValueTask**: Use `ValueTask` for high-frequency async operations to reduce allocations
4. **Channel**: Use `System.Threading.Channels` for inter-port communication

### 4.2 Recommended Packages
- **System.IO.Pipelines**: For high-performance I/O operations
- **System.Text.Json**: For JSON serialization (spillover, debugging)
- **Microsoft.Extensions.ObjectPool**: For object pooling patterns
- **System.Collections.Immutable**: For immutable collections where needed

### 4.3 Performance Guidelines
1. **Allocation Reduction**: Pool all temporary objects (chunks, buffers)
2. **String Interning**: Consider interning frequently used column names
3. **Lazy Evaluation**: Defer computation until data is actually consumed
4. **Batch Operations**: Process data in chunks to improve cache locality

## 5. Testing Strategy

### 5.1 Unit Tests
- Row immutability and equality
- Dataset enumeration with cancellation
- Port connection establishment
- Memory pressure calculation
- Error policy enforcement

### 5.2 Integration Tests
- End-to-end DAG execution with multiple steps
- Memory spillover under pressure conditions
- Concurrent port communication
- Error propagation across step boundaries
- Resource cleanup and disposal

### 5.3 Performance Benchmarks
```csharp
[MemoryDiagnoser]
[SimpleJob(RuntimeMoniker.Net80)]
public class CoreBenchmarks
{
    [Params(1000, 10_000, 100_000)]
    public int RowCount { get; set; }
    
    [Benchmark]
    public async Task StreamingThroughput()
    {
        // Measure raw streaming performance through ports
    }
    
    [Benchmark]
    public async Task ChunkingOverhead()
    {
        // Measure chunking vs. continuous streaming
    }
    
    [Benchmark]
    public void RowImmutabilityOverhead()
    {
        // Measure cost of immutable row operations
    }
    
    [Benchmark]
    public async Task MemorySpilloverThreshold()
    {
        // Measure spillover performance at various memory pressures
    }
}
```

### 5.4 Stress Tests
- Large dataset processing (1B+ rows)
- High fan-out scenarios (1 output → 100 inputs)
- Memory pressure scenarios with forced GC
- Cancellation during various execution stages
- Rapid connection/disconnection cycles

## 6. Security Considerations

### 6.1 Resource Exhaustion
- **Threat**: Malicious jobs consuming excessive memory
- **Mitigation**: Hard memory limits enforced by MemoryManager
- **Implementation**: Process-level resource quotas via job isolation

### 6.2 Data Isolation
- **Threat**: Data leakage between jobs through shared memory
- **Mitigation**: Clear all pooled arrays on return
- **Implementation**: `ArrayPool.Return(array, clearArray: true)`

### 6.3 Spillover File Security
- **Threat**: Sensitive data persisted to disk
- **Mitigation**: Encrypted spillover files with automatic cleanup
- **Implementation**: 
  ```csharp
  // Future enhancement for V1.1
  public interface ISpilloverEncryption
  {
      Stream CreateEncryptedStream(Stream baseStream);
      Stream CreateDecryptedStream(Stream baseStream);
  }
  ```

## 7. Future Considerations

### 7.1 Async Processing (V2)
The current synchronous design can be extended to async:
```csharp
// Future async processor interface
public interface IAsyncStepProcessor
{
    ValueTask<ProcessorResult> ProcessAsync(
        IAsyncEnumerable<Row> input,
        IAsyncRowWriter output,
        ProcessingContext context,
        CancellationToken cancellationToken);
}
```

### 7.2 Schema Evolution
Preparation for schema changes during execution:
```csharp
// Future schema evolution support
public interface ISchemaEvolution
{
    Schema MergeSchemas(Schema current, Schema incoming);
    Row MigrateRow(Row row, Schema fromSchema, Schema toSchema);
}
```

### 7.3 Distributed Processing
Design allows for future distribution:
- Serialize `IDataset` for network transport
- Implement distributed `IMemoryManager`
- Add remote port connections

### 7.4 Advanced Memory Management
- **NUMA awareness** for large server deployments
- **GPU memory** support for compute-intensive transforms
- **Persistent memory** integration for spillover

## 8. Performance Targets

### 8.1 Baseline Metrics
Based on the architecture, expected performance targets:

| Metric | Target | Conditions |
|--------|--------|------------|
| Streaming Throughput | 1M rows/sec | Simple pass-through, no transforms |
| Memory Overhead | < 100 bytes/row | Excluding actual data |
| Chunk Processing | < 1ms overhead | Per 5000-row chunk |
| Port Communication | < 10μs | Per dataset transfer |
| Memory Spillover | 100MB/sec | Compressed write to SSD |

### 8.2 Scalability Targets
- Linear scaling up to 16 concurrent steps
- Support for 1000+ step DAGs
- Memory bounded at 2GB for 1TB input files
- Sub-second job startup time

## 9. API Stability Guarantees

### 9.1 Core Interfaces (Stable in V1)
The following interfaces will remain binary compatible throughout V1.x:
- `IDataset`
- `IInputPort` / `IOutputPort`
- `IMemoryManager`
- Core exception types

### 9.2 Extension Points
The following are designed for extensibility:
- `Row` structure (via extension methods)
- Memory management strategies
- Error handling policies
- Connection backpressure strategies

### 9.3 Internal APIs
The following are internal and may change:
- Chunk pooling implementation
- DAG execution order algorithm
- Spillover file format

## 10. Code Organization

### 10.1 Namespace Structure
```
FlowEngine.Core
├── FlowEngine.Core.Data
│   ├── Row
│   ├── Dataset
│   └── Schema
├── FlowEngine.Core.Ports
│   ├── InputPort
│   ├── OutputPort
│   └── Connection
├── FlowEngine.Core.Execution
│   ├── DagValidator
│   ├── DagExecutor
│   └── ExecutionContext
├── FlowEngine.Core.Memory
│   ├── MemoryManager
│   ├── ChunkManager
│   └── SpilloverManager
└── FlowEngine.Core.Errors
    ├── ErrorHandler
    ├── ErrorPolicy
    └── Exceptions
```

### 10.2 Assembly Structure
- **FlowEngine.Core.dll**: All core functionality
- **FlowEngine.Abstractions.dll**: Interfaces only (for plugins)
- **FlowEngine.Core.Tests.dll**: Comprehensive test suite

## 11. Appendix: Design Decisions Log

### 11.1 Why Immutable Rows?
**Decision**: Make `Row` immutable with copy-on-write semantics
**Rationale**: 
- Prevents corruption in fan-out scenarios
- Enables safe concurrent access
- Simplifies debugging
**Trade-off**: Memory allocation overhead
**Alternative Considered**: Mutable rows with cloning at port boundaries

### 11.2 Why Channels for Port Communication?
**Decision**: Use `System.Threading.Channels` for port connections
**Rationale**:
- Built-in backpressure support
- Excellent async performance
- Bounded memory usage
**Trade-off**: Less flexibility than custom implementation
**Alternative Considered**: Custom ring buffers

### 11.3 Why JSON for Spillover?
**Decision**: Use JSON lines format for V1 spillover files
**Rationale**:
- Simple implementation
- Human-readable for debugging
- No schema versioning issues
**Trade-off**: Larger file sizes
**Alternative Considered**: Binary format with schema headers

### 11.4 Why Synchronous V1?
**Decision**: Keep V1 processing synchronous
**Rationale**:
- Simpler mental model
- Easier debugging
- Predictable performance
**Trade-off**: Can't call external services
**Alternative Considered**: Async from start

---

## Next Steps

This Core Engine specification provides the foundation for FlowEngine. The next specifications to develop are:

1. **Plugin API Specification** - Define how plugins interact with the core
2. **Configuration Specification** - Define YAML schema and validation
3. **Observability Specification** - Define debug taps and monitoring

Each subsequent specification should reference and build upon the interfaces and concepts defined here.