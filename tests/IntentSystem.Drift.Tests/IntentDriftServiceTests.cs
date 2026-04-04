using IntentSystem.Drift.Models;
using IntentSystem.Projection.Serialization;
using IntentSystem.Supervisor.Models;
using IntentSystem.Supervisor.Serialization;

namespace IntentSystem.Drift.Tests;

public sealed class IntentDriftServiceTests
{
    [Fact]
    public void Process_GivenAcceptedContractBreaking_EnqueuesCorrectiveIssueAndAppendsRunLog()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        var queueStatePath = tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "queue-state.json"),
            QueueStateSerializer.Serialize(CreateQueueState()));
        var runLogPath = tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "runs.jsonl"),
            string.Empty);

        var result = IntentDriftService.Process(
            CreateQueueState(),
            [
                new ChangedCanonicalRef
                {
                    CanonicalRef = "intents/rules/intent-diff-and-corrective-issues.md",
                    Classification = DriftClassification.AcceptedContractBreaking,
                    AffectedExecutionUnits = ["G9"],
                    DriftSummary = "required field baseline changed"
                }
            ],
            repoRoot,
            queueStatePath,
            runLogPath,
            DateTimeOffset.Parse("2026-04-04T05:00:00Z"));

        var reportItem = Assert.Single(result.Report.Items);
        Assert.Equal("G9", reportItem.ExecutionUnit);
        Assert.Equal(DriftClassification.AcceptedContractBreaking, reportItem.Classification);
        Assert.Equal("G9-corrective", reportItem.CorrectiveExecutionUnit);

        var correctiveItem = result.UpdatedQueueState.Items.Single(item => item.ExecutionUnit == "G9-corrective");
        Assert.Equal(QueueItemState.Queued, correctiveItem.State);
        Assert.Equal(["G9"], correctiveItem.Dependencies);
        Assert.Equal(".intent-cli/issues/G9-corrective/packet.yaml", correctiveItem.PacketPaths.Yaml);

        var packet = ProjectionPacketSerializer.Deserialize(
            File.ReadAllText(Path.Combine(repoRoot, ".intent-cli", "issues", "G9-corrective", "packet.yaml")));
        Assert.Equal("G9-corrective", packet.ImplementationIssuePacket.SourceExecutionUnit);
        Assert.Contains(
            "required field baseline changed",
            packet.ImplementationIssuePacket.IntentBaseline[0],
            StringComparison.Ordinal);

        var persistedQueueState = QueueStateSerializer.Deserialize(File.ReadAllText(queueStatePath));
        Assert.Equal(3, persistedQueueState.Items.Count);

        var runEvents = RunLogSerializer.DeserializeAll(File.ReadAllText(runLogPath));
        var correctiveEvent = Assert.Single(runEvents);
        Assert.Equal("G9-corrective", correctiveEvent.ExecutionUnit);
        Assert.Equal("corrective-enqueued", correctiveEvent.Event);
        Assert.Contains("required field baseline changed", correctiveEvent.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void Process_GivenDocumentationOnly_DoesNotGenerateCorrectiveIssue()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        var queueStatePath = tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "queue-state.json"),
            QueueStateSerializer.Serialize(CreateQueueState()));
        var runLogPath = tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "runs.jsonl"),
            string.Empty);

        var result = IntentDriftService.Process(
            CreateQueueState(),
            [
                new ChangedCanonicalRef
                {
                    CanonicalRef = "intents/rules/issue-template-and-review-context.md",
                    Classification = DriftClassification.DocumentationOnly,
                    AffectedExecutionUnits = ["G9"],
                    DriftSummary = "wording only"
                }
            ],
            repoRoot,
            queueStatePath,
            runLogPath,
            DateTimeOffset.Parse("2026-04-04T05:00:00Z"));

        var reportItem = Assert.Single(result.Report.Items);
        Assert.Equal(DriftClassification.DocumentationOnly, reportItem.Classification);
        Assert.Null(reportItem.CorrectiveExecutionUnit);
        Assert.Equal(2, result.UpdatedQueueState.Items.Count);
        Assert.Empty(result.AppendedEvents);
        Assert.Equal(QueueStateSerializer.Serialize(CreateQueueState()), File.ReadAllText(queueStatePath));
        Assert.Empty(File.ReadAllText(runLogPath));
        Assert.False(Directory.Exists(Path.Combine(repoRoot, ".intent-cli", "issues", "G9-corrective")));
    }

    [Fact]
    public void Process_GivenMultipleClassifications_UsesHighestSeverityForAcceptedItem()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        var queueStatePath = tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "queue-state.json"),
            QueueStateSerializer.Serialize(CreateQueueState()));
        var runLogPath = tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "runs.jsonl"),
            string.Empty);

        var result = IntentDriftService.Process(
            CreateQueueState(),
            [
                new ChangedCanonicalRef
                {
                    CanonicalRef = "intents/intent-cli/specs/08-config-and-run-model.md",
                    Classification = DriftClassification.StateOnly,
                    AffectedExecutionUnits = ["G9"],
                    DriftSummary = "artifact metadata migration"
                },
                new ChangedCanonicalRef
                {
                    CanonicalRef = "intents/rules/intent-diff-and-corrective-issues.md",
                    Classification = DriftClassification.AcceptedContractBreaking,
                    AffectedExecutionUnits = ["G9"],
                    DriftSummary = "outward contract baseline changed"
                }
            ],
            repoRoot,
            queueStatePath,
            runLogPath,
            DateTimeOffset.Parse("2026-04-04T05:00:00Z"));

        var reportItem = Assert.Single(result.Report.Items);
        Assert.Equal(DriftClassification.AcceptedContractBreaking, reportItem.Classification);
        Assert.Equal(
            [
                "intents/intent-cli/specs/08-config-and-run-model.md",
                "intents/rules/intent-diff-and-corrective-issues.md"
            ],
            reportItem.ChangedCanonicalRefs);
    }

    [Fact]
    public void Process_GivenNonAcceptedQueueItems_IgnoresThemForAffectedUnitSelection()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        var queueState = CreateQueueState() with
        {
            Items =
            [
                CreateQueueItem("G9", QueueItemState.Completed),
                CreateQueueItem("G10", QueueItemState.Review)
            ]
        };
        var queueStatePath = tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "queue-state.json"),
            QueueStateSerializer.Serialize(queueState));
        var runLogPath = tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "runs.jsonl"),
            string.Empty);

        var result = IntentDriftService.Process(
            queueState,
            [
                new ChangedCanonicalRef
                {
                    CanonicalRef = "intents/rules/intent-diff-and-corrective-issues.md",
                    Classification = DriftClassification.AcceptedContractBreaking,
                    AffectedExecutionUnits = ["G9", "G10"],
                    DriftSummary = "contract changed"
                }
            ],
            repoRoot,
            queueStatePath,
            runLogPath,
            DateTimeOffset.Parse("2026-04-04T05:00:00Z"));

        var reportItem = Assert.Single(result.Report.Items);
        Assert.Equal("G9", reportItem.ExecutionUnit);
        Assert.DoesNotContain(result.UpdatedQueueState.Items, item => item.ExecutionUnit == "G10-corrective");
    }

    private static QueueState CreateQueueState()
    {
        return new QueueState
        {
            SchemaVersion = "1",
            UpdatedAt = DateTimeOffset.Parse("2026-04-03T10:12:34Z"),
            Items =
            [
                CreateQueueItem("G9", QueueItemState.Completed),
                CreateQueueItem("G10", QueueItemState.Review)
            ]
        };
    }

    private static QueueItem CreateQueueItem(string executionUnit, QueueItemState state)
    {
        return new QueueItem
        {
            ExecutionUnit = executionUnit,
            Title = $"[{executionUnit}] Item",
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
            WorkerRole = "coder",
            ReviewRole = "reviewer",
            Priority = "normal"
        };
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        private readonly string rootPath = Directory.CreateTempSubdirectory("intent-drift-tests-").FullName;

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
