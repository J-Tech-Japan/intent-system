using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

public sealed class IntakeExecutionApplyCommandTests
{
    [Fact]
    public void Execute_GivenExecutionDraft_UpdatesExecutionSourceFiles()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "intake", "auth.execution.md"),
            """
            # Intake Execution Draft

            ## Domain

            `auth`

            ## Proposed Execution Units

            ### `AUTH-01`

            source_file_path: intents/intent-cli/concepts/oauth2.md
            target_part: concepts
            dependencies:
            - none
            readiness_notes:
            - Source file path: intents/intent-cli/concepts/oauth2.md
            - Current heading: # Auth Concept
            - Detected bullet lines: 1
            verification_hints:
            - Review parent source file 'intents/intent-cli/concepts/oauth2.md' for issue-ready scope.
            - dotnet test IntentSystem.sln

            ### `AUTH-02`

            source_file_path: intents/intent-cli/intent-tree/means/device-code.md
            target_part: intent-tree/means
            dependencies:
            - AUTH-01
            readiness_notes:
            - Source file path: intents/intent-cli/intent-tree/means/device-code.md
            - Current heading: # Device Code
            - Detected bullet lines: 2
            verification_hints:
            - Review parent source file 'intents/intent-cli/intent-tree/means/device-code.md' for issue-ready scope.
            - dotnet test IntentSystem.sln
            """);
        tempDirectory.CreateFile(
            Path.Combine("repo", "intents", "intent-cli", "execution", "05-post-mvp-sub-slices.md"),
            """
            # Post-MVP Sub-Slices

            | subslice_id | belongs_to_slice | goal | depends_on_subslices | target_repo | target_path | target_part | issue_cut_ready |
            |---|---|---|---|---|---|---|---|
            | G1 | G | existing row | - | submodules/intent-system | . | cli shell | yes |
            """);
        tempDirectory.CreateFile(
            Path.Combine("repo", "intents", "intent-cli", "execution", "03-readiness-and-verification.md"),
            """
            # Readiness And Verification

            ## Existing Section

            - Existing baseline note
            """);
        using var writer = new StringWriter();

        var exitCode = IntakeExecutionApplyCommand.Execute(CreateContext(repoRoot), ["auth"], writer);

        Assert.Equal(0, exitCode);
        var output = writer.ToString();
        Assert.Contains("Intake execution apply completed for domain 'auth'.", output, StringComparison.Ordinal);
        Assert.Contains("Applied unit count: 2", output, StringComparison.Ordinal);
        Assert.Contains("intents/intent-cli/execution/05-post-mvp-sub-slices.md", output, StringComparison.Ordinal);
        Assert.Contains("intents/intent-cli/execution/03-readiness-and-verification.md", output, StringComparison.Ordinal);
        Assert.Contains("- AUTH-01", output, StringComparison.Ordinal);

        var subSlices = File.ReadAllText(Path.Combine(repoRoot, "intents", "intent-cli", "execution", "05-post-mvp-sub-slices.md"));
        Assert.Contains("| AUTH-01 | G | reflect updated source 'Auth Concept' into issue-ready execution unit | - | submodules/intent-system | . | concepts | candidate |", subSlices, StringComparison.Ordinal);
        Assert.Contains("| AUTH-02 | G | reflect updated source 'Device Code' into issue-ready execution unit | AUTH-01 | submodules/intent-system | . | intent-tree/means | candidate |", subSlices, StringComparison.Ordinal);
        Assert.Contains("| G1 | G | existing row | - | submodules/intent-system | . | cli shell | yes |", subSlices, StringComparison.Ordinal);

        var readiness = File.ReadAllText(Path.Combine(repoRoot, "intents", "intent-cli", "execution", "03-readiness-and-verification.md"));
        Assert.Contains("## Intake Execution Candidates: auth", readiness, StringComparison.Ordinal);
        Assert.Contains("### `AUTH-01`", readiness, StringComparison.Ordinal);
        Assert.Contains("### `AUTH-02`", readiness, StringComparison.Ordinal);
        Assert.Contains("- AUTH-01", readiness, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_GivenAlreadyAppliedDraft_ReturnsNoOpSummary()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "intake", "auth.execution.md"),
            """
            # Intake Execution Draft

            ## Domain

            `auth`

            ## Proposed Execution Units

            ### `AUTH-01`

            source_file_path: intents/intent-cli/concepts/oauth2.md
            target_part: concepts
            dependencies:
            - none
            readiness_notes:
            - Source file path: intents/intent-cli/concepts/oauth2.md
            - Current heading: # Auth Concept
            verification_hints:
            - dotnet test IntentSystem.sln
            """);
        tempDirectory.CreateFile(
            Path.Combine("repo", "intents", "intent-cli", "execution", "05-post-mvp-sub-slices.md"),
            """
            # Post-MVP Sub-Slices

            | subslice_id | belongs_to_slice | goal | depends_on_subslices | target_repo | target_path | target_part | issue_cut_ready |
            |---|---|---|---|---|---|---|---|
            """);
        tempDirectory.CreateFile(
            Path.Combine("repo", "intents", "intent-cli", "execution", "03-readiness-and-verification.md"),
            """
            # Readiness And Verification
            """);
        using var writer = new StringWriter();

        var firstExitCode = IntakeExecutionApplyCommand.Execute(CreateContext(repoRoot), ["auth"], writer);
        Assert.Equal(0, firstExitCode);

        writer.GetStringBuilder().Clear();

        var exitCode = IntakeExecutionApplyCommand.Execute(CreateContext(repoRoot), ["auth"], writer);

        Assert.Equal(0, exitCode);
        Assert.Contains("Applied unit count: 0", writer.ToString(), StringComparison.Ordinal);
        Assert.Equal(2, writer.ToString().Split("- none", StringSplitOptions.None).Length - 1);
    }

    [Fact]
    public void Execute_GivenMissingExecutionArtifact_ReturnsExitCodeOne()
    {
        using var writer = new StringWriter();

        var exitCode = IntakeExecutionApplyCommand.Execute(CreateContext("/tmp/intent-system"), ["auth"], writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("Intake execution artifact was not found", writer.ToString(), StringComparison.Ordinal);
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

    private sealed class TemporaryDirectory : IDisposable
    {
        private readonly string rootPath = Directory.CreateTempSubdirectory("intent-cli-intake-execution-apply-command-tests-").FullName;

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
