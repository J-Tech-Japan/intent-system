using System.Text.Json;
using System.Text.Json.Nodes;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

[Collection("WorkerNextActionSharedState")]
public sealed class SessionLayerInspectG790Tests : IDisposable
{
    private const string Domain = "intent-cli";
    private const string Team = "g790-team";
    private const string WorkspaceId = "wG790";
    private const string DesignPane = WorkspaceId + ":p1";
    private const string ImplementationPane = WorkspaceId + ":p2";
    private readonly string root = Directory.CreateTempSubdirectory("session-layer-inspect-g790-").FullName;

    public void Dispose()
    {
        NotifyCommand.ProcessRunnerFactory = null;
        NotifyCommand.HerdrExecutableFactory = null;
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Inspect_ReportsRecordedFieldsAndLiveHerdrState_WhileExternalLiveStateIsAbsent_G790()
    {
        WriteTopology();
        var runner = new FixtureRunner(AgentListJson(), "unused");
        NotifyCommand.ProcessRunnerFactory = () => runner;
        NotifyCommand.HerdrExecutableFactory = () => "/absolute/fake-herdr";

        var (exitCode, result) = Run(
            "session-layer", "inspect",
            "--domain", Domain,
            "--team", Team,
            "--format", "json");

        Assert.Equal(0, exitCode);
        Assert.True(result.GetProperty("topology_available").GetBoolean());
        Assert.True(result.GetProperty("live_query_attempted").GetBoolean());
        var design = Role(result, "design");
        Assert.Equal("herdr", design.GetProperty("resident").GetString());
        Assert.Equal("codex", design.GetProperty("kind").GetString());
        Assert.False(design.TryGetProperty("frontend", out _));
        var live = design.GetProperty("live");
        Assert.Equal(WorkspaceId, live.GetProperty("workspace_id").GetString());
        Assert.Equal(DesignPane, live.GetProperty("pane_id").GetString());
        Assert.Equal("design-seat", live.GetProperty("agent").GetString());
        Assert.True(live.GetProperty("agent_running").GetBoolean());
        Assert.Equal("working", live.GetProperty("agent_status").GetString());

        var external = Role(result, "review");
        Assert.Equal("external", external.GetProperty("resident").GetString());
        Assert.Equal("review-web", external.GetProperty("frontend").GetString());
        Assert.False(external.TryGetProperty("live", out _));
        Assert.Collection(
            runner.Calls,
            call =>
            {
                Assert.Equal("/absolute/fake-herdr", call.FileName);
                Assert.Equal(["agent", "list"], call.Arguments);
            });
        AssertNoMutationOrFocusCommands(runner);
    }

    [Fact]
    public void Inspect_ReportsUnavailableHerdrStateWithExitCodeZero_AndRetainsRecordedFields_G790()
    {
        WriteTopology();
        var runner = new FixtureRunner(
            output: "",
            tailOutput: "",
            handler: (_, _) => throw new InvalidOperationException("fake herdr unavailable"));
        NotifyCommand.ProcessRunnerFactory = () => runner;
        NotifyCommand.HerdrExecutableFactory = () => "/absolute/fake-herdr";

        var (exitCode, result) = Run(
            "session-layer", "inspect",
            "--domain", Domain,
            "--team", Team,
            "--format", "json");

        Assert.Equal(0, exitCode);
        Assert.Contains("unavailable", result.GetProperty("unavailable_reason").GetString()!, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("herdr", Role(result, "design").GetProperty("resident").GetString());
        Assert.Equal("codex", Role(result, "design").GetProperty("kind").GetString());
        Assert.False(Role(result, "design").TryGetProperty("live", out _));
        Assert.False(Role(result, "review").TryGetProperty("live", out _));
        Assert.Collection(
            runner.Calls,
            call =>
            {
                Assert.Equal("/absolute/fake-herdr", call.FileName);
                Assert.Equal(["agent", "list"], call.Arguments);
            });
        AssertNoMutationOrFocusCommands(runner);
    }

    [Fact]
    public void Inspect_TailIsExplicitPaneBoundedAndCapped_G790()
    {
        WriteTopology();
        var output = string.Join('\n', Enumerable.Range(0, 250).Select(index => $"line-{index}"));
        var runner = new FixtureRunner(AgentListJson(), output);
        NotifyCommand.ProcessRunnerFactory = () => runner;
        NotifyCommand.HerdrExecutableFactory = () => "/absolute/fake-herdr";

        var (exitCode, result) = Run(
            "session-layer", "inspect",
            "--domain", Domain,
            "--team", Team,
            "--role", "design",
            "--tail", "999",
            "--format", "json");

        Assert.Equal(0, exitCode);
        Assert.Equal(999, result.GetProperty("tail_requested").GetInt32());
        Assert.Equal(SessionLayerInspectCommand.MaximumTailLines, result.GetProperty("tail_limit").GetInt32());
        var tail = Role(result, "design").GetProperty("tail").EnumerateArray().Select(item => item.GetString()).ToArray();
        Assert.Equal(SessionLayerInspectCommand.MaximumTailLines, tail.Length);
        Assert.Equal("line-50", tail[0]);
        Assert.Equal("line-249", tail[^1]);
        Assert.Collection(
            runner.Calls,
            listCall =>
            {
                Assert.Equal("/absolute/fake-herdr", listCall.FileName);
                Assert.Equal(["agent", "list"], listCall.Arguments);
            },
            tailCall =>
            {
                Assert.Equal("/absolute/fake-herdr", tailCall.FileName);
                Assert.Equal(["pane", "read", "--source", "recent-unwrapped", DesignPane], tailCall.Arguments);
            });
        AssertNoMutationOrFocusCommands(runner);
    }

    [Fact]
    public void Inspect_TailWithoutNamedRole_IsRefusedBeforeAnyRunnerCall_G790()
    {
        WriteTopology();
        var runner = new FixtureRunner(AgentListJson(), "unused");
        NotifyCommand.ProcessRunnerFactory = () => runner;

        using var writer = new StringWriter();
        var exitCode = CommandRouter.Execute(
            [
                "session-layer", "inspect",
                "--domain", Domain,
                "--team", Team,
                "--tail", "2",
            ],
            CreateContext(),
            writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("--tail requires --role", writer.ToString(), StringComparison.Ordinal);
        Assert.Empty(runner.Calls);
    }

    [Fact]
    public void Inspect_MissingTopologyIsAnExitCodeZeroUnavailableObservation_G790()
    {
        NotifyCommand.ProcessRunnerFactory = () => new FixtureRunner(AgentListJson(), "unused");
        using var writer = new StringWriter();
        var exitCode = CommandRouter.Execute(
            ["session-layer", "inspect", "--domain", Domain, "--team", Team, "--format", "json"],
            CreateContext(),
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var result = document.RootElement;
        Assert.False(result.GetProperty("topology_available").GetBoolean());
        Assert.Equal("topology-missing", result.GetProperty("unavailable_reason").GetString());
        Assert.Empty(result.GetProperty("roles").EnumerateArray());
    }

    [Fact]
    public void GuideAndDocumentation_NameInspectBesideTheExistingObservationFallback_G790()
    {
        using var writer = new StringWriter();
        Assert.Equal(
            0,
            GuideDesignThreadCommand.Execute(
                CreateContext(),
                ["--domain", Domain, "--team", Team, "--format", "json"],
                writer));
        using var document = JsonDocument.Parse(writer.ToString());
        var boundary = document.RootElement.GetProperty("observation_boundary");
        Assert.Contains("session-layer inspect", boundary.GetProperty("inspect_route").GetString(), StringComparison.Ordinal);
        Assert.Contains("notify status", boundary.GetProperty("fallback_route").GetString(), StringComparison.Ordinal);

        var en = ReadRepoFile("docs/en/12-agent-message-orchestration.md");
        var ja = ReadRepoFile("docs/ja/12-agent-message-orchestration.md");
        foreach (var doc in new[] { en, ja })
        {
            Assert.Contains("session-layer inspect", doc, StringComparison.Ordinal);
            Assert.Contains("herdr agent list", doc, StringComparison.Ordinal);
            Assert.Contains("200", doc, StringComparison.Ordinal);
            Assert.Contains("notify adjudicate", doc, StringComparison.Ordinal);
        }
    }

    private (int ExitCode, JsonElement Result) Run(params string[] args)
    {
        using var writer = new StringWriter();
        var exitCode = CommandRouter.Execute(args, CreateContext(), writer);
        using var document = JsonDocument.Parse(writer.ToString());
        return (exitCode, document.RootElement.Clone());
    }

    private static JsonElement Role(JsonElement result, string name) =>
        Assert.Single(result.GetProperty("roles").EnumerateArray(), role => role.GetProperty("role").GetString() == name);

    private void WriteTopology()
    {
        var path = NotifyRoleTopologyStore.ResolvePath(root, Domain, Team);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var topology = new JsonObject
        {
            ["domain"] = Domain,
            ["team"] = Team,
            ["workspace_id"] = WorkspaceId,
            ["roles"] = new JsonObject
            {
                ["design"] = new JsonObject
                {
                    ["resident"] = "herdr",
                    ["workspace_id"] = WorkspaceId,
                    ["pane_id"] = DesignPane,
                    ["cwd"] = "/g790/design",
                    ["kind"] = "codex",
                },
                ["implementation"] = new JsonObject
                {
                    ["resident"] = "herdr",
                    ["workspace_id"] = WorkspaceId,
                    ["pane_id"] = ImplementationPane,
                    ["cwd"] = "/g790/implementation",
                    ["kind"] = "claude",
                },
                ["review"] = new JsonObject
                {
                    ["resident"] = "external",
                    ["reader"] = ".intent-cli/events/g790-team.jsonl",
                    ["frontend"] = "review-web",
                },
            },
        };
        File.WriteAllText(path, topology.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }

    private static string AgentListJson() =>
        """
        {
          "result": {
            "agents": [
              {
                "name": "design-seat",
                "workspace_id": "wG790",
                "pane_id": "wG790:p1",
                "agent": "codex",
                "agent_status": "working",
                "agent_session": {},
                "interactive_ready": true,
                "cwd": "/g790/design",
                "state_change_seq": 7,
                "last_state_change_at": "2026-09-03T10:00:00Z"
              },
              {
                "name": "implementation-seat",
                "workspace_id": "wG790",
                "pane_id": "wG790:p2",
                "agent": "claude",
                "agent_status": "idle",
                "agent_session": {},
                "interactive_ready": true,
                "cwd": "/g790/implementation"
              }
            ]
          }
        }
        """;

    private static void AssertNoMutationOrFocusCommands(FixtureRunner runner)
    {
        var forbidden = new[] { "send-keys", "send-text", "prompt", "split", "close", "start", "stop", "kill", "--current" };
        Assert.DoesNotContain(runner.Calls, call => call.Arguments.Any(argument => forbidden.Contains(argument, StringComparer.Ordinal)));
    }

    private CliContext CreateContext() => new()
    {
        RepoRoot = root,
        Config = new CliConfig
        {
            Project = new ProjectConfig
            {
                Domain = Domain,
                ArtifactRoot = ".intent-cli",
                WorktreeRoot = ".intent-cli/worktrees",
            },
        },
    };

    private static string ReadRepoFile(string relativePath)
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            var candidate = Path.Combine(current.FullName, relativePath);
            if (File.Exists(candidate)) return File.ReadAllText(candidate);
            current = current.Parent;
        }
        throw new FileNotFoundException(relativePath);
    }

    private sealed record Call(string FileName, IReadOnlyList<string> Arguments);

    private sealed class FixtureRunner : INotifyProcessRunner
    {
        private readonly string output;
        private readonly string tailOutput;
        private readonly Func<string, IReadOnlyList<string>, NotifyProcessResult>? handler;

        public FixtureRunner(
            string output,
            string tailOutput,
            Func<string, IReadOnlyList<string>, NotifyProcessResult>? handler = null)
        {
            this.output = output;
            this.tailOutput = tailOutput;
            this.handler = handler;
        }

        public List<Call> Calls { get; } = [];

        public NotifyProcessResult Run(string fileName, IReadOnlyList<string> arguments)
        {
            Calls.Add(new Call(fileName, arguments.ToArray()));
            if (handler is not null)
            {
                return handler(fileName, arguments);
            }

            if (arguments.SequenceEqual(["agent", "list"]))
            {
                return new NotifyProcessResult(0, output, string.Empty);
            }

            if (arguments.SequenceEqual(["pane", "read", "--source", "recent-unwrapped", DesignPane]))
            {
                return new NotifyProcessResult(0, tailOutput, string.Empty);
            }

            return new NotifyProcessResult(0, string.Empty, string.Empty);
        }
    }
}
