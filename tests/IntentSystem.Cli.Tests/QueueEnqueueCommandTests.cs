using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;
using IntentSystem.Supervisor.Models;
using IntentSystem.Supervisor.Serialization;

namespace IntentSystem.Cli.Tests;

// G569 audit: joins the non-parallel collection that already owns the
// process-global statics this class assigns, so it can no longer interleave
// with the other class that assigns them.
[Collection(RunSubmitCommandCollection.Name)]
public sealed class QueueEnqueueCommandTests
{
    [Fact]
    public void Execute_GivenPacketArtifact_EnqueuesItemAndAppendsRunLog()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "queue-state.json"),
            QueueStateSerializer.Serialize(CreateQueueState()));
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "issues", "G38", "packet.yaml"),
            CreatePacketYaml());
        var originalTimestampFactory = QueueEnqueueCommand.TimestampFactory;
        using var writer = new StringWriter();

        try
        {
            QueueEnqueueCommand.TimestampFactory = () => DateTimeOffset.Parse("2026-04-06T10:30:00Z");

            var exitCode = QueueEnqueueCommand.Execute(CreateContext(repoRoot), ["G38"], writer);

            Assert.Equal(0, exitCode);
            Assert.Contains("Queue enqueue processed for execution unit 'G38'.", writer.ToString(), StringComparison.Ordinal);

            var updatedState = QueueStateSerializer.Deserialize(
                File.ReadAllText(Path.Combine(repoRoot, ".intent-cli", "queue-state.json")));
            var selectedItem = updatedState.Items.Single(item => item.ExecutionUnit == "G38");
            Assert.Equal("G38 Queue Enqueue Command", selectedItem.Title);
            Assert.Equal(QueueItemState.Queued, selectedItem.State);
            Assert.Equal(["G3", "G4"], selectedItem.Dependencies);
            Assert.Equal(["G4"], selectedItem.BlockedBy);
            Assert.Equal("intents/intent-cli/clarifications/open.md", selectedItem.ClarificationReturnPath);
            Assert.Equal(".intent-cli/issues/G38/packet.yaml", selectedItem.PacketPaths.Yaml);
            Assert.Equal("Claude", selectedItem.WorkerRole);
            Assert.Equal("Codex", selectedItem.ReviewRole);
            Assert.Equal("high", selectedItem.Priority);

            var runEvents = RunLogSerializer.DeserializeAll(
                File.ReadAllText(Path.Combine(repoRoot, ".intent-cli", "runs.jsonl")));
            var queued = Assert.Single(runEvents);
            Assert.Equal("queued", queued.Event);
            Assert.Equal("G38", queued.ExecutionUnit);
            Assert.Equal("intent-cli", queued.By);
        }
        finally
        {
            QueueEnqueueCommand.TimestampFactory = originalTimestampFactory;
        }
    }

    [Fact]
    public void Execute_GivenTwoSpaceListItemPacket_EnqueuesSuccessfully()
    {
        // G534 field finding: a verbatim previously-failing packet shape —
        // the documented `implementation_issue_packet` schema with YAML
        // list items indented at the SAME column as their parent key
        // (quoted AND unquoted scalars) — used to throw "field line is
        // missing ':''" on every list item, rejecting every real
        // hand-authored packet using this common convention.
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "queue-state.json"),
            QueueStateSerializer.Serialize(CreateQueueState()));
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "issues", "G38", "packet.yaml"),
            CreateTwoSpaceListItemPacketYaml());
        using var writer = new StringWriter();

        var exitCode = QueueEnqueueCommand.Execute(CreateContext(repoRoot), ["G38"], writer);

        Assert.Equal(0, exitCode);
        Assert.Contains("Queue enqueue processed for execution unit 'G38'.", writer.ToString(), StringComparison.Ordinal);
        var updatedState = QueueStateSerializer.Deserialize(
            File.ReadAllText(Path.Combine(repoRoot, ".intent-cli", "queue-state.json")));
        var selectedItem = updatedState.Items.Single(item => item.ExecutionUnit == "G38");
        Assert.Equal(QueueItemState.Queued, selectedItem.State);
        Assert.Equal(["G3", "G4"], selectedItem.Dependencies);
    }

    [Fact]
    public void Execute_GivenExistingQueueItem_SkipsWithoutMutatingFiles()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        var queueStatePath = tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "queue-state.json"),
            QueueStateSerializer.Serialize(CreateQueueState(includeExisting: true)));
        var runLogPath = tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "runs.jsonl"),
            string.Empty);
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "issues", "G38", "packet.yaml"),
            CreatePacketYaml());
        using var writer = new StringWriter();

        var originalQueueState = File.ReadAllText(queueStatePath);
        var exitCode = QueueEnqueueCommand.Execute(CreateContext(repoRoot), ["G38"], writer);

        Assert.Equal(0, exitCode);
        Assert.Contains("Skipped units:", writer.ToString(), StringComparison.Ordinal);
        Assert.Contains("- G38", writer.ToString(), StringComparison.Ordinal);
        Assert.Equal(originalQueueState, File.ReadAllText(queueStatePath));
        Assert.Empty(File.ReadAllText(runLogPath));
    }

    [Fact]
    public void Execute_GivenMissingPacketArtifact_ReturnsExitCodeOne()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "queue-state.json"),
            QueueStateSerializer.Serialize(CreateQueueState()));
        using var writer = new StringWriter();

        var exitCode = QueueEnqueueCommand.Execute(CreateContext(repoRoot), ["G38"], writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("Projection packet artifact was not found", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_GivenPacketExecutionUnitMismatch_ReturnsExitCodeOneWithoutMutatingFiles()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        var queueStatePath = tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "queue-state.json"),
            QueueStateSerializer.Serialize(CreateQueueState()));
        var runLogPath = tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "runs.jsonl"),
            string.Empty);
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "issues", "G38", "packet.yaml"),
            CreatePacketYaml(packetExecutionUnit: "G99"));
        using var writer = new StringWriter();

        var originalQueueState = File.ReadAllText(queueStatePath);
        var exitCode = QueueEnqueueCommand.Execute(CreateContext(repoRoot), ["G38"], writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("does not match requested execution unit", writer.ToString(), StringComparison.Ordinal);
        Assert.Equal(originalQueueState, File.ReadAllText(queueStatePath));
        Assert.Empty(File.ReadAllText(runLogPath));
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

    private static QueueState CreateQueueState(bool includeExisting = false)
    {
        var items = new List<QueueItem>
        {
            CreateItem("G3", QueueItemState.Completed),
            CreateItem("G4", QueueItemState.Queued)
        };

        if (includeExisting)
        {
            items.Add(CreateItem("G38", QueueItemState.Queued));
        }

        return new QueueState
        {
            SchemaVersion = "1",
            UpdatedAt = DateTimeOffset.Parse("2026-04-06T10:00:00Z"),
            Items = items
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
            WorkerRole = "coder",
            ReviewRole = "reviewer",
            Priority = "high"
        };
    }

    private static string CreatePacketYaml(string packetExecutionUnit = "G38")
    {
        return $"""
        execution_unit: {packetExecutionUnit}
        implementation_issue:
          issue_title: "G38 Queue Enqueue Command"
          goal: "Enqueue issue packet into queue artifacts."
          in_scope:
            - "queue enqueue command"
          out_of_scope:
            - "workflow execution"
          target_repo: "submodules/intent-system"
          target_path: "."
          target_part: "cli queue enqueue command"
          dependencies:
            - "G4"
            - "G3"
          technical_baseline:
            - "C# / .NET"
          project_local_guide:
            - "AGENTS.md"
          intent_baseline:
            - "queue insertion stays thin"
          intent_references:
            - "ICL.P.PRODUCT_GOAL"
          rules_and_specs:
            - "intents/intent-cli/specs/03-queue-json-and-jsonl-schema.md"
          acceptance_criteria:
            - "queue item inserted"
          verification_evidence:
            - "tests-passing"

        review:
          summarize_first: true
          require_explicit_diff_check: true
          require_explicit_scope_check: true
          require_explicit_contract_check: true
          required_checks:
            - "queue insertion remains thin"
        """;
    }

    /// <summary>G534: the documented (new-schema) packet format, with list
    /// items indented at the SAME column as their parent key — quoted and
    /// unquoted — the exact shape that previously threw "field line is
    /// missing ':''" before this fix.</summary>
    private static string CreateTwoSpaceListItemPacketYaml()
    {
        return """
        implementation_issue_packet:
          issue_title: "G38 Queue Enqueue Command"
          issue_kind: "feature"
          source_execution_unit: "G38"
          goal: "Enqueue issue packet into queue artifacts."
          in_scope:
          - "queue enqueue command"
          out_of_scope:
          - workflow execution
          target_repo: "submodules/intent-system"
          target_path: "."
          target_part: "cli queue enqueue command"
          dependencies:
          - "G4"
          - G3
          technical_baseline:
          - "C# / .NET"
          project_local_guide:
          - "AGENTS.md"
          intent_baseline:
          - "queue insertion stays thin"
          intent_references:
          - "ICL.P.PRODUCT_GOAL"
          rules_and_specs:
          - "intents/intent-cli/specs/03-queue-json-and-jsonl-schema.md"
          acceptance_criteria:
          - queue item inserted
          verification_evidence:
          - "tests-passing"
          review_mode: "deterministic-review"
          completion_action: "wait-for-deterministic-review"
          landing_policy: "merge-after-review"

        review_context_packet:
          source_execution_unit: "G38"
          parent_intent_root: "intents/intent-cli/intent-tree/00-map.md"
          intent_references:
          - "ICL.P.PRODUCT_GOAL"
          rules_and_specs:
          - "intents/intent-cli/specs/03-queue-json-and-jsonl-schema.md"
          acceptance_criteria:
          - queue item inserted
          deterministic_review_checks:
          - "queue insertion remains thin"
          clarification_return_path: "intents/intent-cli/clarifications/open.md"
        """;
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        private readonly string rootPath = Directory.CreateTempSubdirectory("intent-cli-queue-enqueue-command-tests-").FullName;

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
