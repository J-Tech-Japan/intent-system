namespace IntentSystem.Cli.Commands;

/// <summary>
/// G179: Read-only <c>intent-cli status brief</c> command. Emits a compact,
/// AI-thread-friendly snapshot of the current intent workspace so the human-operated
/// Codex / Claude tasking thread can decide its next move without launching nested
/// AI processes. Never mutates state.
/// </summary>
internal static class StatusBriefCommand
{
    private const string FormatText = "text";
    private const string FormatJson = "json";

    public static int Execute(CliContext context, string[] args, TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(writer);

        if (!TryParseArguments(args, out var domainOverride, out var team, out var format, out var error))
        {
            writer.WriteLine(error);
            return 1;
        }

        var summary = StatusBriefAnalyzer.Analyze(context, domainOverride, team);

        if (string.Equals(format, FormatJson, StringComparison.Ordinal))
        {
            StatusBriefRenderer.WriteJson(writer, summary);
        }
        else
        {
            StatusBriefRenderer.WriteText(writer, summary);
        }

        return 0;
    }

    private static bool TryParseArguments(
        string[] args,
        out string? domainOverride,
        out string? team,
        out string format,
        out string error)
    {
        domainOverride = null;
        team = null;
        format = FormatText;
        error = string.Empty;

        for (var index = 0; index < args.Length; index++)
        {
            var argument = args[index];
            switch (argument)
            {
                case "--domain":
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                    {
                        error = "--domain requires a value.";
                        return false;
                    }

                    domainOverride = args[index + 1];
                    index++;
                    break;

                case "--team":
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                    {
                        error = "--team requires a value.";
                        return false;
                    }

                    team = args[index + 1];
                    index++;
                    break;

                case "--format":
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                    {
                        error = "--format requires a value (text or json).";
                        return false;
                    }

                    var requestedFormat = args[index + 1];
                    if (!string.Equals(requestedFormat, FormatText, StringComparison.Ordinal)
                        && !string.Equals(requestedFormat, FormatJson, StringComparison.Ordinal))
                    {
                        error = $"--format must be 'text' or 'json' (got '{requestedFormat}').";
                        return false;
                    }

                    format = requestedFormat;
                    index++;
                    break;

                default:
                    error = $"Unknown argument '{argument}'. Supported: --domain <name> --team <name> --format text|json.";
                    return false;
            }
        }

        return true;
    }
}
