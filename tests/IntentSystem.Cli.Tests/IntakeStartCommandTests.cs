using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;
using IntentSystem.Projection.Serialization;
using IntentSystem.Review;
using IntentSystem.Supervisor.Models;
using IntentSystem.Supervisor.Serialization;

namespace IntentSystem.Cli.Tests;

[Collection(RunSubmitCommandCollection.Name)]
public sealed class IntakeStartCommandTests
{
    [Fact]
    public void Execute_GivenIssueReadyDomain_GeneratesArtifactsAndStartsUnits()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateDirectory(Path.Combine("repo", "submodules", "intent-system"));
        tempDirectory.CreateFile(
            Path.Combine("repo", "intents", "intent-cli", "execution", "05-post-mvp-sub-slices.md"),
            CreateExecutionBaselineMarkdown());
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "intake", "auth.execution.md"),
            CreateExecutionArtifactMarkdown("auth"));
        tempDirectory.CreateFile(
            Path.Combine("repo", "intents", "intent-cli", "intent-tree", "00-map.md"),
            "# Intent CLI Map");
        tempDirectory.CreateFile(
            Path.Combine("repo", "intents", "intent-cli", "clarifications", "open.md"),
            "# Clarifications");
        tempDirectory.CreateFile(
            Path.Combine("repo", "intents", "intent-cli", "concepts", "oauth2.md"),
            "# Auth Concept");
        tempDirectory.CreateFile(
            Path.Combine("repo", "intents", "intent-cli", "intent-tree", "means", "device-code.md"),
            "# Device Code");
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "queue-state.json"),
            QueueStateSerializer.Serialize(CreateQueueState()));
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "runs.jsonl"),
            string.Empty);
        using var writer = new StringWriter();
        var originalEnqueueTimestampFactory = QueueEnqueueCommand.TimestampFactory;
        var originalPublisherFactory = QueueDispatchCommand.PublisherFactory;
        var originalRemoteGitFactory = QueueDispatchCommand.GitCommandRunnerFactory;
        var originalDispatchTimestampFactory = QueueDispatchCommand.TimestampFactory;
        var originalStartGitFactory = RunStartCommand.GitCommandRunnerFactory;
        var originalStartTimestampFactory = RunStartCommand.TimestampFactory;
        var startGitRunner = new FakeStartGitRunner();
        var publisher = new FakePublisher();

        try
        {
            QueueEnqueueCommand.TimestampFactory = () => DateTimeOffset.Parse("2026-04-06T11:00:00Z");
            QueueDispatchCommand.PublisherFactory = () => publisher;
            QueueDispatchCommand.GitCommandRunnerFactory = () => new FakeRemoteGitRunner();
            QueueDispatchCommand.TimestampFactory = () => DateTimeOffset.Parse("2026-04-06T11:05:00Z");
            RunStartCommand.GitCommandRunnerFactory = () => startGitRunner;
            RunStartCommand.TimestampFactory = () => DateTimeOffset.Parse("2026-04-06T11:10:00Z");

            var exitCode = IntakeStartCommand.Execute(CreateContext(repoRoot), ["auth"], writer);

            Assert.Equal(0, exitCode);
            var output = writer.ToString();
            Assert.Contains("Intake start processed for domain 'auth'.", output, StringComparison.Ordinal);
            Assert.Contains("Started execution units:", output, StringComparison.Ordinal);
            Assert.Contains("- AUTH-01", output, StringComparison.Ordinal);
            Assert.Contains("- AUTH-02", output, StringComparison.Ordinal);
            Assert.Contains(".intent-cli/issues/AUTH-01/github-body.md", output, StringComparison.Ordinal);
            Assert.Contains("- https://github.com/J-Tech-Japan/intent-system/issues/501", output, StringComparison.Ordinal);
            Assert.Contains("- https://github.com/J-Tech-Japan/intent-system/issues/502", output, StringComparison.Ordinal);

            Assert.True(File.Exists(Path.Combine(repoRoot, ".intent-cli", "issues", "AUTH-01", "packet.yaml")));
            Assert.True(File.Exists(Path.Combine(repoRoot, ".intent-cli", "issues", "AUTH-02", "github-body.md")));
            var packet = ProjectionPacketSerializer.Deserialize(
                File.ReadAllText(Path.Combine(repoRoot, ".intent-cli", "issues", "AUTH-01", "packet.yaml")));
            Assert.Equal("AUTH-01", packet.ImplementationIssuePacket.SourceExecutionUnit);

            var queueState = QueueStateSerializer.Deserialize(
                File.ReadAllText(Path.Combine(repoRoot, ".intent-cli", "queue-state.json")));
            Assert.Equal(QueueItemState.Active, queueState.Items.Single(item => item.ExecutionUnit == "AUTH-01").State);
            Assert.Equal(QueueItemState.Active, queueState.Items.Single(item => item.ExecutionUnit == "AUTH-02").State);

            var runEvents = RunLogSerializer.DeserializeAll(
                File.ReadAllText(Path.Combine(repoRoot, ".intent-cli", "runs.jsonl")));
            Assert.Equal(
                ["queued", "issue-created", "activated", "queued", "issue-created", "activated"],
                runEvents.Select(runEvent => runEvent.Event).ToArray());

            var auth01Worktree = Path.GetFullPath(Path.Combine(repoRoot, ".intent-cli", "worktrees", "AUTH-01"));
            var auth02Worktree = Path.GetFullPath(Path.Combine(repoRoot, ".intent-cli", "worktrees", "AUTH-02"));
            var childRepoPath = Path.Combine(repoRoot, "submodules", "intent-system");
            Assert.Equal(
                [
                    $"{childRepoPath}::fetch origin main",
                    $"{childRepoPath}::worktree add -b issue-501-auth-01 {auth01Worktree} origin/main",
                    $"{childRepoPath}::fetch origin main",
                    $"{childRepoPath}::worktree add -b issue-502-auth-02 {auth02Worktree} origin/main"
                ],
                startGitRunner.Calls);
        }
        finally
        {
            QueueEnqueueCommand.TimestampFactory = originalEnqueueTimestampFactory;
            QueueDispatchCommand.PublisherFactory = originalPublisherFactory;
            QueueDispatchCommand.GitCommandRunnerFactory = originalRemoteGitFactory;
            QueueDispatchCommand.TimestampFactory = originalDispatchTimestampFactory;
            RunStartCommand.GitCommandRunnerFactory = originalStartGitFactory;
            RunStartCommand.TimestampFactory = originalStartTimestampFactory;
        }
    }

    [Fact]
    public void Execute_GivenExistingArtifactsAndQueuedUnit_SkipsGenerationButContinuesStart()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateDirectory(Path.Combine("repo", "submodules", "intent-system"));
        tempDirectory.CreateFile(
            Path.Combine("repo", "intents", "intent-cli", "execution", "05-post-mvp-sub-slices.md"),
            CreateExecutionBaselineMarkdown());
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "intake", "auth.execution.md"),
            CreateExecutionArtifactMarkdown("auth-single"));
        tempDirectory.CreateFile(
            Path.Combine("repo", "intents", "intent-cli", "intent-tree", "00-map.md"),
            "# Intent CLI Map");
        tempDirectory.CreateFile(
            Path.Combine("repo", "intents", "intent-cli", "clarifications", "open.md"),
            "# Clarifications");
        tempDirectory.CreateFile(
            Path.Combine("repo", "intents", "intent-cli", "concepts", "oauth2.md"),
            "# Auth Concept");
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "issues", "AUTH-01", "implementation.md"),
            "existing implementation");
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "issues", "AUTH-01", "review-context.md"),
            "existing review");
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "issues", "AUTH-01", "packet.yaml"),
            CreateCanonicalLaunchPacketYaml("AUTH-01"));
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "issues", "AUTH-01", "github-body.md"),
            "existing body");
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "queue-state.json"),
            QueueStateSerializer.Serialize(CreateQueueState(includeExistingQueued: true)));
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "runs.jsonl"),
            string.Empty);
        using var writer = new StringWriter();
        var originalEnqueueTimestampFactory = QueueEnqueueCommand.TimestampFactory;
        var originalPublisherFactory = QueueDispatchCommand.PublisherFactory;
        var originalRemoteGitFactory = QueueDispatchCommand.GitCommandRunnerFactory;
        var originalDispatchTimestampFactory = QueueDispatchCommand.TimestampFactory;
        var originalStartGitFactory = RunStartCommand.GitCommandRunnerFactory;
        var originalStartTimestampFactory = RunStartCommand.TimestampFactory;
        var startGitRunner = new FakeStartGitRunner();
        var publisher = new FakePublisher();

        try
        {
            QueueEnqueueCommand.TimestampFactory = () => DateTimeOffset.Parse("2026-04-06T11:00:00Z");
            QueueDispatchCommand.PublisherFactory = () => publisher;
            QueueDispatchCommand.GitCommandRunnerFactory = () => new FakeRemoteGitRunner();
            QueueDispatchCommand.TimestampFactory = () => DateTimeOffset.Parse("2026-04-06T11:05:00Z");
            RunStartCommand.GitCommandRunnerFactory = () => startGitRunner;
            RunStartCommand.TimestampFactory = () => DateTimeOffset.Parse("2026-04-06T11:10:00Z");

            var exitCode = IntakeStartCommand.Execute(CreateContext(repoRoot), ["auth"], writer);

            Assert.Equal(0, exitCode);
            var output = writer.ToString();
            Assert.Contains("Started execution units:", output, StringComparison.Ordinal);
            Assert.Contains("- AUTH-01", output, StringComparison.Ordinal);
            Assert.Contains("Generated artifact paths:", output, StringComparison.Ordinal);
            Assert.Contains("- none", output, StringComparison.Ordinal);
            Assert.Contains("Skipped units:", output, StringComparison.Ordinal);
            var skippedSectionIndex = output.IndexOf("Skipped units:", StringComparison.Ordinal);
            Assert.NotEqual(-1, skippedSectionIndex);
            Assert.Contains("- none", output[skippedSectionIndex..], StringComparison.Ordinal);
            Assert.Equal("existing implementation", File.ReadAllText(Path.Combine(repoRoot, ".intent-cli", "issues", "AUTH-01", "implementation.md")));

            var queueState = QueueStateSerializer.Deserialize(
                File.ReadAllText(Path.Combine(repoRoot, ".intent-cli", "queue-state.json")));
            Assert.Equal(QueueItemState.Active, queueState.Items.Single(item => item.ExecutionUnit == "AUTH-01").State);

            var runEvents = RunLogSerializer.DeserializeAll(
                File.ReadAllText(Path.Combine(repoRoot, ".intent-cli", "runs.jsonl")));
            Assert.Equal(["issue-created", "activated"], runEvents.Select(runEvent => runEvent.Event).ToArray());
            Assert.Equal(["AUTH-01", "AUTH-01"], runEvents.Select(runEvent => runEvent.ExecutionUnit).ToArray());
        }
        finally
        {
            QueueEnqueueCommand.TimestampFactory = originalEnqueueTimestampFactory;
            QueueDispatchCommand.PublisherFactory = originalPublisherFactory;
            QueueDispatchCommand.GitCommandRunnerFactory = originalRemoteGitFactory;
            QueueDispatchCommand.TimestampFactory = originalDispatchTimestampFactory;
            RunStartCommand.GitCommandRunnerFactory = originalStartGitFactory;
            RunStartCommand.TimestampFactory = originalStartTimestampFactory;
        }
    }

    [Fact]
    public void Execute_GivenMissingDomainArgument_ReturnsExitCodeOne()
    {
        using var writer = new StringWriter();

        var exitCode = IntakeStartCommand.Execute(CreateContext("/tmp/intent-system"), [], writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("requires a domain", writer.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    private static string CreateExecutionBaselineMarkdown()
    {
        return """
            # Post-MVP Sub-Slices

            | subslice_id | belongs_to_slice | goal | depends_on_subslices | target_repo | target_path | target_part | issue_cut_ready |
            |---|---|---|---|---|---|---|---|
            | G37 | G | `intake issue <domain>` を CLI shell から使えるようにし、updated intake-origin `execution/` source-of-truth から issue-ready execution unit の issue artifact 群を deterministic に生成できるようにする | G2, G35, G36 | submodules/intent-system | . | cli intake issue command | yes |

            ## G37 の current baseline

            - `intake issue <domain>` を最初の intake issue-artifact generation command にする
            - canonical source は current `execution/` source files と current `G2` / `G29` / `G30` / `G32` / `G33` / `G34` / `G35` / `G36` intake baseline である
            - successful output は selected domain の intake-origin issue-ready execution unit に対応する `.intent-cli/issues/<execution-unit>/implementation.md`, `review-context.md`, `packet.yaml`, and `github-body.md` の deterministic generation を baseline にする
            """;
    }

    private static string CreateExecutionArtifactMarkdown(string domain)
    {
        if (string.Equals(domain, "auth-single", StringComparison.Ordinal))
        {
            return """
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
                - Current heading: # Auth Concept
                verification_hints:
                - dotnet test IntentSystem.sln
                """;
        }

        return """
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
            - Current heading: # Auth Concept
            verification_hints:
            - dotnet test IntentSystem.sln

            ### `AUTH-02`
            source_file_path: intents/intent-cli/intent-tree/means/device-code.md
            target_part: intent-tree/means
            dependencies:
            - AUTH-01
            readiness_notes:
            - Current heading: # Device Code
            verification_hints:
            - dotnet test IntentSystem.sln
            """;
    }

    private static string CreateCanonicalLaunchPacketYaml(string executionUnit)
    {
        return $$"""
            execution_unit: {{executionUnit}}
            implementation_issue:
              issue_title: "{{executionUnit}} Intake Start"
              goal: "Start generated issue-ready execution unit through issue-generation and launch flow."
              in_scope:
                - "issue generation"
                - "queue insertion"
                - "run start"
              out_of_scope:
                - "review execution"
              target_repo: "submodules/intent-system"
              target_path: "."
              target_part: "cli intake start command"
              dependencies:
                - "G3"
              technical_baseline:
                - "C# / .NET"
              project_local_guidance:
                - "AGENTS.md"
              intent_baseline:
                - "intake start stays thin"
              acceptance_criteria:
                - "issue-ready unit starts deterministically"
              verification:
                - "tests-passing"

            review:
              summarize_first: true
              require_explicit_diff_check: true
              require_explicit_scope_check: true
              require_explicit_contract_check: true
              required_checks:
                - "intake start remains thin"
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
                    ArtifactRoot = ".intent-cli",
                    WorktreeRoot = ".intent-cli/worktrees"
                },
                Roles = new RoleMappings
                {
                    Implement = "Claude",
                    Review = "Codex"
                }
            }
        };
    }

    private static QueueState CreateQueueState(bool includeExistingQueued = false)
    {
        var items = new List<QueueItem>
        {
            new()
            {
                ExecutionUnit = "G3",
                Title = "[G3] Existing Item",
                State = QueueItemState.Completed,
                Dependencies = [],
                BlockedBy = [],
                ClarificationReturnPath = "intents/intent-cli/clarifications/open.md",
                PacketPaths = new PacketPaths
                {
                    Implementation = ".intent-cli/issues/G3/implementation.md",
                    ReviewContext = ".intent-cli/issues/G3/review-context.md",
                    Yaml = ".intent-cli/issues/G3/packet.yaml"
                },
                LinkedIssue = null,
                WorkerRole = "Claude",
                ReviewRole = "Codex",
                Priority = "high"
            }
        };

        if (includeExistingQueued)
        {
            items.Add(new QueueItem
            {
                ExecutionUnit = "AUTH-01",
                Title = "[AUTH-01] Existing Item",
                State = QueueItemState.Queued,
                Dependencies = ["G3"],
                BlockedBy = [],
                ClarificationReturnPath = "intents/intent-cli/clarifications/open.md",
                PacketPaths = new PacketPaths
                {
                    Implementation = ".intent-cli/issues/AUTH-01/implementation.md",
                    ReviewContext = ".intent-cli/issues/AUTH-01/review-context.md",
                    Yaml = ".intent-cli/issues/AUTH-01/packet.yaml"
                },
                LinkedIssue = null,
                WorkerRole = "Claude",
                ReviewRole = "Codex",
                Priority = "high"
            });
        }

        return new QueueState
        {
            SchemaVersion = "1",
            UpdatedAt = DateTimeOffset.Parse("2026-04-06T10:00:00Z"),
            Items = items
        };
    }

    private sealed class FakePublisher : IQueueDispatchPublisher
    {
        private int nextIssueNumber = 501;

        public LinkedIssue CreateIssue(string targetRepo, string title, string body)
        {
            var issueNumber = nextIssueNumber++;
            return new LinkedIssue
            {
                Repo = targetRepo,
                Number = issueNumber,
                Url = $"https://github.com/J-Tech-Japan/intent-system/issues/{issueNumber}"
            };
        }
    }

    private sealed class FakeRemoteGitRunner : IGitRemoteCommandRunner
    {
        public GitRemoteCommandResult Run(string workingDirectory, IReadOnlyList<string> arguments)
        {
            return new GitRemoteCommandResult
            {
                ExitCode = 0,
                StdOut = "https://github.com/J-Tech-Japan/intent-system.git",
                StdErr = string.Empty
            };
        }
    }

    private sealed class FakeStartGitRunner : IGitCommandRunner
    {
        public List<string> Calls { get; } = [];

        public GitCommandResult Run(string workingDirectory, IReadOnlyList<string> arguments)
        {
            Calls.Add($"{workingDirectory}::{string.Join(' ', arguments)}");
            return new GitCommandResult
            {
                ExitCode = 0,
                StdOut = string.Empty,
                StdErr = string.Empty
            };
        }
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        private readonly string rootPath = Directory.CreateTempSubdirectory("intent-cli-intake-start-command-tests-").FullName;

        public string CreateDirectory(string relativePath)
        {
            var fullPath = Path.Combine(rootPath, relativePath);
            Directory.CreateDirectory(fullPath);
            return fullPath;
        }

        public string CreateFile(string relativePath, string contents)
        {
            var fullPath = Path.Combine(rootPath, relativePath);
            var directoryPath = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(directoryPath))
            {
                Directory.CreateDirectory(directoryPath);
            }

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
