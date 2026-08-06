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
                "four teams", "20 active days", "30-day window", "zero unresolved transport-caused incidents",
                "fresh provisioning", "headless resume", "EOF", "topology", "routing recovery"
            }
            : new[]
            {
                "4 team", "20 active days", "30-day window", "zero unresolved transport-caused incidents",
                "fresh provisioning", "headless resume", "EOF", "topology", "routing recovery"
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
