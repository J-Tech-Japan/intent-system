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
            writer.WriteLine("guide workflow requires a subcommand. Supported: suggest, task.");
            WriteHelp(writer);
            return 1;
        }

        var subcommand = args[0];
        return subcommand switch
        {
            "suggest" => GuideWorkflowSuggestCommand.Execute(context, args[1..], writer),
            // G335: `guide workflow task <task-name>` returns a
            // bounded scaffold/init plan. Today only `init-host` is
            // wired; future tasks plug in via the GuideWorkflowTaskCommand
            // dispatcher.
            "task" => GuideWorkflowTaskCommand.Execute(context, args[1..], writer),
            _ => UnknownSubcommand(writer, subcommand)
        };
    }

    private static int UnknownSubcommand(TextWriter writer, string subcommand)
    {
        writer.WriteLine($"Unknown 'guide workflow' subcommand '{subcommand}'. Supported: suggest, task.");
        WriteHelp(writer);
        return 1;
    }

    private static void WriteHelp(TextWriter writer)
    {
        writer.WriteLine("guide workflow");
        writer.WriteLine("Usage:");
        writer.WriteLine("  intent-cli guide workflow suggest [--domain <name>] (--goal <text> | --from-file <path>) [--format markdown|json]");
        writer.WriteLine("  intent-cli guide workflow task <task-name> [task-specific options]");
        writer.WriteLine();
        writer.WriteLine("Subcommands:");
        writer.WriteLine("- suggest — recommend a workflow + commands + rule topics for a broad operator goal");
        writer.WriteLine("- task — bounded scaffold/init plans; today: `task init-host` (G335), `task intent-interview` (G336), `task packet-draft` (G337: packet files + standalone issue contract), `task issue-publish` (G337: draft/create/publish-flow/automation issue-publish boundary)");
    }
}
