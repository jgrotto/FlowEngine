using FlowEngine.Abstractions.Data;

namespace FlowEngine.Abstractions.Plugins;

/// <summary>
/// Interface for transform plugins that process data flowing through the pipeline.
/// Transform plugins receive input chunks, apply transformations, and produce output chunks.
/// Examples: data cleaning, aggregation, enrichment, format conversion, validation.
/// </summary>
public interface ITransformPlugin : IPlugin
{
    /// <summary>
    /// Gets the input schema that this transform expects to receive.
    /// Used for pipeline validation and connection compatibility checking.
    /// </summary>
    ISchema InputSchema { get; }

    /// <summary>
    /// Transforms input chunks asynchronously and produces output chunks.
    /// This method should process chunks as they arrive and yield results immediately.
    /// </summary>
    /// <param name="input">Async enumerable of input chunks to transform</param>
    /// <param name="cancellationToken">Cancellation token to stop processing</param>
    /// <returns>Async enumerable of transformed chunks with the plugin's output schema</returns>
    /// <exception cref="PluginExecutionException">Thrown when transformation fails</exception>
    IAsyncEnumerable<IChunk> TransformAsync(
        IAsyncEnumerable<IChunk> input, 
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets whether this transform maintains state across chunks.
    /// Stateful transforms may require special handling for parallelization and fault tolerance.
    /// </summary>
    bool IsStateful { get; }

    /// <summary>
    /// Gets the transformation mode indicating how this plugin processes data.
    /// Used for optimization and resource allocation decisions.
    /// </summary>
    TransformMode Mode { get; }

    /// <summary>
    /// Flushes any pending state and produces final output chunks.
    /// Called when the input stream ends to ensure no data is lost.
    /// Only relevant for stateful transforms.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Final chunks from flushed state</returns>
    IAsyncEnumerable<IChunk> FlushAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Defines how a transform plugin processes data chunks.
/// </summary>
public enum TransformMode
{
    /// <summary>
    /// One-to-one transformation: each input chunk produces exactly one output chunk.
    /// Most efficient mode with predictable memory usage.
    /// Examples: field mapping, data validation, simple calculations.
    /// </summary>
    OneToOne,

    /// <summary>
    /// One-to-many transformation: each input chunk may produce multiple output chunks.
    /// Examples: data expansion, splitting records, generating derived data.
    /// </summary>
    OneToMany,

    /// <summary>
    /// Many-to-one transformation: multiple input chunks produce one output chunk.
    /// Requires buffering and may have higher memory usage.
    /// Examples: aggregation, grouping, batching.
    /// </summary>
    ManyToOne,

    /// <summary>
    /// Many-to-many transformation: complex relationships between input and output.
    /// Most flexible but potentially highest resource usage.
    /// Examples: windowing functions, complex joins, stateful processing.
    /// </summary>
    ManyToMany,

    /// <summary>
    /// Filtering transformation: produces subset of input chunks.
    /// Similar to one-to-one but may skip chunks entirely.
    /// Examples: data filtering, conditional processing, sampling.
    /// </summary>
    Filter
}