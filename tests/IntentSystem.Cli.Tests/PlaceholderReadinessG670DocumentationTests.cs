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

            var ledgerRows = ledger.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            foreach (var command in new[] { "automation stalled-work", "intent next-slice" })
            {
                var commandRow = Assert.Single(
                    ledgerRows,
                    row => row.StartsWith($"| `{command}` |", StringComparison.Ordinal));
                Assert.Contains("stable-at-1.0", commandRow, StringComparison.Ordinal);
                Assert.DoesNotContain("G670", commandRow, StringComparison.Ordinal);
            }

            var g670Row = Assert.Single(
                ledgerRows,
                row => row.StartsWith("| shared publish-gate readiness exclusion", StringComparison.Ordinal));
            Assert.Contains("G670", g670Row, StringComparison.Ordinal);
            Assert.Contains("preview-through-1.x", g670Row, StringComparison.Ordinal);
        }
    }
}
