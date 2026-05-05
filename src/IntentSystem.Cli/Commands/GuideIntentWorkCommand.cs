namespace IntentSystem.Cli.Commands;

/// <summary>
/// G261: Top-level router for <c>intent-cli guide intent-work ...</c>.
/// Currently routes the <c>setup</c> sub-token to
/// <see cref="GuideIntentWorkSetupCommand"/>. Future sub-tokens may be added
/// here without touching the CommandRouter.
/// </summary>
internal static class GuideIntentWorkCommand
{
    private const string UsageLine =
        "Usage: intent-cli guide intent-work setup --kind <domain-organize|next-slice|packet-preload|clarification> "
        + "--domain <domain> --target-repo <owner/repo> [--format markdown|json]";

    public static int Execute(CliContext context, string[] args, TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(writer);

        if (args.Length >= 1 && string.Equals(args[0], "setup", StringComparison.Ordinal))
        {
            return GuideIntentWorkSetupCommand.Execute(context, args[1..], writer);
        }

        if (args.Length == 1 && string.Equals(args[0], "--help", StringComparison.Ordinal))
        {
            WriteHelp(writer);
            return 0;
        }

        writer.WriteLine("A subcommand is required for 'guide intent-work'.");
        writer.WriteLine(UsageLine);
        return 1;
    }

    private static void WriteHelp(TextWriter writer)
    {
        writer.WriteLine("guide intent-work");
        writer.WriteLine(UsageLine);
        writer.WriteLine("Subcommands: setup");
    }
}
