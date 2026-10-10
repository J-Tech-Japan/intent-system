using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using IntentSystem.Cli.Infrastructure;
using IntentSystem.Cli.Models;
using IntentSystem.Supervisor.Models;
using IntentSystem.Supervisor.Serialization;
using YamlDotNet.RepresentationModel;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// The bounded G861 exception path. Only a satisfied declared G834 local gate
/// whose existing G856 result is exactly review-missing can prepare evidence.
/// The immutable prepared record is pushed before the one GitHub label action;
/// an observed record is pushed only after head and labels are read back.
/// </summary>
internal static class ApprovalPostingOverride
{
    private const string AuditRoot = ".intent-cli/approval-evidence-overrides";
    private const string Operation = "approved-posting-exception";
    private const string PreparedEvent = "approval-evidence-override-prepared";
    private const string ObservedEvent = "approval-evidence-override-observed";
    private const int MaximumPublicationAttempts = 2;

    private static readonly JsonSerializerOptions AuditJsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    private static readonly JsonSerializerOptions ResultJsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    internal sealed record WriterOperationsHooks(
        Func<string, Func<byte[]>, byte[]> SerializeAudit,
        Action<string, string, Action> AppendRunEvent)
    {
        internal static WriterOperationsHooks Default { get; } = new(
            static (_, serialize) => serialize(),
            static (_, _, append) => append());
    }

    internal static WriterOperationsHooks WriterOperations { get; set; } = WriterOperationsHooks.Default;

    internal static int WriteEarlyRefusal(
        ApprovalPostingOverrideRequest request,
        string cause,
        string detail,
        TextWriter writer) =>
        WriteResult(request, Facts(request, "refused", cause, detail), [], null, null, false, false, writer);

    public static int Execute(CliContext invokingContext, ApprovalPostingOverrideRequest request, TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(invokingContext);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(writer);

        CanonicalTarget target;
        try
        {
            var branch = ClaimCommand.ResolveRemoteDefaultBranch(invokingContext.RepoRoot);
            target = new CanonicalTarget(branch.Remote, branch.Name, branch.RemoteDefaultBranch);
        }
        catch (Exception exception) when (Expected(exception))
        {
            return WriteEarlyRefusal(request, "canonical-target-unavailable", exception.Message, writer);
        }

        var sticky = new ExecutionFacts();

        int WriteResultWithHistory(
            ApprovalPostingOverrideRequest resultRequest,
            ApprovalPostingOverrideResult overrideResult,
            IReadOnlyList<string> currentLabels,
            SoloConductorReviewTransitionOutcome? solo,
            CrossRuntimeReviewTransitionOutcome? crossRuntime,
            bool applied,
            bool ciWaitCleared,
            TextWriter resultWriter,
            string? ciWaitWarning = null,
            bool observedOnly = false) =>
            WriteResult(resultRequest, overrideResult, currentLabels, solo, crossRuntime, applied, ciWaitCleared,
                resultWriter, ciWaitWarning, observedOnly, sticky);

        try
        {
            using var initial = ApprovalPostingCheckout.Open(target);
            var first = Assess(initial, request);
            sticky.RecordAssessment(first);
            if (!first.Readable)
            {
                var historical = ReadHistoricalPairForReporting(initial, request);
                return WriteAssessmentRefusal(request, first, writer, historical);
            }

            var preparedPath = AuditPath(request.ExecutionUnit, request.OverrideId, "prepared.json");
            var observedPath = AuditPath(request.ExecutionUnit, request.OverrideId, "observed.json");
            sticky.RecordPaths(preparedPath, observedPath, first.RunPath);
            var pair = ReadPair(initial, first.RunPath!, request, preparedPath, observedPath);
            sticky.RecordPair(pair);
            if (!pair.Valid)
            {
                return WriteResultWithHistory(request, Facts(request, "refused", pair.Cause!, pair.Detail!,
                    preparedPublished: pair.PreparedExists,
                    outcomePublished: pair.ObservedExists,
                    preparedPath: pair.PreparedExists ? preparedPath : null,
                    observedPath: pair.ObservedExists ? observedPath : null,
                    runPath: first.RunPath,
                    preparedCommit: pair.PreparedCommit,
                    outcomeCommit: pair.ObservedCommit),
                    first.Labels, first.Solo, first.CrossRuntime, false, false, writer);
            }

            if (pair.Prepared is null && !first.MissingEligible)
                return WriteAssessmentRefusal(request, first, writer);
            if (pair.Prepared is not null && !Matches(pair.Prepared, first, request))
            {
                return WriteResultWithHistory(request, Facts(request, "refused", "override-binding-conflict",
                    "The existing prepared UUID is not bound to the current request, claim epoch, host layout, or deciding local review records.",
                    preparedPublished: true, outcomePublished: pair.Observed is not null,
                    preparedPath: preparedPath, observedPath: pair.Observed is null ? null : observedPath,
                    runPath: first.RunPath, preparedCommit: pair.PreparedCommit,
                    outcomeCommit: pair.ObservedCommit),
                    first.Labels, first.Solo, first.CrossRuntime, false, false, writer);
            }

            if (request.Mode == WorkerClaimCompleteConstants.Modes.DryRun)
            {
                if (pair.Observed is not null)
                {
                    IGitHubLabelMutator? previewMutator = null;
                    string? previewMutatorError = null;
                    string? previewHead = first.CurrentHead;
                    IReadOnlyList<string>? previewLabels = first.Labels;
                    string? previewReadError = null;
                    var historicalBindingCurrent = MatchesObserved(pair.Observed, pair.Prepared!, pair.PreparedSha256, first, request)
                        && pair.Observed.PreparedPublicationCommit == pair.PreparedCommit
                        && CanContinue(pair.Prepared!, first)
                        && Converged(first.Labels, pair.Prepared!.IntendedAddLabels, pair.Prepared.IntendedRemoveLabels);
                    var previewStateCurrent = historicalBindingCurrent
                        && TryGetMutator(out previewMutator, out previewMutatorError)
                        && TryObserve(request, previewMutator!, out previewHead, out previewLabels, out previewReadError)
                        && string.Equals(previewHead, request.HeadSha, StringComparison.OrdinalIgnoreCase)
                        && Converged(previewLabels ?? [], pair.Prepared!.IntendedAddLabels, pair.Prepared.IntendedRemoveLabels);
                    if (!previewStateCurrent)
                    {
                        var detail = previewMutatorError ?? previewReadError
                            ?? "The observed UUID is already consumed, but the exact requested head and intended labels are no longer converged.";
                        return WriteResultWithHistory(request, Facts(request, "refused", "override-binding-conflict", detail,
                            preparedPublished: true, outcomePublished: true,
                            preparedPath: preparedPath, observedPath: observedPath, runPath: first.RunPath,
                            preparedCommit: pair.PreparedCommit, outcomeCommit: pair.ObservedCommit,
                            currentHead: previewHead ?? first.CurrentHead),
                            previewLabels ?? first.Labels, first.Solo, first.CrossRuntime, false, false, writer);
                    }
                }
                return WriteResultWithHistory(request,
                    Facts(request, "eligible-preview",
                        pair.Prepared is null ? "eligible-missing-posting" : "prepared-binding-current",
                        pair.Prepared is null
                            ? "Only canonical posted approval for the typed missing relation is absent; preview made no durable or GitHub changes."
                            : "The immutable UUID binding remains compatible with current canonical evidence; preview made no changes.",
                        preparedPublished: pair.Prepared is not null,
                        outcomePublished: pair.Observed is not null,
                        preparedPath: pair.Prepared is null ? null : preparedPath,
                        observedPath: pair.Observed is null ? null : observedPath,
                        runPath: first.RunPath,
                        preparedCommit: pair.PreparedCommit,
                        outcomeCommit: pair.ObservedCommit,
                        currentHead: first.CurrentHead,
                    authorizationBasis: first.Basis),
                    first.Labels, first.Solo, first.CrossRuntime, false, false, writer);
            }

            var prepared = pair.Prepared;
            var preparedCommit = pair.PreparedCommit;
            if (prepared is null)
            {
                var publication = PublishPrepared(target, request);
                if (!publication.Published || publication.Audit is null)
                {
                    sticky.RecordPreparedPublication(publication);
                    var uncertainPublication = publication.Confidence == PublicationConfidence.Unconfirmed;
                    return WriteResultWithHistory(request, Facts(request, uncertainPublication ? "unresolved" : "refused",
                        publication.Cause ?? "prepared-publication-unconfirmed",
                        publication.Detail ?? "The prepared audit could not be confirmed on the canonical host branch.",
                        preparedPath: uncertainPublication ? publication.Path : null,
                        runPath: uncertainPublication ? publication.RunPath : null,
                        preparedCommit: uncertainPublication ? publication.Commit : null,
                        recoveryCommand: uncertainPublication ? Recovery(request) : null),
                        first.Labels, first.Solo, first.CrossRuntime, false, false, writer);
                }
                prepared = publication.Audit;
                preparedCommit = publication.Commit;
                sticky.RecordPrepared(prepared, preparedCommit);
            }
            else
                sticky.RecordPrepared(prepared, preparedCommit);

            if (preparedCommit is null)
            {
                return WriteResultWithHistory(request, Facts(request, "unresolved", "prepared-publication-commit-unavailable",
                    "The prepared audit/event pair is present, but its exact transaction commit could not be verified.",
                    preparedPublished: true, preparedPath: prepared.PreparedPath,
                    runPath: prepared.SelectedRunLogPath, recoveryCommand: Recovery(request)),
                    first.Labels, first.Solo, first.CrossRuntime, false, false, writer);
            }

            using var late = ApprovalPostingCheckout.Open(target);
            var beforeLabels = Assess(late, request);
            sticky.RecordAssessment(beforeLabels);
            if (!beforeLabels.Readable || !Matches(prepared, beforeLabels, request) || !CanContinue(prepared, beforeLabels))
            {
                return WriteResultWithHistory(request, Facts(request, "refused",
                    beforeLabels.Cause ?? "prepared-binding-no-longer-authorizes",
                    beforeLabels.Detail ?? "Current ownership, mode, local approvals, posted evidence, or PR head no longer matches the prepared permission.",
                    preparedPublished: true, preparedPath: prepared.PreparedPath,
                    observedPath: pair.Observed is null ? null : prepared.ObservedPath,
                    runPath: prepared.SelectedRunLogPath, preparedCommit: preparedCommit),
                    beforeLabels.Labels, beforeLabels.Solo, beforeLabels.CrossRuntime, false, false, writer);
            }

            var livePair = ReadPair(late, prepared.SelectedRunLogPath, request, prepared.PreparedPath, prepared.ObservedPath);
            sticky.RecordPair(livePair);
            if (!livePair.Valid)
            {
                return WriteResultWithHistory(request, Facts(request, "unresolved", livePair.Cause!, livePair.Detail!,
                    preparedPublished: true, preparedPath: prepared.PreparedPath,
                    runPath: prepared.SelectedRunLogPath, preparedCommit: preparedCommit,
                    recoveryCommand: Recovery(request)),
                    beforeLabels.Labels, beforeLabels.Solo, beforeLabels.CrossRuntime, false, false, writer);
            }
            if (livePair.Observed is not null)
            {
                if (!MatchesObserved(livePair.Observed, prepared, livePair.PreparedSha256, beforeLabels, request)
                    || livePair.Observed.PreparedPublicationCommit != livePair.PreparedCommit
                    || !Converged(beforeLabels.Labels, prepared.IntendedAddLabels, prepared.IntendedRemoveLabels))
                {
                    return WriteResultWithHistory(request, Facts(request, "refused", "override-binding-conflict",
                        "The UUID already has an observed outcome, but current head or labels diverged; a consumed UUID cannot authorize another mutation.",
                        preparedPublished: true, outcomePublished: true,
                        preparedPath: prepared.PreparedPath, observedPath: prepared.ObservedPath,
                        runPath: prepared.SelectedRunLogPath, preparedCommit: preparedCommit,
                        outcomeCommit: livePair.ObservedCommit, currentHead: beforeLabels.CurrentHead),
                        beforeLabels.Labels, beforeLabels.Solo, beforeLabels.CrossRuntime, false, false, writer);
                }
                return ConfirmSuccess(invokingContext, request, prepared, livePair.Observed, beforeLabels,
                    preparedCommit, livePair.ObservedCommit, false, false, writer);
            }

            if (!TryGetMutator(out var mutator, out var mutatorError))
            {
                return WriteResultWithHistory(request, Facts(request, "refused", "github-label-reader-unavailable", mutatorError!,
                    preparedPublished: true, preparedPath: prepared.PreparedPath,
                    runPath: prepared.SelectedRunLogPath, preparedCommit: preparedCommit),
                    beforeLabels.Labels, beforeLabels.Solo, beforeLabels.CrossRuntime, false, false, writer);
            }

            var mutationAttempted = false;
            var mayHaveApplied = false;
            string? mutationError = null;
            if (!Converged(beforeLabels.Labels, prepared.IntendedAddLabels, prepared.IntendedRemoveLabels))
            {
                mutationAttempted = true;
                mayHaveApplied = true;
                sticky.MutationAttempted = true;
                sticky.MayHaveApplied = true;
                try
                {
                    var remove = AutomationPrTransitionCommand.ResolveRemoveLabelsForMode(
                        "approved", WorkerClaimCompleteConstants.Modes.Write,
                        prepared.IntendedRemoveLabels, beforeLabels.Labels);
                    mutator.ApplyLabelTransitions(request.Repo, GhCliGitHubLabelMutator.Kinds.Pr,
                        request.PullRequest, prepared.IntendedAddLabels, remove);
                }
                catch (Exception exception) when (exception is IOException or InvalidOperationException)
                {
                    mutationError = exception.Message;
                    mayHaveApplied = true;
                    sticky.MayHaveApplied = true;
                }
            }

            if (!TryObserve(request, mutator, out var observedHead, out var observedLabels, out var observeError))
            {
                return WriteResultWithHistory(request, Facts(request, "unresolved", "post-mutation-observation-unavailable",
                    mutationError is null ? observeError! : mutationError + "; " + observeError,
                    preparedPublished: true, preparedPath: prepared.PreparedPath,
                    runPath: prepared.SelectedRunLogPath, preparedCommit: preparedCommit,
                    currentHead: observedHead, mutationAttempted: mutationAttempted,
                    mayHaveApplied: mayHaveApplied, authorizationBasis: beforeLabels.Basis,
                    recoveryCommand: Recovery(request)),
                    observedLabels ?? beforeLabels.Labels, beforeLabels.Solo, beforeLabels.CrossRuntime, false, false, writer);
            }
            sticky.LabelStateObserved = true;
            sticky.CurrentHead = observedHead;
            if (!string.Equals(observedHead, request.HeadSha, StringComparison.OrdinalIgnoreCase)
                || !Converged(observedLabels!, prepared.IntendedAddLabels, prepared.IntendedRemoveLabels))
            {
                return WriteResultWithHistory(request, Facts(request, "unresolved", "label-state-unconfirmed",
                    mutationError ?? "The exact requested PR head and approved labels were not observed after the one permitted label action.",
                    preparedPublished: true, preparedPath: prepared.PreparedPath,
                    runPath: prepared.SelectedRunLogPath, preparedCommit: preparedCommit,
                    currentHead: observedHead, mutationAttempted: mutationAttempted,
                    mayHaveApplied: mayHaveApplied, authorizationBasis: beforeLabels.Basis,
                    recoveryCommand: Recovery(request)),
                    observedLabels!, beforeLabels.Solo, beforeLabels.CrossRuntime, false, false, writer);
            }

            using var beforeOutcome = ApprovalPostingCheckout.Open(target);
            var outcomeAssessment = Assess(beforeOutcome, request);
            sticky.RecordAssessment(outcomeAssessment);
            if (!outcomeAssessment.Readable || !Matches(prepared, outcomeAssessment, request)
                || !CanContinue(prepared, outcomeAssessment))
            {
                return WriteResultWithHistory(request, Facts(request, "unresolved",
                    outcomeAssessment.Cause ?? "post-mutation-binding-unavailable",
                    outcomeAssessment.Detail ?? "Canonical authorization changed after labels were observed; no observed outcome was published.",
                    preparedPublished: true, preparedPath: prepared.PreparedPath,
                    runPath: prepared.SelectedRunLogPath, preparedCommit: preparedCommit,
                    currentHead: observedHead, mutationAttempted: mutationAttempted,
                    mayHaveApplied: mayHaveApplied, labelStateObserved: true,
                    authorizationBasis: outcomeAssessment.Basis, recoveryCommand: Recovery(request)),
                    observedLabels!, outcomeAssessment.Solo, outcomeAssessment.CrossRuntime, false, false, writer);
            }

            var outcome = BuildObserved(prepared, outcomeAssessment, observedLabels!, preparedCommit,
                livePair.PreparedSha256!, outcomeAssessment.Basis ?? "missing-posting-eligible", mutationAttempted, mayHaveApplied);
            var publicationResult = PublishObserved(target, request, prepared, outcome);
            sticky.RecordObservedPublication(publicationResult);
            if (!publicationResult.Published)
            {
                return WriteResultWithHistory(request, Facts(request, "unresolved",
                    publicationResult.Cause ?? "outcome-publication-unconfirmed",
                    publicationResult.Detail ?? "Labels were observed, but the immutable observed result is not confirmed on the canonical branch.",
                    preparedPublished: true, preparedPath: prepared.PreparedPath,
                    runPath: prepared.SelectedRunLogPath, preparedCommit: preparedCommit,
                    currentHead: observedHead, mutationAttempted: mutationAttempted,
                    mayHaveApplied: mayHaveApplied, labelStateObserved: true,
                    authorizationBasis: outcome.AuthorizationBasis, recoveryCommand: Recovery(request)),
                    observedLabels!, outcomeAssessment.Solo, outcomeAssessment.CrossRuntime, false, false, writer);
            }

            using var finalSnapshot = ApprovalPostingCheckout.Open(target);
            var final = Assess(finalSnapshot, request);
            sticky.RecordAssessment(final);
            string? finalHead = null;
            IReadOnlyList<string>? finalLabels = null;
            string? finalReadError = null;
            var finalObservationAvailable = TryObserve(request, mutator, out finalHead, out finalLabels, out finalReadError);
            if (!final.Readable || !Matches(prepared, final, request) || !CanContinue(prepared, final)
                || !finalObservationAvailable
                || !string.Equals(finalHead, request.HeadSha, StringComparison.OrdinalIgnoreCase)
                || !Converged(finalLabels ?? [], prepared.IntendedAddLabels, prepared.IntendedRemoveLabels))
            {
                return WriteResultWithHistory(request, Facts(request, "unresolved",
                    final.Cause ?? "final-observation-changed",
                    final.Detail ?? finalReadError ?? "Head, labels, or authorization changed after the observed pair was published.",
                    preparedPublished: true, outcomePublished: true,
                    preparedPath: prepared.PreparedPath, observedPath: prepared.ObservedPath,
                    runPath: prepared.SelectedRunLogPath, preparedCommit: preparedCommit,
                    outcomeCommit: publicationResult.Commit, currentHead: finalHead,
                    mutationAttempted: mutationAttempted, mayHaveApplied: mayHaveApplied,
                    labelStateObserved: true, authorizationBasis: outcome.AuthorizationBasis,
                    recoveryCommand: Recovery(request)),
                    finalLabels ?? observedLabels!, final.Solo, final.CrossRuntime, false, false, writer);
            }

            return ConfirmSuccess(invokingContext, request, prepared, outcome, final,
                preparedCommit, publicationResult.Commit, mutationAttempted, mayHaveApplied, writer);
        }
        catch (Exception exception) when (Expected(exception))
        {
            var historical = ReadHistoricalPairForReporting(target, request);
            sticky.MergeHistorical(historical);
            var unresolved = sticky.PreparedPublished || sticky.MutationAttempted || sticky.MayHaveApplied
                || sticky.LabelStateObserved || sticky.OutcomePublished;
            var result = Facts(request, unresolved ? "unresolved" : "refused",
                "approval-posting-override-unavailable", exception.Message,
                preparedPublished: sticky.PreparedPublished,
                outcomePublished: sticky.OutcomePublished,
                preparedPath: sticky.PreparedPath,
                observedPath: sticky.ObservedPath,
                runPath: sticky.RunPath,
                preparedCommit: sticky.PreparedCommit,
                outcomeCommit: sticky.OutcomeCommit,
                currentHead: sticky.CurrentHead,
                mutationAttempted: sticky.MutationAttempted,
                mayHaveApplied: sticky.MayHaveApplied,
                labelStateObserved: sticky.LabelStateObserved,
                authorizationBasis: sticky.AuthorizationBasis,
                recoveryCommand: unresolved ? Recovery(request) : null);
            return WriteResultWithHistory(request, result, sticky.Labels, sticky.Solo, sticky.CrossRuntime,
                false, false, writer);
        }
    }

    private static int ConfirmSuccess(
        CliContext invokingContext,
        ApprovalPostingOverrideRequest request,
        PreparedAudit prepared,
        ObservedAudit observed,
        Assessment current,
        string? preparedCommit,
        string? outcomeCommit,
        bool mutationAttempted,
        bool mayHaveApplied,
        TextWriter writer)
    {
        var cleared = false;
        string? warning = null;
        if (request.Mode == WorkerClaimCompleteConstants.Modes.Write)
        {
            var result = CiWaitStore.ClearForTransition(invokingContext.RepoRoot, request.Repo,
                request.PullRequest, "approved", write: true);
            cleared = result.Applied || result.AlreadyConverged;
            warning = result.Error is null ? null : $"The approved transition is confirmed, but the durable CI wait could not be cleared: {result.Error}";
        }
        return WriteResult(request, Facts(request, "observed", "approval-posting-override-observed",
            "The requested head and approved labels were observed, and immutable prepared and observed host records are durable.",
            preparedPublished: true, outcomePublished: true,
            preparedPath: prepared.PreparedPath, observedPath: prepared.ObservedPath,
            runPath: prepared.SelectedRunLogPath, preparedCommit: preparedCommit,
            outcomeCommit: outcomeCommit, currentHead: current.CurrentHead,
            mutationAttempted: mutationAttempted, mayHaveApplied: mayHaveApplied,
            labelStateObserved: true, authorizationBasis: current.Basis),
            current.Labels, current.Solo, current.CrossRuntime, true, cleared, writer, warning,
            observedOnly: !mutationAttempted);
    }

    private static int WriteAssessmentRefusal(ApprovalPostingOverrideRequest request, Assessment assessment, TextWriter writer,
        HistoricalPairReport? historical = null) =>
        WriteResult(request, Facts(request, "refused", assessment.Cause ?? "override-not-applicable",
            AppendHistoricalDetail(assessment.Detail ?? "The current canonical approval state is not eligible for this exception.", historical),
            preparedPublished: historical?.PreparedExists ?? false,
            outcomePublished: historical?.ObservedExists ?? false,
            preparedPath: historical?.PreparedPath,
            observedPath: historical?.ObservedPath,
            runPath: historical?.RunPath,
            preparedCommit: historical?.PreparedCommit,
            outcomeCommit: historical?.ObservedCommit,
            currentHead: assessment.CurrentHead),
            assessment.Labels, assessment.Solo, assessment.CrossRuntime, false, false, writer);

    private static string AppendHistoricalDetail(string detail, HistoricalPairReport? historical)
    {
        if (historical is null) return detail;
        if (!historical.PreparedExists && !historical.ObservedExists && historical.Detail is null) return detail;
        return detail + " Historical UUID publication facts: "
            + (historical.PairVerified ? "the exact canonical audit/run-event pair was verified." : "audit publication is present or could not be fully verified.")
            + (historical.Detail is null ? string.Empty : " " + historical.Detail);
    }

    private static HistoricalPairReport ReadHistoricalPairForReporting(CanonicalTarget target, ApprovalPostingOverrideRequest request)
    {
        try
        {
            using var checkout = ApprovalPostingCheckout.Open(target);
            return ReadHistoricalPairForReporting(checkout, request);
        }
        catch (Exception exception) when (Expected(exception))
        {
            return new HistoricalPairReport(false, false, false, null, null, null, null, null,
                "The canonical historical UUID pair could not be read: " + exception.Message);
        }
    }

    private static HistoricalPairReport ReadHistoricalPairForReporting(ApprovalPostingCheckout checkout, ApprovalPostingOverrideRequest request)
    {
        var preparedPath = AuditPath(request.ExecutionUnit, request.OverrideId, "prepared.json");
        var observedPath = AuditPath(request.ExecutionUnit, request.OverrideId, "observed.json");
        var preparedInspection = InspectPath(checkout.Root, preparedPath, expectDirectory: false);
        var observedInspection = InspectPath(checkout.Root, observedPath, expectDirectory: false);
        var preparedExists = preparedInspection.Kind == PathKind.Regular;
        var observedExists = observedInspection.Kind == PathKind.Regular;
        if (!preparedExists && !observedExists && preparedInspection.Kind == PathKind.Absent
            && observedInspection.Kind == PathKind.Absent)
            return new HistoricalPairReport(false, false, false, preparedPath, observedPath, null, null, null, null);

        if (!ReadAudit<PreparedAudit>(checkout, preparedPath, out var prepared, out var parsedPreparedExists, out var preparedError))
            return new HistoricalPairReport(preparedExists || parsedPreparedExists, observedExists, false,
                preparedPath, observedPath, null, CommitForPath(checkout, preparedPath), CommitForPath(checkout, observedPath),
                "The prepared audit is present but could not be validated: " + preparedError);
        if (!ReadAudit<ObservedAudit>(checkout, observedPath, out _, out var parsedObservedExists, out var observedError))
            return new HistoricalPairReport(preparedExists || parsedPreparedExists, observedExists || parsedObservedExists, false,
                preparedPath, observedPath, prepared?.SelectedRunLogPath, CommitForPath(checkout, preparedPath), CommitForPath(checkout, observedPath),
                "The observed audit is present but could not be validated: " + observedError);
        preparedExists |= parsedPreparedExists;
        observedExists |= parsedObservedExists;
        if (prepared is null)
            return new HistoricalPairReport(preparedExists, observedExists, false, preparedPath, observedPath, null,
                preparedExists ? CommitForPath(checkout, preparedPath) : null,
                observedExists ? CommitForPath(checkout, observedPath) : null,
                observedExists ? "An observed audit exists without a readable prepared audit." : null);

        if (prepared.ExecutionUnit != request.ExecutionUnit || prepared.OverrideId != request.OverrideId
            || prepared.PreparedPath != preparedPath || prepared.ObservedPath != observedPath
            || !SameRepo(prepared.TargetRepo, request.Repo) || prepared.PullRequest != request.PullRequest)
            return new HistoricalPairReport(preparedExists, observedExists, false, preparedPath, observedPath,
                prepared.SelectedRunLogPath, CommitForPath(checkout, preparedPath),
                observedExists ? CommitForPath(checkout, observedPath) : null,
                "The historical audit identity differs from this request, so its run-event pair is not used for authorization or reconciliation.");

        var selectedRunPath = CanonicalRunPathForAudit(checkout.Root, prepared, request.Repo);
        if (selectedRunPath is null)
            return new HistoricalPairReport(preparedExists, observedExists, false, preparedPath, observedPath,
                prepared.SelectedRunLogPath, CommitForPath(checkout, preparedPath),
                observedExists ? CommitForPath(checkout, observedPath) : null,
                "The prepared audit names a run-log path outside the canonical legacy/scoped layouts.");

        var pair = ReadPair(checkout, selectedRunPath, request, preparedPath, observedPath);
        return new HistoricalPairReport(pair.PreparedExists, pair.ObservedExists, pair.Valid,
            preparedPath, observedPath, selectedRunPath, pair.PreparedCommit, pair.ObservedCommit,
            pair.Valid ? null : pair.Detail ?? "The historical audit/run-event pair could not be verified.");
    }

    private static string? CanonicalRunPathForAudit(string root, PreparedAudit prepared, string repo)
    {
        if (string.IsNullOrWhiteSpace(prepared.Domain) || !CrossRuntimeReviewPaths.IsRepositoryName(repo)) return null;
        var scoped = RuntimeScopedStateResolver.GetScopedRunLogPath(root, prepared.Domain, repo)
            .Replace(root + Path.DirectorySeparatorChar, string.Empty, StringComparison.Ordinal)
            .Replace(Path.DirectorySeparatorChar, '/');
        var legacy = RuntimeScopedStateResolver.GetLegacyRunLogPath(root)
            .Replace(root + Path.DirectorySeparatorChar, string.Empty, StringComparison.Ordinal)
            .Replace(Path.DirectorySeparatorChar, '/');
        return prepared.SelectedRunLogPath == scoped || prepared.SelectedRunLogPath == legacy
            ? prepared.SelectedRunLogPath
            : null;
    }

    private sealed record HistoricalPairReport(
        bool PreparedExists,
        bool ObservedExists,
        bool PairVerified,
        string? PreparedPath,
        string? ObservedPath,
        string? RunPath,
        string? PreparedCommit,
        string? ObservedCommit,
        string? Detail);

    private sealed class ExecutionFacts
    {
        public bool PreparedPublished { get; private set; }
        public bool OutcomePublished { get; private set; }
        public bool MutationAttempted { get; set; }
        public bool MayHaveApplied { get; set; }
        public bool LabelStateObserved { get; set; }
        public string? PreparedPath { get; private set; }
        public string? ObservedPath { get; private set; }
        public string? RunPath { get; private set; }
        public string? PreparedCommit { get; private set; }
        public string? OutcomeCommit { get; private set; }
        public string? CurrentHead { get; set; }
        public string? AuthorizationBasis { get; private set; }
        public IReadOnlyList<string> Labels { get; private set; } = [];
        public SoloConductorReviewTransitionOutcome? Solo { get; private set; }
        public CrossRuntimeReviewTransitionOutcome? CrossRuntime { get; private set; }

        public void RecordAssessment(Assessment assessment)
        {
            Labels = assessment.Labels;
            Solo = assessment.Solo;
            CrossRuntime = assessment.CrossRuntime;
            RunPath = assessment.RunPath ?? RunPath;
            CurrentHead = assessment.CurrentHead ?? CurrentHead;
            AuthorizationBasis = assessment.Basis ?? AuthorizationBasis;
        }

        public void RecordPaths(string preparedPath, string observedPath, string? runPath)
        {
            PreparedPath = preparedPath;
            ObservedPath = observedPath;
            RunPath = runPath ?? RunPath;
        }

        public void RecordPair(ExistingPair pair)
        {
            PreparedPublished |= pair.PreparedExists;
            OutcomePublished |= pair.ObservedExists;
            PreparedCommit = pair.PreparedCommit ?? PreparedCommit;
            OutcomeCommit = pair.ObservedCommit ?? OutcomeCommit;
        }

        public void RecordPreparedPublication(PublicationResult publication)
        {
            PreparedPublished |= publication.Published;
            PreparedPath = publication.Path ?? PreparedPath;
            RunPath = publication.RunPath ?? RunPath;
            PreparedCommit = publication.Commit ?? PreparedCommit;
            if (publication.Published && publication.Audit is not null)
            {
                PreparedPath = publication.Audit.PreparedPath;
                ObservedPath = publication.Audit.ObservedPath;
                RunPath = publication.Audit.SelectedRunLogPath;
            }
        }

        public void RecordPrepared(PreparedAudit prepared, string? commit)
        {
            PreparedPublished = true;
            PreparedPath = prepared.PreparedPath;
            ObservedPath = prepared.ObservedPath;
            RunPath = prepared.SelectedRunLogPath;
            PreparedCommit = commit;
        }

        public void RecordObservedPublication(PublicationResult publication)
        {
            OutcomePublished |= publication.Published || publication.CanonicalOutcomeExists;
            ObservedPath = publication.Path ?? ObservedPath;
            OutcomeCommit = publication.Commit ?? OutcomeCommit;
        }

        public void MergeHistorical(HistoricalPairReport historical)
        {
            PreparedPublished |= historical.PreparedExists;
            OutcomePublished |= historical.ObservedExists;
            if (historical.PreparedExists || historical.ObservedExists)
            {
                PreparedPath = historical.PreparedPath ?? PreparedPath;
                ObservedPath = historical.ObservedPath ?? ObservedPath;
                RunPath = historical.RunPath ?? RunPath;
                PreparedCommit = historical.PreparedCommit ?? PreparedCommit;
                OutcomeCommit = historical.ObservedCommit ?? OutcomeCommit;
            }
        }
    }

    private static int WriteResult(
        ApprovalPostingOverrideRequest request,
        ApprovalPostingOverrideResult overrideResult,
        IReadOnlyList<string> currentLabels,
        SoloConductorReviewTransitionOutcome? solo,
        CrossRuntimeReviewTransitionOutcome? crossRuntime,
        bool applied,
        bool ciWaitCleared,
        TextWriter writer,
        string? ciWaitWarning = null,
        bool observedOnly = false,
        ExecutionFacts? publicationHistory = null)
    {
        if (publicationHistory is not null)
        {
            overrideResult = overrideResult with
            {
                PreparedPublished = overrideResult.PreparedPublished || publicationHistory.PreparedPublished,
                OutcomePublished = overrideResult.OutcomePublished || publicationHistory.OutcomePublished,
                PreparedPath = overrideResult.PreparedPath ?? (publicationHistory.PreparedPublished ? publicationHistory.PreparedPath : null),
                ObservedPath = overrideResult.ObservedPath ?? (publicationHistory.OutcomePublished ? publicationHistory.ObservedPath : null),
                SelectedRunLogPath = overrideResult.SelectedRunLogPath ?? publicationHistory.RunPath,
                PreparedCommit = overrideResult.PreparedCommit ?? publicationHistory.PreparedCommit,
                OutcomeCommit = overrideResult.OutcomeCommit ?? publicationHistory.OutcomeCommit,
            };
        }

        var plan = AutomationPrTransitionCommand.PlanTransition("approved");
        var remove = AutomationPrTransitionCommand.ResolveRemoveLabelsForMode(
            "approved", WorkerClaimCompleteConstants.Modes.Write, plan.RemoveLabels, currentLabels);
        var result = new AutomationPrTransitionResult
        {
            Repo = request.Repo,
            Pr = request.PullRequest,
            Transition = "approved",
            Mode = request.Mode,
            Applied = applied,
            AddLabels = applied ? plan.AddLabels : Array.Empty<string>(),
            RemoveLabels = applied ? remove : Array.Empty<string>(),
            CurrentLabels = currentLabels,
            Summary = applied && observedOnly
                ? $"Observed the approved transition already converged on PR #{request.PullRequest} in {request.Repo}; no label action was applied by this invocation."
                : string.Equals(overrideResult.Disposition, "eligible-preview", StringComparison.Ordinal)
                    ? $"Eligible preview for approved transition on PR #{request.PullRequest} in {request.Repo}; no labels were changed."
                    : applied
                        ? $"Applied approved transition to PR #{request.PullRequest} in {request.Repo} after the missing-posting exception was recorded."
                        : $"Refused approved transition on PR #{request.PullRequest} in {request.Repo}: {overrideResult.Cause}.",
            CiWaitCleared = ciWaitCleared,
            CiWaitWarning = ciWaitWarning,
            CrossRuntimeReview = crossRuntime,
            SoloConductorReview = solo,
            ApprovalPostingOverride = overrideResult,
            Error = applied || string.Equals(overrideResult.Disposition, "eligible-preview", StringComparison.Ordinal)
                ? null : $"{overrideResult.Cause}: {overrideResult.Detail}",
        };
        if (request.Format == "json")
            writer.WriteLine(JsonSerializer.Serialize(result, ResultJsonOptions));
        else
            AutomationPrTransitionCommand.WriteText(writer, result);
        return applied || string.Equals(overrideResult.Disposition, "eligible-preview", StringComparison.Ordinal) ? 0 : 1;
    }

    private static ApprovalPostingOverrideResult Facts(
        ApprovalPostingOverrideRequest request,
        string disposition,
        string cause,
        string detail,
        bool preparedPublished = false,
        bool outcomePublished = false,
        string? preparedPath = null,
        string? observedPath = null,
        string? runPath = null,
        string? preparedCommit = null,
        string? outcomeCommit = null,
        string? currentHead = null,
        bool mutationAttempted = false,
        bool mayHaveApplied = false,
        bool labelStateObserved = false,
        string? authorizationBasis = null,
        string? recoveryCommand = null) => new()
    {
        Requested = true,
        OverrideId = request.OverrideId,
        Disposition = disposition,
        Cause = cause,
        Detail = detail,
        Reason = request.Reason,
        PreparedPublished = preparedPublished,
        OutcomePublished = outcomePublished,
        MutationAttempted = mutationAttempted,
        MayHaveApplied = mayHaveApplied,
        LabelStateObserved = labelStateObserved,
        PreparedPath = preparedPath,
        ObservedPath = observedPath,
        SelectedRunLogPath = runPath,
        PreparedCommit = preparedCommit,
        OutcomeCommit = outcomeCommit,
        CurrentHead = currentHead,
        AuthorizationBasis = authorizationBasis,
        RecoveryCommand = recoveryCommand,
    };

    private static Assessment Assess(ApprovalPostingCheckout snapshot, ApprovalPostingOverrideRequest request)
    {
        var refused = Assessment.Refused;
        if (!KnowledgeWriteBackRecord.TryValidateExecutionUnit(request.ExecutionUnit, out var unitError))
            return refused("execution-unit-invalid", unitError);
        if (!CrossRuntimeReviewPaths.IsRepositoryName(request.Repo) || request.PullRequest <= 0
            || !CrossRuntimeReviewPaths.IsFullHeadSha(request.HeadSha))
            return refused("request-identity-invalid", "The explicit repository, positive PR number, execution unit, and full head SHA are required.");
        if (!LogicalRoleNormalizer.TryNormalize(request.Actor, out var role, out _)
            || !string.Equals(role, LogicalRoleNormalizer.Builder, StringComparison.Ordinal))
            return refused("actor-not-builder", "The requested actor does not normalize to builder.");

        var claimRelative = ClaimCommand.ClaimPath($"execution-unit:{request.ExecutionUnit}");
        if (!TryReadRegular(snapshot.Root, claimRelative, out var claimJson, out var claimFailure))
            return refused("claim-unavailable", claimFailure!);
        ClaimRecord? claim;
        try
        {
            claim = JsonSerializer.Deserialize<ClaimRecord>(claimJson!);
        }
        catch (JsonException exception)
        {
            return refused("claim-invalid", "The canonical held claim is malformed: " + exception.Message);
        }
        if (claim is null || claim.SchemaVersion != "1"
            || claim.Scope != $"execution-unit:{request.ExecutionUnit}"
            || string.IsNullOrWhiteSpace(claim.Actor) || claim.Actor != request.Actor
            || string.IsNullOrWhiteSpace(claim.Team) || claim.Team != request.Team
            || claim.ClaimedAt == default
            || !LogicalRoleNormalizer.TryNormalize(claim.Actor, out var claimRole, out _)
            || claimRole != LogicalRoleNormalizer.Builder)
            return refused("claim-holder-mismatch", "The canonical claim is not held by the exact requested builder actor and team for this execution unit.");

        var packetRelative = GuideReachabilityRecord.ResolvePacketPath(snapshot.Root, request.ExecutionUnit)
            .Replace(Path.DirectorySeparatorChar, '/');
        if (!TryReadRegular(snapshot.Root, packetRelative, out var packetText, out var packetFailure))
            return refused("packet-unavailable", packetFailure!);
        if (!TryReadApprovalPostingPacketIdentity(packetText!, out var packet, out var packetError))
            return refused("packet-identity-invalid", packetError!);
        if (packet.SourceUnit is not null && packet.SourceUnit != request.ExecutionUnit)
            return refused("packet-unit-mismatch", "The canonical packet source_execution_unit conflicts with the requested unit.");
        if (!CrossRuntimeReviewPaths.IsRepositoryName(packet.TargetRepo) || string.IsNullOrWhiteSpace(packet.Domain))
            return refused("packet-identity-missing", "The canonical packet must bind a valid domain and target_repo.");
        if (!string.Equals(packet.TargetRepo, request.Repo, StringComparison.OrdinalIgnoreCase))
            return refused("packet-repo-mismatch", "The canonical packet target_repo conflicts with the explicit repository.");

        var configRelative = CliRuntimeContracts.GetConfigPath(snapshot.Root)
            .Replace(snapshot.Root + Path.DirectorySeparatorChar, string.Empty, StringComparison.Ordinal)
            .Replace(Path.DirectorySeparatorChar, '/');
        if (!TryReadRegular(snapshot.Root, configRelative, out var configText, out var configFailure))
            return refused("canonical-config-unavailable", configFailure!);
        CliConfig config;
        try { config = CliConfigLoader.Load(configText!); }
        catch (Exception exception) when (exception is Tomlyn.TomlException or InvalidOperationException or ArgumentException or FormatException)
        { return refused("canonical-config-invalid", exception.Message); }
        if (!config.CrossRuntimeReview.TryGetDeclared(packet.Domain, claim.Team, out var declaration)
            || !declaration.Repos.Contains(request.Repo, StringComparer.OrdinalIgnoreCase))
            return refused("override-not-applicable", "The canonical config does not declare the exact domain/team and repository for G834.");

        var modeInspection = InspectPath(snapshot.Root, TeamModeStore.RelativePath, expectDirectory: false);
        if (modeInspection.Kind != PathKind.Regular)
            return refused("team-mode-unavailable", modeInspection.Detail ?? "The canonical recorded team-mode file is missing or unavailable.");
        TeamModeState? modeState;
        try { modeState = TeamModeStore.TryRead(snapshot.Root); }
        catch (Exception exception) when (exception is InvalidOperationException or IOException or UnauthorizedAccessException)
        { return refused("team-mode-unavailable", exception.Message); }
        TeamModeResolution mode;
        try { mode = TeamModeStore.Resolve(modeState, packet.Domain, claim.Team); }
        catch (InvalidOperationException exception) { return refused("team-mode-ambiguous", exception.Message); }
        if (mode.Source != TeamModeSource.Recorded || !mode.IsSoloConductor
            || !string.Equals(mode.Entry?.Domain, packet.Domain, StringComparison.Ordinal)
            || !string.Equals(mode.Entry?.Team, claim.Team, StringComparison.Ordinal))
            return refused("override-not-applicable", "The current canonical team mode is not a recorded exact-team solo-conductor entry.");

        var (queuePath, scoped) = SelectQueuePath(snapshot.Root, packet.Domain, request.Repo, out var queueError);
        if (queueError is not null) return refused("canonical-queue-unavailable", queueError);
        if (!TryReadRegular(snapshot.Root, queuePath!, out var queueText, out var queueFailure))
            return refused("canonical-queue-unavailable", queueFailure!);
        QueueState queue;
        try { queue = QueueStateSerializer.Deserialize(queueText!); }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or ArgumentException)
        { return refused("canonical-queue-invalid", exception.Message); }
        if (queue.Items is null || queue.Items.Any(item => item is null))
            return refused("canonical-queue-invalid", "Canonical queue items must be an array of non-null queue entries.");
        var unitRows = queue.Items.Where(item => string.Equals(item.ExecutionUnit, request.ExecutionUnit, StringComparison.Ordinal)).ToArray();
        if (unitRows.Length != 1)
            return refused(unitRows.Length == 0 ? "canonical-queue-item-missing" : "canonical-queue-item-ambiguous",
                $"Canonical queue selection contains {unitRows.Length} rows for the requested execution unit; exactly one is required.");
        var queueItem = unitRows[0];
        if (!ValidateRawQueuePr(queueText!, request.ExecutionUnit, packet.TargetRepo!, queueItem, out var pr, out var prError))
            return refused("canonical-queue-pr-identity-conflict", prError!);
        if (pr is null)
            return refused("canonical-queue-pr-missing", "The unique canonical queue item has no repository-bound linked PR identity.");
        if (pr != request.PullRequest)
            return refused("canonical-queue-pr-mismatch", $"The canonical queue links this unit to PR #{pr}, not #{request.PullRequest}.");
        if (queueItem.LinkedIssue?.Repo is { Length: > 0 } issueRepo
            && !string.Equals(issueRepo, packet.TargetRepo, StringComparison.OrdinalIgnoreCase))
            return refused("canonical-queue-repo-conflict", "The canonical queue linked_issue repo conflicts with packet target_repo.");
        if (queueItem.LinkedIssue?.Url is { Length: > 0 } issueUrl
            && (!TryParseGitHubIdentity(issueUrl, "issues", out var issueUrlRepo, out var issueNumber)
                || !SameRepo(issueUrlRepo, packet.TargetRepo)
                || queueItem.LinkedIssue.Number is { } linkedIssueNumber && linkedIssueNumber != issueNumber))
            return refused("canonical-queue-issue-identity-conflict", "The canonical queue linked_issue URL/number is malformed or contradictory.");

        var runRelative = scoped
            ? RuntimeScopedStateResolver.GetScopedRunLogPath(snapshot.Root, packet.Domain!, packet.TargetRepo!)
                .Replace(snapshot.Root + Path.DirectorySeparatorChar, string.Empty, StringComparison.Ordinal)
                .Replace(Path.DirectorySeparatorChar, '/')
            : RuntimeScopedStateResolver.GetLegacyRunLogPath(snapshot.Root)
                .Replace(snapshot.Root + Path.DirectorySeparatorChar, string.Empty, StringComparison.Ordinal)
                .Replace(Path.DirectorySeparatorChar, '/');
        string runText;
        var runInspection = InspectPath(snapshot.Root, runRelative, expectDirectory: false);
        if (runInspection.Kind == PathKind.Unavailable)
            return refused("canonical-run-log-unavailable", runInspection.Detail!);
        if (runInspection.Kind == PathKind.Absent)
            runText = string.Empty;
        else if (!TryReadRegular(snapshot.Root, runRelative, out var readRun, out var runFailure))
            return refused("canonical-run-log-unavailable", runFailure!);
        else runText = readRun!;
        IReadOnlyList<RunEvent> runEvents;
        try { runEvents = RunLogSerializer.DeserializeAll(runText); }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or ArgumentException)
        { return refused("canonical-run-log-invalid", exception.Message); }

        var resolution = new CrossRuntimeReviewResolution
        {
            Resolved = true,
            ExecutionUnit = request.ExecutionUnit,
            ExecutionUnitSource = CrossRuntimeReviewTeamResolver.SourceQueueLinkedPr,
            Domain = packet.Domain,
            DomainSource = CrossRuntimeReviewTeamResolver.SourcePacketDomain,
            Team = claim.Team,
            TeamSource = CrossRuntimeReviewTeamResolver.SourceClaimTeam,
        };

        if (!AutomationPrTransitionCommand.TryReadCurrentHead(request.Repo, request.PullRequest, out var currentHead, out var headError))
            return refused("review-head-unavailable", $"The current PR head could not be read before review inventory: {headError}");
        if (!string.Equals(currentHead, request.HeadSha, StringComparison.OrdinalIgnoreCase))
            return refused(SoloConductorApprovalGate.CauseHeadChanged, $"The current PR head {currentHead} differs from requested head {request.HeadSha}.");

        var reviewDirectory = CrossRuntimeReviewPaths.PrDirectory(snapshot.Root, request.Repo, request.PullRequest)
            .Replace(snapshot.Root + Path.DirectorySeparatorChar, string.Empty, StringComparison.Ordinal)
            .Replace(Path.DirectorySeparatorChar, '/');
        var reviewDirectoryInspection = InspectPath(snapshot.Root, reviewDirectory, expectDirectory: true);
        if (reviewDirectoryInspection.Kind == PathKind.Unavailable)
            return refused("cross-runtime-review-read-unavailable", reviewDirectoryInspection.Detail ?? "The canonical review directory is unavailable.");
        if (reviewDirectoryInspection.Kind == PathKind.Directory)
        {
            try
            {
                foreach (var entry in Directory.EnumerateFileSystemEntries(Path.Combine(snapshot.Root, reviewDirectory.Replace('/', Path.DirectorySeparatorChar))))
                {
                    var relative = Path.GetRelativePath(snapshot.Root, entry).Replace(Path.DirectorySeparatorChar, '/');
                    var isDirectory = (File.GetAttributes(entry) & FileAttributes.Directory) != 0;
                    var inspection = InspectPath(snapshot.Root, relative, expectDirectory: isDirectory);
                    if (inspection.Kind is not (PathKind.Regular or PathKind.Directory))
                        return refused("cross-runtime-review-read-unavailable", inspection.Detail ?? $"Canonical review evidence '{relative}' is unavailable.");
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            { return refused("cross-runtime-review-read-unavailable", exception.Message); }
        }
        CrossRuntimeReviewReadResult localReviews;
        try { localReviews = CrossRuntimeReviewStore.Read(snapshot.Root, request.Repo, request.PullRequest); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
        { return refused("cross-runtime-review-read-unavailable", exception.Message); }
        var localGate = CrossRuntimeReviewGate.Evaluate(declaration, resolution, request.HeadSha, localReviews);
        var crossOutcome = new CrossRuntimeReviewTransitionOutcome
        {
            Decision = localGate.Decision,
            Cause = localGate.PrimaryCause,
            Detail = localGate.Reasons.Count == 0 ? null : string.Join(" ", localGate.Reasons.Select(item => $"[{item.Cause}] {item.Detail}")),
            Reasons = localGate.Reasons,
            ExecutionUnit = request.ExecutionUnit,
            Domain = packet.Domain,
            Team = claim.Team,
        };
        if (localGate.Decision != CrossRuntimeReviewGate.DecisionSatisfied)
            return refused(localGate.Decision == CrossRuntimeReviewGate.DecisionBlocked ? SoloConductorApprovalGate.CauseLocalGateBlocked : SoloConductorApprovalGate.CauseLocalGateMissing,
                crossOutcome.Detail ?? "The existing G834 local gate is not satisfied.", null, crossOutcome);

        var reviewRequest = new SoloConductorReviewReadRequest
        {
            Repo = request.Repo,
            PullRequest = request.PullRequest,
            ExecutionUnit = request.ExecutionUnit,
            Domain = packet.Domain,
            Team = claim.Team,
            LocalReviews = localReviews,
        };
        SoloConductorReviewReadResult posted;
        try { posted = (AutomationPrTransitionCommand.SoloReviewReader ?? (r => new SoloConductorReviewReader().Read(r)))(reviewRequest); }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or ArgumentException)
        { posted = SoloConductorReviewReadResult.Failed(SoloConductorReviewReader.CauseReadUnavailable, exception.Message); }
        if (!posted.Complete)
            return refused(posted.Cause ?? SoloConductorReviewReader.CauseReadUnavailable, posted.Detail ?? "The posted review inventory is unavailable.", null, crossOutcome);

        var soloEvaluation = SoloConductorApprovalGate.Evaluate(posted.Rows, request.ExecutionUnit, request.HeadSha, localGate);
        var soloOutcome = new SoloConductorReviewTransitionOutcome
        {
            Decision = soloEvaluation.Decision,
            Cause = soloEvaluation.Cause,
            Detail = soloEvaluation.Detail,
            ExecutionUnit = request.ExecutionUnit,
            Domain = packet.Domain,
            Team = claim.Team,
            ExpectedHeadSha = request.HeadSha,
            ObservedHeadSha = currentHead,
            QualifyingReviews = soloEvaluation.QualifyingReviews,
            Obligations = soloEvaluation.Obligations,
            SupersededInvalidReviewIds = soloEvaluation.SupersededInvalidReviewIds,
            UnscopableReviewIds = soloEvaluation.UnscopableReviewIds,
            RepairUnavailableReason = soloEvaluation.RepairUnavailableReason,
            MissingRelations = soloEvaluation.MissingRelations,
        };
        if (!AutomationPrTransitionCommand.TryReadCurrentHead(request.Repo, request.PullRequest, out var finalHead, out var finalHeadError))
            return refused("review-head-unavailable", $"The current PR head could not be re-read after review inventory: {finalHeadError}", soloOutcome, crossOutcome);
        if (!string.Equals(finalHead, request.HeadSha, StringComparison.OrdinalIgnoreCase))
            return refused(SoloConductorApprovalGate.CauseHeadChanged, $"The PR head changed to {finalHead} during review inventory.", soloOutcome, crossOutcome);

        if (!TryGetMutator(out var mutator, out var labelsError))
            return refused("github-label-reader-unavailable", labelsError!, soloOutcome, crossOutcome);
        IReadOnlyList<string> labels;
        try { labels = mutator.ReadLabels(request.Repo, GhCliGitHubLabelMutator.Kinds.Pr, request.PullRequest).Select(item => item.Name).Where(item => !string.IsNullOrWhiteSpace(item)).ToArray(); }
        catch (Exception exception) when (exception is IOException or InvalidOperationException)
        { return refused("github-label-reader-unavailable", exception.Message, soloOutcome, crossOutcome); }

        var plan = AutomationPrTransitionCommand.PlanTransition("approved");
        var missing = soloEvaluation.MissingRelations;
        var eligible = soloEvaluation.Decision == SoloConductorApprovalGate.DecisionRefused
            && soloEvaluation.Cause == SoloConductorApprovalGate.CauseReviewMissing
            && missing is { Count: > 0 }
            && missing.All(relation => relation is CrossRuntimeReviewRecord.RelationSameRuntime or CrossRuntimeReviewRecord.RelationCrossRuntime)
            && soloEvaluation.Obligations.Count == 0 && soloEvaluation.UnscopableReviewIds.Count == 0
            && localGate.Decision == CrossRuntimeReviewGate.DecisionSatisfied
            && HasDecidingApprovals(localGate, localReviews, request, missing);
        var ordinarySatisfied = soloEvaluation.Decision == SoloConductorApprovalGate.DecisionSatisfied;
        var cause = eligible ? null : ordinarySatisfied ? "override-not-applicable" : soloEvaluation.Cause ?? "override-not-applicable";
        var detail = eligible ? null : ordinarySatisfied ? "The ordinary approval gate is already satisfied; the exception is not applicable." : soloEvaluation.Detail;
        var deciding = BuildDecidingRecordBindings(snapshot.Root, localGate, localReviews, request, packet.Domain, claim.Team);
        return new Assessment
        {
            Readable = true,
            MissingEligible = eligible,
            OrdinarySatisfied = ordinarySatisfied,
            Cause = cause,
            Detail = detail,
            Domain = packet.Domain,
            ClaimEpoch = claim.ClaimedAt,
            QueuePath = queuePath,
            RunPath = runRelative,
            CurrentHead = currentHead,
            Labels = labels,
            MissingRelations = missing ?? [],
            DecidingRecords = deciding,
            QualifyingReviews = soloEvaluation.QualifyingReviews,
            Solo = soloOutcome,
            CrossRuntime = crossOutcome,
            Basis = eligible ? "missing-posting-eligible" : ordinarySatisfied ? "ordinary-satisfied" : null,
            Claim = claim,
            ClaimPath = claimRelative,
            CanonicalSnapshotOid = snapshot.Commit,
            CanonicalTargetRef = snapshot.Ref,
            RunText = runText,
            RunEvents = runEvents,
            AddLabels = plan.AddLabels,
            RemoveLabels = plan.RemoveLabels,
            SelectedQueueLayout = scoped ? "scoped" : "legacy",
        };
    }

    private static bool HasDecidingApprovals(
        CrossRuntimeReviewGateResult gate,
        CrossRuntimeReviewReadResult localReviews,
        ApprovalPostingOverrideRequest request,
        IReadOnlyList<string> missing)
    {
        var records = localReviews.Records.ToDictionary(item => item.RelativePath, StringComparer.Ordinal);
        foreach (var relation in missing)
        {
            if (!gate.Records.Any(entry => entry.Status == CrossRuntimeReviewGate.StatusDeciding
                    && entry.Verdict == CrossRuntimeReviewVerdict.Approve && entry.Relation == relation
                    && entry.ExecutionUnit == request.ExecutionUnit
                    && records.TryGetValue(entry.File, out var stored)
                    && stored.Record.HeadSha.Equals(request.HeadSha, StringComparison.OrdinalIgnoreCase)))
                return false;
        }
        return true;
    }

    private static IReadOnlyList<ApprovalPostingOverrideDecidingRecord> BuildDecidingRecordBindings(
        string root,
        CrossRuntimeReviewGateResult gate,
        CrossRuntimeReviewReadResult reviews,
        ApprovalPostingOverrideRequest request,
        string domain,
        string team)
    {
        var byPath = reviews.Records.ToDictionary(item => item.RelativePath, StringComparer.Ordinal);
        return gate.Records.Where(entry => entry.Status == CrossRuntimeReviewGate.StatusDeciding
                && entry.Verdict == CrossRuntimeReviewVerdict.Approve
                && byPath.ContainsKey(entry.File))
            .Select(entry => new ApprovalPostingOverrideDecidingRecord
            {
                Runtime = entry.Runtime,
                Relation = entry.Relation,
                Verdict = entry.Verdict,
                HeadSha = entry.HeadSha!,
                File = entry.File,
                Sha256 = Sha256Hex(File.ReadAllBytes(Path.Combine(root, entry.File.Replace('/', Path.DirectorySeparatorChar)))),
            }).ToArray();
    }

    private static bool TryReadApprovalPostingPacketIdentity(string yaml, out ApprovalPostingPacketIdentity identity, out string? error)
    {
        identity = default!;
        error = null;
        try
        {
            var stream = new YamlStream();
            using var reader = new StringReader(yaml);
            stream.Load(reader);
            if (stream.Documents.Count == 0 || stream.Documents[0].RootNode is not YamlMappingNode root)
            {
                error = "Packet YAML has no mapping root.";
                return false;
            }
            YamlMappingNode? nested = null;
            if (root.Children.TryGetValue(new YamlScalarNode("implementation_issue_packet"), out var child))
            {
                if (child is not YamlMappingNode mapping)
                {
                    error = "Packet implementation_issue_packet must be a mapping.";
                    return false;
                }
                nested = mapping;
            }
            if (!ReadField(root, nested, "domain", out var domain, out error)
                || !ReadField(root, nested, "target_repo", out var repo, out error)
                || !ReadField(root, nested, "source_execution_unit", out var unit, out error))
                return false;
            identity = new ApprovalPostingPacketIdentity(domain, repo, unit);
            return true;
        }
        catch (Exception exception) when (exception is YamlDotNet.Core.YamlException or InvalidOperationException)
        {
            error = "Packet identity could not be parsed: " + exception.Message;
            return false;
        }

        static bool ReadField(YamlMappingNode root, YamlMappingNode? nested, string name, out string? value, out string? error)
        {
            value = null;
            error = null;
            var rootPresent = root.Children.TryGetValue(new YamlScalarNode(name), out var rootNode);
            var nestedPresent = nested is not null && nested.Children.TryGetValue(new YamlScalarNode(name), out _);
            YamlNode? nestedNode = null;
            if (nestedPresent) nested!.Children.TryGetValue(new YamlScalarNode(name), out nestedNode);
            string? rootValue = null;
            string? nestedValue = null;
            if (rootPresent && !Scalar(rootNode!, out rootValue))
            {
                error = $"Packet {name} identity must be a nonempty scalar.";
                return false;
            }
            if (nestedPresent && !Scalar(nestedNode!, out nestedValue))
            {
                error = $"Packet implementation_issue_packet.{name} identity must be a nonempty scalar.";
                return false;
            }
            if (rootPresent && nestedPresent && !string.Equals(rootValue, nestedValue, StringComparison.Ordinal))
            {
                error = $"Packet has conflicting root and nested {name} identities.";
                return false;
            }
            value = nestedPresent ? nestedValue : rootPresent ? rootValue : null;
            return true;
        }

        static bool Scalar(YamlNode node, out string value)
        {
            value = string.Empty;
            if (node is not YamlScalarNode scalar || string.IsNullOrWhiteSpace(scalar.Value)
                || scalar.Style == YamlDotNet.Core.ScalarStyle.Plain && scalar.Value.Trim() is "~" or "null" or "Null" or "NULL")
                return false;
            value = scalar.Value.Trim();
            return true;
        }
    }

    private static (string? Path, bool Scoped) SelectQueuePath(string root, string domain, string repo, out string? error)
    {
        error = null;
        var scoped = RuntimeScopedStateResolver.GetScopedQueueStatePath(root, domain, repo)
            .Replace(root + Path.DirectorySeparatorChar, string.Empty, StringComparison.Ordinal)
            .Replace(Path.DirectorySeparatorChar, '/');
        var scopedInspection = InspectPath(root, scoped, expectDirectory: false);
        if (scopedInspection.Kind == PathKind.Unavailable)
        {
            error = scopedInspection.Detail;
            return (null, true);
        }
        if (scopedInspection.Kind != PathKind.Absent) return (scoped, true);
        var legacy = RuntimeScopedStateResolver.GetLegacyQueueStatePath(root)
            .Replace(root + Path.DirectorySeparatorChar, string.Empty, StringComparison.Ordinal)
            .Replace(Path.DirectorySeparatorChar, '/');
        var legacyInspection = InspectPath(root, legacy, expectDirectory: false);
        if (legacyInspection.Kind == PathKind.Unavailable)
        {
            error = legacyInspection.Detail;
            return (null, false);
        }
        return (legacy, false);
    }

    private static bool ValidateRawQueuePr(string queueJson, string unit, string targetRepo, QueueItem item, out int? number, out string? error)
    {
        number = null;
        error = null;
        using var document = JsonDocument.Parse(queueJson);
        if (document.RootElement.ValueKind != JsonValueKind.Object
            || !document.RootElement.TryGetProperty("items", out var items)
            || items.ValueKind != JsonValueKind.Array)
        {
            error = "Canonical queue JSON has no items array.";
            return false;
        }
        var rows = items.EnumerateArray().Where(row => row.ValueKind == JsonValueKind.Object
            && row.TryGetProperty("execution_unit", out var unitNode)
            && unitNode.ValueKind == JsonValueKind.String && unitNode.GetString() == unit).ToArray();
        if (rows.Length != 1)
        {
            error = "Canonical queue raw identity is ambiguous.";
            return false;
        }
        var row = rows[0];
        if (!row.TryGetProperty("linked_pr", out var linked) || linked.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            return true;
        if (linked.ValueKind == JsonValueKind.String)
        {
            var raw = linked.GetString()?.Trim();
            if (string.IsNullOrEmpty(raw)) return true;
            if (int.TryParse(raw, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var legacy))
            {
                if (legacy <= 0 || item.LinkedIssue?.Repo is not { Length: > 0 } issueRepo
                    || !string.Equals(issueRepo, targetRepo, StringComparison.OrdinalIgnoreCase))
                {
                    error = "A numeric legacy linked_pr requires a coherent linked_issue repo assertion.";
                    return false;
                }
                number = legacy;
                return true;
            }
            return TryParsePullUrl(raw, out var urlRepo, out var urlNumber) && SameRepo(urlRepo, targetRepo)
                ? SetNumber(urlNumber, out number)
                : Fail("linked_pr string is not a canonical repository-matching GitHub pull URL.", out error);
        }
        if (linked.ValueKind != JsonValueKind.Object)
        {
            error = "linked_pr must be a string, object, or null.";
            return false;
        }
        string? assertedRepo = null;
        int? assertedNumber = null;
        if (linked.TryGetProperty("repo", out var repoNode) && repoNode.ValueKind != JsonValueKind.Null)
        {
            if (repoNode.ValueKind != JsonValueKind.String || !CrossRuntimeReviewPaths.IsRepositoryName(repoNode.GetString()))
            { error = "linked_pr.repo is invalid."; return false; }
            assertedRepo = repoNode.GetString();
            if (!SameRepo(assertedRepo, targetRepo)) { error = "linked_pr.repo conflicts with packet target_repo."; return false; }
        }
        if (linked.TryGetProperty("number", out var numberNode) && numberNode.ValueKind != JsonValueKind.Null)
        {
            if (numberNode.ValueKind != JsonValueKind.Number || !numberNode.TryGetInt32(out var parsed) || parsed <= 0)
            { error = "linked_pr.number is invalid."; return false; }
            assertedNumber = parsed;
        }
        if (linked.TryGetProperty("url", out var urlNode) && urlNode.ValueKind != JsonValueKind.Null)
        {
            if (urlNode.ValueKind != JsonValueKind.String || !TryParsePullUrl(urlNode.GetString(), out var urlRepo, out var urlPr))
            { error = "linked_pr.url is invalid."; return false; }
            if (!SameRepo(urlRepo, targetRepo) || assertedRepo is not null && !SameRepo(urlRepo, assertedRepo)
                || assertedNumber is not null && urlPr != assertedNumber)
            { error = "linked_pr URL conflicts with its repo or PR number assertion."; return false; }
            assertedRepo = urlRepo;
            assertedNumber = urlPr;
        }
        if (assertedRepo is null || assertedNumber is null)
        {
            error = "Structured linked_pr needs a coherent repository-bearing URL or repo and positive number.";
            return false;
        }
        number = assertedNumber;
        return true;

        static bool SetNumber(int parsed, out int? value) { value = parsed; return true; }
        static bool Fail(string detail, out string? error) { error = detail; return false; }
    }

    private static bool TryParsePullUrl(string? value, out string repo, out int number)
        => TryParseGitHubIdentity(value, "pull", out repo, out number);

    private static bool TryParseGitHubIdentity(string? value, string expectedSegment, out string repo, out int number)
    {
        repo = string.Empty;
        number = 0;
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps
            || !string.Equals(uri.Host, "github.com", StringComparison.OrdinalIgnoreCase)
            || uri.Port != 443 || !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query)
            || !string.IsNullOrEmpty(uri.Fragment)) return false;
        var parts = uri.AbsolutePath.Trim('/').Split('/');
        if (parts.Length != 4 || parts[2] != expectedSegment
            || !int.TryParse(parts[3], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out number)
            || number <= 0) return false;
        repo = $"{parts[0]}/{parts[1]}";
        return CrossRuntimeReviewPaths.IsRepositoryName(repo);
    }

    private static bool SameRepo(string? left, string? right) => string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

    private enum PathKind { Absent, Regular, Directory, Unavailable }
    private sealed record ApprovalPostingPathInspection(PathKind Kind, string? Detail = null);

    private static ApprovalPostingPathInspection InspectPath(string root, string relative, bool expectDirectory)
    {
        try
        {
            var fullRoot = Path.GetFullPath(root);
            var candidate = Path.GetFullPath(Path.Combine(fullRoot, relative.Replace('/', Path.DirectorySeparatorChar)));
            var prefix = fullRoot.EndsWith(Path.DirectorySeparatorChar) ? fullRoot : fullRoot + Path.DirectorySeparatorChar;
            if (!candidate.StartsWith(prefix, StringComparison.Ordinal)) return new ApprovalPostingPathInspection(PathKind.Unavailable, "Path escapes the canonical checkout.");
            var pieces = Path.GetRelativePath(fullRoot, candidate).Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
            var current = fullRoot;
            for (var index = 0; index < pieces.Length; index++)
            {
                current = Path.Combine(current, pieces[index]);
                FileAttributes attributes;
                try { attributes = File.GetAttributes(current); }
                catch (FileNotFoundException) { return new ApprovalPostingPathInspection(PathKind.Absent); }
                catch (DirectoryNotFoundException) { return new ApprovalPostingPathInspection(PathKind.Absent); }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
                { return new ApprovalPostingPathInspection(PathKind.Unavailable, $"Could not inspect canonical path '{relative}': {exception.Message}"); }
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                    return new ApprovalPostingPathInspection(PathKind.Unavailable, $"Canonical path '{relative}' traverses a symbolic link or reparse point.");
                var isDirectory = (attributes & FileAttributes.Directory) != 0;
                if (index < pieces.Length - 1 && !isDirectory)
                    return new ApprovalPostingPathInspection(PathKind.Unavailable, $"Canonical path parent for '{relative}' is not a directory.");
                if (index == pieces.Length - 1)
                {
                    if (expectDirectory != isDirectory)
                        return new ApprovalPostingPathInspection(PathKind.Unavailable, $"Canonical path '{relative}' has the wrong file type.");
                    return new ApprovalPostingPathInspection(isDirectory ? PathKind.Directory : PathKind.Regular);
                }
            }
            return new ApprovalPostingPathInspection(PathKind.Unavailable, "Canonical path is empty.");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        { return new ApprovalPostingPathInspection(PathKind.Unavailable, exception.Message); }
    }

    private static bool TryReadRegular(string root, string relative, out string? text, out string? error)
    {
        text = null;
        var inspection = InspectPath(root, relative, expectDirectory: false);
        if (inspection.Kind != PathKind.Regular)
        {
            error = inspection.Kind == PathKind.Absent ? $"Canonical evidence '{relative}' is missing." : inspection.Detail ?? $"Canonical evidence '{relative}' is unavailable.";
            return false;
        }
        try { text = File.ReadAllText(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar))); error = null; return true; }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        { error = $"Canonical evidence '{relative}' could not be read: {exception.Message}"; return false; }
    }

    private static string Sha256Hex(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static byte[] SerializeAuditBytes<T>(T audit)
    {
        var json = JsonSerializer.SerializeToUtf8Bytes(audit, AuditJsonOptions);
        return [.. json, (byte)'\n'];
    }

    private static bool ReadAudit<T>(ApprovalPostingCheckout checkout, string relative, out T? value, out bool exists, out string? error)
        where T : class
    {
        value = null;
        exists = false;
        error = null;
        var inspection = InspectPath(checkout.Root, relative, expectDirectory: false);
        if (inspection.Kind == PathKind.Absent) return true;
        if (inspection.Kind != PathKind.Regular) { error = inspection.Detail ?? "Audit path is unavailable."; return false; }
        exists = true;
        try
        {
            value = JsonSerializer.Deserialize<T>(File.ReadAllText(Path.Combine(checkout.Root, relative.Replace('/', Path.DirectorySeparatorChar))), AuditJsonOptions);
            if (value is null) { error = $"Audit '{relative}' deserialized to null."; return false; }
            var shapeError = value switch
            {
                PreparedAudit prepared => ValidatePreparedAudit(prepared),
                ObservedAudit observed => ValidateObservedAudit(observed),
                _ => "Unsupported audit payload type.",
            };
            if (shapeError is not null) { error = $"Audit '{relative}' is invalid: {shapeError}"; return false; }
            return true;
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException or InvalidOperationException
            or ArgumentException or FormatException or OverflowException or NotSupportedException)
        { error = $"Audit '{relative}' is unreadable or invalid: {exception.Message}"; return false; }
    }

    private static string? ValidatePreparedAudit(PreparedAudit audit)
    {
        if (audit.SchemaVersion != "1" || audit.Operation != Operation)
            return "schema_version or operation is unsupported.";
        if (string.IsNullOrWhiteSpace(audit.OverrideId) || string.IsNullOrWhiteSpace(audit.ExecutionUnit)
            || string.IsNullOrWhiteSpace(audit.Domain) || string.IsNullOrWhiteSpace(audit.Team)
            || string.IsNullOrWhiteSpace(audit.Actor) || string.IsNullOrWhiteSpace(audit.NormalizedRole)
            || string.IsNullOrWhiteSpace(audit.Scope) || string.IsNullOrWhiteSpace(audit.TargetRepo)
            || string.IsNullOrWhiteSpace(audit.RequestedHeadSha) || string.IsNullOrWhiteSpace(audit.CanonicalSnapshotOid)
            || string.IsNullOrWhiteSpace(audit.CanonicalTargetRef) || string.IsNullOrWhiteSpace(audit.Reason)
            || string.IsNullOrWhiteSpace(audit.SelectedQueuePath) || string.IsNullOrWhiteSpace(audit.SelectedRunLogPath)
            || string.IsNullOrWhiteSpace(audit.PreparedPath) || string.IsNullOrWhiteSpace(audit.ObservedPath))
            return "a required string is null or blank.";
        if (audit.ClaimEpochClaimedAt == default || audit.RecordedAt == default || audit.PullRequest <= 0
            || !KnowledgeWriteBackRecord.TryValidateExecutionUnit(audit.ExecutionUnit, out _)
            || !CrossRuntimeReviewPaths.IsRepositoryName(audit.TargetRepo)
            || !CrossRuntimeReviewPaths.IsFullHeadSha(audit.RequestedHeadSha)
            || !IsGitObjectId(audit.CanonicalSnapshotOid)
            || audit.NormalizedRole != LogicalRoleNormalizer.Builder
            || audit.Scope != $"execution-unit:{audit.ExecutionUnit}")
            return "identity, time, or object-id fields are invalid.";
        if (audit.MissingRelations is null || audit.MissingRelations.Count is < 1 or > 2
            || audit.MissingRelations.Any(item => item is not (CrossRuntimeReviewRecord.RelationSameRuntime or CrossRuntimeReviewRecord.RelationCrossRuntime))
            || audit.MissingRelations.Distinct(StringComparer.Ordinal).Count() != audit.MissingRelations.Count)
            return "missing_relations must be a nonempty unique set of known relations.";
        if (audit.DecidingRecords is null || audit.QualifyingPostedReviews is null
            || audit.OriginalG834Result is null || audit.OriginalSoloResult is null
            || audit.CurrentLabels is null || audit.IntendedAddLabels is null || audit.IntendedRemoveLabels is null)
            return "a required collection or gate result is null.";
        if (audit.DecidingRecords.Any(item => item is null || string.IsNullOrWhiteSpace(item.Runtime)
                || !CrossRuntimeReviewRuntimes.IsSupported(item.Runtime)
                || item.Relation is not (CrossRuntimeReviewRecord.RelationSameRuntime or CrossRuntimeReviewRecord.RelationCrossRuntime)
                || item.Verdict != CrossRuntimeReviewVerdict.Approve
                || string.IsNullOrWhiteSpace(item.HeadSha) || string.IsNullOrWhiteSpace(item.File)
                || string.IsNullOrWhiteSpace(item.Sha256)
                || !CrossRuntimeReviewPaths.IsFullHeadSha(item.HeadSha)
                || !item.HeadSha.Equals(audit.RequestedHeadSha, StringComparison.OrdinalIgnoreCase)
                || item.Sha256.Length != 64 || !item.Sha256.All(Uri.IsHexDigit)))
            return "a deciding-record binding is null or malformed.";
        if (!IsValidLabelSet(audit.CurrentLabels) || !IsValidLabelSet(audit.IntendedAddLabels)
            || !IsValidLabelSet(audit.IntendedRemoveLabels))
            return "a current or intended label collection contains a blank or duplicate value.";
        var approvedPlan = AutomationPrTransitionCommand.PlanTransition("approved");
        if (!LabelSetsEqual(audit.IntendedAddLabels, approvedPlan.AddLabels)
            || !LabelSetsEqual(audit.IntendedRemoveLabels, approvedPlan.RemoveLabels))
            return "intended label changes differ from the normal approved-transition plan.";
        var historicalGateError = ValidateHistoricalGateEvidence(audit);
        if (historicalGateError is not null) return historicalGateError;
        return null;
    }

    private static string? ValidateHistoricalGateEvidence(PreparedAudit audit)
    {
        var g834 = audit.OriginalG834Result;
        if (g834.Decision != CrossRuntimeReviewGate.DecisionSatisfied || g834.Cause is not null
            || g834.Reasons is null || g834.Reasons.Count != 0
            || g834.ExecutionUnit != audit.ExecutionUnit || g834.Domain != audit.Domain || g834.Team != audit.Team)
            return "original_g834_result is not a satisfied, identity-bound gate result.";

        var solo = audit.OriginalSoloResult;
        if (solo.Decision != SoloConductorApprovalGate.DecisionRefused
            || solo.Cause != SoloConductorApprovalGate.CauseReviewMissing
            || solo.ExecutionUnit != audit.ExecutionUnit || solo.Domain != audit.Domain || solo.Team != audit.Team
            || !CrossRuntimeReviewPaths.IsFullHeadSha(solo.ExpectedHeadSha)
            || !CrossRuntimeReviewPaths.IsFullHeadSha(solo.ObservedHeadSha)
            || !string.Equals(solo.ExpectedHeadSha, audit.RequestedHeadSha, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(solo.ObservedHeadSha, audit.RequestedHeadSha, StringComparison.OrdinalIgnoreCase)
            || solo.QualifyingReviews is null || solo.Obligations is null || solo.Obligations.Count != 0
            || solo.UnscopableReviewIds is null || solo.UnscopableReviewIds.Count != 0
            || solo.SupersededInvalidReviewIds is null
            || solo.SupersededInvalidReviewIds.Any(id => id <= 0)
            || solo.SupersededInvalidReviewIds.Distinct().Count() != solo.SupersededInvalidReviewIds.Count
            || solo.RepairUnavailableReason is not null)
            return "original_solo_result is not a clean, identity-bound review-missing result.";

        if (solo.QualifyingReviews.Any(review => !IsValidHistoricalQualifyingReview(review))
            || audit.QualifyingPostedReviews.Any(review => !IsValidHistoricalQualifyingReview(review))
            || !solo.QualifyingReviews.SequenceEqual(audit.QualifyingPostedReviews))
            return "qualifying posted-review evidence does not match the original solo result.";

        foreach (var review in solo.QualifyingReviews.Where(item => item.CitedRecordPath is not null))
        {
            var decidingRecord = audit.DecidingRecords.SingleOrDefault(item => item.File == review.CitedRecordPath);
            if (decidingRecord is not null
                && (decidingRecord.Runtime != review.Runtime || decidingRecord.Relation != review.Relation))
                return "a qualifying posted review contradicts its cited deciding record runtime or relation.";
        }

        var expectedMissing = new HashSet<string>(StringComparer.Ordinal);
        foreach (var relation in new[]
                 {
                     CrossRuntimeReviewRecord.RelationSameRuntime,
                     CrossRuntimeReviewRecord.RelationCrossRuntime,
                 })
        {
            var decidingFiles = audit.DecidingRecords
                .Where(record => record.Relation == relation)
                .Select(record => record.File)
                .ToHashSet(StringComparer.Ordinal);
            if (decidingFiles.Count == 0)
                return "original_g834_result is satisfied but has no bound deciding record for a required relation.";
            if (!solo.QualifyingReviews.Any(review => review.Relation == relation
                    && review.CitedRecordPath is not null && decidingFiles.Contains(review.CitedRecordPath)))
                expectedMissing.Add(relation);
        }
        return expectedMissing.SetEquals(audit.MissingRelations)
            ? null
            : "missing_relations contradict the original deciding records and qualifying posted reviews.";
    }

    private static bool IsValidHistoricalQualifyingReview(SoloConductorQualifyingReview? review)
    {
        if (review is null || review.ReviewId <= 0 || string.IsNullOrWhiteSpace(review.Login)) return false;
        var hasRuntime = review.Runtime is not null;
        var hasRelation = review.Relation is not null;
        var hasCitedPath = review.CitedRecordPath is not null;
        if (!hasRuntime && !hasRelation && !hasCitedPath) return true;
        return hasRuntime && hasRelation && hasCitedPath
            && CrossRuntimeReviewRuntimes.IsSupported(review.Runtime)
            && review.Relation is CrossRuntimeReviewRecord.RelationSameRuntime or CrossRuntimeReviewRecord.RelationCrossRuntime
            && !string.IsNullOrWhiteSpace(review.CitedRecordPath);
    }

    private static bool IsValidLabelSet(IReadOnlyList<string> labels) =>
        labels.All(label => !string.IsNullOrWhiteSpace(label))
        && labels.Distinct(StringComparer.Ordinal).Count() == labels.Count;

    private static bool LabelSetsEqual(IReadOnlyList<string> left, IReadOnlyList<string> right) =>
        left.OrderBy(item => item, StringComparer.Ordinal)
            .SequenceEqual(right.OrderBy(item => item, StringComparer.Ordinal), StringComparer.Ordinal);

    private static string? ValidateObservedAudit(ObservedAudit audit)
    {
        if (audit.SchemaVersion != "1" || audit.Operation != Operation)
            return "schema_version or operation is unsupported.";
        if (string.IsNullOrWhiteSpace(audit.OverrideId) || string.IsNullOrWhiteSpace(audit.ExecutionUnit)
            || string.IsNullOrWhiteSpace(audit.Domain) || string.IsNullOrWhiteSpace(audit.Team)
            || string.IsNullOrWhiteSpace(audit.Actor) || string.IsNullOrWhiteSpace(audit.Scope)
            || string.IsNullOrWhiteSpace(audit.TargetRepo) || string.IsNullOrWhiteSpace(audit.RequestedHeadSha)
            || string.IsNullOrWhiteSpace(audit.PreparedPath) || string.IsNullOrWhiteSpace(audit.ObservedPath)
            || string.IsNullOrWhiteSpace(audit.PreparedSha256) || string.IsNullOrWhiteSpace(audit.PreparedPublicationCommit)
            || string.IsNullOrWhiteSpace(audit.HeadBeforeAction) || string.IsNullOrWhiteSpace(audit.HeadAfterAction)
            || string.IsNullOrWhiteSpace(audit.AuthorizationBasis) || string.IsNullOrWhiteSpace(audit.ObservationKind)
            || string.IsNullOrWhiteSpace(audit.ResultProvenance))
            return "a required string is null or blank.";
        if (audit.ClaimEpochClaimedAt == default || audit.ObservedAt == default || audit.PullRequest <= 0
            || !KnowledgeWriteBackRecord.TryValidateExecutionUnit(audit.ExecutionUnit, out _)
            || !CrossRuntimeReviewPaths.IsRepositoryName(audit.TargetRepo)
            || !CrossRuntimeReviewPaths.IsFullHeadSha(audit.RequestedHeadSha)
            || !CrossRuntimeReviewPaths.IsFullHeadSha(audit.HeadBeforeAction)
            || !CrossRuntimeReviewPaths.IsFullHeadSha(audit.HeadAfterAction)
            || !IsGitObjectId(audit.PreparedPublicationCommit)
            || audit.PreparedSha256.Length != 64 || !audit.PreparedSha256.All(Uri.IsHexDigit)
            || audit.Scope != $"execution-unit:{audit.ExecutionUnit}"
            || audit.LabelsObserved is null || !IsValidLabelSet(audit.LabelsObserved))
            return "identity, time, digest, or labels fields are invalid.";
        return audit.ObservationKind == "label-state-observed" && audit.ResultProvenance == "github-label-read"
            ? null
            : "observation provenance is unsupported.";
    }

    private static bool IsGitObjectId(string value) => value.Length is 40 or 64 && value.All(Uri.IsHexDigit);

    private static ExistingPair ReadPair(ApprovalPostingCheckout checkout, string runPath, ApprovalPostingOverrideRequest request, string preparedPath, string observedPath)
    {
        if (!ReadAudit<PreparedAudit>(checkout, preparedPath, out var prepared, out var preparedExists, out var preparedError))
            return Invalid(preparedError!, preparedExists, false);
        if (!ReadAudit<ObservedAudit>(checkout, observedPath, out var observed, out var observedExists, out var observedError))
            return Invalid(observedError!, preparedExists, observedExists);
        IReadOnlyList<RunEvent> events;
        var run = InspectPath(checkout.Root, runPath, expectDirectory: false);
        if (run.Kind == PathKind.Absent) events = [];
        else if (run.Kind != PathKind.Regular) return Invalid(run.Detail ?? "Selected run log is unavailable.", preparedExists, observedExists);
        else
        {
            if (!TryReadRegular(checkout.Root, runPath, out var content, out var readError)) return Invalid(readError!, preparedExists, observedExists);
            try { events = RunLogSerializer.DeserializeAll(content!); }
            catch (Exception exception) when (exception is JsonException or InvalidOperationException or ArgumentException)
            { return Invalid("Selected run log is malformed: " + exception.Message, preparedExists, observedExists); }
        }
        var preparedEvents = events.Where(e => e.ResultRef == preparedPath).ToArray();
        var observedEvents = events.Where(e => e.ResultRef == observedPath).ToArray();
        if (preparedExists != (preparedEvents.Length == 1) || !preparedExists && preparedEvents.Length != 0
            || observedExists && (!preparedExists || observedEvents.Length != 1)
            || !observedExists && observedEvents.Length != 0)
            return Invalid("Audit files and selected-run UUID events are incomplete, orphaned, or duplicated.", preparedExists, observedExists);
        if (prepared is not null && (prepared.OverrideId != request.OverrideId || prepared.PreparedPath != preparedPath || prepared.ObservedPath != observedPath))
            return Invalid("Prepared audit identity/path does not match the requested UUID.", preparedExists, observedExists);
        if (observed is not null && (observed.OverrideId != request.OverrideId || observed.PreparedPath != preparedPath || observed.ObservedPath != observedPath))
            return Invalid("Observed audit identity/path does not match the requested UUID.", preparedExists, observedExists);
        if (prepared is not null && !EventMatches(preparedEvents[0], prepared, preparedPath, PreparedEvent))
            return Invalid("Prepared audit identity conflicts with its selected-run UUID event.", preparedExists, observedExists);
        if (observed is not null && !EventMatches(observedEvents[0], observed, prepared!, observedPath, ObservedEvent))
            return Invalid("Observed audit identity conflicts with its selected-run UUID event.", preparedExists, observedExists);
        var preparedSha = preparedExists
            ? Sha256Hex(File.ReadAllBytes(Path.Combine(checkout.Root, preparedPath.Replace('/', Path.DirectorySeparatorChar))))
            : null;
        var observedSha = observedExists
            ? Sha256Hex(File.ReadAllBytes(Path.Combine(checkout.Root, observedPath.Replace('/', Path.DirectorySeparatorChar))))
            : null;
        if (observed is not null && observed.PreparedSha256 != preparedSha)
            return Invalid("Observed audit does not hash the exact immutable prepared bytes.", preparedExists, observedExists);
        return new ExistingPair(true, null, null, preparedExists, observedExists, prepared, observed,
            prepared is null ? null : CommitForPath(checkout, preparedPath),
            observed is null ? null : CommitForPath(checkout, observedPath), preparedSha, observedSha);

        ExistingPair Invalid(string detail, bool p, bool o) => new(false, "override-audit-unavailable", detail, p, o, null, null, null, null);
    }

    private static bool EventMatches(RunEvent runEvent, PreparedAudit audit, string path, string eventName) =>
        runEvent.ExecutionUnit == audit.ExecutionUnit && runEvent.Event == eventName && runEvent.ResultRef == path
        && SameRepo(runEvent.Repo, audit.TargetRepo) && runEvent.Pr == audit.PullRequest
        && runEvent.LinkedPr == $"https://github.com/{audit.TargetRepo}/pull/{audit.PullRequest}"
        && runEvent.By == "intent-cli automation pr-transition" && runEvent.Reason == audit.Reason
        && runEvent.TeamMode == TeamMode.SoloConductor && runEvent.ActorRole == LogicalRoleNormalizer.Builder
        && runEvent.Ts != default;

    private static bool EventMatches(RunEvent runEvent, ObservedAudit audit, PreparedAudit prepared, string path, string eventName) =>
        runEvent.ExecutionUnit == audit.ExecutionUnit && runEvent.Event == eventName && runEvent.ResultRef == path
        && SameRepo(runEvent.Repo, audit.TargetRepo) && runEvent.Pr == audit.PullRequest
        && runEvent.LinkedPr == $"https://github.com/{audit.TargetRepo}/pull/{audit.PullRequest}"
        && runEvent.By == "intent-cli automation pr-transition" && runEvent.Reason == prepared.Reason
        && runEvent.TeamMode == TeamMode.SoloConductor && runEvent.ActorRole == LogicalRoleNormalizer.Builder
        && runEvent.Ts != default;

    private static string? CommitForPath(ApprovalPostingCheckout checkout, string path)
    {
        var result = GitRun(checkout.Root, ["log", "-1", "--format=%H", "--", path]);
        return result.ExitCode == 0 && !string.IsNullOrWhiteSpace(result.StandardOutput)
            ? result.StandardOutput.Trim().ToLowerInvariant()
            : null;
    }

    private static bool Matches(PreparedAudit prepared, Assessment current, ApprovalPostingOverrideRequest request) =>
        prepared.Operation == Operation && prepared.OverrideId == request.OverrideId
        && prepared.ExecutionUnit == request.ExecutionUnit && prepared.Domain == current.Domain
        && prepared.Team == request.Team && prepared.Actor == request.Actor
        && prepared.NormalizedRole == LogicalRoleNormalizer.Builder
        && prepared.Scope == $"execution-unit:{request.ExecutionUnit}"
        && prepared.ClaimEpochClaimedAt == current.ClaimEpoch
        && string.Equals(prepared.TargetRepo, request.Repo, StringComparison.Ordinal) && prepared.PullRequest == request.PullRequest
        && prepared.RequestedHeadSha.Equals(request.HeadSha, StringComparison.OrdinalIgnoreCase)
        && prepared.Reason == request.Reason && prepared.SelectedQueuePath == current.QueuePath
        && prepared.SelectedRunLogPath == current.RunPath && prepared.CanonicalTargetRef == current.CanonicalTargetRef
        && LabelSetsEqual(prepared.IntendedAddLabels, current.AddLabels)
        && LabelSetsEqual(prepared.IntendedRemoveLabels,
            AutomationPrTransitionCommand.PlanTransition("approved").RemoveLabels)
        && (current.OrdinarySatisfied || current.MissingEligible
            && current.MissingRelations.All(relation => prepared.MissingRelations.Contains(relation, StringComparer.Ordinal)))
        && prepared.DecidingRecords.OrderBy(x => x.File, StringComparer.Ordinal).SequenceEqual(current.DecidingRecords.OrderBy(x => x.File, StringComparer.Ordinal));

    private static bool CanContinue(PreparedAudit prepared, Assessment current) =>
        current.Readable && (current.MissingEligible || current.OrdinarySatisfied)
        && current.ClaimEpoch == prepared.ClaimEpochClaimedAt;

    private static bool MatchesObserved(ObservedAudit observed, PreparedAudit prepared, string? preparedSha,
        Assessment current, ApprovalPostingOverrideRequest request) =>
        MatchesObservedImmutable(observed, prepared, preparedSha, request)
        && observed.ClaimEpochClaimedAt == current.ClaimEpoch;

    private static bool MatchesObservedImmutable(ObservedAudit observed, PreparedAudit prepared, string? preparedSha,
        ApprovalPostingOverrideRequest request) =>
        observed.SchemaVersion == "1" && observed.Operation == Operation
        && observed.OverrideId == request.OverrideId && observed.PreparedPath == prepared.PreparedPath
        && observed.ObservedPath == prepared.ObservedPath
        && observed.PreparedSha256 == preparedSha
        && observed.ExecutionUnit == request.ExecutionUnit && observed.Domain == prepared.Domain
        && observed.Team == prepared.Team && observed.Actor == prepared.Actor && observed.Scope == prepared.Scope
        && string.Equals(observed.TargetRepo, prepared.TargetRepo, StringComparison.Ordinal)
        && string.Equals(observed.TargetRepo, request.Repo, StringComparison.Ordinal) && observed.PullRequest == request.PullRequest
        && observed.RequestedHeadSha.Equals(request.HeadSha, StringComparison.OrdinalIgnoreCase)
        && string.Equals(observed.HeadBeforeAction, request.HeadSha, StringComparison.OrdinalIgnoreCase)
        && string.Equals(observed.HeadAfterAction, request.HeadSha, StringComparison.OrdinalIgnoreCase)
        && Converged(observed.LabelsObserved, prepared.IntendedAddLabels, prepared.IntendedRemoveLabels)
        && observed.ClaimEpochClaimedAt == prepared.ClaimEpochClaimedAt;

    private static ObservedAudit BuildObserved(PreparedAudit prepared, Assessment current, IReadOnlyList<string> labels,
        string preparedCommit, string preparedSha256, string basis, bool mutationAttempted, bool mayHaveApplied) => new()
    {
        SchemaVersion = "1", Operation = Operation, OverrideId = prepared.OverrideId,
        ExecutionUnit = prepared.ExecutionUnit, Domain = prepared.Domain, Team = prepared.Team,
        Actor = prepared.Actor, Scope = prepared.Scope, ClaimEpochClaimedAt = prepared.ClaimEpochClaimedAt,
        TargetRepo = prepared.TargetRepo, PullRequest = prepared.PullRequest, RequestedHeadSha = prepared.RequestedHeadSha,
        PreparedPath = prepared.PreparedPath, ObservedPath = prepared.ObservedPath,
        PreparedSha256 = preparedSha256,
        PreparedPublicationCommit = preparedCommit, ObservedAt = DateTimeOffset.UtcNow,
        HeadBeforeAction = prepared.RequestedHeadSha, HeadAfterAction = current.CurrentHead!,
        LabelsObserved = labels, AuthorizationBasis = basis,
        MutationAttempted = mutationAttempted, MayHaveApplied = mayHaveApplied,
        ObservationKind = "label-state-observed", ResultProvenance = "github-label-read",
    };

    private static PublicationResult PublishPrepared(CanonicalTarget target, ApprovalPostingOverrideRequest request)
    {
        var path = AuditPath(request.ExecutionUnit, request.OverrideId, "prepared.json");
        var observedPath = AuditPath(request.ExecutionUnit, request.OverrideId, "observed.json");
        string? lastError = null;
        bool mayHaveBeenPublished = false;
        PreparedAudit? lastAttemptAudit = null;
        byte[]? lastAttemptBytes = null;
        string? lastAttemptRunPath = null;
        string? lastAttemptCommit = null;

        PublicationResult StopBeforeAnotherAttempt(string cause, string? detail, string? currentRunPath)
        {
            if (!mayHaveBeenPublished)
                return new PublicationResult(false, cause, detail, path, currentRunPath, null);

            if (lastAttemptAudit is not null && lastAttemptBytes is not null
                && TryVerifyPreparedPublication(target, request, path, observedPath, lastAttemptRunPath!,
                    Sha256Hex(lastAttemptBytes), lastAttemptCommit, out var verified))
                return new PublicationResult(true, null, null, path, lastAttemptRunPath,
                    verified.PreparedCommit, verified.Prepared);

            try
            {
                using var checkout = ApprovalPostingCheckout.Open(target);
                var pair = ReadPair(checkout, lastAttemptRunPath!, request, path, observedPath);
                if (pair.Valid && !pair.PreparedExists && !pair.ObservedExists)
                    return new PublicationResult(false, cause, detail, path, currentRunPath, null);
            }
            catch (Exception exception) when (Expected(exception)) { }

            return new PublicationResult(false, "prepared-publication-unconfirmed",
                "A previous prepared push may have reached canonical state but its exact audit/event pair could not be verified. "
                + (detail ?? lastError ?? "The current guard prevented another publication attempt."),
                path, lastAttemptRunPath, lastAttemptCommit, MayHaveBeenPublished: true);
        }

        for (var attempt = 0; attempt < MaximumPublicationAttempts; attempt++)
        {
            try
            {
                using var checkout = ApprovalPostingCheckout.Open(target);
                var current = Assess(checkout, request);
                if (!current.Readable || !current.MissingEligible)
                    return StopBeforeAnotherAttempt(current.Cause ?? "eligibility-changed", current.Detail, current.RunPath);
                var existing = ReadPair(checkout, current.RunPath!, request, path, observedPath);
                if (!existing.Valid)
                    return StopBeforeAnotherAttempt(existing.Cause ?? "override-audit-unavailable", existing.Detail, current.RunPath);
                if (existing.Prepared is not null)
                    return existing.PreparedCommit is null
                        ? new PublicationResult(false, "prepared-publication-commit-unavailable",
                            "The existing prepared pair has no verifiable transaction commit.", path, current.RunPath, null,
                            MayHaveBeenPublished: true)
                        : new PublicationResult(true, null, null, path, current.RunPath, existing.PreparedCommit, existing.Prepared);
                var audit = CreatePrepared(current, request, path, observedPath);
                var relativeDirectory = Path.GetDirectoryName(Path.Combine(checkout.Root, path.Replace('/', Path.DirectorySeparatorChar)))!;
                Directory.CreateDirectory(relativeDirectory);
                var auditFull = Path.Combine(checkout.Root, path.Replace('/', Path.DirectorySeparatorChar));
                byte[] auditBytes;
                using (var stream = new FileStream(auditFull, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    auditBytes = WriterOperations.SerializeAudit(path, () => SerializeAuditBytes(audit));
                    stream.Write(auditBytes);
                }
                if (!AppendRunEvent(checkout, current, request, new RunEvent
                {
                    Ts = DateTimeOffset.UtcNow, ExecutionUnit = request.ExecutionUnit, Event = PreparedEvent,
                    By = "intent-cli automation pr-transition", Repo = request.Repo, Pr = request.PullRequest,
                    LinkedPr = $"https://github.com/{request.Repo}/pull/{request.PullRequest}", Reason = request.Reason,
                    TeamMode = TeamMode.SoloConductor, ActorRole = LogicalRoleNormalizer.Builder, ResultRef = path,
                }, out var appendError))
                    return StopBeforeAnotherAttempt("prepared-append-failed", appendError, current.RunPath);
                if (!CommitAndPush(checkout, [path, current.RunPath!], "Record prepared approval posting override", out var commit, out lastError))
                {
                    if (commit is not null)
                    {
                        lastAttemptAudit = audit;
                        lastAttemptBytes = auditBytes;
                        lastAttemptRunPath = current.RunPath;
                        lastAttemptCommit = commit;
                        mayHaveBeenPublished = true;
                    }
                    if (TryVerifyPreparedPublication(target, request, path, observedPath, current.RunPath!,
                            Sha256Hex(auditBytes), commit, out var publishedPair))
                        return new PublicationResult(true, null, null, path, current.RunPath,
                            publishedPair.PreparedCommit, publishedPair.Prepared);
                    if (attempt + 1 < MaximumPublicationAttempts) continue;
                    return StopBeforeAnotherAttempt("prepared-publication-unconfirmed", lastError, current.RunPath);
                }
                lastAttemptAudit = audit;
                lastAttemptBytes = auditBytes;
                lastAttemptRunPath = current.RunPath;
                lastAttemptCommit = commit;
                mayHaveBeenPublished = true;
                if (TryVerifyPreparedPublication(target, request, path, observedPath, current.RunPath!,
                        Sha256Hex(auditBytes), commit, out var verifiedPair))
                    return new PublicationResult(true, null, null, path, current.RunPath,
                        verifiedPair.PreparedCommit, verifiedPair.Prepared);
                lastError = "The canonical branch did not contain the exact prepared file, event, and transaction commit after push.";
            }
            catch (Exception exception) when (Expected(exception))
            {
                return StopBeforeAnotherAttempt("prepared-publication-failed", exception.Message, lastAttemptRunPath);
            }
        }
        return StopBeforeAnotherAttempt(mayHaveBeenPublished ? "prepared-publication-unconfirmed" : "prepared-publication-failed",
            lastError ?? "Canonical prepared publication was not confirmed.", lastAttemptRunPath);
    }

    private static PublicationResult PublishObserved(CanonicalTarget target, ApprovalPostingOverrideRequest request,
        PreparedAudit prepared, ObservedAudit observed)
    {
        string? lastError = null;
        bool mayHaveBeenPublished = false;
        byte[]? lastAttemptBytes = null;
        string? lastAttemptRunPath = null;
        string? lastAttemptCommit = null;
        string? lastPreparedSha256 = null;

        PublicationResult StopBeforeAnotherAttempt(string cause, string? detail, string? currentRunPath,
            bool canonicalOutcomeExists = false, string? canonicalOutcomeCommit = null)
        {
            if (!mayHaveBeenPublished)
                return new PublicationResult(false, cause, detail, prepared.ObservedPath, currentRunPath, canonicalOutcomeCommit,
                    CanonicalOutcomeExists: canonicalOutcomeExists);

            if (lastAttemptBytes is not null
                && TryVerifyObservedPublication(target, request, prepared, lastPreparedSha256!, lastAttemptBytes,
                    out var verifiedCommit, out _))
                return new PublicationResult(true, null, null, prepared.ObservedPath, lastAttemptRunPath, verifiedCommit);

            try
            {
                using var checkout = ApprovalPostingCheckout.Open(target);
                var pair = ReadPair(checkout, lastAttemptRunPath!, request, prepared.PreparedPath, prepared.ObservedPath);
                if (pair.Valid && pair.Prepared is not null && pair.Observed is null
                    && !pair.ObservedExists && pair.PreparedSha256 == lastPreparedSha256)
                    return new PublicationResult(false, cause, detail, prepared.ObservedPath, currentRunPath, null);
                if (pair.ObservedExists)
                    return new PublicationResult(false, "observed-binding-conflict",
                        pair.Detail ?? "An observed audit exists, but its immutable pair could not be verified.",
                        prepared.ObservedPath, lastAttemptRunPath, pair.ObservedCommit,
                        MayHaveBeenPublished: true, CanonicalOutcomeExists: true);
            }
            catch (Exception exception) when (Expected(exception)) { }

            return new PublicationResult(false, "observed-publication-unconfirmed",
                "A previous observed push may have reached canonical state but its exact audit/event pair could not be verified. "
                + (detail ?? lastError ?? "The current guard prevented another publication attempt."),
                prepared.ObservedPath, lastAttemptRunPath, lastAttemptCommit, MayHaveBeenPublished: true);
        }

        for (var attempt = 0; attempt < MaximumPublicationAttempts; attempt++)
        {
            try
            {
                using var checkout = ApprovalPostingCheckout.Open(target);
                var current = Assess(checkout, request);
                if (!current.Readable || !Matches(prepared, current, request) || !CanContinue(prepared, current))
                    return StopBeforeAnotherAttempt(current.Cause ?? "outcome-binding-changed", current.Detail, current.RunPath);
                var pair = ReadPair(checkout, current.RunPath!, request, prepared.PreparedPath, prepared.ObservedPath);
                if (!pair.Valid || pair.Prepared is null || pair.PreparedCommit is null)
                    return StopBeforeAnotherAttempt(pair.Cause ?? "prepared-pair-unavailable", pair.Detail,
                        current.RunPath, canonicalOutcomeExists: pair.ObservedExists);
                IGitHubLabelMutator? mutator = null;
                string? mutatorError = null;
                string? liveHead = null;
                IReadOnlyList<string>? liveLabels = null;
                string? liveError = null;
                if (!TryGetMutator(out mutator, out mutatorError)
                    || !TryObserve(request, mutator!, out liveHead, out liveLabels, out liveError)
                    || !string.Equals(liveHead, request.HeadSha, StringComparison.OrdinalIgnoreCase)
                    || !Converged(liveLabels ?? [], prepared.IntendedAddLabels, prepared.IntendedRemoveLabels))
                    return StopBeforeAnotherAttempt("observed-live-state-unconfirmed",
                        mutatorError ?? liveError ?? "The requested head and intended labels no longer converge.",
                        current.RunPath, canonicalOutcomeExists: pair.ObservedExists,
                        canonicalOutcomeCommit: pair.ObservedCommit);
                if (pair.Observed is not null)
                {
                    if (!MatchesObserved(pair.Observed, pair.Prepared, pair.PreparedSha256, current, request)
                        || pair.Observed.PreparedPublicationCommit != pair.PreparedCommit
                        || pair.ObservedCommit is null
                        || !Converged(liveLabels!, prepared.IntendedAddLabels, prepared.IntendedRemoveLabels))
                        return new PublicationResult(false, "observed-binding-conflict",
                            "The existing observed audit no longer matches the exact prepared bytes or current live state.",
                            prepared.ObservedPath, current.RunPath, pair.ObservedCommit,
                            CanonicalOutcomeExists: true);
                    return new PublicationResult(true, null, null, prepared.ObservedPath, current.RunPath, pair.ObservedCommit);
                }
                var refreshedObserved = BuildObserved(pair.Prepared, current, liveLabels!, pair.PreparedCommit,
                    pair.PreparedSha256!, observed.AuthorizationBasis, observed.MutationAttempted, observed.MayHaveApplied);
                Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(checkout.Root, prepared.ObservedPath.Replace('/', Path.DirectorySeparatorChar)))!);
                var auditBytes = WriterOperations.SerializeAudit(prepared.ObservedPath, () => SerializeAuditBytes(refreshedObserved));
                using (var stream = new FileStream(Path.Combine(checkout.Root, prepared.ObservedPath.Replace('/', Path.DirectorySeparatorChar)), FileMode.CreateNew, FileAccess.Write, FileShare.None))
                { stream.Write(auditBytes); }
                if (!AppendRunEvent(checkout, current, request, new RunEvent
                    {
                        Ts = DateTimeOffset.UtcNow, ExecutionUnit = request.ExecutionUnit, Event = ObservedEvent,
                        By = "intent-cli automation pr-transition", Repo = request.Repo, Pr = request.PullRequest,
                        LinkedPr = $"https://github.com/{request.Repo}/pull/{request.PullRequest}", Reason = request.Reason,
                        TeamMode = TeamMode.SoloConductor, ActorRole = LogicalRoleNormalizer.Builder, ResultRef = prepared.ObservedPath,
                    }, out var appendError))
                    return StopBeforeAnotherAttempt("observed-append-failed", appendError, current.RunPath);
                if (!CommitAndPush(checkout, [prepared.ObservedPath, current.RunPath!], "Record observed approval posting override", out var commit, out lastError))
                {
                    if (commit is not null)
                    {
                        lastAttemptBytes = auditBytes;
                        lastAttemptRunPath = current.RunPath;
                        lastAttemptCommit = commit;
                        lastPreparedSha256 = pair.PreparedSha256;
                        mayHaveBeenPublished = true;
                    }
                    if (TryVerifyObservedPublication(target, request, prepared, pair.PreparedSha256!, auditBytes,
                            out var remoteCommit, out var remoteError))
                        return new PublicationResult(true, null, null, prepared.ObservedPath, current.RunPath, remoteCommit);
                    lastError = remoteError ?? lastError;
                    if (attempt + 1 < MaximumPublicationAttempts) continue;
                    return StopBeforeAnotherAttempt("observed-publication-unconfirmed", lastError, current.RunPath);
                }
                lastAttemptBytes = auditBytes;
                lastAttemptRunPath = current.RunPath;
                lastAttemptCommit = commit;
                lastPreparedSha256 = pair.PreparedSha256;
                mayHaveBeenPublished = true;
                if (TryVerifyObservedPublication(target, request, prepared, pair.PreparedSha256!, auditBytes,
                        out var verifiedCommit, out var verifyError))
                    return new PublicationResult(true, null, null, prepared.ObservedPath, current.RunPath, verifiedCommit);
                lastError = verifyError ?? "The canonical branch did not contain the observed audit/event pair after push.";
            }
            catch (Exception exception) when (Expected(exception))
            {
                return StopBeforeAnotherAttempt("observed-publication-failed", exception.Message, lastAttemptRunPath);
            }
        }
        return StopBeforeAnotherAttempt(mayHaveBeenPublished ? "observed-publication-unconfirmed" : "observed-publication-failed",
            lastError ?? "Canonical outcome publication was not confirmed.", lastAttemptRunPath);
    }

    private static bool TryVerifyPreparedPublication(CanonicalTarget target, ApprovalPostingOverrideRequest request,
        string preparedPath, string observedPath, string runPath, string expectedSha, string? attemptedCommit,
        out ExistingPair verified)
    {
        verified = new ExistingPair(false, null, null, false, false, null, null, null, null);
        try
        {
            using var checkout = ApprovalPostingCheckout.Open(target);
            var pair = ReadPair(checkout, runPath, request, preparedPath, observedPath);
            if (!pair.Valid || pair.Prepared is null || pair.PreparedSha256 != expectedSha
                || string.IsNullOrWhiteSpace(attemptedCommit)
                || !string.Equals(pair.PreparedCommit, attemptedCommit, StringComparison.OrdinalIgnoreCase)) return false;
            verified = pair;
            return true;
        }
        catch (Exception exception) when (Expected(exception))
        {
            return false;
        }
    }

    private static bool TryVerifyObservedPublication(CanonicalTarget target, ApprovalPostingOverrideRequest request,
        PreparedAudit prepared, string expectedPreparedSha256, byte[] expectedObservedBytes,
        out string? commit, out string? error)
    {
        commit = null;
        error = null;
        try
        {
            using var checkout = ApprovalPostingCheckout.Open(target);
            var pair = ReadPair(checkout, prepared.SelectedRunLogPath, request, prepared.PreparedPath, prepared.ObservedPath);
            if (!pair.Valid || pair.Prepared is null || pair.Observed is null
                || pair.PreparedCommit is null || pair.ObservedCommit is null
                || pair.ObservedSha256 != Sha256Hex(expectedObservedBytes)
                || pair.PreparedSha256 != expectedPreparedSha256
                || !MatchesObservedImmutable(pair.Observed, pair.Prepared, pair.PreparedSha256, request)
                || pair.Observed.PreparedPublicationCommit != pair.PreparedCommit)
            {
                error = pair.Detail ?? "The exact observed audit/event pair is not present on the canonical branch.";
                return false;
            }
            commit = pair.ObservedCommit;
            return true;
        }
        catch (Exception exception) when (Expected(exception))
        {
            error = "The canonical outcome could not be re-read after the push attempt: " + exception.Message;
            return false;
        }
    }

    private static PreparedAudit CreatePrepared(Assessment current, ApprovalPostingOverrideRequest request, string preparedPath, string observedPath) => new()
    {
        SchemaVersion = "1", Operation = Operation, OverrideId = request.OverrideId,
        ExecutionUnit = request.ExecutionUnit, Domain = current.Domain!, Team = request.Team,
        Actor = request.Actor, NormalizedRole = LogicalRoleNormalizer.Builder,
        Scope = $"execution-unit:{request.ExecutionUnit}", ClaimEpochClaimedAt = current.ClaimEpoch!.Value,
        TargetRepo = request.Repo, PullRequest = request.PullRequest, RequestedHeadSha = request.HeadSha,
        CanonicalSnapshotOid = current.CanonicalSnapshotOid!, CanonicalTargetRef = current.CanonicalTargetRef!,
        RecordedAt = DateTimeOffset.UtcNow, Reason = request.Reason, SelectedQueuePath = current.QueuePath!,
        SelectedRunLogPath = current.RunPath!, PreparedPath = preparedPath, ObservedPath = observedPath,
        MissingRelations = current.MissingRelations,
        DecidingRecords = current.DecidingRecords, QualifyingPostedReviews = current.QualifyingReviews,
        OriginalG834Result = current.CrossRuntime!, OriginalSoloResult = current.Solo!,
        CurrentLabels = current.Labels, IntendedAddLabels = current.AddLabels, IntendedRemoveLabels = current.RemoveLabels,
    };

    private static bool AppendRunEvent(ApprovalPostingCheckout checkout, Assessment assessment, ApprovalPostingOverrideRequest request, RunEvent runEvent, out string? error)
    {
        var appended = false;
        string? appendError = null;
        try
        {
            WriterOperations.AppendRunEvent(assessment.RunPath!, runEvent.Event,
                () => appended = AppendRunEventCore(checkout, assessment, request, runEvent, out appendError));
            error = appendError;
            return appended;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            error = exception.Message;
            return false;
        }
    }

    private static bool AppendRunEventCore(ApprovalPostingCheckout checkout, Assessment assessment, ApprovalPostingOverrideRequest request, RunEvent runEvent, out string? error)
    {
        error = null;
        try
        {
            var full = Path.Combine(checkout.Root, assessment.RunPath!.Replace('/', Path.DirectorySeparatorChar));
            var inspection = InspectPath(checkout.Root, assessment.RunPath, expectDirectory: false);
            if (inspection.Kind == PathKind.Unavailable) { error = inspection.Detail; return false; }
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            var prefix = inspection.Kind == PathKind.Regular ? File.ReadAllBytes(full) : [];
            var row = Encoding.UTF8.GetBytes(RunLogSerializer.SerializeLine(runEvent) + "\n");
            using var output = new FileStream(full, FileMode.Append, FileAccess.Write, FileShare.None);
            if (prefix.Length > 0 && prefix[^1] != (byte)'\n') output.WriteByte((byte)'\n');
            output.Write(row);
            error = null;
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        { error = exception.Message; return false; }
    }

    private static bool CommitAndPush(ApprovalPostingCheckout checkout, IReadOnlyList<string> paths, string message, out string? commit, out string? error)
    {
        commit = null;
        error = null;
        try
        {
            var add = new List<string> { "add", "--" };
            add.AddRange(paths);
            var addResult = GitRun(checkout.Root, add, hostStateWrite: true);
            if (addResult.ExitCode != 0) { error = GitFailureDetail(addResult); return false; }
            var commitResult = GitRun(checkout.Root,
                ["-c", "user.name=intent-cli", "-c", "user.email=intent-cli@localhost", "commit", "--quiet", "-m", message],
                hostStateWrite: true);
            if (commitResult.ExitCode != 0) { error = GitFailureDetail(commitResult); return false; }
            commit = GitChecked(checkout.Root, ["rev-parse", "HEAD"]).StandardOutput.Trim().ToLowerInvariant();
            var push = GitRun(checkout.Root, ["push", "origin", $"HEAD:{checkout.Ref}"], hostStateWrite: true);
            if (push.ExitCode != 0) { error = GitFailureDetail(push); return false; }
            return true;
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or UnauthorizedAccessException)
        { error = exception.Message; return false; }
    }

    private static ProcessResult GitChecked(string workingDirectory, IReadOnlyList<string> arguments)
    {
        var result = GitRun(workingDirectory, arguments);
        if (result.ExitCode != 0)
            throw new InvalidOperationException($"git {string.Join(' ', arguments)} failed: {result.StandardError.Trim()}");
        return result;
    }

    private static ProcessResult GitRun(string workingDirectory, IReadOnlyList<string> arguments, bool hostStateWrite = false)
    {
        var result = hostStateWrite
            ? HostStateGitRetryRunner.Run(workingDirectory, arguments, () => RunGitOnce(workingDirectory, arguments))
            : RunGitOnce(workingDirectory, arguments);
        return new ProcessResult(result.ExitCode, result.StandardOutput, result.StandardError, result.RetryEvidence);
    }

    private static ClaimProcessResult RunGitOnce(string workingDirectory, IReadOnlyList<string> arguments)
    {
        var start = new ProcessStartInfo("git")
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new IOException("Could not start git.");
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();
        return new ClaimProcessResult(process.ExitCode, stdout, stderr);
    }

    private static string GitFailureDetail(ProcessResult result) => result.RetryEvidence is null
        ? result.StandardError.Trim()
        : HostStateGitRetryRunner.TerminalDetail(result.RetryEvidence);

    private sealed record ProcessResult(
        int ExitCode,
        string StandardOutput,
        string StandardError,
        HostStateGitRetryEvidence? RetryEvidence = null);

    private sealed record ApprovalPostingPacketIdentity(string? Domain, string? TargetRepo, string? SourceUnit);

    private static bool Expected(Exception exception) => exception is IOException or UnauthorizedAccessException
        or InvalidOperationException or JsonException or ArgumentException or FormatException
        or System.ComponentModel.Win32Exception or System.Security.SecurityException or YamlDotNet.Core.YamlException;

    private static string Recovery(ApprovalPostingOverrideRequest request) =>
        "intent-cli automation pr-transition"
        + $" --repo {ShellWord(request.Repo)} --pr {request.PullRequest} --transition approved"
        + $" --execution-unit {ShellWord(request.ExecutionUnit)} --head-sha {ShellWord(request.HeadSha)}"
        + $" --actor {ShellWord(request.Actor)} --team {ShellWord(request.Team)} --reason {ShellWord(request.Reason)}"
        + $" --override-loop-evidence --override-id {ShellWord(request.OverrideId)} --write --format {ShellWord(request.Format)}";

    private static string ShellWord(string value) =>
        "'" + value.Replace("'", "'\\''", StringComparison.Ordinal) + "'";

    private static string AuditPath(string unit, string id, string file) => $"{AuditRoot}/{unit}/{id}/{file}";

    private static bool TryGetMutator(out IGitHubLabelMutator mutator, out string? error)
    {
        try
        {
            mutator = AutomationPrTransitionCommand.MutatorFactory?.Invoke() ?? new GhCliGitHubLabelMutator();
            error = null;
            return true;
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException)
        {
            mutator = null!;
            error = exception.Message;
            return false;
        }
    }

    private static bool TryObserve(
        ApprovalPostingOverrideRequest request,
        IGitHubLabelMutator mutator,
        out string? head,
        out IReadOnlyList<string>? labels,
        out string? error)
    {
        head = null;
        labels = null;
        error = null;
        if (!AutomationPrTransitionCommand.TryReadCurrentHead(request.Repo, request.PullRequest, out head, out error))
            return false;
        try
        {
            labels = mutator.ReadLabels(request.Repo, GhCliGitHubLabelMutator.Kinds.Pr, request.PullRequest)
                .Select(item => item.Name).Where(name => !string.IsNullOrWhiteSpace(name)).ToArray();
            return true;
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException)
        {
            error = $"current labels could not be read: {exception.Message}";
            return false;
        }
    }

    private static bool Converged(IReadOnlyList<string> current, IReadOnlyList<string> add, IReadOnlyList<string> remove) =>
        LabelsEqual(current, Desired(current, add, remove));

    private static IReadOnlyList<string> Desired(IReadOnlyList<string> baseline, IReadOnlyList<string> add, IReadOnlyList<string> remove)
    {
        var removed = remove.ToHashSet(StringComparer.Ordinal);
        return baseline.Where(label => !removed.Contains(label)).Concat(add).Distinct(StringComparer.Ordinal)
            .OrderBy(label => label, StringComparer.Ordinal).ToArray();
    }

    private static bool LabelsEqual(IReadOnlyList<string> left, IReadOnlyList<string> right) =>
        left.OrderBy(item => item, StringComparer.Ordinal).SequenceEqual(right.OrderBy(item => item, StringComparer.Ordinal), StringComparer.Ordinal);

    private sealed record CanonicalTarget(string Remote, string Branch, string DefaultBranch);

    private sealed class ApprovalPostingCheckout : IDisposable
    {
        private ApprovalPostingCheckout(string root, string commit, string branch) { Root = root; Commit = commit; Branch = branch; }
        public string Root { get; }
        public string Commit { get; }
        public string Branch { get; }
        public string Ref => $"refs/heads/{Branch}";

        public static ApprovalPostingCheckout Open(CanonicalTarget target)
        {
            var root = Directory.CreateTempSubdirectory("intent-cli-g861-").FullName;
            try
            {
                GitChecked(root, ["clone", "--quiet", "--single-branch", "--branch", target.Branch, "--", target.Remote, root]);
                var commit = GitChecked(root, ["rev-parse", "HEAD"]).StandardOutput.Trim();
                if (commit.Length is not (40 or 64) || !commit.All(Uri.IsHexDigit))
                    throw new InvalidOperationException("The canonical metadata branch has no full Git object id.");
                return new ApprovalPostingCheckout(root, commit.ToLowerInvariant(), target.Branch);
            }
            catch { try { Directory.Delete(root, true); } catch { } throw; }
        }
        public void Dispose() { try { Directory.Delete(Root, true); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
    }

    private sealed class Assessment
    {
        public bool Readable { get; init; }
        public bool MissingEligible { get; init; }
        public bool OrdinarySatisfied { get; init; }
        public string? Cause { get; init; }
        public string? Detail { get; init; }
        public string? Domain { get; init; }
        public DateTimeOffset? ClaimEpoch { get; init; }
        public string? QueuePath { get; init; }
        public string? RunPath { get; init; }
        public string? CurrentHead { get; init; }
        public IReadOnlyList<string> Labels { get; init; } = [];
        public IReadOnlyList<string> MissingRelations { get; init; } = [];
        public IReadOnlyList<ApprovalPostingOverrideDecidingRecord> DecidingRecords { get; init; } = [];
        public IReadOnlyList<SoloConductorQualifyingReview> QualifyingReviews { get; init; } = [];
        public SoloConductorReviewTransitionOutcome? Solo { get; init; }
        public CrossRuntimeReviewTransitionOutcome? CrossRuntime { get; init; }
        public string? Basis { get; init; }
        public ClaimRecord? Claim { get; init; }
        public string? ClaimPath { get; init; }
        public string? CanonicalSnapshotOid { get; init; }
        public string? CanonicalTargetRef { get; init; }
        public string RunText { get; init; } = string.Empty;
        public IReadOnlyList<RunEvent> RunEvents { get; init; } = [];
        public IReadOnlyList<string> AddLabels { get; init; } = [];
        public IReadOnlyList<string> RemoveLabels { get; init; } = [];
        public string? SelectedQueueLayout { get; init; }

        public static Assessment Refused(string cause, string detail,
            SoloConductorReviewTransitionOutcome? solo = null,
            CrossRuntimeReviewTransitionOutcome? crossRuntime = null) => new()
        {
            Readable = false, Cause = cause, Detail = detail, Labels = [], Solo = solo, CrossRuntime = crossRuntime,
        };
    }

    private sealed record ExistingPair(
        bool Valid,
        string? Cause,
        string? Detail,
        bool PreparedExists,
        bool ObservedExists,
        PreparedAudit? Prepared,
        ObservedAudit? Observed,
        string? PreparedCommit,
        string? ObservedCommit,
        string? PreparedSha256 = null,
        string? ObservedSha256 = null);

    private sealed record PublicationResult(
        bool Published,
        string? Cause,
        string? Detail,
        string? Path,
        string? RunPath,
        string? Commit,
        PreparedAudit? Audit = null,
        bool MayHaveBeenPublished = false,
        bool CanonicalOutcomeExists = false)
    {
        public PublicationConfidence Confidence => Published
            ? PublicationConfidence.Published
            : MayHaveBeenPublished ? PublicationConfidence.Unconfirmed : PublicationConfidence.NotPublished;
    }

    private enum PublicationConfidence
    {
        NotPublished,
        Published,
        Unconfirmed,
    }
}

internal sealed record ApprovalPostingOverrideRequest
{
    public required string Repo { get; init; }
    public required int PullRequest { get; init; }
    public required string Transition { get; init; }
    public required string Mode { get; init; }
    public required string Format { get; init; }
    public required string HeadSha { get; init; }
    public required string ExecutionUnit { get; init; }
    public required string Actor { get; init; }
    public required string Team { get; init; }
    public required string Reason { get; init; }
    public required string OverrideId { get; init; }
}

internal sealed record ApprovalPostingOverrideResult
{
    [JsonPropertyName("requested")] public required bool Requested { get; init; }
    [JsonPropertyName("override_id")] public required string OverrideId { get; init; }
    [JsonPropertyName("disposition")] public required string Disposition { get; init; }
    [JsonPropertyName("cause")] public required string Cause { get; init; }
    [JsonPropertyName("detail")] public required string Detail { get; init; }
    [JsonPropertyName("reason")] public required string Reason { get; init; }
    [JsonPropertyName("prepared_published")] public required bool PreparedPublished { get; init; }
    [JsonPropertyName("outcome_published")] public required bool OutcomePublished { get; init; }
    [JsonPropertyName("mutation_attempted")] public required bool MutationAttempted { get; init; }
    [JsonPropertyName("may_have_applied")] public required bool MayHaveApplied { get; init; }
    [JsonPropertyName("label_state_observed")] public required bool LabelStateObserved { get; init; }
    [JsonPropertyName("prepared_path")] public string? PreparedPath { get; init; }
    [JsonPropertyName("observed_path")] public string? ObservedPath { get; init; }
    [JsonPropertyName("selected_run_log_path")] public string? SelectedRunLogPath { get; init; }
    [JsonPropertyName("prepared_commit")] public string? PreparedCommit { get; init; }
    [JsonPropertyName("outcome_commit")] public string? OutcomeCommit { get; init; }
    [JsonPropertyName("current_head")] public string? CurrentHead { get; init; }
    [JsonPropertyName("authorization_basis")] public string? AuthorizationBasis { get; init; }
    [JsonPropertyName("recovery_command")] public string? RecoveryCommand { get; init; }
}

internal sealed record ApprovalPostingOverrideDecidingRecord
{
    [JsonPropertyName("runtime")] public required string Runtime { get; init; }
    [JsonPropertyName("relation")] public required string Relation { get; init; }
    [JsonPropertyName("verdict")] public required string Verdict { get; init; }
    [JsonPropertyName("head_sha")] public required string HeadSha { get; init; }
    [JsonPropertyName("file")] public required string File { get; init; }
    [JsonPropertyName("sha256")] public required string Sha256 { get; init; }
}

internal sealed record PreparedAudit
{
    [JsonPropertyName("schema_version")] public required string SchemaVersion { get; init; }
    [JsonPropertyName("operation")] public required string Operation { get; init; }
    [JsonPropertyName("override_id")] public required string OverrideId { get; init; }
    [JsonPropertyName("execution_unit")] public required string ExecutionUnit { get; init; }
    [JsonPropertyName("domain")] public required string Domain { get; init; }
    [JsonPropertyName("team")] public required string Team { get; init; }
    [JsonPropertyName("actor")] public required string Actor { get; init; }
    [JsonPropertyName("normalized_role")] public required string NormalizedRole { get; init; }
    [JsonPropertyName("scope")] public required string Scope { get; init; }
    [JsonPropertyName("claim_epoch_claimed_at")] public required DateTimeOffset ClaimEpochClaimedAt { get; init; }
    [JsonPropertyName("target_repo")] public required string TargetRepo { get; init; }
    [JsonPropertyName("pull_request")] public required int PullRequest { get; init; }
    [JsonPropertyName("requested_head_sha")] public required string RequestedHeadSha { get; init; }
    [JsonPropertyName("canonical_snapshot_oid")] public required string CanonicalSnapshotOid { get; init; }
    [JsonPropertyName("canonical_target_ref")] public required string CanonicalTargetRef { get; init; }
    [JsonPropertyName("recorded_at")] public required DateTimeOffset RecordedAt { get; init; }
    [JsonPropertyName("reason")] public required string Reason { get; init; }
    [JsonPropertyName("selected_queue_path")] public required string SelectedQueuePath { get; init; }
    [JsonPropertyName("selected_run_log_path")] public required string SelectedRunLogPath { get; init; }
    [JsonPropertyName("prepared_path")] public required string PreparedPath { get; init; }
    [JsonPropertyName("observed_path")] public required string ObservedPath { get; init; }
    [JsonPropertyName("missing_relations")] public required IReadOnlyList<string> MissingRelations { get; init; }
    [JsonPropertyName("deciding_records")] public required IReadOnlyList<ApprovalPostingOverrideDecidingRecord> DecidingRecords { get; init; }
    [JsonPropertyName("qualifying_posted_reviews")] public required IReadOnlyList<SoloConductorQualifyingReview> QualifyingPostedReviews { get; init; }
    [JsonPropertyName("original_g834_result")] public required CrossRuntimeReviewTransitionOutcome OriginalG834Result { get; init; }
    [JsonPropertyName("original_solo_result")] public required SoloConductorReviewTransitionOutcome OriginalSoloResult { get; init; }
    [JsonPropertyName("current_labels")] public required IReadOnlyList<string> CurrentLabels { get; init; }
    [JsonPropertyName("intended_add_labels")] public required IReadOnlyList<string> IntendedAddLabels { get; init; }
    [JsonPropertyName("intended_remove_labels")] public required IReadOnlyList<string> IntendedRemoveLabels { get; init; }
}

internal sealed record ObservedAudit
{
    [JsonPropertyName("schema_version")] public required string SchemaVersion { get; init; }
    [JsonPropertyName("operation")] public required string Operation { get; init; }
    [JsonPropertyName("override_id")] public required string OverrideId { get; init; }
    [JsonPropertyName("execution_unit")] public required string ExecutionUnit { get; init; }
    [JsonPropertyName("domain")] public required string Domain { get; init; }
    [JsonPropertyName("team")] public required string Team { get; init; }
    [JsonPropertyName("actor")] public required string Actor { get; init; }
    [JsonPropertyName("scope")] public required string Scope { get; init; }
    [JsonPropertyName("claim_epoch_claimed_at")] public required DateTimeOffset ClaimEpochClaimedAt { get; init; }
    [JsonPropertyName("target_repo")] public required string TargetRepo { get; init; }
    [JsonPropertyName("pull_request")] public required int PullRequest { get; init; }
    [JsonPropertyName("requested_head_sha")] public required string RequestedHeadSha { get; init; }
    [JsonPropertyName("prepared_path")] public required string PreparedPath { get; init; }
    [JsonPropertyName("observed_path")] public required string ObservedPath { get; init; }
    [JsonPropertyName("prepared_sha256")] public required string PreparedSha256 { get; init; }
    [JsonPropertyName("prepared_publication_commit")] public required string PreparedPublicationCommit { get; init; }
    [JsonPropertyName("observed_at")] public required DateTimeOffset ObservedAt { get; init; }
    [JsonPropertyName("head_before_action")] public required string HeadBeforeAction { get; init; }
    [JsonPropertyName("head_after_action")] public required string HeadAfterAction { get; init; }
    [JsonPropertyName("labels_observed")] public required IReadOnlyList<string> LabelsObserved { get; init; }
    [JsonPropertyName("authorization_basis")] public required string AuthorizationBasis { get; init; }
    [JsonPropertyName("mutation_attempted")] public required bool MutationAttempted { get; init; }
    [JsonPropertyName("may_have_applied")] public required bool MayHaveApplied { get; init; }
    [JsonPropertyName("observation_kind")] public required string ObservationKind { get; init; }
    [JsonPropertyName("result_provenance")] public required string ResultProvenance { get; init; }
}
