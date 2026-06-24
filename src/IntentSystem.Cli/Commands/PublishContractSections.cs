namespace IntentSystem.Cli.Commands;

/// <summary>
/// G482: single source of truth for the GitHub issue "Child Issue Contract"
/// section headings. Before G482 the same list was duplicated in
/// <see cref="PacketDraftCommand"/> (scaffold + draft validation) and
/// <c>IssueValidateBodyValidator</c> (publish validation); the two copies could
/// silently drift. Both now reference this constant so the scaffold template,
/// the draft contract check, and the publish gate can never disagree about
/// which sections are required.
///
/// <para>
/// <see cref="Required"/> is the fail-closed publish gate — the headings a
/// <c>github-body.md</c> MUST carry to be publish-ready. Keeping this set stable
/// preserves the contract for existing/in-flight host packets (G482 widens the
/// scaffold output and guidance, not the hard gate).
/// </para>
/// <para>
/// <see cref="ScaffoldHeadings"/> is the fuller shape a freshly drafted packet
/// emits by default. It is a superset of <see cref="Required"/> — it also
/// includes <c>Standalone Child Issue Contract</c> so a newly created packet is
/// publish-ready and self-describing without the author memorizing every
/// heading.
/// </para>
/// </summary>
internal static class PublishContractSections
{
    /// <summary>
    /// G482: the standalone <c>Standalone Child Issue Contract</c> heading — a
    /// one-paragraph restatement of what the child PR must deliver. Emitted by
    /// the scaffold so design-thread packets carry it by default.
    /// </summary>
    public const string StandaloneChildIssueContract = "Standalone Child Issue Contract";

    /// <summary>
    /// Required Child Issue Contract headings, in canonical order. This is the
    /// fail-closed publish gate shared by the packet scaffold's draft check and
    /// the publish-body validator. <c>Target Repo / Path / Part</c> is the
    /// canonical form; the hyphen variant <c>Target Repo-Path-Part</c> is
    /// accepted by the validator's alias map.
    /// </summary>
    public static readonly IReadOnlyList<string> Required =
        new[]
        {
            "Goal",
            "Why This Slice Exists Now",
            "Current Observed State",
            "Accepted Baseline You May Assume",
            "Target Repo / Path / Part",
            "In Scope",
            "Out Of Scope",
            "Acceptance Criteria",
            "Verification",
            "Related Links",
            // G347: base branch policy must be explicit in the published contract
            // so child implementation agents can choose the correct PR base
            // branch without reading host metadata.
            "Base Branch Policy",
        };

    /// <summary>
    /// G482: the full set of headings a freshly scaffolded <c>github-body.md</c>
    /// emits — every <see cref="Required"/> heading plus
    /// <see cref="StandaloneChildIssueContract"/>. New packets carry the
    /// complete publish-ready shape by default.
    /// </summary>
    public static readonly IReadOnlyList<string> ScaffoldHeadings =
        [.. Required, StandaloneChildIssueContract];
}
