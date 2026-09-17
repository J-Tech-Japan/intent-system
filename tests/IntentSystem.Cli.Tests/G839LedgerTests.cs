namespace IntentSystem.Cli.Tests;

public sealed class G839LedgerTests
{
    [Fact]
    public void Ledger_RecordsG839Entry()
    {
        var repoRoot = RepoVersionPolicySource.RepoRoot();
        foreach (var language in new[] { "en", "ja" })
        {
            var ledger = File.ReadAllText(Path.Combine(repoRoot, "docs", language, "1.0-compatibility-ledger.md"));
            Assert.Contains("G839", ledger, StringComparison.Ordinal);
            Assert.Contains("aborted-event-append-failed", ledger, StringComparison.Ordinal);
            Assert.Contains("recheck_cause", ledger, StringComparison.Ordinal);
            Assert.Contains("recovered-event-append-failed", ledger, StringComparison.Ordinal);
            Assert.Contains("Refused host PR transition", ledger, StringComparison.Ordinal);
            Assert.Contains("Would apply host PR transition", ledger, StringComparison.Ordinal);
            Assert.Contains("No labels were changed", ledger, StringComparison.Ordinal);

            var recoveryLine = Assert.Single(
                ledger.Split('\n'),
                line => line.StartsWith("| `automation pr-created-stale-recovery` |", StringComparison.Ordinal));
            Assert.Contains("G839", recoveryLine, StringComparison.Ordinal);

            var transitionLine = Assert.Single(
                ledger.Split('\n'),
                line => line.StartsWith("| `automation pr-transition` |", StringComparison.Ordinal));
            Assert.Contains("G839", transitionLine, StringComparison.Ordinal);
        }
    }
}
