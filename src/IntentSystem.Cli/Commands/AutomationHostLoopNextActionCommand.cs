using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G308: <c>intent-cli automation host-loop-next-action --repo
/// &lt;owner/repo&gt; [--format markdown|json]</c> — read-only
/// aggregator that captures the highest-priority host loop signal from
/// the candidate lister (review PR / WIP cap / lease) plus optional
/// preflight inputs, calls
/// <see cref="HostLoopNextActionAnalyzer"/>, and emits ONE primary
/// recommended action with a deterministic next command and structured
/// evidence.
///
/// Tests inject a fake
/// <see cref="IGitHubAutomationCandidateLister"/> through
/// <see cref="CandidateListerFactory"/>; preflight inputs (host-sync,
/// next-slice, publish-recovery / publish-lifecycle) are accepted on
/// the command line as flags so the wrapper stays cheap and
/// composable. The host loop guidance can pre-run the specialized
/// commands and pipe their results into this aggregator.
/// </summary>
internal static class AutomationHostLoopNextActionCommand
{
    private const string FormatJson = "json";
    private const string FormatMarkdown = "markdown";

    public static Func<IGitHubAutomationCandidateLister>? CandidateListerFactory { get; set; }

    /// <summary>
    /// G318: testability seam for the automatic <c>intent next-slice --dry-run</c>
    /// probe. Production uses <see cref="IntentCliNextSliceDryRunProbe"/>
    /// (in-process invocation of <see cref="IntentNextSliceCommand"/>) so
    /// the host loop never reports <c>true-idle</c> when a candidate is
    /// ready to publish. Tests inject a fake that returns canned outcomes
    /// without touching the live queue-state.
    /// </summary>
    public static Func<CliContext, INextSliceDryRunProbe>? NextSliceDryRunProbeFactory { get; set; }

    /// <summary>
    /// G342: testability seam for the publish-recovery auto-probe.
    /// Production uses <see cref="IntentCliPublishRecoveryProbe"/>;
    /// tests inject a fake to model recoverable / unsafe-stop /
    /// no-repairs outcomes without touching live queue-state.
    /// </summary>
    public static Func<CliContext, IPublishRecoveryProbe>? PublishRecoveryProbeFactory { get; set; }

    /// <summary>
    /// G342: testability seam for the host-sync-preflight auto-probe.
    /// Production uses <see cref="IntentCliHostSyncPreflightProbe"/>;
    /// tests inject a fake to model clean / behind / dirty-*
    /// classifications without touching the local git working tree.
    /// </summary>
    public static Func<CliContext, IHostSyncPreflightProbe>? HostSyncPreflightProbeFactory { get; set; }

    /// <summary>
    /// G358: testability seam for the closeout-drift-check auto-probe.
    /// Production uses <see cref="IntentCliCloseoutDriftCheckProbe"/> which
    /// runs closeout-drift-check in dry-run mode; tests inject a fake to
    /// model repairable / no-repairs outcomes without touching live
    /// queue-state or network.
    /// </summary>
    public static Func<CliContext, ICloseoutDriftCheckProbe>? CloseoutDriftCheckProbeFactory { get; set; }

    /// <summary>
    /// G365: testability seam for the host-binding domain resolver.
    /// Production uses <see cref="HostBindingDomainResolver.Resolve"/>;
    /// tests inject a fake to model Match / Mismatch / Missing
    /// outcomes deterministically without writing a real
    /// <c>.intent-cli/host-binding.toml</c> to the workspace.
    /// </summary>
    public static Func<CliContext, string, HostBindingDomainResolution>? HostBindingDomainResolverDelegate { get; set; }

    /// <summary>
    /// G365: classification emitted when the operator did not supply
    /// <c>--domain</c> and the host-binding lookup for the requested
    /// <c>--repo</c> records a different <c>target_repo</c> (no safe
    /// domain inference is possible). Surfacing this classification
    /// instead of silently falling back to the host's configured
    /// domain prevents the next-slice probe from probing the wrong
    /// domain and reporting a misleading <c>design-needed</c> /
    /// <c>true-idle</c> outcome.
    /// </summary>
    public const string ClassificationMissingDomainBinding = "missing-domain-binding";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never
    };

    public static int Execute(CliContext context, string[] args, TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(writer);

        if (!TryParseArguments(args, out var parsed, out var error))
        {
            writer.WriteLine(error);
            return 1;
        }

        // G341 / G365: domain resolution priority when --domain is
        // omitted:
        //
        //   1. `.intent-cli/host-binding.toml` (G365) — the canonical
        //      domain-for-this-target-repo mapping. The host's binding
        //      records (host_repo, target_repo, domain); when the
        //      caller's `--repo` matches the binding's `target_repo`
        //      the binding's `domain` is authoritative. This stops a
        //      host whose own config records domain X from probing
        //      next-slice against target_repo Y with domain X.
        //   2. `context.Config.Project.Domain` (G341 legacy) — used
        //      when no binding file is present.
        //
        // The Mismatch outcome (binding present but its `target_repo`
        // names a different repo) short-circuits to a structured
        // `missing-domain-binding` emission below the lister, so the
        // host loop surfaces the gap instead of silently using the
        // wrong domain.
        var domainBinding = HostBindingDomainResolution.Missing(null);
        if (string.IsNullOrWhiteSpace(parsed.Domain))
        {
            var resolver = HostBindingDomainResolverDelegate
                ?? HostBindingDomainResolver.Resolve;
            domainBinding = resolver(context, parsed.Repo);
            if (domainBinding.Kind == HostBindingDomainResolutionKind.Match
                && !string.IsNullOrWhiteSpace(domainBinding.Domain))
            {
                parsed = parsed with { Domain = domainBinding.Domain };
            }
            else if (domainBinding.Kind == HostBindingDomainResolutionKind.Missing)
            {
                var configuredDomain = context.Config?.Project?.Domain;
                if (!string.IsNullOrWhiteSpace(configuredDomain))
                {
                    parsed = parsed with { Domain = configuredDomain.Trim() };
                }
            }
            // Mismatch: deliberately do NOT fall through to the
            // configured domain. The structured missing-domain-binding
            // emission below explains why.
        }

        if (domainBinding.Kind == HostBindingDomainResolutionKind.Mismatch
            && string.IsNullOrWhiteSpace(parsed.Domain))
        {
            EmitMissingDomainBinding(writer, parsed.Repo, domainBinding, parsed.Format);
            return 0;
        }

        IReadOnlyList<GitHubAutomationPrCandidate> openPrs;
        IReadOnlyList<GitHubAutomationIssueCandidate> openIssues;
        try
        {
            var lister = CandidateListerFactory?.Invoke() ?? new GhCliGitHubAutomationCandidateLister();
            openPrs = lister.ListPullRequests(parsed.Repo, requiredLabels: Array.Empty<string>());
            openIssues = lister.ListIssues(parsed.Repo, requiredLabels: Array.Empty<string>());
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException)
        {
            writer.WriteLine($"failed to list automation candidates for {parsed.Repo}: {exception.Message}");
            return 1;
        }

        var actionableReviewPr = FindActionableReviewPr(openPrs);
        var approvedPr = FindApprovedPrPendingContinuation(
            openPrs,
            parsed.ApprovedPrMergeStateStatus,
            parsed.ApprovedPrMetadataBlocked);
        var openIntentTargetExists = OpenIntentTargetExists(openPrs, openIssues);
        var anyLeaseHeld = AnyChildWorkerLeaseHeld(openPrs, openIssues);

        // G318: automatically probe `intent next-slice --dry-run` when the
        // operator did not pre-pipe the flag. The host loop must NOT report
        // `true-idle` while a matching next-slice candidate is ready to
        // publish OR a blocked next-slice outcome (clarification-required,
        // skip-next-slice-due-to-wip) is pending, so the command takes
        // ownership of the probe instead of relying on the caller to wire
        // it. An operator-supplied `--next-slice-issue-cut-ready` (with
        // `--publish-next-execution-unit`) still wins so existing pre-pipe
        // flows are byte-identical.
        var nextSliceIssueCutReady = parsed.NextSliceIssueCutReady;
        var publishNextSliceExecutionUnit = parsed.PublishNextSliceExecutionUnit;
        var hardClarificationOpen = parsed.HardClarificationOpen;
        var openIntentTargetExistsResolved = openIntentTargetExists;
        var designNeeded = false; // G328
        if (!nextSliceIssueCutReady && !string.IsNullOrWhiteSpace(parsed.Domain))
        {
            var probe = NextSliceDryRunProbeFactory?.Invoke(context)
                ?? new IntentCliNextSliceDryRunProbe(context);
            var probed = probe.Probe(parsed.Repo, parsed.Domain!);
            if (probed != null)
            {
                switch (probed.RecommendedOutcome)
                {
                    case "issue-cut-ready":
                        // Happy path: surface the publish-next-issue lane.
                        if (!string.IsNullOrWhiteSpace(probed.ExecutionUnit))
                        {
                            nextSliceIssueCutReady = true;
                            publishNextSliceExecutionUnit = probed.ExecutionUnit;
                        }
                        break;

                    case "design-needed":
                        // G328: probe reports no prepared packet and
                        // runtime creation is not permitted. Route the
                        // analyzer to the `design-needed` lane so the
                        // host loop never falls through to `true-idle`
                        // while a design-side packet draft is the
                        // actual next move.
                        designNeeded = true;
                        break;

                    case "clarification-required":
                        // G318 review fix: a blocked next-slice with an
                        // open hard-clarification must NOT fall through to
                        // true-idle. Force the analyzer's
                        // `hard-clarification` lane.
                        hardClarificationOpen = true;
                        break;

                    case "skip-next-slice-due-to-wip":
                        // G318 review fix: queue-state authoritatively
                        // reports a WIP-cap block. Surface the existing
                        // `wip-cap-blocked` classification vocabulary even
                        // when the GitHub label listing happens to look
                        // empty (queue-state and label state can diverge
                        // transiently). The analyzer's wip-cap-blocked
                        // lane requires `!AnyChildWorkerLeaseHeld`, which
                        // matches the next-slice "skip" semantic (the
                        // skip is about new publication, not about a
                        // child worker holding a lease).
                        openIntentTargetExistsResolved = true;
                        break;

                    case "no-actionable-item":
                    default:
                        // Truly idle from next-slice's perspective; let
                        // the analyzer fall through to existing lanes
                        // (review-pr / wip-cap-blocked / wait-for-child /
                        // true-idle).
                        break;
                }
            }
        }

        // G342: auto-probe `automation publish-recovery --dry-run` when
        // the operator did not pre-supply `--publish-recovery-repairs`.
        // An `safe_repairs.Count > 0` outcome surfaces the existing
        // `repair-host-metadata` analyzer lane (mutationAllowed: true,
        // recommends `automation publish-recovery --write`); without
        // this probe the lane would only fire when the operator typed
        // the count, and recoverable `linked_pr` blockers would fall
        // through to true-idle.
        // The unsafe_stop_count gate is critical: publish-recovery --write
        // refuses all mutations when any unsafe stop is present, so
        // recommending it in a mixed (safe+unsafe) state would be a no-op
        // and confuse the operator. Gate on UnsafeStopCount == 0 to ensure
        // the lane is only surfaced when the repair is actually writeable.
        var publishRecoveryRepairsAvailable = parsed.PublishRecoveryRepairsAvailable;
        if (publishRecoveryRepairsAvailable == 0)
        {
            var recoveryProbe = PublishRecoveryProbeFactory?.Invoke(context)
                ?? new IntentCliPublishRecoveryProbe(context);
            var recoveryProbed = recoveryProbe.Probe(parsed.Repo);
            if (recoveryProbed != null && recoveryProbed.SafeRepairCount > 0 && recoveryProbed.UnsafeStopCount == 0)
            {
                publishRecoveryRepairsAvailable = recoveryProbed.SafeRepairCount;
            }
        }

        // G342: auto-probe `automation host-sync-preflight` when the
        // operator did not pre-supply `--sync-classification` /
        // `--safe-stash-required`. `dirty-unrelated-submodule` flips
        // to the `safe-stash` analyzer lane (recommends
        // `workspace-guard --mode begin --write`);
        // `dirty-host-durable-state` and `dirty-mixed` flip to the
        // `dirty-host-state` block-and-surface lane so the host loop
        // never silently publishes on top of dirty durable state.
        var syncClassification = parsed.SyncClassification;
        var safeStashRequired = parsed.SafeStashRequired;
        if (string.IsNullOrWhiteSpace(syncClassification) && !safeStashRequired)
        {
            var syncProbe = HostSyncPreflightProbeFactory?.Invoke(context)
                ?? new IntentCliHostSyncPreflightProbe(context);
            var syncProbed = syncProbe.Probe();
            if (syncProbed != null
                && !string.Equals(syncProbed.Classification, HostSyncPreflightAnalyzer.ClassificationClean, StringComparison.Ordinal))
            {
                syncClassification = syncProbed.Classification;
                if (string.Equals(syncProbed.Classification, HostSyncPreflightAnalyzer.ClassificationDirtyUnrelatedSubmodule, StringComparison.Ordinal))
                {
                    safeStashRequired = true;
                }
            }
        }

        // G358: auto-probe `automation closeout-drift-check --dry-run` to detect
        // items with linked_issue but no linked_pr where GitHub confirms a single
        // merged closing PR. When safe_repair_count > 0 AND unsafe_stop_count == 0
        // the analyzer surfaces `repair-host-metadata` with the
        // `closeout-drift-check --write` recommendation, so the host loop never
        // falls through to true-idle while an infer-linked_pr repair is available.
        // The unsafe_stop_count gate is critical: closeout-drift-check --write
        // refuses all mutations when any unsafe stop is present (ambiguous closing
        // PRs), so recommending it in that mixed state would be a no-op and confuse
        // the operator.
        var closeoutDriftRepairsAvailable = 0;
        {
            var driftProbe = CloseoutDriftCheckProbeFactory?.Invoke(context)
                ?? new IntentCliCloseoutDriftCheckProbe(context);
            var driftProbed = driftProbe.Probe(parsed.Repo);
            if (driftProbed != null && driftProbed.SafeRepairCount > 0 && driftProbed.UnsafeStopCount == 0)
            {
                closeoutDriftRepairsAvailable = driftProbed.SafeRepairCount;
            }
        }

        var input = new HostLoopNextActionInput
        {
            Repo = parsed.Repo,
            Domain = parsed.Domain,
            StaleCli = parsed.StaleCli,
            SyncClassification = syncClassification,
            SafeStashRequired = safeStashRequired,
            ActionableReviewPr = actionableReviewPr,
            ApprovedPrPendingMergeCloseout = approvedPr,
            PublishRecoveryRepairsAvailable = publishRecoveryRepairsAvailable,
            PublishLifecycleDriftCount = parsed.PublishLifecycleDriftCount,
            CloseoutDriftRepairsAvailable = closeoutDriftRepairsAvailable,
            NextSliceIssueCutReady = nextSliceIssueCutReady,
            PublishNextSliceExecutionUnit = publishNextSliceExecutionUnit,
            // G444: when next-slice says issue-cut-ready, cross-check GitHub
            // reality so a stale queue-state cannot drive a duplicate publish.
            NextSliceUnitAlreadyOnGitHub = nextSliceIssueCutReady
                && ExecutionUnitAlreadyOnGitHub(publishNextSliceExecutionUnit, openPrs, openIssues),
            OpenIntentTargetPrOrIssueExists = openIntentTargetExistsResolved,
            AnyChildWorkerLeaseHeld = anyLeaseHeld,
            HardClarificationOpen = hardClarificationOpen,
            DesignNeeded = designNeeded,
            PreparedPacketCommitReadyAvailable = parsed.PreparedPacketCommitReadyAvailable,
            PreparedPacketExecutionUnit = parsed.PreparedPacketExecutionUnit,
        };

        var result = HostLoopNextActionAnalyzer.Analyze(input);

        // G364: surface the next-slice candidate execution unit as a
        // top-level structured field whenever it is relevant to the
        // emitted action. Today the unit appears inside the recommended
        // command and the evidence; promoting it to its own field lets
        // downstream consumers (host loop wake scripts, dashboards) read
        // it without parsing the recommended_command string.
        //
        // The field is populated for two analyzer lanes:
        //   * publish-next-issue (G275/G318): next-slice surfaced an
        //     `issue-cut-ready` candidate ready to publish.
        //   * prepared-packet-commit-ready (G361): durable-state preflight
        //     surfaced a prepared packet directory whose execution unit
        //     should be committed before the next host action.
        // For all other lanes the field is null so consumers can detect
        // "no specific candidate selected" deterministically.
        var candidateExecutionUnit =
            string.Equals(result.Classification, HostLoopNextActionAnalyzer.ClassificationPublishNextIssue, StringComparison.Ordinal)
                ? input.PublishNextSliceExecutionUnit
                : string.Equals(result.Classification, HostLoopNextActionAnalyzer.ClassificationPreparedPacketCommitReady, StringComparison.Ordinal)
                    ? input.PreparedPacketExecutionUnit
                    : null;

        var emitted = new HostLoopNextActionEmittedResult
        {
            Repo = parsed.Repo,
            Domain = parsed.Domain,
            Classification = result.Classification,
            MutationAllowed = result.MutationAllowed,
            RecommendedCommand = result.RecommendedCommand,
            CandidateExecutionUnit = candidateExecutionUnit,
            Evidence = result.Evidence,
            Summary = result.Summary
        };

        if (string.Equals(parsed.Format, FormatJson, StringComparison.Ordinal))
        {
            writer.Write(JsonSerializer.Serialize(emitted, JsonOptions));
            writer.WriteLine();
        }
        else
        {
            WriteMarkdown(writer, emitted);
        }
        return 0;
    }

    /// <summary>
    /// G365: structured emission for the
    /// <see cref="ClassificationMissingDomainBinding"/> lane. Fires when
    /// the operator omitted <c>--domain</c> and the host-binding
    /// resolution returned <see cref="HostBindingDomainResolutionKind.Mismatch"/>:
    /// a binding exists but its <c>target_repo</c> records a different
    /// repo than the <c>--repo</c> argument, so neither the binding's
    /// nor the configured domain can be safely used for the next-slice
    /// probe. The recommended command points the operator at the
    /// canonical fix: pass <c>--domain</c> explicitly or align the
    /// host-binding's <c>target_repo</c>.
    /// </summary>
    private static void EmitMissingDomainBinding(
        TextWriter writer,
        string repo,
        HostBindingDomainResolution binding,
        string format)
    {
        var bindingPath = binding.BindingPath ?? "(unresolved)";
        var boundRepo = binding.BoundTargetRepo ?? "(none)";
        var boundDomain = binding.Domain ?? "(none)";
        var emitted = new HostLoopNextActionEmittedResult
        {
            Repo = repo,
            Domain = null,
            Classification = ClassificationMissingDomainBinding,
            MutationAllowed = false,
            RecommendedCommand =
                $"intent-cli automation host-loop-next-action --repo {repo} --domain <DOMAIN> --format json",
            CandidateExecutionUnit = null,
            Evidence = new[]
            {
                $"`{bindingPath}` records target_repo=`{boundRepo}` / domain=`{boundDomain}`, "
                + $"which does not match --repo=`{repo}`. Cannot infer the active domain safely.",
                "Either pass --domain explicitly or update host-binding.toml's `target_repo` to match this repo."
            },
            Summary =
                $"Missing domain binding for `{repo}` — host-binding.toml maps a different target_repo; "
                + "pass --domain or fix the binding before re-running the host loop."
        };
        if (string.Equals(format, FormatJson, StringComparison.Ordinal))
        {
            writer.Write(JsonSerializer.Serialize(emitted, JsonOptions));
            writer.WriteLine();
        }
        else
        {
            WriteMarkdown(writer, emitted);
        }
    }

    private static ActionableReviewPr? FindActionableReviewPr(IReadOnlyList<GitHubAutomationPrCandidate> openPrs)
    {
        foreach (var pr in openPrs)
        {
            var labels = (pr.Labels ?? Array.Empty<GitHubAutomationLabel>())
                .Select(label => label.Name)
                .ToHashSet(StringComparer.Ordinal);
            var hasIntentTarget = labels.Contains(WorkerNextActionConstants.Labels.IntentTarget);
            var hasBlockingReviewLabel =
                labels.Contains(WorkerNextActionConstants.Labels.IntentPrRequestUpdate)
                || labels.Contains(WorkerNextActionConstants.Labels.IntentPrUpdateInProgress)
                || labels.Contains(WorkerNextActionConstants.Labels.IntentPrApproved);
            if (hasIntentTarget && !hasBlockingReviewLabel)
            {
                return new ActionableReviewPr { Number = pr.Number, Url = pr.Url };
            }
        }
        return null;
    }

    /// <summary>
    /// G319: find an open PR carrying both <c>intent-target</c> and
    /// <c>intent-pr-approved</c> that the host loop must continue
    /// through merge + closeout BEFORE reporting wip-cap-blocked.
    /// Skips PRs that are simultaneously holding a child-worker lease
    /// (<c>intent-pr-update-in-progress</c>) or carrying a fresh
    /// request-update label (<c>intent-pr-request-update</c>) — those
    /// states mean review hasn't reached a stable approval point and
    /// the review-pr / request-update lanes own them.
    /// </summary>
    private static ApprovedPrContinuation? FindApprovedPrPendingContinuation(
        IReadOnlyList<GitHubAutomationPrCandidate> openPrs,
        string? mergeStateStatusOverride,
        bool hostMetadataBlocked)
    {
        foreach (var pr in openPrs)
        {
            var labels = (pr.Labels ?? Array.Empty<GitHubAutomationLabel>())
                .Select(label => label.Name)
                .ToHashSet(StringComparer.Ordinal);
            var hasIntentTarget = labels.Contains(WorkerNextActionConstants.Labels.IntentTarget);
            var hasApproved = labels.Contains(WorkerNextActionConstants.Labels.IntentPrApproved);
            var hasUpdateInProgress = labels.Contains(WorkerNextActionConstants.Labels.IntentPrUpdateInProgress);
            var hasRequestUpdate = labels.Contains(WorkerNextActionConstants.Labels.IntentPrRequestUpdate);
            if (!hasIntentTarget || !hasApproved || hasUpdateInProgress || hasRequestUpdate)
            {
                continue;
            }

            // G289 defensive: skip PRs that aren't actually OPEN (the
            // lister already filters by --state open, but live data may
            // race against a recent merge).
            if (!string.IsNullOrEmpty(pr.State)
                && !string.Equals(pr.State, "OPEN", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            int? linkedIssueNumber = null;
            if (pr.ClosingIssuesReferences is { Count: > 0 })
            {
                var first = pr.ClosingIssuesReferences[0];
                if (first.Number > 0)
                {
                    linkedIssueNumber = first.Number;
                }
            }

            return new ApprovedPrContinuation
            {
                Number = pr.Number,
                Url = pr.Url,
                IsDraft = pr.IsDraft,
                MergeStateStatus = mergeStateStatusOverride,
                HostMetadataBlocked = hostMetadataBlocked,
                LinkedIssueNumber = linkedIssueNumber
            };
        }
        return null;
    }

    private static bool OpenIntentTargetExists(
        IReadOnlyList<GitHubAutomationPrCandidate> openPrs,
        IReadOnlyList<GitHubAutomationIssueCandidate> openIssues)
    {
        bool HasIntentTarget(IReadOnlyList<GitHubAutomationLabel>? labels) =>
            (labels ?? Array.Empty<GitHubAutomationLabel>())
            .Any(label => string.Equals(label.Name, WorkerNextActionConstants.Labels.IntentTarget, StringComparison.Ordinal));
        return openPrs.Any(pr => HasIntentTarget(pr.Labels))
            || openIssues.Any(issue => HasIntentTarget(issue.Labels));
    }

    /// <summary>
    /// G444: deterministic GitHub-reality cross-check for the issue-cut-ready
    /// execution unit. Returns true when an open issue or PR already names the
    /// execution unit as a standalone token in its title (issues/PRs) or body
    /// (PRs) — the signal that the unit is already published and the
    /// queue-state is stale.
    ///
    /// The match is TOKEN-BOUNDARY aware, not a plain substring: the
    /// execution unit must be delimited by a non-identifier character (or
    /// string start/end) on both sides, where identifier characters are
    /// <c>[A-Za-z0-9-]</c>. This prevents a shorter candidate from
    /// false-matching a longer adjacent id — e.g. `G44` must NOT match
    /// `G444 ...` and `SKS-G67` must NOT match `SKS-G670 ...`, which would
    /// otherwise wrongly suppress a legitimate publish. Exact ids such as
    /// `Z4R-G329` still match `Z4R-G329 implement thing`.
    /// </summary>
    private static bool ExecutionUnitAlreadyOnGitHub(
        string? executionUnit,
        IReadOnlyList<GitHubAutomationPrCandidate> openPrs,
        IReadOnlyList<GitHubAutomationIssueCandidate> openIssues)
    {
        if (string.IsNullOrWhiteSpace(executionUnit))
        {
            return false;
        }

        // (?<![A-Za-z0-9-]) <unit> (?![A-Za-z0-9-]) — the unit may itself
        // contain '-', so identifier boundaries (not \b word boundaries)
        // are required to reject adjacent longer ids.
        var pattern = $"(?<![A-Za-z0-9-]){Regex.Escape(executionUnit)}(?![A-Za-z0-9-])";
        var matcher = new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(200));

        bool Names(string? text) => !string.IsNullOrEmpty(text) && matcher.IsMatch(text);

        return openIssues.Any(issue => Names(issue.Title))
            || openPrs.Any(pr => Names(pr.Title) || Names(pr.Body));
    }

    private static bool AnyChildWorkerLeaseHeld(
        IReadOnlyList<GitHubAutomationPrCandidate> openPrs,
        IReadOnlyList<GitHubAutomationIssueCandidate> openIssues)
    {
        bool LabelMatches(IReadOnlyList<GitHubAutomationLabel>? labels, string name) =>
            (labels ?? Array.Empty<GitHubAutomationLabel>())
            .Any(label => string.Equals(label.Name, name, StringComparison.Ordinal));
        var prLease = openPrs.Any(pr =>
            LabelMatches(pr.Labels, WorkerNextActionConstants.Labels.IntentPrUpdateInProgress));
        var issueLease = openIssues.Any(issue =>
            LabelMatches(issue.Labels, WorkerNextActionConstants.Labels.IntentIssueInProgress));
        return prLease || issueLease;
    }

    private static void WriteMarkdown(TextWriter writer, HostLoopNextActionEmittedResult result)
    {
        writer.WriteLine($"# automation host-loop-next-action (G308) — `{result.Repo}`");
        writer.WriteLine();
        writer.WriteLine($"- classification: **{result.Classification}**");
        writer.WriteLine($"- mutation_allowed: {(result.MutationAllowed ? "yes" : "no")}");
        if (!string.IsNullOrEmpty(result.RecommendedCommand))
        {
            writer.WriteLine($"- recommended_command: `{result.RecommendedCommand}`");
        }
        writer.WriteLine();
        writer.WriteLine(result.Summary);
        if (result.Evidence.Count > 0)
        {
            writer.WriteLine();
            writer.WriteLine("## Evidence");
            foreach (var ev in result.Evidence)
            {
                writer.WriteLine($"- {ev}");
            }
        }
    }

    private static bool TryParseArguments(string[] args, out ParsedArgs parsed, out string error)
    {
        parsed = default!;
        error = string.Empty;

        string? repo = null;
        string? domain = null;
        var staleCli = false;
        string? syncClassification = null;
        var safeStashRequired = false;
        var publishRecoveryRepairs = 0;
        var publishLifecycleDrift = 0;
        var nextSliceIssueCutReady = false;
        string? publishNextSliceExecutionUnit = null;
        var hardClarificationOpen = false;
        string? approvedPrMergeStateStatus = null;
        var approvedPrMetadataBlocked = false;
        var preparedPacketCommitReady = false;
        string? preparedPacketExecutionUnit = null;
        var format = FormatMarkdown;

        for (var index = 0; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--repo":
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                    {
                        error = "--repo requires a value (owner/repo)."; return false;
                    }
                    repo = args[++index].Trim();
                    break;
                case "--domain":
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                    {
                        error = "--domain requires a value."; return false;
                    }
                    domain = args[++index].Trim();
                    break;
                case "--stale-cli":
                    staleCli = true;
                    break;
                case "--sync-classification":
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                    {
                        error = "--sync-classification requires a value."; return false;
                    }
                    syncClassification = args[++index].Trim();
                    break;
                case "--safe-stash-required":
                    safeStashRequired = true;
                    break;
                case "--publish-recovery-repairs":
                    if (index + 1 >= args.Length || !int.TryParse(args[index + 1], out var prr) || prr < 0)
                    {
                        error = "--publish-recovery-repairs requires a non-negative integer."; return false;
                    }
                    publishRecoveryRepairs = prr; index++;
                    break;
                case "--publish-lifecycle-drift":
                    if (index + 1 >= args.Length || !int.TryParse(args[index + 1], out var pld) || pld < 0)
                    {
                        error = "--publish-lifecycle-drift requires a non-negative integer."; return false;
                    }
                    publishLifecycleDrift = pld; index++;
                    break;
                case "--next-slice-issue-cut-ready":
                    nextSliceIssueCutReady = true;
                    break;
                case "--publish-next-execution-unit":
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                    {
                        error = "--publish-next-execution-unit requires a value."; return false;
                    }
                    publishNextSliceExecutionUnit = args[++index].Trim();
                    break;
                case "--hard-clarification-open":
                    hardClarificationOpen = true;
                    break;
                case "--approved-pr-merge-state":
                    // G319: optional override of `gh pr view --json
                    // mergeStateStatus` for the approved PR continuation
                    // lane. Pass `CLEAN` (or omit entirely) for the happy
                    // path; `BLOCKED` / `CONFLICTING` / `DIRTY` / `BEHIND`
                    // / `UNKNOWN` map to `approved-pr-merge-blocked`.
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                    {
                        error = "--approved-pr-merge-state requires a value (e.g. CLEAN, BLOCKED, CONFLICTING)."; return false;
                    }
                    approvedPrMergeStateStatus = args[++index].Trim();
                    break;
                case "--approved-pr-metadata-blocked":
                    // G319: operator/preflight has determined the parent
                    // host metadata (queue-state linked_pr / linked_issue
                    // / packet directory) is not ready for closeout.
                    approvedPrMetadataBlocked = true;
                    break;
                case "--prepared-packet-commit-ready":
                    // G361: durable-state-preflight reported a complete
                    // prepared packet directory under
                    // `.intent-cli/issues/<unit>/` is safe to commit.
                    // Combined with a dirty sync classification, routes
                    // the analyzer to the prepared-packet-commit-ready
                    // lane.
                    preparedPacketCommitReady = true;
                    break;
                case "--prepared-packet-execution-unit":
                    // G361: execution unit name surfaced by
                    // durable-state-preflight; flows into the
                    // prepared-packet-commit-ready evidence.
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                    {
                        error = "--prepared-packet-execution-unit requires a value."; return false;
                    }
                    preparedPacketExecutionUnit = args[++index].Trim();
                    break;
                case "--format":
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                    {
                        error = "--format requires a value (markdown or json)."; return false;
                    }
                    var requested = args[++index].Trim();
                    if (!string.Equals(requested, FormatMarkdown, StringComparison.Ordinal)
                        && !string.Equals(requested, FormatJson, StringComparison.Ordinal))
                    {
                        error = $"--format must be 'markdown' or 'json' (got '{requested}')."; return false;
                    }
                    format = requested;
                    break;
                default:
                    error = $"Unknown argument '{args[index]}'."; return false;
            }
        }

        if (string.IsNullOrWhiteSpace(repo))
        {
            error = "automation host-loop-next-action requires '--repo <owner/repo>'.";
            return false;
        }

        parsed = new ParsedArgs
        {
            Repo = repo!,
            Domain = domain,
            StaleCli = staleCli,
            SyncClassification = syncClassification,
            SafeStashRequired = safeStashRequired,
            PublishRecoveryRepairsAvailable = publishRecoveryRepairs,
            PublishLifecycleDriftCount = publishLifecycleDrift,
            NextSliceIssueCutReady = nextSliceIssueCutReady,
            PublishNextSliceExecutionUnit = publishNextSliceExecutionUnit,
            HardClarificationOpen = hardClarificationOpen,
            ApprovedPrMergeStateStatus = approvedPrMergeStateStatus,
            ApprovedPrMetadataBlocked = approvedPrMetadataBlocked,
            PreparedPacketCommitReadyAvailable = preparedPacketCommitReady,
            PreparedPacketExecutionUnit = preparedPacketExecutionUnit,
            Format = format
        };
        return true;
    }

    private sealed record ParsedArgs
    {
        public required string Repo { get; init; }
        public string? Domain { get; init; }
        public required bool StaleCli { get; init; }
        public string? SyncClassification { get; init; }
        public required bool SafeStashRequired { get; init; }
        public required int PublishRecoveryRepairsAvailable { get; init; }
        public required int PublishLifecycleDriftCount { get; init; }
        public required bool NextSliceIssueCutReady { get; init; }
        public string? PublishNextSliceExecutionUnit { get; init; }
        public required bool HardClarificationOpen { get; init; }
        public string? ApprovedPrMergeStateStatus { get; init; }
        public required bool ApprovedPrMetadataBlocked { get; init; }
        public required bool PreparedPacketCommitReadyAvailable { get; init; }
        public string? PreparedPacketExecutionUnit { get; init; }
        public required string Format { get; init; }
    }
}

internal sealed record HostLoopNextActionEmittedResult
{
    [JsonPropertyName("repo")] public required string Repo { get; init; }
    [JsonPropertyName("domain")] public string? Domain { get; init; }
    [JsonPropertyName("classification")] public required string Classification { get; init; }
    [JsonPropertyName("mutation_allowed")] public required bool MutationAllowed { get; init; }
    [JsonPropertyName("recommended_command")] public required string? RecommendedCommand { get; init; }

    /// <summary>
    /// G364: when the analyzer chose the <c>publish-next-issue</c> or
    /// <c>prepared-packet-commit-ready</c> lane, the next-slice / prepared
    /// packet execution unit is also surfaced here as a top-level structured
    /// field so consumers can read it directly rather than parsing it out of
    /// <see cref="RecommendedCommand"/>. <c>null</c> for all other
    /// classifications.
    /// </summary>
    [JsonPropertyName("candidate_execution_unit")] public string? CandidateExecutionUnit { get; init; }

    [JsonPropertyName("evidence")] public required IReadOnlyList<string> Evidence { get; init; }
    [JsonPropertyName("summary")] public required string Summary { get; init; }
}
