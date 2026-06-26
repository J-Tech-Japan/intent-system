using System.Text.Json;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

/// <summary>
/// G488: coverage for the thin agent skill pack guide surface — one generic
/// `intent-cli` skill body with role sections. Asserts the authority boundary
/// (installed guidance wins), the fixed-condition fields (cwd/worktree/domain/
/// target-repo/base-branch/metadata-branch), the role sections (design,
/// implement, review, orchestrator, generic), the dry-run install plan, and the
/// ABSENCE of raw label / queue-state mutation recipes and hard-coded numbers —
/// across both markdown and JSON output.
/// </summary>
public sealed class GuideSkillPackCommandTests
{
    [Fact]
    public void Execute_Markdown_StatesInstalledGuidanceIsAuthoritativeOverThisSkill()
    {
        var output = RunMarkdown(["--domain", "intent-cli", "--target-repo", "owner/repo"]);

        Assert.Contains("# Agent skill pack — `intent-cli` (G488)", output, StringComparison.Ordinal);
        Assert.Contains("## Authority boundary", output, StringComparison.Ordinal);
        Assert.Contains("installed guidance wins", output, StringComparison.Ordinal);
        // It is explicitly NOT a workflow source of truth / runbook copy.
        Assert.Contains("NOT a workflow source of truth", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_Markdown_SurfacesFixedConditionSafetyFields()
    {
        var output = RunMarkdown(["--domain", "intent-cli", "--target-repo", "owner/repo"]);

        Assert.Contains("## Fixed conditions (safety boundaries)", output, StringComparison.Ordinal);
        Assert.Contains("**cwd / worktree**", output, StringComparison.Ordinal);
        Assert.Contains("**domain**", output, StringComparison.Ordinal);
        Assert.Contains("**target repo**", output, StringComparison.Ordinal);
        Assert.Contains("**implementation base branch**", output, StringComparison.Ordinal);
        Assert.Contains("**PR base branch**", output, StringComparison.Ordinal);
        Assert.Contains("**metadata branch**", output, StringComparison.Ordinal);
        Assert.Contains("**same-repo topology**", output, StringComparison.Ordinal);
        // Overrides are substituted into the surfaced values.
        Assert.Contains("`owner/repo`", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_Markdown_EmitsAllFiveRoleSections()
    {
        var output = RunMarkdown(["--domain", "intent-cli", "--target-repo", "owner/repo"]);

        Assert.Contains("### design", output, StringComparison.Ordinal);
        Assert.Contains("### implement", output, StringComparison.Ordinal);
        Assert.Contains("### review", output, StringComparison.Ordinal);
        Assert.Contains("### orchestrator", output, StringComparison.Ordinal);
        Assert.Contains("### generic", output, StringComparison.Ordinal);
        // Implement role still derives numbers from worker next-action, not memory.
        Assert.Contains("worker next-action", output, StringComparison.Ordinal);
        Assert.Contains("Closes #<issue>", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_Markdown_InstallPlanIsDryRunAndWritesNoFiles()
    {
        var output = RunMarkdown([]);

        Assert.Contains("## Install plan (dry-run — no files written)", output, StringComparison.Ordinal);
        Assert.Contains("mode: dry-run", output, StringComparison.Ordinal);
        Assert.Contains("suggested destination", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_Markdown_ContainsNoRawLabelOrQueueStateMutationRecipes()
    {
        var output = RunMarkdown(["--domain", "intent-cli", "--target-repo", "owner/repo"]);

        // No raw label-edit recipes leak into the portable skill body.
        Assert.DoesNotContain("--add-label", output, StringComparison.Ordinal);
        Assert.DoesNotContain("--remove-label", output, StringComparison.Ordinal);
        Assert.DoesNotContain("gh issue edit", output, StringComparison.Ordinal);
        // But it DOES warn against raw mutation as guardrails (naming queue-state
        // files only to forbid hand-editing them — never as a how-to recipe).
        Assert.Contains("## Guardrails", output, StringComparison.Ordinal);
        Assert.Contains("No raw gh label-mutation flags", output, StringComparison.Ordinal);
        Assert.Contains("No hand-editing queue-state", output, StringComparison.Ordinal);
        Assert.Contains("No hard-coded issue/PR numbers", output, StringComparison.Ordinal);
        Assert.Contains("No provider launch", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_Markdown_ContainsNoHardCodedIssueOrPrNumbers()
    {
        var output = RunMarkdown(["--domain", "intent-cli", "--target-repo", "owner/repo"]);

        // The only '#' usages are the placeholder Closes #<issue> / #<issue> forms.
        foreach (var line in output.Split('\n'))
        {
            var hashIndex = line.IndexOf('#');
            while (hashIndex >= 0 && hashIndex + 1 < line.Length)
            {
                Assert.False(
                    char.IsDigit(line[hashIndex + 1]),
                    $"Skill body must not hard-code issue/PR numbers; found one in: {line}");
                hashIndex = line.IndexOf('#', hashIndex + 1);
            }
        }
    }

    [Fact]
    public void Execute_Json_HasStableShape_WithRolesAndFixedConditionsAndGuardrails()
    {
        using var writer = new StringWriter();
        var exitCode = GuideSkillPackCommand.Execute(
            CreateContext(),
            ["--domain", "intent-cli", "--target-repo", "owner/repo", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var doc = JsonDocument.Parse(writer.ToString());
        var root = doc.RootElement;

        Assert.Equal("intent-cli", root.GetProperty("skill_name").GetString());
        Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("authority_boundary").GetString()));

        var roleNames = root.GetProperty("roles").EnumerateArray()
            .Select(r => r.GetProperty("name").GetString())
            .ToArray();
        Assert.Equal(new[] { "design", "implement", "review", "orchestrator", "generic" }, roleNames);

        var fields = root.GetProperty("fixed_conditions").EnumerateArray()
            .Select(c => c.GetProperty("field").GetString())
            .ToArray();
        Assert.Contains("cwd / worktree", fields);
        Assert.Contains("domain", fields);
        Assert.Contains("target repo", fields);
        Assert.Contains("implementation base branch", fields);

        Assert.Equal("dry-run", root.GetProperty("install_plan").GetProperty("mode").GetString());
        Assert.NotEmpty(root.GetProperty("guardrails").EnumerateArray());
        Assert.NotEmpty(root.GetProperty("concept_model").EnumerateArray());
        Assert.NotEmpty(root.GetProperty("first_call_sequence").EnumerateArray());
        Assert.NotEmpty(root.GetProperty("fail_closed").EnumerateArray());
    }

    [Fact]
    public void Execute_UsesConfiguredDomain_WhenNoOverrideProvided()
    {
        var output = RunMarkdown([]);

        // CreateContext configures domain = intent-cli; it is surfaced as the fixed condition value.
        Assert.Contains("**domain** = `intent-cli`", output, StringComparison.Ordinal);
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
    public void Execute_Help_ExplainsThinAndDryRun()
    {
        using var writer = new StringWriter();
        var exitCode = GuideSkillPackCommand.Execute(CreateContext(), ["--help"], writer);

        Assert.Equal(0, exitCode);
        var output = writer.ToString();
        Assert.Contains("guide skill-pack", output, StringComparison.Ordinal);
        Assert.Contains("thin", output, StringComparison.Ordinal);
        Assert.Contains("writes no files", output, StringComparison.Ordinal);
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
