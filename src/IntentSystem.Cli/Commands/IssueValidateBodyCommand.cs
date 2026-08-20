namespace IntentSystem.Cli.Commands;

/// <summary>
/// G183: Read-only <c>intent-cli issue validate-body --from-file &lt;path&gt;
/// [--format text|json]</c> command. Validates a standalone Markdown child
/// issue body against the host-review-loop Child Issue Contract: required
/// headings present, the <c>Target Repo / Path / Part</c> section contains a
/// target-path declaration, and <c>Related Links</c> contains at least one
/// non-placeholder list item. Exits 0 when valid and non-zero otherwise.
/// Never mutates parent queue state, runs, packets, GitHub objects, or labels.
/// </summary>
internal static class IssueValidateBodyCommand
{
    private const string FormatText = "text";
    private const string FormatJson = "json";

    public static int Execute(CliContext context, string[] args, TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(writer);

        if (!TryParseArguments(args, out var fromFile, out var format, out var error))
        {
            writer.WriteLine(error);
            return 1;
        }

        if (!File.Exists(fromFile))
        {
            writer.WriteLine($"--from-file path '{fromFile}' does not exist.");
            return 1;
        }

        var content = File.ReadAllText(fromFile);
        var result = IssueValidateBodyValidator.Validate(
            fromFile,
            content,
            requireTargetPathsDeclaration: true);

        if (string.Equals(format, FormatJson, StringComparison.Ordinal))
        {
            IssueValidateBodyRenderer.WriteJson(writer, result);
        }
        else
        {
            IssueValidateBodyRenderer.WriteText(writer, result);
        }

        return result.IsValid ? 0 : 1;
    }

    private static bool TryParseArguments(
        string[] args,
        out string fromFile,
        out string format,
        out string error)
    {
        fromFile = string.Empty;
        format = FormatText;
        error = string.Empty;

        for (var index = 0; index < args.Length; index++)
        {
            var argument = args[index];
            switch (argument)
            {
                case "--from-file":
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                    {
                        error = "--from-file requires a non-empty path.";
                        return false;
                    }

                    fromFile = args[index + 1];
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
                    error = $"Unknown argument '{argument}'. Supported: --from-file <path> --format text|json.";
                    return false;
            }
        }

        if (string.IsNullOrWhiteSpace(fromFile))
        {
            error = "--from-file is required.";
            return false;
        }

        return true;
    }
}
