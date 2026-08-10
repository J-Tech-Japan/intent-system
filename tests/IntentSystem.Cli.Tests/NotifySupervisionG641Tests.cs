using System.Text.Json;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

/// <summary>
/// Constructed G641 evidence: an undelivered escalation is surfaced and
/// measured, a restart gap breaks the declared bound, and a missing recorded
/// seat is reported without inventing a transport.
/// </summary>
[Collection("WorkerNextActionSharedState")]
public sealed class NotifySupervisionG641Tests : IDisposable
{
    private const string Domain = "intent-cli";
    private const string Team = "intent-cli-dev";
    private readonly string root = Directory.CreateTempSubdirectory("notify-g641-").FullName;
    private readonly DateTimeOffset firstNow = new(2026, 8, 7, 12, 0, 0, TimeSpan.Zero);
    private DateTimeOffset now;

    public NotifySupervisionG641Tests()
    {
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
        NotifySupervisionStore.WriteOverride = null;
        NotifySupervisor.Delay = Thread.Sleep;
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void EscalationIsRecordedWithUnknownStart_ThenRestartGapBreaksBound_G641()
    {
        var scripts = Path.Combine(root, "agmsg");
        Directory.CreateDirectory(scripts);
        File.WriteAllText(Path.Combine(scripts, "team.sh"), "fixture");
        File.WriteAllText(Path.Combine(scripts, "send.sh"), "fixture");
        var runner = new FakeRunner();
        var context = CreateContext();
        NotifyEventWriter.Append(
            ResolveEventPath(),
            new NotifyDesignEvent
            {
                Timestamp = firstNow.AddMinutes(-10),
                Team = Team,
                Kind = "escalation",
                Unit = "G641-escalation",
                Summary = "approval is durable but nobody was woken",
                Artifact = "approval",
            });

        var supervisor = CreateSupervisor(context, scripts, runner, write: true, boundSeconds: 300);
        var first = supervisor.RunOnce();
        var escalation = Assert.Single(first.Findings, finding => finding.Kind == "undelivered-escalation");
        Assert.Null(escalation.DetectableAt);
        Assert.True(first.Bound!.Recorded);
        Assert.Null(first.Bound.BoundMet);
        Assert.Contains(first.RecoveryRecords, record =>
            record.Kind == "undelivered-escalation" && record.DetectableAtUnknown && record.WakeDelivered);
        Assert.Contains(runner.Calls, call => call.Arguments.Count >= 3 && Path.GetFileName(call.Arguments[0]) == "send.sh");

        File.Delete(ResolveEventPath());
        now = firstNow.AddSeconds(301);
        var second = supervisor.RunOnce();

        Assert.False(second.Bound!.BoundMet);
        Assert.True(second.Liveness!.AbsentSinceLastCycle);
        Assert.Equal(301, second.Liveness.GapSeconds);
        Assert.Contains(second.Findings, finding => finding.Kind == "supervisor-not-running");
        Assert.Contains(second.RecoveryRecords, record =>
            record.Kind == "undelivered-escalation" && record.ClearedAt is not null && record.DurationSeconds is null);
    }

    [Fact]
    public void RestartGapReportsSelfAbsenceWithoutClaimingUndeclaredBound_G641()
    {
        var context = CreateContext();
        var supervisor = CreateSupervisor(context, "unused-agmsg", new FakeRunner(), write: true, boundSeconds: null);

        var first = supervisor.RunOnce();
        Assert.True(first.Silent);
        Assert.False(first.Bound!.Recorded);
        Assert.Null(first.Bound.BoundSeconds);

        now = firstNow.AddHours(3);
        var second = supervisor.RunOnce();

        Assert.False(second.Silent);
        Assert.False(second.Bound!.Recorded);
        Assert.Null(second.Bound.BoundSeconds);
        Assert.Null(second.Bound.BoundMet);
        Assert.True(second.Liveness!.AbsentSinceLastCycle);
        Assert.Equal(300, second.Liveness.CadenceIntervalSeconds);
        Assert.Equal(600, second.Liveness.AbsenceThresholdSeconds);
        Assert.Equal("configured-interval", second.Liveness.AbsenceThresholdKind);
        Assert.DoesNotContain("within the declared", second.Liveness.Summary, StringComparison.Ordinal);
        Assert.DoesNotContain("exceeding the declared", second.Liveness.Summary, StringComparison.Ordinal);
        Assert.Contains(second.Findings, finding => finding.Kind == "supervisor-not-running");
    }

    [Fact]
    public void HealthyNoBoundCadenceAcrossThreeCyclesRemainsSilent_G641()
    {
        var context = CreateContext();
        RecordMode(context, SessionLayerMode.HerdrOnly);
        WriteTopology();
        var runner = new FakeRunner
        {
            AgentsJson = """
                {"result":{"agents":[
                  {"name":"orchestration","workspace_id":"wG641","pane_id":"wG641:p1","agent":"fixture","agent_session":{"id":"orchestration"},"agent_status":"working","interactive_ready":true},
                  {"name":"implementation","workspace_id":"wG641","pane_id":"wG641:p2","agent":"fixture","agent_session":{"id":"implementation"},"agent_status":"working","interactive_ready":true}
                ]}}
                """,
        };
        var supervisor = CreateSupervisor(context, "unused-agmsg", runner, write: true, boundSeconds: null);
        var clockReads = 0;
        NotifyCommand.UtcNowFactory = () =>
        {
            var cycleStart = now;
            return (clockReads++ % 2) == 0
                ? cycleStart
                : cycleStart.AddSeconds(5);
        };

        for (var cycle = 0; cycle < 3; cycle++)
        {
            if (cycle > 0)
            {
                // Five seconds of realistic cycle work leaves a 300-second
                // completion-to-start gap at a 300-second loop cadence.
                now = firstNow.AddSeconds(cycle * 305);
            }

            var pass = supervisor.RunOnce();

            Assert.True(pass.Silent);
            Assert.Equal(0, pass.ExitCode);
            Assert.Empty(pass.Findings);
            Assert.False(pass.Liveness!.AbsentSinceLastCycle);
            Assert.Equal(300, pass.Liveness.CadenceIntervalSeconds);
            Assert.Equal(600, pass.Liveness.AbsenceThresholdSeconds);
            Assert.Equal("configured-interval", pass.Liveness.AbsenceThresholdKind);
            if (cycle == 0)
            {
                Assert.Null(pass.Liveness.GapSeconds);
            }
            else
            {
                Assert.Equal(300, pass.Liveness.GapSeconds);
                Assert.Equal(firstNow.AddSeconds((cycle - 1) * 305 + 5), pass.Liveness.LastCycleAt);
            }
        }
    }

    [Fact]
    public void DeliveredEscalationClearsAndReturnsToSilence_G641()
    {
        var scripts = Path.Combine(root, "agmsg");
        Directory.CreateDirectory(scripts);
        File.WriteAllText(Path.Combine(scripts, "team.sh"), "fixture");
        File.WriteAllText(Path.Combine(scripts, "send.sh"), "fixture");
        var runner = new FakeRunner();
        var context = CreateContext();
        NotifyEventWriter.Append(
            ResolveEventPath(),
            new NotifyDesignEvent
            {
                Timestamp = firstNow.AddMinutes(-10),
                Team = Team,
                Kind = "escalation",
                Unit = "G641-acknowledged-escalation",
                Summary = "approval is durable but nobody was woken",
                Artifact = "approval",
            });

        var supervisor = CreateSupervisor(context, scripts, runner, write: true, boundSeconds: 300);
        var first = supervisor.RunOnce();
        Assert.Contains(first.Findings, finding =>
            finding.Kind == "undelivered-escalation" && finding.WakeDelivered);

        now = firstNow.AddSeconds(1);
        var second = supervisor.RunOnce();

        Assert.True(second.Silent);
        Assert.DoesNotContain(second.Findings, finding => finding.Kind == "undelivered-escalation");
        Assert.Contains(second.RecoveryRecords, record =>
            record.Kind == "undelivered-escalation" && record.WakeDelivered && record.ClearedAt is not null);

        now = firstNow.AddSeconds(2);
        var third = supervisor.RunOnce();
        Assert.True(third.Silent);
        Assert.DoesNotContain(third.Findings, finding => finding.Kind == "undelivered-escalation");
    }

    [Fact]
    public void HealthyRecordedSeatsRemainSilentAndAbsentSeatsAreConstructedFromTopology_G641()
    {
        var context = CreateContext();
        RecordMode(context, SessionLayerMode.HerdrOnly);
        WriteTopology();
        var runner = new FakeRunner { AgentsJson = "{\"result\":{\"agents\":[]}}" };
        var supervisor = CreateSupervisor(context, "unused-agmsg", runner, write: false, boundSeconds: null);

        var pass = supervisor.RunOnce();

        Assert.Contains(pass.Findings, finding => finding.Kind == "seat-absent");
        Assert.All(pass.Findings.Where(finding => finding.Kind == "seat-absent"), finding =>
            Assert.Equal("recorded-topology", finding.Source));
        Assert.Contains(runner.Calls, call => call.Arguments.SequenceEqual(["agent", "list"]));
    }

    [Fact]
    public void ColdStartWorkingActivityEstablishesBaselineBeforeLiveIdleIsSurfacedOnce_G652()
    {
        var context = CreateContext();
        RecordMode(context, SessionLayerMode.HerdrOnly);
        WriteTopology();
        Assert.True(NotifyPendingDelegationStore.WriteDispatch(root, new NotifyPendingDelegation
        {
            Domain = Domain,
            Team = Team,
            TaskId = "G652-live-activity",
            DelegatingRole = "orchestration",
            RecipientRole = "implementation",
            RecipientIdentity = "implementation",
            ExpectedArtifact = "draft-pr",
            DispatchedAt = firstNow.AddSeconds(-121),
            TransportMode = SessionLayerMode.HerdrOnly,
            Resident = NotifyRecordedRole.HerdrResident,
            WorkspaceId = "wG641",
            PaneId = "wG641:p2",
        }).Written);
        var runner = new FakeRunner
        {
            AgentsJson = HerdrAgentsJson(stateChangeSequence: 7),
        };
        var supervisor = CreateSupervisor(context, "unused-agmsg", runner, write: true, boundSeconds: 120, intervalSeconds: 600);

        var first = supervisor.RunOnce();

        Assert.DoesNotContain(first.Findings, finding => finding.Kind == "live-idle-no-report");
        Assert.Contains("not corrected", Assert.Single(first.Warnings), StringComparison.Ordinal);
        Assert.Equal(120, first.Bound!.BoundSeconds);
        var recordedFirst = NotifySupervisionStore.Read(context.ResolveSupervisionArtifactRootPath(), Domain, Team);
        Assert.True(recordedFirst.LastCycle!.BoundBelowInterval);
        Assert.Equal(7, recordedFirst.LastCycle.LastObservedStateChangeSequences["activity:wG641:wG641:p2"]);

        now = firstNow.AddSeconds(1);
        var unchanged = supervisor.RunOnce();

        var idle = Assert.Single(unchanged.Findings, finding => finding.Kind == "live-idle-no-report");
        Assert.Equal("herdr.activity", idle.Source);
        Assert.False(idle.WakeAttempted);
        Assert.Contains("not corrected", Assert.Single(unchanged.Warnings), StringComparison.Ordinal);

        now = firstNow.AddSeconds(2);
        var unchangedAgain = supervisor.RunOnce();

        Assert.DoesNotContain(unchangedAgain.Findings, finding => finding.Kind == "live-idle-no-report");

        now = firstNow.AddSeconds(3);
        runner.AgentsJson = HerdrAgentsJson(stateChangeSequence: 8);
        var advancing = supervisor.RunOnce();

        Assert.DoesNotContain(advancing.Findings, finding => finding.Kind == "live-idle-no-report");
        var recordedAdvancing = NotifySupervisionStore.Read(context.ResolveSupervisionArtifactRootPath(), Domain, Team);
        Assert.Equal(8, recordedAdvancing.LastCycle!.LastObservedStateChangeSequences["activity:wG641:wG641:p2"]);
        Assert.DoesNotContain(runner.Calls, call => call.Arguments.Contains("send-text"));
    }

    [Fact]
    public void ContinuousSupervisionEmitsBoundBelowIntervalWarningAtStart_G652()
    {
        var context = CreateContext();
        var supervisor = CreateSupervisor(
            context,
            "unused-agmsg",
            new FakeRunner(),
            write: true,
            boundSeconds: 120,
            intervalSeconds: 600);
        using var cancellation = new CancellationTokenSource();
        NotifySupervisor.Delay = _ => cancellation.Cancel();
        using var writer = new StringWriter();

        var exitCode = supervisor.RunLoop(writer, cancellation.Token, once: false);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        Assert.False(document.RootElement.GetProperty("silent").GetBoolean());
        Assert.Contains("not corrected", document.RootElement.GetProperty("warnings")[0].GetString(), StringComparison.Ordinal);
        Assert.True(document.RootElement.GetProperty("bound").GetProperty("recorded").GetBoolean());
    }

    [Fact]
    public void UndeliveredReportOutboxIsSurfacedOnceWithoutAutoSendOrRedispatch_G653()
    {
        var context = CreateContext();
        RecordMode(context, SessionLayerMode.HerdrOnly);
        WriteTopology();
        Assert.True(NotifyPendingDelegationStore.WriteDispatch(root, new NotifyPendingDelegation
        {
            Domain = Domain,
            Team = Team,
            TaskId = "G653-undelivered",
            DelegatingRole = "orchestration",
            RecipientRole = "implementation",
            ReportToRole = "orchestration",
            RecipientIdentity = "implementation",
            ExpectedArtifact = "draft-pr",
            ResultNonce = "g653-undelivered-generation",
            DispatchedAt = firstNow,
            TransportMode = SessionLayerMode.HerdrOnly,
            Resident = NotifyRecordedRole.HerdrResident,
            WorkspaceId = "wG641",
            PaneId = "wG641:p2",
        }).Written);
        Assert.True(NotifyReportOutboxStore.WriteNew(root, new NotifyReportOutboxEntry
        {
            Domain = Domain,
            Team = Team,
            TaskId = "G653-undelivered",
            ResultNonce = "g653-undelivered-generation",
            FromRole = "implementation",
            ToRole = "orchestration",
            Status = "completed",
            Artifact = "draft-pr",
            Summary = "completed but transport failed",
            CreatedAt = firstNow,
            DeliveryState = "undelivered",
            DeliveryError = "fixture",
        }).Written);
        var runner = new FakeRunner { AgentsJson = HerdrAgentsJson(stateChangeSequence: 1) };
        var supervisor = CreateSupervisor(context, "unused-agmsg", runner, write: true, boundSeconds: null);

        var first = supervisor.RunOnce();

        var finding = Assert.Single(first.Findings, item => item.Kind == "undelivered-report-outbox");
        Assert.Contains(
            $"intent-cli notify collect --domain {Domain} --team {Team} --task-id G653-undelivered --write --routing-root {root}",
            finding.Summary,
            StringComparison.Ordinal);
        Assert.False(finding.WakeAttempted);
        Assert.DoesNotContain(runner.Calls, call => call.Arguments.Contains("send-text") || call.Arguments.Contains("prompt"));

        now = firstNow.AddSeconds(1);
        var second = supervisor.RunOnce();
        Assert.DoesNotContain(second.Findings, item => item.Kind == "undelivered-report-outbox");

        NotifyCommand.ProcessRunnerFactory = () => runner;
        NotifyCommand.HerdrExecutableFactory = () => "fake-herdr";
        runner.Calls.Clear();
        using var writer = new StringWriter();
        Assert.Equal(0, CommandRouter.Execute(
            ["notify", "collect", "--domain", Domain, "--team", Team, "--task-id", "G653-undelivered", "--write", "--format", "json"],
            context,
            writer));
        Assert.True(NotifyPendingDelegationStore.Find(root, Domain, Team, "G653-undelivered").Record!.ReportArrived);
        Assert.Contains(runner.Calls, call => call.Arguments.Contains("prompt"));
        Assert.DoesNotContain(runner.Calls, call => call.Arguments.Contains("send-text"));
    }

    [Fact]
    public void OwnerSeatLossWakesOnlyDesignThroughRecordedTopologyAsEscalation_G657()
    {
        var context = CreateContext();
        RecordMode(context, SessionLayerMode.HerdrOnly);
        WriteTopology(includeDesign: true);
        var runner = new FakeRunner
        {
            AgentsJson = RunningAgentsJson("design", "implementation"),
        };
        var supervisor = CreateSupervisor(context, "unused-agmsg", runner, write: true, boundSeconds: null);

        var pass = supervisor.RunOnce();

        var finding = Assert.Single(pass.Findings, item => item.Kind == "seat-absent");
        Assert.Equal("orchestration", finding.SubjectRole);
        Assert.Equal("design", finding.WakeTargetRole);
        Assert.Equal("escalation", finding.WakeClass);
        Assert.True(finding.WakeDelivered);
        var prompt = Assert.Single(runner.Calls, call =>
            call.Arguments.Take(2).SequenceEqual(["agent", "prompt"]));
        Assert.Equal("wG641:p0", prompt.Arguments[2]);
        Assert.Contains("\"wake_class\":\"escalation\"", prompt.Arguments[3], StringComparison.Ordinal);
        Assert.DoesNotContain(runner.Calls, call =>
            call.Arguments.Take(3).SequenceEqual(["agent", "prompt", "wG641:p1"]));
    }

    [Fact]
    public void ImplementationSeatLossStillWakesOnlyOrchestration_G657()
    {
        var context = CreateContext();
        RecordMode(context, SessionLayerMode.HerdrOnly);
        WriteTopology(includeDesign: true);
        var runner = new FakeRunner
        {
            AgentsJson = RunningAgentsJson("design", "orchestration"),
        };
        var supervisor = CreateSupervisor(context, "unused-agmsg", runner, write: true, boundSeconds: null);

        var pass = supervisor.RunOnce();

        var finding = Assert.Single(pass.Findings, item => item.Kind == "seat-absent");
        Assert.Equal("implementation", finding.SubjectRole);
        Assert.Equal("orchestration", finding.WakeTargetRole);
        Assert.Null(finding.WakeClass);
        var prompt = Assert.Single(runner.Calls, call =>
            call.Arguments.Take(2).SequenceEqual(["agent", "prompt"]));
        Assert.Equal("wG641:p1", prompt.Arguments[2]);
        Assert.DoesNotContain(runner.Calls, call =>
            call.Arguments.Take(3).SequenceEqual(["agent", "prompt", "wG641:p0"]));
    }

    [Fact]
    public void MissingDesignSeatIsTheOperatorRungAndIsNotWatched_G657()
    {
        var context = CreateContext();
        RecordMode(context, SessionLayerMode.HerdrOnly);
        WriteTopology(includeDesign: true);
        var runner = new FakeRunner
        {
            AgentsJson = RunningAgentsJson("orchestration", "implementation"),
        };
        var supervisor = CreateSupervisor(context, "unused-agmsg", runner, write: true, boundSeconds: null);

        var pass = supervisor.RunOnce();

        Assert.DoesNotContain(pass.Findings, item => item.SubjectRole == "design");
        Assert.DoesNotContain(runner.Calls, call =>
            call.Arguments.Take(2).SequenceEqual(["agent", "prompt"]));
    }

    [Fact]
    public void EnglishAndJapaneseGuidanceNameTheMeasuredPreviewContract_G641()
    {
        var rootPath = RepoVersionPolicySource.RepoRoot();
        var english = File.ReadAllText(Path.Combine(rootPath, "docs", "en", "12-agent-message-orchestration.md"));
        var japanese = File.ReadAllText(Path.Combine(rootPath, "docs", "ja", "12-agent-message-orchestration.md"));
        var englishLedger = File.ReadAllText(Path.Combine(rootPath, "docs", "en", "1.0-compatibility-ledger.md"));
        var japaneseLedger = File.ReadAllText(Path.Combine(rootPath, "docs", "ja", "1.0-compatibility-ledger.md"));

        foreach (var document in new[] { english, japanese })
        {
            Assert.Contains("G641", document, StringComparison.Ordinal);
            Assert.Contains("G652", document, StringComparison.Ordinal);
            Assert.Contains("G657", document, StringComparison.Ordinal);
            Assert.Contains("--bound", document, StringComparison.Ordinal);
            Assert.Contains("undelivered-escalation", document, StringComparison.Ordinal);
            Assert.Contains("detectable_at", document, StringComparison.Ordinal);
            Assert.Contains("absent_since_last_cycle", document, StringComparison.Ordinal);
            Assert.Contains("state_change_seq", document, StringComparison.Ordinal);
            Assert.Contains("live-idle", document, StringComparison.Ordinal);
            Assert.Contains("preview-through-1.x", document, StringComparison.Ordinal);
            Assert.Contains("subject_role", document, StringComparison.Ordinal);
            Assert.Contains("wake_target_role", document, StringComparison.Ordinal);
            Assert.Contains("wake_class", document, StringComparison.Ordinal);
            Assert.Contains("declared-label-fallback", document, StringComparison.Ordinal);
        }

        Assert.Contains("measured supervision records", englishLedger, StringComparison.Ordinal);
        Assert.Contains("measured supervision records", japaneseLedger, StringComparison.Ordinal);
        Assert.Contains("supervision escalation ladder", englishLedger, StringComparison.Ordinal);
        Assert.Contains("supervision escalation ladder", japaneseLedger, StringComparison.Ordinal);
    }

    private NotifyMeasuredSupervisor CreateSupervisor(
        CliContext context,
        string scripts,
        FakeRunner runner,
        bool write,
        int? boundSeconds,
        int intervalSeconds = 300) => new(
        context,
        root,
        Domain,
        Team,
        repo: null,
        ownerRole: "orchestration",
        intervalSeconds: intervalSeconds,
        declaredBoundSeconds: boundSeconds,
        staleMinutes: 45,
        claimedSilentMinutes: 720,
        backlogIdleMinutes: 45,
        repairSilentMinutes: 180,
        autoRedispatch: false,
        write,
        format: "json",
        runner,
        herdrExecutable: "fake-herdr",
        agmsgScriptsDirectory: scripts);

    private static string HerdrAgentsJson(long stateChangeSequence) => JsonSerializer.Serialize(new
    {
        result = new
        {
            agents = new[]
            {
                new
                {
                    name = "orchestration", workspace_id = "wG641", pane_id = "wG641:p1", agent = "fixture",
                    agent_session = new { id = "orchestration" }, agent_status = "working", interactive_ready = true,
                    state_change_seq = 1L, last_state_change_at = (string?)null,
                },
                new
                {
                    name = "implementation", workspace_id = "wG641", pane_id = "wG641:p2", agent = "fixture",
                    agent_session = new { id = "implementation" }, agent_status = "working", interactive_ready = true,
                    state_change_seq = stateChangeSequence, last_state_change_at = (string?)"2026-08-07T12:00:00.0000000+00:00",
                },
            },
        },
    });

    private CliContext CreateContext() => new()
    {
        RepoRoot = root,
        Config = new CliConfig
        {
            Project = new ProjectConfig { Domain = Domain, ArtifactRoot = ".intent-cli" },
        },
    };

    private string ResolveEventPath()
    {
        NotifyEventWriter.TryResolvePath(root, Team, out var path, out var error);
        Assert.True(string.IsNullOrEmpty(error), error);
        return path;
    }

    private void RecordMode(CliContext context, string mode)
    {
        using var writer = new StringWriter();
        Assert.Equal(0, SessionLayerCommand.ExecuteSet(
            context,
            ["--domain", Domain, "--team", Team, "--mode", mode, "--write", "--format", "json"],
            writer));
    }

    private void WriteTopology(bool includeDesign = false)
    {
        var path = NotifyRoleTopologyStore.ResolvePath(root, Domain, Team);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var roles = new Dictionary<string, object>
        {
            ["orchestration"] = new { resident = "herdr", workspace_id = "wG641", pane_id = "wG641:p1" },
            ["implementation"] = new { resident = "herdr", workspace_id = "wG641", pane_id = "wG641:p2" },
        };
        if (includeDesign)
        {
            roles["design"] = new { resident = "herdr", workspace_id = "wG641", pane_id = "wG641:p0" };
        }
        File.WriteAllText(path, JsonSerializer.Serialize(new
        {
            domain = Domain,
            team = Team,
            workspace_id = "wG641",
            roles,
        }));
    }

    private static string RunningAgentsJson(params string[] roles) => JsonSerializer.Serialize(new
    {
        result = new
        {
            agents = roles.Select(role => new
            {
                name = role,
                workspace_id = "wG641",
                pane_id = role switch
                {
                    "design" => "wG641:p0",
                    "orchestration" => "wG641:p1",
                    _ => "wG641:p2",
                },
                agent = "fixture",
                agent_session = new { id = role },
                agent_status = "working",
                interactive_ready = true,
            }),
        },
    });

    private sealed class FakeRunner : INotifyProcessRunner
    {
        public List<(string FileName, IReadOnlyList<string> Arguments)> Calls { get; } = [];
        public string AgentsJson { get; set; } = "{\"result\":{\"agents\":[]}}";

        public NotifyProcessResult Run(string fileName, IReadOnlyList<string> arguments)
        {
            Calls.Add((fileName, arguments.ToArray()));
            if (arguments.SequenceEqual(["agent", "list"]))
            {
                return new NotifyProcessResult(0, AgentsJson, string.Empty);
            }

            if (arguments.SequenceEqual(["pane", "process-info", "--pane", "wG641:p1"])
                || arguments.SequenceEqual(["pane", "process-info", "--pane", "wG641:p2"])
                || arguments.SequenceEqual(["pane", "process-info", "--pane", "wG641:p0"]))
            {
                return new NotifyProcessResult(
                    0,
                    "{\"result\":{\"process_info\":{\"foreground_processes\":[]}}}",
                    string.Empty);
            }

            if (fileName == "bash" && arguments.Count > 1 && Path.GetFileName(arguments[0]) == "team.sh")
            {
                return new NotifyProcessResult(0, "orchestration (codex)\n", string.Empty);
            }

            return new NotifyProcessResult(0, string.Empty, string.Empty);
        }
    }
}
