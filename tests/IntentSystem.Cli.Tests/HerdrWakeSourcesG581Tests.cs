using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

public sealed class HerdrWakeSourcesG581Tests : IDisposable
{
    // G685/G686/G690/G698/G699/G700/G707/G708/G719 extend the shared orchestrator guidance; keep the snapshot
    // assertion explicit so a future wake-source or route change remains
    // intentional.
    private const string G594AgmsgGuideSha256 =
        "4357D08439FCC3CCEB2060E0FABF8FDA3C3BBDCB91C9012E04EC4FD88503A75E";

    private readonly string root = Directory.CreateTempSubdirectory("herdr-wake-g581-").FullName;

    [Fact]
    public void HerdrOnlyGuide_PublishesTheNormativeSecondWakeSource_G581()
    {
        var markdown = Render(herdrOnly: true, format: "markdown");
        var operations = JsonDocument.Parse(Render(herdrOnly: true, format: "json"))
            .RootElement.GetProperty(HerdrOnlyOperatingGuide.JsonProperty);
        var wakeSources = operations.GetProperty("wake_sources");
        var stateChange = wakeSources.GetProperty("state_change");

        Assert.Contains("## Herdr-only wake sources", markdown, StringComparison.Ordinal);
        Assert.Contains("normative SECOND wake source", markdown, StringComparison.Ordinal);
        Assert.Contains("events.subscribe", markdown, StringComparison.Ordinal);
        Assert.Contains(
            "{\"method\":\"events.subscribe\",\"params\":{\"subscriptions\":[{\"type\":\"pane.agent_status_changed\",\"pane_id\":\"<resolved-pane-id>\"}]} }",
            markdown,
            StringComparison.Ordinal);
        Assert.Contains("one subscription entry per watched pane", markdown, StringComparison.Ordinal);
        Assert.Contains("logical-role→pane mapping", markdown, StringComparison.Ordinal);
        Assert.Contains("NEVER hard-code pane ids", markdown, StringComparison.Ordinal);
        Assert.Equal("herdr 0.8.0", stateChange.GetProperty("measured_version").GetString());
        Assert.Equal("events.subscribe", stateChange.GetProperty("method").GetString());
        Assert.Contains("One subscription entry per watched pane", stateChange.GetProperty("cardinality").GetString());
    }

    [Fact]
    public void StateChangeWake_IsSettledDedupedAndOutcomeNeutral_G581()
    {
        var markdown = Render(herdrOnly: true, format: "markdown");
        var wakeSources = JsonDocument.Parse(Render(herdrOnly: true, format: "json"))
            .RootElement.GetProperty(HerdrOnlyOperatingGuide.JsonProperty).GetProperty("wake_sources");

        Assert.Contains(
            "{\"event\":\"pane.agent_status_changed\",\"data\":{\"agent\":\"<agent>\",\"agent_status\":\"<working|idle|done|blocked|unknown>\",\"pane_id\":\"<resolved-pane-id>\",\"workspace_id\":\"<workspace-id>\"} }",
            markdown,
            StringComparison.Ordinal);
        Assert.Contains("transitions from `working` to a settled state (`idle`, `done`, or `blocked`)", markdown, StringComparison.Ordinal);
        Assert.Contains("Apply a settle delay", markdown, StringComparison.Ordinal);
        Assert.Contains("per-role dedupe", markdown, StringComparison.Ordinal);
        Assert.Contains("A newly observed `working` state re-arms that role", markdown, StringComparison.Ordinal);
        Assert.Contains("means only that something happened, never that a task succeeded", markdown, StringComparison.Ordinal);

        Assert.Contains("working-to-settled", wakeSources.GetProperty("state_change").GetProperty("transition").GetString());
        Assert.Contains("settle delay and per-role dedupe", wakeSources.GetProperty("state_change").GetProperty("settle_and_dedupe").GetString());
        Assert.Equal(
            "A state change means only that something happened, never that a task succeeded.",
            wakeSources.GetProperty("semantic_boundary").GetString());
    }

    [Fact]
    public void EveryWake_RequiresTheCompositeCheckAndKeepsThePeriodicLastNet_G581()
    {
        var markdown = Render(herdrOnly: true, format: "markdown");
        var wakeSources = JsonDocument.Parse(Render(herdrOnly: true, format: "json"))
            .RootElement.GetProperty(HerdrOnlyOperatingGuide.JsonProperty).GetProperty("wake_sources");

        Assert.Contains("After EVERY wake from either source", markdown, StringComparison.Ordinal);
        Assert.Contains("current herdr state and pending approval/question", markdown, StringComparison.Ordinal);
        Assert.Contains("exact fresh-nonce completion marker and status", markdown, StringComparison.Ordinal);
        Assert.Contains("named verified artifact", markdown, StringComparison.Ordinal);
        Assert.Contains("fresh canonical intent-cli/GitHub facts", markdown, StringComparison.Ordinal);
        Assert.Contains("notify report is the richest signal but depends on worker cooperation", markdown, StringComparison.Ordinal);
        Assert.Contains("state change depends only on herdr observation but carries no outcome", markdown, StringComparison.Ordinal);
        Assert.Contains("stalled-work ...` check remains the last net", markdown, StringComparison.Ordinal);
        Assert.Contains("Consult installed herdr help/schema", wakeSources.GetProperty("version_rule").GetString());
        Assert.Contains("approval/question pauses", wakeSources.GetProperty("composite_check").GetString());
    }

    [Theory]
    [InlineData("en")]
    [InlineData("ja")]
    public void OrchestrationDocs_MirrorTheExactWakeContract_G581(string language)
    {
        var path = Path.Combine(
            RepoVersionPolicySource.RepoRoot(),
            "docs",
            language,
            "12-agent-message-orchestration.md");
        var content = File.ReadAllText(path);

        foreach (var token in new[]
                 {
                     "SECOND wake source",
                     "events.subscribe",
                     "pane.agent_status_changed",
                     "subscription entry",
                     "<resolved-pane-id>",
                     "\"agent\"",
                     "\"agent_status\"",
                     "\"pane_id\"",
                     "\"workspace_id\"",
                     "working",
                     "idle",
                     "done",
                     "blocked",
                     "settle delay",
                     "per-role dedupe",
                     "completion marker",
                     "artifact",
                     "canonical intent-cli/GitHub facts",
                     "approval/question",
                     "stalled-work",
                     "herdr 0.8.0",
                     "installed herdr help/schema",
                 })
        {
            Assert.Contains(token, content, StringComparison.Ordinal);
        }

        Assert.Contains(
            "{\"method\":\"events.subscribe\",\"params\":{\"subscriptions\":[{\"type\":\"pane.agent_status_changed\",\"pane_id\":\"<resolved-pane-id>\"}]}}",
            content,
            StringComparison.Ordinal);
        Assert.Contains(
            "{\"event\":\"pane.agent_status_changed\",\"data\":{\"agent\":\"<agent>\",\"agent_status\":\"<working|idle|done|blocked|unknown>\",\"pane_id\":\"<resolved-pane-id>\",\"workspace_id\":\"<workspace-id>\"}}",
            content,
            StringComparison.Ordinal);
    }

    [Fact]
    public void AgmsgGuide_OutsideG582SwitchChecklist_IncludesG594SharedPreflight_G594()
    {
        var markdown = Render(herdrOnly: false, format: "markdown");
        var withoutG582Checklist = WithoutSection(markdown, SessionLayerSwitchChecklist.Heading);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(withoutG582Checklist)));

        Assert.True(
            string.Equals(G594AgmsgGuideSha256, hash, StringComparison.Ordinal),
            $"agmsg guide hash changed: {hash}");
        Assert.Contains("shared preflight verdict", markdown, StringComparison.Ordinal);
        Assert.DoesNotContain("## Herdr-only wake sources", markdown, StringComparison.Ordinal);
        Assert.DoesNotContain("pane.agent_status_changed", markdown, StringComparison.Ordinal);
    }

    private static string WithoutSection(string markdown, string heading)
    {
        var start = markdown.IndexOf(heading, StringComparison.Ordinal);
        Assert.True(start >= 0, $"missing section {heading}");
        var end = markdown.IndexOf("\n## ", start + heading.Length, StringComparison.Ordinal);
        Assert.True(end > start, $"section {heading} must be followed by another section");
        return markdown[..start] + markdown[(end + 1)..];
    }

    private string Render(bool herdrOnly, string format)
    {
        Directory.CreateDirectory(Path.Combine(root, ".intent-cli"));
        if (herdrOnly)
        {
            File.WriteAllText(
                SessionLayerModeStore.ResolvePath(root),
                """
                {
                  "schema_version": "1",
                  "entries": [
                    {
                      "domain": "intent-cli",
                      "mode": "herdr-only",
                      "updated_at": "2026-08-02T12:00:00+00:00",
                      "transitions": [
                        { "from": "agmsg", "to": "herdr-only", "at": "2026-08-02T12:00:00+00:00" }
                      ]
                    }
                  ]
                }
                """);
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

    public void Dispose()
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
