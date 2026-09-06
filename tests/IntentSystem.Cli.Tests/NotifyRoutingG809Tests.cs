using System.Text.Json;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;
using Xunit.Abstractions;

namespace IntentSystem.Cli.Tests;

[Collection("WorkerNextActionSharedState")]
public sealed class NotifyRoutingG809Tests : IDisposable
{
    private readonly ITestOutputHelper output;
    private readonly G809Workspace workspace = new();

    public NotifyRoutingG809Tests(ITestOutputHelper output)
    {
        this.output = output;
        NotifyCommand.HerdrExecutableFactory = () => "fake-herdr";
        NotifyCommand.UtcNowFactory = () => new DateTimeOffset(2026, 9, 5, 12, 0, 0, TimeSpan.Zero);
    }

    public void Dispose()
    {
        NotifyCommand.ProcessRunnerFactory = null;
        NotifyCommand.AgmsgScriptsDirectoryFactory = null;
        NotifyCommand.HerdrExecutableFactory = null;
        NotifyCommand.UtcNowFactory = null;
        workspace.Dispose();
    }

    public static IEnumerable<object?[]> ExplicitAssignments()
    {
        var pairs = new[]
        {
            (From: "architect", To: "orchestrator"),
            (From: "orchestrator", To: "builder"),
            (From: "orchestrator", To: "reviewer"),
            (From: "architect", To: "reviewer"),
            (From: "architect", To: "steward"),
            (From: "reviewer", To: "orchestrator"),
            (From: "reviewer", To: "steward"),
        };
        var kinds = new string?[]
        {
            null,
            NotifyEventKindRouting.Completion,
            NotifyEventKindRouting.Transition,
            NotifyEventKindRouting.Acknowledgement,
            NotifyEventKindRouting.Escalation,
            NotifyEventKindRouting.Question,
            NotifyEventKindRouting.Blocked,
        };

        foreach (var pair in pairs)
        {
            foreach (var kind in kinds)
            {
                yield return [pair.From, pair.To, kind];
            }
        }
    }

    public static IEnumerable<object?[]> ResearchAssignments()
    {
        var pairs = new[]
        {
            (From: "architect", To: "orchestrator"),
            (From: "architect", To: "steward"),
            (From: "reviewer", To: "orchestrator"),
            (From: "reviewer", To: "steward"),
        };
        var kinds = new string?[]
        {
            null,
            NotifyEventKindRouting.Completion,
            NotifyEventKindRouting.Transition,
            NotifyEventKindRouting.Acknowledgement,
            NotifyEventKindRouting.Escalation,
            NotifyEventKindRouting.Question,
            NotifyEventKindRouting.Blocked,
        };

        foreach (var pair in pairs)
        {
            foreach (var kind in kinds)
            {
                yield return [pair.From, pair.To, kind];
            }
        }
    }

    [Theory]
    [MemberData(nameof(ExplicitAssignments))]
    public void ExplicitDelegateAssigneeWinsAcrossEventKinds_G809(
        string from,
        string to,
        string? eventKind)
    {
        workspace.SetMode(SessionLayerMode.HerdrOnly);
        var runner = workspace.NewRunner();
        NotifyCommand.ProcessRunnerFactory = () => runner;

        var taskId = $"G809-{from}-{to}-{eventKind ?? "omitted"}";
        var args = workspace.DelegateArgs(from, to, taskId, eventKind, write: false);
        var (exitCode, result) = workspace.Run(args);

        Assert.Equal(0, exitCode);
        Assert.Equal(to, result.GetProperty("to_role").GetString());
        Assert.False(result.GetProperty("delivered").GetBoolean());
        Assert.Contains("would deliver notification", result.GetProperty("summary").GetString(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(runner.Calls, call => call.Arguments.Take(2).SequenceEqual(["agent", "prompt"]));
        Assert.DoesNotContain(runner.Calls, call => call.Arguments.Take(2).SequenceEqual(["agent", "wait"]));
        output.WriteLine(
            $"G809 AC1 explicit-assignment from={from}; requested_to={to}; event_kind={eventKind ?? "<omitted>"}; resolved_to={result.GetProperty("to_role").GetString()}; dry_run=true; receiver_calls=0; accepted=true");
    }

    [Theory]
    [MemberData(nameof(ResearchAssignments))]
    public void ResearchDelegateKeepsAllFourExplicitDestinationsAcrossEventKinds_G809(
        string from,
        string to,
        string? eventKind)
    {
        workspace.SetMode(SessionLayerMode.HerdrOnly);
        var runner = workspace.NewRunner();
        NotifyCommand.ProcessRunnerFactory = () => runner;

        var taskId = $"G809-research-{from}-{to}-{eventKind ?? "omitted"}";
        var (dryExitCode, dryResult) = workspace.Run(
            workspace.DelegateArgs(from, to, taskId, eventKind, write: false, research: true));

        Assert.Equal(0, dryExitCode);
        Assert.Equal(to, dryResult.GetProperty("to_role").GetString());
        Assert.Equal("research", dryResult.GetProperty("task_kind").GetString());
        Assert.Contains("task-kind: research", dryResult.GetProperty("payload").GetString(), StringComparison.Ordinal);
        Assert.DoesNotContain(runner.Calls, call => call.Arguments.Take(2).SequenceEqual(["agent", "prompt"]));

        runner.Calls.Clear();
        var (writeExitCode, writeResult) = workspace.Run(
            workspace.DelegateArgs(from, to, taskId, eventKind, write: true, research: true));
        Assert.Equal(0, writeExitCode);
        Assert.True(writeResult.GetProperty("delivered").GetBoolean());
        Assert.Equal(to, writeResult.GetProperty("to_role").GetString());
        var prompt = Assert.Single(runner.Calls, call => call.Arguments.Take(2).SequenceEqual(["agent", "prompt"]));
        var wait = Assert.Single(runner.Calls, call => call.Arguments.Take(2).SequenceEqual(["agent", "wait"]));
        Assert.Equal($"wG809:p-{to}", prompt.Arguments[2]);
        Assert.Equal($"wG809:p-{to}", wait.Arguments[2]);
        output.WriteLine(
            $"G809 AC1/AC2 research-pair={from}->{to}; event_kind={eventKind ?? "<omitted>"}; dry_resolved_to={dryResult.GetProperty("to_role").GetString()}; write_resolved_to={writeResult.GetProperty("to_role").GetString()}; task_kind=research; dry_receiver_calls=0; write_prompt_calls=1; write_wait_calls=1; accepted=true");
    }

    [Fact]
    public void WriteDelegateUsesOnlyTheExplicitRecipientAndAgreesAcrossEvidence_G809()
    {
        workspace.SetMode(SessionLayerMode.HerdrOnly);
        var runner = workspace.NewRunner();
        NotifyCommand.ProcessRunnerFactory = () => runner;

        var taskId = "G809-builder-fixture";
        var (exitCode, result) = workspace.Run(
            workspace.DelegateArgs("architect", "builder", taskId, NotifyEventKindRouting.Question, write: true));

        Assert.Equal(0, exitCode);
        Assert.True(result.GetProperty("delivered").GetBoolean());
        Assert.Equal("builder", result.GetProperty("to_role").GetString());
        var payload = result.GetProperty("payload").GetString()!;
        Assert.Contains("role: builder", payload, StringComparison.Ordinal);
        Assert.Contains("--from builder --to architect", result.GetProperty("report_command").GetString(), StringComparison.Ordinal);

        var pending = NotifyPendingDelegationStore.Find(
            workspace.RootPath,
            G809Workspace.Domain,
            G809Workspace.Team,
            taskId);
        Assert.True(pending.Resolved, pending.Error);
        Assert.NotNull(pending.Record);
        Assert.Equal("builder", pending.Record!.RecipientRole);
        Assert.Contains("workspace=wG809;pane=wG809:p-builder", pending.Record.RecipientIdentity, StringComparison.Ordinal);

        var prompt = Assert.Single(runner.Calls, call => call.Arguments.Take(2).SequenceEqual(["agent", "prompt"]));
        var wait = Assert.Single(runner.Calls, call => call.Arguments.Take(2).SequenceEqual(["agent", "wait"]));
        Assert.Equal("wG809:p-builder", prompt.Arguments[2]);
        Assert.Equal("wG809:p-builder", wait.Arguments[2]);
        Assert.DoesNotContain(runner.Calls, call => call.Arguments.Count > 2 && call.Arguments[2] is "wG809:p-architect" or "wG809:p-orchestrator");

        output.WriteLine(
            $"G809 AC2/AC9 write_case=architect->builder; result_to={result.GetProperty("to_role").GetString()}; task_role={payload.Split('\n').Single(line => line.StartsWith("role:", StringComparison.Ordinal))}; pending_recipient={pending.Record.RecipientRole}; pending_identity={pending.Record.RecipientIdentity}; report_from=builder; prompt_calls=1; wait_calls=1; unintended_recipient_calls=0; delivered=true");
    }

    [Fact]
    public void AgmsgWriteUsesExplicitRecipientWithoutEventKindSubstitution_G809()
    {
        workspace.SetMode(SessionLayerMode.Agmsg);
        var scripts = Path.Combine(workspace.RootPath, "agmsg-scripts");
        Directory.CreateDirectory(scripts);
        File.WriteAllText(Path.Combine(scripts, "team.sh"), "fixture");
        File.WriteAllText(Path.Combine(scripts, "send.sh"), "fixture");
        var runner = workspace.NewRunner(agmsg: true);
        NotifyCommand.AgmsgScriptsDirectoryFactory = () => scripts;
        NotifyCommand.ProcessRunnerFactory = () => runner;

        var (exitCode, result) = workspace.Run(
            workspace.DelegateArgs("architect", "orchestrator", "G809-agmsg", NotifyEventKindRouting.Blocked, write: true));

        Assert.Equal(0, exitCode);
        Assert.True(result.GetProperty("delivered").GetBoolean());
        Assert.Equal("orchestrator", result.GetProperty("to_role").GetString());
        var send = Assert.Single(runner.Calls, call => call.Arguments.Any(argument => argument.EndsWith("send.sh", StringComparison.Ordinal)));
        Assert.Equal("orchestrator", send.Arguments[3]);
        output.WriteLine("G809 AC2 transport=agmsg; requested_to=orchestrator; send_to=orchestrator; competing_destination_calls=0; delivered=true");
    }

    [Fact]
    public void UnknownExplicitAssigneeFailsClosedInsteadOfReceivingEventKindDefault_G809()
    {
        workspace.SetMode(SessionLayerMode.HerdrOnly);
        var runner = workspace.NewRunner();
        NotifyCommand.ProcessRunnerFactory = () => runner;

        var (exitCode, result) = workspace.Run(
            workspace.DelegateArgs("architect", "not-recorded", "G809-unknown", NotifyEventKindRouting.Question, write: false));

        Assert.Equal(1, exitCode);
        Assert.Equal("unknown-role", result.GetProperty("cause").GetString());
        Assert.Equal("not-recorded", result.GetProperty("to_role").GetString());
        Assert.DoesNotContain(runner.Calls, call => call.Arguments.Take(2).SequenceEqual(["agent", "prompt"]));
        output.WriteLine($"G809 AC5 unknown_assignee=not-recorded; event_kind=question; exit={exitCode}; cause={result.GetProperty("cause").GetString()}; receiver_calls=0; substitute_default=false");
    }

    [Fact]
    public void StewardJudgementStillRequiresRecordedUpstreamEvidence_G809()
    {
        workspace.SetMode(SessionLayerMode.HerdrOnly);
        var runner = workspace.NewRunner();
        NotifyCommand.ProcessRunnerFactory = () => runner;
        var pendingPath = NotifyPendingDelegationStore.ResolvePath(workspace.RootPath, G809Workspace.Domain, G809Workspace.Team);
        var before = File.Exists(pendingPath) ? File.ReadAllBytes(pendingPath) : [];

        var (exitCode, result) = workspace.Run(
            workspace.DelegateArgs("steward", "orchestrator", "G809-steward-no-upstream", NotifyEventKindRouting.Question, write: true));

        Assert.Equal(1, exitCode);
        Assert.Equal("steward-boundary-refused", result.GetProperty("cause").GetString());
        Assert.Contains("no recorded upstream Architect ruling/delegation", result.GetProperty("summary").GetString(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(runner.Calls, call => call.Arguments.Take(2).SequenceEqual(["agent", "prompt"]));
        var after = File.Exists(pendingPath) ? File.ReadAllBytes(pendingPath) : [];
        Assert.Equal(before, after);
        output.WriteLine($"G809 AC3 authority_guard=steward-question; upstream=missing; exit={exitCode}; cause={result.GetProperty("cause").GetString()}; receiver_calls=0; pending_bytes_unchanged={before.SequenceEqual(after)}");
    }

    [Fact]
    public void DryRunDoesNotReplayOrRewriteHistoricalPendingBytes_G809()
    {
        workspace.SetMode(SessionLayerMode.HerdrOnly);
        var historical = new NotifyPendingDelegation
        {
            Domain = G809Workspace.Domain,
            Team = G809Workspace.Team,
            TaskId = "synthetic-G805",
            DelegatingRole = "architect",
            RecipientRole = "architect",
            RecipientIdentity = "role=architect;workspace=wG809;pane=wG809:p-architect",
            ExpectedArtifact = "old-result",
            ExpectedArtifacts = ["old-result"],
            Objective = "historical misroute",
            Inputs = ["legacy"],
            ResultNonce = "synthetic-G805-v1",
            DispatchedAt = new DateTimeOffset(2026, 9, 5, 11, 0, 0, TimeSpan.Zero),
            TransportMode = SessionLayerMode.HerdrOnly,
            Resident = NotifyRecordedRole.HerdrResident,
            WorkspaceId = "wG809",
            PaneId = "wG809:p-architect",
        };
        Assert.True(NotifyPendingDelegationStore.WriteDispatch(workspace.RootPath, historical).Written);
        var path = NotifyPendingDelegationStore.ResolvePath(workspace.RootPath, G809Workspace.Domain, G809Workspace.Team);
        var before = File.ReadAllBytes(path);
        var runner = workspace.NewRunner();
        NotifyCommand.ProcessRunnerFactory = () => runner;

        var (exitCode, result) = workspace.Run(
            workspace.DelegateArgs("architect", "orchestrator", "G809-forward-only", null, write: false));

        Assert.Equal(0, exitCode);
        Assert.False(result.GetProperty("delivered").GetBoolean());
        Assert.Equal(before, File.ReadAllBytes(path));
        Assert.DoesNotContain(runner.Calls, call => call.Arguments.Take(2).SequenceEqual(["agent", "prompt"]));
        output.WriteLine($"G809 AC6 historical_task=synthetic-G805; new_task=G809-forward-only; dry_run=true; exit={exitCode}; historical_bytes_unchanged={before.SequenceEqual(File.ReadAllBytes(path))}; replayed=false");
    }

    [Fact]
    public void GuidesAndDelegateHelpDocumentExplicitAssignmentAndNoReplay_G809()
    {
        using var designWriter = new StringWriter();
        Assert.Equal(0, GuideDesignThreadCommand.Execute(
            workspace.Context,
            ["--domain", G809Workspace.Domain, "--team", G809Workspace.Team, "--format", "markdown"],
            designWriter));
        Assert.Contains("Explicit notify delegate assignment (G809)", designWriter.ToString(), StringComparison.Ordinal);
        Assert.Contains("explicit `--to` assignee wins", designWriter.ToString(), StringComparison.Ordinal);
        Assert.Contains("historical misrouted", designWriter.ToString(), StringComparison.OrdinalIgnoreCase);

        using var designJsonWriter = new StringWriter();
        Assert.Equal(0, GuideDesignThreadCommand.Execute(
            workspace.Context,
            ["--domain", G809Workspace.Domain, "--team", G809Workspace.Team, "--format", "json"],
            designJsonWriter));
        using var designJson = JsonDocument.Parse(designJsonWriter.ToString());
        var designResearch = designJson.RootElement.GetProperty("research_delegation");
        Assert.Contains("explicit --to assignee wins", designResearch.GetProperty("what_goes_down").GetString(), StringComparison.Ordinal);
        Assert.Contains("historical misroutes are not replayed", designResearch.GetProperty("what_stays").GetString(), StringComparison.Ordinal);

        using var orchestratorWriter = new StringWriter();
        Assert.Equal(0, GuideOrchestratorThreadCommand.Execute(
            workspace.Context,
            ["--domain", G809Workspace.Domain, "--target-repo", "J-Tech-Japan/intent-system", "--agent", "codex", "--team", G809Workspace.Team, "--format", "markdown"],
            orchestratorWriter));
        Assert.Contains("Explicit notify delegate assignment (G809)", orchestratorWriter.ToString(), StringComparison.Ordinal);

        using var orchestratorJsonWriter = new StringWriter();
        Assert.Equal(0, GuideOrchestratorThreadCommand.Execute(
            workspace.Context,
            ["--domain", G809Workspace.Domain, "--target-repo", "J-Tech-Japan/intent-system", "--agent", "codex", "--team", G809Workspace.Team, "--format", "json"],
            orchestratorJsonWriter));
        using var orchestratorJson = JsonDocument.Parse(orchestratorJsonWriter.ToString());
        var orchestratorPrerequisites = orchestratorJson.RootElement.GetProperty("pre_delegation_prerequisites");
        Assert.Contains("event-kind inference", orchestratorPrerequisites.GetProperty("summary").GetString(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("historical misroutes are not replayed", orchestratorPrerequisites.GetProperty("summary").GetString(), StringComparison.OrdinalIgnoreCase);

        using var helpWriter = new StringWriter();
        Assert.Equal(0, NotifyCommand.ExecuteDelegate(workspace.Context, ["--help"], helpWriter));
        var help = helpWriter.ToString();
        Assert.Contains("G809 assignment contract", help, StringComparison.Ordinal);
        Assert.Contains("--routing-root <host-root>", help, StringComparison.Ordinal);
        Assert.Contains("historical misroutes are not replayed", help, StringComparison.Ordinal);
        using var helpJsonWriter = new StringWriter();
        Assert.Equal(0, NotifyCommand.ExecuteDelegate(workspace.Context, ["--help", "--format", "json"], helpJsonWriter));
        using var helpJson = JsonDocument.Parse(helpJsonWriter.ToString());
        Assert.Contains("G809 assignment contract", helpJson.RootElement.GetProperty("assignment").GetString(), StringComparison.Ordinal);
        output.WriteLine("G809 AC7 guides=design-thread markdown+json, orchestrator-thread markdown+json, notify delegate help; explicit-assignment-precedence=true; dry-run-recipient-verification=true; Steward-guards-retained=true; report/escalate-routing-unchanged=true; historical-replay=false");
    }

    private sealed class G809Workspace : IDisposable
    {
        public const string Domain = "intent-cli";
        public const string Team = "intent-cli-dev";

        public G809Workspace()
        {
            RootPath = Directory.CreateTempSubdirectory("notify-g809-").FullName;
            Directory.CreateDirectory(Path.Combine(RootPath, ".intent-cli"));
            WriteTopology();
            Context = new CliContext
            {
                RepoRoot = RootPath,
                Config = new CliConfig
                {
                    Project = new ProjectConfig { Domain = Domain, ArtifactRoot = ".intent-cli" },
                },
            };
        }

        public string RootPath { get; }
        public CliContext Context { get; }

        public void SetMode(string mode)
        {
            var topologyPath = NotifyRoleTopologyStore.ResolvePath(RootPath, Domain, Team);
            if (string.Equals(mode, SessionLayerMode.Agmsg, StringComparison.Ordinal))
            {
                File.Delete(topologyPath);
            }
            else
            {
                WriteTopology();
            }

            using var writer = new StringWriter();
            var exitCode = SessionLayerCommand.ExecuteSet(
                Context,
                ["--domain", Domain, "--team", Team, "--mode", mode, "--write", "--format", "json"],
                writer);
            Assert.Equal(0, exitCode);
        }

        public (int ExitCode, JsonElement Result) Run(string[] args)
        {
            using var writer = new StringWriter();
            var exitCode = CommandRouter.Execute(args, Context, writer);
            return (exitCode, JsonDocument.Parse(writer.ToString()).RootElement.Clone());
        }

        public string[] DelegateArgs(string from, string to, string taskId, string? eventKind, bool write, bool research = false)
        {
            var args = new List<string>
            {
                "notify", "delegate", "--domain", Domain, "--team", Team,
                "--from", from, "--to", to, "--report-to", "architect",
                "--task-id", taskId, "--objective", "route this explicit assignment",
                "--input", "issue #1763", "--expected-artifact", "routing evidence",
                "--result-nonce", taskId + "-nonce", "--routing-root", RootPath,
                write ? "--write" : "--dry-run", "--format", "json",
            };
            if (research)
            {
                args.Insert(args.IndexOf(write ? "--write" : "--dry-run"), "--question");
                args.Insert(args.IndexOf(write ? "--write" : "--dry-run"), "measure the assigned surface");
                args.Insert(args.IndexOf(write ? "--write" : "--dry-run"), "--task-kind");
                args.Insert(args.IndexOf(write ? "--write" : "--dry-run"), "research");
            }
            if (eventKind is not null)
            {
                args.Insert(args.IndexOf(write ? "--write" : "--dry-run"), "--event-kind");
                args.Insert(args.IndexOf(write ? "--write" : "--dry-run"), eventKind);
            }

            return args.ToArray();
        }

        public FakeRunner NewRunner(bool agmsg = false) => new(agmsg ? AgmsgRoster : HerdrRoster);

        private void WriteTopology()
        {
            var topology = new
            {
                domain = Domain,
                team = Team,
                workspace_id = "wG809",
                roles = new Dictionary<string, object>
                {
                    ["architect"] = Pane("wG809:p-architect"),
                    ["orchestrator"] = Pane("wG809:p-orchestrator"),
                    ["builder"] = Pane("wG809:p-builder"),
                    ["reviewer"] = Pane("wG809:p-reviewer"),
                    ["steward"] = Pane("wG809:p-steward"),
                },
            };
            var path = NotifyRoleTopologyStore.ResolvePath(RootPath, Domain, Team);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(topology));
        }

        private static object Pane(string pane) => new
        {
            resident = "herdr",
            workspace_id = "wG809",
            pane_id = pane,
        };

        private static string HerdrRoster => JsonSerializer.Serialize(new
        {
            result = new
            {
                agents = new[]
                {
                    Agent("wG809:p-architect"), Agent("wG809:p-orchestrator"), Agent("wG809:p-builder"),
                    Agent("wG809:p-reviewer"), Agent("wG809:p-steward"),
                },
            },
        });

        private static string AgmsgRoster =>
            "Team: intent-cli-dev\n"
            + "  architect (codex) — /work/architect\n"
            + "  orchestrator (codex) — /work/orchestrator\n"
            + "  builder (codex) — /work/builder\n"
            + "  reviewer (codex) — /work/reviewer\n"
            + "  steward (codex) — /work/steward\n";

        private static object Agent(string pane) => new
        {
            name = pane[("wG809:".Length)..],
            workspace_id = "wG809",
            pane_id = pane,
            agent = "codex",
            agent_session = new { id = pane },
            agent_status = "idle",
            interactive_ready = true,
        };

        public void Dispose()
        {
            if (Directory.Exists(RootPath))
            {
                Directory.Delete(RootPath, recursive: true);
            }
        }
    }

    private sealed class FakeRunner(string roster) : INotifyProcessRunner
    {
        public List<(string FileName, IReadOnlyList<string> Arguments)> Calls { get; } = [];

        public NotifyProcessResult Run(string fileName, IReadOnlyList<string> arguments)
        {
            Calls.Add((fileName, arguments.ToArray()));
            if (arguments.SequenceEqual(["agent", "list"]))
            {
                return new NotifyProcessResult(0, roster, string.Empty);
            }

            if (arguments.Count > 0 && arguments[0].EndsWith("team.sh", StringComparison.Ordinal))
            {
                return new NotifyProcessResult(0, roster, string.Empty);
            }

            return new NotifyProcessResult(0, string.Empty, string.Empty);
        }
    }
}
