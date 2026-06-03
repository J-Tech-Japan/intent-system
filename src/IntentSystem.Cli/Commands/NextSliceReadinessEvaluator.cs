using System.Text.Json.Serialization;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G449: the single shared next-slice readiness engine. Many surfaces
/// historically decided "is this candidate ready to cut an issue?" with their
/// own ad-hoc logic — <c>intent next-slice</c>, <c>automation
/// host-loop-next-action</c>, <c>automation host-review-diagnostics</c>,
/// packet draft validation, and <c>issue publish-flow</c> — which let one
/// surface report <c>issue-cut-ready</c> while another rejected the same
/// candidate, producing duplicate issues, no-op wakes, and contradictory
/// publish/idle signals.
///
/// This evaluator centralizes that decision into ONE deterministic precedence
/// so every surface can map its inputs through the same engine and agree on a
/// single terminal readiness class. It is pure (no I/O, no GitHub calls, no AI
/// provider) and fail-closed: any missing or ambiguous evidence resolves to a
/// NON-ready class, never to <c>issue-cut-ready</c>.
///
/// Precedence (highest first):
/// <list type="number">
///   <item><c>true-idle</c> — there is no candidate at all.</item>
///   <item><c>contract-incomplete</c> — the candidate's packet/issue contract
///         is missing required sections (publish-flow would reject it).</item>
///   <item><c>clarification-required</c> — an open hard clarification blocks
///         the candidate.</item>
///   <item><c>duplicate-existing</c> — GitHub already has an open issue/PR for
///         the candidate; publishing again would duplicate. Routes to
///         recovery/reconcile instead of publish.</item>
///   <item><c>issue-cut-ready</c> — contract complete, no clarification, no
///         duplicate. The only publishable class.</item>
/// </list>
/// </summary>
internal static class NextSliceReadinessEvaluator
{
    /// <summary>
    /// G449: shared adapter every publish-readiness surface calls for the
    /// contract-completeness gate (intent next-slice, packet draft validation,
    /// issue publish-flow, host-review diagnostics). Returns true only when the
    /// candidate is publishable per the single shared engine — i.e. it has a
    /// candidate, a complete contract, no open hard clarification, and no
    /// duplicate. Routing every surface through this one method guarantees a
    /// candidate rejected by one (e.g. publish-flow for an incomplete contract)
    /// is never reported <c>issue-cut-ready</c> by another (e.g. next-slice).
    /// </summary>
    public static bool IsPublishable(
        string? candidateExecutionUnit,
        bool contractComplete,
        bool openHardClarification = false,
        NextSliceExistingReference? existingGitHubReference = null) =>
        Evaluate(new NextSliceReadinessInput
        {
            HasCandidate = true,
            CandidateExecutionUnit = candidateExecutionUnit,
            ContractComplete = contractComplete,
            OpenHardClarification = openHardClarification,
            ExistingGitHubReference = existingGitHubReference,
        }).IssueCutReady;

    public static NextSliceReadinessResult Evaluate(NextSliceReadinessInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        var unit = string.IsNullOrWhiteSpace(input.CandidateExecutionUnit)
            ? null
            : input.CandidateExecutionUnit;

        // 1. No candidate → genuinely idle.
        if (!input.HasCandidate)
        {
            return new NextSliceReadinessResult
            {
                ReadinessClass = NextSliceReadinessClass.TrueIdle,
                ExecutionUnit = unit,
                Reason = "no actionable next-slice candidate is available.",
                Evidence = ["next-slice produced no candidate execution unit"],
            };
        }

        // 2. Contract incomplete — publish-flow would reject it, so the candidate
        // is never publishable. Fail-closed above the publishable class.
        if (!input.ContractComplete)
        {
            var missing = input.MissingContractSections.Count > 0
                ? input.MissingContractSections
                : new[] { "one or more required contract sections are missing" };
            return new NextSliceReadinessResult
            {
                ReadinessClass = NextSliceReadinessClass.ContractIncomplete,
                ExecutionUnit = unit,
                Reason = $"candidate '{unit ?? "<unknown>"}' has an incomplete issue/packet contract; publish-flow would reject it.",
                Evidence = missing.Select(section => $"missing/invalid contract: {section}").ToArray(),
            };
        }

        // 3. Clarification required — an open hard clarification blocks progress.
        if (input.OpenHardClarification)
        {
            return new NextSliceReadinessResult
            {
                ReadinessClass = NextSliceReadinessClass.ClarificationRequired,
                ExecutionUnit = unit,
                Reason = $"candidate '{unit ?? "<unknown>"}' is blocked by an open hard clarification.",
                Evidence =
                [
                    string.IsNullOrWhiteSpace(input.ClarificationSummary)
                        ? "an open hard clarification is present"
                        : $"open hard clarification: {input.ClarificationSummary}",
                ],
            };
        }

        // 4. Duplicate existing — GitHub already has an open issue/PR for this
        // unit. Suppress duplicate publish and route to recovery/reconcile.
        if (input.ExistingGitHubReference is { } reference)
        {
            return new NextSliceReadinessResult
            {
                ReadinessClass = NextSliceReadinessClass.DuplicateExisting,
                ExecutionUnit = unit,
                Reason = $"candidate '{unit ?? "<unknown>"}' already has an open GitHub {reference.Kind} #{reference.Number}; publishing again would duplicate it.",
                Evidence =
                [
                    $"existing GitHub {reference.Kind} #{reference.Number} references execution unit '{unit}'",
                    "route to reconcile/recovery instead of publishing a duplicate",
                ],
                ExistingReference = reference,
            };
        }

        // 5. Ready: contract complete, no clarification, no duplicate.
        return new NextSliceReadinessResult
        {
            ReadinessClass = NextSliceReadinessClass.IssueCutReady,
            ExecutionUnit = unit,
            Reason = $"candidate '{unit ?? "<unknown>"}' is contract-complete with no clarification or duplicate; ready to cut an issue.",
            Evidence =
            [
                "issue/packet contract is complete",
                "no open hard clarification",
                "no existing GitHub issue/PR for the candidate",
            ],
        };
    }
}

/// <summary>G449: canonical terminal readiness classes shared across surfaces.</summary>
internal static class NextSliceReadinessClass
{
    public const string TrueIdle = "true-idle";
    public const string ContractIncomplete = "contract-incomplete";
    public const string ClarificationRequired = "clarification-required";
    public const string DuplicateExisting = "duplicate-existing";
    public const string IssueCutReady = "issue-cut-ready";
}

/// <summary>G449: an existing GitHub work item that suppresses duplicate publish.</summary>
internal sealed record NextSliceExistingReference
{
    /// <summary>"issue" or "pr".</summary>
    public required string Kind { get; init; }
    public required int Number { get; init; }
    public string? Url { get; init; }
}

/// <summary>
/// G449: structured signals a surface projects into the shared evaluator. Each
/// surface populates the fields it can observe; the evaluator owns the
/// precedence so all surfaces reach the same verdict.
/// </summary>
internal sealed record NextSliceReadinessInput
{
    public required bool HasCandidate { get; init; }
    public string? CandidateExecutionUnit { get; init; }

    /// <summary>True when the issue/packet contract has all required sections
    /// (publish-flow contract gate would accept it).</summary>
    public bool ContractComplete { get; init; }

    /// <summary>Names of missing/invalid contract sections (evidence only).</summary>
    public IReadOnlyList<string> MissingContractSections { get; init; } = Array.Empty<string>();

    public bool OpenHardClarification { get; init; }
    public string? ClarificationSummary { get; init; }

    /// <summary>An existing open GitHub issue/PR for the candidate, if any.</summary>
    public NextSliceExistingReference? ExistingGitHubReference { get; init; }
}

/// <summary>
/// G449: the unified readiness verdict. Provides convenience flags and explicit
/// mappings onto each consuming surface's existing vocabulary so the surfaces
/// agree without re-deriving readiness independently.
/// </summary>
internal sealed record NextSliceReadinessResult
{
    [JsonPropertyName("readiness_class")]
    public required string ReadinessClass { get; init; }

    [JsonPropertyName("readinessClass")]
    public string ReadinessClassCamel => ReadinessClass;

    [JsonPropertyName("execution_unit")]
    public string? ExecutionUnit { get; init; }

    [JsonPropertyName("executionUnit")]
    public string? ExecutionUnitCamel => ExecutionUnit;

    [JsonPropertyName("reason")]
    public required string Reason { get; init; }

    [JsonPropertyName("evidence")]
    public required IReadOnlyList<string> Evidence { get; init; }

    [JsonPropertyName("existing_reference")]
    public NextSliceExistingReference? ExistingReference { get; init; }

    /// <summary>The ONLY publishable class. Every surface must gate publish on this.</summary>
    [JsonPropertyName("issue_cut_ready")]
    public bool IssueCutReady =>
        string.Equals(ReadinessClass, NextSliceReadinessClass.IssueCutReady, StringComparison.Ordinal);

    /// <summary>True when the verdict is genuine idleness (no candidate).</summary>
    [JsonPropertyName("true_idle")]
    public bool TrueIdle =>
        string.Equals(ReadinessClass, NextSliceReadinessClass.TrueIdle, StringComparison.Ordinal);

    /// <summary>True when an existing GitHub issue/PR should route to reconcile/recovery.</summary>
    [JsonPropertyName("routes_to_recovery")]
    public bool RoutesToRecovery =>
        string.Equals(ReadinessClass, NextSliceReadinessClass.DuplicateExisting, StringComparison.Ordinal);

    /// <summary>
    /// G449: map the canonical verdict onto the existing
    /// <see cref="NextSliceClassification"/> vocabulary so
    /// <c>intent next-slice classify</c> stays JSON-compatible while sharing
    /// this engine's decision.
    /// </summary>
    public string ToNextSliceClassification() => ReadinessClass switch
    {
        NextSliceReadinessClass.IssueCutReady => NextSliceClassification.IssueCutReady,
        NextSliceReadinessClass.ClarificationRequired => NextSliceClassification.ClarificationRequired,
        NextSliceReadinessClass.DuplicateExisting => NextSliceClassification.ExistingIssueNeedsRepair,
        NextSliceReadinessClass.ContractIncomplete => NextSliceClassification.InspectManually,
        _ => NextSliceClassification.NoActionableItem,
    };

    /// <summary>
    /// G449: map the canonical verdict onto the host-loop-next-action lane so
    /// the host loop and next-slice never contradict. A duplicate routes to the
    /// G444 stale-next-slice reconcile lane; only <c>issue-cut-ready</c> reaches
    /// the publish lane.
    /// </summary>
    public string ToHostLoopClassification() => ReadinessClass switch
    {
        NextSliceReadinessClass.IssueCutReady => HostLoopNextActionAnalyzer.ClassificationPublishNextIssue,
        NextSliceReadinessClass.DuplicateExisting => HostLoopNextActionAnalyzer.ClassificationStaleNextSliceReconcile,
        NextSliceReadinessClass.ClarificationRequired => HostLoopNextActionAnalyzer.ClassificationHardClarification,
        NextSliceReadinessClass.ContractIncomplete => HostLoopNextActionAnalyzer.ClassificationDesignNeeded,
        _ => HostLoopNextActionAnalyzer.ClassificationTrueIdle,
    };
}
