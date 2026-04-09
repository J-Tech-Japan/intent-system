using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;
using IntentSystem.Supervisor.Models;
using IntentSystem.Supervisor.Serialization;

namespace IntentSystem.Cli.Tests;

public sealed class BugIntentEnqueueCommandTests
{
    [Fact]
    public void Execute_GivenReadyIntentIssue_AllocatesNextExecutionUnitAndEnqueuesLinkedIssue()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateFile(Path.Combine("parent-intent", "intents", "intent-cli", "means", "auth.md"), "# auth");
        tempDirectory.CreateFile(
            Path.Combine("parent-intent", "intents", "intent-cli", "specs", "12-bug-fix-and-intent-repair.md"),
            "# spec");
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "queue-state.json"),
            QueueStateSerializer.Serialize(CreateQueueState()));
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "bugs", "BUG-123.intent-repair.yaml"),
            BugIntentRepairArtifactYaml.Serialize(CreateRepairArtifact()));
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "bugs", "BUG-123.intent-issue.yaml"),
            BugIntentIssueArtifactYaml.Serialize(CreateIssueArtifact()));
        var originalTimestampFactory = QueueEnqueueCommand.TimestampFactory;
        using var writer = new StringWriter();

        try
        {
            QueueEnqueueCommand.TimestampFactory = () => DateTimeOffset.Parse("2026-04-08T12:30:00Z");

            var exitCode = BugIntentEnqueueCommand.Execute(CreateContext(repoRoot), ["BUG-123"], writer);

            Assert.Equal(0, exitCode);
            Assert.Contains("Bug intent-enqueue artifact generated for 'BUG-123'.", writer.ToString(), StringComparison.Ordinal);
            Assert.Contains("Allocated execution unit: G13", writer.ToString(), StringComparison.Ordinal);

            var enqueueArtifact = BugIntentEnqueueArtifactYaml.Deserialize(
                File.ReadAllText(Path.Combine(repoRoot, ".intent-cli", "bugs", "BUG-123.intent-enqueue.yaml")));
            Assert.Equal(".intent-cli/bugs/BUG-123.intent-issue.yaml", enqueueArtifact.IntentIssueRef);
            Assert.Equal(".intent-cli/bugs/BUG-123.intent-repair.yaml", enqueueArtifact.IntentRepairRef);
            Assert.Equal("G13", enqueueArtifact.AllocatedExecutionUnit);
            Assert.Equal("https://github.com/J-Tech-Japan/MyIntentHost/issues/53", enqueueArtifact.LinkedIssueUrl);
            Assert.True(enqueueArtifact.WasEnqueued);

            Assert.True(File.Exists(Path.Combine(repoRoot, ".intent-cli", "issues", "G13", "implementation.md")));
            Assert.True(File.Exists(Path.Combine(repoRoot, ".intent-cli", "issues", "G13", "review-context.md")));
            Assert.True(File.Exists(Path.Combine(repoRoot, ".intent-cli", "issues", "G13", "packet.yaml")));

            var queueState = QueueStateSerializer.Deserialize(
                File.ReadAllText(Path.Combine(repoRoot, ".intent-cli", "queue-state.json")));
            var selectedItem = queueState.Items.Single(item => item.ExecutionUnit == "G13");
            Assert.Equal("[G13] Intent repair: OAuth callback loop (BUG-123)", selectedItem.Title);
            Assert.NotNull(selectedItem.LinkedIssue);
            Assert.Equal("J-Tech-Japan/MyIntentHost", selectedItem.LinkedIssue!.Repo);
            Assert.Equal(53, selectedItem.LinkedIssue.Number);
            Assert.Equal("https://github.com/J-Tech-Japan/MyIntentHost/issues/53", selectedItem.LinkedIssue.Url);
            Assert.Equal(".intent-cli/issues/G13/packet.yaml", selectedItem.PacketPaths.Yaml);

            var runEvents = RunLogSerializer.DeserializeAll(
                File.ReadAllText(Path.Combine(repoRoot, ".intent-cli", "runs.jsonl")));
            var queued = Assert.Single(runEvents);
            Assert.Equal("queued", queued.Event);
            Assert.Equal("G13", queued.ExecutionUnit);
            Assert.Equal("https://github.com/J-Tech-Japan/MyIntentHost/issues/53", queued.LinkedIssue);
        }
        finally
        {
            QueueEnqueueCommand.TimestampFactory = originalTimestampFactory;
        }
    }

    [Fact]
    public void Execute_GivenNotReadyIntentIssue_WritesArtifactWithoutQueueMutation()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        var queueStatePath = tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "queue-state.json"),
            QueueStateSerializer.Serialize(CreateQueueState()));
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "bugs", "BUG-124.intent-repair.yaml"),
            BugIntentRepairArtifactYaml.Serialize(CreateRepairArtifact(bugId: "BUG-124")));
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "bugs", "BUG-124.intent-issue.yaml"),
            BugIntentIssueArtifactYaml.Serialize(CreateIssueArtifact(bugId: "BUG-124", linkedIssueUrl: null, linkedIssueNumber: null)));
        using var writer = new StringWriter();

        var originalQueueState = File.ReadAllText(queueStatePath);
        var exitCode = BugIntentEnqueueCommand.Execute(CreateContext(repoRoot), ["BUG-124"], writer);

        Assert.Equal(0, exitCode);
        Assert.Contains("Allocated execution unit: not-allocated", writer.ToString(), StringComparison.Ordinal);

        var artifact = BugIntentEnqueueArtifactYaml.Deserialize(
            File.ReadAllText(Path.Combine(repoRoot, ".intent-cli", "bugs", "BUG-124.intent-enqueue.yaml")));
        Assert.Null(artifact.AllocatedExecutionUnit);
        Assert.Null(artifact.LinkedIssueUrl);
        Assert.False(artifact.WasEnqueued);
        Assert.Empty(artifact.GeneratedPacketPaths);

        Assert.Equal(originalQueueState, File.ReadAllText(queueStatePath));
        Assert.False(File.Exists(Path.Combine(repoRoot, ".intent-cli", "runs.jsonl")));
    }

    [Fact]
    public void Execute_GivenMissingIntentIssueArtifact_ReturnsExitCodeOne()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        using var writer = new StringWriter();

        var exitCode = BugIntentEnqueueCommand.Execute(CreateContext(repoRoot), ["BUG-123"], writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("Bug intent-issue artifact was not found", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void AllocateNextExecutionUnit_GivenCurrentQueueSnapshot_UsesNextMonotonicGNumber()
    {
        var executionUnit = BugIntentEnqueueCommand.AllocateNextExecutionUnit(CreateQueueState());

        Assert.Equal("G13", executionUnit);
    }

    private static BugIntentRepairArtifact CreateRepairArtifact(string bugId = "BUG-123")
    {
        return new BugIntentRepairArtifact
        {
            BugId = bugId,
            ExecutionRef = $".intent-cli/bugs/{bugId}.plan.yaml",
            IntentTaskCandidates =
            [
                "intents/intent-cli/means/auth.md",
                "intents/intent-cli/specs/12-bug-fix-and-intent-repair.md"
            ],
            ParentRepairTargets =
            [
                "intent:intents/intent-cli/means/auth.md",
                "rule-spec:intents/intent-cli/specs/12-bug-fix-and-intent-repair.md"
            ],
            SuggestedIssueTitle = $"Intent repair: OAuth callback loop ({bugId})",
            SuggestedGoal = $"Repair parent intent targets for 'OAuth callback loop' ({bugId}) using .intent-cli/bugs/{bugId}.plan.yaml: intent:intents/intent-cli/means/auth.md, rule-spec:intents/intent-cli/specs/12-bug-fix-and-intent-repair.md",
            ReadyToIssueCut = true
        };
    }

    private static BugIntentIssueArtifact CreateIssueArtifact(
        string bugId = "BUG-123",
        string? linkedIssueUrl = "https://github.com/J-Tech-Japan/MyIntentHost/issues/53",
        int? linkedIssueNumber = 53)
    {
        return new BugIntentIssueArtifact
        {
            BugId = bugId,
            IntentRepairRef = $".intent-cli/bugs/{bugId}.intent-repair.yaml",
            CreatedIssueTitle = $"Intent repair: OAuth callback loop ({bugId})",
            CreatedIssueUrl = linkedIssueUrl,
            CreatedIssueNumber = linkedIssueNumber,
            ParentRepairTargets =
            [
                "intent:intents/intent-cli/means/auth.md",
                "rule-spec:intents/intent-cli/specs/12-bug-fix-and-intent-repair.md"
            ]
        };
    }

    private static QueueState CreateQueueState()
    {
        return new QueueState
        {
            SchemaVersion = "1",
            UpdatedAt = DateTimeOffset.Parse("2026-04-08T12:00:00Z"),
            Items =
            [
                CreateItem("G9", QueueItemState.Completed),
                CreateItem("G12", QueueItemState.Queued),
                CreateItem("AUTH-01", QueueItemState.Queued)
            ]
        };
    }

    private static QueueItem CreateItem(string executionUnit, QueueItemState state)
    {
        return new QueueItem
        {
            ExecutionUnit = executionUnit,
            Title = $"[{executionUnit}] Existing Item",
            State = state,
            Dependencies = [],
            BlockedBy = [],
            ClarificationReturnPath = "intents/intent-cli/clarifications/open.md",
            PacketPaths = new PacketPaths
            {
                Implementation = $".intent-cli/issues/{executionUnit}/implementation.md",
                ReviewContext = $".intent-cli/issues/{executionUnit}/review-context.md",
                Yaml = $".intent-cli/issues/{executionUnit}/packet.yaml"
            },
            LinkedIssue = null,
            WorkerRole = "Claude",
            ReviewRole = "Codex",
            Priority = "high"
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
                },
                Roles = new RoleMappings
                {
                    Implement = "Claude",
                    Review = "Codex"
                }
            }
        };
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        private readonly string rootPath = Directory.CreateTempSubdirectory("intent-cli-bug-intent-enqueue-tests-").FullName;

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
