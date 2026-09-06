using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;
using IntentSystem.Supervisor.Models;
using IntentSystem.Supervisor.Serialization;
using Xunit.Abstractions;

namespace IntentSystem.Cli.Tests;

/// <summary>
/// G806: published issue findings distinguish missing intake from an intake
/// delivered to orchestrator but not yet dispatched into the runtime queue.
/// The analyzer is read-only; the only state transition remains the ordinary
/// orchestrator queue transition command.
/// </summary>
[Collection(AutomationStalledWorkSharedStateCollection.Name)]
public sealed class AutomationStalledWorkG806Tests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 9, 5, 12, 0, 0, TimeSpan.Zero);
    private readonly G806Workspace workspace = new();
    private readonly ITestOutputHelper output;

    public AutomationStalledWorkG806Tests(ITestOutputHelper output)
    {
        this.output = output;
        AutomationStalledWorkCommand.CandidateListerFactory = null;
        AutomationStalledWorkCommand.UtcNowFactory = () => Now;
        AutomationStalledWorkCommand.GitCommandRunnerFactory = null;
    }

    public void Dispose()
    {
        AutomationStalledWorkCommand.CandidateListerFactory = null;
        AutomationStalledWorkCommand.UtcNowFactory = null;
        AutomationStalledWorkCommand.GitCommandRunnerFactory = null;
        workspace.Dispose();
    }

    [Fact]
    public void G806_AC1_AC2_NoIntakeAndDeliveredIntakeAreDistinctWithOwnerEvidence()
    {
        workspace.WritePacket("G806");
        var noIntakeIssue = BuildIssue(1801, "G806: no intake", Now.AddMinutes(-80));
        var noIntake = Analyze(noIntakeIssue, "G806");
        var noIntakeFinding = Assert.Single(noIntake.Items, item => item.ExecutionUnit == "G806");
        Assert.Equal(AutomationStalledWorkCommand.KindPublishedWithoutIntake, noIntakeFinding.Kind);
        Assert.Equal("architect", noIntakeFinding.OriginatingRole);
        Assert.Contains("notify delegate", noIntakeFinding.RecommendedAction, StringComparison.Ordinal);

        var deliveredIssue = BuildIssue(1802, "G806: delivered intake", Now.AddMinutes(-70));
        var pending = BuildPending(
            "G806-intake",
            "G806-delivered",
            delegatingRole: "architect",
            dispatchedAt: Now.AddMinutes(-55));
        Assert.True(NotifyPendingDelegationStore.WriteDispatch(workspace.Root, pending).Written);
        Assert.True(NotifyDelegationDeliveryStore.Write(workspace.Root, pending, Now.AddMinutes(-42)).Written);
        var delivered = Analyze(deliveredIssue, "G806");
        var awaiting = Assert.Single(delivered.Items, item => item.ExecutionUnit == "G806");
        Assert.Equal(AutomationStalledWorkCommand.KindPublishedIntakeAwaitingDispatch, awaiting.Kind);
        Assert.Equal("G806-intake", awaiting.IntakeTaskId);
        Assert.Equal("architect", awaiting.IntakeDeliveringRole);
        Assert.Equal(42, awaiting.IntakeMinutesElapsed);
        Assert.Equal(Now.AddMinutes(-42), awaiting.IntakeDeliveredAt);
        Assert.Contains("queue transition G806 active", awaiting.RecommendedAction, StringComparison.Ordinal);

        output.WriteLine(JsonSerializer.Serialize(new { no_intake = noIntakeFinding, awaiting_dispatch = awaiting }, new JsonSerializerOptions { WriteIndented = true }));
    }

    [Fact]
    public void G806_AC3_RecordedLegacyDesignOriginIsNamedWithoutRuntimeRolePairing()
    {
        workspace.WritePacket("G806");
        workspace.WriteTopology("design");
        var issue = BuildIssue(1803, "G806 legacy design origin", Now.AddMinutes(-75));
        var result = Analyze(issue, "G806");
        var finding = Assert.Single(result.Items, item => item.ExecutionUnit == "G806");
        Assert.Equal(AutomationStalledWorkCommand.KindPublishedWithoutIntake, finding.Kind);
        Assert.Equal("design", finding.OriginatingRole);
        Assert.Contains("design owes intake delegation", finding.RecommendedAction, StringComparison.Ordinal);
        output.WriteLine(JsonSerializer.Serialize(finding, new JsonSerializerOptions { WriteIndented = true }));
    }

    [Fact]
    public void G806_AC5_AwaitingDispatchDisappearsAfterRuntimeQueueTransition()
    {
        workspace.WritePacket("G806");
        var issue = BuildIssue(1804, "G806: dispatch transition", Now.AddMinutes(-60));
        var pending = BuildPending("G806-dispatch-intake", "G806", "architect", Now.AddMinutes(-45));
        Assert.True(NotifyPendingDelegationStore.WriteDispatch(workspace.Root, pending).Written);
        Assert.True(NotifyDelegationDeliveryStore.Write(workspace.Root, pending, Now.AddMinutes(-35)).Written);
        var awaiting = Analyze(issue, "G806");
        Assert.Contains(awaiting.Items, item => item.Kind == AutomationStalledWorkCommand.KindPublishedIntakeAwaitingDispatch);

        workspace.WriteQueueState(BuildQueueState("G806", QueueItemState.Active, issue.Number));
        var dispatched = Analyze(issue, "G806");
        Assert.DoesNotContain(dispatched.Items, item => item.ExecutionUnit == "G806");
        output.WriteLine("before=published-intake-awaiting-dispatch\nafter=runtime-dispatched; findings=0");
    }

    [Fact]
    public void G806_AC4_AC6_NonOrchestratorIntakeAndStalledScanPreserveBytes()
    {
        workspace.WritePacket("G806");
        var issue = BuildIssue(1805, "G806: read-only", Now.AddMinutes(-90));
        var pending = BuildPending("G806-read-only-intake", "G806", "steward", Now.AddMinutes(-75), recipientRole: "builder");
        Assert.True(NotifyPendingDelegationStore.WriteDispatch(workspace.Root, pending).Written);
        Assert.True(NotifyDelegationDeliveryStore.Write(workspace.Root, pending, Now.AddMinutes(-65)).Written);
        workspace.WriteQueueState(BuildQueueState("G806", QueueItemState.Queued, issue.Number));
        var beforeFiles = SnapshotFiles(workspace.Root);
        var before = Digest(beforeFiles);

        // A non-orchestrator delivery is not intake for this finding. It is
        // still observable, but it cannot flip the queue marker.
        var result = Analyze(issue, "G806");
        Assert.DoesNotContain(result.Items, item => item.Kind == AutomationStalledWorkCommand.KindPublishedIntakeAwaitingDispatch);
        Assert.Contains(result.Items, item => item.Kind == AutomationStalledWorkCommand.KindPublishedWithoutIntake);
        var afterFiles = SnapshotFiles(workspace.Root);
        var after = Digest(afterFiles);
        if (before != after)
        {
            output.WriteLine($"before={before}\nafter={after}");
            foreach (var path in beforeFiles.Keys.Union(afterFiles.Keys).OrderBy(path => path, StringComparer.Ordinal))
            {
                beforeFiles.TryGetValue(path, out var oldHash);
                afterFiles.TryGetValue(path, out var newHash);
                output.WriteLine($"file={path} before={oldHash} after={newHash}");
            }
        }
        Assert.Equal(before, after);
        output.WriteLine($"queue_state_unchanged={before == after}; non_orchestrator_intake=not-counted; bytes={before}");
    }

    [Theory]
    [InlineData("architect")]
    [InlineData("design")]
    [InlineData("steward")]
    public void G806_AC4_NotifyDelegateFromNonOrchestratorLeavesQueueBytesUnchanged(string fromRole)
    {
        workspace.WriteNotifyTopology();
        workspace.WriteQueueState(BuildQueueState("G806", QueueItemState.Queued, 1806));
        var before = File.ReadAllBytes(workspace.Context.GetQueueStatePath());
        using var writer = new StringWriter();
        var exitCode = NotifyCommand.ExecuteDelegate(
            workspace.Context,
            [
                "--domain", "intent-cli", "--team", "intent-cli-dev", "--from", fromRole,
                "--to", "builder", "--report-to", "architect", "--task-id", $"G806-{fromRole}-intake",
                "--objective", "deliver G806 to orchestrator", "--input", "issue=1806",
                "--expected-artifact", "published issue intake", "--result-nonce", $"G806-{fromRole}-nonce",
                "--event-kind", "completion", "--write", "--format", "json",
            ],
            writer);
        Assert.True(exitCode == 0, writer.ToString());
        Assert.Equal(before, File.ReadAllBytes(workspace.Context.GetQueueStatePath()));
        output.WriteLine($"from={fromRole}; exit={exitCode}; queue_bytes_unchanged=true; queue_transition=none; output={writer}");
    }

    [Fact]
    public void G806_AC7_G799PublishedFixtureUsesAwaitingDispatchAndCorrectElapsed()
    {
        workspace.WritePacket("G799");
        var issue = BuildIssue(1744, "G799: published implementation", Now.AddMinutes(-120));
        var pending = BuildPending("G799-implementation-v1", "G799", "orchestration", Now.AddMinutes(-90));
        Assert.True(NotifyPendingDelegationStore.WriteDispatch(workspace.Root, pending).Written);
        Assert.True(NotifyDelegationDeliveryStore.Write(workspace.Root, pending, Now.AddMinutes(-25)).Written);
        var result = Analyze(issue, "G799");
        var finding = Assert.Single(result.Items, item => item.ExecutionUnit == "G799");
        Assert.Equal(AutomationStalledWorkCommand.KindPublishedIntakeAwaitingDispatch, finding.Kind);
        Assert.Equal("G799-implementation-v1", finding.IntakeTaskId);
        Assert.Equal("orchestration", finding.IntakeDeliveringRole);
        Assert.Equal(25, finding.IntakeMinutesElapsed);
        output.WriteLine(JsonSerializer.Serialize(finding, new JsonSerializerOptions { WriteIndented = true }));
    }

    private AutomationStalledWorkResult Analyze(
        GitHubAutomationIssueCandidate issue,
        string? executionUnit = null)
    {
        var unit = executionUnit ?? "G806";
        AutomationStalledWorkCommand.CandidateListerFactory = () => new FakeLister([issue]);
        return AutomationStalledWorkCommand.Analyze(
            workspace.Context,
            "intent-cli",
            "J-Tech-Japan/intent-system",
            staleMinutes: 0,
            team: "intent-cli-dev");
    }

    private static GitHubAutomationIssueCandidate BuildIssue(int number, string title, DateTimeOffset createdAt) => new()
    {
        Number = number,
        Title = title,
        Url = $"https://github.com/J-Tech-Japan/intent-system/issues/{number}",
        CreatedAt = createdAt.ToString("O"),
        UpdatedAt = createdAt.ToString("O"),
        State = "OPEN",
        Labels = [new GitHubAutomationLabel { Name = "intent-target" }],
    };

    private static NotifyPendingDelegation BuildPending(
        string taskId,
        string unit,
        string delegatingRole,
        DateTimeOffset dispatchedAt,
        string recipientRole = "orchestrator") => new()
    {
        Domain = "intent-cli",
        Team = "intent-cli-dev",
        TaskId = taskId,
        DelegatingRole = delegatingRole,
        RecipientRole = recipientRole,
        RecipientIdentity = "orchestrator-seat",
        ExpectedArtifact = $"https://github.com/J-Tech-Japan/intent-system/issues/{unit}",
        ExpectedArtifacts = [$"https://github.com/J-Tech-Japan/intent-system/issues/{unit}"],
        Objective = $"Deliver {unit} intake to orchestrator.",
        Inputs = [$"https://github.com/J-Tech-Japan/intent-system/issues/{unit}"],
        ResultNonce = $"{taskId}-nonce",
        DispatchedAt = dispatchedAt,
    };

    private static string BuildQueueState(string unit, QueueItemState state, int issueNumber) =>
        QueueStateSerializer.Serialize(new QueueState
        {
            SchemaVersion = "1",
            UpdatedAt = Now,
            Items =
            [
                new QueueItem
                {
                    ExecutionUnit = unit,
                    Title = unit + " title",
                    State = state,
                    Dependencies = [],
                    BlockedBy = [],
                    ClarificationReturnPath = string.Empty,
                    PacketPaths = new PacketPaths { Yaml = "a", Implementation = "b", ReviewContext = "c" },
                    LinkedIssue = new LinkedIssue
                    {
                        Repo = "J-Tech-Japan/intent-system",
                        Number = issueNumber,
                        Url = $"https://github.com/J-Tech-Japan/intent-system/issues/{issueNumber}",
                    },
                    LinkedPr = null,
                    WorkerRole = "builder",
                    ReviewRole = "reviewer",
                    Priority = "normal",
                },
            ],
        });

    private static Dictionary<string, string> SnapshotFiles(string root)
    {
        return Directory.GetFiles(root, "*", SearchOption.AllDirectories)
            .Where(path => !path.Contains("/bin/", StringComparison.Ordinal) && !path.Contains("/obj/", StringComparison.Ordinal))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToDictionary(
                path => path[(root.Length + 1)..],
                path => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))),
                StringComparer.Ordinal);
    }

    private static string Digest(IReadOnlyDictionary<string, string> files) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("\n", files.Select(pair => pair.Key + "=" + pair.Value)))));

    private sealed class FakeLister(IReadOnlyList<GitHubAutomationIssueCandidate> issues) : IGitHubAutomationCandidateLister
    {
        public IReadOnlyList<GitHubAutomationPrCandidate> ListPullRequests(string repo, IReadOnlyCollection<string> requiredLabels) => [];
        public IReadOnlyList<GitHubAutomationIssueCandidate> ListIssues(string repo, IReadOnlyCollection<string> requiredLabels) => issues;
    }

    private sealed class G806Workspace : IDisposable
    {
        public G806Workspace()
        {
            Root = Directory.CreateTempSubdirectory("stalled-work-g806-").FullName;
            Directory.CreateDirectory(Path.Combine(Root, ".intent-cli"));
            Context = new CliContext
            {
                RepoRoot = Root,
                Config = new CliConfig
                {
                    Project = new ProjectConfig { Domain = "intent-cli", ArtifactRoot = ".intent-cli", WorktreeRoot = ".intent-cli/worktrees" },
                },
            };
        }

        public string Root { get; }
        public CliContext Context { get; }

        public void WritePacket(string unit) => WriteFile(
            $".intent-cli/issues/{unit}/packet.yaml",
            "implementation_issue_packet:\n  domain: intent-cli\n");

        public void WriteQueueState(string content) => File.WriteAllText(Context.GetQueueStatePath(), content);

        public void WriteTopology(string role)
        {
            WriteFile(
                ".intent-cli/topology/intent-cli/intent-cli-dev.json",
                $$"""
                {
                  "domain": "intent-cli",
                  "team": "intent-cli-dev",
                  "workspace_id": "g806",
                  "roles": {
                    "{{role}}": { "resident": "external", "reader": "codex" }
                  }
                }
                """);
        }

        public void WriteNotifyTopology()
        {
            WriteFile(
                ".intent-cli/topology/intent-cli/intent-cli-dev.json",
                """
                {
                  "domain": "intent-cli",
                  "team": "intent-cli-dev",
                  "workspace_id": "g806",
                  "roles": {
                    "architect": { "resident": "external", "reader": "codex" },
                    "orchestrator": { "resident": "external", "reader": "codex" },
                    "builder": { "resident": "external", "reader": "codex" },
                    "reviewer": { "resident": "external", "reader": "codex" },
                    "steward": { "resident": "external", "reader": "codex" },
                    "design": { "resident": "external", "reader": "codex" }
                  }
                }
            """);
            using var writer = new StringWriter();
            Assert.Equal(0, SessionLayerCommand.ExecuteSet(
                Context,
                ["--domain", "intent-cli", "--team", "intent-cli-dev", "--mode", SessionLayerMode.HerdrOnly, "--write", "--format", "json"],
                writer));
        }

        public void WriteFile(string relativePath, string content)
        {
            var path = Path.Combine(Root, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content);
        }

        public void Dispose()
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }
    }
}
