using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using IntentSystem.Supervisor.Models;
using IntentSystem.Supervisor.Serialization;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G525: <c>intent-cli automation issue-retire --repo &lt;r&gt; --issue &lt;n&gt;
/// --reason &lt;superseded|decomposed|obsolete&gt; [--note &lt;text&gt;] [--domain
/// &lt;name&gt;] [--write]</c> — the canonical, atomic transition that
/// supersedes a published <c>intent-target</c> issue that can never be
/// started as authored (e.g. a research pass proves the slice must be
/// decomposed).
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
/// PR, an active claim (<c>intent-issue-in-progress</c>), or a matched queue
/// item that is already <see cref="QueueItemState.Completed"/> (merged/
/// finished work is out of scope for retirement). Idempotent: re-running on
/// an already-retired execution unit is a safe no-op that also finishes any
/// missing <c>runs.jsonl</c> event from a prior partial write. Never deletes
/// packet files.
///
/// Partial-failure recovery (review repair): the target issue is resolved
/// via a direct <c>gh issue view</c> snapshot — regardless of open/closed
/// state — instead of scanning the OPEN-issues list. This means a retry
/// after a mid-sequence failure (close succeeded but label removal or the
/// durable-state write did not) can still find the issue and finish the
/// remaining steps: the issue no longer needs to be OPEN for the retry to
/// converge. Recovery for an already-CLOSED issue is only authorized when
/// GitHub's own <c>stateReason</c> is <c>NOT_PLANNED</c> (the exact reason
/// this command uses to close) — a closed issue with any other reason (e.g.
/// completed via merge) is left untouched.
///
/// Domain/repo isolation (G522 boundary): queue-item matching requires an
/// EXACT <c>(repo, issue number)</c> pair — a same-numbered issue in a
/// different repo can never match. The execution unit's domain is resolved
/// via <see cref="PacketDomainResolution"/> (explicit <c>--domain</c> &gt;
/// packet-declared <c>domain:</c> &gt; fail loud with candidate domains and
/// an exact <c>--domain</c> re-invocation) for BOTH an existing queue item
/// and a brand-new one derived from the issue title — a misleading title
/// prefix alone can never authorize queue creation without a packet.yaml
/// (or an explicit operator-supplied <c>--domain</c>) confirming it.
/// </summary>
internal static class AutomationIssueRetireCommand
{
    private const string FormatJson = "json";
    private const string FormatMarkdown = "markdown";

    public const string ReasonSuperseded = "superseded";
    public const string ReasonDecomposed = "decomposed";
    public const string ReasonObsolete = "obsolete";

    private static readonly string[] AllowedReasons = { ReasonSuperseded, ReasonDecomposed, ReasonObsolete };

    /// <summary>GitHub's own reason for a CLOSED issue that this command uses when closing.</summary>
    private const string StateReasonNotPlanned = "NOT_PLANNED";

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
        "Usage: intent-cli automation issue-retire --repo <owner/repo> --issue <n> --reason <superseded|decomposed|obsolete> [--note <text>] [--domain <name>] [--write] [--format json|markdown]";

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

        if (!TryParseArguments(args, out var repo, out var issue, out var reason, out var note, out var domain, out var write, out var format, out var error))
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

        var existingItem = queueState?.Items.FirstOrDefault(item => MatchesLinkedIssue(item.LinkedIssue, repo!, issue!.Value));

        // Out of scope: retiring merged/completed work. Refuse before any
        // GitHub call — zero GitHub or local mutation for this refusal.
        if (existingItem is { State: QueueItemState.Completed })
        {
            writer.WriteLine(
                $"refusing to retire issue #{issue}: queue item '{existingItem.ExecutionUnit}' is already "
                + $"{QueueItemState.Completed} (merged/finished work) — `automation issue-retire` only applies to "
                + "work that was published but can never be completed as authored. No GitHub or local state was "
                + "touched.");
            return 1;
        }

        // Idempotency: durable state is the source of truth. If this
        // execution unit is already retired, never re-attempt the GitHub
        // mutation (the issue is presumably already closed). A dry-run
        // always reports the no-op without touching anything; --write also
        // finishes a missing runs.jsonl event left over from a prior
        // partial write (queue-state persisted, but the audit-trail append
        // that follows it did not) instead of silently dropping it forever.
        if (existingItem is { State: QueueItemState.Retired })
        {
            if (!write || RunsLogHasRetireEvent(context, existingItem.ExecutionUnit))
            {
                var alreadyRetired = new AutomationIssueRetireResult
                {
                    Repo = repo!,
                    Issue = issue!.Value,
                    Reason = reason!,
                    Note = note,
                    Domain = null,
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

            try
            {
                AppendRetireRunEvent(
                    context,
                    existingItem.ExecutionUnit,
                    repo!,
                    issue!.Value,
                    existingItem.RetirementReason ?? reason!,
                    (UtcNowFactory?.Invoke() ?? DateTimeOffset.UtcNow).ToUniversalTime());
            }
            catch (Exception exception) when (exception is IOException or InvalidOperationException or JsonException or UnauthorizedAccessException)
            {
                writer.WriteLine(
                    $"'{existingItem.ExecutionUnit}' is already retired in queue-state, but appending the "
                    + $"runs.jsonl event failed: {exception.Message}. Re-run `automation issue-retire --write` to retry.");
                return 1;
            }

            var recovered = new AutomationIssueRetireResult
            {
                Repo = repo!,
                Issue = issue!.Value,
                Reason = reason!,
                Note = note,
                Domain = null,
                Mode = "write",
                Applied = true,
                AlreadyRetired = true,
                ExecutionUnit = existingItem.ExecutionUnit,
                PlannedMutations = new[] { $"append missing `{RetireRunEventName}` event to runs.jsonl for '{existingItem.ExecutionUnit}'" },
                RefusalReason = null,
                Summary = $"'{existingItem.ExecutionUnit}' was already retired; completed a missing runs.jsonl event from a prior partial write.",
            };
            EmitResult(writer, format, recovered);
            return 0;
        }

        IGitHubIssueRetirementMutator retirementMutator = RetirementMutatorFactory?.Invoke() ?? new GhCliGitHubIssueRetirementMutator();
        IssueSnapshot snapshot;
        try
        {
            snapshot = retirementMutator.GetSnapshot(repo!, issue!.Value);
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException)
        {
            writer.WriteLine($"issue #{issue} not found in {repo}, or GitHub could not be reached: {exception.Message}");
            return 1;
        }

        var currentLabels = snapshot.Labels.ToHashSet(StringComparer.Ordinal);
        var issueIsOpen = IsOpen(snapshot.State);
        var executionUnit = existingItem?.ExecutionUnit ?? ExecutionUnitFromTitle(snapshot.Title);

        if (issueIsOpen)
        {
            var lister = CandidateListerFactory?.Invoke() ?? new GhCliGitHubAutomationCandidateLister();
            IReadOnlyList<GitHubAutomationPrCandidate> openPrs;
            try
            {
                openPrs = lister.ListPullRequests(repo!, Array.Empty<string>());
            }
            catch (Exception exception) when (exception is IOException or InvalidOperationException)
            {
                writer.WriteLine($"failed to read GitHub state for {repo}: {exception.Message}");
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
        }
        else if (!string.Equals(snapshot.StateReason, StateReasonNotPlanned, StringComparison.OrdinalIgnoreCase))
        {
            writer.WriteLine(
                $"refusing to retire issue #{issue}: it is CLOSED in {repo} with reason "
                + $"'{(string.IsNullOrWhiteSpace(snapshot.StateReason) ? "(unknown)" : snapshot.StateReason)}', not "
                + "'not planned' — this does not look like an `automation issue-retire` partial-failure recovery. "
                + "If it was already fully processed (e.g. merged and closed out), no action is needed.");
            return 1;
        }
        else if (!HasCanonicalRetireMarker(snapshot.Comments, executionUnit, reason!))
        {
            // Repair: `stateReason == NOT_PLANNED` alone is not durable
            // provenance — an issue closed manually/independently as "not
            // planned" would pass that check too. Require the canonical
            // marker comment this command itself posts alongside its close,
            // anchored to the exact candidate execution unit AND the exact
            // requested reason, before resuming. A quoted/substring marker,
            // a unit-prefix collision, or a marker recorded under a
            // different reason all fail this check.
            writer.WriteLine(
                $"refusing to retire issue #{issue}: it is CLOSED in {repo} with reason 'not planned', but no "
                + $"canonical `automation issue-retire` marker comment for '{executionUnit}' with reason "
                + $"'{reason}' was found — this does not look like a partial-failure recovery from a prior run of "
                + "this command with the same reason (e.g. a manual/unrelated 'not planned' closure, a marker for "
                + "a different unit or reason, or a comment merely quoting the marker text). Refusing with zero "
                + "GitHub or local mutation.");
            return 1;
        }

        var candidateDomains = DomainCandidateScanner.Scan(context);
        var packetDeclaredDomain = ReadPacketDeclaredDomain(context, executionUnit);
        var noteSuffix = string.IsNullOrWhiteSpace(note) ? string.Empty : $" --note \"{note}\"";
        var domainResolution = PacketDomainResolution.Resolve(
            domain,
            packetDeclaredDomain,
            candidateDomains,
            $"intent-cli automation issue-retire --repo {repo} --issue {issue} --reason {reason} --domain <name>{noteSuffix} --write");
        if (domainResolution.IsError)
        {
            writer.WriteLine(
                $"refusing to retire issue #{issue}: {domainResolution.ErrorMessage} A misleading title prefix "
                + "alone is never sufficient to create or mutate a queue entry.");
            return 1;
        }
        var resolvedDomain = domainResolution.Domain!;

        var labelsToRemove = KnownIssueWorkflowLabels.Where(currentLabels.Contains).ToArray();
        var reasonComment = BuildReasonComment(executionUnit, reason!, note);

        var plannedMutations = new List<string>();
        if (issueIsOpen)
        {
            plannedMutations.Add($"gh issue close #{issue} --repo {repo} --reason \"not planned\" --comment <reason comment>");
        }
        else
        {
            plannedMutations.Add($"issue #{issue} already CLOSED (not planned) — resuming a partial prior --write");
        }
        if (labelsToRemove.Length > 0)
        {
            plannedMutations.Add($"remove label(s) from issue #{issue}: {string.Join(", ", labelsToRemove)}");
        }
        plannedMutations.Add(
            existingItem is null
                ? $"create queue-state entry for '{executionUnit}' (domain: {resolvedDomain}) with state=retired, reason={reason}"
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
                Domain = resolvedDomain,
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

        // --write: GitHub mutation first (close, then labels), then the
        // durable-state mutation (queue-state, then runs.jsonl). Each stage
        // is isolated in its own try/catch so a mid-sequence failure yields
        // an accurate, actionable recovery hint instead of a blanket "no
        // durable state changed" claim that would be false once the issue
        // is already closed. Every stage is safe to re-run: closing is
        // skipped once the issue is already CLOSED (checked above via the
        // snapshot), label removal only targets labels still present, and
        // the queue-state upsert is idempotent by construction.
        if (issueIsOpen)
        {
            try
            {
                retirementMutator.CloseAsNotPlanned(repo!, issue!.Value, reasonComment);
            }
            catch (Exception exception) when (exception is IOException or InvalidOperationException)
            {
                writer.WriteLine(
                    $"failed to close issue #{issue} in {repo}: {exception.Message}. No durable state was "
                    + "changed — re-run with --write to retry.");
                return 1;
            }
        }

        if (labelsToRemove.Length > 0)
        {
            try
            {
                var labelMutator = LabelMutatorFactory?.Invoke() ?? new GhCliGitHubLabelMutator();
                labelMutator.ApplyLabelTransitions(
                    repo!, GhCliGitHubLabelMutator.Kinds.Issue, issue!.Value,
                    Array.Empty<string>(), labelsToRemove);
            }
            catch (Exception exception) when (exception is IOException or InvalidOperationException)
            {
                writer.WriteLine(
                    $"issue #{issue} in {repo} is closed (reason: not planned), but label removal failed: "
                    + $"{exception.Message}. Re-run `automation issue-retire --write` — the issue is already "
                    + "closed, so the retry will resume from label removal without re-closing it.");
                return 1;
            }
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
                    Title = snapshot.Title,
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
                    LinkedIssue = new LinkedIssue { Repo = repo!, Number = issue, Url = snapshot.Url },
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
            // G548: guarded write. Retire never REMOVES an item (it rewrites
            // it as state=retired), so no removal is expected here — any item
            // that would disappear is unrequested loss.
            QueueStatePersistence.Persist(
                queueStatePath,
                queueState ?? new QueueState { SchemaVersion = "1", UpdatedAt = now, Items = Array.Empty<QueueItem>() },
                updatedState);
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or JsonException or UnauthorizedAccessException)
        {
            writer.WriteLine(
                $"issue #{issue} in {repo} is closed and relabeled, but the queue-state update failed: "
                + $"{exception.Message}. Re-run `automation issue-retire --write` (idempotent) to retry the "
                + "queue-state and runs.jsonl update.");
            return 1;
        }

        try
        {
            AppendRetireRunEvent(context, executionUnit, repo!, issue!.Value, retirementReason, now);
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or JsonException or UnauthorizedAccessException)
        {
            writer.WriteLine(
                $"issue #{issue} in {repo} is closed, relabeled, and queue-state now shows it retired, but "
                + $"appending the runs.jsonl event failed: {exception.Message}. Re-run `automation issue-retire "
                + "--write` (idempotent) — it will detect the queue-state is already retired and finish only "
                + "the missing runs.jsonl event.");
            return 1;
        }

        var applied = new AutomationIssueRetireResult
        {
            Repo = repo!,
            Issue = issue!.Value,
            Reason = reason!,
            Note = note,
            Domain = resolvedDomain,
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

    private static bool RunsLogHasRetireEvent(CliContext context, string executionUnit)
    {
        var runsPath = context.GetRunLogPath();
        if (!File.Exists(runsPath))
        {
            return false;
        }

        try
        {
            var events = RunLogSerializer.DeserializeAll(File.ReadAllText(runsPath));
            return events.Any(runEvent =>
                string.Equals(runEvent.Event, RetireRunEventName, StringComparison.Ordinal)
                && string.Equals(runEvent.ExecutionUnit, executionUnit, StringComparison.Ordinal));
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException)
        {
            return false;
        }
    }

    /// <summary>
    /// G522 repair: exact <c>(repo, issue number)</c> match — a same-numbered
    /// issue in a different repo (a multi-repo host queue-state.json) must
    /// never match this execution unit.
    /// </summary>
    private static bool MatchesLinkedIssue(LinkedIssue? linkedIssue, string repo, int issueNumber) =>
        linkedIssue is { Number: { } number } linked
        && number == issueNumber
        && string.Equals(linked.Repo, repo, StringComparison.OrdinalIgnoreCase);

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

    /// <summary>
    /// Derives a CANDIDATE execution unit from the issue title. Used ONLY to
    /// locate <c>.intent-cli/issues/&lt;unit&gt;/packet.yaml</c> for domain
    /// confirmation via <see cref="PacketDomainResolution"/> — never trusted
    /// on its own to authorize queue creation (see the domain resolution
    /// call in <see cref="Execute"/>).
    /// </summary>
    private static string ExecutionUnitFromTitle(string title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return string.Empty;
        }
        var colonIndex = title.IndexOf(':');
        return (colonIndex > 0 ? title[..colonIndex] : title).Trim();
    }

    private static string? ReadPacketDeclaredDomain(CliContext context, string executionUnit)
    {
        if (string.IsNullOrWhiteSpace(executionUnit))
        {
            return null;
        }
        var packetYamlPath = Path.Combine(context.RepoRoot, ".intent-cli", "issues", executionUnit, "packet.yaml");
        if (!File.Exists(packetYamlPath))
        {
            return null;
        }
        try
        {
            PreparedPacketYamlScalarParser.Parse(File.ReadAllText(packetYamlPath)).TryGetValue("domain", out var declaredDomain);
            return declaredDomain;
        }
        catch (FormatException)
        {
            return null;
        }
    }

    // Repair: the marker literals below are the SINGLE source of truth for
    // both writing (BuildReasonComment) and reading (the compiled regex)
    // the canonical provenance record, so the two can never drift apart. A
    // naive substring `Contains` check on the prefix alone would accept a
    // comment merely quoting the marker, a differently-reasoned marker, or
    // an unrelated unit whose name happens to share a prefix — the regex is
    // anchored to the WHOLE trimmed comment body and captures both the exact
    // unit and reason for an equality check, closing all three gaps.
    private const string RetireMarkerPrefixLiteral = "[issue-retire:";
    private const string RetireMarkerMiddleLiteral = "] Retired via canonical `automation issue-retire` transition — reason: ";

    private static readonly Regex CanonicalRetireMarkerPattern = new(
        "^" + Regex.Escape(RetireMarkerPrefixLiteral) + "(?<unit>[^\\]]+)" + Regex.Escape(RetireMarkerMiddleLiteral)
        + "(?<reason>" + string.Join("|", AllowedReasons.Select(Regex.Escape)) + @")\.(?: Note: .*)?$",
        RegexOptions.Singleline);

    private static string BuildReasonComment(string executionUnit, string reason, string? note)
    {
        var comment = $"{RetireMarkerPrefixLiteral}{executionUnit}{RetireMarkerMiddleLiteral}{reason}.";
        if (!string.IsNullOrWhiteSpace(note))
        {
            comment += $" Note: {note}";
        }
        return comment;
    }

    /// <summary>
    /// Repair: durable provenance that THIS command (not a manual/unrelated
    /// closure, and not merely a comment quoting the marker) closed the
    /// issue as "not planned" for THIS candidate execution unit AND THIS
    /// requested reason — required before a CLOSED issue is treated as a
    /// partial-failure recovery target, since <c>stateReason == NOT_PLANNED</c>
    /// alone is not sufficient authentication.
    /// </summary>
    private static bool HasCanonicalRetireMarker(IReadOnlyList<string> comments, string executionUnit, string reason)
    {
        if (string.IsNullOrWhiteSpace(executionUnit))
        {
            return false;
        }
        return comments.Any(body =>
        {
            if (string.IsNullOrEmpty(body))
            {
                return false;
            }
            var match = CanonicalRetireMarkerPattern.Match(body.Trim());
            return match.Success
                && string.Equals(match.Groups["unit"].Value, executionUnit, StringComparison.Ordinal)
                && string.Equals(match.Groups["reason"].Value, reason, StringComparison.Ordinal);
        });
    }

    private static bool TryParseArguments(
        string[] args,
        out string? repo,
        out int? issue,
        out string? reason,
        out string? note,
        out string? domain,
        out bool write,
        out string format,
        out string error)
    {
        repo = null;
        issue = null;
        reason = null;
        note = null;
        domain = null;
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
                case "--domain":
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                    {
                        error = "--domain requires a value.";
                        return false;
                    }
                    domain = args[++index].Trim();
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
            if (!string.IsNullOrWhiteSpace(result.Domain))
            {
                writer.WriteLine($"- domain: {result.Domain}");
            }
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
/// a comment, and for reading a single issue's CURRENT state regardless of
/// open/closed — the production implementation shells out to
/// <c>gh issue close --reason "not planned" --comment &lt;text&gt;</c> /
/// <c>gh issue view --json state,stateReason,title,url,labels,comments</c>.
/// </summary>
internal interface IGitHubIssueRetirementMutator
{
    void CloseAsNotPlanned(string repo, int issueNumber, string comment);

    /// <summary>
    /// Repair: fetches the issue regardless of open/closed state, so a
    /// retry after a partial <c>--write</c> failure (issue already closed)
    /// can still locate it instead of dead-ending on an OPEN-only scan.
    /// </summary>
    IssueSnapshot GetSnapshot(string repo, int issueNumber);
}

/// <summary>
/// G525 repair: point-in-time snapshot of a single GitHub issue, used to
/// resolve the retire target and to authenticate a same-command
/// partial-failure recovery — <see cref="StateReason"/> == <c>NOT_PLANNED</c>
/// on a CLOSED issue is a necessary but NOT sufficient signal (a manual or
/// unrelated "not planned" closure would also match it); <see cref="Comments"/>
/// is required in addition to confirm the canonical retire marker this
/// command itself posts (see <c>HasCanonicalRetireMarker</c>).
/// </summary>
internal sealed record IssueSnapshot
{
    public required string State { get; init; }

    public required string StateReason { get; init; }

    public required string Title { get; init; }

    public required string Url { get; init; }

    public required IReadOnlyList<string> Labels { get; init; }

    public required IReadOnlyList<string> Comments { get; init; }
}

/// <summary>
/// G525: default retirement mutator backed by <c>gh issue close</c> /
/// <c>gh issue view</c>.
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
        RunGh(args, $"close issue #{issueNumber} in {repo}");
    }

    public IssueSnapshot GetSnapshot(string repo, int issueNumber)
    {
        var args = new List<string>
        {
            "issue", "view",
            issueNumber.ToString(CultureInfo.InvariantCulture),
            "--repo", repo,
            "--json", "state,stateReason,title,url,labels,comments",
        };
        var stdout = RunGh(args, $"view issue #{issueNumber} in {repo}");
        return ParseSnapshot(stdout);
    }

    private static IssueSnapshot ParseSnapshot(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        var labels = new List<string>();
        if (root.TryGetProperty("labels", out var labelsElement) && labelsElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var label in labelsElement.EnumerateArray())
            {
                if (label.TryGetProperty("name", out var nameElement) && nameElement.ValueKind == JsonValueKind.String)
                {
                    labels.Add(nameElement.GetString() ?? string.Empty);
                }
            }
        }

        var comments = new List<string>();
        if (root.TryGetProperty("comments", out var commentsElement) && commentsElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var comment in commentsElement.EnumerateArray())
            {
                if (comment.TryGetProperty("body", out var bodyElement) && bodyElement.ValueKind == JsonValueKind.String)
                {
                    comments.Add(bodyElement.GetString() ?? string.Empty);
                }
            }
        }

        return new IssueSnapshot
        {
            State = root.TryGetProperty("state", out var stateElement) && stateElement.ValueKind == JsonValueKind.String
                ? stateElement.GetString() ?? string.Empty
                : string.Empty,
            StateReason = root.TryGetProperty("stateReason", out var stateReasonElement) && stateReasonElement.ValueKind == JsonValueKind.String
                ? stateReasonElement.GetString() ?? string.Empty
                : string.Empty,
            Title = root.TryGetProperty("title", out var titleElement) && titleElement.ValueKind == JsonValueKind.String
                ? titleElement.GetString() ?? string.Empty
                : string.Empty,
            Url = root.TryGetProperty("url", out var urlElement) && urlElement.ValueKind == JsonValueKind.String
                ? urlElement.GetString() ?? string.Empty
                : string.Empty,
            Labels = labels,
            Comments = comments,
        };
    }

    private static string RunGh(IReadOnlyList<string> arguments, string description)
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

        return stdout;
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

    /// <summary>
    /// G522 repair: the resolved domain (explicit <c>--domain</c> or
    /// packet-declared) this retirement was authorized under. Null for the
    /// already-retired no-op paths, which never re-resolve it.
    /// </summary>
    [JsonPropertyName("domain")]
    public string? Domain { get; init; }

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
