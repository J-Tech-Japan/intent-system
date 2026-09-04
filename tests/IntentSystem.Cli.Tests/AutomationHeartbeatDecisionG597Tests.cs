using System.Text.Json;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

[Collection(AutomationStalledWorkSharedStateCollection.Name)]
public sealed class AutomationHeartbeatDecisionG597Tests : IDisposable
{
    private static readonly DateTimeOffset FixedNow = new(2026, 8, 3, 10, 0, 0, TimeSpan.Zero);

    public AutomationHeartbeatDecisionG597Tests()
    {
        AutomationStalledWorkCommand.CandidateListerFactory = null;
        AutomationStalledWorkCommand.UtcNowFactory = () => FixedNow;
        OperatorAttentionCommand.UtcNowFactory = () => FixedNow;
    }

    public void Dispose()
    {
        AutomationStalledWorkCommand.CandidateListerFactory = null;
        AutomationStalledWorkCommand.UtcNowFactory = null;
        OperatorAttentionCommand.UtcNowFactory = null;
    }

    [Fact]
    public void Execute_ClosedVerdictsCoverHealthyActionableOperatorAndCannotDetermine_G597()
    {
        using var healthy = new HeartbeatWorkspace();
        healthy.WriteTopology();
        healthy.WritePacketDomain("G589");
        var ciIssue = Issue(1281, "G589: CI wait", FixedNow.AddHours(-2), "intent-pr-created");
        var pendingPr = Pr(1282, ciIssue.Title, FixedNow.AddMinutes(-20), ciIssue.Number,
            [CheckRun("IN_PROGRESS")]);
        var healthyResult = Run(healthy, new FakeLister([ciIssue], [pendingPr]));
        Assert.Equal("healthy-active-wait", healthyResult.GetProperty("verdict").GetString());
        Assert.Equal("github.pr.status_check_rollup", healthyResult.GetProperty("last_progress_source").GetString());
        Assert.Equal("CI for PR #1282 head deadbeef to reach a terminal outcome",
            healthyResult.GetProperty("wait_condition").GetString());
        Assert.Equal("the mode-specific CI-completion wake followed by an exact-head GitHub re-check",
            healthyResult.GetProperty("wait_end_signal").GetString());
        Assert.Equal(45, healthyResult.GetProperty("wait_bound_minutes").GetInt32());
        Assert.Equal("orchestration", healthyResult.GetProperty("target_role").GetString());

        using var actionable = new HeartbeatWorkspace();
        actionable.WriteTopology();
        actionable.WritePacketDomain("G600");
        var oldIssue = Issue(1600, "G600: action needed", FixedNow.AddHours(-2), "intent-target");
        var actionableResult = Run(actionable, new FakeLister([oldIssue]));
        Assert.Equal("actionable-stall", actionableResult.GetProperty("verdict").GetString());
        Assert.Equal("orchestration", actionableResult.GetProperty("action_owner").GetString());
        Assert.Equal("orchestration", actionableResult.GetProperty("target_role").GetString());
        Assert.Contains("intent-cli notify report", actionableResult.GetProperty("canonical_notify_command").GetString(), StringComparison.Ordinal);
        Assert.DoesNotContain("herdr agent prompt", actionableResult.GetProperty("canonical_notify_command").GetString(), StringComparison.Ordinal);

        using var operatorRequired = new HeartbeatWorkspace();
        operatorRequired.WriteTopology();
        operatorRequired.OpenAttention("G596-operator", "Approve the operator decision");
        var operatorResult = Run(operatorRequired, new FakeLister());
        Assert.Equal("operator-required", operatorResult.GetProperty("verdict").GetString());
        Assert.Equal("operator", operatorResult.GetProperty("action_owner").GetString());
        Assert.Contains("G596-operator", operatorResult.GetProperty("reason").GetString(), StringComparison.Ordinal);
        Assert.Contains("Operator action required", operatorResult.GetProperty("suggested_action").GetString(), StringComparison.Ordinal);
        Assert.False(operatorResult.TryGetProperty("canonical_notify_command", out _));

        using var unresolved = new HeartbeatWorkspace();
        var cannotDetermine = Run(unresolved, new FakeLister());
        Assert.Equal("cannot-determine", cannotDetermine.GetProperty("verdict").GetString());
        Assert.Contains("Recorded role topology", cannotDetermine.GetProperty("reason").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_RecordedOrchestratorAliasResolvesAndEmitsDestination_G723()
    {
        using var workspace = new HeartbeatWorkspace();
        workspace.WriteTopology("orchestrator");
        workspace.WritePacketDomain("G600");
        var issue = Issue(1600, "G600: action needed", FixedNow.AddHours(-2), "intent-target");

        var result = Run(workspace, new FakeLister([issue]));

        Assert.Equal("actionable-stall", result.GetProperty("verdict").GetString());
        Assert.Equal("orchestration", result.GetProperty("target_role").GetString());
        Assert.Equal("orchestrator", result.GetProperty("resolved_recorded_role").GetString());
        Assert.Equal("pane:wH:p1", result.GetProperty("resolved_destination").GetString());
        Assert.Contains("--to orchestration", result.GetProperty("canonical_notify_command").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_GenuineMissingCoordinatingSeatExplainsTheRecordAction_G723()
    {
        using var workspace = new HeartbeatWorkspace();
        workspace.WriteTopology(coordinatingRole: null);

        var result = Run(workspace, new FakeLister());

        Assert.Equal("cannot-determine", result.GetProperty("verdict").GetString());
        var reason = result.GetProperty("reason").GetString()!;
        Assert.Contains("logical role 'orchestration'", reason, StringComparison.Ordinal);
        Assert.Contains("accepted recorded alias 'orchestration'", reason, StringComparison.Ordinal);
        Assert.Contains("session-layer topology record", reason, StringComparison.Ordinal);
        Assert.Contains("--write", reason, StringComparison.Ordinal);
        Assert.Contains("do not rename", reason, StringComparison.Ordinal);
        Assert.Contains("session-layer topology record", result.GetProperty("suggested_action").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_DedupeKeyIsStableAcrossPollsAndChangesOnlyWithMaterialState_G597()
    {
        using var workspace = new HeartbeatWorkspace();
        workspace.WriteTopology();
        workspace.WritePacketDomain("G600");
        workspace.WritePacketDomain("G601");
        var firstIssue = Issue(1600, "G600: action needed", FixedNow.AddHours(-2), "intent-target");
        var first = Run(workspace, new FakeLister([firstIssue]));
        var repeated = Run(workspace, new FakeLister([firstIssue]));
        var secondIssue = Issue(1601, "G601: different action", FixedNow.AddHours(-2), "intent-target");
        var changed = Run(workspace, new FakeLister([secondIssue]));

        var key = first.GetProperty("dedupe_key").GetString()!;
        Assert.Equal(key, repeated.GetProperty("dedupe_key").GetString());
        Assert.NotEqual(key, changed.GetProperty("dedupe_key").GetString());
        Assert.DoesNotContain("120", key, StringComparison.Ordinal);
        Assert.DoesNotContain("2026", key, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_OverdueCiWaitBecomesActionableAndNeverRemainsHealthy_G597()
    {
        using var workspace = new HeartbeatWorkspace();
        workspace.WriteTopology();
        workspace.WritePacketDomain("G589");
        var issue = Issue(1281, "G589: CI wait", FixedNow.AddHours(-2), "intent-pr-created");
        var overduePr = Pr(1282, issue.Title, FixedNow.AddMinutes(-46), issue.Number, [CheckRun("IN_PROGRESS")]);

        var result = Run(workspace, new FakeLister([issue], [overduePr]));

        Assert.Equal("actionable-stall", result.GetProperty("verdict").GetString());
        Assert.Contains("exceeded its 45-minute bound", result.GetProperty("reason").GetString(), StringComparison.Ordinal);
        Assert.True(result.GetProperty("stale").GetBoolean());
        Assert.True(result.TryGetProperty("message_body", out var messageBody));
        Assert.False(string.IsNullOrWhiteSpace(messageBody.GetString()));
        Assert.True(result.TryGetProperty("canonical_notify_command", out _));
    }

    [Fact]
    public void Execute_SameIdlePipelineClassifiesNoWaitCiWaitAndOperatorAttention_G597()
    {
        using var neither = new HeartbeatWorkspace();
        neither.WriteTopology();
        var neitherResult = Run(neither, new FakeLister());
        Assert.Equal("actionable-stall", neitherResult.GetProperty("verdict").GetString());

        using var ciWait = new HeartbeatWorkspace();
        ciWait.WriteTopology();
        ciWait.WritePacketDomain("G589");
        var ciIssue = Issue(1281, "G589: CI wait", FixedNow.AddHours(-2), "intent-pr-created");
        var pendingPr = Pr(1282, ciIssue.Title, FixedNow.AddMinutes(-20), ciIssue.Number,
            [CheckRun("IN_PROGRESS")]);
        var ciWaitResult = Run(ciWait, new FakeLister([ciIssue], [pendingPr]));
        Assert.Equal("healthy-active-wait", ciWaitResult.GetProperty("verdict").GetString());

        using var operatorWait = new HeartbeatWorkspace();
        operatorWait.WriteTopology();
        operatorWait.OpenAttention("same-idle-pipeline", "Approve the operator decision");
        var operatorResult = Run(operatorWait, new FakeLister());
        Assert.Equal("operator-required", operatorResult.GetProperty("verdict").GetString());
    }

    [Fact]
    public void Execute_ActionableStallOutranksFreshCiWait_G597()
    {
        using var workspace = new HeartbeatWorkspace();
        workspace.WriteTopology();
        workspace.WritePacketDomain("G600");
        workspace.WritePacketDomain("G589");
        var stalledIssue = Issue(1600, "G600: action needed", FixedNow.AddHours(-6), "intent-target");
        var ciIssue = Issue(1281, "G589: CI wait", FixedNow.AddHours(-2), "intent-pr-created");
        var pendingPr = Pr(1282, ciIssue.Title, FixedNow.AddMinutes(-5), ciIssue.Number,
            [CheckRun("IN_PROGRESS")]);

        var result = Run(workspace, new FakeLister([stalledIssue, ciIssue], [pendingPr]));

        Assert.Equal("actionable-stall", result.GetProperty("verdict").GetString());
        Assert.Contains("G600", result.GetProperty("reason").GetString(), StringComparison.Ordinal);
        Assert.Contains("intent-cli notify report", result.GetProperty("canonical_notify_command").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_TerminalCiEndSignalNoLongerReportsHealthyActiveWait_G597()
    {
        using var workspace = new HeartbeatWorkspace();
        workspace.WriteTopology();
        workspace.WritePacketDomain("G589");
        var issue = Issue(1281, "G589: CI wait", FixedNow.AddHours(-2), "intent-pr-created");
        var completedPr = Pr(1282, issue.Title, FixedNow.AddMinutes(-20), issue.Number,
            [CheckRun("COMPLETED", "SUCCESS")]);

        var result = Run(workspace, new FakeLister([issue], [completedPr]));

        Assert.Equal("actionable-stall", result.GetProperty("verdict").GetString());
        Assert.Contains("ci-all-green-not-transitioned", result.GetProperty("reason").GetString(), StringComparison.Ordinal);
        Assert.True(result.GetProperty("stale").GetBoolean());
    }

    [Fact]
    public void Execute_IsReadOnlyAndDoesNotPersistPollOrDedupeState_G597()
    {
        using var workspace = new HeartbeatWorkspace();
        workspace.WriteTopology();
        workspace.WritePacketDomain("G600");
        var issue = Issue(1600, "G600: action needed", FixedNow.AddHours(-2), "intent-target");
        var before = SnapshotFiles(workspace.RootPath);

        _ = Run(workspace, new FakeLister([issue]));

        Assert.Equal(before, SnapshotFiles(workspace.RootPath));
    }

    [Theory]
    [InlineData("en")]
    [InlineData("ja")]
    public void GuidancePinsOneReadOnlyDecisionContractForBothModes_G597(string language)
    {
        var path = Path.Combine(RepoVersionPolicySource.RepoRoot(), "docs", language, "12-agent-message-orchestration.md");
        var content = File.ReadAllText(path);

        Assert.Contains("healthy-active-wait", content, StringComparison.Ordinal);
        Assert.Contains("actionable-stall", content, StringComparison.Ordinal);
        Assert.Contains("operator-required", content, StringComparison.Ordinal);
        Assert.Contains("cannot-determine", content, StringComparison.Ordinal);
        Assert.Contains("dedupe key", content, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("canonical `intent-cli notify", content, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("only a genuine `stale=true` result ever produces", content, StringComparison.Ordinal);
        Assert.Contains("actionable-stall", content, StringComparison.Ordinal);
    }

    private static JsonElement Run(HeartbeatWorkspace workspace, FakeLister lister)
    {
        AutomationStalledWorkCommand.CandidateListerFactory = () => lister;
        using var writer = new StringWriter();
        var exitCode = AutomationHeartbeatCommand.Execute(
            workspace.Context,
            ["--domain", "intent-cli", "--repo", "J-Tech-Japan/intent-system", "--team", "intent-cli-dev", "--format", "json"],
            writer);
        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        return document.RootElement.Clone();
    }

    private static GitHubAutomationIssueCandidate Issue(int number, string title, DateTimeOffset createdAt, params string[] labels) => new()
    {
        Number = number,
        Title = title,
        Url = $"https://github.com/J-Tech-Japan/intent-system/issues/{number}",
        CreatedAt = createdAt.ToString("O"),
        UpdatedAt = createdAt.ToString("O"),
        State = "OPEN",
        Labels = labels.Select(name => new GitHubAutomationLabel { Name = name }).ToArray(),
    };

    private static GitHubAutomationPrCandidate Pr(
        int number,
        string title,
        DateTimeOffset createdAt,
        int issueNumber,
        IReadOnlyList<GitHubAutomationStatusCheckCandidate> checks) => new()
    {
        Number = number,
        Title = title,
        Url = $"https://github.com/J-Tech-Japan/intent-system/pull/{number}",
        CreatedAt = createdAt.ToString("O"),
        UpdatedAt = createdAt.ToString("O"),
        State = "OPEN",
        IsDraft = false,
        HeadRefOid = "deadbeef",
        Labels = [],
        StatusCheckRollup = checks,
        ClosingIssuesReferences =
        [
            new GitHubPrClosingIssueReference
            {
                Number = issueNumber,
                Repository = new GitHubPrClosingIssueRepository
                {
                    Name = "intent-system",
                    Owner = new GitHubPrClosingIssueRepositoryOwner { Login = "J-Tech-Japan" },
                },
            },
        ],
    };

    private static GitHubAutomationStatusCheckCandidate CheckRun(string status, string conclusion = "") => new()
    {
        TypeName = "CheckRun",
        Status = status,
        Conclusion = conclusion,
    };

    private static IReadOnlyDictionary<string, string> SnapshotFiles(string rootPath) =>
        Directory.GetFiles(rootPath, "*", SearchOption.AllDirectories)
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToDictionary(
                path => Path.GetRelativePath(rootPath, path),
                path => Convert.ToHexString(File.ReadAllBytes(path)),
                StringComparer.Ordinal);

    private sealed class FakeLister(
        IReadOnlyList<GitHubAutomationIssueCandidate>? issues = null,
        IReadOnlyList<GitHubAutomationPrCandidate>? prs = null) : IGitHubAutomationCandidateLister
    {
        public IReadOnlyList<GitHubAutomationPrCandidate> ListPullRequests(string repo, IReadOnlyCollection<string> requiredLabels) =>
            prs ?? [];

        public IReadOnlyList<GitHubAutomationIssueCandidate> ListIssues(string repo, IReadOnlyCollection<string> requiredLabels) =>
            issues ?? [];

        public IReadOnlyList<GitHubAutomationPrCandidate> ListMergedPullRequests(string repo, IReadOnlyCollection<string> requiredLabels) => [];
    }

    private sealed class HeartbeatWorkspace : IDisposable
    {
        public HeartbeatWorkspace()
        {
            RootPath = Directory.CreateTempSubdirectory("heartbeat-g597-").FullName;
            Directory.CreateDirectory(Path.Combine(RootPath, ".intent-cli"));
            Context = new CliContext
            {
                RepoRoot = RootPath,
                Config = new CliConfig { Project = new ProjectConfig { Domain = "intent-cli", ArtifactRoot = ".intent-cli" } },
            };
        }

        public string RootPath { get; }
        public CliContext Context { get; }

        public void WritePacketDomain(string executionUnit)
        {
            var directory = Path.Combine(RootPath, ".intent-cli", "issues", executionUnit);
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "packet.yaml"), "domain: intent-cli\n");
        }

        public void WriteTopology(string? coordinatingRole = "orchestration")
        {
            var path = NotifyRoleTopologyStore.ResolvePath(RootPath, "intent-cli", "intent-cli-dev");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var roles = new Dictionary<string, object>
            {
                ["design"] = new { resident = "herdr", workspace_id = "wH", pane_id = "wH:p3" },
            };
            if (coordinatingRole is not null)
            {
                roles[coordinatingRole] = new { resident = "herdr", workspace_id = "wH", pane_id = "wH:p1" };
            }
            File.WriteAllText(path, JsonSerializer.Serialize(new
            {
                domain = "intent-cli",
                team = "intent-cli-dev",
                workspace_id = "wH",
                roles,
            }));
        }

        public void OpenAttention(string record, string action)
        {
            using var writer = new StringWriter();
            var exitCode = OperatorAttentionCommand.ExecuteOpen(
                Context,
                ["--record", record, "--domain", "intent-cli", "--team", "intent-cli-dev", "--owner", "operator",
                    "--blocking-reference", "https://example.test/decision", "--action-needed", action,
                    "--evidence", "operator must decide", "--write", "--format", "json"],
                writer);
            Assert.Equal(0, exitCode);
        }

        public void Dispose()
        {
            if (Directory.Exists(RootPath))
            {
                Directory.Delete(RootPath, recursive: true);
            }
        }
    }
}
