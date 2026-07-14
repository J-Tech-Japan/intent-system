using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using IntentSystem.Supervisor.Models;
using IntentSystem.Supervisor.Serialization;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G525: <c>intent-cli automation issue-retire --repo &lt;r&gt; --issue &lt;n&gt;
/// --reason &lt;superseded|decomposed|obsolete&gt; [--note &lt;text&gt;]
/// [--write]</c> — the canonical, atomic transition that supersedes a
/// published <c>intent-target</c> issue that can never be started as
/// authored (e.g. a research pass proves the slice must be decomposed).
///
/// <c>--write</c> performs, in order:
/// <list type="number">
/// <item>closes the GitHub issue as "not planned" with a comment carrying
///   the reason, the optional note, and a canonical-transition marker;</item>
/// <item>removes <c>intent-target</c> and any other workflow labels
///   present on the issue;</item>
/// <item>marks the corresponding queue-state item's lifecycle
///   <see cref="QueueItemState.Retired"/> (creating the entry if none
///   existed yet — a published-but-never-delegated issue commonly has no
///   queue-state entry at all) so <c>metadata validate</c> never reports a
///   missing entry for a legitimately retired unit;</item>
/// <item>appends a <c>packet-retired</c> event to <c>runs.jsonl</c>.</item>
/// </list>
///
/// Without <c>--write</c> it is a dry-run that lists the exact planned
/// mutations. Fails closed (no mutation) when the issue has an open linked
/// PR or an active claim (<c>intent-issue-in-progress</c>), naming the
/// release step to run first. Idempotent: re-running on an already-retired
/// execution unit (queue-state already shows <see cref="QueueItemState.Retired"/>)
/// is a safe no-op. Never deletes packet files.
/// </summary>
internal static class AutomationIssueRetireCommand
{
    private const string FormatJson = "json";
    private const string FormatMarkdown = "markdown";

    public const string ReasonSuperseded = "superseded";
    public const string ReasonDecomposed = "decomposed";
    public const string ReasonObsolete = "obsolete";

    private static readonly string[] AllowedReasons = { ReasonSuperseded, ReasonDecomposed, ReasonObsolete };

    /// <summary>Issue-side workflow labels this command may remove.</summary>
    private static readonly string[] KnownIssueWorkflowLabels =
    {
        WorkerNextActionConstants.Labels.IntentTarget,
        WorkerNextActionConstants.Labels.IntentIssueInProgress,
        WorkerNextActionConstants.Labels.IntentPrCreated,
    };

    public const string RetireRunEventName = "packet-retired";

    public static Func<IGitHubAutomationCandidateLister>? CandidateListerFactory { get; set; }

    public static Func<IGitHubLabelMutator>? LabelMutatorFactory { get; set; }

    public static Func<IGitHubIssueRetirementMutator>? RetirementMutatorFactory { get; set; }

    public static Func<DateTimeOffset>? UtcNowFactory { get; set; }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    private const string UsageLine =
        "Usage: intent-cli automation issue-retire --repo <owner/repo> --issue <n> --reason <superseded|decomposed|obsolete> [--note <text>] [--write] [--format json|markdown]";

    public static int Execute(CliContext context, string[] args, TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(writer);

        if (args.Length == 1 && string.Equals(args[0], "--help", StringComparison.Ordinal))
        {
            writer.WriteLine(UsageLine);
            return 0;
        }

        if (!TryParseArguments(args, out var repo, out var issue, out var reason, out var note, out var write, out var format, out var error))
        {
            writer.WriteLine(error);
            writer.WriteLine(UsageLine);
            return 1;
        }

        var queueStatePath = context.GetQueueStatePath();
        QueueState? queueState = null;
        if (File.Exists(queueStatePath))
        {
            try
            {
                queueState = QueueStateSerializer.Deserialize(File.ReadAllText(queueStatePath));
            }
            catch (Exception exception) when (exception is JsonException or InvalidOperationException)
            {
                writer.WriteLine($"queue-state.json at '{queueStatePath}' could not be parsed: {exception.Message}");
                return 1;
            }
        }

        var existingItem = queueState?.Items.FirstOrDefault(item => MatchesLinkedIssue(item.LinkedIssue, issue!.Value));

        // Idempotency: durable state is the source of truth. If this
        // execution unit is already retired, this is a safe no-op —
        // never re-attempt the GitHub mutation (the issue is presumably
        // already closed) and never error.
        if (existingItem is { State: QueueItemState.Retired })
        {
            var alreadyRetired = new AutomationIssueRetireResult
            {
                Repo = repo!,
                Issue = issue!.Value,
                Reason = reason!,
                Note = note,
                Mode = write ? "write" : "dry-run",
                Applied = false,
                AlreadyRetired = true,
                ExecutionUnit = existingItem.ExecutionUnit,
                PlannedMutations = Array.Empty<string>(),
                RefusalReason = null,
                Summary = $"'{existingItem.ExecutionUnit}' is already retired ({existingItem.RetirementReason}); no-op.",
            };
            EmitResult(writer, format, alreadyRetired);
            return 0;
        }

        IGitHubAutomationCandidateLister lister;
        IReadOnlyList<GitHubAutomationIssueCandidate> openIssues;
        IReadOnlyList<GitHubAutomationPrCandidate> openPrs;
        try
        {
            lister = CandidateListerFactory?.Invoke() ?? new GhCliGitHubAutomationCandidateLister();
            openIssues = lister.ListIssues(repo!, Array.Empty<string>());
            openPrs = lister.ListPullRequests(repo!, Array.Empty<string>());
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException)
        {
            writer.WriteLine($"failed to read GitHub state for {repo}: {exception.Message}");
            return 1;
        }

        var targetIssue = openIssues.FirstOrDefault(candidate => candidate.Number == issue!.Value);
        if (targetIssue is null)
        {
            writer.WriteLine(
                $"issue #{issue} not found among OPEN issues in {repo}; either it does not exist, or it is already "
                + "closed for a reason other than `automation issue-retire`. This command only retires OPEN "
                + "issues that still carry workflow labels.");
            return 1;
        }

        var openClosingPr = openPrs.FirstOrDefault(pr => IsOpen(pr.State) && HasClosingReference(pr, issue!.Value, repo!));
        if (openClosingPr is not null)
        {
            writer.WriteLine(
                $"refusing to retire issue #{issue}: OPEN PR #{openClosingPr.Number} in {repo} closes it — work is in "
                + $"flight. Merge, close, or release PR #{openClosingPr.Number} first (e.g. `intent-cli automation "
                + "pr-transition` / `closeout pr` / `gh pr close`), then retry.");
            return 1;
        }

        var currentLabels = targetIssue.Labels.Select(label => label.Name).ToHashSet(StringComparer.Ordinal);
        if (currentLabels.Contains(WorkerNextActionConstants.Labels.IntentIssueInProgress))
        {
            writer.WriteLine(
                $"refusing to retire issue #{issue}: it carries `{WorkerNextActionConstants.Labels.IntentIssueInProgress}` "
                + "— an active claim is in flight. Release the claim first (e.g. `intent-cli worker complete --kind "
                + "issue --number "
                + issue
                + " --outcome declined-contract-incomplete --write`), then retry.");
            return 1;
        }

        var executionUnit = existingItem?.ExecutionUnit ?? ExecutionUnitFromTitle(targetIssue.Title);
        var labelsToRemove = KnownIssueWorkflowLabels.Where(currentLabels.Contains).ToArray();
        var reasonComment = BuildReasonComment(reason!, note);

        var plannedMutations = new List<string>
        {
            $"gh issue close #{issue} --repo {repo} --reason \"not planned\" --comment <reason comment>",
        };
        if (labelsToRemove.Length > 0)
        {
            plannedMutations.Add($"remove label(s) from issue #{issue}: {string.Join(", ", labelsToRemove)}");
        }
        plannedMutations.Add(
            existingItem is null
                ? $"create queue-state entry for '{executionUnit}' with state=retired, reason={reason}"
                : $"update queue-state entry for '{executionUnit}': state=retired, reason={reason}");
        plannedMutations.Add($"append `{RetireRunEventName}` event to runs.jsonl for '{executionUnit}'");

        if (!write)
        {
            var dryRun = new AutomationIssueRetireResult
            {
                Repo = repo!,
                Issue = issue!.Value,
                Reason = reason!,
                Note = note,
                Mode = "dry-run",
                Applied = false,
                AlreadyRetired = false,
                ExecutionUnit = executionUnit,
                PlannedMutations = plannedMutations,
                RefusalReason = null,
                Summary = $"'{executionUnit}' (issue #{issue}) would be retired ({reason}). Re-run with --write to apply.",
            };
            EmitResult(writer, format, dryRun);
            return 0;
        }

        // --write: GitHub mutation first (close + label removal), then the
        // durable-state mutation (queue-state + runs.jsonl). If the GitHub
        // step fails, nothing durable changed — safe to retry as-is. If the
        // durable-state step fails AFTER the GitHub step succeeded, the
        // issue is already closed; retrying is still safe because the
        // command is idempotent once queue-state reflects `retired` (and
        // harmless to re-run before then, since the GitHub calls are
        // themselves idempotent — closing an already-closed issue / removing
        // an already-absent label is a no-op from gh's perspective).
        try
        {
            var retirementMutator = RetirementMutatorFactory?.Invoke() ?? new GhCliGitHubIssueRetirementMutator();
            retirementMutator.CloseAsNotPlanned(repo!, issue!.Value, reasonComment);

            if (labelsToRemove.Length > 0)
            {
                var labelMutator = LabelMutatorFactory?.Invoke() ?? new GhCliGitHubLabelMutator();
                labelMutator.ApplyLabelTransitions(
                    repo!, GhCliGitHubLabelMutator.Kinds.Issue, issue!.Value,
                    Array.Empty<string>(), labelsToRemove);
            }
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException)
        {
            writer.WriteLine(
                $"failed to close/relabel issue #{issue} in {repo}: {exception.Message}. No durable state was "
                + "changed — re-run with --write to retry.");
            return 1;
        }

        var now = (UtcNowFactory?.Invoke() ?? DateTimeOffset.UtcNow).ToUniversalTime();
        var retirementReason = string.IsNullOrWhiteSpace(note) ? reason! : $"{reason}: {note}";
        try
        {
            var updatedItems = (queueState?.Items ?? Array.Empty<QueueItem>()).ToList();
            var indexToUpdate = existingItem is null
                ? -1
                : updatedItems.FindIndex(item => string.Equals(item.ExecutionUnit, existingItem.ExecutionUnit, StringComparison.Ordinal));

            if (indexToUpdate >= 0)
            {
                updatedItems[indexToUpdate] = updatedItems[indexToUpdate] with
                {
                    State = QueueItemState.Retired,
                    RetirementReason = retirementReason,
                };
            }
            else
            {
                updatedItems.Add(new QueueItem
                {
                    ExecutionUnit = executionUnit,
                    Title = targetIssue.Title,
                    State = QueueItemState.Retired,
                    Dependencies = Array.Empty<string>(),
                    BlockedBy = Array.Empty<string>(),
                    ClarificationReturnPath = string.Empty,
                    PacketPaths = new PacketPaths
                    {
                        Yaml = $".intent-cli/issues/{executionUnit}/packet.yaml",
                        Implementation = $".intent-cli/issues/{executionUnit}/implementation.md",
                        ReviewContext = $".intent-cli/issues/{executionUnit}/review-context.md",
                    },
                    LinkedIssue = new LinkedIssue { Repo = repo!, Number = issue, Url = targetIssue.Url },
                    LinkedPr = null,
                    WorkerRole = CliRuntimeContracts.DefaultImplementRole,
                    ReviewRole = CliRuntimeContracts.DefaultReviewRole,
                    Priority = "high",
                    RetirementReason = retirementReason,
                });
            }

            var updatedState = new QueueState
            {
                SchemaVersion = queueState?.SchemaVersion ?? "1",
                UpdatedAt = now,
                Items = updatedItems,
            };
            File.WriteAllText(queueStatePath, QueueStateSerializer.Serialize(updatedState));

            AppendRetireRunEvent(context, executionUnit, repo!, issue!.Value, retirementReason, now);
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or JsonException)
        {
            writer.WriteLine(
                $"issue #{issue} in {repo} was closed and relabeled, but the queue-state/runs.jsonl update failed: "
                + $"{exception.Message}. Re-run `automation issue-retire` with --write (idempotent) to retry the "
                + "durable-state update.");
            return 1;
        }

        var applied = new AutomationIssueRetireResult
        {
            Repo = repo!,
            Issue = issue!.Value,
            Reason = reason!,
            Note = note,
            Mode = "write",
            Applied = true,
            AlreadyRetired = false,
            ExecutionUnit = executionUnit,
            PlannedMutations = plannedMutations,
            RefusalReason = null,
            Summary = $"'{executionUnit}' (issue #{issue}) retired ({reason}).",
        };
        EmitResult(writer, format, applied);
        return 0;
    }

    private static void AppendRetireRunEvent(
        CliContext context, string executionUnit, string repo, int issue, string reason, DateTimeOffset ts)
    {
        var runsPath = context.GetRunLogPath();
        Directory.CreateDirectory(Path.GetDirectoryName(runsPath)!);
        var runEvent = new RunEvent
        {
            Ts = ts,
            ExecutionUnit = executionUnit,
            Event = RetireRunEventName,
            By = "intent-cli automation issue-retire (G525)",
            Reason = reason,
        };
        File.AppendAllText(runsPath, RunLogSerializer.SerializeLine(runEvent) + "\n");
    }

    private static bool MatchesLinkedIssue(LinkedIssue? linkedIssue, int issueNumber) =>
        linkedIssue is { Number: { } number } && number == issueNumber;

    private static bool IsOpen(string state) =>
        string.IsNullOrEmpty(state) || string.Equals(state, "OPEN", StringComparison.OrdinalIgnoreCase);

    private static bool HasClosingReference(GitHubAutomationPrCandidate pr, int issueNumber, string repo)
    {
        foreach (var reference in pr.ClosingIssuesReferences)
        {
            if (reference.Number != issueNumber)
            {
                continue;
            }
            if (reference.Repository is not { Name.Length: > 0, Owner.Login.Length: > 0 } repository)
            {
                return true;
            }
            var candidateRepo = $"{repository.Owner!.Login}/{repository.Name}";
            if (string.Equals(candidateRepo, repo, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }

    private static string ExecutionUnitFromTitle(string title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return string.Empty;
        }
        var colonIndex = title.IndexOf(':');
        return (colonIndex > 0 ? title[..colonIndex] : title).Trim();
    }

    private static string BuildReasonComment(string reason, string? note)
    {
        var comment = $"Retired via canonical `automation issue-retire` transition — reason: {reason}.";
        if (!string.IsNullOrWhiteSpace(note))
        {
            comment += $" Note: {note}";
        }
        return comment;
    }

    private static bool TryParseArguments(
        string[] args,
        out string? repo,
        out int? issue,
        out string? reason,
        out string? note,
        out bool write,
        out string format,
        out string error)
    {
        repo = null;
        issue = null;
        reason = null;
        note = null;
        write = false;
        format = FormatMarkdown;
        error = string.Empty;

        for (var index = 0; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--repo":
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                    {
                        error = "--repo requires a value (owner/repo).";
                        return false;
                    }
                    repo = args[++index].Trim();
                    break;
                case "--issue":
                    if (index + 1 >= args.Length
                        || !int.TryParse(args[index + 1].TrimStart('#'), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedIssue)
                        || parsedIssue <= 0)
                    {
                        error = "--issue requires a positive integer issue number.";
                        return false;
                    }
                    issue = parsedIssue;
                    index++;
                    break;
                case "--reason":
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                    {
                        error = "--reason requires a value (superseded, decomposed, or obsolete).";
                        return false;
                    }
                    var requestedReason = args[++index].Trim();
                    if (!AllowedReasons.Contains(requestedReason, StringComparer.Ordinal))
                    {
                        error = $"--reason must be one of: {string.Join(", ", AllowedReasons)} (got '{requestedReason}').";
                        return false;
                    }
                    reason = requestedReason;
                    break;
                case "--note":
                    if (index + 1 >= args.Length)
                    {
                        error = "--note requires a value.";
                        return false;
                    }
                    note = args[++index];
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
                    var requestedFormat = args[++index].Trim();
                    if (!string.Equals(requestedFormat, FormatMarkdown, StringComparison.Ordinal)
                        && !string.Equals(requestedFormat, FormatJson, StringComparison.Ordinal))
                    {
                        error = $"--format must be 'markdown' or 'json' (got '{requestedFormat}').";
                        return false;
                    }
                    format = requestedFormat;
                    break;
                default:
                    error = $"Unknown argument '{args[index]}'.";
                    return false;
            }
        }

        if (string.IsNullOrWhiteSpace(repo))
        {
            error = "automation issue-retire requires '--repo <owner/repo>'.";
            return false;
        }
        if (issue is null)
        {
            error = "automation issue-retire requires '--issue <n>'.";
            return false;
        }
        if (string.IsNullOrWhiteSpace(reason))
        {
            error = "automation issue-retire requires '--reason <superseded|decomposed|obsolete>'.";
            return false;
        }
        return true;
    }

    private static void EmitResult(TextWriter writer, string format, AutomationIssueRetireResult result)
    {
        if (string.Equals(format, FormatJson, StringComparison.Ordinal))
        {
            writer.Write(JsonSerializer.Serialize(result, JsonOptions));
            writer.WriteLine();
        }
        else
        {
            writer.WriteLine($"# automation issue-retire — `{result.Repo}` #{result.Issue} ({result.Mode})");
            writer.WriteLine();
            writer.WriteLine($"- execution_unit: `{result.ExecutionUnit}`");
            writer.WriteLine($"- reason: {result.Reason}");
            if (!string.IsNullOrWhiteSpace(result.Note))
            {
                writer.WriteLine($"- note: {result.Note}");
            }
            writer.WriteLine($"- applied: {(result.Applied ? "true" : "false")}");
            writer.WriteLine($"- already_retired: {(result.AlreadyRetired ? "true" : "false")}");
            writer.WriteLine();
            writer.WriteLine(result.Summary);
            if (result.PlannedMutations.Count > 0)
            {
                writer.WriteLine();
                writer.WriteLine(result.Applied ? "## Applied mutations" : "## Planned mutations");
                foreach (var mutation in result.PlannedMutations)
                {
                    writer.WriteLine($"- {mutation}");
                }
            }
        }
    }
}

/// <summary>
/// G525: testability seam for closing a GitHub issue as "not planned" with
/// a comment — the production implementation shells out to
/// <c>gh issue close --reason "not planned" --comment &lt;text&gt;</c>.
/// </summary>
internal interface IGitHubIssueRetirementMutator
{
    void CloseAsNotPlanned(string repo, int issueNumber, string comment);
}

/// <summary>
/// G525: default retirement mutator backed by <c>gh issue close</c>.
/// </summary>
internal sealed class GhCliGitHubIssueRetirementMutator : IGitHubIssueRetirementMutator
{
    public void CloseAsNotPlanned(string repo, int issueNumber, string comment)
    {
        var args = new List<string>
        {
            "issue", "close",
            issueNumber.ToString(CultureInfo.InvariantCulture),
            "--repo", repo,
            "--reason", "not planned",
            "--comment", comment,
        };
        RunGh(args, $"close issue #{issueNumber} in {repo} as not planned");
    }

    private static void RunGh(IReadOnlyList<string> arguments, string description)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "gh",
            StandardOutputEncoding = GitHubCliProcessEncoding.Utf8NoBom,
            StandardErrorEncoding = GitHubCliProcessEncoding.Utf8NoBom,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        string stdout;
        string stderr;
        int exitCode;
        try
        {
            using var process = Process.Start(startInfo)
                ?? throw new InvalidOperationException($"failed to start `gh` process to {description}");
            stdout = process.StandardOutput.ReadToEnd();
            stderr = process.StandardError.ReadToEnd();
            process.WaitForExit();
            exitCode = process.ExitCode;
        }
        catch (Exception exception) when (
            exception is System.ComponentModel.Win32Exception
            or InvalidOperationException
            or IOException)
        {
            throw new InvalidOperationException($"could not invoke `gh` to {description}: {exception.Message}", exception);
        }

        if (exitCode != 0)
        {
            var classification = GitHubCliJsonBoundary.ClassifyProcessFailure(stderr, stdout);
            var errorBody = string.IsNullOrWhiteSpace(stderr) ? stdout : stderr;
            throw new InvalidOperationException(
                $"[{classification}] `gh` failed to {description} with exit {exitCode}: "
                + GitHubCliJsonBoundary.SanitizePreview(errorBody));
        }
    }
}

internal sealed record AutomationIssueRetireResult
{
    [JsonPropertyName("repo")]
    public required string Repo { get; init; }

    [JsonPropertyName("issue")]
    public required int Issue { get; init; }

    [JsonPropertyName("reason")]
    public required string Reason { get; init; }

    [JsonPropertyName("note")]
    public string? Note { get; init; }

    [JsonPropertyName("mode")]
    public required string Mode { get; init; }

    [JsonPropertyName("applied")]
    public required bool Applied { get; init; }

    [JsonPropertyName("already_retired")]
    public required bool AlreadyRetired { get; init; }

    [JsonPropertyName("execution_unit")]
    public required string ExecutionUnit { get; init; }

    [JsonPropertyName("planned_mutations")]
    public required IReadOnlyList<string> PlannedMutations { get; init; }

    [JsonPropertyName("refusal_reason")]
    public string? RefusalReason { get; init; }

    [JsonPropertyName("summary")]
    public required string Summary { get; init; }
}
