namespace IntentSystem.Cli.Commands;

/// <summary>
/// G644/G658/G659: the small deployment contract that makes measured supervision
/// discoverable from the workflow guides. The detailed behavior remains in
/// the orchestration reference; these strings deliberately carry the setup
/// facts and a pointer rather than copying that reference.
/// </summary>
internal static class SupervisionGuideText
{
    public const string PreviewLabel =
        "Preview through 1.x (G628/G644/G658/G659/G671/G675/G676): supervision setup, scheduler emission, event mode, explicit pending-delegation dispositions, and duplicate-supervisor detection are outside the 1.0 compatibility promise.";

    public const string RuntimeSelfSufficiency =
        "G675 measured evidence (host macOS node 08, 2026-08-12, two separately attributed acts): act one observed `/usr/bin/env intent-cli` under launchd's minimal PATH as loaded but silently exiting with exit 127; a same-machine audit found four accumulated supervisors. Act two observed missing `herdr` causing ten false recipient losses in one cycle while the recipient was alive and mid-task. At emission, the scheduler artifact resolves intent-cli absolutely, carries each runtime transport executable absolutely when resolvable, names every unresolved binary, and records PATH for any remaining name. Verify the live PID and the first cycle record in `cycles.jsonl`; a loaded PID alone is not proof of a live loop. The loaded-but-silently-exiting / exit-127 shape is a scheduler-environment failure, not recipient loss.";

    public const string InstallBoundRule =
        "G704 install validation is fail-closed: `--bound` must be greater than or equal to `--interval`. A smaller bound is named `bound-below-interval` because a healthy supervisor is structurally judged absent (`supervisor-not-running`); the runtime warning remains defense in depth for legacy records and never corrects the value.";

    public const string InstallEvidenceRule =
        "A write reports success only after the managed process appends a first `cycles.jsonl` record with writer identity within the declared `--startup-bound` (default 30s). A timeout is named `first-cycle-proof-failed` and points to both stdout and stderr log paths; the artifact remains available for operator inspection. intent-cli still does not register, start, stop, or unregister the scheduler.";

    public const string InstallArtifactRule =
        "On macOS the launchd plist sets `WorkingDirectory` to the routing root and `StandardOutPath`/`StandardErrorPath` below `.intent-cli/supervision/<domain>/<team>/runtime/`; install output names all three paths. Re-emit an existing installation after changing these fields.";

    public const string DuplicateSupervisorDetection =
        "G676 adds an additive `writer` identity to new `cycles.jsonl` records (`pid`, `process_start_time`, and `host`); legacy records remain readable and do not produce a duplicate finding. Before registering the G658 artifact, the operator checks for and stops stale hand-run supervisors left behind by session restarts. When a recent cycle belongs to a different live writer, the loop emits a named `duplicate-supervisor` finding naming both writers, the other-cycle age, the duplicate-wake cost, and the per-team scheduler label `intent-cli.supervise.<domain>.<team>` as the remedy. G704 routes this same key through the recorded G699 repeat-backoff and parked state, so repeated cycles do not become a new repeat alarm. This is detection only: intent-cli never kills, stops, ranks, elects, locks, or leases a supervisor, and duplicate seat processes remain out of scope. The measured G676 incident on this machine and date (2026-08-12) found four concurrent loops for one team and duplicate wakes for the same stalls; the four-loop evidence is incident attribution, not a new recovery authority.";

    public const string DeploymentBasis =
        "Measured on this host (2026-08-07): omitting `--once` leaves the command looping. Measured with herdr 0.8.0 on macOS (other versions/platforms unverified): optional `--event-mode` holds blocking per-seat agent waits inside that same process for seconds-scale implementation/review settlement wakes, records wait death/error and re-arms, while the independent interval cycle remains the safety floor; both sources de-duplicate one transition/one wake. Deployment guidance (not a host measurement): deploy exactly one standing loop per team outside the seats; two supervisors on one team can wake the same stall twice; and a supervisor inside a seat cannot report its own death and dies with that seat. Use `intent-cli notify supervise install` to emit the current platform's scheduler artifact, or explicit `--platform macos|windows|linux` for cross-authoring; Windows and Linux output from macOS is emitted-but-unverified. The CLI prints the exact registration and unregistration commands but never runs them. Because the artifact embeds the invocation, event-mode adoption requires re-emitting with `supervise install --event-mode` and explicitly re-registering it; an installed artifact otherwise stays interval-only. Hand-written watchers are superseded only by adoption and are never killed by intent-cli. Leave process start/stop/management to the operator or scheduler; intent-cli neither starts nor manages it. Judge each team's supervisor liveness canonically from the age of that team's `cycles.jsonl` record against its declared bound. Process-name grep is an anti-pattern: on 2026-08-08 it conflated teams, killed the design team's own supervisor, retained another team's process, and hid the absence for about 47 hours until `absent_since_last_cycle=true` reported a 169796s gap. " + InstallBoundRule + " " + InstallArtifactRule + " " + InstallEvidenceRule;

    public const string ReferencePointer =
        "Read the orchestration reference for supervision semantics instead of copying them here: `docs/en/12-agent-message-orchestration.md` (JA mirror: `docs/ja/12-agent-message-orchestration.md`).";

    public const string InitHostSetup =
        "Set up exactly one standing `intent-cli notify supervise` loop per team outside the agent seats through `intent-cli notify supervise install`; inspect the emitted artifact and run its printed registration command as an explicit operator action. The command loops when `--once` is omitted; intent-cli neither starts nor manages that background process. "
        + DeploymentBasis + " " + RuntimeSelfSufficiency + " " + DuplicateSupervisorDetection + " " + ReferencePointer + " " + PreviewLabel;

    public static string HostLoopSetup(string domainPlaceholder, string targetRepoPlaceholder) =>
        $"Supervision setup (G644): G658 routes setup through `intent-cli notify supervise install`. Before relying on bounded recovery for `{domainPlaceholder}` / `{targetRepoPlaceholder}`, emit one per-team scheduler artifact for exactly one standing loop outside the agent seats, inspect it, then use the printed registration command as an explicit operator action. Do not put the supervisor inside a seat: it cannot report its own death and dies with that seat. intent-cli neither starts nor manages the background process. {DeploymentBasis} {RuntimeSelfSufficiency} {DuplicateSupervisorDetection} Use `intent-cli guide next --domain {domainPlaceholder} --team <team> --target-repo {targetRepoPlaceholder} --format markdown` to detect a team with no recorded cycle. {ReferencePointer} {PreviewLabel}";

    public static string NextAction(string domain, string team, string repo) =>
        $"Emit the scheduler artifact for exactly one standing supervision loop for `{domain}` / `{team}` outside the seats: `intent-cli notify supervise install --domain {domain} --team {team} --repo {repo} --owner-role <logical-role> --bound <seconds> --interval <seconds> --startup-bound <seconds> [--event-mode] --write --format json`. {InstallBoundRule} Inspect the artifact, then run the exact printed registration command as an explicit operator action; intent-cli executes no registration or process-management command. {InstallArtifactRule} {InstallEvidenceRule} To adopt event mode, re-emit with `--event-mode` and explicitly re-register because an installed artifact remains interval-only. Verify the live PID and the first cycle record in `cycles.jsonl`, then judge liveness from that record's age against its declared bound, never from process-name grep. {RuntimeSelfSufficiency} {DuplicateSupervisorDetection} {ReferencePointer}";
}
