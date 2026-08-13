using System.Text.Json;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

public sealed class ModelResolutionLedgerG685Tests : IDisposable
{
    private readonly string root = Directory.CreateTempSubdirectory("g685-model-resolution-").FullName;

    [Fact]
    public void Registry_ShipsGrammarForCodexAndClaudeOnly_WithNoModelIds()
    {
        var grammars = AgentLaunchRecipeRegistry.RecordedModelFlagGrammars
            .OrderBy(value => value.Kind, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(["claude", "codex"], grammars.Select(value => value.Kind));
        Assert.Equal("--model <id>", grammars.Single(value => value.Kind == "codex").Model);
        Assert.Equal("-c model_reasoning_effort=<level>", grammars.Single(value => value.Kind == "codex").Effort);
        Assert.Equal("--model <id>", grammars.Single(value => value.Kind == "claude").Model);
        Assert.Equal("--effort <level>", grammars.Single(value => value.Kind == "claude").Effort);
        Assert.Null(AgentLaunchRecipeRegistry.FindModelFlagGrammar("copilot"));
        Assert.Null(AgentLaunchRecipeRegistry.FindModelFlagGrammar("cursor"));
        Assert.Equal("--effort <level>", AgentLaunchRecipeRegistry.Describe("claude").ModelFlagGrammar?.Effort);

        var shipped = string.Join('\n', EnumerateFiles(Path.Combine(FindRepoRoot(), "src"))
            .Concat(EnumerateFiles(Path.Combine(FindRepoRoot(), "docs")))
            .Select(File.ReadAllText));
        Assert.DoesNotMatch("(?i)(gpt-[0-9]|claude-(opus|sonnet)-[0-9])", shipped);
        Assert.DoesNotContain("gpt-5.6-sol", shipped, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("gpt-5.4-mini", shipped, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("claude-opus-5", shipped, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void VerifiedRecord_AppendsFullInvocation_AndSubsequentQueryHits()
    {
        var context = CreateContext();
        const string workingInvocation =
            "codex --model gpt-5.6-sol -c model_reasoning_effort=medium --sandbox workspace-write";

        using var recordWriter = new StringWriter();
        Assert.Equal(0, ModelResolutionLedgerCommand.Execute(
            context,
            ["record", "--kind", "codex", "--informal-name", "sol medium", "--outcome", "verified",
                "--invocation", workingInvocation, "--evidence", "running seat argv plus successful banner",
                "--write", "--format", "json"],
            recordWriter));
        using (var recorded = JsonDocument.Parse(recordWriter.ToString()))
        {
            Assert.True(recorded.RootElement.GetProperty("applied").GetBoolean());
            Assert.Equal("none", recorded.RootElement.GetProperty("provider_operation").GetString());
            Assert.Equal(workingInvocation,
                recorded.RootElement.GetProperty("entry").GetProperty("full_invocation").GetString());
        }

        using var queryWriter = new StringWriter();
        Assert.Equal(0, ModelResolutionLedgerCommand.Execute(
            context,
            ["query", "--kind", "codex", "--informal-name", "sol medium", "--format", "json"],
            queryWriter));
        using var query = JsonDocument.Parse(queryWriter.ToString());
        Assert.True(query.RootElement.GetProperty("resolved").GetBoolean());
        Assert.Equal("ledger-hit", query.RootElement.GetProperty("status").GetString());
        Assert.Equal(workingInvocation,
            query.RootElement.GetProperty("positive_entry").GetProperty("full_invocation").GetString());

        var lines = File.ReadAllLines(ModelResolutionLedgerStore.ResolvePath(root));
        Assert.Single(lines);
        Assert.True(File.Exists(Path.Combine(root, ".intent-cli", "model-resolution", ".gitignore")));
    }

    [Fact]
    public void Record_PreservesPreExistingIgnoreRules_AndAddsLedgerRuleExactlyOnce()
    {
        var directory = Path.Combine(root, ".intent-cli", "model-resolution");
        Directory.CreateDirectory(directory);
        var ignorePath = Path.Combine(directory, ".gitignore");
        File.WriteAllText(ignorePath, "operator-local.tmp\n# keep this rule\n");

        var args = new[]
        {
            "record", "--kind", "codex", "--informal-name", "fixture medium",
            "--outcome", "verified", "--invocation", "codex --model fixture-id",
            "--evidence", "captured READY banner", "--write", "--format", "json",
        };
        Assert.Equal(0, ModelResolutionLedgerCommand.Execute(CreateContext(), args, TextWriter.Null));
        Assert.Equal(0, ModelResolutionLedgerCommand.Execute(CreateContext(), args, TextWriter.Null));

        var lines = File.ReadAllLines(ignorePath);
        Assert.Contains("operator-local.tmp", lines);
        Assert.Contains("# keep this rule", lines);
        Assert.Single(lines, line => line.Trim() == "ledger.jsonl");
    }

    [Theory]
    [InlineData("verified")]
    [InlineData("refused")]
    public void RenderedLaunchWorkflow_RequiresAndAppendsCapturedEvidenceThroughRealCommandRouter(string outcome)
    {
        var context = CreateContext();
        var guide = GuideBootstrapCommand.BuildResult(context, root, "intent-cli", "dev", "owner/repo");
        var workflow = guide.ModelResolution.LaunchEvidenceWorkflow;
        var renderedStep = outcome == "verified" ? workflow.Verified : workflow.Refused;
        var launchStep = Assert.Single(guide.Steps,
            step => step.Id == "emit-workspace-pane-and-seat-commands");

        Assert.True(workflow.Mandatory);
        Assert.Equal("none", workflow.ProviderOperation);
        Assert.Contains(renderedStep.Command, launchStep.EmittedCommands);
        Assert.Contains("<captured-exact-launched-invocation>", renderedStep.Command, StringComparison.Ordinal);
        Assert.Contains(outcome == "verified"
            ? "<captured-ready-banner-and-running-argv-evidence>"
            : "<captured-refusal-error-text>", renderedStep.Command, StringComparison.Ordinal);

        var invocation = "codex --model fixture-id -c model_reasoning_effort=medium";
        var arguments = renderedStep.CommandArguments.Select(value => value switch
        {
            "<resolved-kind>" => "codex",
            "<captured-informal-name-and-effort>" => "fixture medium",
            "<captured-exact-launched-invocation>" => invocation,
            "<captured-ready-banner-and-running-argv-evidence>" => "READY banner plus running argv",
            "<captured-refusal-error-text>" => "captured provider refusal",
            _ => value,
        }).ToArray();

        using var writer = new StringWriter();
        Assert.Equal(0, CommandRouter.Execute(arguments, context, writer));
        using var result = JsonDocument.Parse(writer.ToString());
        Assert.True(result.RootElement.GetProperty("applied").GetBoolean());
        Assert.Equal("none", result.RootElement.GetProperty("provider_operation").GetString());

        var entry = Assert.Single(ModelResolutionLedgerStore.Read(root).Entries);
        Assert.Equal(outcome, entry.Outcome);
        Assert.Equal(invocation, entry.Invocation);
        if (outcome == "verified")
            Assert.Equal("READY banner plus running argv", entry.Evidence);
        else
            Assert.Equal("captured provider refusal", entry.ErrorText);
    }

    [Fact]
    public void RefusedRecord_AppendsError_AndExactCandidateCannotBeRetried()
    {
        var context = CreateContext();
        const string refusedInvocation = "codex --model sol";
        const string refusal = "400: informal model is unavailable for this account";
        Assert.Equal(0, ModelResolutionLedgerCommand.Execute(
            context,
            ["record", "--kind", "codex", "--informal-name", "sol medium", "--outcome", "refused",
                "--invocation", refusedInvocation, "--error", refusal, "--write", "--format", "json"],
            TextWriter.Null));

        using var queryWriter = new StringWriter();
        Assert.Equal(0, ModelResolutionLedgerCommand.Execute(
            context,
            ["query", "--kind", "codex", "--informal-name", "sol medium",
                "--candidate-invocation", refusedInvocation, "--format", "json"],
            queryWriter));
        using var query = JsonDocument.Parse(queryWriter.ToString());
        Assert.False(query.RootElement.GetProperty("resolved").GetBoolean());
        Assert.Equal("refused-invocation", query.RootElement.GetProperty("status").GetString());
        Assert.False(query.RootElement.GetProperty("candidate_retry_permitted").GetBoolean());
        Assert.Equal(refusal,
            query.RootElement.GetProperty("negative_entry").GetProperty("error_text").GetString());
        Assert.Equal("none", query.RootElement.GetProperty("provider_operation").GetString());
    }

    [Fact]
    public void LaterRefusalOfPreviouslyVerifiedInvocation_FailsClosedWithoutCandidateHint()
    {
        var context = CreateContext();
        const string invocation = "claude --model test-fixture-id --effort high";
        ModelResolutionLedgerCommand.UtcNowFactory = () =>
            new DateTimeOffset(2026, 8, 12, 12, 0, 0, TimeSpan.Zero);
        Assert.Equal(0, ModelResolutionLedgerCommand.Execute(
            context,
            ["record", "--kind", "claude", "--informal-name", "opus high", "--outcome", "verified",
                "--invocation", invocation, "--evidence", "successful banner", "--write", "--format", "json"],
            TextWriter.Null));
        ModelResolutionLedgerCommand.UtcNowFactory = () =>
            new DateTimeOffset(2026, 8, 12, 13, 0, 0, TimeSpan.Zero);
        Assert.Equal(0, ModelResolutionLedgerCommand.Execute(
            context,
            ["record", "--kind", "claude", "--informal-name", "opus high", "--outcome", "refused",
                "--invocation", invocation, "--error", "account no longer permits the invocation",
                "--write", "--format", "json"],
            TextWriter.Null));

        using var writer = new StringWriter();
        Assert.Equal(0, ModelResolutionLedgerCommand.Execute(
            context,
            ["query", "--kind", "claude", "--informal-name", "opus high", "--format", "json"],
            writer));
        using var result = JsonDocument.Parse(writer.ToString());
        Assert.False(result.RootElement.GetProperty("resolved").GetBoolean());
        Assert.Equal("negative-evidence-available", result.RootElement.GetProperty("status").GetString());
        Assert.False(result.RootElement.TryGetProperty("positive_entry", out _));
    }

    [Fact]
    public void Record_RejectsUnmeasuredKindInsteadOfInventingGrammar()
    {
        using var writer = new StringWriter();
        var exit = ModelResolutionLedgerCommand.Execute(
            CreateContext(),
            ["record", "--kind", "copilot", "--informal-name", "fast", "--outcome", "verified",
                "--invocation", "copilot --model fast", "--evidence", "operator report", "--write", "--format", "json"],
            writer);

        Assert.Equal(1, exit);
        Assert.Contains("Known values: claude, codex", writer.ToString(), StringComparison.Ordinal);
        Assert.Contains("Refusing to invent grammar", writer.ToString(), StringComparison.Ordinal);
        Assert.False(File.Exists(ModelResolutionLedgerStore.ResolvePath(root)));
    }

    [Fact]
    public void LedgerMiss_ExposesStructuredReadOnlyLiveArgvFallbackBeforeHuman()
    {
        using var writer = new StringWriter();
        Assert.Equal(0, CommandRouter.Execute(
            ["session-layer", "model-resolution", "query", "--kind", "codex",
                "--informal-name", "fixture medium", "--format", "json"],
            CreateContext(), writer));

        using var result = JsonDocument.Parse(writer.ToString());
        var rootElement = result.RootElement;
        Assert.Equal("ledger-miss", rootElement.GetProperty("status").GetString());
        Assert.Equal("inspect-live-same-kind-argv", rootElement.GetProperty("next_step").GetString());
        var fallback = rootElement.GetProperty("live_argv_fallback");
        Assert.Equal("read-only", fallback.GetProperty("mode").GetString());
        Assert.Equal("herdr agent list", fallback.GetProperty("list_command").GetString());
        Assert.Equal("herdr pane process-info --pane <selected-pane-id>",
            fallback.GetProperty("inspect_command").GetString());
        Assert.Equal("result.process_info.foreground_processes[].argv",
            fallback.GetProperty("argv_path").GetString());
        Assert.Contains("agent equals <resolved-kind>", fallback.GetProperty("selection").GetString(),
            StringComparison.Ordinal);
        Assert.Contains("only after the ledger miss", fallback.GetProperty("human_fallback").GetString(),
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("bootstrap")]
    [InlineData("orchestrator-agmsg")]
    [InlineData("orchestrator-herdr-only")]
    public void RealGuides_RenderLedgerThenLiveArgvThenHuman_AndNeverGuess(string guide)
    {
        var output = guide switch
        {
            "bootstrap" => RenderBootstrap(),
            "orchestrator-agmsg" => RenderOrchestrator(herdrOnly: false),
            "orchestrator-herdr-only" => RenderOrchestrator(herdrOnly: true),
            _ => throw new InvalidOperationException(),
        };

        var ledger = output.IndexOf(AgentModelResolutionGuidance.QueryCommand, StringComparison.Ordinal);
        var live = output.IndexOf(AgentModelResolutionGuidance.LiveArgvFallback.ListCommand, ledger + 1,
            StringComparison.Ordinal);
        var inspect = output.IndexOf(AgentModelResolutionGuidance.LiveArgvFallback.InspectCommand, live + 1,
            StringComparison.Ordinal);
        var human = output.IndexOf("ask the human", inspect + 1, StringComparison.OrdinalIgnoreCase);
        Assert.True(ledger >= 0, output);
        Assert.True(live > ledger, output);
        Assert.True(inspect > live, output);
        Assert.True(human > inspect, output);
        Assert.Contains("result.process_info.foreground_processes[].argv", output, StringComparison.Ordinal);
        Assert.Contains(AgentModelResolutionGuidance.LaunchEvidenceWorkflow.Verified.Command, output,
            StringComparison.Ordinal);
        Assert.Contains(AgentModelResolutionGuidance.LaunchEvidenceWorkflow.Refused.Command, output,
            StringComparison.Ordinal);
        Assert.Contains("mandatory", output, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Never guess a bare model id", output, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ships no model identifiers", output, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("--model sol", output, StringComparison.Ordinal);
        Assert.Contains("HTTP 400", output, StringComparison.Ordinal);
    }

    [Fact]
    public void CommandHelp_StatesProviderAndValidationBoundary()
    {
        using var writer = new StringWriter();
        Assert.Equal(0, ModelResolutionLedgerCommand.Execute(CreateContext(), ["--help"], writer));
        Assert.Contains("launches no provider", writer.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("no provider validation", writer.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    private string RenderBootstrap()
    {
        using var writer = new StringWriter();
        Assert.Equal(0, GuideBootstrapCommand.Execute(
            CreateContext(), ["--routing-root", root, "--format", "markdown"], writer));
        return writer.ToString();
    }

    private string RenderOrchestrator(bool herdrOnly)
    {
        if (herdrOnly)
        {
            Assert.Equal(0, SessionLayerCommand.ExecuteSet(
                CreateContext(),
                ["--domain", "intent-cli", "--team", "dev", "--mode", "herdr-only", "--write", "--format", "json"],
                TextWriter.Null));
        }

        using var writer = new StringWriter();
        var args = herdrOnly
            ? new[] { "--domain", "intent-cli", "--team", "dev", "--format", "markdown" }
            : new[] { "--format", "markdown" };
        Assert.Equal(0, GuideOrchestratorThreadCommand.Execute(CreateContext(), args, writer));
        return writer.ToString();
    }

    private CliContext CreateContext() => new()
    {
        RepoRoot = root,
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

    private static IEnumerable<string> EnumerateFiles(string directory) => Directory.EnumerateFiles(
        directory,
        "*",
        SearchOption.AllDirectories).Where(path => Path.GetExtension(path) is ".cs" or ".md");

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "IntentSystem.sln")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Repository root was not found.");
    }

    public void Dispose()
    {
        ModelResolutionLedgerStore.WriteOverride = null;
        ModelResolutionLedgerCommand.UtcNowFactory = () => DateTimeOffset.UtcNow;
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
    }
}
