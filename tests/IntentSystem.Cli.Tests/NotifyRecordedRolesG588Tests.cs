using System.Text.Json;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

[Collection("WorkerNextActionSharedState")]
public sealed class NotifyRecordedRolesG588Tests : IDisposable
{
    private static readonly DateTimeOffset FixedNow = new(2026, 8, 2, 20, 30, 0, TimeSpan.Zero);
    private readonly Workspace workspace = new();

    public NotifyRecordedRolesG588Tests()
    {
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

    [Fact]
    public void ExternalResidentSenderAndReportTo_NeedTopologyExistenceButNotAPane_G588()
    {
        workspace.WriteTopology(externalReader: null);
        var runner = Runner((_, arguments) => arguments.SequenceEqual(["agent", "list"])
            ? Success(Roster(
                Agent("orchestration", "wH", "wH:p1"),
                Agent("implementation", "wH", "wH:p2")))
            : Success());
        NotifyCommand.ProcessRunnerFactory = () => runner;

        var (exitCode, result) = workspace.Run(DelegateArgs(
            from: "design",
            to: "implementation",
            reportTo: "design",
            write: true));

        Assert.Equal(0, exitCode);
        Assert.True(result.GetProperty("delivered").GetBoolean());
        Assert.False(result.GetProperty("event_appended").GetBoolean());
        var prompt = Assert.Single(runner.Calls, call =>
            call.Arguments.Take(2).SequenceEqual(["agent", "prompt"]));
        Assert.Equal("wH:p2", prompt.Arguments[2]);
        Assert.DoesNotContain(runner.Calls, call => call.Arguments.Contains("design"));
    }

    [Fact]
    public void DelegateToExternalResident_AppendsExactlyOneUnchangedSchemaEvent_G588()
    {
        var runner = Runner((_, _) => throw new InvalidOperationException(
            "an external-reader route must not start herdr"));
        NotifyCommand.ProcessRunnerFactory = () => runner;

        var (exitCode, result) = workspace.Run(DelegateArgs(
            from: "orchestration",
            to: "design",
            reportTo: "orchestration",
            write: true));

        Assert.Equal(0, exitCode);
        Assert.True(result.GetProperty("delivered").GetBoolean());
        Assert.True(result.GetProperty("event_appended").GetBoolean());
        Assert.Equal(workspace.EventPath, result.GetProperty("event_path").GetString());
        var line = Assert.Single(File.ReadAllLines(workspace.EventPath));
        using var document = JsonDocument.Parse(line);
        Assert.Equal(
            ["timestamp", "team", "kind", "unit", "summary", "artifact"],
            document.RootElement.EnumerateObject().Select(property => property.Name));
        Assert.Equal(FixedNow, document.RootElement.GetProperty("timestamp").GetDateTimeOffset());
        Assert.Equal("question", document.RootElement.GetProperty("kind").GetString());
        Assert.Equal("G588-demo", document.RootElement.GetProperty("unit").GetString());
        Assert.Equal("Implement external routing", document.RootElement.GetProperty("summary").GetString());
        Assert.Equal("issue #1279", document.RootElement.GetProperty("artifact").GetString());
        Assert.Empty(runner.Calls);
    }

    [Fact]
    public void ReportToExternalResident_AppendsExactlyOneOutcomeEvent_G588()
    {
        var runner = Runner((_, _) => throw new InvalidOperationException(
            "an external-reader route must not start herdr"));
        NotifyCommand.ProcessRunnerFactory = () => runner;

        var (exitCode, result) = workspace.Run(ReportArgs(from: "implementation", to: "design", write: true));

        Assert.Equal(0, exitCode);
        Assert.True(result.GetProperty("delivered").GetBoolean());
        Assert.True(result.GetProperty("event_appended").GetBoolean());
        var line = Assert.Single(File.ReadAllLines(workspace.EventPath));
        using var document = JsonDocument.Parse(line);
        Assert.Equal("completion", document.RootElement.GetProperty("kind").GetString());
        Assert.Equal("https://example.test/pr/1280", document.RootElement.GetProperty("artifact").GetString());
        Assert.Equal("external routing completed", document.RootElement.GetProperty("summary").GetString());
        Assert.Empty(runner.Calls);
    }

    [Fact]
    public void EveryEventKindNotifyCanEmit_BelongsToTheDocumentedSchema_G588()
    {
        var runner = Runner((_, _) => throw new InvalidOperationException(
            "external-reader and escalation routes must not start herdr"));
        NotifyCommand.ProcessRunnerFactory = () => runner;

        Assert.Equal(0, workspace.Run(DelegateArgs(
            from: "orchestration",
            to: "design",
            reportTo: "orchestration",
            write: true)).ExitCode);
        var reportStatuses = NotifyCommand.SupportedReportStatuses.ToArray();
        foreach (var status in reportStatuses)
        {
            Assert.Equal(0, workspace.Run(ReportArgs(
                from: "implementation",
                to: "design",
                write: true,
                status: status)).ExitCode);
        }

        Assert.Equal(0, workspace.Run(EscalateArgs()).ExitCode);

        var emittedKinds = File.ReadAllLines(workspace.EventPath)
            .Select(line =>
            {
                using var document = JsonDocument.Parse(line);
                return document.RootElement.GetProperty("kind").GetString()!;
            })
            .ToArray();
        var documentedKinds = new HashSet<string>(StringComparer.Ordinal)
        {
            "completion",
            "blocked",
            "question",
            "escalation",
        };

        Assert.Equal(reportStatuses.Length + 2, emittedKinds.Length);
        Assert.All(emittedKinds, kind => Assert.Contains(kind, documentedKinds));
        Assert.Equal(
            documentedKinds.Order(StringComparer.Ordinal),
            emittedKinds.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal));
        Assert.Empty(runner.Calls);
    }

    [Fact]
    public void ForeignWorkspaceSameName_IsDiagnosticOnlyAndNeverARecipientFallback_G594()
    {
        var runner = Runner((_, arguments) => arguments.SequenceEqual(["agent", "list"])
            ? Success(Roster(
                Agent("orchestration", "wH", "wH:p1"),
                Agent("implementation", "wH", "wH:p2"),
                Agent("review", "wForeign", "wForeign:p9")))
            : throw new InvalidOperationException("a refused route must never prompt"));
        NotifyCommand.ProcessRunnerFactory = () => runner;

        var (exitCode, result) = workspace.Run(DelegateArgs(
            from: "orchestration",
            to: "review",
            reportTo: "orchestration",
            write: true));

        Assert.Equal(1, exitCode);
        Assert.Equal("pane-absent", result.GetProperty("cause").GetString());
        var summary = result.GetProperty("summary").GetString()!;
        Assert.Contains("Team 'intent-cli-dev' recorded workspace 'wH' pane 'wH:p3'", summary, StringComparison.Ordinal);
        Assert.Contains("review@wForeign/wForeign:p9", summary, StringComparison.Ordinal);
        Assert.Contains("diagnostic only", summary, StringComparison.Ordinal);
        Assert.Contains("never a routing fallback", summary, StringComparison.Ordinal);
        Assert.DoesNotContain("recorded agent mapping", summary, StringComparison.Ordinal);
        Assert.DoesNotContain(runner.Calls, call =>
            call.Arguments.Take(2).SequenceEqual(["agent", "prompt"]));
    }

    [Fact]
    public void UnrecordedRole_ReportsTheRecordedTeamWorkspaceAndRoster_G588()
    {
        var runner = Runner((_, _) => throw new InvalidOperationException(
            "topology refusal must happen before herdr lookup"));
        NotifyCommand.ProcessRunnerFactory = () => runner;

        var (exitCode, result) = workspace.Run(DelegateArgs(
            from: "orchestration",
            to: "missing",
            reportTo: "orchestration",
            write: true));

        Assert.Equal(1, exitCode);
        Assert.Equal("unknown-role", result.GetProperty("cause").GetString());
        var summary = result.GetProperty("summary").GetString()!;
        Assert.Contains(NotifyRoleTopologyStore.RelativePath, summary, StringComparison.Ordinal);
        Assert.Contains("team 'intent-cli-dev' workspace 'wH'", summary, StringComparison.Ordinal);
        Assert.Contains(
            "found in that team scope: design, implementation, orchestration, review",
            summary,
            StringComparison.Ordinal);
        Assert.Contains("Record that role for this team", summary, StringComparison.Ordinal);
        Assert.Empty(runner.Calls);
    }

    [Fact]
    public void DryRunAndWrite_HaveTheSameDeliverableResolution_WithoutDryRunPrompt_G588()
    {
        var runner = Runner((_, arguments) => arguments.SequenceEqual(["agent", "list"])
            ? Success(Roster(
                Agent("orchestration", "wH", "wH:p1"),
                Agent("implementation", "wH", "wH:p2")))
            : Success());
        NotifyCommand.ProcessRunnerFactory = () => runner;

        var (dryExit, dry) = workspace.Run(DelegateArgs(
            from: "design",
            to: "implementation",
            reportTo: "design",
            write: false));
        Assert.Equal(0, dryExit);
        Assert.False(dry.TryGetProperty("cause", out _));
        Assert.False(dry.GetProperty("delivered").GetBoolean());
        Assert.DoesNotContain(runner.Calls, call =>
            call.Arguments.Take(2).SequenceEqual(["agent", "prompt"]));

        var (writeExit, write) = workspace.Run(DelegateArgs(
            from: "design",
            to: "implementation",
            reportTo: "design",
            write: true));
        Assert.Equal(dryExit, writeExit);
        Assert.False(write.TryGetProperty("cause", out _));
        Assert.True(write.GetProperty("delivered").GetBoolean());
        Assert.Single(runner.Calls, call =>
            call.Arguments.Take(2).SequenceEqual(["agent", "prompt"]));
        Assert.False(File.Exists(workspace.EventPath));
    }

    [Fact]
    public void DryRunAndWrite_HaveTheSameRefusalCause_WithoutSideEffects_G588()
    {
        var runner = Runner((_, arguments) => arguments.SequenceEqual(["agent", "list"])
            ? Success(Roster(
                Agent("orchestration", "wH", "wH:p1"),
                Agent("implementation", "wH", "wH:p2"),
                Agent("review", "wForeign", "wForeign:p9")))
            : throw new InvalidOperationException("a refused route must never prompt"));
        NotifyCommand.ProcessRunnerFactory = () => runner;
        var args = DelegateArgs("orchestration", "review", "orchestration", write: false);

        var (dryExit, dry) = workspace.Run(args);
        args[^3] = "--write";
        var (writeExit, write) = workspace.Run(args);

        Assert.Equal(1, dryExit);
        Assert.Equal(dryExit, writeExit);
        Assert.Equal("pane-absent", dry.GetProperty("cause").GetString());
        Assert.Equal(dry.GetProperty("cause").GetString(), write.GetProperty("cause").GetString());
        Assert.Equal(dry.GetProperty("summary").GetString(), write.GetProperty("summary").GetString());
        Assert.DoesNotContain(runner.Calls, call =>
            call.Arguments.Take(2).SequenceEqual(["agent", "prompt"]));
        Assert.False(File.Exists(workspace.EventPath));
    }

    [Fact]
    public void UnsafeExternalReader_IsRefusedInDryRunAndWriteWithoutAppend_G588()
    {
        workspace.WriteTopology("../../outside-intent-cli-events.jsonl");
        var runner = Runner((_, _) => throw new InvalidOperationException(
            "an unsafe external-reader route must not start herdr"));
        NotifyCommand.ProcessRunnerFactory = () => runner;

        var dryArgs = ReportArgs(from: "implementation", to: "design", write: false);
        var writeArgs = ReportArgs(from: "implementation", to: "design", write: true);
        var (dryExit, dry) = workspace.Run(dryArgs);
        var (writeExit, write) = workspace.Run(writeArgs);

        Assert.Equal(1, dryExit);
        Assert.Equal(dryExit, writeExit);
        Assert.Equal("reader-unavailable", dry.GetProperty("cause").GetString());
        Assert.Equal(dry.GetProperty("cause").GetString(), write.GetProperty("cause").GetString());
        Assert.Contains("escapes --routing-root", dry.GetProperty("summary").GetString(), StringComparison.Ordinal);
        Assert.Empty(runner.Calls);
        Assert.False(File.Exists(workspace.EventPath));
        Assert.False(File.Exists(Path.GetFullPath(Path.Combine(workspace.RootPath, "../../outside-intent-cli-events.jsonl"))));
    }

    [Theory]
    [InlineData("en")]
    [InlineData("ja")]
    public void Guidance_ExplainsRecordedResidenceAndResolutionContract_G588(string language)
    {
        var content = File.ReadAllText(Path.Combine(
            RepoVersionPolicySource.RepoRoot(),
            "docs",
            language,
            "12-agent-message-orchestration.md"));

        Assert.Contains(".intent-cli/role-pane-mapping.json", content, StringComparison.Ordinal);
        Assert.Contains("resident: external", content, StringComparison.Ordinal);
        Assert.Contains("reader", content, StringComparison.Ordinal);
        Assert.Contains("recipient must be deliverable", content, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("dry-run", content, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("workspace", content, StringComparison.Ordinal);

        var runtimeGuide = HerdrOnlyOperatingGuide.RenderMarkdown([]);
        Assert.Contains(".intent-cli/role-pane-mapping.json", runtimeGuide, StringComparison.Ordinal);
        Assert.Contains("resident: external", runtimeGuide, StringComparison.Ordinal);
        Assert.Contains("ONLY the recipient must be deliverable", runtimeGuide, StringComparison.Ordinal);
        Assert.Contains("team-scoped unknown-role diagnostics", runtimeGuide, StringComparison.Ordinal);
    }

    private static FakeRunner Runner(
        Func<string, IReadOnlyList<string>, NotifyProcessResult> handler) => new(handler);

    private static NotifyProcessResult Success(string output = "") => new(0, output, "");

    private static string Roster(params object[] agents) =>
        JsonSerializer.Serialize(new { result = new { agents } });

    private static object Agent(string name, string workspaceId, string paneId) => new
    {
        name,
        workspace_id = workspaceId,
        pane_id = paneId,
        agent = "codex",
        agent_session = new { id = name },
        agent_status = "idle",
        interactive_ready = true,
    };

    private static string[] DelegateArgs(string from, string to, string reportTo, bool write) =>
    [
        "notify", "delegate", "--domain", Workspace.Domain, "--team", Workspace.Team,
        "--from", from, "--to", to, "--report-to", reportTo,
        "--task-id", "G588-demo", "--objective", "Implement external routing",
        "--input", "issue #1279", "--expected-artifact", "draft PR URL",
        "--result-nonce", "g588-nonce", write ? "--write" : "--dry-run", "--format", "json",
    ];

    private static string[] ReportArgs(string from, string to, bool write, string status = "completed") =>
    [
        "notify", "report", "--domain", Workspace.Domain, "--team", Workspace.Team,
        "--from", from, "--to", to, "--task-id", "G588-demo", "--status", status,
        "--artifact", "https://example.test/pr/1280", "--summary", "external routing completed",
        write ? "--write" : "--dry-run", "--format", "json",
    ];

    private static string[] EscalateArgs() =>
    [
        "notify", "escalate", "--domain", Workspace.Domain, "--team", Workspace.Team,
        "--from", "implementation", "--task-id", "G588-demo",
        "--artifact", "https://example.test/pr/1280", "--summary", "needs design input",
        "--write", "--format", "json",
    ];

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

    private sealed class Workspace : IDisposable
    {
        public const string Domain = "intent-cli";
        public const string Team = "intent-cli-dev";

        public Workspace()
        {
            RootPath = Directory.CreateTempSubdirectory("notify-g588-").FullName;
            Directory.CreateDirectory(Path.Combine(RootPath, ".intent-cli"));
            WriteTopology($".intent-cli/events/{Team}.jsonl");
            using var writer = new StringWriter();
            var setExit = SessionLayerCommand.ExecuteSet(
                CreateContext(),
                ["--domain", Domain, "--team", Team, "--mode", SessionLayerMode.HerdrOnly, "--write", "--format", "json"],
                writer);
            if (setExit != 0)
            {
                throw new InvalidOperationException(writer.ToString());
            }
        }

        public string RootPath { get; }
        public string EventPath => Path.Combine(RootPath, ".intent-cli", "events", $"{Team}.jsonl");

        public void WriteTopology(string? externalReader)
        {
            File.WriteAllText(
                Path.Combine(RootPath, NotifyRoleTopologyStore.RelativePath.Replace('/', Path.DirectorySeparatorChar)),
                JsonSerializer.Serialize(new
                {
                    team = Team,
                    workspace_id = "wH",
                    roles = new Dictionary<string, object>
                    {
                        ["design"] = new
                        {
                            resident = "external",
                            frontend = "claude-app",
                            reader = externalReader,
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
                }));
        }

        public (int ExitCode, JsonElement Result) Run(string[] args)
        {
            using var writer = new StringWriter();
            var exitCode = CommandRouter.Execute(args, CreateContext(), writer);
            return (exitCode, JsonDocument.Parse(writer.ToString()).RootElement.Clone());
        }

        private CliContext CreateContext() => new()
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

        public void Dispose()
        {
            if (Directory.Exists(RootPath))
            {
                Directory.Delete(RootPath, recursive: true);
            }
        }
    }
}
