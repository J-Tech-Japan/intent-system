using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

public sealed class IntakeExecutionCommandTests
{
    [Fact]
    public void Execute_GivenUpdatedParentSourceFiles_WritesExecutionDraft()
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
            - intents/intent-cli/concepts/oauth2.md
            - intents/intent-cli/intent-tree/means/device-code.md

            source_concept_refs:
            - intents/intent-cli/concepts/oauth2.md

            ## File-By-File Patch Candidates

            ### `intents/intent-cli/concepts/oauth2.md`

            current_file_state: present
            foldin_anchors:
            - answered_question_ids:iq-1
            source_concept_refs:
            - intents/intent-cli/concepts/oauth2.md
            proposed_edits:
            - Reconcile this source concept file with the current fold-in draft.
            rationale:
            - This path is listed in source_concept_refs.
            current_file_excerpt:
            ```text
            # Auth Concept
            - Reconcile this source concept file with the current fold-in draft.
            ```

            ### `intents/intent-cli/intent-tree/means/device-code.md`

            current_file_state: present
            foldin_anchors:
            - answered_question_ids:iq-1
            - recommended_updates:Add device-code note
            source_concept_refs:
            - intents/intent-cli/concepts/oauth2.md
            proposed_edits:
            - Apply update candidate: Add device-code note
            rationale:
            - This path is listed in return_to_intent_paths.
            current_file_excerpt:
            ```text
            # Auth Means
            - Add device-code note
            ```
            """);
        tempDirectory.CreateFile(
            Path.Combine("repo", "intents", "intent-cli", "concepts", "oauth2.md"),
            "# Auth Concept" + Environment.NewLine + "- Reconcile this source concept file with the current fold-in draft.");
        tempDirectory.CreateFile(
            Path.Combine("repo", "intents", "intent-cli", "intent-tree", "means", "device-code.md"),
            "# Auth Means" + Environment.NewLine + "- Add device-code note");
        tempDirectory.CreateFile(
            Path.Combine("repo", "intents", "intent-cli", "intent-tree", "means", "payments.md"),
            "# Payments");
        using var writer = new StringWriter();

        var exitCode = IntakeExecutionCommand.Execute(CreateContext(repoRoot), ["auth"], writer);

        Assert.Equal(0, exitCode);
        var output = writer.ToString();
        Assert.Contains("Intake execution draft generated for domain 'auth'.", output, StringComparison.Ordinal);
        var artifactPath = Path.Combine(repoRoot, ".intent-cli", "intake", "auth.execution.md");
        Assert.True(File.Exists(artifactPath));
        var markdown = File.ReadAllText(artifactPath);
        Assert.Contains("### `AUTH-01`", markdown, StringComparison.Ordinal);
        Assert.Contains("### `AUTH-02`", markdown, StringComparison.Ordinal);
        Assert.Contains("source_file_path: intents/intent-cli/concepts/oauth2.md", markdown, StringComparison.Ordinal);
        Assert.Contains("source_file_path: intents/intent-cli/intent-tree/means/device-code.md", markdown, StringComparison.Ordinal);
        Assert.Contains("target_part: concepts", markdown, StringComparison.Ordinal);
        Assert.Contains("target_part: intent-tree/means", markdown, StringComparison.Ordinal);
        Assert.Contains("- AUTH-01", markdown, StringComparison.Ordinal);
        Assert.DoesNotContain("payments.md", markdown, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_GivenNoMatchingParentSourceFiles_ReturnsExitCodeOne()
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
            - intents/intent-cli/concepts/oauth2.md

            source_concept_refs:
            - intents/intent-cli/concepts/oauth2.md

            ## File-By-File Patch Candidates
            """);
        using var writer = new StringWriter();

        var exitCode = IntakeExecutionCommand.Execute(CreateContext(repoRoot), ["auth"], writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("No updated parent source files were found for domain 'auth'.", writer.ToString(), StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(repoRoot, ".intent-cli", "intake", "auth.execution.md")));
    }

    [Fact]
    public void Execute_GivenNoPatchTargetsButIssueReadyExecutionRows_WritesExecutionDraftFromCurrentExecutionSource()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "intake", "toy-calc.patch.md"),
            """
            # Intake Patch Draft

            ## Domain

            `toy-calc`

            target_file_paths:
            - none

            source_concept_refs:
            - .intent-cli/intake/toy-calc.concept.yaml

            ## File-By-File Patch Candidates
            """);
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

        var exitCode = IntakeExecutionCommand.Execute(CreateContext(repoRoot), ["toy-calc"], writer);

        Assert.Equal(0, exitCode);
        var output = writer.ToString();
        Assert.Contains("Intake execution draft generated for domain 'toy-calc'.", output, StringComparison.Ordinal);
        var artifactPath = Path.Combine(repoRoot, ".intent-cli", "intake", "toy-calc.execution.md");
        Assert.True(File.Exists(artifactPath));
        var markdown = File.ReadAllText(artifactPath);
        Assert.Contains("### `TOY-CALC-V0-06`", markdown, StringComparison.Ordinal);
        Assert.Contains("### `TOY-CALC-V0-07`", markdown, StringComparison.Ordinal);
        Assert.Contains("source_file_path: intents/toy-calc/execution/01-issue-ready-slices.md", markdown, StringComparison.Ordinal);
        Assert.Contains("target_part: CLI modulo command and verification", markdown, StringComparison.Ordinal);
        Assert.Contains("target_part: CLI min command and verification", markdown, StringComparison.Ordinal);
        Assert.Contains("- TOY-CALC-V0-06", markdown, StringComparison.Ordinal);
        Assert.Contains("Current goal: integer min command を追加する", markdown, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_GivenMissingPatchArtifact_ReturnsExitCodeOne()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        using var writer = new StringWriter();

        var exitCode = IntakeExecutionCommand.Execute(CreateContext(repoRoot), ["auth"], writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("Intake patch artifact was not found", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_GivenMissingDomainArgument_ReturnsExitCodeOne()
    {
        using var writer = new StringWriter();

        var exitCode = IntakeExecutionCommand.Execute(CreateContext("/tmp/intent-system"), [], writer);

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

    private sealed class TemporaryDirectory : IDisposable
    {
        private readonly string rootPath = Directory.CreateTempSubdirectory("intent-cli-intake-execution-command-tests-").FullName;

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
