using System.Text.Json;
using System.Text.Json.Serialization;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G487: read-only guide surface for an OPTIONAL agmsg-backed orchestrator
/// thread (ADR-012 / spec-26). Renders paste-ready prompts for an orchestrator
/// thread plus the implementation/review threads it delegates to, and pins the
/// operating contract: agmsg is a message/progress/completion signal layer
/// ONLY; <c>intent-cli</c> and GitHub remain authoritative for domain status,
/// queue-state, issue/PR facts, labels, CI, and closeout. The existing
/// timer-loop mode stays valid; orchestrator-message mode is opt-in and MUST
/// NOT also launch implement/review recurring timer loops for the same
/// domain/repo (no mixed-mode timer races). Host-state-free; never launches an
/// AI provider; never sends agmsg messages itself.
/// </summary>
internal static class GuideOrchestratorThreadCommand
{
    private const string FormatJson = "json";
    private const string FormatMarkdown = "markdown";

    private const string UsageLine =
        "Usage: intent-cli guide orchestrator-thread [--domain <name>] [--target-repo <owner/repo>] [--agent <agent>] [--format markdown|json]";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static int Execute(CliContext context, string[] args, TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(writer);

        if (args.Length == 1 && string.Equals(args[0], "--help", StringComparison.Ordinal))
        {
            WriteHelp(writer);
            return 0;
        }

        if (!TryParseArguments(args, out var format, out var values, out var error))
        {
            writer.WriteLine(error);
            writer.WriteLine(UsageLine);
            return 1;
        }

        var guide = BuildGuide(values);

        if (string.Equals(format, FormatJson, StringComparison.Ordinal))
        {
            writer.Write(JsonSerializer.Serialize(guide, JsonOptions));
            writer.WriteLine();
            return 0;
        }

        WriteMarkdown(writer, guide);
        return 0;
    }

    private static OrchestratorThreadGuide BuildGuide(IReadOnlyDictionary<string, string> values)
    {
        var domain = values["<domain>"];
        var repo = values["<owner/repo>"];
        var agent = values["<agent>"];

        string Apply(string template) => template
            .Replace("<domain>", domain, StringComparison.Ordinal)
            .Replace("<owner/repo>", repo, StringComparison.Ordinal)
            .Replace("<agent>", agent, StringComparison.Ordinal);

        return new OrchestratorThreadGuide
        {
            Summary =
                "Optional agmsg-backed orchestrator thread (ADR-012 / spec-26). agmsg carries natural-language "
                + "delegation / progress / completion / blocker signals between threads; it is NOT workflow state. "
                + "intent-cli and GitHub remain authoritative for domain status, queue-state, issue/PR facts, labels, "
                + "CI, and closeout.",
            ModeSeparation = new OrchestratorModeSeparation
            {
                TimerLoopMode =
                    "Existing mode and still fully supported: implementation and review threads run on recurring "
                    + "timers and use intent-cli `worker next-action` / host review-next-slice as their source of truth. "
                    + "Use `intent-cli guide prompt-matrix` / `guide prompt-template` to set these up. No orchestrator "
                    + "thread is required.",
                OrchestratorMessageMode =
                    "Opt-in mode: a fourth orchestrator thread delegates to implementation/review threads over agmsg "
                    + "instead of relying on independent timers. Choose ONE mode per domain/repo.",
                MixedModeWarning =
                    "Do NOT run both modes for the same domain/repo. In orchestrator-message mode, do NOT launch the "
                    + "implementation/review recurring timer loops for that domain/repo — two drivers (a timer AND the "
                    + "orchestrator) would race on the same GitHub state. The orchestrator paces those threads; they do "
                    + "not also self-schedule.",
            },
            Threads = new[]
            {
                new OrchestratorThreadPrompt
                {
                    Role = "orchestrator",
                    Purpose =
                        "Coordinate implementation/review threads for domain `" + domain + "` via agmsg; never mutate "
                        + "workflow state directly.",
                    Prompt = Apply(
                        "You are the ORCHESTRATOR thread for domain `<domain>` against `<owner/repo>` using `<agent>`. "
                        + "You coordinate the implementation and review threads over agmsg; you do NOT implement code, "
                        + "perform semantic review, or mutate GitHub/intent-cli workflow state yourself. agmsg is a "
                        + "signal layer only — intent-cli and GitHub are authoritative. Per wake: read pending agmsg "
                        + "replies, ask intent-cli for the real state (`intent-cli intent status --domain <domain> "
                        + "--format json`, `intent-cli worker next-action --repo <owner/repo> --github-only --format "
                        + "json`, `intent-cli automation host-review-preflight --repo <owner/repo> --format json`), "
                        + "verify the GitHub facts that an agmsg reply claims (merged PR, CI, labels), then send AT MOST "
                        + "ONE message: a delegation (assign the next slice/PR), a repair request (point a stalled "
                        + "thread back to the official intent-cli workflow), or an escalation to the operator. Do NOT "
                        + "launch recurring implement/review timers for this domain/repo while orchestrating. Fail "
                        + "closed: if you detect a second orchestrator for this domain/repo, or agmsg replies conflict "
                        + "with GitHub/intent-cli facts, STOP and escalate rather than guessing."),
                },
                new OrchestratorThreadPrompt
                {
                    Role = "implementation",
                    Purpose =
                        "Implement exactly one delegated item, then report a structured agmsg reply.",
                    Prompt = Apply(
                        "You are the IMPLEMENTATION thread for domain `<domain>` against `<owner/repo>` using `<agent>`, "
                        + "driven by orchestrator agmsg delegations (NOT a recurring timer). When delegated an item, run "
                        + "the normal child implementation workflow: the issue/PR number comes from `intent-cli worker "
                        + "next-action --repo <owner/repo> --github-only`, NOT from the agmsg text; claim, implement, "
                        + "open the PR with a `Closes #<issue>` reference, and `worker complete` — all label transitions "
                        + "through intent-cli worker/automation only. intent-cli and GitHub remain authoritative; agmsg "
                        + "is only how you receive the delegation and send back your reply. When done or blocked, send "
                        + "ONE structured agmsg reply (accepted / progress / completed / blocked) citing the GitHub "
                        + "facts (PR number, CI). Do NOT read host metadata (`.intent-cli/**`, `intents/**`)."),
                },
                new OrchestratorThreadPrompt
                {
                    Role = "review",
                    Purpose =
                        "Review/closeout exactly one delegated PR through intent-cli, then report a structured agmsg reply.",
                    Prompt = Apply(
                        "You are the REVIEW thread for domain `<domain>` against `<owner/repo>` using `<agent>`, driven "
                        + "by orchestrator agmsg delegations (NOT a recurring timer). When delegated a PR, run the "
                        + "official host review/closeout through intent-cli surfaces (`review closeout-plan`, `guide "
                        + "review`, `automation pr-transition`, `closeout pr`) — agmsg never replaces semantic review or "
                        + "authorizes a merge. Perform semantic review only when you are the packet `review_role` or "
                        + "explicitly assigned (G480); otherwise orchestrate the merge/closeout of an already-approved "
                        + "PR. Report ONE structured agmsg reply (accepted / progress / completed / blocked) citing the "
                        + "intent-cli/GitHub facts. intent-cli and GitHub stay authoritative."),
                },
            },
            AgmsgReplyContract = new OrchestratorReplyContract
            {
                Description =
                    "Implementation/review threads reply to a delegation with exactly one structured agmsg message. "
                    + "The reply is a SIGNAL; the orchestrator re-verifies every claim against intent-cli / GitHub "
                    + "before acting on it.",
                Accepted = "{\"status\":\"accepted\",\"thread\":\"implementation\",\"ref\":\"issue#<n>\",\"note\":\"claimed; starting\"}",
                Progress = "{\"status\":\"progress\",\"thread\":\"implementation\",\"ref\":\"issue#<n>\",\"note\":\"branch pushed; CI running\"}",
                Completed = "{\"status\":\"completed\",\"thread\":\"implementation\",\"ref\":\"pr#<n>\",\"note\":\"PR opened, Closes #<n>, CI green\"}",
                Blocked = "{\"status\":\"blocked\",\"thread\":\"review\",\"ref\":\"pr#<n>\",\"classification\":\"clarification-required\",\"note\":\"one operator action: <text>\"}",
            },
            OrchestratorFirstWake = new[]
            {
                "Confirm you are the ONLY orchestrator for this domain/repo; if a second is detected, STOP and escalate (fail closed).",
                "Read pending agmsg replies from the implementation/review threads (signals only — do not trust them as state).",
                Apply("Ask intent-cli for the real state: `intent-cli intent status --domain <domain> --format json` and `intent-cli worker next-action --repo <owner/repo> --github-only --format json`."),
                "Verify every GitHub fact an agmsg reply claims (PR merged, CI concluded, labels) before acting on it.",
                "Send AT MOST ONE message this wake: one delegation, one repair request, or one operator escalation — never a batch.",
                "Do not launch implement/review recurring timers for this domain/repo while orchestrating.",
            },
            SafetyBoundaries = new[]
            {
                "agmsg is a message/progress/completion signal layer only; intent-cli and GitHub are authoritative for all workflow state.",
                "No raw label mutation (`gh ... --add-label`/`--remove-label`); every label transition goes through intent-cli worker/automation.",
                "No hand-editing queue-state, runs.jsonl, packets, or any host metadata (`.intent-cli/**`, `intents/**`).",
                "agmsg never replaces semantic review or authorizes a merge; review/closeout decisions run through intent-cli review surfaces (G480).",
                "Process at most one delegation/repair/escalation per orchestrator wake; one delegated item per implementation/review wake.",
                "Fail closed on duplicate orchestrators for the same domain/repo, or when an agmsg reply conflicts with intent-cli/GitHub facts — STOP and escalate, never guess.",
                "Never ask intent-cli to launch Claude/Codex/Copilot or any AI provider; intent-cli only emits text the human agent acts on.",
            },
            DetailedGuideCommands = new[]
            {
                Apply("intent-cli guide prompt-matrix --mode child-loop --target-repo <owner/repo> --agent <agent> --format markdown"),
                Apply("intent-cli guide prompt-matrix --mode host-loop --domain <domain> --target-repo <owner/repo> --agent <agent> --format markdown"),
                Apply("intent-cli automation summary --domain <domain> --format json"),
            },
        };
    }

    private static bool TryParseArguments(
        string[] args,
        out string format,
        out IReadOnlyDictionary<string, string> values,
        out string error)
    {
        format = FormatMarkdown;
        error = string.Empty;

        var parsed = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["<domain>"] = "<domain>",
            ["<owner/repo>"] = "<owner/repo>",
            ["<agent>"] = "<agent>",
        };

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (!RequiresValue(arg))
            {
                values = parsed;
                error = $"Unknown argument '{arg}'.";
                return false;
            }

            if (i + 1 >= args.Length)
            {
                values = parsed;
                error = $"{arg} requires a value.";
                return false;
            }

            var value = args[++i];
            switch (arg)
            {
                case "--format":
                    format = value;
                    break;
                case "--domain":
                    parsed["<domain>"] = value;
                    break;
                case "--target-repo":
                    parsed["<owner/repo>"] = value;
                    break;
                case "--agent":
                    parsed["<agent>"] = value;
                    break;
            }
        }

        if (!string.Equals(format, FormatMarkdown, StringComparison.Ordinal)
            && !string.Equals(format, FormatJson, StringComparison.Ordinal))
        {
            values = parsed;
            error = $"Unknown --format '{format}'. Supported: markdown, json.";
            return false;
        }

        values = parsed;
        return true;
    }

    private static bool RequiresValue(string arg) =>
        string.Equals(arg, "--format", StringComparison.Ordinal)
        || string.Equals(arg, "--domain", StringComparison.Ordinal)
        || string.Equals(arg, "--target-repo", StringComparison.Ordinal)
        || string.Equals(arg, "--agent", StringComparison.Ordinal);

    private static void WriteMarkdown(TextWriter writer, OrchestratorThreadGuide guide)
    {
        writer.WriteLine("# Guide — agmsg-backed orchestrator thread (G487)");
        writer.WriteLine();
        writer.WriteLine(guide.Summary);
        writer.WriteLine();

        writer.WriteLine("## Mode separation");
        writer.WriteLine();
        writer.WriteLine($"- **timer-loop mode** — {guide.ModeSeparation.TimerLoopMode}");
        writer.WriteLine($"- **orchestrator-message mode** — {guide.ModeSeparation.OrchestratorMessageMode}");
        writer.WriteLine($"- **mixed-mode warning** — {guide.ModeSeparation.MixedModeWarning}");
        writer.WriteLine();

        writer.WriteLine("## Thread prompts");
        foreach (var thread in guide.Threads)
        {
            writer.WriteLine();
            writer.WriteLine($"### {thread.Role}");
            writer.WriteLine();
            writer.WriteLine($"- purpose: {thread.Purpose}");
            writer.WriteLine();
            writer.WriteLine("```text");
            writer.WriteLine(thread.Prompt);
            writer.WriteLine("```");
        }
        writer.WriteLine();

        writer.WriteLine("## agmsg reply contract");
        writer.WriteLine();
        writer.WriteLine(guide.AgmsgReplyContract.Description);
        writer.WriteLine();
        writer.WriteLine("```json");
        writer.WriteLine(guide.AgmsgReplyContract.Accepted);
        writer.WriteLine(guide.AgmsgReplyContract.Progress);
        writer.WriteLine(guide.AgmsgReplyContract.Completed);
        writer.WriteLine(guide.AgmsgReplyContract.Blocked);
        writer.WriteLine("```");
        writer.WriteLine();

        writer.WriteLine("## Orchestrator first wake");
        writer.WriteLine();
        foreach (var step in guide.OrchestratorFirstWake)
        {
            writer.WriteLine($"1. {step}");
        }
        writer.WriteLine();

        writer.WriteLine("## Safety boundaries");
        writer.WriteLine();
        foreach (var boundary in guide.SafetyBoundaries)
        {
            writer.WriteLine($"- {boundary}");
        }
        writer.WriteLine();

        writer.WriteLine("## Detailed guide commands");
        writer.WriteLine();
        foreach (var command in guide.DetailedGuideCommands)
        {
            writer.WriteLine($"- `{command}`");
        }
    }

    private static void WriteHelp(TextWriter writer)
    {
        writer.WriteLine("guide orchestrator-thread");
        writer.WriteLine(UsageLine);
        writer.WriteLine();
        writer.WriteLine("Renders paste-ready prompts for an OPTIONAL agmsg-backed orchestrator thread plus the");
        writer.WriteLine("implementation/review threads it delegates to. agmsg is a signal layer only; intent-cli and");
        writer.WriteLine("GitHub remain authoritative. Existing timer-loop mode stays valid and is not replaced.");
    }
}

internal sealed record OrchestratorThreadGuide
{
    [JsonPropertyName("summary")]
    public required string Summary { get; init; }

    [JsonPropertyName("mode_separation")]
    public required OrchestratorModeSeparation ModeSeparation { get; init; }

    [JsonPropertyName("threads")]
    public required IReadOnlyList<OrchestratorThreadPrompt> Threads { get; init; }

    [JsonPropertyName("agmsg_reply_contract")]
    public required OrchestratorReplyContract AgmsgReplyContract { get; init; }

    [JsonPropertyName("orchestrator_first_wake")]
    public required IReadOnlyList<string> OrchestratorFirstWake { get; init; }

    [JsonPropertyName("safety_boundaries")]
    public required IReadOnlyList<string> SafetyBoundaries { get; init; }

    [JsonPropertyName("detailed_guide_commands")]
    public required IReadOnlyList<string> DetailedGuideCommands { get; init; }
}

internal sealed record OrchestratorModeSeparation
{
    [JsonPropertyName("timer_loop_mode")]
    public required string TimerLoopMode { get; init; }

    [JsonPropertyName("orchestrator_message_mode")]
    public required string OrchestratorMessageMode { get; init; }

    [JsonPropertyName("mixed_mode_warning")]
    public required string MixedModeWarning { get; init; }
}

internal sealed record OrchestratorThreadPrompt
{
    [JsonPropertyName("role")]
    public required string Role { get; init; }

    [JsonPropertyName("purpose")]
    public required string Purpose { get; init; }

    [JsonPropertyName("prompt")]
    public required string Prompt { get; init; }
}

internal sealed record OrchestratorReplyContract
{
    [JsonPropertyName("description")]
    public required string Description { get; init; }

    [JsonPropertyName("accepted")]
    public required string Accepted { get; init; }

    [JsonPropertyName("progress")]
    public required string Progress { get; init; }

    [JsonPropertyName("completed")]
    public required string Completed { get; init; }

    [JsonPropertyName("blocked")]
    public required string Blocked { get; init; }
}
