namespace IntentSystem.Cli.Commands;

/// <summary>
/// G563: single source of truth for the dispatcher-skill carve-out that every
/// local-skill prohibition surface must carry.
///
/// G559 made this CLI ship its own agent skill (<c>intent-cli skill install</c>).
/// Every prohibition written before that shipped forbids "local skill files"
/// without qualification, so an agent that reached the guide *through* the
/// shipped skill was told not to use the thing that brought it there. The
/// carve-out is narrow and conditional: it exempts only the CLI-owned
/// <c>intent-cli</c> dispatcher skill, and only because that skill restates no
/// workflow, is single-sourced from this CLI with <c>skill diff</c> drift
/// detection, and is distributed exclusively by <c>skill install</c>. Local
/// skill files that restate workflow stay forbidden exactly as before.
///
/// Centralizing the wording means the prohibition surfaces cannot drift apart:
/// tests assert this text appears on each surface rather than re-spelling it.
/// </summary>
internal static class DispatcherSkillCarveOut
{
    /// <summary>Name of the one CLI-owned skill artifact.</summary>
    public const string SkillName = "intent-cli";

    /// <summary>The only sanctioned distribution command for that artifact.</summary>
    public const string InstallCommand = "intent-cli skill install";

    /// <summary>
    /// Canonical carve-out sentence. Added verbatim to every guide surface
    /// that carries a blanket local-skill prohibition, so the permission and
    /// its three conditions are always stated together with the prohibition.
    /// </summary>
    public const string Sentence =
        "CARVE-OUT: the CLI-owned `intent-cli` dispatcher skill installed by `intent-cli skill install` is PERMITTED — "
        + "it restates no workflow, is single-sourced from this CLI with `intent-cli skill diff` drift detection, and is "
        + "distributed only by `intent-cli skill install`. Local skill files that restate workflow (`gh-issue-to-pr`, "
        + "`gh-fix-pr-comment`, copied runbooks) remain forbidden.";

    /// <summary>
    /// Entry used inside structured forbidden-source lists. Keeps the
    /// "local skill files" anchor those lists have always carried while
    /// naming the one exemption, so a list of forbidden sources never
    /// silently includes the permitted artifact.
    /// </summary>
    public const string ForbiddenSourceItem =
        "local skill files that restate workflow (the CLI-owned `intent-cli` dispatcher skill installed by "
        + "`intent-cli skill install` is exempt)";

    /// <summary>
    /// Same entry for the lists that also name the historical adapters. Kept
    /// separate rather than folded into <see cref="ForbiddenSourceItem"/> so
    /// those lists keep the concrete examples operators recognize.
    /// </summary>
    public const string ForbiddenSourceItemWithExamples =
        "local skill files that restate workflow (gh-issue-to-pr, gh-fix-pr-comment, etc.; the CLI-owned "
        + "`intent-cli` dispatcher skill installed by `intent-cli skill install` is exempt)";

    /// <summary>
    /// Short clause appended to the G300 / G330 / G333 child-cwd boundary
    /// lines, which enumerate "local skills" among host-owned state a child
    /// loop must not touch. The boundary is about host state, not about the
    /// child's own tooling, so the exemption belongs on the same line.
    /// </summary>
    public const string BoundaryClause =
        "The CLI-owned `intent-cli` dispatcher skill installed by `intent-cli skill install` is exempt from this "
        + "local-skill prohibition: it restates no workflow and carries no host state.";
}
