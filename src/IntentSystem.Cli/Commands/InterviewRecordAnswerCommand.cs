using System.Text.Json;
using System.Text.Json.Serialization;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G250: <c>intent-cli interview record-answer</c>. Writes the operator's
/// answer for a question to the per-domain interview session store.
/// Supports a dry-run preview without <c>--write</c>; with <c>--write</c>
/// the answer is persisted. If the question id is new and <c>--prompt</c>
/// is provided, a new question entry is appended; otherwise an unknown
/// id is rejected. Never launches an AI provider.
/// </summary>
internal static class InterviewRecordAnswerCommand
{
    private const string FormatJson = "json";
    private const string FormatMarkdown = "markdown";

    private const string ModeWrite = "write";
    private const string ModeDryRun = "dry-run";

    private const string UsageLine =
        "Usage: intent-cli interview record-answer --session <id> --question <q> --from-file <path> "
        + "[--domain <name>] [--prompt <text>] [--write] [--format markdown|json]";

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

        if (!TryParseArguments(args, out var session, out var question, out var fromFile, out var domainOverride, out var prompt, out var write, out var format, out var error))
        {
            writer.WriteLine(error);
            writer.WriteLine(UsageLine);
            return 1;
        }

        if (!File.Exists(fromFile))
        {
            EmitError(writer, format, NewError(domainOverride ?? context.Config.Project.Domain, session!, question!, write,
                $"answer file not found: {fromFile}"));
            return 1;
        }

        var domain = string.IsNullOrWhiteSpace(domainOverride)
            ? context.Config.Project.Domain
            : domainOverride!;

        var sessionPath = InterviewSessionStore.ResolvePath(context.RepoRoot, domain, session!);
        var stored = InterviewSessionStore.Read(sessionPath) ?? new InterviewSession
        {
            Session = session!,
            Domain = domain,
            Questions = new List<InterviewQuestion>()
        };

        var existing = stored.Questions.FirstOrDefault(q => string.Equals(q.Id, question, StringComparison.Ordinal));
        var newlyAdded = false;
        var answerText = File.ReadAllText(fromFile);

        if (existing is null)
        {
            if (string.IsNullOrWhiteSpace(prompt))
            {
                EmitError(writer, format, NewError(domain, session!, question!, write,
                    $"no question with id '{question}' in session '{session}'; pass --prompt <text> to add a new question."));
                return 1;
            }

            var newQuestion = new InterviewQuestion
            {
                Id = question!,
                Prompt = prompt!,
                Answer = answerText
            };
            stored.Questions.Add(newQuestion);
            existing = newQuestion;
            newlyAdded = true;
        }
        else
        {
            existing.Answer = answerText;
        }

        if (write)
        {
            InterviewSessionStore.Write(sessionPath, stored);
        }

        var result = new InterviewRecordAnswerResult
        {
            Domain = domain,
            Session = session!,
            QuestionId = question!,
            SessionPath = sessionPath,
            Mode = write ? ModeWrite : ModeDryRun,
            NewlyAdded = newlyAdded,
            AnswerLength = answerText.Length,
            TotalQuestions = stored.Questions.Count,
            PendingCount = stored.Questions.Count(q => string.IsNullOrEmpty(q.Answer)),
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

    private static void EmitError(TextWriter writer, string format, InterviewRecordAnswerResult error)
    {
        if (string.Equals(format, FormatJson, StringComparison.Ordinal))
        {
            writer.Write(JsonSerializer.Serialize(error, JsonOptions));
            writer.WriteLine();
        }
        else
        {
            writer.WriteLine($"# Interview record-answer — {error.Domain} / {error.Session}");
            writer.WriteLine();
            writer.WriteLine($"## Error");
            writer.WriteLine($"- {error.Error}");
        }
    }

    private static InterviewRecordAnswerResult NewError(string domain, string session, string question, bool write, string message)
    {
        return new InterviewRecordAnswerResult
        {
            Domain = domain,
            Session = session,
            QuestionId = question,
            SessionPath = "(unresolved)",
            Mode = write ? ModeWrite : ModeDryRun,
            NewlyAdded = false,
            AnswerLength = 0,
            TotalQuestions = 0,
            PendingCount = 0,
            Error = message
        };
    }

    private static void WriteMarkdown(TextWriter writer, InterviewRecordAnswerResult result)
    {
        writer.WriteLine($"# Interview record-answer — {result.Domain} / {result.Session}");
        writer.WriteLine();
        writer.WriteLine($"- session path: {result.SessionPath}");
        writer.WriteLine($"- mode: {result.Mode}");
        writer.WriteLine($"- question id: {result.QuestionId}");
        writer.WriteLine($"- newly added: {(result.NewlyAdded ? "yes" : "no")}");
        writer.WriteLine($"- answer length: {result.AnswerLength}");
        writer.WriteLine($"- total questions: {result.TotalQuestions}");
        writer.WriteLine($"- pending count: {result.PendingCount}");
        if (!string.IsNullOrWhiteSpace(result.Error))
        {
            writer.WriteLine($"- error: {result.Error}");
        }
    }

    private static bool TryParseArguments(
        string[] args,
        out string? session,
        out string? question,
        out string? fromFile,
        out string? domainOverride,
        out string? prompt,
        out bool write,
        out string format,
        out string error)
    {
        session = null;
        question = null;
        fromFile = null;
        domainOverride = null;
        prompt = null;
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

                case "--question":
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                    {
                        error = "--question requires a value.";
                        return false;
                    }

                    question = args[index + 1];
                    index++;
                    break;

                case "--from-file":
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                    {
                        error = "--from-file requires a path.";
                        return false;
                    }

                    fromFile = args[index + 1];
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

                case "--prompt":
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                    {
                        error = "--prompt requires a value.";
                        return false;
                    }

                    prompt = args[index + 1];
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

        if (string.IsNullOrWhiteSpace(question))
        {
            error = "--question is required.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(fromFile))
        {
            error = "--from-file is required.";
            return false;
        }

        return true;
    }

    private static void WriteHelp(TextWriter writer)
    {
        writer.WriteLine("interview record-answer");
        writer.WriteLine(UsageLine);
        writer.WriteLine("Records an operator answer for a question id. Without --write the call is a dry-run.");
    }

    private static readonly JsonSerializerOptions JsonOptions = InterviewSessionStore.JsonOptions;
}

internal sealed record InterviewRecordAnswerResult
{
    [JsonPropertyName("domain")]
    public required string Domain { get; init; }

    [JsonPropertyName("session")]
    public required string Session { get; init; }

    [JsonPropertyName("question_id")]
    public required string QuestionId { get; init; }

    [JsonPropertyName("session_path")]
    public required string SessionPath { get; init; }

    [JsonPropertyName("mode")]
    public required string Mode { get; init; }

    [JsonPropertyName("newly_added")]
    public required bool NewlyAdded { get; init; }

    [JsonPropertyName("answer_length")]
    public required int AnswerLength { get; init; }

    [JsonPropertyName("total_questions")]
    public required int TotalQuestions { get; init; }

    [JsonPropertyName("pending_count")]
    public required int PendingCount { get; init; }

    [JsonPropertyName("error")]
    public string? Error { get; init; }
}
