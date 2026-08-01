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
        Assert.Contains("message-driven steady state", output, StringComparison.Ordinal);
        Assert.Contains("- **alternative** —", output, StringComparison.Ordinal);
        Assert.Contains("Timer-loop mode remains fully supported", output, StringComparison.Ordinal);

        // G570 scoped this assertion to the section it is about, and did not
        // weaken it. G540 ruled that the four-thread MODEL carries no
        // qualifier; the assertion was written document-wide because the
        // document only discussed the model. It now also describes the SESSION
        // TRANSPORT, where `herdr-only` is honestly a preview — so a
        // document-wide ban would force the transport to be described
        // inaccurately to keep a guard green, which is the wrong trade.
        //
        // What G540 ruled is asserted exactly: no qualifier appears anywhere in
        // the execution-orchestration-model section.
        var modelSection = SectionBetween(
            output,
            "## Execution orchestration model (PRIMARY for autonomous multi-thread execution)",
            "## Session layer (transport for the four threads)");
        Assert.DoesNotContain("opt-in", modelSection, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("preview", modelSection, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("experimental", modelSection, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// G570: the preview qualifier is allowed ONLY on the transport, and only
    /// while it says so in its own words. Without this, scoping the assertion
    /// above would have left the qualifier free to drift back onto the model.
    /// </summary>
    [Fact]
    public void Execute_Markdown_PreviewQualifierAppearsOnlyInTheSessionLayerSection_G570()
    {
        using var writer = new StringWriter();
        Assert.Equal(0, GuideModelCommand.Execute(CreateContext(), [], writer));
        var output = writer.ToString();

        var sessionLayerIndex = output.IndexOf("## Session layer (transport for the four threads)", StringComparison.Ordinal);
        Assert.True(sessionLayerIndex > 0, "guide model must carry the session-layer section");

        // Every occurrence of the qualifier is inside that section.
        var index = output.IndexOf("preview", StringComparison.OrdinalIgnoreCase);
        Assert.True(index >= 0, "the session-layer section must state the preview qualifier honestly");
        while (index >= 0)
        {
            Assert.True(
                index > sessionLayerIndex,
                "a preview/PREVIEW qualifier appears before the session-layer section — G540 rules the four-thread "
                + "model unqualified, so the qualifier must never attach to it.");
            index = output.IndexOf("preview", index + 1, StringComparison.OrdinalIgnoreCase);
        }

        // And the qualifier carries its own scoping sentence, so a reader can
        // never take it as a statement about the model.
        Assert.Contains("PREVIEW here scopes the SESSION TRANSPORT only", output, StringComparison.Ordinal);
        Assert.Contains("PRIMARY and unqualified in both modes", output, StringComparison.Ordinal);
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
        Assert.Equal(4, roles.Length);
        Assert.Contains(roles, r => r.StartsWith("design —", StringComparison.Ordinal));
        Assert.Contains(roles, r => r.StartsWith("orchestrator —", StringComparison.Ordinal));
        Assert.Contains(roles, r => r.StartsWith("implementation —", StringComparison.Ordinal));
        Assert.Contains(roles, r => r.StartsWith("review —", StringComparison.Ordinal));

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
