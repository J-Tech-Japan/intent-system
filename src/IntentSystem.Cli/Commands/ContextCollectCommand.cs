namespace IntentSystem.Cli.Commands;

/// <summary>
/// G180: Read-only <c>intent-cli context collect</c> command. Aggregates
/// queue-state, runs.jsonl, parent-host clarification and automation binding
/// files, and packet paths for the focus execution unit into a single Markdown
/// (default) or JSON packet for the high-context AI tasking thread. Never
/// mutates state.
/// </summary>
internal static class ContextCollectCommand
{
    private const string FormatMarkdown = "markdown";
    private const string FormatJson = "json";

    public static int Execute(CliContext context, string[] args, TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(writer);

        if (!TryParseArguments(args, out var domainOverride, out var format, out var error))
        {
            writer.WriteLine(error);
            return 1;
        }

        var packet = ContextCollectAnalyzer.Analyze(context, domainOverride);

        if (string.Equals(format, FormatJson, StringComparison.Ordinal))
        {
            ContextCollectRenderer.WriteJson(writer, packet);
        }
        else
        {
            ContextCollectRenderer.WriteMarkdown(writer, packet);
        }

        return 0;
    }

    private static bool TryParseArguments(
        string[] args,
        out string? domainOverride,
        out string format,
        out string error)
    {
        domainOverride = null;
        format = FormatMarkdown;
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

                case "--format":
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                    {
                        error = "--format requires a value (markdown or json).";
                        return false;
                    }

                    var requestedFormat = args[index + 1];
                    if (!string.Equals(requestedFormat, FormatMarkdown, StringComparison.Ordinal)
                        && !string.Equals(requestedFormat, FormatJson, StringComparison.Ordinal))
                    {
                        error = $"--format must be 'markdown' or 'json' (got '{requestedFormat}').";
                        return false;
                    }

                    format = requestedFormat;
                    index++;
                    break;

                default:
                    error = $"Unknown argument '{argument}'. Supported: --domain <name> --format markdown|json.";
                    return false;
            }
        }

        return true;
    }
}
