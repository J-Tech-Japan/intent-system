using System.Text.Json;
using System.Text.Json.Serialization;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// Installed host-side PR label transition surface for review-loop state
/// changes that previously required raw <c>gh pr edit</c> fallback blocks.
/// </summary>
internal static class AutomationPrTransitionCommand
{
    private const string FormatText = "text";
    private const string FormatJson = "json";
    private const string TransitionReviewStart = "review-start";
    private const string TransitionRequestUpdate = "request-update";
    private const string TransitionApproved = "approved";

    /// <summary>
    /// G292: release the <c>intent-pr-reviewing</c> lease without adding
    /// <c>intent-pr-request-update</c>. Used when host-owned metadata
    /// (e.g. missing parent <c>linked_pr</c>) blocks closeout-plan after
    /// review-start was applied; the next wake should be free to reselect
    /// the PR once the metadata is repaired by reconcile.
    /// </summary>
    private const string TransitionReviewRelease = "review-release";

    public static Func<IGitHubLabelMutator>? MutatorFactory { get; set; }

    public static Func<bool>? NestedProviderLauncher { get; set; }

    /// <summary>
    /// G834: test seam for the PR head read, next to the label read
    /// (<see cref="MutatorFactory"/>). Production runs
    /// <c>gh pr view &lt;n&gt; --repo &lt;repo&gt; --json headRefOid</c>. It is read only
    /// for the approved transition of a declared team in a gated repository.
    /// </summary>
    public static Func<string, int, string>? PrHeadReader { get; set; }

    /// <summary>G856: deterministic read-only review inventory seam for solo approval.</summary>
    internal static Func<SoloConductorReviewReadRequest, SoloConductorReviewReadResult>? SoloReviewReader { get; set; }

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
                out var pr,
                out var transition,
                out var mode,
                out var format,
                out var headSha,
                out var executionUnit,
                out var error))
        {
            writer.WriteLine(error);
            WriteHelp(writer);
            return 1;
        }

        var resolvedWorkdir = WorkdirResolver.Resolve(context, workdir);
        if (string.IsNullOrWhiteSpace(repo)
            && !AutomationCheckCommand.TryInferGitHubRepo(resolvedWorkdir, out repo, out error))
        {
            writer.WriteLine(error);
            return 1;
        }

        var plan = PlanTransition(transition!);
        IGitHubLabelMutator mutator;
        try
        {
            mutator = MutatorFactory?.Invoke()
                ?? new GhCliGitHubLabelMutator();
        }
        catch (Exception exception) when (
            exception is InvalidOperationException
            or IOException)
        {
            writer.WriteLine($"failed to initialize GitHub mutator: {exception.Message}");
            return 1;
        }

        IReadOnlyList<string> currentLabels;
        try
        {
            currentLabels = mutator
                .ReadLabels(repo!, GhCliGitHubLabelMutator.Kinds.Pr, pr!.Value)
                .Select(label => label.Name)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .ToArray();
        }
        catch (Exception exception) when (
            exception is InvalidOperationException
            or IOException)
        {
            writer.WriteLine($"failed to read current labels for PR #{pr} in {repo}: {exception.Message}");
            return 1;
        }

        var removeLabels = ResolveRemoveLabelsForMode(transition!, mode, plan.RemoveLabels, currentLabels);

        // G834: the cross-runtime review gate. PRs outside every declared
        // team's repositories take none of this path, so their result output is
        // byte-identical; a PR that resolves to an undeclared team is also
        // unchanged. A refusal returns before the write block: no label changes
        // and the CI wait is kept.
        CrossRuntimeReviewTransitionOutcome? crossRuntimeReview = null;
        SoloConductorReviewTransitionOutcome? soloConductorReview = null;
        var approvedGate = string.Equals(transition, TransitionApproved, StringComparison.Ordinal)
            ? EvaluateApprovedTransition(context, repo!, pr!.Value, headSha, executionUnit)
            : ApprovedTransitionEvaluation.NotApplicable;
        crossRuntimeReview = approvedGate.CrossRuntimeReview;
        soloConductorReview = approvedGate.SoloConductorReview;
        if (approvedGate.Refused)
        {
            var refusalCause = soloConductorReview?.Cause ?? crossRuntimeReview?.Cause ?? "review-approval-refused";
            var refusalDetail = soloConductorReview?.Detail ?? crossRuntimeReview?.Detail ?? "The approved transition could not be verified.";
            var refusedResult = new AutomationPrTransitionResult
            {
                Repo = repo!,
                Pr = pr!.Value,
                Transition = transition!,
                Mode = mode,
                Applied = false,
                AddLabels = Array.Empty<string>(),
                RemoveLabels = Array.Empty<string>(),
                CurrentLabels = currentLabels,
                Summary = BuildRefusalSummary(transition!, pr!.Value, repo!, refusalCause),
                Error = $"{refusalCause}: {refusalDetail}",
                CrossRuntimeReview = crossRuntimeReview,
                SoloConductorReview = soloConductorReview,
            };

            if (string.Equals(format, FormatJson, StringComparison.Ordinal))
            {
                writer.WriteLine(JsonSerializer.Serialize(refusedResult, JsonOptions));
            }
            else
            {
                WriteText(writer, refusedResult);
            }

            return 1;
        }

        var applied = false;
        var mayHaveApplied = false;
        IReadOnlyList<string>? intendedLabels = null;
        string? recoveryCommand = null;
        string? failureMessage = null;

        if (string.Equals(mode, WorkerClaimCompleteConstants.Modes.Write, StringComparison.Ordinal))
        {
            if (string.Equals(transition, TransitionRequestUpdate, StringComparison.Ordinal))
            {
                // G535 review repair: request-update must never apply as
                // sequential add/remove gh calls — a failure between the
                // two would leave a half-transitioned PR (e.g. rereview-ready
                // removed but request-update not yet added, or vice versa).
                // Compute the full desired label set (every current label
                // minus the ones actually being superseded, plus the ones
                // being added) and replace it atomically in one GitHub
                // request via IGitHubLabelSetReplacer.
                if (mutator is not IGitHubLabelSetReplacer replacer)
                {
                    writer.WriteLine(
                        "the configured GitHub mutator does not support atomic label-set replacement, "
                        + "required for the request-update transition (G535).");
                    return 1;
                }

                var removeLabelSet = new HashSet<string>(removeLabels, StringComparer.Ordinal);
                var desiredLabels = currentLabels
                    .Where(label => !removeLabelSet.Contains(label))
                    .Concat(plan.AddLabels)
                    .Distinct(StringComparer.Ordinal)
                    .ToArray();
                intendedLabels = desiredLabels.OrderBy(label => label, StringComparer.Ordinal).ToArray();

                try
                {
                    // The replacer itself treats an already-converged desired
                    // set as a genuine no-op (zero GitHub calls) — see
                    // GhCliGitHubLabelMutator.ReplaceLabelSet.
                    replacer.ReplaceLabelSet(
                        repo!, GhCliGitHubLabelMutator.Kinds.Pr, pr!.Value, currentLabels, desiredLabels);
                    applied = true;
                }
                catch (LabelSetReplacementException replacementException)
                {
                    failureMessage = replacementException.Message;
                    if (replacementException.Certainty != LabelSetReplacementFailureCertainty.KnownUnapplied)
                    {
                        // G535 review repair: once the request may have
                        // reached GitHub (transmitted, or the PUT itself
                        // succeeded but verification could not confirm the
                        // result), this is NOT a "nothing changed" failure —
                        // report it as ambiguous and tell the operator
                        // exactly how to resolve it, never a false "safe"
                        // claim.
                        mayHaveApplied = true;
                        recoveryCommand =
                            $"gh {GhCliGitHubLabelMutator.Kinds.Pr} view {pr} --repo {repo} --json labels";
                    }
                }
                catch (Exception exception) when (
                    exception is InvalidOperationException
                    or IOException)
                {
                    failureMessage = exception.Message;
                }
            }
            else
            {
                try
                {
                    mutator.ApplyLabelTransitions(
                        repo!,
                        GhCliGitHubLabelMutator.Kinds.Pr,
                        pr!.Value,
                        plan.AddLabels,
                        removeLabels);
                    applied = true;
                }
                catch (Exception exception) when (
                    exception is InvalidOperationException
                    or IOException)
                {
                    failureMessage = exception.Message;
                }
            }
        }

        if (failureMessage is not null)
        {
            var failureResult = new AutomationPrTransitionResult
            {
                Repo = repo!,
                Pr = pr!.Value,
                Transition = transition!,
                Mode = mode,
                Applied = false,
                AddLabels = plan.AddLabels,
                RemoveLabels = removeLabels,
                CurrentLabels = currentLabels,
                Summary = BuildSummary(transition!, plan.AddLabels, removeLabels),
                MayHaveApplied = mayHaveApplied,
                IntendedLabels = mayHaveApplied ? intendedLabels : null,
                RecoveryCommand = recoveryCommand,
                Error = mayHaveApplied
                    ? $"failed to confirm PR transition on PR #{pr} in {repo} (the mutation may already have applied — do not assume it did not): {failureMessage}"
                    : $"failed to apply PR transition on PR #{pr} in {repo}: {failureMessage}",
            };

            if (string.Equals(format, FormatJson, StringComparison.Ordinal))
            {
                writer.WriteLine(JsonSerializer.Serialize(failureResult, JsonOptions));
            }
            else
            {
                WriteText(writer, failureResult);
            }

            return 1;
        }

        var ciWaitCleared = false;
        string? ciWaitWarning = null;
        if (applied && string.Equals(mode, WorkerClaimCompleteConstants.Modes.Write, StringComparison.Ordinal))
        {
            var clear = CiWaitStore.ClearForTransition(context.RepoRoot, repo!, pr!.Value, transition!, write: true);
            ciWaitCleared = clear.Applied || clear.AlreadyConverged;
            if (clear.Error is not null)
            {
                // The GitHub label transition is already authoritative. Do
                // not claim it was rolled back; surface the durable-store
                // repair instead so the next wake can retry the clear.
                ciWaitWarning = $"PR transition applied, but durable CI wait could not be cleared: {clear.Error}";
            }
        }

        var result = new AutomationPrTransitionResult
        {
            Repo = repo!,
            Pr = pr!.Value,
            Transition = transition!,
            Mode = mode,
            Applied = applied,
            AddLabels = plan.AddLabels,
            RemoveLabels = removeLabels,
            CurrentLabels = currentLabels,
            Summary = BuildSummary(transition!, plan.AddLabels, removeLabels),
            CiWaitCleared = ciWaitCleared,
            CiWaitWarning = ciWaitWarning,
            CrossRuntimeReview = crossRuntimeReview,
            SoloConductorReview = soloConductorReview,
        };

        if (string.Equals(format, FormatJson, StringComparison.Ordinal))
        {
            writer.WriteLine(JsonSerializer.Serialize(result, JsonOptions));
        }
        else
        {
            WriteText(writer, result);
        }

        return 0;
    }

    private static ApprovedTransitionEvaluation EvaluateApprovedTransition(
        CliContext context,
        string repo,
        int pr,
        string? headSha,
        string? executionUnit)
    {
        TeamModeState? modeState;
        try
        {
            modeState = TeamModeStore.TryRead(context.RepoRoot);
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException or UnauthorizedAccessException or NullReferenceException)
        {
            return ApprovedTransitionEvaluation.Failure(
                solo: SoloRefused(
                    SoloConductorApprovalGate.CauseApplicabilityUnresolved,
                    $"recorded team mode could not be read or validated: {exception.Message} Repair `.intent-cli/team-mode.json` through `intent-cli team-mode`; approval applicability is unresolved."),
                cause: SoloConductorApprovalGate.CauseApplicabilityUnresolved);
        }

        var hostHasSoloEntries = modeState?.Entries.Any(entry => TeamMode.IsSoloConductor(entry.Mode)) == true;
        var g834Repo = context.Config.CrossRuntimeReview.IsGatedRepo(repo);
        if (!hostHasSoloEntries && !g834Repo)
        {
            return ApprovedTransitionEvaluation.NotApplicable;
        }

        var resolution = CrossRuntimeReviewTeamResolver.Resolve(
            context.RepoRoot,
            repo,
            pr,
            executionUnit,
            "pass the linked unit to `intent-cli automation pr-transition` with `--execution-unit <unit>`");
        if (!resolution.Resolved)
        {
            var crossRuntimeRefusal = g834Repo
                ? Outcome("refused", resolution.Cause!, $"{resolution.Detail} Fix: {resolution.Fix}", resolution, [])
                : null;
            if (!hostHasSoloEntries)
            {
                return ApprovedTransitionEvaluation.Failure(crossRuntime: crossRuntimeRefusal, cause: resolution.Cause);
            }

            var soloRefusal = SoloRefused(
                SoloConductorApprovalGate.CauseApplicabilityUnresolved,
                $"The host records solo-conductor teams, but PR #{pr} in {repo} could not be authoritatively resolved to determine whether this transition is solo-conductor: {resolution.Detail} Fix: {resolution.Fix}",
                resolution,
                headSha);
            return ApprovedTransitionEvaluation.Failure(soloRefusal, crossRuntimeRefusal, soloRefusal.Cause);
        }

        TeamModeResolution? modeResolution = null;
        if (hostHasSoloEntries)
        {
            try
            {
                modeResolution = TeamModeStore.Resolve(modeState, resolution.Domain!, resolution.Team);
            }
            catch (InvalidOperationException exception)
            {
                var soloRefusal = SoloRefused(
                    SoloConductorApprovalGate.CauseApplicabilityUnresolved,
                    $"The resolved team mode for '{resolution.Domain}/{resolution.Team}' is ambiguous or invalid: {exception.Message}",
                    resolution,
                    headSha);
                return ApprovedTransitionEvaluation.Failure(soloRefusal, cause: soloRefusal.Cause);
            }
        }

        if (modeResolution?.IsSoloConductor == true)
        {
            return EvaluateSoloConductorApproval(context, repo, pr, headSha, resolution, g834Repo);
        }

        if (!g834Repo)
        {
            return ApprovedTransitionEvaluation.NotApplicable;
        }

        // No solo entries use the historical G834 path verbatim. When a
        // different team is resolved on a solo-configured host, reuse that
        // authoritative resolution rather than parsing queue/packet/claim twice.
        var crossRuntime = EvaluateCrossRuntimeReviewGate(
            context,
            repo,
            pr,
            headSha,
            executionUnit,
            resolution);
        if (crossRuntime.Refusal is not null)
        {
            return ApprovedTransitionEvaluation.Failure(crossRuntime: crossRuntime.Refusal, cause: crossRuntime.Refusal.Cause);
        }

        return ApprovedTransitionEvaluation.Success(crossRuntime: crossRuntime.Satisfied);
    }

    private static ApprovedTransitionEvaluation EvaluateSoloConductorApproval(
        CliContext context,
        string repo,
        int pr,
        string? headSha,
        CrossRuntimeReviewResolution resolution,
        bool g834Repo)
    {
        if (string.IsNullOrWhiteSpace(headSha))
        {
            var g834 = g834Repo
                ? EvaluateCrossRuntimeReviewGate(context, repo, pr, headSha, resolution.ExecutionUnit, resolution)
                : CrossRuntimeReviewGateEvaluation.Empty;
            var soloRefusal = SoloRefused(
                SoloConductorApprovalGate.CauseHeadRequired,
                $"team '{resolution.Domain}/{resolution.Team}' is recorded as solo-conductor; pass `--head-sha <full-head-sha>` for the exact current PR head before review reads.",
                resolution,
                headSha,
                g834.ObservedHead);
            return ApprovedTransitionEvaluation.Failure(soloRefusal, g834.Refusal, soloRefusal.Cause);
        }

        CrossRuntimeReviewGateEvaluation localEvaluation = CrossRuntimeReviewGateEvaluation.Empty;
        CrossRuntimeReviewReadResult localReviews;
        CrossRuntimeReviewGateResult? localGate = null;
        string? observedHead;
        var hasG834Declaration = g834Repo
            && context.Config.CrossRuntimeReview.TryGetDeclared(resolution.Domain, resolution.Team, out _);
        if (hasG834Declaration)
        {
            localEvaluation = EvaluateCrossRuntimeReviewGate(context, repo, pr, headSha, resolution.ExecutionUnit, resolution);
            observedHead = localEvaluation.ObservedHead;
            if (localEvaluation.Refusal is not null)
            {
                var cause = localEvaluation.Refusal.Cause switch
                {
                    CrossRuntimeReviewCauses.HeadRequired => SoloConductorApprovalGate.CauseHeadRequired,
                    CrossRuntimeReviewCauses.HeadStale => observedHead is null
                        ? "review-head-unavailable"
                        : SoloConductorApprovalGate.CauseHeadChanged,
                    _ => localEvaluation.Gate?.Decision == CrossRuntimeReviewGate.DecisionBlocked
                        ? SoloConductorApprovalGate.CauseLocalGateBlocked
                        : SoloConductorApprovalGate.CauseLocalGateMissing,
                };
                var soloRefusal = SoloRefused(cause, localEvaluation.Refusal.Detail ?? "The existing G834 local gate is not satisfied.", resolution, headSha, observedHead);
                return ApprovedTransitionEvaluation.Failure(soloRefusal, localEvaluation.Refusal, cause);
            }

            localReviews = localEvaluation.LocalReviews ?? new CrossRuntimeReviewReadResult([], []);
            localGate = localEvaluation.Gate;
        }
        else
        {
            if (!TryReadCurrentHead(repo, pr, out observedHead, out var headError))
            {
                var soloRefusal = SoloRefused(
                    "review-head-unavailable",
                    $"the current head of PR #{pr} in {repo} could not be read before the posted review inventory: {headError}",
                    resolution,
                    headSha);
                return ApprovedTransitionEvaluation.Failure(soloRefusal, cause: soloRefusal.Cause);
            }
            if (!string.Equals(observedHead, headSha, StringComparison.OrdinalIgnoreCase))
            {
                var soloRefusal = SoloRefused(
                    SoloConductorApprovalGate.CauseHeadChanged,
                    $"--head-sha {headSha} is not the current head of PR #{pr} in {repo} ({observedHead}); review and CI must bind to the current head.",
                    resolution,
                    headSha,
                    observedHead);
                return ApprovedTransitionEvaluation.Failure(soloRefusal, cause: soloRefusal.Cause);
            }

            try
            {
                localReviews = CrossRuntimeReviewStore.Read(context.RepoRoot, repo, pr);
            }
            catch (Exception exception) when (exception is InvalidOperationException or IOException or UnauthorizedAccessException)
            {
                var soloRefusal = SoloRefused(
                    SoloConductorApprovalGate.CauseInvalidEvidence,
                    $"local canonical review records could not be read for citation validation: {exception.Message}",
                    resolution,
                    headSha,
                    observedHead);
                return ApprovedTransitionEvaluation.Failure(soloRefusal, cause: soloRefusal.Cause);
            }
        }

        var readRequest = new SoloConductorReviewReadRequest
        {
            Repo = repo,
            PullRequest = pr,
            ExecutionUnit = resolution.ExecutionUnit!,
            Domain = resolution.Domain!,
            Team = resolution.Team!,
            LocalReviews = localReviews,
        };
        SoloConductorReviewReadResult reviewRead;
        try
        {
            reviewRead = (SoloReviewReader ?? (request => new SoloConductorReviewReader().Read(request)))(readRequest);
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException or ArgumentException)
        {
            reviewRead = SoloConductorReviewReadResult.Failed(
                SoloConductorReviewReader.CauseReadUnavailable,
                $"GitHub PR reviews for {repo}#{pr} could not be read: {exception.Message}");
        }

        if (!reviewRead.Complete)
        {
            var soloRefusal = SoloRefused(
                reviewRead.Cause ?? SoloConductorReviewReader.CauseReadUnavailable,
                reviewRead.Detail ?? "The complete GitHub PR review inventory is unavailable.",
                resolution,
                headSha,
                observedHead);
            return ApprovedTransitionEvaluation.Failure(soloRefusal, localEvaluation.Satisfied, soloRefusal.Cause);
        }

        var approval = SoloConductorApprovalGate.Evaluate(
            reviewRead.Rows,
            resolution.ExecutionUnit!,
            headSha,
            localGate);
        var soloOutcome = SoloOutcome(approval, resolution, headSha, observedHead);
        if (approval.Decision != SoloConductorApprovalGate.DecisionSatisfied)
        {
            return ApprovedTransitionEvaluation.Failure(soloOutcome, localEvaluation.Satisfied, approval.Cause);
        }

        if (!TryReadCurrentHead(repo, pr, out var finalHead, out var finalHeadError))
        {
            var soloRefusal = SoloRefused(
                "review-head-unavailable",
                $"the current head of PR #{pr} in {repo} could not be re-read after review evaluation and before label write: {finalHeadError}",
                resolution,
                headSha,
                observedHead,
                approval);
            return ApprovedTransitionEvaluation.Failure(soloRefusal, localEvaluation.Satisfied, soloRefusal.Cause);
        }
        if (!string.Equals(finalHead, headSha, StringComparison.OrdinalIgnoreCase))
        {
            var soloRefusal = SoloRefused(
                SoloConductorApprovalGate.CauseHeadChanged,
                $"PR #{pr} in {repo} moved from --head-sha {headSha} to {finalHead} after review evaluation; re-review and rerun before changing labels.",
                resolution,
                headSha,
                finalHead,
                approval);
            return ApprovedTransitionEvaluation.Failure(soloRefusal, localEvaluation.Satisfied, soloRefusal.Cause);
        }

        soloOutcome = soloOutcome with { ObservedHeadSha = finalHead };
        return ApprovedTransitionEvaluation.Success(localEvaluation.Satisfied, soloOutcome);
    }

    /// <summary>
    /// G834: resolve once, bind the local gate to the observed current head,
    /// and return the exact local records used so G856 can validate citations.
    /// </summary>
    private static CrossRuntimeReviewGateEvaluation EvaluateCrossRuntimeReviewGate(
        CliContext context,
        string repo,
        int pr,
        string? headSha,
        string? executionUnit,
        CrossRuntimeReviewResolution? resolved = null)
    {
        var resolution = resolved ?? CrossRuntimeReviewTeamResolver.Resolve(
            context.RepoRoot,
            repo,
            pr,
            executionUnit,
            "pass the linked unit to `intent-cli automation pr-transition` with `--execution-unit <unit>`");
        if (!resolution.Resolved)
        {
            return CrossRuntimeReviewGateEvaluation.Refused(
                Outcome("refused", resolution.Cause!, $"{resolution.Detail} Fix: {resolution.Fix}", resolution, []));
        }

        if (!context.Config.CrossRuntimeReview.TryGetDeclared(resolution.Domain, resolution.Team, out var declaration))
        {
            return CrossRuntimeReviewGateEvaluation.Empty;
        }

        if (string.IsNullOrWhiteSpace(headSha))
        {
            return CrossRuntimeReviewGateEvaluation.Refused(Outcome("refused", CrossRuntimeReviewCauses.HeadRequired,
                $"team '{resolution.Domain}/{resolution.Team}' declares cross-runtime review; pass `--head-sha <head-sha>` with the exact head the reviewers approved.",
                resolution, []));
        }

        if (!TryReadCurrentHead(repo, pr, out var currentHead, out var headError))
        {
            return CrossRuntimeReviewGateEvaluation.Refused(Outcome("refused", CrossRuntimeReviewCauses.HeadStale,
                $"the current head of PR #{pr} in {repo} could not be read, so --head-sha {headSha} cannot be confirmed: {headError}",
                resolution, []), observedHead: null);
        }

        if (!string.Equals(currentHead, headSha, StringComparison.OrdinalIgnoreCase))
        {
            return CrossRuntimeReviewGateEvaluation.Refused(Outcome("refused", CrossRuntimeReviewCauses.HeadStale,
                $"--head-sha {headSha} is not the current head of PR #{pr} in {repo} ({currentHead}); review and CI must bind to the current head.",
                resolution, []), observedHead: currentHead);
        }

        var localReviews = CrossRuntimeReviewStore.Read(context.RepoRoot, repo, pr);
        var gate = CrossRuntimeReviewGate.Evaluate(declaration, resolution, headSha, localReviews);
        return gate.Passes
            ? CrossRuntimeReviewGateEvaluation.SatisfiedEvaluation(
                Outcome(gate.Decision, null, null, resolution, gate.Reasons), gate, localReviews, currentHead)
            : CrossRuntimeReviewGateEvaluation.Refused(
                Outcome(gate.Decision, gate.PrimaryCause, string.Join(" ", gate.Reasons.Select(reason => $"[{reason.Cause}] {reason.Detail}")), resolution, gate.Reasons),
                gate,
                localReviews,
                currentHead);
    }

    private static bool TryReadCurrentHead(string repo, int pr, out string? currentHead, out string? error)
    {
        currentHead = null;
        error = null;
        try
        {
            currentHead = (PrHeadReader ?? GhCliGitHubLabelMutator.ReadPullRequestHeadSha)(repo, pr).Trim();
            return true;
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException)
        {
            error = exception.Message;
            return false;
        }
    }

    private static SoloConductorReviewTransitionOutcome SoloOutcome(
        SoloConductorApprovalEvaluation evaluation,
        CrossRuntimeReviewResolution resolution,
        string expectedHead,
        string? observedHead) => new()
        {
            Decision = evaluation.Decision,
            Cause = evaluation.Cause,
            Detail = evaluation.Detail,
            ExecutionUnit = resolution?.ExecutionUnit,
            Domain = resolution?.Domain,
            Team = resolution?.Team,
            ExpectedHeadSha = expectedHead,
            ObservedHeadSha = observedHead,
            QualifyingReviews = evaluation.QualifyingReviews,
            Obligations = evaluation.Obligations,
            SupersededInvalidReviewIds = evaluation.SupersededInvalidReviewIds,
            UnscopableReviewIds = evaluation.UnscopableReviewIds,
            RepairUnavailableReason = evaluation.RepairUnavailableReason,
        };

    private static SoloConductorReviewTransitionOutcome SoloRefused(
        string cause,
        string detail,
        CrossRuntimeReviewResolution? resolution = null,
        string? expectedHead = null,
        string? observedHead = null,
        SoloConductorApprovalEvaluation? evaluation = null) => new()
        {
            Decision = SoloConductorApprovalGate.DecisionRefused,
            Cause = cause,
            Detail = detail,
            ExecutionUnit = resolution?.ExecutionUnit,
            Domain = resolution?.Domain,
            Team = resolution?.Team,
            ExpectedHeadSha = expectedHead,
            ObservedHeadSha = observedHead,
            QualifyingReviews = evaluation?.QualifyingReviews ?? [],
            Obligations = evaluation?.Obligations ?? [],
            SupersededInvalidReviewIds = evaluation?.SupersededInvalidReviewIds ?? [],
            UnscopableReviewIds = evaluation?.UnscopableReviewIds ?? [],
            RepairUnavailableReason = evaluation?.RepairUnavailableReason,
        };

    private static CrossRuntimeReviewTransitionOutcome Outcome(
        string decision,
        string? cause,
        string? detail,
        CrossRuntimeReviewResolution resolution,
        IReadOnlyList<CrossRuntimeReviewGateReason> reasons) =>
        new()
        {
            Decision = decision,
            Cause = cause,
            Detail = detail,
            Reasons = reasons,
            ExecutionUnit = resolution.ExecutionUnit,
            Domain = resolution.Domain,
            Team = resolution.Team,
        };

    /// <summary>
    /// G824: the add/remove label sets each transition executes, exposed so
    /// automation summary reports exactly what the command applies instead of
    /// a hand-maintained copy.
    /// </summary>
    internal static (IReadOnlyList<string> AddLabels, IReadOnlyList<string> RemoveLabels) PlannedLabels(string transition)
    {
        var plan = PlanTransition(transition);
        return (plan.AddLabels, plan.RemoveLabels);
    }

    private static TransitionPlan PlanTransition(string transition) =>
        transition switch
        {
            TransitionReviewStart => new TransitionPlan(
                AddLabels:
                [
                    WorkerNextActionConstants.Labels.IntentTarget,
                    WorkerPrReviewPreflightConstants.Labels.IntentPrReviewing,
                ],
                RemoveLabels:
                [
                    WorkerNextActionConstants.Labels.IntentPrRereviewReady,
                    "rereview-ready",
                ]),
            // G503: approved is the terminal review state — it supersedes
            // rereview-ready ("waiting for another review pass") and clears the
            // other active review-state labels so a merged/approved PR never
            // visibly carries both intent-pr-approved and a stale in-flight
            // review label. Removing an absent label is a no-op (the write path
            // filters to present labels), so the transition stays idempotent.
            TransitionApproved => new TransitionPlan(
                AddLabels:
                [
                    WorkerPrReviewPreflightConstants.Labels.IntentPrApproved,
                ],
                RemoveLabels:
                [
                    WorkerPrReviewPreflightConstants.Labels.IntentPrReviewing,
                    WorkerNextActionConstants.Labels.IntentPrRereviewReady,
                    "rereview-ready",
                    WorkerPrReviewPreflightConstants.Labels.IntentPrRequestUpdate,
                    WorkerPrReviewPreflightConstants.Labels.IntentPrUpdateInProgress,
                ]),
            // G535: request-update supersedes a stale intent-pr-rereview-ready
            // in the SAME write. Field finding #5 (SKS-G824 / PR #1760): a
            // design amendment arriving while a PR is rereview-ready
            // previously left rereview-ready in place after request-update,
            // producing a PR `worker claim` correctly refuses (rereview-ready
            // is the reviewer's to pick up) — a canonical-state deadlock with
            // no installed command able to proceed. A repair request always
            // supersedes pending rereview-readiness, so rereview-ready (and
            // its legacy string form) is cleared alongside the pre-existing
            // intent-pr-reviewing removal.
            // G824 (#1782): a repair request issued after an approval withdraws
            // that approval. approved already supersedes request-update; without
            // the reverse, a corrected approval left both labels on the PR and
            // the child loop waited on approved-or-merged with no canonical
            // recovery. The G535 atomic replacement removes it in the same write.
            TransitionRequestUpdate => new TransitionPlan(
                AddLabels:
                [
                    WorkerPrReviewPreflightConstants.Labels.IntentPrRequestUpdate,
                ],
                RemoveLabels:
                [
                    WorkerPrReviewPreflightConstants.Labels.IntentPrReviewing,
                    WorkerNextActionConstants.Labels.IntentPrRereviewReady,
                    "rereview-ready",
                    WorkerPrReviewPreflightConstants.Labels.IntentPrApproved,
                ]),
            // G292: release the reviewer lease without claiming an
            // implementation-side repair is needed. Removes
            // intent-pr-reviewing (and any stale intent-pr-update-in-progress
            // / intent-pr-request-update leftovers) without adding any new
            // review-side label, so the next host wake reselects the PR.
            TransitionReviewRelease => new TransitionPlan(
                AddLabels: Array.Empty<string>(),
                RemoveLabels:
                [
                    WorkerPrReviewPreflightConstants.Labels.IntentPrReviewing,
                ]),
            _ => throw new ArgumentOutOfRangeException(nameof(transition), transition, "Unsupported PR transition."),
        };

    private static IReadOnlyList<string> ResolveRemoveLabelsForMode(
        string transition,
        string mode,
        IReadOnlyList<string> plannedRemoveLabels,
        IReadOnlyList<string> currentLabels)
    {
        // G535 review repair: request-update's audit output must be
        // truthful in BOTH modes, never other transitions'. A PR carrying
        // only intent-pr-rereview-ready must not be reported (dry-run OR
        // write) as also superseding an absent intent-pr-reviewing or
        // legacy "rereview-ready" — that would claim a removal that never
        // happens. Filter to present-only regardless of mode, computed
        // from the already-fetched current labels (no extra GitHub call).
        if (string.Equals(transition, TransitionRequestUpdate, StringComparison.Ordinal))
        {
            var requestUpdateCurrentLabelSet = new HashSet<string>(currentLabels, StringComparer.Ordinal);
            return plannedRemoveLabels
                .Where(label => requestUpdateCurrentLabelSet.Contains(label))
                .ToArray();
        }

        // G292: review-release is also defensive about stale labels — only
        // remove labels that are actually present, so the dry-run plan
        // matches what gh will actually mutate.
        // G503: approved joins this set so clearing rereview-ready /
        // request-update / update-in-progress stays idempotent when those
        // labels are absent.
        // Unlike request-update above, these three transitions intentionally
        // report the FULL planned removal set in dry-run (pre-existing,
        // unchanged behavior) and only filter to present-only in --write.
        if (string.Equals(mode, WorkerClaimCompleteConstants.Modes.Write, StringComparison.Ordinal)
            && (string.Equals(transition, TransitionReviewStart, StringComparison.Ordinal)
                || string.Equals(transition, TransitionReviewRelease, StringComparison.Ordinal)
                || string.Equals(transition, TransitionApproved, StringComparison.Ordinal)))
        {
            var currentLabelSet = new HashSet<string>(currentLabels, StringComparer.Ordinal);
            return plannedRemoveLabels
                .Where(label => currentLabelSet.Contains(label))
                .ToArray();
        }

        return plannedRemoveLabels;
    }

    private static bool TryParseArguments(
        string[] args,
        out string? repo,
        out string? workdir,
        out int? pr,
        out string? transition,
        out string mode,
        out string format,
        out string? headSha,
        out string? executionUnit,
        out string error)
    {
        headSha = null;
        executionUnit = null;
        repo = null;
        workdir = null;
        pr = null;
        transition = null;
        mode = WorkerClaimCompleteConstants.Modes.DryRun;
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

                case "--pr":
                    if (!TryReadPositiveInt(args, index, "--pr", out var prNumber, out error))
                    {
                        return false;
                    }
                    pr = prNumber;
                    index++;
                    break;

                case "--transition":
                    if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                    {
                        error = "--transition requires a value (review-start, request-update, approved, or review-release).";
                        return false;
                    }
                    transition = args[index + 1].Trim();
                    if (!string.Equals(transition, TransitionReviewStart, StringComparison.Ordinal)
                        && !string.Equals(transition, TransitionRequestUpdate, StringComparison.Ordinal)
                        && !string.Equals(transition, TransitionApproved, StringComparison.Ordinal)
                        && !string.Equals(transition, TransitionReviewRelease, StringComparison.Ordinal))
                    {
                        error = $"--transition must be '{TransitionReviewStart}', '{TransitionRequestUpdate}', '{TransitionApproved}', or '{TransitionReviewRelease}' (got '{transition}').";
                        return false;
                    }
                    index++;
                    break;

                case "--head-sha":
                    if (index + 1 >= args.Length || !CrossRuntimeReviewPaths.IsFullHeadSha(args[index + 1]))
                    {
                        error = "--head-sha requires the PR's full 40-character hexadecimal head SHA (G834).";
                        return false;
                    }
                    headSha = args[index + 1];
                    index++;
                    break;

                case "--execution-unit":
                    if (index + 1 >= args.Length
                        || !KnowledgeWriteBackRecord.TryValidateExecutionUnit(args[index + 1], out var unitError))
                    {
                        error = "--execution-unit requires a canonical execution unit id (G834).";
                        return false;
                    }
                    executionUnit = args[index + 1];
                    index++;
                    break;

                case "--write":
                    mode = WorkerClaimCompleteConstants.Modes.Write;
                    break;

                case "--dry-run":
                    mode = WorkerClaimCompleteConstants.Modes.DryRun;
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
                    error = $"Unknown argument '{argument}'. Supported: [--repo <owner/repo>] [--workdir <path>] --pr <n> --transition <review-start|request-update|approved|review-release> [--head-sha <sha>] [--execution-unit <unit>] [--write] [--dry-run] [--format text|json].";
                    return false;
            }
        }

        if (pr is null)
        {
            error = "--pr is required.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(transition))
        {
            error = "--transition is required (review-start, request-update, approved, or review-release).";
            return false;
        }

        return true;
    }

    private static bool TryReadPositiveInt(
        string[] args,
        int index,
        string option,
        out int value,
        out string error)
    {
        value = 0;
        error = string.Empty;
        if (index + 1 >= args.Length
            || !int.TryParse(args[index + 1], System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture, out value)
            || value <= 0)
        {
            error = $"{option} requires a positive integer.";
            return false;
        }
        return true;
    }

    private static string BuildSummary(
        string transition,
        IReadOnlyCollection<string> addLabels,
        IReadOnlyCollection<string> removeLabels)
    {
        var addPart = addLabels.Count == 0 ? "(none)" : string.Join(", ", addLabels);
        var removePart = removeLabels.Count == 0 ? "(none)" : string.Join(", ", removeLabels);
        return $"Would apply host PR transition '{transition}': add {addPart}; remove {removePart}.";
    }

    private static string BuildRefusalSummary(string transition, int pr, string repo, string? cause) =>
        $"Refused host PR transition '{transition}' on PR #{pr} in {repo}: "
        + $"{(string.IsNullOrWhiteSpace(cause) ? "(cause unrecorded)" : cause)}. No labels were changed.";

    private static void WriteText(TextWriter writer, AutomationPrTransitionResult result)
    {
        writer.WriteLine(result.Error ?? result.Summary);
        writer.WriteLine($"mode: {result.Mode}");
        writer.WriteLine($"repo: {result.Repo}");
        writer.WriteLine($"pr: {result.Pr.ToString(System.Globalization.CultureInfo.InvariantCulture)}");
        writer.WriteLine($"applied: {result.Applied.ToString().ToLowerInvariant()}");
        writer.WriteLine($"ci_wait_cleared: {result.CiWaitCleared.ToString().ToLowerInvariant()}");
        if (result.CiWaitWarning is not null)
        {
            writer.WriteLine($"ci_wait_warning: {result.CiWaitWarning}");
        }

        if (result.CrossRuntimeReview is not null)
        {
            writer.WriteLine($"cross_runtime_review: {result.CrossRuntimeReview.Decision}"
                + (result.CrossRuntimeReview.Cause is null ? string.Empty : $" ({result.CrossRuntimeReview.Cause})"));
        }

        if (result.SoloConductorReview is not null)
        {
            var solo = result.SoloConductorReview;
            writer.WriteLine($"solo_conductor_review: {solo.Decision}"
                + (solo.Cause is null ? string.Empty : $" ({solo.Cause})"));
            writer.WriteLine("solo_conductor_identity: "
                + $"execution_unit={solo.ExecutionUnit ?? "(unresolved)"} "
                + $"domain={solo.Domain ?? "(unresolved)"} "
                + $"team={solo.Team ?? "(unresolved)"}");
            writer.WriteLine("solo_conductor_head: "
                + $"expected={solo.ExpectedHeadSha ?? "(unavailable)"} "
                + $"observed={solo.ObservedHeadSha ?? "(unavailable)"}");
            foreach (var review in solo.QualifyingReviews)
            {
                writer.WriteLine("solo_conductor_qualifying_review: "
                    + $"review_id={review.ReviewId.ToString(System.Globalization.CultureInfo.InvariantCulture)} "
                    + $"login={review.Login} "
                    + $"url={review.Url ?? "(unavailable)"}"
                    + (review.Runtime is null ? string.Empty : $" runtime={review.Runtime}")
                    + (review.Relation is null ? string.Empty : $" relation={review.Relation}")
                    + (review.CitedRecordPath is null ? string.Empty : $" cited_record_path={review.CitedRecordPath}"));
            }

            foreach (var obligation in solo.Obligations)
            {
                writer.WriteLine("solo_conductor_obligation: "
                    + $"kind={obligation.Kind} identity_kind={obligation.IdentityKind} "
                    + $"identity={obligation.Identity} review_ids={FormatReviewIds(obligation.ReviewIds)} "
                    + $"recovery={obligation.Recovery}");
            }

            writer.WriteLine($"solo_conductor_superseded_invalid_review_ids: {FormatReviewIds(solo.SupersededInvalidReviewIds)}");
            writer.WriteLine($"solo_conductor_unscopable_review_ids: {FormatReviewIds(solo.UnscopableReviewIds)}");
            if (solo.RepairUnavailableReason is not null)
            {
                writer.WriteLine($"solo_conductor_repair_unavailable_reason: {solo.RepairUnavailableReason}");
            }

            if (solo.UnscopableReviewIds.Count > 0)
            {
                writer.WriteLine("solo_conductor_recovery_guidance: no unrelated review or repost can repair missing review identity/order. "
                    + "Create a replacement PR from the same reviewed source through normal PR creation, fresh review, and canonical worker-complete linkage; "
                    + "this command does not create, relink, or close it.");
            }
        }

        // G535 review repair: phase-aware ambiguity reporting — only ever
        // emitted for a failed mutation whose outcome on GitHub is unknown.
        if (result.MayHaveApplied)
        {
            writer.WriteLine("may_have_applied: true");
            writer.WriteLine(
                $"intended_labels: {string.Join(", ", result.IntendedLabels ?? Array.Empty<string>())}");
            writer.WriteLine($"recovery_command: {result.RecoveryCommand}");
        }
    }

    private static string FormatReviewIds(IEnumerable<long?> values)
    {
        var ids = values.Select(value => value?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "unknown").ToArray();
        return ids.Length == 0 ? "(none)" : string.Join(",", ids);
    }

    private static string FormatReviewIds(IEnumerable<long> values)
    {
        var ids = values.Select(value => value.ToString(System.Globalization.CultureInfo.InvariantCulture)).ToArray();
        return ids.Length == 0 ? "(none)" : string.Join(",", ids);
    }

    private static void WriteHelp(TextWriter writer)
    {
        writer.WriteLine("automation pr-transition");
        writer.WriteLine("Usage: intent-cli automation pr-transition --repo <owner/repo> --pr <n> --transition <review-start|request-update|approved|review-release> [--head-sha <sha>] [--execution-unit <unit>] [--write] [--dry-run] [--format text|json]");
        writer.WriteLine("Supported transitions:");
        writer.WriteLine("- review-start");
        writer.WriteLine("- request-update");
        writer.WriteLine("- approved");
        writer.WriteLine("- review-release (G292: drop intent-pr-reviewing without adding intent-pr-request-update; use when host-owned metadata blocks closeout)");
        writer.WriteLine("G834: for a PR in a repository listed by [[cross_runtime_review.teams]], approved resolves the team from queue-state, packet, and claim; a declared team requires --head-sha <sha> equal to the PR's current head and a satisfied cross-runtime review gate (`intent-cli review cross-runtime status`). Run it from the host root. --execution-unit <unit> names the unit of a PR the host queue has not linked.");
    }

    private sealed record ApprovedTransitionEvaluation
    {
        public static ApprovedTransitionEvaluation NotApplicable { get; } = new();

        public bool Refused { get; init; }
        public string? Cause { get; init; }
        public CrossRuntimeReviewTransitionOutcome? CrossRuntimeReview { get; init; }
        public SoloConductorReviewTransitionOutcome? SoloConductorReview { get; init; }

        public static ApprovedTransitionEvaluation Failure(
            SoloConductorReviewTransitionOutcome? solo = null,
            CrossRuntimeReviewTransitionOutcome? crossRuntime = null,
            string? cause = null) => new()
            {
                Refused = true,
                Cause = cause,
                CrossRuntimeReview = crossRuntime,
                SoloConductorReview = solo,
            };

        public static ApprovedTransitionEvaluation Success(
            CrossRuntimeReviewTransitionOutcome? crossRuntime = null,
            SoloConductorReviewTransitionOutcome? solo = null) => new()
            {
                CrossRuntimeReview = crossRuntime,
                SoloConductorReview = solo,
            };
    }

    private sealed record CrossRuntimeReviewGateEvaluation
    {
        public static CrossRuntimeReviewGateEvaluation Empty { get; } = new();

        public CrossRuntimeReviewTransitionOutcome? Refusal { get; init; }
        public CrossRuntimeReviewTransitionOutcome? Satisfied { get; init; }
        public CrossRuntimeReviewGateResult? Gate { get; init; }
        public CrossRuntimeReviewReadResult? LocalReviews { get; init; }
        public string? ObservedHead { get; init; }

        public static CrossRuntimeReviewGateEvaluation Refused(
            CrossRuntimeReviewTransitionOutcome refusal,
            CrossRuntimeReviewGateResult? gate = null,
            CrossRuntimeReviewReadResult? localReviews = null,
            string? observedHead = null) => new()
            {
                Refusal = refusal,
                Gate = gate,
                LocalReviews = localReviews,
                ObservedHead = observedHead,
            };

        public static CrossRuntimeReviewGateEvaluation SatisfiedEvaluation(
            CrossRuntimeReviewTransitionOutcome satisfied,
            CrossRuntimeReviewGateResult gate,
            CrossRuntimeReviewReadResult localReviews,
            string? observedHead) => new()
            {
                Satisfied = satisfied,
                Gate = gate,
                LocalReviews = localReviews,
                ObservedHead = observedHead,
            };
    }

    private sealed record TransitionPlan(
        IReadOnlyList<string> AddLabels,
        IReadOnlyList<string> RemoveLabels);
}

internal sealed record AutomationPrTransitionResult
{
    [JsonPropertyName("repo")]
    public required string Repo { get; init; }

    [JsonPropertyName("pr")]
    public required int Pr { get; init; }

    [JsonPropertyName("transition")]
    public required string Transition { get; init; }

    [JsonPropertyName("mode")]
    public required string Mode { get; init; }

    [JsonPropertyName("applied")]
    public required bool Applied { get; init; }

    [JsonPropertyName("add_labels")]
    public required IReadOnlyList<string> AddLabels { get; init; }

    [JsonPropertyName("addLabels")]
    public IReadOnlyList<string> AddLabelsCamel => AddLabels;

    [JsonPropertyName("remove_labels")]
    public required IReadOnlyList<string> RemoveLabels { get; init; }

    [JsonPropertyName("removeLabels")]
    public IReadOnlyList<string> RemoveLabelsCamel => RemoveLabels;

    [JsonPropertyName("current_labels")]
    public required IReadOnlyList<string> CurrentLabels { get; init; }

    [JsonPropertyName("currentLabels")]
    public IReadOnlyList<string> CurrentLabelsCamel => CurrentLabels;

    [JsonPropertyName("summary")]
    public required string Summary { get; init; }

    [JsonPropertyName("ci_wait_cleared")]
    public bool CiWaitCleared { get; init; }

    [JsonPropertyName("ci_wait_warning")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? CiWaitWarning { get; init; }

    /// <summary>
    /// G535 review repair: true when a mutation attempt failed at a point
    /// where GitHub may already have applied it (the request was
    /// transmitted, or a post-write verification read failed/mismatched) —
    /// distinct from a failure known to have applied nothing at all (e.g.
    /// the `gh` process never started). Always false when <see cref="Applied"/>
    /// is true. Never omitted so callers can distinguish "definitely safe"
    /// (both false) from "must re-check" (this true).
    /// </summary>
    [JsonPropertyName("may_have_applied")]
    public bool MayHaveApplied { get; init; }

    [JsonPropertyName("mayHaveApplied")]
    public bool MayHaveAppliedCamel => MayHaveApplied;

    /// <summary>
    /// G535 review repair: the full label set the failed mutation was
    /// attempting to establish, so an operator re-reading current labels
    /// knows exactly what to compare against. Populated only alongside
    /// <see cref="MayHaveApplied"/>.
    /// </summary>
    [JsonPropertyName("intended_labels")]
    public IReadOnlyList<string>? IntendedLabels { get; init; }

    [JsonPropertyName("intendedLabels")]
    public IReadOnlyList<string>? IntendedLabelsCamel => IntendedLabels;

    /// <summary>
    /// G535 review repair: the exact command an operator should run to
    /// read the PR's actual current labels and resolve the ambiguity.
    /// Populated only alongside <see cref="MayHaveApplied"/>.
    /// </summary>
    [JsonPropertyName("recovery_command")]
    public string? RecoveryCommand { get; init; }

    [JsonPropertyName("recoveryCommand")]
    public string? RecoveryCommandCamel => RecoveryCommand;

    /// <summary>G535 review repair: the failure message, populated only when <see cref="Applied"/> is false.</summary>
    [JsonPropertyName("error")]
    public string? Error { get; init; }

    /// <summary>
    /// G834: present only for the approved transition of a PR in a gated
    /// repository whose team is declared, or whose resolution was refused.
    /// </summary>
    [JsonPropertyName("cross_runtime_review")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public CrossRuntimeReviewTransitionOutcome? CrossRuntimeReview { get; init; }

    [JsonPropertyName("solo_conductor_review")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public SoloConductorReviewTransitionOutcome? SoloConductorReview { get; init; }
}

internal sealed record SoloConductorReviewTransitionOutcome
{
    [JsonPropertyName("decision")]
    public required string Decision { get; init; }

    [JsonPropertyName("cause")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Cause { get; init; }

    [JsonPropertyName("detail")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Detail { get; init; }

    [JsonPropertyName("execution_unit")]
    public string? ExecutionUnit { get; init; }

    [JsonPropertyName("domain")]
    public string? Domain { get; init; }

    [JsonPropertyName("team")]
    public string? Team { get; init; }

    [JsonPropertyName("expected_head_sha")]
    public string? ExpectedHeadSha { get; init; }

    [JsonPropertyName("observed_head_sha")]
    public string? ObservedHeadSha { get; init; }

    [JsonPropertyName("qualifying_reviews")]
    public IReadOnlyList<SoloConductorQualifyingReview> QualifyingReviews { get; init; } = [];

    [JsonPropertyName("obligations")]
    public IReadOnlyList<SoloConductorReviewObligation> Obligations { get; init; } = [];

    [JsonPropertyName("superseded_invalid_review_ids")]
    public IReadOnlyList<long> SupersededInvalidReviewIds { get; init; } = [];

    [JsonPropertyName("unscopable_review_ids")]
    public IReadOnlyList<long?> UnscopableReviewIds { get; init; } = [];

    [JsonPropertyName("repair_unavailable_reason")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? RepairUnavailableReason { get; init; }
}

internal sealed record CrossRuntimeReviewTransitionOutcome
{
    [JsonPropertyName("decision")]
    public required string Decision { get; init; }

    [JsonPropertyName("cause")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Cause { get; init; }

    [JsonPropertyName("detail")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Detail { get; init; }

    [JsonPropertyName("reasons")]
    public required IReadOnlyList<CrossRuntimeReviewGateReason> Reasons { get; init; }

    [JsonPropertyName("execution_unit")]
    public string? ExecutionUnit { get; init; }

    [JsonPropertyName("domain")]
    public string? Domain { get; init; }

    [JsonPropertyName("team")]
    public string? Team { get; init; }
}
