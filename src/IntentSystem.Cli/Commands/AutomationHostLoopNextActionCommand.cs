using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using IntentSystem.Supervisor.Models;
using IntentSystem.Supervisor.Serialization;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G308: <c>intent-cli automation host-loop-next-action --repo
/// &lt;owner/repo&gt; [--domain &lt;domain&gt;] [--team &lt;team&gt;]
/// [--task-id &lt;task&gt;] [--result-nonce &lt;nonce&gt;]
/// [--routing-root &lt;root&gt;] [--format markdown|json]</c> — read-only
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
    /// G813: source-bound identity capture is injectable so tests can prove
    /// the team/task/nonce envelope without consulting a host store. The
    /// production capture is read-only and only observes cwd and Git facts.
    /// </summary>
    public static Func<CliContext, HostLoopIdentityCapture>? IdentityCaptureFactory { get; set; }

    public const string ClassificationIdentityUnresolved = "identity-unresolved";

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

        // G813: a team-scoped request must establish its immutable identity
        // and restrictive host preflight before remote GitHub enumeration.
        // Legacy callers (which do not provide any identity fields) retain
        // the existing result shape and ordering.
        HostLoopIdentityCapture? identityCapture = null;
        if (parsed.RequiresIdentity)
        {
            try
            {
                identityCapture = IdentityCaptureFactory?.Invoke(context)
                    ?? HostLoopIdentityCapture.Capture(context);
            }
            catch (Exception exception) when (
                exception is IOException
                or InvalidOperationException
                or UnauthorizedAccessException)
            {
                // Identity capture is an observation boundary. A missing or
                // unreadable source fact is reported as unresolved rather
                // than allowing the request to reach GitHub enumeration.
                identityCapture = new HostLoopIdentityCapture
                {
                    Cwd = null,
                    Origin = null,
                    Ref = null,
                    Head = null,
                    DispatchGeneration = null,
                    DispatchDigest = null,
                };
            }
        }
        var preflightSyncClassification = parsed.SyncClassification;
        var preflightSafeStashRequired = parsed.SafeStashRequired;
        if (parsed.RequiresIdentity
            && string.IsNullOrWhiteSpace(preflightSyncClassification)
            && !preflightSafeStashRequired)
        {
            var syncProbe = HostSyncPreflightProbeFactory?.Invoke(context)
                ?? new IntentCliHostSyncPreflightProbe(context);
            var syncProbed = syncProbe.Probe();
            if (syncProbed is not null)
            {
                preflightSyncClassification = syncProbed.Classification;
                if (string.Equals(
                        syncProbed.Classification,
                        HostSyncPreflightAnalyzer.ClassificationDirtyUnrelatedSubmodule,
                        StringComparison.Ordinal))
                {
                    preflightSafeStashRequired = true;
                }
            }
        }

        var identityResolution = parsed.RequiresIdentity
            ? ResolveIdentity(parsed, identityCapture!)
            : null;
        if (parsed.RequiresIdentity
            && IsRestrictiveSyncClassification(preflightSyncClassification))
        {
            var dirtyEvidence = new List<string>
            {
                $"host-sync-preflight classification: {preflightSyncClassification} (caller-supplied or locally observed).",
                "G304/G306 dirty-host-state is a hard stop; remote GitHub enumeration was not attempted."
            };
            if (identityResolution is { Qualified: false })
            {
                dirtyEvidence.AddRange(identityResolution.MissingEvidence);
            }
            EmitIdentityBoundary(
                writer,
                parsed,
                identityCapture,
                identityResolution,
                HostLoopNextActionAnalyzer.ClassificationDirtyHostState,
                dirtyEvidence,
                "Dirty durable host-state present — refusing to mutate or select a candidate (G304/G306).");
            return 0;
        }

        if (parsed.RequiresIdentity && identityResolution is { Qualified: false })
        {
            EmitIdentityBoundary(
                writer,
                parsed,
                identityCapture,
                identityResolution,
                ClassificationIdentityUnresolved,
                identityResolution.MissingEvidence,
                "Team-scoped host-loop identity is unresolved — no candidate or mutation is permitted.");
            return 0;
        }

        IReadOnlyList<GitHubAutomationPrCandidate> openPrs;
        IReadOnlyList<GitHubAutomationIssueCandidate> openIssues;
        try
        {
            var lister = CandidateListerFactory?.Invoke() ?? new GhCliGitHubAutomationCandidateLister();
            openPrs = lister.ListPullRequests(
                parsed.Repo,
                requiredLabels: Array.Empty<string>(),
                surface: GitHubAutomationReadSurface.HostLoopNextAction);
            openIssues = lister.ListIssues(
                parsed.Repo,
                requiredLabels: Array.Empty<string>(),
                surface: GitHubAutomationReadSurface.HostLoopNextAction);
        }
        catch (GitHubApiRequestException exception)
        {
            var degraded = BuildGitHubUnavailableResult(parsed.Repo, parsed.Domain, exception);
            if (parsed.RequiresIdentity)
            {
                degraded = ApplyIdentity(degraded, parsed, identityCapture!, identityResolution!);
            }
            if (string.Equals(parsed.Format, FormatJson, StringComparison.Ordinal))
            {
                writer.Write(JsonSerializer.Serialize(degraded, JsonOptions));
                writer.WriteLine();
            }
            else
            {
                WriteMarkdown(writer, degraded);
            }

            return exception.IsQuotaDegraded ? 0 : 1;
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException)
        {
            writer.WriteLine($"failed to list automation candidates for {parsed.Repo}: {exception.Message}");
            return 1;
        }

        var actionableReviewPr = FindActionableReviewPr(openPrs);
        var approvedPr = FindApprovedPrPendingContinuation(
            context,
            parsed.Repo,
            parsed.Domain,
            openPrs,
            openIssues,
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
        var syncClassification = preflightSyncClassification;
        var safeStashRequired = preflightSafeStashRequired;
        if (!parsed.RequiresIdentity
            && string.IsNullOrWhiteSpace(syncClassification)
            && !safeStashRequired)
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

        if (parsed.RequiresIdentity)
        {
            emitted = ApplyIdentity(emitted, parsed, identityCapture!, identityResolution!);
        }

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

    private static bool IsRestrictiveSyncClassification(string? classification) =>
        string.Equals(classification, "dirty-host-durable-state", StringComparison.Ordinal)
        || string.Equals(classification, "dirty-mixed", StringComparison.Ordinal)
        || string.Equals(classification, "unsafe", StringComparison.Ordinal)
        || string.Equals(classification, "ff-blocked", StringComparison.Ordinal)
        || string.Equals(classification, "diverged", StringComparison.Ordinal);

    private static HostLoopIdentityResolution ResolveIdentity(
        ParsedArgs parsed,
        HostLoopIdentityCapture capture)
    {
        var missing = new List<string>();
        if (string.IsNullOrWhiteSpace(parsed.Team)) missing.Add("team");
        if (string.IsNullOrWhiteSpace(parsed.TaskId)) missing.Add("task_id");
        if (string.IsNullOrWhiteSpace(parsed.ResultNonce)) missing.Add("result_nonce");
        if (string.IsNullOrWhiteSpace(parsed.RoutingRoot)) missing.Add("routing_root");
        if (string.IsNullOrWhiteSpace(capture.Cwd)) missing.Add("captured_cwd");
        if (string.IsNullOrWhiteSpace(capture.Origin)) missing.Add("captured_origin");
        if (string.IsNullOrWhiteSpace(capture.Ref)) missing.Add("captured_ref");
        if (string.IsNullOrWhiteSpace(capture.Head)) missing.Add("captured_head");
        if (string.IsNullOrWhiteSpace(capture.DispatchGeneration)) missing.Add("dispatch_generation");
        if (string.IsNullOrWhiteSpace(capture.DispatchDigest)) missing.Add("dispatch_digest");

        if (missing.Count == 0)
        {
            return new HostLoopIdentityResolution(
                Qualified: true,
                MissingEvidence: Array.Empty<string>(),
                Source: "authoritative-dispatch-identity");
        }

        return new HostLoopIdentityResolution(
            Qualified: false,
            MissingEvidence: new[]
            {
                "identity-unresolved: authoritative team-scoped dispatch identity is incomplete.",
                $"missing_fields: {string.Join(", ", missing)}.",
                "Legacy/no-team host-loop output remains readable but cannot certify modern ownership or mutation."
            },
            Source: "identity-unresolved");
    }

    private static void EmitIdentityBoundary(
        TextWriter writer,
        ParsedArgs parsed,
        HostLoopIdentityCapture? capture,
        HostLoopIdentityResolution? identity,
        string classification,
        IReadOnlyList<string> evidence,
        string summary)
    {
        var result = new HostLoopNextActionEmittedResult
        {
            Repo = parsed.Repo,
            Domain = parsed.Domain,
            Classification = classification,
            MutationAllowed = false,
            RecommendedCommand = null,
            CandidateExecutionUnit = null,
            Evidence = evidence,
            Summary = summary,
        };
        if (parsed.RequiresIdentity && capture is not null && identity is not null)
        {
            result = ApplyIdentity(result, parsed, capture, identity);
        }

        if (string.Equals(parsed.Format, FormatJson, StringComparison.Ordinal))
        {
            writer.Write(JsonSerializer.Serialize(result, JsonOptions));
            writer.WriteLine();
        }
        else
        {
            WriteMarkdown(writer, result);
        }
    }

    private static HostLoopNextActionEmittedResult ApplyIdentity(
        HostLoopNextActionEmittedResult result,
        ParsedArgs parsed,
        HostLoopIdentityCapture capture,
        HostLoopIdentityResolution identity)
    {
        var observedAt = DateTimeOffset.UtcNow;
        DateTimeOffset? expiresAt = parsed.TimeoutSeconds is int timeout
            ? observedAt.AddSeconds(timeout)
            : null;
        return result with
        {
            Operation = "automation host-loop-next-action",
            Version = "1",
            RequestedRepo = parsed.Repo,
            ResolvedRepo = parsed.Repo,
            RequestedDomain = parsed.Domain,
            ResolvedDomain = parsed.Domain,
            RequestedTeam = parsed.Team,
            ResolvedTeam = parsed.Team,
            Team = parsed.Team,
            TaskId = parsed.TaskId,
            ResultNonce = parsed.ResultNonce,
            IdentityQualification = identity.Qualified ? "qualified" : ClassificationIdentityUnresolved,
            IdentitySource = identity.Source,
            CompletionIdentity = identity.Qualified
                ? BuildCompletionIdentity(parsed, capture)
                : null,
            RecipientContext = capture.RecipientContext,
            DispatchGeneration = capture.DispatchGeneration,
            DispatchDigest = capture.DispatchDigest,
            RoutingRoot = parsed.RoutingRoot,
            CapturedCwd = capture.Cwd,
            CapturedOrigin = capture.Origin,
            CapturedRef = capture.Ref,
            CapturedHead = capture.Head,
            ObservedAt = observedAt.ToUniversalTime().ToString("O"),
            ExpiresAt = expiresAt?.ToUniversalTime().ToString("O"),
            TimeoutSeconds = parsed.TimeoutSeconds,
            UpstreamTimeout = false,
            Owner = capture.Owner,
            Action = capture.Action,
            Deadline = capture.Deadline,
            GateEvidence = result.Evidence,
            Provenance = identity.Qualified
                ? new[] { "authoritative-dispatch-identity", "read-only-source-observation" }
                : new[] { "identity-unresolved", "read-only-source-observation" },
            ElapsedMilliseconds = null,
        };
    }

    private static string BuildCompletionIdentity(
        ParsedArgs parsed,
        HostLoopIdentityCapture capture) =>
        string.Join("/", [
            parsed.Repo,
            parsed.Domain ?? string.Empty,
            parsed.Team ?? string.Empty,
            parsed.TaskId ?? string.Empty,
            parsed.ResultNonce ?? string.Empty,
            capture.DispatchGeneration ?? string.Empty,
            capture.DispatchDigest ?? string.Empty,
        ]);

    private sealed record HostLoopIdentityResolution(
        bool Qualified,
        IReadOnlyList<string> MissingEvidence,
        string Source);

    private static HostLoopNextActionEmittedResult BuildGitHubUnavailableResult(
        string repo,
        string? domain,
        GitHubApiRequestException exception) =>
        new()
        {
            Repo = repo,
            Domain = domain,
            Classification = exception.IsQuotaDegraded
                ? GitHubApiQuotaConstants.DetectionUnavailableCause
                : "github-api-error",
            MutationAllowed = false,
            RecommendedCommand = null,
            CandidateExecutionUnit = null,
            Evidence = new[]
            {
                $"cause={exception.Cause}; operation={exception.Operation}; message={exception.Message}",
                exception.DegradedState is { } state
                    ? $"resource={state.Resource}; remaining={state.Remaining?.ToString() ?? "unknown"}; reset_at={state.ResetAt ?? state.Reset?.ToString() ?? "unknown"}"
                    : "No quota state was observed; this is not classified as quota exhaustion.",
            },
            Summary = exception.IsQuotaDegraded
                ? "GitHub-backed host-loop detection is unavailable because a named API quota is exhausted; no host mutation is recommended."
                : $"GitHub-backed host-loop detection failed with non-quota cause '{exception.Cause}'.",
            Degraded = exception.IsQuotaDegraded,
            GithubApiStatus = exception.IsQuotaDegraded
                ? GitHubApiQuotaConstants.Degraded
                : GitHubApiQuotaConstants.Error,
            Cause = exception.Cause,
            DegradedState = exception.DegradedState,
        };

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
        CliContext context,
        string repo,
        string? domain,
        IReadOnlyList<GitHubAutomationPrCandidate> openPrs,
        IReadOnlyList<GitHubAutomationIssueCandidate> openIssues,
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
            GitHubAutomationIssueCandidate? linkedIssue = null;
            if (pr.ClosingIssuesReferences is { Count: > 0 })
            {
                var first = pr.ClosingIssuesReferences[0];
                if (first.Number > 0)
                {
                    linkedIssueNumber = first.Number;
                    linkedIssue = openIssues.FirstOrDefault(issue => issue.Number == first.Number);
                }
            }

            var landing = ResolveApprovedPrLandingAuthority(
                context,
                repo,
                domain,
                pr.Number,
                linkedIssueNumber,
                linkedIssue?.Body);

            return new ApprovedPrContinuation
            {
                Number = pr.Number,
                Url = pr.Url,
                IsDraft = pr.IsDraft,
                MergeStateStatus = mergeStateStatusOverride,
                HostMetadataBlocked = hostMetadataBlocked || landing.UnsafeAmbiguity,
                LinkedIssueNumber = linkedIssueNumber,
                LaneId = landing.LaneId,
                LandingMode = landing.LandingMode,
                LandingAuthorityEvidence = landing.Evidence,
                ChecksGreen = AutomationStalledWorkCommand.HasAllGreenChecks(pr),
            };
        }
        return null;
    }

    /// <summary>
    /// G678 repair: host runtime state resolves landing authority from the
    /// immutable queue/packet snapshot before consulting GitHub. The issue
    /// projection is an explicitly GitHub-only fallback for selectors where
    /// no linked immutable artifact is available. Mutable projection drift
    /// never overrides a snapshot; ambiguity between immutable sources fails
    /// closed through <see cref="ApprovedPrContinuation.HostMetadataBlocked"/>.
    /// </summary>
    private static ApprovedPrLandingResolution ResolveApprovedPrLandingAuthority(
        CliContext context,
        string repo,
        string? domain,
        int prNumber,
        int? linkedIssueNumber,
        string? linkedIssueBody)
    {
        var issueProjection = BranchLaneLandingModes.TryReadIssueProjection(linkedIssueBody);
        if (string.IsNullOrWhiteSpace(domain))
        {
            return ApprovedPrLandingResolution.GitHubFallback(issueProjection);
        }

        var queueLocation = RuntimeScopedStateResolver.ResolveQueueStatePathForRead(
            context.RepoRoot,
            domain,
            repo);
        if (!File.Exists(queueLocation.Path))
        {
            return ApprovedPrLandingResolution.GitHubFallback(issueProjection);
        }

        QueueState queueState;
        try
        {
            queueState = QueueStateSerializer.Deserialize(File.ReadAllText(queueLocation.Path));
        }
        catch (Exception exception) when (exception is IOException or JsonException or InvalidOperationException)
        {
            return ApprovedPrLandingResolution.Blocked(
                $"immutable queue-state could not be read safely: {exception.Message}");
        }

        var linkedCandidates = queueState.Items.Where(item =>
                GitHubWorkItemIdentity.MatchesPullRequest(item, repo, prNumber)
                || (linkedIssueNumber is { } issueNumber
                    && GitHubWorkItemIdentity.MatchesIssue(item.LinkedIssue, repo, issueNumber)))
            .ToArray();

        if (linkedCandidates.Length == 0)
        {
            return ApprovedPrLandingResolution.Blocked(
                $"immutable routing could not be resolved: queue-state has no item linked to PR #{prNumber}/issue #{linkedIssueNumber}");
        }
        if (linkedCandidates.Length > 1)
        {
            return ApprovedPrLandingResolution.Blocked(
                $"immutable routing is ambiguous: {linkedCandidates.Length} queue items match PR #{prNumber}/issue #{linkedIssueNumber}");
        }

        var queueItem = linkedCandidates[0];
        var packet = ReadPacketRoutingSnapshot(context, queueItem);
        if (packet.Error is not null)
        {
            return ApprovedPrLandingResolution.Blocked(packet.Error);
        }

        var queueSnapshot = queueItem.RoutingSnapshot is null
            ? null
            : ToBranchSnapshot(queueItem.RoutingSnapshot);
        if (queueSnapshot is not null
            && packet.Snapshot is not null
            && !SnapshotsMatch(queueSnapshot, packet.Snapshot))
        {
            return ApprovedPrLandingResolution.Blocked(
                $"immutable queue and packet routing snapshots disagree for {queueItem.ExecutionUnit}");
        }

        var immutable = queueSnapshot ?? packet.Snapshot;
        if (immutable is null)
        {
            // Pre-G668 queue items did not declare lane snapshots. Preserve
            // their existing issue-projection/undeclared behavior.
            return ApprovedPrLandingResolution.GitHubFallback(issueProjection);
        }

        var source = queueSnapshot is not null && packet.Snapshot is not null
            ? "immutable queue+packet routing snapshot"
            : queueSnapshot is not null
                ? "immutable queue routing snapshot"
                : "immutable packet routing snapshot";
        var drift = issueProjection is not null
            && (!string.Equals(issueProjection.LaneId, immutable.LaneId, StringComparison.Ordinal)
                || !string.Equals(issueProjection.LandingMode, immutable.LandingMode, StringComparison.Ordinal));
        var evidence = drift
            ? $"{source} is authoritative; mismatched GitHub issue projection was ignored"
            : $"landing authority resolved from {source}";
        return new ApprovedPrLandingResolution(
            immutable.LaneId,
            immutable.LandingMode,
            UnsafeAmbiguity: false,
            evidence);
    }

    private static PacketRoutingRead ReadPacketRoutingSnapshot(CliContext context, QueueItem queueItem)
    {
        var declaredPath = queueItem.PacketPaths.Yaml;
        var path = Path.IsPathRooted(declaredPath)
            ? declaredPath
            : Path.GetFullPath(Path.Combine(context.RepoRoot, declaredPath));
        if (!File.Exists(path))
        {
            return new PacketRoutingRead(null, null);
        }

        try
        {
            if (!PacketYamlDocument.TryParse(File.ReadAllText(path), out var document, out var error)
                || document is null)
            {
                return new PacketRoutingRead(
                    null,
                    $"immutable packet routing could not be parsed for {queueItem.ExecutionUnit}: {error}");
            }

            var snapshot = BranchLaneResolver.TryReadSnapshot(document.Fields);
            var declaredLane = BranchLaneResolver.TryReadDeclaredLane(document.Fields);
            if (!string.IsNullOrWhiteSpace(declaredLane) && snapshot is null)
            {
                return new PacketRoutingRead(
                    null,
                    $"immutable packet for {queueItem.ExecutionUnit} declares a lane without a complete routing snapshot");
            }
            return new PacketRoutingRead(snapshot, null);
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException)
        {
            return new PacketRoutingRead(
                null,
                $"immutable packet routing is unsafe for {queueItem.ExecutionUnit}: {exception.Message}");
        }
    }

    private static BranchRoutingSnapshot ToBranchSnapshot(QueueRoutingSnapshot snapshot) => new()
    {
        LaneId = snapshot.LaneId,
        DefinitionRevision = snapshot.DefinitionRevision,
        StartBranch = snapshot.StartBranch,
        PrBaseBranch = snapshot.PrBaseBranch,
        LandingMode = snapshot.LandingMode,
    };

    private static bool SnapshotsMatch(BranchRoutingSnapshot left, BranchRoutingSnapshot right) =>
        string.Equals(left.LaneId, right.LaneId, StringComparison.Ordinal)
        && string.Equals(left.DefinitionRevision, right.DefinitionRevision, StringComparison.Ordinal)
        && string.Equals(left.StartBranch, right.StartBranch, StringComparison.Ordinal)
        && string.Equals(left.PrBaseBranch, right.PrBaseBranch, StringComparison.Ordinal)
        && string.Equals(left.LandingMode, right.LandingMode, StringComparison.Ordinal);

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
        if (result.Operation is not null)
        {
            writer.WriteLine($"- operation: `{result.Operation}` (v{result.Version})");
            writer.WriteLine($"- team: `{result.Team ?? "(unresolved)"}`");
            writer.WriteLine($"- task_id: `{result.TaskId ?? "(unresolved)"}`");
            writer.WriteLine($"- result_nonce: `{result.ResultNonce ?? "(unresolved)"}`");
            writer.WriteLine($"- identity_qualification: `{result.IdentityQualification ?? "(unresolved)"}`");
            writer.WriteLine($"- completion_identity: `{result.CompletionIdentity ?? "(none)"}`");
            writer.WriteLine($"- recipient_context: `{result.RecipientContext ?? "(none)"}`");
            writer.WriteLine($"- captured_cwd: `{result.CapturedCwd ?? "(unresolved)"}`");
            writer.WriteLine($"- captured_origin: `{result.CapturedOrigin ?? "(unresolved)"}`");
            writer.WriteLine($"- captured_ref: `{result.CapturedRef ?? "(unresolved)"}`");
            writer.WriteLine($"- captured_head: `{result.CapturedHead ?? "(unresolved)"}`");
            if (result.Provenance is { Count: > 0 })
            {
                writer.WriteLine($"- provenance: `{string.Join(", ", result.Provenance)}`");
            }
        }
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
        string? team = null;
        string? taskId = null;
        string? resultNonce = null;
        string? routingRoot = null;
        int? timeoutSeconds = null;
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
                case "--team":
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                    {
                        error = "--team requires a non-empty value."; return false;
                    }
                    team = args[++index].Trim();
                    break;
                case "--task-id":
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                    {
                        error = "--task-id requires a non-empty value."; return false;
                    }
                    taskId = args[++index].Trim();
                    break;
                case "--result-nonce":
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                    {
                        error = "--result-nonce requires a non-empty value."; return false;
                    }
                    resultNonce = args[++index].Trim();
                    break;
                case "--routing-root":
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                    {
                        error = "--routing-root requires a non-empty value."; return false;
                    }
                    routingRoot = args[++index].Trim();
                    break;
                case "--timeout-seconds":
                    if (index + 1 >= args.Length
                        || !int.TryParse(args[index + 1], out var timeout)
                        || timeout <= 0
                        || timeout > 30)
                    {
                        error = "--timeout-seconds requires an integer from 1 through 30."; return false;
                    }
                    timeoutSeconds = timeout;
                    index++;
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
                    error = $"Unknown argument '{args[index]}'. Supported: --repo <owner/repo> [--domain <domain>] [--team <team>] [--task-id <task>] [--result-nonce <nonce>] [--routing-root <root>] [--timeout-seconds <1..30>] [--format markdown|json]."; return false;
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
            Team = team,
            TaskId = taskId,
            ResultNonce = resultNonce,
            RoutingRoot = routingRoot,
            TimeoutSeconds = timeoutSeconds,
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

    private sealed record ApprovedPrLandingResolution(
        string? LaneId,
        string? LandingMode,
        bool UnsafeAmbiguity,
        string? Evidence)
    {
        public static ApprovedPrLandingResolution GitHubFallback(BranchLaneIssueProjection? projection) =>
            new(
                projection?.LaneId,
                projection?.LandingMode,
                UnsafeAmbiguity: false,
                projection is null
                    ? null
                    : "landing authority resolved from GitHub issue projection fallback; no linked immutable snapshot was available");

        public static ApprovedPrLandingResolution Blocked(string evidence) =>
            new(null, null, UnsafeAmbiguity: true, evidence);
    }

    private sealed record PacketRoutingRead(BranchRoutingSnapshot? Snapshot, string? Error);

    private sealed record ParsedArgs
    {
        public required string Repo { get; init; }
        public string? Domain { get; init; }
        public string? Team { get; init; }
        public string? TaskId { get; init; }
        public string? ResultNonce { get; init; }
        public string? RoutingRoot { get; init; }
        public int? TimeoutSeconds { get; init; }
        public bool RequiresIdentity => Team is not null
            || TaskId is not null
            || ResultNonce is not null
            || RoutingRoot is not null
            || TimeoutSeconds is not null;
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
    /// <summary>G813: versioned, read-only identity envelope.</summary>
    [JsonPropertyName("operation")] public string? Operation { get; init; }
    [JsonPropertyName("version")] public string? Version { get; init; }
    [JsonPropertyName("requested_repo")] public string? RequestedRepo { get; init; }
    [JsonPropertyName("resolved_repo")] public string? ResolvedRepo { get; init; }
    [JsonPropertyName("requested_domain")] public string? RequestedDomain { get; init; }
    [JsonPropertyName("resolved_domain")] public string? ResolvedDomain { get; init; }
    [JsonPropertyName("requested_team")] public string? RequestedTeam { get; init; }
    [JsonPropertyName("resolved_team")] public string? ResolvedTeam { get; init; }
    [JsonPropertyName("team")] public string? Team { get; init; }
    [JsonPropertyName("task_id")] public string? TaskId { get; init; }
    [JsonPropertyName("result_nonce")] public string? ResultNonce { get; init; }
    [JsonPropertyName("identity_qualification")] public string? IdentityQualification { get; init; }
    [JsonPropertyName("identity_source")] public string? IdentitySource { get; init; }
    [JsonPropertyName("completion_identity")] public string? CompletionIdentity { get; init; }
    [JsonPropertyName("recipient_context")] public string? RecipientContext { get; init; }
    [JsonPropertyName("dispatch_generation")] public string? DispatchGeneration { get; init; }
    [JsonPropertyName("dispatch_digest")] public string? DispatchDigest { get; init; }
    [JsonPropertyName("routing_root")] public string? RoutingRoot { get; init; }
    [JsonPropertyName("captured_cwd")] public string? CapturedCwd { get; init; }
    [JsonPropertyName("captured_origin")] public string? CapturedOrigin { get; init; }
    [JsonPropertyName("captured_ref")] public string? CapturedRef { get; init; }
    [JsonPropertyName("captured_head")] public string? CapturedHead { get; init; }
    [JsonPropertyName("observed_at")] public string? ObservedAt { get; init; }
    [JsonPropertyName("expires_at")] public string? ExpiresAt { get; init; }
    [JsonPropertyName("timeout_seconds")] public int? TimeoutSeconds { get; init; }
    [JsonPropertyName("upstream_timeout")] public bool? UpstreamTimeout { get; init; }
    [JsonPropertyName("owner")] public string? Owner { get; init; }
    [JsonPropertyName("action")] public string? Action { get; init; }
    [JsonPropertyName("deadline")] public string? Deadline { get; init; }
    [JsonPropertyName("gate_evidence")] public IReadOnlyList<string>? GateEvidence { get; init; }
    [JsonPropertyName("provenance")] public IReadOnlyList<string>? Provenance { get; init; }
    [JsonPropertyName("elapsed_ms")] public long? ElapsedMilliseconds { get; init; }

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

    /// <summary>G673: structured upstream GitHub availability.</summary>
    [JsonPropertyName("github_api_status")] public string GithubApiStatus { get; init; } = GitHubApiQuotaConstants.Healthy;
    [JsonPropertyName("degraded")] public bool Degraded { get; init; }
    [JsonPropertyName("cause")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Cause { get; init; }
    [JsonPropertyName("degraded_state")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public GitHubApiDegradedState? DegradedState { get; init; }
    [JsonPropertyName("resource")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Resource => DegradedState?.Resource;
    [JsonPropertyName("remaining")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? Remaining => DegradedState?.Remaining;
    [JsonPropertyName("reset")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? Reset => DegradedState?.Reset;
    [JsonPropertyName("reset_at")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ResetAt => DegradedState?.ResetAt;
}
