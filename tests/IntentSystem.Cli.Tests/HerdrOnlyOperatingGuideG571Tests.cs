using System.Text.Json;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

public sealed class HerdrOnlyOperatingGuideG571Tests : IDisposable
{
    private readonly string root = Directory.CreateTempSubdirectory("herdr-guide-g571-").FullName;

    [Fact]
    public void HerdrOnlyMarkdown_RendersTheCompleteOperatingContract_G571()
    {
        var output = Render(herdrOnly: true, format: "markdown");

        foreach (var heading in HerdrOnlyOperatingGuide.Headings)
        {
            Assert.Contains(heading, output, StringComparison.Ordinal);
        }

        Assert.Contains("herdr agent start <logical-role>", output, StringComparison.Ordinal);
        Assert.Contains("logical-role→pane", output, StringComparison.Ordinal);
        Assert.Contains("G556 verified-liveness", output, StringComparison.Ordinal);
        Assert.Contains("result-nonce: <fresh-per-dispatch-nonce>", output, StringComparison.Ordinal);
        Assert.Contains("herdr agent wait <logical-role> --until idle --until done --until blocked --timeout", output, StringComparison.Ordinal);
        Assert.Contains("Composite success is mandatory", output, StringComparison.Ordinal);
        Assert.Contains("State alone and marker alone NEVER mean task success", output, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Approvals are NEVER auto-answered", output, StringComparison.Ordinal);
        Assert.DoesNotContain("ship in G571", output, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void EventsJsonl_UsesTheClarifiedNormativeBoundary_G571()
    {
        var output = Render(herdrOnly: true, format: "markdown");
        var json = JsonDocument.Parse(Render(herdrOnly: true, format: "json")).RootElement;
        var events = json.GetProperty(HerdrOnlyOperatingGuide.JsonProperty).GetProperty("events_jsonl");

        Assert.Contains("<host-repo>/.intent-cli/events/<team>.jsonl", output, StringComparison.Ordinal);
        Assert.Contains("O_APPEND", output, StringComparison.Ordinal);
        Assert.Contains("no embedded newline", output, StringComparison.Ordinal);
        Assert.Contains("leading dot", output, StringComparison.Ordinal);
        Assert.Contains("`/` or `\\`, and any", output, StringComparison.Ordinal);
        Assert.DoesNotContain("`/` or `\\\\`, and any", output, StringComparison.Ordinal);
        Assert.Contains("any `..` sequence", output, StringComparison.Ordinal);
        Assert.Contains("Claude app watcher", output, StringComparison.Ordinal);
        Assert.Contains("Codex CLI", output, StringComparison.Ordinal);
        Assert.Contains("Codex Desktop", output, StringComparison.Ordinal);
        Assert.Contains("one-minute-class", output, StringComparison.Ordinal);
        Assert.Contains("watermark", output, StringComparison.Ordinal);
        Assert.Contains("NEVER an inter-agent bus", output, StringComparison.Ordinal);

        Assert.Equal("<host-repo>/.intent-cli/events/<team>.jsonl", events.GetProperty("path").GetString());
        Assert.Equal("timestamp, team, kind, unit, summary, artifact", events.GetProperty("schema").GetString());
        Assert.Equal(4, events.GetProperty("kinds").GetArrayLength());
        Assert.True(events.GetProperty("readers").TryGetProperty("claude_app", out _));
        Assert.True(events.GetProperty("readers").TryGetProperty("codex_cli", out _));
        Assert.True(events.GetProperty("readers").TryGetProperty("codex_desktop", out _));
    }

    [Fact]
    public void ApprovalBoundary_IsPaneVisibleAndContrastsHeadlessAutoDecline_G571()
    {
        var output = Render(herdrOnly: true, format: "markdown");
        var provisioning = JsonDocument.Parse(Render(herdrOnly: true, format: "json"))
            .RootElement
            .GetProperty(HerdrOnlyOperatingGuide.JsonProperty)
            .GetProperty("provisioning");

        Assert.Contains("Approvals surface visibly in the pane", output, StringComparison.Ordinal);
        Assert.Contains("supervision boundary", output, StringComparison.Ordinal);
        Assert.Contains("agmsg Codex bridge's headless auto-decline", output, StringComparison.Ordinal);
        Assert.Contains("surface visibly in the pane", provisioning.GetProperty("approval_boundary").GetString());
        Assert.Contains("headless auto-decline", provisioning.GetProperty("approval_boundary").GetString());
    }

    [Fact]
    public void ReadyGate_IsSelfContainedAndResidualScopeIsTruthful_G571()
    {
        var section = HerdrOnlyOperatingGuide.RenderMarkdown([]);
        var output = Render(herdrOnly: true, format: "markdown");
        var provisioning = JsonDocument.Parse(Render(herdrOnly: true, format: "json"))
            .RootElement
            .GetProperty(HerdrOnlyOperatingGuide.JsonProperty)
            .GetProperty("provisioning");

        Assert.Contains("After the startup report, wait a settle delay, then re-check", section, StringComparison.Ordinal);
        Assert.Contains("repeat this entire settle-and-re-check sequence", section, StringComparison.Ordinal);
        Assert.DoesNotContain("complete G556", section, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("wait a settle delay, then re-check", provisioning.GetProperty("ready_gate").GetString());
        Assert.Contains("transport-specific examples in them govern only their named transport", output, StringComparison.Ordinal);
        Assert.Contains("the concrete herdr-only counterparts below govern this mode", output, StringComparison.Ordinal);
    }

    [Fact]
    public void SwitchesAndRecoveries_ArePresentInBothDirections_G571()
    {
        var output = Render(herdrOnly: true, format: "markdown");

        Assert.Contains("Modifier-chord injection", output, StringComparison.Ordinal);
        Assert.Contains("Post-reboot dead pty wiring", output, StringComparison.Ordinal);
        Assert.Contains("Long-wait turn death", output, StringComparison.Ordinal);
        Assert.Contains("**agmsg → herdr-only**", output, StringComparison.Ordinal);
        Assert.Contains("**herdr-only → agmsg**", output, StringComparison.Ordinal);
        Assert.Equal(2, Count(output, "As the FINAL canonical step"));
        Assert.Contains("mixed-delivery CONTRACT VIOLATION", output, StringComparison.Ordinal);
    }

    [Fact]
    public void AgmsgMode_DoesNotRenderAnyHerdrOnlyOperatingSurface_G571()
    {
        var markdown = Render(herdrOnly: false, format: "markdown");
        var json = JsonDocument.Parse(Render(herdrOnly: false, format: "json")).RootElement;

        foreach (var heading in HerdrOnlyOperatingGuide.Headings)
        {
            Assert.DoesNotContain(heading, markdown, StringComparison.Ordinal);
        }

        Assert.False(json.TryGetProperty(HerdrOnlyOperatingGuide.JsonProperty, out _));
        Assert.Contains(SessionLayerSwitchChecklist.Heading, markdown, StringComparison.Ordinal);
        Assert.True(json.TryGetProperty(SessionLayerSwitchChecklist.JsonProperty, out _));
        Assert.Contains("## Terminal-workspace provisioning (G549)", markdown, StringComparison.Ordinal);
        Assert.Contains("## agmsg reply contract", markdown, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("en")]
    [InlineData("ja")]
    public void OrchestrationDocs_MirrorTheNormativeContract_G571(string language)
    {
        var path = Path.Combine(RepoVersionPolicySource.RepoRoot(), "docs", language, "12-agent-message-orchestration.md");
        var content = File.ReadAllText(path);

        foreach (var token in new[]
                 {
                     "<host-repo>/.intent-cli/events/<team>.jsonl",
                     "O_APPEND",
                     "timestamp",
                     "completion|blocked|question|escalation",
                     "fresh-per-dispatch-nonce",
                     "workspace.workspace_id",
                     "tab.tab_id",
                     "root_pane.pane_id",
                     "root_pane.cwd",
                     "--until idle --until done --until blocked",
                     "pane read --source recent-unwrapped",
                     "approval/question-paused",
                     "final gate",
                     "one-minute-class",
                     "byte-offset watermark",
                     "agmsg → herdr-only",
                     "herdr-only → agmsg",
                     "pane-visible",
                     "supervision boundary",
                     "headless auto-decline",
                     "settle delay",
                     "settle-and-re-check",
                 })
        {
            Assert.Contains(token, content, StringComparison.Ordinal);
        }

        Assert.Contains(
            language == "en" ? "neither state nor marker" : "state 単独も marker 単独も",
            content,
            StringComparison.Ordinal);
    }

    [Fact]
    public void DispatchMarker_CannotMatchItsOwnPromptEcho_G575()
    {
        var output = Render(herdrOnly: true, format: "markdown");
        var taskBlockStart = output.IndexOf("```text\nTASK <task-id>", StringComparison.Ordinal);
        Assert.True(taskBlockStart >= 0);
        var taskBlockEnd = output.IndexOf("```", taskBlockStart + "```text".Length, StringComparison.Ordinal);
        Assert.True(taskBlockEnd > taskBlockStart);
        var taskBlock = output[taskBlockStart..taskBlockEnd];
        var dispatch = JsonDocument.Parse(Render(herdrOnly: true, format: "json"))
            .RootElement.GetProperty(HerdrOnlyOperatingGuide.JsonProperty).GetProperty("dispatch");

        Assert.Contains("result-prefix: ORCH_RESULT", taskBlock, StringComparison.Ordinal);
        Assert.Contains("result-nonce: <fresh-per-dispatch-nonce>", taskBlock, StringComparison.Ordinal);
        Assert.DoesNotContain("ORCH_RESULT <fresh-per-dispatch-nonce>", taskBlock, StringComparison.Ordinal);
        Assert.Contains("searches existing pane output immediately", output, StringComparison.Ordinal);
        Assert.Contains("falsely match before the agent does any work", output, StringComparison.Ordinal);
        Assert.Contains("never embed the composed wait needle", dispatch.GetProperty("marker_construction").GetString());
        Assert.Contains("falsely match", dispatch.GetProperty("echo_hazard").GetString());
    }

    [Fact]
    public void Completion_RequiresArtifactAndPostWaitPaneInspection_G575()
    {
        var output = Render(herdrOnly: true, format: "markdown");
        var json = JsonDocument.Parse(Render(herdrOnly: true, format: "json"))
            .RootElement.GetProperty(HerdrOnlyOperatingGuide.JsonProperty);
        var waiting = json.GetProperty("waiting");

        Assert.Contains("--until idle --until done --until blocked", output, StringComparison.Ordinal);
        Assert.Contains("After EVERY wait return", output, StringComparison.Ordinal);
        Assert.Contains("herdr pane read --source recent-unwrapped <pane-id>", output, StringComparison.Ordinal);
        Assert.Contains("`idle` can mean approval-paused", output, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("G550 MAY", output, StringComparison.Ordinal);
        Assert.Contains("re-enter the wake and wait again", output, StringComparison.Ordinal);
        Assert.Contains("marker alone NEVER mean task success", output, StringComparison.Ordinal);
        Assert.Contains("named artifact verification is the final gate", output, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("idle can be approval-paused", waiting.GetProperty("post_wait_inspection").GetString());
        Assert.Contains("G550 MAY/escalate", waiting.GetProperty("paused_reentry").GetString());
        Assert.Contains("neither state nor marker alone", waiting.GetProperty("success").GetString());
    }

    [Fact]
    public void WorkspaceCreate_UsesMeasuredHerdr080ResultFields_G605()
    {
        var output = Render(herdrOnly: true, format: "markdown");
        var provisioning = JsonDocument.Parse(Render(herdrOnly: true, format: "json"))
            .RootElement.GetProperty(HerdrOnlyOperatingGuide.JsonProperty).GetProperty("provisioning");

        foreach (var field in new[]
                 {
                     "workspace_created",
                     "workspace.workspace_id",
                     "tab.tab_id",
                     "root_pane.pane_id",
                     "root_pane.cwd",
                 })
        {
            Assert.Contains(field, output, StringComparison.Ordinal);
        }

        Assert.Contains("workspace.workspace_id", provisioning.GetProperty("workspace_result_mapping").GetString());
        Assert.Contains("root_pane.cwd", provisioning.GetProperty("workspace_result_mapping").GetString());
        Assert.Contains("herdr 0.8.0", output, StringComparison.Ordinal);
        Assert.DoesNotContain("0.7.5", output, StringComparison.Ordinal);
    }

    [Fact]
    public void LiveHandoffRecovery_RebaselinesGuidanceWithoutChangingDeliverySemantics_G605()
    {
        var output = Render(herdrOnly: true, format: "markdown");
        var recovery = JsonDocument.Parse(Render(herdrOnly: true, format: "json"))
            .RootElement.GetProperty(HerdrOnlyOperatingGuide.JsonProperty).GetProperty("failure_recovery")
            .EnumerateArray().Select(item => item.GetString()).ToArray();

        Assert.Contains("herdr server live-handoff", output, StringComparison.Ordinal);
        Assert.Contains("EOF as a resubscribe trigger", output, StringComparison.Ordinal);
        Assert.Contains("re-read the pane and re-judge", output, StringComparison.Ordinal);
        Assert.Contains("headless resize/zoom does not restore the PTY", output, StringComparison.Ordinal);
        Assert.Contains("server_not_running", output, StringComparison.Ordinal);
        Assert.Contains("restored agent sessions without waiting for a TUI client", output, StringComparison.Ordinal);
        Assert.Contains("latest stable herdr on macOS/Linux", output, StringComparison.Ordinal);
        Assert.Contains("Windows support is beta", output, StringComparison.Ordinal);
        Assert.Contains("herdr --skill", output, StringComparison.Ordinal);
        Assert.Contains("never replaces intent-cli guide authority", output, StringComparison.Ordinal);
        Assert.Contains(recovery, item => item!.Contains("live-handoff", StringComparison.Ordinal));
    }

    private string Render(bool herdrOnly, string format)
    {
        Directory.CreateDirectory(Path.Combine(root, ".intent-cli"));
        var record = SessionLayerModeStore.ResolvePath(root);
        if (herdrOnly)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(record)!);
            File.WriteAllText(
                record,
                """
                {
                  "schema_version": "1",
                  "entries": [
                    {
                      "domain": "intent-cli",
                      "mode": "herdr-only",
                      "updated_at": "2026-08-01T12:00:00+00:00",
                      "transitions": [
                        { "from": "agmsg", "to": "herdr-only", "at": "2026-08-01T12:00:00+00:00" }
                      ]
                    }
                  ]
                }
                """);
        }
        else if (File.Exists(record))
        {
            File.Delete(record);
        }

        var context = new CliContext
        {
            RepoRoot = root,
            Config = new CliConfig
            {
                Project = new ProjectConfig
                {
                    Domain = "intent-cli",
                    ArtifactRoot = ".intent-cli",
                    WorktreeRoot = ".intent-cli/worktrees",
                },
            },
        };
        using var writer = new StringWriter();
        var exitCode = CommandRouter.Execute(
            ["guide", "orchestrator-thread", "--domain", "intent-cli", "--target-repo", "J-Tech-Japan/intent-system", "--agent", "claude", "--format", format],
            context,
            writer);

        Assert.Equal(0, exitCode);
        return writer.ToString();
    }

    private static int Count(string value, string needle) =>
        (value.Length - value.Replace(needle, string.Empty, StringComparison.Ordinal).Length) / needle.Length;

    public void Dispose()
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
