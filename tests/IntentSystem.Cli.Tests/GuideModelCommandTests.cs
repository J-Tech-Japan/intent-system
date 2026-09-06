using System.Text.Json;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

public sealed class GuideModelCommandTests
{
    [Fact]
    public void Execute_DefaultMarkdown_EmitsAllSections()
    {
        using var writer = new StringWriter();
        var exitCode = GuideModelCommand.Execute(
            CreateContext(),
            [],
            writer);

        Assert.Equal(0, exitCode);
        var output = writer.ToString();
        Assert.Contains("# Guide model — primary collaboration model", output, StringComparison.Ordinal);
        Assert.Contains("## Primary model", output, StringComparison.Ordinal);
        Assert.Contains("## Roles", output, StringComparison.Ordinal);
        Assert.Contains("## Primary data paths", output, StringComparison.Ordinal);
        Assert.Contains("## Optional advanced runtime", output, StringComparison.Ordinal);
        Assert.Contains("## Hard rules", output, StringComparison.Ordinal);

        Assert.Contains("Human product owner", output, StringComparison.Ordinal);
        Assert.Contains("Codex / Claude coding agent", output, StringComparison.Ordinal);
        Assert.Contains("intent-cli", output, StringComparison.Ordinal);
        Assert.Contains("Host git repository", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_Markdown_HasExecutionOrchestrationModel_AsPrimaryForMultiThread_G540()
    {
        using var writer = new StringWriter();
        var exitCode = GuideModelCommand.Execute(
            CreateContext(),
            [],
            writer);

        Assert.Equal(0, exitCode);
        var output = writer.ToString();
        Assert.Contains("## Execution orchestration model (PRIMARY for autonomous multi-thread execution)", output, StringComparison.Ordinal);
        Assert.Contains("### Four threads", output, StringComparison.Ordinal);
        Assert.Contains("design — authors intent", output, StringComparison.Ordinal);
        Assert.Contains("orchestrator — inspects canonical intent-cli/GitHub state", output, StringComparison.Ordinal);
        Assert.Contains("implementation — a loopless receiver", output, StringComparison.Ordinal);
        Assert.Contains("review — a loopless receiver", output, StringComparison.Ordinal);
        Assert.Contains("steward — a loopless transmission boundary", output, StringComparison.Ordinal);
        Assert.Contains("message-driven steady state", output, StringComparison.Ordinal);
        Assert.Contains("- **alternative** —", output, StringComparison.Ordinal);
        Assert.Contains("Timer-loop mode remains fully supported", output, StringComparison.Ordinal);

        // G624 graduates herdr-only, and G540's unqualified model designation
        // remains independent of the transport preference.
        var modelSection = SectionBetween(
            output,
            "## Execution orchestration model (PRIMARY for autonomous multi-thread execution)",
            "## Session layer (transport for the four threads)");
        Assert.DoesNotContain("opt-in", modelSection, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("preview", modelSection, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("experimental", modelSection, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// G624: the graduated transport must not regain a PREVIEW qualifier.
    /// </summary>
    [Fact]
    public void Execute_Markdown_GraduatedTransportHasNoPreviewQualifier_G624()
    {
        using var writer = new StringWriter();
        Assert.Equal(0, GuideModelCommand.Execute(CreateContext(), [], writer));
        var output = writer.ToString();

        Assert.Contains("## Session layer (transport for the four threads)", output, StringComparison.Ordinal);
        Assert.DoesNotContain("preview", output, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(SessionLayerMode.TransportPreferenceSentence, output, StringComparison.Ordinal);
        Assert.Contains("PRIMARY and unqualified in both modes", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_Markdown_PresentsBothTransportsAsConditionalChoices_G608()
    {
        using var writer = new StringWriter();
        Assert.Equal(0, GuideModelCommand.Execute(CreateContext(), [], writer));
        var output = writer.ToString();

        Assert.Contains("herdr-only (preferred — fewer dependencies)", output, StringComparison.Ordinal);
        Assert.Contains("agmsg + herdr (supported, not retired)", output, StringComparison.Ordinal);
        Assert.Contains("Prefer herdr-only", output, StringComparison.Ordinal);
        Assert.DoesNotContain("agmsg (PRIMARY)", output, StringComparison.Ordinal);
        Assert.DoesNotContain("Use this unless", output, StringComparison.Ordinal);
    }

    private static string SectionBetween(string output, string startHeading, string endHeading)
    {
        var start = output.IndexOf(startHeading, StringComparison.Ordinal);
        Assert.True(start >= 0, $"missing section heading: {startHeading}");
        var end = output.IndexOf(endHeading, start, StringComparison.Ordinal);
        Assert.True(end > start, $"missing section heading after {startHeading}: {endHeading}");
        return output[start..end];
    }

    [Fact]
    public void Execute_JsonFormat_EmitsStructuredFields()
    {
        using var writer = new StringWriter();
        var exitCode = GuideModelCommand.Execute(
            CreateContext(),
            ["--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var root = document.RootElement;
        Assert.Contains("chat-first", root.GetProperty("primary_model").GetString()!, StringComparison.Ordinal);
        Assert.Equal(4, root.GetProperty("roles").GetArrayLength());
        Assert.True(root.GetProperty("primary_data_paths").GetArrayLength() >= 4);
        Assert.True(root.GetProperty("optional_advanced_runtime").GetArrayLength() >= 2);
        Assert.True(root.GetProperty("hard_rules").GetArrayLength() >= 3);

        var roles = root.GetProperty("roles").EnumerateArray().Select(e => e.GetProperty("actor").GetString()).ToArray();
        Assert.Contains("Human product owner", roles);
        Assert.Contains("Codex / Claude coding agent", roles);
        Assert.Contains("intent-cli", roles);
        Assert.Contains("Host git repository", roles);
    }

    [Fact]
    public void Execute_Json_CarriesExecutionOrchestrationModel_AsPrimary_G540()
    {
        using var writer = new StringWriter();
        var exitCode = GuideModelCommand.Execute(
            CreateContext(),
            ["--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var model = document.RootElement.GetProperty("execution_orchestration_model");

        Assert.Contains("PRIMARY", model.GetProperty("summary").GetString(), StringComparison.Ordinal);
        Assert.Contains("G540", model.GetProperty("summary").GetString(), StringComparison.Ordinal);

        var roles = model.GetProperty("roles").EnumerateArray().Select(e => e.GetString()!).ToArray();
        Assert.Equal(5, roles.Length);
        Assert.Contains(roles, r => r.StartsWith("design —", StringComparison.Ordinal));
        Assert.Contains(roles, r => r.StartsWith("orchestrator —", StringComparison.Ordinal));
        Assert.Contains(roles, r => r.StartsWith("implementation —", StringComparison.Ordinal));
        Assert.Contains(roles, r => r.StartsWith("review —", StringComparison.Ordinal));
        Assert.Contains(roles, r => r.StartsWith("steward —", StringComparison.Ordinal));

        Assert.True(model.TryGetProperty("message_driven_steady_state", out _));
        Assert.Contains("Timer-loop mode remains fully supported", model.GetProperty("alternative").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_Output_NamesIntentCliRunAsOptional()
    {
        using var writer = new StringWriter();
        var exitCode = GuideModelCommand.Execute(
            CreateContext(),
            ["--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var optional = document.RootElement.GetProperty("optional_advanced_runtime")
            .EnumerateArray().Select(e => e.GetString()).ToArray();
        Assert.Contains(optional, item => item!.Contains("intent-cli run", StringComparison.Ordinal));
        Assert.Contains(optional, item => item!.Contains("subprocess", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(optional, item => item!.Contains("Supervisor", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Execute_HardRules_NameLabelOwnershipAndProviderBoundary()
    {
        using var writer = new StringWriter();
        var exitCode = GuideModelCommand.Execute(
            CreateContext(),
            ["--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var rules = document.RootElement.GetProperty("hard_rules")
            .EnumerateArray().Select(e => e.GetString()).ToArray();
        Assert.Contains(rules, rule => rule!.Contains("must not launch Codex/Claude", StringComparison.Ordinal));
        Assert.Contains(rules, rule => rule!.Contains("intent-target", StringComparison.Ordinal));
        Assert.Contains(rules, rule => rule!.Contains("intent-pr-created", StringComparison.Ordinal));
    }

    [Fact]
    public void Execute_UnsupportedFormat_ReturnsUsageError()
    {
        using var writer = new StringWriter();
        var exitCode = GuideModelCommand.Execute(
            CreateContext(),
            ["--format", "yaml"],
            writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("--format must be 'markdown' or 'json'", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_UnknownArgument_ReturnsUsageError()
    {
        using var writer = new StringWriter();
        var exitCode = GuideModelCommand.Execute(
            CreateContext(),
            ["--surprise"],
            writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("Unknown argument '--surprise'", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_HelpFlag_PrintsUsage()
    {
        using var writer = new StringWriter();
        var exitCode = GuideModelCommand.Execute(
            CreateContext(),
            ["--help"],
            writer);

        Assert.Equal(0, exitCode);
        Assert.Contains("guide model", writer.ToString(), StringComparison.Ordinal);
    }

    private static CliContext CreateContext()
    {
        return new CliContext
        {
            RepoRoot = Path.GetTempPath(),
            Config = new CliConfig
            {
                Project = new ProjectConfig
                {
                    Domain = "intent-cli",
                    ArtifactRoot = ".intent-cli",
                    WorktreeRoot = ".intent-cli/worktrees"
                }
            }
        };
    }
}
