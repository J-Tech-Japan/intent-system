using System.Text.Json;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

/// <summary>
/// G594: absence is check-not-completed, the three production consumers share
/// one record-first predicate, and herdr delivery reports bounded unattended
/// receiver outcomes honestly.
/// </summary>
[Collection("WorkerNextActionSharedState")]
public sealed class SessionLayerPreflightG594Tests : IDisposable
{
    private readonly Workspace workspace = new();

    public void Dispose()
    {
        NotifyCommand.ProcessRunnerFactory = null;
        NotifyCommand.AgmsgScriptsDirectoryFactory = null;
        NotifyCommand.HerdrExecutableFactory = null;
        AutomationInstalledCliSurfaceProbe.PathResolver = null;
        workspace.Dispose();
    }

    [Fact]
    public void NamedUnrecordedTeam_OneVerdictDrivesDoctorGuideAndNotifyWithoutTransport_G594()
    {
        var expected = SessionLayerPreflight.Analyze(workspace.RootPath, Workspace.Domain, Workspace.Team);
        Assert.Equal(SessionLayerPreflight.ConfigurationIncomplete, expected.Verdict);
        Assert.False(expected.Ready);
        var expectedFinding = Assert.Single(expected.Scopes).Findings[0];
        Assert.Equal("session-layer-mode-unrecorded", expectedFinding.Cause);
        Assert.Contains("session-layer set --domain intent-cli --team intent-cli-dev", expectedFinding.Message, StringComparison.Ordinal);

        AutomationInstalledCliSurfaceProbe.PathResolver = _ => null;
        using var doctorWriter = new StringWriter();
        Assert.Equal(1, AutomationDoctorCommand.Execute(
            workspace.Context,
            ["--domain", Workspace.Domain, "--team", Workspace.Team, "--format", "json"],
            doctorWriter));
        var doctor = JsonSerializer.Deserialize<AutomationDoctorResult>(doctorWriter.ToString())!;

        using var guideWriter = new StringWriter();
        Assert.Equal(0, GuideOrchestratorThreadCommand.Execute(
            workspace.Context,
            [
                "--domain", Workspace.Domain,
                "--target-repo", "J-Tech-Japan/intent-system",
                "--agent", "codex",
                "--team", Workspace.Team,
                "--format", "json",
            ],
            guideWriter));
        using var guide = JsonDocument.Parse(guideWriter.ToString());
        var guidePreflight = guide.RootElement.GetProperty("session_layer").GetProperty("preflight");

        var runner = new FakeRunner((_, _) => throw new InvalidOperationException(
            "an unrecorded named team must fail before any transport probe"));
        NotifyCommand.ProcessRunnerFactory = () => runner;
        var (notifyExit, notify) = workspace.RunNotify(write: true);

        Assert.Equal(1, notifyExit);
        Assert.Equal(expected.Verdict, doctor.SessionLayerPreflight.Verdict);
        Assert.Equal(expected.Verdict, guidePreflight.GetProperty("verdict").GetString());
        Assert.Equal(expected.Verdict,
            notify.GetProperty("session_layer_preflight").GetProperty("verdict").GetString());
        Assert.Equal("session-layer-mode-unrecorded", notify.GetProperty("cause").GetString());
        Assert.Equal("default", notify.GetProperty("mode_source").GetString());
        Assert.Contains("session-layer set --domain intent-cli --team intent-cli-dev",
            notify.GetProperty("summary").GetString(), StringComparison.Ordinal);
        Assert.DoesNotContain("not-required", doctorWriter.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("not-required", guideWriter.ToString(), StringComparison.Ordinal);
        Assert.Empty(runner.Calls);
        Assert.False(File.Exists(workspace.ModePath));
    }

    [Fact]
    public void AnonymousEmptyRoot_RemainsUnjudgedUntilExpectedTeamIsDeclared_G594()
    {
        var result = SessionLayerPreflight.Analyze(workspace.RootPath);

        Assert.Equal(SessionLayerPreflight.Unjudged, result.Verdict);
        Assert.Null(result.Ready);
        Assert.False(result.ExpectedTeamDeclared);
        Assert.Empty(result.Scopes);
        Assert.Equal(SessionLayerPreflight.Unjudged, result.PassivePhase.Status);
        Assert.Equal(SessionLayerPreflight.ActiveSkipped, result.ActivePhase.Status);
    }

    [Fact]
    public void RecordedAgmsg_UsesOnlyAgmsgAndDoesNotRequireHerdr_G594()
    {
        workspace.RecordMode(SessionLayerMode.Agmsg);
        workspace.CreateAgmsgScripts();
        var runner = new FakeRunner((_, arguments) =>
            arguments[0].EndsWith("team.sh", StringComparison.Ordinal)
                ? Success(AgmsgRoster())
                : arguments[0].EndsWith("send.sh", StringComparison.Ordinal)
                    ? Success()
                    : throw new InvalidOperationException("recorded agmsg preflight must never probe herdr"));
        NotifyCommand.ProcessRunnerFactory = () => runner;
        NotifyCommand.AgmsgScriptsDirectoryFactory = () => workspace.AgmsgScriptsPath;

        var (exitCode, result) = workspace.RunNotify(write: true);

        Assert.Equal(0, exitCode);
        Assert.True(result.GetProperty("delivered").GetBoolean());
        Assert.Equal(SessionLayerMode.Agmsg, result.GetProperty("mode").GetString());
        Assert.Equal(SessionLayerPreflight.Ready,
            result.GetProperty("session_layer_preflight").GetProperty("verdict").GetString());
        Assert.Equal(SessionLayerPreflight.ActiveAcknowledged,
            result.GetProperty("session_layer_preflight").GetProperty("active_phase").GetProperty("status").GetString());
        Assert.DoesNotContain(runner.Calls, call =>
            call.Arguments.Take(2).SequenceEqual(["agent", "list"])
            || call.Arguments.Take(2).SequenceEqual(["agent", "prompt"]));
    }

    [Fact]
    public void RecordedAgmsgWithHerdrTopology_IsMismatchNamingBothModesWithoutRepair_G594()
    {
        workspace.RecordMode(SessionLayerMode.Agmsg);
        workspace.WriteTopology();
        var before = File.ReadAllText(workspace.ModePath);

        var result = SessionLayerPreflight.Analyze(workspace.RootPath, Workspace.Domain, Workspace.Team);

        Assert.Equal(SessionLayerPreflight.ConfigurationIncomplete, result.Verdict);
        var scope = Assert.Single(result.Scopes);
        var finding = Assert.Single(scope.Findings, item => item.Cause == "topology-mode-mismatch");
        Assert.Equal(Workspace.Team, finding.Team);
        Assert.Equal(SessionLayerMode.Agmsg, finding.RecordedMode);
        Assert.Equal(SessionLayerMode.HerdrOnly, finding.TopologyMode);
        Assert.Contains("recorded mode 'agmsg'", finding.Message, StringComparison.Ordinal);
        Assert.Contains("topology describes mode 'herdr-only'", finding.Message, StringComparison.Ordinal);
        Assert.Equal(before, File.ReadAllText(workspace.ModePath));
    }

    [Fact]
    public void UnreadableModeRecord_IsCannotDetermineAndNeverGreen_G594()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(workspace.ModePath)!);
        File.WriteAllText(workspace.ModePath, "{ not-json");

        var result = SessionLayerPreflight.Analyze(workspace.RootPath, Workspace.Domain, Workspace.Team);

        Assert.Equal(SessionLayerPreflight.CannotDetermine, result.Verdict);
        Assert.False(result.Ready);
        Assert.Equal("session-layer-mode-unreadable", Assert.Single(result.Scopes).Findings[0].Cause);
    }

    [Fact]
    public void UnrecordedNamedTeam_RemainsConfigurationIncompleteWhenTopologyIsUnreadable_G594()
    {
        var topologyPath = NotifyRoleTopologyStore.ResolvePath(workspace.RootPath);
        Directory.CreateDirectory(Path.GetDirectoryName(topologyPath)!);
        File.WriteAllText(topologyPath, "{ not-json");

        var result = SessionLayerPreflight.Analyze(workspace.RootPath, Workspace.Domain, Workspace.Team);

        Assert.Equal(SessionLayerPreflight.ConfigurationIncomplete, result.Verdict);
        var findings = Assert.Single(result.Scopes).Findings;
        Assert.Contains(findings, finding => finding.Cause == "session-layer-mode-unrecorded");
        Assert.Contains(findings, finding => finding.Cause == "topology-unreadable");
        Assert.False(File.Exists(workspace.ModePath));
    }

    [Theory]
    [InlineData("idle-stays-idle", false, "not-observed", "not-applicable", true, SessionLayerPreflight.ActiveNotObserved)]
    [InlineData("idle-transitions", true, "observed", "observed", false, SessionLayerPreflight.ActiveObserved)]
    [InlineData("working-observed-in-progress", true, "observed", "pending", false, SessionLayerPreflight.ActiveInProgress)]
    [InlineData("already-working", true, "unobservable", "not-applicable", false, SessionLayerPreflight.ActiveUnobservable)]
    public void HerdrDelivery_ReportsFourDistinctReceiverOutcomesAndSeparateSettleVerdicts_G598(
        string receiverCase,
        bool expectedDelivered,
        string expectedTransition,
        string expectedSettleOutcome,
        bool expectedResendPermitted,
        string expectedActiveStatus)
    {
        workspace.RecordMode(SessionLayerMode.HerdrOnly);
        workspace.WriteTopology();
        var initialStatus = string.Equals(receiverCase, "already-working", StringComparison.Ordinal)
            ? "working"
            : "idle";
        var runner = new FakeRunner((_, arguments) =>
        {
            if (arguments.SequenceEqual(["agent", "list"]))
            {
                return Success(HerdrRoster(initialStatus));
            }

            if (arguments.Take(2).SequenceEqual(["agent", "prompt"]))
            {
                return string.Equals(receiverCase, "idle-stays-idle", StringComparison.Ordinal)
                    ? Failure("agent_prompt_stalled: no observed state change")
                    : Success("working observed");
            }

            if (arguments.Take(2).SequenceEqual(["agent", "wait"]))
            {
                return string.Equals(receiverCase, "working-observed-in-progress", StringComparison.Ordinal)
                    ? Failure("timeout waiting for settled state")
                    : Success("settled acknowledgement");
            }

            throw new InvalidOperationException("unexpected transport call");
        });
        NotifyCommand.ProcessRunnerFactory = () => runner;
        NotifyCommand.HerdrExecutableFactory = () => "fake-herdr";

        var (exitCode, result) = workspace.RunNotify(write: true);

        Assert.Equal(expectedDelivered ? 0 : 1, exitCode);
        Assert.Equal(expectedDelivered, result.GetProperty("delivered").GetBoolean());
        Assert.Equal(receiverCase, result.GetProperty("receiver_state_outcome").GetString());
        Assert.Equal(expectedTransition, result.GetProperty("working_transition").GetString());
        Assert.Equal(expectedSettleOutcome, result.GetProperty("settle_outcome").GetString());
        Assert.Equal(expectedResendPermitted, result.GetProperty("resend_permitted").GetBoolean());
        Assert.Equal(SessionLayerPreflight.Ready,
            result.GetProperty("session_layer_preflight").GetProperty("passive_phase").GetProperty("status").GetString());
        Assert.Equal(expectedActiveStatus,
            result.GetProperty("session_layer_preflight").GetProperty("active_phase").GetProperty("status").GetString());

        var prompt = Assert.Single(runner.Calls, call => call.Arguments.Take(2).SequenceEqual(["agent", "prompt"]));
        if (string.Equals(receiverCase, "already-working", StringComparison.Ordinal))
        {
            Assert.DoesNotContain("--wait", prompt.Arguments);
        }
        else
        {
            Assert.Contains("--wait", prompt.Arguments);
            Assert.Contains("--timeout", prompt.Arguments);
            Assert.Contains("working", prompt.Arguments);
            if (expectedTransition == "observed")
            {
                var settled = Assert.Single(runner.Calls, call =>
                    call.Arguments.Take(2).SequenceEqual(["agent", "wait"]));
                Assert.Contains("idle", settled.Arguments);
                Assert.Contains("done", settled.Arguments);
                Assert.Contains("blocked", settled.Arguments);
                Assert.Contains("--timeout", settled.Arguments);
            }
            else
            {
                Assert.DoesNotContain(runner.Calls, call =>
                    call.Arguments.Take(2).SequenceEqual(["agent", "wait"]));
            }
        }
    }

    [Fact]
    public void HerdrDelivery_ObservedWorkingWithoutFreshSettledAck_IsSuccessfulAndInProgress_G626()
    {
        workspace.RecordMode(SessionLayerMode.HerdrOnly);
        workspace.WriteTopology();
        var runner = new FakeRunner((_, arguments) =>
        {
            if (arguments.SequenceEqual(["agent", "list"]))
            {
                return Success(HerdrRoster("idle"));
            }

            if (arguments.Take(2).SequenceEqual(["agent", "prompt"]))
            {
                return Success("working observed");
            }

            if (arguments.Take(2).SequenceEqual(["agent", "wait"]))
            {
                return Failure("timeout waiting for settled state");
            }

            throw new InvalidOperationException("unexpected transport call");
        });
        NotifyCommand.ProcessRunnerFactory = () => runner;
        NotifyCommand.HerdrExecutableFactory = () => "fake-herdr";

        var (exitCode, result) = workspace.RunNotify(write: true);

        Assert.Equal(0, exitCode);
        Assert.True(result.GetProperty("delivered").GetBoolean());
        Assert.False(result.GetProperty("resend_permitted").GetBoolean());
        Assert.Equal("working-observed-in-progress", result.GetProperty("receiver_state_outcome").GetString());
        Assert.Equal("observed", result.GetProperty("working_transition").GetString());
        Assert.Equal("pending", result.GetProperty("settle_outcome").GetString());
        Assert.Contains("Delivery succeeded", result.GetProperty("summary").GetString(), StringComparison.Ordinal);
        Assert.Contains("do not resend while it is working", result.GetProperty("summary").GetString(), StringComparison.Ordinal);
        Assert.DoesNotContain("Resend is forbidden", result.GetProperty("summary").GetString(), StringComparison.Ordinal);
        Assert.DoesNotContain("not observed", result.GetProperty("summary").GetString(), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(
            SessionLayerPreflight.ActiveInProgress,
            result.GetProperty("session_layer_preflight")
                .GetProperty("active_phase")
                .GetProperty("status")
                .GetString());
    }

    [Fact]
    public void TwoWorkspaces_WithDifferentGloballyUniqueAgentNames_RouteByRecordedPane_G594()
    {
        using var intentCli = new AmendedRouteWorkspace("intent-cli-dev", "wH", "wH:p2");
        using var sekiban = new AmendedRouteWorkspace("sekiban-workers", "wM", "wM:p7");
        var roster = JsonSerializer.Serialize(new
        {
            result = new
            {
                agents = new object[]
                {
                    Agent("implementation", "wH:p2", "working", "wH"),
                    Agent("sekiban-implementation", "wM:p7", "working", "wM"),
                },
            },
        });
        var runner = new FakeRunner((_, arguments) =>
            arguments.SequenceEqual(["agent", "list"])
                ? Success(roster)
                : arguments.Take(2).SequenceEqual(["agent", "prompt"])
                    ? Success("prompt accepted")
                    : throw new InvalidOperationException("unexpected transport call"));
        NotifyCommand.ProcessRunnerFactory = () => runner;
        NotifyCommand.HerdrExecutableFactory = () => "fake-herdr";

        var first = intentCli.RunNotify();
        var second = sekiban.RunNotify();

        Assert.Equal(0, first.ExitCode);
        Assert.Equal(0, second.ExitCode);
        Assert.True(first.Result.GetProperty("delivered").GetBoolean());
        Assert.True(second.Result.GetProperty("delivered").GetBoolean());
        var prompts = runner.Calls
            .Where(call => call.Arguments.Take(2).SequenceEqual(["agent", "prompt"]))
            .ToArray();
        Assert.Equal(2, prompts.Length);
        Assert.Equal("wH:p2", prompts[0].Arguments[2]);
        Assert.Equal("wM:p7", prompts[1].Arguments[2]);
        Assert.DoesNotContain("implementation", prompts[0].Arguments.Take(3));
        Assert.DoesNotContain("sekiban-implementation", prompts[1].Arguments.Take(3));
    }

    [Theory]
    [InlineData("none", "pane-absent")]
    [InlineData("several", "multiple-agents-at-pane")]
    [InlineData("foreign", "pane-foreign-workspace")]
    public void RecordedWorkspacePane_RequiresExactlyOneRunningAgentAndFailsClosed_G594(
        string scenario,
        string expectedCause)
    {
        workspace.RecordMode(SessionLayerMode.HerdrOnly);
        workspace.WriteTopology();
        var agents = scenario switch
        {
            "none" => new[] { Agent("orchestration-diagnostic", "wH:p9", "idle", "wH") },
            "several" => new[]
            {
                Agent("orchestration-a", "wH:p1", "idle", "wH"),
                Agent("orchestration-b", "wH:p1", "idle", "wH"),
            },
            "foreign" => new[] { Agent("foreign-orchestration", "wH:p1", "idle", "wM") },
            _ => throw new InvalidOperationException($"unknown scenario {scenario}"),
        };
        var roster = JsonSerializer.Serialize(new { result = new { agents } });
        var runner = new FakeRunner((_, arguments) => arguments.SequenceEqual(["agent", "list"])
            ? Success(roster)
            : throw new InvalidOperationException("failed pane identity must never prompt"));
        NotifyCommand.ProcessRunnerFactory = () => runner;
        NotifyCommand.HerdrExecutableFactory = () => "fake-herdr";

        var (exitCode, result) = workspace.RunNotify(write: true);

        Assert.Equal(1, exitCode);
        Assert.Equal(expectedCause, result.GetProperty("cause").GetString());
        var summary = result.GetProperty("summary").GetString()!;
        Assert.Contains(Workspace.Team, summary, StringComparison.Ordinal);
        Assert.Contains("wH", summary, StringComparison.Ordinal);
        Assert.Contains("wH:p1", summary, StringComparison.Ordinal);
        Assert.Contains("diagnostic only", summary, StringComparison.Ordinal);
        Assert.DoesNotContain(runner.Calls, call =>
            call.Arguments.Take(2).SequenceEqual(["agent", "prompt"]));
    }

    [Fact]
    public void HerdrDryRun_ReportsPassiveReadyAndActiveSkippedWithoutPrompt_G594()
    {
        workspace.RecordMode(SessionLayerMode.HerdrOnly);
        workspace.WriteTopology();
        var runner = new FakeRunner((_, arguments) => arguments.SequenceEqual(["agent", "list"])
            ? Success(HerdrRoster("idle"))
            : throw new InvalidOperationException("dry-run must never prompt"));
        NotifyCommand.ProcessRunnerFactory = () => runner;
        NotifyCommand.HerdrExecutableFactory = () => "fake-herdr";

        var (exitCode, result) = workspace.RunNotify(write: false);

        Assert.Equal(0, exitCode);
        Assert.False(result.GetProperty("delivered").GetBoolean());
        var preflight = result.GetProperty("session_layer_preflight");
        Assert.Equal(SessionLayerPreflight.Ready, preflight.GetProperty("passive_phase").GetProperty("status").GetString());
        Assert.Equal(SessionLayerPreflight.ActiveSkipped, preflight.GetProperty("active_phase").GetProperty("status").GetString());
        Assert.DoesNotContain(runner.Calls, call => call.Arguments.Take(2).SequenceEqual(["agent", "prompt"]));
    }

    [Fact]
    public void ProductionConsumersCallTheSharedPreflightInsteadOfOwningPredicates_G594()
    {
        var root = RepoVersionPolicySource.RepoRoot();
        var consumers = new[]
        {
            "AutomationDoctorCommand.cs",
            "GuideOrchestratorThreadCommand.cs",
            "NotifyCommand.cs",
        };

        foreach (var consumer in consumers)
        {
            var source = File.ReadAllText(Path.Combine(root, "src", "IntentSystem.Cli", "Commands", consumer));
            Assert.Contains("SessionLayerPreflight.Analyze(", source, StringComparison.Ordinal);
            Assert.DoesNotContain("SessionLayerModeStore.TryRead", source, StringComparison.Ordinal);
            Assert.DoesNotContain("NotifyRoleTopologyStore.Validate", source, StringComparison.Ordinal);
        }

        var shared = File.ReadAllText(Path.Combine(
            root,
            "src",
            "IntentSystem.Cli",
            "Commands",
            "SessionLayerPreflight.cs"));
        Assert.Contains("SessionLayerModeStore.TryRead", shared, StringComparison.Ordinal);
        Assert.Contains("NotifyRoleTopologyStore.Validate", shared, StringComparison.Ordinal);

        var transport = File.ReadAllText(Path.Combine(
            root,
            "src",
            "IntentSystem.Cli",
            "Commands",
            "NotifyTransport.cs"));
        Assert.DoesNotContain("roles.TryGetValue(toRole)", transport, StringComparison.Ordinal);
        Assert.Contains("agent.PaneId, recordedPane", transport, StringComparison.Ordinal);
        Assert.Contains("agent.WorkspaceId, topology.WorkspaceId", transport, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("en")]
    [InlineData("ja")]
    public void GuidancePinsRecordFirstSharedPhasesAndHonestDelivery_G594(string language)
    {
        var content = File.ReadAllText(Path.Combine(
            RepoVersionPolicySource.RepoRoot(),
            "docs",
            language,
            "12-agent-message-orchestration.md"));

        Assert.Contains("session_layer_preflight", content, StringComparison.Ordinal);
        Assert.Contains("configuration-incomplete", content, StringComparison.Ordinal);
        Assert.Contains("unjudged", content, StringComparison.Ordinal);
        Assert.Contains("cannot-determine", content, StringComparison.Ordinal);
        Assert.Contains("passive", content, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("active", content, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("idle-stays-idle", content, StringComparison.Ordinal);
        Assert.Contains("already-working", content, StringComparison.Ordinal);
        Assert.Contains("unobservable", content, StringComparison.Ordinal);
        Assert.Contains("settle_outcome", content, StringComparison.Ordinal);
        Assert.Contains("pending", content, StringComparison.Ordinal);
        Assert.Contains("resend_permitted", content, StringComparison.Ordinal);
        Assert.Contains("working-observed-in-progress", content, StringComparison.Ordinal);
        Assert.Contains("agent prompt --wait", content, StringComparison.Ordinal);
        Assert.Contains("logical role name", content, StringComparison.Ordinal);
        Assert.Contains("agent name", content, StringComparison.Ordinal);
        Assert.Contains("workspace", content, StringComparison.Ordinal);
        Assert.Contains("pane", content, StringComparison.Ordinal);
        Assert.Contains("fallback", content, StringComparison.Ordinal);

        var runtime = HerdrOnlyOperatingGuide.RenderMarkdown([]);
        Assert.Contains("shared machine-readable session-layer preflight", runtime, StringComparison.Ordinal);
        Assert.Contains("Absence is check-not-completed", runtime, StringComparison.Ordinal);
        Assert.Contains("already-working", runtime, StringComparison.Ordinal);
        Assert.Contains("settle_outcome", runtime, StringComparison.Ordinal);
        Assert.Contains("resend_permitted: false", runtime, StringComparison.Ordinal);
    }

    private static NotifyProcessResult Success(string output = "") => new(0, output, "");

    private static NotifyProcessResult Failure(string error) => new(1, "", error);

    private static string AgmsgRoster() =>
        "Team: intent-cli-dev\n"
        + "  implementation (codex) — /work/implementation\n"
        + "  orchestration (claude) — /work/orchestration\n";

    private static string HerdrRoster(string status) => JsonSerializer.Serialize(new
    {
        result = new
        {
            agents = new object[]
            {
                Agent("implementation", "wH:p2", "idle"),
                Agent("orchestration", "wH:p1", status),
            },
        },
    });

    private static object Agent(string name, string paneId, string status, string workspaceId = "wH") => new
    {
        name,
        workspace_id = workspaceId,
        pane_id = paneId,
        agent = "codex",
        agent_session = new { id = name },
        agent_status = status,
        interactive_ready = true,
    };

    private sealed class FakeRunner(
        Func<string, IReadOnlyList<string>, NotifyProcessResult> handler) : INotifyProcessRunner
    {
        public List<(string FileName, IReadOnlyList<string> Arguments)> Calls { get; } = [];

        public NotifyProcessResult Run(string fileName, IReadOnlyList<string> arguments)
        {
            Calls.Add((fileName, arguments.ToArray()));
            return handler(fileName, arguments);
        }
    }

    private sealed class AmendedRouteWorkspace : IDisposable
    {
        private readonly CliContext context;

        public AmendedRouteWorkspace(string team, string workspaceId, string paneId)
        {
            Team = team;
            WorkspaceId = workspaceId;
            PaneId = paneId;
            RootPath = Directory.CreateTempSubdirectory("amended-route-g594-").FullName;
            context = new CliContext
            {
                RepoRoot = RootPath,
                Config = new CliConfig
                {
                    Project = new ProjectConfig
                    {
                        Domain = Workspace.Domain,
                        ArtifactRoot = ".intent-cli",
                    },
                },
            };
            using var writer = new StringWriter();
            var exitCode = SessionLayerCommand.ExecuteSet(
                context,
                [
                    "--domain", Workspace.Domain,
                    "--team", team,
                    "--mode", SessionLayerMode.HerdrOnly,
                    "--write",
                    "--format", "json",
                ],
                writer);
            Assert.True(exitCode == 0, writer.ToString());

            var topologyPath = NotifyRoleTopologyStore.ResolvePath(RootPath);
            Directory.CreateDirectory(Path.GetDirectoryName(topologyPath)!);
            File.WriteAllText(topologyPath, JsonSerializer.Serialize(new
            {
                team,
                workspace_id = workspaceId,
                roles = new Dictionary<string, object>
                {
                    ["orchestration"] = new
                    {
                        resident = "herdr",
                        workspace_id = workspaceId,
                        pane_id = $"{workspaceId}:p1",
                    },
                    ["implementation"] = new
                    {
                        resident = "herdr",
                        workspace_id = workspaceId,
                        pane_id = paneId,
                    },
                },
            }));
        }

        public string RootPath { get; }

        public string Team { get; }

        public string WorkspaceId { get; }

        public string PaneId { get; }

        public (int ExitCode, JsonElement Result) RunNotify()
        {
            using var writer = new StringWriter();
            var exitCode = CommandRouter.Execute(
                [
                    "notify", "report",
                    "--domain", Workspace.Domain,
                    "--team", Team,
                    "--from", "orchestration",
                    "--to", "implementation",
                    "--task-id", $"G594-{Team}",
                    "--status", "completed",
                    "--artifact", "https://example.test/pr/1292",
                    "--summary", "pane identity verified",
                    "--write",
                    "--format", "json",
                ],
                context,
                writer);
            return (exitCode, JsonDocument.Parse(writer.ToString()).RootElement.Clone());
        }

        public void Dispose()
        {
            if (Directory.Exists(RootPath))
            {
                Directory.Delete(RootPath, recursive: true);
            }
        }
    }

    private sealed class Workspace : IDisposable
    {
        public const string Domain = "intent-cli";
        public const string Team = "intent-cli-dev";

        public Workspace()
        {
            RootPath = Directory.CreateTempSubdirectory("session-preflight-g594-").FullName;
            Context = new CliContext
            {
                RepoRoot = RootPath,
                Config = new CliConfig
                {
                    Project = new ProjectConfig
                    {
                        Domain = Domain,
                        ArtifactRoot = ".intent-cli",
                    },
                },
            };
        }

        public string RootPath { get; }

        public CliContext Context { get; }

        public string ModePath => SessionLayerModeStore.ResolvePath(RootPath);

        public string AgmsgScriptsPath => Path.Combine(RootPath, "agmsg-scripts");

        public void RecordMode(string mode)
        {
            using var writer = new StringWriter();
            var exitCode = SessionLayerCommand.ExecuteSet(
                Context,
                ["--domain", Domain, "--team", Team, "--mode", mode, "--write", "--format", "json"],
                writer);
            Assert.True(exitCode == 0, writer.ToString());
        }

        public void CreateAgmsgScripts()
        {
            Directory.CreateDirectory(AgmsgScriptsPath);
            File.WriteAllText(Path.Combine(AgmsgScriptsPath, "team.sh"), "fixture");
            File.WriteAllText(Path.Combine(AgmsgScriptsPath, "send.sh"), "fixture");
        }

        public void WriteTopology()
        {
            var path = NotifyRoleTopologyStore.ResolvePath(RootPath);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(new
            {
                team = Team,
                workspace_id = "wH",
                roles = new Dictionary<string, object>
                {
                    ["implementation"] = new
                    {
                        resident = "herdr",
                        workspace_id = "wH",
                        pane_id = "wH:p2",
                    },
                    ["orchestration"] = new
                    {
                        resident = "herdr",
                        workspace_id = "wH",
                        pane_id = "wH:p1",
                    },
                },
            }));
        }

        public (int ExitCode, JsonElement Result) RunNotify(bool write)
        {
            using var writer = new StringWriter();
            var exitCode = CommandRouter.Execute(
                [
                    "notify", "report",
                    "--domain", Domain,
                    "--team", Team,
                    "--from", "implementation",
                    "--to", "orchestration",
                    "--task-id", "G594-demo",
                    "--status", "completed",
                    "--artifact", "https://example.test/pr/1292",
                    "--summary", "preflight complete",
                    write ? "--write" : "--dry-run",
                    "--format", "json",
                ],
                Context,
                writer);
            return (exitCode, JsonDocument.Parse(writer.ToString()).RootElement.Clone());
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
