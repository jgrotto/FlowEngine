using FlowEngine.Abstractions.Data;

namespace FlowEngine.Abstractions.Plugins;

/// <summary>
/// Interface for sink plugins that consume data from the pipeline.
/// Sink plugins are the endpoints of data processing pipelines and don't produce output connections.
/// Examples: file writers, database sinks, web APIs, message queues, monitoring systems.
/// </summary>
public interface ISinkPlugin : IPlugin
{
    /// <summary>
    /// Gets the input schema that this sink expects to receive.
    /// Used for pipeline validation and connection compatibility checking.
    /// </summary>
    ISchema InputSchema { get; }

    /// <summary>
    /// Consumes chunks of data asynchronously from the pipeline.
    /// This method should process chunks as they arrive and handle backpressure appropriately.
    /// </summary>
    /// <param name="input">Async enumerable of input chunks to consume</param>
    /// <param name="cancellationToken">Cancellation token to stop consumption</param>
    /// <returns>Task representing the consumption operation</returns>
    /// <exception cref="PluginExecutionException">Thrown when consumption fails</exception>
    Task ConsumeAsync(
        IAsyncEnumerable<IChunk> input,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets whether this sink supports transactional operations.
    /// Transactional sinks can rollback changes if pipeline execution fails.
    /// </summary>
    bool SupportsTransactions { get; }

    /// <summary>
    /// Gets the write mode indicating how this sink handles data.
    /// Used for optimization and resource allocation decisions.
    /// </summary>
    WriteMode Mode { get; }

    /// <summary>
    /// Gets the current transaction state if transactions are supported.
    /// Returns null if transactions are not supported or no transaction is active.
    /// </summary>
    TransactionState? CurrentTransaction { get; }

    /// <summary>
    /// Begins a new transaction for atomic writes.
    /// Only supported if SupportsTransactions returns true.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Transaction identifier</returns>
    /// <exception cref="NotSupportedException">Thrown if transactions are not supported</exception>
    /// <exception cref="PluginExecutionException">Thrown if transaction creation fails</exception>
    Task<string> BeginTransactionAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Commits the current transaction, making all writes permanent.
    /// Only supported if SupportsTransactions returns true.
    /// </summary>
    /// <param name="transactionId">Transaction to commit</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Task representing the commit operation</returns>
    /// <exception cref="NotSupportedException">Thrown if transactions are not supported</exception>
    /// <exception cref="PluginExecutionException">Thrown if commit fails</exception>
    Task CommitTransactionAsync(string transactionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Rolls back the current transaction, discarding all writes.
    /// Only supported if SupportsTransactions returns true.
    /// </summary>
    /// <param name="transactionId">Transaction to rollback</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Task representing the rollback operation</returns>
    /// <exception cref="NotSupportedException">Thrown if transactions are not supported</exception>
    /// <exception cref="PluginExecutionException">Thrown if rollback fails</exception>
    Task RollbackTransactionAsync(string transactionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Flushes any buffered data to ensure durability.
    /// Called at the end of pipeline execution or during checkpoints.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Task representing the flush operation</returns>
    Task FlushAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Defines how a sink plugin writes data.
/// </summary>
public enum WriteMode
{
    /// <summary>
    /// Streaming write: data is written immediately as chunks arrive.
    /// Lowest latency but may have higher overhead per write.
    /// Examples: real-time monitoring, event streams, live dashboards.
    /// </summary>
    Streaming,

    /// <summary>
    /// Buffered write: data is accumulated and written in larger batches.
    /// Higher throughput but increased latency and memory usage.
    /// Examples: database bulk inserts, file writing, batch processing.
    /// </summary>
    Buffered,

    /// <summary>
    /// Transactional write: data is written within explicit transactions.
    /// Ensures consistency but may have performance implications.
    /// Examples: ACID databases, financial systems, critical data stores.
    /// </summary>
    Transactional,

    /// <summary>
    /// Append-only write: data is always appended, never updated or deleted.
    /// Optimized for high-throughput scenarios with simple data patterns.
    /// Examples: log files, event stores, audit trails.
    /// </summary>
    AppendOnly
}

/// <summary>
/// Represents the current state of a transaction in a sink plugin.
/// </summary>
public sealed record TransactionState
{
    /// <summary>
    /// Gets the unique identifier for this transaction.
    /// </summary>
    public required string TransactionId { get; init; }

    /// <summary>
    /// Gets the timestamp when this transaction was started.
    /// </summary>
    public DateTimeOffset StartTime { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Gets the number of chunks written in this transaction.
    /// </summary>
    public long ChunksWritten { get; init; }

    /// <summary>
    /// Gets the number of rows written in this transaction.
    /// </summary>
    public long RowsWritten { get; init; }

    /// <summary>
    /// Gets the current size of the transaction in bytes.
    /// </summary>
    public long TransactionSizeBytes { get; init; }

    /// <summary>
    /// Gets whether this transaction is ready to commit.
    /// </summary>
    public bool IsReadyToCommit { get; init; }

    /// <summary>
    /// Gets optional transaction metadata for debugging and monitoring.
    /// </summary>
    public IReadOnlyDictionary<string, object>? Metadata { get; init; }
}
