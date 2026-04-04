using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;
using IntentSystem.Review.Serialization;
using IntentSystem.Supervisor.Models;
using IntentSystem.Supervisor.Serialization;

namespace IntentSystem.Cli.Tests;

public sealed class ReviewRunCommandTests
{
    [Fact]
    public void Execute_GivenQueueItemReviewContextAndRunLog_WritesReviewRequestArtifact()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "queue-state.json"),
            QueueStateSerializer.Serialize(CreateQueueState()));
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "issues", "G9", "review-context.md"),
            CreateReviewContextMarkdown());
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "runs.jsonl"),
            CreateRunLog());
        using var writer = new StringWriter();

        var exitCode = ReviewRunCommand.Execute(CreateContext(repoRoot), ["G9"], writer);

        Assert.Equal(0, exitCode);
        Assert.Contains("Review request artifact generated for G9", writer.ToString(), StringComparison.Ordinal);

        var artifactPath = Path.Combine(repoRoot, ".intent-cli", "reviews", "G9.request.json");
        Assert.True(File.Exists(artifactPath));
        var request = ReviewRequestSerializer.Deserialize(File.ReadAllText(artifactPath));
        Assert.Equal("G9", request.ExecutionUnit);
        Assert.Equal(".intent-cli/issues/G9/review-context.md", request.ReviewContextRef);
        Assert.Equal("https://github.com/J-Tech-Japan/intent-system/pull/45", request.LinkedPr);
        Assert.Equal(["input paths stay canonical"], request.DeterministicReviewChecks);
        Assert.Equal(["review request artifact is generated"], request.AcceptanceCriteria);
        Assert.Equal(["dotnet test IntentSystem.sln"], request.ExpectedEvidence);
    }

    [Fact]
    public void Execute_GivenMissingExecutionUnit_ReturnsExitCodeOne()
    {
        using var writer = new StringWriter();

        var exitCode = ReviewRunCommand.Execute(CreateContext("/tmp/intent-system"), [], writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("requires an execution unit", writer.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Execute_GivenMissingReviewContext_ReturnsExitCodeOne()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "queue-state.json"),
            QueueStateSerializer.Serialize(CreateQueueState()));
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "runs.jsonl"),
            CreateRunLog());
        using var writer = new StringWriter();

        var exitCode = ReviewRunCommand.Execute(CreateContext(repoRoot), ["G9"], writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("Review context artifact was not found", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_GivenMissingRunLog_ReturnsExitCodeOne()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "queue-state.json"),
            QueueStateSerializer.Serialize(CreateQueueState()));
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "issues", "G9", "review-context.md"),
            CreateReviewContextMarkdown());
        using var writer = new StringWriter();

        var exitCode = ReviewRunCommand.Execute(CreateContext(repoRoot), ["G9"], writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("Run log was not found", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_GivenReviewContextMismatch_ReturnsExitCodeOne()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "queue-state.json"),
            QueueStateSerializer.Serialize(CreateQueueState()));
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "issues", "G9", "review-context.md"),
            CreateReviewContextMarkdown("G8"));
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "runs.jsonl"),
            CreateRunLog());
        using var writer = new StringWriter();

        var exitCode = ReviewRunCommand.Execute(CreateContext(repoRoot), ["G9"], writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("must match queue item execution unit", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_GivenMissingLinkedPr_ReturnsExitCodeOne()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "queue-state.json"),
            QueueStateSerializer.Serialize(CreateQueueState()));
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "issues", "G9", "review-context.md"),
            CreateReviewContextMarkdown());
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "runs.jsonl"),
            """{"ts":"2026-04-03T10:00:00Z","execution_unit":"G9","event":"queued","by":"intent-cli"}""" + Environment.NewLine);
        using var writer = new StringWriter();

        var exitCode = ReviewRunCommand.Execute(CreateContext(repoRoot), ["G9"], writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("No linked PR found", writer.ToString(), StringComparison.Ordinal);
    }

    private static CliContext CreateContext(string repoRoot)
    {
        return new CliContext
        {
            RepoRoot = repoRoot,
            Config = new CliConfig
            {
                Project = new ProjectConfig
                {
                    Domain = "intent-system",
                    WorkflowEngine = "intent-cli",
                    ArtifactRoot = ".intent-cli"
                }
            }
        };
    }

    private static QueueState CreateQueueState()
    {
        return new QueueState
        {
            SchemaVersion = "1",
            UpdatedAt = DateTimeOffset.Parse("2026-04-03T10:12:34Z"),
            Items =
            [
                new QueueItem
                {
                    ExecutionUnit = "G9",
                    Title = "Review run command",
                    State = QueueItemState.Review,
                    Dependencies = ["G7"],
                    BlockedBy = [],
                    ClarificationReturnPath = "intents/intent-cli/clarifications/open.md",
                    PacketPaths = new PacketPaths
                    {
                        Implementation = ".intent-cli/issues/G9/implementation.md",
                        ReviewContext = ".intent-cli/issues/G9/review-context.md",
                        Yaml = ".intent-cli/issues/G9/packet.yaml"
                    },
                    WorkerRole = "coder",
                    ReviewRole = "reviewer",
                    Priority = "high"
                }
            ]
        };
    }

    private static string CreateReviewContextMarkdown(string executionUnit = "G9")
    {
        return $$"""
        # Review Context

        - **execution-unit**: `{{executionUnit}}`

        ## Acceptance Criteria

        - review request artifact is generated

        ## Deterministic Review Checks

        - input paths stay canonical

        ## Expected Evidence

        - dotnet test IntentSystem.sln
        """;
    }

    private static string CreateRunLog()
    {
        return """
        {"ts":"2026-04-03T10:00:00Z","execution_unit":"G9","event":"review-started","by":"intent-cli","linked_pr":"https://github.com/J-Tech-Japan/intent-system/pull/44"}
        {"ts":"2026-04-03T10:10:00Z","execution_unit":"A1","event":"review-started","by":"intent-cli","linked_pr":"https://github.com/J-Tech-Japan/intent-system/pull/12"}
        {"ts":"2026-04-03T10:20:00Z","execution_unit":"G9","event":"review-started","by":"intent-cli","linked_pr":"https://github.com/J-Tech-Japan/intent-system/pull/45"}
        """ + Environment.NewLine;
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        private readonly string rootPath = Directory.CreateTempSubdirectory("intent-cli-review-run-tests-").FullName;

        public string CreateDirectory(string relativePath)
        {
            var fullPath = Path.Combine(rootPath, relativePath);
            Directory.CreateDirectory(fullPath);
            return fullPath;
        }

        public string CreateFile(string relativePath, string contents)
        {
            var fullPath = Path.Combine(rootPath, relativePath);
            var directoryPath = Path.GetDirectoryName(fullPath)
                ?? throw new InvalidOperationException("Temporary file path did not contain a directory.");

            Directory.CreateDirectory(directoryPath);
            File.WriteAllText(fullPath, contents);
            return fullPath;
        }

        public void Dispose()
        {
            if (Directory.Exists(rootPath))
            {
                Directory.Delete(rootPath, recursive: true);
            }
        }
    }
}
