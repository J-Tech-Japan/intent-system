using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

[Collection(RunSubmitCommandCollection.Name)]
public sealed class BugIntentRepairCommandTests
{
    [Fact]
    public void Execute_GivenDualTrackExecution_WritesIntentRepairArtifact()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "bugs", "BUG-123.report.yaml"),
            BugReportArtifactYaml.Serialize(CreateBugReportArtifact()));
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "bugs", "BUG-123.triage.yaml"),
            BugTriageArtifactYaml.Serialize(CreateBugTriageArtifact("dual-track", clarificationRequired: false)));
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "bugs", "BUG-123.plan.yaml"),
            BugExecutionArtifactYaml.Serialize(CreateBugExecutionArtifact("dual-track")));
        using var writer = new StringWriter();

        var exitCode = BugIntentRepairCommand.Execute(CreateContext(repoRoot), ["BUG-123"], writer);

        Assert.Equal(0, exitCode);
        Assert.Contains("Bug intent-repair artifact generated for 'BUG-123'.", writer.ToString(), StringComparison.Ordinal);
        Assert.Contains("Ready to issue cut: true", writer.ToString(), StringComparison.Ordinal);

        var artifact = BugIntentRepairArtifactYaml.Deserialize(
            File.ReadAllText(Path.Combine(repoRoot, ".intent-cli", "bugs", "BUG-123.intent-repair.yaml")));
        Assert.Equal("BUG-123", artifact.BugId);
        Assert.Equal(".intent-cli/bugs/BUG-123.plan.yaml", artifact.ExecutionRef);
        Assert.Equal(
            ["intents/intent-cli/means/auth.md", "intents/intent-cli/specs/12-bug-fix-and-intent-repair.md"],
            artifact.IntentTaskCandidates);
        Assert.Equal(
            ["intent:intents/intent-cli/means/auth.md", "rule-spec:intents/intent-cli/specs/12-bug-fix-and-intent-repair.md"],
            artifact.ParentRepairTargets);
        Assert.Equal("Intent repair: OAuth callback loop (BUG-123)", artifact.SuggestedIssueTitle);
        Assert.Equal(
            "Repair parent intent targets for 'OAuth callback loop' (BUG-123) using .intent-cli/bugs/BUG-123.plan.yaml: intent:intents/intent-cli/means/auth.md, rule-spec:intents/intent-cli/specs/12-bug-fix-and-intent-repair.md",
            artifact.SuggestedGoal);
        Assert.True(artifact.ReadyToIssueCut);
    }

    [Fact]
    public void Execute_GivenImplementationOnlyTriage_ReturnsNotReadyWithoutParentTargets()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "bugs", "BUG-124.report.yaml"),
            BugReportArtifactYaml.Serialize(CreateBugReportArtifact(bugId: "BUG-124")));
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "bugs", "BUG-124.triage.yaml"),
            BugTriageArtifactYaml.Serialize(CreateBugTriageArtifact("implementation-only", clarificationRequired: false, bugId: "BUG-124")));
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "bugs", "BUG-124.plan.yaml"),
            BugExecutionArtifactYaml.Serialize(CreateBugExecutionArtifact("implementation-only", bugId: "BUG-124")));
        using var writer = new StringWriter();

        var exitCode = BugIntentRepairCommand.Execute(CreateContext(repoRoot), ["BUG-124"], writer);

        Assert.Equal(0, exitCode);

        var artifact = BugIntentRepairArtifactYaml.Deserialize(
            File.ReadAllText(Path.Combine(repoRoot, ".intent-cli", "bugs", "BUG-124.intent-repair.yaml")));
        Assert.False(artifact.ReadyToIssueCut);
        Assert.Empty(artifact.ParentRepairTargets);
        Assert.Equal(
            ["intents/intent-cli/means/auth.md", "intents/intent-cli/specs/12-bug-fix-and-intent-repair.md"],
            artifact.IntentTaskCandidates);
    }

    [Fact]
    public void Execute_GivenMissingBugExecutionArtifact_ReturnsExitCodeOne()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "bugs", "BUG-123.report.yaml"),
            BugReportArtifactYaml.Serialize(CreateBugReportArtifact()));
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "bugs", "BUG-123.triage.yaml"),
            BugTriageArtifactYaml.Serialize(CreateBugTriageArtifact("dual-track", clarificationRequired: false)));
        using var writer = new StringWriter();

        var exitCode = BugIntentRepairCommand.Execute(CreateContext(repoRoot), ["BUG-123"], writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("Bug plan artifact was not found", writer.ToString(), StringComparison.Ordinal);
    }

    private static BugReportArtifact CreateBugReportArtifact(string bugId = "BUG-123")
    {
        return new BugReportArtifact
        {
            DomainSlug = "auth",
            BugId = bugId,
            Title = "OAuth callback loop",
            ReportSource = "from-file",
            ProblemStatement = "Observed callback loop after login.",
            SuspectedFailureLocus = "Observed callback loop after login.",
            OriginalInstructionRefs = ["ICL.P.PRODUCT_GOAL"],
            AffectedIntentRefs = ["intents/intent-cli/means/auth.md"],
            AffectedRuleSpecRefs = ["intents/intent-cli/specs/12-bug-fix-and-intent-repair.md"],
            ClarificationCandidates = ["Should provider retry reuse callback state token?"],
            LinkedExecutionUnits = ["G25"],
            LinkedIssueRefs = [],
            LinkedPrRefs = [],
            LinkedReviewRefs = ["https://github.com/J-Tech-Japan/intent-system/pull/180#issuecomment-1"]
        };
    }

    private static BugTriageArtifact CreateBugTriageArtifact(string downstreamAction, bool clarificationRequired, string bugId = "BUG-123")
    {
        return new BugTriageArtifact
        {
            BugId = bugId,
            ReportRef = $".intent-cli/bugs/{bugId}.report.yaml",
            TriageClassification = downstreamAction == "implementation-only" ? "implementation-mismatch" : "intent-gap",
            DownstreamAction = downstreamAction,
            ClarificationRequired = clarificationRequired,
            ClarificationReasons = clarificationRequired ? ["Need clarification."] : [],
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

    private static BugExecutionArtifact CreateBugExecutionArtifact(string downstreamAction, string bugId = "BUG-123")
    {
        return new BugExecutionArtifact
        {
            BugId = bugId,
            ReportRef = $".intent-cli/bugs/{bugId}.report.yaml",
            TriageRef = $".intent-cli/bugs/{bugId}.triage.yaml",
            DownstreamAction = downstreamAction,
            ResolvedImplementationRefs = [".intent-cli/issues/G25/implementation.md"],
            ResolvedReviewContextRefs = [".intent-cli/issues/G25/review-context.md"],
            ResolvedPacketRefs = [".intent-cli/issues/G25/packet.yaml"],
            ImplementationTaskCandidates = ["G25"],
            IntentTaskCandidates = ["intents/intent-cli/means/auth.md", "intents/intent-cli/specs/12-bug-fix-and-intent-repair.md"],
            ClarificationRequired = false,
            ReadyToLaunch = true
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
                    ArtifactRoot = ".intent-cli"
                }
            }
        };
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        private readonly string rootPath = Directory.CreateTempSubdirectory("intent-cli-bug-intent-repair-tests-").FullName;

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
