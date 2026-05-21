using System.Text.Json;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G382: read-only <c>intent-cli guide interview-readiness</c>. The
/// measurable finish line for the persistent interview mode (G381): it
/// tells an agent whether an interview has enough resolved information to
/// draft a packet or publish an issue. With no input it prints the
/// readiness checklist (all dimensions + the classification legend). With
/// <c>--resolved &lt;keys&gt;</c> it runs
/// <see cref="InterviewReadinessAnalyzer"/> and reports the verdict
/// (<c>packet-ready</c> / <c>issue-ready</c> / <c>clarification-required</c>
/// / <c>remaining-gaps</c>), the per-dimension checklist, the concrete
/// missing dimensions, and the next highest-value question. Advisory:
/// host-state-free, never publishes, never launches an AI provider.
/// </summary>
internal static class GuideInterviewReadinessCommand
{
    private const string FormatMarkdown = "markdown";
    private const string FormatJson = "json";

    private const string UsageLine =
        "Usage: intent-cli guide interview-readiness [--resolved <key,key,...>] [--format markdown|json]";

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

        if (!TryParseArguments(args, out var resolved, out var hasResolvedFlag, out var format, out var error))
        {
            writer.WriteLine(error);
            writer.WriteLine(UsageLine);
            return 1;
        }

        // No `--resolved` → emit the static checklist (the "what does
        // ready mean?" path). With `--resolved` → evaluate the verdict.
        var result = InterviewReadinessAnalyzer.Analyze(resolved);

        if (string.Equals(format, FormatJson, StringComparison.Ordinal))
        {
            writer.Write(JsonSerializer.Serialize(result, JsonOptions));
            writer.WriteLine();
        }
        else
        {
            WriteMarkdown(writer, result, evaluated: hasResolvedFlag);
        }

        return 0;
    }

    private static void WriteMarkdown(TextWriter writer, InterviewReadinessResult result, bool evaluated)
    {
        writer.WriteLine("# Guide — interview readiness checklist");
        writer.WriteLine();
        writer.WriteLine("Advisory readiness gate for moving an interview to a packet/issue. It does not publish anything; canonical writes still require operator acceptance.");
        writer.WriteLine();

        if (evaluated)
        {
            writer.WriteLine($"## Verdict: `{result.Classification}`");
            writer.WriteLine($"- {result.Summary}");
            writer.WriteLine();
        }

        writer.WriteLine("## Dimensions");
        foreach (var dimension in result.Dimensions)
        {
            var mark = evaluated ? (dimension.Resolved ? "[x] " : "[ ] ") : "- ";
            writer.WriteLine($"{mark}{dimension.Key} ({dimension.Tier}) — {dimension.Name}");
        }
        writer.WriteLine();

        if (evaluated)
        {
            writer.WriteLine("## Missing");
            if (result.MissingDimensions.Count == 0)
            {
                writer.WriteLine("- (none)");
            }
            else
            {
                foreach (var key in result.MissingDimensions)
                {
                    writer.WriteLine($"- {key}");
                }
            }
            writer.WriteLine();

            if (!string.IsNullOrWhiteSpace(result.NextQuestion))
            {
                writer.WriteLine("## Next question");
                writer.WriteLine($"- ({result.NextQuestionDimension}) {result.NextQuestion}");
                writer.WriteLine();
            }
        }

        writer.WriteLine("## Classification legend");
        writer.WriteLine("- `packet-ready`: all issue + packet dimensions resolved, no blocking decision — draft the packet (with operator acceptance).");
        writer.WriteLine("- `issue-ready`: issue-contract dimensions resolved, no blocking decision — publish the issue (with operator acceptance).");
        writer.WriteLine("- `clarification-required`: a blocking owner/open decision is unresolved — resolve it before drafting.");
        writer.WriteLine("- `remaining-gaps`: issue-contract dimensions still missing — keep interviewing; ask the next question above.");
        writer.WriteLine();
        writer.WriteLine("Evaluate your interview: `intent-cli guide interview-readiness --resolved goal,scope,target,acceptance,verification,... --format json`.");
    }

    private static bool TryParseArguments(
        string[] args,
        out IReadOnlyCollection<string> resolved,
        out bool hasResolvedFlag,
        out string format,
        out string error)
    {
        var resolvedList = new List<string>();
        resolved = resolvedList;
        hasResolvedFlag = false;
        format = FormatMarkdown;
        error = string.Empty;

        for (var index = 0; index < args.Length; index++)
        {
            var argument = args[index];
            switch (argument)
            {
                case "--resolved":
                    if (index + 1 >= args.Length)
                    {
                        error = "--resolved requires a comma-separated list of dimension keys (or an empty value).";
                        return false;
                    }
                    hasResolvedFlag = true;
                    foreach (var key in args[index + 1].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                    {
                        resolvedList.Add(key);
                    }
                    index++;
                    break;

                case "--format":
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                    {
                        error = "--format requires a value (markdown or json).";
                        return false;
                    }
                    var requested = args[index + 1];
                    if (!string.Equals(requested, FormatMarkdown, StringComparison.Ordinal)
                        && !string.Equals(requested, FormatJson, StringComparison.Ordinal))
                    {
                        error = $"--format must be 'markdown' or 'json' (got '{requested}').";
                        return false;
                    }
                    format = requested;
                    index++;
                    break;

                default:
                    error = $"Unknown argument '{argument}'.";
                    return false;
            }
        }

        return true;
    }

    private static void WriteHelp(TextWriter writer)
    {
        writer.WriteLine("guide interview-readiness");
        writer.WriteLine(UsageLine);
        writer.WriteLine("Read-only readiness checklist + classification (packet-ready / issue-ready / clarification-required / remaining-gaps) with the next highest-value question.");
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };
}
