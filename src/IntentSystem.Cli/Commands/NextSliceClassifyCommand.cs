namespace IntentSystem.Cli.Commands;

/// <summary>
/// G185: Read-only <c>intent-cli next-slice classify</c> command. Emits a single
/// deterministic continuation classification for the AI tasking thread without
/// mutating queue state, runs, GitHub, or source files.
/// </summary>
internal static class NextSliceClassifyCommand
{
    private const string FormatText = "text";
    private const string FormatJson = "json";

    public static int Execute(CliContext context, string[] args, TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(writer);

        if (!TryParseArguments(args, out var domainOverride, out var targetRepo, out var format, out var error))
        {
            writer.WriteLine(error);
            return 1;
        }

        var result = NextSliceClassifyAnalyzer.Analyze(context, domainOverride, targetRepo);

        if (string.Equals(format, FormatJson, StringComparison.Ordinal))
        {
            NextSliceClassifyRenderer.WriteJson(writer, result);
        }
        else
        {
            NextSliceClassifyRenderer.WriteText(writer, result);
        }

        return 0;
    }

    private static bool TryParseArguments(
        string[] args,
        out string? domainOverride,
        out string? targetRepo,
        out string format,
        out string error)
    {
        domainOverride = null;
        targetRepo = null;
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

                case "--target-repo":
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                    {
                        error = "--target-repo requires a value.";
                        return false;
                    }

                    targetRepo = args[index + 1];
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
                    error = $"Unknown argument '{argument}'. Supported: --domain <name> --target-repo <owner/repo> --format text|json.";
                    return false;
            }
        }

        return true;
    }
}
