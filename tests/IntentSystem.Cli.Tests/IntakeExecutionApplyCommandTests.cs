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
            Path.Combine("repo", "intents", "intent-cli", "concepts", "oauth2.md"),
            "# Auth Concept" + Environment.NewLine + "- Existing concept line");
        tempDirectory.CreateFile(
            Path.Combine("repo", "intents", "intent-cli", "intent-tree", "means", "device-code.md"),
            "# Device Code" + Environment.NewLine + "- Existing means line");
        tempDirectory.CreateFile(
            Path.Combine("repo", "intents", "intent-cli", "intent-tree", "means", "payments.md"),
            "# Payments" + Environment.NewLine + "- Untouched");
        using var writer = new StringWriter();

        var exitCode = IntakeExecutionApplyCommand.Execute(CreateContext(repoRoot), ["auth"], writer);

        Assert.Equal(0, exitCode);
        var output = writer.ToString();
        Assert.Contains("Intake execution apply completed for domain 'auth'.", output, StringComparison.Ordinal);
        Assert.Contains("Applied unit count: 2", output, StringComparison.Ordinal);
        Assert.Contains("intents/intent-cli/concepts/oauth2.md", output, StringComparison.Ordinal);
        Assert.Contains("intents/intent-cli/intent-tree/means/device-code.md", output, StringComparison.Ordinal);
        Assert.Contains("- AUTH-01", output, StringComparison.Ordinal);
        Assert.DoesNotContain("payments.md", output, StringComparison.Ordinal);

        var conceptSource = File.ReadAllText(Path.Combine(repoRoot, "intents", "intent-cli", "concepts", "oauth2.md"));
        Assert.Contains("- execution_unit: AUTH-01", conceptSource, StringComparison.Ordinal);
        Assert.Contains("- target_part: concepts", conceptSource, StringComparison.Ordinal);
        Assert.Contains("- readiness_notes: Current heading: # Auth Concept", conceptSource, StringComparison.Ordinal);
        Assert.Contains("- verification_hints: dotnet test IntentSystem.sln", conceptSource, StringComparison.Ordinal);

        var meansSource = File.ReadAllText(Path.Combine(repoRoot, "intents", "intent-cli", "intent-tree", "means", "device-code.md"));
        Assert.Contains("- execution_unit: AUTH-02", meansSource, StringComparison.Ordinal);
        Assert.Contains("- target_part: intent-tree/means", meansSource, StringComparison.Ordinal);
        Assert.Contains("- dependencies: AUTH-01", meansSource, StringComparison.Ordinal);

        var untouchedSource = File.ReadAllText(Path.Combine(repoRoot, "intents", "intent-cli", "intent-tree", "means", "payments.md"));
        Assert.DoesNotContain("execution_unit:", untouchedSource, StringComparison.Ordinal);
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
            Path.Combine("repo", "intents", "intent-cli", "concepts", "oauth2.md"),
            "# Auth Concept");
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
