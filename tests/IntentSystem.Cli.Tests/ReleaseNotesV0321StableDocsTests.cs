using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using IntentSystem.Cli.Infrastructure;

namespace IntentSystem.Cli.Tests;

/// <summary>
/// G873: stable 0.32.1 preparation is explicit, bilingual, and separate from
/// both the already published preview and the later stable publication.
/// </summary>
public sealed class ReleaseNotesV0321StableDocsTests
{
    private const string StableVersion = "0.32.1";
    private const string NextVersion = "0.32.2";
    private const string Baseline = "dba5724bc00f2207f601b5643cfb87595dc27795";
    private const string StableReleaseUrl = "https://github.com/J-Tech-Japan/intent-system/releases/tag/v0.32.1";
    private const string PreviewReleaseUrl = "https://github.com/J-Tech-Japan/intent-system/releases/tag/v0.32.1-preview.1";

    private static readonly (string Unit, string Pr)[] Units =
    [
        ("G851", "1848"), ("G852", "1850"), ("G844", "1851"), ("G843", "1852"),
        ("G814", "1854"), ("G853", "1856"), ("G854", "1860"), ("G855", "1863"),
        ("G856", "1871"), ("G857", "1874"), ("G858", "1877"), ("G859", "1880"),
        ("G860", "1882"), ("G861", "1884"), ("G862", "1886"), ("G863", "1888"),
        ("G864", "1890"), ("G865", "1892"), ("G866", "1894"), ("G867", "1896"),
        ("G868", "1898"), ("G869", "1900"), ("G870", "1902"), ("G871", "1904"),
    ];

    [Fact]
    public void VersionPolicyAndReplaceableNextNotesAreExact()
    {
        var root = RepoVersionPolicySource.RepoRoot();
        var policyText = File.ReadAllText(Path.Combine(root, "eng", "version.json"));
        Assert.Equal("{\n  \"stableVersion\": \"0.32.1\",\n  \"nextVersion\": \"0.32.2\"\n}\n", policyText);
        AssertVersionPolicy(policyText);
        var policy = RepoVersionPolicySource.Read();
        Assert.Equal(StableVersion, policy.StableVersion);
        Assert.Equal(NextVersion, policy.NextVersion);

        var wrongStable = MutateOnce(policyText, "\"stableVersion\": \"0.32.1\"", "\"stableVersion\": \"0.32.0\"");
        Assert.ThrowsAny<Exception>(() => AssertVersionPolicy(wrongStable));
        var swapped = MutateOnce(
            policyText,
            "\"stableVersion\": \"0.32.1\",\n  \"nextVersion\": \"0.32.2\"",
            "\"stableVersion\": \"0.32.2\",\n  \"nextVersion\": \"0.32.1\"");
        Assert.ThrowsAny<Exception>(() => AssertVersionPolicy(swapped));

        foreach (var language in new[] { "en", "ja" })
        {
            var stub = Normalize(Read(language, $"release-notes-v{NextVersion}.md").Replace("> ", string.Empty, StringComparison.Ordinal));
            AssertNextPlaceholder(stub, language);
            Assert.DoesNotContain("- G", stub, StringComparison.Ordinal);
            Assert.DoesNotContain("PR #", stub, StringComparison.Ordinal);
            Assert.DoesNotContain(StableReleaseUrl, stub, StringComparison.Ordinal);

            var placeholderTitle = language == "en" ? "# Release notes — intent-cli v0.32.2" : "# リリースノート — intent-cli v0.32.2";
            var wrongPlaceholderVersion = MutateOnce(stub, placeholderTitle, placeholderTitle.Replace("v0.32.2", "v0.32.3", StringComparison.Ordinal));
            Assert.ThrowsAny<Exception>(() => AssertNextPlaceholder(wrongPlaceholderVersion, language));
            var missingDraft = MutateOnce(stub, language == "en" ? "DRAFT / UNRELEASED" : "DRAFT / 未リリース", "PREPARED / NOT PUBLISHED");
            Assert.ThrowsAny<Exception>(() => AssertNextPlaceholder(missingDraft, language));
        }
    }

    [Theory]
    [InlineData("en")]
    [InlineData("ja")]
    public void StableNotesContainTheMeasuredInventoryAndPublicationBoundary(string language)
    {
        var notes = Normalize(Read(language, $"release-notes-v{StableVersion}.md"));
        AssertStableNotesContract(notes, language);

        var actual = Regex.Matches(
                notes,
                @"\| (G\d+) \| \[PR #(\d+)\]\(https://github\.com/J-Tech-Japan/intent-system/pull/(\d+)\) \|",
                RegexOptions.CultureInvariant)
            .Select(match => (match.Groups[1].Value, match.Groups[2].Value, match.Groups[3].Value))
            .ToArray();
        Assert.Equal(Units.Length, actual.Length);
        Assert.Equal(
            Units.Select(unit => (unit.Unit, unit.Pr, unit.Pr)),
            actual);
        Assert.Contains(language == "en" ? "24 measured product units" : "製品単位が 24 件", notes, StringComparison.Ordinal);
        Assert.Contains(language == "en" ? "G872 / PR #1906 is documentation preparation" : "G872 / PR #1906 は文書準備", notes, StringComparison.Ordinal);
        Assert.DoesNotContain("| G872 |", notes, StringComparison.Ordinal);
        Assert.DoesNotContain("| G873 |", notes, StringComparison.Ordinal);
        Assert.Contains(Baseline, notes, StringComparison.Ordinal);

        Assert.Contains("10,708", notes, StringComparison.Ordinal);
        Assert.Contains(language == "en" ? "six `NotExecuted` test-result rows" : "`NotExecuted` の test-result row 6 件", notes, StringComparison.Ordinal);
        Assert.Contains(language == "en" ? "raw TRX counters separately" : "raw TRX counter は別に", notes, StringComparison.Ordinal);
        Assert.Contains("10,714", notes, StringComparison.Ordinal);
        Assert.Contains("`notExecuted` 0", notes, StringComparison.Ordinal);
        Assert.Contains(language == "en" ? "not proof of a stable package" : "安定版 package や native artifact の証明ではありません", notes, StringComparison.Ordinal);
        if (language == "en")
        {
            Assert.Contains("smoke is likewise preview evidence", notes, StringComparison.Ordinal);
        }
        else
        {
            Assert.Contains("preview の証拠であり", notes, StringComparison.Ordinal);
            Assert.Contains("安定版 artifact の証明ではありません", notes, StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData("en")]
    [InlineData("ja")]
    public void StableNotesContractRejectsWrongVersionUrlChannelAndReceiptApplicability(string language)
    {
        var notes = Normalize(Read(language, $"release-notes-v{StableVersion}.md"));
        AssertStableNotesContract(notes, language);

        var title = language == "en" ? "# Release notes — intent-cli 0.32.1" : "# リリースノート — intent-cli 0.32.1";
        var wrongTitle = MutateOnce(notes, title, title.Replace("0.32.1", "0.32.0", StringComparison.Ordinal));
        Assert.ThrowsAny<Exception>(() => AssertStableNotesContract(wrongTitle, language));

        var stableLink = StableMarkdownLink(language);
        var wrongUrl = MutateOnce(
            notes,
            stableLink,
            stableLink.Replace(StableReleaseUrl, "https://github.com/J-Tech-Japan/intent-system/releases/tag/v0.32.0", StringComparison.Ordinal));
        Assert.Contains($"]({PreviewReleaseUrl})", wrongUrl, StringComparison.Ordinal);
        Assert.ThrowsAny<Exception>(() => AssertStableNotesContract(wrongUrl, language));

        var channel = language == "en"
            ? "Stable publication will move npm `latest` to `0.32.1`"
            : "安定版を実際に公開すると、npm の `latest` は `0.32.1` に移ります";
        var wrongChannel = MutateOnce(notes, channel, channel.Replace("latest", "preview", StringComparison.Ordinal));
        Assert.ThrowsAny<Exception>(() => AssertStableNotesContract(wrongChannel, language));

        var receiptCondition = language == "en"
            ? "when knowledge write-back is declared required"
            : "ナレッジの書き戻しが必須と宣言された場合";
        var replacementCondition = language == "en"
            ? "when knowledge write-back is optional"
            : "ナレッジの書き戻しが任意の場合";
        var missingCondition = MutateOnce(notes, receiptCondition, replacementCondition);
        Assert.ThrowsAny<Exception>(() => AssertStableNotesContract(missingCondition, language));
    }

    [Theory]
    [InlineData("en")]
    [InlineData("ja")]
    public void CurrentIndexInstallLedgerAndReferenceDistinguishStablePreviewAndNext(string language)
    {
        var root = RepoVersionPolicySource.RepoRoot();
        var index = Normalize(Read(language, "README.md"));
        var installRaw = Read(language, "01-install.md").Replace("\r\n", "\n", StringComparison.Ordinal);
        var install = Normalize(installRaw);
        var ledger = Normalize(Read(language, "1.0-compatibility-ledger.md"));
        var reference = Read(language, "09-developer-reference.md");
        var stableHeading = language == "en"
            ? "### Stable release readiness (v0.32.1)"
            : "### 安定版リリース準備 (v0.32.1)";

        Assert.Contains("release-notes-v0.32.1.md", index, StringComparison.Ordinal);
        Assert.Contains("release-notes-v0.32.1-preview.1.md", index, StringComparison.Ordinal);
        Assert.Contains("0.32.2", index, StringComparison.Ordinal);
        Assert.Contains("0.32.1", install, StringComparison.Ordinal);
        Assert.Contains("0.32.1-preview.1", install, StringComparison.Ordinal);
        Assert.Contains("0.32.0", install, StringComparison.Ordinal);
        Assert.Contains($"]({StableReleaseUrl})", install, StringComparison.Ordinal);
        Assert.Contains(PreviewReleaseUrl, install, StringComparison.Ordinal);
        Assert.Contains("dotnet tool install --tool-path <isolated-directory> JTechJapan.IntentSystem.Cli --version 0.32.1\n<isolated-directory>/intent-cli --version", installRaw, StringComparison.Ordinal);
        Assert.Contains("npm install -g intent-system@0.32.1\nintent-cli --version", installRaw, StringComparison.Ordinal);
        Assert.Contains("dotnet tool update -g JTechJapan.IntentSystem.Cli --version 0.32.1", install, StringComparison.Ordinal);
        Assert.Contains("npm install -g intent-system@0.32.0", install, StringComparison.Ordinal);
        Assert.Contains("dotnet tool update -g JTechJapan.IntentSystem.Cli --allow-downgrade --version 0.32.0", install, StringComparison.Ordinal);
        Assert.Contains(language == "en"
            ? "stable `0.32.1` follows the prerelease `0.32.1-preview.1`"
            : "SemVer では同じ `0.32.1` の stable は `0.32.1-preview.1` より後の版です", install, StringComparison.Ordinal);
        Assert.Contains(".NET 10", install, StringComparison.Ordinal);
        Assert.Contains("macOS-arm64", install, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Linux-x64", install, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Windows-x64", install, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(".sha256", install, StringComparison.Ordinal);
        Assert.Contains("PATH", install, StringComparison.Ordinal);
        Assert.Contains("intent-cli --version", install, StringComparison.Ordinal);
        Assert.Contains(language == "en" ? "Until the exact" : "実際に公開されるまでは", install, StringComparison.Ordinal);

        Assert.Contains($"]({StableReleaseUrl})", ledger, StringComparison.Ordinal);
        Assert.Contains("0.32.1-preview.1", ledger, StringComparison.Ordinal);
        Assert.Contains("0.32.2", ledger, StringComparison.Ordinal);
        Assert.Contains(language == "en" ? "prepared/not published" : "準備済み・未公開", ledger, StringComparison.OrdinalIgnoreCase);

        Assert.Single(Regex.Matches(reference, $"^{Regex.Escape(stableHeading)}$", RegexOptions.Multiline).Cast<Match>());
        Assert.Contains(language == "en"
            ? "Previous v0.32.0 release-prep evidence (retained in history only)"
            : "以前の v0.32.0 release-prep evidence（履歴のみ）", reference, StringComparison.Ordinal);
        Assert.Contains(language == "en"
            ? "<a id=\"next-release-readiness-v0321\"></a>\n### Previous v0.32.0 release-prep evidence"
            : "<a id=\"next-release-readiness-v0321\"></a>\n<a id=\"次リリース準備v0321\"></a>\n### 以前の v0.32.0 release-prep evidence", reference, StringComparison.Ordinal);
        Assert.DoesNotContain(language == "en"
            ? "<a id=\"next-release-readiness-v0321\"></a>\n### Stable release readiness"
            : "<a id=\"next-release-readiness-v0321\"></a>\n<a id=\"次リリース準備v0321\"></a>\n### 安定版リリース準備", reference, StringComparison.Ordinal);
        Assert.Contains("v0.32.2", reference, StringComparison.Ordinal);
        Assert.Contains($"]({StableReleaseUrl})", reference, StringComparison.Ordinal);
        Assert.Contains("0.32.1-preview.1", reference, StringComparison.Ordinal);
        Assert.Contains("stableVersion", reference, StringComparison.Ordinal);
        Assert.Contains("nextVersion", reference, StringComparison.Ordinal);
        Assert.Contains(language == "en"
            ? "The prepared stable line is `v0.32.1`, while the public stable release remains\n`v0.32.0` until that separate publication."
            : "準備中の安定版は `v0.32.1` ですが、別途公開されるまでは\n公開済みの安定版は `v0.32.0` です。", reference, StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(root, "docs", language, $"release-notes-v{NextVersion}.md")));
    }

    [Theory]
    [InlineData("en", "da7c178c65cd2c05bc30b93a6c07ee6d496bb31f242a84f553a7b11f8a890a3f")]
    [InlineData("ja", "1b66ac18a3ecfe679302f407d34433178e510cc4886eb855ab09feff7c1e9f4b")]
    public void PublishedPreviewNotesRemainByteIdentical(string language, string expectedSha256)
    {
        var path = Path.Combine(RepoVersionPolicySource.RepoRoot(), "docs", language, "release-notes-v0.32.1-preview.1.md");
        var bytes = File.ReadAllBytes(path);
        var actual = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        Assert.Equal(expectedSha256, actual);
    }

    private static void AssertStableNotesContract(string notes, string language)
    {
        var title = language == "en" ? "# Release notes — intent-cli 0.32.1" : "# リリースノート — intent-cli 0.32.1";
        Assert.Contains(title, notes, StringComparison.Ordinal);
        Assert.Contains(language == "en" ? "PREPARED — NOT PUBLISHED" : "準備済み — 未公開", notes, StringComparison.Ordinal);
        Assert.Contains(StableMarkdownLink(language), notes, StringComparison.Ordinal);
        Assert.Contains(language == "en" ? "public stable release remains `v0.32.0`" : "公開中の安定版は `v0.32.0`", notes, StringComparison.Ordinal);
        Assert.Contains(PreviewReleaseUrl, notes, StringComparison.Ordinal);
        Assert.Contains(language == "en" ? "this preparation PR" : "この準備 PR", notes, StringComparison.Ordinal);
        Assert.Contains(language == "en" ? "Stable publication will move npm `latest` to `0.32.1`" : "安定版を実際に公開すると、npm の `latest` は `0.32.1` に移ります", notes, StringComparison.Ordinal);
        Assert.Contains(language == "en" ? "preview` tag remain separate" : "`preview` tag は別のまま", notes, StringComparison.Ordinal);

        var requiredReceiptCondition = language == "en"
            ? "when knowledge write-back is declared required"
            : "ナレッジの書き戻しが必須と宣言された場合";
        Assert.Contains(requiredReceiptCondition, notes, StringComparison.Ordinal);
        Assert.Contains(language == "en"
            ? "Use the normal exact-head review and eligible closeout path with applicable receipts by default"
            : "通常は 対象 commit に対する review を投稿し、該当する receipt をそろえて、条件を満たす closeout 経路を使ってください", notes, StringComparison.Ordinal);
        Assert.Contains(language == "en" ? "posted for the exact head" : "対象 commit に対して投稿された独立 review", notes, StringComparison.Ordinal);
        Assert.Contains(language == "en" ? "one attributed architect and one attributed orchestrator knowledge-writeback receipt" : "architect と orchestrator それぞれに帰属する knowledge-writeback receipt", notes, StringComparison.Ordinal);
        Assert.Contains(language == "en" ? "architect guide-reachability receipt" : "architect に帰属する guide reachability receipt", notes, StringComparison.Ordinal);
        Assert.Contains("--override-loop-evidence", notes, StringComparison.Ordinal);
        Assert.Contains("--override-id <uuid>", notes, StringComparison.Ordinal);
        Assert.Contains(language == "en" ? "A reason alone does not waive duties" : "理由だけでは責務を免除しません", notes, StringComparison.Ordinal);
        Assert.Contains("--require-verification", notes, StringComparison.Ordinal);
        Assert.Contains(language == "en" ? "not inferred from the checkout or a default" : "実行中の checkout や既定値から identity や evidence を推測しません", notes, StringComparison.Ordinal);
        Assert.Contains("declared-not-verified", notes, StringComparison.Ordinal);
        Assert.Contains(language == "en" ? "caller-supplied provenance is not authenticated" : "呼び出し元から渡された来歴情報は認証されません", notes, StringComparison.Ordinal);
        Assert.Contains(language == "en" ? "Raw TRX evidence can include test names, output, and machine paths" : "raw TRX evidence には test 名、output、machine path が含まれることがあります", notes, StringComparison.Ordinal);
        Assert.Contains(language == "en" ? "partial or foreign residual" : "不完全な出力や別プロセスが置いたファイル", notes, StringComparison.Ordinal);
        Assert.Contains(language == "en" ? "#1868" : "#1868", notes, StringComparison.Ordinal);
    }

    private static string MutateOnce(string source, string oldValue, string newValue)
    {
        Assert.NotEqual(oldValue, newValue);
        Assert.Single(Regex.Matches(source, Regex.Escape(oldValue), RegexOptions.CultureInvariant).Cast<Match>());
        return source.Replace(oldValue, newValue, StringComparison.Ordinal);
    }

    private static void AssertVersionPolicy(string json)
    {
        using var document = JsonDocument.Parse(json);
        Assert.Equal(StableVersion, document.RootElement.GetProperty("stableVersion").GetString());
        Assert.Equal(NextVersion, document.RootElement.GetProperty("nextVersion").GetString());
    }

    private static void AssertNextPlaceholder(string text, string language)
    {
        Assert.Contains(language == "en" ? "# Release notes — intent-cli v0.32.2" : "# リリースノート — intent-cli v0.32.2", text, StringComparison.Ordinal);
        Assert.Contains(language == "en" ? "DRAFT / UNRELEASED" : "DRAFT / 未リリース", text, StringComparison.Ordinal);
        Assert.Contains(language == "en" ? "replaceable" : "差し替え可能", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(language == "en" ? "not a changelog" : "changelog ではありません", text, StringComparison.OrdinalIgnoreCase);
    }

    private static string StableMarkdownLink(string language) => language == "en"
        ? $"[GitHub release page for `v0.32.1`]({StableReleaseUrl})"
        : $"[`v0.32.1` GitHub release page]({StableReleaseUrl})";

    private static string Read(string language, string fileName) => File.ReadAllText(Path.Combine(
        RepoVersionPolicySource.RepoRoot(), "docs", language, fileName));

    private static string Normalize(string value) => Regex.Replace(value, @"\s+", " ").Trim();
}
