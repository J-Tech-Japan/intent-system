using IntentSystem.ConceptIntake.Models;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

public sealed class IntakeCompileCommandTests
{
    [Fact]
    public void Execute_GivenOpenInterviewQuestion_RendersNotReadyAndDoesNotWriteArtifact()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "interviews", "auth", "iq-1.yaml"),
            CreateInterviewArtifactYaml(CreateItem("iq-1", InterviewQueueItemStatus.Open, createdAt: "2026-04-13T08:00:00Z")));
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "interviews", "auth", "iq-1.md"),
            "# Interview Question");
        using var writer = new StringWriter();

        var exitCode = IntakeCompileCommand.Execute(CreateContext(repoRoot), ["auth"], writer);

        Assert.Equal(0, exitCode);
        var output = writer.ToString();
        Assert.Contains("Intake compile is not ready for domain 'auth'.", output, StringComparison.Ordinal);
        Assert.Contains("Next interview question:", output, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(repoRoot, ".intent-cli", "intake", "auth.compile.md")));
    }

    [Fact]
    public void Execute_GivenAnsweredItems_WritesCompileArtifact()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "interviews", "auth", "iq-2.yaml"),
            CreateInterviewArtifactYaml(CreateAnsweredItem(
                "iq-2",
                "2026-04-13T08:00:00Z",
                sourceConceptRef: "intents/intent-cli/concepts/auth-device-code.md",
                returnToIntentPaths:
                [
                    "intents/intent-cli/intent-tree/means/auth-device-code.md",
                    "intents/intent-cli/intent-tree/means/auth-oauth2.md"
                ],
                recommendedUpdates:
                [
                    "Document OAuth2 fallback",
                    "Add device-code note"
                ])));
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "interviews", "auth", "iq-2.md"),
            "# Interview Question");
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "interviews", "auth", "iq-1.yaml"),
            CreateInterviewArtifactYaml(CreateAnsweredItem(
                "iq-1",
                "2026-04-13T07:00:00Z",
                sourceConceptRef: "intents/intent-cli/concepts/auth-oauth2.md",
                returnToIntentPaths:
                [
                    "intents/intent-cli/intent-tree/means/auth-oauth2.md"
                ],
                recommendedUpdates:
                [
                    "Add device-code note",
                    "Align login UX wording"
                ])));
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "interviews", "auth", "iq-1.md"),
            "# Interview Question");
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "interviews", "auth", "iq-3.yaml"),
            CreateInterviewArtifactYaml(CreateAppliedItem("iq-3")));
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "interviews", "auth", "iq-3.md"),
            "# Interview Question");
        using var writer = new StringWriter();

        var exitCode = IntakeCompileCommand.Execute(CreateContext(repoRoot), ["auth"], writer);

        Assert.Equal(0, exitCode);
        var output = writer.ToString();
        Assert.Contains("Intake compile artifact generated for domain 'auth'.", output, StringComparison.Ordinal);
        var artifactPath = Path.Combine(repoRoot, ".intent-cli", "intake", "auth.compile.md");
        Assert.True(File.Exists(artifactPath));
        var markdown = File.ReadAllText(artifactPath);
        Assert.Contains("answered_question_ids:", markdown, StringComparison.Ordinal);
        Assert.True(markdown.IndexOf("- iq-1", StringComparison.Ordinal) < markdown.IndexOf("- iq-2", StringComparison.Ordinal));
        Assert.True(markdown.IndexOf("- Add device-code note", StringComparison.Ordinal) < markdown.IndexOf("- Align login UX wording", StringComparison.Ordinal));
        Assert.True(markdown.IndexOf("- Align login UX wording", StringComparison.Ordinal) < markdown.IndexOf("- Document OAuth2 fallback", StringComparison.Ordinal));
        Assert.Contains("- intents/intent-cli/intent-tree/means/auth-device-code.md", markdown, StringComparison.Ordinal);
        Assert.Equal(1, CountOccurrences(markdown, "- intents/intent-cli/intent-tree/means/auth-oauth2.md"));
        Assert.Contains("source_concept_refs:", markdown, StringComparison.Ordinal);
        Assert.Contains("- intents/intent-cli/concepts/auth-device-code.md", markdown, StringComparison.Ordinal);
        Assert.Contains("- intents/intent-cli/concepts/auth-oauth2.md", markdown, StringComparison.Ordinal);
        Assert.DoesNotContain("Use OAuth2 with PKCE.", markdown, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_GivenNoInterviewArtifacts_RendersDeterministicNotReadyResult()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        using var writer = new StringWriter();

        var exitCode = IntakeCompileCommand.Execute(CreateContext(repoRoot), ["auth"], writer);

        Assert.Equal(0, exitCode);
        var output = writer.ToString();
        Assert.Contains("Intake compile is not ready for domain 'auth'.", output, StringComparison.Ordinal);
        Assert.Contains("No interview artifacts found for domain 'auth'.", output, StringComparison.Ordinal);
        var artifactPath = Path.Combine(repoRoot, ".intent-cli", "intake", "auth.compile.md");
        Assert.False(File.Exists(artifactPath));
    }

    [Fact]
    public void Execute_GivenMissingDomainArgument_ReturnsExitCodeOne()
    {
        using var writer = new StringWriter();

        var exitCode = IntakeCompileCommand.Execute(CreateContext("/tmp/intent-system"), [], writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("requires a domain", writer.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    private static int CountOccurrences(string text, string needle)
    {
        var count = 0;
        var currentIndex = 0;

        while ((currentIndex = text.IndexOf(needle, currentIndex, StringComparison.Ordinal)) >= 0)
        {
            count++;
            currentIndex += needle.Length;
        }

        return count;
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

    private static InterviewQueueItem CreateItem(
        string questionId,
        InterviewQueueItemStatus status,
        string createdAt = "2026-04-13T06:00:00Z",
        string sourceConceptRef = "intents/intent-cli/concepts/auth-oauth2.md",
        IReadOnlyList<string>? returnToIntentPaths = null,
        IReadOnlyList<string>? recommendedUpdates = null)
    {
        return new InterviewQueueItem
        {
            DomainSlug = "auth",
            SourceConceptRef = sourceConceptRef,
            QuestionId = questionId,
            QuestionText = $"Question for {questionId}",
            Reason = "Explore unknown area.",
            Affects = ["auth-oauth2"],
            BlockingOrNonblocking = "blocking",
            Status = status,
            ReturnToIntentPaths = returnToIntentPaths ?? ["intents/intent-cli/intent-tree/means/auth-oauth2.md"],
            CreatedAt = DateTimeOffset.Parse(createdAt),
            Answer = null,
            RecommendedUpdates = recommendedUpdates
        };
    }

    private static InterviewQueueItem CreateAnsweredItem(
        string questionId,
        string createdAt,
        string sourceConceptRef,
        IReadOnlyList<string>? returnToIntentPaths = null,
        IReadOnlyList<string>? recommendedUpdates = null)
    {
        return CreateItem(questionId, InterviewQueueItemStatus.Answered, createdAt, sourceConceptRef, returnToIntentPaths, recommendedUpdates) with
        {
            Answer = $"Answer for {questionId}",
            AnsweredAt = DateTimeOffset.Parse("2026-04-13T10:00:00Z")
        };
    }

    private static InterviewQueueItem CreateAppliedItem(string questionId)
    {
        return CreateItem(questionId, InterviewQueueItemStatus.Applied) with
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
        lines.Add($"status: {item.Status.ToString().ToLowerInvariant()}");
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
        private readonly string rootPath = Directory.CreateTempSubdirectory("intent-cli-intake-compile-tests-").FullName;

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
