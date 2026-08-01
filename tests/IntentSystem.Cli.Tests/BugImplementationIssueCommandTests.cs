using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;
using IntentSystem.Supervisor.Models;

namespace IntentSystem.Cli.Tests;

// G569 audit: joins the non-parallel collection that already owns the
// process-global statics this class assigns, so it can no longer interleave
// with the other class that assigns them.
[Collection(RunSubmitCommandCollection.Name)]
public sealed class BugImplementationIssueCommandTests
{
    [Fact]
    public void Execute_GivenReadyImplementationRepair_CreatesSingleChildIssueAndWritesArtifact()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "bugs", "BUG-123.report.yaml"),
            BugReportArtifactYaml.Serialize(CreateBugReportArtifact()));
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "bugs", "BUG-123.triage.yaml"),
            BugTriageArtifactYaml.Serialize(CreateBugTriageArtifact()));
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "bugs", "BUG-123.plan.yaml"),
            BugExecutionArtifactYaml.Serialize(CreateBugExecutionArtifact()));
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "bugs", "BUG-123.implementation-repair.yaml"),
            BugImplementationRepairArtifactYaml.Serialize(CreateRepairArtifact(readyToIssueCut: true)));
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "issues", "G25", "packet.yaml"),
            CreatePacketYaml());
        tempDirectory.CreateDirectory(Path.Combine("repo", "submodules", "intent-system"));
        using var writer = new StringWriter();
        var originalPublisherFactory = BugImplementationIssueCommand.PublisherFactory;
        var originalGitRunnerFactory = BugImplementationIssueCommand.GitCommandRunnerFactory;
        var publisher = new CapturingPublisher();

        try
        {
            BugImplementationIssueCommand.PublisherFactory = () => publisher;
            BugImplementationIssueCommand.GitCommandRunnerFactory = () => new FakeGitCommandRunner();

            var exitCode = BugImplementationIssueCommand.Execute(CreateContext(repoRoot), ["BUG-123"], writer);

            Assert.Equal(0, exitCode);
            Assert.Contains("Bug implementation-issue artifact generated for 'BUG-123'.", writer.ToString(), StringComparison.Ordinal);

            var artifact = BugImplementationIssueArtifactYaml.Deserialize(
                File.ReadAllText(Path.Combine(repoRoot, ".intent-cli", "bugs", "BUG-123.implementation-issue.yaml")));
            Assert.Equal(".intent-cli/bugs/BUG-123.implementation-repair.yaml", artifact.ImplementationRepairRef);
            Assert.Equal("Implementation repair: OAuth callback loop (BUG-123)", artifact.CreatedIssueTitle);
            Assert.Equal("https://github.com/J-Tech-Japan/intent-system/issues/53", artifact.CreatedIssueUrl);
            Assert.Equal(53, artifact.CreatedIssueNumber);
            Assert.Equal([".intent-cli/issues/G25/packet.yaml"], artifact.ImplementationRepairTargets);

            Assert.Equal("J-Tech-Japan/intent-system", publisher.TargetRepo);
            Assert.Equal("Implementation repair: OAuth callback loop (BUG-123)", publisher.Title);
            Assert.NotNull(publisher.Body);
            Assert.Contains("## Goal", publisher.Body, StringComparison.Ordinal);
            Assert.Contains("## Background", publisher.Body, StringComparison.Ordinal);
            Assert.Contains("## Reproduction or Current Observed State", publisher.Body, StringComparison.Ordinal);
            Assert.Contains("## Actual Result", publisher.Body, StringComparison.Ordinal);
            Assert.Contains("## Expected Repaired Result", publisher.Body, StringComparison.Ordinal);
            Assert.Contains("## Narrowest Acceptable Repair Direction", publisher.Body, StringComparison.Ordinal);
            Assert.Contains("## Accepted Baseline You May Assume", publisher.Body, StringComparison.Ordinal);
            Assert.Contains("## Dependencies", publisher.Body, StringComparison.Ordinal);
            Assert.Contains("## Target Repo / Path / Part", publisher.Body, StringComparison.Ordinal);
            Assert.Contains("## In Scope", publisher.Body, StringComparison.Ordinal);
            Assert.Contains("## Out Of Scope", publisher.Body, StringComparison.Ordinal);
            Assert.Contains("## Acceptance Criteria", publisher.Body, StringComparison.Ordinal);
            Assert.Contains("## Verification", publisher.Body, StringComparison.Ordinal);
            Assert.Contains("## Relevant Links", publisher.Body, StringComparison.Ordinal);
            Assert.Contains("Problem statement: Observed callback loop after login.", publisher.Body, StringComparison.Ordinal);
            Assert.Contains("Suspected failure locus: OAuth callback state is not reset after the redirect.", publisher.Body, StringComparison.Ordinal);
            Assert.Contains("Repair callback flow so the OAuth redirect completes exactly once without looping.", publisher.Body, StringComparison.Ordinal);
            Assert.Contains("repo 'submodules/intent-system', path '.', part 'auth callback'.", publisher.Body, StringComparison.Ordinal);
            Assert.Contains("- G24", publisher.Body, StringComparison.Ordinal);
            Assert.Contains("- C# / .NET", publisher.Body, StringComparison.Ordinal);
            Assert.Contains("- AGENTS.md", publisher.Body, StringComparison.Ordinal);
            Assert.Contains("- keep repair deterministic", publisher.Body, StringComparison.Ordinal);
            Assert.Contains("- ICL.P.PRODUCT_GOAL", publisher.Body, StringComparison.Ordinal);
            Assert.Contains("- intents/rules/issue-lifecycle-and-landing.md", publisher.Body, StringComparison.Ordinal);
            Assert.Contains("accept callback responses without looping", publisher.Body, StringComparison.Ordinal);
            Assert.Contains("dotnet test IntentSystem.sln --nologo", publisher.Body, StringComparison.Ordinal);
        }
        finally
        {
            BugImplementationIssueCommand.PublisherFactory = originalPublisherFactory;
            BugImplementationIssueCommand.GitCommandRunnerFactory = originalGitRunnerFactory;
        }
    }

    [Fact]
    public void Execute_GivenNotReadyImplementationRepair_DoesNotCreateIssue()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "bugs", "BUG-124.implementation-repair.yaml"),
            BugImplementationRepairArtifactYaml.Serialize(CreateRepairArtifact(bugId: "BUG-124", readyToIssueCut: false)));
        using var writer = new StringWriter();
        var originalPublisherFactory = BugImplementationIssueCommand.PublisherFactory;

        try
        {
            BugImplementationIssueCommand.PublisherFactory = () => new ThrowingPublisher();

            var exitCode = BugImplementationIssueCommand.Execute(CreateContext(repoRoot), ["BUG-124"], writer);

            Assert.Equal(0, exitCode);

            var artifact = BugImplementationIssueArtifactYaml.Deserialize(
                File.ReadAllText(Path.Combine(repoRoot, ".intent-cli", "bugs", "BUG-124.implementation-issue.yaml")));
            Assert.Equal("Implementation repair: OAuth callback loop (BUG-124)", artifact.CreatedIssueTitle);
            Assert.Null(artifact.CreatedIssueUrl);
            Assert.Null(artifact.CreatedIssueNumber);
            Assert.Empty(artifact.ImplementationRepairTargets);
        }
        finally
        {
            BugImplementationIssueCommand.PublisherFactory = originalPublisherFactory;
        }
    }

    [Fact]
    public void Execute_GivenMissingImplementationRepairArtifact_ReturnsExitCodeOne()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        using var writer = new StringWriter();

        var exitCode = BugImplementationIssueCommand.Execute(CreateContext(repoRoot), ["BUG-123"], writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("Bug implementation-repair artifact was not found", writer.ToString(), StringComparison.Ordinal);
    }

    private static BugImplementationRepairArtifact CreateRepairArtifact(string bugId = "BUG-123", bool readyToIssueCut = true)
    {
        return new BugImplementationRepairArtifact
        {
            BugId = bugId,
            ExecutionRef = $".intent-cli/bugs/{bugId}.plan.yaml",
            ImplementationTaskCandidates = readyToIssueCut ? ["G25"] : ["G25"],
            ImplementationRepairTargets = readyToIssueCut ? [".intent-cli/issues/G25/packet.yaml"] : [],
            SuggestedIssueTitle = $"Implementation repair: OAuth callback loop ({bugId})",
            SuggestedGoal = readyToIssueCut
                ? $"Repair child implementation targets for 'OAuth callback loop' ({bugId}) using .intent-cli/bugs/{bugId}.plan.yaml: .intent-cli/issues/G25/packet.yaml"
                : $"Prepare child implementation repair for 'OAuth callback loop' ({bugId}) once issue-cut blockers are cleared from .intent-cli/bugs/{bugId}.plan.yaml.",
            ReadyToIssueCut = readyToIssueCut
        };
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
            SuspectedFailureLocus = "OAuth callback state is not reset after the redirect.",
            OriginalInstructionRefs = ["ICL.P.PRODUCT_GOAL"],
            AffectedIntentRefs = ["intents/intent-cli/means/auth.md"],
            AffectedRuleSpecRefs = ["intents/intent-cli/specs/12-bug-fix-and-intent-repair.md"],
            ClarificationCandidates = ["Should the callback token be consumed on first successful redirect?"],
            LinkedExecutionUnits = ["G25"],
            LinkedIssueRefs = ["https://github.com/J-Tech-Japan/intent-system/issues/369"],
            LinkedPrRefs = ["https://github.com/J-Tech-Japan/intent-system/pull/370"],
            LinkedReviewRefs = ["https://github.com/J-Tech-Japan/intent-system/pull/180#issuecomment-1"]
        };
    }

    private static BugTriageArtifact CreateBugTriageArtifact(string bugId = "BUG-123")
    {
        return new BugTriageArtifact
        {
            BugId = bugId,
            ReportRef = $".intent-cli/bugs/{bugId}.report.yaml",
            TriageClassification = "implementation-mismatch",
            DownstreamAction = "dual-track",
            ClarificationRequired = false,
            ClarificationReasons = [],
            OriginalInstructionRootRefs = ["ICL.P.PRODUCT_GOAL"],
            LinkedReviewRefs = ["https://github.com/J-Tech-Japan/intent-system/pull/180#issuecomment-1"],
            ResolvedExecutionUnits = ["G25"],
            ResolvedImplementationRefs = [".intent-cli/issues/G25/implementation.md"],
            ResolvedReviewContextRefs = [".intent-cli/issues/G25/review-context.md"],
            ResolvedPacketRefs = [".intent-cli/issues/G25/packet.yaml"],
            UnresolvedExecutionUnits = [],
            ImplementationRepairCandidates = ["G25"],
            IntentRepairCandidates = ["intents/intent-cli/means/auth.md"]
        };
    }

    private static BugExecutionArtifact CreateBugExecutionArtifact(string bugId = "BUG-123")
    {
        return new BugExecutionArtifact
        {
            BugId = bugId,
            ReportRef = $".intent-cli/bugs/{bugId}.report.yaml",
            TriageRef = $".intent-cli/bugs/{bugId}.triage.yaml",
            DownstreamAction = "dual-track",
            ResolvedImplementationRefs = [".intent-cli/issues/G25/implementation.md"],
            ResolvedReviewContextRefs = [".intent-cli/issues/G25/review-context.md"],
            ResolvedPacketRefs = [".intent-cli/issues/G25/packet.yaml"],
            ImplementationTaskCandidates = ["G25"],
            IntentTaskCandidates = ["intents/intent-cli/means/auth.md"],
            ClarificationRequired = false,
            ReadyToLaunch = true
        };
    }

    private static string CreatePacketYaml()
    {
        return """
        implementation_issue_packet:
          issue_title: "[G25] Repair callback flow"
          issue_kind: "bugfix"
          source_execution_unit: "G25"
          goal: "Repair callback flow so the OAuth redirect completes exactly once without looping."
          in_scope:
            - "callback flow"
            - "OAuth redirect state handling"
          out_of_scope:
            - "review flow"
          target_repo: "submodules/intent-system"
          target_path: "."
          target_part: "auth callback"
          dependencies:
            - "G24"
          technical_baseline:
            - "C# / .NET"
          project_local_guide:
            - "AGENTS.md"
          intent_baseline:
            - "keep repair deterministic"
          intent_references:
            - "ICL.P.PRODUCT_GOAL"
          rules_and_specs:
            - "intents/rules/issue-lifecycle-and-landing.md"
          acceptance_criteria:
            - "accept callback responses without looping"
          verification_evidence:
            - "dotnet test IntentSystem.sln --nologo"
          review_mode: "deterministic-review"
          completion_action: "wait-for-deterministic-review"
          landing_policy: "merge-after-review"

        review_context_packet:
          source_execution_unit: "G25"
          parent_intent_root: "intents/intent-cli/intent-tree/00-map.md"
          intent_references:
            - "ICL.P.PRODUCT_GOAL"
          rules_and_specs:
            - "intents/rules/issue-lifecycle-and-landing.md"
          acceptance_criteria:
            - "repair issue created"
          deterministic_review_checks:
            - "repair remains thin"
          clarification_return_path: "intents/intent-cli/clarifications/open.md"
        """;
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

    private sealed class CapturingPublisher : IQueueDispatchPublisher
    {
        public string? TargetRepo { get; private set; }

        public string? Title { get; private set; }

        public string? Body { get; private set; }

        public LinkedIssue CreateIssue(string targetRepo, string title, string body)
        {
            TargetRepo = targetRepo;
            Title = title;
            Body = body;
            return new LinkedIssue
            {
                Repo = targetRepo,
                Number = 53,
                Url = "https://github.com/J-Tech-Japan/intent-system/issues/53"
            };
        }
    }

    private sealed class ThrowingPublisher : IQueueDispatchPublisher
    {
        public LinkedIssue CreateIssue(string targetRepo, string title, string body)
        {
            throw new InvalidOperationException("CreateIssue should not be called when ready_to_issue_cut is false.");
        }
    }

    private sealed class FakeGitCommandRunner : IGitRemoteCommandRunner
    {
        public GitRemoteCommandResult Run(string workingDirectory, IReadOnlyList<string> arguments)
        {
            return new GitRemoteCommandResult
            {
                ExitCode = 0,
                StdOut = "git@github.com:J-Tech-Japan/intent-system.git" + Environment.NewLine,
                StdErr = string.Empty
            };
        }
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        private readonly string rootPath = Directory.CreateTempSubdirectory("intent-cli-bug-implementation-issue-tests-").FullName;

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
