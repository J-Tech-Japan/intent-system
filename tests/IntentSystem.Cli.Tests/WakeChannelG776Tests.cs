using System.Text;
using System.Text.Json;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

/// <summary>
/// G776: operator-declared external wake templates are rendered as text in
/// the existing dispatch/reporting surfaces. These tests deliberately use an
/// unavailable-looking command name so a process-start regression is visible.
/// </summary>
[Collection("WorkerNextActionSharedState")]
public sealed class WakeChannelG776Tests : IDisposable
{
    private const string Domain = "intent-cli";
    private const string Team = "g776-team";
    private const string TaskId = "G776-task";
    private const string Objective = "Implement wake contract";
    private const string Nonce = "g776-nonce";
    private readonly string root = Directory.CreateTempSubdirectory("wake-channel-g776-").FullName;
    private readonly string agmsgScriptsPath;
    private readonly CliContext context;

    public WakeChannelG776Tests()
    {
        agmsgScriptsPath = Path.Combine(root, "agmsg-scripts");
        Directory.CreateDirectory(agmsgScriptsPath);
        File.WriteAllText(Path.Combine(agmsgScriptsPath, "team.sh"), "fixture");
        File.WriteAllText(Path.Combine(agmsgScriptsPath, "send.sh"), "fixture");
        context = new CliContext
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
        NotifyCommand.AgmsgScriptsDirectoryFactory = () => agmsgScriptsPath;
        NotifyCommand.HerdrExecutableFactory = () => "fake-herdr";
    }

    public void Dispose()
    {
        NotifyCommand.ProcessRunnerFactory = null;
        NotifyCommand.AgmsgScriptsDirectoryFactory = null;
        NotifyCommand.HerdrExecutableFactory = null;
        NotifyTaskEnvelopeStore.WriteOverride = null;
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void RecordAndShow_ExternalWakeCommandRoundTripsVerbatim_G776()
    {
        const string template = "wake-sentinel --task {task_id} --summary {summary} --unknown {foo}";
        var (recordExit, record) = RunJson(
            "session-layer", "topology", "record",
            "--domain", Domain,
            "--team", Team,
            "--role", "design",
            "--resident", "external",
            "--reader", $".intent-cli/events/{Domain}/{Team}.jsonl",
            "--frontend", "orca",
            "--wake-command", template,
            "--write",
            "--format", "json");

        Assert.True(recordExit == 0, record.GetRawText());
        Assert.True(record.GetProperty("applied").GetBoolean());

        var topologyPath = NotifyRoleTopologyStore.ResolvePath(root, Domain, Team);
        using var topology = JsonDocument.Parse(File.ReadAllText(topologyPath));
        Assert.Equal(
            template,
            topology.RootElement.GetProperty("roles").GetProperty("design").GetProperty("wake_command").GetString());

        var (herdrRecordExit, herdrRecord) = RunJson(
            "session-layer", "topology", "record",
            "--domain", Domain,
            "--team", Team,
            "--role", "orchestration",
            "--resident", "herdr",
            "--workspace-id", "w-g776",
            "--pane-id", "w-g776:p1",
            "--cwd", "/workspace",
            "--write",
            "--format", "json");
        Assert.True(herdrRecordExit == 0, herdrRecord.GetRawText());

        var (showExit, showRaw) = RunRaw(
            "session-layer", "topology", "show",
            "--domain", Domain,
            "--team", Team,
            "--format", "json");
        Assert.True(showExit == 0, showRaw);
        using var show = JsonDocument.Parse(showRaw);
        var design = Assert.Single(show.RootElement.GetProperty("roles").EnumerateArray(), role =>
            role.GetProperty("role").GetString() == "design");
        Assert.Equal(template, design.GetProperty("wake_command").GetString());

        var (markdownExit, markdown) = RunRaw(
            "session-layer", "topology", "show",
            "--domain", Domain,
            "--team", Team,
            "--format", "markdown");
        Assert.True(markdownExit == 0, markdown);
        Assert.Contains("wake_command=" + template, markdown, StringComparison.Ordinal);
    }

    [Fact]
    public void Record_HerdrRejectsWakeCommandWithoutWriting_G776()
    {
        var (exitCode, output) = RunRaw(
            "session-layer", "topology", "record",
            "--domain", Domain,
            "--team", Team,
            "--role", "implementation",
            "--resident", "herdr",
            "--workspace-id", "w-g776",
            "--pane-id", "w-g776:p1",
            "--cwd", "/workspace",
            "--wake-command", "wake-sentinel {task_id}",
            "--write",
            "--format", "json");

        Assert.Equal(1, exitCode);
        Assert.Contains("does not accept --reader, --frontend, or --wake-command", output, StringComparison.Ordinal);
        Assert.False(File.Exists(NotifyRoleTopologyStore.ResolvePath(root, Domain, Team)));
    }

    [Fact]
    public void Record_RejectsMultilineWakeCommandWithoutWriting_G776()
    {
        var (exitCode, output) = RunRaw(
            "session-layer", "topology", "record",
            "--domain", Domain,
            "--team", Team,
            "--role", "design",
            "--resident", "external",
            "--reader", ".intent-cli/events/intent-cli/g776-team-design.jsonl",
            "--wake-command", "wake-sentinel {task_id}\nsecond-line",
            "--write",
            "--format", "json");

        Assert.Equal(1, exitCode);
        Assert.Contains("must be a one-line literal command template", output, StringComparison.Ordinal);
        Assert.False(File.Exists(NotifyRoleTopologyStore.ResolvePath(root, Domain, Team)));
    }

    [Fact]
    public void Delegate_DeclaredRecipientRendersWakeAfterCanonicalWrite_AndNeverExecutesIt_G776()
    {
        const string recipientTemplate = "wake-sentinel --task {task_id} --summary {summary} --unknown {foo}";
        const string reportTemplate = "report-wake --task {task_id} --summary {summary} --unknown {foo}";
        WriteTopology(
            implementationExternal: true,
            implementationWakeCommand: recipientTemplate,
            orchestrationWakeCommand: reportTemplate);
        var runner = SuccessfulRunner();
        NotifyCommand.ProcessRunnerFactory = () => runner;

        var (exitCode, output) = RunRaw(DelegateArgs());

        Assert.True(exitCode == 0, output);
        using var result = JsonDocument.Parse(output);
        var rootElement = result.RootElement;
        Assert.Equal(
            "wake-sentinel --task G776-task --summary Implement wake contract --unknown {foo}",
            rootElement.GetProperty("courtesy_wake_command").GetString());
        Assert.Contains(
            "After the canonical notify write succeeds",
            rootElement.GetProperty("courtesy_wake_instruction").GetString(),
            StringComparison.Ordinal);
        Assert.True(
            output.IndexOf("\"report_command\"", StringComparison.Ordinal)
            < output.IndexOf("\"courtesy_wake_command\"", StringComparison.Ordinal));

        var envelope = rootElement.GetProperty("payload").GetString()!;
        var canonicalIndex = envelope.IndexOf("  canonical-report-command: ", StringComparison.Ordinal);
        var wakeIndex = envelope.IndexOf("  courtesy-wake-command: ", StringComparison.Ordinal);
        Assert.True(canonicalIndex >= 0 && wakeIndex > canonicalIndex, envelope);
        Assert.Contains(
            "report-wake --task G776-task --summary <one-line-summary> --unknown {foo}",
            envelope,
            StringComparison.Ordinal);
        Assert.Contains(
            "after it succeeds, send the rendered courtesy-wake-command as a courtesy-only signal",
            envelope,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "never hand-write a transport invocation.",
            envelope,
            StringComparison.Ordinal);
        Assert.Contains(
            "Do not hand-write any other transport invocation.",
            envelope,
            StringComparison.Ordinal);

        Assert.DoesNotContain(runner.Calls, call =>
            string.Equals(call.FileName, "wake-sentinel", StringComparison.Ordinal)
            || call.Arguments.FirstOrDefault() == "wake-sentinel"
            || string.Equals(call.FileName, "report-wake", StringComparison.Ordinal)
            || call.Arguments.FirstOrDefault() == "report-wake");
        Assert.Empty(runner.Calls);
    }

    [Fact]
    public void Delegate_FileBackedEnvelope_RendersReportRecipientWakeAfterCanonicalCommand_G776()
    {
        const string reportTemplate = "report-wake --task {task_id} --summary {summary} --unknown {foo}";
        WriteTopology(
            implementationExternal: false,
            implementationWakeCommand: null,
            orchestrationWakeCommand: reportTemplate);
        var runner = SuccessfulRunner();
        NotifyCommand.ProcessRunnerFactory = () => runner;

        var (exitCode, output) = RunRaw(DelegateArgs());

        Assert.True(exitCode == 0, output);
        using var result = JsonDocument.Parse(output);
        Assert.False(result.RootElement.TryGetProperty("courtesy_wake_command", out _));
        Assert.Equal("file-backed", result.RootElement.GetProperty("delivery_method").GetString());
        var payload = result.RootElement.GetProperty("payload").GetString()!;
        var taskFile = result.RootElement.GetProperty("task_file").GetString()!;
        Assert.Equal(Encoding.UTF8.GetBytes(payload), File.ReadAllBytes(taskFile));
        var canonicalIndex = payload.IndexOf("  canonical-report-command: ", StringComparison.Ordinal);
        var wakeIndex = payload.IndexOf("  courtesy-wake-command: ", StringComparison.Ordinal);
        Assert.True(canonicalIndex >= 0 && wakeIndex > canonicalIndex, payload);
        Assert.Contains(
            "report-wake --task G776-task --summary <one-line-summary> --unknown {foo}",
            payload,
            StringComparison.Ordinal);
        Assert.DoesNotContain(runner.Calls, call =>
            string.Equals(call.FileName, "report-wake", StringComparison.Ordinal)
            || call.Arguments.FirstOrDefault() == "report-wake");
        Assert.Contains(runner.Calls, call =>
            call.Arguments.Take(3).SequenceEqual(["agent", "prompt", "w-g776:p2"]));
    }

    [Fact]
    public void Delegate_UndeclaredTeamRetainsParentResultAndEnvelopeBytes_G776()
    {
        WriteTopology(
            implementationExternal: false,
            implementationWakeCommand: null,
            orchestrationWakeCommand: null);
        var runner = SuccessfulRunner();
        NotifyCommand.ProcessRunnerFactory = () => runner;

        var (exitCode, output) = RunRaw(DelegateArgs());

        Assert.True(exitCode == 0, output);
        using var result = JsonDocument.Parse(output);
        Assert.False(result.RootElement.TryGetProperty("courtesy_wake_command", out _));
        Assert.False(result.RootElement.TryGetProperty("courtesy_wake_instruction", out _));
        var payload = result.RootElement.GetProperty("payload").GetString()!;
        Assert.Contains(
            "  required-final-step: Run canonical-report-command after all other work; never hand-write a transport invocation.",
            payload,
            StringComparison.Ordinal);
        Assert.DoesNotContain("courtesy-wake-command", payload, StringComparison.Ordinal);

        var taskFile = result.RootElement.GetProperty("task_file").GetString()!;
        Assert.Equal(Encoding.UTF8.GetBytes(payload), File.ReadAllBytes(taskFile));
        Assert.Equal(ParentUndeclaredEnvelope, NormalizeRoot(payload));
        Assert.Equal(ParentUndeclaredDelegateResult, NormalizeRoot(output));
    }

    [Fact]
    public void Guides_RenderOneDeclaredWakeRule_WithoutReplacingTheCanonicalRecord_G776()
    {
        using var designWriter = new StringWriter();
        Assert.Equal(
            0,
            GuideDesignThreadCommand.Execute(
                context,
                ["--domain", Domain, "--team", Team, "--routing-root", root, "--format", "markdown"],
                designWriter));
        var design = designWriter.ToString();
        Assert.Equal(1, Count(design, "**declared wake:**"));
        Assert.Contains("--wake-command", design, StringComparison.Ordinal);
        Assert.Contains("{task_id}", design, StringComparison.Ordinal);
        Assert.Contains("{summary}", design, StringComparison.Ordinal);
        Assert.Contains("unknown placeholders untouched", design, StringComparison.Ordinal);
        Assert.Contains("never executes, validates, health-checks, launches, or manages", design, StringComparison.Ordinal);

        using var orchestratorWriter = new StringWriter();
        Assert.Equal(
            0,
            GuideOrchestratorThreadCommand.Execute(
                context,
                ["--domain", Domain, "--target-repo", "J-Tech-Japan/intent-system", "--agent", "codex"],
                orchestratorWriter));
        var orchestrator = orchestratorWriter.ToString();
        Assert.Equal(1, Count(orchestrator, "**declared external wake:**"));
        Assert.Contains("canonical notify write is the durable record and always comes first", orchestrator, StringComparison.Ordinal);
        Assert.Contains("never executes, validates, health-checks, launches, or manages", orchestrator, StringComparison.Ordinal);
    }

    [Fact]
    public void DocumentationMirrors_DescribeLiteralDeclarationPlaceholdersAndNonExecution_G776()
    {
        var en = ReadRepoFile("docs/en/12-agent-message-orchestration.md");
        var ja = ReadRepoFile("docs/ja/12-agent-message-orchestration.md");

        foreach (var document in new[] { en, ja })
        {
            Assert.Contains("--wake-command", document, StringComparison.Ordinal);
            Assert.Contains("{task_id}", document, StringComparison.Ordinal);
            Assert.Contains("{summary}", document, StringComparison.Ordinal);
            Assert.Contains("{foo}", document, StringComparison.Ordinal);
            Assert.Contains("orca orchestration send --run <run-id> --to run:<run-id>", document, StringComparison.Ordinal);
        }

        Assert.Contains("never executes, validates by shelling out, health-checks, launches,", en, StringComparison.Ordinal);
        Assert.Contains("`health-check` を", ja, StringComparison.Ordinal);
        Assert.Contains("command を起動・管理", ja, StringComparison.Ordinal);
        Assert.Equal(1, Count(en, "Declare the courtesy wake explicitly (G776)."));
        Assert.Equal(1, Count(ja, "courtesy wake を明示的に宣言（G776）。"));
    }

    // Captured from the exact G775 parent 75216283875b08ade3d100de7ddabe3fad0bd21c
    // with this fixture. The sole substitution makes the test's temporary
    // routing root deterministic; no field-level projection is used.
    private static readonly string ParentUndeclaredEnvelope =
        ReadFixture("parent-undeclared-envelope.md").TrimEnd('\r', '\n');
    private static readonly string ParentUndeclaredDelegateResult =
        ReadFixture("parent-undeclared-delegate-result.json");

    private void WriteTopology(
        bool implementationExternal,
        string? implementationWakeCommand,
        string? orchestrationWakeCommand)
    {
        var roles = new Dictionary<string, object>
        {
            ["orchestration"] = ExternalRole("orchestration", orchestrationWakeCommand),
            ["implementation"] = implementationExternal
                ? ExternalRole("implementation", implementationWakeCommand)
                : new
                {
                    resident = "herdr",
                    workspace_id = "w-g776",
                    pane_id = "w-g776:p2",
                    kind = "codex",
                    delivery_method = "file-backed",
                },
            ["review"] = new
            {
                resident = "herdr",
                workspace_id = "w-g776",
                pane_id = "w-g776:p3",
            },
        };
        var topology = new
        {
            domain = Domain,
            team = Team,
            workspace_id = "w-g776",
            roles,
            host_state = new
            {
                role = "orchestration",
                envelope = "test-owned-host-state",
            },
        };
        var topologyPath = NotifyRoleTopologyStore.ResolvePath(root, Domain, Team);
        Directory.CreateDirectory(Path.GetDirectoryName(topologyPath)!);
        File.WriteAllText(topologyPath, JsonSerializer.Serialize(topology));
        SetHerdrOnlyMode();
    }

    private void SetHerdrOnlyMode()
    {
        using var writer = new StringWriter();
        var exitCode = SessionLayerCommand.ExecuteSet(
            context,
            ["--domain", Domain, "--team", Team, "--mode", SessionLayerMode.HerdrOnly, "--write", "--format", "json"],
            writer);
        Assert.True(exitCode == 0, writer.ToString());
    }

    private object ExternalRole(string role, string? wakeCommand)
    {
        var record = new Dictionary<string, object>
        {
            ["resident"] = "external",
            ["reader"] = $".intent-cli/events/{Domain}/{Team}-{role}.jsonl",
            ["frontend"] = "orca",
        };
        if (wakeCommand is not null)
        {
            record["wake_command"] = wakeCommand;
        }

        return record;
    }

    private (int ExitCode, JsonElement Result) RunJson(params string[] args)
    {
        var (exitCode, output) = RunRaw(args);
        Assert.True(exitCode == 0, output);
        return (exitCode, JsonDocument.Parse(output).RootElement.Clone());
    }

    private (int ExitCode, string Output) RunRaw(params string[] args)
    {
        using var writer = new StringWriter();
        var exitCode = CommandRouter.Execute(args, context, writer);
        return (exitCode, writer.ToString());
    }

    private static string[] DelegateArgs() =>
    [
        "notify", "delegate", "--domain", Domain, "--team", Team,
        "--from", "orchestration", "--to", "implementation", "--report-to", "orchestration",
        "--task-id", TaskId, "--objective", Objective,
        "--input", "issue #1689", "--expected-artifact", "ready PR", "--result-nonce", Nonce,
        "--write", "--format", "json",
    ];

    private static FakeNotifyProcessRunner SuccessfulRunner() => new((_, arguments) =>
        arguments.SequenceEqual(["agent", "list"])
            ? new NotifyProcessResult(
                0,
                "{\"result\":{\"agents\":[{\"name\":\"implementation\",\"workspace_id\":\"w-g776\",\"pane_id\":\"w-g776:p2\",\"agent\":\"codex\",\"agent_session\":{\"id\":\"implementation\"},\"agent_status\":\"idle\",\"interactive_ready\":true},{\"name\":\"review\",\"workspace_id\":\"w-g776\",\"pane_id\":\"w-g776:p3\",\"agent\":\"codex\",\"agent_session\":{\"id\":\"review\"},\"agent_status\":\"idle\",\"interactive_ready\":true}]}}",
                string.Empty)
            : new NotifyProcessResult(0, string.Empty, string.Empty));

    private static int Count(string text, string value) =>
        text.Split(value, StringSplitOptions.None).Length - 1;

    private string NormalizeRoot(string value) =>
        value.Replace(root, "<workspace-root>", StringComparison.Ordinal);

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

    private static string ReadFixture(string name) => File.ReadAllText(Path.Combine(
        RepoVersionPolicySource.RepoRoot(),
        "tests",
        "IntentSystem.Cli.Tests",
        "Fixtures",
        "G776",
        name));

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
}
