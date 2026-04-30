using System.Text.Json;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G207: <c>intent-cli metadata validate --root &lt;path&gt; --execution-unit &lt;ID&gt;
/// [--format text|json]</c>. Read-only mechanical validator for parent-host
/// packet metadata graphs.
///
/// No-mutation invariants (verified by tests):
/// - never invokes <c>NestedProviderLauncher</c>;
/// - never edits files (whole-workspace byte-snapshot before/after);
/// - source-scan asserts the analyzer + command files contain no
///   <c>Process.Start(</c>, no <c>gh issue edit</c>/<c>gh pr edit</c>/
///   <c>gh pr merge</c>/<c>gh pr close</c>/<c>gh pr reopen</c>/
///   <c>gh pr comment</c>/<c>gh pr review</c> literals in executable code,
///   and no <c>resolveReviewThread</c> mutation literal.
/// </summary>
internal static class MetadataValidateCommand
{
    private const string FormatText = "text";
    private const string FormatJson = "json";

    /// <summary>
    /// Test sentinel: must NEVER be invoked. Tests assert it remains
    /// uninvoked across all paths.
    /// </summary>
    public static Func<bool>? NestedProviderLauncher { get; set; }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.Never,
    };

    public static int Execute(CliContext context, string[] args, TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(writer);

        if (!TryParseArguments(args, out var root, out var executionUnit, out var format, out var error))
        {
            writer.WriteLine(error);
            return 1;
        }

        var rootPath = string.IsNullOrWhiteSpace(root)
            ? context.RepoRoot ?? Directory.GetCurrentDirectory()
            : root!;

        MetadataValidateInputs inputs;
        try
        {
            inputs = LoadInputs(rootPath, executionUnit!);
        }
        catch (Exception exception) when (
            exception is IOException
            or UnauthorizedAccessException)
        {
            writer.WriteLine($"failed to load metadata for {executionUnit} under {rootPath}: {exception.Message}");
            return 1;
        }

        MetadataValidateResult result;
        try
        {
            result = MetadataValidateAnalyzer.Analyze(inputs);
        }
        catch (Exception exception) when (
            exception is ArgumentException
            or InvalidOperationException)
        {
            writer.WriteLine($"failed to validate metadata for {executionUnit}: {exception.Message}");
            return 1;
        }

        if (string.Equals(format, FormatJson, StringComparison.Ordinal))
        {
            writer.WriteLine(JsonSerializer.Serialize(result, JsonOptions));
        }
        else
        {
            WriteText(writer, result);
        }

        return result.Valid ? 0 : 1;
    }

    private static MetadataValidateInputs LoadInputs(string rootPath, string executionUnit)
    {
        var unitDir = Path.Combine(rootPath, ".intent-cli", "issues", executionUnit);
        var queueStatePath = Path.Combine(rootPath, ".intent-cli", "queue-state.json");

        return new MetadataValidateInputs
        {
            ExecutionUnit = executionUnit,
            PacketYaml = ReadIfExists(Path.Combine(unitDir, "packet.yaml")),
            GithubBodyMarkdown = ReadIfExists(Path.Combine(unitDir, "github-body.md")),
            ReviewContextMarkdown = ReadIfExists(Path.Combine(unitDir, "review-context.md")),
            ImplementationMarkdown = ReadIfExists(Path.Combine(unitDir, "implementation.md")),
            PublishYaml = ReadIfExists(Path.Combine(unitDir, "publish.yaml")),
            QueueStateJson = ReadIfExists(queueStatePath),
        };
    }

    private static string? ReadIfExists(string path) =>
        File.Exists(path) ? File.ReadAllText(path) : null;

    private static void WriteText(TextWriter writer, MetadataValidateResult result)
    {
        writer.WriteLine($"# Metadata validation for {result.ExecutionUnit}");
        writer.WriteLine();
        writer.WriteLine($"- valid: {result.Valid.ToString().ToLowerInvariant()}");
        writer.WriteLine($"- errors: {result.Errors.Count}");
        writer.WriteLine($"- warnings: {result.Warnings.Count}");
        writer.WriteLine();

        if (result.Errors.Count > 0)
        {
            writer.WriteLine("## Errors");
            foreach (var finding in result.Errors)
            {
                writer.WriteLine($"- [{finding.Code}] {finding.Message} ({finding.Path})");
            }
            writer.WriteLine();
        }

        if (result.Warnings.Count > 0)
        {
            writer.WriteLine("## Warnings");
            foreach (var finding in result.Warnings)
            {
                writer.WriteLine($"- [{finding.Code}] {finding.Message} ({finding.Path})");
            }
            writer.WriteLine();
        }

        writer.WriteLine("## Checked files");
        foreach (var file in result.CheckedFiles)
        {
            writer.WriteLine($"- {file}");
        }
    }

    private static bool TryParseArguments(
        string[] args,
        out string? root,
        out string? executionUnit,
        out string format,
        out string error)
    {
        root = null;
        executionUnit = null;
        format = FormatText;
        error = string.Empty;

        for (var index = 0; index < args.Length; index++)
        {
            var argument = args[index];
            switch (argument)
            {
                case "--root":
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                    {
                        error = "--root requires a value (path).";
                        return false;
                    }
                    root = args[index + 1];
                    index++;
                    break;

                case "--execution-unit":
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                    {
                        error = "--execution-unit requires a value (e.g. G206).";
                        return false;
                    }
                    executionUnit = args[index + 1];
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
                    error = $"Unknown argument '{argument}'. Supported: --root <path> --execution-unit <ID> [--format text|json].";
                    return false;
            }
        }

        if (string.IsNullOrWhiteSpace(executionUnit))
        {
            error = "--execution-unit is required (e.g. --execution-unit G206).";
            return false;
        }

        return true;
    }
}
