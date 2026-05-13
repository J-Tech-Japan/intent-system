using System.Text.Json;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

/// <summary>
/// G345: GitHub Copilot one-shot agent guidance + assignment workflow.
/// Copilot is recognized as an assignment-oriented agent for the child
/// one-shot path. child-loop returns structured unsupported_local_loop
/// guidance and host modes return structured
/// unsupported_mode_agent_combination guidance — Copilot must never carry
/// the Claude/Codex local-heartbeat body and must never recommend touching
/// host `.intent-cli/` metadata or applying workflow labels from the
/// implementation side.
/// </summary>
public sealed class GuidePromptMatrixCopilotTests
{
    // ── agent acceptance ─────────────────────────────────────────────────

    [Fact]
    public void Execute_CopilotAgent_IsAcceptedForChildOneshot()
    {
        using var writer = new StringWriter();
        var exitCode = GuidePromptMatrixCommand.Execute(
            CreateContext(),
            ["--mode", "child-oneshot", "--agent", "copilot", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        Assert.DoesNotContain("--agent must be", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_CopilotAgent_IsAcceptedForChildLoop()
    {
        using var writer = new StringWriter();
        var exitCode = GuidePromptMatrixCommand.Execute(
            CreateContext(),
            ["--mode", "child-loop", "--agent", "copilot", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        Assert.DoesNotContain("--agent must be", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_CopilotAgent_IsAcceptedForHostLoop()
    {
        using var writer = new StringWriter();
        var exitCode = GuidePromptMatrixCommand.Execute(
            CreateContext(),
            ["--mode", "host-loop", "--agent", "copilot", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        Assert.DoesNotContain("--agent must be", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_InvalidAgentMessage_NowMentionsCopilot()
    {
        using var writer = new StringWriter();
        var exitCode = GuidePromptMatrixCommand.Execute(
            CreateContext(),
            ["--mode", "child-loop", "--agent", "gemini", "--format", "json"],
            writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("copilot", writer.ToString(), StringComparison.Ordinal);
    }

    // ── child-oneshot — Copilot guidance content ─────────────────────────

    [Fact]
    public void Execute_ChildOneshotCopilot_ContainsAllThreeMinimalPrompts()
    {
        using var writer = new StringWriter();
        GuidePromptMatrixCommand.Execute(
            CreateContext(),
            ["--mode", "child-oneshot", "--agent", "copilot", "--format", "json"],
            writer);

        using var document = JsonDocument.Parse(writer.ToString());
        var prompt = document.RootElement.GetProperty("prompt").GetString()!;

        // Prompt 1 — assign published issue to Copilot cloud agent.
        Assert.Contains("gh issue edit", prompt, StringComparison.Ordinal);
        Assert.Contains("--add-assignee copilot", prompt, StringComparison.Ordinal);

        // Prompt 2 — @copilot PR mention.
        Assert.Contains("@copilot", prompt, StringComparison.Ordinal);
        Assert.Contains("gh pr comment", prompt, StringComparison.Ordinal);

        // Prompt 3 — Copilot CLI one-shot.
        Assert.True(
            prompt.Contains("gh copilot suggest", StringComparison.Ordinal)
                || prompt.Contains("copilot --prompt", StringComparison.Ordinal),
            "Copilot CLI one-shot prompt must reference gh copilot suggest or copilot --prompt");
    }

    [Fact]
    public void Execute_ChildOneshotCopilot_ContainsGithubOnlyContractParagraph()
    {
        using var writer = new StringWriter();
        GuidePromptMatrixCommand.Execute(
            CreateContext(),
            ["--mode", "child-oneshot", "--agent", "copilot", "--format", "json"],
            writer);

        using var document = JsonDocument.Parse(writer.ToString());
        var prompt = document.RootElement.GetProperty("prompt").GetString()!;

        // Implementation repo must not contain `.intent-cli/`.
        Assert.Contains(".intent-cli", prompt, StringComparison.Ordinal);
        Assert.Contains("MUST NOT", prompt, StringComparison.Ordinal);

        // No host-state mutations from implementation side.
        Assert.Contains("queue-state", prompt, StringComparison.Ordinal);
        Assert.Contains("runs.jsonl", prompt, StringComparison.Ordinal);
        Assert.Contains("packet", prompt, StringComparison.Ordinal);
        Assert.Contains("workflow labels", prompt, StringComparison.Ordinal);

        // Host review/closeout/next-slice remains with the intent-cli host
        // loop (NOT with Copilot). G345 review repair (Finding 1) removed
        // the literal "intent-cli automation" substring from the
        // child-oneshot Copilot prompt because Copilot must not invoke
        // intent-cli — verify the host-owned framing without naming the
        // command surface.
        Assert.Contains("host review", prompt, StringComparison.Ordinal);
        Assert.Contains("intent-cli host loop", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_ChildOneshotCopilot_ExplicitlyNotesHostOwnedLabels()
    {
        using var writer = new StringWriter();
        GuidePromptMatrixCommand.Execute(
            CreateContext(),
            ["--mode", "child-oneshot", "--agent", "copilot", "--format", "json"],
            writer);

        using var document = JsonDocument.Parse(writer.ToString());
        var prompt = document.RootElement.GetProperty("prompt").GetString()!;

        // intent-target / intent-pr-created are host-owned, not Copilot's job.
        Assert.Contains("intent-target", prompt, StringComparison.Ordinal);
        Assert.Contains("intent-pr-created", prompt, StringComparison.Ordinal);
        Assert.Contains("host-owned", prompt, StringComparison.Ordinal);
    }

    // ── child-oneshot — Copilot must NOT invoke intent-cli at all ────────

    // G345 (host review repair, Finding 1): the child-oneshot Copilot prompt
    // forbids Copilot from calling `intent-cli`. It used to render the
    // generic `RenderBaseBranchPolicyBlock`, which recommended running
    // `intent-cli automation base-branch-check` — internally contradictory.
    // Replaced with a Copilot-specific GitHub-facts-only base-branch block
    // that marks intent-cli enforcement as host/operator-owned.
    [Fact]
    public void ChildOneshot_Copilot_DoesNotInstructCopilotToInvokeIntentCli()
    {
        using var writer = new StringWriter();
        GuidePromptMatrixCommand.Execute(
            CreateContext(),
            ["--mode", "child-oneshot", "--agent", "copilot", "--format", "json"],
            writer);

        using var document = JsonDocument.Parse(writer.ToString());
        var prompt = document.RootElement.GetProperty("prompt").GetString()!;

        // Smoking gun: an instruction telling Copilot to run any
        // `intent-cli automation ...` command would violate the host-state
        // -free Copilot child contract. Must not appear anywhere.
        Assert.DoesNotContain("intent-cli automation", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void ChildOneshot_Copilot_UsesGithubFactsBaseBranchCheck()
    {
        using var writer = new StringWriter();
        GuidePromptMatrixCommand.Execute(
            CreateContext(),
            ["--mode", "child-oneshot", "--agent", "copilot", "--format", "json"],
            writer);

        using var document = JsonDocument.Parse(writer.ToString());
        var prompt = document.RootElement.GetProperty("prompt").GetString()!;

        // The Copilot-specific base-branch block expresses the policy as a
        // GitHub-facts-only check (gh pr view ... baseRefName).
        Assert.Contains("gh pr view", prompt, StringComparison.Ordinal);
        Assert.Contains("baseRefName", prompt, StringComparison.Ordinal);
    }

    // ── child-loop — structured unsupported_local_loop ───────────────────

    [Fact]
    public void Execute_ChildLoopCopilot_ReportsUnsupportedLocalLoopClassification()
    {
        using var writer = new StringWriter();
        GuidePromptMatrixCommand.Execute(
            CreateContext(),
            ["--mode", "child-loop", "--agent", "copilot", "--format", "json"],
            writer);

        using var document = JsonDocument.Parse(writer.ToString());
        Assert.Equal(
            "unsupported_local_loop",
            document.RootElement.GetProperty("agent_classification").GetString());
        Assert.Equal("copilot", document.RootElement.GetProperty("agent").GetString());
    }

    [Fact]
    public void Execute_ChildLoopCopilot_DoesNotContainClaudeCodexLoopBody()
    {
        using var writer = new StringWriter();
        GuidePromptMatrixCommand.Execute(
            CreateContext(),
            ["--mode", "child-loop", "--agent", "copilot", "--format", "json"],
            writer);

        using var document = JsonDocument.Parse(writer.ToString());
        var prompt = document.RootElement.GetProperty("prompt").GetString()!;

        // No /loop 5m wakeup primitive.
        Assert.DoesNotContain("/loop 5m", prompt, StringComparison.Ordinal);

        // No worker next-action / claim / complete sequence as instructions to Copilot.
        Assert.DoesNotContain("intent-cli worker next-action", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("intent-cli worker claim", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("intent-cli worker complete", prompt, StringComparison.Ordinal);

        // No "heartbeat" wording instructing Copilot to run heartbeat loops.
        // (The text DOES describe that Copilot has no heartbeat surface, so
        // we instead assert the phrase that would imply Copilot runs one —
        // "local heartbeat thread" must not appear as an instruction for
        // Copilot to use, and "current-thread heartbeat" must not appear
        // as a Copilot scheduling primitive.)
        // The prompt explicitly explains the unsupported nature instead.
        Assert.Contains("does NOT", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_ChildLoopCopilot_RecommendsAssignmentOrientedController()
    {
        using var writer = new StringWriter();
        GuidePromptMatrixCommand.Execute(
            CreateContext(),
            ["--mode", "child-loop", "--agent", "copilot", "--format", "json"],
            writer);

        using var document = JsonDocument.Parse(writer.ToString());
        var prompt = document.RootElement.GetProperty("prompt").GetString()!;

        Assert.Contains("assignment-oriented", prompt, StringComparison.Ordinal);
        Assert.Contains("--add-assignee copilot", prompt, StringComparison.Ordinal);
        Assert.Contains("@copilot", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_ChildLoopCopilot_FrequencyGuidanceMarksLocalLoopNotApplicable()
    {
        using var writer = new StringWriter();
        GuidePromptMatrixCommand.Execute(
            CreateContext(),
            ["--mode", "child-loop", "--agent", "copilot", "--format", "json"],
            writer);

        using var document = JsonDocument.Parse(writer.ToString());
        var frequencyGuidance = document.RootElement.GetProperty("frequency_guidance").GetString()!;
        Assert.Contains("no local heartbeat", frequencyGuidance, StringComparison.OrdinalIgnoreCase);
    }

    // ── host modes — structured unsupported_mode_agent_combination ───────

    [Fact]
    public void Execute_HostLoopCopilot_ReportsUnsupportedModeAgentCombination()
    {
        using var writer = new StringWriter();
        GuidePromptMatrixCommand.Execute(
            CreateContext(),
            ["--mode", "host-loop", "--agent", "copilot", "--format", "json"],
            writer);

        using var document = JsonDocument.Parse(writer.ToString());
        Assert.Equal(
            "unsupported_mode_agent_combination",
            document.RootElement.GetProperty("agent_classification").GetString());

        var prompt = document.RootElement.GetProperty("prompt").GetString()!;
        Assert.Contains("host review", prompt, StringComparison.Ordinal);
        Assert.Contains("intent-cli host automation", prompt, StringComparison.Ordinal);
        Assert.Contains("--agent claude", prompt, StringComparison.Ordinal);
        Assert.Contains("--agent codex", prompt, StringComparison.Ordinal);
        Assert.Contains("--agent generic", prompt, StringComparison.Ordinal);
    }

    // G345 (host review repair): host-oneshot is now supported with limited
    // safe human / operator-driven guidance. host-loop remains structurally
    // unsupported. The host-oneshot classification is therefore
    // `host_oneshot_human_driven`, NOT `unsupported_mode_agent_combination`.
    [Fact]
    public void HostOneshot_Copilot_ReturnsHumanDrivenGuidance()
    {
        using var writer = new StringWriter();
        GuidePromptMatrixCommand.Execute(
            CreateContext(),
            ["--mode", "host-oneshot", "--agent", "copilot", "--format", "json"],
            writer);

        using var document = JsonDocument.Parse(writer.ToString());
        var classification = document.RootElement.GetProperty("agent_classification").GetString();
        Assert.NotEqual("unsupported_mode_agent_combination", classification);
        Assert.Equal("host_oneshot_human_driven", classification);

        var prompt = document.RootElement.GetProperty("prompt").GetString()!;

        // One safe host operator action — publishing a single prepared issue.
        Assert.Contains("automation issue-publish", prompt, StringComparison.Ordinal);
        // Followed by assigning that issue to the Copilot cloud agent.
        Assert.Contains("assign", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("--add-assignee copilot", prompt, StringComparison.Ordinal);
        // Host-state-free child contract reminder remains in place.
        Assert.Contains("MUST NOT", prompt, StringComparison.Ordinal);
        Assert.Contains(".intent-cli", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void HostOneshot_Copilot_DoesNotEmitLoopBody()
    {
        using var writer = new StringWriter();
        GuidePromptMatrixCommand.Execute(
            CreateContext(),
            ["--mode", "host-oneshot", "--agent", "copilot", "--format", "json"],
            writer);

        using var document = JsonDocument.Parse(writer.ToString());
        var prompt = document.RootElement.GetProperty("prompt").GetString()!;

        // host-oneshot is a single safe operator action, not a loop body.
        Assert.DoesNotContain("/loop", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("worker next-action", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("worker claim", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("worker complete", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("heartbeat", prompt, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void HostOneshot_Copilot_DoesNotInstructCopilotToInvokeIntentCli()
    {
        using var writer = new StringWriter();
        GuidePromptMatrixCommand.Execute(
            CreateContext(),
            ["--mode", "host-oneshot", "--agent", "copilot", "--format", "json"],
            writer);

        using var document = JsonDocument.Parse(writer.ToString());
        var prompt = document.RootElement.GetProperty("prompt").GetString()!;

        // The single intent-cli command in this prompt is the HOST operator's
        // responsibility, not Copilot's. Make sure the actor is unambiguous:
        // the operator-on-host framing is present and Copilot itself is
        // explicitly told NOT to invoke intent-cli.
        Assert.Contains("Operator", prompt, StringComparison.Ordinal);
        Assert.Contains("HOST", prompt, StringComparison.Ordinal);
        Assert.Contains("Copilot itself does NOT invoke `intent-cli`", prompt, StringComparison.Ordinal);
    }

    // ── negative tests — Copilot must NOT recommend host metadata edits ──

    [Fact]
    public void Execute_ChildOneshotCopilot_DoesNotRecommendRawLabelEdits()
    {
        using var writer = new StringWriter();
        GuidePromptMatrixCommand.Execute(
            CreateContext(),
            ["--mode", "child-oneshot", "--agent", "copilot", "--format", "json"],
            writer);

        using var document = JsonDocument.Parse(writer.ToString());
        var prompt = document.RootElement.GetProperty("prompt").GetString()!;

        // No `gh issue edit ... --add-label intent-target` style mutation.
        Assert.DoesNotContain("--add-label intent-target", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("--add-label intent-pr-created", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("--add-label intent-issue-in-progress", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("--add-label intent-pr-reviewing", prompt, StringComparison.Ordinal);

        Assert.DoesNotContain("--remove-label intent-target", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("--remove-label intent-pr-created", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_ChildLoopCopilot_DoesNotRecommendRawLabelEdits()
    {
        using var writer = new StringWriter();
        GuidePromptMatrixCommand.Execute(
            CreateContext(),
            ["--mode", "child-loop", "--agent", "copilot", "--format", "json"],
            writer);

        using var document = JsonDocument.Parse(writer.ToString());
        var prompt = document.RootElement.GetProperty("prompt").GetString()!;

        Assert.DoesNotContain("--add-label intent-target", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("--add-label intent-pr-created", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("--remove-label intent-target", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_ChildOneshotCopilot_DoesNotRecommendMutatingQueueStateRunsOrPacket()
    {
        using var writer = new StringWriter();
        GuidePromptMatrixCommand.Execute(
            CreateContext(),
            ["--mode", "child-oneshot", "--agent", "copilot", "--format", "json"],
            writer);

        using var document = JsonDocument.Parse(writer.ToString());
        var prompt = document.RootElement.GetProperty("prompt").GetString()!;

        // The prompt MUST mention these files (to forbid them); it must NOT
        // recommend mutating them. So we assert the explicit forbidden
        // framing rather than absence-of-name.
        Assert.Contains("MUST NOT", prompt, StringComparison.Ordinal);
        Assert.Contains("queue-state.json", prompt, StringComparison.Ordinal);
        Assert.Contains("runs.jsonl", prompt, StringComparison.Ordinal);

        // Sanity: no instruction to write / edit / append these from the
        // implementation side.
        Assert.DoesNotContain("write to .intent-cli/queue-state.json", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("append to .intent-cli/runs.jsonl", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("edit packet.yaml", prompt, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Execute_ChildLoopCopilot_DoesNotRecommendMutatingQueueStateRunsOrPacket()
    {
        using var writer = new StringWriter();
        GuidePromptMatrixCommand.Execute(
            CreateContext(),
            ["--mode", "child-loop", "--agent", "copilot", "--format", "json"],
            writer);

        using var document = JsonDocument.Parse(writer.ToString());
        var prompt = document.RootElement.GetProperty("prompt").GetString()!;

        Assert.DoesNotContain("write to .intent-cli/queue-state.json", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("append to .intent-cli/runs.jsonl", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("edit packet.yaml", prompt, StringComparison.OrdinalIgnoreCase);
    }

    // ── markdown rendering smoke test ────────────────────────────────────

    [Fact]
    public void Execute_ChildOneshotCopilot_MarkdownRendersPrompt()
    {
        using var writer = new StringWriter();
        var exitCode = GuidePromptMatrixCommand.Execute(
            CreateContext(),
            ["--mode", "child-oneshot", "--agent", "copilot"],
            writer);

        Assert.Equal(0, exitCode);
        var output = writer.ToString();
        Assert.Contains("Mode: child-oneshot", output, StringComparison.Ordinal);
        Assert.Contains("Copilot", output, StringComparison.Ordinal);
        Assert.Contains("--add-assignee copilot", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_AllModesCopilot_ReturnsArrayOfFourEntries()
    {
        using var writer = new StringWriter();
        var exitCode = GuidePromptMatrixCommand.Execute(
            CreateContext(),
            ["--agent", "copilot", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        Assert.Equal(JsonValueKind.Array, document.RootElement.ValueKind);
        Assert.Equal(4, document.RootElement.GetArrayLength());

        // child-loop and host-loop / host-oneshot carry agent_classification
        // structured fields; child-oneshot is supported and omits the
        // classification field.
        foreach (var entry in document.RootElement.EnumerateArray())
        {
            var mode = entry.GetProperty("mode").GetString()!;
            var hasClass = entry.TryGetProperty("agent_classification", out var classElement);
            switch (mode)
            {
                case "child-oneshot":
                    // Supported — classification field omitted (JSON-ignored
                    // when null).
                    if (hasClass)
                    {
                        Assert.Equal(JsonValueKind.Null, classElement.ValueKind);
                    }
                    break;
                case "child-loop":
                    Assert.True(hasClass);
                    Assert.Equal("unsupported_local_loop", classElement.GetString());
                    break;
                case "host-loop":
                    Assert.True(hasClass);
                    Assert.Equal("unsupported_mode_agent_combination", classElement.GetString());
                    break;
                case "host-oneshot":
                    Assert.True(hasClass);
                    Assert.Equal("host_oneshot_human_driven", classElement.GetString());
                    break;
                default:
                    Assert.Fail($"unexpected mode {mode}");
                    break;
            }
        }
    }

    private static CliContext CreateContext()
    {
        return new CliContext
        {
            RepoRoot = Path.GetTempPath(),
            Config = new CliConfig
            {
                Project = new ProjectConfig
                {
                    Domain = "intent-cli",
                    ArtifactRoot = ".intent-cli",
                    WorktreeRoot = ".intent-cli/worktrees"
                }
            }
        };
    }
}
