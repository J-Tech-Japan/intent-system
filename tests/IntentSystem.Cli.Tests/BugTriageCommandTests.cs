using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

[Collection(RunSubmitCommandCollection.Name)]
public sealed class BugTriageCommandTests
{
    [Fact]
    public void Execute_GivenResolvedExecutionRoots_WritesBugTriageArtifact()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "bugs", "BUG-123.report.yaml"),
            BugReportArtifactYaml.Serialize(CreateBugReportArtifact(["G25"], ["https://github.com/J-Tech-Japan/intent-system/pull/180#issuecomment-1"])));
        tempDirectory.CreateFile(Path.Combine("repo", ".intent-cli", "issues", "G25", "implementation.md"), "# Implementation");
        tempDirectory.CreateFile(Path.Combine("repo", ".intent-cli", "issues", "G25", "review-context.md"), "# Review Context");
        tempDirectory.CreateFile(Path.Combine("repo", ".intent-cli", "issues", "G25", "packet.yaml"), "execution_unit: G25");
        using var writer = new StringWriter();

        var exitCode = BugTriageCommand.Execute(CreateContext(repoRoot), ["BUG-123"], writer);

        Assert.Equal(0, exitCode);
        Assert.Contains("Bug triage artifact generated for 'BUG-123'.", writer.ToString(), StringComparison.Ordinal);
        Assert.Contains("Classification: implementation-and-intent-impact", writer.ToString(), StringComparison.Ordinal);

        var artifact = BugTriageArtifactYaml.Deserialize(
            File.ReadAllText(Path.Combine(repoRoot, ".intent-cli", "bugs", "BUG-123.triage.yaml")));
        Assert.Equal("BUG-123", artifact.BugId);
        Assert.Equal(".intent-cli/bugs/BUG-123.report.yaml", artifact.ReportRef);
        Assert.Equal("implementation-and-intent-impact", artifact.Classification);
        Assert.Equal("implementation-and-intent-repair", artifact.DownstreamAction);
        Assert.False(artifact.ClarificationRequired);
        Assert.Equal(["G25"], artifact.ResolvedExecutionUnits);
        Assert.Equal([".intent-cli/issues/G25/implementation.md"], artifact.ResolvedImplementationRefs);
        Assert.Equal([".intent-cli/issues/G25/review-context.md"], artifact.ResolvedReviewContextRefs);
        Assert.Equal([".intent-cli/issues/G25/packet.yaml"], artifact.ResolvedPacketRefs);
        Assert.Equal(["G25"], artifact.ImplementationRepairCandidates);
        Assert.Equal(
            ["intents/intent-cli/means/auth.md", "intents/intent-cli/specs/12-bug-fix-and-intent-repair.md"],
            artifact.IntentRepairCandidates);
    }

    [Fact]
    public void Execute_GivenUnreconstructableRoot_ReturnsClarificationFirstArtifact()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "bugs", "BUG-124.report.yaml"),
            BugReportArtifactYaml.Serialize(
                new BugReportArtifact
                {
                    DomainSlug = "auth",
                    BugId = "BUG-124",
                    Title = "OAuth callback loop",
                    ReportSource = "from-file",
                    ProblemStatement = "Observed callback loop after login.",
                    SuspectedFailureLocus = "Observed callback loop after login.",
                    OriginalInstructionRefs = [],
                    AffectedIntentRefs = [],
                    AffectedRuleSpecRefs = [],
                    ClarificationCandidates = [],
                    LinkedExecutionUnits = ["G77"],
                    LinkedIssueRefs = [],
                    LinkedPrRefs = [],
                    LinkedReviewRefs = []
                }));
        using var writer = new StringWriter();

        var exitCode = BugTriageCommand.Execute(CreateContext(repoRoot), ["BUG-124"], writer);

        Assert.Equal(0, exitCode);

        var artifact = BugTriageArtifactYaml.Deserialize(
            File.ReadAllText(Path.Combine(repoRoot, ".intent-cli", "bugs", "BUG-124.triage.yaml")));
        Assert.Equal("clarification-first", artifact.Classification);
        Assert.Equal("clarification-first", artifact.DownstreamAction);
        Assert.True(artifact.ClarificationRequired);
        Assert.Equal(["G77"], artifact.UnresolvedExecutionUnits);
        Assert.Contains(
            "original instruction root could not be reconstructed from current bug report artifact and linked packet/review refs.",
            artifact.ClarificationReasons,
            StringComparer.Ordinal);
    }

    [Fact]
    public void Execute_GivenMissingBugReportArtifact_ReturnsExitCodeOne()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        using var writer = new StringWriter();

        var exitCode = BugTriageCommand.Execute(CreateContext(repoRoot), ["BUG-999"], writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("Bug report artifact was not found", writer.ToString(), StringComparison.Ordinal);
    }

    private static BugReportArtifact CreateBugReportArtifact(IReadOnlyList<string> linkedExecutionUnits, IReadOnlyList<string> linkedReviewRefs)
    {
        return new BugReportArtifact
        {
            DomainSlug = "auth",
            BugId = "BUG-123",
            Title = "OAuth callback loop",
            ReportSource = "from-file",
            ProblemStatement = "Observed callback loop after login.",
            SuspectedFailureLocus = "Observed callback loop after login.",
            OriginalInstructionRefs = ["ICL.P.PRODUCT_GOAL"],
            AffectedIntentRefs = ["intents/intent-cli/means/auth.md"],
            AffectedRuleSpecRefs = ["intents/intent-cli/specs/12-bug-fix-and-intent-repair.md"],
            ClarificationCandidates = ["Should provider retry reuse callback state token?"],
            LinkedExecutionUnits = linkedExecutionUnits,
            LinkedIssueRefs = ["https://github.com/J-Tech-Japan/intent-system/issues/178"],
            LinkedPrRefs = ["https://github.com/J-Tech-Japan/intent-system/pull/180"],
            LinkedReviewRefs = linkedReviewRefs
        };
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
        private readonly string rootPath = Directory.CreateTempSubdirectory("intent-cli-bug-triage-tests-").FullName;

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
