using System.Text.Json;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;
using IntentSystem.Supervisor.Models;
using IntentSystem.Supervisor.Serialization;

namespace IntentSystem.Cli.Tests;

/// <summary>
/// G746: duplicate execution-unit queue rows are observable, repairable only
/// under a strict dominance rule, and fail closed when the evidence conflicts.
/// </summary>
[Collection("WorkerNextActionSharedState")]
public sealed class DuplicateQueueItemG746Tests : IDisposable
{
    private const string Repo = "J-Tech-Japan/intent-system";

    public DuplicateQueueItemG746Tests()
    {
        AutomationStateDoctorCommand.CandidateListerFactory = null;
        AutomationCloseoutDriftCheckCommand.PrLookupFactory = null;
        AutomationCloseoutDriftCheckCommand.IssueLookupFactory = null;
        AutomationInstalledCliSurfaceProbe.ProbeRunner = null;
        AutomationInstalledCliSurfaceProbe.ExplicitInstalledCliPathReader = null;
    }

    public void Dispose()
    {
        AutomationStateDoctorCommand.CandidateListerFactory = null;
        AutomationCloseoutDriftCheckCommand.PrLookupFactory = null;
        AutomationCloseoutDriftCheckCommand.IssueLookupFactory = null;
        AutomationInstalledCliSurfaceProbe.ProbeRunner = null;
        AutomationInstalledCliSurfaceProbe.ExplicitInstalledCliPathReader = null;
    }

    [Fact]
    public void Analyze_ByteIdenticalDuplicate_IsUnsafeAndShowsBothFullEntries()
    {
        var fullEntry = """{"execution_unit":"G746","state":"queued","linked_pr":null}""";
        var queue = new[]
        {
            Projection("G746", 0, null, fullEntry),
            Projection("G746", 1, null, fullEntry),
        };

        var analysis = AutomationStateDoctorAnalyzer.Analyze(
            Repo,
            queue,
            Array.Empty<StateDoctorPublishEvidence>(),
            Array.Empty<StateDoctorPr>());

        Assert.Empty(analysis.Findings);
        var unsafeFinding = Assert.Single(
            analysis.UnsafeFindings,
            finding => finding.Kind == AutomationStateDoctorUnsafeKinds.DuplicateQueueItem);
        Assert.Equal("G746", unsafeFinding.ExecutionUnit);
        Assert.Contains("unsafe-stop", unsafeFinding.Reason, StringComparison.Ordinal);
        Assert.Contains("entry[0]", unsafeFinding.Reason, StringComparison.Ordinal);
        Assert.Contains("entry[1]", unsafeFinding.Reason, StringComparison.Ordinal);
        Assert.Equal([fullEntry, fullEntry], unsafeFinding.CompetingEntries);
    }

    [Fact]
    public void Analyze_LinkedPrOnlyDifference_ProducesStrictDominanceRepair()
    {
        var queue = new[]
        {
            Projection("G746", 0, null),
            Projection("G746", 1, $"https://github.com/{Repo}/pull/1624"),
        };

        var analysis = AutomationStateDoctorAnalyzer.Analyze(
            Repo,
            queue,
            Array.Empty<StateDoctorPublishEvidence>(),
            Array.Empty<StateDoctorPr>());

        var finding = Assert.Single(
            analysis.Findings,
            candidate => candidate.Category == AutomationStateDoctorCategories.DuplicateQueueItem);
        Assert.Equal(AutomationStateDoctorRepairKinds.DeduplicateQueueItem, finding.RepairKind);
        Assert.Equal(1, finding.QueueItemIndex);
        Assert.Equal([0], finding.RemoveQueueItemIndices);
        Assert.Empty(analysis.UnsafeFindings);
    }

    [Fact]
    public void Execute_Write_AmbiguousDuplicate_IsUnsafeStopAndByteIdentical()
    {
        using var workspace = new TestWorkspace();
        var original = QueueStateSerializer.Serialize(
            new QueueState
            {
                SchemaVersion = "1",
                UpdatedAt = DateTimeOffset.Parse("2026-08-28T03:44:15Z"),
                Items =
                [
                    Item("G746", title: "first competing entry"),
                    Item("G746", title: "second competing entry"),
                ],
            });
        workspace.WriteQueueState(original);
        ConfigureStateDoctorSurface(workspace);
        AutomationStateDoctorCommand.CandidateListerFactory = () => new EmptyLister();

        using var writer = new StringWriter();
        var exit = AutomationStateDoctorCommand.Execute(
            workspace.Context,
            ["--repo", Repo, "--write", "--format", "json"],
            writer);

        Assert.Equal(0, exit);
        using var document = JsonDocument.Parse(writer.ToString());
        var unsafeFinding = Assert.Single(
            document.RootElement.GetProperty("unsafe_findings").EnumerateArray(),
            finding => finding.GetProperty("kind").GetString() == "duplicate-queue-item");
        Assert.Equal("G746", unsafeFinding.GetProperty("execution_unit").GetString());
        var reason = unsafeFinding.GetProperty("reason").GetString()!;
        Assert.Contains("unsafe-stop", reason, StringComparison.Ordinal);
        Assert.Contains("first competing entry", reason, StringComparison.Ordinal);
        Assert.Contains("second competing entry", reason, StringComparison.Ordinal);
        Assert.Equal(2, unsafeFinding.GetProperty("competing_entries").GetArrayLength());

        Assert.Equal(original, workspace.QueueStateOnDisk());
        Assert.False(File.Exists(workspace.Context.GetRunLogPath()));
    }

    [Fact]
    public void Execute_Write_LinkedPrDominance_RemovesOnlyLessInformativeEntry()
    {
        using var workspace = new TestWorkspace();
        var linkedPr = $"https://github.com/{Repo}/pull/1624";
        workspace.WriteQueueState(
            QueueStateSerializer.Serialize(
                new QueueState
                {
                    SchemaVersion = "1",
                    UpdatedAt = DateTimeOffset.Parse("2026-08-28T03:44:15Z"),
                    Items =
                    [
                        Item("G746", linkedPr: null),
                        Item("G746", linkedPr: linkedPr),
                    ],
                }));
        ConfigureStateDoctorSurface(workspace);
        AutomationStateDoctorCommand.CandidateListerFactory = () => new EmptyLister();

        using var writer = new StringWriter();
        var exit = AutomationStateDoctorCommand.Execute(
            workspace.Context,
            ["--repo", Repo, "--write", "--format", "json"],
            writer);

        Assert.Equal(0, exit);
        using var document = JsonDocument.Parse(writer.ToString());
        var finding = Assert.Single(
            document.RootElement.GetProperty("findings").EnumerateArray(),
            candidate => candidate.GetProperty("category").GetString() == "duplicate-queue-item");
        Assert.True(finding.GetProperty("applied").GetBoolean());
        Assert.Equal(1, finding.GetProperty("queue_item_index").GetInt32());
        Assert.Equal([0], finding.GetProperty("remove_queue_item_indices").EnumerateArray().Select(value => value.GetInt32()));

        var after = QueueStateSerializer.Deserialize(workspace.QueueStateOnDisk());
        var item = Assert.Single(after.Items);
        Assert.Equal(linkedPr, item.LinkedPr);
        Assert.Contains("G746", File.ReadAllText(workspace.Context.GetRunLogPath()), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_ReadOnly_DuplicateFreeFixture_IsByteIdentical()
    {
        using var workspace = new TestWorkspace();
        var fixturePath = Path.Combine(
            RepoVersionPolicySource.RepoRoot(),
            "tests",
            "IntentSystem.Cli.Tests",
            "Fixtures",
            "G746",
            "duplicate-free-queue-state.json");
        var fixture = File.ReadAllText(fixturePath);
        workspace.WriteQueueState(fixture);
        ConfigureStateDoctorSurface(workspace);
        AutomationStateDoctorCommand.CandidateListerFactory = () => new EmptyLister();

        using var writer = new StringWriter();
        var exit = AutomationStateDoctorCommand.Execute(
            workspace.Context,
            ["--repo", Repo, "--format", "json"],
            writer);

        Assert.Equal(0, exit);
        Assert.Equal(fixture, workspace.QueueStateOnDisk());
        using var document = JsonDocument.Parse(writer.ToString());
        Assert.Equal(0, document.RootElement.GetProperty("unsafe_findings").GetArrayLength());
    }

    [Fact]
    public void Execute_CloseoutReportsDuplicateAndContinuesWithOtherUnits()
    {
        using var workspace = new TestWorkspace();
        workspace.WriteQueueState(
            QueueStateSerializer.Serialize(
                new QueueState
                {
                    SchemaVersion = "1",
                    UpdatedAt = DateTimeOffset.Parse("2026-08-28T03:44:15Z"),
                    Items =
                    [
                        Item("G746", linkedPr: $"https://github.com/{Repo}/pull/1624", linkedIssue: 1624),
                        Item("G746", linkedPr: $"https://github.com/{Repo}/pull/1624", linkedIssue: 1624),
                        Item("G748", linkedPr: $"https://github.com/{Repo}/pull/1625", linkedIssue: 1625),
                    ],
                }));
        var lookup = new RecordingPrLookup(
            new Dictionary<int, (bool Merged, int[] Closing)>
            {
                [1624] = (true, [1624]),
                [1625] = (true, [1625]),
            });
        AutomationCloseoutDriftCheckCommand.PrLookupFactory = () => lookup;

        using var writer = new StringWriter();
        var exit = AutomationCloseoutDriftCheckCommand.Execute(
            workspace.Context,
            ["--repo", Repo, "--format", "json"],
            writer);

        Assert.Equal(1, exit);
        var result = JsonSerializer.Deserialize<CloseoutDriftCheckResult>(writer.ToString())!;
        Assert.Equal(1, result.SafeRepairCount);
        Assert.Equal(1, result.UnsafeStopCount);
        var duplicate = Assert.Single(
            result.Records,
            record => record.ReasonCode == AutomationCloseoutDriftCheckCommand.ReasonDuplicateQueueItem);
        Assert.Equal("G746", duplicate.ExecutionUnit);
        Assert.Equal(2, duplicate.CompetingEntries!.Count);
        var remaining = Assert.Single(result.Records, record => record.ExecutionUnit == "G748");
        Assert.Equal(AutomationCloseoutDriftCheckCommand.ResultSafeRepair, remaining.Result);
        Assert.Equal([1625], lookup.SeenNumbers);
    }

    [Fact]
    public void DeveloperReference_DocumentsDuplicateFindingAndCanonicalRepair_InBothMirrors()
    {
        var root = RepoVersionPolicySource.RepoRoot();
        foreach (var language in new[] { "en", "ja" })
        {
            var document = File.ReadAllText(
                Path.Combine(root, "docs", language, "09-developer-reference.md"));
            Assert.Contains("G746", document, StringComparison.Ordinal);
            Assert.Contains("duplicate-queue-item", document, StringComparison.Ordinal);
            Assert.Contains("closeout-drift-check", document, StringComparison.Ordinal);
            Assert.Contains("state-doctor", document, StringComparison.Ordinal);
            Assert.Contains("strictly more informative", document, StringComparison.Ordinal);
            Assert.Contains("recoverable", document, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("not impossible", document, StringComparison.OrdinalIgnoreCase);
        }
    }

    private static StateDoctorQueueItem Projection(
        string unit,
        int sourceIndex,
        string? linkedPr,
        string? fullEntry = null) =>
        new()
        {
            ExecutionUnit = unit,
            LinkedPrUrl = linkedPr,
            Completed = false,
            SourceIndex = sourceIndex,
            State = "queued",
            FullEntryJson = fullEntry,
            ComparableFields = new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["title"] = "same title",
                ["state"] = "queued",
                ["linked_pr"] = linkedPr,
                ["linked_issue"] = null,
            },
        };

    private static QueueItem Item(
        string unit,
        string title = "same title",
        string? linkedPr = null,
        int? linkedIssue = null,
        QueueItemState state = QueueItemState.Review) =>
        new()
        {
            ExecutionUnit = unit,
            Title = title,
            State = state,
            Dependencies = Array.Empty<string>(),
            BlockedBy = Array.Empty<string>(),
            ClarificationReturnPath = string.Empty,
            PacketPaths = new PacketPaths
            {
                Implementation = $"issues/{unit}/implementation.md",
                ReviewContext = $"issues/{unit}/review-context.md",
                Yaml = $"issues/{unit}/packet.yaml",
            },
            LinkedIssue = linkedIssue is { } number
                ? new LinkedIssue
                {
                    Repo = Repo,
                    Number = number,
                    Url = $"https://github.com/{Repo}/issues/{number}",
                }
                : null,
            LinkedPr = linkedPr,
            WorkerRole = "coder",
            ReviewRole = "reviewer",
            Priority = "normal",
        };

    private static void ConfigureStateDoctorSurface(TestWorkspace workspace)
    {
        AutomationInstalledCliSurfaceProbe.ExplicitInstalledCliPathReader =
            () => workspace.InstalledCliPath;
        AutomationInstalledCliSurfaceProbe.ProbeRunner = (_, _) =>
            new InstalledCliProbeResult(
                0,
                "automation summary host-review-preflight issue-publish pr-transition review-start request-update approved",
                string.Empty);
    }

    private sealed class EmptyLister : IGitHubAutomationCandidateLister
    {
        public IReadOnlyList<GitHubAutomationPrCandidate> ListPullRequests(
            string repo, IReadOnlyCollection<string> requiredLabels) =>
            Array.Empty<GitHubAutomationPrCandidate>();

        public IReadOnlyList<GitHubAutomationIssueCandidate> ListIssues(
            string repo, IReadOnlyCollection<string> requiredLabels) =>
            Array.Empty<GitHubAutomationIssueCandidate>();
    }

    private sealed class RecordingPrLookup : IGitHubPrLookup
    {
        private readonly IReadOnlyDictionary<int, (bool Merged, int[] Closing)> entries;

        public RecordingPrLookup(
            IReadOnlyDictionary<int, (bool Merged, int[] Closing)> entries) =>
            this.entries = entries;

        public List<int> SeenNumbers { get; } = [];

        public GitHubPrLookupResult Lookup(string repo, int number)
        {
            SeenNumbers.Add(number);
            var entry = entries[number];
            return new GitHubPrLookupResult
            {
                Number = number,
                State = entry.Merged ? "MERGED" : "OPEN",
                Merged = entry.Merged,
                ClosingIssuesReferences = entry.Closing
                    .Select(issue => new GitHubPrClosingIssueReference { Number = issue })
                    .ToArray(),
            };
        }
    }

    private sealed class TestWorkspace : IDisposable
    {
        private readonly string root;

        public TestWorkspace()
        {
            root = Directory.CreateTempSubdirectory("g746-tests-").FullName;
            Directory.CreateDirectory(Path.Combine(root, ".intent-cli"));
            InstalledCliPath = Path.Combine(root, ".intent-cli", "installed-cli-stub");
            File.WriteAllText(InstalledCliPath, "stub");
            Context = new CliContext
            {
                RepoRoot = root,
                Config = new CliConfig
                {
                    Project = new ProjectConfig
                    {
                        Domain = "intent-cli",
                        ArtifactRoot = ".intent-cli",
                    },
                },
            };
        }

        public CliContext Context { get; }
        public string InstalledCliPath { get; }

        public void WriteQueueState(string content) =>
            File.WriteAllText(Context.GetQueueStatePath(), content);

        public string QueueStateOnDisk() =>
            File.ReadAllText(Context.GetQueueStatePath());

        public void Dispose()
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }
}
