using System.Text.Json;
using System.Text.Json.Serialization;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G335: read-only <c>intent-cli guide workflow task init-host</c>.
/// Explains the three host/child roles a new project needs to choose
/// between (design / review-runtime / child-implementation), names
/// where <c>.intent-cli/</c> is required, optional, or forbidden, and
/// emits a deterministic scaffold plan (AGENTS.md / CLAUDE.md /
/// host-binding.toml suggestions plus the canonical
/// <c>intent-cli intent init</c> incantation). It also surfaces the
/// G300 / G333 invariant that initializing <c>.intent-cli/</c> inside
/// a child implementation repo is forbidden unless the operator is
/// deliberately upgrading that repo to a host.
///
/// Pure data — never reads parent host queue-state, never calls
/// <c>gh</c>, never mutates state, never launches an AI provider. Safe
/// from a child cwd that has no <c>.intent-cli/</c> directory
/// (G299 / G333 bootstrap allow-list).
/// </summary>
internal static class GuideWorkflowTaskInitHostCommand
{
    private const string FormatJson = "json";
    private const string FormatMarkdown = "markdown";

    private const string UsageLine =
        "Usage: intent-cli guide workflow task init-host [--role design|review-runtime|child-implementation] [--force-host] [--format markdown|json]";

    /// <summary>
    /// G335: canonical role definitions for a new project. Mirrors the
    /// runtime <see cref="HostOwnershipModel"/> roles but adds the
    /// "what should I do FIRST" scaffold/init recipe each role needs
    /// before its loop is wired up. Tests assert each role advertises
    /// its <c>.intent-cli/</c> placement, its scaffold files, and at
    /// least one canonical <c>intent-cli</c> command an external agent
    /// can run.
    /// </summary>
    internal static readonly IReadOnlyList<HostRoleScaffold> RoleScaffolds = new[]
    {
        new HostRoleScaffold
        {
            Role = HostOwnershipModel.RoleDesign,
            Summary = "Intent design / packet backlog workspace (e.g. MyIntentHost). Owns intents/<domain>/** and the packet/clarification scaffolds.",
            DotIntentCliPlacement = "REQUIRED — the design host repo carries `.intent-cli/` at its root. Queue-state, runs.jsonl, packets, and clarifications all live here.",
            ScaffoldFiles = new[]
            {
                "AGENTS.md — author the project-scoped agent guide; reference `intent-cli guide model --format json` and `intent-cli guide collaborate --format json` so the AI agent reads them via the CLI, not via copied prompts.",
                "CLAUDE.md (optional) — operator-facing project guide for human + Claude collaboration.",
                "host-binding.toml — declare this repo's role explicitly (`role = \"design\"`) so cross-repo tools can distinguish design vs review-runtime cwds without inferring from path."
            },
            InitCommands = new[]
            {
                "intent-cli intent init --domain <domain-name> [--target-repo <owner/repo>] --write",
                "# Confirm `.intent-cli/` is provisioned and queue-state.json is empty:",
                "intent-cli intent status --domain <domain-name> --format json",
                "# Read the canonical capability JSON before any mutation:",
                "intent-cli automation summary --domain <domain-name> --format json"
            },
            FirstLoop = "Run `intent-cli guide intent-work --format json` for the issue-publish / next-slice workflow."
        },
        new HostRoleScaffold
        {
            Role = HostOwnershipModel.RoleReviewRuntime,
            Summary = "Review / closeout / runtime workspace (e.g. IntentSystemReview, SekibanAsAServiceReview). Pulls --ff-only, runs G316 review, owns `closeout pr`.",
            DotIntentCliPlacement = "REQUIRED — the review-runtime workspace carries its own `.intent-cli/`. It is a separate local checkout of the same remote as the design host; it does NOT share the design host's `.intent-cli/` by path, but writes through the same intent-cli surfaces.",
            ScaffoldFiles = new[]
            {
                "AGENTS.md — declare this is the review-runtime role and that approval / closeout commands run here, never from the design host or a child repo.",
                "host-binding.toml — declare `role = \"review-runtime\"`; cross-repo tools refuse design-only mutations from this cwd.",
                "(no CLAUDE.md required) — review-runtime is typically driven by intent-cli + gh from a controller; CLAUDE.md is operator-facing and lives in the design host."
            },
            InitCommands = new[]
            {
                "intent-cli intent init --domain <domain-name> [--target-repo <owner/repo>] --write",
                "# Verify the host preflight surfaces are all installed:",
                "intent-cli automation doctor --format json",
                "intent-cli automation host-review-preflight --repo <owner/repo> --format json"
            },
            FirstLoop = "Run `intent-cli guide review --pr <n> --repo <owner/repo> --domain <d> --format json` for the G316 packet-aware review surface; closeout via `intent-cli closeout pr --pr <n> --repo <r> --pr-merged true --write --format json`."
        },
        new HostRoleScaffold
        {
            Role = HostOwnershipModel.RoleChildWorker,
            Summary = "Per-issue child implementation worktree (issue-to-PR or PR-comment-fix). GitHub-contract-only.",
            DotIntentCliPlacement = "FORBIDDEN — the implementation repo MUST NOT contain its own `.intent-cli/` directory (G300 / G333). Absence is the expected steady state. `intent-cli` runs against `--repo <owner/repo>` and uses GitHub labels only; parent host queue-state stays host-owned. Initializing `.intent-cli/` here is allowed ONLY if the operator is deliberately upgrading this checkout to a host role (pass --force-host to acknowledge).",
            ScaffoldFiles = new[]
            {
                "AGENTS.md — declare child-implementation role; point at `intent-cli guide worker --format json` so loops read guidance from the CLI, not from local rules.",
                "CLAUDE.md (optional) — operator-facing child loop notes; never duplicates host rules.",
                "host-binding.toml — declare `role = \"child-implementation\"`; cross-repo tools refuse host writes from this cwd."
            },
            InitCommands = new[]
            {
                "# Do NOT run `intent-cli intent init` here. The child repo is GitHub-contract-only.",
                "# Verify the global intent-cli is installed and fresh:",
                "intent-cli automation doctor --format json",
                "# Discover the loop surface:",
                "intent-cli guide prompt-matrix --mode child-loop --format json"
            },
            FirstLoop = "Run the canonical wake body: `intent-cli worker next-action --repo <owner/repo> --workdir $PWD --format json`, dispatch on `action`, then `worker claim` / `worker result-summary` / `worker complete`."
        }
    };

    /// <summary>
    /// G335: explicit invariants the workflow guide must surface so
    /// external agents do not mis-initialize a project. Tests pin
    /// each invariant verbatim.
    /// </summary>
    internal static readonly IReadOnlyList<string> Invariants = new[]
    {
        "A child implementation repo MUST NOT contain its own `.intent-cli/` directory (G300 / G333). Initialize `.intent-cli/` only in design or review-runtime workspaces; never in a child cwd unless the operator is explicitly upgrading that cwd to a host role.",
        "If the cwd already contains `.intent-cli/` AND the operator selected --role child-implementation, this guide refuses to scaffold and surfaces a structured stop — re-run with --force-host to override (you are deliberately turning this repo into a host).",
        "Parent host queue-state, runs.jsonl, packet directories, intent tree, and review-runtime state are host-owned. Child implementation loops read GitHub issues / PRs / comments / labels / implementation-repo files only and MUST NOT inspect or mutate parent host state.",
        "Prefer intent-cli-backed metadata mutation over hand-editing. Ask `intent-cli guide commands list --format json` or `intent-cli automation summary --domain <d> --format json` which command performs the transition, run that command, then validate the result. Routine automation MUST NOT directly edit queue-state, runs logs, publish artifacts, workflow labels, or runtime metadata by hand when a supported intent-cli command exists.",
        "Never launch AI providers (Claude / Codex / any LLM) from intent-cli; the chat-first model has the human agent driving the conversation."
    };

    public static int Execute(CliContext context, string[] args, TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(writer);

        if (!TryParseArguments(args, out var focusRole, out var forceHost, out var format, out var error))
        {
            writer.WriteLine(error);
            writer.WriteLine(UsageLine);
            return 1;
        }

        var visibleRoles = string.IsNullOrEmpty(focusRole)
            ? RoleScaffolds
            : RoleScaffolds.Where(r => string.Equals(r.Role, focusRole, StringComparison.Ordinal)).ToArray();

        var refusalReasons = new List<string>();
        if (!string.IsNullOrEmpty(focusRole) && visibleRoles.Count == 0)
        {
            refusalReasons.Add($"--role '{focusRole}' did not resolve to a known role (design / review-runtime / child-implementation).");
        }

        // G335: surface the child-cwd guard explicitly. The guide
        // detects whether the bootstrap context's repo root carries a
        // local `.intent-cli/` and refuses to scaffold a
        // child-implementation role when one already exists — unless
        // --force-host is passed to acknowledge the upgrade.
        var hasDotIntentCli = ChildCwdHasDotIntentCli(context);
        var childRoleSelected = string.Equals(focusRole, HostOwnershipModel.RoleChildWorker, StringComparison.Ordinal);
        if (hasDotIntentCli && childRoleSelected && !forceHost)
        {
            refusalReasons.Add("The current cwd already contains `.intent-cli/`, but --role child-implementation was selected. Re-run with --force-host to explicitly upgrade this repo to a host role, or cd to a directory without `.intent-cli/` if you intended to scaffold a child-implementation checkout.");
        }

        if (string.Equals(format, FormatJson, StringComparison.Ordinal))
        {
            var payload = new InitHostGuidance
            {
                Usage = UsageLine,
                Roles = visibleRoles,
                Invariants = Invariants,
                FocusRole = string.IsNullOrEmpty(focusRole) ? null : focusRole,
                CwdHasDotIntentCli = hasDotIntentCli,
                ForceHost = forceHost,
                Refusals = refusalReasons.Count == 0 ? null : refusalReasons
            };
            writer.Write(JsonSerializer.Serialize(payload, JsonOptions));
            writer.WriteLine();
        }
        else
        {
            WriteMarkdown(visibleRoles, hasDotIntentCli, forceHost, refusalReasons, writer);
        }

        return refusalReasons.Count == 0 ? 0 : 1;
    }

    private static bool ChildCwdHasDotIntentCli(CliContext context)
    {
        // G335: only inspect the bootstrap context's repo root. Never
        // climb the filesystem or read state from elsewhere.
        var root = context.RepoRoot;
        if (string.IsNullOrEmpty(root))
        {
            return false;
        }

        try
        {
            return Directory.Exists(Path.Combine(root, ".intent-cli"));
        }
        catch (Exception ex) when (ex is ArgumentException or PathTooLongException)
        {
            return false;
        }
    }

    private static void WriteMarkdown(
        IReadOnlyList<HostRoleScaffold> roles,
        bool hasDotIntentCli,
        bool forceHost,
        IReadOnlyList<string> refusals,
        TextWriter writer)
    {
        writer.WriteLine("# intent-cli — init-host workflow guide");
        writer.WriteLine();
        writer.WriteLine(UsageLine);
        writer.WriteLine();
        writer.WriteLine("Pick the role this new repo will play. A single remote can have multiple local workspaces — typically one design host, one or more review-runtime workspaces, and one per-issue child worktree at a time.");
        writer.WriteLine();
        writer.WriteLine($"_Detected `.intent-cli/` in cwd: **{(hasDotIntentCli ? "yes" : "no")}**; --force-host: **{(forceHost ? "yes" : "no")}**._");
        writer.WriteLine();

        if (refusals.Count > 0)
        {
            writer.WriteLine("## Refusals");
            foreach (var reason in refusals)
            {
                writer.WriteLine($"- {reason}");
            }
            writer.WriteLine();
        }

        foreach (var role in roles)
        {
            writer.WriteLine($"## Role: {role.Role}");
            writer.WriteLine();
            writer.WriteLine($"- Summary: {role.Summary}");
            writer.WriteLine($"- `.intent-cli/` placement: {role.DotIntentCliPlacement}");
            writer.WriteLine();
            writer.WriteLine("### Scaffold files");
            foreach (var file in role.ScaffoldFiles)
            {
                writer.WriteLine($"- {file}");
            }
            writer.WriteLine();
            writer.WriteLine("### Init commands");
            foreach (var cmd in role.InitCommands)
            {
                writer.WriteLine($"- `{cmd}`");
            }
            writer.WriteLine();
            writer.WriteLine($"### First loop");
            writer.WriteLine($"- {role.FirstLoop}");
            writer.WriteLine();
        }

        writer.WriteLine("## Invariants");
        foreach (var line in Invariants)
        {
            writer.WriteLine($"- {line}");
        }
    }

    private static bool TryParseArguments(
        string[] args,
        out string focusRole,
        out bool forceHost,
        out string format,
        out string error)
    {
        focusRole = string.Empty;
        forceHost = false;
        format = FormatMarkdown;
        error = string.Empty;

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            switch (arg)
            {
                case "--help":
                    // Treat as harmless; the calling dispatcher already
                    // handled the standalone help case.
                    break;
                case "--role":
                    if (i + 1 >= args.Length || string.IsNullOrWhiteSpace(args[i + 1]))
                    {
                        error = "--role requires a value (design, review-runtime, or child-implementation).";
                        return false;
                    }
                    var requestedRole = args[i + 1];
                    var normalizedRole = NormalizeRoleArgument(requestedRole);
                    if (normalizedRole is null)
                    {
                        error = $"--role '{requestedRole}' did not resolve to a known role (design / review-runtime / child-implementation).";
                        return false;
                    }
                    focusRole = normalizedRole;
                    i++;
                    break;
                case "--force-host":
                    forceHost = true;
                    break;
                case "--format":
                    if (i + 1 >= args.Length || string.IsNullOrWhiteSpace(args[i + 1]))
                    {
                        error = "--format requires a value (markdown or json).";
                        return false;
                    }
                    var requestedFormat = args[i + 1];
                    if (!string.Equals(requestedFormat, FormatMarkdown, StringComparison.Ordinal)
                        && !string.Equals(requestedFormat, FormatJson, StringComparison.Ordinal))
                    {
                        error = $"--format must be 'markdown' or 'json' (got '{requestedFormat}').";
                        return false;
                    }
                    format = requestedFormat;
                    i++;
                    break;
                default:
                    error = $"Unknown argument '{arg}'.";
                    return false;
            }
        }

        return true;
    }

    private static string? NormalizeRoleArgument(string raw)
    {
        // Accept both the canonical role IDs and the "child-implementation"
        // alias the issue advertises. The runtime ownership model uses
        // "child-worker" internally, but external operators learn the
        // role from the init-host guide first, so accept both spellings.
        if (string.Equals(raw, HostOwnershipModel.RoleDesign, StringComparison.OrdinalIgnoreCase))
        {
            return HostOwnershipModel.RoleDesign;
        }
        if (string.Equals(raw, HostOwnershipModel.RoleReviewRuntime, StringComparison.OrdinalIgnoreCase))
        {
            return HostOwnershipModel.RoleReviewRuntime;
        }
        if (string.Equals(raw, HostOwnershipModel.RoleChildWorker, StringComparison.OrdinalIgnoreCase)
            || string.Equals(raw, "child-implementation", StringComparison.OrdinalIgnoreCase))
        {
            return HostOwnershipModel.RoleChildWorker;
        }
        return null;
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
}

/// <summary>
/// G335: one role's init-host scaffold entry. <c>role</c> matches the
/// canonical <see cref="HostOwnershipModel"/> identifier; the
/// child-worker role accepts both <c>child-worker</c> and the
/// <c>child-implementation</c> alias on input.
/// </summary>
internal sealed record HostRoleScaffold
{
    [JsonPropertyName("role")]
    public required string Role { get; init; }

    [JsonPropertyName("summary")]
    public required string Summary { get; init; }

    [JsonPropertyName("dot_intent_cli_placement")]
    public required string DotIntentCliPlacement { get; init; }

    [JsonPropertyName("scaffold_files")]
    public required IReadOnlyList<string> ScaffoldFiles { get; init; }

    [JsonPropertyName("init_commands")]
    public required IReadOnlyList<string> InitCommands { get; init; }

    [JsonPropertyName("first_loop")]
    public required string FirstLoop { get; init; }
}

/// <summary>
/// G335: full payload for <c>guide workflow task init-host --format json</c>.
/// </summary>
internal sealed record InitHostGuidance
{
    [JsonPropertyName("usage")]
    public required string Usage { get; init; }

    [JsonPropertyName("focus_role")]
    public string? FocusRole { get; init; }

    [JsonPropertyName("cwd_has_dot_intent_cli")]
    public required bool CwdHasDotIntentCli { get; init; }

    [JsonPropertyName("force_host")]
    public required bool ForceHost { get; init; }

    [JsonPropertyName("roles")]
    public required IReadOnlyList<HostRoleScaffold> Roles { get; init; }

    [JsonPropertyName("invariants")]
    public required IReadOnlyList<string> Invariants { get; init; }

    [JsonPropertyName("refusals")]
    public IReadOnlyList<string>? Refusals { get; init; }
}
