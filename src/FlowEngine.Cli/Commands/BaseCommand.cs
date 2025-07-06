namespace FlowEngine.Cli.Commands;

/// <summary>
/// Base class for CLI commands with common functionality.
/// </summary>
public abstract class BaseCommand : ICommand
{
    /// <inheritdoc />
    public abstract string Name { get; }

    /// <inheritdoc />
    public abstract string Description { get; }

    /// <inheritdoc />
    public abstract Task<int> ExecuteAsync(string[] args);

    /// <inheritdoc />
    public abstract void ShowHelp();

    /// <summary>
    /// Checks if help is requested in the arguments.
    /// </summary>
    /// <param name="args">Command arguments</param>
    /// <returns>True if help is requested</returns>
    protected static bool IsHelpRequested(string[] args)
    {
        return args.Contains("--help") || args.Contains("-h");
    }

    /// <summary>
    /// Checks if verbose output is requested.
    /// </summary>
    /// <param name="args">Command arguments</param>
    /// <returns>True if verbose output is requested</returns>
    protected static bool IsVerbose(string[] args)
    {
        return args.Contains("--verbose") || args.Contains("-v");
    }

    /// <summary>
    /// Writes an error message to stderr.
    /// </summary>
    /// <param name="message">Error message</param>
    protected static void WriteError(string message)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.Error.WriteLine($"Error: {message}");
        Console.ResetColor();
    }

    /// <summary>
    /// Writes a success message to stdout.
    /// </summary>
    /// <param name="message">Success message</param>
    protected static void WriteSuccess(string message)
    {
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"✓ {message}");
        Console.ResetColor();
    }

    /// <summary>
    /// Writes a warning message to stdout.
    /// </summary>
    /// <param name="message">Warning message</param>
    protected static void WriteWarning(string message)
    {
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine($"⚠ {message}");
        Console.ResetColor();
    }

    /// <summary>
    /// Writes an info message to stdout.
    /// </summary>
    /// <param name="message">Info message</param>
    protected static void WriteInfo(string message)
    {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine($"ℹ {message}");
        Console.ResetColor();
    }
}