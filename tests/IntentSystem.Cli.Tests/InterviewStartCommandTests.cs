using IntentSystem.ConceptIntake.Models;
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
            Path.Combine("repo", ".intent-cli", "interviews", "auth", "iq-1.yaml"),
            CreateInterviewArtifactYaml(CreateItem("iq-1", "blocking", InterviewQueueItemStatus.Open, createdAt: "2026-04-13T08:00:00Z")));
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "interviews", "auth", "iq-1.md"),
            "# Interview Question");
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "interviews", "auth", "iq-2.yaml"),
            CreateInterviewArtifactYaml(CreateItem("iq-2", "nonblocking", InterviewQueueItemStatus.Open, createdAt: "2026-04-13T07:00:00Z")));
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "interviews", "auth", "iq-2.md"),
            "# Interview Question");
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "interviews", "auth", "iq-3.yaml"),
            CreateInterviewArtifactYaml(CreateAppliedItem("iq-3", "auth")));
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "interviews", "auth", "iq-3.md"),
            "# Interview Question");
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
            Path.Combine("repo", ".intent-cli", "interviews", "auth", "iq-1.yaml"),
            CreateInterviewArtifactYaml(CreateAppliedItem("iq-1", "auth")));
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "interviews", "auth", "iq-1.md"),
            "# Interview Question");
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
            Path.Combine("repo", ".intent-cli", "interviews", "auth", "iq-1.yaml"),
            CreateInterviewArtifactYaml(CreateItem("iq-1", "blocking", InterviewQueueItemStatus.Open, domainSlug: "billing")));
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "interviews", "auth", "iq-1.md"),
            "# Interview Question");
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

    private static string CreateInterviewArtifactYaml(InterviewQueueItem item)
    {
        var lines = new List<string>
        {
            "artifact_kind: interview",
            $"domain_slug: {item.DomainSlug}",
            $"source_concept_ref: {Quote(item.SourceConceptRef)}",
            $"question_id: {item.QuestionId}",
            $"question_text: {Quote(item.QuestionText)}",
            $"reason: {Quote(item.Reason)}",
            "affects:"
        };

        lines.AddRange(item.Affects.Select(affect => $"  - {Quote(affect)}"));
        lines.Add($"blocking_or_nonblocking: {item.BlockingOrNonblocking}");
        lines.Add($"status: {FormatStatus(item.Status)}");
        lines.Add("return_to_intent_paths:");
        lines.AddRange(item.ReturnToIntentPaths.Select(path => $"  - {Quote(path)}"));
        lines.Add($"created_at: {Quote(item.CreatedAt.ToString("O"))}");
        lines.Add(item.Answer is null ? "answer: null" : $"answer: {Quote(item.Answer)}");

        if (item.AnsweredAt.HasValue)
        {
            lines.Add($"answered_at: {Quote(item.AnsweredAt.Value.ToString("O"))}");
        }

        if (item.RecommendedUpdates is not null)
        {
            lines.Add(item.RecommendedUpdates.Count == 0 ? "recommended_updates: []" : "recommended_updates:");
            if (item.RecommendedUpdates.Count > 0)
            {
                lines.AddRange(item.RecommendedUpdates.Select(update => $"  - {Quote(update)}"));
            }
        }

        return string.Join(Environment.NewLine, lines) + Environment.NewLine;
    }

    private static string FormatStatus(InterviewQueueItemStatus status)
    {
        return status.ToString().ToLowerInvariant();
    }

    private static string Quote(string value)
    {
        return "\"" + value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal)
            .Replace("\r", "\\r", StringComparison.Ordinal) + "\"";
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
