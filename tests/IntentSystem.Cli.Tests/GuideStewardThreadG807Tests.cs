using System.Text.Json;
using System.Text.RegularExpressions;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

/// <summary>
/// G807: the Steward is a first-class, read-only guide surface. These tests
/// exercise the compiled JSON and Markdown route, the G796 boundary, route
/// preservation, and discoverability without reading host metadata.
/// </summary>
public sealed class GuideStewardThreadG807Tests
{
    [Fact]
    public void JsonAndMarkdownRenderEveryStewardContractSection_G807_AC1()
    {
        using var jsonWriter = new StringWriter();
        Assert.Equal(0, GuideStewardThreadCommand.Execute(CreateContext(), ["--format", "json"], jsonWriter));
        using var json = JsonDocument.Parse(jsonWriter.ToString());
        var root = json.RootElement;

        Assert.Equal("intent-cli guide steward-thread", root.GetProperty("route").GetString());
        Assert.Equal("steward", root.GetProperty("role").GetString());
        Assert.Equal("g807-steward-thread/v1", root.GetProperty("contract_version").GetString());
        Assert.True(root.GetProperty("read_only").GetBoolean());
        Assert.True(root.GetProperty("metadata_free").GetBoolean());
        Assert.Contains("transport boundary", root.GetProperty("identity_and_reader_path").GetProperty("identity").GetString()!, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("reader", root.GetProperty("identity_and_reader_path").GetProperty("reader_path").GetString()!, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("notify", root.GetProperty("handoff_rules").GetProperty("design").GetString()!, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("architect", root.GetProperty("handoff_rules").GetProperty("design").GetString()!, StringComparison.Ordinal);
        Assert.Contains("reviewer", root.GetProperty("handoff_rules").GetProperty("review").GetString()!, StringComparison.Ordinal);
        Assert.Contains("orchestrator", root.GetProperty("handoff_rules").GetProperty("orchestration").GetString()!, StringComparison.Ordinal);
        Assert.Contains("notify delegate --from steward", root.GetProperty("refusals_and_reporting").GetProperty("missing_delegation").GetString()!, StringComparison.Ordinal);
        Assert.Contains("notify report --from steward --to orchestrator", root.GetProperty("refusals_and_reporting").GetProperty("report_route").GetString()!, StringComparison.Ordinal);
        Assert.Contains("notify adjudicate live-pair", root.GetProperty("dialog_path").GetProperty("live_pair").GetString()!, StringComparison.Ordinal);
        Assert.Contains("worktree", root.GetProperty("working_tree_discipline").GetProperty("rule").GetString()!, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("G796", root.GetProperty("g796_boundary").GetString()!, StringComparison.Ordinal);
        Assert.NotEmpty(root.GetProperty("negative_boundaries").EnumerateArray());

        using var markdownWriter = new StringWriter();
        Assert.Equal(0, GuideStewardThreadCommand.Execute(CreateContext(), ["--format", "markdown"], markdownWriter));
        var markdown = markdownWriter.ToString();
        foreach (var heading in new[]
        {
            "## Identity and reader path",
            "## What Steward may do alone",
            "## Handoff rules",
            "## Refusals and report routing",
            "## Dialog path",
            "## Working-tree discipline",
            "## G796 boundary",
        })
        {
            Assert.Contains(heading, markdown, StringComparison.Ordinal);
        }

        Assert.Contains("notify delegate --from steward", markdown, StringComparison.Ordinal);
        Assert.Contains("notify report --from steward --to orchestrator", markdown, StringComparison.Ordinal);
        Assert.Contains("notify adjudicate live-pair", markdown, StringComparison.Ordinal);
        Assert.Contains("architect", markdown, StringComparison.Ordinal);
        Assert.Contains("reviewer", markdown, StringComparison.Ordinal);
        Assert.Contains("orchestrator", markdown, StringComparison.Ordinal);
        Console.WriteLine($"G807 AC1 steward-guide-json: route={root.GetProperty("route").GetString()}; sections=identity,alone,handoff,refusal,dialog,worktree,g796; exit_code=0");
        Console.WriteLine($"G807 AC1 steward-guide-markdown: headings=7; output_bytes={markdown.Length}; exit_code=0");
    }

    [Fact]
    public void G796Boundary_IsExplicitAndDoesNotGrantJudgment_G807_AC2()
    {
        var guide = GuideStewardThreadCommand.BuildGuide();
        var boundary = guide.G796Boundary;

        Assert.Contains("not a specialist", boundary, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("weight on transmission", boundary, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("answers neither design nor review questions itself", boundary, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("bytes", boundary, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("digest", boundary, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("origin", boundary, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Codex", boundary, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Claude", boundary, StringComparison.OrdinalIgnoreCase);
        Console.WriteLine($"G807 AC2 g796-boundary: not_specialist=true; transmission_weight=true; answers_design_or_review=false; bytes_digest_origin=retained");
    }

    [Fact]
    public void ExistingGuideRoutesRemainReachableWithoutWarnings_G807_AC3()
    {
        var routes = new (string Name, string[] Args)[]
        {
            ("design-thread", ["guide", "design-thread", "--format", "json"]),
            ("orchestrator-thread", ["guide", "orchestrator-thread", "--format", "json"]),
            ("implementation-loop", ["guide", "workflow", "task", "implementation-loop", "--format", "json"]),
            ("review-next-slice-loop", ["guide", "workflow", "task", "review-next-slice-loop", "--format", "json"]),
        };

        foreach (var (name, args) in routes)
        {
            using var writer = new StringWriter();
            var exitCode = CommandRouter.Execute(args, CreateContext(), writer);
            var output = writer.ToString();
            Assert.Equal(0, exitCode);
            Assert.NotEmpty(output);
            Assert.DoesNotContain("Unknown argument", output, StringComparison.Ordinal);
            Assert.DoesNotContain("Refusing to render", output, StringComparison.Ordinal);
            Assert.DoesNotContain("warning:", output, StringComparison.OrdinalIgnoreCase);
            Console.WriteLine($"G807 AC3 route={name}; exit_code={exitCode}; warning=false; output_bytes={output.Length}");
        }
    }

    [Fact]
    public void ExistingRoleEnumerationsAndDiscoverabilityIncludeSteward_G807_AC5_AC6()
    {
        using var modelWriter = new StringWriter();
        Assert.Equal(0, GuideModelCommand.Execute(CreateContext(), ["--format", "json"], modelWriter));
        using var model = JsonDocument.Parse(modelWriter.ToString());
        var orchestrationRoles = model.RootElement
            .GetProperty("execution_orchestration_model")
            .GetProperty("roles")
            .EnumerateArray()
            .Select(role => role.GetString() ?? string.Empty)
            .ToArray();
        Assert.Contains(orchestrationRoles, role => role.StartsWith("steward —", StringComparison.Ordinal));

        using var listWriter = new StringWriter();
        Assert.Equal(0, GuideCommandsListCommand.Execute(CreateContext(), ["--format", "json"], listWriter));
        using var list = JsonDocument.Parse(listWriter.ToString());
        Assert.Contains(
            list.RootElement.GetProperty("groups").EnumerateArray(),
            group => group.GetProperty("name").GetString() == "guide steward-thread");

        using var helpWriter = new StringWriter();
        Assert.Equal(0, GuideHelpCommand.Execute(CreateContext(), ["--format", "json"], helpWriter));
        using var help = JsonDocument.Parse(helpWriter.ToString());
        Assert.Contains(
            help.RootElement.GetProperty("subcommands").EnumerateArray(),
            subcommand => subcommand.GetProperty("name").GetString() == "steward-thread");

        Assert.NotNull(GuideRoleContractGuidance.Resolve("steward"));
        Assert.Equal("intent-cli guide steward-thread", GuideRoleContractGuidance.Resolve("steward")!.Guide);
        Console.WriteLine("G807 AC5/AC6 role-enumeration: steward=present; guide_commands_list=present; guide_help=present; onboarding-pointer=present");
    }

    [Fact]
    public void FullStewardPayloadHasNoVendorAdjacencyOrSizeThresholdRule_G807_AC5()
    {
        using var writer = new StringWriter();
        Assert.Equal(0, GuideStewardThreadCommand.Execute(CreateContext(), ["--format", "markdown"], writer));
        var output = writer.ToString();
        var vendorRole = new Regex(@"(?i)\b(?:claude|codex|opencode|astra|fable|luna|terra|sol)\b[^\r\n]{0,80}\bsteward\b|\bsteward\b[^\r\n]{0,80}\b(?:claude|codex|opencode|astra|fable|luna|terra|sol)\b", RegexOptions.Compiled);
        Assert.Empty(vendorRole.Matches(output));
        Assert.Contains("No vendor or runtime is a role", output, StringComparison.Ordinal);
        Assert.Contains("No size threshold", output, StringComparison.Ordinal);
        Console.WriteLine("G807 AC5 full-payload-scan: vendor_adjacent_steward=0; size_threshold_rule=0; runtime_default=0");
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
