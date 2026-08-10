using IntentSystem.Supervisor;
using System.Text.Json;
using System.Text.Json.Serialization;
using IntentSystem.Supervisor.Models;
using IntentSystem.Supervisor.Serialization;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G246: <c>intent-cli closeout pr</c> command. Records and applies parent
/// state updates for an already-accepted child PR without depending on
/// environment-specific closeout skills. Resolves the queue item via the
/// PR's <c>linked_pr</c> reference, plans (or applies) the
/// queue-completion transition, appends <c>runs.jsonl</c> events, and
/// emits a continuation classification hint plus the submodule sync
/// step the operator must run separately. Never merges a PR. Never
/// launches an AI provider.
/// </summary>
internal static class CloseoutPrCommand
{
    private const string FormatJson = "json";
    private const string FormatMarkdown = "markdown";

    private const string ModeWrite = "write";
    private const string ModeDryRun = "dry-run";

    private const string ContinuationNextSliceReady = "next-slice-ready";
    private const string ContinuationNoActionableItem = "no-actionable-item";
    private const string ContinuationClarificationRequired = "clarification-required";

    private const string UsageLine =
        "Usage: intent-cli closeout pr --pr <n> --repo <owner/repo> [--issue <n>] [--domain <name>] [--pr-merged true|false] [--dry-run|--write] [--format json|markdown]";

    private const string RecoveryActionRecoverLinkedPr = "recover-linked-pr-from-github-closing-reference";

    /// <summary>
    /// Test seam — replaces the default UTC timestamp source for runs events.
    /// </summary>
    public static Func<DateTimeOffset>? UtcNowFactory { get; set; }

    /// <summary>
    /// G477: test seam for the PR closing-issues fetcher used by the
    /// missing-<c>linked_pr</c> auto-recovery path. Production leaves this null
    /// and falls back to <see cref="GhCliPrClosingIssuesFetcher"/> (which
    /// fail-softs to an empty list on any <c>gh</c> error). Tests inject a
    /// deterministic fake so closeout never shells out to live GitHub.
    /// </summary>
    public static Func<IPrClosingIssuesFetcher>? PrClosingIssuesFetcherFactory { get; set; }

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

        if (!TryParseArguments(args, out var pr, out var repo, out var domainOverride, out var linkedIssueNumber, out var prMerged, out var write, out var format, out var error))
        {
            writer.WriteLine(error);
            writer.WriteLine(UsageLine);
            return 1;
        }

        var domain = string.IsNullOrWhiteSpace(domainOverride)
            ? context.Config.Project.Domain
            : domainOverride!;

        // G327: route queue-state read/write and runs.jsonl append
        // through the runtime-scoped resolver so two domain/repo pairs
        // (e.g. intent-system vs Sekiban) stop sharing the same active
        // runtime queue at the root `.intent-cli/queue-state.json`.
        //
        // queue-state.json is the authoritative source of layout:
        // - Scoped on disk → both files live under
        //   `.intent-cli/runtime/<domain>/<owner>__<repo>/`.
        // - Only legacy queue-state on disk → fall back to legacy root
        //   for BOTH files (transition fallback; keeps queue + audit
        //   trail in the same layout so closeout history stays
        //   consistent).
        // - Neither exists → scoped first-write target.
        //
        // The runs.jsonl path is intentionally bound to the queue-state
        // layout decision rather than resolved independently — they
        // are sibling durable-state files for the same closeout.
        var queueStateLocation = RuntimeScopedStateResolver.ResolveQueueStatePathForRead(
            context.RepoRoot, domain, repo!);
        var queueStatePath = queueStateLocation.Path;
        string runsLogPath;
        string stateLayout;
        switch (queueStateLocation.Kind)
        {
            case StateLocationKind.Scoped:
                runsLogPath = RuntimeScopedStateResolver.GetScopedRunLogPath(
                    context.RepoRoot, domain, repo!);
                stateLayout = "scoped";
                break;
            case StateLocationKind.Legacy:
                runsLogPath = RuntimeScopedStateResolver.GetLegacyRunLogPath(
                    context.RepoRoot);
                stateLayout = "legacy-fallback";
                break;
            default:
                // MissingPreferScoped: queue-state will be created at
                // the scoped path on first write; runs.jsonl follows it.
                runsLogPath = RuntimeScopedStateResolver.GetScopedRunLogPath(
                    context.RepoRoot, domain, repo!);
                stateLayout = "scoped";
                break;
        }

        // G297: when the host loop explicitly tells us the PR has not merged
        // (`--pr-merged false`, e.g. captured via `gh pr view <n> --json
        // merged --jq .merged`), refuse closeout — recording closeout for a
        // non-merged PR would diverge parent durable state from GitHub.
        if (prMerged == false)
        {
            EmitErrorResult(writer, format, NewFailureResult(domain, repo!, pr!.Value, queueStatePath, runsLogPath, write, stateLayout: stateLayout, error:
                $"PR #{pr} is not merged; closeout pr requires merge success (G297). Re-run after the PR is merged. If the PR is still draft, ready it for review and re-run host review/merge first."));
            return 1;
        }

        if (!File.Exists(queueStatePath))
        {
            EmitErrorResult(writer, format, NewFailureResult(domain, repo!, pr!.Value, queueStatePath, runsLogPath, write, stateLayout: stateLayout, error:
                $"queue-state file not found: {queueStatePath}"));
            return 1;
        }

        QueueState queueState;
        try
        {
            queueState = QueueStateSerializer.Deserialize(File.ReadAllText(queueStatePath));
        }
        catch (JsonException jsonException)
        {
            EmitErrorResult(writer, format, NewFailureResult(domain, repo!, pr!.Value, queueStatePath, runsLogPath, write, stateLayout: stateLayout, error:
                $"queue-state JSON could not be parsed: {jsonException.Message}"));
            return 1;
        }
        catch (InvalidOperationException invalidOperation)
        {
            EmitErrorResult(writer, format, NewFailureResult(domain, repo!, pr!.Value, queueStatePath, runsLogPath, write, stateLayout: stateLayout, error:
                $"queue-state payload was invalid: {invalidOperation.Message}"));
            return 1;
        }

        var prToken = pr!.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var matchedItem = queueState.Items.FirstOrDefault(item => MatchesLinkedPr(item, repo!, prToken));

        // Fallback A (operator-supplied): when linked_pr is absent, resolve via
        // --issue (linked_issue.Number). Deterministic operator input wins.
        if (matchedItem is null && linkedIssueNumber.HasValue)
        {
            matchedItem = queueState.Items.FirstOrDefault(item =>
                GitHubWorkItemIdentity.MatchesIssue(item.LinkedIssue, repo!, linkedIssueNumber.Value));
        }

        // G477 Fallback B (automatic, deterministic): when linked_pr is missing
        // and the operator did not (or could not) disambiguate via --issue,
        // recover from GitHub closing-issue facts. `linked_pr` is a host-owned
        // projection that can be absent even when the GitHub issue/PR
        // relationship is deterministic; a merged PR whose closing references
        // identify exactly ONE queue item (by linked_issue) is safe to recover
        // without forcing the operator to rerun with --issue <n>. Ambiguous
        // evidence still fails closed (no guessing). The fetcher fail-softs to
        // an empty list on any gh error, which routes to the existing
        // not-found failure path.
        var recoverableMissingLinkedPr = false;
        int? inferredIssue = null;
        string? recoverySource = null;
        string? recoveryAction = null;
        if (matchedItem is null)
        {
            var fetcher = PrClosingIssuesFetcherFactory?.Invoke() ?? new GhCliPrClosingIssuesFetcher();
            var closingIssues = fetcher.Fetch(repo!, pr!.Value);
            var reconstruction = GitHubLinkageReconstructor.Reconstruct(closingIssues, queueState, repo!);
            switch (reconstruction.Kind)
            {
                case LinkageReconstructionKind.Deterministic:
                    var candidate = reconstruction.Candidates[0];
                    matchedItem = queueState.Items.First(item =>
                        string.Equals(item.ExecutionUnit, candidate.ExecutionUnit, StringComparison.Ordinal));
                    recoverableMissingLinkedPr = true;
                    inferredIssue = candidate.LinkedIssueNumber;
                    recoverySource = LinkageReconstructionConstants.RecoverySourceGitHubClosingReference;
                    recoveryAction = RecoveryActionRecoverLinkedPr;
                    break;

                case LinkageReconstructionKind.Ambiguous:
                    var candidateUnits = string.Join(", ", reconstruction.Candidates.Select(c =>
                        $"{c.ExecutionUnit} (issue #{c.LinkedIssueNumber})"));
                    EmitErrorResult(writer, format, NewFailureResult(domain, repo!, pr.Value, queueStatePath, runsLogPath, write,
                        $"linkage-ambiguous: PR #{pr} closing-issues match {reconstruction.Candidates.Count} queue items "
                        + $"({candidateUnits}); refusing to guess. Re-run `closeout pr --pr {pr} --issue <n>` with the correct linked issue.",
                        stateLayout: stateLayout));
                    return 1;

                case LinkageReconstructionKind.NoClosingReferences:
                case LinkageReconstructionKind.NoMatch:
                default:
                    break;
            }
        }

        if (matchedItem is null)
        {
            var hint = linkedIssueNumber.HasValue
                ? $"no queue item found with linked_pr matching #{pr} or linked_issue.number {linkedIssueNumber.Value}."
                : $"no queue item found with linked_pr matching #{pr}. If the queue item has linked_issue but not linked_pr, retry with --issue <n>.";
            EmitErrorResult(writer, format, NewFailureResult(domain, repo!, pr.Value, queueStatePath, runsLogPath, write, hint, stateLayout: stateLayout));
            return 1;
        }

        var alreadyCompleted = matchedItem.State == QueueItemState.Completed;
        var beforeState = matchedItem.State.ToString().ToLowerInvariant();
        var mode = write ? ModeWrite : ModeDryRun;

        // Plan the queue transition. Queued/Active/Review/Fixing → Completed is supported here.
        // Queued is included because the publish/review loop may leave items in queued state
        // when the PR is accepted before all intermediate state transitions are recorded.
        if (!alreadyCompleted
            && matchedItem.State != QueueItemState.Queued
            && matchedItem.State != QueueItemState.Active
            && matchedItem.State != QueueItemState.Review
            && matchedItem.State != QueueItemState.Fixing)
        {
            EmitErrorResult(writer, format, NewFailureResult(domain, repo!, pr.Value, queueStatePath, runsLogPath, write,
                $"queue item '{matchedItem.ExecutionUnit}' is in state '{beforeState}'; closeout supports queued/active/review/fixing → completed only.",
                stateLayout: stateLayout));
            return 1;
        }

        var executionUnit = matchedItem.ExecutionUnit;
        var nowTs = (UtcNowFactory?.Invoke() ?? DateTimeOffset.UtcNow).ToUniversalTime();

        // G661 / RH-030A: shipped work does not erase contradictory packet
        // history. Report the still-retired sidecar and leave it byte-identical;
        // only an explicit, evidenced reactivation may change lifecycle.
        var closeoutFindings = new List<CloseoutPrFinding>();
        var packetDirectory = Path.Combine(context.RepoRoot, ".intent-cli", "issues", executionUnit);
        var lifecycle = PacketLifecycle.ReadState(packetDirectory);
        if (lifecycle.State == PacketLifecycleState.ValidRetired)
        {
            closeoutFindings.Add(new CloseoutPrFinding
            {
                Kind = "shipped-while-retired-contradiction",
                Path = lifecycle.SidecarPath,
                Summary =
                    $"Execution unit '{executionUnit}' is being closed out as shipped while lifecycle.yaml still "
                    + $"declares '{lifecycle.Metadata?.Lifecycle}'. The lifecycle record was not changed.",
                RecommendedAction =
                    $"Inspect the history, then run `intent-cli packet retire --execution-unit {executionUnit} "
                    + "--reactivate --evidence <why-the-prior-retirement-is-no-longer-valid> --write --format json` "
                    + "if reactivation is correct. Never silently unretire a shipped unit.",
            });
        }

        var runsEvents = new List<string>
        {
            BuildRunsEvent(executionUnit, "pr-merged", repo!, pr.Value, nowTs),
            BuildRunsEvent(executionUnit, "closeout-recorded", repo!, pr.Value, nowTs)
        };

        // G477: when linkage was recovered from GitHub facts, repair the
        // host-owned `linked_pr` projection while completing the item so the
        // queue-state stops being deterministically incomplete. Stored as the
        // canonical PR URL, matching the recovered-linkage payload shape.
        var recoveredLinkedPr = recoverableMissingLinkedPr
            ? $"https://github.com/{repo}/pull/{pr.Value}"
            : null;

        if (write && !alreadyCompleted)
        {
            var updatedItems = queueState.Items
                .Select(item => item.ExecutionUnit == matchedItem.ExecutionUnit
                    ? UpdateItemState(item, QueueItemState.Completed, recoveredLinkedPr)
                    : item)
                .ToArray();
            var updatedState = new QueueState
            {
                SchemaVersion = queueState.SchemaVersion,
                UpdatedAt = (UtcNowFactory?.Invoke() ?? DateTimeOffset.UtcNow).ToUniversalTime(),
                Items = updatedItems
            };
            // G327: the scoped runtime tree may need its directory
            // created on first write (legacy root path is always
            // present because RepoRoot/.intent-cli/ exists by host
            // contract). Idempotent.
            Directory.CreateDirectory(Path.GetDirectoryName(queueStatePath)!);
            // G548: guarded write (no-item-loss + stale-base re-application).
            QueueStatePersistence.Persist(queueStatePath, queueState, updatedState);

            Directory.CreateDirectory(Path.GetDirectoryName(runsLogPath)!);
            using var stream = new FileStream(runsLogPath, FileMode.Append, FileAccess.Write);
            using var streamWriter = new StreamWriter(stream);
            foreach (var line in runsEvents)
            {
                streamWriter.WriteLine(line);
            }
        }

        var continuation = ClassifyContinuation(queueState, matchedItem.ExecutionUnit);

        var nextSteps = new List<string>();

        var linkedIssue = matchedItem.LinkedIssue;
        if (linkedIssue is not null)
        {
            if (linkedIssue.Number.HasValue)
            {
                nextSteps.Add($"Close the linked issue: `gh issue close {linkedIssue.Number.Value} --repo {linkedIssue.Repo} --comment 'Closed by PR #{pr}.'`");
            }
            else if (!string.IsNullOrWhiteSpace(linkedIssue.Url))
            {
                nextSteps.Add($"Close the linked issue at {linkedIssue.Url}: `gh issue close <n> --repo {linkedIssue.Repo} --comment 'Closed by PR #{pr}.'`");
            }
            else
            {
                nextSteps.Add($"Confirm and close the linked issue in {linkedIssue.Repo} (number not resolved): `gh issue close <n> --repo {linkedIssue.Repo} --comment 'Closed by PR #{pr}.'`");
            }
        }
        else
        {
            nextSteps.Add("Confirm and close the linked issue if one exists (linked_issue not set in queue state).");
        }

        if (HasConfiguredSubmodules(context.RepoRoot))
        {
            nextSteps.Add($"Sync the parent submodule pointer for {repo} to the merge commit (manual step: git -C submodules/<child> fetch && git -C submodules/<child> reset --hard <merge-sha>; git add submodules/<child>).");
            nextSteps.Add("Commit and push the parent durable state (queue-state, runs, submodule pointer).");
        }
        else
        {
            nextSteps.Add("Commit and push the parent durable state (queue-state, runs).");
        }

        var result = new CloseoutPrResult
        {
            Domain = domain,
            Repo = repo!,
            Pr = pr.Value,
            ExecutionUnit = executionUnit,
            Mode = mode,
            QueueStatePath = queueStatePath,
            RunsLogPath = runsLogPath,
            QueueStateBeforeState = beforeState,
            QueueStateAfterState = "completed",
            QueueAlreadyCompleted = alreadyCompleted,
            RunsEvents = runsEvents,
            ContinuationHint = continuation,
            NextSteps = nextSteps,
            Error = null,
            StateLayout = stateLayout,
            RecoverableMissingLinkedPr = recoverableMissingLinkedPr,
            InferredIssue = inferredIssue,
            RecoverySource = recoverySource,
            RecoveryAction = recoveryAction,
            Findings = closeoutFindings,
        };

        EmitResult(writer, result, format);
        return 0;
    }

    private static bool HasConfiguredSubmodules(string repoRoot)
    {
        var gitmodulesPath = Path.Combine(repoRoot, ".gitmodules");
        if (!File.Exists(gitmodulesPath))
        {
            return false;
        }

        try
        {
            return File.ReadLines(gitmodulesPath)
                .Any(line => line.TrimStart().StartsWith("[submodule ", StringComparison.Ordinal));
        }
        catch (IOException)
        {
            // A closeout plan must not claim a submodule sync step it could not
            // establish from the host layout.
            return false;
        }
    }

    private static bool MatchesLinkedPr(QueueItem item, string repo, string prToken)
    {
        return int.TryParse(prToken, out var number)
            && GitHubWorkItemIdentity.MatchesPullRequest(item, repo, number);
    }

    private static QueueItem UpdateItemState(QueueItem item, QueueItemState state, string? recoveredLinkedPr = null)
    {
        return new QueueItem
        {
            ExecutionUnit = item.ExecutionUnit,
            Title = item.Title,
            State = state,
            Dependencies = item.Dependencies,
            BlockedBy = item.BlockedBy,
            ClarificationReturnPath = item.ClarificationReturnPath,
            PacketPaths = item.PacketPaths,
            LinkedIssue = item.LinkedIssue,
            // G477: recover the missing linked_pr projection when GitHub facts
            // deterministically identified this item; otherwise preserve it.
            LinkedPr = recoveredLinkedPr ?? item.LinkedPr,
            WorkerRole = item.WorkerRole,
            ReviewRole = item.ReviewRole,
            Priority = item.Priority
        };
    }

    /// <summary>
    /// G324: emit a current-schema <see cref="RunEvent"/> line that the
    /// supervisor / durable-state preflight stack can deserialize without
    /// errors. Replaces the legacy <c>timestamp</c> + <c>kind</c> fields
    /// (which the strict G312 preflight rejects as invalid) with the
    /// canonical <c>ts</c> / <c>event</c> / <c>by</c> trio plus the new
    /// optional <c>repo</c> / <c>pr</c> correlation fields.
    /// </summary>
    private static string BuildRunsEvent(string executionUnit, string @event, string repo, int pr, DateTimeOffset ts)
    {
        var runEvent = new RunEvent
        {
            Ts = ts,
            ExecutionUnit = executionUnit,
            Event = @event,
            By = "intent-cli closeout pr",
            Repo = repo,
            Pr = pr
        };
        return RunLogSerializer.SerializeLine(runEvent);
    }

    private static string ClassifyContinuation(QueueState state, string completingUnit)
    {
        var openClarification = state.Items.Any(item => item.State == QueueItemState.ClarifyBlocked);
        if (openClarification)
        {
            return ContinuationClarificationRequired;
        }

        var queuedRemaining = state.Items.Any(item =>
            item.State == QueueItemState.Queued
            && !string.Equals(item.ExecutionUnit, completingUnit, StringComparison.Ordinal));
        if (queuedRemaining)
        {
            return ContinuationNextSliceReady;
        }

        return ContinuationNoActionableItem;
    }

    private static CloseoutPrResult NewFailureResult(
        string domain,
        string repo,
        int pr,
        string queueStatePath,
        string runsLogPath,
        bool write,
        string error,
        string? stateLayout = null)
    {
        return new CloseoutPrResult
        {
            Domain = domain,
            Repo = repo,
            Pr = pr,
            ExecutionUnit = null,
            Mode = write ? ModeWrite : ModeDryRun,
            QueueStatePath = queueStatePath,
            RunsLogPath = runsLogPath,
            QueueStateBeforeState = null,
            QueueStateAfterState = null,
            QueueAlreadyCompleted = false,
            RunsEvents = Array.Empty<string>(),
            ContinuationHint = null,
            NextSteps = Array.Empty<string>(),
            Error = error,
            StateLayout = stateLayout,
            Findings = Array.Empty<CloseoutPrFinding>(),
        };
    }

    private static void EmitErrorResult(TextWriter writer, string format, CloseoutPrResult result)
    {
        EmitResult(writer, result, format);
    }

    private static void EmitResult(TextWriter writer, CloseoutPrResult result, string format)
    {
        if (string.Equals(format, FormatJson, StringComparison.Ordinal))
        {
            writer.Write(JsonSerializer.Serialize(result, JsonOptions));
            writer.WriteLine();
        }
        else
        {
            WriteMarkdown(writer, result);
        }
    }

    private static void WriteMarkdown(TextWriter writer, CloseoutPrResult result)
    {
        writer.WriteLine($"# Closeout PR — {result.Repo}#{result.Pr}");
        writer.WriteLine();
        writer.WriteLine($"- domain: {result.Domain}");
        writer.WriteLine($"- mode: {result.Mode}");
        writer.WriteLine($"- queue-state path: {result.QueueStatePath}");
        writer.WriteLine($"- runs-log path: {result.RunsLogPath}");
        if (!string.IsNullOrWhiteSpace(result.ExecutionUnit))
        {
            writer.WriteLine($"- execution unit: {result.ExecutionUnit}");
        }
        writer.WriteLine();

        if (!string.IsNullOrWhiteSpace(result.Error))
        {
            writer.WriteLine("## Error");
            writer.WriteLine($"- {result.Error}");
            return;
        }

        writer.WriteLine("## Findings");
        if (result.Findings.Count == 0)
        {
            writer.WriteLine("- none");
        }
        else
        {
            foreach (var finding in result.Findings)
            {
                writer.WriteLine($"- {finding.Kind}: {finding.Summary}");
                writer.WriteLine($"  - path: {finding.Path}");
                writer.WriteLine($"  - recommended_action: {finding.RecommendedAction}");
            }
        }
        writer.WriteLine();

        writer.WriteLine("## Queue transition");
        writer.WriteLine($"- before: {result.QueueStateBeforeState}");
        writer.WriteLine($"- after: {result.QueueStateAfterState}");
        writer.WriteLine($"- already completed: {(result.QueueAlreadyCompleted ? "yes" : "no")}");
        writer.WriteLine();

        if (result.RecoverableMissingLinkedPr)
        {
            writer.WriteLine("## Recovered linkage (G477)");
            writer.WriteLine("- recoverable missing linked_pr: yes");
            writer.WriteLine($"- inferred issue: #{result.InferredIssue}");
            writer.WriteLine($"- recovery source: {result.RecoverySource}");
            writer.WriteLine($"- recovery action: {result.RecoveryAction}");
            writer.WriteLine();
        }

        writer.WriteLine("## Runs events");
        if (result.RunsEvents.Count == 0)
        {
            writer.WriteLine("- none");
        }
        else
        {
            foreach (var line in result.RunsEvents)
            {
                writer.WriteLine($"- {line}");
            }
        }
        writer.WriteLine();

        writer.WriteLine($"## Continuation hint: {result.ContinuationHint}");
        writer.WriteLine();

        if (result.NextSteps.Count > 0)
        {
            writer.WriteLine("## Next steps");
            foreach (var step in result.NextSteps)
            {
                writer.WriteLine($"- {step}");
            }
        }
    }

    private static bool TryParseArguments(
        string[] args,
        out int? pr,
        out string? repo,
        out string? domainOverride,
        out int? linkedIssueNumber,
        out bool? prMerged,
        out bool write,
        out string format,
        out string error)
    {
        pr = null;
        repo = null;
        domainOverride = null;
        linkedIssueNumber = null;
        prMerged = null;
        write = false;
        var dryRun = false;
        format = FormatMarkdown;
        error = string.Empty;

        for (var index = 0; index < args.Length; index++)
        {
            var argument = args[index];
            switch (argument)
            {
                case "--pr":
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                    {
                        error = "--pr requires a value.";
                        return false;
                    }

                    if (!int.TryParse(args[index + 1], System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var prValue) || prValue <= 0)
                    {
                        error = $"--pr must be a positive integer (got '{args[index + 1]}').";
                        return false;
                    }

                    pr = prValue;
                    index++;
                    break;

                case "--repo":
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                    {
                        error = "--repo requires a value.";
                        return false;
                    }

                    repo = args[index + 1];
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

                case "--issue":
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                    {
                        error = "--issue requires a value.";
                        return false;
                    }

                    if (!int.TryParse(args[index + 1], System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var issueValue) || issueValue <= 0)
                    {
                        error = $"--issue must be a positive integer (got '{args[index + 1]}').";
                        return false;
                    }

                    linkedIssueNumber = issueValue;
                    index++;
                    break;

                case "--write":
                    if (dryRun)
                    {
                        error = "--write and --dry-run are mutually exclusive.";
                        return false;
                    }

                    write = true;
                    break;

                case "--dry-run":
                    if (write)
                    {
                        error = "--write and --dry-run are mutually exclusive.";
                        return false;
                    }

                    dryRun = true;
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

                case "--pr-merged":
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                    {
                        error = "--pr-merged requires a value (true or false).";
                        return false;
                    }
                    var rawMerged = args[index + 1].Trim().ToLowerInvariant();
                    if (string.Equals(rawMerged, "true", StringComparison.Ordinal))
                    {
                        prMerged = true;
                    }
                    else if (string.Equals(rawMerged, "false", StringComparison.Ordinal))
                    {
                        prMerged = false;
                    }
                    else
                    {
                        error = $"--pr-merged must be 'true' or 'false' (got '{args[index + 1]}').";
                        return false;
                    }
                    index++;
                    break;

                default:
                    error = $"Unknown argument '{argument}'.";
                    return false;
            }
        }

        if (pr is null)
        {
            error = "--pr is required.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(repo))
        {
            error = "--repo is required.";
            return false;
        }

        return true;
    }

    private static void WriteHelp(TextWriter writer)
    {
        writer.WriteLine("closeout pr");
        writer.WriteLine(UsageLine);
        writer.WriteLine("Records the queue/runs closeout for an accepted child PR. --dry-run plans only; --write applies queue + runs updates. Submodule sync remains a manual next step.");
        writer.WriteLine("  Supported states: queued, active, review, fixing → completed.");
        writer.WriteLine("  --issue <n>      Optional: fallback linked-issue number for queue items where linked_pr is absent.");
        writer.WriteLine("  G477: when linked_pr is missing, closeout auto-recovers from GitHub closing-issue facts — a merged PR closing exactly one issue that maps to a single queue item completes without --issue. Ambiguous evidence fails closed (recovery_action / inferred_issue surfaced in the result).");
        writer.WriteLine("  --pr-merged true|false  Optional (G297): explicit GitHub merge state. When 'false', closeout refuses the operation so a draft / unmerged PR cannot record closeout; capture the value via 'gh pr view <n> --json merged --jq .merged'.");
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    // G324: legacy raw-dictionary serialization options removed —
    // closeout now emits canonical `RunEvent` lines via
    // `RunLogSerializer.SerializeLine` so durable-state preflight can
    // deserialize them.
}

internal sealed record CloseoutPrResult
{
    [JsonPropertyName("domain")]
    public required string Domain { get; init; }

    [JsonPropertyName("repo")]
    public required string Repo { get; init; }

    [JsonPropertyName("pr")]
    public required int Pr { get; init; }

    [JsonPropertyName("execution_unit")]
    public string? ExecutionUnit { get; init; }

    [JsonPropertyName("mode")]
    public required string Mode { get; init; }

    [JsonPropertyName("queue_state_path")]
    public required string QueueStatePath { get; init; }

    [JsonPropertyName("runs_log_path")]
    public required string RunsLogPath { get; init; }

    [JsonPropertyName("queue_state_before")]
    public string? QueueStateBeforeState { get; init; }

    [JsonPropertyName("queue_state_after")]
    public string? QueueStateAfterState { get; init; }

    [JsonPropertyName("queue_already_completed")]
    public required bool QueueAlreadyCompleted { get; init; }

    [JsonPropertyName("runs_events")]
    public required IReadOnlyList<string> RunsEvents { get; init; }

    [JsonPropertyName("continuation_hint")]
    public string? ContinuationHint { get; init; }

    [JsonPropertyName("next_steps")]
    public required IReadOnlyList<string> NextSteps { get; init; }

    [JsonPropertyName("error")]
    public string? Error { get; init; }

    /// <summary>
    /// G327: which on-disk layout the runtime-scoped resolver chose
    /// for this closeout — <c>scoped</c> when the per-(domain, repo)
    /// `.intent-cli/runtime/&lt;domain&gt;/&lt;owner&gt;__&lt;repo&gt;/queue-state.json`
    /// is on disk (or selected as the first-write target), or
    /// <c>legacy-fallback</c> when only the legacy root path exists.
    /// Surfaced for operator/controller diagnostics during migration.
    /// </summary>
    [JsonPropertyName("state_layout")]
    public string? StateLayout { get; init; }

    /// <summary>
    /// G477: true when this closeout matched its queue item by recovering a
    /// missing <c>linked_pr</c> from deterministic GitHub closing-issue facts
    /// (the merged PR closed exactly one issue mapping to a single queue item)
    /// rather than by a direct <c>linked_pr</c> match or an operator-supplied
    /// <c>--issue</c>. Lets the host loop converge automatically instead of
    /// treating the missing projection as an operator policy question.
    /// </summary>
    [JsonPropertyName("recoverable_missing_linked_pr")]
    public bool RecoverableMissingLinkedPr { get; init; }

    /// <summary>
    /// G477: the linked-issue number inferred from GitHub closing references
    /// when <see cref="RecoverableMissingLinkedPr"/> is true; null otherwise.
    /// </summary>
    [JsonPropertyName("inferred_issue")]
    public int? InferredIssue { get; init; }

    /// <summary>
    /// G477: provenance of the recovery (<c>github-closing-reference</c>) when
    /// linkage was recovered; null otherwise.
    /// </summary>
    [JsonPropertyName("recovery_source")]
    public string? RecoverySource { get; init; }

    /// <summary>
    /// G477: the safe recovery/fallback action taken
    /// (<c>recover-linked-pr-from-github-closing-reference</c>) when linkage was
    /// recovered; null otherwise.
    /// </summary>
    [JsonPropertyName("recovery_action")]
    public string? RecoveryAction { get; init; }

    [JsonPropertyName("findings")]
    public required IReadOnlyList<CloseoutPrFinding> Findings { get; init; }
}

internal sealed record CloseoutPrFinding
{
    [JsonPropertyName("kind")]
    public required string Kind { get; init; }

    [JsonPropertyName("path")]
    public required string Path { get; init; }

    [JsonPropertyName("summary")]
    public required string Summary { get; init; }

    [JsonPropertyName("recommended_action")]
    public required string RecommendedAction { get; init; }
}
