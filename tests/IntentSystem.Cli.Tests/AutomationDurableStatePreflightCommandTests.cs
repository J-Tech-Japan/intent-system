using System.Text.Json;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

public sealed class AutomationDurableStatePreflightCommandTests : IDisposable
{
    public AutomationDurableStatePreflightCommandTests()
    {
        AutomationDurableStatePreflightCommand.ProbeFactory = null;
    }

    public void Dispose()
    {
        AutomationDurableStatePreflightCommand.ProbeFactory = null;
    }

    [Fact]
    public void Execute_VerifiedCommitReady_ReturnsExitZero_AndRecommendedCommitMessage()
    {
        AutomationDurableStatePreflightCommand.ProbeFactory = _ => new DurableStatePreflightProbe
        {
            DirtyPaths = new[]
            {
                new DurableStateDirtyPath
                {
                    Path = ".intent-cli/queue-state.json",
                    IsDeleted = false,
                    QueueStateDelta = new QueueStateForwardDeltaResult
                    {
                        Classification = QueueStateForwardDeltaAnalyzer.ClassificationForwardOnly,
                        Summary = "added linked_pr=`https://github.com/o/r/pull/551` on `SKS-G215`",
                        Changes = new[]
                        {
                            new QueueStateForwardChange
                            {
                                ExecutionUnit = "SKS-G215",
                                Kind = QueueStateForwardChangeKind.AddedLinkedPr,
                                LinkedPrUrl = "https://github.com/o/r/pull/551",
                            },
                        },
                    },
                },
            },
        };

        using var workspace = new DurableStateWorkspace();
        using var writer = new StringWriter();

        var exitCode = AutomationDurableStatePreflightCommand.Execute(
            workspace.Context,
            new[] { "--format", "json" },
            writer);

        Assert.Equal(0, exitCode);
        using var doc = JsonDocument.Parse(writer.ToString());
        Assert.Equal(
            DurableStatePreflightAnalyzer.ClassificationVerifiedCommitReady,
            doc.RootElement.GetProperty("classification").GetString());
        var commitMessage = doc.RootElement.GetProperty("recommended_commit_message").GetString();
        Assert.NotNull(commitMessage);
        Assert.Contains("G312", commitMessage!, StringComparison.Ordinal);
        Assert.Contains(".intent-cli/queue-state.json", commitMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_NeedsOperatorReview_ReturnsExitOne()
    {
        AutomationDurableStatePreflightCommand.ProbeFactory = _ => new DurableStatePreflightProbe
        {
            DirtyPaths = new[]
            {
                new DurableStateDirtyPath
                {
                    Path = ".intent-cli/queue-state.json",
                    IsDeleted = false,
                    QueueStateDelta = new QueueStateForwardDeltaResult
                    {
                        Classification = QueueStateForwardDeltaAnalyzer.ClassificationNeedsOperatorReview,
                        Summary = "title changed",
                        Changes = Array.Empty<QueueStateForwardChange>(),
                    },
                },
            },
        };

        using var workspace = new DurableStateWorkspace();
        using var writer = new StringWriter();

        var exitCode = AutomationDurableStatePreflightCommand.Execute(
            workspace.Context,
            new[] { "--format", "json" },
            writer);

        Assert.Equal(1, exitCode);
        using var doc = JsonDocument.Parse(writer.ToString());
        Assert.Equal(
            DurableStatePreflightAnalyzer.ClassificationNeedsOperatorReview,
            doc.RootElement.GetProperty("classification").GetString());
    }

    [Fact]
    public void Execute_UnsafeDurableState_ReturnsExitOne()
    {
        AutomationDurableStatePreflightCommand.ProbeFactory = _ => new DurableStatePreflightProbe
        {
            DirtyPaths = new[]
            {
                new DurableStateDirtyPath
                {
                    Path = "intents/intent-cli/intent-tree/00-map.md",
                    IsDeleted = false,
                },
            },
        };

        using var workspace = new DurableStateWorkspace();
        using var writer = new StringWriter();

        var exitCode = AutomationDurableStatePreflightCommand.Execute(
            workspace.Context,
            new[] { "--format", "json" },
            writer);

        Assert.Equal(1, exitCode);
        using var doc = JsonDocument.Parse(writer.ToString());
        Assert.Equal(
            DurableStatePreflightAnalyzer.ClassificationUnsafe,
            doc.RootElement.GetProperty("classification").GetString());
    }

    [Fact]
    public void Execute_MarkdownDefault_RendersHeaderAndSections()
    {
        AutomationDurableStatePreflightCommand.ProbeFactory = _ => new DurableStatePreflightProbe
        {
            DirtyPaths = new[]
            {
                new DurableStateDirtyPath
                {
                    Path = ".intent-cli/runs.jsonl",
                    IsDeleted = false,
                    RunsJsonlDelta = new RunsJsonlAppendOnlyResult
                    {
                        Classification = RunsJsonlAppendOnlyAnalyzer.ClassificationAppendOnly,
                        Summary = "runs.jsonl is append-only with 2 new event(s).",
                        AppendedEventCount = 2,
                    },
                },
            },
        };

        using var workspace = new DurableStateWorkspace();
        using var writer = new StringWriter();

        var exitCode = AutomationDurableStatePreflightCommand.Execute(
            workspace.Context,
            Array.Empty<string>(),
            writer);

        Assert.Equal(0, exitCode);
        var output = writer.ToString();
        Assert.Contains("# automation durable-state-preflight (G312)", output, StringComparison.Ordinal);
        Assert.Contains("verified-commit-ready", output, StringComparison.Ordinal);
        Assert.Contains("Recommended commit message", output, StringComparison.Ordinal);
        Assert.Contains("```", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_RejectsUnknownArgument()
    {
        using var workspace = new DurableStateWorkspace();
        using var writer = new StringWriter();

        var exitCode = AutomationDurableStatePreflightCommand.Execute(
            workspace.Context,
            new[] { "--unknown" },
            writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("Unknown argument", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_AcceptsDomainAndRepoFlagsButIgnoresThem()
    {
        // Host-loop guidance passes --domain / --repo for parity with
        // other automation commands; this command must accept (and ignore)
        // them rather than error out.
        AutomationDurableStatePreflightCommand.ProbeFactory = _ => new DurableStatePreflightProbe
        {
            DirtyPaths = Array.Empty<DurableStateDirtyPath>(),
        };

        using var workspace = new DurableStateWorkspace();
        using var writer = new StringWriter();

        var exitCode = AutomationDurableStatePreflightCommand.Execute(
            workspace.Context,
            new[]
            {
                "--domain", "intent-cli",
                "--repo", "J-Tech-Japan/intent-system",
                "--format", "json",
            },
            writer);

        // Empty bundle classifies as needs-operator-review per the analyzer.
        Assert.Equal(1, exitCode);
        using var doc = JsonDocument.Parse(writer.ToString());
        Assert.Equal(
            DurableStatePreflightAnalyzer.ClassificationNeedsOperatorReview,
            doc.RootElement.GetProperty("classification").GetString());
    }

    private sealed class DurableStateWorkspace : IDisposable
    {
        public DurableStateWorkspace()
        {
            RepoRoot = Directory.CreateTempSubdirectory("durable-state-preflight-tests-").FullName;
            Directory.CreateDirectory(Path.Combine(RepoRoot, ".intent-cli"));
            Context = new CliContext
            {
                RepoRoot = RepoRoot,
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
        }

        public string RepoRoot { get; }

        public CliContext Context { get; }

        public void Dispose()
        {
            if (Directory.Exists(RepoRoot)) Directory.Delete(RepoRoot, recursive: true);
        }
    }
}
