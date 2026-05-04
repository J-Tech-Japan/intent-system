using System.Text.Json;
using System.Text.Json.Serialization;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G251: Read-only <c>intent-cli interview compile</c>. Summarizes the
/// per-domain interview session into accepted baseline (answered Qs),
/// open questions (pending Qs), and a placeholder for candidate
/// execution units. Read-only. Never launches an AI provider.
/// </summary>
internal static class InterviewCompileCommand
{
    private const string FormatJson = "json";
    private const string FormatMarkdown = "markdown";

    private const string UsageLine =
        "Usage: intent-cli interview compile --session <id> [--domain <name>] [--format markdown|json]";

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

        var accepted = new List<InterviewCompileAccepted>();
        var open = new List<InterviewCompileOpen>();
        if (stored is not null)
        {
            foreach (var question in stored.Questions)
            {
                if (string.IsNullOrEmpty(question.Answer))
                {
                    open.Add(new InterviewCompileOpen { Id = question.Id, Prompt = question.Prompt });
                }
                else
                {
                    accepted.Add(new InterviewCompileAccepted
                    {
                        Id = question.Id,
                        Prompt = question.Prompt,
                        Answer = question.Answer!
                    });
                }
            }
        }

        var result = new InterviewCompileResult
        {
            Domain = domain,
            Session = session!,
            SessionPath = sessionPath,
            SessionExists = stored is not null,
            TotalQuestions = stored?.Questions.Count ?? 0,
            AcceptedCount = accepted.Count,
            OpenCount = open.Count,
            AcceptedBaseline = accepted,
            OpenQuestions = open,
            CandidateExecutionUnits = Array.Empty<string>(),
            Ready = stored is not null && accepted.Count > 0
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

    private static void WriteMarkdown(TextWriter writer, InterviewCompileResult result)
    {
        writer.WriteLine($"# Interview compile — {result.Domain} / {result.Session}");
        writer.WriteLine();
        writer.WriteLine($"- session path: {result.SessionPath}");
        writer.WriteLine($"- session exists: {(result.SessionExists ? "yes" : "no")}");
        writer.WriteLine($"- total questions: {result.TotalQuestions}");
        writer.WriteLine($"- accepted count: {result.AcceptedCount}");
        writer.WriteLine($"- open count: {result.OpenCount}");
        writer.WriteLine($"- ready for draft: {(result.Ready ? "yes" : "no")}");
        writer.WriteLine();

        writer.WriteLine("## Accepted baseline");
        if (result.AcceptedBaseline.Count == 0)
        {
            writer.WriteLine("- none");
        }
        else
        {
            foreach (var accepted in result.AcceptedBaseline)
            {
                writer.WriteLine($"- **{accepted.Id}** — {accepted.Prompt}");
                writer.WriteLine($"  - answer: {accepted.Answer}");
            }
        }
        writer.WriteLine();

        writer.WriteLine("## Open questions");
        if (result.OpenQuestions.Count == 0)
        {
            writer.WriteLine("- none");
        }
        else
        {
            foreach (var open in result.OpenQuestions)
            {
                writer.WriteLine($"- **{open.Id}** — {open.Prompt}");
            }
        }
        writer.WriteLine();

        writer.WriteLine("## Candidate execution units");
        if (result.CandidateExecutionUnits.Count == 0)
        {
            writer.WriteLine("- none yet — promote with `intent draft-from-interview`");
        }
        else
        {
            foreach (var unit in result.CandidateExecutionUnits)
            {
                writer.WriteLine($"- {unit}");
            }
        }
    }

    private static bool TryParseArguments(string[] args, out string? session, out string? domainOverride, out string format, out string error)
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
        writer.WriteLine("interview compile");
        writer.WriteLine(UsageLine);
        writer.WriteLine("Read-only summary of accepted answers, open questions, and candidate execution units for a session.");
    }

    private static readonly JsonSerializerOptions JsonOptions = InterviewSessionStore.JsonOptions;
}

internal sealed record InterviewCompileResult
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

    [JsonPropertyName("accepted_count")]
    public required int AcceptedCount { get; init; }

    [JsonPropertyName("open_count")]
    public required int OpenCount { get; init; }

    [JsonPropertyName("accepted_baseline")]
    public required IReadOnlyList<InterviewCompileAccepted> AcceptedBaseline { get; init; }

    [JsonPropertyName("open_questions")]
    public required IReadOnlyList<InterviewCompileOpen> OpenQuestions { get; init; }

    [JsonPropertyName("candidate_execution_units")]
    public required IReadOnlyList<string> CandidateExecutionUnits { get; init; }

    [JsonPropertyName("ready")]
    public required bool Ready { get; init; }
}

internal sealed record InterviewCompileAccepted
{
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonPropertyName("prompt")]
    public required string Prompt { get; init; }

    [JsonPropertyName("answer")]
    public required string Answer { get; init; }
}

internal sealed record InterviewCompileOpen
{
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonPropertyName("prompt")]
    public required string Prompt { get; init; }
}
