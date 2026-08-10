using System.Text.Json;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

/// <summary>
/// G645: guide reachability is declared by the packet, checked as closeout
/// debt, and explicitly silent only when the packet says no role-facing
/// surface. Nothing in these fixtures infers a guide or judges its wording.
/// </summary>
[Collection(AutomationStalledWorkSharedStateCollection.Name)]
public sealed class GuideReachabilityG645Tests : IDisposable
{
    private static readonly DateTimeOffset FixedNow = new(2026, 8, 15, 12, 0, 0, TimeSpan.Zero);
    private const string Repo = "J-Tech-Japan/intent-system";
    private const string HostCommit = "a1b2c3d4e5f60718293a4b5c6d7e8f9012345678";

    public GuideReachabilityG645Tests()
    {
        AutomationStalledWorkCommand.CandidateListerFactory = () => new EmptyCandidateLister();
        AutomationStalledWorkCommand.UtcNowFactory = () => FixedNow;
        AutomationGuideReachabilityRecordCommand.UtcNowFactory = () => FixedNow;
    }

    public void Dispose()
    {
        AutomationStalledWorkCommand.CandidateListerFactory = null;
        AutomationStalledWorkCommand.UtcNowFactory = null;
        AutomationGuideReachabilityRecordCommand.UtcNowFactory = null;
    }

    [Fact]
    public void DeclaredRouteAppearsAsDebtAndClearsAfterRecording_G645()
    {
        using var workspace = new ReachabilityWorkspace();
        workspace.WritePacket("G645", """
            implementation_issue_packet:
              source_execution_unit: G645
              domain: intent-cli
            guide_reachability:
              no_role_facing_surface: false
              routes:
                - guide_surface: guide workflow task implementation-loop
                  role: implementation
                  target_surface: new role-facing command
            """);
        workspace.WriteCloseout("G645", FixedNow.AddMinutes(-180));

        var pending = Assert.Single(workspace.RunStalledWork().EnumerateArray());
        Assert.Equal(AutomationStalledWorkCommand.KindGuideReachabilityPending, pending.GetProperty("kind").GetString());
        Assert.Equal("G645", pending.GetProperty("execution_unit").GetString());
        Assert.Contains(
            "guide workflow task implementation-loop",
            pending.GetProperty("declared_guide_surfaces").EnumerateArray().Select(value => value.GetString()));
        Assert.Contains("implementation", pending.GetProperty("declared_guide_roles").EnumerateArray().Select(value => value.GetString()));
        Assert.Contains("guide-reachability-record", pending.GetProperty("recommended_action").GetString(), StringComparison.Ordinal);

        var recorded = workspace.RunRecord(["--execution-unit", "G645", "--commit", HostCommit, "--write", "--format", "json"]);
        Assert.True(recorded.ExitCode == 0, recorded.Json.TryGetProperty("error", out var error) ? error.GetString() : recorded.Json.ToString());
        Assert.True(recorded.Json.GetProperty("applied").GetBoolean());
        Assert.Equal(0, workspace.RunStalledWork().GetArrayLength());
    }

    [Fact]
    public void ExplicitNoSurfaceProducesNoDebt_G645()
    {
        using var workspace = new ReachabilityWorkspace();
        workspace.WritePacket("G645", """
            implementation_issue_packet:
              source_execution_unit: G645
              domain: intent-cli
            guide_reachability:
              no_role_facing_surface: true
              routes: []
            """);
        workspace.WriteCloseout("G645", FixedNow.AddMinutes(-180));

        Assert.Equal(0, workspace.RunStalledWork().GetArrayLength());
        var recorded = workspace.RunRecord(["--execution-unit", "G645", "--commit", HostCommit, "--write", "--format", "json"]);
        Assert.True(recorded.ExitCode == 0, recorded.Json.TryGetProperty("error", out var error) ? error.GetString() : recorded.Json.ToString());
        Assert.True(recorded.Json.GetProperty("no_role_facing_surface").GetBoolean());
        Assert.False(File.Exists(workspace.RecordPath("G645")));
    }

    [Fact]
    public void AbsentDeclarationIsWarnedAndDistinctFromNoSurface_G645()
    {
        using var workspace = new ReachabilityWorkspace();
        workspace.WritePacket("G645", """
            implementation_issue_packet:
              source_execution_unit: G645
              domain: intent-cli
            """);
        workspace.WriteCloseout("G645", FixedNow.AddMinutes(-180));

        var result = workspace.RunStalledWorkResult();
        Assert.Equal(0, result.GetProperty("items").GetArrayLength());
        Assert.DoesNotContain(
            result.GetProperty("excluded").EnumerateArray(),
            excluded => string.Equals(
                excluded.GetProperty("reason").GetString(),
                AutomationStalledWorkCommand.ReasonGuideReachabilityDeclarationMissing,
                StringComparison.Ordinal));
        Assert.Contains(
            result.GetProperty("warnings").EnumerateArray().Select(warning => warning.GetString()),
            warning => warning is not null
                && warning.Contains("no guide_reachability declaration", StringComparison.Ordinal)
                && warning.Contains(GuideReachabilityDeclaration.RouteYaml, StringComparison.Ordinal)
                && warning.Contains(GuideReachabilityDeclaration.NoSurfaceYaml, StringComparison.Ordinal));

        var recorded = workspace.RunRecord(["--execution-unit", "G645", "--commit", HostCommit, "--format", "json"]);
        Assert.Equal(1, recorded.ExitCode);
        Assert.Contains("no guide_reachability declaration", recorded.Json.GetProperty("error").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public void PacketDraftEmitsExplicitReachabilityPromptAndTemplate_G645()
    {
        using var workspace = new ReachabilityWorkspace();
        using var writer = new StringWriter();
        Assert.Equal(0, PacketDraftCommand.Execute(
            workspace.Context,
            ["--execution-unit", "G645", "--target-repo", Repo],
            writer));

        var packet = File.ReadAllText(Path.Combine(workspace.RootPath, ".intent-cli", "issues", "G645", "packet.yaml"));
        Assert.Contains("# guide_reachability:", packet, StringComparison.Ordinal);
        Assert.Contains("#   no_role_facing_surface: true", packet, StringComparison.Ordinal);
        Assert.False(GuideReachabilityDeclaration.Read(packet).IsDeclared);
        var guide = CommandRouterOutput("guide", "workflow", "task", "packet-draft");
        Assert.Contains("guide-reachability", guide, StringComparison.Ordinal);
        Assert.Contains("keyword-to-guide", guide, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void WarningYamlPastedVerbatim_IsAcceptedAndSilencesWarning_G661(bool routeForm)
    {
        using var workspace = new ReachabilityWorkspace();
        var declaration = routeForm
            ? GuideReachabilityDeclaration.RouteYaml.Replace("<role-facing-surface>", "notify supervise", StringComparison.Ordinal)
            : GuideReachabilityDeclaration.NoSurfaceYaml;
        workspace.WritePacket("G661", $"""
            implementation_issue_packet:
              source_execution_unit: G661
              domain: intent-cli
            {declaration}
            """);
        workspace.WriteCloseout("G661", FixedNow.AddMinutes(-180));

        var result = workspace.RunStalledWorkResult();
        Assert.DoesNotContain(
            result.GetProperty("warnings").EnumerateArray(),
            warning => warning.GetString()!.Contains("no guide_reachability declaration", StringComparison.Ordinal));
        if (routeForm)
        {
            Assert.Contains(result.GetProperty("items").EnumerateArray(),
                item => item.GetProperty("kind").GetString() == AutomationStalledWorkCommand.KindGuideReachabilityPending);
        }
        else
        {
            Assert.Empty(result.GetProperty("items").EnumerateArray());
        }
    }

    [Fact]
    public void CommandRouterAdvertisesAndDispatchesRecorder_G645()
    {
        using var writer = new StringWriter();
        Assert.Equal(0, CommandRouter.Execute(
            ["automation", "guide-reachability-record", "--help"],
            new CliContext
            {
                RepoRoot = Path.GetTempPath(),
                Config = new CliConfig
                {
                    Project = new ProjectConfig
                    {
                        Domain = "intent-cli",
                        ArtifactRoot = ".intent-cli",
                        WorktreeRoot = ".intent-cli/worktrees",
                    },
                },
            },
            writer));
        Assert.Contains("guide-reachability-record", writer.ToString(), StringComparison.Ordinal);
        Assert.Contains("KEYWORD-TO-GUIDE", writer.ToString(), StringComparison.Ordinal);
    }

    private static string CommandRouterOutput(params string[] args)
    {
        using var writer = new StringWriter();
        var context = new CliContext
        {
            RepoRoot = Path.GetTempPath(),
            Config = new CliConfig
            {
                Project = new ProjectConfig
                {
                    Domain = "intent-cli",
                    ArtifactRoot = ".intent-cli",
                    WorktreeRoot = ".intent-cli/worktrees",
                },
            },
        };
        Assert.Equal(0, CommandRouter.Execute(args, context, writer));
        return writer.ToString();
    }

    private sealed class EmptyCandidateLister : IGitHubAutomationCandidateLister
    {
        public IReadOnlyList<GitHubAutomationIssueCandidate> ListIssues(string repo, IReadOnlyCollection<string> requiredLabels) =>
            Array.Empty<GitHubAutomationIssueCandidate>();

        public IReadOnlyList<GitHubAutomationPrCandidate> ListPullRequests(string repo, IReadOnlyCollection<string> requiredLabels) =>
            Array.Empty<GitHubAutomationPrCandidate>();

        public IReadOnlyList<GitHubAutomationPrCandidate> ListMergedPullRequests(string repo, IReadOnlyCollection<string> requiredLabels) =>
            Array.Empty<GitHubAutomationPrCandidate>();
    }

    private sealed record RecordRun(int ExitCode, JsonElement Json);

    private sealed class ReachabilityWorkspace : IDisposable
    {
        private readonly List<JsonDocument> documents = new();

        public ReachabilityWorkspace()
        {
            RootPath = Directory.CreateTempSubdirectory("guide-reachability-g645-").FullName;
            Directory.CreateDirectory(Path.Combine(RootPath, ".intent-cli"));
            Context = new CliContext
            {
                RepoRoot = RootPath,
                Config = new CliConfig
                {
                    Project = new ProjectConfig
                    {
                        Domain = "intent-cli",
                        ArtifactRoot = ".intent-cli",
                        WorktreeRoot = ".intent-cli/worktrees",
                    },
                },
            };
        }

        public string RootPath { get; }
        public CliContext Context { get; }

        public string RecordPath(string unit) => GuideReachabilityRecord.ResolveFullPath(RootPath, unit);

        public void WritePacket(string unit, string yaml)
        {
            var directory = Path.Combine(RootPath, ".intent-cli", "issues", unit);
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "packet.yaml"), yaml);
        }

        public void WriteCloseout(string unit, DateTimeOffset at)
        {
            var runLogPath = Context.GetRunLogPath();
            Directory.CreateDirectory(Path.GetDirectoryName(runLogPath)!);
            var line = IntentSystem.Supervisor.Serialization.RunLogSerializer.SerializeLine(
                new IntentSystem.Supervisor.Models.RunEvent
                {
                    Ts = at,
                    ExecutionUnit = unit,
                    Event = "closeout-recorded",
                    By = "intent-cli closeout pr",
                });
            File.AppendAllText(runLogPath, line + Environment.NewLine);
        }

        public JsonElement RunStalledWorkResult()
        {
            using var writer = new StringWriter();
            var exit = AutomationStalledWorkCommand.Execute(
                Context,
                ["--domain", "intent-cli", "--repo", Repo, "--format", "json"],
                writer);
            Assert.Equal(0, exit);
            var document = JsonDocument.Parse(writer.ToString());
            documents.Add(document);
            return document.RootElement;
        }

        public JsonElement RunStalledWork() => RunStalledWorkResult().GetProperty("items");

        public RecordRun RunRecord(string[] args)
        {
            using var writer = new StringWriter();
            var exit = AutomationGuideReachabilityRecordCommand.Execute(Context, args, writer);
            var document = JsonDocument.Parse(writer.ToString());
            documents.Add(document);
            return new RecordRun(exit, document.RootElement);
        }

        public void Dispose()
        {
            foreach (var document in documents)
            {
                document.Dispose();
            }

            try
            {
                Directory.Delete(RootPath, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }
}
