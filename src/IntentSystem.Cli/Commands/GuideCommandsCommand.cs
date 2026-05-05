namespace IntentSystem.Cli.Commands;

/// <summary>
/// G257: Dispatcher for the <c>guide commands</c> subcommand group. Peels
/// the next token (<c>list</c>) and delegates to the matching handler.
/// </summary>
internal static class GuideCommandsCommand
{
    public static int Execute(CliContext context, string[] args, TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(writer);

        if (args.Length == 1 && string.Equals(args[0], "--help", StringComparison.Ordinal))
        {
            WriteHelp(writer);
            return 0;
        }

        if (args.Length == 0)
        {
            writer.WriteLine("guide commands requires a subcommand. Supported: list.");
            WriteHelp(writer);
            return 1;
        }

        var subcommand = args[0];
        return subcommand switch
        {
            "list" => GuideCommandsListCommand.Execute(context, args[1..], writer),
            _ => UnknownSubcommand(writer, subcommand)
        };
    }

    private static int UnknownSubcommand(TextWriter writer, string subcommand)
    {
        writer.WriteLine($"Unknown 'guide commands' subcommand '{subcommand}'. Supported: list.");
        WriteHelp(writer);
        return 1;
    }

    private static void WriteHelp(TextWriter writer)
    {
        writer.WriteLine("guide commands");
        writer.WriteLine("Usage: intent-cli guide commands list [--format markdown|json]");
        writer.WriteLine();
        writer.WriteLine("Subcommands:");
        writer.WriteLine("- list — list intent-cli command groups with primary/support/advanced/experimental classification");
    }
}
