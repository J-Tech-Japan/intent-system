namespace IntentSystem.Cli.Tests;

public sealed class CollaborativeIntentShapingDocTests
{
    [Fact]
    public void Doc_ExistsAndContainsCanonicalSections()
    {
        var path = Path.Combine(GetSolutionRoot(), "docs", "automation-templates", "collaborative-intent-shaping.md");
        Assert.True(File.Exists(path), $"missing doc: {path}");

        var content = File.ReadAllText(path);
        Assert.Contains("# Collaborative intent shaping — smoke guide", content, StringComparison.Ordinal);
        Assert.Contains("## Caller model", content, StringComparison.Ordinal);
        Assert.Contains("## Smoke flow (read-only by default)", content, StringComparison.Ordinal);
        Assert.Contains("## Where operator decisions are required", content, StringComparison.Ordinal);
        Assert.Contains("## Skill-file independence", content, StringComparison.Ordinal);
        Assert.Contains("## Failure modes (deterministic stops)", content, StringComparison.Ordinal);
        Assert.Contains("## Related installed surfaces", content, StringComparison.Ordinal);
    }

    [Fact]
    public void Doc_ReferencesEachInstalledSurfaceFromTheArc()
    {
        var path = Path.Combine(GetSolutionRoot(), "docs", "automation-templates", "collaborative-intent-shaping.md");
        var content = File.ReadAllText(path);

        // G249 collaborate
        Assert.Contains("intent-cli guide collaborate --kind feature-intake", content, StringComparison.Ordinal);

        // G250 interview Q/A
        Assert.Contains("intent-cli interview next-question", content, StringComparison.Ordinal);
        Assert.Contains("intent-cli interview record-answer", content, StringComparison.Ordinal);

        // G251 compile + draft
        Assert.Contains("intent-cli interview compile", content, StringComparison.Ordinal);
        Assert.Contains("intent-cli intent draft-from-interview", content, StringComparison.Ordinal);

        // G252 rules
        Assert.Contains("intent-cli guide rules --topic", content, StringComparison.Ordinal);

        // G241/G242/G243 supporting commands
        Assert.Contains("intent-cli intent status", content, StringComparison.Ordinal);
        Assert.Contains("intent-cli intent search", content, StringComparison.Ordinal);
        Assert.Contains("intent-cli intent explain", content, StringComparison.Ordinal);
        Assert.Contains("intent-cli intent next-slice --dry-run", content, StringComparison.Ordinal);
    }

    [Fact]
    public void Doc_NamesOperatorDecisionGatesAndSkillIndependence()
    {
        var path = Path.Combine(GetSolutionRoot(), "docs", "automation-templates", "collaborative-intent-shaping.md");
        var content = File.ReadAllText(path);

        Assert.Contains("Acceptance of each interview answer", content, StringComparison.Ordinal);
        Assert.Contains("Acceptance of the compiled draft", content, StringComparison.Ordinal);
        Assert.Contains("Promotion to a published child issue", content, StringComparison.Ordinal);

        Assert.Contains("intents/rules/*.md", content, StringComparison.Ordinal);
        Assert.Contains("local skill files", content, StringComparison.Ordinal);
        Assert.Contains("intent-cli guide rules --topic <name>", content, StringComparison.Ordinal);
        Assert.Contains("intent-cli automation summary --format json", content, StringComparison.Ordinal);
    }

    [Fact]
    public void Doc_NamesDeterministicStopsForEachFailureMode()
    {
        var path = Path.Combine(GetSolutionRoot(), "docs", "automation-templates", "collaborative-intent-shaping.md");
        var content = File.ReadAllText(path);

        Assert.Contains("`idle`", content, StringComparison.Ordinal);
        Assert.Contains("`clarification-required`", content, StringComparison.Ordinal);
        Assert.Contains("`skip-next-slice-due-to-wip`", content, StringComparison.Ordinal);
        Assert.Contains("unknown question id and no `--prompt`", content, StringComparison.Ordinal);
        Assert.Contains("session with no accepted answers", content, StringComparison.Ordinal);
    }

    [Fact]
    public void Readme_ListsCollaborativeIntentShapingDoc()
    {
        var path = Path.Combine(GetSolutionRoot(), "docs", "automation-templates", "README.md");
        var content = File.ReadAllText(path);

        Assert.Contains("collaborative-intent-shaping.md", content, StringComparison.Ordinal);
        Assert.Contains("G253", content, StringComparison.Ordinal);
    }

    [Fact]
    public void Doc_DoesNotReintroduceForbiddenInvocations()
    {
        // The collaborative shaping flow must not point operators at intent-cli run.
        var path = Path.Combine(GetSolutionRoot(), "docs", "automation-templates", "collaborative-intent-shaping.md");
        var content = File.ReadAllText(path);

        // `intent-cli run` may appear only inside the "What this guide is not" disclaimer section.
        var disclaimerIndex = content.IndexOf("## What this guide is not", StringComparison.Ordinal);
        Assert.True(disclaimerIndex > 0, "missing 'What this guide is not' section");
        var beforeDisclaimer = content[..disclaimerIndex];
        Assert.DoesNotContain("intent-cli run", beforeDisclaimer, StringComparison.Ordinal);

        // The flow must not recommend AI provider launch through intent-cli.
        Assert.DoesNotContain("intent-cli launch", content, StringComparison.Ordinal);
        Assert.DoesNotContain("intent-cli provider", content, StringComparison.Ordinal);
    }

    private static int CountSubstring(string content, string needle)
    {
        var count = 0;
        var index = 0;
        while ((index = content.IndexOf(needle, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += needle.Length;
        }
        return count;
    }

    private static string GetSolutionRoot()
    {
        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
    }
}
