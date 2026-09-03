using System.Text.Json;
using System.Text.RegularExpressions;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;
using IntentSystem.Supervisor.Models;
using IntentSystem.Supervisor.Serialization;

namespace IntentSystem.Cli.Tests;

/// <summary>
/// G788: a delivered parent delegation can be visibly executed by recipient
/// downstream work, a child report, or a queue transition. The true-stall
/// branch remains deliberately discriminated from those evidence cases.
/// </summary>
[Collection("WorkerNextActionSharedState")]
public sealed class NotifySupervisionG788Tests : IDisposable
{
    private const string Domain = "intent-cli";
    private const string Team = "intent-cli-dev";
    private readonly string root = Path.Combine(Path.GetTempPath(), $"intent-g788-{Guid.NewGuid():N}");
    private readonly DateTimeOffset firstNow = new(2026, 9, 3, 12, 0, 0, TimeSpan.Zero);
    private DateTimeOffset now;

    public NotifySupervisionG788Tests()
    {
        Directory.CreateDirectory(root);
        now = firstNow;
        NotifyCommand.UtcNowFactory = () => now;
        NotifySupervisor.Delay = _ => { };
    }

    public void Dispose()
    {
        NotifyCommand.UtcNowFactory = null;
        NotifyCommand.ProcessRunnerFactory = null;
        NotifyCommand.AgmsgScriptsDirectoryFactory = null;
        NotifyCommand.HerdrExecutableFactory = null;
        NotifyCommand.BashExecutableFactory = null;
        NotifySupervisionStore.WriteOverride = null;
        NotifyPendingDelegationStore.WriteOverride = null;
        NotifyReportOutboxStore.WriteOverride = null;
        NotifySupervisor.Delay = Thread.Sleep;
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void DiscriminatingPair_TokenCarryingRecipientDownstreamDelegationSuppressesButNoEvidenceFires_G788()
    {
        var suppressed = CreateScenario("pair-with-ledger");
        var child = WriteDownstreamDelegation(suppressed, tokenCarrier: "task-id");
        var suppressedPass = RunAfterWindow(suppressed);

        Assert.DoesNotContain(suppressedPass.Findings, finding =>
            finding.Kind == "delegation-delivered-never-executed");
        var suppressedState = NotifySupervisionStore.Read(
            suppressed.Context.ResolveSupervisionArtifactRootPath(),
            Domain,
            Team);
        Assert.Contains(
            suppressedState.LastCycle!.DelegationExecutionEvidence!,
            observation => observation.Contains($"pending-ledger:task_id={child.TaskId}", StringComparison.Ordinal));
        Assert.Contains(
            suppressedState.LastCycle.DelegationExecutionEvidence!,
            observation => observation.Contains("pending-ledger:count=1", StringComparison.Ordinal));

        var fired = CreateScenario("pair-no-evidence");
        var firedPass = RunAfterWindow(fired);
        var finding = Assert.Single(firedPass.Findings, item =>
            item.Kind == "delegation-delivered-never-executed");

        Assert.Equal("delegation-delivered-never-executed:G788-start:g788-parent-nonce", finding.Key);
        Assert.Equal("design", finding.OwnerRole);
        Assert.Equal("design", finding.WakeTargetRole);
        Assert.Null(finding.WakeClass);
        Assert.Contains("Source-derived execution evidence counts: pending-ledger=0; report-outbox=0; notification-events=0; queue-state=0; continuation-chain=0; expected-artifact=0.", finding.Summary, StringComparison.Ordinal);
        Assert.DoesNotContain("Canonical report absent", finding.Summary, StringComparison.Ordinal);
        Assert.Contains(finding.ConsultedObservations!, item =>
            item.Contains("delegation-execution-evidence: task_id=G788-start; unit=G788", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("task-id")]
    [InlineData("objective")]
    [InlineData("input")]
    public void RecipientDownstreamTokenCanAppearInEveryConfiguredCarrier_G788(string tokenCarrier)
    {
        var scenario = CreateScenario($"downstream-{tokenCarrier}");
        WriteDownstreamDelegation(scenario, tokenCarrier);

        var pass = RunAfterWindow(scenario);

        Assert.DoesNotContain(pass.Findings, finding =>
            finding.Kind == "delegation-delivered-never-executed");
        Assert.Contains(
            NotifySupervisionStore.Read(scenario.Context.ResolveSupervisionArtifactRootPath(), Domain, Team)
                .LastCycle!.DelegationExecutionEvidence!,
            item => item.Contains("pending-ledger:count=1", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("objective")]
    [InlineData("input")]
    public void DeliveredParentTokenFromObjectiveOrInputMatchesRecipientDownstream_G788(string parentTokenCarrier)
    {
        var scenario = CreateScenario(
            $"parent-{parentTokenCarrier}-fallback",
            parentTaskId: "parent-start",
            parentObjective: parentTokenCarrier == "objective"
                ? "coordinate execution unit G788"
                : "coordinate the child delegation",
            parentInputs: parentTokenCarrier == "input" ? ["G788 fixture input"] : ["ordinary input"]);
        var child = WriteDownstreamDelegation(scenario, tokenCarrier: "task-id");

        var pass = RunAfterWindow(scenario);

        Assert.DoesNotContain(pass.Findings, finding =>
            finding.Kind == "delegation-delivered-never-executed");
        var audit = NotifySupervisionStore.Read(
            scenario.Context.ResolveSupervisionArtifactRootPath(),
            Domain,
            Team).LastCycle!.DelegationExecutionEvidence!;
        Assert.Contains(audit, item => item.Contains("task_id=parent-start; unit=G788", StringComparison.Ordinal));
        Assert.Contains(audit, item => item.Contains($"pending-ledger:task_id={child.TaskId}", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("outbox", "task-id")]
    [InlineData("outbox", "summary")]
    [InlineData("outbox", "artifact")]
    [InlineData("event", "task-id")]
    [InlineData("event", "summary")]
    [InlineData("event", "artifact")]
    public void ChildReportEvidenceFromEveryConfiguredCarrierSuppressesFinding_G788(
        string source,
        string tokenCarrier)
    {
        var scenario = CreateScenario($"child-report-{source}-{tokenCarrier}");
        if (source == "outbox")
        {
            WriteChildReportOutbox(scenario, tokenCarrier);
        }
        else
        {
            WriteChildNotificationEvent(scenario, tokenCarrier);
        }

        var pass = RunAfterWindow(scenario);

        Assert.DoesNotContain(pass.Findings, finding =>
            finding.Kind == "delegation-delivered-never-executed");
        var audit = NotifySupervisionStore.Read(
            scenario.Context.ResolveSupervisionArtifactRootPath(),
            Domain,
            Team).LastCycle!.DelegationExecutionEvidence!;
        Assert.Contains(audit, item => item.StartsWith($"{(source == "outbox" ? "report-outbox" : "notification-events")}:count=1", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("delegation")]
    [InlineData("outbox")]
    [InlineData("event")]
    public void EarlierDifferentUnitInChildCarrierDoesNotSuppressTrueStall_G788(string source)
    {
        var scenario = CreateScenario($"earlier-different-unit-{source}");
        switch (source)
        {
            case "delegation":
                WriteDownstreamDelegation(scenario, tokenCarrier: "earlier-different-unit");
                break;
            case "outbox":
                WriteChildReportOutbox(scenario, tokenCarrier: "earlier-different-unit");
                break;
            default:
                WriteChildNotificationEvent(scenario, tokenCarrier: "earlier-different-unit");
                break;
        }

        var pass = RunAfterWindow(scenario);

        var finding = Assert.Single(pass.Findings, item =>
            item.Kind == "delegation-delivered-never-executed");
        Assert.Contains(
            "pending-ledger=0; report-outbox=0; notification-events=0; queue-state=0; continuation-chain=0; expected-artifact=0.",
            finding.Summary,
            StringComparison.Ordinal);
        Assert.DoesNotContain(pass.Findings, item =>
            item.Kind == "delegation-in-progress-no-direct-report");
        Assert.Contains(
            NotifySupervisionStore.Read(scenario.Context.ResolveSupervisionArtifactRootPath(), Domain, Team)
                .LastCycle!.DelegationExecutionEvidence!,
            item => item.StartsWith(
                $"{(source == "delegation" ? "pending-ledger" : source == "outbox" ? "report-outbox" : "notification-events")}:count=0",
                StringComparison.Ordinal));
    }

    [Fact]
    public void QueueTransitionAfterDeliverySuppressesFinding_G788()
    {
        var scenario = CreateScenario("queue-transition");
        WriteQueueTransition(scenario);

        var pass = RunAfterWindow(scenario);

        Assert.DoesNotContain(pass.Findings, finding =>
            finding.Kind == "delegation-delivered-never-executed");
        Assert.Contains(
            NotifySupervisionStore.Read(scenario.Context.ResolveSupervisionArtifactRootPath(), Domain, Team)
                .LastCycle!.DelegationExecutionEvidence!,
            item => item.Contains("queue-state:execution_unit=G788; state=active", StringComparison.Ordinal));
    }

    [Fact]
    public void ExistingExactTaskNotificationEventRemainsExecutionEvidence_G788()
    {
        var scenario = CreateScenario("existing-exact-event");
        Assert.True(NotifyEventWriter.TryResolveWritePath(scenario.Root, Domain, Team, out var path, out var error), error);
        NotifyEventWriter.Append(path, new NotifyDesignEvent
        {
            Timestamp = firstNow.AddSeconds(10),
            Team = Team,
            Kind = "report",
            Unit = "G788-start",
            Summary = "existing direct-task report evidence",
            Artifact = "https://example.test/direct",
        });

        var pass = RunAfterWindow(scenario);

        Assert.DoesNotContain(pass.Findings, finding =>
            finding.Kind == "delegation-delivered-never-executed");
        Assert.Contains(
            NotifySupervisionStore.Read(scenario.Context.ResolveSupervisionArtifactRootPath(), Domain, Team)
                .LastCycle!.DelegationExecutionEvidence!,
            item => item.StartsWith("notification-events:count=1", StringComparison.Ordinal));
    }

    [Fact]
    public void TokenlessDownstreamIsInformationalAndDoesNotWake_G788()
    {
        var scenario = CreateScenario("tokenless");
        WriteDownstreamDelegation(scenario, tokenCarrier: "none");

        var pass = RunAfterWindow(scenario);

        Assert.DoesNotContain(pass.Findings, finding =>
            finding.Kind == "delegation-delivered-never-executed");
        var informational = Assert.Single(pass.Findings, finding =>
            finding.Kind == "delegation-in-progress-no-direct-report");
        Assert.Null(informational.WakeClass);
        Assert.False(informational.WakeAttempted);
        Assert.False(informational.WakeDelivered);
        Assert.Contains("no escalation wake class", informational.Summary, StringComparison.Ordinal);
        Assert.Contains(informational.ConsultedObservations!, item =>
            item.Contains("tokenless-downstream", StringComparison.Ordinal));
    }

    [Fact]
    public void EvidenceArrivingAfterAFirstFindingPreventsReemissionWithoutRetraction_G788()
    {
        var scenario = CreateScenario("two-cycle");
        var firstPass = RunAfterWindow(scenario);
        Assert.Contains(firstPass.Findings, finding =>
            finding.Kind == "delegation-delivered-never-executed");

        now = firstNow.AddSeconds(302);
        WriteDownstreamDelegation(scenario, tokenCarrier: "input", dispatchedAt: now);
        var secondPass = scenario.Supervisor.RunOnce();

        Assert.DoesNotContain(secondPass.Findings, finding =>
            finding.Kind == "delegation-delivered-never-executed");
        Assert.Contains(secondPass.RecoveryRecords, record =>
            record.Kind == "delegation-delivered-never-executed" && record.ClearedAt is not null);
    }

    [Theory]
    [InlineData("G779-start", null, null, "G779")]
    [InlineData("design-delegate-SKS-G909-implement-20260902", null, null, "SKS-G909")]
    [InlineData("sks-g909-implementation-20260902", null, null, "SKS-G909")]
    [InlineData("tokenless-task-id", null, null, null)]
    [InlineData("parent-start", "coordinate G788 downstream", "ordinary input", "G788")]
    [InlineData("parent-start", "coordinate ordinary work", "G788 input", "G788")]
    [InlineData("G789-parent", "coordinate G788 downstream", "G788 input", "G789")]
    [InlineData("parent-start", "implement G789 before G788 downstream", "G788 input", "G789")]
    public void ExecutionUnitTokenRuleIsOrderedAndCaseInsensitive_G788(
        string taskId,
        string? objective,
        string? input,
        string? expected)
    {
        var pattern = new Regex(@"^(?:[A-Z]+-)?G[0-9]+$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

        var actual = NotifyDelegationExecutionEvidence.ExtractExecutionUnitToken(
            taskId,
            objective,
            input is null ? null : [input],
            pattern);

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void EnglishAndJapaneseDocsCarryTheSameG788Terms_G788()
    {
        var repoRoot = RepoVersionPolicySource.RepoRoot();
        var english = File.ReadAllText(Path.Combine(repoRoot, "docs", "en", "12-agent-message-orchestration.md"));
        var japanese = File.ReadAllText(Path.Combine(repoRoot, "docs", "ja", "12-agent-message-orchestration.md"));
        var canonicalTerms = new[]
        {
            "G788",
            "pending-ledger",
            "report-outbox",
            "notification-events",
            "queue-state",
            "continuation-chain",
            "expected-artifact",
            "delegation-in-progress-no-direct-report",
            "execution-unit token",
            "Source-derived",
        };

        foreach (var term in canonicalTerms)
        {
            Assert.Contains(term, english, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(term, japanese, StringComparison.OrdinalIgnoreCase);
        }
    }

    private Scenario CreateScenario(
        string name,
        string parentTaskId = "G788-start",
        string? parentObjective = null,
        IReadOnlyList<string>? parentInputs = null)
    {
        now = firstNow;
        var scenarioRoot = Path.Combine(root, name);
        Directory.CreateDirectory(scenarioRoot);
        var context = CreateContext(scenarioRoot);
        RecordHerdrOnlyMode(context);
        WriteExecutionUnitBinding(scenarioRoot);
        WriteTopology(scenarioRoot);
        var pending = WriteDeliveredParentDelegation(
            scenarioRoot,
            parentTaskId,
            parentObjective,
            parentInputs);
        var runner = new FixtureRunner();
        return new Scenario
        {
            Root = scenarioRoot,
            Context = context,
            Pending = pending,
            Runner = runner,
            Supervisor = CreateSupervisor(context, scenarioRoot, runner),
        };
    }

    private NotifySupervisorPass RunAfterWindow(Scenario scenario)
    {
        var baseline = scenario.Supervisor.RunOnce();
        Assert.DoesNotContain(baseline.Findings, finding =>
            finding.Kind == "delegation-delivered-never-executed");
        now = firstNow.AddSeconds(301);
        return scenario.Supervisor.RunOnce();
    }

    private NotifyMeasuredSupervisor CreateSupervisor(
        CliContext context,
        string scenarioRoot,
        FixtureRunner runner) => new(
        context: context,
        routingRoot: scenarioRoot,
        domain: Domain,
        team: Team,
        repo: null,
        ownerRole: "design",
        intervalSeconds: 600,
        declaredBoundSeconds: null,
        staleMinutes: 45,
        claimedSilentMinutes: 720,
        backlogIdleMinutes: 45,
        repairSilentMinutes: 180,
        autoRedispatch: false,
        write: true,
        format: "json",
        runner: runner,
        herdrExecutable: "fake-herdr",
        agmsgScriptsDirectory: scenarioRoot,
        delegationExecutionWindowSeconds: 300);

    private NotifyPendingDelegation WriteDeliveredParentDelegation(
        string scenarioRoot,
        string taskId,
        string? objective,
        IReadOnlyList<string>? inputs)
    {
        var pending = new NotifyPendingDelegation
        {
            Domain = Domain,
            Team = Team,
            TaskId = taskId,
            DelegatingRole = "design",
            RecipientRole = "orchestration",
            ReportToRole = "design",
            RecipientIdentity = "role=orchestration;workspace=wG788;pane=wG788:p2",
            ExpectedArtifact = "expected-artifact.txt",
            ExpectedArtifacts = ["expected-artifact.txt"],
            Objective = objective ?? "coordinate the G788 delegation",
            Inputs = inputs ?? ["fixture"],
            ResultNonce = "g788-parent-nonce",
            DispatchedAt = firstNow.AddSeconds(-1),
            TransportMode = SessionLayerMode.HerdrOnly,
            Resident = NotifyRecordedRole.HerdrResident,
            WorkspaceId = "wG788",
            PaneId = "wG788:p2",
            Cwd = Path.Combine(scenarioRoot, "work", "parent"),
            Kind = "orchestration",
            LaunchArguments = ["fixture"],
        };
        var dispatch = NotifyPendingDelegationStore.WriteDispatch(scenarioRoot, pending);
        Assert.True(dispatch.Written, dispatch.Error);
        var delivery = NotifyDelegationDeliveryStore.Write(scenarioRoot, pending, firstNow);
        Assert.True(delivery.Written, delivery.Error);
        return pending;
    }

    private NotifyPendingDelegation WriteDownstreamDelegation(
        Scenario scenario,
        string tokenCarrier,
        DateTimeOffset? dispatchedAt = null)
    {
        var taskId = tokenCarrier == "task-id" ? "G788-implementation" : "child-implementation";
        var objective = tokenCarrier == "objective"
            ? "implement G788 downstream"
            : tokenCarrier == "earlier-different-unit"
                ? "implement G789 before G788 downstream"
                : "implement the child work";
        IReadOnlyList<string> inputs = tokenCarrier == "input" ? ["G788 evidence"] : ["ordinary input"];
        var child = new NotifyPendingDelegation
        {
            Domain = Domain,
            Team = Team,
            TaskId = taskId,
            DelegatingRole = "orchestration",
            RecipientRole = "implementation",
            ReportToRole = "orchestration",
            RecipientIdentity = "role=implementation;workspace=wG788;pane=wG788:p3",
            ExpectedArtifact = "child-artifact.txt",
            ExpectedArtifacts = ["child-artifact.txt"],
            Objective = objective,
            Inputs = inputs,
            ResultNonce = $"{tokenCarrier}-child-nonce",
            DispatchedAt = dispatchedAt ?? firstNow.AddSeconds(10),
            TransportMode = SessionLayerMode.HerdrOnly,
            Resident = NotifyRecordedRole.HerdrResident,
            WorkspaceId = "wG788",
            PaneId = "wG788:p3",
            Cwd = Path.Combine(scenario.Root, "work", tokenCarrier),
            Kind = "implementation",
            LaunchArguments = ["fixture"],
        };
        var dispatch = NotifyPendingDelegationStore.WriteDispatch(scenario.Root, child);
        Assert.True(dispatch.Written, dispatch.Error);
        return child;
    }

    private void WriteChildReportOutbox(Scenario scenario, string tokenCarrier = "task-id")
    {
        var taskId = tokenCarrier == "task-id" ? "G788-child-report" : "child-report";
        var summary = tokenCarrier == "summary"
            ? "child report for G788"
            : tokenCarrier == "earlier-different-unit"
                ? "child report for G789 before G788"
                : "child report";
        var artifact = tokenCarrier == "artifact"
            ? "https://example.test/G788-child"
            : "https://example.test/child";
        var write = NotifyReportOutboxStore.WriteNew(scenario.Root, new NotifyReportOutboxEntry
        {
            Domain = Domain,
            Team = Team,
            TaskId = taskId,
            ResultNonce = "g788-child-report-nonce",
            FromRole = "implementation",
            ToRole = "design",
            Status = "completed",
            Artifact = artifact,
            Summary = summary,
            CreatedAt = firstNow.AddSeconds(10),
            DeliveryState = "delivered",
        });
        Assert.True(write.Written, write.Error);
    }

    private void WriteChildNotificationEvent(Scenario scenario, string tokenCarrier = "task-id")
    {
        var unit = tokenCarrier == "task-id" ? "G788-child-event" : "child-event";
        var summary = tokenCarrier == "summary"
            ? "child report for G788"
            : tokenCarrier == "earlier-different-unit"
                ? "child report for G789 before G788"
                : "child report";
        var artifact = tokenCarrier == "artifact"
            ? "https://example.test/G788-child"
            : "https://example.test/child";
        Assert.True(NotifyEventWriter.TryResolveWritePath(scenario.Root, Domain, Team, out var path, out var error), error);
        NotifyEventWriter.Append(path, new NotifyDesignEvent
        {
            Timestamp = firstNow.AddSeconds(10),
            Team = Team,
            Kind = "report",
            Unit = unit,
            Summary = summary,
            Artifact = artifact,
        });
    }

    private void WriteQueueTransition(Scenario scenario)
    {
        var item = new QueueItem
        {
            ExecutionUnit = "G788",
            Title = "G788 fixture",
            State = QueueItemState.Active,
            Dependencies = [],
            BlockedBy = [],
            ClarificationReturnPath = string.Empty,
            PacketPaths = new PacketPaths
            {
                Yaml = ".intent-cli/issues/G788/packet.yaml",
                Implementation = ".intent-cli/issues/G788/implementation.md",
                ReviewContext = ".intent-cli/issues/G788/review-context.md",
            },
            WorkerRole = "implementation",
            ReviewRole = "review",
            Priority = "high",
        };
        var queueState = new QueueState
        {
            SchemaVersion = "1",
            UpdatedAt = firstNow.AddSeconds(10),
            Items = [item],
        };
        var path = scenario.Context.GetQueueStatePath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, QueueStateSerializer.Serialize(queueState));
    }

    private static CliContext CreateContext(string scenarioRoot) => new()
    {
        RepoRoot = scenarioRoot,
        Config = new CliConfig
        {
            Project = new ProjectConfig { Domain = Domain, ArtifactRoot = ".intent-cli" },
        },
    };

    private static void RecordHerdrOnlyMode(CliContext context)
    {
        using var writer = new StringWriter();
        Assert.Equal(
            0,
            SessionLayerCommand.ExecuteSet(
                context,
                ["--domain", Domain, "--team", Team, "--mode", SessionLayerMode.HerdrOnly, "--write", "--format", "json"],
                writer));
    }

    private static void WriteExecutionUnitBinding(string scenarioRoot)
    {
        var path = Path.Combine(scenarioRoot, "intents", Domain, "automation", "bindings.md");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "---\nexecution_unit_regex: '^(?:[A-Z]+-)?G[0-9]+$'\n---\n");
    }

    private static void WriteTopology(string scenarioRoot)
    {
        var path = NotifyRoleTopologyStore.ResolvePath(scenarioRoot, Domain, Team);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(
            path,
            JsonSerializer.Serialize(new
            {
                domain = Domain,
                team = Team,
                workspace_id = "wG788",
                roles = new Dictionary<string, object>
                {
                    ["design"] = new { resident = "herdr", workspace_id = "wG788", pane_id = "wG788:p1" },
                    ["orchestration"] = new { resident = "herdr", workspace_id = "wG788", pane_id = "wG788:p2" },
                    ["implementation"] = new { resident = "herdr", workspace_id = "wG788", pane_id = "wG788:p3" },
                },
            }));
    }

    private sealed record Scenario
    {
        public required string Root { get; init; }
        public required CliContext Context { get; init; }
        public required NotifyPendingDelegation Pending { get; init; }
        public required FixtureRunner Runner { get; init; }
        public required NotifyMeasuredSupervisor Supervisor { get; init; }
    }

    private sealed class FixtureRunner : INotifyProcessRunner
    {
        public List<(string FileName, IReadOnlyList<string> Arguments)> Calls { get; } = [];

        public NotifyProcessResult Run(string fileName, IReadOnlyList<string> arguments)
        {
            Calls.Add((fileName, arguments.ToArray()));
            if (arguments.SequenceEqual(["agent", "list"]))
            {
                return new NotifyProcessResult(0, AgentsJson(), string.Empty);
            }

            if (arguments.Count >= 2
                && arguments[0] == "pane"
                && arguments[1] == "process-info")
            {
                return new NotifyProcessResult(0, "{\"result\":{\"process_info\":{\"foreground_processes\":[]}}}", string.Empty);
            }

            if (arguments.Count >= 2
                && arguments[0] == "agent"
                && arguments[1] is "prompt" or "wait")
            {
                return new NotifyProcessResult(0, string.Empty, string.Empty);
            }

            return new NotifyProcessResult(0, string.Empty, string.Empty);
        }

        private static string AgentsJson()
        {
            static object Agent(string role, string pane, string status, long sequence) => new
            {
                name = role,
                workspace_id = "wG788",
                pane_id = pane,
                agent = "fixture",
                agent_session = new { id = role },
                agent_status = status,
                agent_running = true,
                interactive_ready = true,
                state_change_seq = sequence,
                last_state_change_at = "2026-09-03T12:00:00.0000000+00:00",
            };

            return JsonSerializer.Serialize(new
            {
                result = new
                {
                    agents = new[]
                    {
                        Agent("design", "wG788:p1", "working", 1),
                        Agent("orchestration", "wG788:p2", "idle", 7),
                        Agent("implementation", "wG788:p3", "working", 8),
                    },
                },
            });
        }
    }
}
