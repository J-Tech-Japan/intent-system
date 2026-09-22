using System.Text.RegularExpressions;

namespace IntentSystem.Cli.Commands;

/// <summary>
/// G834/G842: the supported reviewer runtimes (ordinal), pinned read-only
/// invocations, allow-lists, and enforcement text. intent-cli renders this text
/// only; it never starts, launches, or manages any agent or provider process.
/// </summary>
internal static class CrossRuntimeReviewRuntimes
{
    public const string Codex = "codex";
    public const string Claude = "claude";
    public const string Cursor = "cursor";
    public const string Copilot = "copilot";
    public const string Opencode = "opencode";

    public static readonly IReadOnlyList<string> All = [Codex, Claude, Cursor, Copilot, Opencode];

    private static readonly string[] CopilotEffortLevelOrder =
        ["none", "minimal", "low", "medium", "high", "xhigh", "max"];

    private static readonly HashSet<string> CopilotEffortLevels = new(CopilotEffortLevelOrder, StringComparer.Ordinal);

    private static readonly Regex OpencodeProviderIdPattern = new(
        @"^[A-Za-z0-9][A-Za-z0-9._-]*$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static bool IsSupported(string? runtime) =>
        runtime is not null && All.Contains(runtime, StringComparer.Ordinal);

    public static bool RequiresModel(string runtime) =>
        runtime is Copilot or Opencode;

    public static bool AcceptsEffort(string runtime) =>
        runtime is Copilot or Opencode;

    public static string Describe() => "codex, claude, cursor, copilot, or opencode";

    public static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> AllowedFlags =
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
        {
            [Codex] = ["-s", "-C", "--output-schema", "-o", "-", "-m"],
            [Claude] = ["-p", "--permission-mode", "--disallowedTools", "--output-format", "--json-schema", "--model"],
            [Cursor] = ["-p", "--mode", "--sandbox", "--trust", "--workspace", "--output-format", "--model"],
            [Copilot] = ["-C", "--model", "--reasoning-effort", "--available-tools", "--allow-all-tools", "--disable-builtin-mcps", "--no-custom-instructions", "--stream", "--output-format"],
            [Opencode] = ["--pure", "--dir", "-m", "--variant", "--agent", "--format"],
        };

    public static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> EnvAssignments =
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
        {
            [Copilot] =
            [
                "COPILOT_ALLOW_ALL=",
                CrossRuntimeReviewFiles.CopilotHome,
                CrossRuntimeReviewFiles.CopilotXdg,
            ],
            [Opencode] =
            [
                "OPENCODE_PERMISSION=",
                "OPENCODE_CONFIG_CONTENT=",
                CrossRuntimeReviewFiles.OpencodeXdg,
                CrossRuntimeReviewFiles.OpencodeConfigDir,
                "OPENCODE_DISABLE_PROJECT_CONFIG=1",
                CrossRuntimeReviewFiles.OpencodeReviewerConfig,
            ],
        };

    public static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> DenyFlags =
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
        {
            [Codex] =
            [
                "--force", "--yolo", "--approve-for-me", "--auto-review", "--approve-mcps", "bypassPermissions",
                "acceptEdits", "dontAsk", "--permission-mode auto", "--dangerously-skip-permissions",
                "--allow-dangerously-skip-permissions", "--dangerously-bypass-approvals-and-sandbox",
                "--dangerously-bypass-hook-trust", "danger-full-access", "workspace-write", "--sandbox disabled",
            ],
            [Claude] =
            [
                "--force", "--yolo", "--approve-for-me", "--auto-review", "--approve-mcps", "bypassPermissions",
                "acceptEdits", "dontAsk", "--permission-mode auto", "--dangerously-skip-permissions",
                "--allow-dangerously-skip-permissions", "--dangerously-bypass-approvals-and-sandbox",
                "--dangerously-bypass-hook-trust", "danger-full-access", "workspace-write", "--sandbox disabled",
            ],
            [Cursor] =
            [
                "--force", "--yolo", "--approve-for-me", "--auto-review", "--approve-mcps", "bypassPermissions",
                "acceptEdits", "dontAsk", "--permission-mode auto", "--dangerously-skip-permissions",
                "--allow-dangerously-skip-permissions", "--dangerously-bypass-approvals-and-sandbox",
                "--dangerously-bypass-hook-trust", "danger-full-access", "workspace-write", "--sandbox disabled",
            ],
            [Copilot] =
            [
                "--force", "--yolo", "--approve-for-me", "--auto-review", "--approve-mcps", "bypassPermissions",
                "acceptEdits", "dontAsk", "--permission-mode auto", "--dangerously-skip-permissions",
                "--allow-dangerously-skip-permissions", "--dangerously-bypass-approvals-and-sandbox",
                "--dangerously-bypass-hook-trust", "danger-full-access", "workspace-write", "--sandbox disabled",
                "--allow-all", "--yolo", "--allow-all-paths", "--allow-all-urls", "--allow-tool", "--allow-url",
                "--add-dir", "--autopilot", "--mode", "--plan", "--experimental", "--remote", "--share",
                "--share-gist", "--enable-all-github-mcp-tools", "--additional-mcp-config", "--plugin-dir",
                "--agent", "COPILOT_ALLOW_ALL=true",
            ],
            [Opencode] =
            [
                "--force", "--yolo", "--approve-for-me", "--auto-review", "--approve-mcps", "bypassPermissions",
                "acceptEdits", "dontAsk", "--permission-mode auto", "--dangerously-skip-permissions",
                "--allow-dangerously-skip-permissions", "--dangerously-bypass-approvals-and-sandbox",
                "--dangerously-bypass-hook-trust", "danger-full-access", "workspace-write", "--sandbox disabled",
                "--auto", "--attach", "--share", "--continue", "--session", "--fork", "--port", "--command",
                "--interactive", "-i", "OPENCODE_PERMISSION", "OPENCODE_CONFIG_CONTENT", "XDG_DATA_HOME",
            ],
        };

    public static readonly IReadOnlyDictionary<string, string> ReadOnlyEnforcement =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [Codex] = "codex `-s read-only` is sandbox-enforced: the reviewer can read and run commands, but the sandbox refuses file writes.",
            [Claude] = "claude `--permission-mode plan --disallowedTools Edit,Write,NotebookEdit` removes the file-writing tools only. Command execution (for example building and running tests) is allowed, and writes made through shell commands are not sandbox-enforced.",
            [Cursor] = "cursor `--mode ask` refuses every non-read-only tool, including all shell commands, so the reviewer reads files but cannot run git or tests; the head it echoes is read from files such as `.git/HEAD`, not checked with git. `--mode plan` was not used because a measured plan-mode run switched itself to agent mode and wrote files inside and outside the workspace; `--sandbox enabled` did not stop those writes.",
            [Copilot] = "--available-tools view rg glob leaves only those three tools, so the reviewer reads but cannot write or run commands, and path verification refuses reads outside the workspace. --disable-builtin-mcps disables the GitHub MCP server, and with the isolated home the operator's user MCP servers do not start (measured); another server's tools would not be exposed anyway. --no-custom-instructions stops the reviewed workspace from instructing the reviewer through AGENTS.md or .github/copilot-instructions.md, which matters most for the implementation kind, where the workspace is the clone of the PR head (canary-measured 2026-09-18 both ways; fixtures copilot-instruction-canary-*.jsonl). COPILOT_ALLOW_ALL= stops the variable from trusting the workspace, and COPILOT_HOME and XDG_CONFIG_HOME point at empty directories created under the out-dir, so the operator's trustedFolders, IDE lock files, user hooks and user MCP servers are not read. Credentials: the isolated home holds no token, so Copilot runs gh auth token --hostname github.com itself (measured 2026-09-17: with a fake gh first on PATH it exits 1, \"No authentication information found\"); a signed-in real gh must therefore be first on PATH, and intent-cli never launches it. The earlier note that authentication is unaffected by COPILOT_HOME (2026-09-16) is superseded: it held only because the real gh was on PATH. Unmeasured trust sources (implementation notes) are mitigated by the isolated home and by using no SDK or ACP host.",
            [Opencode] = "The rendered config denies every tool (`*`) and allows only read, glob, grep and list, at the top level and for the intent-cli-reviewer agent. task is denied because agent-level denials do not reach subagents: a reviewer delegated a file change to general. external_directory is denied, so a path that is lexically outside the workspace is refused. Measured 2026-09-18: that check compares lexically, so a symlink inside the workspace whose target is outside is still read. intent-cli refuses such a workspace before rendering; the runtime does not confine itself. OPENCODE_DISABLE_PROJECT_CONFIG=1 and --pure stop the reviewed workspace's opencode.json, .opencode agents, plugins and MCP entries from overriding these rules or running code, and its AGENTS.md does not reach the model (measured with a canary). The isolated config directories and the two empty variables keep out the operator's global config and inherited overrides; each re-enabled writes when not isolated, and the full form held against all of them (measured). Data, state and cache stay the operator's (credentials); they were not probed as permission sources.",
        };

    public static string InvocationLabel(string runtime) =>
        $"# run by the seat ({runtime}); intent-cli renders this text and never runs it";

    public static string RenderInvocation(string runtime, string clone, string outDir, string? model = null, string? effort = null)
    {
        var quotedClone = CrossRuntimeReviewPaths.ShellQuote(clone);
        string Out(string file) => CrossRuntimeReviewPaths.ShellQuote(Path.Combine(outDir, file));
        var modelFlag = string.IsNullOrEmpty(model) ? string.Empty : $" {ModelFlag(runtime, model)}";
        var effortFlag = string.IsNullOrEmpty(effort) ? string.Empty : $" {EffortFlag(runtime, effort)}";

        return runtime switch
        {
            Codex =>
                $"codex exec -s read-only -C {quotedClone}{modelFlag} --output-schema {Out(CrossRuntimeReviewFiles.Schema)} "
                + $"-o {Out(CrossRuntimeReviewFiles.RawVerdict)} - < {Out(CrossRuntimeReviewFiles.Prompt)}",
            Claude =>
                $"cd {quotedClone} && claude -p --permission-mode plan --disallowedTools Edit,Write,NotebookEdit --output-format json{modelFlag} "
                + $"--json-schema \"$(cat {Out(CrossRuntimeReviewFiles.Schema)})\" < {Out(CrossRuntimeReviewFiles.Prompt)} "
                + $"> {Out(CrossRuntimeReviewFiles.RawVerdict)}",
            Cursor =>
                $"cursor-agent -p --mode ask --sandbox enabled --trust --workspace {quotedClone} --output-format json{modelFlag} "
                + $"\"$(cat {Out(CrossRuntimeReviewFiles.Prompt)})\" > {Out(CrossRuntimeReviewFiles.RawVerdict)}",
            Copilot =>
                $"COPILOT_ALLOW_ALL= COPILOT_HOME={Out(CrossRuntimeReviewFiles.CopilotHome)} XDG_CONFIG_HOME={Out(CrossRuntimeReviewFiles.CopilotXdg)} "
                + $"copilot -C {quotedClone} --model {CrossRuntimeReviewPaths.ShellQuote(model!)}{effortFlag} "
                + $"--available-tools view rg glob --allow-all-tools --disable-builtin-mcps --no-custom-instructions --stream off --output-format json "
                + $"< {Out(CrossRuntimeReviewFiles.Prompt)} > {Out(CrossRuntimeReviewFiles.RawVerdict)}",
            Opencode =>
                $"rm -f {Out(CrossRuntimeReviewFiles.OpencodeExit)}; OPENCODE_PERMISSION= OPENCODE_CONFIG_CONTENT= "
                + $"XDG_CONFIG_HOME={Out(CrossRuntimeReviewFiles.OpencodeXdg)} "
                + $"OPENCODE_CONFIG_DIR={Out(CrossRuntimeReviewFiles.OpencodeConfigDir)} OPENCODE_DISABLE_PROJECT_CONFIG=1 "
                + $"OPENCODE_CONFIG={Out(CrossRuntimeReviewFiles.OpencodeReviewerConfig)} opencode run --pure --dir {quotedClone} "
                + $"-m {CrossRuntimeReviewPaths.ShellQuote(model!)}{effortFlag} --agent intent-cli-reviewer --format json "
                + $"< {Out(CrossRuntimeReviewFiles.Prompt)} > {Out(CrossRuntimeReviewFiles.RawVerdict)}; "
                + $"printf '%s\\n' \"$?\" > {Out(CrossRuntimeReviewFiles.OpencodeExit)}",
            _ => throw new ArgumentOutOfRangeException(nameof(runtime), runtime, "Unsupported cross-runtime review runtime."),
        };
    }

    public static string ModelFlag(string runtime, string model) =>
        runtime switch
        {
            Codex => $"-m {CrossRuntimeReviewPaths.ShellQuote(model)}",
            Claude or Cursor => $"--model {CrossRuntimeReviewPaths.ShellQuote(model)}",
            Copilot => $"--model {CrossRuntimeReviewPaths.ShellQuote(model)}",
            Opencode => $"-m {CrossRuntimeReviewPaths.ShellQuote(model)}",
            _ => throw new ArgumentOutOfRangeException(nameof(runtime), runtime, "Unsupported cross-runtime review runtime."),
        };

    public static string EffortFlag(string runtime, string effort) =>
        runtime switch
        {
            Copilot => $"--reasoning-effort {CrossRuntimeReviewPaths.ShellQuote(effort)}",
            Opencode => $"--variant {CrossRuntimeReviewPaths.ShellQuote(effort)}",
            _ => throw new ArgumentOutOfRangeException(nameof(runtime), runtime, "Unsupported cross-runtime review runtime."),
        };

    public static bool TryValidateModel(string? runtime, string? model, out string error)
    {
        if (model is null)
        {
            if (runtime is Copilot or Opencode)
            {
                error = $"--model is required for runtime '{runtime}'.";
                return false;
            }

            error = string.Empty;
            return true;
        }

        if (!TryValidateModelCharacters(model, out error))
        {
            return false;
        }

        if (runtime is Copilot && string.Equals(model, "auto", StringComparison.OrdinalIgnoreCase))
        {
            error = "copilot refuses model 'auto' because the model that reviews would be unknown until the run ends.";
            return false;
        }

        if (runtime is Opencode && !TryValidateOpencodeModel(model, out error))
        {
            return false;
        }

        error = string.Empty;
        return true;
    }

    public static bool TryValidateModelCharacters(string? model, out string error)
    {
        if (model is null)
        {
            error = string.Empty;
            return true;
        }

        if (model.Length == 0)
        {
            error = "model must not be empty.";
            return false;
        }

        if (model[0] == '-')
        {
            error = "model must not start with '-'.";
            return false;
        }

        foreach (var character in model)
        {
            if (char.GetUnicodeCategory(character) == System.Globalization.UnicodeCategory.Control)
            {
                error = "model must not contain Unicode control characters.";
                return false;
            }
        }

        error = string.Empty;
        return true;
    }

    public static bool TryValidateOpencodeModel(string model, out string error)
    {
        var slashIndex = model.IndexOf('/');
        if (slashIndex <= 0)
        {
            error = "opencode model must be a provider id, then '/', then a non-empty remainder.";
            return false;
        }

        var provider = model[..slashIndex];
        var remainder = model[(slashIndex + 1)..];
        if (remainder.Length == 0)
        {
            error = "opencode model must be a provider id, then '/', then a non-empty remainder.";
            return false;
        }

        if (!OpencodeProviderIdPattern.IsMatch(provider))
        {
            error = "opencode provider id must match ^[A-Za-z0-9][A-Za-z0-9._-]*$.";
            return false;
        }

        error = string.Empty;
        return true;
    }

    public static bool TryValidateEffort(string? runtime, string? effort, out string error)
    {
        if (effort is null)
        {
            error = string.Empty;
            return true;
        }

        if (runtime is Codex or Claude or Cursor)
        {
            error = $"--effort is accepted only for runtimes {Copilot} and {Opencode}.";
            return false;
        }

        if (!TryValidateModelCharacters(effort, out error))
        {
            return false;
        }

        if (runtime is Copilot && !CopilotEffortLevels.Contains(effort))
        {
            error = $"copilot --effort must be one of: {string.Join(", ", CopilotEffortLevelOrder)}.";
            return false;
        }

        error = string.Empty;
        return true;
    }

    public static IReadOnlyList<string> IsolationDirectories(string runtime) =>
        runtime switch
        {
            Copilot => [CrossRuntimeReviewFiles.CopilotHome, CrossRuntimeReviewFiles.CopilotXdg],
            Opencode => [CrossRuntimeReviewFiles.OpencodeXdg, CrossRuntimeReviewFiles.OpencodeConfigDir],
            _ => [],
        };
}

/// <summary>G834/G842: file names a review request renders and a run produces.</summary>
internal static class CrossRuntimeReviewFiles
{
    public const string Prompt = "prompt.md";
    public const string Schema = "verdict.schema.json";
    public const string Invocation = "invocation.txt";
    public const string RawVerdict = "verdict.raw.json";
    public const string OpencodeReviewerConfig = "opencode-reviewer.json";
    public const string CopilotHome = "copilot-home";
    public const string CopilotXdg = "copilot-xdg";
    public const string OpencodeXdg = "opencode-xdg";
    public const string OpencodeConfigDir = "opencode-config-dir";
    public const string OpencodeExit = "opencode-exit.txt";

    public static readonly IReadOnlyList<string> Rendered = [Prompt, Schema, Invocation];

    public static IReadOnlyList<string> RenderedFor(string runtime) =>
        RenderedFor(runtime, CrossRuntimeReviewRecord.KindImplementation, hasClone: true);

    public static IReadOnlyList<string> RenderedFor(string runtime, string kind, bool hasClone) =>
        runtime switch
        {
            CrossRuntimeReviewRuntimes.Copilot => hasClone || kind != CrossRuntimeReviewRecord.KindDesign
                ? [Prompt, Schema, Invocation, CopilotHome, CopilotXdg]
                : [Prompt, Schema, Invocation, CopilotHome, CopilotXdg, Workspace],
            CrossRuntimeReviewRuntimes.Opencode => hasClone || kind != CrossRuntimeReviewRecord.KindDesign
                ? [Prompt, Schema, Invocation, OpencodeReviewerConfig, OpencodeXdg, OpencodeConfigDir]
                : [Prompt, Schema, Invocation, OpencodeReviewerConfig, OpencodeXdg, OpencodeConfigDir, Workspace],
            _ => Rendered,
        };

    public const string Workspace = "workspace";

    public static bool IsRenderedEntry(string runtime, string name) =>
        RenderedFor(runtime).Contains(name, StringComparer.Ordinal);

    public static bool IsRenderedEntry(string runtime, string kind, bool hasClone, string name) =>
        RenderedFor(runtime, kind, hasClone).Contains(name, StringComparer.Ordinal);
}

/// <summary>G834/G842: every named cause the cross-runtime review surfaces emit.</summary>
internal static class CrossRuntimeReviewCauses
{
    public const string HostRootRequired = "cross-runtime-review-host-root-required";
    public const string PathInvalid = "cross-runtime-review-path-invalid";
    public const string OutDirNotEmpty = "cross-runtime-review-out-dir-not-empty";
    public const string PacketMissing = "cross-runtime-review-packet-missing";
    public const string PacketInvalid = "cross-runtime-review-packet-invalid";
    public const string PacketUnreadable = "cross-runtime-review-packet-unreadable";
    public const string ArgumentInvalid = "cross-runtime-review-argument-invalid";
    public const string TeamUnresolved = "cross-runtime-review-team-unresolved";
    public const string UnitMismatch = "cross-runtime-review-unit-mismatch";
    public const string NotDeclared = "cross-runtime-review-not-declared";
    public const string HeadMismatch = "cross-runtime-review-head-mismatch";
    public const string VerdictInvalid = "cross-runtime-review-verdict-invalid";
    public const string RuntimeInvalid = "cross-runtime-review-runtime-invalid";
    public const string RecordCollision = "cross-runtime-review-record-collision";
    public const string HeadSuperseded = "cross-runtime-review-head-superseded";
    public const string Blocked = "cross-runtime-review-blocked";
    public const string Missing = "cross-runtime-review-missing";
    public const string RereviewMissing = "cross-runtime-review-rereview-missing";
    public const string RecordUnreadable = "cross-runtime-review-record-unreadable";
    public const string HeadRequired = "cross-runtime-review-head-required";
    public const string HeadStale = "cross-runtime-review-head-stale";
    public const string ModelInvalid = "cross-runtime-review-model-invalid";
    public const string ModelRequired = "cross-runtime-review-model-required";
    public const string ModelMismatch = "cross-runtime-review-model-mismatch";
    public const string EffortInvalid = "cross-runtime-review-effort-invalid";
    public const string EffortMismatch = "cross-runtime-review-effort-mismatch";
    public const string OpencodeProviderConfigInvalid = "cross-runtime-review-opencode-provider-config-invalid";
    public const string ExitStatusMissing = "cross-runtime-review-exit-status-missing";
    public const string ExitStatusNonzero = "cross-runtime-review-exit-status-nonzero";
    public const string DigestStale = "cross-runtime-review-digest-stale";
    public const string DigestMismatch = "cross-runtime-review-digest-mismatch";
    public const string DomainMismatch = "cross-runtime-review-domain-mismatch";
    public const string TargetRepoMismatch = "cross-runtime-review-target-repo-mismatch";
    public const string LookupInputChanged = "cross-runtime-review-lookup-input-changed";
}
