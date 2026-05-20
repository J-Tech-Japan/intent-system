namespace IntentSystem.Cli.Tests;

/// <summary>
/// G378: build-validation regression guards for the
/// <c>intent-cli --version</c> execution-unit metadata. These read the
/// CLI <c>.csproj</c> source directly so the stale hard-coded fallback
/// (the old <c>G360</c>) cannot silently return: the no-override default
/// must be the neutral <c>unknown</c>, the latest unit must be derived
/// from git history at build time, and the explicit-override path must be
/// preserved for controlled CI/release builds.
/// </summary>
public sealed class IntentCliBuildMetadataTests
{
    [Fact]
    public void Csproj_LatestExecutionUnitDefault_IsNeutralUnknown_NotStaleHardCodedGNumber()
    {
        var csproj = File.ReadAllText(LocateCliCsproj());

        // The no-override default must be the neutral `unknown` marker.
        Assert.Matches(
            @"<IntentSystemLatestExecutionUnit Condition=""'\$\(IntentSystemLatestExecutionUnit\)' == ''"">unknown</IntentSystemLatestExecutionUnit>",
            csproj);

        // It must NEVER hard-code a completed G-number as the default —
        // that is exactly the G360 staleness bug this slice removes.
        Assert.DoesNotMatch(
            @"<IntentSystemLatestExecutionUnit Condition=""'\$\(IntentSystemLatestExecutionUnit\)' == ''"">G\d+</IntentSystemLatestExecutionUnit>",
            csproj);
    }

    [Fact]
    public void Csproj_DerivesLatestExecutionUnitFromGit_AndPreservesExplicitOverride()
    {
        var csproj = File.ReadAllText(LocateCliCsproj());

        // A best-effort git-derivation target supplies the real unit for
        // normal packs (no manual MSBuild property required).
        Assert.Contains("ResolveIntentCliLatestExecutionUnit", csproj, StringComparison.Ordinal);
        Assert.Contains("git log", csproj, StringComparison.Ordinal);

        // The explicit-override path is preserved: an operator/CI build
        // pinning -p:IntentSystemLatestExecutionUnit=Gxxx must skip the
        // derivation. The explicit flag captures that intent.
        Assert.Contains("IntentSystemLatestExecutionUnitExplicit", csproj, StringComparison.Ordinal);
        Assert.Matches(
            @"Condition=""'\$\(IntentSystemLatestExecutionUnitExplicit\)' != 'true'""",
            csproj);
    }

    private static string LocateCliCsproj()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(
                dir.FullName, "src", "IntentSystem.Cli", "IntentSystem.Cli.csproj");
            if (File.Exists(candidate))
            {
                return candidate;
            }
            dir = dir.Parent;
        }
        throw new FileNotFoundException("Could not locate IntentSystem.Cli.csproj");
    }
}
