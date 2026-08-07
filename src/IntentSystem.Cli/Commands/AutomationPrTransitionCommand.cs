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
        out string error)
    {
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
                    error = $"Unknown argument '{argument}'. Supported: [--repo <owner/repo>] [--workdir <path>] --pr <n> --transition <review-start|request-update|approved|review-release> [--write] [--dry-run] [--format text|json].";
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

    private static void WriteHelp(TextWriter writer)
    {
        writer.WriteLine("automation pr-transition");
        writer.WriteLine("Usage: intent-cli automation pr-transition --repo <owner/repo> --pr <n> --transition <review-start|request-update|approved|review-release> [--write] [--dry-run] [--format text|json]");
        writer.WriteLine("Supported transitions:");
        writer.WriteLine("- review-start");
        writer.WriteLine("- request-update");
        writer.WriteLine("- approved");
        writer.WriteLine("- review-release (G292: drop intent-pr-reviewing without adding intent-pr-request-update; use when host-owned metadata blocks closeout)");
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
}
