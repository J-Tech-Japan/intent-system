using System.Text.Json;
using System.Text.Json.Serialization;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G280: Read-only host review/next-slice diagnostic. Differentiates true
/// idle from stale-CLI, stuck review label, missing target on a PR,
/// conflicting review-side labels, WIP-cap blockage, and
/// clarification-required so an operator running the host loop can tell
/// why the loop did not advance. Never mutates GitHub, never applies
/// labels, never touches durable parent state, and never launches an AI
/// provider. Pairs with <c>automation host-review-preflight</c> (which
/// selects a target) and <c>automation reconcile</c> (which actually
/// repairs label drift); this command only classifies and recommends.
/// </summary>
internal static class AutomationHostReviewDiagnosticsCommand
{
    private const string FormatText = "text";
    private const string FormatJson = "json";

    public static Func<IGitHubAutomationCandidateLister>? CandidateListerFactory { get; set; }

    public static Func<bool>? NestedProviderLauncher { get; set; }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

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

        // G383: visible/manual/runtime-gated verification-AC decision lane.
        // Isolated early-return so the rest of host-review-diagnostics is
        // untouched. When `--review-verification-ac` is passed, run the
        // verification-policy classifier and emit the stable
        // `review-policy-gap` / `implementation-finding` classification (or
        // a proceed verdict) so the host loop converges instead of
        // re-asking the operator the same standing A/B/C policy question.
        if (Array.IndexOf(args, "--review-verification-ac") >= 0)
        {
            return HandleReviewVerificationPolicy(args, writer);
        }

        // G384: internal-submodule-edit redundancy decision lane. Isolated
        // early-return: classifies a dirty internal submodule edit as
        // redundant-safe vs protected-operator-work, deduplicates repeated
        // wakes (--prior-fingerprint), and keeps a failing required CI
        // visible as the implementation blocker (--required-ci-failing).
        if (Array.IndexOf(args, "--in-submodule-edit") >= 0)
        {
            return HandleInSubmoduleEdit(args, writer);
        }

        // G390: isolated lane — classify whether a review wake that stopped on
        // a host metadata blocker must restore intent-pr-rereview-ready so the
        // PR stays review-actionable (a stale-review-lease safe repair) and
        // suppress an implementation request-update comment.
        if (Array.IndexOf(args, "--review-lease-preservation") >= 0)
        {
            return HandleReviewLeasePreservation(args, writer);
        }

        // G453: isolated lane — classify a selected review PR blocked by a dirty
        // host worktree that contains UNPUBLISHED host metadata, so cleanup does
        // not destroy the queue-state / runs.jsonl / packet / publish / intent
        // metadata that review/closeout needs. Also detects wrong review
        // worktree / branch in same-repo topology and keeps failing CI separate
        // from workspace-dirty blockers.
        if (Array.IndexOf(args, "--selected-review-workspace") >= 0)
        {
            return HandleSelectedReviewWorkspace(args, writer);
        }

        if (!TryParseArguments(
                args,
                out var repo,
                out var workdir,
                out var candidate,
                out var domain,
                out var clarificationRequired,
                out var staleClarificationMetadata,
                out var reconcileUnsafeStopKinds,
                out var reconcileRepairsAvailable,
                out var publishRecoveryRepairsAvailable,
                out var closeoutDriftRepairsAvailable,
                out var allowWipCapOverride,
                out var prDraft,
                out var workspaceSafeDirty,
                out var draftReviewReady,
                out var draftFindingsPresent,
                out var operatorIntendedDraft,
                out var format,
                out var error))
        {
            writer.WriteLine(error);
            return 1;
        }

        var resolvedWorkdir = WorkdirResolver.Resolve(context, workdir);
        if (string.IsNullOrWhiteSpace(repo)
            && !AutomationCheckCommand.TryInferGitHubRepo(resolvedWorkdir, out repo, out error))
        {
            writer.WriteLine(error);
            return 1;
        }

        // G341 / G365: domain resolution priority when --domain is
        // omitted:
        //
        //   1. `.intent-cli/host-binding.toml` (G365) — the canonical
        //      domain-for-this-target-repo mapping. When the requested
        //      `--repo` matches the binding's `target_repo` the
        //      binding's `domain` is authoritative.
        //   2. `context.Config.Project.Domain` (G341 legacy) — used
        //      when no binding file is present.
        //
        // Mismatch outcome (binding records a different target_repo)
        // short-circuits to a `missing-domain-binding` emission so the
        // host loop surfaces the gap rather than silently using the
        // wrong domain.
        var domainBinding = HostBindingDomainResolution.Missing(null);
        if (string.IsNullOrWhiteSpace(domain))
        {
            var resolver = HostBindingDomainResolverDelegate
                ?? HostBindingDomainResolver.Resolve;
            domainBinding = resolver(context, repo!);
            if (domainBinding.Kind == HostBindingDomainResolutionKind.Match
                && !string.IsNullOrWhiteSpace(domainBinding.Domain))
            {
                domain = domainBinding.Domain;
            }
            else if (domainBinding.Kind == HostBindingDomainResolutionKind.Missing)
            {
                var configuredDomain = context.Config?.Project?.Domain;
                if (!string.IsNullOrWhiteSpace(configuredDomain))
                {
                    domain = configuredDomain.Trim();
                }
            }
            // Mismatch: deliberately do NOT fall through to the
            // configured domain; the structured emission below names
            // the inconsistency.
        }

        if (domainBinding.Kind == HostBindingDomainResolutionKind.Mismatch
            && string.IsNullOrWhiteSpace(domain))
        {
            EmitMissingDomainBinding(writer, repo!, domainBinding, format);
            return 0;
        }

        var surfaceReport = AutomationInstalledCliSurfaceProbe.Check(context);
        if (!surfaceReport.Available)
        {
            var missingSurfaces = surfaceReport.Checks
                .Where(check => !check.Available)
                .Select(check => string.IsNullOrWhiteSpace(check.Transition)
                    ? check.Command
                    : $"{check.Command} --transition {check.Transition}")
                .ToArray();
            var stale = new AutomationHostReviewDiagnosticsResult
            {
                Repo = repo!,
                Classification = AutomationHostReviewDiagnosticsClassifications.StaleHostCli,
                Summary = $"installed CLI at {surfaceReport.InstalledCliPath} is missing or stale for required automation command surfaces ({string.Join("; ", missingSurfaces)}); refresh the installed CLI before running the host loop.",
                ReadOnly = true,
                RecommendedNextCommand = "intent-cli automation doctor --format json",
                StructuredClarification = null,
                Details =
                [
                    new AutomationHostReviewDiagnosticsDetail
                    {
                        Kind = AutomationHostReviewDiagnosticsClassifications.StaleHostCli,
                        TargetKind = null,
                        TargetNumber = null,
                        TargetUrl = null,
                        Description = $"installed_cli_path: {surfaceReport.InstalledCliPath}; missing_surfaces: {string.Join(", ", missingSurfaces)}",
                    }
                ],
                Warnings = staleClarificationMetadata
                    ? new[] { "stale-clarification-metadata" }
                    : Array.Empty<string>(),
                SafeRepairAvailable = false,
                SafeRepairCategory = null,
            };
            Emit(writer, stale, format);
            return 1;
        }

        IGitHubAutomationCandidateLister lister;
        try
        {
            lister = CandidateListerFactory?.Invoke()
                ?? new GhCliGitHubAutomationCandidateLister();
        }
        catch (Exception exception) when (
            exception is InvalidOperationException
            or IOException)
        {
            writer.WriteLine($"failed to initialize GitHub lister: {exception.Message}");
            return 1;
        }

        IReadOnlyList<GitHubAutomationPrCandidate> openPrs;
        IReadOnlyList<GitHubAutomationIssueCandidate> publishedIssues;
        try
        {
            openPrs = lister.ListPullRequests(repo!, Array.Empty<string>());
            publishedIssues = lister.ListIssues(
                repo!,
                [WorkerNextActionConstants.Labels.IntentTarget]);
        }
        catch (Exception exception) when (
            exception is InvalidOperationException
            or IOException)
        {
            writer.WriteLine($"failed to list diagnostic candidates for {repo}: {exception.Message}");
            return 1;
        }

        // G341: when the operator did not pre-supply `--candidate`,
        // auto-probe `intent next-slice --dry-run` so a real
        // `issue-cut-ready` candidate is classified as
        // `issue-publish-ready` rather than `true-idle`. Mirrors the
        // G318 auto-probe in `automation host-loop-next-action` so
        // both surfaces agree on the same next-slice signal.
        // Fail-soft: a probe error (queue-state missing, packet root
        // unreadable) falls through to the existing
        // candidate-not-supplied path so the host loop never crashes.
        //
        // G364: extend the switch so `clarification-required` from the
        // next-slice probe routes to the `clarification-required`
        // diagnostics lane instead of falling through to `true-idle`.
        // This keeps diagnostics in lockstep with the host-loop-next-action
        // probe (which already handles all next-slice outcomes) and is
        // what the observed SekibanAsAService SKS-G403 case requires.
        if (string.IsNullOrWhiteSpace(candidate) && !string.IsNullOrWhiteSpace(domain))
        {
            var probe = NextSliceDryRunProbeFactory?.Invoke(context)
                ?? new IntentCliNextSliceDryRunProbe(context);
            var probed = probe.Probe(repo!, domain!);
            if (probed != null)
            {
                switch (probed.RecommendedOutcome)
                {
                    case "issue-cut-ready":
                        if (!string.IsNullOrWhiteSpace(probed.ExecutionUnit))
                        {
                            candidate = probed.ExecutionUnit;
                        }
                        break;

                    case "clarification-required":
                        // G364: next-slice authoritatively reports a
                        // clarification gate. Forward to the analyzer's
                        // existing `clarification-required` lane so the
                        // host loop surfaces the blocker rather than
                        // falling through to `true-idle`.
                        clarificationRequired = true;
                        break;

                    // Other outcomes (design-needed, skip-next-slice-due-to-wip,
                    // no-actionable-item) intentionally not handled here:
                    // diagnostics has no design-needed lane (host-loop-next-action
                    // owns that lane via its analyzer), and skip-due-to-wip /
                    // no-actionable-item correctly flow through the existing
                    // wip-cap / true-idle logic based on GitHub label state.
                }
            }
        }

        // G342: auto-probe `automation publish-recovery --dry-run`
        // when the operator did not pre-supply
        // `--publish-recovery-repairs-available`. A `safe_repairs > 0`
        // outcome surfaces the existing `publish-recovery-ready`
        // diagnostics lane (the deterministic `linked_pr` recovery
        // path) instead of falling through to `true-idle`.
        if (publishRecoveryRepairsAvailable == 0)
        {
            var recoveryProbe = PublishRecoveryProbeFactory?.Invoke(context)
                ?? new IntentCliPublishRecoveryProbe(context);
            var recoveryProbed = recoveryProbe.Probe(repo!);
            if (recoveryProbed != null && recoveryProbed.SafeRepairCount > 0 && recoveryProbed.UnsafeStopCount == 0)
            {
                publishRecoveryRepairsAvailable = recoveryProbed.SafeRepairCount;
            }
        }

        // G433: validate the candidate's packet contract so that neither the
        // auto-probe path nor the explicit --candidate path can return
        // issue-publish-ready for a packet that issue publish-flow would reject.
        // Skip validation when the body file is absent (no file means no
        // validation signal — callers that supply --candidate without a packet
        // body on disk retain the pre-G433 behavior).
        var candidateMissingSections = ComputeCandidateMissingContractSections(
            resolvedWorkdir, candidate, context);

        var result = AutomationHostReviewDiagnosticsAnalyzer.Analyze(
            repo!,
            openPrs,
            publishedIssues,
            clarificationRequired,
            candidate,
            staleClarificationMetadata,
            reconcileUnsafeStopKinds,
            reconcileRepairsAvailable,
            allowWipCapOverride,
            prDraft,
            publishRecoveryRepairsAvailable,
            workspaceSafeDirty,
            closeoutDriftRepairsAvailable,
            draftReviewReady,
            draftFindingsPresent,
            operatorIntendedDraft,
            candidateMissingSections);

        Emit(writer, result, format);
        return 0;
    }

    /// <summary>
    /// G341: testability seam for the auto-probe of
    /// <c>intent next-slice --dry-run</c>. Production uses
    /// <see cref="IntentCliNextSliceDryRunProbe"/>; tests inject a
    /// fake to model `issue-cut-ready` / `no-actionable-item`
    /// outcomes without touching live queue-state.
    /// </summary>
    public static Func<CliContext, INextSliceDryRunProbe>? NextSliceDryRunProbeFactory { get; set; }

    /// <summary>
    /// G342: testability seam for the publish-recovery auto-probe.
    /// Mirrors the seam in <see cref="AutomationHostLoopNextActionCommand"/>
    /// so both surfaces agree on the same `linked_pr` recoverable
    /// signal.
    /// </summary>
    public static Func<CliContext, IPublishRecoveryProbe>? PublishRecoveryProbeFactory { get; set; }

    /// <summary>
    /// G365: testability seam for the host-binding domain resolver.
    /// Mirrors the seam in <see cref="AutomationHostLoopNextActionCommand"/>
    /// so both surfaces agree on the same per-repo domain mapping.
    /// </summary>
    public static Func<CliContext, string, HostBindingDomainResolution>? HostBindingDomainResolverDelegate { get; set; }

    private static void EmitMissingDomainBinding(
        TextWriter writer,
        string repo,
        HostBindingDomainResolution binding,
        string format)
    {
        var bindingPath = binding.BindingPath ?? "(unresolved)";
        var boundRepo = binding.BoundTargetRepo ?? "(none)";
        var boundDomain = binding.Domain ?? "(none)";
        var emitted = new AutomationHostReviewDiagnosticsResult
        {
            Repo = repo,
            Classification = AutomationHostReviewDiagnosticsClassifications.MissingDomainBinding,
            Summary =
                $"Missing domain binding for `{repo}` — host-binding.toml maps a different target_repo (`{boundRepo}` / domain `{boundDomain}`); "
                + "pass --domain explicitly or align the host-binding so candidate-less diagnostics can probe the right domain.",
            ReadOnly = true,
            RecommendedNextCommand =
                $"intent-cli automation host-review-diagnostics --repo {repo} --domain <DOMAIN> --format json",
            StructuredClarification = null,
            Details =
            [
                new AutomationHostReviewDiagnosticsDetail
                {
                    Kind = AutomationHostReviewDiagnosticsClassifications.MissingDomainBinding,
                    TargetKind = null,
                    TargetNumber = null,
                    TargetUrl = null,
                    Description =
                        $"binding_path: {bindingPath}; bound_target_repo: {boundRepo}; bound_domain: {boundDomain}; requested_repo: {repo}",
                }
            ],
            Warnings = Array.Empty<string>(),
            SafeRepairAvailable = false,
            SafeRepairCategory = null,
        };
        Emit(writer, emitted, format);
    }

    private static bool TryParseArguments(
        string[] args,
        out string? repo,
        out string? workdir,
        out string? candidate,
        out string? domain,
        out bool clarificationRequired,
        out bool staleClarificationMetadata,
        out IReadOnlyList<string> reconcileUnsafeStopKinds,
        out int reconcileRepairsAvailable,
        out int publishRecoveryRepairsAvailable,
        out int closeoutDriftRepairsAvailable,
        out bool allowWipCapOverride,
        out bool? prDraft,
        out bool workspaceSafeDirty,
        out bool draftReviewReady,
        out bool draftFindingsPresent,
        out bool operatorIntendedDraft,
        out string format,
        out string error)
    {
        repo = null;
        workdir = null;
        candidate = null;
        domain = null;
        clarificationRequired = false;
        staleClarificationMetadata = false;
        var unsafeStops = new List<string>();
        reconcileUnsafeStopKinds = unsafeStops;
        reconcileRepairsAvailable = 0;
        publishRecoveryRepairsAvailable = 0;
        closeoutDriftRepairsAvailable = 0;
        allowWipCapOverride = false;
        prDraft = null;
        workspaceSafeDirty = false;
        draftReviewReady = false;
        draftFindingsPresent = false;
        operatorIntendedDraft = false;
        format = FormatText;
        error = string.Empty;

        for (var index = 0; index < args.Length; index++)
        {
            var argument = args[index];
            switch (argument)
            {
                case "--repo":
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                    {
                        error = "--repo requires a value (e.g. owner/repo).";
                        return false;
                    }
                    repo = args[index + 1].Trim();
                    index++;
                    break;
                case "--workdir":
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                    {
                        error = "--workdir requires a value.";
                        return false;
                    }
                    workdir = args[index + 1];
                    index++;
                    break;
                case "--candidate":
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                    {
                        error = "--candidate requires an execution unit value.";
                        return false;
                    }
                    candidate = args[index + 1].Trim();
                    index++;
                    break;
                case "--domain":
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                    {
                        error = "--domain requires a value.";
                        return false;
                    }
                    domain = args[index + 1].Trim();
                    index++;
                    break;
                case "--clarification-required":
                    clarificationRequired = true;
                    break;
                case "--stale-clarification-metadata":
                    staleClarificationMetadata = true;
                    break;
                case "--reconcile-unsafe-stop":
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                    {
                        error = "--reconcile-unsafe-stop requires a value (one stop kind per flag; repeat for multiple).";
                        return false;
                    }
                    unsafeStops.Add(args[index + 1].Trim());
                    index++;
                    break;
                case "--reconcile-repairs-available":
                    if (index + 1 >= args.Length
                        || !int.TryParse(args[index + 1], System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var repairCount)
                        || repairCount < 0)
                    {
                        error = "--reconcile-repairs-available requires a non-negative integer value.";
                        return false;
                    }
                    reconcileRepairsAvailable = repairCount;
                    index++;
                    break;
                case "--publish-recovery-repairs-available":
                    if (index + 1 >= args.Length
                        || !int.TryParse(args[index + 1], System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var publishRecoveryRepairCount)
                        || publishRecoveryRepairCount < 0)
                    {
                        error = "--publish-recovery-repairs-available requires a non-negative integer value.";
                        return false;
                    }
                    publishRecoveryRepairsAvailable = publishRecoveryRepairCount;
                    index++;
                    break;
                case "--closeout-drift-repairs-available":
                    if (index + 1 >= args.Length
                        || !int.TryParse(args[index + 1], System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var closeoutDriftRepairCount)
                        || closeoutDriftRepairCount < 0)
                    {
                        error = "--closeout-drift-repairs-available requires a non-negative integer value.";
                        return false;
                    }
                    closeoutDriftRepairsAvailable = closeoutDriftRepairCount;
                    index++;
                    break;
                case "--allow-wip-cap-override":
                    allowWipCapOverride = true;
                    break;
                case "--workspace-safe-dirty":
                    workspaceSafeDirty = true;
                    break;
                // G376: draft-aware host review decision signals. The host
                // loop passes these after positively verifying review state
                // (closeout-plan ready, guide review ready, base on policy,
                // diff check passed, findings). Absent flags preserve the
                // pre-G376 fail-closed draft-merge-blocked default.
                case "--draft-review-ready":
                    draftReviewReady = true;
                    break;
                case "--draft-findings-present":
                    draftFindingsPresent = true;
                    break;
                case "--operator-intended-draft":
                    operatorIntendedDraft = true;
                    break;
                case "--pr-draft":
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                    {
                        error = "--pr-draft requires a value (true or false).";
                        return false;
                    }
                    var rawDraft = args[index + 1].Trim().ToLowerInvariant();
                    if (string.Equals(rawDraft, "true", StringComparison.Ordinal))
                    {
                        prDraft = true;
                    }
                    else if (string.Equals(rawDraft, "false", StringComparison.Ordinal))
                    {
                        prDraft = false;
                    }
                    else
                    {
                        error = $"--pr-draft must be 'true' or 'false' (got '{args[index + 1]}').";
                        return false;
                    }
                    index++;
                    break;
                case "--format":
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                    {
                        error = "--format requires a value (text or json).";
                        return false;
                    }
                    var requestedFormat = args[index + 1];
                    if (!string.Equals(requestedFormat, FormatText, StringComparison.Ordinal)
                        && !string.Equals(requestedFormat, FormatJson, StringComparison.Ordinal))
                    {
                        error = $"--format must be 'text' or 'json' (got '{requestedFormat}').";
                        return false;
                    }
                    format = requestedFormat;
                    index++;
                    break;
                default:
                    error = $"Unknown argument '{argument}'. Supported: [--repo <owner/repo>] [--workdir <path>] [--candidate <execution-unit>] [--clarification-required] [--stale-clarification-metadata] [--reconcile-unsafe-stop <kind>] [--reconcile-repairs-available <N>] [--publish-recovery-repairs-available <N>] [--closeout-drift-repairs-available <N>] [--allow-wip-cap-override] [--workspace-safe-dirty] [--pr-draft true|false] [--draft-review-ready] [--draft-findings-present] [--operator-intended-draft] [--format text|json].";
                    return false;
            }
        }

        return true;
    }

    /// <summary>
    /// G433: reads the candidate's <c>github-body.md</c> and returns the
    /// required Child Issue Contract sections that are absent. Returns an
    /// empty list when the body file does not exist (no signal → no
    /// validation) or when <paramref name="candidate"/> is empty.
    /// </summary>
    private static IReadOnlyList<string> ComputeCandidateMissingContractSections(
        string workdir,
        string? candidate,
        CliContext context)
    {
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return Array.Empty<string>();
        }

        var artifactRoot = context.Config?.Project?.ArtifactRoot ?? ".intent-cli";
        var bodyPath = Path.Combine(workdir, artifactRoot, "issues", candidate, "github-body.md");
        if (!File.Exists(bodyPath))
        {
            return Array.Empty<string>();
        }

        var content = File.ReadAllText(bodyPath);
        var missing = new List<string>();
        foreach (var section in PacketDraftCommand.RequiredContractSections)
        {
            if (!ContainsPacketSectionHeading(content, section))
            {
                missing.Add(section);
            }
        }

        return missing;
    }

    private static bool ContainsPacketSectionHeading(string content, string section)
    {
        var lines = content.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();
            if (!line.StartsWith("##", StringComparison.Ordinal))
            {
                continue;
            }

            var heading = line.TrimStart('#').Trim();
            if (string.Equals(heading, section, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static void Emit(TextWriter writer, AutomationHostReviewDiagnosticsResult result, string format)
    {
        if (string.Equals(format, FormatJson, StringComparison.Ordinal))
        {
            writer.WriteLine(JsonSerializer.Serialize(result, JsonOptions));
        }
        else
        {
            WriteText(writer, result);
        }
    }

    private static void WriteText(TextWriter writer, AutomationHostReviewDiagnosticsResult result)
    {
        writer.WriteLine($"# Host review diagnostics for {result.Repo}");
        writer.WriteLine($"- classification: {result.Classification}");
        writer.WriteLine($"- read_only: {result.ReadOnly.ToString().ToLowerInvariant()}");
        writer.WriteLine($"- {result.Summary}");
        if (!string.IsNullOrWhiteSpace(result.RecommendedNextCommand))
        {
            writer.WriteLine();
            writer.WriteLine("## Recommended next command");
            writer.WriteLine($"- {result.RecommendedNextCommand}");
        }
        if (result.StructuredClarification is { } clarification)
        {
            writer.WriteLine();
            writer.WriteLine("## Structured clarification");
            writer.WriteLine($"- background: {clarification.Background}");
            writer.WriteLine($"- question: {clarification.Question}");
            foreach (var option in clarification.Options)
            {
                writer.WriteLine($"  - option: {option}");
            }
        }
        if (result.Details.Count > 0)
        {
            writer.WriteLine();
            writer.WriteLine("## Details");
            foreach (var detail in result.Details)
            {
                writer.WriteLine($"- {detail.Kind}: {detail.Description}");
            }
        }
    }

    /// <summary>
    /// G383: classify a visible/manual/runtime-gated verification AC via
    /// <see cref="ReviewVerificationPolicyClassifier"/> and emit the stable
    /// host-review-diagnostics classification so the host loop converges
    /// (approve / request-update / record-policy-gap-once) instead of
    /// re-asking the operator. Read-only; no host state, no GitHub mutation.
    /// </summary>
    private static int HandleReviewVerificationPolicy(string[] args, TextWriter writer)
    {
        var standingPolicy = false;
        var falseRuntimeClaim = false;
        var implementationActionable = false;
        var evidence = ReviewVerificationPolicyClassifier.Evidence.None;
        string? repo = null;
        var pr = "<n>";
        var format = FormatText;

        for (var index = 0; index < args.Length; index++)
        {
            var argument = args[index];
            switch (argument)
            {
                case "--review-verification-ac":
                    break;
                case "--standing-policy":
                    standingPolicy = true;
                    break;
                case "--false-runtime-claim":
                    falseRuntimeClaim = true;
                    break;
                case "--implementation-actionable":
                    implementationActionable = true;
                    break;
                case "--evidence":
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                    {
                        writer.WriteLine("--evidence requires a value (source-mapping | documented-observation | static-screenshot | none).");
                        return 1;
                    }
                    evidence = args[index + 1].Trim();
                    // G383 (review follow-up): reject unknown/misspelled evidence
                    // so an input-contract error is not routed as a host policy gap.
                    if (!ReviewVerificationPolicyClassifier.Evidence.IsKnown(evidence))
                    {
                        writer.WriteLine($"--evidence must be one of {string.Join(" | ", ReviewVerificationPolicyClassifier.Evidence.All)} (got '{evidence}').");
                        return 1;
                    }
                    index++;
                    break;
                case "--repo":
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                    {
                        writer.WriteLine("--repo requires a value (e.g. owner/repo).");
                        return 1;
                    }
                    repo = args[index + 1].Trim();
                    index++;
                    break;
                case "--pr":
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                    {
                        writer.WriteLine("--pr requires a positive integer.");
                        return 1;
                    }
                    pr = args[index + 1].Trim();
                    index++;
                    break;
                case "--format":
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                    {
                        writer.WriteLine("--format requires a value (text or json).");
                        return 1;
                    }
                    var requested = args[index + 1];
                    if (!string.Equals(requested, FormatText, StringComparison.Ordinal)
                        && !string.Equals(requested, FormatJson, StringComparison.Ordinal))
                    {
                        writer.WriteLine($"--format must be 'text' or 'json' (got '{requested}').");
                        return 1;
                    }
                    format = requested;
                    index++;
                    break;
                default:
                    writer.WriteLine($"Unknown argument '{argument}' for --review-verification-ac. Supported: --review-verification-ac [--standing-policy] [--evidence <kind>] [--false-runtime-claim] [--implementation-actionable] [--repo <owner/repo>] [--pr <n>] [--format text|json].");
                    return 1;
            }
        }

        var repoValue = string.IsNullOrWhiteSpace(repo) ? "<owner/repo>" : repo!;
        var decision = ReviewVerificationPolicyClassifier.Classify(
            standingPolicy, evidence, falseRuntimeClaim, implementationActionable);

        string classification;
        string? recommended;
        switch (decision.Decision)
        {
            case ReviewVerificationPolicyClassifier.Decisions.StandingPolicyApprove:
                classification = AutomationHostReviewDiagnosticsClassifications.ReviewPrActionable;
                recommended = $"intent-cli automation host-review-preflight --repo {repoValue} --format json";
                break;
            case ReviewVerificationPolicyClassifier.Decisions.ImplementationFinding:
                classification = AutomationHostReviewDiagnosticsClassifications.ImplementationFinding;
                recommended = $"intent-cli automation pr-transition --transition request-update --repo {repoValue} --pr {pr} --write --format json";
                break;
            default:
                classification = AutomationHostReviewDiagnosticsClassifications.ReviewPolicyGap;
                recommended = $"intent-cli clarify open --domain <domain> --question \"visible-verification policy for {repoValue}#{pr}\" --format json";
                break;
        }

        var result = new AutomationHostReviewDiagnosticsResult
        {
            Repo = repoValue,
            Classification = classification,
            Summary = $"G383 visible-verification AC: {decision.Reason} The review summary MUST state exactly what was verified and what was NOT run.",
            ReadOnly = true,
            RecommendedNextCommand = recommended,
            StructuredClarification = null,
            Details =
            [
                new AutomationHostReviewDiagnosticsDetail
                {
                    Kind = classification,
                    TargetKind = null,
                    TargetNumber = null,
                    TargetUrl = null,
                    Description = $"decision={decision.Decision}; route={decision.Route}; record_host_gap_once={decision.RecordHostGapOnce.ToString().ToLowerInvariant()}; post_pr_feedback={decision.PostPrFeedback.ToString().ToLowerInvariant()}",
                }
            ],
            Warnings = Array.Empty<string>(),
            SafeRepairAvailable = false,
            SafeRepairCategory = null,
        };

        Emit(writer, result, format);
        return 0;
    }

    /// <summary>
    /// G384: classify a dirty internal submodule edit via
    /// <see cref="WorkspaceGuardSubmoduleEditClassifier"/> and emit a stable
    /// diagnostics classification — `redundant-in-submodule-edit` (bounded
    /// safe repair), `protected-operator-work` (hard-stop), or, when the
    /// workspace is safe/redundant-safe and required CI is failing,
    /// `required-ci-failing` (implementation-actionable). Deduplicates a
    /// repeated unchanged report via `--prior-fingerprint`. Read-only.
    /// </summary>
    private static int HandleInSubmoduleEdit(string[] args, TextWriter writer)
    {
        var uniqueLocalContent = false;
        var requiredCiFailing = false;
        string submodulePath = "(submodule)";
        string? submoduleRoot = null;
        var localFingerprint = string.Empty;
        var headFingerprint = string.Empty;
        string? priorFingerprint = null;
        string? repo = null;
        int? pr = null;
        var format = FormatText;

        for (var index = 0; index < args.Length; index++)
        {
            var argument = args[index];
            switch (argument)
            {
                case "--in-submodule-edit":
                    break;
                case "--unique-local-content":
                    uniqueLocalContent = true;
                    break;
                case "--required-ci-failing":
                    requiredCiFailing = true;
                    break;
                case "--path":
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                    {
                        writer.WriteLine("--path requires a value (the dirty submodule path).");
                        return 1;
                    }
                    submodulePath = args[index + 1].Trim();
                    index++;
                    break;
                case "--submodule-root":
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                    {
                        writer.WriteLine("--submodule-root requires a value (the submodule worktree root, e.g. submodules/X).");
                        return 1;
                    }
                    submoduleRoot = args[index + 1].Trim();
                    index++;
                    break;
                case "--local-fingerprint":
                    if (index + 1 >= args.Length) { writer.WriteLine("--local-fingerprint requires a value."); return 1; }
                    localFingerprint = args[index + 1].Trim();
                    index++;
                    break;
                case "--pr-head-fingerprint":
                    if (index + 1 >= args.Length) { writer.WriteLine("--pr-head-fingerprint requires a value."); return 1; }
                    headFingerprint = args[index + 1].Trim();
                    index++;
                    break;
                case "--prior-fingerprint":
                    if (index + 1 >= args.Length) { writer.WriteLine("--prior-fingerprint requires a value."); return 1; }
                    priorFingerprint = args[index + 1].Trim();
                    index++;
                    break;
                case "--repo":
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1])) { writer.WriteLine("--repo requires a value (e.g. owner/repo)."); return 1; }
                    repo = args[index + 1].Trim();
                    index++;
                    break;
                case "--pr":
                    if (index + 1 >= args.Length
                        || !int.TryParse(args[index + 1], System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var prValue)
                        || prValue <= 0)
                    {
                        writer.WriteLine("--pr requires a positive integer.");
                        return 1;
                    }
                    pr = prValue;
                    index++;
                    break;
                case "--format":
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1])) { writer.WriteLine("--format requires a value (text or json)."); return 1; }
                    var requested = args[index + 1];
                    if (!string.Equals(requested, FormatText, StringComparison.Ordinal) && !string.Equals(requested, FormatJson, StringComparison.Ordinal))
                    {
                        writer.WriteLine($"--format must be 'text' or 'json' (got '{requested}').");
                        return 1;
                    }
                    format = requested;
                    index++;
                    break;
                default:
                    writer.WriteLine($"Unknown argument '{argument}' for --in-submodule-edit. Supported: --in-submodule-edit [--path <p>] [--local-fingerprint <f>] [--pr-head-fingerprint <f>] [--unique-local-content] [--required-ci-failing] [--prior-fingerprint <f>] [--repo <owner/repo>] [--pr <n>] [--format text|json].");
                    return 1;
            }
        }

        // Derive the submodule worktree root from the path when not supplied
        // (convention: `submodules/<name>/...`), so the bounded repair points
        // `git -C` at the submodule root and clears only the relative file.
        var resolvedRoot = string.IsNullOrWhiteSpace(submoduleRoot)
            ? DeriveSubmoduleRoot(submodulePath)
            : submoduleRoot!;

        var decision = WorkspaceGuardSubmoduleEditClassifier.Classify(
            isInternalSubmoduleEdit: true,
            resolvedRoot,
            submodulePath,
            localFingerprint,
            headFingerprint,
            uniqueLocalContent,
            pr);

        var deduplicated = WorkspaceGuardSubmoduleEditClassifier.IsDuplicateReport(priorFingerprint, decision.DedupeFingerprint);
        var primaryBlocker = WorkspaceGuardSubmoduleEditClassifier.ResolvePrimaryBlocker(requiredCiFailing, decision.Classification);

        // Required CI failing takes precedence as the implementation blocker
        // when the workspace edit is redundant-safe (or not blocking).
        string classification;
        string? recommended;
        if (string.Equals(primaryBlocker, WorkspaceGuardSubmoduleEditClassifier.Blockers.RequiredCiFailing, StringComparison.Ordinal))
        {
            classification = AutomationHostReviewDiagnosticsClassifications.RequiredCiFailing;
            recommended = repo is not null && pr is not null
                ? $"intent-cli automation pr-transition --transition request-update --repo {repo} --pr {pr} --write --format json"
                : null;
        }
        else if (string.Equals(decision.Classification, WorkspaceGuardSubmoduleEditClassifier.Classifications.RedundantInSubmoduleEdit, StringComparison.Ordinal))
        {
            classification = AutomationHostReviewDiagnosticsClassifications.RedundantInSubmoduleEdit;
            recommended = decision.RecommendedRepair;
        }
        else
        {
            classification = AutomationHostReviewDiagnosticsClassifications.ProtectedOperatorWork;
            recommended = null;
        }

        var warnings = deduplicated ? new[] { "deduplicated-unchanged-report" } : Array.Empty<string>();
        var summaryPrefix = deduplicated
            ? "G384 (deduplicated — unchanged dirty fingerprint since last wake): "
            : "G384: ";

        var result = new AutomationHostReviewDiagnosticsResult
        {
            Repo = repo ?? "(repo)",
            Classification = classification,
            Summary = summaryPrefix + decision.Reason
                + (requiredCiFailing ? " Required CI is failing and remains the visible implementation blocker." : string.Empty),
            ReadOnly = true,
            RecommendedNextCommand = recommended,
            StructuredClarification = null,
            Details =
            [
                new AutomationHostReviewDiagnosticsDetail
                {
                    Kind = decision.Classification,
                    TargetKind = null,
                    TargetNumber = pr,
                    TargetUrl = null,
                    Description = $"path={submodulePath}; safe_repair_available={decision.SafeRepairAvailable.ToString().ToLowerInvariant()}; primary_blocker={primaryBlocker}; required_ci_failing={requiredCiFailing.ToString().ToLowerInvariant()}; dedupe_fingerprint={decision.DedupeFingerprint}",
                }
            ],
            Warnings = warnings,
            SafeRepairAvailable = decision.SafeRepairAvailable,
            SafeRepairCategory = decision.SafeRepairAvailable ? SafeRepairCategories.WorkspaceSafeDirty : null,
        };

        Emit(writer, result, format);
        return 0;
    }

    /// <summary>
    /// G390: classify whether a review wake that stopped on a host metadata
    /// blocker must restore <c>intent-pr-rereview-ready</c> (keeping the PR
    /// review-actionable) and suppress an implementation request-update
    /// comment. Read-only — the host loop applies the stale-review-lease
    /// restore based on the emitted decision.
    /// </summary>
    private static int HandleSelectedReviewWorkspace(string[] args, TextWriter writer)
    {
        int? selectedPr = null;
        int? linkedIssue = null;
        string? repo = null;
        string? domain = null;
        var dirtyDurablePaths = new List<string>();
        var dirtyUnrelatedPaths = new List<string>();
        var sameRepoTopology = false;
        string? metadataSourceBranch = null;
        string? currentBranch = null;
        string? ciStatus = null;
        var format = FormatText;

        for (var index = 0; index < args.Length; index++)
        {
            var argument = args[index];
            switch (argument)
            {
                case "--selected-review-workspace":
                    break;
                case "--selected-pr":
                    if (index + 1 >= args.Length
                        || !int.TryParse(args[index + 1], System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var prValue)
                        || prValue <= 0)
                    {
                        writer.WriteLine("--selected-pr requires a positive integer.");
                        return 1;
                    }
                    selectedPr = prValue;
                    index++;
                    break;
                case "--linked-issue":
                    if (index + 1 >= args.Length
                        || !int.TryParse(args[index + 1], System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var issueValue)
                        || issueValue <= 0)
                    {
                        writer.WriteLine("--linked-issue requires a positive integer.");
                        return 1;
                    }
                    linkedIssue = issueValue;
                    index++;
                    break;
                case "--repo":
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1])) { writer.WriteLine("--repo requires a value (e.g. owner/repo)."); return 1; }
                    repo = args[index + 1].Trim();
                    index++;
                    break;
                case "--domain":
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1])) { writer.WriteLine("--domain requires a value."); return 1; }
                    domain = args[index + 1].Trim();
                    index++;
                    break;
                case "--dirty-durable-path":
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1])) { writer.WriteLine("--dirty-durable-path requires a value."); return 1; }
                    dirtyDurablePaths.Add(args[index + 1].Trim());
                    index++;
                    break;
                case "--dirty-unrelated-path":
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1])) { writer.WriteLine("--dirty-unrelated-path requires a value."); return 1; }
                    dirtyUnrelatedPaths.Add(args[index + 1].Trim());
                    index++;
                    break;
                case "--same-repo-topology":
                    sameRepoTopology = true;
                    break;
                case "--metadata-source-branch":
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1])) { writer.WriteLine("--metadata-source-branch requires a value."); return 1; }
                    metadataSourceBranch = args[index + 1].Trim();
                    index++;
                    break;
                case "--current-branch":
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1])) { writer.WriteLine("--current-branch requires a value."); return 1; }
                    currentBranch = args[index + 1].Trim();
                    index++;
                    break;
                case "--ci-status":
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1])) { writer.WriteLine("--ci-status requires a value (e.g. passing|failing|pending)."); return 1; }
                    ciStatus = args[index + 1].Trim();
                    index++;
                    break;
                case "--format":
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1])) { writer.WriteLine("--format requires a value (text or json)."); return 1; }
                    var requested = args[index + 1];
                    if (!string.Equals(requested, FormatText, StringComparison.Ordinal) && !string.Equals(requested, FormatJson, StringComparison.Ordinal))
                    {
                        writer.WriteLine($"--format must be 'text' or 'json' (got '{requested}').");
                        return 1;
                    }
                    format = requested;
                    index++;
                    break;
                default:
                    writer.WriteLine($"Unknown argument '{argument}' for --selected-review-workspace. Supported: --selected-review-workspace --selected-pr <n> [--linked-issue <n>] [--repo <owner/repo>] [--domain <name>] [--dirty-durable-path <p> ...] [--dirty-unrelated-path <p> ...] [--same-repo-topology] [--metadata-source-branch <b>] [--current-branch <b>] [--ci-status passing|failing|pending] [--format text|json].");
                    return 1;
            }
        }

        if (selectedPr is null)
        {
            writer.WriteLine("--selected-pr <n> is required for --selected-review-workspace.");
            return 1;
        }

        var decision = SelectedReviewWorkspaceSalvageAnalyzer.Analyze(new SelectedReviewWorkspaceSalvageInput
        {
            SelectedPr = selectedPr.Value,
            LinkedIssue = linkedIssue,
            Repo = repo,
            Domain = domain,
            DirtyDurablePaths = dirtyDurablePaths,
            DirtyUnrelatedPaths = dirtyUnrelatedPaths,
            SameRepoTopology = sameRepoTopology,
            MetadataSourceBranch = metadataSourceBranch,
            CurrentBranch = currentBranch,
            CiStatus = ciStatus,
        });

        var summary = "G453: " + decision.Reason
            + (decision.DestructiveCleanupWarning
                ? " WARNING: `git reset`/`checkout`/`clean`/stash-drop on these paths may remove host metadata needed by review/closeout — salvage first."
                : string.Empty)
            + (decision.CiFailing && decision.Classification != AutomationHostReviewDiagnosticsClassifications.RequiredCiFailing
                ? " (Required CI is ALSO failing — that is a separate implementation blocker, not a workspace-cleanup problem.)"
                : string.Empty);

        var details = new List<AutomationHostReviewDiagnosticsDetail>
        {
            new()
            {
                Kind = decision.Classification,
                TargetKind = "pr",
                TargetNumber = selectedPr,
                TargetUrl = null,
                Description = $"destructive_cleanup_warning={decision.DestructiveCleanupWarning.ToString().ToLowerInvariant()}; "
                    + $"salvage_first={decision.SalvageFirst.ToString().ToLowerInvariant()}; "
                    + $"ci_failing={decision.CiFailing.ToString().ToLowerInvariant()}; "
                    + $"same_repo_topology={sameRepoTopology.ToString().ToLowerInvariant()}; "
                    + $"current_branch={currentBranch ?? "(unknown)"}; "
                    + $"metadata_source_branch={metadataSourceBranch ?? "(none)"}; "
                    + $"linked_issue={(linkedIssue is null ? "(unknown)" : linkedIssue.ToString())}; "
                    + $"unpublished_metadata_paths=[{string.Join(", ", decision.UnpublishedMetadataPaths)}]; "
                    + $"unrelated_paths=[{string.Join(", ", decision.UnrelatedPaths)}]",
            },
        };
        foreach (var action in decision.RecommendedSalvageActions)
        {
            details.Add(new AutomationHostReviewDiagnosticsDetail
            {
                Kind = "salvage-action",
                TargetKind = null,
                TargetNumber = null,
                TargetUrl = null,
                Description = action,
            });
        }

        var result = new AutomationHostReviewDiagnosticsResult
        {
            Repo = repo ?? "(repo)",
            Classification = decision.Classification,
            Summary = summary,
            ReadOnly = true,
            RecommendedNextCommand = decision.RecommendedNextCommand,
            StructuredClarification = null,
            Details = details,
            Warnings = Array.Empty<string>(),
            // Salvage of unpublished metadata is NOT a blind auto-repair: it may
            // require committing operator-owned `intents/**` edits, which the
            // host loop must never auto-commit. So safe_repair_available stays
            // false; the recommended salvage actions guide the operator-aware
            // recovery before any cleanup.
            SafeRepairAvailable = false,
            SafeRepairCategory = null,
        };

        Emit(writer, result, format);
        return 0;
    }

    private static int HandleReviewLeasePreservation(string[] args, TextWriter writer)
    {
        var rereviewReadyConsumed = false;
        var reviewVerdictProduced = false;
        var hostMetadataBlocker = false;
        string? repo = null;
        int? pr = null;
        var format = FormatText;

        for (var index = 0; index < args.Length; index++)
        {
            var argument = args[index];
            switch (argument)
            {
                case "--review-lease-preservation":
                    break;
                case "--rereview-ready-consumed":
                    rereviewReadyConsumed = true;
                    break;
                case "--review-verdict-produced":
                    reviewVerdictProduced = true;
                    break;
                case "--host-metadata-blocker":
                    hostMetadataBlocker = true;
                    break;
                case "--repo":
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1])) { writer.WriteLine("--repo requires a value (e.g. owner/repo)."); return 1; }
                    repo = args[index + 1].Trim();
                    index++;
                    break;
                case "--pr":
                    if (index + 1 >= args.Length
                        || !int.TryParse(args[index + 1], System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var prValue)
                        || prValue <= 0)
                    {
                        writer.WriteLine("--pr requires a positive integer.");
                        return 1;
                    }
                    pr = prValue;
                    index++;
                    break;
                case "--format":
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1])) { writer.WriteLine("--format requires a value (text or json)."); return 1; }
                    var requested = args[index + 1];
                    if (!string.Equals(requested, FormatText, StringComparison.Ordinal) && !string.Equals(requested, FormatJson, StringComparison.Ordinal))
                    {
                        writer.WriteLine($"--format must be 'text' or 'json' (got '{requested}').");
                        return 1;
                    }
                    format = requested;
                    index++;
                    break;
                default:
                    writer.WriteLine($"Unknown argument '{argument}' for --review-lease-preservation. Supported: --review-lease-preservation [--rereview-ready-consumed] [--review-verdict-produced] [--host-metadata-blocker] [--repo <owner/repo>] [--pr <n>] [--format text|json].");
                    return 1;
            }
        }

        var decision = ReviewLeasePreservationClassifier.Classify(
            reviewStartConsumedRereviewReady: rereviewReadyConsumed,
            reviewVerdictProduced: reviewVerdictProduced,
            stoppedOnHostMetadataBlocker: hostMetadataBlocker);

        var classification = decision.RestoreRereviewReady
            ? AutomationHostReviewDiagnosticsClassifications.MetadataBlockedReviewPreserved
            : AutomationHostReviewDiagnosticsClassifications.ReviewPrActionable;
        // Recover via the supported `review-release` transition: it removes the
        // `intent-pr-reviewing` lease (and stale leftovers) without adding any
        // review-side label, so the next host wake reselects the PR for review.
        // This restores review actionability after a metadata-blocked abort
        // WITHOUT creating an implementation request-update. (`automation
        // pr-transition` supports review-start / request-update / approved /
        // review-release only — there is intentionally no `rereview-ready`
        // transition.)
        var recommended = decision.RestoreRereviewReady && repo is not null && pr is not null
            ? $"intent-cli automation pr-transition --transition review-release --repo {repo} --pr {pr} --write --format json"
            : null;

        var result = new AutomationHostReviewDiagnosticsResult
        {
            Repo = repo ?? "(repo)",
            Classification = classification,
            Summary = "G390: " + decision.Reason
                + (decision.RestoreRereviewReady
                    ? " Recover with the `review-release` transition so the host reselects the PR for review (keeps it review-actionable)."
                    : string.Empty)
                + (decision.SuppressRequestUpdateComment
                    ? " A host metadata blocker is host-owned and must NOT be posted as an implementation request-update comment."
                    : string.Empty),
            ReadOnly = true,
            RecommendedNextCommand = recommended,
            StructuredClarification = null,
            Details =
            [
                new AutomationHostReviewDiagnosticsDetail
                {
                    Kind = classification,
                    TargetKind = pr is not null ? "pr" : null,
                    TargetNumber = pr,
                    TargetUrl = null,
                    Description = $"restore_rereview_ready={decision.RestoreRereviewReady.ToString().ToLowerInvariant()}; "
                        + $"suppress_request_update_comment={decision.SuppressRequestUpdateComment.ToString().ToLowerInvariant()}; "
                        + $"rereview_ready_consumed={rereviewReadyConsumed.ToString().ToLowerInvariant()}; "
                        + $"review_verdict_produced={reviewVerdictProduced.ToString().ToLowerInvariant()}; "
                        + $"host_metadata_blocker={hostMetadataBlocker.ToString().ToLowerInvariant()}",
                }
            ],
            Warnings = Array.Empty<string>(),
            // Restoring a consumed rereview-ready label is a stale-review-lease
            // recovery the host loop already knows how to apply.
            SafeRepairAvailable = decision.RestoreRereviewReady,
            SafeRepairCategory = decision.RestoreRereviewReady ? SafeRepairCategories.StaleReviewLease : null,
        };

        Emit(writer, result, format);
        return 0;
    }

    /// <summary>
    /// G384: best-effort submodule worktree root from a dirty path. For the
    /// `submodules/&lt;name&gt;/...` convention this returns
    /// `submodules/&lt;name&gt;`; otherwise it falls back to the path's first
    /// segment (or the path itself). Callers should pass `--submodule-root`
    /// explicitly when the layout differs.
    /// </summary>
    private static string DeriveSubmoduleRoot(string dirtyPath)
    {
        var normalized = dirtyPath.Replace('\\', '/').TrimStart('/');
        var segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length >= 2 && string.Equals(segments[0], "submodules", StringComparison.Ordinal))
        {
            return $"{segments[0]}/{segments[1]}";
        }
        return segments.Length >= 1 ? segments[0] : normalized;
    }

    private static void WriteHelp(TextWriter writer)
    {
        writer.WriteLine("automation host-review-diagnostics");
        writer.WriteLine("Usage: intent-cli automation host-review-diagnostics [--repo <owner/repo>] [--workdir <path>] [--candidate <execution-unit>] [--domain <name>] [--clarification-required] [--stale-clarification-metadata] [--reconcile-unsafe-stop <kind> ...] [--reconcile-repairs-available <N>] [--allow-wip-cap-override] [--workspace-safe-dirty] [--pr-draft true|false] [--draft-review-ready] [--draft-findings-present] [--operator-intended-draft] [--review-verification-ac [--standing-policy] [--evidence <kind>] [--false-runtime-claim] [--implementation-actionable] [--pr <n>]] [--in-submodule-edit [--path <p>] [--local-fingerprint <f>] [--pr-head-fingerprint <f>] [--unique-local-content] [--required-ci-failing] [--prior-fingerprint <f>] [--pr <n>]] [--format text|json]");
        writer.WriteLine("Read-only host-loop convergence diagnostic. Classifies stuck-reviewing, missing-target-on-pr, request-update-rereview-conflict, wip-cap-blocked, clarification-required, stale-host-cli, review-pr-actionable, issue-publish-ready, unsafe-metadata, repaired-and-retry, draft-merge-blocked (G297), draft-ready-to-promote / draft-request-update (G376), and true-idle (G286). Stale clarification metadata surfaces in `warnings` without flipping the terminal class. With `--allow-wip-cap-override` (G288) and a complete candidate, an in-flight intent-target item is bypassed for one publish; the override surfaces as `wip-cap-overridden` in `warnings`. With `--pr-draft true` and a selected review PR, the draft decision is draft-aware (G376): pass `--draft-review-ready` (host verified closeout/guide/base/diff and no findings) to get `draft-ready-to-promote` (mark ready + continue) instead of releasing the lease; `--draft-findings-present` yields `draft-request-update`; `--operator-intended-draft` (or no readiness signal) stays fail-closed at `draft-merge-blocked` (G297). With `--review-verification-ac` (G383) the diagnostic classifies a visible/manual/runtime-gated verification AC into `review-pr-actionable` (standing policy permits approval; proceed), `implementation-finding` (route a PR feedback comment + request-update), or `review-policy-gap` (record a host-owned policy decision once; do not re-ask) — pass `--standing-policy` / `--evidence <kind>` / `--false-runtime-claim` / `--implementation-actionable` to drive the classification. With `--in-submodule-edit` (G384) the diagnostic classifies a dirty internal submodule edit into `redundant-in-submodule-edit` (bounded safe repair when `--local-fingerprint` matches `--pr-head-fingerprint` and no `--unique-local-content`), `protected-operator-work` (unique/unproven — hard-stop), or `required-ci-failing` (when `--required-ci-failing` and the workspace is redundant-safe); `--prior-fingerprint` deduplicates an unchanged repeat. Never mutates GitHub or local state.");
    }
}
