using System.Text.Json;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

[Collection("WorkerNextActionSharedState")]
public sealed class SessionLayerMarkerG601Tests : IDisposable
{
    private readonly MarkerWorkspace workspace = new();

    public void Dispose()
    {
        NotifyCommand.ProcessRunnerFactory = null;
        NotifyCommand.HerdrExecutableFactory = null;
        AutomationInstalledCliSurfaceProbe.ProbeRunner = null;
        workspace.Dispose();
    }

    [Fact]
    public void Generate_UpdatesOnlyTheExplicitManagedBlock_AndNeverWritesTheRecord_G601()
    {
        workspace.Record("alpha", SessionLayerMode.Agmsg);
        var file = workspace.WriteStartup("before\n" + Placeholder("alpha") + "\nafter\n");
        var recordBefore = File.ReadAllBytes(workspace.ModePath);

        var (exitCode, result) = workspace.Generate("alpha", file);

        Assert.Equal(0, exitCode);
        Assert.True(result.GetProperty("written").GetBoolean());
        var content = File.ReadAllText(file);
        Assert.StartsWith("before\n", content, StringComparison.Ordinal);
        Assert.EndsWith("\nafter\n", content, StringComparison.Ordinal);
        Assert.Contains("domain=\"intent-cli\" team=\"alpha\"", content, StringComparison.Ordinal);
        Assert.Contains("mode=\"agmsg\"", content, StringComparison.Ordinal);
        Assert.Contains("intent-cli session-layer show --domain intent-cli --team alpha", content, StringComparison.Ordinal);
        Assert.Contains("record-hash=\"sha256:", content, StringComparison.Ordinal);
        Assert.Equal(recordBefore, File.ReadAllBytes(workspace.ModePath));
    }

    [Fact]
    public void Generate_TwoTeamsKeepIndependentNonContradictingMarkers_G601()
    {
        workspace.Record("alpha", SessionLayerMode.Agmsg);
        workspace.Record("beta", SessionLayerMode.HerdrOnly);
        var file = workspace.WriteStartup(Placeholder("alpha") + "\n\n" + Placeholder("beta") + "\n");

        Assert.Equal(0, workspace.Generate("alpha", file).ExitCode);
        Assert.Equal(0, workspace.Generate("beta", file).ExitCode);

        var content = File.ReadAllText(file);
        Assert.Contains("team=\"alpha\" mode=\"agmsg\"", content, StringComparison.Ordinal);
        Assert.Contains("team=\"beta\" mode=\"herdr-only\"", content, StringComparison.Ordinal);
        Assert.DoesNotContain("team=\"alpha\" mode=\"herdr-only\"", content, StringComparison.Ordinal);
        Assert.DoesNotContain("team=\"beta\" mode=\"agmsg\"", content, StringComparison.Ordinal);
    }

    [Fact]
    public void Generate_RefusesAbsentOrMalformedMarkers_G601()
    {
        workspace.Record("alpha", SessionLayerMode.Agmsg);
        var file = workspace.WriteStartup("ordinary startup instructions\n");
        var before = File.ReadAllText(file);

        var absent = workspace.Generate("alpha", file);
        Assert.Equal(1, absent.ExitCode);
        Assert.Equal("marker-absent", absent.Result.GetProperty("cause").GetString());
        Assert.Equal(before, File.ReadAllText(file));

        File.WriteAllText(file, "<!-- intent-cli:session-layer-marker:start domain=\"intent-cli\" team=\"alpha\" -->\nnot generated\n<!-- intent-cli:session-layer-marker:end -->\n");
        var malformed = workspace.Generate("alpha", file);
        Assert.Equal(1, malformed.ExitCode);
        Assert.Equal("marker-malformed", malformed.Result.GetProperty("cause").GetString());
    }

    [Fact]
    public void Preflight_StaleMarkerFailsClosedAndNamesFileClaimAndTruth_G601()
    {
        workspace.Record("alpha", SessionLayerMode.HerdrOnly);
        workspace.WriteTopology("alpha");
        var file = workspace.WriteStartup(Placeholder("alpha") + "\n");
        Assert.Equal(0, workspace.Generate("alpha", file).ExitCode);
        workspace.Record("alpha", SessionLayerMode.Agmsg);
        File.Delete(NotifyRoleTopologyStore.ResolvePath(workspace.RootPath, MarkerWorkspace.Domain, "alpha"));

        var result = SessionLayerPreflight.Analyze(workspace.RootPath, MarkerWorkspace.Domain, "alpha");

        Assert.Equal(SessionLayerPreflight.ConfigurationIncomplete, result.Verdict);
        var finding = Assert.Single(Assert.Single(result.Scopes).Findings, item => item.Cause == "marker-drift");
        Assert.Equal("AGENTS.md", finding.Role);
        Assert.Contains("claims mode 'herdr-only'", finding.Message, StringComparison.Ordinal);
        Assert.Contains("canonical record truth is mode 'agmsg'", finding.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Preflight_UnmarkedRecordedTeamIsInformationalAndRemainsReady_G601()
    {
        workspace.Record("alpha", SessionLayerMode.Agmsg);

        var result = SessionLayerPreflight.Analyze(workspace.RootPath, MarkerWorkspace.Domain, "alpha");

        Assert.Equal(SessionLayerPreflight.Ready, result.Verdict);
        var finding = Assert.Single(Assert.Single(result.Scopes).Findings);
        Assert.Equal("marker-not-generated", finding.Cause);
        Assert.Contains("session-layer marker generate --domain intent-cli --team alpha", finding.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void EmptyPlaceholder_IsInformationalAndAutomationDoctorRemainsReady_G601()
    {
        workspace.Record("alpha", SessionLayerMode.Agmsg);
        workspace.WriteStartup(Placeholder("alpha") + "\n");
        workspace.WriteInstalledCliProbeStub();
        AutomationInstalledCliSurfaceProbe.ProbeRunner = (_, _) => new InstalledCliProbeResult(
            1,
            "review-start request-update approved",
            "");

        using var writer = new StringWriter();
        var exitCode = AutomationDoctorCommand.Execute(
            workspace.Context,
            ["--domain", MarkerWorkspace.Domain, "--team", "alpha", "--format", "json"],
            writer);
        var doctor = JsonSerializer.Deserialize<AutomationDoctorResult>(writer.ToString())!;

        Assert.Equal(0, exitCode);
        Assert.Equal("ok", doctor.Status);
        Assert.Equal(SessionLayerPreflight.Ready, doctor.SessionLayerPreflight.Verdict);
        var finding = Assert.Single(Assert.Single(doctor.SessionLayerPreflight.Scopes).Findings);
        Assert.Equal("marker-not-generated", finding.Cause);
        Assert.Equal("AGENTS.md", finding.Role);
    }

    [Fact]
    public void EmptyPlaceholder_DoesNotBlockHerdrNotifyDelivery_G601()
    {
        workspace.Record("alpha", SessionLayerMode.HerdrOnly);
        workspace.WriteTopology("alpha");
        workspace.WriteStartup(Placeholder("alpha") + "\n");
        var runner = new FakeRunner((_, arguments) =>
        {
            if (arguments.SequenceEqual(["agent", "list"])) return Success(HerdrRoster("idle"));
            if (arguments.Take(2).SequenceEqual(["agent", "prompt"])) return Success("working observed");
            if (arguments.Take(2).SequenceEqual(["agent", "wait"])) return Success("settled acknowledgement");
            throw new InvalidOperationException("unexpected transport call");
        });
        NotifyCommand.ProcessRunnerFactory = () => runner;
        NotifyCommand.HerdrExecutableFactory = () => "fake-herdr";

        var (exitCode, result) = workspace.RunNotify("alpha");

        Assert.Equal(0, exitCode);
        Assert.True(result.GetProperty("delivered").GetBoolean());
        Assert.Equal(SessionLayerPreflight.Ready,
            result.GetProperty("session_layer_preflight").GetProperty("verdict").GetString());
        Assert.Contains(
            result.GetProperty("session_layer_preflight").GetProperty("scopes")[0].GetProperty("findings").EnumerateArray(),
            finding => finding.GetProperty("cause").GetString() == "marker-not-generated");
    }

    [Fact]
    public void Preflight_ContentInsidePlaceholderRemainsMalformedAndNotReady_G601()
    {
        workspace.Record("alpha", SessionLayerMode.Agmsg);
        workspace.WriteStartup("<!-- intent-cli:session-layer-marker:start domain=\"intent-cli\" team=\"alpha\" -->\nnot generated\n<!-- intent-cli:session-layer-marker:end -->\n");

        var result = SessionLayerPreflight.Analyze(workspace.RootPath, MarkerWorkspace.Domain, "alpha");

        Assert.Equal(SessionLayerPreflight.ConfigurationIncomplete, result.Verdict);
        Assert.Equal("marker-malformed", Assert.Single(Assert.Single(result.Scopes).Findings).Cause);
    }

    [Fact]
    public void Generate_UnrecordedTeamRefusesAndNamesRecordingCommand_G601()
    {
        var file = workspace.WriteStartup(Placeholder("alpha") + "\n");

        var (exitCode, result) = workspace.Generate("alpha", file);

        Assert.Equal(1, exitCode);
        Assert.Equal("session-layer-mode-unrecorded", result.GetProperty("cause").GetString());
        Assert.Contains("intent-cli session-layer set --domain intent-cli --team alpha", result.GetProperty("recording_command").GetString(), StringComparison.Ordinal);
        Assert.False(File.Exists(workspace.ModePath));
    }

    [Fact]
    public void HerdrGuide_LabelsTheWorkspaceWithoutMakingItModeEvidence_G601()
    {
        var guide = HerdrOnlyOperatingGuide.RenderMarkdown([]);

        Assert.Contains("<team> · herdr-only", guide, StringComparison.Ordinal);
        Assert.Contains("non-authoritative", guide, StringComparison.Ordinal);
        Assert.Contains("session-layer-mode.json", guide, StringComparison.Ordinal);
        Assert.Contains("source of truth", guide, StringComparison.Ordinal);
    }

    private static string Placeholder(string team) =>
        $"<!-- intent-cli:session-layer-marker:start domain=\"intent-cli\" team=\"{team}\" -->\n"
        + "<!-- intent-cli:session-layer-marker:end -->";

    private static NotifyProcessResult Success(string output = "") => new(0, output, "");

    private static string HerdrRoster(string status) => JsonSerializer.Serialize(new
    {
        result = new
        {
            agents = new object[]
            {
                Agent("implementation", "w:p2", "idle"),
                Agent("orchestration", "w:p1", status),
            },
        },
    });

    private static object Agent(string name, string paneId, string status) => new
    {
        name,
        workspace_id = "w",
        pane_id = paneId,
        agent = "codex",
        agent_session = new { id = name },
        agent_status = status,
        interactive_ready = true,
    };

    private sealed class FakeRunner(
        Func<string, IReadOnlyList<string>, NotifyProcessResult> handler) : INotifyProcessRunner
    {
        public NotifyProcessResult Run(string fileName, IReadOnlyList<string> arguments) => handler(fileName, arguments);
    }

    private sealed class MarkerWorkspace : IDisposable
    {
        public const string Domain = "intent-cli";
        public MarkerWorkspace()
        {
            RootPath = Directory.CreateTempSubdirectory("session-layer-marker-g601-").FullName;
            Context = new CliContext
            {
                RepoRoot = RootPath,
                Config = new CliConfig { Project = new ProjectConfig { Domain = Domain, ArtifactRoot = ".intent-cli" } },
            };
        }

        public string RootPath { get; }
        public CliContext Context { get; }
        public string ModePath => SessionLayerModeStore.ResolvePath(RootPath);

        public void WriteInstalledCliProbeStub()
        {
            var path = Path.Combine(
                RootPath,
                ".intent-cli",
                "bin",
                OperatingSystem.IsWindows() ? "intent-cli.exe" : "intent-cli");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, "probe stub");
        }

        public string WriteStartup(string content)
        {
            var path = Path.Combine(RootPath, "AGENTS.md");
            File.WriteAllText(path, content);
            return path;
        }

        public void Record(string team, string mode)
        {
            using var writer = new StringWriter();
            var exitCode = SessionLayerCommand.ExecuteSet(Context,
                ["--domain", Domain, "--team", team, "--mode", mode, "--write", "--format", "json"], writer);
            Assert.True(exitCode == 0, writer.ToString());
        }

        public (int ExitCode, JsonElement Result) Generate(string team, string file)
        {
            using var writer = new StringWriter();
            var exitCode = SessionLayerMarkerCommand.Execute(Context,
                ["generate", "--domain", Domain, "--team", team, "--file", file, "--write", "--format", "json"], writer);
            return (exitCode, JsonDocument.Parse(writer.ToString()).RootElement.Clone());
        }

        public void WriteTopology(string team)
        {
            var path = NotifyRoleTopologyStore.ResolvePath(RootPath, Domain, team);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(new
            {
                domain = Domain,
                team,
                workspace_id = "w",
                roles = new
                {
                    orchestration = new { resident = "herdr", workspace_id = "w", pane_id = "w:p1" },
                    implementation = new { resident = "herdr", workspace_id = "w", pane_id = "w:p2" },
                },
            }));
        }

        private void SeedPending(string team)
        {
            var result = NotifyPendingDelegationStore.WriteDispatch(RootPath, new NotifyPendingDelegation
            {
                Domain = Domain,
                Team = team,
                TaskId = "G601-ac4",
                RecipientRole = "implementation",
                RecipientIdentity = "role=implementation;workspace=w;pane=w:p2",
                ExpectedArtifact = "https://example.test/pr/1306",
                DispatchedAt = DateTimeOffset.UtcNow,
                TransportMode = SessionLayerMode.HerdrOnly,
                Resident = NotifyRecordedRole.HerdrResident,
                WorkspaceId = "w",
                PaneId = "w:p2",
            });
            Assert.True(result.Written, result.Error);
        }

        public (int ExitCode, JsonElement Result) RunNotify(string team)
        {
            SeedPending(team);
            using var writer = new StringWriter();
            var exitCode = CommandRouter.Execute(
                [
                    "notify", "report",
                    "--domain", Domain,
                    "--team", team,
                    "--from", "implementation",
                    "--to", "orchestration",
                    "--task-id", "G601-ac4",
                    "--status", "completed",
                    "--artifact", "https://example.test/pr/1306",
                    "--summary", "empty marker placeholder delivery",
                    "--write",
                    "--format", "json",
                ],
                Context,
                writer);
            return (exitCode, JsonDocument.Parse(writer.ToString()).RootElement.Clone());
        }

        public void Dispose()
        {
            if (Directory.Exists(RootPath)) Directory.Delete(RootPath, recursive: true);
        }
    }
}
