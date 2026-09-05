using System.Text.Json;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

[Collection("WorkerNextActionSharedState")]
public sealed class PromptAdjudicationG799Tests : IDisposable
{
    private const string Domain = "g799-cas";
    private const string Team = "g799-team";
    private const string Workspace = "wG799";
    private const string Pane = "wG799:p2";
    private const long StateSequence = 7;
    private const string Prompt = "Allow GitHub to add a comment to a pull request?";

    public void Dispose() => NotifyCommand.ProcessRunnerFactory = null;

    [Fact]
    public void Help_DocumentsCasDerivationAndCanonicalLivePairSurface_G799()
    {
        using var writer = new StringWriter();
        var exit = CommandRouter.Execute(
            ["notify", "adjudicate", "--help"],
            CreateContext(Directory.GetCurrentDirectory()),
            writer);

        Assert.Equal(0, exit);
        var output = writer.ToString();
        Assert.Contains("state_change_seq", output, StringComparison.Ordinal);
        Assert.Contains("herdr agent read <pane> --source detection --lines 200", output, StringComparison.Ordinal);
        Assert.Contains("SHA-256", output, StringComparison.Ordinal);
        Assert.Contains("notify adjudicate live-pair", output, StringComparison.Ordinal);
        Console.WriteLine($"G799 AC1 help derivation evidence:\n{output}");
    }

    [Fact]
    public void LivePair_DerivesInputsThatAuthorizeSubsequentAdjudication_G799()
    {
        using var fixture = CreateFixture();
        NotifyCommand.ProcessRunnerFactory = () => fixture.Runner;

        using var pairWriter = new StringWriter();
        var pairExit = NotifyAdjudicateCommand.Execute(
            fixture.Context,
            [
                "live-pair",
                "--domain", Domain,
                "--team", Team,
                "--pane", Pane,
                "--routing-root", fixture.Root,
                "--herdr-executable", "fake-herdr",
                "--format", "json",
            ],
            pairWriter);

        Assert.Equal(0, pairExit);
        using var pairDocument = JsonDocument.Parse(pairWriter.ToString());
        var pair = pairDocument.RootElement.GetProperty("result");
        Assert.Equal(Pane, pair.GetProperty("pane").GetString());
        Assert.Equal(StateSequence, pair.GetProperty("state_sequence").GetInt64());
        Assert.Equal(PromptDialogCas.HashText(Prompt), pair.GetProperty("text_hash").GetString());
        Assert.Contains("agent read", pair.GetProperty("text_source").GetString(), StringComparison.Ordinal);

        using var adjudicationWriter = new StringWriter();
        var adjudicationExit = NotifyAdjudicateCommand.Execute(
            fixture.Context,
            AdjudicationArguments(
                fixture.Root,
                StateSequence.ToString(System.Globalization.CultureInfo.InvariantCulture),
                pair.GetProperty("text_hash").GetString()!),
            adjudicationWriter);

        Assert.Equal(0, adjudicationExit);
        using var adjudicationDocument = JsonDocument.Parse(adjudicationWriter.ToString());
        var result = adjudicationDocument.RootElement.GetProperty("result");
        Assert.Equal("accept", result.GetProperty("Decision").GetString());
        Assert.DoesNotContain(fixture.Runner.Calls, call => call.Arguments.Take(2).SequenceEqual(["agent", "send-keys"]));
        Console.WriteLine($"G799 AC2/AC6 live-pair evidence:\n{pairWriter}{adjudicationWriter}");
    }

    [Fact]
    public void HashMismatch_DistinguishesWrongProjectionFromDialogChange_G799()
    {
        using var unchanged = CreateFixture();
        NotifyCommand.ProcessRunnerFactory = () => unchanged.Runner;
        using var wrongWriter = new StringWriter();
        var wrongExit = NotifyAdjudicateCommand.Execute(
            unchanged.Context,
            AdjudicationArguments(
                unchanged.Root,
                StateSequence.ToString(System.Globalization.CultureInfo.InvariantCulture),
                PromptDialogCas.HashText(Prompt + " projected differently")),
            wrongWriter);

        using var wrongDocument = JsonDocument.Parse(wrongWriter.ToString());
        var wrong = wrongDocument.RootElement.GetProperty("result");
        Assert.Equal(1, wrongExit);
        Assert.Equal("wrong-projection-hash-mismatch", wrong.GetProperty("Rule").GetString());
        Assert.Contains("unchanged", wrong.GetProperty("Summary").GetString(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(unchanged.Runner.Calls, call => call.Arguments.Take(2).SequenceEqual(["agent", "send-keys"]));

        using var changed = CreateFixture(changedPrompt: Prompt + " changed");
        NotifyCommand.ProcessRunnerFactory = () => changed.Runner;
        using var changedWriter = new StringWriter();
        var changedExit = NotifyAdjudicateCommand.Execute(
            changed.Context,
            AdjudicationArguments(
                changed.Root,
                StateSequence.ToString(System.Globalization.CultureInfo.InvariantCulture),
                PromptDialogCas.HashText(Prompt),
                write: true),
            changedWriter);

        using var changedDocument = JsonDocument.Parse(changedWriter.ToString());
        var changedResult = changedDocument.RootElement.GetProperty("result");
        Assert.Equal(1, changedExit);
        Assert.Equal("escalate", changedResult.GetProperty("Decision").GetString());
        Assert.Equal("dialog-changed-hash-mismatch", changedResult.GetProperty("Rule").GetString());
        Assert.Contains("changed", changedResult.GetProperty("Summary").GetString(), StringComparison.OrdinalIgnoreCase);
        Assert.Empty(changedResult.GetProperty("AnswerKeys").EnumerateArray());
        Assert.Null(changedResult.GetProperty("MechanicalExecutor").GetString());
        Assert.DoesNotContain(changed.Runner.Calls, call => call.Arguments.Take(2).SequenceEqual(["agent", "send-keys"]));
        Assert.NotEqual(wrong.GetProperty("Rule").GetString(), changedResult.GetProperty("Rule").GetString());
        Console.WriteLine($"G799 AC4 hash diagnosis evidence:\nwrong_projection={wrongWriter}\ndialog_changed={changedWriter}");
    }

    [Fact]
    public void SequenceMismatch_NamesStateChangeSourceAndRefusesBeforeExecution_G799()
    {
        using var fixture = CreateFixture();
        NotifyCommand.ProcessRunnerFactory = () => fixture.Runner;
        using var writer = new StringWriter();
        var exit = NotifyAdjudicateCommand.Execute(
            fixture.Context,
            AdjudicationArguments(
                fixture.Root,
                (StateSequence - 1).ToString(System.Globalization.CultureInfo.InvariantCulture),
                PromptDialogCas.HashText(Prompt)),
            writer);

        using var document = JsonDocument.Parse(writer.ToString());
        var result = document.RootElement.GetProperty("result");
        Assert.Equal(1, exit);
        Assert.Equal("dialog-changed-sequence", result.GetProperty("Rule").GetString());
        Assert.Contains("state_change_seq", result.GetProperty("Summary").GetString(), StringComparison.Ordinal);
        Assert.DoesNotContain(fixture.Runner.Calls, call => call.Arguments.Take(2).SequenceEqual(["agent", "send-keys"]));
        Console.WriteLine($"G799 AC3 sequence evidence:\n{writer}");
    }

    [Fact]
    public void StrictCas_HasNoForceBypassAndNoWarningFallback_G799()
    {
        using var writer = new StringWriter();
        var exit = CommandRouter.Execute(
            ["notify", "adjudicate", "--help"],
            CreateContext(Directory.GetCurrentDirectory()),
            writer);

        Assert.Equal(0, exit);
        Assert.DoesNotContain("--force", writer.ToString(), StringComparison.Ordinal);
        var sequence = PromptDialogCas.Verify(Pane, Pane, StateSequence, StateSequence + 1, "hash", "hash");
        var hash = PromptDialogCas.Verify(Pane, Pane, StateSequence, StateSequence, "hash", "other");
        Assert.False(sequence.Matches);
        Assert.False(hash.Matches);
        Assert.Contains("ref", sequence.Summary, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(PromptDialogCas.TextHashMismatch, hash.Cause);
        Assert.Contains("caller must hash", hash.Summary, StringComparison.OrdinalIgnoreCase);
        Console.WriteLine($"G799 AC5 strict CAS evidence:\nforce_flag_present={writer.ToString().Contains("--force", StringComparison.Ordinal)}\nsequence={sequence.Cause}: {sequence.Summary}\nhash={hash.Cause}: {hash.Summary}");
    }

    private static string[] AdjudicationArguments(
        string root,
        string stateSequence,
        string textHash,
        bool write = false) =>
    [
        "--domain", Domain,
        "--team", Team,
        "--actor-role", "orchestration",
        "--agent-kind", "codex",
        "--prompt-class", "github-comment-post",
        "--pane", Pane,
        "--state-sequence", stateSequence,
        "--text-hash", textHash,
        "--routing-root", root,
        "--herdr-executable", "fake-herdr",
        write ? "--write" : "--dry-run",
        "--format", "json",
    ];

    private static CliContext CreateContext(string root) => new()
    {
        RepoRoot = root,
        Config = new CliConfig
        {
            Project = new ProjectConfig { Domain = Domain, ArtifactRoot = ".intent-cli" },
            Supervision = new SupervisionConfig { ArtifactRoot = Path.Combine(root, "supervision") },
        },
    };

    private static Fixture CreateFixture(string? changedPrompt = null)
    {
        var root = Directory.CreateTempSubdirectory("intent-g799-").FullName;
        var context = CreateContext(root);

        using var modeWriter = new StringWriter();
        Assert.Equal(0, SessionLayerCommand.ExecuteSet(
            context,
            ["--domain", Domain, "--team", Team, "--mode", "herdr-only", "--write", "--format", "json"],
            modeWriter));

        var topologyPath = NotifyRoleTopologyStore.ResolvePath(root, Domain, Team);
        Directory.CreateDirectory(Path.GetDirectoryName(topologyPath)!);
        File.WriteAllText(topologyPath, JsonSerializer.Serialize(new
        {
            domain = Domain,
            team = Team,
            workspace_id = Workspace,
            roles = new Dictionary<string, object>
            {
                ["review"] = new
                {
                    resident = "herdr",
                    workspace_id = Workspace,
                    pane_id = Pane,
                    kind = "codex",
                },
            },
        }));

        var cyclePath = NotifySupervisionStore.ResolveCyclePath(
            context.ResolveSupervisionArtifactRootPath(), Domain, Team);
        var cycle = NotifySupervisionStore.RecordCycle(
            cyclePath,
            new NotifySupervisionCycle
            {
                CycleId = "g799-cycle",
                StartedAt = DateTimeOffset.UtcNow,
                CompletedAt = DateTimeOffset.UtcNow,
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
                RecordedAt = DateTimeOffset.UtcNow,
                Accept = [new NotifyPreApprovalRule { AgentKind = "codex", PromptClass = "github-comment-post" }],
                Escalate = [],
            },
            write: true);
        Assert.True(policy.Applied, policy.Error);

        return new Fixture(root, context, new G799Runner(Workspace, Pane, Prompt, changedPrompt));
    }

    private sealed class Fixture(string root, CliContext context, G799Runner runner) : IDisposable
    {
        public string Root { get; } = root;
        public CliContext Context { get; } = context;
        public G799Runner Runner { get; } = runner;

        public void Dispose()
        {
            NotifyCommand.ProcessRunnerFactory = null;
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }
    }

    private sealed class G799Runner(
        string workspace,
        string pane,
        string initialPrompt,
        string? changedPrompt) : INotifyProcessRunner
    {
        private int readCount;

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
                                name = "review",
                                workspace_id = workspace,
                                pane_id = pane,
                                agent = "codex",
                                agent_session = new { id = "review" },
                                agent_status = "working",
                                interactive_ready = true,
                                state_change_seq = StateSequence,
                            },
                        },
                    },
                }), string.Empty);
            }

            if (arguments.Take(3).SequenceEqual(["agent", "read", pane]))
            {
                readCount++;
                var prompt = changedPrompt is not null && readCount > 1 ? changedPrompt : initialPrompt;
                return new NotifyProcessResult(0, prompt, string.Empty);
            }

            throw new InvalidOperationException($"Unexpected fixture transport call: {fileName} {string.Join(' ', arguments)}");
        }
    }
}
