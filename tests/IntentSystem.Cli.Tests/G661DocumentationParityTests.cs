namespace IntentSystem.Cli.Tests;

public sealed class G661DocumentationParityTests
{
    [Fact]
    public void OrchestrationReference_CarriesFiveFixesInEnglishAndJapanese_G661()
    {
        var root = RepoVersionPolicySource.RepoRoot();
        var required = new[]
        {
            "knowledge-writeback-record",
            "knowledge-writeback-recorded-uncommitted",
            "--reactivate --evidence",
            "shipped-while-retired-contradiction",
            "publish validator",
            "guide_reachability:",
            ".intent-cli/runs.jsonl merge=union",
            ".intent-cli/**/*.jsonl merge=union",
            ".intent-cli/supervision/**/cycles.jsonl",
            ".intent-cli/supervision/**/stalls.jsonl",
        };

        foreach (var language in new[] { "en", "ja" })
        {
            var content = File.ReadAllText(Path.Combine(root, "docs", language, "12-agent-message-orchestration.md"));
            Assert.Contains("G661", content, StringComparison.Ordinal);
            Assert.Contains("preview", content, StringComparison.OrdinalIgnoreCase);
            Assert.All(required, item => Assert.Contains(item, content, StringComparison.Ordinal));
        }
    }
}
