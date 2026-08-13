using System.Text.Json;
using IntentSystem.Cli.Commands;

namespace IntentSystem.Cli.Tests;

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
