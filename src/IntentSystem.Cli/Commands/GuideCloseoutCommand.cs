namespace IntentSystem.Cli.Commands;

/// <summary>
/// G268: Top-level router for <c>intent-cli guide closeout ...</c>.
/// Routes the <c>run</c> sub-token to <see cref="GuideCloseoutRunCommand"/>.
/// </summary>
internal static class GuideCloseoutCommand
{
    private const string UsageLine =
        "Usage: intent-cli guide closeout <run> [--repo <owner/repo>] [--domain <name>] [--format markdown|json]";

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

        if (args.Length >= 1 && string.Equals(args[0], "run", StringComparison.Ordinal))
        {
            return GuideCloseoutRunCommand.Execute(context, args[1..], writer);
        }

        writer.WriteLine("A subcommand is required for 'guide closeout'.");
        writer.WriteLine(UsageLine);
        return 1;
    }

    private static void WriteHelp(TextWriter writer)
    {
        writer.WriteLine("guide closeout");
        writer.WriteLine(UsageLine);
        writer.WriteLine("Subcommands: run");
    }
}
