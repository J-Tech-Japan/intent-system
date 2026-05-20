using System.Text.Json;
using System.Text.Json.Serialization;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G374: read-only <c>intent-cli guide worker signal</c>. Emits
/// paste-ready comment templates and the exact <c>intent-cli</c> commands
/// for the structured worker-signal protocol so a child implementation
/// agent can hand a blocker / follow-up / scope-warning back to host
/// review/design automation without reading host metadata or guessing
/// the label transitions. Never mutates state; never launches a provider.
/// </summary>
internal static class GuideWorkerSignalCommand
{
    private const string FormatJson = "json";
    private const string FormatMarkdown = "markdown";

    private const string UsageLine =
        "Usage: intent-cli guide worker signal [--repo <owner/repo>] [--format markdown|json]";

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

        if (!TryParseArguments(args, out var repo, out var format, out var error))
        {
            writer.WriteLine(error);
            writer.WriteLine(UsageLine);
            return 1;
        }

        var result = Build(repo);
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

    private static GuideWorkerSignalResult Build(string? repo)
    {
        var repoArg = string.IsNullOrWhiteSpace(repo) ? "<OWNER>/<REPO>" : repo;

        var templates = new[]
        {
            new GuideWorkerSignalTemplate
            {
                Kind = WorkerSignalContract.Kinds.Blocker,
                Target = WorkerSignalContract.Targets.Issue,
                When = "The assigned issue cannot be safely implemented and should be declined before any code is written (missing contract, unbuildable premise, conflicting parent intent).",
                Command = $"intent-cli worker signal blocker --repo {repoArg} --issue <n> --from-file signal.md --write --github-only --format json",
                Body = "Summary: <one line — why this issue cannot be implemented as written>.\n\nEvidence:\n- <file/path or command output that demonstrates the blocker>\n\nWhat host design should decide:\n- <the clarification, packet change, or parent-intent reconciliation needed before this is implementable>",
            },
            new GuideWorkerSignalTemplate
            {
                Kind = WorkerSignalContract.Kinds.FollowUp,
                Target = WorkerSignalContract.Targets.Pr,
                When = "Implementation can proceed and the PR is open, but you found a follow-up defect or design gap that is out of scope for this PR.",
                Command = $"intent-cli worker signal follow-up --repo {repoArg} --pr <n> --from-file signal.md --write --github-only --format json",
                Body = "Summary: <one line — the follow-up defect/gap, distinct from this PR's scope>.\n\nEvidence:\n- <where it surfaces; why it is out of scope for the current PR>\n\nSuggested follow-up:\n- <a candidate next slice / issue the host could cut>",
            },
            new GuideWorkerSignalTemplate
            {
                Kind = WorkerSignalContract.Kinds.ScopeWarning,
                Target = "issue|pr",
                When = "The finding belongs to host intent/packet metadata (paths under intents/** or .intent-cli/**) or would widen scope beyond the assigned slice. Child workers must not edit those paths.",
                Command = $"intent-cli worker signal scope-warning --repo {repoArg} (--issue <n> | --pr <n>) --from-file signal.md --write --github-only --format json",
                Body = "Summary: <one line — the host-owned metadata or scope concern>.\n\nWhy it is host-owned / out of scope:\n- <the path or contract that a child worker must not touch>\n\nRequested host action:\n- <packet edit, rule update, or metadata repair for the host loop to perform>",
            },
        };

        var prompt =
$@"Raise a structured worker signal for {repoArg} when you find something outside the assigned slice. A signal is a GitHub issue/PR comment carrying a hidden marker plus the `intent-signal-sent` label; host review/design automation collects it with `intent-cli review collect-signals` and clears it with `intent-cli review signal-handled`.

Pick the kind:
- blocker (issue): decline-before-implementation — the issue cannot be safely implemented as written.
- follow-up (PR): a follow-up defect / design gap found while the PR is open, out of scope for this PR.
- scope-warning (issue or PR): the finding is host-owned metadata (intents/** or .intent-cli/**) or widens scope.

Send a signal:
1. Write the signal body to a file (e.g. signal.md) using the matching template below.
2. Run the `intent-cli worker signal <kind>` command for the target. It posts the marker-wrapped comment and adds `intent-signal-sent` in one step. Default is dry-run; add --write to actually post.
3. Do NOT hand-edit labels with `gh ... edit --add-label`; the worker signal command owns the transition.

Hard rules:
- Do not read or edit host metadata (intents/**, .intent-cli/**); a scope-warning signal is how you hand those findings back.
- Do not raise a signal for ordinary PR review comments — those go through the normal pr-comment-fix flow.
- One signal per finding; if you already sent one and nothing changed, do not duplicate it.";

        return new GuideWorkerSignalResult
        {
            Kind = "worker-signal",
            Repo = string.IsNullOrWhiteSpace(repo) ? null : repo,
            Prompt = prompt,
            Labels = new GuideWorkerSignalLabels
            {
                Sent = WorkerSignalContract.Labels.SignalSent,
                Handled = WorkerSignalContract.Labels.SignalHandled,
            },
            MarkerExample = $"{WorkerSignalContract.MarkerPrefix} v={WorkerSignalContract.MarkerVersion} kind=blocker target=issue#<n> -->",
            Templates = templates,
            HostCommands = new[]
            {
                $"intent-cli review collect-signals --repo {repoArg} --format json",
                $"intent-cli review signal-handled --repo {repoArg} (--issue <n> | --pr <n>) --write --format json",
            },
        };
    }

    private static void WriteMarkdown(TextWriter writer, GuideWorkerSignalResult result)
    {
        writer.WriteLine("# Guide worker — structured signal protocol");
        writer.WriteLine();
        if (!string.IsNullOrWhiteSpace(result.Repo))
        {
            writer.WriteLine($"- repo: {result.Repo}");
        }
        writer.WriteLine($"- pending label: {result.Labels.Sent}");
        writer.WriteLine($"- handled label: {result.Labels.Handled}");
        writer.WriteLine($"- marker shape: `{result.MarkerExample}`");
        writer.WriteLine();

        writer.WriteLine("## Templates");
        foreach (var template in result.Templates)
        {
            writer.WriteLine();
            writer.WriteLine($"### {template.Kind} ({template.Target})");
            writer.WriteLine();
            writer.WriteLine($"When: {template.When}");
            writer.WriteLine();
            writer.WriteLine($"Command: `{template.Command}`");
            writer.WriteLine();
            writer.WriteLine("Body (write to --from-file):");
            writer.WriteLine();
            writer.WriteLine("```text");
            writer.WriteLine(template.Body);
            writer.WriteLine("```");
        }
        writer.WriteLine();

        writer.WriteLine("## Host collection commands");
        foreach (var command in result.HostCommands)
        {
            writer.WriteLine($"- `{command}`");
        }
        writer.WriteLine();

        writer.WriteLine("## Prompt");
        writer.WriteLine();
        writer.WriteLine("```text");
        writer.WriteLine(result.Prompt);
        writer.WriteLine("```");
    }

    private static bool TryParseArguments(string[] args, out string? repo, out string format, out string error)
    {
        repo = null;
        format = FormatMarkdown;
        error = string.Empty;

        for (var index = 0; index < args.Length; index++)
        {
            var argument = args[index];
            switch (argument)
            {
                case "--repo":
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                    {
                        error = "--repo requires a value.";
                        return false;
                    }
                    repo = args[index + 1];
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
        writer.WriteLine("guide worker signal");
        writer.WriteLine(UsageLine);
        writer.WriteLine("Read-only paste-ready structured worker-signal templates (blocker / follow-up / scope-warning).");
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };
}

internal sealed record GuideWorkerSignalResult
{
    [JsonPropertyName("kind")]
    public required string Kind { get; init; }

    [JsonPropertyName("repo")]
    public string? Repo { get; init; }

    [JsonPropertyName("prompt")]
    public required string Prompt { get; init; }

    [JsonPropertyName("labels")]
    public required GuideWorkerSignalLabels Labels { get; init; }

    [JsonPropertyName("marker_example")]
    public required string MarkerExample { get; init; }

    [JsonPropertyName("templates")]
    public required IReadOnlyList<GuideWorkerSignalTemplate> Templates { get; init; }

    [JsonPropertyName("host_commands")]
    public required IReadOnlyList<string> HostCommands { get; init; }
}

internal sealed record GuideWorkerSignalLabels
{
    [JsonPropertyName("sent")]
    public required string Sent { get; init; }

    [JsonPropertyName("handled")]
    public required string Handled { get; init; }
}

internal sealed record GuideWorkerSignalTemplate
{
    [JsonPropertyName("kind")]
    public required string Kind { get; init; }

    [JsonPropertyName("target")]
    public required string Target { get; init; }

    [JsonPropertyName("when")]
    public required string When { get; init; }

    [JsonPropertyName("command")]
    public required string Command { get; init; }

    [JsonPropertyName("body")]
    public required string Body { get; init; }
}
