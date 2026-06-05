using System.Text.Json;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

/// <summary>
/// G466: tests for the evidence-backed inspect guide surface.
/// </summary>
public sealed class GuideInspectCommandTests
{
    [Fact]
    public void Execute_DefaultMarkdown_DescribesObservationVsInferenceAndReadOnly()
    {
        using var writer = new StringWriter();
        var exitCode = GuideInspectCommand.Execute(CreateContext(), [], writer);

        Assert.Equal(0, exitCode);
        var output = writer.ToString();
        Assert.Contains("# Guide inspect", output, StringComparison.Ordinal);
        // AC: observation vs inference rules.
        Assert.Contains("Observation vs inference", output, StringComparison.Ordinal);
        // AC: first pass read-only by default.
        Assert.Contains("read-only: yes", output, StringComparison.Ordinal);
        // AC: report sections + routing present.
        Assert.Contains("Inspect Report sections", output, StringComparison.Ordinal);
        Assert.Contains("routes next", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_Json_IncludesAllReportSections()
    {
        using var writer = new StringWriter();
        var exitCode = GuideInspectCommand.Execute(
            CreateContext(),
            ["--domain", "intent-cli", "--target-repo", "J-Tech-Japan/intent-system", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var doc = JsonDocument.Parse(writer.ToString());
        var root = doc.RootElement;
        Assert.Equal("evidence-backed-inspect", root.GetProperty("process").GetString());
        Assert.True(root.GetProperty("read_only").GetBoolean());

        // AC: Inspect Report sections — observed behavior, expected intent, evidence,
        // gaps, risk/severity, recommended next action, packet candidates.
        var sections = root.GetProperty("report_shape").EnumerateArray()
            .Select(s => s.GetProperty("section").GetString()).ToArray();
        Assert.Equal(
            new[]
            {
                "observed_behavior", "expected_intent", "evidence", "gaps",
                "risk_severity", "recommended_next_action", "packet_candidates",
            },
            sections);

        // AC: observation vs inference rules present.
        Assert.True(root.GetProperty("observation_vs_inference").GetArrayLength() > 0);
    }

    [Fact]
    public void Execute_Json_RoutingCoversStackGrillImproveRecoveryNoAction()
    {
        // AC: explains when inspect should lead to stack, grill, improve, recovery, or no action.
        using var writer = new StringWriter();
        var exitCode = GuideInspectCommand.Execute(CreateContext(), ["--format", "json"], writer);

        Assert.Equal(0, exitCode);
        using var doc = JsonDocument.Parse(writer.ToString());
        var routes = doc.RootElement.GetProperty("next_action_routing").EnumerateArray()
            .Select(r => r.GetProperty("route").GetString()).ToArray();
        foreach (var required in new[] { "stack", "grill", "improve", "recovery", "no-action" })
        {
            Assert.Contains(required, routes);
        }

        // do-not-substitute names the unavailable-surface signal.
        var doNotSubstitute = string.Join("\n", doc.RootElement.GetProperty("do_not_substitute").EnumerateArray().Select(e => e.GetString()));
        Assert.Contains("inspect guidance unavailable", doNotSubstitute, StringComparison.Ordinal);

        // Safety boundary: read-only first pass, no destructive interactions.
        var safety = string.Join("\n", doc.RootElement.GetProperty("safety_boundary").EnumerateArray().Select(e => e.GetString()));
        Assert.Contains("read-only", safety, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("destructive", safety, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Execute_WithDomain_SubstitutesDomainInInspectionTargets()
    {
        using var writer = new StringWriter();
        var exitCode = GuideInspectCommand.Execute(CreateContext(), ["--domain", "aic", "--format", "json"], writer);

        Assert.Equal(0, exitCode);
        using var doc = JsonDocument.Parse(writer.ToString());
        var targets = string.Join("\n", doc.RootElement.GetProperty("inspection_targets").EnumerateArray().Select(e => e.GetString()));
        Assert.Contains("intents/aic/", targets, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_UnknownFormat_ReturnsError()
    {
        using var writer = new StringWriter();
        var exitCode = GuideInspectCommand.Execute(CreateContext(), ["--format", "yaml"], writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("--format must be", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_HelpFlag_PrintsUsage()
    {
        using var writer = new StringWriter();
        var exitCode = GuideInspectCommand.Execute(CreateContext(), ["--help"], writer);

        Assert.Equal(0, exitCode);
        var output = writer.ToString();
        Assert.Contains("inspect", output, StringComparison.Ordinal);
        Assert.Contains("evidence-backed", output, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void GuideHelp_ListsInspectSubcommand_ForDiscoverability()
    {
        using var writer = new StringWriter();
        var exitCode = GuideHelpCommand.Execute(CreateContext(), ["--format", "json"], writer);

        Assert.Equal(0, exitCode);
        using var doc = JsonDocument.Parse(writer.ToString());
        var names = doc.RootElement.GetProperty("subcommands").EnumerateArray()
            .Select(s => s.GetProperty("name").GetString()).ToArray();
        Assert.Contains("inspect", names);
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
