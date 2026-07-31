namespace IntentSystem.Cli.Commands;

/// <summary>
/// G564: the operator's 2026-07-31 ruling, single-sourced so every guide
/// surface that states it states the SAME thing — the duty, the packet-time
/// authoring rule, and the closeout-cadence check.
///
/// The ruling: intent-cli is developed with intent-cli, and the intent tree
/// falling behind development is itself a serious fault
/// (「開発だけ進め、intent-tree を更新しないことは重要な過失」). Reinforcing the
/// tree CONCURRENTLY with development is a primary design responsibility, not
/// a nice-to-have that a busy release can defer.
///
/// The field evidence is the pre-v0.7.0 audit: G559 shipped while node 09
/// still described a pre-implementation design, node 02 recorded none of the
/// seven release-flow rules the docs implement, and node 08 lagged the wake
/// contract by releases — weeks of drift with no structural signal, found only
/// by a manual operator-ordered audit right before a release.
///
/// These strings are guidance TEXT. They never write intent content: the tree
/// is written by design, and this CLI only declares, records, and detects.
/// </summary>
internal static class IntentTreeCoEvolutionDuty
{
    /// <summary>The duty itself, for design-thread guidance.</summary>
    public const string Duty =
        "PRIMARY DESIGN DUTY — intent-tree co-evolution: the intent tree moves WITH development, not after it. "
        + "Leaving the tree unupdated while implementation advances is a serious fault in its own right, not a "
        + "deferred chore: a tree that describes a design the code no longer has is worse than no tree, because "
        + "every downstream packet, review, and audit is written against it. Reinforce the tree in the same wake "
        + "that changes the surface it describes.";

    /// <summary>The packet-authoring rule this duty implies.</summary>
    public const string AuthoringRule =
        "Declare knowledge write-backs HONESTLY at packet-authoring time: a slice that adds a surface or changes "
        + "behavior almost always owes the tree something, so `knowledge_updates.*.required` and "
        + "`closeout_learning.write_back_required` must reflect what is actually owed. Defaulting them to `false` "
        + "to keep the packet quiet is the failure mode this rule exists to stop — an undeclared obligation is "
        + "invisible to closeout, to review, and to `automation stalled-work`.";

    /// <summary>The closeout-cadence check the duty implies (G524 same-cadence rule).</summary>
    public const string CloseoutCheck =
        "Same-cadence write-back check: perform the packet's declared write-backs and RECORD them in the same "
        + "closeout wake, with `intent-cli automation knowledge-writeback-record --execution-unit <unit> --commit "
        + "<host-sha> --write`. Until it is recorded, the unit stays visible as a `knowledge-writeback-pending` "
        + "item in `automation stalled-work` / `automation heartbeat` — closing the PR does not clear it, and "
        + "nothing here writes intent content on design's behalf.";

    /// <summary>The canonical recording command for <paramref name="executionUnit"/>.</summary>
    public static string RecordCommand(string executionUnit) =>
        $"intent-cli automation knowledge-writeback-record --execution-unit {executionUnit} "
        + "--commit <host-commit-sha> [--target <path>]... --write";
}
