using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

[Collection(RunSubmitCommandCollection.Name)]
public sealed class BugExecutionCommandTests
{
    [Fact]
    public void Execute_GivenDualTrackTriage_WritesExecutionArtifact()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "bugs", "BUG-123.report.yaml"),
            BugReportArtifactYaml.Serialize(CreateBugReportArtifact()));
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "bugs", "BUG-123.triage.yaml"),
            BugTriageArtifactYaml.Serialize(CreateBugTriageArtifact(
                downstreamAction: "dual-track",
                clarificationRequired: false)));
        using var writer = new StringWriter();

        var exitCode = BugExecutionCommand.Execute(CreateContext(repoRoot), ["BUG-123"], writer);

        Assert.Equal(0, exitCode);
        Assert.Contains("Bug plan artifact generated for 'BUG-123'.", writer.ToString(), StringComparison.Ordinal);
        Assert.Contains("Ready to launch: true", writer.ToString(), StringComparison.Ordinal);

        var artifact = BugExecutionArtifactYaml.Deserialize(
            File.ReadAllText(Path.Combine(repoRoot, ".intent-cli", "bugs", "BUG-123.plan.yaml")));
        Assert.Equal("BUG-123", artifact.BugId);
        Assert.Equal(".intent-cli/bugs/BUG-123.report.yaml", artifact.ReportRef);
        Assert.Equal(".intent-cli/bugs/BUG-123.triage.yaml", artifact.TriageRef);
        Assert.Equal("dual-track", artifact.DownstreamAction);
        Assert.False(artifact.ClarificationRequired);
        Assert.True(artifact.ReadyToLaunch);
        Assert.Equal([".intent-cli/issues/G25/implementation.md"], artifact.ResolvedImplementationRefs);
        Assert.Equal([".intent-cli/issues/G25/review-context.md"], artifact.ResolvedReviewContextRefs);
        Assert.Equal([".intent-cli/issues/G25/packet.yaml"], artifact.ResolvedPacketRefs);
        Assert.Equal(["G25"], artifact.ImplementationTaskCandidates);
        Assert.Equal(
            ["intents/intent-cli/means/auth.md", "intents/intent-cli/specs/12-bug-fix-and-intent-repair.md"],
            artifact.IntentTaskCandidates);
    }

    [Fact]
    public void Execute_GivenClarificationRequired_DoesNotInventTaskCandidates()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "bugs", "BUG-124.report.yaml"),
            BugReportArtifactYaml.Serialize(CreateBugReportArtifact()));
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "bugs", "BUG-124.triage.yaml"),
            BugTriageArtifactYaml.Serialize(CreateBugTriageArtifact(
                downstreamAction: "clarification-first",
                clarificationRequired: true)));
        using var writer = new StringWriter();

        var exitCode = BugExecutionCommand.Execute(CreateContext(repoRoot), ["BUG-124"], writer);

        Assert.Equal(0, exitCode);

        var artifact = BugExecutionArtifactYaml.Deserialize(
            File.ReadAllText(Path.Combine(repoRoot, ".intent-cli", "bugs", "BUG-124.plan.yaml")));
        Assert.Equal("clarification-first", artifact.DownstreamAction);
        Assert.True(artifact.ClarificationRequired);
        Assert.False(artifact.ReadyToLaunch);
        Assert.Empty(artifact.ImplementationTaskCandidates);
        Assert.Empty(artifact.IntentTaskCandidates);
    }

    [Fact]
    public void Execute_GivenMissingBugTriageArtifact_ReturnsExitCodeOne()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "bugs", "BUG-123.report.yaml"),
            BugReportArtifactYaml.Serialize(CreateBugReportArtifact()));
        using var writer = new StringWriter();

        var exitCode = BugExecutionCommand.Execute(CreateContext(repoRoot), ["BUG-123"], writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("Bug triage artifact was not found", writer.ToString(), StringComparison.Ordinal);
    }

    private static BugReportArtifact CreateBugReportArtifact()
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
            AffectedIntentRefs = ["intents/intent-cli/means/auth.md", "intents/intent-cli/means/session.md"],
            AffectedRuleSpecRefs = ["intents/intent-cli/specs/12-bug-fix-and-intent-repair.md", "intents/intent-cli/specs/99-extra.md"],
            ClarificationCandidates = ["Should provider retry reuse callback state token?"],
            LinkedExecutionUnits = ["G25"],
            LinkedIssueRefs = [],
            LinkedPrRefs = [],
            LinkedReviewRefs = ["https://github.com/J-Tech-Japan/intent-system/pull/180#issuecomment-1"]
        };
    }

    private static BugTriageArtifact CreateBugTriageArtifact(string downstreamAction, bool clarificationRequired)
    {
        return new BugTriageArtifact
        {
            BugId = "BUG-123",
            ReportRef = ".intent-cli/bugs/BUG-123.report.yaml",
            TriageClassification = downstreamAction == "clarification-first" ? "unknown" : "implementation-mismatch",
            DownstreamAction = downstreamAction,
            ClarificationRequired = clarificationRequired,
            ClarificationReasons = clarificationRequired
                ? ["original instruction root could not be reconstructed from current bug report artifact and linked packet/review refs."]
                : [],
            OriginalInstructionRootRefs = ["ICL.P.PRODUCT_GOAL"],
            LinkedReviewRefs = ["https://github.com/J-Tech-Japan/intent-system/pull/180#issuecomment-1"],
            ResolvedExecutionUnits = ["G25"],
            ResolvedImplementationRefs = [".intent-cli/issues/G25/implementation.md"],
            ResolvedReviewContextRefs = [".intent-cli/issues/G25/review-context.md"],
            ResolvedPacketRefs = [".intent-cli/issues/G25/packet.yaml"],
            UnresolvedExecutionUnits = [],
            ImplementationRepairCandidates = ["G25"],
            IntentRepairCandidates = ["intents/intent-cli/means/auth.md", "intents/intent-cli/specs/12-bug-fix-and-intent-repair.md"]
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
        private readonly string rootPath = Directory.CreateTempSubdirectory("intent-cli-bug-execution-tests-").FullName;

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
