namespace IntentSystem.Cli.Tests;

public sealed class NotifySupervisionAtomicAppendG768Tests
{
    [Fact]
    public void AppendContractIsMirroredInGuidesAndAdr_G768()
    {
        var root = RepoVersionPolicySource.RepoRoot();
        var english = File.ReadAllText(Path.Combine(root, "docs", "en", "12-agent-message-orchestration.md"));
        var japanese = File.ReadAllText(Path.Combine(root, "docs", "ja", "12-agent-message-orchestration.md"));
        var adr = File.ReadAllText(Path.Combine(root, "docs", "adr", "0014-supervision-atomic-per-record-appends.md"));

        Assert.Contains("### Atomic per-record supervision history appends (G768", english, StringComparison.Ordinal);
        Assert.Contains("one UTF-8 append operation", english, StringComparison.Ordinal);
        Assert.Contains("does not add supervisor killing, stopping, ranking, election, or leasing", english, StringComparison.Ordinal);
        Assert.Contains("### supervision history の record 単位 append (G768", japanese, StringComparison.Ordinal);
        Assert.Contains("record を失いません", japanese, StringComparison.Ordinal);
        Assert.Contains("supervisor の", japanese, StringComparison.Ordinal);
        Assert.Contains("# ADR 0014: supervision history appends are atomic per record", adr, StringComparison.Ordinal);
        Assert.Contains("OS append primitive", adr, StringComparison.Ordinal);
        Assert.Contains("cycle, stall, and prompt-audit", adr, StringComparison.Ordinal);
    }
}
