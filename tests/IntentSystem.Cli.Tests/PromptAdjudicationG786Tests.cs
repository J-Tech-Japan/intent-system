using System.Text.Json;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

[Collection("WorkerNextActionSharedState")]
public sealed class PromptAdjudicationG786Tests : IDisposable
{
    private const string Domain = "g786-adjudicate";
    private const string Team = "g786-team";
    private const string Workspace = "wG786";
    private const string Pane = "wG786:p2";
    private const long StateSequence = 786;
    private const string CycleId = "g786-current-cycle";
    private const string FirstScratchPath = "/tmp/g781-evidence.GuzWkP";
    private const string SecondScratchPath = "/tmp/g781-default-evidence.iA5IUD";
    private readonly string root = Path.Combine(
        RepoVersionPolicySource.RepoRoot(),
        ".artifacts",
        "g786-adjudicate-" + Guid.NewGuid().ToString("N"));
    private readonly CliContext context;

    public PromptAdjudicationG786Tests()
    {
        Directory.CreateDirectory(root);
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
                Supervision = new SupervisionConfig { ArtifactRoot = ".intent-cli/supervision" },
            },
        };
    }

    public void Dispose() => NotifyCommand.ProcessRunnerFactory = null;

    [Fact]
    public void DryRun_AnswersVerbatimFiftyColumnDialogWithTheOwnedScratchScopeAndYesKey()
    {
        Prepare([FirstScratchPath, SecondScratchPath]);
        var runner = new FixtureRunner(ObservedDialog());
        NotifyCommand.ProcessRunnerFactory = () => runner;

        using var writer = new StringWriter();
        var exitCode = NotifyAdjudicateCommand.Execute(context, Arguments(), writer);

        Assert.Equal(0, exitCode);
        using var output = JsonDocument.Parse(writer.ToString());
        var result = output.RootElement.GetProperty("result");
        Assert.Equal("accept", result.GetProperty("Decision").GetString());
        Assert.Contains(
            result.GetProperty("MatchedScopes").EnumerateArray().Select(value => value.GetString()),
            value => value == "owned-scratch-delete");
        Assert.Contains(
            result.GetProperty("AnswerKeys").EnumerateArray().Select(value => value.GetString()),
            value => value == "y");
        Assert.False(result.GetProperty("Audited").GetBoolean());
        Assert.False(result.GetProperty("Executed").GetBoolean());
        Assert.DoesNotContain(runner.Calls, call => call.Arguments.Take(2).SequenceEqual(["agent", "send-keys"]));
    }

    [Fact]
    public void DryRun_VerbatimFiftyColumnDialogEscalatesAndNamesTheUncoveredScratchPath()
    {
        Prepare([FirstScratchPath]);
        var runner = new FixtureRunner(ObservedDialog());
        NotifyCommand.ProcessRunnerFactory = () => runner;

        using var writer = new StringWriter();
        var exitCode = NotifyAdjudicateCommand.Execute(context, Arguments(), writer);

        Assert.Equal(1, exitCode);
        using var output = JsonDocument.Parse(writer.ToString());
        var result = output.RootElement.GetProperty("result");
        Assert.Equal("escalate", result.GetProperty("Decision").GetString());
        Assert.Contains("shell-segment-out-of-scope", result.GetProperty("Rule").GetString(), StringComparison.Ordinal);
        Assert.Contains(SecondScratchPath, result.GetProperty("Summary").GetString(), StringComparison.Ordinal);
        Assert.DoesNotContain(runner.Calls, call => call.Arguments.Take(2).SequenceEqual(["agent", "send-keys"]));
    }

    private void Prepare(IReadOnlyList<string> scratchPaths)
    {
        var topologyPath = NotifyRoleTopologyStore.ResolvePath(root, Domain, Team);
        Directory.CreateDirectory(Path.GetDirectoryName(topologyPath)!);
        File.WriteAllText(topologyPath, JsonSerializer.Serialize(new
        {
            domain = Domain,
            team = Team,
            workspace_id = Workspace,
            roles = new Dictionary<string, object>
            {
                ["orchestration"] = new
                {
                    resident = "herdr",
                    workspace_id = Workspace,
                    pane_id = Pane,
                    kind = "codex",
                    cwd = "/repo",
                },
            },
        }));

        var cycle = NotifySupervisionStore.RecordCycle(
            NotifySupervisionStore.ResolveCyclePath(context.ResolveSupervisionArtifactRootPath(), Domain, Team),
            new NotifySupervisionCycle
            {
                CycleId = CycleId,
                StartedAt = new DateTimeOffset(2026, 9, 2, 12, 0, 0, TimeSpan.Zero),
                CompletedAt = new DateTimeOffset(2026, 9, 2, 12, 0, 1, TimeSpan.Zero),
                IntervalSeconds = 60,
            },
            write: true);
        Assert.True(cycle.Applied, cycle.Error);

        var policy = NotifyPreApprovalPolicyStore.Record(
            context.ResolveSupervisionArtifactRootPath(),
            new NotifyPreApprovalPolicy
            {
                Domain = Domain,
                Team = Team,
                RecordedAt = new DateTimeOffset(2026, 9, 2, 12, 0, 1, TimeSpan.Zero),
                Accept = [],
                Escalate = [],
                ScopedPolicies = [OwnedScratchPolicy(scratchPaths)],
            },
            write: true);
        Assert.True(policy.Applied, policy.Error);
    }

    private string[] Arguments() =>
    [
        "--domain", Domain,
        "--team", Team,
        "--actor-role", "orchestration",
        "--agent-kind", "codex",
        "--prompt-class", "shell-command",
        "--pane", Pane,
        "--state-sequence", StateSequence.ToString(System.Globalization.CultureInfo.InvariantCulture),
        "--text-hash", PromptDialogCas.HashText(ObservedDialog()),
        "--cycle-id", CycleId,
        "--routing-root", root,
        "--dry-run",
        "--format", "json",
    ];

    private static string ObservedDialog() => "Would you like to run the following command?\n"
        + "Environment: local\n"
        + "$ rm -rf " + FirstScratchPath + "\n"
        + SecondScratchPath + "\n"
        + "› 1. Yes, proceed (y)\n"
        + "  2. Yes, and don't ask again for commands that\n"
        + "     start with `rm -rf …`\n"
        + "  3. No, and tell Codex what to do differently\n"
        + "Press enter to confirm or esc to cancel";

    private static NotifyScopedPromptPolicy OwnedScratchPolicy(IReadOnlyList<string> paths) => new()
    {
        PolicyId = "g786-owned-scratch",
        AgentKind = "codex",
        PromptClass = "shell-command",
        Scope = "owned-scratch-delete",
        Decision = "accept",
        Category = "destructive-scratch-cleanup",
        ArgvTokenPrefix = ["rm", "-rf"],
        Cwd = "/repo",
        PathConstraints = paths,
        ScratchLedgerPaths = paths,
        ScratchLedgerCycleId = CycleId,
        EffectTags = ["destructive"],
    };

    private sealed class FixtureRunner(string prompt) : INotifyProcessRunner
    {
        public List<(string FileName, IReadOnlyList<string> Arguments)> Calls { get; } = [];

        public NotifyProcessResult Run(string fileName, IReadOnlyList<string> arguments)
        {
            Calls.Add((fileName, arguments.ToArray()));
            if (arguments.SequenceEqual(["agent", "list"]))
            {
                return new NotifyProcessResult(0, JsonSerializer.Serialize(new
                {
                    result = new
                    {
                        agents = new[]
                        {
                            new
                            {
                                name = "orchestration",
                                workspace_id = Workspace,
                                pane_id = Pane,
                                agent = "codex",
                                agent_session = new { id = "orchestration" },
                                agent_status = "working",
                                interactive_ready = true,
                                state_change_seq = StateSequence,
                                cwd = "/repo",
                            },
                        },
                    },
                }), string.Empty);
            }

            if (arguments.Take(3).SequenceEqual(["agent", "read", Pane]))
            {
                return new NotifyProcessResult(0, prompt, string.Empty);
            }

            throw new InvalidOperationException($"Unexpected fixture transport call: {fileName} {string.Join(' ', arguments)}");
        }
    }
}
