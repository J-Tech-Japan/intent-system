namespace IntentSystem.Cli.Commands;

/// <summary>
/// G644/G658: the small deployment contract that makes measured supervision
/// discoverable from the workflow guides. The detailed behavior remains in
/// the orchestration reference; these strings deliberately carry the setup
/// facts and a pointer rather than copying that reference.
/// </summary>
internal static class SupervisionGuideText
{
    public const string PreviewLabel =
        "Preview through 1.x (G628/G644/G658): supervision setup and scheduler emission are outside the 1.0 compatibility promise.";

    public const string DeploymentBasis =
        "Measured on this host (2026-08-07): only the loop behavior was observed here — omitting `--once` leaves the command looping. Deployment guidance (not a host measurement): deploy exactly one standing loop per team outside the seats; two supervisors on one team can wake the same stall twice; and a supervisor inside a seat cannot report its own death and dies with that seat. Use `intent-cli notify supervise install` to emit the current platform's scheduler artifact, or explicit `--platform macos|windows|linux` for cross-authoring; Windows and Linux output from macOS is emitted-but-unverified. The CLI prints the exact registration and unregistration commands but never runs them. Leave process start/stop/management to the operator or scheduler; intent-cli neither starts nor manages it. Judge each team's supervisor liveness canonically from the age of that team's `cycles.jsonl` record against its declared bound. Process-name grep is an anti-pattern: on 2026-08-08 it conflated teams, killed the design team's own supervisor, retained another team's process, and hid the absence for about 47 hours until `absent_since_last_cycle=true` reported a 169796s gap.";

    public const string ReferencePointer =
        "Read the orchestration reference for supervision semantics instead of copying them here: `docs/en/12-agent-message-orchestration.md` (JA mirror: `docs/ja/12-agent-message-orchestration.md`).";

    public const string InitHostSetup =
        "Set up exactly one standing `intent-cli notify supervise` loop per team outside the agent seats through `intent-cli notify supervise install`; inspect the emitted artifact and run its printed registration command as an explicit operator action. The command loops when `--once` is omitted; intent-cli neither starts nor manages that background process. "
        + DeploymentBasis + " " + ReferencePointer + " " + PreviewLabel;

    public static string HostLoopSetup(string domainPlaceholder, string targetRepoPlaceholder) =>
        $"Supervision setup (G644): G658 routes setup through `intent-cli notify supervise install`. Before relying on bounded recovery for `{domainPlaceholder}` / `{targetRepoPlaceholder}`, emit one per-team scheduler artifact for exactly one standing loop outside the agent seats, inspect it, then use the printed registration command as an explicit operator action. Do not put the supervisor inside a seat: it cannot report its own death and dies with that seat. intent-cli neither starts nor manages the background process. {DeploymentBasis} Use `intent-cli guide next --domain {domainPlaceholder} --team <team> --target-repo {targetRepoPlaceholder} --format markdown` to detect a team with no recorded cycle. {ReferencePointer} {PreviewLabel}";

    public static string NextAction(string domain, string team, string repo) =>
        $"Emit the scheduler artifact for exactly one standing supervision loop for `{domain}` / `{team}` outside the seats: `intent-cli notify supervise install --domain {domain} --team {team} --repo {repo} --owner-role <logical-role> --bound <seconds> --interval <seconds> --write --format json`. Inspect the artifact, then run the exact printed registration command as an explicit operator action; intent-cli executes no registration or process-management command. Verify liveness from the age of this team's `cycles.jsonl` record against its declared bound, never from process-name grep. {ReferencePointer}";
}
