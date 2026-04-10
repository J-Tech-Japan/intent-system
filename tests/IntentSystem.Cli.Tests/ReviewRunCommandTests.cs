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
        var originalTimestampFactory = ReviewRunCommand.TimestampFactory;
        var originalLauncherFactory = ReviewRunCommand.DirectRunLauncherFactory;

        try
        {
            ReviewRunCommand.TimestampFactory = () => DateTimeOffset.Parse("2026-04-09T10:35:00Z");
            ReviewRunCommand.DirectRunLauncherFactory = () => new FakeDirectRunLauncher(
                "pid:9999",
                "ReviewBot",
                "gpt-5.4-mini",
                "grpc",
                "reviewbot",
                ["launch", "--model", "{model}", "--artifact", "{request_artifact_path}"],
                "grpc transport launched via 'reviewbot' in '/repo' for provider 'ReviewBot'.");

            var exitCode = ReviewRunCommand.Execute(CreateContext(repoRoot), ["G9"], writer);

            Assert.Equal(0, exitCode);
            Assert.Contains("Review request artifact generated for G9", writer.ToString(), StringComparison.Ordinal);
            Assert.Contains("Direct run request artifact: .intent-cli/runtime-runs/G9.request.json", writer.ToString(), StringComparison.Ordinal);
            Assert.Contains("Direct provider: ReviewBot", writer.ToString(), StringComparison.Ordinal);
            Assert.Contains("Direct model: gpt-5.4-mini", writer.ToString(), StringComparison.Ordinal);
            Assert.Contains("Direct transport: grpc", writer.ToString(), StringComparison.Ordinal);
            Assert.Contains("Provider session: pid:9999", writer.ToString(), StringComparison.Ordinal);

            var artifactPath = Path.Combine(repoRoot, ".intent-cli", "reviews", "G9.request.json");
            Assert.True(File.Exists(artifactPath));
            var request = ReviewRequestSerializer.Deserialize(File.ReadAllText(artifactPath));
            Assert.Equal("G9", request.ExecutionUnit);
            Assert.Equal(".intent-cli/issues/G9/review-context.md", request.ReviewContextRef);
            Assert.Equal("https://github.com/J-Tech-Japan/intent-system/pull/45", request.LinkedPr);
            Assert.Equal(
                ["review run command が PR comment 投稿や closeout の責務へ広がっていない"],
                request.DeterministicReviewChecks);
            Assert.Empty(request.AcceptanceCriteria);
            Assert.Equal(
                ["dotnet test IntentSystem.sln", "review run command tests"],
                request.ExpectedEvidence);

            var directRunArtifactPath = Path.Combine(repoRoot, ".intent-cli", "runtime-runs", "G9.request.json");
            Assert.True(File.Exists(directRunArtifactPath));
            var directRunArtifact = DirectRunRequestArtifactJson.Deserialize(File.ReadAllText(directRunArtifactPath));
            Assert.Equal("G9", directRunArtifact.ExecutionUnit);
            Assert.Equal("review", directRunArtifact.EntryKind);
            Assert.Equal(".intent-cli/reviews/G9.request.json", directRunArtifact.UpstreamRequestRef);
            Assert.Equal("ReviewBot", directRunArtifact.Provider);
            Assert.Equal("gpt-5.4-mini", directRunArtifact.Model);
            Assert.Equal("grpc", directRunArtifact.Transport);
            Assert.Equal("pid:9999", directRunArtifact.ProviderSessionId);
        }
        finally
        {
            ReviewRunCommand.TimestampFactory = originalTimestampFactory;
            ReviewRunCommand.DirectRunLauncherFactory = originalLauncherFactory;
        }
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
                },
                DirectRun = new DirectRunConfig
                {
                    ArtifactRoot = ".intent-cli/runtime-runs",
                    Command = "fallback-review-launcher",
                    Args = ["--prompt", "{prompt}"],
                    Review = new DirectRunEntryConfig
                    {
                        Provider = "ReviewBot",
                        Model = "gpt-5.4-mini",
                        Transport = "grpc",
                        Command = "reviewbot",
                        Args = ["launch", "--model", "{model}", "--artifact", "{request_artifact_path}"]
                    }
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
        # Execution Unit

        `{{executionUnit}}`

        # Goal

        `intent-cli review run <execution-unit>` を working command として実装し、
        review context packet と latest linked PR をもとに
        deterministic review request artifact を `.intent-cli/reviews/<execution-unit>.request.json` へ生成できるようにする。

        # Parent References

        - [Intent CLI Surface](/Users/tomohisa/dev/GitHub/MyIntentHost/intents/intent-cli/specs/05-intent-cli-surface.md)
        - [Config And Run Model](/Users/tomohisa/dev/GitHub/MyIntentHost/intents/intent-cli/specs/08-config-and-run-model.md)

        # Deterministic Review Checks

        - review run command が PR comment 投稿や closeout の責務へ広がっていない

        # Expected Evidence

        - dotnet test IntentSystem.sln
        - review run command tests
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

    private sealed class FakeDirectRunLauncher(
        string providerSessionId,
        string provider,
        string model,
        string transport,
        string command,
        IReadOnlyList<string> argsTemplate,
        string transportSummary) : IDirectRunLauncher
    {
        public DirectRunLaunchResult Launch(
            string executionUnit,
            string entryKind,
            string requestArtifactPath,
            string providerArg,
            string modelArg,
            string transportArg,
            string commandArg,
            IReadOnlyList<string> argsTemplateArg,
            DateTimeOffset launchedAt,
            string workingDirectory,
            string absoluteRequestArtifactPath)
        {
            Assert.Equal("G9", executionUnit);
            Assert.Equal("review", entryKind);
            Assert.Equal(".intent-cli/runtime-runs/G9.request.json", requestArtifactPath);
            Assert.Equal(provider, providerArg);
            Assert.Equal(model, modelArg);
            Assert.Equal(transport, transportArg);
            Assert.Equal(command, commandArg);
            Assert.Equal(argsTemplate, argsTemplateArg);
            Assert.EndsWith("/repo", workingDirectory, StringComparison.Ordinal);
            Assert.EndsWith("/.intent-cli/reviews/G9.request.json", absoluteRequestArtifactPath, StringComparison.Ordinal);

            return new DirectRunLaunchResult
            {
                RequestArtifactPath = ".intent-cli/runtime-runs/G9.request.json",
                Provider = providerArg,
                Model = modelArg,
                Transport = transportArg,
                ProviderSessionId = providerSessionId,
                TransportSummary = transportSummary
            };
        }
    }
}
