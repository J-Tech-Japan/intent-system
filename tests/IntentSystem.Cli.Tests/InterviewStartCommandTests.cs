using IntentSystem.ConceptIntake.Models;
using IntentSystem.ConceptIntake.Serialization;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

public sealed class InterviewStartCommandTests
{
    [Fact]
    public void Execute_GivenOpenInterviewItems_RendersNextBlockingQuestion()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        var questionPath = tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "interviews", "auth", "iq-1.json"),
            InterviewQueueSerializer.Serialize(CreateItem("iq-1", "blocking", InterviewQueueItemStatus.Open, createdAt: "2026-04-13T08:00:00Z")));
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "interviews", "auth", "iq-2.json"),
            InterviewQueueSerializer.Serialize(CreateItem("iq-2", "nonblocking", InterviewQueueItemStatus.Open, createdAt: "2026-04-13T07:00:00Z")));
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "interviews", "auth", "iq-3.json"),
            InterviewQueueSerializer.Serialize(CreateAppliedItem("iq-3", "auth")));
        using var writer = new StringWriter();

        var originalQuestion = File.ReadAllText(questionPath);
        var exitCode = InterviewStartCommand.Execute(CreateContext(repoRoot), ["auth"], writer);

        Assert.Equal(0, exitCode);
        var output = writer.ToString();
        Assert.Contains("Next interview question:", output, StringComparison.Ordinal);
        Assert.Contains("Domain: auth", output, StringComparison.Ordinal);
        Assert.Contains("Question: Question for iq-1", output, StringComparison.Ordinal);
        Assert.Contains("Reason: Explore unknown area.", output, StringComparison.Ordinal);
        Assert.Contains("Affects: auth-oauth2", output, StringComparison.Ordinal);
        Assert.Contains("Blocking mode: blocking", output, StringComparison.Ordinal);
        Assert.Contains("Return paths: intents/intent-cli/intent-tree/means/auth-oauth2.md", output, StringComparison.Ordinal);
        Assert.Contains("Question id: iq-1", output, StringComparison.Ordinal);
        Assert.Equal(originalQuestion, File.ReadAllText(questionPath));
    }

    [Fact]
    public void Execute_GivenNoInterviewDirectory_ReturnsDeterministicNoOpenResult()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        using var writer = new StringWriter();

        var exitCode = InterviewStartCommand.Execute(CreateContext(repoRoot), ["auth"], writer);

        Assert.Equal(0, exitCode);
        Assert.Contains("No open interview questions found for domain 'auth'.", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_GivenOnlyAnsweredItems_ReturnsDeterministicNoOpenResult()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "interviews", "auth", "iq-1.json"),
            InterviewQueueSerializer.Serialize(CreateAppliedItem("iq-1", "auth")));
        using var writer = new StringWriter();

        var exitCode = InterviewStartCommand.Execute(CreateContext(repoRoot), ["auth"], writer);

        Assert.Equal(0, exitCode);
        Assert.Contains("No open interview questions found for domain 'auth'.", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_GivenMissingDomainArgument_ReturnsExitCodeOne()
    {
        using var writer = new StringWriter();

        var exitCode = InterviewStartCommand.Execute(CreateContext("/tmp/intent-system"), [], writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("requires a domain", writer.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Execute_GivenArtifactDomainMismatch_ReturnsExitCodeOne()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "interviews", "auth", "iq-1.json"),
            InterviewQueueSerializer.Serialize(CreateItem("iq-1", "blocking", InterviewQueueItemStatus.Open, domainSlug: "billing")));
        using var writer = new StringWriter();

        var exitCode = InterviewStartCommand.Execute(CreateContext(repoRoot), ["auth"], writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("must match requested domain 'auth'", writer.ToString(), StringComparison.Ordinal);
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

    private static InterviewQueueItem CreateItem(
        string questionId,
        string blockingOrNonblocking,
        InterviewQueueItemStatus status,
        string domainSlug = "auth",
        string createdAt = "2026-04-13T06:00:00Z")
    {
        return new InterviewQueueItem
        {
            DomainSlug = domainSlug,
            SourceConceptRef = $"intents/intent-cli/concepts/{domainSlug}-oauth2.md",
            QuestionId = questionId,
            QuestionText = $"Question for {questionId}",
            Reason = "Explore unknown area.",
            Affects = ["auth-oauth2"],
            BlockingOrNonblocking = blockingOrNonblocking,
            Status = status,
            ReturnToIntentPaths = ["intents/intent-cli/intent-tree/means/auth-oauth2.md"],
            CreatedAt = DateTimeOffset.Parse(createdAt),
            Answer = null
        };
    }

    private static InterviewQueueItem CreateAppliedItem(string questionId, string domainSlug)
    {
        return CreateItem(questionId, "blocking", InterviewQueueItemStatus.Applied, domainSlug) with
        {
            Answer = "Use OAuth2 with PKCE.",
            AnsweredAt = DateTimeOffset.Parse("2026-04-13T06:30:00Z"),
            RecommendedUpdates = ["Update auth strategy"]
        };
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        private readonly string rootPath = Directory.CreateTempSubdirectory("intent-cli-interview-start-tests-").FullName;

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
