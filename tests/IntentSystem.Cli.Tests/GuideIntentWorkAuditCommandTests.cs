using System.Text.Json;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

public sealed class GuideIntentWorkAuditCommandTests
{
    [Fact]
    public void Execute_NoArgs_RendersMarkdownTemplate_WithPlaceholders()
    {
        using var writer = new StringWriter();
        var exitCode = GuideIntentWorkAuditCommand.Execute(CreateContext(), [], writer);

        Assert.Equal(0, exitCode);
        var output = writer.ToString();
        Assert.Contains("# Intent-work audit — final-report template", output, StringComparison.Ordinal);
        Assert.Contains("## Read-only call expectations", output, StringComparison.Ordinal);
        Assert.Contains("## Mutation call boundaries", output, StringComparison.Ordinal);
        Assert.Contains("## Final-report sections to fill in", output, StringComparison.Ordinal);
        Assert.Contains("## Forbidden sources", output, StringComparison.Ordinal);
        Assert.Contains("intents/rules/**", output, StringComparison.Ordinal);
        Assert.Contains("local skill files", output, StringComparison.Ordinal);
        Assert.Contains("`<DOMAIN>`", output, StringComparison.Ordinal);
        Assert.Contains("`<TARGET-REPO>`", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_WithDomainAndTargetRepo_SubstitutesPlaceholders()
    {
        using var writer = new StringWriter();
        var exitCode = GuideIntentWorkAuditCommand.Execute(
            CreateContext(),
            ["--domain", "auth", "--target-repo", "owner/repo"],
            writer);

        Assert.Equal(0, exitCode);
        var output = writer.ToString();
        Assert.Contains("`auth`", output, StringComparison.Ordinal);
        Assert.Contains("`owner/repo`", output, StringComparison.Ordinal);
        Assert.DoesNotContain("`<DOMAIN>`", output, StringComparison.Ordinal);
        Assert.DoesNotContain("`<TARGET-REPO>`", output, StringComparison.Ordinal);
        Assert.Contains("intent-cli intent status --domain auth", output, StringComparison.Ordinal);
        Assert.Contains("intent-cli packet draft --execution-unit <id> --target-repo owner/repo --write", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_JsonFormat_ProducesParseableShape()
    {
        using var writer = new StringWriter();
        var exitCode = GuideIntentWorkAuditCommand.Execute(
            CreateContext(),
            ["--domain", "auth", "--target-repo", "owner/repo", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        var root = document.RootElement;
        Assert.Equal("auth", root.GetProperty("domain").GetString());
        Assert.Equal("owner/repo", root.GetProperty("target_repo").GetString());

        var readOnly = root.GetProperty("read_only_call_expectations");
        Assert.True(readOnly.GetArrayLength() >= 4, "read-only expectations should include onboarding, status, and search");
        var firstReadOnly = readOnly[0];
        Assert.Contains("guide model", firstReadOnly.GetProperty("command").GetString()!, StringComparison.Ordinal);
        Assert.Contains("Pure read", firstReadOnly.GetProperty("no_mutation").GetString()!, StringComparison.Ordinal);

        var mutations = root.GetProperty("mutation_call_boundaries");
        Assert.True(mutations.GetArrayLength() >= 2, "mutation boundaries should include record-answer and packet draft");
        Assert.Contains("Mutation:", mutations[0].GetProperty("no_mutation").GetString()!, StringComparison.Ordinal);

        var sections = root.GetProperty("final_report_sections")
            .EnumerateArray().Select(e => e.GetString()).ToArray();
        Assert.Contains(sections, s => s!.Contains("Read-only commands invoked", StringComparison.Ordinal));
        Assert.Contains(sections, s => s!.Contains("Mutation commands invoked", StringComparison.Ordinal));
        Assert.Contains(sections, s => s!.Contains("Skipped commands", StringComparison.Ordinal));
        Assert.Contains(sections, s => s!.Contains("Forbidden sources NOT consulted", StringComparison.Ordinal));

        var forbidden = root.GetProperty("forbidden_sources")
            .EnumerateArray().Select(e => e.GetString()).ToArray();
        Assert.Contains("intents/rules/**", forbidden);

        var rules = root.GetProperty("hard_rules")
            .EnumerateArray().Select(e => e.GetString()).ToArray();
        Assert.Contains(rules, r => r!.Contains("must not launch", StringComparison.Ordinal));
        Assert.Contains(rules, r => r!.Contains("audit template", StringComparison.Ordinal));
    }

    [Fact]
    public void Execute_RejectsUnknownArgument()
    {
        using var writer = new StringWriter();
        var exitCode = GuideIntentWorkAuditCommand.Execute(
            CreateContext(),
            ["--bogus"],
            writer);

        Assert.Equal(1, exitCode);
        Assert.Contains("Unknown argument", writer.ToString(), StringComparison.Ordinal);
        Assert.Contains("Usage: intent-cli guide intent-work audit", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void GuideIntentWorkRouter_DispatchesAuditSubcommand()
    {
        using var writer = new StringWriter();
        var exitCode = GuideIntentWorkCommand.Execute(
            CreateContext(),
            ["audit", "--format", "json"],
            writer);

        Assert.Equal(0, exitCode);
        using var document = JsonDocument.Parse(writer.ToString());
        Assert.Equal("<DOMAIN>", document.RootElement.GetProperty("domain").GetString());
    }

    [Fact]
    public void GuideIntentWorkSetup_MarkdownAndJson_PointAtAuditCommand()
    {
        using var markdownWriter = new StringWriter();
        GuideIntentWorkSetupCommand.Execute(
            CreateContext(),
            ["--kind", "domain-organize", "--domain", "auth", "--target-repo", "owner/repo"],
            markdownWriter);
        var markdown = markdownWriter.ToString();
        Assert.Contains("## Final-report audit (G295)", markdown, StringComparison.Ordinal);
        Assert.Contains("intent-cli guide intent-work audit", markdown, StringComparison.Ordinal);

        using var jsonWriter = new StringWriter();
        GuideIntentWorkSetupCommand.Execute(
            CreateContext(),
            ["--kind", "domain-organize", "--domain", "auth", "--target-repo", "owner/repo", "--format", "json"],
            jsonWriter);
        using var document = JsonDocument.Parse(jsonWriter.ToString());
        var auditField = document.RootElement.GetProperty("final_report_audit").GetString()!;
        Assert.Contains("intent-cli guide intent-work audit", auditField, StringComparison.Ordinal);
        Assert.Contains("intents/rules/**", auditField, StringComparison.Ordinal);
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
