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

        var resolvedWorkdir = ResolveWorkdir(context, workdir);
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
            operatorIntendedDraft);

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

    private static string ResolveWorkdir(CliContext context, string? workdir)
    {
        if (string.IsNullOrWhiteSpace(workdir))
        {
            return context.RepoRoot;
        }

        return Path.IsPathRooted(workdir)
            ? workdir
            : Path.GetFullPath(Path.Combine(context.RepoRoot, workdir));
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

    private static void WriteHelp(TextWriter writer)
    {
        writer.WriteLine("automation host-review-diagnostics");
        writer.WriteLine("Usage: intent-cli automation host-review-diagnostics [--repo <owner/repo>] [--workdir <path>] [--candidate <execution-unit>] [--domain <name>] [--clarification-required] [--stale-clarification-metadata] [--reconcile-unsafe-stop <kind> ...] [--reconcile-repairs-available <N>] [--allow-wip-cap-override] [--workspace-safe-dirty] [--pr-draft true|false] [--draft-review-ready] [--draft-findings-present] [--operator-intended-draft] [--format text|json]");
        writer.WriteLine("Read-only host-loop convergence diagnostic. Classifies stuck-reviewing, missing-target-on-pr, request-update-rereview-conflict, wip-cap-blocked, clarification-required, stale-host-cli, review-pr-actionable, issue-publish-ready, unsafe-metadata, repaired-and-retry, draft-merge-blocked (G297), draft-ready-to-promote / draft-request-update (G376), and true-idle (G286). Stale clarification metadata surfaces in `warnings` without flipping the terminal class. With `--allow-wip-cap-override` (G288) and a complete candidate, an in-flight intent-target item is bypassed for one publish; the override surfaces as `wip-cap-overridden` in `warnings`. With `--pr-draft true` and a selected review PR, the draft decision is draft-aware (G376): pass `--draft-review-ready` (host verified closeout/guide/base/diff and no findings) to get `draft-ready-to-promote` (mark ready + continue) instead of releasing the lease; `--draft-findings-present` yields `draft-request-update`; `--operator-intended-draft` (or no readiness signal) stays fail-closed at `draft-merge-blocked` (G297). Never mutates GitHub or local state.");
    }
}
