using System.Text.Json;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

/// <summary>
/// G563: coverage for the RETIRED `guide skill-pack` surface. G488 rendered a
/// skill body the operator copied out by hand; G559 shipped an embedded
/// SKILL.md with a real installer, which left two artifacts named `intent-cli`
/// disagreeing about how the skill is obtained. These tests pin the retirement:
/// the command prints only a pointer at the `skill` group, no skill body, and
/// — critically — no copy-out instruction of any kind.
/// </summary>
public sealed class GuideSkillPackCommandTests
{
    [Fact]
    public void Execute_Markdown_IsADeprecationPointerAtTheSkillCommandGroup_G563()
    {
        var output = RunMarkdown(["--domain", "intent-cli", "--target-repo", "owner/repo"]);

        Assert.Contains("# `guide skill-pack` — deprecated (G563)", output, StringComparison.Ordinal);
        Assert.Contains("DEPRECATED (G563)", output, StringComparison.Ordinal);
        Assert.Contains("intent-cli skill install", output, StringComparison.Ordinal);
        Assert.Contains("intent-cli skill list", output, StringComparison.Ordinal);
        Assert.Contains("intent-cli skill diff", output, StringComparison.Ordinal);
        // The point of the retirement: exactly one artifact carries the name.
        Assert.Contains("exactly one artifact named `intent-cli`", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_Markdown_RendersNoSkillBodyAndNoCopyOutInstruction_G563()
    {
        var output = RunMarkdown(["--domain", "intent-cli", "--target-repo", "owner/repo"]);

        // The rendered skill body is gone: no role sections, no concept model,
        // no fixed-condition table, no guardrail list.
        Assert.DoesNotContain("## Concept model", output, StringComparison.Ordinal);
        Assert.DoesNotContain("## Fixed conditions", output, StringComparison.Ordinal);
        Assert.DoesNotContain("## Roles", output, StringComparison.Ordinal);
        Assert.DoesNotContain("## Guardrails", output, StringComparison.Ordinal);
        Assert.DoesNotContain("## Fail closed", output, StringComparison.Ordinal);

        // And the workflow `skill install` replaced is gone with it. This is
        // the defect the retirement exists to remove: a live surface telling
        // an agent to paste a skill body somewhere by hand.
        Assert.DoesNotContain("copy the rendered body", output, StringComparison.Ordinal);
        Assert.DoesNotContain("This command writes NO files", output, StringComparison.Ordinal);
        Assert.DoesNotContain("dry-run", output, StringComparison.Ordinal);
        Assert.DoesNotContain("suggested destination", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_Json_HasDeprecationShape_PointingAtTheSkillGroup_G563()
    {
        using var writer = new StringWriter();
        var exitCode = GuideSkillPackCommand.Execute(
            CreateContext(),
            ["--domain", "intent-cli", "--target-repo", "owner/repo", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var root = document.RootElement;

        Assert.Equal("deprecated", root.GetProperty("status").GetString());
        Assert.Equal("skill", root.GetProperty("superseded_by").GetString());
        Assert.Contains("DEPRECATED", root.GetProperty("summary").GetString()!, StringComparison.Ordinal);
        Assert.Contains("G559", root.GetProperty("reason").GetString()!, StringComparison.Ordinal);

        var useInstead = root.GetProperty("use_instead")
            .EnumerateArray()
            .Select(entry => entry.GetString()!)
            .ToArray();
        Assert.Equal(3, useInstead.Length);
        Assert.Contains(useInstead, entry => entry.StartsWith("intent-cli skill list", StringComparison.Ordinal));
        Assert.Contains(useInstead, entry => entry.StartsWith("intent-cli skill install", StringComparison.Ordinal));
        Assert.Contains(useInstead, entry => entry.StartsWith("intent-cli skill diff", StringComparison.Ordinal));

        // The old skill-body shape is gone from JSON too, so a consumer that
        // parsed it fails loudly instead of reading a stale body.
        Assert.False(root.TryGetProperty("roles", out _));
        Assert.False(root.TryGetProperty("fixed_conditions", out _));
        Assert.False(root.TryGetProperty("install_plan", out _));
        Assert.False(root.TryGetProperty("guardrails", out _));
    }

    [Fact]
    public void Execute_StillAcceptsTheOldArguments_SoExistingInvocationsReachThePointer_G563()
    {
        // A caller with the G488 invocation in a script should land on the
        // pointer, not on an argument error that hides the redirection.
        var output = RunMarkdown(["--domain", "some-domain", "--target-repo", "owner/repo"]);

        Assert.Contains("deprecated (G563)", output, StringComparison.Ordinal);
        // …and the arguments no longer select any content.
        Assert.DoesNotContain("some-domain", output, StringComparison.Ordinal);
        Assert.DoesNotContain("owner/repo", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_UnknownArgument_ExitsOne()
    {
        using var writer = new StringWriter();
        var exitCode = GuideSkillPackCommand.Execute(CreateContext(), ["--nope"], writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("Unknown argument", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_Help_AnnouncesTheDeprecationAndTheReplacement_G563()
    {
        using var writer = new StringWriter();
        var exitCode = GuideSkillPackCommand.Execute(CreateContext(), ["--help"], writer);

        Assert.Equal(0, exitCode);
        var output = writer.ToString();
        Assert.Contains("guide skill-pack", output, StringComparison.Ordinal);
        Assert.Contains("DEPRECATED (G563)", output, StringComparison.Ordinal);
        Assert.Contains("intent-cli skill install", output, StringComparison.Ordinal);
    }

    private static string RunMarkdown(string[] args)
    {
        using var writer = new StringWriter();
        var fullArgs = args.Concat(new[] { "--format", "markdown" }).ToArray();
        var exitCode = GuideSkillPackCommand.Execute(CreateContext(), fullArgs, writer);
        Assert.Equal(0, exitCode);
        return writer.ToString();
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
                    WorktreeRoot = ".intent-cli/worktrees",
                },
            },
        };
    }
}
