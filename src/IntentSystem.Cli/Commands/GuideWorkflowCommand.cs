namespace IntentSystem.Cli.Commands;

/// <summary>
/// G255: Dispatcher for the <c>guide workflow</c> subcommand group. The
/// command router treats <c>guide</c> as a top-level group and
/// <c>workflow</c> as its subcommand; this dispatcher peels the next
/// token (<c>suggest</c>) and delegates to the matching handler.
/// </summary>
internal static class GuideWorkflowCommand
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
            writer.WriteLine("guide workflow requires a subcommand. Supported: suggest.");
            WriteHelp(writer);
            return 1;
        }

        var subcommand = args[0];
        return subcommand switch
        {
            "suggest" => GuideWorkflowSuggestCommand.Execute(context, args[1..], writer),
            _ => UnknownSubcommand(writer, subcommand)
        };
    }

    private static int UnknownSubcommand(TextWriter writer, string subcommand)
    {
        writer.WriteLine($"Unknown 'guide workflow' subcommand '{subcommand}'. Supported: suggest.");
        WriteHelp(writer);
        return 1;
    }

    private static void WriteHelp(TextWriter writer)
    {
        writer.WriteLine("guide workflow");
        writer.WriteLine("Usage: intent-cli guide workflow suggest [--domain <name>] (--goal <text> | --from-file <path>) [--format markdown|json]");
        writer.WriteLine();
        writer.WriteLine("Subcommands:");
        writer.WriteLine("- suggest — recommend a workflow + commands + rule topics for a broad operator goal");
    }
}
