using System.Text.Json;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

[Collection("WorkerNextActionSharedState")]
public sealed class PreApprovalApplicabilityG682Tests : IDisposable
{
    private const string Domain = "g682-domain";
    private const string Team = "g682-team";
    private readonly string root = Directory.CreateTempSubdirectory("intent-g682-").FullName;

    public PreApprovalApplicabilityG682Tests()
    {
        NotifyCommand.UtcNowFactory = () => DateTimeOffset.Parse("2026-08-12T18:00:00Z");
        NotifyCommand.ProcessRunnerFactory = () => new NoOpRunner();
        NotifyPromptClassProducerRegistry.AvailabilityOverride = _ => false;
    }

    [Fact]
    public void RecordStoredShapeAndEveryCycle_LoudlyStateInapplicability_ThenClearWithProducer_G682()
    {
        var recorded = RunSupervise(
            "--pre-approve", "codex:github-comment",
            "--pre-escalate", "codex:credential");

        Assert.Equal(0, recorded.ExitCode);
        Assert.False(recorded.Output.GetProperty("silent").GetBoolean());
        AssertInapplicable(recorded.Output.GetProperty("pre_approval_policy"), recorded: true);

        var policyPath = recorded.Output.GetProperty("pre_approval_policy").GetProperty("path").GetString()!;
        Assert.True(File.Exists(policyPath));
        using (var stored = JsonDocument.Parse(File.ReadAllText(policyPath)))
        {
            AssertInapplicable(stored.RootElement, recorded: null);
        }

        var nextCycle = RunSupervise();
        Assert.Equal(0, nextCycle.ExitCode);
        Assert.False(nextCycle.Output.GetProperty("silent").GetBoolean());
        AssertInapplicable(nextCycle.Output.GetProperty("pre_approval_policy"), recorded: true);

        NotifyPromptClassProducerRegistry.AvailabilityOverride = kind =>
            string.Equals(kind, "codex", StringComparison.OrdinalIgnoreCase);
        var producerCycle = RunSupervise();

        Assert.Equal(0, producerCycle.ExitCode);
        AssertApplicable(producerCycle.Output.GetProperty("pre_approval_policy"), recorded: true);
        using var refreshed = JsonDocument.Parse(File.ReadAllText(policyPath));
        AssertApplicable(refreshed.RootElement, recorded: null);
    }

    [Fact]
    public void GuidesAndDocs_StateEscalateOnlyInterimRecipePathAndMeasuredAttribution_G682()
    {
        foreach (var format in new[] { "markdown", "json" })
        {
            using var design = new StringWriter();
            Assert.Equal(0, GuideDesignThreadCommand.Execute(
                CreateContext(), ["--domain", Domain, "--team", Team, "--format", format], design));
            AssertGuide(design.ToString());

            using var orchestrator = new StringWriter();
            Assert.Equal(0, GuideOrchestratorThreadCommand.Execute(
                CreateContext(),
                ["--domain", Domain, "--team", Team, "--target-repo", "owner/repo", "--agent", "codex", "--format", format],
                orchestrator));
            AssertGuide(orchestrator.ToString());
        }

        foreach (var language in new[] { "en", "ja" })
        {
            var doc = ReadRepoFile($"docs/{language}/12-agent-message-orchestration.md");
            var ledger = ReadRepoFile($"docs/{language}/1.0-compatibility-ledger.md");
            AssertGuide(doc);
            Assert.Contains("G682", ledger, StringComparison.Ordinal);
            Assert.Contains("preview-through-1.x", ledger, StringComparison.Ordinal);
            Assert.Contains(NotifyPromptClassProducerRegistry.InapplicableStatus, ledger, StringComparison.Ordinal);
            Assert.Contains("G684", ledger, StringComparison.Ordinal);
        }
    }

    private (int ExitCode, JsonElement Output) RunSupervise(params string[] policyArguments)
    {
        using var writer = new StringWriter();
        var args = new List<string>
        {
            "notify", "supervise", "--domain", Domain, "--team", Team,
            "--routing-root", root, "--once", "--write", "--format", "json",
        };
        args.AddRange(policyArguments);
        var exitCode = CommandRouter.Execute(args.ToArray(), CreateContext(), writer);
        return (exitCode, JsonDocument.Parse(writer.ToString()).RootElement.Clone());
    }

    private static void AssertInapplicable(JsonElement policy, bool? recorded)
    {
        if (recorded is not null)
        {
            Assert.Equal(recorded.Value, policy.GetProperty("recorded").GetBoolean());
            Assert.Equal("recorded-inapplicable", policy.GetProperty("status").GetString());
        }
        Assert.False(policy.GetProperty("applicable").GetBoolean());
        Assert.Equal(
            NotifyPromptClassProducerRegistry.InapplicableStatus,
            policy.GetProperty("applicability_status").GetString());
        Assert.Equal("codex", Assert.Single(policy.GetProperty("inapplicable_agent_kinds").EnumerateArray()).GetString());
        Assert.Contains("cannot currently apply", policy.GetProperty("inapplicability_reason").GetString(), StringComparison.Ordinal);
        AssertRuleInapplicable(Assert.Single(policy.GetProperty("accept").EnumerateArray()));
        AssertRuleInapplicable(Assert.Single(policy.GetProperty("escalate").EnumerateArray()));
    }

    private static void AssertRuleInapplicable(JsonElement rule)
    {
        Assert.False(rule.GetProperty("applicable").GetBoolean());
        Assert.Equal(
            NotifyPromptClassProducerRegistry.InapplicableStatus,
            rule.GetProperty("applicability_status").GetString());
        Assert.Contains("No prompt-class producer", rule.GetProperty("inapplicability_reason").GetString(), StringComparison.Ordinal);
    }

    private static void AssertApplicable(JsonElement policy, bool? recorded)
    {
        if (recorded is not null)
        {
            Assert.Equal(recorded.Value, policy.GetProperty("recorded").GetBoolean());
            Assert.Equal("recorded", policy.GetProperty("status").GetString());
        }
        Assert.True(policy.GetProperty("applicable").GetBoolean());
        Assert.Equal(
            NotifyPromptClassProducerRegistry.ApplicableStatus,
            policy.GetProperty("applicability_status").GetString());
        Assert.Empty(policy.GetProperty("inapplicable_agent_kinds").EnumerateArray());
        Assert.All(policy.GetProperty("accept").EnumerateArray(), rule => Assert.True(rule.GetProperty("applicable").GetBoolean()));
        Assert.All(policy.GetProperty("escalate").EnumerateArray(), rule => Assert.True(rule.GetProperty("applicable").GetBoolean()));
    }

    private static void AssertGuide(string output)
    {
        var normalized = string.Join(' ', output.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        Assert.Contains("G683", output, StringComparison.Ordinal);
        Assert.True(
            normalized.Contains("escalate-only by construction", StringComparison.Ordinal)
            || normalized.Contains("構造上 escalate-only", StringComparison.Ordinal),
            "The escalate-only-by-construction rule was absent.");
        Assert.Contains("agent-side allow configuration", normalized, StringComparison.Ordinal);
        Assert.Contains("G636", output, StringComparison.Ordinal);
        Assert.Contains("#1469", output, StringComparison.Ordinal);
        Assert.Contains("47", output, StringComparison.Ordinal);
        Assert.True(
            output.Contains("three", StringComparison.OrdinalIgnoreCase)
            || output.Contains("3", StringComparison.Ordinal),
            "The measured three-wedge attribution was absent.");
        Assert.Contains("#1465", output, StringComparison.Ordinal);
        Assert.True(
            output.Contains("fabricat", StringComparison.OrdinalIgnoreCase)
            || output.Contains("捏造", StringComparison.Ordinal),
            "The no-class-fabrication boundary was absent.");
    }

    private CliContext CreateContext() => new()
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
        NotifyCommand.ProcessRunnerFactory = null;
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
    }

    private sealed class NoOpRunner : INotifyProcessRunner
    {
        public NotifyProcessResult Run(string fileName, IReadOnlyList<string> arguments) =>
            new(0, "{\"result\":{\"agents\":[]}}", string.Empty);
    }
}
