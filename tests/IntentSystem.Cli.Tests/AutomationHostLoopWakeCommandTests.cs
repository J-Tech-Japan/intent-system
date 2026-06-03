using System.Text.Json;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

/// <summary>
/// G450: coverage for the one-wake host-loop orchestration command — the four
/// wake-action classes (true-idle / review / publish / blocker), the
/// at-most-one-PR / at-most-one-publish invariant, the stale-cli gate, and the
/// fail-closed <c>--write</c> behavior.
/// </summary>
public sealed class AutomationHostLoopWakeCommandTests : IDisposable
{
    private const string Repo = "J-Tech-Japan/intent-system";

    public AutomationHostLoopWakeCommandTests()
    {
        AutomationHostLoopWakeCommand.NextActionDelegate = null;
        AutomationInstalledCliSurfaceProbe.ProbeRunner = null;
        AutomationInstalledCliSurfaceProbe.ExplicitInstalledCliPathReader = null;
    }

    public void Dispose()
    {
        AutomationHostLoopWakeCommand.NextActionDelegate = null;
        AutomationInstalledCliSurfaceProbe.ProbeRunner = null;
        AutomationInstalledCliSurfaceProbe.ExplicitInstalledCliPathReader = null;
    }

    [Fact]
    public void Execute_TrueIdle_ReportsTrueIdle_NoPlannedWork()
    {
        using var ws = new Workspace();
        ws.StubSurfaceAvailable();
        AutomationHostLoopWakeCommand.NextActionDelegate = CannedNextAction("true-idle", mutationAllowed: false);

        using var writer = new StringWriter();
        var exit = AutomationHostLoopWakeCommand.Execute(ws.Context, ["--repo", Repo, "--format", "json"], writer);

        Assert.Equal(0, exit);
        using var doc = JsonDocument.Parse(writer.ToString());
        Assert.Equal("true-idle", doc.RootElement.GetProperty("wake_action").GetString());
        Assert.Equal(0, doc.RootElement.GetProperty("processed_pr_count").GetInt32());
        Assert.Equal(0, doc.RootElement.GetProperty("processed_issue_publish_count").GetInt32());
        Assert.False(doc.RootElement.GetProperty("write_executed").GetBoolean());
    }

    [Fact]
    public void Execute_ReviewPr_ReportsReviewAction_OnePrPlanned()
    {
        using var ws = new Workspace();
        ws.StubSurfaceAvailable();
        AutomationHostLoopWakeCommand.NextActionDelegate = CannedNextAction(
            HostLoopNextActionAnalyzer.ClassificationReviewPr,
            mutationAllowed: false,
            recommendedCommand: "intent-cli automation host-review-preflight --repo " + Repo);

        using var writer = new StringWriter();
        var exit = AutomationHostLoopWakeCommand.Execute(ws.Context, ["--repo", Repo, "--format", "json"], writer);

        Assert.Equal(0, exit);
        using var doc = JsonDocument.Parse(writer.ToString());
        Assert.Equal("review", doc.RootElement.GetProperty("wake_action").GetString());
        Assert.Equal(1, doc.RootElement.GetProperty("processed_pr_count").GetInt32());
        Assert.Equal(0, doc.RootElement.GetProperty("processed_issue_publish_count").GetInt32());
    }

    [Fact]
    public void Execute_PublishNextIssue_ReportsPublishAction_OnePublishPlanned()
    {
        using var ws = new Workspace();
        ws.StubSurfaceAvailable();
        AutomationHostLoopWakeCommand.NextActionDelegate = CannedNextAction(
            HostLoopNextActionAnalyzer.ClassificationPublishNextIssue,
            mutationAllowed: true,
            candidateExecutionUnit: "G500",
            recommendedCommand: "intent-cli automation issue-publish --issue 0 --write");

        using var writer = new StringWriter();
        var exit = AutomationHostLoopWakeCommand.Execute(ws.Context, ["--repo", Repo, "--format", "json"], writer);

        Assert.Equal(0, exit);
        using var doc = JsonDocument.Parse(writer.ToString());
        Assert.Equal("publish", doc.RootElement.GetProperty("wake_action").GetString());
        Assert.Equal(0, doc.RootElement.GetProperty("processed_pr_count").GetInt32());
        Assert.Equal(1, doc.RootElement.GetProperty("processed_issue_publish_count").GetInt32());
        Assert.Equal("G500", doc.RootElement.GetProperty("candidate_execution_unit").GetString());
    }

    [Fact]
    public void Execute_DirtyHostState_ReportsBlocker()
    {
        using var ws = new Workspace();
        ws.StubSurfaceAvailable();
        AutomationHostLoopWakeCommand.NextActionDelegate = CannedNextAction(
            HostLoopNextActionAnalyzer.ClassificationDirtyHostState,
            mutationAllowed: false);

        using var writer = new StringWriter();
        var exit = AutomationHostLoopWakeCommand.Execute(ws.Context, ["--repo", Repo, "--format", "json"], writer);

        Assert.Equal(0, exit);
        using var doc = JsonDocument.Parse(writer.ToString());
        Assert.Equal("blocker", doc.RootElement.GetProperty("wake_action").GetString());
        Assert.Equal(
            HostLoopNextActionAnalyzer.ClassificationDirtyHostState,
            doc.RootElement.GetProperty("classification").GetString());
    }

    [Fact]
    public void Execute_StaleCliSurface_ReportsStaleCliBlocker_AndDoesNotDelegate()
    {
        using var ws = new Workspace();
        ws.StubSurfaceUnavailable();
        var delegated = false;
        AutomationHostLoopWakeCommand.NextActionDelegate = (_, _, w) =>
        {
            delegated = true;
            w.WriteLine("{}");
            return 0;
        };

        using var writer = new StringWriter();
        var exit = AutomationHostLoopWakeCommand.Execute(ws.Context, ["--repo", Repo, "--format", "json"], writer);

        Assert.Equal(1, exit);
        Assert.False(delegated);
        using var doc = JsonDocument.Parse(writer.ToString());
        Assert.Equal("blocker", doc.RootElement.GetProperty("wake_action").GetString());
        Assert.Equal(
            HostLoopNextActionAnalyzer.ClassificationStaleCli,
            doc.RootElement.GetProperty("classification").GetString());
    }

    [Fact]
    public void Execute_ReadOnly_PerformsNoWrite_NoPendingCommand()
    {
        using var ws = new Workspace();
        ws.StubSurfaceAvailable();
        AutomationHostLoopWakeCommand.NextActionDelegate = CannedNextAction(
            "repair-host-metadata",
            mutationAllowed: true,
            recommendedCommand: "intent-cli automation publish-recovery --repo " + Repo + " --write");

        using var writer = new StringWriter();
        var exit = AutomationHostLoopWakeCommand.Execute(ws.Context, ["--repo", Repo, "--format", "json"], writer);

        Assert.Equal(0, exit);
        using var doc = JsonDocument.Parse(writer.ToString());
        Assert.Equal("read-only", doc.RootElement.GetProperty("mode").GetString());
        Assert.False(doc.RootElement.GetProperty("write_executed").GetBoolean());
        Assert.Equal(JsonValueKind.Null, doc.RootElement.GetProperty("pending_command").ValueKind);
        Assert.Equal(0, doc.RootElement.GetProperty("mutations").GetArrayLength());
    }

    [Fact]
    public void Execute_Write_FailClosed_SurfacesPendingCommand_ButDoesNotExecute()
    {
        using var ws = new Workspace();
        ws.StubSurfaceAvailable();
        const string command = "intent-cli automation publish-recovery --repo " + Repo + " --write";
        AutomationHostLoopWakeCommand.NextActionDelegate = CannedNextAction(
            "repair-host-metadata", mutationAllowed: true, recommendedCommand: command);

        using var writer = new StringWriter();
        var exit = AutomationHostLoopWakeCommand.Execute(ws.Context, ["--repo", Repo, "--write", "--format", "json"], writer);

        Assert.Equal(0, exit);
        using var doc = JsonDocument.Parse(writer.ToString());
        Assert.Equal("write", doc.RootElement.GetProperty("mode").GetString());
        // Fail-closed: the command itself executes nothing; it hands the host
        // the single safe pending command.
        Assert.False(doc.RootElement.GetProperty("write_executed").GetBoolean());
        Assert.Equal(0, doc.RootElement.GetProperty("mutations").GetArrayLength());
        Assert.Equal(command, doc.RootElement.GetProperty("pending_command").GetString());
    }

    [Theory]
    [InlineData("true-idle")]
    [InlineData("review-pr")]
    [InlineData("publish-next-issue")]
    [InlineData("dirty-host-state")]
    public void Execute_NeverExceedsOnePrAndOnePublish(string classification)
    {
        using var ws = new Workspace();
        ws.StubSurfaceAvailable();
        AutomationHostLoopWakeCommand.NextActionDelegate = CannedNextAction(classification, mutationAllowed: false);

        using var writer = new StringWriter();
        AutomationHostLoopWakeCommand.Execute(ws.Context, ["--repo", Repo, "--format", "json"], writer);

        using var doc = JsonDocument.Parse(writer.ToString());
        Assert.True(doc.RootElement.GetProperty("processed_pr_count").GetInt32() <= 1);
        Assert.True(doc.RootElement.GetProperty("processed_issue_publish_count").GetInt32() <= 1);
    }

    // --- helpers -------------------------------------------------------------

    private static Func<CliContext, string[], TextWriter, int> CannedNextAction(
        string classification,
        bool mutationAllowed,
        string? recommendedCommand = null,
        string? candidateExecutionUnit = null)
        => (_, _, writer) =>
        {
            var payload = new
            {
                repo = Repo,
                classification,
                mutation_allowed = mutationAllowed,
                recommended_command = recommendedCommand,
                candidate_execution_unit = candidateExecutionUnit,
                evidence = new[] { $"canned evidence for {classification}" },
                summary = $"canned summary for {classification}",
            };
            writer.WriteLine(JsonSerializer.Serialize(payload));
            return 0;
        };

    private sealed class Workspace : IDisposable
    {
        private readonly string installedCliPath;

        public Workspace()
        {
            RootPath = Directory.CreateTempSubdirectory("host-loop-wake-tests-").FullName;
            Directory.CreateDirectory(Path.Combine(RootPath, ".intent-cli"));
            installedCliPath = Path.Combine(RootPath, ".intent-cli", "installed-cli-stub");
            Context = new CliContext
            {
                RepoRoot = RootPath,
                Config = new CliConfig
                {
                    Project = new ProjectConfig
                    {
                        Domain = "intent-cli",
                        ArtifactRoot = ".intent-cli",
                    },
                },
            };
        }

        public string RootPath { get; }
        public CliContext Context { get; }

        public void StubSurfaceAvailable()
        {
            File.WriteAllText(installedCliPath, "stub");
            AutomationInstalledCliSurfaceProbe.ExplicitInstalledCliPathReader = () => installedCliPath;
            AutomationInstalledCliSurfaceProbe.ProbeRunner = (_, _) =>
                new InstalledCliProbeResult(
                    0,
                    "automation summary host-review-preflight issue-publish pr-transition review-start request-update approved",
                    string.Empty);
        }

        public void StubSurfaceUnavailable()
        {
            // Resolvable path, but the probe reports every surface as
            // not-yet-implemented → the report is deterministically unavailable
            // (independent of any real intent-cli on PATH).
            File.WriteAllText(installedCliPath, "stub");
            AutomationInstalledCliSurfaceProbe.ExplicitInstalledCliPathReader = () => installedCliPath;
            AutomationInstalledCliSurfaceProbe.ProbeRunner = (_, _) =>
                new InstalledCliProbeResult(1, "Command is not yet implemented.", string.Empty);
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
