namespace IntentSystem.Cli.Commands;

/// <summary>
/// G644: the small deployment contract that makes measured supervision
/// discoverable from the workflow guides. The detailed behavior remains in
/// the orchestration reference; these strings deliberately carry the setup
/// facts and a pointer rather than copying that reference.
/// </summary>
internal static class SupervisionGuideText
{
    public const string PreviewLabel =
        "Preview through 1.x (G628/G644): supervision setup is outside the 1.0 compatibility promise.";

    public const string DeploymentBasis =
        "Measured on this host (2026-08-07): only the loop behavior was observed here — omitting `--once` leaves the command looping. Deployment guidance (not a host measurement): deploy exactly one standing loop per team outside the seats; two supervisors on one team can wake the same stall twice; and a supervisor inside a seat cannot report its own death and dies with that seat. Leave process start/stop/management to the operator or scheduler; intent-cli neither starts nor manages it.";

    public const string ReferencePointer =
        "Read the orchestration reference for supervision semantics instead of copying them here: `docs/en/12-agent-message-orchestration.md` (JA mirror: `docs/ja/12-agent-message-orchestration.md`).";

    public const string InitHostSetup =
        "Set up exactly one standing `intent-cli notify supervise` loop per team outside the agent seats. The command loops when `--once` is omitted; intent-cli neither starts nor manages that background process. "
        + DeploymentBasis + " " + ReferencePointer + " " + PreviewLabel;

    public static string HostLoopSetup(string domainPlaceholder, string targetRepoPlaceholder) =>
        $"Supervision setup (G644): before relying on bounded recovery for `{domainPlaceholder}` / `{targetRepoPlaceholder}`, set up exactly one standing `intent-cli notify supervise` loop per team outside the agent seats. Do not put the supervisor inside a seat: it cannot report its own death and dies with that seat. intent-cli neither starts nor manages the background process. {DeploymentBasis} Use `intent-cli guide next --domain {domainPlaceholder} --team <team> --target-repo {targetRepoPlaceholder} --format markdown` to detect a team with no recorded cycle. {ReferencePointer} {PreviewLabel}";

    public static string NextAction(string domain, string team, string repo) =>
        $"Set up exactly one standing supervision loop for `{domain}` / `{team}` outside the seats, then leave it running: `intent-cli notify supervise --domain {domain} --team {team} --repo {repo} --owner-role <logical-role> --write --format json`. The command loops when `--once` is omitted; do not add an interval or bound here. {ReferencePointer}";
}
