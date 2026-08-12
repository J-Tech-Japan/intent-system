using System.Text.Json;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

public sealed class GuideRoleContractG672Tests
{
    [Theory]
    [InlineData("design", "intent-cli guide design-thread")]
    [InlineData("orchestration", "intent-cli guide orchestrator-thread")]
    [InlineData("implementation", "intent-cli guide worker issue-to-pr")]
    [InlineData("review", "intent-cli guide review")]
    public void GuideNext_PutsTheInvokingRoleContractBeforeTheExistingProcedure_G672(string role, string guide)
    {
        using var writer = new StringWriter();
        Assert.Equal(0, GuideNextCommand.Execute(CreateContext(), ["--role", role], writer));

        var output = writer.ToString();
        var pointer = output.IndexOf("## Read your role contract first", StringComparison.Ordinal);
        var procedure = output.IndexOf("## Procedure", StringComparison.Ordinal);

        Assert.True(pointer >= 0);
        Assert.True(pointer < procedure);
        Assert.Contains($"operating guide: `{guide}`", output, StringComparison.Ordinal);
        Assert.Contains("before acting on the rest of this output", output, StringComparison.Ordinal);
        Assert.Contains("Do not force a reread on every wake", output, StringComparison.Ordinal);
        Assert.Contains("## Measured incident record (G672 — preview-through-1.x)", output, StringComparison.Ordinal);
    }

    [Fact]
    public void GuideNext_RoleWithoutAContractAddsNoInventedPointer_G672()
    {
        using var writer = new StringWriter();
        Assert.Equal(0, GuideNextCommand.Execute(
            CreateContext(),
            ["--role", "human", "--format", "json"],
            writer));

        using var document = JsonDocument.Parse(writer.ToString());
        var root = document.RootElement;
        Assert.Equal("human", root.GetProperty("invoking_role").GetString());
        Assert.False(root.TryGetProperty("role_contract_first", out _));
        Assert.Contains("issue #1441", root.GetProperty("measured_incident").GetString(), StringComparison.Ordinal);
        Assert.Contains("session-scoped nohup", root.GetProperty("measured_incident").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public void GuideOnboarding_PutsContractFirstWithoutRenumberingTheExistingSequence_G672()
    {
        using var writer = new StringWriter();
        Assert.Equal(0, GuideOnboardingCommand.Execute(
            CreateContext(),
            ["--role", "implementation"],
            writer));

        var output = writer.ToString();
        var pointer = output.IndexOf("## Read your role contract first", StringComparison.Ordinal);
        var summary = output.IndexOf("AI-agent onboarding smoke", StringComparison.Ordinal);
        var sequence = output.IndexOf("## First-call sequence", StringComparison.Ordinal);
        Assert.True(pointer >= 0);
        Assert.True(pointer < summary);
        Assert.True(summary < sequence);
        Assert.Contains("intent-cli guide worker issue-to-pr", output, StringComparison.Ordinal);
        Assert.Contains("### 1. `intent-cli guide model --format json`", output, StringComparison.Ordinal);
        Assert.Contains("### 10. `intent-cli automation summary --format json`", output, StringComparison.Ordinal);
    }

    [Fact]
    public void GuideOnboarding_RoleWithoutAContractHasNoNewPointer_G672()
    {
        using var writer = new StringWriter();
        Assert.Equal(0, GuideOnboardingCommand.Execute(
            CreateContext(),
            ["--role", "human", "--format", "json"],
            writer));

        using var document = JsonDocument.Parse(writer.ToString());
        var root = document.RootElement;
        Assert.False(root.TryGetProperty("role_contract_first", out _));
        Assert.Equal(10, root.GetProperty("first_call_sequence").GetArrayLength());
        Assert.Contains("seven findings", root.GetProperty("measured_incident").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public void SuperviseHelp_ExplainsEventModeWakeSource_G672()
    {
        using var writer = new StringWriter();
        Assert.Equal(0, NotifyCommand.ExecuteSupervise(CreateContext(), ["--help"], writer));

        var output = writer.ToString();
        Assert.Contains("--event-mode", output, StringComparison.Ordinal);
        Assert.Contains("blocking per-seat herdr wait", output, StringComparison.Ordinal);
        Assert.Contains("pane.agent_status_changed", output, StringComparison.Ordinal);
        Assert.Contains("normative SECOND wake source", output, StringComparison.Ordinal);
        Assert.Contains("interval safety floor", output, StringComparison.Ordinal);
    }

    [Fact]
    public void RoleAwareGuideHelp_AdvertisesTheOptionalRoleInput_G672()
    {
        using var next = new StringWriter();
        using var onboarding = new StringWriter();
        Assert.Equal(0, GuideNextCommand.Execute(CreateContext(), ["--help"], next));
        Assert.Equal(0, GuideOnboardingCommand.Execute(CreateContext(), ["--help"], onboarding));

        Assert.Contains("--role <role>", next.ToString(), StringComparison.Ordinal);
        Assert.Contains("first read-before-acting", next.ToString(), StringComparison.Ordinal);
        Assert.Contains("--role <role>", onboarding.ToString(), StringComparison.Ordinal);
        Assert.Contains("roles without a contract", onboarding.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void HerdrWakeGuide_NamesTheConcreteEventModeFlagWithoutChangingTheWakeContract_G672()
    {
        var markdown = HerdrOnlyOperatingGuide.RenderMarkdown([]);
        using var document = JsonDocument.Parse(HerdrOnlyOperatingGuide.CreateJson([]).ToJsonString());
        var stateChange = document.RootElement
            .GetProperty("wake_sources")
            .GetProperty("state_change");

        Assert.Contains("--event-mode", markdown, StringComparison.Ordinal);
        Assert.Contains("pane.agent_status_changed", markdown, StringComparison.Ordinal);
        Assert.Equal("--event-mode", stateChange.GetProperty("flag").GetString());
        Assert.Contains("notify supervise", stateChange.GetProperty("invocation").GetString(), StringComparison.Ordinal);
        Assert.Equal("events.subscribe", stateChange.GetProperty("method").GetString());
        Assert.Contains("carries no outcome", stateChange.GetProperty("role").GetString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("en")]
    [InlineData("ja")]
    public void GuidanceDocsAndLedgerRecordTheG672PreviewAndIncident(string language)
    {
        var root = RepoVersionPolicySource.RepoRoot();
        var orchestration = File.ReadAllText(Path.Combine(root, "docs", language, "12-agent-message-orchestration.md"));
        var commandReference = File.ReadAllText(Path.Combine(root, "docs", language, "08-command-reference.md"));
        var ledger = File.ReadAllText(Path.Combine(root, "docs", language, "1.0-compatibility-ledger.md"));

        foreach (var document in new[] { orchestration, commandReference, ledger })
        {
            Assert.Contains("G672", document, StringComparison.Ordinal);
            Assert.Contains("preview-through-1.x", document, StringComparison.Ordinal);
            Assert.Contains("--event-mode", document, StringComparison.Ordinal);
            Assert.Contains("pane.agent_status_changed", document, StringComparison.Ordinal);
            Assert.Contains("#1441", document, StringComparison.Ordinal);
        }
    }

    private static CliContext CreateContext() => new()
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
}
