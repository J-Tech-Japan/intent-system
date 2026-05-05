namespace IntentSystem.Cli.Commands;

/// <summary>
/// G265: Top-level router for <c>intent-cli guide worker ...</c>.
/// Currently routes the <c>issue-to-pr</c> sub-token to
/// <see cref="GuideWorkerIssueToPrCommand"/>. Future sub-tokens may be added
/// here without touching the CommandRouter.
/// </summary>
internal static class GuideWorkerCommand
{
    private const string UsageLine =
        "Usage: intent-cli guide worker <issue-to-pr> [--repo <owner/repo>] [--domain <name>] [--format markdown|json]";

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

        if (args.Length >= 1 && string.Equals(args[0], "issue-to-pr", StringComparison.Ordinal))
        {
            return GuideWorkerIssueToPrCommand.Execute(context, args[1..], writer);
        }

        writer.WriteLine("A subcommand is required for 'guide worker'.");
        writer.WriteLine(UsageLine);
        return 1;
    }

    private static void WriteHelp(TextWriter writer)
    {
        writer.WriteLine("guide worker");
        writer.WriteLine(UsageLine);
        writer.WriteLine("Subcommands: issue-to-pr");
    }
}
