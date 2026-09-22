using IntentSystem.Cli.Commands;

namespace IntentSystem.Cli.Tests;

/// <summary>G842: generated solely from the pinned implementation notes.</summary>
internal static class G842PinnedContractTexts
{
    internal const string BuilderContractItem1 = "The conductor runs one builder at a time in the unit's isolated clone (step 6), outside every runtime's trusted folders. The copilot line uses a fresh, empty `COPILOT_HOME` and `XDG_CONFIG_HOME` per run (`mktemp -d`, removed after), and carries `GH_CONFIG_DIR=<operator-gh-config-root>`; replace that placeholder with gh's config root found in gh's documented order so `gh auth token` can find a `hosts.yml` token. Copilot itself runs `gh auth token` for credentials, so a signed-in real `gh` must be on PATH; item 3's no-`gh` rule covers the builder's own commands.";
    internal const string BuilderContractItem2 = "The task prompt is a file the conductor writes and passes on stdin, except cursor, which takes it as an argument. Any OpenCode command shown without a prompt file carries `< /dev/null`, because with stdin open `opencode run` waits silently (measured), including in the background.";
    internal const string BuilderContractItem3 = "The builder may edit and commit inside the clone and never pushes, opens a PR or runs `gh`; the conductor pushes, opens the PR and owns claims and transitions.";
    internal const string BuilderContractItem4 = "What bounds the builder differs by runtime, and no measured invocation sandboxes shell commands, so the conductor reviews the clone's diff before pushing.";
    internal const string BuilderContractItem5 = "The OpenCode builder line uses fresh, empty `XDG_CONFIG_HOME` and `OPENCODE_CONFIG_DIR` directories per run. The clone's config and `AGENTS.md` are not loaded, so the task file carries every repository instruction. A provider defined only in the global config goes into the builder config's `provider` block after `$schema`.";
    internal const string BuilderContractItem6 = "A local model provider serves one seat at a time; do not start one while another runs. A local-model review can outgrow the context window as conversation and tool output accumulate, even when the prompt fits, and the same prompt varies between runs; for tool-heavy reviews prefer a smaller packet or a larger window, and expect a retry. intent-cli checks none of this.";
    internal const string BuilderContractItem7 = "intent-cli renders these lines and never launches a builder, reviewer or AI provider CLI; the review commands' only launch is the existing claim-read `git fetch`.";
    internal static readonly IReadOnlyList<string> BuilderContractItems =
    [
        BuilderContractItem1,
        BuilderContractItem2,
        BuilderContractItem3,
        BuilderContractItem4,
        BuilderContractItem5,
        BuilderContractItem6,
        BuilderContractItem7,
    ];

    internal const string CodexBuilderCommand = "codex exec -s workspace-write -C <isolated-clone> [-m <model>] - < <task-file>";
    internal const string ClaudeBuilderCommand = "cd <isolated-clone> && claude -p --permission-mode acceptEdits --allowedTools Bash --disallowedTools 'Bash(git push:*)' 'Bash(gh:*)' [--model <model>] < <task-file>";
    internal const string CursorBuilderCommand = "cursor-agent -p --force --sandbox enabled --trust --workspace <isolated-clone> [--model <model>] --output-format json \"$(cat <task-file>)\"";
    internal const string CopilotBuilderCommand = "COPILOT_ALLOW_ALL= COPILOT_HOME=<fresh-empty-copilot-home> XDG_CONFIG_HOME=<fresh-empty-xdg-dir> GH_CONFIG_DIR=<operator-gh-config-root> copilot -C <isolated-clone> --model <model> [--reasoning-effort <effort>] --allow-all-tools --deny-tool 'shell(git push)' --deny-tool 'shell(gh)' --disable-builtin-mcps --no-ask-user --stream off --output-format json < <task-file>";
    internal const string OpencodeBuilderCommand = "OPENCODE_PERMISSION= OPENCODE_CONFIG_CONTENT= XDG_CONFIG_HOME=<fresh-empty-dir> OPENCODE_CONFIG_DIR=<fresh-empty-dir-2> OPENCODE_DISABLE_PROJECT_CONFIG=1 OPENCODE_CONFIG=<builder-config-file> opencode run --pure --dir <isolated-clone> -m <provider/model> [--variant <effort>] --auto --format json < <task-file>";

    internal const string CodexBuilderEnforcement = "not measured.";
    internal const string ClaudeBuilderEnforcement = "a file-tool write outside the clone and `git push` refused; an outside shell write not refused.";
    internal const string CursorBuilderEnforcement = "nothing enforced.";
    internal const string CopilotBuilderMeasuredEnforcement = "an inside write succeeded; a file-tool write and a literal shell redirect outside refused by path verification (not a sandbox); `git push` refused; other outside forms unmeasured.";
    internal const string OpencodeBuilderMeasuredEnforcement = "an inside write and a commit succeeded; `git push` refused, even through `task` and with a permissive clone and global config; no plugin or MCP server ran; outside writes unmeasured here.";
    internal const string CopilotBuilderEnforcement = "The model is required. an inside write succeeded; a file-tool write and a literal shell redirect outside refused by path verification (not a sandbox); `git push` refused; other outside forms unmeasured.";
    internal const string OpencodeBuilderEnforcement = "The model is required. an inside write and a commit succeeded; `git push` refused, even through `task` and with a permissive clone and global config; no plugin or MCP server ran; outside writes unmeasured here.";
    internal const string OpencodeBuilderConfigJson = "{\n  \"$schema\": \"https://opencode.ai/config.json\",\n  \"permission\": {\n    \"external_directory\": \"deny\",\n    \"bash\": {\n      \"*\": \"allow\",\n      \"git push*\": \"deny\",\n      \"gh *\": \"deny\"\n    }\n  }\n}\n";

    internal static readonly IReadOnlyList<string> CopilotReadOnlyEnforcementStatements =
    [
        "`--available-tools view rg glob` leaves only those three tools, so the reviewer reads but cannot write or run commands, and path verification refuses reads outside the workspace.",
        "`--disable-builtin-mcps` disables the GitHub MCP server, and with the isolated home the operator's user MCP servers do not start (measured); another server's tools would not be exposed anyway.",
        "`--no-custom-instructions` stops the reviewed workspace from instructing the reviewer through `AGENTS.md` or `.github/copilot-instructions.md`, which matters most for the implementation kind, where the workspace is the clone of the PR head (canary-measured 2026-09-18 both ways; fixtures `copilot-instruction-canary-without-flag.jsonl` and `copilot-instruction-canary-with-flag.jsonl`).",
        "`COPILOT_ALLOW_ALL=` stops the variable from trusting the workspace, and `COPILOT_HOME` and `XDG_CONFIG_HOME` point at empty directories created under the out-dir, so the operator's `trustedFolders`, IDE lock files, user hooks and user MCP servers are not read.",
        "**Credentials.** The isolated home holds no token, so Copilot runs `gh auth token --hostname github.com` itself (measured 2026-09-17: with a fake `gh` first on PATH it exits 1, \"No authentication information found\"); a signed-in real `gh` must therefore be first on PATH, and intent-cli never launches it. The earlier note that authentication is unaffected by `COPILOT_HOME` (2026-09-16) is superseded: it held only because the real `gh` was on PATH.",
        "Unmeasured trust sources (implementation notes) are mitigated by the isolated home and by using no SDK or ACP host.",
    ];

    internal static readonly IReadOnlyList<string> OpencodeReadOnlyEnforcementStatements =
    [
        "The rendered config denies every tool (`*`) and allows only read, glob, grep and list, at the top level and for the `intent-cli-reviewer` agent.",
        "`task` is denied because agent-level denials do not reach subagents: a reviewer delegated a file change to `general`.",
        "`external_directory` is denied, so a path that is lexically outside the workspace is refused. Measured 2026-09-18: that check compares lexically, so a symlink inside the workspace whose target is outside is still read. intent-cli refuses such a workspace before rendering; the runtime does not confine itself.",
        "`OPENCODE_DISABLE_PROJECT_CONFIG=1` and `--pure` stop a workspace's config, agents, plugins and MCP entries from overriding these rules or running code, and its `AGENTS.md` does not reach the model (measured with a canary).",
        "The isolated config directories and the two empty variables keep out the operator's global config and inherited overrides; each re-enabled writes when not isolated, and the full form held against all of them (measured).",
        "Data, state and cache stay the operator's (credentials); they were not probed as permission sources.",
    ];

    internal static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> ReadOnlyEnforcementStatements =
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
        {
            ["copilot"] = CopilotReadOnlyEnforcementStatements,
            ["opencode"] = OpencodeReadOnlyEnforcementStatements,
        };

    internal static readonly IReadOnlyDictionary<string, string> ExpectedReadOnlyEnforcement =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["copilot"] = string.Join(" ", CopilotReadOnlyEnforcementStatements),
            ["opencode"] = string.Join(" ", OpencodeReadOnlyEnforcementStatements),
        };

    internal static readonly IReadOnlyList<string> CopilotDenyFlags =
    [
        "--allow-all",
        "--yolo",
        "--allow-all-paths",
        "--allow-all-urls",
        "--allow-tool",
        "--allow-url",
        "--add-dir",
        "--autopilot",
        "--mode",
        "--plan",
        "--experimental",
        "--remote",
        "--share",
        "--share-gist",
        "--enable-all-github-mcp-tools",
        "--additional-mcp-config",
        "--plugin-dir",
        "--agent",
        "COPILOT_ALLOW_ALL=true",
    ];

    internal static readonly IReadOnlyList<string> CopilotAllowedFlags =
    [
        "-C",
        "--model",
        "--reasoning-effort",
        "--available-tools",
        "--allow-all-tools",
        "--disable-builtin-mcps",
        "--no-custom-instructions",
        "--stream",
        "--output-format",
    ];

    internal static readonly IReadOnlyList<string> CopilotEnvAssignments =
    [
        "COPILOT_ALLOW_ALL=",
        "COPILOT_HOME=<quoted path>",
        "XDG_CONFIG_HOME=<quoted path>",
        "GH_CONFIG_DIR=<quoted path>",
    ];

    internal static readonly IReadOnlyList<string> OpencodeDenyFlags =
    [
        "--auto",
        "--attach",
        "--share",
        "--continue",
        "--session",
        "--fork",
        "--port",
        "--command",
        "--interactive",
        "-i",
        "OPENCODE_PERMISSION",
        "OPENCODE_CONFIG_CONTENT",
        "XDG_DATA_HOME",
    ];

    internal static readonly IReadOnlyList<string> OpencodeAllowedFlags =
    [
        "--pure",
        "--dir",
        "-m",
        "--variant",
        "--agent",
        "--format",
    ];

    internal static readonly IReadOnlyList<string> OpencodeEnvAssignments =
    [
        "OPENCODE_PERMISSION=",
        "OPENCODE_CONFIG_CONTENT=",
        "XDG_CONFIG_HOME=<quoted path>",
        "OPENCODE_CONFIG_DIR=<quoted path>",
        "OPENCODE_DISABLE_PROJECT_CONFIG=1",
        "OPENCODE_CONFIG=<quoted path>",
    ];

    internal static string ExpectedReviewerInvocation(string runtime, string workspace, string outDir, string model, string? effort)
    {
        var quotedWorkspace = PosixSingleQuote(workspace);
        var quotedModel = PosixSingleQuote(model);
        var effortFlag = string.IsNullOrEmpty(effort)
            ? string.Empty
            : runtime == "copilot"
                ? $" --reasoning-effort {PosixSingleQuote(effort)}"
                : $" --variant {PosixSingleQuote(effort)}";

        return runtime switch
        {
            "copilot" =>
                $"COPILOT_ALLOW_ALL= COPILOT_HOME={PosixSingleQuote(Path.Combine(outDir, "copilot-home"))} "
                + $"XDG_CONFIG_HOME={PosixSingleQuote(Path.Combine(outDir, "copilot-xdg"))} "
                + $"GH_CONFIG_DIR={PosixSingleQuote(CrossRuntimeReviewHomeAccessGuard.ResolveGhConfigDir())} "
                + $"copilot -C {quotedWorkspace} --model {quotedModel}{effortFlag} "
                + "--available-tools view rg glob --allow-all-tools --disable-builtin-mcps --no-custom-instructions --stream off --output-format json "
                + $"< {PosixSingleQuote(Path.Combine(outDir, "prompt.md"))} > {PosixSingleQuote(Path.Combine(outDir, "verdict.raw.json"))}",
            "opencode" =>
                $"rm -f {PosixSingleQuote(Path.Combine(outDir, "opencode-exit.txt"))}; "
                + $"OPENCODE_PERMISSION= OPENCODE_CONFIG_CONTENT= XDG_CONFIG_HOME={PosixSingleQuote(Path.Combine(outDir, "opencode-xdg"))} "
                + $"OPENCODE_CONFIG_DIR={PosixSingleQuote(Path.Combine(outDir, "opencode-config-dir"))} OPENCODE_DISABLE_PROJECT_CONFIG=1 "
                + $"OPENCODE_CONFIG={PosixSingleQuote(Path.Combine(outDir, "opencode-reviewer.json"))} opencode run --pure --dir {quotedWorkspace} "
                + $"-m {quotedModel}{effortFlag} --agent intent-cli-reviewer --format json "
                + $"< {PosixSingleQuote(Path.Combine(outDir, "prompt.md"))} > {PosixSingleQuote(Path.Combine(outDir, "verdict.raw.json"))}; "
                + $"printf '%s\\n' \"$?\" > {PosixSingleQuote(Path.Combine(outDir, "opencode-exit.txt"))}",
            _ => throw new ArgumentOutOfRangeException(nameof(runtime), runtime, "Unsupported G842 reviewer runtime."),
        };
    }

    private static string PosixSingleQuote(string value) => "'" + value.Replace("'", "'\\''", StringComparison.Ordinal) + "'";
}
