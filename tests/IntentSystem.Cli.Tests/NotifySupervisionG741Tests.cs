using System.Text.Json;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

/// <summary>
/// G741: a successful delegation delivery is not execution evidence. The
/// finding is emitted only after a later idle observation and a complete
/// absence check across the durable report, artifact, and target-transition
/// sources.
/// </summary>
[Collection("WorkerNextActionSharedState")]
public sealed class NotifySupervisionG741Tests : IDisposable
{
    private const string Domain = "intent-cli";
    private const string Team = "intent-cli-dev";
    private readonly string root = Path.Combine(Path.GetTempPath(), $"intent-g741-{Guid.NewGuid():N}");
    private readonly DateTimeOffset firstNow = new(2026, 8, 27, 12, 0, 0, TimeSpan.Zero);
    private DateTimeOffset now;

    public NotifySupervisionG741Tests()
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
        NotifySupervisor.Delay = Thread.Sleep;
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void DeliveredIdleDelegationFiresAfterWindowAndWakesOwner_G741()
    {
        var context = CreateContext();
        RecordHerdrOnlyMode(context);
        WriteTopology();
        var pending = WriteDeliveredPending("G741-fired");
        var runner = new FixtureRunner { AgentStatus = "idle", StateChangeSequence = 7 };
        var supervisor = CreateSupervisor(context, runner, delegationWindowSeconds: 300);

        var baseline = supervisor.RunOnce();
        Assert.DoesNotContain(baseline.Findings, finding =>
            finding.Kind == "delegation-delivered-never-executed");

        now = firstNow.AddSeconds(301);
        var pass = supervisor.RunOnce();

        var finding = Assert.Single(
            pass.Findings,
            item => item.Kind == "delegation-delivered-never-executed");
        Assert.Equal("orchestration", finding.OwnerRole);
        Assert.Equal("orchestration", finding.WakeTargetRole);
        Assert.True(finding.WakeAttempted);
        Assert.True(finding.WakeDelivered);
        Assert.Contains("G741-fired", finding.Summary, StringComparison.Ordinal);
        Assert.Contains("seat 'role=implementation;workspace=wG741;pane=wG741:p2'", finding.Summary, StringComparison.Ordinal);
        Assert.Contains("300s execution-start window", finding.Summary, StringComparison.Ordinal);
        Assert.Contains("Canonical report absent", finding.Summary, StringComparison.Ordinal);
        Assert.Contains("expected artifact absent", finding.Summary, StringComparison.Ordinal);
        Assert.Contains("durable target-entity transition absent", finding.Summary, StringComparison.Ordinal);
        Assert.Contains("Checked sources:", finding.Summary, StringComparison.Ordinal);
        Assert.Contains("task_id:G741-fired", finding.Evidence!, StringComparer.Ordinal);
        Assert.Contains($"delivered_at:{firstNow:O}", finding.Evidence!, StringComparer.Ordinal);
        Assert.Contains("window_seconds:300", finding.Evidence!, StringComparer.Ordinal);
        Assert.Contains(finding.Evidence!, item => item.StartsWith("canonical_report:absent", StringComparison.Ordinal));
        Assert.Contains(finding.Evidence!, item => item.StartsWith("expected_artifact:absent", StringComparison.Ordinal));
        Assert.Contains(finding.Evidence!, item => item.StartsWith("durable_target_entity_transition:absent", StringComparison.Ordinal));
        Assert.Contains(runner.Calls, call =>
            call.Arguments.Take(3).SequenceEqual(["agent", "prompt", "wG741:p1"]));

        var state = NotifySupervisionStore.Read(
            context.ResolveSupervisionArtifactRootPath(),
            Domain,
            Team);
        var stored = Assert.Single(
            state.ActiveStalls.Values,
            item => item.Kind == "delegation-delivered-never-executed");
        Assert.Equal(finding.Key, stored.Key);
        Assert.Contains("task_id:G741-fired", stored.Evidence!, StringComparer.Ordinal);
        Assert.Equal(pending.TaskId, NotifyPendingDelegationStore.Find(
            root,
            Domain,
            Team,
            pending.TaskId).Record!.TaskId);
    }

    [Fact]
    public void WorkingSeatIsAProvenStartedNonFinding_G741()
    {
        var context = CreateContext();
        RecordHerdrOnlyMode(context);
        WriteTopology();
        WriteDeliveredPending("G741-working");
        var runner = new FixtureRunner { AgentStatus = "working", StateChangeSequence = 8 };
        var supervisor = CreateSupervisor(context, runner, delegationWindowSeconds: 300);

        supervisor.RunOnce();
        now = firstNow.AddSeconds(301);
        var pass = supervisor.RunOnce();

        Assert.DoesNotContain(pass.Findings, finding =>
            finding.Kind == "delegation-delivered-never-executed");
        Assert.DoesNotContain(runner.Calls, call =>
            call.Arguments.Take(3).SequenceEqual(["agent", "prompt", "wG741:p1"]));
    }

    [Fact]
    public void VisibleExpectedArtifactIsAStartedNonFinding_G741()
    {
        var context = CreateContext();
        RecordHerdrOnlyMode(context);
        WriteTopology();
        var pending = WriteDeliveredPending("G741-artifact");
        Directory.CreateDirectory(pending.Cwd!);
        var runner = new FixtureRunner { AgentStatus = "idle", StateChangeSequence = 9 };
        var supervisor = CreateSupervisor(context, runner, delegationWindowSeconds: 300);

        supervisor.RunOnce();
        File.WriteAllText(Path.Combine(pending.Cwd!, "expected-artifact.txt"), "started");
        now = firstNow.AddSeconds(301);
        var pass = supervisor.RunOnce();

        Assert.DoesNotContain(pass.Findings, finding =>
            finding.Kind == "delegation-delivered-never-executed");
    }

    [Fact]
    public void DurableTargetTransitionIsAStartedNonFinding_G741()
    {
        var context = CreateContext();
        RecordHerdrOnlyMode(context);
        WriteTopology();
        var pending = WriteDeliveredPending("G741-transition");
        var runner = new FixtureRunner { AgentStatus = "idle", StateChangeSequence = 10 };
        var supervisor = CreateSupervisor(context, runner, delegationWindowSeconds: 300);

        supervisor.RunOnce();
        var signalId = ContinuationChainStore.BuildCompletionSignalId(
            pending.TaskId,
            pending.ResultNonce);
        var chainId = ContinuationChainStore.BuildChainId(signalId);
        var transition = ContinuationChainStore.RecordLink(
            root,
            Domain,
            Team,
            signalId,
            pending.TaskId,
            chainId,
            ContinuationChainStore.CanonicalStateClassified,
            "g741-test",
            ["target-entity-transition:observed"],
            timestamp: firstNow.AddSeconds(1),
            write: true,
            resultNonce: pending.ResultNonce);
        Assert.True(transition.Applied || transition.AlreadyConverged, transition.Error);

        now = firstNow.AddSeconds(301);
        var pass = supervisor.RunOnce();

        Assert.DoesNotContain(pass.Findings, finding =>
            finding.Kind == "delegation-delivered-never-executed");
    }

    [Fact]
    public void DeliveryEvidenceIsRequiredAndLegacyPendingLinesRemainReadable_G741()
    {
        var context = CreateContext();
        RecordHerdrOnlyMode(context);
        WriteTopology();
        var pending = WriteDispatchedPending("G741-undelivered");
        var rawDispatch = File.ReadAllText(NotifyPendingDelegationStore.ResolvePath(root, Domain, Team));
        Assert.DoesNotContain("delivery_succeeded", rawDispatch, StringComparison.Ordinal);

        var runner = new FixtureRunner { AgentStatus = "idle", StateChangeSequence = 11 };
        var supervisor = CreateSupervisor(context, runner, delegationWindowSeconds: 300);
        supervisor.RunOnce();
        now = firstNow.AddSeconds(301);
        var pass = supervisor.RunOnce();

        Assert.DoesNotContain(pass.Findings, finding =>
            finding.Kind == "delegation-delivered-never-executed");
        var beforeDelivery = NotifyDelegationDeliveryStore.Find(
            root,
            Domain,
            Team,
            pending.TaskId,
            pending.ResultNonce);
        Assert.True(beforeDelivery.Resolved, beforeDelivery.Error);
        Assert.Null(beforeDelivery.Evidence);

        var delivery = NotifyDelegationDeliveryStore.Write(root, pending, firstNow);
        Assert.True(delivery.Written, delivery.Error);
        var afterDelivery = NotifyDelegationDeliveryStore.Find(
            root,
            Domain,
            Team,
            pending.TaskId,
            pending.ResultNonce);
        Assert.True(afterDelivery.Resolved, afterDelivery.Error);
        Assert.True(afterDelivery.Evidence?.DeliverySucceeded == true);
        Assert.Equal(firstNow, afterDelivery.Evidence?.DeliveredAt);
        Assert.Single(File.ReadAllLines(NotifyPendingDelegationStore.ResolvePath(root, Domain, Team)));
        var rawWithDelivery = File.ReadAllText(delivery.Path);
        Assert.Contains("\"event\":\"delivery\"", rawWithDelivery, StringComparison.Ordinal);
        Assert.Contains("\"delivery_succeeded\":true", rawWithDelivery, StringComparison.Ordinal);
    }

    [Fact]
    public void LegacySupervisionStateAndEmissionPolicyFixturesReadWithoutMutation_G741()
    {
        var fixtureRoot = Path.Combine(
            RepoVersionPolicySource.RepoRoot(),
            "tests",
            "IntentSystem.Cli.Tests",
            "Fixtures",
            "g741-supervision-state");
        var artifactRoot = Path.Combine(root, "fixture-artifact");
        CopyDirectory(fixtureRoot, artifactRoot);

        var fixtureFiles = Directory
            .EnumerateFiles(artifactRoot, "*", SearchOption.AllDirectories)
            .ToDictionary(
                path => Path.GetRelativePath(artifactRoot, path),
                path => Convert.ToHexString(File.ReadAllBytes(path)),
                StringComparer.Ordinal);
        var state = NotifySupervisionStore.Read(artifactRoot, Domain, Team);

        Assert.True(state.Resolved, state.Error);
        Assert.Equal(300, state.Bound!.BoundSeconds);
        Assert.Equal(1_800, state.EmissionPolicy!.RepeatBackoffSeconds);
        Assert.Equal(3, state.EmissionPolicy.DebounceConsecutiveObservations);
        Assert.Equal("g741-fixture-cycle", state.LastCycle!.CycleId);
        Assert.Contains("legacy:fixture", state.ActiveStalls.Keys, StringComparer.Ordinal);

        foreach (var fixtureFile in fixtureFiles)
        {
            var path = Path.Combine(artifactRoot, fixtureFile.Key);
            Assert.Equal(fixtureFile.Value, Convert.ToHexString(File.ReadAllBytes(path)));
        }

        Assert.DoesNotContain(
            "delegation-delivered-never-executed",
            File.ReadAllText(Path.Combine(
                artifactRoot,
                Domain,
                Team,
                NotifySupervisionStore.BoundFileName)),
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "delegation-delivered-never-executed",
            File.ReadAllText(Path.Combine(
                artifactRoot,
                Domain,
                Team,
                NotifySupervisionStore.EmissionPolicyFileName)),
            StringComparison.Ordinal);
    }

    [Fact]
    public void DelegationExecutionWindowIsBoundedAndDocumentedOnSuperviseSurface_G741()
    {
        using var help = new StringWriter();
        Assert.Equal(0, NotifyCommand.ExecuteSupervise(CreateContext(), ["--help"], help));
        Assert.Contains("--delegation-execution-window-seconds", help.ToString(), StringComparison.Ordinal);
        Assert.Contains("300", help.ToString(), StringComparison.Ordinal);

        using var invalid = new StringWriter();
        Assert.Equal(
            1,
            NotifyCommand.ExecuteSupervise(
                CreateContext(),
                [
                    "--domain", Domain,
                    "--team", Team,
                    "--delegation-execution-window-seconds", "86401",
                    "--format", "json",
                ],
                invalid));
        Assert.Contains("between 1 and 86400", invalid.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void EnglishAndJapaneseOrchestrationDocsDescribeG741Contract_G741()
    {
        var repoRoot = RepoVersionPolicySource.RepoRoot();
        var english = File.ReadAllText(Path.Combine(repoRoot, "docs", "en", "12-agent-message-orchestration.md"));
        var japanese = File.ReadAllText(Path.Combine(repoRoot, "docs", "ja", "12-agent-message-orchestration.md"));

        foreach (var document in new[] { english, japanese })
        {
            Assert.Contains("G741", document, StringComparison.Ordinal);
            Assert.Contains("delegation-delivered-never-executed", document, StringComparison.Ordinal);
            Assert.Contains("--delegation-execution-window-seconds", document, StringComparison.Ordinal);
            Assert.Contains("300", document, StringComparison.Ordinal);
            Assert.Contains("delivered_at", document, StringComparison.Ordinal);
            Assert.Contains("canonical", document, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("expected artifact", document, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("durable", document, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("observation-only", document, StringComparison.OrdinalIgnoreCase);
        }

        Assert.Contains("opaque in-seat thought", english, StringComparison.Ordinal);
        Assert.Contains("seat 内部の opaque な thought", japanese, StringComparison.Ordinal);
    }

    private NotifyMeasuredSupervisor CreateSupervisor(
        CliContext context,
        FixtureRunner runner,
        int delegationWindowSeconds) => new(
        context: context,
        routingRoot: root,
        domain: Domain,
        team: Team,
        repo: null,
        ownerRole: "orchestration",
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
        agmsgScriptsDirectory: root,
        delegationExecutionWindowSeconds: delegationWindowSeconds);

    private NotifyPendingDelegation WriteDeliveredPending(string taskId)
    {
        var pending = WriteDispatchedPending(taskId);
        var delivery = NotifyDelegationDeliveryStore.Write(root, pending, firstNow);
        Assert.True(delivery.Written, delivery.Error);
        return pending;
    }

    private NotifyPendingDelegation WriteDispatchedPending(string taskId)
    {
        var pending = new NotifyPendingDelegation
        {
            Domain = Domain,
            Team = Team,
            TaskId = taskId,
            DelegatingRole = "orchestration",
            RecipientRole = "implementation",
            ReportToRole = "orchestration",
            RecipientIdentity = "role=implementation;workspace=wG741;pane=wG741:p2",
            ExpectedArtifact = "expected-artifact.txt",
            ExpectedArtifacts = ["expected-artifact.txt"],
            Objective = "execute the delegated task",
            Inputs = ["fixture"],
            ResultNonce = $"{taskId}-nonce",
            DispatchedAt = firstNow.AddSeconds(-1),
            TransportMode = SessionLayerMode.HerdrOnly,
            Resident = NotifyRecordedRole.HerdrResident,
            WorkspaceId = "wG741",
            PaneId = "wG741:p2",
            Cwd = Path.Combine(root, "work", taskId),
            Kind = "implementation",
            LaunchArguments = ["fixture"],
        };
        var write = NotifyPendingDelegationStore.WriteDispatch(root, pending);
        Assert.True(write.Written, write.Error);
        return pending;
    }

    private CliContext CreateContext() => new()
    {
        RepoRoot = root,
        Config = new CliConfig
        {
            Project = new ProjectConfig { Domain = Domain, ArtifactRoot = ".intent-cli" },
        },
    };

    private void RecordHerdrOnlyMode(CliContext context)
    {
        using var writer = new StringWriter();
        Assert.Equal(
            0,
            SessionLayerCommand.ExecuteSet(
                context,
                ["--domain", Domain, "--team", Team, "--mode", SessionLayerMode.HerdrOnly, "--write", "--format", "json"],
                writer));
    }

    private void WriteTopology()
    {
        var path = NotifyRoleTopologyStore.ResolvePath(root, Domain, Team);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(
            path,
            JsonSerializer.Serialize(new
            {
                domain = Domain,
                team = Team,
                workspace_id = "wG741",
                roles = new Dictionary<string, object>
                {
                    ["orchestration"] = new { resident = "herdr", workspace_id = "wG741", pane_id = "wG741:p1" },
                    ["implementation"] = new { resident = "herdr", workspace_id = "wG741", pane_id = "wG741:p2" },
                },
            }));
    }

    private static string AgentsJson(string status, long sequence)
    {
        static object Agent(string role, string pane, string status, long sequence) => new
        {
            name = role,
            workspace_id = "wG741",
            pane_id = pane,
            agent = "fixture",
            agent_session = new { id = role },
            agent_status = status,
            agent_running = true,
            interactive_ready = true,
            state_change_seq = sequence,
            last_state_change_at = "2026-08-27T12:00:00.0000000+00:00",
        };

        return JsonSerializer.Serialize(new
        {
            result = new
            {
                agents = new[]
                {
                    Agent("orchestration", "wG741:p1", "working", 1),
                    Agent("implementation", "wG741:p2", status, sequence),
                },
            },
        });
    }

    private static void CopyDirectory(string source, string destination)
    {
        foreach (var path in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(source, path);
            var target = Path.Combine(destination, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(path, target);
        }
    }

    private sealed class FixtureRunner : INotifyProcessRunner
    {
        public string AgentStatus { get; set; } = "idle";
        public long StateChangeSequence { get; set; } = 7;
        public List<(string FileName, IReadOnlyList<string> Arguments)> Calls { get; } = [];

        public NotifyProcessResult Run(string fileName, IReadOnlyList<string> arguments)
        {
            Calls.Add((fileName, arguments.ToArray()));
            if (arguments.SequenceEqual(["agent", "list"]))
            {
                return new NotifyProcessResult(
                    0,
                    AgentsJson(AgentStatus, StateChangeSequence),
                    string.Empty);
            }

            if (arguments.Count >= 2
                && arguments[0] == "pane"
                && arguments[1] == "process-info")
            {
                return new NotifyProcessResult(
                    0,
                    "{\"result\":{\"process_info\":{\"foreground_processes\":[]}}}",
                    string.Empty);
            }

            if (arguments.Count >= 2
                && arguments[0] == "agent"
                && arguments[1] is "prompt" or "wait")
            {
                return new NotifyProcessResult(0, string.Empty, string.Empty);
            }

            return new NotifyProcessResult(0, string.Empty, string.Empty);
        }
    }
}
