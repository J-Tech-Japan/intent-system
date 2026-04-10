using IntentSystem.ConceptIntake.Models;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

[Collection(RunSubmitCommandCollection.Name)]
public sealed class InterviewAnswerCommandTests
{
    [Fact]
    public void Execute_GivenInteractiveAnswer_PersistsMultilineAnsweredArtifactAndRendersSummary()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        var artifactPath = tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "interviews", "auth", "iq-1.yaml"),
            InterviewArtifactYaml.Serialize(CreateOpenItem("iq-1", "blocking", createdAt: "2026-04-13T08:00:00Z")));
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "interviews", "auth", "iq-1.md"),
            "# Interview Question");
        using var writer = new StringWriter();
        var originalTimestampFactory = InterviewAnswerCommand.TimestampFactory;
        var originalInputReaderFactory = InterviewAnswerCommand.InputReaderFactory;

        try
        {
            InterviewAnswerCommand.TimestampFactory = () => DateTimeOffset.Parse("2026-04-13T09:15:00Z");
            InterviewAnswerCommand.InputReaderFactory = () => new StringReader(
                "Use OAuth2 with PKCE." + Environment.NewLine +
                "Prefer GitHub first." + Environment.NewLine);

            var exitCode = InterviewAnswerCommand.Execute(CreateContext(repoRoot), ["auth"], writer);

            Assert.Equal(0, exitCode);
            var output = writer.ToString();
            Assert.Contains("Interview answered for domain 'auth'.", output, StringComparison.Ordinal);
            Assert.Contains("Question id: iq-1", output, StringComparison.Ordinal);
            Assert.Contains("Status: Answered", output, StringComparison.Ordinal);
            Assert.Contains("Recommended updates: none", output, StringComparison.Ordinal);
            Assert.Contains("Return paths: intents/intent-cli/intent-tree/means/auth-oauth2.md", output, StringComparison.Ordinal);

            var updatedItem = InterviewArtifactYaml.Deserialize(File.ReadAllText(artifactPath));
            Assert.Equal(InterviewQueueItemStatus.Answered, updatedItem.Status);
            Assert.Equal(
                "Use OAuth2 with PKCE." + Environment.NewLine + "Prefer GitHub first.",
                updatedItem.Answer);
            Assert.Equal(DateTimeOffset.Parse("2026-04-13T09:15:00Z"), updatedItem.AnsweredAt);
            Assert.Null(updatedItem.RecommendedUpdates);
        }
        finally
        {
            InterviewAnswerCommand.TimestampFactory = originalTimestampFactory;
            InterviewAnswerCommand.InputReaderFactory = originalInputReaderFactory;
        }
    }

    [Fact]
    public void Execute_GivenAnswerFile_PersistsAnswerAndRendersRecommendedUpdates()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        var artifactPath = tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "interviews", "auth", "iq-1.yaml"),
            InterviewArtifactYaml.Serialize(CreateOpenItem("iq-1", "blocking", recommendedUpdates: ["Update auth strategy", "Clarify provider list"])));
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "interviews", "auth", "iq-1.md"),
            "# Interview Question");
        tempDirectory.CreateFile(
            Path.Combine("repo", "answers", "auth.txt"),
            "Use GitHub OAuth first.");
        using var writer = new StringWriter();
        var originalTimestampFactory = InterviewAnswerCommand.TimestampFactory;

        try
        {
            InterviewAnswerCommand.TimestampFactory = () => DateTimeOffset.Parse("2026-04-13T10:00:00Z");

            var exitCode = InterviewAnswerCommand.Execute(
                CreateContext(repoRoot),
                ["auth", "--from-file", "answers/auth.txt"],
                writer);

            Assert.Equal(0, exitCode);
            var output = writer.ToString();
            Assert.Contains("Recommended updates:", output, StringComparison.Ordinal);
            Assert.Contains("- Update auth strategy", output, StringComparison.Ordinal);
            Assert.Contains("- Clarify provider list", output, StringComparison.Ordinal);

            var updatedItem = InterviewArtifactYaml.Deserialize(File.ReadAllText(artifactPath));
            Assert.Equal(InterviewQueueItemStatus.Answered, updatedItem.Status);
            Assert.Equal("Use GitHub OAuth first.", updatedItem.Answer);
            Assert.Equal(["Update auth strategy", "Clarify provider list"], updatedItem.RecommendedUpdates);
        }
        finally
        {
            InterviewAnswerCommand.TimestampFactory = originalTimestampFactory;
        }
    }

    [Fact]
    public void Execute_GivenNoOpenQuestion_ReturnsDeterministicNoOpenResult()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "interviews", "auth", "iq-1.yaml"),
            InterviewArtifactYaml.Serialize(CreateAnsweredItem("iq-1")));
        using var writer = new StringWriter();

        var exitCode = InterviewAnswerCommand.Execute(CreateContext(repoRoot), ["auth"], writer);

        Assert.Equal(0, exitCode);
        Assert.Contains("No open interview questions found for domain 'auth'.", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_GivenMissingAnswerFile_ReturnsExitCodeOne()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "interviews", "auth", "iq-1.yaml"),
            InterviewArtifactYaml.Serialize(CreateOpenItem("iq-1", "blocking")));
        using var writer = new StringWriter();

        var exitCode = InterviewAnswerCommand.Execute(
            CreateContext(repoRoot),
            ["auth", "--from-file", "answers/missing.txt"],
            writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("Interview answer file was not found", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_GivenMissingDomainArgument_ReturnsExitCodeOne()
    {
        using var writer = new StringWriter();

        var exitCode = InterviewAnswerCommand.Execute(CreateContext("/tmp/intent-system"), [], writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("requires a domain", writer.ToString(), StringComparison.OrdinalIgnoreCase);
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
                    ArtifactRoot = ".intent-cli"
                }
            }
        };
    }

    private static InterviewQueueItem CreateOpenItem(
        string questionId,
        string blockingOrNonblocking,
        string createdAt = "2026-04-13T08:00:00Z",
        IReadOnlyList<string>? recommendedUpdates = null)
    {
        return new InterviewQueueItem
        {
            DomainSlug = "auth",
            SourceConceptRef = "intents/intent-cli/concepts/auth-oauth2.md",
            QuestionId = questionId,
            QuestionText = $"Question for {questionId}",
            Reason = "Explore unknown area.",
            Affects = ["auth-oauth2"],
            BlockingOrNonblocking = blockingOrNonblocking,
            Status = InterviewQueueItemStatus.Open,
            ReturnToIntentPaths = ["intents/intent-cli/intent-tree/means/auth-oauth2.md"],
            CreatedAt = DateTimeOffset.Parse(createdAt),
            Answer = null,
            RecommendedUpdates = recommendedUpdates
        };
    }

    private static InterviewQueueItem CreateAnsweredItem(string questionId)
    {
        return CreateOpenItem(questionId, "blocking") with
        {
            Status = InterviewQueueItemStatus.Answered,
            Answer = "Use OAuth2 with PKCE.",
            AnsweredAt = DateTimeOffset.Parse("2026-04-13T08:30:00Z"),
            RecommendedUpdates = ["Update auth strategy"]
        };
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        private readonly string rootPath = Directory.CreateTempSubdirectory("intent-cli-interview-answer-tests-").FullName;

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
