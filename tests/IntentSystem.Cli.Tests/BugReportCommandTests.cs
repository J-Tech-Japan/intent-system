using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

[Collection(RunSubmitCommandCollection.Name)]
public sealed class BugReportCommandTests
{
    [Fact]
    public void Execute_GivenPreparedProblemStatementAndRefs_WritesBugReportArtifact()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateFile(
            Path.Combine("repo", "prepared", "bug.md"),
            "Observed callback loop after login." + Environment.NewLine + "Affects GitHub provider path.");
        using var writer = new StringWriter();

        var exitCode = BugReportCommand.Execute(
            CreateContext(repoRoot),
            [
                "auth",
                "BUG-123",
                "--title", "OAuth callback loop",
                "--from-file", "prepared/bug.md",
                "--instruction-refs", "ICL.P.PRODUCT_GOAL,intents/rules/provider-interruption-and-retry.md",
                "--affected-intent-refs", "intents/intent-cli/means/auth.md,intents/intent-cli/README.md",
                "--affected-rule-spec-refs", "intents/intent-cli/specs/12-bug-fix-and-intent-repair.md",
                "--clarification-candidates", "Should provider retry reuse callback state token?,Should callback token be invalidated on second pass?",
                "--execution-units", "G25,G77",
                "--issues", "https://github.com/J-Tech-Japan/intent-system/issues/178",
                "--prs", "https://github.com/J-Tech-Japan/intent-system/pull/180",
                "--reviews", "https://github.com/J-Tech-Japan/intent-system/pull/180#issuecomment-1"
            ],
            writer);

        Assert.Equal(0, exitCode);
        Assert.Contains("Bug report artifact generated for domain 'auth'.", writer.ToString(), StringComparison.Ordinal);
        Assert.Contains("Bug ID: BUG-123", writer.ToString(), StringComparison.Ordinal);

        var artifact = BugReportArtifactYaml.Deserialize(
            File.ReadAllText(Path.Combine(repoRoot, ".intent-cli", "bugs", "BUG-123.report.yaml")));
        Assert.Equal("auth", artifact.DomainSlug);
        Assert.Equal("BUG-123", artifact.BugId);
        Assert.Equal("OAuth callback loop", artifact.Title);
        Assert.Equal("from-file", artifact.ReportSource);
        Assert.Equal(
            "Observed callback loop after login." + Environment.NewLine + "Affects GitHub provider path.",
            artifact.ProblemStatement);
        Assert.Equal("Observed callback loop after login.", artifact.SuspectedFailureLocus);
        Assert.Equal(["ICL.P.PRODUCT_GOAL", "intents/rules/provider-interruption-and-retry.md"], artifact.OriginalInstructionRefs);
        Assert.Equal(["intents/intent-cli/means/auth.md", "intents/intent-cli/README.md"], artifact.AffectedIntentRefs);
        Assert.Equal(["intents/intent-cli/specs/12-bug-fix-and-intent-repair.md"], artifact.AffectedRuleSpecRefs);
        Assert.Equal(
            ["Should provider retry reuse callback state token?", "Should callback token be invalidated on second pass?"],
            artifact.ClarificationCandidates);
        Assert.Equal(["G25", "G77"], artifact.LinkedExecutionUnits);
        Assert.Equal(["https://github.com/J-Tech-Japan/intent-system/issues/178"], artifact.LinkedIssueRefs);
        Assert.Equal(["https://github.com/J-Tech-Japan/intent-system/pull/180"], artifact.LinkedPrRefs);
        Assert.Equal(["https://github.com/J-Tech-Japan/intent-system/pull/180#issuecomment-1"], artifact.LinkedReviewRefs);
    }

    [Fact]
    public void Execute_GivenMissingProblemStatementFile_ReturnsExitCodeOne()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        using var writer = new StringWriter();

        var exitCode = BugReportCommand.Execute(
            CreateContext(repoRoot),
            ["auth", "BUG-123", "--title", "OAuth callback loop", "--from-file", "prepared/missing.md"],
            writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("Prepared problem statement file was not found", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_GivenMissingRequiredFlags_ReturnsExitCodeOne()
    {
        using var writer = new StringWriter();

        var exitCode = BugReportCommand.Execute(CreateContext("/tmp/intent-system"), ["auth", "BUG-123"], writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("requires '<domain> <bug-id> --title <text> --from-file <path>'", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_GivenSuspectedFailureLocusFlag_UsesExplicitValue()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateFile(Path.Combine("repo", "prepared", "bug.md"), "Observed callback loop after login.");
        using var writer = new StringWriter();

        var exitCode = BugReportCommand.Execute(
            CreateContext(repoRoot),
            [
                "auth",
                "BUG-123",
                "--title", "OAuth callback loop",
                "--from-file", "prepared/bug.md",
                "--suspected-failure-locus", "explicit fallback locus"
            ],
            writer);

        Assert.Equal(0, exitCode);

        var artifact = BugReportArtifactYaml.Deserialize(
            File.ReadAllText(Path.Combine(repoRoot, ".intent-cli", "bugs", "BUG-123.report.yaml")));
        Assert.Equal("explicit fallback locus", artifact.SuspectedFailureLocus);
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
        private readonly string rootPath = Directory.CreateTempSubdirectory("intent-cli-bug-report-tests-").FullName;

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
