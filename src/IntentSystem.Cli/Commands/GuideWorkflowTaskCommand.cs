namespace IntentSystem.Cli.Commands;

/// <summary>
/// G335: dispatcher for the <c>guide workflow task</c> subcommand
/// group. Peels the next token (the task name) and delegates to the
/// per-task handler. Today only <c>init-host</c> is wired; future
/// tasks (deploy-host, retire-host, migrate-host) can plug in
/// without touching the router. Pure read-only — never mutates state,
/// never launches an AI provider.
/// </summary>
internal static class GuideWorkflowTaskCommand
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
            writer.WriteLine("guide workflow task requires a task name. Supported: init-host.");
            WriteHelp(writer);
            return 1;
        }

        var task = args[0];
        return task switch
        {
            "init-host" => GuideWorkflowTaskInitHostCommand.Execute(context, args[1..], writer),
            _ => UnknownTask(writer, task)
        };
    }

    private static int UnknownTask(TextWriter writer, string task)
    {
        writer.WriteLine($"Unknown 'guide workflow task' name '{task}'. Supported: init-host.");
        WriteHelp(writer);
        return 1;
    }

    private static void WriteHelp(TextWriter writer)
    {
        writer.WriteLine("guide workflow task");
        writer.WriteLine("Usage: intent-cli guide workflow task <task-name> [task-specific options]");
        writer.WriteLine();
        writer.WriteLine("Tasks:");
        writer.WriteLine("- init-host — explain host/review-runtime/child roles, name where `.intent-cli/` is required/optional/forbidden, emit scaffold plan (AGENTS.md / CLAUDE.md / host-binding.toml). Run with --role <role> for a single role; --force-host to scaffold a host role over an existing child cwd.");
    }
}
