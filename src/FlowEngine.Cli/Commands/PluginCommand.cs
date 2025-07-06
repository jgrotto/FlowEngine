using FlowEngine.Cli.Commands.Plugin;

namespace FlowEngine.Cli.Commands;

/// <summary>
/// Main plugin command that handles plugin development subcommands.
/// </summary>
public class PluginCommand : BaseCommand
{
    private readonly Dictionary<string, ICommand> _subCommands;

    /// <summary>
    /// Initializes a new plugin command.
    /// </summary>
    public PluginCommand()
    {
        _subCommands = new Dictionary<string, ICommand>
        {
            ["create"] = new PluginCreateCommand(),
            ["validate"] = new PluginValidateCommand(),
            ["test"] = new PluginTestCommand(),
            ["benchmark"] = new PluginBenchmarkCommand()
        };
    }

    /// <inheritdoc />
    public override string Name => "plugin";

    /// <inheritdoc />
    public override string Description => "Plugin development commands";

    /// <inheritdoc />
    public override async Task<int> ExecuteAsync(string[] args)
    {
        if (args.Length == 0 || IsHelpRequested(args))
        {
            ShowHelp();
            return args.Length == 0 ? 1 : 0;
        }

        var subCommandName = args[0].ToLowerInvariant();
        
        if (_subCommands.TryGetValue(subCommandName, out var subCommand))
        {
            // Pass remaining arguments to subcommand
            var subArgs = args.Skip(1).ToArray();
            return await subCommand.ExecuteAsync(subArgs);
        }
        else
        {
            WriteError($"Unknown plugin command: {args[0]}");
            Console.WriteLine();
            ShowHelp();
            return 1;
        }
    }

    /// <inheritdoc />
    public override void ShowHelp()
    {
        Console.WriteLine("Usage: flowengine plugin <command> [options]");
        Console.WriteLine();
        Console.WriteLine("Plugin development commands:");
        Console.WriteLine();
        
        foreach (var subCommand in _subCommands.Values)
        {
            Console.WriteLine($"  {subCommand.Name,-12} {subCommand.Description}");
        }
        
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine("  --help, -h     Show this help");
        Console.WriteLine();
        Console.WriteLine("Examples:");
        Console.WriteLine("  flowengine plugin create MySourcePlugin");
        Console.WriteLine("  flowengine plugin validate ./MyPlugin");
        Console.WriteLine("  flowengine plugin test ./MyPlugin --verbose");
        Console.WriteLine("  flowengine plugin benchmark ./MyPlugin --quick");
        Console.WriteLine();
        Console.WriteLine("Use 'flowengine plugin <command> --help' for detailed command help.");
    }
}