using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;
using IntentSystem.Supervisor.Models;

namespace IntentSystem.Cli.Tests;

public sealed class BugImplementationIssueCommandTests
{
    [Fact]
    public void Execute_GivenReadyImplementationRepair_CreatesSingleChildIssueAndWritesArtifact()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
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

        try
        {
            BugImplementationIssueCommand.PublisherFactory = () => new FakePublisher();
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

    private static string CreatePacketYaml()
    {
        return """
        implementation_issue_packet:
          issue_title: "[G25] Repair callback flow"
          issue_kind: "bugfix"
          source_execution_unit: "G25"
          goal: "Repair callback flow."
          in_scope:
            - "callback flow"
          out_of_scope:
            - "review flow"
          target_repo: "submodules/intent-system"
          target_path: "."
          target_part: "auth callback"
          dependencies: []
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
            - "repair issue created"
          verification_evidence:
            - "tests-passing"
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
                    WorkflowEngine = "intent-cli",
                    ArtifactRoot = ".intent-cli"
                }
            }
        };
    }

    private sealed class FakePublisher : IQueueDispatchPublisher
    {
        public LinkedIssue CreateIssue(string targetRepo, string title, string body)
        {
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
