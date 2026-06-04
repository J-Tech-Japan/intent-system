using IntentSystem.Cli.Commands;

namespace IntentSystem.Cli.Tests;

/// <summary>
/// G462: pure tests for the host-only packet classifier. The central
/// regression is G458 / issue #1018: a product-goal / intent-tree refresh
/// targeting only `intents/intent-cli/**` was mis-published as a child
/// implementation issue.
/// </summary>
public sealed class HostOnlyPacketClassifierTests
{
    private const string Aic1018Body = """
## Target Repo / Path / Part

- Target repo: `J-Tech-Japan/intent-system`
- Target paths: `intents/intent-cli/intent-tree/purpose/04-product-goal.md`, `intents/intent-cli/intent-tree/00-map.md`, `intents/intent-cli/README.md`
- Target part: canonical product goal
""";

    private const string MixedBody = """
## Target Repo / Path / Part

- Target paths: `intents/intent-cli/intent-tree/00-map.md`, `docs`, `README.md`
""";

    private const string ChildBody = """
## Target Repo / Path / Part

- Target paths: `src/IntentSystem.Cli/Commands`, `tests/IntentSystem.Cli.Tests`, `docs`
""";

    [Fact]
    public void Issue1018Regression_AllHostOwnedPaths_ClassifiesHostOnly()
    {
        var verdict = HostOnlyPacketClassifier.Classify(Aic1018Body);

        Assert.True(verdict.IsHostOnly);
        Assert.Empty(verdict.ChildOwnedPaths);
        Assert.Equal(3, verdict.TargetPaths.Count);
        Assert.All(verdict.HostOwnedPaths, p => Assert.StartsWith("intents/", p));
    }

    [Fact]
    public void MixedPaths_WithDocsAndReadme_IsNotHostOnly()
    {
        var verdict = HostOnlyPacketClassifier.Classify(MixedBody);

        Assert.False(verdict.IsHostOnly);
        Assert.Contains("docs", verdict.ChildOwnedPaths);
        Assert.Contains("README.md", verdict.ChildOwnedPaths);
    }

    [Fact]
    public void ChildOnlyPaths_IsNotHostOnly()
    {
        var verdict = HostOnlyPacketClassifier.Classify(ChildBody);

        Assert.False(verdict.IsHostOnly);
        Assert.Empty(verdict.HostOwnedPaths);
    }

    [Fact]
    public void NoTargetPathsLine_IsNotHostOnly()
    {
        var verdict = HostOnlyPacketClassifier.Classify("# An issue\n\nNo target paths section here.");

        Assert.False(verdict.IsHostOnly);
        Assert.Empty(verdict.TargetPaths);
    }

    [Fact]
    public void DotIntentCliPaths_AreHostOwned()
    {
        Assert.True(HostOnlyPacketClassifier.IsHostOwnedPath(".intent-cli/queue-state.json"));
        Assert.True(HostOnlyPacketClassifier.IsHostOwnedPath("intents/intent-cli/specs/05.md"));
        Assert.True(HostOnlyPacketClassifier.IsHostOwnedPath("`intents/x`"));
        Assert.True(HostOnlyPacketClassifier.IsHostOwnedPath("AGENTS.md"));
        Assert.False(HostOnlyPacketClassifier.IsHostOwnedPath("src/Foo.cs"));
        Assert.False(HostOnlyPacketClassifier.IsHostOwnedPath("docs/en/01.md"));
        Assert.False(HostOnlyPacketClassifier.IsHostOwnedPath("README.md"));
    }

    [Fact]
    public void AllPathsHostOwned_RequiresNonEmpty()
    {
        Assert.False(HostOnlyPacketClassifier.AllPathsAreHostOwned(System.Array.Empty<string>()));
        Assert.True(HostOnlyPacketClassifier.AllPathsAreHostOwned(new[] { "intents/x", ".intent-cli/y" }));
        Assert.False(HostOnlyPacketClassifier.AllPathsAreHostOwned(new[] { "intents/x", "src/y" }));
    }

    [Fact]
    public void ExtractTargetPaths_DeduplicatesAndPreservesOrder()
    {
        var paths = HostOnlyPacketClassifier.ExtractTargetPaths(
            "Target paths: `a/b`, `a/b`, `c/d`");
        Assert.Equal(new[] { "a/b", "c/d" }, paths);
    }
}
