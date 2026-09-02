using System.Text.Json;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

/// <summary>
/// G784: external-role presentation labels are changed only through the
/// confirmed, compare-and-swap update-field surface.
/// </summary>
[Collection("WorkerNextActionSharedState")]
public sealed class SessionLayerTopologyG784Tests
{
    private const string Domain = "intent-cli";

    [Fact]
    public void UpdateField_ExternalLabelsKeepCasDryRunAndOneFieldDiff_G784()
    {
        using var workspace = new TopologyWorkspace("labels");
        workspace.RecordExternal("implementation", "claude-app");
        workspace.RecordHerdr("review");
        var beforeBytes = File.ReadAllBytes(workspace.TopologyPath);
        var beforeTopology = Parse(File.ReadAllText(workspace.TopologyPath));
        var beforeRole = workspace.ReadRole("implementation");

        var frontendDryRun = workspace.RunRaw(UpdateArgs(
            workspace, "implementation", "frontend", "claude-app", "orca", write: false));
        Assert.True(frontendDryRun.ExitCode == 0, frontendDryRun.Output);
        var frontendDryRunResult = Parse(frontendDryRun.Output);
        Assert.Equal("dry-run", frontendDryRunResult.GetProperty("mode").GetString());
        Assert.False(frontendDryRunResult.GetProperty("applied").GetBoolean());
        Assert.True(frontendDryRunResult.GetProperty("changed").GetBoolean());
        Assert.Equal(beforeBytes, File.ReadAllBytes(workspace.TopologyPath));

        var frontendWrite = workspace.RunRaw(UpdateArgs(
            workspace, "implementation", "frontend", "claude-app", "orca", write: true));
        Assert.Equal(0, frontendWrite.ExitCode);
        var frontendWriteResult = Parse(frontendWrite.Output);
        Assert.Equal("write", frontendWriteResult.GetProperty("mode").GetString());
        Assert.True(frontendWriteResult.GetProperty("applied").GetBoolean());
        var afterFrontendRole = workspace.ReadRole("implementation");
        var afterFrontendTopology = Parse(File.ReadAllText(workspace.TopologyPath));
        Assert.Equal("orca", afterFrontendRole.GetProperty("frontend").GetString());
        Assert.Equal(["frontend"], ChangedFields(beforeRole, afterFrontendRole));
        Assert.Equal(beforeTopology.GetProperty("domain").GetRawText(), afterFrontendTopology.GetProperty("domain").GetRawText());
        Assert.Equal(beforeTopology.GetProperty("team").GetRawText(), afterFrontendTopology.GetProperty("team").GetRawText());
        Assert.Equal(beforeTopology.GetProperty("workspace_id").GetRawText(), afterFrontendTopology.GetProperty("workspace_id").GetRawText());
        Assert.Equal(
            beforeTopology.GetProperty("roles").GetProperty("review").GetRawText(),
            afterFrontendTopology.GetProperty("roles").GetProperty("review").GetRawText());

        var wakeSetBefore = workspace.ReadRole("implementation");
        var wakeSet = workspace.RunRaw(UpdateArgs(
            workspace, "implementation", "wake_command", "absent", "wake-sentinel-{task_id}", write: true));
        Assert.Equal(0, wakeSet.ExitCode);
        var afterWakeSetRole = workspace.ReadRole("implementation");
        Assert.Equal("wake-sentinel-{task_id}", afterWakeSetRole.GetProperty("wake_command").GetString());
        Assert.Equal(["wake_command"], ChangedFields(wakeSetBefore, afterWakeSetRole));

        var wakeClearBefore = workspace.ReadRole("implementation");
        var wakeClear = workspace.RunRaw(UpdateArgs(
            workspace, "implementation", "wake_command", "wake-sentinel-{task_id}", "absent", write: true));
        Assert.Equal(0, wakeClear.ExitCode);
        var afterWakeClearRole = workspace.ReadRole("implementation");
        Assert.False(afterWakeClearRole.TryGetProperty("wake_command", out _));
        Assert.Equal(["wake_command"], ChangedFields(wakeClearBefore, afterWakeClearRole));

        Fixture("frontend dry-run JSON", frontendDryRun.Output);
        Fixture("frontend write JSON", frontendWrite.Output);
        Fixture("frontend role before", beforeRole.GetRawText());
        Fixture("frontend role after", afterFrontendRole.GetRawText());
        Fixture("wake set JSON", wakeSet.Output);
        Fixture("wake clear JSON", wakeClear.Output);
    }

    [Fact]
    public void UpdateField_RefusesStaleConfirmationHerdrAndUnregisteredFields_G784()
    {
        using var workspace = new TopologyWorkspace("refusals");
        workspace.RecordExternal("implementation", "claude-app");
        workspace.RecordHerdr("review");
        var before = File.ReadAllBytes(workspace.TopologyPath);

        var wrongAbsent = workspace.RunRaw(UpdateArgs(
            workspace, "implementation", "frontend", "absent", "orca", write: true));
        Assert.Equal(1, wrongAbsent.ExitCode);
        Assert.Contains("records frontend 'claude-app', not stated current value 'absent'", Summary(wrongAbsent.Output), StringComparison.Ordinal);

        var valueWhenAbsent = workspace.RunRaw(UpdateArgs(
            workspace, "implementation", "wake_command", "wake-old", "wake-new", write: true));
        Assert.Equal(1, valueWhenAbsent.ExitCode);
        Assert.Contains("records wake_command 'absent', not stated current value 'wake-old'", Summary(valueWhenAbsent.Output), StringComparison.Ordinal);

        var withoutConfirmation = workspace.RunRaw(RemoveConfirmation(UpdateArgs(
            workspace, "implementation", "frontend", "claude-app", "orca", write: true)));
        Assert.Equal(1, withoutConfirmation.ExitCode);
        Assert.Contains("--confirm-update-field", withoutConfirmation.Output, StringComparison.Ordinal);

        var herdrFrontend = workspace.RunRaw(UpdateArgs(
            workspace, "review", "frontend", "absent", "orca", write: true));
        Assert.Equal(1, herdrFrontend.ExitCode);
        Assert.Contains("external resident", Summary(herdrFrontend.Output), StringComparison.Ordinal);
        Assert.Contains("residence 'herdr'", Summary(herdrFrontend.Output), StringComparison.Ordinal);

        var herdrWake = workspace.RunRaw(UpdateArgs(
            workspace, "review", "wake_command", "absent", "wake-new", write: true));
        Assert.Equal(1, herdrWake.ExitCode);
        Assert.Contains("residence 'herdr'", Summary(herdrWake.Output), StringComparison.Ordinal);

        var unregistered = workspace.RunRaw(UpdateArgs(
            workspace, "implementation", "reader", ".intent-cli/events/old.jsonl", ".intent-cli/events/new.jsonl", write: true));
        Assert.Equal(1, unregistered.ExitCode);
        Assert.Contains("Field 'reader'", Summary(unregistered.Output), StringComparison.Ordinal);
        Assert.Contains("'delivery_method', 'frontend', and 'wake_command'", Summary(unregistered.Output), StringComparison.Ordinal);
        Assert.Equal(before, File.ReadAllBytes(workspace.TopologyPath));

        Fixture("wrong current refusal", wrongAbsent.Output);
        Fixture("value when absent refusal", valueWhenAbsent.Output);
        Fixture("missing confirmation refusal", withoutConfirmation.Output);
        Fixture("herdr frontend refusal", herdrFrontend.Output);
        Fixture("herdr wake refusal", herdrWake.Output);
        Fixture("unregistered field refusal", unregistered.Output);
    }

    [Fact]
    public void Record_ExplainsExternalLabelAndResidenceDifferencesWithoutReplacing_G784()
    {
        using var workspace = new TopologyWorkspace("record-conflict");
        workspace.RecordExternal("implementation", "claude-app", "wake-old");
        var before = File.ReadAllBytes(workspace.TopologyPath);

        var frontendConflict = workspace.RunRaw(workspace.ExternalRecordArgs("implementation", "orca", "wake-old"));
        Assert.Equal(1, frontendConflict.ExitCode);
        var frontendSummary = Parse(frontendConflict.Output).GetProperty("summary").GetString()!;
        Assert.Contains("frontend: recorded 'claude-app', requested 'orca'", frontendSummary, StringComparison.Ordinal);
        Assert.Contains("update-field --field frontend", frontendSummary, StringComparison.Ordinal);

        var wakeConflict = workspace.RunRaw(workspace.ExternalRecordArgs("implementation", "claude-app", "wake-new"));
        Assert.Equal(1, wakeConflict.ExitCode);
        var wakeSummary = Parse(wakeConflict.Output).GetProperty("summary").GetString()!;
        Assert.Contains("wake_command: recorded 'wake-old', requested 'wake-new'", wakeSummary, StringComparison.Ordinal);
        Assert.Contains("update-field --field wake_command", wakeSummary, StringComparison.Ordinal);

        var residenceConflict = workspace.RunRaw(workspace.HerdrRecordArgs("implementation"));
        Assert.Equal(1, residenceConflict.ExitCode);
        var residenceSummary = Parse(residenceConflict.Output).GetProperty("summary").GetString()!;
        Assert.Contains("resident: recorded 'external', requested 'herdr'", residenceSummary, StringComparison.Ordinal);
        Assert.Contains("topology update-residence", residenceSummary, StringComparison.Ordinal);
        Assert.Equal(before, File.ReadAllBytes(workspace.TopologyPath));

        Fixture("frontend record conflict", frontendConflict.Output);
        Fixture("wake command record conflict", wakeConflict.Output);
        Fixture("residence record conflict", residenceConflict.Output);
    }

    [Fact]
    public void GuideNamedCommands_RelabelAnExistingExternalRole_G784()
    {
        using var workspace = new TopologyWorkspace("guide");
        workspace.RecordExternal("design", "claude-app");
        workspace.RecordHerdr("review");
        using var guideWriter = new StringWriter();
        Assert.Equal(0, GuideDesignThreadCommand.Execute(
            workspace.Context,
            ["--domain", Domain, "--team", workspace.Team, "--routing-root", workspace.Root, "--format", "json"],
            guideWriter));
        var guide = Parse(guideWriter.ToString()).GetProperty("external_residence_operating_contract");
        var frontendGuidance = guide.GetProperty("frontend_relabel").GetString()!;
        var wakeGuidance = guide.GetProperty("wake_channel_declaration").GetString()!;

        Assert.DoesNotContain("session-layer topology record", frontendGuidance, StringComparison.Ordinal);
        var frontendCommand = ExtractUpdateFieldCommand(frontendGuidance, "frontend");
        var wakeCommand = ExtractUpdateFieldCommand(wakeGuidance, "wake_command");
        Assert.Contains("--confirm-update-field", frontendCommand, StringComparison.Ordinal);
        Assert.Contains("--confirm-update-field", wakeCommand, StringComparison.Ordinal);

        var relabel = workspace.RunRaw(MaterializeGuideCommand(
            frontendCommand, "design", "claude-app", "orca"));
        Assert.Equal(0, relabel.ExitCode);
        Assert.Equal("orca", workspace.ReadRole("design").GetProperty("frontend").GetString());

        var declareWake = workspace.RunRaw(MaterializeGuideCommand(
            wakeCommand, "design", "absent", "guide-wake-{task_id}"));
        Assert.Equal(0, declareWake.ExitCode);
        Assert.Equal("guide-wake-{task_id}", workspace.ReadRole("design").GetProperty("wake_command").GetString());

        Fixture("guide frontend command", frontendCommand);
        Fixture("guide frontend relabel JSON", relabel.Output);
        Fixture("guide wake command", wakeCommand);
        Fixture("guide wake declaration JSON", declareWake.Output);
    }

    [Fact]
    public void WakeCommand_UpdateFieldRendersTheSameDelegateEnvelopeAsCreation_G784()
    {
        using var workspace = new TopologyWorkspace("wake-parity");
        const string template = "wake-sentinel --task {task_id} --summary {summary}";
        workspace.RecordExternal("orchestration", "orca");
        workspace.RecordExternal("implementation", "orca", template);
        workspace.RecordHerdr("review");
        workspace.SetHerdrOnlyMode();

        var fromCreation = workspace.RunRaw(DelegateArgs());
        Assert.Equal(0, fromCreation.ExitCode);
        var creationResult = Parse(fromCreation.Output);
        var creationEnvelope = creationResult.GetProperty("payload").GetString()!;
        var creationWake = creationResult.GetProperty("courtesy_wake_command").GetString()!;
        Assert.Equal("wake-sentinel --task G784-fixture --summary Relabel external role", creationWake);

        File.Delete(workspace.TopologyPath);
        workspace.RecordExternal("orchestration", "orca");
        workspace.RecordExternal("implementation", "orca");
        workspace.RecordHerdr("review");
        var update = workspace.RunRaw(UpdateArgs(
            workspace, "implementation", "wake_command", "absent", template, write: true));
        Assert.Equal(0, update.ExitCode);

        var fromUpdate = workspace.RunRaw(DelegateArgs());
        Assert.Equal(0, fromUpdate.ExitCode);
        var updateResult = Parse(fromUpdate.Output);
        var updateEnvelope = updateResult.GetProperty("payload").GetString()!;
        var updateWake = updateResult.GetProperty("courtesy_wake_command").GetString()!;
        Assert.Equal(creationEnvelope, updateEnvelope);
        Assert.Equal(creationWake, updateWake);

        Fixture("wake creation delegate envelope", creationEnvelope);
        Fixture("wake creation rendered command", creationWake);
        Fixture("wake update-field JSON", update.Output);
        Fixture("wake update delegate envelope", updateEnvelope);
        Fixture("wake update rendered command", updateWake);
    }

    [Fact]
    public void DocumentationMirrors_DescribeTheExternalLabelUpdateAndRecordRefusal_G784()
    {
        var repo = RepoVersionPolicySource.RepoRoot();
        var english = File.ReadAllText(Path.Combine(repo, "docs", "en", "12-agent-message-orchestration.md"));
        var japanese = File.ReadAllText(Path.Combine(repo, "docs", "ja", "12-agent-message-orchestration.md"));

        foreach (var document in new[] { english, japanese })
        {
            Assert.Contains("--field frontend", document, StringComparison.Ordinal);
            Assert.Contains("--field wake_command", document, StringComparison.Ordinal);
            Assert.Contains("--new absent", document, StringComparison.Ordinal);
            Assert.Contains("update-residence", document, StringComparison.Ordinal);
        }

        Assert.Contains("recorded and requested values", english, StringComparison.Ordinal);
        Assert.Contains("recorded/requested value", japanese, StringComparison.Ordinal);
    }

    private static string[] UpdateArgs(
        TopologyWorkspace workspace,
        string role,
        string field,
        string current,
        string next,
        bool write) =>
    [
        "session-layer", "topology", "update-field",
        "--domain", Domain,
        "--team", workspace.Team,
        "--role", role,
        "--field", field,
        "--current", current,
        "--new", next,
        "--confirm-update-field",
        write ? "--write" : "--dry-run",
        "--format", "json",
    ];

    private static string[] RemoveConfirmation(IEnumerable<string> args) =>
        args.Where(arg => !string.Equals(arg, "--confirm-update-field", StringComparison.Ordinal)).ToArray();

    private static string[] DelegateArgs() =>
    [
        "notify", "delegate",
        "--domain", Domain,
        "--team", "g784-wake-parity",
        "--from", "orchestration",
        "--to", "implementation",
        "--report-to", "orchestration",
        "--task-id", "G784-fixture",
        "--objective", "Relabel external role",
        "--input", "issue #1708",
        "--expected-artifact", "ready PR",
        "--result-nonce", "g784-fixture",
        "--write",
        "--format", "json",
    ];

    private static JsonElement Parse(string output)
    {
        using var document = JsonDocument.Parse(output);
        return document.RootElement.Clone();
    }

    private static string Summary(string output) => Parse(output).GetProperty("summary").GetString()!;

    private static IReadOnlyList<string> ChangedFields(JsonElement before, JsonElement after)
    {
        var beforeValues = before.EnumerateObject()
            .ToDictionary(property => property.Name, property => property.Value.GetRawText(), StringComparer.Ordinal);
        var afterValues = after.EnumerateObject()
            .ToDictionary(property => property.Name, property => property.Value.GetRawText(), StringComparer.Ordinal);
        return beforeValues.Keys
            .Union(afterValues.Keys, StringComparer.Ordinal)
            .Where(field => !beforeValues.TryGetValue(field, out var oldValue)
                || !afterValues.TryGetValue(field, out var newValue)
                || !string.Equals(oldValue, newValue, StringComparison.Ordinal))
            .OrderBy(field => field, StringComparer.Ordinal)
            .ToArray();
    }

    private static string ExtractUpdateFieldCommand(string guidance, string field)
    {
        var prefix = $"`intent-cli session-layer topology update-field ";
        var start = guidance.IndexOf(prefix, StringComparison.Ordinal);
        Assert.True(start >= 0, guidance);
        var end = guidance.IndexOf('`', start + 1);
        Assert.True(end > start, guidance);
        var command = guidance[(start + 1)..end];
        Assert.Contains($"--field {field}", command, StringComparison.Ordinal);
        return command;
    }

    private static string[] MaterializeGuideCommand(string command, string role, string current, string next)
    {
        var tokens = command.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal("intent-cli", tokens[0]);
        var roleIndex = Array.IndexOf(tokens, "--role");
        var currentIndex = Array.IndexOf(tokens, "--current");
        var newIndex = Array.IndexOf(tokens, "--new");
        Assert.True(roleIndex >= 0 && currentIndex >= 0 && newIndex >= 0, command);
        tokens[roleIndex + 1] = role;
        tokens[currentIndex + 1] = current;
        tokens[newIndex + 1] = next;
        return tokens[1..];
    }

    private static void Fixture(string name, string value) =>
        Console.WriteLine($"G784 {name}:\n{value.TrimEnd()}");

    private sealed class TopologyWorkspace : IDisposable
    {
        public TopologyWorkspace(string suffix)
        {
            Root = Directory.CreateTempSubdirectory($"session-layer-topology-g784-{suffix}-").FullName;
            Team = $"g784-{suffix}";
            Context = new CliContext
            {
                RepoRoot = Root,
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

        public string Root { get; }
        public string Team { get; }
        public CliContext Context { get; }
        public string TopologyPath => NotifyRoleTopologyStore.ResolvePath(Root, Domain, Team);

        public void RecordExternal(string role, string frontend, string? wakeCommand = null)
        {
            var result = RunRaw(ExternalRecordArgs(role, frontend, wakeCommand));
            Assert.Equal(0, result.ExitCode);
        }

        public void RecordHerdr(string role)
        {
            var result = RunRaw(HerdrRecordArgs(role));
            Assert.Equal(0, result.ExitCode);
        }

        public string[] ExternalRecordArgs(string role, string frontend, string? wakeCommand = null)
        {
            var args = new List<string>
            {
                "session-layer", "topology", "record",
                "--domain", Domain,
                "--team", Team,
                "--role", role,
                "--resident", "external",
                "--reader", $".intent-cli/events/{Domain}/{Team}-{role}.jsonl",
                "--frontend", frontend,
            };
            if (wakeCommand is not null)
            {
                args.AddRange(["--wake-command", wakeCommand]);
            }
            args.AddRange(["--write", "--format", "json"]);
            return args.ToArray();
        }

        public string[] HerdrRecordArgs(string role) =>
        [
            "session-layer", "topology", "record",
            "--domain", Domain,
            "--team", Team,
            "--role", role,
            "--resident", "herdr",
            "--workspace-id", "wG784",
            "--pane-id", role == "review" ? "wG784:p2" : "wG784:p1",
            "--cwd", "/g784",
            "--delivery-method", "inline",
            "--write",
            "--format", "json",
        ];

        public (int ExitCode, string Output) RunRaw(params string[] args)
        {
            using var writer = new StringWriter();
            var exitCode = CommandRouter.Execute(args, Context, writer);
            return (exitCode, writer.ToString());
        }

        public JsonElement ReadRole(string role)
        {
            using var document = JsonDocument.Parse(File.ReadAllText(TopologyPath));
            return document.RootElement.GetProperty("roles").GetProperty(role).Clone();
        }

        public void SetHerdrOnlyMode()
        {
            using var writer = new StringWriter();
            Assert.Equal(0, SessionLayerCommand.ExecuteSet(
                Context,
                ["--domain", Domain, "--team", Team, "--mode", "herdr-only", "--write", "--format", "json"],
                writer));
        }

        public void Dispose()
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }
    }
}
