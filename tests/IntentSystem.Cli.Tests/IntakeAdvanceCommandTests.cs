using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

public sealed class IntakeAdvanceCommandTests
{
    [Fact]
    public void Execute_GivenReadyIntakeArtifacts_AdvancesToUpdatedExecutionSourceOfTruth()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "intake", "auth.concept.yaml"),
            CreateConceptArtifactYaml("auth"));
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "interviews", "auth", "iq-1.yaml"),
            CreateInterviewArtifactYaml(CreateAnsweredItem()));
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "interviews", "auth", "iq-1.md"),
            "# Interview Question");
        tempDirectory.CreateFile(
            Path.Combine("repo", "intents", "intent-cli", "concepts", "auth-oauth2.md"),
            "# Auth Concept" + Environment.NewLine + Environment.NewLine + "- Existing note" + Environment.NewLine);
        tempDirectory.CreateFile(
            Path.Combine("repo", "intents", "intent-cli", "intent-tree", "means", "auth-oauth2.md"),
            "# Auth Means" + Environment.NewLine + Environment.NewLine + "- Existing rule" + Environment.NewLine);
        tempDirectory.CreateFile(
            Path.Combine("repo", "intents", "intent-cli", "execution", "05-post-mvp-sub-slices.md"),
            """
            # Post-MVP Sub-Slices

            ## G36 の current baseline

            - `intake execution apply <domain>` を最初の execution source-of-truth apply command にする
            - canonical source は current `.intent-cli/intake/<domain>.execution.md` と current `execution/` source files, plus the `G29` / `G30` / `G32` / `G33` / `G34` / `G35` intake baseline である
            - successful output は execution draft で指定された source files だけを deterministic に更新することを baseline にする

            ## Another Section

            - Keep this section untouched
            """);
        using var writer = new StringWriter();

        var exitCode = IntakeAdvanceCommand.Execute(CreateContext(repoRoot), ["auth"], writer);

        Assert.Equal(0, exitCode);
        var output = writer.ToString();
        Assert.Contains("Intake advance processed for domain 'auth'.", output, StringComparison.Ordinal);
        Assert.Contains("Readiness status: ready", output, StringComparison.Ordinal);
        Assert.Contains("Updated source file paths:", output, StringComparison.Ordinal);
        Assert.Contains("- intents/intent-cli/concepts/auth-oauth2.md", output, StringComparison.Ordinal);
        Assert.Contains("- intents/intent-cli/intent-tree/means/auth-oauth2.md", output, StringComparison.Ordinal);
        Assert.Contains("Updated execution file paths:", output, StringComparison.Ordinal);
        Assert.Contains("- intents/intent-cli/execution/05-post-mvp-sub-slices.md", output, StringComparison.Ordinal);
        Assert.Contains("Regenerated artifact paths:", output, StringComparison.Ordinal);
        Assert.Contains("- .intent-cli/intake/auth.compile.md", output, StringComparison.Ordinal);
        Assert.Contains("- .intent-cli/intake/auth.foldin.md", output, StringComparison.Ordinal);
        Assert.Contains("- .intent-cli/intake/auth.patch.md", output, StringComparison.Ordinal);
        Assert.Contains("- .intent-cli/intake/auth.execution.md", output, StringComparison.Ordinal);
        Assert.Contains("Skipped stages:", output, StringComparison.Ordinal);
        var skippedSectionIndex = output.IndexOf("Skipped stages:", StringComparison.Ordinal);
        Assert.NotEqual(-1, skippedSectionIndex);
        Assert.Contains("- none", output[skippedSectionIndex..], StringComparison.Ordinal);

        Assert.True(File.Exists(Path.Combine(repoRoot, ".intent-cli", "intake", "auth.compile.md")));
        Assert.True(File.Exists(Path.Combine(repoRoot, ".intent-cli", "intake", "auth.foldin.md")));
        Assert.True(File.Exists(Path.Combine(repoRoot, ".intent-cli", "intake", "auth.patch.md")));
        Assert.True(File.Exists(Path.Combine(repoRoot, ".intent-cli", "intake", "auth.execution.md")));

        var updatedConcept = File.ReadAllText(Path.Combine(repoRoot, "intents", "intent-cli", "concepts", "auth-oauth2.md"));
        Assert.Contains("- Reconcile this source concept file with the current fold-in draft.", updatedConcept, StringComparison.Ordinal);
        var updatedMeans = File.ReadAllText(Path.Combine(repoRoot, "intents", "intent-cli", "intent-tree", "means", "auth-oauth2.md"));
        Assert.Contains("- Align login UX wording", updatedMeans, StringComparison.Ordinal);

        var executionSource = File.ReadAllText(Path.Combine(repoRoot, "intents", "intent-cli", "execution", "05-post-mvp-sub-slices.md"));
        Assert.Contains("- execution_unit: AUTH-01", executionSource, StringComparison.Ordinal);
        Assert.Contains("- execution_unit: AUTH-02", executionSource, StringComparison.Ordinal);
        Assert.Contains("## Another Section", executionSource, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_GivenNotReadyCompileState_RendersNotReadySummaryAndSkipsLaterStages()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "intake", "auth.concept.yaml"),
            CreateConceptArtifactYaml("auth"));
        using var writer = new StringWriter();

        var exitCode = IntakeAdvanceCommand.Execute(CreateContext(repoRoot), ["auth"], writer);

        Assert.Equal(0, exitCode);
        var output = writer.ToString();
        Assert.Contains("Intake advance processed for domain 'auth'.", output, StringComparison.Ordinal);
        Assert.Contains("Readiness status: not-ready", output, StringComparison.Ordinal);
        Assert.Contains("Skipped stages:", output, StringComparison.Ordinal);
        Assert.Contains("- foldin", output, StringComparison.Ordinal);
        Assert.Contains("- patch", output, StringComparison.Ordinal);
        Assert.Contains("- apply", output, StringComparison.Ordinal);
        Assert.Contains("- execution", output, StringComparison.Ordinal);
        Assert.Contains("- execution-apply", output, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(repoRoot, ".intent-cli", "intake", "auth.compile.md")));
    }

    [Fact]
    public void Execute_GivenMissingDomainArgument_ReturnsExitCodeOne()
    {
        using var writer = new StringWriter();

        var exitCode = IntakeAdvanceCommand.Execute(CreateContext("/tmp/intent-system"), [], writer);

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
                    WorkflowEngine = "intent-cli",
                    ArtifactRoot = ".intent-cli"
                }
            }
        };
    }

    private static IntentSystem.ConceptIntake.Models.InterviewQueueItem CreateAnsweredItem()
    {
        return new IntentSystem.ConceptIntake.Models.InterviewQueueItem
        {
            DomainSlug = "auth",
            SourceConceptRef = "intents/intent-cli/concepts/auth-oauth2.md",
            QuestionId = "iq-1",
            QuestionText = "What should be updated?",
            Reason = "Clarify auth direction.",
            Affects = ["auth-oauth2"],
            BlockingOrNonblocking = "blocking",
            Status = IntentSystem.ConceptIntake.Models.InterviewQueueItemStatus.Answered,
            ReturnToIntentPaths = ["intents/intent-cli/intent-tree/means/auth-oauth2.md"],
            CreatedAt = DateTimeOffset.Parse("2026-04-13T07:00:00Z"),
            Answer = "Align login UX wording.",
            AnsweredAt = DateTimeOffset.Parse("2026-04-13T10:00:00Z"),
            RecommendedUpdates = ["Align login UX wording"]
        };
    }

    private static string CreateConceptArtifactYaml(string domain)
    {
        return $$"""
            domain_slug: {{domain}}
            concept_source: interactive
            concept_text: "Add OAuth2 provider support."
            upstream_paths:
              - "intents/intent-cli/intent-tree/means/04-worker-interface-strategy.md"
            initial_goal: "Add OAuth2 provider support."
            constraints:
              - "Must not break existing session flow"
            known_unknowns:
              - "Which OAuth providers to support?"
            """;
    }

    private static string CreateInterviewArtifactYaml(IntentSystem.ConceptIntake.Models.InterviewQueueItem item)
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
        lines.Add($"answered_at: {Quote(item.AnsweredAt!.Value.ToString("O"))}");
        lines.Add("recommended_updates:");
        lines.AddRange(item.RecommendedUpdates!.Select(update => $"  - {Quote(update)}"));

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
        private readonly string rootPath = Directory.CreateTempSubdirectory("intent-cli-intake-advance-tests-").FullName;

        public string CreateDirectory(string relativePath)
        {
            var fullPath = Path.Combine(rootPath, relativePath);
            Directory.CreateDirectory(fullPath);
            return fullPath;
        }

        public string CreateFile(string relativePath, string contents)
        {
            var fullPath = Path.Combine(rootPath, relativePath);
            var directoryPath = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(directoryPath))
            {
                Directory.CreateDirectory(directoryPath);
            }

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
