using System.Text.Json;
using System.Text.Json.Serialization;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G833: the solo conductor model's per-unit operating contract. One
/// conductor seat carries architect, orchestrator, and builder; a fresh
/// independent reviewer subagent carries reviewer for every review. The route
/// is render-only and metadata-free: it renders command text with placeholders
/// and never starts, launches, or manages an agent or subagent.
/// </summary>
internal static class GuideSoloConductorCommand
{
    internal const string CommandName = "intent-cli guide solo-conductor";
    internal const string ContractVersion = "g833-solo-conductor/v1";
    internal const string ToolIntentCli = "intent-cli";
    internal const string ToolGh = "gh";
    internal const string ToolGit = "git";
    private const string FormatJson = "json";
    private const string FormatMarkdown = "markdown";
    private const string RuntimePlaceholder = "<codex|claude|cursor|copilot|opencode>";
    private const string ModelRequiredPrefix = "The model is required. ";
    private const string UsageLine =
        "Usage: intent-cli guide solo-conductor [--format markdown|json]";

    private static readonly string[] BuilderContractItems =
    [
        "The conductor runs one builder at a time in the unit's isolated clone (step 6), outside every runtime's trusted folders. The copilot line uses a fresh, empty `COPILOT_HOME` and `XDG_CONFIG_HOME` per run (`mktemp -d`, removed after). Copilot itself runs `gh auth token` for credentials, so a signed-in real `gh` must be on PATH; item 3's no-`gh` rule covers the builder's own commands.",
        "The task prompt is a file the conductor writes and passes on stdin, except cursor, which takes it as an argument. Any OpenCode command shown without a prompt file carries `< /dev/null>`, because with stdin open `opencode run` waits silently (measured), including in the background.",
        "The builder may edit and commit inside the clone and never pushes, opens a PR or runs `gh`; the conductor pushes, opens the PR and owns claims and transitions.",
        "What bounds the builder differs by runtime, and no measured builder invocation sandboxes shell commands, so the conductor reviews the clone's diff before pushing.",
        "The OpenCode builder line uses fresh, empty `XDG_CONFIG_HOME` and `OPENCODE_CONFIG_DIR` directories per run. The clone's config and `AGENTS.md` are not loaded, so the task file carries every repository instruction. A provider defined only in the global config goes into the builder config's `provider` block after `$schema`.",
        "A local model provider serves one seat at a time; do not start one while another runs. A local-model review can outgrow the context window as conversation and tool output accumulate, even when the prompt fits, and the same prompt varies between runs; for tool-heavy reviews prefer a smaller packet or a larger window, and expect a retry. intent-cli checks none of this.",
        "intent-cli renders these lines and never launches a builder, reviewer or AI provider CLI; the review commands' only launch is the existing claim-read `git fetch`.",
    ];

    internal static int Execute(CliContext context, string[] args, TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(writer);

        if (args.Length == 1 && string.Equals(args[0], "--help", StringComparison.Ordinal))
        {
            WriteHelp(writer);
            return 0;
        }

        if (!TryParseFormat(args, out var format, out var error))
        {
            writer.WriteLine(error);
            writer.WriteLine(UsageLine);
            return 1;
        }

        var guide = BuildGuide();
        if (string.Equals(format, FormatJson, StringComparison.Ordinal))
        {
            writer.WriteLine(JsonSerializer.Serialize(guide, JsonOptions));
        }
        else
        {
            WriteMarkdown(writer, guide);
        }

        return 0;
    }

    private static SoloConductorCommand IntentCli(string command) => new() { Tool = ToolIntentCli, Command = command };

    private static SoloConductorCommand Gh(string command) => new() { Tool = ToolGh, Command = command };

    private static SoloConductorCommand Git(string command) => new() { Tool = ToolGit, Command = command };

    internal static SoloConductorBuilder BuildBuilder() => new()
    {
        Contract = BuilderContractItems,
        Invocations =
        [
            new SoloConductorBuilderInvocation
            {
                Runtime = CrossRuntimeReviewRuntimes.Codex,
                Command = "codex exec -s workspace-write -C <isolated-clone> [-m <model>] - < <task-file>",
                Enforcement = "not measured.",
                Measured = false,
            },
            new SoloConductorBuilderInvocation
            {
                Runtime = CrossRuntimeReviewRuntimes.Claude,
                Command = "cd <isolated-clone> && claude -p --permission-mode acceptEdits --allowedTools Bash --disallowedTools 'Bash(git push:*)' 'Bash(gh:*)' [--model <model>] < <task-file>",
                Enforcement = "a file-tool write outside the clone and `git push` refused; a shell write outside not refused.",
                Measured = true,
            },
            new SoloConductorBuilderInvocation
            {
                Runtime = CrossRuntimeReviewRuntimes.Cursor,
                Command = "cursor-agent -p --force --sandbox enabled --trust --workspace <isolated-clone> [--model <model>] --output-format json \"$(cat <task-file>)\"",
                Enforcement = "nothing enforced.",
                Measured = true,
            },
            new SoloConductorBuilderInvocation
            {
                Runtime = CrossRuntimeReviewRuntimes.Copilot,
                Command = "COPILOT_ALLOW_ALL= COPILOT_HOME=<fresh-empty-copilot-home> XDG_CONFIG_HOME=<fresh-empty-xdg-dir> copilot -C <isolated-clone> --model <model> [--reasoning-effort <effort>] --allow-all-tools --deny-tool 'shell(git push)' --deny-tool 'shell(gh)' --disable-builtin-mcps --no-ask-user --stream off --output-format json < <task-file>",
                Enforcement = ModelRequiredPrefix
                    + "An inside write succeeded; a file-tool write and a literal shell redirect outside refused by path verification (not a sandbox); `git push` refused; other outside forms unmeasured.",
                Measured = true,
            },
            new SoloConductorBuilderInvocation
            {
                Runtime = CrossRuntimeReviewRuntimes.Opencode,
                Command = "OPENCODE_PERMISSION= OPENCODE_CONFIG_CONTENT= XDG_CONFIG_HOME=<fresh-empty-dir> OPENCODE_CONFIG_DIR=<fresh-empty-dir-2> OPENCODE_DISABLE_PROJECT_CONFIG=1 OPENCODE_CONFIG=<builder-config-file> opencode run --pure --dir <isolated-clone> -m <provider/model> [--variant <effort>] --auto --format json < <task-file>",
                Enforcement = ModelRequiredPrefix
                    + "An inside write and a commit succeeded; `git push` refused, even through `task` and with a permissive clone and global config; no plugin or MCP server ran; outside writes unmeasured in this form.",
                Measured = true,
            },
        ],
        OpencodeConfig = CrossRuntimeReviewOpencodeConfig.BuilderConfigJson,
    };

    internal static SoloConductorGuide BuildGuide() => new()
    {
        Route = CommandName,
        ContractVersion = ContractVersion,
        ReadOnly = true,
        MetadataFree = true,
        Model = new SoloConductorModel
        {
            Summary = "The solo conductor model is one of the three supported thread shapes (see `intent-cli guide model`). One conductor seat carries architect, orchestrator, and builder and runs every execution-unit phase in order; a fresh independent reviewer subagent carries reviewer for every review and re-review. Every delivery gate still applies.",
            TeamMode = "Record it explicitly with `intent-cli team-mode set --domain <domain> --team <team> --mode solo-conductor --write`; it is never inferred from topology. `intent-cli guide bootstrap` and `intent-cli guide next` then recognize the team without a seat roster.",
            Roles =
            [
                "architect — the conductor, under a `design` claim actor: packet authoring and rulings.",
                "orchestrator — the conductor: publish, queue and label transitions, closeout.",
                "builder — the conductor, under an `implementation` claim actor, in an isolated clone.",
                "reviewer — a fresh independent subagent per review, started by the conductor; intent-cli never starts or manages it.",
            ],
        },
        Loop =
        [
            new SoloConductorLoopStep
            {
                Number = 1,
                Id = "claim-design",
                Instruction = "Acquire the execution-unit claim as the design actor before authoring.",
                Commands =
                [
                    IntentCli("intent-cli claim acquire --scope execution-unit:<unit> --actor design --team <team> --reason <text> --write --format json"),
                ],
            },
            new SoloConductorLoopStep
            {
                Number = 2,
                Id = "bug-chain-or-ruling",
                Instruction = "Start from a bug chain or from an operator ruling recorded in the host; do not author a packet without one.",
                Commands =
                [
                    IntentCli("intent-cli bug report <domain> --title <text> --from-file <path>"),
                    IntentCli("intent-cli bug triage <bug-id>"),
                    IntentCli("intent-cli bug plan <bug-id>"),
                ],
            },
            new SoloConductorLoopStep
            {
                Number = 3,
                Id = "packet-draft-and-validate",
                Instruction = "Author the packet, validate the body, run the facet check, and dry-run publication. Read `title_source` and every warning before writing. For a team declared in `[[cross_runtime_review.teams]]` (G835), after the dry runs render one `review cross-runtime request --kind design` for the declared conductor runtime and one for a different runtime; the seat runs each invocation; `review cross-runtime record --kind design --write`; `review cross-runtime status --kind design`; after any packet edit the digest changes and the review repeats. Records copied from another host clone with a skewed clock can empty the epoch and fail closed. Dry-run `issue publish-flow` reports the design gate even when the issue exists only on GitHub. Only `issue publish-flow` is gated; `issue draft` / `issue create`, `issue publish-reviewed`, `queue dispatch`, and `bug implementation-issue` bypass the design gate and are not part of this loop.",
                Commands =
                [
                    IntentCli("intent-cli packet draft --execution-unit <unit> --domain <domain> --target-repo <owner/repo> --team <team> --dry-run --format json"),
                    IntentCli("intent-cli issue validate-body --from-file .intent-cli/issues/<unit>/github-body.md"),
                    IntentCli("intent-cli intent facet-check --domain <domain> --packet <unit> --format json"),
                    IntentCli("intent-cli issue publish-flow <unit> --domain <domain> --team <team> --repo <owner/repo> --format json"),
                    IntentCli($"intent-cli review cross-runtime request --kind design --execution-unit <unit> --runtime {RuntimePlaceholder} --out-dir <out-dir> [--clone <read-only-clone>] [--model <model>] [--effort <level>] [--opencode-provider-config <file>] --format json"),
                    IntentCli("intent-cli review cross-runtime record --kind design --execution-unit <unit> --packet-digest <sha256> --runtime <runtime> --runtime-version <version> --verdict-file <verdict-file> --write --format json"),
                    IntentCli("intent-cli review cross-runtime status --kind design --execution-unit <unit> --format json"),
                ],
            },
            new SoloConductorLoopStep
            {
                Number = 4,
                Id = "seed-and-publish",
                Instruction = "Seed the queue from the packet, publish, and record the issue publication.",
                Commands =
                [
                    IntentCli("intent-cli automation queue-seed-from-packet --execution-unit <unit> --domain <domain> --team <team> --target-repo <owner/repo> --write --format json"),
                    IntentCli("intent-cli issue publish-flow <unit> --domain <domain> --team <team> --repo <owner/repo> --write --format json"),
                    IntentCli("intent-cli automation issue-publish --repo <owner/repo> --execution-unit <unit> --write --format json"),
                ],
            },
            new SoloConductorLoopStep
            {
                Number = 5,
                Id = "switch-to-implementation",
                Instruction = "Release the design claim, acquire the implementation claim, and take the worker claim for the published issue.",
                Commands =
                [
                    IntentCli("intent-cli claim release --scope execution-unit:<unit> --actor design --team <team> --reason <text> --write --format json"),
                    IntentCli("intent-cli claim acquire --scope execution-unit:<unit> --actor implementation --team <team> --reason <text> --write --format json"),
                    IntentCli("intent-cli worker claim --repo <owner/repo> --kind issue --number <issue> --team <team> --write --format json"),
                ],
            },
            new SoloConductorLoopStep
            {
                Number = 6,
                Id = "implement-and-open-pr",
                Instruction = "Implement in an isolated clone using the Builder invocations (guidance) below, run the full suite, open the PR, and record the worker completion.",
                Commands =
                [
                    Git("git clone <repo-url> <isolated-clone> && git -C <isolated-clone> switch -c <branch>"),
                    Gh("gh pr create --repo <owner/repo> --base main --head <branch> --body-file <pr-body>"),
                    IntentCli("intent-cli worker complete --repo <owner/repo> --domain <domain> --kind issue --number <issue> --outcome pr-created --pr <pr> --write --format json"),
                ],
            },
            new SoloConductorLoopStep
            {
                Number = 7,
                Id = "independent-subagent-review",
                Instruction = "Start a fresh reviewer subagent with only the review inputs, then record its verdict on the PR naming it an independent subagent review. For a team declared in `[[cross_runtime_review.teams]]` (G834), from the host root: render one request with `review cross-runtime request` for the declared conductor runtime (the same-runtime reviewer) and one for a different runtime (the cross-runtime reviewer), each into its own out-dir; the seat runs the command in each `<out-dir>/invocation.txt` itself in a read-only clone at the head (a claude or cursor verdict must be that pinned invocation's envelope, so an in-session subagent's reply cannot be recorded for those runtimes); then `review cross-runtime record --write` both verdicts, and post each rendered comment with `gh pr review --comment --body-file`. Read-only enforcement differs by runtime (measured): codex `-s read-only` is sandbox-enforced; the claude invocation removes only the Edit, Write, and NotebookEdit tools, so the Claude reviewer may run commands such as tests and shell-level writes are not sandbox-enforced; cursor `--mode ask` refuses every non-read-only tool, including shell commands, so the Cursor reviewer reads files but cannot run git or tests; copilot `--available-tools view rg glob` leaves only those three tools, so the reviewer reads files but cannot write or run commands; opencode's rendered config denies every tool and allows only read, glob, grep, and list. copilot and opencode reviewers may return the verdict as one trailing fenced JSON object. `--model` is required for copilot and opencode on `request` and `record`. Advise a reviewer model from a different vendor and model family than the conductor's model; the gate does not check this. intent-cli renders the request and records verdicts; it does not start, launch, or manage the reviewer. Confirming each vendor's automation terms is the operator's responsibility.",
                Commands =
                [
                    IntentCli($"intent-cli review cross-runtime request --repo <owner/repo> --pr <pr> --head-sha <head-sha> --execution-unit <unit> --runtime {RuntimePlaceholder} --clone <read-only-clone> --out-dir <out-dir> [--model <model>] [--effort <level>] [--opencode-provider-config <file>] --format json"),
                    IntentCli("intent-cli review cross-runtime record --repo <owner/repo> --pr <pr> --head-sha <head-sha> --execution-unit <unit> --kind implementation --runtime <runtime> --runtime-version <version> --verdict-file <verdict-file> --comment-out <review-body> --write --format json"),
                    Gh("gh pr review <pr> --repo <owner/repo> --comment --body-file <review-body>"),
                ],
            },
            new SoloConductorLoopStep
            {
                Number = 8,
                Id = "fix-and-delta-review",
                Instruction = "Fix blocking findings, push, and have a new reviewer subagent re-review the delta against the new head before merge. For a declared team, a blocking finding from either reviewer stops merge, and the runtime that raised it must re-review the new head (`cross-runtime-review-rereview-missing` otherwise). A later approve on the same head supersedes that runtime's request-changes only when it comes from a fresh reviewer run, never a repeated prompt to the same session.",
                Commands =
                [
                    Git("git -C <isolated-clone> push origin <branch>"),
                    Gh("gh pr review <pr> --repo <owner/repo> --comment --body-file <re-review-body>"),
                ],
            },
            new SoloConductorLoopStep
            {
                Number = 9,
                Id = "exact-head-ci-and-merge",
                Instruction = "Wait for CI on the exact head SHA, record the approved transition from the host root with that head, and merge only that head. For a declared team the transition refuses unless the cross-runtime review gate on that head is satisfied; `review cross-runtime status` shows why.",
                Commands =
                [
                    Gh("gh run list --repo <owner/repo> --commit <head-sha> --json databaseId,headSha,attempt,status,conclusion"),
                    IntentCli("intent-cli review cross-runtime status --repo <owner/repo> --pr <pr> --head-sha <head-sha> --execution-unit <unit> --format json"),
                    IntentCli("intent-cli automation pr-transition --repo <owner/repo> --pr <pr> --transition approved --head-sha <head-sha> --write"),
                    Gh("gh pr merge <pr> --repo <owner/repo> --squash --match-head-commit <head-sha>"),
                ],
            },
            new SoloConductorLoopStep
            {
                Number = 10,
                Id = "closeout-and-release",
                Instruction = "Close out the PR, update the host submodule pointer, write back the intent node, record the evidence, and release the claim.",
                Commands =
                [
                    IntentCli("intent-cli closeout pr --pr <pr> --repo <owner/repo> --domain <domain> --pr-merged true --write --format json"),
                    Git("git -C <host-root> add <submodule-path> <node-path> && git -C <host-root> commit -m <message> && git -C <host-root> push origin main"),
                    IntentCli("intent-cli automation knowledge-writeback-record --execution-unit <unit> --role design --commit <sha> --target <node-path> --write --format json"),
                    IntentCli("intent-cli automation guide-reachability-record --execution-unit <unit> --role design --commit <sha> --write --format json"),
                    IntentCli("intent-cli claim release --scope execution-unit:<unit> --actor implementation --team <team> --reason <text> --write --format json"),
                ],
            },
        ],
        IndependenceRules =
        [
            "Blocking: the reviewer is a new subagent for every review and every re-review; a reviewer is never reused.",
            "Blocking: the reviewer receives only the packet, `review-context.md`, the PR body, and read-only clone paths.",
            "Blocking: the reviewer never receives the implementation conversation.",
            "Blocking: the verdict is recorded with `gh pr review --comment` and names an independent subagent review.",
            "Blocking: any fix commit after a verdict is re-reviewed as a delta before merge.",
        ],
        Pacing =
        [
            "CI and full test suites run in the background while the next unit is investigated.",
            "Merge only on exact-head CI and a reviewer verdict on that same head.",
            "A CI waiter binds to the head SHA and the run attempt, never to the latest run on the branch.",
            "A mutation or negative check runs only after a successful build, so a stale binary cannot pass it.",
        ],
        OperatorQuestions = "Ask the operator only at scope, version, policy, or default decision points; otherwise proceed with the stated recommendation and record the ruling in the host.",
        HostDiscipline =
        [
            "One host checkout per seat.",
            "On a `queue-state` conflict, take the origin side and re-apply the transition through the canonical command.",
            "Never hand-edit labels, queue state, claims, or runs logs.",
        ],
        HandoffDurability = "Keep a scope ruling and per-unit closeout writebacks in the host so a compacted or new session can resume from recorded state.",
        Limits =
        [
            "Parallelism is 1; use the four-thread or five-thread model for throughput.",
            "Review independence depends on the subagent boundary; a runtime that cannot start an isolated subagent must not use this model.",
            "A deviation from an acceptance criterion is recorded as an architect decision before merge.",
            "Cross-runtime review (G834) is required only for teams declared in `[[cross_runtime_review.teams]]`; the gate assumes an honest seat and is not a security boundary, because `gh pr merge` is not gated.",
        ],
        NoExecutionBoundary = "This guide renders text only. The conductor seat starts reviewer subagents; intent-cli does not start, launch, or manage any agent, and reads no host metadata to render this contract.",
        Builder = BuildBuilder(),
    };

    private static bool TryParseFormat(string[] args, out string format, out string error)
    {
        format = FormatMarkdown;
        error = string.Empty;

        for (var index = 0; index < args.Length; index++)
        {
            if (!string.Equals(args[index], "--format", StringComparison.Ordinal))
            {
                error = $"Unknown argument '{args[index]}'.";
                return false;
            }

            if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[++index]))
            {
                error = "--format requires markdown or json.";
                return false;
            }

            format = args[index];
            if (format is not FormatJson and not FormatMarkdown)
            {
                error = "--format must be markdown or json.";
                return false;
            }
        }

        return true;
    }

    private static void WriteHelp(TextWriter writer)
    {
        writer.WriteLine("guide solo-conductor");
        writer.WriteLine(UsageLine);
        writer.WriteLine("Read-only, metadata-free solo conductor contract: one conductor seat (architect, orchestrator, builder) runs the per-unit loop; a fresh independent reviewer subagent reviews every head.");
    }

    private static void WriteMarkdown(TextWriter writer, SoloConductorGuide guide)
    {
        writer.WriteLine("# intent-cli — solo conductor operating contract (G833)");
        writer.WriteLine();
        writer.WriteLine(UsageLine);
        writer.WriteLine();
        writer.WriteLine("## Model");
        writer.WriteLine();
        writer.WriteLine(guide.Model.Summary);
        writer.WriteLine();
        writer.WriteLine($"- team mode: {guide.Model.TeamMode}");
        foreach (var role in guide.Model.Roles)
        {
            writer.WriteLine($"- {role}");
        }

        writer.WriteLine();
        writer.WriteLine("## Per-unit loop");
        writer.WriteLine();
        foreach (var step in guide.Loop)
        {
            writer.WriteLine($"{step.Number}. **{step.Id}** — {step.Instruction}");
            foreach (var command in step.Commands)
            {
                writer.WriteLine($"   - [{command.Tool}] `{command.Command}`");
            }
        }

        WriteBuilder(writer, guide.Builder);

        WriteList(writer, "Independence rules (blocking)", guide.IndependenceRules);
        WriteList(writer, "Pacing", guide.Pacing);
        writer.WriteLine();
        writer.WriteLine("## Operator questions");
        writer.WriteLine();
        writer.WriteLine(guide.OperatorQuestions);
        WriteList(writer, "Host discipline", guide.HostDiscipline);
        writer.WriteLine();
        writer.WriteLine("## Handoff durability");
        writer.WriteLine();
        writer.WriteLine(guide.HandoffDurability);
        WriteList(writer, "Limits", guide.Limits);
        writer.WriteLine();
        writer.WriteLine("## No-execution boundary");
        writer.WriteLine();
        writer.WriteLine(guide.NoExecutionBoundary);
    }

    private static void WriteBuilder(TextWriter writer, SoloConductorBuilder builder)
    {
        writer.WriteLine();
        writer.WriteLine("## Builder invocations (guidance)");
        writer.WriteLine();
        foreach (var item in builder.Contract)
        {
            writer.WriteLine($"- {item}");
        }

        writer.WriteLine();
        foreach (var invocation in builder.Invocations)
        {
            writer.WriteLine($"- **{invocation.Runtime}** (`measured: {invocation.Measured.ToString().ToLowerInvariant()}`): `{invocation.Command}`");
            writer.WriteLine($"  - enforcement: {invocation.Enforcement}");
        }

        writer.WriteLine();
        writer.WriteLine("**OpenCode builder config** (`opencode_config`):");
        writer.WriteLine();
        writer.WriteLine("```json");
        writer.Write(builder.OpencodeConfig.TrimEnd());
        writer.WriteLine();
        writer.WriteLine("```");
    }

    private static void WriteList(TextWriter writer, string heading, IReadOnlyList<string> items)
    {
        writer.WriteLine();
        writer.WriteLine($"## {heading}");
        writer.WriteLine();
        foreach (var item in items)
        {
            writer.WriteLine($"- {item}");
        }
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };
}

internal sealed record SoloConductorGuide
{
    [JsonPropertyName("route")] public required string Route { get; init; }
    [JsonPropertyName("contract_version")] public required string ContractVersion { get; init; }
    [JsonPropertyName("read_only")] public required bool ReadOnly { get; init; }
    [JsonPropertyName("metadata_free")] public required bool MetadataFree { get; init; }
    [JsonPropertyName("model")] public required SoloConductorModel Model { get; init; }
    [JsonPropertyName("loop")] public required IReadOnlyList<SoloConductorLoopStep> Loop { get; init; }
    [JsonPropertyName("independence_rules")] public required IReadOnlyList<string> IndependenceRules { get; init; }
    [JsonPropertyName("pacing")] public required IReadOnlyList<string> Pacing { get; init; }
    [JsonPropertyName("operator_questions")] public required string OperatorQuestions { get; init; }
    [JsonPropertyName("host_discipline")] public required IReadOnlyList<string> HostDiscipline { get; init; }
    [JsonPropertyName("handoff_durability")] public required string HandoffDurability { get; init; }
    [JsonPropertyName("limits")] public required IReadOnlyList<string> Limits { get; init; }
    [JsonPropertyName("no_execution_boundary")] public required string NoExecutionBoundary { get; init; }
    [JsonPropertyName("builder")] public required SoloConductorBuilder Builder { get; init; }
}

internal sealed record SoloConductorBuilder
{
    [JsonPropertyName("contract")] public required IReadOnlyList<string> Contract { get; init; }
    [JsonPropertyName("invocations")] public required IReadOnlyList<SoloConductorBuilderInvocation> Invocations { get; init; }
    [JsonPropertyName("opencode_config")] public required string OpencodeConfig { get; init; }
}

internal sealed record SoloConductorBuilderInvocation
{
    [JsonPropertyName("runtime")] public required string Runtime { get; init; }
    [JsonPropertyName("command")] public required string Command { get; init; }
    [JsonPropertyName("enforcement")] public required string Enforcement { get; init; }
    [JsonPropertyName("measured")] public required bool Measured { get; init; }
}

internal sealed record SoloConductorModel
{
    [JsonPropertyName("summary")] public required string Summary { get; init; }
    [JsonPropertyName("team_mode")] public required string TeamMode { get; init; }
    [JsonPropertyName("roles")] public required IReadOnlyList<string> Roles { get; init; }
}

internal sealed record SoloConductorLoopStep
{
    [JsonPropertyName("number")] public required int Number { get; init; }
    [JsonPropertyName("id")] public required string Id { get; init; }
    [JsonPropertyName("instruction")] public required string Instruction { get; init; }
    [JsonPropertyName("commands")] public required IReadOnlyList<SoloConductorCommand> Commands { get; init; }
}

internal sealed record SoloConductorCommand
{
    [JsonPropertyName("tool")] public required string Tool { get; init; }
    [JsonPropertyName("command")] public required string Command { get; init; }
}
