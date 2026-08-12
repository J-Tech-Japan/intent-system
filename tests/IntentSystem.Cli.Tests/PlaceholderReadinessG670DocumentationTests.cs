namespace IntentSystem.Cli.Tests;

public sealed class PlaceholderReadinessG670DocumentationTests
{
    [Fact]
    public void SharedPublishGateReadinessIsDocumentedInBothLanguagesAndLedger()
    {
        var root = RepoVersionPolicySource.RepoRoot();

        foreach (var language in new[] { "en", "ja" })
        {
            var reference = File.ReadAllText(
                Path.Combine(root, "docs", language, "09-developer-reference.md"));
            Assert.Contains("G670", reference, StringComparison.Ordinal);
            Assert.Contains("backlog-ready-idle", reference, StringComparison.Ordinal);
            Assert.Contains("issue-cut-ready", reference, StringComparison.Ordinal);
            Assert.Contains("publish-gate", reference, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("preview-through-1.x", reference, StringComparison.Ordinal);

            var ledger = File.ReadAllText(
                Path.Combine(root, "docs", language, "1.0-compatibility-ledger.md"));
            Assert.Contains("G670", ledger, StringComparison.Ordinal);
            Assert.Contains("preview-through-1.x", ledger, StringComparison.Ordinal);
        }
    }
}
