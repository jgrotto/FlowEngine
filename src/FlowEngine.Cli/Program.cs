namespace FlowEngine.Cli;

/// <summary>
/// Entry point for the FlowEngine command-line interface.
/// </summary>
internal class Program
{
    /// <summary>
    /// Main entry point for the application.
    /// </summary>
    /// <param name="args">Command line arguments</param>
    /// <returns>Exit code</returns>
    static Task<int> Main(string[] args)
    {
        Console.WriteLine("FlowEngine CLI v1.0");
        Console.WriteLine("Core engine implementation ready for phase 1 testing.");
        return Task.FromResult(0);
    }
}