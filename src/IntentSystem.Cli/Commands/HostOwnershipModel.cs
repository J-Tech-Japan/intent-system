using System.Text.Json.Serialization;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G326: structured ownership model for the role-scoped host topology.
///
/// The system intentionally runs from multiple local worktrees against
/// the same remote — a <em>design</em> workspace (MyIntentHost),
/// one or more <em>review-runtime</em> workspaces (IntentSystemReview,
/// SekibanAsAServiceReview), and per-issue <em>child-worker</em>
/// implementation worktrees. The problem isn't multiple worktrees — it's
/// that without an explicit ownership boundary the same queue-state /
/// runs.jsonl / packet files get written from incompatible angles.
///
/// This model is the canonical source of truth for which role owns
/// which write surface; commands that emit ownership-aware guidance
/// (<see cref="GuideHostOwnershipCommand"/>, the
/// <c>intent-cli guide automation setup</c> contract surface, the
/// G324 closeout durable writes) all reference it.
///
/// Pure data — no I/O, no Process.Start, no provider launch.
/// </summary>
internal static class HostOwnershipModel
{
    public const string RoleDesign = "design";
    public const string RoleReviewRuntime = "review-runtime";
    public const string RoleChildWorker = "child-worker";
    public const string RoleAmbiguous = "ambiguous";

    /// <summary>
    /// G326: canonical list of host roles. Order is the order operators
    /// see in markdown rendering; JSON output mirrors the same order
    /// for stable diffs.
    /// </summary>
    public static IReadOnlyList<HostOwnershipRole> Roles { get; } = new HostOwnershipRole[]
    {
        new()
        {
            Id = RoleDesign,
            Summary = "Intent design / packet backlog workspace (e.g. MyIntentHost).",
            Responsibilities = new[]
            {
                "Shape intents under `intents/<domain>/**`.",
                "Draft and maintain prepared packets (packet.yaml + implementation.md + review-context.md).",
                "Author clarification artifacts and chase Hard Clarification answers.",
                "Prepare NextSlice candidates so review-runtime can publish them as GitHub issues.",
                "Update host bindings / automation summary intent metadata."
            },
            MayWrite = new[]
            {
                "intents/<domain>/**",
                ".intent-cli/issues/<execution-unit>/packet.yaml",
                ".intent-cli/issues/<execution-unit>/implementation.md",
                ".intent-cli/issues/<execution-unit>/review-context.md",
                ".intent-cli/issues/<execution-unit>/github-body.md",
                "intents/<domain>/clarifications/**"
            },
            MustNotWrite = new[]
            {
                "Closeout records on `.intent-cli/runs.jsonl` (review-runtime owns the runtime audit trail).",
                "Submodule pointers from a merge commit (review-runtime owns runtime sync).",
                "Labels on a merged PR (host review owns transition; child-worker drives child label state)."
            }
        },
        new()
        {
            Id = RoleReviewRuntime,
            Summary = "Review / closeout / runtime workspace (e.g. IntentSystemReview, SekibanAsAServiceReview).",
            Responsibilities = new[]
            {
                "Pull --ff-only continuously; never block on operator design work.",
                "Run host-review (G316 packet-aware review).",
                "Mark approval / request-update through `intent-cli automation pr-transition`.",
                "Run `intent-cli closeout pr --write` for merged PRs; emit current-schema (G324) RunEvent lines.",
                "Apply submodule pointer updates from the merged commit.",
                "Publish the next-slice issue from a prepared packet when WIP cap is clear.",
                "Optionally produce a runtime-created NextSlice packet (record ownership label so design knows)."
            },
            MayWrite = new[]
            {
                ".intent-cli/queue-state.json (closeout state transition; G324 forward-only delta).",
                ".intent-cli/runs.jsonl (closeout-recorded / pr-merged / publish events; append-only).",
                "submodules/<child>/ pointer (`git add submodules/<child>`).",
                ".intent-cli/issues/<execution-unit>/publish.yaml (publish lifecycle metadata).",
                "Workflow labels via `intent-cli automation pr-transition` / `intent-cli automation issue-publish`."
            },
            MustNotWrite = new[]
            {
                "Packet content (`packet.yaml` / `implementation.md` / `review-context.md`) — that is design-owned; runtime-created packets must be authored explicitly via a packet-draft command with a runtime ownership label, not by hand-editing the design surface.",
                "Local `intents/rules/**` (those are read-only canonical rules).",
                "Child worker branch contents (the implementer owns the child branch)."
            }
        },
        new()
        {
            Id = RoleChildWorker,
            Summary = "Per-issue child implementation worktree (issue-to-PR or PR-comment-fix).",
            Responsibilities = new[]
            {
                "Implement the GitHub issue contract for a single issue or PR comment scope.",
                "Open / push a child PR with `Closes #<issue>` (G311) targeting `main` (G294 base policy).",
                "Run targeted tests; classify outcome; surface stale-cli / missing-command-surface abort gates.",
                "Mark issue-side and PR-side workflow labels exclusively via `intent-cli worker claim` / `worker complete`."
            },
            MayWrite = new[]
            {
                "Source / test files inside the child worktree (the implementation repo's working tree).",
                "Git branch + remote refs for the child PR.",
                "GitHub PR body / comments via `gh pr` (read-only label edits are forbidden — see MustNotWrite)."
            },
            MustNotWrite = new[]
            {
                "`.intent-cli/queue-state.json` / `.intent-cli/runs.jsonl` / `.intent-cli/issues/**` in the child cwd — parent durable state is host-owned (G300). The implementation repo MUST NOT contain a `.intent-cli/` directory, and the child loop MUST NOT read parent host queue-state.",
                "`intent-target` (host-owned) or `intent-pr-created` (issue-side completion) labels via raw `gh` calls — both are routed through `worker complete`.",
                "Submodule pointer updates — runtime owns the merge-sha sync."
            }
        },
        new()
        {
            Id = RoleAmbiguous,
            Summary = "Role cannot be deterministically inferred from the cwd / bindings — surface the gap to the operator.",
            Responsibilities = new[]
            {
                "Refuse to write parent durable state until the operator (or `--role` flag) names the role.",
                "Run read-only diagnostics (`automation doctor`, `intent status`, `guide automation setup`) without mutation.",
                "Surface an explicit ask so the operator can disambiguate."
            },
            MayWrite = Array.Empty<string>(),
            MustNotWrite = new[]
            {
                "Anything that requires a deterministic role binding (closeout, publish, packet draft, label transitions)."
            }
        }
    };

    /// <summary>
    /// G326: deterministic role resolver. Returns the canonical role for
    /// the supplied <paramref name="raw"/> identifier (case-insensitive,
    /// hyphen/underscore-tolerant) or <see cref="RoleAmbiguous"/> when
    /// the value does not match any known role.
    /// </summary>
    public static string ResolveRole(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return RoleAmbiguous;
        }

        var normalized = raw.Trim().ToLowerInvariant().Replace('_', '-');
        return normalized switch
        {
            "design" or "design-host" or "intent-design" => RoleDesign,
            "review-runtime" or "review" or "runtime" or "review-host" => RoleReviewRuntime,
            "child-worker" or "child" or "worker" or "implementer" => RoleChildWorker,
            _ => RoleAmbiguous
        };
    }
}

/// <summary>
/// G326: one row of the host ownership matrix. <c>Id</c> is the stable
/// machine-readable role identifier; <c>Summary</c>, <c>Responsibilities</c>,
/// <c>MayWrite</c>, and <c>MustNotWrite</c> form the operator-facing
/// contract that downstream tooling renders to JSON or markdown.
/// </summary>
internal sealed record HostOwnershipRole
{
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonPropertyName("summary")]
    public required string Summary { get; init; }

    [JsonPropertyName("responsibilities")]
    public required IReadOnlyList<string> Responsibilities { get; init; }

    [JsonPropertyName("may_write")]
    public required IReadOnlyList<string> MayWrite { get; init; }

    [JsonPropertyName("must_not_write")]
    public required IReadOnlyList<string> MustNotWrite { get; init; }
}
