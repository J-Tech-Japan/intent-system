namespace IntentSystem.Cli.Commands;

/// <summary>
/// G382: pure classifier that turns the set of interview dimensions an
/// agent has resolved into a readiness verdict — the measurable finish
/// line for the persistent interview mode (G381). It never reads host
/// state or GitHub; the agent passes the dimensions it has resolved and
/// the analyzer reports whether the interview is ready to become a
/// packet/issue, which dimensions are still missing, and the next
/// highest-value question to ask. Advisory only — it does not publish
/// anything.
///
/// Dimensions form three tiers:
/// - <b>blocking</b> (<c>owner-decision</c>, <c>open-decisions</c>):
///   while unresolved the verdict is <c>clarification-required</c>
///   regardless of how complete the rest is — a pending product-owner
///   decision must be made before drafting.
/// - <b>issue</b> (goal, scope, non-goals, constraints, target,
///   acceptance, verification): the standalone child-issue contract core.
/// - <b>packet</b> (dependencies, risks): the extra thoroughness a packet
///   draft wants on top of the issue contract.
/// </summary>
internal static class InterviewReadinessAnalyzer
{
    public static class Classifications
    {
        public const string PacketReady = "packet-ready";
        public const string IssueReady = "issue-ready";
        public const string ClarificationRequired = "clarification-required";
        public const string RemainingGaps = "remaining-gaps";
    }

    public static class Tiers
    {
        public const string Blocking = "blocking";
        public const string Issue = "issue";
        public const string Packet = "packet";
    }

    /// <summary>
    /// The eleven readiness dimensions in next-question priority order
    /// (highest value first). Blocking decisions come first so a pending
    /// owner decision is surfaced before documentation gaps.
    /// </summary>
    public static readonly IReadOnlyList<InterviewReadinessDimension> Dimensions = new[]
    {
        new InterviewReadinessDimension("owner-decision", "Owner / operator decision required", Tiers.Blocking,
            "Is there a product-owner / operator decision still required before this can proceed? If yes, ask for that exact decision now."),
        new InterviewReadinessDimension("open-decisions", "Open decisions", Tiers.Blocking,
            "Which open decisions remain unresolved? Resolve each (or record it as deferred) before drafting."),
        new InterviewReadinessDimension("goal", "Goal / problem statement", Tiers.Issue,
            "What is the one-sentence goal / problem this work solves?"),
        new InterviewReadinessDimension("scope", "Scope", Tiers.Issue,
            "What is explicitly in scope for this slice?"),
        new InterviewReadinessDimension("target", "Target repo / path / component", Tiers.Issue,
            "Which repo, paths, and component does this change target?"),
        new InterviewReadinessDimension("acceptance", "Acceptance criteria", Tiers.Issue,
            "What are the concrete, checkable acceptance criteria?"),
        new InterviewReadinessDimension("verification", "Verification approach", Tiers.Issue,
            "How will the change be verified (tests / commands / evidence)?"),
        new InterviewReadinessDimension("constraints", "Constraints", Tiers.Issue,
            "What constraints (stack, performance, compatibility, policy) bound the solution?"),
        new InterviewReadinessDimension("non-goals", "Non-goals", Tiers.Issue,
            "What is explicitly out of scope / a non-goal?"),
        new InterviewReadinessDimension("dependencies", "Dependencies", Tiers.Packet,
            "What does this depend on (other units, external services, prior PRs)?"),
        new InterviewReadinessDimension("risks", "Risks / edge cases", Tiers.Packet,
            "What are the main risks and edge cases to handle?"),
    };

    public static InterviewReadinessResult Analyze(IReadOnlyCollection<string> resolvedDimensions)
    {
        ArgumentNullException.ThrowIfNull(resolvedDimensions);

        var resolved = new HashSet<string>(
            resolvedDimensions
                .Select(d => d?.Trim().ToLowerInvariant() ?? string.Empty)
                .Where(d => d.Length > 0),
            StringComparer.Ordinal);

        var statuses = Dimensions
            .Select(dimension => new InterviewReadinessDimensionStatus
            {
                Key = dimension.Key,
                Name = dimension.Name,
                Tier = dimension.Tier,
                Resolved = resolved.Contains(dimension.Key),
            })
            .ToArray();

        var missing = statuses.Where(s => !s.Resolved).Select(s => s.Key).ToArray();

        bool AllResolved(string tier) =>
            Dimensions.Where(d => string.Equals(d.Tier, tier, StringComparison.Ordinal))
                .All(d => resolved.Contains(d.Key));

        var blockingResolved = AllResolved(Tiers.Blocking);
        var issueResolved = AllResolved(Tiers.Issue);
        var packetResolved = AllResolved(Tiers.Packet);

        string classification;
        if (!blockingResolved)
        {
            // A pending owner/open decision blocks drafting regardless of
            // documentation completeness.
            classification = Classifications.ClarificationRequired;
        }
        else if (issueResolved && packetResolved)
        {
            classification = Classifications.PacketReady;
        }
        else if (issueResolved)
        {
            classification = Classifications.IssueReady;
        }
        else
        {
            classification = Classifications.RemainingGaps;
        }

        // Next highest-value question: the first missing dimension in
        // priority order (Dimensions is already ordered).
        var nextDimension = Dimensions.FirstOrDefault(d => !resolved.Contains(d.Key));

        var summary = classification switch
        {
            Classifications.PacketReady => "All issue + packet dimensions resolved and no blocking decision remains; ready to draft the packet (with operator acceptance).",
            Classifications.IssueReady => "Issue-contract dimensions resolved and no blocking decision remains; ready to publish the issue (with operator acceptance). Resolve dependencies + risks for packet-ready.",
            Classifications.ClarificationRequired => "A blocking owner/open decision is unresolved; stop and resolve it before drafting.",
            _ => $"{missing.Length} dimension(s) still missing; keep interviewing — ask the next highest-value question.",
        };

        return new InterviewReadinessResult
        {
            Classification = classification,
            Dimensions = statuses,
            MissingDimensions = missing,
            NextQuestion = nextDimension is null ? null : nextDimension.NextQuestion,
            NextQuestionDimension = nextDimension?.Key,
            Summary = summary,
        };
    }
}

/// <summary>G382: a readiness dimension definition (static catalog).</summary>
internal sealed record InterviewReadinessDimension(string Key, string Name, string Tier, string NextQuestion);
