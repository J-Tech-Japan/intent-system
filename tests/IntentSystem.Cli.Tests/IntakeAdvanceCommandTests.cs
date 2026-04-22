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
    public void Execute_GivenRuntimeConceptArtifactSourceRef_DoesNotCorruptConceptYaml()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        var conceptArtifactPath = Path.Combine(repoRoot, ".intent-cli", "intake", "auth.concept.yaml");
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "intake", "auth.concept.yaml"),
            CreateConceptArtifactYaml("auth"));
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "interviews", "auth", "iq-1.yaml"),
            CreateInterviewArtifactYaml(CreateAnsweredItem(sourceConceptRef: ".intent-cli/intake/auth.concept.yaml")));
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "interviews", "auth", "iq-1.md"),
            "# Interview Question");
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
            """);
        using var writer = new StringWriter();

        var originalConceptYaml = File.ReadAllText(conceptArtifactPath);
        var exitCode = IntakeAdvanceCommand.Execute(CreateContext(repoRoot), ["auth"], writer);

        Assert.Equal(0, exitCode);
        Assert.Equal(originalConceptYaml, File.ReadAllText(conceptArtifactPath));
        var packet = IntakeConceptArtifactYaml.Deserialize(File.ReadAllText(conceptArtifactPath));
        Assert.Equal("auth", packet.DomainSlug);

        var output = writer.ToString();
        Assert.Contains("Readiness status: ready", output, StringComparison.Ordinal);
        Assert.DoesNotContain("- .intent-cli/intake/auth.concept.yaml", output, StringComparison.Ordinal);
        Assert.Contains("- intents/intent-cli/intent-tree/means/auth-oauth2.md", output, StringComparison.Ordinal);

        var patchDraft = File.ReadAllText(Path.Combine(repoRoot, ".intent-cli", "intake", "auth.patch.md"));
        Assert.Contains("- .intent-cli/intake/auth.concept.yaml", patchDraft, StringComparison.Ordinal);
        Assert.DoesNotContain("### `.intent-cli/intake/auth.concept.yaml`", patchDraft, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_GivenIssueReadyExecutionRowsWithoutExecutionApplyBaseline_SkipsExecutionApplyAndRefreshesDraft()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "intake", "toy-calc.concept.yaml"),
            CreateConceptArtifactYaml("toy-calc"));
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "interviews", "toy-calc", "iq-1.yaml"),
            CreateInterviewArtifactYaml(CreateAnsweredItem(domain: "toy-calc", sourceConceptRef: ".intent-cli/intake/toy-calc.concept.yaml")));
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "interviews", "toy-calc", "iq-1.md"),
            "# Interview Question");
        tempDirectory.CreateFile(
            Path.Combine("repo", "intents", "toy-calc", "execution", "01-issue-ready-slices.md"),
            """
            # Toy Calc Issue-Ready Slices

            | subslice_id | belongs_to_slice | goal | depends_on_subslices | target_repo | target_path | target_part | issue_cut_ready |
            |---|---|---|---|---|---|---|---|
            | TOY-CALC-V0-06 | V0 | integer modulo command を追加する | TOY-CALC-V0-05 | . | . | CLI modulo command and verification | yes |
            | TOY-CALC-V0-07 | V0 | integer min command を追加する | TOY-CALC-V0-06 | . | . | CLI min command and verification | yes |
            """);
        using var writer = new StringWriter();

        var exitCode = IntakeAdvanceCommand.Execute(CreateContext(repoRoot), ["toy-calc"], writer);

        Assert.Equal(0, exitCode);
        var output = writer.ToString();
        Assert.Contains("Readiness status: ready", output, StringComparison.Ordinal);
        Assert.Contains("Regenerated artifact paths:", output, StringComparison.Ordinal);
        Assert.Contains("- .intent-cli/intake/toy-calc.execution.md", output, StringComparison.Ordinal);
        Assert.Contains("Skipped stages:", output, StringComparison.Ordinal);
        Assert.Contains("- execution-apply", output, StringComparison.Ordinal);
        Assert.DoesNotContain("Execution apply target could not be derived", output, StringComparison.Ordinal);

        Assert.True(File.Exists(Path.Combine(repoRoot, ".intent-cli", "intake", "toy-calc.execution.md")));
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
                    ArtifactRoot = ".intent-cli"
                }
            }
        };
    }

    private static IntentSystem.ConceptIntake.Models.InterviewQueueItem CreateAnsweredItem(
        string domain = "auth",
        string sourceConceptRef = "intents/intent-cli/concepts/auth-oauth2.md")
    {
        return new IntentSystem.ConceptIntake.Models.InterviewQueueItem
        {
            DomainSlug = domain,
            SourceConceptRef = sourceConceptRef,
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
