using FlowEngine.Abstractions.Data;

namespace FlowEngine.Abstractions.Plugins;

/// <summary>
/// Interface for plugins that need to be aware of their input schema before they can function.
/// Plugins implementing this interface will have their schema set during pipeline initialization.
/// </summary>
public interface ISchemaAwarePlugin
{
    /// <summary>
    /// Sets the input schema for this plugin.
    /// This method is called during pipeline initialization before execution begins.
    /// </summary>
    /// <param name="schema">The input schema for this plugin</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Task representing the schema setup operation</returns>
    Task SetSchemaAsync(ISchema schema, CancellationToken cancellationToken = default);
}