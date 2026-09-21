using System.Text.RegularExpressions;

namespace IntentSystem.Cli.Tests;

public sealed class G847DocumentationTests
{
    [Fact]
    public void EnglishAndJapaneseDocs_StateTheMeasuredScopeAndConservativeLimit()
    {
        foreach (var path in DocumentationPaths())
        {
            var content = File.ReadAllText(path);
            var normalized = Regex.Replace(content, @"\s+", " ");

            Assert.Contains("HardLimitBytes = 65536", content, StringComparison.Ordinal);
            Assert.Contains("WarningThresholdBytes = 58000", content, StringComparison.Ordinal);
            Assert.Contains("submitted body content", normalized, StringComparison.OrdinalIgnoreCase);
            if (path.Contains(Path.Combine("docs", "ja"), StringComparison.Ordinal))
            {
                Assert.Contains("wire 上の bytes", normalized, StringComparison.OrdinalIgnoreCase);
            }
            else
            {
                Assert.Contains("not bytes on the wire", normalized, StringComparison.OrdinalIgnoreCase);
            }
            Assert.Contains("content-dependent", normalized, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("StrictUtf8FileReader.ReadText", content, StringComparison.Ordinal);
            Assert.Contains("UTF-16", content, StringComparison.Ordinal);
            Assert.Contains("UTF-32", content, StringComparison.Ordinal);
            Assert.Contains("65,536 is intent-cli's own conservative limit", normalized, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Ledgers_StateBothPinnedRefusalMessagesExitCodesAndNoResultSurface()
    {
        foreach (var path in LedgerPaths())
        {
            var content = File.ReadAllText(path);

            Assert.Contains("Issue body is <n> bytes, which exceeds the 65536-byte limit.", content, StringComparison.Ordinal);
            Assert.Contains("Issue body is not valid UTF-8 at byte offset <k> in <file>.", content, StringComparison.Ordinal);
            Assert.Contains("exit 1", content, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("--format", content, StringComparison.Ordinal);
            Assert.Contains("result surface", content, StringComparison.OrdinalIgnoreCase);
            if (path.Contains(Path.Combine("docs", "ja"), StringComparison.Ordinal))
            {
                Assert.Contains("既存の不正確さ", content, StringComparison.Ordinal);
            }
            else
            {
                Assert.Contains("pre-existing", content, StringComparison.OrdinalIgnoreCase);
            }
        }
    }

    private static IEnumerable<string> DocumentationPaths()
    {
        var root = RepoVersionPolicySource.RepoRoot();
        yield return Path.Combine(root, "docs", "en", "04-packets-issues.md");
        yield return Path.Combine(root, "docs", "ja", "04-packets-issues.md");
        yield return Path.Combine(root, "docs", "en", "1.0-compatibility-ledger.md");
        yield return Path.Combine(root, "docs", "ja", "1.0-compatibility-ledger.md");
    }

    private static IEnumerable<string> LedgerPaths()
    {
        var root = RepoVersionPolicySource.RepoRoot();
        yield return Path.Combine(root, "docs", "en", "1.0-compatibility-ledger.md");
        yield return Path.Combine(root, "docs", "ja", "1.0-compatibility-ledger.md");
    }
}
