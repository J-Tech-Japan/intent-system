namespace IntentSystem.Cli.Commands;

/// <summary>
/// G181: Read-only <c>intent-cli clarify draft</c> command. Emits a structured
/// clarification draft scaffold (background, question, options, pros/cons,
/// recommendation, return path) so the AI tasking thread + owner can review a
/// consistent shape before a follow-up command records the accepted decision.
/// Never mutates clarifications/open.md or any other state.
/// </summary>
internal static class ClarifyDraftCommand
{
    private const string FormatMarkdown = "markdown";
    private const string FormatJson = "json";

    public static int Execute(CliContext context, string[] args, TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(writer);

        if (!TryParseArguments(args, out var domainOverride, out var question, out var format, out var error))
        {
            writer.WriteLine(error);
            return 1;
        }

        var packet = ClarifyDraftAnalyzer.Analyze(context, domainOverride, question);

        if (string.Equals(format, FormatJson, StringComparison.Ordinal))
        {
            ClarifyDraftRenderer.WriteJson(writer, packet);
        }
        else
        {
            ClarifyDraftRenderer.WriteMarkdown(writer, packet);
        }

        return 0;
    }

    private static bool TryParseArguments(
        string[] args,
        out string? domainOverride,
        out string question,
        out string format,
        out string error)
    {
        domainOverride = null;
        question = string.Empty;
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

                case "--question":
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                    {
                        error = "--question requires a value.";
                        return false;
                    }

                    question = args[index + 1];
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
                    error = $"Unknown argument '{argument}'. Supported: --domain <name> --question <text> --format markdown|json.";
                    return false;
            }
        }

        if (string.IsNullOrWhiteSpace(question))
        {
            error = "--question is required.";
            return false;
        }

        return true;
    }
}
