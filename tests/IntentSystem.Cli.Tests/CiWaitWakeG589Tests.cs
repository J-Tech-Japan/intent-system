using System.Text.Json;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

/// <summary>
/// G589: the CI wait instruction must name a real re-check producer in every
/// rendered session-layer mode. A terminal exact head is a new wake signal,
/// while pending remains quiet and non-escalating.
/// </summary>
public sealed class CiWaitWakeG589Tests
{
    [Fact]
    public void BothSessionModes_NameAConcreteProducer_AndNeverPromiseAnUnscheduledNextWake()
    {
        using var workspace = new GuideWorkspace();

        var agmsg = workspace.Render(SessionLayerMode.Agmsg);
        var agmsgCi = SectionFrom(agmsg, "## CI wait state");
        Assert.Contains("TIMER-LOOP re-check producer", agmsgCi, StringComparison.Ordinal);
        Assert.Contains("configured recurring timer", agmsgCi, StringComparison.Ordinal);
        Assert.Contains("gh pr checks <pr> --repo J-Tech-Japan/intent-system --watch", agmsgCi,
            StringComparison.Ordinal);

        var herdr = workspace.Render(SessionLayerMode.HerdrOnly);
        var herdrCi = SectionFrom(herdr, "## CI wait state");
        Assert.Contains("HERDR-ONLY re-check producer", herdrCi, StringComparison.Ordinal);
        Assert.Contains("explicitly arm an exact-head CI-completion watch", herdrCi, StringComparison.Ordinal);
        Assert.Contains("gh pr checks <pr> --repo J-Tech-Japan/intent-system --watch", herdrCi,
            StringComparison.Ordinal);
        Assert.Contains("logical-role mapping", herdrCi, StringComparison.Ordinal);
        Assert.Contains("never hard-code a pane ID", herdrCi, StringComparison.Ordinal);
        Assert.Contains("intent-cli does not launch or manage", herdrCi, StringComparison.Ordinal);

        foreach (var section in new[] { agmsgCi, herdrCi })
        {
            Assert.Contains("pending CI", section, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("NEVER triggers", section, StringComparison.Ordinal);
            Assert.DoesNotContain("re-check on the next wake", section, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("wait-and-recheck next wake", section, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void PendingDependencyUsesNamedProducer_AndTerminalEndIsALegitimateWakeSignal()
    {
        using var workspace = new GuideWorkspace();

        foreach (var mode in new[] { SessionLayerMode.Agmsg, SessionLayerMode.HerdrOnly })
        {
            var output = workspace.Render(mode);
            var dependency = SectionFrom(output, "## Dependency planning");
            Assert.Contains("named mode-specific re-check producer", dependency, StringComparison.Ordinal);
            Assert.DoesNotContain("wait and re-check on the next wake", dependency,
                StringComparison.OrdinalIgnoreCase);

            var escalation = SectionFrom(output, "## Design-thread escalation filter");
            Assert.Contains("pending checks are an active wait state", escalation, StringComparison.Ordinal);
            Assert.Contains("end of the CI wait is a legitimate orchestration wake signal", escalation,
                StringComparison.Ordinal);
            Assert.Contains("classified as green or failed", escalation, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void JsonCarriesTheSameModeSpecificProducerContract()
    {
        using var workspace = new GuideWorkspace();

        using var agmsg = workspace.RenderJson(SessionLayerMode.Agmsg);
        Assert.Contains("TIMER-LOOP", agmsg.RootElement.GetProperty("ci_wait_state")
            .GetProperty("recheck_producer").GetString(), StringComparison.Ordinal);

        using var herdr = workspace.RenderJson(SessionLayerMode.HerdrOnly);
        var producer = herdr.RootElement.GetProperty("ci_wait_state")
            .GetProperty("recheck_producer").GetString();
        Assert.Contains("HERDR-ONLY", producer, StringComparison.Ordinal);
        Assert.Contains("gh pr checks", producer, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("en")]
    [InlineData("ja")]
    public void DeveloperDocsPinKindsEvidenceReadOnlyBoundaryAndBothWakeProducers(string language)
    {
        var path = Path.Combine(
            RepoVersionPolicySource.RepoRoot(),
            "docs",
            language,
            "12-agent-message-orchestration.md");
        var docs = File.ReadAllText(path);

        Assert.Contains("ci-pending", docs, StringComparison.Ordinal);
        Assert.Contains("ci-all-green-not-transitioned", docs, StringComparison.Ordinal);
        Assert.Contains("ci-failed-not-transitioned", docs, StringComparison.Ordinal);
        Assert.Contains("pr_head_sha", docs, StringComparison.Ordinal);
        Assert.Contains("dedupe_key", docs, StringComparison.Ordinal);
        Assert.Contains("read-only", docs, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("timer-loop", docs, StringComparison.Ordinal);
        Assert.Contains("gh pr checks <pr> --repo <owner/repo> --watch", docs, StringComparison.Ordinal);
        Assert.Contains("logical-role mapping", docs, StringComparison.Ordinal);
        Assert.Contains(language == "en" ? "configured recurring timer" : "設定済みの定期タイマー", docs,
            StringComparison.Ordinal);
        Assert.Contains(language == "en" ? "intent-cli does not" : "intent-cli はこの background process を起動も管理もしません",
            docs, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("wait and re-check next wake", docs, StringComparison.OrdinalIgnoreCase);
    }

    private static string SectionFrom(string output, string heading)
    {
        var start = output.IndexOf(heading, StringComparison.Ordinal);
        Assert.True(start >= 0, $"missing section heading: {heading}");
        var next = output.IndexOf("\n## ", start + heading.Length, StringComparison.Ordinal);
        return next < 0 ? output[start..] : output[start..next];
    }

    private sealed class GuideWorkspace : IDisposable
    {
        private const string Domain = "intent-cli";
        private const string Team = "intent-cli-dev";
        private const string Repo = "J-Tech-Japan/intent-system";

        public GuideWorkspace()
        {
            RootPath = Directory.CreateTempSubdirectory("g589-guide-").FullName;
            Context = new CliContext
            {
                RepoRoot = RootPath,
                Config = new CliConfig
                {
                    Project = new ProjectConfig
                    {
                        Domain = Domain,
                        ArtifactRoot = ".intent-cli",
                        WorktreeRoot = ".intent-cli/worktrees",
                    },
                },
            };
        }

        private string RootPath { get; }

        private CliContext Context { get; }

        public string Render(string mode)
        {
            SetMode(mode);
            using var writer = new StringWriter();
            var exitCode = GuideOrchestratorThreadCommand.Execute(
                Context,
                ["--domain", Domain, "--target-repo", Repo, "--agent", "claude", "--team", Team,
                    "--format", "markdown"],
                writer);
            Assert.Equal(0, exitCode);
            return writer.ToString();
        }

        public JsonDocument RenderJson(string mode)
        {
            SetMode(mode);
            using var writer = new StringWriter();
            var exitCode = GuideOrchestratorThreadCommand.Execute(
                Context,
                ["--domain", Domain, "--target-repo", Repo, "--agent", "claude", "--team", Team,
                    "--format", "json"],
                writer);
            Assert.Equal(0, exitCode);
            return JsonDocument.Parse(writer.ToString());
        }

        private void SetMode(string mode)
        {
            var recordPath = SessionLayerModeStore.ResolvePath(RootPath);
            if (File.Exists(recordPath))
            {
                File.Delete(recordPath);
            }

            using var writer = new StringWriter();
            var exitCode = SessionLayerCommand.ExecuteSet(
                Context,
                ["--domain", Domain, "--team", Team, "--mode", mode, "--write", "--format", "json"],
                writer);
            Assert.True(exitCode == 0, writer.ToString());
        }

        public void Dispose()
        {
            if (Directory.Exists(RootPath))
            {
                Directory.Delete(RootPath, recursive: true);
            }
        }
    }
}
