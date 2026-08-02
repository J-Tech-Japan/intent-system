using System.Text;
using System.Text.Json;
using IntentSystem.Cli;
using IntentSystem.Cli.Commands;
using IntentSystem.Cli.Models;

namespace IntentSystem.Cli.Tests;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class SkillVersionLineageG573Collection
{
    public const string Name = "Skill version lineage G573";
}

[Collection(SkillVersionLineageG573Collection.Name)]
public sealed class SkillVersionLineageG573Tests : IDisposable
{
    private readonly string root = Path.Combine(
        Path.GetTempPath(), "intent-cli-skill-lineage-tests", Guid.NewGuid().ToString("n"));
    private readonly Func<string>? previousUserHome = SkillTargets.UserHomeFactory;
    private readonly Func<EmbeddedSkill, string>? previousBodyReader = SkillAssets.BodyReader;
    private readonly Func<EmbeddedSkill, IReadOnlyList<string>>? previousLineageReader = SkillAssets.LineageReader;

    public SkillVersionLineageG573Tests()
    {
        Directory.CreateDirectory(RepoRoot);
        Directory.CreateDirectory(UserHome);
        SkillTargets.UserHomeFactory = () => UserHome;
    }

    private string RepoRoot => Path.Combine(root, "repo");

    private string UserHome => Path.Combine(root, "home");

    [Fact]
    public void CurrentEmbeddedSkill_IsPresentInShippedLineage_G573()
    {
        var skill = SkillAssets.Find("intent-cli")!;
        var body = SkillAssets.ReadBody(skill);

        Assert.Contains(SkillAssets.ComputeNormalizedHash(body), SkillAssets.ReadLineage(skill));
    }

    [Fact]
    public void UnlistedEmbeddedSkillContent_FailsClosed_G573()
    {
        var skill = SkillAssets.Find("intent-cli")!;
        var shipped = SkillAssets.ReadBody(skill);
        SkillAssets.BodyReader = _ => shipped + "\nfuture unlisted content\n";

        var exception = Assert.Throws<InvalidOperationException>(() => SkillAssets.ReadBody(skill));

        Assert.Contains("absent from its shipped-version lineage", exception.Message, StringComparison.Ordinal);
        Assert.Contains("add normalized SHA-256", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void FixtureWalk_ClassifiesNudgesUpdatesAndProtectsLocalEdits_G573()
    {
        var path = Path.Combine(RepoRoot, ".github", "skills", "intent-cli", "SKILL.md");

        Assert.Equal(0, Run(["skill", "list", "--format", "json"], out var absentReport));
        Assert.Equal("not-installed", StateFor(absentReport, "copilot", "repo"));
        Assert.DoesNotContain("Skill update available", RenderGuide(["guide", "model"]), StringComparison.Ordinal);

        Assert.Equal(0, Run(["skill", "install", "--target", "copilot"], out _));
        Assert.Equal("current", StateFor(ListJson(), "copilot", "repo"));

        var previous = File.ReadAllText(path);
        var current = previous + "\nFuture shipped dispatcher content.\n";
        UseFutureVersion(previous, current);

        var staleReport = ListJson();
        Assert.Equal("stale-shipped", StateFor(staleReport, "copilot", "repo"));
        Assert.True(UpdateAvailableFor(staleReport, "copilot", "repo"));

        Assert.Equal(0, Run(["skill", "diff", "--target", "copilot"], out var staleDiff));
        Assert.Contains("stale-shipped", staleDiff, StringComparison.Ordinal);
        Assert.Contains("previous shipped version → current embedded version", staleDiff, StringComparison.Ordinal);

        var markdown = RenderGuide(["guide", "model"]);
        const string exactCommand = "intent-cli skill install --target copilot --scope repo --skill intent-cli";
        Assert.Equal(1, Count(markdown, "Skill update available"));
        Assert.Contains(exactCommand, markdown, StringComparison.Ordinal);

        var guideHelp = RenderGuide(["guide", "--help"]);
        Assert.Equal(1, Count(guideHelp, "Skill update available"));
        Assert.Contains(exactCommand, guideHelp, StringComparison.Ordinal);

        using (var json = JsonDocument.Parse(RenderGuide(["guide", "model", "--format", "json"])))
        {
            Assert.Contains(exactCommand, json.RootElement.GetProperty("skill_update_nudge").GetString());
        }

        Assert.Equal(0, Run(["skill", "install", "--target", "copilot"], out var updateOutput));
        Assert.Contains("updated-stale", updateOutput, StringComparison.Ordinal);
        Assert.Equal(SkillAssets.Normalize(current), SkillAssets.Normalize(File.ReadAllText(path)));
        Assert.DoesNotContain("Skill update available", RenderGuide(["guide", "model"]), StringComparison.Ordinal);

        var edited = File.ReadAllText(path) + "\nOperator edit.\n";
        File.WriteAllText(path, edited);
        var modifiedReport = ListJson();
        Assert.Equal("locally-modified", StateFor(modifiedReport, "copilot", "repo"));
        Assert.False(UpdateAvailableFor(modifiedReport, "copilot", "repo"));
        Assert.DoesNotContain("Skill update available", RenderGuide(["guide", "model"]), StringComparison.Ordinal);

        Assert.Equal(1, Run(["skill", "install", "--target", "copilot"], out var refusal));
        Assert.Contains("refused-drifted", refusal, StringComparison.Ordinal);
        Assert.Equal(edited, File.ReadAllText(path));
    }

    [Fact]
    public void GuideOutput_IsByteIdenticalWhenLocalProbeFails_G573()
    {
        var expected = RenderGuide(["guide", "model"]);
        SkillAssets.BodyReader = _ => throw new IOException("simulated unreadable skill asset");

        var actual = RenderGuide(["guide", "model"]);

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void ThreePhasePlan_DoesNotUpdateStaleSiblingWhenLocalEditAborts_G573()
    {
        Assert.Equal(0, Run(["skill", "install", "--target", "all"], out _));
        var skill = SkillAssets.Find("intent-cli")!;
        var previous = SkillAssets.ReadBody(skill);
        var current = previous + "\nFuture shipped dispatcher content.\n";
        UseFutureVersion(previous, current);

        var claude = Path.Combine(RepoRoot, ".claude", "skills", "intent-cli", "SKILL.md");
        var codex = Path.Combine(UserHome, ".codex", "skills", "intent-cli", "SKILL.md");
        var copilot = Path.Combine(RepoRoot, ".github", "skills", "intent-cli", "SKILL.md");
        var edited = File.ReadAllText(codex) + "\nOperator edit.\n";
        File.WriteAllText(codex, edited);

        Assert.Equal(1, Run(["skill", "install", "--target", "all"], out var output));

        Assert.Contains("refused-drifted", output, StringComparison.Ordinal);
        Assert.Contains("skipped-plan-aborted", output, StringComparison.Ordinal);
        Assert.Equal(SkillAssets.Normalize(previous), SkillAssets.Normalize(File.ReadAllText(claude)));
        Assert.Equal(edited, File.ReadAllText(codex));
        Assert.Equal(SkillAssets.Normalize(previous), SkillAssets.Normalize(File.ReadAllText(copilot)));
    }

    private void UseFutureVersion(string previous, string current)
    {
        SkillAssets.BodyReader = _ => current;
        SkillAssets.LineageReader = _ =>
        [
            SkillAssets.ComputeNormalizedHash(previous),
            SkillAssets.ComputeNormalizedHash(current),
        ];
    }

    private string ListJson()
    {
        Assert.Equal(0, Run(["skill", "list", "--format", "json"], out var output));
        return output;
    }

    private string RenderGuide(string[] args)
    {
        Assert.Equal(0, Run(args, out var output));
        return output;
    }

    private int Run(string[] args, out string output)
    {
        var builder = new StringBuilder();
        using var writer = new StringWriter(builder);
        var exitCode = CommandRouter.Execute(args, BuildContext(), writer);
        output = builder.ToString();
        return exitCode;
    }

    private static string? StateFor(string report, string target, string scope) =>
        InstallationFor(report, target, scope).GetProperty("state").GetString();

    private static bool UpdateAvailableFor(string report, string target, string scope) =>
        InstallationFor(report, target, scope).GetProperty("update_available").GetBoolean();

    private static JsonElement InstallationFor(string report, string target, string scope)
    {
        using var document = JsonDocument.Parse(report);
        return document.RootElement
            .GetProperty("skills")[0]
            .GetProperty("installations")
            .EnumerateArray()
            .Single(element =>
                element.GetProperty("target").GetString() == target
                && element.GetProperty("scope").GetString() == scope)
            .Clone();
    }

    private CliContext BuildContext() => new()
    {
        RepoRoot = RepoRoot,
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

    private static int Count(string value, string needle) =>
        (value.Length - value.Replace(needle, string.Empty, StringComparison.Ordinal).Length) / needle.Length;

    public void Dispose()
    {
        SkillTargets.UserHomeFactory = previousUserHome;
        SkillAssets.BodyReader = previousBodyReader;
        SkillAssets.LineageReader = previousLineageReader;

        try
        {
            Directory.Delete(root, recursive: true);
        }
        catch (IOException)
        {
            // A leftover temp directory is not worth failing a test over.
        }
    }
}
