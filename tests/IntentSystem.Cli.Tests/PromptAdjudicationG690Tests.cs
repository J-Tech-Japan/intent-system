using System.Text.Json;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

[Collection("WorkerNextActionSharedState")]
public sealed class PromptAdjudicationG690Tests
{
    [Fact]
    public void DesignDeclaredClass_UsesSharedPipelineAndSeparatesActorFromExecutor()
    {
        var recipe = Recipe(answerableBy: "design");
        var observation = Observation(recipe);
        var policy = PolicyFor(recipe);

        var authorization = PromptAdjudicationPipeline.Evaluate(
            observation,
            policy,
            actorRole: "design",
            cwd: "/repo");

        Assert.Equal("accept", authorization.Decision);
        Assert.Equal("design", authorization.AnswerableBy);
        Assert.Equal("design", authorization.DecisionActorRole);
        Assert.Equal("herdr:agent send-keys", authorization.MechanicalExecutor);
        Assert.Equal(["2", "enter"], authorization.AnswerKeys);
    }

    [Fact]
    public void OrchestrationOnlyClass_RefusesDesignEvenWithAnAcceptRule()
    {
        var recipe = Recipe(answerableBy: "orchestration");
        var authorization = PromptAdjudicationPipeline.Evaluate(
            Observation(recipe),
            PolicyFor(recipe),
            actorRole: "design",
            cwd: "/repo");

        Assert.Equal("escalate", authorization.Decision);
        Assert.Contains("capability-denied", authorization.Rule, StringComparison.Ordinal);
        Assert.Empty(authorization.AnswerKeys);
        Assert.Null(authorization.MechanicalExecutor);
    }

    [Theory]
    [InlineData("destructive")]
    [InlineData("credential")]
    [InlineData("permission-change")]
    [InlineData("security")]
    [InlineData("product-decision")]
    [InlineData("unverifiable")]
    public void DesignClass_HardRiskFloorCannotBeOverridden(string riskTag)
    {
        var recipe = Recipe(answerableBy: "design", riskTags: [riskTag]);
        var authorization = PromptAdjudicationPipeline.Evaluate(
            Observation(recipe),
            PolicyFor(recipe),
            actorRole: "design",
            cwd: "/repo");

        Assert.Equal("escalate", authorization.Decision);
        Assert.Contains(riskTag, authorization.Summary, StringComparison.Ordinal);
        Assert.Empty(authorization.AnswerKeys);
    }

    [Fact]
    public void LiveDialogCas_RefusesPaneSequenceOrTextMutation()
    {
        var hash = PromptDialogCas.HashText("recorded approval");

        Assert.True(PromptDialogCas.Verify("w:p2", "w:p2", 7, 7, hash, hash).Matches);
        Assert.False(PromptDialogCas.Verify("w:p2", "w:p2", 7, 8, hash, hash).Matches);
        Assert.False(PromptDialogCas.Verify("w:p2", "w:p2", 7, 7, hash, PromptDialogCas.HashText("new approval")).Matches);
        Assert.False(PromptDialogCas.Verify("w:p2", "w:p3", 7, 7, hash, hash).Matches);
        Assert.False(PromptDialogCas.Verify("w:p2", "w:p2", null, 7, hash, hash).Matches);
    }

    [Fact]
    public void PromptAudit_PersistsDecisionActorAndMechanicalExecutor()
    {
        var audit = new NotifyPromptAudit
        {
            PromptKey = "prompt",
            Seat = "design",
            Pane = "w:p2",
            AgentKind = "codex",
            PromptClass = "synthetic-design-class",
            Rule = "codex:synthetic-design-class",
            Actor = "design",
            DecisionActorRole = "design",
            MechanicalExecutor = "herdr:agent send-keys",
            ScopeOrRuleId = "codex:synthetic-design-class",
            StateChangeSequence = 7,
            ObservedTextHash = PromptDialogCas.HashText("approval"),
            Timestamp = DateTimeOffset.UtcNow,
            Outcome = "bounded-answer-executed",
        };

        var json = JsonSerializer.Serialize(audit);

        Assert.Contains("decision_actor_role", json, StringComparison.Ordinal);
        Assert.Contains("mechanical_executor", json, StringComparison.Ordinal);
        Assert.Contains("scope_or_rule_id", json, StringComparison.Ordinal);
        Assert.Contains("state_change_sequence", json, StringComparison.Ordinal);
        Assert.Contains("observed_text_hash", json, StringComparison.Ordinal);
    }

    [Fact]
    public void NotifyAdjudicate_IsDiscoverableAsCanonicalSurface()
    {
        using var writer = new StringWriter();

        var exit = CommandRouter.Execute(
            ["notify", "adjudicate", "--help"],
            new IntentSystem.Cli.CliContext
            {
                RepoRoot = Directory.GetCurrentDirectory(),
                Config = new IntentSystem.Cli.Models.CliConfig
                {
                    Project = new IntentSystem.Cli.Models.ProjectConfig
                    {
                        Domain = "g690",
                        ArtifactRoot = ".intent-cli",
                    },
                },
            },
            writer);

        Assert.Equal(0, exit);
        Assert.Contains("notify adjudicate", writer.ToString(), StringComparison.Ordinal);
        Assert.Contains("state-sequence", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void NotifyAdjudicate_RefusalUsesDeclaredActorAndNonzeroJsonExit()
    {
        using var writer = new StringWriter();
        var exit = CommandRouter.Execute(
            [
                "notify", "adjudicate",
                "--domain", "g690",
                "--team", "tests",
                "--actor-role", "review",
                "--agent-kind", "synthetic",
                "--prompt-class", "synthetic-design-class",
                "--pane", "w:p2",
                "--state-sequence", "7",
                "--text-hash", new string('a', 64),
                "--routing-root", "/definitely-missing-g690-topology",
                "--format", "json",
            ],
            new IntentSystem.Cli.CliContext
            {
                RepoRoot = Directory.GetCurrentDirectory(),
                Config = new IntentSystem.Cli.Models.CliConfig
                {
                    Project = new IntentSystem.Cli.Models.ProjectConfig
                    {
                        Domain = "g690",
                        ArtifactRoot = ".intent-cli",
                    },
                },
            },
            writer);

        using var document = JsonDocument.Parse(writer.ToString());
        Assert.Equal(1, exit);
        Assert.Equal(1, document.RootElement.GetProperty("exit_code").GetInt32());
        Assert.Equal("review", document.RootElement.GetProperty("result").GetProperty("ActorRole").GetString());
        Assert.Equal("topology-unresolved", document.RootElement.GetProperty("result").GetProperty("Rule").GetString());
    }

    [Fact]
    public void SupervisionState_ExposesOnlyRecordedCycleAsTrustedIdentity()
    {
        var now = DateTimeOffset.UtcNow;
        var state = new NotifySupervisionReadResult
        {
            Resolved = true,
            Directory = "/supervision",
            LastCycle = new NotifySupervisionCycle
            {
                CycleId = "recorded-cycle",
                StartedAt = now,
                CompletedAt = now,
                IntervalSeconds = 60,
            },
            ActiveStalls = new Dictionary<string, NotifySupervisionStallRecord>(StringComparer.Ordinal),
            StallHistory = [],
            PromptAudits = [],
        };

        Assert.Equal("recorded-cycle", state.TrustedCycleId);
    }

    [Fact]
    public void NotifyAdjudicate_RefusesCallerSuppliedStaleCycleIdentityBeforeExecution()
    {
        const string domain = "g690-cycle";
        const string team = "tests";
        const string workspace = "wG690";
        const string pane = "wG690:p2";
        const string prompt = "Allow GitHub to add a comment to a pull request?";
        var root = Directory.CreateTempSubdirectory("intent-g690-cycle-").FullName;
        try
        {
            var context = new CliContext
            {
                RepoRoot = root,
                Config = new CliConfig
                {
                    Project = new ProjectConfig { Domain = domain, ArtifactRoot = ".intent-cli" },
                    Supervision = new SupervisionConfig
                    {
                        ArtifactRoot = Path.Combine(root, "supervision"),
                    },
                },
            };

            var topologyPath = NotifyRoleTopologyStore.ResolvePath(root, domain, team);
            Directory.CreateDirectory(Path.GetDirectoryName(topologyPath)!);
            File.WriteAllText(topologyPath, JsonSerializer.Serialize(new
            {
                domain,
                team,
                workspace_id = workspace,
                roles = new Dictionary<string, object>
                {
                    ["review"] = new
                    {
                        resident = "herdr",
                        workspace_id = workspace,
                        pane_id = pane,
                        kind = "codex",
                    },
                },
            }));

            var cyclePath = NotifySupervisionStore.ResolveCyclePath(
                context.ResolveSupervisionArtifactRootPath(), domain, team);
            var cycleWrite = NotifySupervisionStore.RecordCycle(
                cyclePath,
                new NotifySupervisionCycle
                {
                    CycleId = "recorded-cycle",
                    StartedAt = DateTimeOffset.UtcNow,
                    CompletedAt = DateTimeOffset.UtcNow,
                    IntervalSeconds = 60,
                },
                write: true);
            Assert.True(cycleWrite.Applied, cycleWrite.Error);

            var runner = new AdjudicateRunner(workspace, pane, prompt);
            NotifyCommand.ProcessRunnerFactory = () => runner;
            using var writer = new StringWriter();
            var exit = NotifyAdjudicateCommand.Execute(
                context,
                [
                    "--domain", domain,
                    "--team", team,
                    "--actor-role", "review",
                    "--agent-kind", "codex",
                    "--prompt-class", "github-comment-post",
                    "--pane", pane,
                    "--state-sequence", "7",
                    "--text-hash", PromptDialogCas.HashText(prompt),
                    "--cycle-id", "caller-stale",
                    "--routing-root", root,
                    "--format", "json",
                ],
                writer);

            using var document = JsonDocument.Parse(writer.ToString());
            Assert.Equal(1, exit);
            Assert.Equal(1, document.RootElement.GetProperty("exit_code").GetInt32());
            var result = document.RootElement.GetProperty("result");
            Assert.Equal("cycle-identity-mismatch", result.GetProperty("Rule").GetString());
            Assert.Equal("review", result.GetProperty("ActorRole").GetString());
            Assert.DoesNotContain(runner.Calls, call => call.Arguments.Take(2).SequenceEqual(["agent", "send-keys"]));
        }
        finally
        {
            NotifyCommand.ProcessRunnerFactory = null;
            Directory.Delete(root, recursive: true);
        }
    }

    private sealed class AdjudicateRunner(string workspace, string pane, string prompt) : INotifyProcessRunner
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
                                name = "review",
                                workspace_id = workspace,
                                pane_id = pane,
                                agent = "codex",
                                agent_session = new { id = "review" },
                                agent_status = "working",
                                interactive_ready = true,
                                state_change_seq = 7L,
                            },
                        },
                    },
                }), string.Empty);
            }

            if (arguments.Take(3).SequenceEqual(["agent", "read", pane]))
            {
                return new NotifyProcessResult(0, prompt, string.Empty);
            }

            throw new InvalidOperationException($"Unexpected herdr invocation: {string.Join(' ', arguments)}");
        }
    }

    private static AgentPromptClassRecipe Recipe(
        string answerableBy,
        IReadOnlyList<string>? riskTags = null) => new()
    {
        PromptClass = "synthetic-design-class",
        LiteralTextFragments = ["Synthetic approval"],
        ExactAnswerScope = "Synthetic class only",
        AnswerKeys = ["2", "enter"],
        Provenance = "G690 test measurement",
        AnswerableBy = answerableBy,
        RiskTags = riskTags ?? [],
    };

    private static AgentPromptClassObservation Observation(AgentPromptClassRecipe recipe) => new()
    {
        AgentKind = "synthetic",
        PromptClass = recipe.PromptClass,
        ObservedText = "Synthetic approval",
        Recipe = recipe,
    };

    private static NotifyPreApprovalPolicy PolicyFor(AgentPromptClassRecipe recipe) => new()
    {
        Domain = "g690",
        Team = "tests",
        RecordedAt = DateTimeOffset.UtcNow,
        Accept = [new NotifyPreApprovalRule
        {
            AgentKind = "synthetic",
            PromptClass = recipe.PromptClass,
            Applicable = true,
        }],
        Escalate = [],
        Applicable = true,
    };
}
