namespace IntentSystem.Cli.Commands;

/// <summary>
/// G645: the packet-time and closeout guidance for guide reachability.  The
/// declaration is deliberately carried as data: a tool must not infer which
/// role should discover a new surface, and it must not judge the wording of
/// the guide that was named.
/// </summary>
internal static class GuideReachabilityDuty
{
    /// <summary>The operator's keyword-to-guide standard.</summary>
    public const string Standard =
        "KEYWORD-TO-GUIDE STANDARD — handing a thread a keyword must be enough for that thread to reach the "
        + "named guide, understand the surface, and act. A role-facing surface that no guide names is "
        + "unreachable by the process meant to adopt it; reachability is a design obligation, not a memory test.";

    /// <summary>The packet authoring rule.</summary>
    public const string AuthoringRule =
        "Declare guide reachability while authoring the packet: for every role-facing surface, name the guide "
        + "surface and the role it routes to the new surface. If this slice adds no role-facing surface, record "
        + "that explicit no-surface decision. A blank declaration is not a decision, and reachability is never "
        + "inferred from filenames, keywords, or guide wording.";

    /// <summary>The closeout cadence and recording rule.</summary>
    public const string CloseoutCheck =
        "Same-cadence guide-reachability check: after the slice lands, confirm each packet-declared guide route "
        + "is recorded in the host with `intent-cli automation guide-reachability-record --execution-unit <unit> "
        + "--commit <host-sha> --write`. Until it is recorded, `automation stalled-work` reports a "
        + "`guide-reachability-pending` debt naming the execution unit and declared guide; an explicit no-surface "
        + "declaration produces no debt. This is not a merge gate and the CLI never judges guide quality or writes "
        + "guide content on design's behalf.";

    /// <summary>The canonical recording command for a unit.</summary>
    public static string RecordCommand(string executionUnit) =>
        $"intent-cli automation guide-reachability-record --execution-unit {executionUnit} "
        + "--commit <host-commit-sha> --write";
}
