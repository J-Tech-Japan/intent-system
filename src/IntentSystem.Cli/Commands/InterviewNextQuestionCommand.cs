using System.Text.Json;
using System.Text.Json.Serialization;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G250: Read-only <c>intent-cli interview next-question</c>. Returns the
/// first pending question (one whose answer is empty) for a given
/// <c>--session</c> in a per-domain interview store. Never launches an AI
/// provider, never mutates state.
/// </summary>
internal static class InterviewNextQuestionCommand
{
    private const string FormatJson = "json";
    private const string FormatMarkdown = "markdown";

    private const string UsageLine =
        "Usage: intent-cli interview next-question --session <id> [--domain <name>] [--format markdown|json]";

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

        if (!TryParseArguments(args, out var session, out var domainOverride, out var format, out var error))
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

        InterviewQuestion? pending = null;
        var totalQuestions = 0;
        var pendingCount = 0;
        var sessionExists = stored is not null;

        if (stored is not null)
        {
            totalQuestions = stored.Questions.Count;
            pending = stored.Questions.FirstOrDefault(q => string.IsNullOrEmpty(q.Answer));
            pendingCount = stored.Questions.Count(q => string.IsNullOrEmpty(q.Answer));
        }

        var result = new InterviewNextQuestionResult
        {
            Domain = domain,
            Session = session!,
            SessionPath = sessionPath,
            SessionExists = sessionExists,
            TotalQuestions = totalQuestions,
            PendingCount = pendingCount,
            HasPending = pending is not null,
            Pending = pending is null
                ? null
                : new InterviewNextQuestionPending
                {
                    Id = pending.Id,
                    Prompt = pending.Prompt
                }
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

    private static void WriteMarkdown(TextWriter writer, InterviewNextQuestionResult result)
    {
        writer.WriteLine($"# Interview next question — {result.Domain} / {result.Session}");
        writer.WriteLine();
        writer.WriteLine($"- session path: {result.SessionPath}");
        writer.WriteLine($"- session exists: {(result.SessionExists ? "yes" : "no")}");
        writer.WriteLine($"- total questions: {result.TotalQuestions}");
        writer.WriteLine($"- pending count: {result.PendingCount}");
        writer.WriteLine();

        if (result.Pending is null)
        {
            writer.WriteLine("No pending question.");
            return;
        }

        writer.WriteLine("## Next pending");
        writer.WriteLine($"- id: {result.Pending.Id}");
        writer.WriteLine($"- prompt:");
        writer.WriteLine();
        writer.WriteLine(result.Pending.Prompt);
    }

    private static bool TryParseArguments(
        string[] args,
        out string? session,
        out string? domainOverride,
        out string format,
        out string error)
    {
        session = null;
        domainOverride = null;
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
        writer.WriteLine("interview next-question");
        writer.WriteLine(UsageLine);
        writer.WriteLine("Read-only first-pending-question lookup for the per-domain interview session.");
    }

    private static readonly JsonSerializerOptions JsonOptions = InterviewSessionStore.JsonOptions;
}

internal sealed record InterviewNextQuestionResult
{
    [JsonPropertyName("domain")]
    public required string Domain { get; init; }

    [JsonPropertyName("session")]
    public required string Session { get; init; }

    [JsonPropertyName("session_path")]
    public required string SessionPath { get; init; }

    [JsonPropertyName("session_exists")]
    public required bool SessionExists { get; init; }

    [JsonPropertyName("total_questions")]
    public required int TotalQuestions { get; init; }

    [JsonPropertyName("pending_count")]
    public required int PendingCount { get; init; }

    [JsonPropertyName("has_pending")]
    public required bool HasPending { get; init; }

    [JsonPropertyName("pending")]
    public InterviewNextQuestionPending? Pending { get; init; }
}

internal sealed record InterviewNextQuestionPending
{
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonPropertyName("prompt")]
    public required string Prompt { get; init; }
}
