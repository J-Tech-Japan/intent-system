namespace IntentSystem.Cli.Commands;

/// <summary>
/// Allowed recommended next operator actions for <c>status brief</c>.
/// G179: deterministic single-string output the AI tasking thread can switch on.
/// </summary>
internal static class StatusBriefRecommendation
{
    public const string ReviewCloseout = "review-closeout";
    public const string ClarificationRequired = "clarification-required";
    public const string IssueCutReady = "issue-cut-ready";
    public const string SkipDueToWip = "skip-due-to-wip";
    public const string NoActionableItem = "no-actionable-item";
    public const string InspectManually = "inspect-manually";
}
