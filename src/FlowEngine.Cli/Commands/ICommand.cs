namespace FlowEngine.Cli.Commands;

/// <summary>
/// Interface for CLI commands.
/// </summary>
public interface ICommand
{
    /// <summary>
    /// Gets the command name.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Gets the command description.
    /// </summary>
    string Description { get; }

    /// <summary>
    /// Executes the command.
    /// </summary>
    /// <param name="args">Command arguments</param>
    /// <returns>Exit code</returns>
    Task<int> ExecuteAsync(string[] args);

    /// <summary>
    /// Shows help for the command.
    /// </summary>
    void ShowHelp();
}
