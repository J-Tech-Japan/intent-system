namespace IntentSystem.Cli.Commands;

/// <summary>
/// G366: canonical palette for the intent workflow labels that
/// <c>intent-cli</c> owns end-to-end. Each entry pairs a label name
/// with its canonical color and description so the audit/sync
/// commands can detect repos where the same logical label was created
/// with a different color or description (a common drift signal once
/// the same workflow lights up across multiple repositories such as
/// <c>intent-system</c>, <c>SekibanAsAService</c>, <c>TraceForge</c>,
/// and <c>Zero4Racer</c>).
///
/// The palette only governs label metadata (color + description). It
/// does NOT add or remove label assignments from existing issues or
/// PRs — that remains the exclusive responsibility of the
/// <c>intent-cli automation</c> / <c>intent-cli worker</c> transition
/// commands.
///
/// Colors use the 6-character hex form GitHub returns from
/// <c>gh label list --json color</c> (no leading <c>#</c>), uppercased
/// so comparisons remain case-insensitive.
/// </summary>
internal static class WorkflowLabelPaletteContract
{
    /// <summary>
    /// G366: canonical entries in stable visual-lane order so audit
    /// output reads top-to-bottom through the workflow (target →
    /// implementation → review → approval).
    /// </summary>
    public static readonly IReadOnlyList<WorkflowLabelPaletteEntry> Canonical =
    [
        new()
        {
            Name = "intent-target",
            Color = "0E8A16",
            Description = "Intent automation target — implementation worker may claim this issue."
        },
        new()
        {
            Name = "intent-issue-in-progress",
            Color = "E11D21",
            Description = "Automation is currently working on this issue."
        },
        new()
        {
            Name = "intent-pr-created",
            Color = "1D76DB",
            Description = "PR created by intent automation; ready for review."
        },
        new()
        {
            Name = "intent-pr-reviewing",
            Color = "FBCA04",
            Description = "PR is currently under review by intent automation."
        },
        new()
        {
            Name = "intent-pr-request-update",
            Color = "D93F0B",
            Description = "Review requested updates to this PR; implementer must address."
        },
        new()
        {
            Name = "intent-pr-update-in-progress",
            Color = "BFD4F2",
            Description = "Implementer is updating this PR per review feedback."
        },
        new()
        {
            Name = "intent-pr-rereview-ready",
            Color = "5319E7",
            Description = "PR has been updated and is ready for re-review."
        },
        new()
        {
            Name = "intent-pr-approved",
            Color = "0E8A16",
            Description = "PR is approved by intent automation review; ready to merge + close out."
        },
        // G374: structured worker-signal protocol. These two labels are
        // the durable pending/handled markers a child implementation
        // worker uses to hand a blocker / follow-up / scope-warning back
        // to host review/design automation without touching host intent
        // metadata. They sit outside the linear target→review→approval
        // lane, so they appear last in the canonical palette.
        new()
        {
            Name = "intent-signal-sent",
            Color = "B60205",
            Description = "Item has an unhandled structured worker signal comment awaiting host review."
        },
        new()
        {
            Name = "intent-signal-handled",
            Color = "C2E0C6",
            Description = "Host/review side processed the current worker signal set."
        },
    ];
}

/// <summary>
/// G366: a single canonical label entry. All three fields are
/// authoritative: <see cref="WorkflowLabelPaletteAnalyzer"/> compares
/// observed <c>color</c> and <c>description</c> against these to
/// classify the label as <c>ok</c>, <c>wrong-color</c>,
/// <c>wrong-description</c>, <c>wrong-color-and-description</c>, or
/// <c>missing</c>.
/// </summary>
internal sealed record WorkflowLabelPaletteEntry
{
    public required string Name { get; init; }
    public required string Color { get; init; }
    public required string Description { get; init; }
}
