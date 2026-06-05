using System.Text.Json;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

/// <summary>
/// G456: tests for the design-thread improve / realignment guide surface.
/// </summary>
public sealed class GuideImproveCommandTests
{
    [Fact]
    public void Execute_DefaultMarkdown_EmitsShortPromptAndSections()
    {
        using var writer = new StringWriter();
        var exitCode = GuideImproveCommand.Execute(CreateContext(), [], writer);

        Assert.Equal(0, exitCode);
        var output = writer.ToString();
        Assert.Contains("# Guide improve", output, StringComparison.Ordinal);
        // AC: the short natural-language prompt is present verbatim.
        Assert.Contains("intent-cli で improve プロセスを実行してください。", output, StringComparison.Ordinal);
        // AC: states it is a reflection process, not a scheduler/provider/loop-recovery.
        Assert.Contains("5m/20m schedule", output, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("NOT a scheduler", output, StringComparison.Ordinal);
        Assert.Contains("does NOT launch", output, StringComparison.Ordinal);
        // AC: inspection of MVV / ADR / intent tree / packet history / clarification / loops.
        Assert.Contains("Mission / Vision / Values", output, StringComparison.Ordinal);
        Assert.Contains("ADR", output, StringComparison.Ordinal);
        Assert.Contains("Intent tree", output, StringComparison.Ordinal);
        Assert.Contains("packet history", output, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Clarification history", output, StringComparison.Ordinal);
        Assert.Contains("Short-term loop", output, StringComparison.Ordinal);
        // AC: structured report template + mutation boundary.
        Assert.Contains("Structured report template", output, StringComparison.Ordinal);
        Assert.Contains("Mutation boundary", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_DefaultJson_IsImplementationAware_WithDeepSections()
    {
        // G460: the DEFAULT improve path is implementation-aware with the three
        // deep report sections and shallow-vs-target contrast examples.
        using var writer = new StringWriter();
        var exitCode = GuideImproveCommand.Execute(CreateContext(), ["--format", "json"], writer);

        Assert.Equal(0, exitCode);
        using var doc = JsonDocument.Parse(writer.ToString());
        var root = doc.RootElement;
        Assert.Equal("implementation-aware", root.GetProperty("mode").GetString());

        var report = string.Join("\n", root.GetProperty("report_template").EnumerateArray().Select(e => e.GetString()));
        Assert.Contains("Implementation Reality Check", report, StringComparison.Ordinal);
        Assert.Contains("Blocker Cluster Analysis", report, StringComparison.Ordinal);
        Assert.Contains("Corrective Backlog Candidates", report, StringComparison.Ordinal);

        var checklist = root.GetProperty("inspection_checklist").EnumerateArray().Select(e => e.GetString()!).ToArray();
        Assert.Contains(checklist, c => c.Contains("Implementation reality", StringComparison.Ordinal));
        Assert.Contains(checklist, c => c.Contains("top product blocker", StringComparison.Ordinal));

        // Contrast examples (AIC shallow vs Sekiban target) present in default mode.
        var labels = root.GetProperty("contrast_examples").EnumerateArray()
            .Select(e => e.GetProperty("label").GetString()!).ToArray();
        Assert.Contains(labels, l => l.Contains("AIC", StringComparison.Ordinal));
        Assert.Contains(labels, l => l.Contains("SekibanAsAService", StringComparison.Ordinal));

        // Mutation boundary: at most one first issue after approval.
        var boundary = root.GetProperty("mutation_boundary").EnumerateArray().Select(e => e.GetString()!).ToArray();
        Assert.Contains(boundary, b => b.Contains("AT MOST ONE first GitHub issue", StringComparison.Ordinal));
    }

    [Fact]
    public void Execute_LightMode_IsIntentOnly_NoDeepSections()
    {
        // G460: --light keeps the quick intent-only reflection (no deep sections).
        using var writer = new StringWriter();
        var exitCode = GuideImproveCommand.Execute(CreateContext(), ["--light", "--format", "json"], writer);

        Assert.Equal(0, exitCode);
        using var doc = JsonDocument.Parse(writer.ToString());
        var root = doc.RootElement;
        Assert.Equal("light", root.GetProperty("mode").GetString());

        var report = string.Join("\n", root.GetProperty("report_template").EnumerateArray().Select(e => e.GetString()));
        Assert.DoesNotContain("Implementation Reality Check", report, StringComparison.Ordinal);
        Assert.DoesNotContain("Corrective Backlog Candidates", report, StringComparison.Ordinal);
        Assert.Empty(root.GetProperty("contrast_examples").EnumerateArray());
    }

    [Fact]
    public void Execute_Json_EmitsOutcomeClassificationsAndShortPrompt()
    {
        using var writer = new StringWriter();
        var exitCode = GuideImproveCommand.Execute(CreateContext(), ["--format", "json"], writer);

        Assert.Equal(0, exitCode);
        using var doc = JsonDocument.Parse(writer.ToString());
        var root = doc.RootElement;
        Assert.Equal("design-thread-improve", root.GetProperty("process").GetString());
        Assert.Equal("intent-cli で improve プロセスを実行してください。", root.GetProperty("short_prompt").GetString());

        var outcomes = root.GetProperty("outcome_classifications").EnumerateArray()
            .Select(o => o.GetProperty("id").GetString()).ToArray();
        Assert.Equal(
            new[]
            {
                "aligned",
                "intent-strengthening-recommended",
                "clarification-recommended",
                "corrective-packet-recommended",
                "adr-update-recommended",
                "short-term-loop-detected",
                "operator-policy-required",
            },
            outcomes);

        // Mutation boundary keeps metadata/label/queue repair in operational surfaces.
        var boundary = root.GetProperty("mutation_boundary").EnumerateArray()
            .Select(e => e.GetString()!).ToArray();
        Assert.Contains(boundary, b => b.Contains("Never hand-edit workflow labels", StringComparison.Ordinal));
    }

    [Fact]
    public void Execute_WithDomain_SubstitutesDomainInChecklistPaths()
    {
        using var writer = new StringWriter();
        var exitCode = GuideImproveCommand.Execute(CreateContext(), ["--domain", "aic", "--format", "json"], writer);

        Assert.Equal(0, exitCode);
        using var doc = JsonDocument.Parse(writer.ToString());
        var root = doc.RootElement;
        Assert.Equal("aic", root.GetProperty("domain").GetString());
        var checklist = root.GetProperty("inspection_checklist").EnumerateArray()
            .Select(e => e.GetString()!).ToArray();
        Assert.Contains(checklist, c => c.Contains("intents/aic/", StringComparison.Ordinal));
    }

    [Fact]
    public void Execute_UnknownFormat_ReturnsError()
    {
        using var writer = new StringWriter();
        var exitCode = GuideImproveCommand.Execute(CreateContext(), ["--format", "yaml"], writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("--format must be", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_HelpFlag_PrintsUsageWithShortPrompt()
    {
        using var writer = new StringWriter();
        var exitCode = GuideImproveCommand.Execute(CreateContext(), ["--help"], writer);

        Assert.Equal(0, exitCode);
        var output = writer.ToString();
        Assert.Contains("guide improve", output, StringComparison.Ordinal);
        Assert.Contains("intent-cli で improve プロセスを実行してください。", output, StringComparison.Ordinal);
    }

    [Fact]
    public void GuideHelp_ListsImproveSubcommand_ForDiscoverability()
    {
        // AC: improve is discoverable from the guide help surface.
        using var writer = new StringWriter();
        var exitCode = GuideHelpCommand.Execute(CreateContext(), ["--format", "json"], writer);

        Assert.Equal(0, exitCode);
        using var doc = JsonDocument.Parse(writer.ToString());
        var names = doc.RootElement.GetProperty("subcommands").EnumerateArray()
            .Select(s => s.GetProperty("name").GetString()).ToArray();
        Assert.Contains("improve", names);
    }

    [Fact]
    public void Execute_Json_EmitsDoNotSubstituteAntiMisroutingGuidance()
    {
        // G457: improve output must explicitly tell agents not to substitute
        // operational recovery workflows.
        using var writer = new StringWriter();
        var exitCode = GuideImproveCommand.Execute(CreateContext(), ["--format", "json"], writer);

        Assert.Equal(0, exitCode);
        using var doc = JsonDocument.Parse(writer.ToString());
        var doNotSubstitute = doc.RootElement.GetProperty("do_not_substitute").EnumerateArray()
            .Select(e => e.GetString()!).ToArray();
        Assert.Contains(doNotSubstitute, s => s.Contains("bug-to-intent-repair", StringComparison.Ordinal));
        Assert.Contains(doNotSubstitute, s => s.Contains("host-loop", StringComparison.Ordinal));
        Assert.Contains(doNotSubstitute, s => s.Contains("state-doctor", StringComparison.Ordinal));
        Assert.Contains(doNotSubstitute, s => s.Contains("dirty-state", StringComparison.Ordinal));
        // AC: missing improve surfaces `improve guidance unavailable`.
        Assert.Contains(doNotSubstitute, s => s.Contains("improve guidance unavailable", StringComparison.Ordinal));
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
