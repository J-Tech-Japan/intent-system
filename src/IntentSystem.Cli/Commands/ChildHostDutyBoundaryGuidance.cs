using System.Text.Json.Serialization;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G733: the implementation seat owns child-repository work, while the
/// non-sandboxed host role owns host-state work.  This is rendered by the
/// child implementation guides so the division is a shipped contract rather
/// than an accidental property of a co-located machine.
/// </summary>
internal static class ChildHostDutyBoundaryGuidance
{
    public const string TargetRepositoryLabelContract =
        "Raw/manual target-repository label mutation is forbidden, and host-state publication/linkage remains host-owned. The sole child exception is the installed canonical `worker complete --kind issue --outcome pr-created --github-only --write`, which may apply target-repository `intent-target` to the PR; `intent-pr-created` remains on the source issue and is never applied to the PR. For PR `repair-pushed`, canonical child completion changes only PR repair labels.";

    public static ChildHostDutyBoundary Build(string domainPlaceholder) =>
        new()
        {
            Summary =
                "The implementation seat owns the child repository end to end: it reads the assigned GitHub issue, fetches origin/main, creates the branch, edits and tests the child code, commits, pushes, opens the ready-for-review PR, and reports the result. Host state is a separate authority and is never a prerequisite for those child-repository steps.",
            SeatResponsibilities = new[]
            {
                "Use the assigned issue/PR and target-repository GitHub facts as the child contract; run the child workflow from the child worktree.",
                "Run `git fetch origin main`, create the implementation branch from `origin/main`, edit only the requested child-repository paths, run focused tests, commit, push, and open a ready-for-review PR containing `Closes #<issue>`.",
                "Use `intent-cli worker ... --github-only` for target-repository lifecycle labels and completion. For issue-to-PR `pr-created`, the canonical completion may apply target-repository `intent-target` to the PR; that command-owned transition is allowed and is not raw label mutation or host-state publication.",
                "Emit the exact host-duty request over `intent-cli notify report` when host evidence is missing or a host-owned operation is required; do not turn that request into a local host write."
            },
            HostResponsibilities = new[]
            {
                "Own `.intent-cli/` queue, claims, runs, packet, and metadata state, including the host Git refresh and host-repository pushes.",
                "Acquire and verify the execution-unit claim with the canonical claim commands, preserving the Git compare-and-swap transaction and returning its JSON evidence to the implementation seat.",
                "Perform host-repository credential/API operations and host-state linkage, queue publication, review, or closeout that the child `--github-only` path explicitly cannot perform."
            },
            HostDutyRequest = BuildHostDutyRequest(domainPlaceholder),
            HostClaimCommands = new[]
            {
                "intent-cli claim acquire --scope execution-unit:<EU> --actor <actor> --team <team> --write --format json",
                "intent-cli claim verify --scope execution-unit:<EU> --team <team> --format json"
            },
            ClaimSafety =
                "Claim acquisition remains compare-and-swap: host `claim acquire` performs pull, immutable claim record, commit, and plain push; only `status=acquired` with `push_succeeded=true` is ownership. The returned scope, actor, team, and commit must match, and `claim verify` must report `passed=true` / `status=owned`. A lifecycle label, local claim file, local commit, or preflight read is not ownership; force-push, expiry, or inferred takeover is forbidden.",
            SeatCannot = new[]
            {
                "Read or mutate host `.intent-cli/`, queue-state, claims, runs, packet files, metadata branches, or host-repository Git state.",
                "acquire, release, or take over an execution-unit claim; the seat can only consume host-returned claim evidence.",
                "Use host-repository credentials or the host-repository GitHub API, or perform host-state linkage, queue publication, review, or closeout transitions.",
                "Widen its sandbox, add a host root, create an improvised clone, or make co-located host access a hidden fallback."
            },
            CoLocationRule =
                "A co-located machine may make host files or host credentials appear readable today, but that is not a capability to design against: remote-herdr can place the implementation seat on another VM with neither shared filesystem nor host credentials. Therefore the child path must remain correct when host state, host credentials, and the host-repository GitHub API are all unavailable; request host duties through the message channel instead.",
            Verification = new[]
            {
                "Seat-owned issue-to-PR proof: emitted `worker ... --github-only` JSON plus child `git fetch`, branch, test, commit, push, and ready-for-review PR evidence, with no host-repository command in the path.",
                "Behavioral label proof: canonical child-cwd `worker complete --kind issue --outcome pr-created --github-only --write` applies `intent-pr-created` to the source issue and `intent-target` to the target-repository PR; this installed transition is allowed, while raw/manual label mutation is forbidden and `linked_pr_synced=false` remains host-state follow-up.",
                "Host-duty proof: the exact `intent-cli notify report ... --status question ...` request names both canonical claim commands and asks for their JSON evidence, including the pushed commit and `passed=true` verification.",
                "Boundary proof: a test-owned sentinel under the host routing root is refused by the sandbox (`touch <host-routing-root>/.intent-cli/probe-should-fail` exits nonzero with `Operation not permitted`); do not widen roots or tidy a file if the probe unexpectedly succeeds."
            }
        };

    public static string RenderPromptBlock(string domainPlaceholder)
    {
        var boundary = Build(domainPlaceholder);
        return $@"## Seat/host duty boundary (G733)

{boundary.Summary}

### The implementation seat performs
{RenderBullets(boundary.SeatResponsibilities)}

### The host role performs
{RenderBullets(boundary.HostResponsibilities)}

### Exact host-duty request over the message channel

If the host claim evidence is missing or a host-owned operation is needed, send this exact canonical request through `intent-cli notify report`; never hand-write agmsg/herdr transport and never attempt the host operation from the child seat:

```bash
{boundary.HostDutyRequest}
```

The host role runs the named claim commands and returns their JSON evidence:

```bash
{string.Join(Environment.NewLine, boundary.HostClaimCommands)}
```

### Claim safety and the seat's remaining limits

{boundary.ClaimSafety}

The seat still cannot:
{RenderBullets(boundary.SeatCannot)}

### Why co-located host access is not a dependency

{boundary.CoLocationRule}

### Required emitted verification

{RenderBullets(boundary.Verification)}";
    }

    private static string BuildHostDutyRequest(string domainPlaceholder) =>
        $"intent-cli notify report --domain {domainPlaceholder} --team <team> --from implementation --to orchestration --task-id <task-id> --status question --artifact <child-artifact> --summary 'HOST DUTY REQUEST: run intent-cli claim acquire --scope execution-unit:<EU> --actor <actor> --team <team> --write --format json; then intent-cli claim verify --scope execution-unit:<EU> --team <team> --format json; return the JSON evidence, pushed commit, and owned verdict.' --routing-root <host-routing-root> --report-root . --write --format json";

    private static string RenderBullets(IEnumerable<string> values) =>
        string.Join(Environment.NewLine, values.Select(value => $"- {value}"));
}

internal sealed record ChildHostDutyBoundary
{
    [JsonPropertyName("summary")]
    public required string Summary { get; init; }

    [JsonPropertyName("seat_responsibilities")]
    public required IReadOnlyList<string> SeatResponsibilities { get; init; }

    [JsonPropertyName("host_responsibilities")]
    public required IReadOnlyList<string> HostResponsibilities { get; init; }

    [JsonPropertyName("host_duty_request")]
    public required string HostDutyRequest { get; init; }

    [JsonPropertyName("host_claim_commands")]
    public required IReadOnlyList<string> HostClaimCommands { get; init; }

    [JsonPropertyName("claim_safety")]
    public required string ClaimSafety { get; init; }

    [JsonPropertyName("seat_cannot")]
    public required IReadOnlyList<string> SeatCannot { get; init; }

    [JsonPropertyName("co_location_rule")]
    public required string CoLocationRule { get; init; }

    [JsonPropertyName("verification")]
    public required IReadOnlyList<string> Verification { get; init; }
}
