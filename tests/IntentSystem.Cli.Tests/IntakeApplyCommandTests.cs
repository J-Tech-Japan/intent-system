using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

public sealed class IntakeApplyCommandTests
{
    [Fact]
    public void Execute_GivenPatchArtifact_UpdatesOnlyTargetFilesAndWritesSummary()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "intake", "auth.patch.md"),
            CreatePatchArtifactMarkdown());
        tempDirectory.CreateFile(
            Path.Combine("repo", "intents", "intent-cli", "intent-tree", "means", "auth-oauth2.md"),
            "# Auth Means" + Environment.NewLine + "Existing line");
        tempDirectory.CreateFile(
            Path.Combine("repo", "intents", "intent-cli", "concepts", "auth-oauth2.md"),
            "# Auth Concept" + Environment.NewLine + "Known flow");
        tempDirectory.CreateFile(
            Path.Combine("repo", "README.md"),
            "# Repo");
        using var writer = new StringWriter();

        var exitCode = IntakeApplyCommand.Execute(CreateContext(repoRoot), ["auth"], writer);

        Assert.Equal(0, exitCode);
        var output = writer.ToString();
        Assert.Contains("Intake apply completed for domain 'auth'.", output, StringComparison.Ordinal);
        Assert.Contains("Applied edit count: 3", output, StringComparison.Ordinal);
        Assert.Contains("- intents/intent-cli/concepts/auth-oauth2.md", output, StringComparison.Ordinal);
        Assert.Contains("- intents/intent-cli/intent-tree/means/auth-oauth2.md", output, StringComparison.Ordinal);

        var meansFile = File.ReadAllText(Path.Combine(repoRoot, "intents", "intent-cli", "intent-tree", "means", "auth-oauth2.md"));
        Assert.DoesNotContain("intake-apply:start", meansFile, StringComparison.Ordinal);
        Assert.Contains("# Auth Means", meansFile, StringComparison.Ordinal);
        Assert.Contains("Existing line", meansFile, StringComparison.Ordinal);
        Assert.Contains("- Add device-code note", meansFile, StringComparison.Ordinal);
        Assert.Contains("- Document OAuth2 fallback", meansFile, StringComparison.Ordinal);

        var conceptFile = File.ReadAllText(Path.Combine(repoRoot, "intents", "intent-cli", "concepts", "auth-oauth2.md"));
        Assert.DoesNotContain("intake-apply:start", conceptFile, StringComparison.Ordinal);
        Assert.Contains("Reconcile this source concept file with the current fold-in draft.", conceptFile, StringComparison.Ordinal);

        var untouchedFile = File.ReadAllText(Path.Combine(repoRoot, "README.md"));
        Assert.Equal("# Repo", untouchedFile);
    }

    [Fact]
    public void Execute_GivenRepeatedApply_IsDeterministicNoOpOnSecondRun()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "intake", "auth.patch.md"),
            CreatePatchArtifactMarkdown());
        tempDirectory.CreateFile(
            Path.Combine("repo", "intents", "intent-cli", "intent-tree", "means", "auth-oauth2.md"),
            "# Auth Means" + Environment.NewLine + "Existing line");
        tempDirectory.CreateFile(
            Path.Combine("repo", "intents", "intent-cli", "concepts", "auth-oauth2.md"),
            "# Auth Concept" + Environment.NewLine + "Known flow");
        using var firstWriter = new StringWriter();
        using var secondWriter = new StringWriter();

        var firstExitCode = IntakeApplyCommand.Execute(CreateContext(repoRoot), ["auth"], firstWriter);
        var secondExitCode = IntakeApplyCommand.Execute(CreateContext(repoRoot), ["auth"], secondWriter);

        Assert.Equal(0, firstExitCode);
        Assert.Equal(0, secondExitCode);
        Assert.Contains("Applied edit count: 0", secondWriter.ToString(), StringComparison.Ordinal);
        Assert.Contains("Changed file paths:", secondWriter.ToString(), StringComparison.Ordinal);
        Assert.Contains("- none", secondWriter.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_GivenPatchFileBlockOutsideTargetPaths_ReturnsExitCodeOne()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "intake", "auth.patch.md"),
            """
            # Intake Patch Draft

            ## Domain

            `auth`

            target_file_paths:
            - intents/intent-cli/intent-tree/means/auth-oauth2.md

            source_concept_refs:
            - intents/intent-cli/concepts/auth-oauth2.md

            ## File-By-File Patch Candidates

            ### `intents/intent-cli/concepts/auth-oauth2.md`

            current_file_state: present
            foldin_anchors:
            - answered_question_ids:iq-1
            source_concept_refs:
            - intents/intent-cli/concepts/auth-oauth2.md
            proposed_edits:
            - Reconcile this source concept file with the current fold-in draft.
            rationale:
            - This path is listed in source_concept_refs.
            current_file_excerpt:
            ```text
            # Auth Concept
            ```
            """);
        using var writer = new StringWriter();

        var exitCode = IntakeApplyCommand.Execute(CreateContext(repoRoot), ["auth"], writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("is not listed in target_file_paths", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_GivenMissingPatchArtifact_ReturnsExitCodeOne()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        using var writer = new StringWriter();

        var exitCode = IntakeApplyCommand.Execute(CreateContext(repoRoot), ["auth"], writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("Intake patch artifact was not found", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_GivenCurrentContentThatDoesNotMatchExcerpt_ReturnsExitCodeOne()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "intake", "auth.patch.md"),
            CreatePatchArtifactMarkdown());
        tempDirectory.CreateFile(
            Path.Combine("repo", "intents", "intent-cli", "intent-tree", "means", "auth-oauth2.md"),
            "# Auth Means" + Environment.NewLine + "Drifted line");
        tempDirectory.CreateFile(
            Path.Combine("repo", "intents", "intent-cli", "concepts", "auth-oauth2.md"),
            "# Auth Concept" + Environment.NewLine + "Known flow");
        using var writer = new StringWriter();

        var exitCode = IntakeApplyCommand.Execute(CreateContext(repoRoot), ["auth"], writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("no longer matches the patch draft excerpt", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_GivenMissingDomainArgument_ReturnsExitCodeOne()
    {
        using var writer = new StringWriter();

        var exitCode = IntakeApplyCommand.Execute(CreateContext("/tmp/intent-system"), [], writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("requires a domain", writer.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Execute_GivenRuntimeConceptArtifactSourceRef_KeepsConceptYamlDeserializable()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "intake", "auth.patch.md"),
            """
            # Intake Patch Draft

            ## Domain

            `auth`

            target_file_paths:
            - intents/intent-cli/intent-tree/means/auth-oauth2.md

            source_concept_refs:
            - .intent-cli/intake/auth.concept.yaml

            ## File-By-File Patch Candidates

            ### `intents/intent-cli/intent-tree/means/auth-oauth2.md`

            current_file_state: present
            foldin_anchors:
            - answered_question_ids:iq-1
            - recommended_updates:Add device-code note
            source_concept_refs:
            - .intent-cli/intake/auth.concept.yaml
            proposed_edits:
            - Apply update candidate: Add device-code note
            rationale:
            - This path is listed in return_to_intent_paths.
            current_file_excerpt:
            ```text
            # Auth Means
            Existing line
            ```
            """);
        var conceptArtifactPath = Path.Combine(repoRoot, ".intent-cli", "intake", "auth.concept.yaml");
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "intake", "auth.concept.yaml"),
            """
            domain_slug: auth
            concept_source: interactive
            concept_text: "Add OAuth2 provider support."
            upstream_paths: []
            initial_goal: "Add OAuth2 provider support."
            constraints: []
            known_unknowns: []
            """);
        tempDirectory.CreateFile(
            Path.Combine("repo", "intents", "intent-cli", "intent-tree", "means", "auth-oauth2.md"),
            "# Auth Means" + Environment.NewLine + "Existing line");
        using var writer = new StringWriter();

        var originalConceptYaml = File.ReadAllText(conceptArtifactPath);
        var exitCode = IntakeApplyCommand.Execute(CreateContext(repoRoot), ["auth"], writer);

        Assert.Equal(0, exitCode);
        Assert.Equal(originalConceptYaml, File.ReadAllText(conceptArtifactPath));
        var packet = IntakeConceptArtifactYaml.Deserialize(File.ReadAllText(conceptArtifactPath));
        Assert.Equal("auth", packet.DomainSlug);

        var updatedMeans = File.ReadAllText(Path.Combine(repoRoot, "intents", "intent-cli", "intent-tree", "means", "auth-oauth2.md"));
        Assert.Contains("- Add device-code note", updatedMeans, StringComparison.Ordinal);
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

    private static string CreatePatchArtifactMarkdown()
    {
        return """
            # Intake Patch Draft

            ## Domain

            `auth`

            target_file_paths:
            - intents/intent-cli/concepts/auth-oauth2.md
            - intents/intent-cli/intent-tree/means/auth-oauth2.md

            source_concept_refs:
            - intents/intent-cli/concepts/auth-oauth2.md

            ## File-By-File Patch Candidates

            ### `intents/intent-cli/concepts/auth-oauth2.md`

            current_file_state: present
            foldin_anchors:
            - answered_question_ids:iq-1
            - source_concept_refs:intents/intent-cli/concepts/auth-oauth2.md
            source_concept_refs:
            - intents/intent-cli/concepts/auth-oauth2.md
            proposed_edits:
            - Reconcile this source concept file with the current fold-in draft.
            rationale:
            - This path is listed in source_concept_refs.
            current_file_excerpt:
            ```text
            # Auth Concept
            Known flow
            ```

            ### `intents/intent-cli/intent-tree/means/auth-oauth2.md`

            current_file_state: present
            foldin_anchors:
            - answered_question_ids:iq-1
            - recommended_updates:Add device-code note
            - return_to_intent_paths:intents/intent-cli/intent-tree/means/auth-oauth2.md
            source_concept_refs:
            - intents/intent-cli/concepts/auth-oauth2.md
            proposed_edits:
            - Apply update candidate: Add device-code note
            - Apply update candidate: Document OAuth2 fallback
            rationale:
            - This path is listed in return_to_intent_paths.
            current_file_excerpt:
            ```text
            # Auth Means
            Existing line
            ```
            """;
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        private readonly string rootPath = Directory.CreateTempSubdirectory("intent-cli-intake-apply-command-tests-").FullName;

        public string CreateDirectory(string relativePath)
        {
            var fullPath = Path.Combine(rootPath, relativePath);
            Directory.CreateDirectory(fullPath);
            return fullPath;
        }

        public void CreateFile(string relativePath, string contents)
        {
            var fullPath = Path.Combine(rootPath, relativePath);
            var directoryPath = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(directoryPath))
            {
                Directory.CreateDirectory(directoryPath);
            }

            File.WriteAllText(fullPath, contents);
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
