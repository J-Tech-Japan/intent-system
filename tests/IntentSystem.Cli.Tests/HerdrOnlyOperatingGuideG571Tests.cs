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
        Assert.Contains("ORCH_RESULT <task-id> <status> <artifact>", output, StringComparison.Ordinal);
        Assert.Contains("herdr agent wait <logical-role> --until done --until blocked --timeout", output, StringComparison.Ordinal);
        Assert.Contains("Composite success is mandatory", output, StringComparison.Ordinal);
        Assert.Contains("state alone NEVER means task success", output, StringComparison.OrdinalIgnoreCase);
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
                     "ORCH_RESULT <task-id> <status> <artifact>",
                     "one-minute-class",
                     "byte-offset watermark",
                     "agmsg → herdr-only",
                     "herdr-only → agmsg",
                 })
        {
            Assert.Contains(token, content, StringComparison.Ordinal);
        }
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
