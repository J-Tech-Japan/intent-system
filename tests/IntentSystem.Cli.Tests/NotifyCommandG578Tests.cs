using System.Text.Json;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

[Collection("WorkerNextActionSharedState")]
public sealed class NotifyCommandG578Tests : IDisposable
{
    private static readonly DateTimeOffset FixedNow = new(2026, 8, 2, 12, 34, 56, TimeSpan.Zero);
    private readonly NotifyWorkspace workspace = new();

    public NotifyCommandG578Tests()
    {
        NotifyCommand.AgmsgScriptsDirectoryFactory = () => workspace.AgmsgScriptsPath;
        NotifyCommand.HerdrExecutableFactory = () => "fake-herdr";
        NotifyCommand.UtcNowFactory = () => FixedNow;
    }

    public void Dispose()
    {
        NotifyCommand.ProcessRunnerFactory = null;
        NotifyCommand.AgmsgScriptsDirectoryFactory = null;
        NotifyCommand.HerdrExecutableFactory = null;
        NotifyCommand.UtcNowFactory = null;
        workspace.Dispose();
    }

    [Theory]
    [InlineData(SessionLayerMode.Agmsg)]
    [InlineData(SessionLayerMode.HerdrOnly)]
    public void Delegate_UsesTheSameSurface_AndEmbedsTheCanonicalReportingContract_G578(string mode)
    {
        workspace.SetMode(mode);
        var runner = SuccessfulRunner();
        NotifyCommand.ProcessRunnerFactory = () => runner;

        var (exitCode, result) = workspace.Run(DelegateArgs());

        Assert.Equal(0, exitCode);
        Assert.Equal(mode, result.GetProperty("mode").GetString());
        Assert.Equal("recorded", result.GetProperty("mode_source").GetString());
        Assert.True(result.GetProperty("delivered").GetBoolean());
        var payload = result.GetProperty("payload").GetString()!;
        Assert.Contains("TASK G578-demo", payload, StringComparison.Ordinal);
        Assert.Contains("expected-artifact: draft PR URL", payload, StringComparison.Ordinal);
        Assert.Contains("result-prefix: ORCH_RESULT", payload, StringComparison.Ordinal);
        Assert.Contains("result-nonce: demo-nonce", payload, StringComparison.Ordinal);
        Assert.Contains("canonical-report-command: intent-cli notify report", payload, StringComparison.Ordinal);
        Assert.Contains("--from implementation --to orchestration", payload, StringComparison.Ordinal);
        Assert.Contains($"--routing-root '{workspace.RootPath}'", payload, StringComparison.Ordinal);
        Assert.Contains("required-final-step", payload, StringComparison.Ordinal);
        Assert.DoesNotContain("agmsg send", payload, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("herdr agent prompt", payload, StringComparison.OrdinalIgnoreCase);

        var delivery = Assert.Single(runner.Calls, call =>
            mode == SessionLayerMode.Agmsg
                ? call.Arguments.Any(argument => argument.EndsWith("send.sh", StringComparison.Ordinal))
                : call.Arguments.Take(3).SequenceEqual(["agent", "prompt", "wH:p2"]));
        Assert.Equal(payload, mode == SessionLayerMode.Agmsg ? delivery.Arguments[^1] : delivery.Arguments[3]);
    }

    [Fact]
    public void Delegate_BelowCopilotInlinePayloadAdvisoryThreshold_HasNoWarning_G618()
    {
        workspace.SetMode(SessionLayerMode.HerdrOnly);
        NotifyCommand.ProcessRunnerFactory = SuccessfulRunner;

        var (exitCode, result) = workspace.Run(DelegateArgs());

        Assert.Equal(0, exitCode);
        Assert.True(result.GetProperty("delivered").GetBoolean());
        Assert.False(result.TryGetProperty("inline_payload_warning", out _));
    }

    [Fact]
    public void Delegate_AboveCopilotInlinePayloadAdvisoryThreshold_WarnsWithoutChangingDelivery_G618()
    {
        workspace.SetMode(SessionLayerMode.HerdrOnly);
        var runner = SuccessfulRunner();
        NotifyCommand.ProcessRunnerFactory = () => runner;
        var args = DelegateArgs();
        args[Array.IndexOf(args, "--objective") + 1] = new string('x', 5_000);

        var (exitCode, result) = workspace.Run(args);

        Assert.Equal(0, exitCode);
        Assert.True(result.GetProperty("delivered").GetBoolean());
        var warning = result.GetProperty("inline_payload_warning");
        Assert.Equal("copilot-autopilot-observed-paste-risk", warning.GetProperty("profile").GetString());
        Assert.Equal(result.GetProperty("payload").GetString()!.Length, warning.GetProperty("payload_chars").GetInt32());
        Assert.Equal(4096, warning.GetProperty("threshold_chars").GetInt32());
        Assert.Contains("review-context.md", warning.GetProperty("remedy").GetString(), StringComparison.Ordinal);
        Assert.Contains(runner.Calls, call =>
            call.Arguments.Take(3).SequenceEqual(["agent", "prompt", "wH:p2"]));

        var markdownArgs = DelegateArgs();
        markdownArgs[Array.IndexOf(markdownArgs, "--objective") + 1] = new string('x', 5_000);
        markdownArgs[^1] = "markdown";
        var (markdownExitCode, markdown) = workspace.RunRaw(markdownArgs);

        Assert.Equal(0, markdownExitCode);
        Assert.Contains("inline payload warning", markdown, StringComparison.Ordinal);
        Assert.Contains("size=", markdown, StringComparison.Ordinal);
        Assert.Contains("threshold=4096", markdown, StringComparison.Ordinal);
        Assert.Contains("remedy:", markdown, StringComparison.Ordinal);
    }

    [Fact]
    public void UnrecordedMode_IsConfigurationIncompleteBeforeDefaultAgmsgTransport_G594()
    {
        var runner = SuccessfulRunner();
        NotifyCommand.ProcessRunnerFactory = () => runner;

        var (exitCode, result) = workspace.Run(DelegateArgs());

        Assert.Equal(1, exitCode);
        Assert.Equal(SessionLayerMode.Agmsg, result.GetProperty("mode").GetString());
        Assert.Equal("default", result.GetProperty("mode_source").GetString());
        Assert.Equal("session-layer-mode-unrecorded", result.GetProperty("cause").GetString());
        Assert.Contains("session-layer set --domain intent-cli --team intent-cli-dev", result.GetProperty("summary").GetString(), StringComparison.Ordinal);
        Assert.Empty(runner.Calls);
    }

    [Fact]
    public void AgmsgDryRun_ResolvesItsTeamRosterWithoutSendingOrStartingHerdr_G588()
    {
        workspace.SetMode(SessionLayerMode.Agmsg);
        var runner = SuccessfulRunner();
        NotifyCommand.ProcessRunnerFactory = () => runner;
        var args = DelegateArgs();
        args[^3] = "--dry-run";

        var (exitCode, result) = workspace.Run(args);

        Assert.Equal(0, exitCode);
        Assert.Equal(SessionLayerMode.Agmsg, result.GetProperty("mode").GetString());
        Assert.False(result.GetProperty("delivered").GetBoolean());
        Assert.Contains(runner.Calls, call =>
            call.Arguments.Any(argument => argument.EndsWith("team.sh", StringComparison.Ordinal)));
        Assert.DoesNotContain(runner.Calls, call =>
            call.Arguments.Any(argument => argument.EndsWith("send.sh", StringComparison.Ordinal))
            || call.Arguments.Take(2).SequenceEqual(["agent", "list"])
            || call.Arguments.Take(2).SequenceEqual(["agent", "prompt"]));
    }

    [Theory]
    [InlineData(SessionLayerMode.Agmsg)]
    [InlineData(SessionLayerMode.HerdrOnly)]
    public void UnknownRole_FailsBeforeDelivery_InBothModes_G578(string mode)
    {
        workspace.SetMode(mode);
        var runner = mode == SessionLayerMode.Agmsg
            ? Runner((_, arguments) => arguments[0].EndsWith("team.sh", StringComparison.Ordinal)
                ? Success(AgmsgRoster(withImplementation: false))
                : Success())
            : Runner((_, arguments) => arguments.SequenceEqual(["agent", "list"])
                ? Success(HerdrRoster(withImplementation: false))
                : Success());
        NotifyCommand.ProcessRunnerFactory = () => runner;

        var (exitCode, result) = workspace.Run(DelegateArgs());

        Assert.Equal(1, exitCode);
        Assert.Equal(
            mode == SessionLayerMode.HerdrOnly ? "pane-absent" : "unknown-role",
            result.GetProperty("cause").GetString());
        Assert.False(result.GetProperty("delivered").GetBoolean());
        Assert.DoesNotContain(runner.Calls, call =>
            call.Arguments.Any(argument => argument.EndsWith("send.sh", StringComparison.Ordinal))
            || call.Arguments.Take(2).SequenceEqual(["agent", "prompt"]));
    }

    [Theory]
    [InlineData(SessionLayerMode.Agmsg)]
    [InlineData(SessionLayerMode.HerdrOnly)]
    public void TransportFailure_IsNamedAndFailsClosed_InBothModes_G578(string mode)
    {
        workspace.SetMode(mode);
        var runner = mode == SessionLayerMode.Agmsg
            ? Runner((_, arguments) => arguments[0].EndsWith("team.sh", StringComparison.Ordinal)
                ? Success(AgmsgRoster())
                : Failure("receiver unavailable"))
            : Runner((_, arguments) => arguments.SequenceEqual(["agent", "list"])
                ? Success(HerdrRoster())
                : Failure("prompt transport broke"));
        NotifyCommand.ProcessRunnerFactory = () => runner;

        var (exitCode, result) = workspace.Run(DelegateArgs());

        Assert.Equal(1, exitCode);
        Assert.Equal("transport-failure", result.GetProperty("cause").GetString());
        Assert.False(result.GetProperty("delivered").GetBoolean());
    }

    [Fact]
    public void AgmsgRosterLookupFailure_IsReceiverMissing_G578()
    {
        workspace.SetMode(SessionLayerMode.Agmsg);
        var runner = Runner((_, _) => Failure("team receiver missing"));
        NotifyCommand.ProcessRunnerFactory = () => runner;

        var (exitCode, result) = workspace.Run(DelegateArgs());

        Assert.Equal(1, exitCode);
        Assert.Equal("receiver-missing", result.GetProperty("cause").GetString());
    }

    [Theory]
    [InlineData("pane-absent")]
    [InlineData("agent-not-running")]
    public void HerdrRoleStateFailures_AreNamedBeforePrompt_G578(string expectedCause)
    {
        workspace.SetMode(SessionLayerMode.HerdrOnly);
        var roster = expectedCause == "pane-absent"
            ? HerdrRoster(implementationPane: null)
            : HerdrRoster(implementationRunning: false);
        var runner = Runner((_, arguments) => arguments.SequenceEqual(["agent", "list"])
            ? Success(roster)
            : Success());
        NotifyCommand.ProcessRunnerFactory = () => runner;

        var (exitCode, result) = workspace.Run(DelegateArgs());

        Assert.Equal(1, exitCode);
        Assert.Equal(expectedCause, result.GetProperty("cause").GetString());
        Assert.DoesNotContain(runner.Calls, call => call.Arguments.Take(2).SequenceEqual(["agent", "prompt"]));
    }

    [Theory]
    [InlineData(SessionLayerMode.Agmsg)]
    [InlineData(SessionLayerMode.HerdrOnly)]
    public void Report_UsesTheRecordedTransport_WithStructuredOutcome_G578(string mode)
    {
        workspace.SetMode(mode);
        var runner = SuccessfulRunner();
        NotifyCommand.ProcessRunnerFactory = () => runner;

        var (exitCode, result) = workspace.Run([
            "notify", "report", "--domain", NotifyWorkspace.Domain, "--team", NotifyWorkspace.Team,
            "--from", "implementation", "--to", "orchestration", "--task-id", "G578-demo",
            "--status", "completed", "--artifact", "https://example.test/pr/1",
            "--summary", "notify surface implemented", "--write", "--format", "json",
        ]);

        Assert.Equal(0, exitCode);
        Assert.Equal(mode, result.GetProperty("mode").GetString());
        Assert.True(result.GetProperty("delivered").GetBoolean());
        using var payload = JsonDocument.Parse(result.GetProperty("payload").GetString()!);
        Assert.Equal("completed", payload.RootElement.GetProperty("status").GetString());
        Assert.Equal("https://example.test/pr/1", payload.RootElement.GetProperty("artifact").GetString());
    }

    [Fact]
    public void Report_ResolvesTheRecordedModeFromDelegationRoutingData_OutsideTheHostCwd_G578()
    {
        workspace.SetMode(SessionLayerMode.HerdrOnly);
        var runner = SuccessfulRunner();
        NotifyCommand.ProcessRunnerFactory = () => runner;
        var childRoot = Directory.CreateTempSubdirectory("notify-child-g578-").FullName;
        try
        {
            var childContext = workspace.CreateContext(childRoot);
            using var writer = new StringWriter();

            var exitCode = CommandRouter.Execute([
                "notify", "report", "--domain", NotifyWorkspace.Domain, "--team", NotifyWorkspace.Team,
                "--from", "implementation", "--to", "orchestration", "--task-id", "G578-demo",
                "--status", "completed", "--artifact", "https://example.test/pr/1",
                "--summary", "reported from child cwd", "--routing-root", workspace.RootPath,
                "--write", "--format", "json",
            ], childContext, writer);

            using var result = JsonDocument.Parse(writer.ToString());
            Assert.Equal(0, exitCode);
            Assert.Equal(SessionLayerMode.HerdrOnly, result.RootElement.GetProperty("mode").GetString());
            Assert.Equal(workspace.RootPath, result.RootElement.GetProperty("routing_root").GetString());
            Assert.Contains(runner.Calls, call =>
                call.Arguments.Take(3).SequenceEqual(["agent", "prompt", "wH:p1"]));
        }
        finally
        {
            Directory.Delete(childRoot, recursive: true);
        }
    }

    [Theory]
    [InlineData(SessionLayerMode.Agmsg)]
    [InlineData(SessionLayerMode.HerdrOnly)]
    public void Escalate_AppendsTheExistingSixFieldEventSchema_InEitherMode_G578(string mode)
    {
        workspace.SetMode(mode);
        NotifyCommand.ProcessRunnerFactory = () => throw new InvalidOperationException("escalate must not start a transport");

        var (exitCode, result) = workspace.Run([
            "notify", "escalate", "--domain", NotifyWorkspace.Domain, "--team", NotifyWorkspace.Team,
            "--from", "implementation", "--task-id", "G578-demo", "--artifact", "notes/blocker.md",
            "--summary", "needs   a\n design decision", "--write", "--format", "json",
        ]);

        Assert.Equal(0, exitCode);
        Assert.True(result.GetProperty("event_appended").GetBoolean());
        Assert.False(result.GetProperty("delivered").GetBoolean());
        var lines = File.ReadAllLines(workspace.EventPath);
        Assert.Single(lines);
        using var document = JsonDocument.Parse(lines[0]);
        var root = document.RootElement;
        Assert.Equal(
            ["timestamp", "team", "kind", "unit", "summary", "artifact"],
            root.EnumerateObject().Select(property => property.Name));
        Assert.Equal(FixedNow, root.GetProperty("timestamp").GetDateTimeOffset());
        Assert.Equal(NotifyWorkspace.Team, root.GetProperty("team").GetString());
        Assert.Equal("escalation", root.GetProperty("kind").GetString());
        Assert.Equal("G578-demo", root.GetProperty("unit").GetString());
        Assert.Equal("needs a design decision", root.GetProperty("summary").GetString());
        Assert.Equal("notes/blocker.md", root.GetProperty("artifact").GetString());
    }

    [Fact]
    public void Escalate_RejectsPathLikeTeam_WithoutWriting_G578()
    {
        var args = new[]
        {
            "notify", "escalate", "--domain", NotifyWorkspace.Domain, "--team", "../escape",
            "--from", "implementation", "--task-id", "G578-demo", "--artifact", "notes/blocker.md",
            "--summary", "needs design", "--write", "--format", "json",
        };

        using var writer = new StringWriter();
        var exitCode = CommandRouter.Execute(args, workspace.Context, writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("must contain only", writer.ToString(), StringComparison.Ordinal);
        Assert.False(Directory.Exists(Path.Combine(workspace.RootPath, ".intent-cli", "events")));
    }

    [Theory]
    [InlineData("en")]
    [InlineData("ja")]
    public void OrchestrationDocs_TeachOneCanonicalNotifyVocabulary_G578(string language)
    {
        var path = Path.Combine(
            RepoVersionPolicySource.RepoRoot(),
            "docs",
            language,
            "12-agent-message-orchestration.md");
        var content = File.ReadAllText(path);

        Assert.Contains("intent-cli notify delegate", content, StringComparison.Ordinal);
        Assert.Contains("intent-cli notify report", content, StringComparison.Ordinal);
        Assert.Contains("intent-cli notify escalate", content, StringComparison.Ordinal);
        Assert.Contains("--routing-root", content, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "Send one structured block with `herdr agent prompt",
            content,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "`herdr agent prompt <logical-role> <task-block>` で",
            content,
            StringComparison.Ordinal);
    }

    [Fact]
    public void RuntimeGuides_RequireCanonicalNotifyAndEmbeddedWakeBack_G578()
    {
        var herdrGuide = HerdrOnlyOperatingGuide.RenderMarkdown([]);
        Assert.Contains("intent-cli notify delegate", herdrGuide, StringComparison.Ordinal);
        Assert.Contains("intent-cli notify report", herdrGuide, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "Submit one structured task block with `herdr agent prompt",
            herdrGuide,
            StringComparison.Ordinal);

        using var writer = new StringWriter();
        Assert.Equal(0, GuideOrchestratorThreadCommand.Execute(
            workspace.Context,
            ["--domain", NotifyWorkspace.Domain, "--target-repo", "J-Tech-Japan/intent-system", "--agent", "codex", "--format", "json"],
            writer));
        using var document = JsonDocument.Parse(writer.ToString());
        var prompts = document.RootElement.GetProperty("threads").EnumerateArray()
            .ToDictionary(item => item.GetProperty("role").GetString()!, item => item.GetProperty("prompt").GetString()!);
        foreach (var role in new[] { "implementation", "review" })
        {
            Assert.Contains("intent-cli notify report", prompts[role], StringComparison.Ordinal);
            Assert.Contains("REQUIRED FINAL STEP", prompts[role], StringComparison.Ordinal);
            Assert.Contains("Never hand-write", prompts[role], StringComparison.Ordinal);
            Assert.DoesNotContain("structured agmsg reply", prompts[role], StringComparison.OrdinalIgnoreCase);
        }
    }

    private FakeNotifyProcessRunner SuccessfulRunner() => Runner((_, arguments) =>
    {
        if (arguments.SequenceEqual(["agent", "list"]))
        {
            return Success(HerdrRoster());
        }

        if (arguments.Count > 0 && arguments[0].EndsWith("team.sh", StringComparison.Ordinal))
        {
            return Success(AgmsgRoster());
        }

        return Success();
    });

    private static FakeNotifyProcessRunner Runner(
        Func<string, IReadOnlyList<string>, NotifyProcessResult> handler) => new(handler);

    private static NotifyProcessResult Success(string output = "") => new(0, output, "");

    private static NotifyProcessResult Failure(string error) => new(1, "", error);

    private static string AgmsgRoster(bool withImplementation = true) =>
        "Team: intent-cli-dev\n"
        + "  orchestration (claude) — /work/orchestration\n"
        + (withImplementation ? "  implementation (codex) — /work/implementation\n" : string.Empty)
        + "  review (codex) — /work/review\n";

    private static string HerdrRoster(
        bool withImplementation = true,
        string? implementationPane = "wH:p2",
        bool implementationRunning = true)
    {
        var agents = new List<object>
        {
            HerdrAgent("orchestration", "wH:p1", running: true),
            HerdrAgent("review", "wH:p3", running: true),
        };
        if (withImplementation)
        {
            agents.Add(HerdrAgent("implementation", implementationPane, implementationRunning));
        }

        return JsonSerializer.Serialize(new { result = new { agents } });
    }

    private static object HerdrAgent(string name, string? paneId, bool running) => new
    {
        name,
        workspace_id = "wH",
        pane_id = paneId,
        agent = running ? "codex" : null,
        agent_session = running ? new { id = name } : null,
        agent_status = running ? "idle" : "unknown",
        interactive_ready = running,
    };

    private static string[] DelegateArgs() =>
    [
        "notify", "delegate", "--domain", NotifyWorkspace.Domain, "--team", NotifyWorkspace.Team,
        "--from", "orchestration", "--to", "implementation", "--report-to", "orchestration",
        "--task-id", "G578-demo", "--objective", "Implement the notification contract",
        "--input", "issue #1259", "--expected-artifact", "draft PR URL", "--result-nonce", "demo-nonce",
        "--write", "--format", "json",
    ];

    private sealed class FakeNotifyProcessRunner(
        Func<string, IReadOnlyList<string>, NotifyProcessResult> handler) : INotifyProcessRunner
    {
        public List<(string FileName, IReadOnlyList<string> Arguments)> Calls { get; } = [];

        public NotifyProcessResult Run(string fileName, IReadOnlyList<string> arguments)
        {
            Calls.Add((fileName, arguments.ToArray()));
            return handler(fileName, arguments);
        }
    }

    private sealed class NotifyWorkspace : IDisposable
    {
        public const string Domain = "intent-cli";
        public const string Team = "intent-cli-dev";

        public NotifyWorkspace()
        {
            RootPath = Directory.CreateTempSubdirectory("notify-g578-").FullName;
            Directory.CreateDirectory(Path.Combine(RootPath, ".intent-cli"));
            WriteTopology();
            AgmsgScriptsPath = Path.Combine(RootPath, "agmsg-scripts");
            Directory.CreateDirectory(AgmsgScriptsPath);
            File.WriteAllText(Path.Combine(AgmsgScriptsPath, "team.sh"), "fixture");
            File.WriteAllText(Path.Combine(AgmsgScriptsPath, "send.sh"), "fixture");
            Context = CreateContext(RootPath);
        }

        public CliContext CreateContext(string repoRoot) => new()
        {
            RepoRoot = repoRoot,
            Config = new CliConfig
            {
                Project = new ProjectConfig
                {
                    Domain = Domain,
                    ArtifactRoot = ".intent-cli",
                },
            },
        };

        public string RootPath { get; }
        public string AgmsgScriptsPath { get; }
        public string EventPath => Path.Combine(RootPath, ".intent-cli", "events", $"{Team}.jsonl");
        public CliContext Context { get; }

        private void WriteTopology()
        {
            var topology = new
            {
                team = Team,
                workspace_id = "wH",
                roles = new Dictionary<string, object>
                {
                    ["design"] = new
                    {
                        resident = "external",
                        frontend = "claude-app",
                        reader = $".intent-cli/events/{Team}.jsonl",
                    },
                    ["orchestration"] = new
                    {
                        resident = "herdr",
                        workspace_id = "wH",
                        pane_id = "wH:p1",
                    },
                    ["implementation"] = new
                    {
                        resident = "herdr",
                        kind = "copilot",
                        workspace_id = "wH",
                        pane_id = "wH:p2",
                    },
                    ["review"] = new
                    {
                        resident = "herdr",
                        workspace_id = "wH",
                        pane_id = "wH:p3",
                    },
                },
            };
            File.WriteAllText(
                Path.Combine(RootPath, NotifyRoleTopologyStore.RelativePath.Replace('/', Path.DirectorySeparatorChar)),
                JsonSerializer.Serialize(topology));
        }

        public void SetMode(string mode)
        {
            var topologyPath = Path.Combine(
                RootPath,
                NotifyRoleTopologyStore.RelativePath.Replace('/', Path.DirectorySeparatorChar));
            if (string.Equals(mode, SessionLayerMode.Agmsg, StringComparison.Ordinal))
            {
                File.Delete(topologyPath);
            }
            else if (!File.Exists(topologyPath))
            {
                WriteTopology();
            }

            using var writer = new StringWriter();
            var exitCode = SessionLayerCommand.ExecuteSet(
                Context,
                ["--domain", Domain, "--team", Team, "--mode", mode, "--write", "--format", "json"],
                writer);
            Assert.True(exitCode == 0, writer.ToString());
        }

        public (int ExitCode, JsonElement Result) Run(string[] args)
        {
            var (exitCode, output) = RunRaw(args);
            return (exitCode, JsonDocument.Parse(output).RootElement.Clone());
        }

        public (int ExitCode, string Output) RunRaw(string[] args)
        {
            using var writer = new StringWriter();
            var exitCode = CommandRouter.Execute(args, Context, writer);
            return (exitCode, writer.ToString());
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
