using System.Text.Json;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

public sealed class ModelResolutionConsumersG814Tests : IDisposable
{
    private readonly string root = Directory.CreateTempSubdirectory("g814-consumers-").FullName;

    [Fact]
    public void SharedGuidanceAndHerdrOnlyConsumerExposeScopedReadOnlyResolutionAndOutcomeSpecificCapture_G814()
    {
        var query = AgentModelResolutionGuidance.QueryCommand;
        var record = AgentModelResolutionGuidance.RecordCommand;
        Assert.Contains("--routing-root <absolute-host-root>", query, StringComparison.Ordinal);
        Assert.Contains("--domain <domain>", query, StringComparison.Ordinal);
        Assert.Contains("--team <team>", query, StringComparison.Ordinal);
        Assert.Contains("--role <logical-role>", query, StringComparison.Ordinal);
        Assert.Contains("--requested-effort <effort>", query, StringComparison.Ordinal);
        Assert.Contains("[--requested-model <model-id>]", query, StringComparison.Ordinal);
        Assert.Contains("[--requested-model <explicit-id>]", record, StringComparison.Ordinal);
        Assert.Contains("newest matching scoped verified baseline", string.Join(' ', AgentModelResolutionGuidance.ResolutionOrder), StringComparison.Ordinal);
        Assert.Contains("ask the human", string.Join(' ', AgentModelResolutionGuidance.ResolutionOrder), StringComparison.OrdinalIgnoreCase);
        Assert.True(AgentModelResolutionGuidance.LaunchEvidenceWorkflow.Mandatory);

        var verified = AgentModelResolutionGuidance.LaunchEvidenceWorkflow.Verified;
        var refused = AgentModelResolutionGuidance.LaunchEvidenceWorkflow.Refused;
        Assert.Contains("--capture-target-evidence", verified.CommandArguments);
        Assert.Contains("--evidence", verified.CommandArguments);
        Assert.DoesNotContain("--capture-target-evidence", refused.CommandArguments);
        Assert.DoesNotContain("--evidence", refused.CommandArguments);
        Assert.Contains("--error", refused.CommandArguments);
        Assert.All(verified.CommandArguments.Concat(refused.CommandArguments), token =>
            Assert.DoesNotContain('[', token));

        var markdown = HerdrOnlyOperatingGuide.RenderMarkdown([]);
        var herdrJson = JsonDocument.Parse(HerdrOnlyOperatingGuide.CreateJson([]).ToJsonString());
        var jsonText = herdrJson.RootElement.GetProperty("launch_recipes").GetProperty("model_resolution");
        Assert.Contains(query, markdown, StringComparison.Ordinal);
        Assert.Contains(record, markdown, StringComparison.Ordinal);
        Assert.Contains("do not guess, substitute, or auto-replace", markdown, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(query, jsonText.GetProperty("query_command").GetString(), StringComparison.Ordinal);
        Assert.Contains(record, jsonText.GetProperty("record_command").GetString(), StringComparison.Ordinal);
        Assert.False(jsonText.TryGetProperty("live_argv_fallback", out _));
        Assert.DoesNotContain("herdr agent list", string.Join(' ', jsonText.GetProperty("resolution_order")
            .EnumerateArray().Select(item => item.GetString())), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BootstrapOrchestratorAndCommandCatalogKeepGuidanceInJsonAndMarkdownWithoutLiveFallback_G814()
    {
        var context = CreateContext();
        var bootstrapJson = RenderBootstrap(context, "json");
        var bootstrapMarkdown = RenderBootstrap(context, "markdown");
        var orchestratorJson = RenderOrchestrator(context, "json");
        var orchestratorMarkdown = RenderOrchestrator(context, "markdown");
        foreach (var rawOutput in new[] { bootstrapJson, bootstrapMarkdown, orchestratorJson, orchestratorMarkdown })
        {
            var output = TryFlattenJson(rawOutput);
            Assert.Contains(AgentModelResolutionGuidance.QueryCommand, output, StringComparison.Ordinal);
            Assert.Contains("--requested-effort", output, StringComparison.Ordinal);
            Assert.Contains("ask the human", output, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("live_argv_fallback", output, StringComparison.OrdinalIgnoreCase);
            Assert.True(output.Contains("selected target", StringComparison.OrdinalIgnoreCase)
                || output.Contains("selected pane argv", StringComparison.OrdinalIgnoreCase), output);
            Assert.Contains("do not substitute", output, StringComparison.OrdinalIgnoreCase);
        }

        var verified = AgentModelResolutionGuidance.LaunchEvidenceWorkflow.Verified.Command;
        var refused = AgentModelResolutionGuidance.LaunchEvidenceWorkflow.Refused.Command;
        Assert.Contains(verified, bootstrapMarkdown, StringComparison.Ordinal);
        Assert.Contains(refused, bootstrapMarkdown, StringComparison.Ordinal);
        Assert.Contains("mandatory", bootstrapMarkdown, StringComparison.OrdinalIgnoreCase);

        var purpose = GuideCommandsListCommand.Groups.Single(group => group.Name == "session-layer").Purpose;
        Assert.Contains("G814", purpose, StringComparison.Ordinal);
        Assert.Contains("exact scoped query", purpose, StringComparison.Ordinal);
        Assert.Contains("Legacy unscoped rows remain readable diagnostics", purpose, StringComparison.Ordinal);

        var solo = GuideSoloConductorCommand.BuildGuide();
        Assert.Contains("architect, orchestrator, and builder", solo.Model.Summary, StringComparison.Ordinal);
        Assert.Contains("fresh independent reviewer", solo.Model.Summary, StringComparison.Ordinal);
        var steward = GuideStewardThreadCommand.BuildGuide();
        Assert.Contains("not a specialist", steward.G796Boundary, StringComparison.OrdinalIgnoreCase);
        var docs = File.ReadAllText(Path.Combine(FindRepoRoot(), "docs", "en", "12-agent-message-orchestration.md"));
        Assert.Contains("distributed session-layer behavior applies conditionally", docs, StringComparison.Ordinal);
        Assert.Contains("A Steward only relays", docs, StringComparison.Ordinal);
    }

    private string RenderBootstrap(CliContext context, string format)
    {
        using var writer = new StringWriter();
        Assert.Equal(0, GuideBootstrapCommand.Execute(context,
            ["--routing-root", root, "--domain", "intent-cli", "--team", "dev", "--format", format], writer));
        return writer.ToString();
    }

    private static string RenderOrchestrator(CliContext context, string format)
    {
        using var writer = new StringWriter();
        Assert.Equal(0, GuideOrchestratorThreadCommand.Execute(context,
            ["--domain", "intent-cli", "--team", "dev", "--format", format], writer));
        return writer.ToString();
    }

    private static string TryFlattenJson(string output)
    {
        try
        {
            using var document = JsonDocument.Parse(output);
            var values = new List<string>();
            CollectStrings(document.RootElement, values);
            return string.Join('\n', values);
        }
        catch (JsonException)
        {
            return output;
        }
    }

    private static void CollectStrings(JsonElement element, List<string> values)
    {
        if (element.ValueKind == JsonValueKind.String)
        {
            values.Add(element.GetString() ?? string.Empty);
        }
        else if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject()) CollectStrings(property.Value, values);
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray()) CollectStrings(item, values);
        }
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

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "IntentSystem.sln")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Repository root was not found.");
    }

    public void Dispose()
    {
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
    }
}
