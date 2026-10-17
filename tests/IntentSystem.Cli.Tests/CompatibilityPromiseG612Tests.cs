using System.Collections;
using System.Reflection;
using IntentSystem.Cli.Commands;

namespace IntentSystem.Cli.Tests;

/// <summary>
/// G612: 1.0 compatibility documentation is an executable inventory. The
/// guard reads the existing dispatcher by reflection, so it adds no runtime
/// hook or behaviour to the shipped CLI.
/// </summary>
public sealed class CompatibilityPromiseG612Tests
{
    [Theory]
    [InlineData("en")]
    [InlineData("ja")]
    public void Promise_CoversAndExcludesTheAgreedMachineSurfaces_G612(string language)
    {
        var promise = Read(language, "1.0-compatibility-promise.md");

        foreach (var covered in new[]
        {
            "command", "flag", "JSON", "cause", "exit", "durable", "state transition"
        })
        {
            Assert.Contains(covered, promise, StringComparison.OrdinalIgnoreCase);
        }

        foreach (var excluded in new[] { "prose", "layout", "unstructured diagnostic" })
        {
            Assert.Contains(excluded, promise, StringComparison.OrdinalIgnoreCase);
        }

        Assert.Contains("documented replacement", promise, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("structured warning", promise, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("alias", promise, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("MAJOR", promise, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("en")]
    [InlineData("ja")]
    public void Promise_RecordsTheV012FreezeAndPreviewReaderTest_G628(string language)
    {
        var promise = Read(language, "1.0-compatibility-promise.md");

        Assert.Contains("v0.12.0", promise, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("2026-08-05", promise, StringComparison.Ordinal);
        Assert.Contains("tomohisa", promise, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("preview", promise, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("preview-through-1.x", promise, StringComparison.Ordinal);
        var outsideAndWithdrawn = language == "en"
            ? new[] { "outside", "withdraw" }
            : new[] { "対象外", "撤回" };
        foreach (var term in outsideAndWithdrawn)
        {
            Assert.Contains(term, promise, StringComparison.OrdinalIgnoreCase);
        }
        Assert.Contains("2.0", promise, StringComparison.Ordinal);
        Assert.Contains("stable-at-1.0", promise, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("en")]
    [InlineData("ja")]
    public void Ledger_ContainsEveryRegisteredCommandAndRequiredLegacyDispositions_G612(string language)
    {
        var ledger = Read(language, "1.0-compatibility-ledger.md");

        foreach (var command in RegisteredCommands().Concat(TopLevelAliases))
        {
            Assert.Contains($"`{command}`", ledger, StringComparison.Ordinal);
        }

        foreach (var disposition in new[]
        {
            "stable-at-1.0", "deprecate-with-alias", "retire-before-1.0", "retain-through-1.x"
        })
        {
            Assert.Contains(disposition, ledger, StringComparison.Ordinal);
        }

        Assert.Contains("operator-attention", ledger, StringComparison.Ordinal);
        Assert.Contains("role-pane-mapping", ledger, StringComparison.Ordinal);
        Assert.Contains("runtime-state", ledger, StringComparison.Ordinal);
        Assert.Contains("packet-schema", ledger, StringComparison.Ordinal);
        Assert.Contains("field alias", ledger, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("en")]
    [InlineData("ja")]
    public void Ledger_EnumeratesThePreviewDispositionAlongsideTheCompletenessGuard_G628(string language)
    {
        var ledger = Read(language, "1.0-compatibility-ledger.md");

        var previewRows = ledger.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Where(line => line.StartsWith("| post-freeze preview surface |", StringComparison.Ordinal))
            .ToArray();
        Assert.Single(previewRows);
        Assert.Contains("`preview-through-1.x`", previewRows[0], StringComparison.Ordinal);
        Assert.Contains("preview", previewRows[0], StringComparison.OrdinalIgnoreCase);

        // Keep the existing registered-command completeness guard exercised in
        // the same fixture: preview rows are additive and cannot replace it.
        foreach (var command in RegisteredCommands().Concat(TopLevelAliases))
        {
            Assert.Contains($"`{command}`", ledger, StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData("en")]
    [InlineData("ja")]
    public void Ledger_RecordsTheG636PostStartInteractionPreviewSurface(string language)
    {
        var ledger = Read(language, "1.0-compatibility-ledger.md");

        var rows = ledger.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Where(line => line.StartsWith("| unattended-launch post-start interaction |", StringComparison.Ordinal))
            .ToArray();

        Assert.Single(rows);
        Assert.Contains("`preview-through-1.x`", rows[0], StringComparison.Ordinal);
        Assert.Contains("`default_is_safe`", rows[0], StringComparison.Ordinal);
        Assert.Contains("G636", rows[0], StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("en")]
    [InlineData("ja")]
    public void Ledger_RecordsG870G871ManifestOrderAndLessonCompatibility(string language)
    {
        var ledger = Read(language, "1.0-compatibility-ledger.md");
        var commandReference = Read(language, "08-command-reference.md");
        var row = Assert.Single(ledger.Split('\n', StringSplitOptions.RemoveEmptyEntries),
            line => line.StartsWith("| deterministic role brief |", StringComparison.Ordinal));

        if (language == "en")
        {
            Assert.Contains("Packet manifest rows keep fixed capture order; only ruling rows are sorted by path.", row, StringComparison.Ordinal);
            Assert.Contains("Bounded sibling-name checks enforce exact allowed input names", row, StringComparison.Ordinal);
            Assert.Contains("reviewer output does not open or stat `implementation.md`", row, StringComparison.Ordinal);
            Assert.Contains("--lessons-registry", row, StringComparison.Ordinal);
            Assert.Contains("--lessons-sha256", row, StringComparison.Ordinal);
            Assert.Contains("repeated `--lesson`", row, StringComparison.Ordinal);
            Assert.Contains("prior output bytes, v1 digest, and read behavior remain unchanged", row, StringComparison.Ordinal);
            Assert.Contains("not a registry writer or enforcement engine", row, StringComparison.Ordinal);
            Assert.DoesNotContain("lessons are unsupported", row, StringComparison.Ordinal);
        }
        else
        {
            Assert.Contains("packet row は固定 capture order、ruling row だけを path 順", row, StringComparison.Ordinal);
            Assert.Contains("bounded sibling-name check", row, StringComparison.Ordinal);
            Assert.Contains("reviewer は `implementation.md` を open/stat しない", row, StringComparison.Ordinal);
            Assert.Contains("--lessons-registry", row, StringComparison.Ordinal);
            Assert.Contains("--lessons-sha256", row, StringComparison.Ordinal);
            Assert.Contains("複数 `--lesson`", row, StringComparison.Ordinal);
            Assert.Contains("従来の output bytes、v1 digest、read behavior を保ち", row, StringComparison.Ordinal);
            Assert.Contains("registry writer や enforcement engine ではない", row, StringComparison.Ordinal);
            Assert.DoesNotContain("lessons は未対応", row, StringComparison.Ordinal);
        }

        var normalizedReference = string.Join(' ', commandReference.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        var normalizedLedger = string.Join(' ', ledger.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        foreach (var document in new[] { normalizedReference, normalizedLedger })
        {
            if (language == "en")
            {
                Assert.Contains("`brief-output-cleanup-incomplete`", document, StringComparison.Ordinal);
                Assert.Contains("cleanup was not attempted", document, StringComparison.Ordinal);
                Assert.Contains("a partial file or foreign replacement may remain", document, StringComparison.Ordinal);
                Assert.Contains("makes no claim that it deleted the file or that publication succeeded", document, StringComparison.Ordinal);
                Assert.Contains("retry to the same output path is refused as existing", document, StringComparison.OrdinalIgnoreCase);
                Assert.Contains("inspect any residual separately", document, StringComparison.Ordinal);
            }
            else
            {
                Assert.Contains("`brief-output-cleanup-incomplete`", document, StringComparison.Ordinal);
                Assert.Contains("cleanup は試みません", document, StringComparison.Ordinal);
                Assert.Contains("部分 file または外部 process に置き換えられた file が残る場合があります", document, StringComparison.Ordinal);
                Assert.Contains("file の削除や publication 成功を主張しません", document, StringComparison.Ordinal);
                Assert.Contains("同じ出力 path", document, StringComparison.Ordinal);
                Assert.Contains("残った file は別途確認してください", document, StringComparison.Ordinal);
            }
        }

        if (language == "en")
        {
            Assert.Contains("`.intent-cli/lessons/<registry-id>.json`", normalizedReference, StringComparison.Ordinal);
            Assert.Contains("`active_from <= --as-of`", normalizedReference, StringComparison.Ordinal);
            Assert.Contains("superseded row is refused", normalizedReference, StringComparison.Ordinal);
            Assert.Contains("1 MiB", normalizedReference, StringComparison.Ordinal);
            Assert.Contains("`lessons` has 0–128 rows", normalizedReference, StringComparison.Ordinal);
            Assert.Contains("1–16 distinct repeated `--lesson` IDs", normalizedReference, StringComparison.Ordinal);
            Assert.Contains("--lesson L1", normalizedReference, StringComparison.Ordinal);
            Assert.Contains("is the JSON integer `1`", normalizedReference, StringComparison.Ordinal);
            Assert.Contains("[A-Za-z][A-Za-z0-9._-]{0,63}", normalizedReference, StringComparison.Ordinal);
            Assert.Contains("unknown and duplicate properties", normalizedReference, StringComparison.Ordinal);
            Assert.Contains("source` and `rule` are non-whitespace strings of 1–4,096 UTF-16 units", normalizedReference, StringComparison.Ordinal);
            Assert.Contains("one initial UTF-8 BOM", normalizedReference, StringComparison.Ordinal);
            Assert.Contains("\"schema_version\": 1", normalizedReference, StringComparison.Ordinal);
            Assert.Contains("Without that group, prior output bytes, v1 digest, and read behavior remain unchanged", normalizedLedger, StringComparison.Ordinal);
        }
        else
        {
            Assert.Contains("`.intent-cli/lessons/<registry-id>.json`", normalizedReference, StringComparison.Ordinal);
            Assert.Contains("`active_from <= --as-of`", normalizedReference, StringComparison.Ordinal);
            Assert.Contains("有効な active successor があっても拒否し", normalizedReference, StringComparison.Ordinal);
            Assert.Contains("1 MiB", normalizedReference, StringComparison.Ordinal);
            Assert.Contains("lessons` は0–128件", normalizedReference, StringComparison.Ordinal);
            Assert.Contains("selected ID は最大16個", normalizedReference, StringComparison.Ordinal);
            Assert.Contains("--lesson L1", normalizedReference, StringComparison.Ordinal);
            Assert.Contains("JSON integer `1`", normalizedReference, StringComparison.Ordinal);
            Assert.Contains("[A-Za-z][A-Za-z0-9._-]{0,63}", normalizedReference, StringComparison.Ordinal);
            Assert.Contains("object の未知・重複 key は拒否", normalizedReference, StringComparison.Ordinal);
            Assert.Contains("source` / `rule` は空白だけでない1–4,096 UTF-16 code unit", normalizedReference, StringComparison.Ordinal);
            Assert.Contains("先頭の UTF-8 BOM は1つ許可", normalizedReference, StringComparison.Ordinal);
            Assert.Contains("\"schema_version\": 1", normalizedReference, StringComparison.Ordinal);
            Assert.Contains("group を省略すると従来の output bytes、v1 digest、read behavior を保ち", normalizedLedger, StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData("en", "all machine-emitted `cause` values", "Every `cause` value actually emitted")]
    [InlineData("ja", "machine-consumed JSON payload で実際に emit されるすべての `cause` value", "実際に emit されるすべての `cause` value")]
    public void PromiseAndLedger_CoverEveryMachineEmittedCauseValue_G612(
        string language,
        string promiseCoverage,
        string ledgerCoverage)
    {
        var promise = Read(language, "1.0-compatibility-promise.md");
        var ledger = Read(language, "1.0-compatibility-ledger.md");

        Assert.Contains(promiseCoverage, promise, StringComparison.Ordinal);
        Assert.Contains(ledgerCoverage, ledger, StringComparison.Ordinal);
        Assert.DoesNotContain("documented `cause`", promise, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("documented `cause`", ledger, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("topology-location-conflict", ledger, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("en")]
    [InlineData("ja")]
    public void Promise_RecordsEvidenceGatedRoadAndLinksTheAdr_G612(string language)
    {
        var promise = Read(language, "1.0-compatibility-promise.md");

        foreach (var step in new[] { "G611", "G612", "operator-attention", "herdr-only", "1.0 release" })
        {
            Assert.Contains(step, promise, StringComparison.OrdinalIgnoreCase);
        }

        var criteria = language == "en"
            ? new[]
            {
                "operator's", "2026-08-05", "zero unresolved transport-caused incidents",
                "fresh provisioning", "headless resume", "EOF", "topology", "routing recovery"
            }
            : new[]
            {
                "operator decision", "2026-08-05", "zero unresolved", "fresh provisioning",
                "headless resume", "EOF", "topology", "routing"
            };
        foreach (var criterion in criteria)
        {
            Assert.Contains(criterion, promise, StringComparison.OrdinalIgnoreCase);
        }

        Assert.Contains("0002-one-dot-zero-compatibility-promise.md", promise, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("en")]
    [InlineData("ja")]
    public void Ledger_RecordsG623JudgmentWaitAndItsOneXAlias_G623(string language)
    {
        var ledger = Read(language, "1.0-compatibility-ledger.md");
        var promise = Read(language, "1.0-compatibility-promise.md");

        foreach (var subcommand in new[] { "open", "query", "resolve", "supersede" })
        {
            Assert.Contains($"`judgment-wait {subcommand}`", ledger, StringComparison.Ordinal);
            Assert.Contains($"`operator-attention {subcommand}`", ledger, StringComparison.Ordinal);
        }

        Assert.Contains("judgment-wait record", ledger, StringComparison.Ordinal);
        Assert.Contains("stable-at-1.0", ledger, StringComparison.Ordinal);
        Assert.Contains("deprecation_warning", ledger, StringComparison.Ordinal);
        Assert.Contains("next MAJOR", ledger, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("G623", promise, StringComparison.Ordinal);
        Assert.Contains("judgment-wait", promise, StringComparison.Ordinal);
    }

    [Fact]
    public void Adr_RecordsTheSameDecision_G612()
    {
        var adr = File.ReadAllText(Path.Combine(RepoVersionPolicySource.RepoRoot(), "docs", "adr", "0002-one-dot-zero-compatibility-promise.md"));
        Assert.Contains("machine-surface compatibility promise", adr, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("structured warning", adr, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("next major", adr, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("operator-attention", adr, StringComparison.Ordinal);
        Assert.Contains("every machine-emitted cause value", adr, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("topology-location-conflict", adr, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("en")]
    [InlineData("ja")]
    public void DocumentationIndex_LinksThePromise_G612(string language)
    {
        Assert.Contains("1.0-compatibility-promise.md", Read(language, "README.md"), StringComparison.Ordinal);
    }

    private static IEnumerable<string> RegisteredCommands()
    {
        var registryField = typeof(CommandRouter).GetField("ImplementedCommands", BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(registryField);
        var registry = registryField!.GetValue(null) as IDictionary;
        Assert.NotNull(registry);

        foreach (DictionaryEntry group in registry!)
        {
            var subcommands = group.Value as IDictionary;
            Assert.NotNull(subcommands);
            foreach (DictionaryEntry subcommand in subcommands!)
            {
                yield return $"{group.Key} {subcommand.Key}";
            }
        }
    }

    private static readonly string[] TopLevelAliases = ["improve", "grill", "stack", "next", "inspect"];

    private static string Read(string language, string path) =>
        File.ReadAllText(Path.Combine(RepoVersionPolicySource.RepoRoot(), "docs", language, path));
}
