using System.Text.Json;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

[Collection("WorkerNextActionSharedState")]
public sealed class ApprovalRecipeDriftG666Tests : IDisposable
{
    private const string Domain = "g666-domain";
    private const string Team = "g666-team";
    private readonly string root = Directory.CreateTempSubdirectory("intent-g666-").FullName;

    [Fact]
    public void LaunchShapeComparison_IgnoresArgumentOrderWhitespaceAndOptionalRoot()
    {
        var recipe = Assert.IsType<AgentLaunchRecipe>(AgentLaunchRecipeRegistry.Find("codex"));
        var process = new NotifyPaneProcess(
            17,
            "/work",
            "codex",
            "/usr/local/bin/codex",
            ["/usr/local/bin/codex", "--add-dir", "/work", "--ask-for-approval", "never", "--sandbox", "workspace-write"]);

        var result = AgentLaunchShapeComparer.Compare("codex", recipe, [process]);

        Assert.True(result.Resolved);
        Assert.True(result.Conforming, result.Summary);
        Assert.Equal(AgentLaunchEnvelopeDrift.None, result.Drift);
    }

    [Fact]
    public void LaunchShapeComparison_MissingSandboxIsAlarming()
    {
        var recipe = Assert.IsType<AgentLaunchRecipe>(AgentLaunchRecipeRegistry.Find("codex"));
        var process = Process(
            "/usr/local/bin/codex", "--ask-for-approval", "never", "--add-dir", "/work");

        var result = AgentLaunchShapeComparer.Compare("codex", recipe, [process]);

        Assert.False(result.Conforming);
        Assert.Equal(AgentLaunchEnvelopeDrift.Alarming, result.Drift);
        Assert.Contains("required sandbox mode", result.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public void LaunchShapeComparison_ExtraAndFewerRootsHaveAsymmetricSeverity()
    {
        var recorded = Assert.IsType<AgentLaunchRecipe>(AgentLaunchRecipeRegistry.Find("codex")) with
        {
            Invocation = "herdr agent start role --kind codex --pane pane -- --sandbox workspace-write "
                + "--ask-for-approval never --add-dir <role-work-root> --add-dir <host-routing-root>",
        };
        var extra = AgentLaunchShapeComparer.Compare("codex", recorded,
        [
            Process("/usr/local/bin/codex", "--sandbox", "workspace-write", "--ask-for-approval", "never",
                "--add-dir", "/work", "--add-dir", "/host", "--add-dir", "/unrelated"),
        ]);
        var fewer = AgentLaunchShapeComparer.Compare("codex", recorded,
        [
            Process("/usr/local/bin/codex", "--sandbox", "workspace-write", "--ask-for-approval", "never",
                "--add-dir", "/work"),
        ]);

        Assert.Equal(AgentLaunchEnvelopeDrift.Alarming, extra.Drift);
        Assert.Contains("extra writable root", extra.Summary, StringComparison.Ordinal);
        Assert.Equal(AgentLaunchEnvelopeDrift.Informational, fewer.Drift);
        Assert.Contains("fewer writable root", fewer.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public void LaunchShapeComparison_ModelAndReasoningWishOnlyDifferenceIsSilentForRealShapedArgv()
    {
        var recipe = Assert.IsType<AgentLaunchRecipe>(AgentLaunchRecipeRegistry.Find("codex"));
        var process = Process(
            "/usr/local/bin/codex", "--model", "gpt-5.6-terra", "-c", "model_reasoning_effort=high",
            "--sandbox", "workspace-write", "--ask-for-approval", "never", "--add-dir", "/work");

        var result = AgentLaunchShapeComparer.Compare("codex", recipe, [process]);

        Assert.True(result.Conforming, result.Summary);
        Assert.Equal(AgentLaunchEnvelopeDrift.None, result.Drift);
        Assert.Contains("model and reasoning effort are excluded", result.Summary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void LaunchShapeComparison_BroaderNetworkIsAlarming()
    {
        var recipe = Assert.IsType<AgentLaunchRecipe>(AgentLaunchRecipeRegistry.Find("codex"));
        var process = Process(
            "/usr/local/bin/codex", "--sandbox", "workspace-write", "--ask-for-approval", "never",
            "--add-dir", "/work", "-c", "sandbox_workspace_write.network_access=true");

        var result = AgentLaunchShapeComparer.Compare("codex", recipe, [process]);

        Assert.Equal(AgentLaunchEnvelopeDrift.Alarming, result.Drift);
        Assert.Contains("network access", result.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public void Policy_IsDurableAndEscalateOnlyWhileItsProducerIsAbsent()
    {
        NotifyPromptClassProducerRegistry.AvailabilityOverride = _ => false;
        var policy = new NotifyPreApprovalPolicy
        {
            Domain = Domain,
            Team = Team,
            RecordedAt = DateTimeOffset.Parse("2026-08-11T10:00:00Z"),
            Accept = [new NotifyPreApprovalRule { AgentKind = "codex", PromptClass = "read-only" }],
            Escalate = [new NotifyPreApprovalRule { AgentKind = "codex", PromptClass = "credential" }],
        };

        Assert.Equal("escalate", NotifyPreApprovalPolicyStore.Adjudicate(null, "codex", "read-only"));
        Assert.True(NotifyPreApprovalPolicyStore.Record(root, policy, write: true).Applied);
        var read = NotifyPreApprovalPolicyStore.Read(root, Domain, Team);
        Assert.True(read.Resolved, read.Error);
        Assert.False(read.Policy!.Applicable);
        Assert.False(Assert.Single(read.Policy.Accept).Applicable);
        Assert.Equal("escalate", NotifyPreApprovalPolicyStore.Adjudicate(read.Policy, "CODEX", "read-only"));
        Assert.Equal("escalate", NotifyPreApprovalPolicyStore.Adjudicate(read.Policy, "codex", "credential"));
        Assert.Equal("escalate", NotifyPreApprovalPolicyStore.Adjudicate(read.Policy, "codex", "unmatched"));
    }

    [Theory]
    [InlineData("agmsg", false, "markdown")]
    [InlineData("agmsg", false, "json")]
    [InlineData("agmsg", true, "markdown")]
    [InlineData("agmsg", true, "json")]
    [InlineData("herdr-only", false, "markdown")]
    [InlineData("herdr-only", false, "json")]
    [InlineData("herdr-only", true, "markdown")]
    [InlineData("herdr-only", true, "json")]
    public void BothGuides_RenderApprovalContractAcrossModesFormatsAndTeamScopes(
        string mode,
        bool withTeam,
        string format)
    {
        var context = CreateContext();
        using (var modeWriter = new StringWriter())
        {
            var modeArgs = new List<string> { "--domain", Domain, "--mode", mode, "--write", "--format", "json" };
            if (withTeam) modeArgs.InsertRange(2, ["--team", Team]);
            Assert.Equal(0, SessionLayerCommand.ExecuteSet(context, modeArgs.ToArray(), modeWriter));
        }

        var common = new List<string> { "--domain", Domain };
        if (withTeam) common.AddRange(["--team", Team]);

        using var designWriter = new StringWriter();
        Assert.Equal(0, GuideDesignThreadCommand.Execute(
            context,
            common.Concat(["--format", format]).ToArray(),
            designWriter));
        AssertGuideContract(designWriter.ToString());
        Assert.Contains("never relays keystrokes", designWriter.ToString(), StringComparison.OrdinalIgnoreCase);

        var orchestratorArgs = common
            .Concat(["--target-repo", "owner/repo", "--agent", "codex", "--format", format])
            .ToArray();
        using var orchestratorWriter = new StringWriter();
        Assert.Equal(0, GuideOrchestratorThreadCommand.Execute(context, orchestratorArgs, orchestratorWriter));
        AssertGuideContract(orchestratorWriter.ToString());
        Assert.Contains("design never", orchestratorWriter.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("sandbox mode", orchestratorWriter.ToString(), StringComparison.Ordinal);
        Assert.Contains("approval mode", orchestratorWriter.ToString(), StringComparison.Ordinal);
        Assert.Contains("network access", orchestratorWriter.ToString(), StringComparison.Ordinal);
        Assert.Contains("Model and reasoning effort", orchestratorWriter.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Supervision_EmitsOneDriftFindingOrStaysSilentWhenConforming(bool conforming)
    {
        var context = CreateContext();
        using (var writer = new StringWriter())
        {
            Assert.Equal(0, SessionLayerCommand.ExecuteSet(
                context,
                ["--domain", Domain, "--team", Team, "--mode", "herdr-only", "--write", "--format", "json"],
                writer));
        }
        WriteTopology();
        var runner = new FakeRunner(conforming);
        var supervisor = new NotifyMeasuredSupervisor(
            context,
            root,
            Domain,
            Team,
            repo: null,
            ownerRole: "orchestration",
            intervalSeconds: 300,
            declaredBoundSeconds: null,
            staleMinutes: 45,
            claimedSilentMinutes: 720,
            backlogIdleMinutes: 45,
            repairSilentMinutes: 180,
            autoRedispatch: false,
            write: false,
            format: "json",
            runner,
            herdrExecutable: "fake-herdr",
            agmsgScriptsDirectory: "unused");

        var pass = supervisor.RunOnce();
        var drift = pass.Findings.Where(item => item.Kind == "recipe-drift").ToArray();
        if (conforming)
        {
            Assert.Empty(drift);
        }
        else
        {
            var finding = Assert.Single(drift);
            Assert.Contains("observed launch shape", finding.Summary, StringComparison.Ordinal);
            Assert.Contains("recorded 'codex' recipe", finding.Summary, StringComparison.Ordinal);
            Assert.Contains("Model and reasoning effort", finding.Summary, StringComparison.Ordinal);
            Assert.Equal("recipe-envelope-alarming", finding.Cause);
            Assert.False(finding.WakeAttempted);
        }
        Assert.DoesNotContain(runner.Calls, call =>
            call.Arguments.Contains("send-keys")
            || call.Arguments.Contains("send-text")
            || call.Arguments.Take(2).SequenceEqual(["agent", "start"]));
    }

    [Fact]
    public void Supervision_PersistentDriftEmitsExactlyOnceInEachCycleWithoutAccumulatingOrActing()
    {
        var context = CreateContext();
        using (var writer = new StringWriter())
        {
            Assert.Equal(0, SessionLayerCommand.ExecuteSet(
                context,
                ["--domain", Domain, "--team", Team, "--mode", "herdr-only", "--write", "--format", "json"],
                writer));
        }
        WriteTopology();
        var runner = new FakeRunner(conforming: false);
        var supervisor = new NotifyMeasuredSupervisor(
            context,
            root,
            Domain,
            Team,
            repo: null,
            ownerRole: "orchestration",
            intervalSeconds: 300,
            declaredBoundSeconds: null,
            staleMinutes: 45,
            claimedSilentMinutes: 720,
            backlogIdleMinutes: 45,
            repairSilentMinutes: 180,
            autoRedispatch: false,
            write: true,
            format: "json",
            runner,
            herdrExecutable: "fake-herdr",
            agmsgScriptsDirectory: "unused");

        var first = supervisor.RunOnce();
        var second = supervisor.RunOnce();

        Assert.Single(first.Findings, item => item.Kind == "recipe-drift");
        Assert.Single(second.Findings, item => item.Kind == "recipe-drift");
        Assert.DoesNotContain(runner.Calls, call =>
            call.Arguments.Contains("send-keys")
            || call.Arguments.Contains("send-text")
            || call.Arguments.Take(2).SequenceEqual(["agent", "start"]));
    }

    [Fact]
    public void EnglishJapaneseAndPreviewLedgersMirrorTheContract()
    {
        foreach (var path in new[]
        {
            "docs/en/12-agent-message-orchestration.md",
            "docs/ja/12-agent-message-orchestration.md",
        })
        {
            var doc = ReadRepoFile(path);
            Assert.Contains("G666", doc, StringComparison.Ordinal);
            Assert.Contains("escalate-only", doc, StringComparison.Ordinal);
            Assert.Contains("recipe-drift", doc, StringComparison.Ordinal);
            Assert.Contains("sandbox mode", doc, StringComparison.Ordinal);
            Assert.Contains("approval mode", doc, StringComparison.Ordinal);
            Assert.Contains("network access", doc, StringComparison.Ordinal);
            Assert.Contains("model", doc, StringComparison.Ordinal);
            Assert.Contains("reasoning effort", doc, StringComparison.Ordinal);
            Assert.Contains("recipe-envelope-alarming", doc, StringComparison.Ordinal);
            Assert.Contains("recipe-envelope-narrower", doc, StringComparison.Ordinal);
            Assert.Contains("intent-cli guide orchestrator-thread", doc, StringComparison.Ordinal);
            Assert.Contains("intent-cli guide design-thread", doc, StringComparison.Ordinal);
        }
        foreach (var path in new[] { "docs/en/1.0-compatibility-ledger.md", "docs/ja/1.0-compatibility-ledger.md" })
        {
            var ledger = ReadRepoFile(path);
            Assert.Contains("per-team residual pre-approval policy", ledger, StringComparison.Ordinal);
            Assert.Contains("G684", ledger, StringComparison.Ordinal);
            Assert.Contains("recipe-envelope-alarming", ledger, StringComparison.Ordinal);
            Assert.Contains("preview-through-1.x", ledger, StringComparison.Ordinal);
        }
    }

    private static void AssertGuideContract(string output)
    {
        Assert.Contains("G666", output, StringComparison.Ordinal);
        Assert.Contains("escalate-only", output, StringComparison.Ordinal);
        Assert.Contains("four judgment-bearing threads plus one supervision process", output, StringComparison.Ordinal);
        Assert.Contains("2026-08-11", output, StringComparison.Ordinal);
        Assert.Contains("wK", output, StringComparison.Ordinal);
    }

    private static NotifyPaneProcess Process(params string[] argv) =>
        new(17, "/work", "codex", argv[0], argv, string.Join(' ', argv));

    private CliContext CreateContext() => new()
    {
        RepoRoot = root,
        Config = new CliConfig
        {
            Project = new ProjectConfig { Domain = Domain, ArtifactRoot = ".intent-cli", WorktreeRoot = ".intent-cli/worktrees" },
        },
    };

    private void WriteTopology()
    {
        var path = NotifyRoleTopologyStore.ResolvePath(root, Domain, Team);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(new
        {
            domain = Domain,
            team = Team,
            workspace_id = "wG666",
            roles = new Dictionary<string, object>
            {
                ["orchestration"] = new { resident = "herdr", workspace_id = "wG666", pane_id = "wG666:p1", kind = "codex" },
            },
        }));
    }

    private sealed class FakeRunner(bool conforming) : INotifyProcessRunner
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
                            new { name = "orchestration", workspace_id = "wG666", pane_id = "wG666:p1", agent = "codex", agent_session = new { id = "a" }, agent_status = "working", interactive_ready = true },
                        },
                    },
                }), string.Empty);
            }
            if (arguments.SequenceEqual(["pane", "process-info", "--pane", "wG666:p1"]))
            {
                var argv = conforming
                    ? new[] { "/usr/local/bin/codex", "--add-dir", "/work", "--ask-for-approval", "never", "--sandbox", "workspace-write" }
                    : new[] { "/usr/local/bin/codex", "--sandbox", "workspace-write" };
                return new NotifyProcessResult(0, JsonSerializer.Serialize(new
                {
                    result = new { process_info = new { foreground_processes = new[] { new { pid = 23, cwd = "/work", name = "codex", argv0 = argv[0], argv, cmdline = string.Join(' ', argv) } } } },
                }), string.Empty);
            }
            return new NotifyProcessResult(0, string.Empty, string.Empty);
        }
    }

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

    public void Dispose()
    {
        NotifyPromptClassProducerRegistry.AvailabilityOverride = null;
        NotifyCommand.UtcNowFactory = null;
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
    }
}
