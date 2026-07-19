using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G251: <c>intent-cli intent draft-from-interview</c>. Compiles accepted
/// interview answers into a draft intent/packet candidate at
/// <c>intents/&lt;domain&gt;/drafts/&lt;session&gt;.md</c>. Without
/// <c>--write</c> the draft is emitted but not written. Never publishes
/// a GitHub issue and never launches an AI provider.
/// </summary>
internal static class IntentDraftFromInterviewCommand
{
    private const string FormatJson = "json";
    private const string FormatMarkdown = "markdown";

    private const string ModeWrite = "write";
    private const string ModeDryRun = "dry-run";

    private const string UsageLine =
        "Usage: intent-cli intent draft-from-interview --session <id> [--domain <name>] [--write] [--format markdown|json]";

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

        if (!TryParseArguments(args, out var session, out var domainOverride, out var write, out var format, out var error))
        {
            writer.WriteLine(error);
            writer.WriteLine(UsageLine);
            return 1;
        }

        var domain = string.IsNullOrWhiteSpace(domainOverride)
            ? context.Config.Project.Domain
            : domainOverride!;

        var sessionPath = InterviewSessionStore.ResolvePath(context.RepoRoot, domain, session!);
        var stored = InterviewSessionStore.Read(sessionPath);

        if (stored is null)
        {
            EmitFailure(writer, format, NewFailureResult(domain, session!, sessionPath, write,
                $"interview session not found: {sessionPath}"));
            return 1;
        }

        var accepted = stored.Questions.Where(q => !string.IsNullOrEmpty(q.Answer)).ToArray();
        var open = stored.Questions.Where(q => string.IsNullOrEmpty(q.Answer)).ToArray();
        if (accepted.Length == 0)
        {
            EmitFailure(writer, format, NewFailureResult(domain, session!, sessionPath, write,
                "interview session has no accepted answers; record at least one with `interview record-answer --write`."));
            return 1;
        }

        var draft = BuildDraftMarkdown(domain, session!, accepted, open);
        var draftPath = ResolveDraftPath(context.RepoRoot, domain, session!);

        if (write)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(draftPath)!);
            File.WriteAllText(draftPath, draft);
        }

        var result = new IntentDraftFromInterviewResult
        {
            Domain = domain,
            Session = session!,
            SessionPath = sessionPath,
            DraftPath = draftPath,
            Mode = write ? ModeWrite : ModeDryRun,
            AcceptedCount = accepted.Length,
            OpenCount = open.Length,
            DraftMarkdown = draft,
            Error = null
        };

        if (string.Equals(format, FormatJson, StringComparison.Ordinal))
        {
            writer.Write(JsonSerializer.Serialize(result, JsonOptions));
            writer.WriteLine();
        }
        else
        {
            WriteMarkdown(writer, result);
        }

        return 0;
    }

    private static string ResolveDraftPath(string repoRoot, string domain, string session)
    {
        return Path.Combine(repoRoot, "intents", domain, "drafts", $"{session}.md");
    }

    private static string BuildDraftMarkdown(string domain, string session, IReadOnlyList<InterviewQuestion> accepted, IReadOnlyList<InterviewQuestion> open)
    {
        var builder = new StringBuilder();
        builder.AppendLine("---");
        builder.AppendLine("# Optional semantic facets (G529) — closed set, one line each:");
        builder.AppendLine("#   vocabulary            — event/command vocabulary: what counts as a fact");
        builder.AppendLine("#   invariant              — invariants and consistency boundaries");
        builder.AppendLine("#   decider                — decider judgments: what a command decides");
        builder.AppendLine("#   acceptance-property    — what must not break");
        builder.AppendLine("# Uncomment and edit to annotate this node, e.g.:");
        builder.AppendLine("# facets: [vocabulary]");
        builder.AppendLine("---");
        builder.AppendLine();
        builder.AppendLine($"# Draft intent — {domain} / session {session}");
        builder.AppendLine();
        builder.AppendLine("> Compiled from accepted interview answers. Operator must accept this draft before any source-of-truth mutation.");
        builder.AppendLine();

        builder.AppendLine("## Accepted baseline");
        foreach (var question in accepted)
        {
            builder.AppendLine();
            builder.AppendLine($"### {question.Id} — {question.Prompt}");
            builder.AppendLine();
            builder.AppendLine(question.Answer);
        }
        builder.AppendLine();

        builder.AppendLine("## Open questions");
        if (open.Count == 0)
        {
            builder.AppendLine();
            builder.AppendLine("- none");
        }
        else
        {
            foreach (var question in open)
            {
                builder.AppendLine($"- **{question.Id}** — {question.Prompt}");
            }
        }

        builder.AppendLine();
        builder.AppendLine("## Candidate execution units");
        builder.AppendLine();
        builder.AppendLine("- TODO — operator decides whether to promote this draft into a published child issue.");

        return builder.ToString();
    }

    private static IntentDraftFromInterviewResult NewFailureResult(string domain, string session, string sessionPath, bool write, string message)
    {
        return new IntentDraftFromInterviewResult
        {
            Domain = domain,
            Session = session,
            SessionPath = sessionPath,
            DraftPath = "(unresolved)",
            Mode = write ? ModeWrite : ModeDryRun,
            AcceptedCount = 0,
            OpenCount = 0,
            DraftMarkdown = string.Empty,
            Error = message
        };
    }

    private static void EmitFailure(TextWriter writer, string format, IntentDraftFromInterviewResult error)
    {
        if (string.Equals(format, FormatJson, StringComparison.Ordinal))
        {
            writer.Write(JsonSerializer.Serialize(error, JsonOptions));
            writer.WriteLine();
        }
        else
        {
            writer.WriteLine($"# Intent draft-from-interview — {error.Domain} / {error.Session}");
            writer.WriteLine();
            writer.WriteLine($"## Error");
            writer.WriteLine($"- {error.Error}");
        }
    }

    private static void WriteMarkdown(TextWriter writer, IntentDraftFromInterviewResult result)
    {
        writer.WriteLine($"# Intent draft-from-interview — {result.Domain} / {result.Session}");
        writer.WriteLine();
        writer.WriteLine($"- session path: {result.SessionPath}");
        writer.WriteLine($"- draft path: {result.DraftPath}");
        writer.WriteLine($"- mode: {result.Mode}");
        writer.WriteLine($"- accepted count: {result.AcceptedCount}");
        writer.WriteLine($"- open count: {result.OpenCount}");
        writer.WriteLine();
        writer.WriteLine("## Draft");
        writer.WriteLine();
        writer.WriteLine(result.DraftMarkdown);
    }

    private static bool TryParseArguments(
        string[] args,
        out string? session,
        out string? domainOverride,
        out bool write,
        out string format,
        out string error)
    {
        session = null;
        domainOverride = null;
        write = false;
        format = FormatMarkdown;
        error = string.Empty;

        for (var index = 0; index < args.Length; index++)
        {
            var argument = args[index];
            switch (argument)
            {
                case "--session":
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                    {
                        error = "--session requires a value.";
                        return false;
                    }

                    session = args[index + 1];
                    index++;
                    break;

                case "--domain":
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                    {
                        error = "--domain requires a value.";
                        return false;
                    }

                    domainOverride = args[index + 1];
                    index++;
                    break;

                case "--write":
                    write = true;
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

        if (string.IsNullOrWhiteSpace(session))
        {
            error = "--session is required.";
            return false;
        }

        return true;
    }

    private static void WriteHelp(TextWriter writer)
    {
        writer.WriteLine("intent draft-from-interview");
        writer.WriteLine(UsageLine);
        writer.WriteLine("Compiles accepted interview answers into a draft intent at intents/<domain>/drafts/<session>.md (write requires --write).");
    }

    private static readonly JsonSerializerOptions JsonOptions = InterviewSessionStore.JsonOptions;
}

internal sealed record IntentDraftFromInterviewResult
{
    [JsonPropertyName("domain")]
    public required string Domain { get; init; }

    [JsonPropertyName("session")]
    public required string Session { get; init; }

    [JsonPropertyName("session_path")]
    public required string SessionPath { get; init; }

    [JsonPropertyName("draft_path")]
    public required string DraftPath { get; init; }

    [JsonPropertyName("mode")]
    public required string Mode { get; init; }

    [JsonPropertyName("accepted_count")]
    public required int AcceptedCount { get; init; }

    [JsonPropertyName("open_count")]
    public required int OpenCount { get; init; }

    [JsonPropertyName("draft_markdown")]
    public required string DraftMarkdown { get; init; }

    [JsonPropertyName("error")]
    public string? Error { get; init; }
}
