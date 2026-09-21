namespace IntentSystem.Cli.Tests;

/// <summary>G842: literal pinned contract texts; only temp paths are templated.</summary>
internal static class G842PinnedContractTexts
{
    internal const string CodexBuilderCommand =
        "codex exec -s workspace-write -C <isolated-clone> [-m <model>] - < <task-file>";

    internal const string ClaudeBuilderCommand =
        "cd <isolated-clone> && claude -p --permission-mode acceptEdits --allowedTools Bash --disallowedTools 'Bash(git push:*)' 'Bash(gh:*)' [--model <model>] < <task-file>";

    internal const string CursorBuilderCommand =
        "cursor-agent -p --force --sandbox enabled --trust --workspace <isolated-clone> [--model <model>] --output-format json \"$(cat <task-file>)\"";

    internal const string CopilotBuilderCommand =
        "COPILOT_ALLOW_ALL= COPILOT_HOME=<fresh-empty-copilot-home> XDG_CONFIG_HOME=<fresh-empty-xdg-dir> copilot -C <isolated-clone> --model <model> [--reasoning-effort <effort>] --allow-all-tools --deny-tool 'shell(git push)' --deny-tool 'shell(gh)' --disable-builtin-mcps --no-ask-user --stream off --output-format json < <task-file>";

    internal const string OpencodeBuilderCommand =
        "OPENCODE_PERMISSION= OPENCODE_CONFIG_CONTENT= XDG_CONFIG_HOME=<fresh-empty-dir> OPENCODE_CONFIG_DIR=<fresh-empty-dir-2> OPENCODE_DISABLE_PROJECT_CONFIG=1 OPENCODE_CONFIG=<builder-config-file> opencode run --pure --dir <isolated-clone> -m <provider/model> [--variant <effort>] --auto --format json < <task-file>";

    internal const string CodexBuilderEnforcement = "not measured.";

    internal const string ClaudeBuilderEnforcement =
        "a file-tool write outside the clone and `git push` refused; a shell write outside not refused.";

    internal const string CursorBuilderEnforcement = "nothing enforced.";

    internal const string CopilotBuilderEnforcement =
        "The model is required. An inside write succeeded; a file-tool write and a literal shell redirect outside refused by path verification (not a sandbox); `git push` refused; other outside forms unmeasured.";

    internal const string OpencodeBuilderEnforcement =
        "The model is required. An inside write and a commit succeeded; `git push` refused, even through `task` and with a permissive clone and global config; no plugin or MCP server ran; outside writes unmeasured in this form.";

    internal const string OpencodeBuilderConfigJson =
        """
        {
          "$schema": "https://opencode.ai/config.json",
          "permission": {
            "external_directory": "deny",
            "bash": {
              "*": "allow",
              "git push*": "deny",
              "gh *": "deny"
            }
          }
        }
        """ + "\n";

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
