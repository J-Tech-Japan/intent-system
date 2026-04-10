using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;
using IntentSystem.Projection.Serialization;
using IntentSystem.Review;
using IntentSystem.Supervisor.Models;
using IntentSystem.Supervisor.Serialization;

namespace IntentSystem.Cli.Tests;

[Collection(RunSubmitCommandCollection.Name)]
public sealed class IntakeActivateCommandTests
{
    [Fact]
    public void Execute_GivenReadyIntakeArtifacts_AdvancesAndStartsUnits()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateDirectory(Path.Combine("repo", "submodules", "intent-system"));
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "intake", "auth.concept.yaml"),
            CreateConceptArtifactYaml("auth"));
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "interviews", "auth", "iq-1.yaml"),
            CreateInterviewArtifactYaml(CreateAnsweredItem()));
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "interviews", "auth", "iq-1.md"),
            "# Interview Question");
        tempDirectory.CreateFile(
            Path.Combine("repo", "intents", "intent-cli", "concepts", "auth-oauth2.md"),
            "# Auth Concept" + Environment.NewLine + Environment.NewLine + "- Existing note" + Environment.NewLine);
        tempDirectory.CreateFile(
            Path.Combine("repo", "intents", "intent-cli", "intent-tree", "means", "auth-oauth2.md"),
            "# Auth Means" + Environment.NewLine + Environment.NewLine + "- Existing rule" + Environment.NewLine);
        tempDirectory.CreateFile(
            Path.Combine("repo", "intents", "intent-cli", "execution", "05-post-mvp-sub-slices.md"),
            CreateExecutionBaselineMarkdown());
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

            var exitCode = IntakeActivateCommand.Execute(CreateContext(repoRoot), ["auth"], writer);

            Assert.Equal(0, exitCode);
            var output = writer.ToString();
            Assert.Contains("Intake activate processed for domain 'auth'.", output, StringComparison.Ordinal);
            Assert.Contains("Readiness status: ready", output, StringComparison.Ordinal);
            Assert.Contains("Updated source file paths:", output, StringComparison.Ordinal);
            Assert.Contains("- intents/intent-cli/concepts/auth-oauth2.md", output, StringComparison.Ordinal);
            Assert.Contains("- intents/intent-cli/intent-tree/means/auth-oauth2.md", output, StringComparison.Ordinal);
            Assert.Contains("Updated execution file paths:", output, StringComparison.Ordinal);
            Assert.Contains("- intents/intent-cli/execution/05-post-mvp-sub-slices.md", output, StringComparison.Ordinal);
            Assert.Contains("Regenerated artifact paths:", output, StringComparison.Ordinal);
            Assert.Contains("- .intent-cli/intake/auth.compile.md", output, StringComparison.Ordinal);
            Assert.Contains("- .intent-cli/intake/auth.foldin.md", output, StringComparison.Ordinal);
            Assert.Contains("- .intent-cli/intake/auth.patch.md", output, StringComparison.Ordinal);
            Assert.Contains("- .intent-cli/intake/auth.execution.md", output, StringComparison.Ordinal);
            Assert.Contains("Started execution units:", output, StringComparison.Ordinal);
            Assert.Contains("- AUTH-01", output, StringComparison.Ordinal);
            Assert.Contains("- AUTH-02", output, StringComparison.Ordinal);
            Assert.Contains("Generated issue artifact paths:", output, StringComparison.Ordinal);
            Assert.Contains(".intent-cli/issues/AUTH-01/packet.yaml", output, StringComparison.Ordinal);
            Assert.Contains(".intent-cli/issues/AUTH-02/github-body.md", output, StringComparison.Ordinal);
            Assert.Contains("Created issue refs:", output, StringComparison.Ordinal);
            Assert.Contains("- https://github.com/J-Tech-Japan/intent-system/issues/501", output, StringComparison.Ordinal);
            Assert.Contains("- https://github.com/J-Tech-Japan/intent-system/issues/502", output, StringComparison.Ordinal);
            Assert.Contains("Worktree paths:", output, StringComparison.Ordinal);
            Assert.Contains(Path.GetFullPath(Path.Combine(repoRoot, ".intent-cli", "worktrees", "AUTH-01")), output, StringComparison.Ordinal);
            Assert.Contains("Skipped stages:", output, StringComparison.Ordinal);
            var skippedSectionIndex = output.IndexOf("Skipped stages:", StringComparison.Ordinal);
            Assert.NotEqual(-1, skippedSectionIndex);
            Assert.Contains("- none", output[skippedSectionIndex..], StringComparison.Ordinal);

            Assert.True(File.Exists(Path.Combine(repoRoot, ".intent-cli", "intake", "auth.compile.md")));
            Assert.True(File.Exists(Path.Combine(repoRoot, ".intent-cli", "intake", "auth.foldin.md")));
            Assert.True(File.Exists(Path.Combine(repoRoot, ".intent-cli", "intake", "auth.patch.md")));
            Assert.True(File.Exists(Path.Combine(repoRoot, ".intent-cli", "intake", "auth.execution.md")));
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

            var updatedConcept = File.ReadAllText(Path.Combine(repoRoot, "intents", "intent-cli", "concepts", "auth-oauth2.md"));
            Assert.Contains("- Reconcile this source concept file with the current fold-in draft.", updatedConcept, StringComparison.Ordinal);
            var updatedMeans = File.ReadAllText(Path.Combine(repoRoot, "intents", "intent-cli", "intent-tree", "means", "auth-oauth2.md"));
            Assert.Contains("- Align login UX wording", updatedMeans, StringComparison.Ordinal);

            var executionSource = File.ReadAllText(Path.Combine(repoRoot, "intents", "intent-cli", "execution", "05-post-mvp-sub-slices.md"));
            Assert.Contains("- execution_unit: AUTH-01", executionSource, StringComparison.Ordinal);
            Assert.Contains("- execution_unit: AUTH-02", executionSource, StringComparison.Ordinal);

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
    public void Execute_GivenNotReadyCompileState_RendersNotReadySummaryAndSkipsDownstreamStages()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "intake", "auth.concept.yaml"),
            CreateConceptArtifactYaml("auth"));
        using var writer = new StringWriter();

        var exitCode = IntakeActivateCommand.Execute(CreateContext(repoRoot), ["auth"], writer);

        Assert.Equal(0, exitCode);
        var output = writer.ToString();
        Assert.Contains("Intake activate processed for domain 'auth'.", output, StringComparison.Ordinal);
        Assert.Contains("Readiness status: not-ready", output, StringComparison.Ordinal);
        Assert.Contains("Started execution units:", output, StringComparison.Ordinal);
        Assert.Contains("Generated issue artifact paths:", output, StringComparison.Ordinal);
        Assert.Contains("Created issue refs:", output, StringComparison.Ordinal);
        Assert.Contains("Worktree paths:", output, StringComparison.Ordinal);
        Assert.Contains("Skipped stages:", output, StringComparison.Ordinal);
        Assert.Contains("- foldin", output, StringComparison.Ordinal);
        Assert.Contains("- patch", output, StringComparison.Ordinal);
        Assert.Contains("- apply", output, StringComparison.Ordinal);
        Assert.Contains("- execution", output, StringComparison.Ordinal);
        Assert.Contains("- execution-apply", output, StringComparison.Ordinal);
        Assert.Contains("- issue", output, StringComparison.Ordinal);
        Assert.Contains("- enqueue", output, StringComparison.Ordinal);
        Assert.Contains("- dispatch", output, StringComparison.Ordinal);
        Assert.Contains("- start", output, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(repoRoot, ".intent-cli", "issues", "AUTH-01", "packet.yaml")));
    }

    [Fact]
    public void Execute_GivenMissingDomainArgument_ReturnsExitCodeOne()
    {
        using var writer = new StringWriter();

        var exitCode = IntakeActivateCommand.Execute(CreateContext("/tmp/intent-system"), [], writer);

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

            ## G36 の current baseline

            - `intake execution apply <domain>` を最初の execution source-of-truth apply command にする
            - canonical source は current `.intent-cli/intake/<domain>.execution.md` と current `execution/` source files, plus the `G29` / `G30` / `G32` / `G33` / `G34` / `G35` intake baseline である
            - successful output は execution draft で指定された source files だけを deterministic に更新することを baseline にする

            ## G37 の current baseline

            - `intake issue <domain>` を最初の intake issue-artifact generation command にする
            - canonical source は current `execution/` source files と current `G2` / `G29` / `G30` / `G32` / `G33` / `G34` / `G35` / `G36` intake baseline である
            - successful output は selected domain の intake-origin issue-ready execution unit に対応する `.intent-cli/issues/<execution-unit>/implementation.md`, `review-context.md`, `packet.yaml`, and `github-body.md` の deterministic generation を baseline にする

            ## Another Section

            - Keep this section untouched
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

    private static QueueState CreateQueueState()
    {
        return new QueueState
        {
            SchemaVersion = "1",
            UpdatedAt = DateTimeOffset.Parse("2026-04-06T10:00:00Z"),
            Items =
            [
                new QueueItem
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
            ]
        };
    }

    private static IntentSystem.ConceptIntake.Models.InterviewQueueItem CreateAnsweredItem()
    {
        return new IntentSystem.ConceptIntake.Models.InterviewQueueItem
        {
            DomainSlug = "auth",
            SourceConceptRef = "intents/intent-cli/concepts/auth-oauth2.md",
            QuestionId = "iq-1",
            QuestionText = "What should be updated?",
            Reason = "Clarify auth direction.",
            Affects = ["auth-oauth2"],
            BlockingOrNonblocking = "blocking",
            Status = IntentSystem.ConceptIntake.Models.InterviewQueueItemStatus.Answered,
            ReturnToIntentPaths = ["intents/intent-cli/intent-tree/means/auth-oauth2.md"],
            CreatedAt = DateTimeOffset.Parse("2026-04-13T07:00:00Z"),
            Answer = "Align login UX wording.",
            AnsweredAt = DateTimeOffset.Parse("2026-04-13T10:00:00Z"),
            RecommendedUpdates = ["Align login UX wording"]
        };
    }

    private static string CreateConceptArtifactYaml(string domain)
    {
        return $$"""
            domain_slug: {{domain}}
            concept_source: interactive
            concept_text: "Add OAuth2 provider support."
            upstream_paths:
              - "intents/intent-cli/intent-tree/means/04-worker-interface-strategy.md"
            initial_goal: "Add OAuth2 provider support."
            constraints:
              - "Must not break existing session flow"
            known_unknowns:
              - "Which OAuth providers to support?"
            """;
    }

    private static string CreateInterviewArtifactYaml(IntentSystem.ConceptIntake.Models.InterviewQueueItem item)
    {
        var lines = new List<string>
        {
            "artifact_kind: interview",
            $"domain_slug: {item.DomainSlug}",
            $"source_concept_ref: {Quote(item.SourceConceptRef)}",
            $"question_id: {item.QuestionId}",
            $"question_text: {Quote(item.QuestionText)}",
            $"reason: {Quote(item.Reason)}",
            "affects:"
        };

        lines.AddRange(item.Affects.Select(affect => $"  - {Quote(affect)}"));
        lines.Add($"blocking_or_nonblocking: {item.BlockingOrNonblocking}");
        lines.Add($"status: {item.Status.ToString().ToLowerInvariant()}");
        lines.Add("return_to_intent_paths:");
        lines.AddRange(item.ReturnToIntentPaths.Select(path => $"  - {Quote(path)}"));
        lines.Add($"created_at: {Quote(item.CreatedAt.ToString("O"))}");
        lines.Add(item.Answer is null ? "answer: null" : $"answer: {Quote(item.Answer)}");
        lines.Add($"answered_at: {Quote(item.AnsweredAt!.Value.ToString("O"))}");
        lines.Add("recommended_updates:");
        lines.AddRange(item.RecommendedUpdates!.Select(update => $"  - {Quote(update)}"));

        return string.Join(Environment.NewLine, lines) + Environment.NewLine;
    }

    private static string Quote(string value)
    {
        return "\"" + value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal)
            .Replace("\r", "\\r", StringComparison.Ordinal) + "\"";
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
        private readonly string rootPath = Directory.CreateTempSubdirectory("intent-cli-intake-activate-command-tests-").FullName;

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
