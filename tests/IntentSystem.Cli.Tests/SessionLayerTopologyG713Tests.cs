using System.Text.Json;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

/// <summary>
/// G713: the live pane-label check is advisory and the rendered provisioning
/// guide keeps the herdr-owned label action beside topology recording.
/// </summary>
[Collection("WorkerNextActionSharedState")]
public sealed class SessionLayerTopologyG713Tests : IDisposable
{
    private const string Domain = "intent-cli";
    private const string Team = "intent-cli-dev";
    private const string WorkspaceId = "wG713";

    private readonly TopologyWorkspace workspace = new();

    [Fact]
    public void LiveValidate_MissingLabelIsInformational_AndClearsAfterLabelAppears_G713()
    {
        Assert.Equal(0, workspace.Run(HerdrRecord("wG713:p1")).ExitCode);
        workspace.RecordCurrentSeatPreflight("orchestration");
        var runner = new FakeProcessRunner(PaneList("wG713:p1", label: null));
        NotifyCommand.ProcessRunnerFactory = () => runner;

        var before = workspace.Run(ValidateArguments());

        Assert.Equal(0, before.ExitCode);
        Assert.True(before.Result.GetProperty("valid").GetBoolean());
        var beforeFinding = Assert.Single(
            before.Result.GetProperty("findings").EnumerateArray(),
            finding => finding.GetProperty("field").GetString() == "pane_label");
        Assert.Equal("orchestration", beforeFinding.GetProperty("role").GetString());
        Assert.Equal("pane_label", beforeFinding.GetProperty("field").GetString());
        Assert.Equal("pane-label-missing", beforeFinding.GetProperty("cause").GetString());
        Assert.True(beforeFinding.GetProperty("is_informational").GetBoolean());
        var message = beforeFinding.GetProperty("message").GetString();
        Assert.Contains("wG713:p1", message, StringComparison.Ordinal);
        Assert.Contains("herdr pane rename wG713:p1 orchestration", message, StringComparison.Ordinal);

        runner.Output = PaneList("wG713:p1", label: "orchestration");
        var after = workspace.Run(ValidateArguments());

        Assert.Equal(0, after.ExitCode);
        Assert.True(after.Result.GetProperty("valid").GetBoolean());
        var afterFindings = after.Result.GetProperty("findings").EnumerateArray().ToArray();
        Assert.DoesNotContain(afterFindings, finding => finding.GetProperty("field").GetString() == "pane_label");
        Assert.Contains(afterFindings, finding =>
            finding.GetProperty("cause").GetString() == NotifyRoleTopologyStore.HostStateRoleMissingCause);
        Assert.Equal(2, runner.Calls.Count);
        Assert.All(runner.Calls, call =>
        {
            Assert.Equal(new[] { "pane", "list", "--workspace", WorkspaceId }, call.Arguments);
            Assert.DoesNotContain("rename", call.Arguments);
        });
    }

    [Fact]
    public void LiveValidate_DoesNotChangeRecordedTopologyOrSetHerdrLabel_G713()
    {
        Assert.Equal(0, workspace.Run(HerdrRecord("wG713:p2")).ExitCode);
        workspace.RecordCurrentSeatPreflight("orchestration");
        var before = File.ReadAllText(workspace.TopologyPath);
        var runner = new FakeProcessRunner(PaneList("wG713:p2", label: null));
        NotifyCommand.ProcessRunnerFactory = () => runner;

        var (exitCode, result) = workspace.Run(ValidateArguments());

        Assert.Equal(0, exitCode);
        Assert.True(result.GetProperty("valid").GetBoolean());
        Assert.Equal(before, File.ReadAllText(workspace.TopologyPath));
        Assert.DoesNotContain(runner.Calls, call => call.Arguments.Contains("rename"));
    }

    [Fact]
    public void HerdrOnlyGuide_PlacesHumanFacingLabelCommandBesideRecordStep_G713()
    {
        var markdown = workspace.RenderGuide();
        var record = markdown.IndexOf(
            "intent-cli session-layer topology record --domain <domain> --team <team>",
            StringComparison.Ordinal);
        var rename = markdown.IndexOf(
            "herdr pane rename <pane-id> <logical-role>",
            record,
            StringComparison.Ordinal);
        var validate = markdown.IndexOf(
            "intent-cli session-layer topology validate --domain <domain> --team <team> --live --format json",
            rename,
            StringComparison.Ordinal);

        Assert.True(record >= 0);
        Assert.True(rename > record);
        Assert.True(validate > rename);
        Assert.Contains("so the operator can identify the pane", markdown, StringComparison.Ordinal);
        Assert.Contains("never calls `herdr pane rename` or sets labels", markdown, StringComparison.Ordinal);
    }

    [Fact]
    public void BootstrapGuide_PlacesPaneLabelCommandBesideTopologyRecord_G713()
    {
        using var writer = new StringWriter();
        var exitCode = GuideBootstrapCommand.Execute(
            workspace.Context,
            [
                "--domain", Domain,
                "--team", Team,
                "--routing-root", workspace.RootPath,
                "--format", "json",
            ],
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var step = document.RootElement.GetProperty("steps")[2];
        var commands = step.GetProperty("emitted_commands").EnumerateArray()
            .Select(command => command.GetString()!)
            .ToArray();
        var record = Array.FindIndex(commands, command => command.Contains("topology record", StringComparison.Ordinal));
        var rename = Array.FindIndex(commands, command => command.Contains("herdr pane rename", StringComparison.Ordinal));
        var validate = Array.FindIndex(commands, command => command.Contains("topology validate", StringComparison.Ordinal));

        Assert.True(record >= 0);
        Assert.Equal(record + 1, rename);
        Assert.Equal(rename + 1, validate);
        Assert.Contains("human can identify", step.GetProperty("instruction").GetString()!, StringComparison.Ordinal);
    }

    private static string[] ValidateArguments() =>
    [
        "session-layer", "topology", "validate",
        "--domain", Domain,
        "--team", Team,
        "--live",
        "--format", "json",
    ];

    private static string[] HerdrRecord(string paneId) =>
    [
        "session-layer", "topology", "record",
        "--domain", Domain,
        "--team", Team,
        "--role", "orchestration",
        "--resident", "herdr",
        "--workspace-id", WorkspaceId,
        "--pane-id", paneId,
        "--cwd", "/machine-local",
        "--kind", "codex",
        "--write", "--format", "json",
    ];

    private static string PaneList(string paneId, string? label) => JsonSerializer.Serialize(new
    {
        result = new
        {
            panes = new[]
            {
                new
                {
                    workspace_id = WorkspaceId,
                    pane_id = paneId,
                    label,
                },
            },
        },
    });

    public void Dispose()
    {
        NotifyCommand.ProcessRunnerFactory = null;
        NotifyCommand.HerdrExecutableFactory = null;
        workspace.Dispose();
    }

    private sealed class FakeProcessRunner(string output) : INotifyProcessRunner
    {
        public string Output { get; set; } = output;

        public List<Invocation> Calls { get; } = [];

        public NotifyProcessResult Run(string fileName, IReadOnlyList<string> arguments)
        {
            Calls.Add(new Invocation(fileName, arguments.ToArray()));
            return new NotifyProcessResult(0, Output, string.Empty);
        }
    }

    private sealed record Invocation(string FileName, IReadOnlyList<string> Arguments);

    private sealed class TopologyWorkspace : IDisposable
    {
        public TopologyWorkspace()
        {
            RootPath = Directory.CreateTempSubdirectory("session-layer-topology-g713-").FullName;
            Directory.CreateDirectory(Path.Combine(RootPath, ".intent-cli"));
            Context = new CliContext
            {
                RepoRoot = RootPath,
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
        }

        public string RootPath { get; }
        public CliContext Context { get; }
        public string TopologyPath => NotifyRoleTopologyStore.ResolvePath(RootPath, Domain, Team);

        public void RecordCurrentSeatPreflight(string role)
        {
            Assert.True(SessionLayerSeatPreflightStore.Append(RootPath, new SessionLayerSeatPreflightRecord
            {
                Domain = Domain,
                Team = Team,
                Role = role,
                ObservedAt = DateTimeOffset.UtcNow,
                LaunchAt = DateTimeOffset.UtcNow.AddMinutes(-1),
                Passed = true,
                RuntimeFamily = "unmarked",
                Probes = [],
            }).Applied);
        }

        public (int ExitCode, JsonElement Result) Run(params string[] args)
        {
            using var writer = new StringWriter();
            var exitCode = CommandRouter.Execute(args, Context, writer);
            return (exitCode, JsonDocument.Parse(writer.ToString()).RootElement.Clone());
        }

        public string RenderGuide()
        {
            var modePath = SessionLayerModeStore.ResolvePath(RootPath);
            Directory.CreateDirectory(Path.GetDirectoryName(modePath)!);
            File.WriteAllText(
                modePath,
                $$"""
                {
                  "schema_version": "1",
                  "entries": [
                    {
                      "domain": "{{Domain}}",
                      "team": "{{Team}}",
                      "mode": "herdr-only",
                      "updated_at": "2026-08-02T12:00:00+00:00",
                      "transitions": [
                        { "from": "agmsg", "to": "herdr-only", "at": "2026-08-02T12:00:00+00:00" }
                      ]
                    }
                  ]
                }
                """);

            using var writer = new StringWriter();
            var exitCode = CommandRouter.Execute(
                [
                    "guide", "orchestrator-thread", "--domain", Domain,
                    "--target-repo", "J-Tech-Japan/intent-system", "--agent", "codex",
                    "--team", Team, "--format", "markdown",
                ],
                Context,
                writer);

            Assert.Equal(0, exitCode);
            return writer.ToString();
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
