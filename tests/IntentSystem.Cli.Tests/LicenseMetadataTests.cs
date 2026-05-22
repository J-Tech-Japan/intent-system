namespace IntentSystem.Cli.Tests;

/// <summary>
/// G387: regression guards for Apache-2.0 OSS license readiness. Like the
/// other build-metadata tests, these read the repository sources directly so
/// the license contract cannot silently regress: the repo-root LICENSE must
/// carry the Apache-2.0 text, a NOTICE must accompany it, and the packed
/// <c>intent-cli</c> project must declare the SPDX <c>Apache-2.0</c>
/// expression so the published NuGet package reports the license.
/// </summary>
public sealed class LicenseMetadataTests
{
    [Fact]
    public void RepositoryRoot_ContainsApache2LicenseText()
    {
        var license = File.ReadAllText(LocateRepoFile("LICENSE"));

        Assert.Contains("Apache License", license, StringComparison.Ordinal);
        Assert.Contains("Version 2.0", license, StringComparison.Ordinal);
        Assert.Contains("http://www.apache.org/licenses/LICENSE-2.0", license, StringComparison.Ordinal);
    }

    [Fact]
    public void RepositoryRoot_ContainsNoticeWithApacheAttribution()
    {
        var notice = File.ReadAllText(LocateRepoFile("NOTICE"));

        Assert.Contains("Apache License", notice, StringComparison.Ordinal);
        Assert.Contains("J-Tech-Japan", notice, StringComparison.Ordinal);
    }

    [Fact]
    public void CliCsproj_DeclaresApache2SpdxLicenseExpression()
    {
        var csproj = File.ReadAllText(
            LocateRepoFile(Path.Combine("src", "IntentSystem.Cli", "IntentSystem.Cli.csproj")));

        Assert.Contains(
            "<PackageLicenseExpression>Apache-2.0</PackageLicenseExpression>",
            csproj,
            StringComparison.Ordinal);
    }

    private static string LocateRepoFile(string relativePath)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, relativePath);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            dir = dir.Parent;
        }

        throw new FileNotFoundException($"Could not locate repository file '{relativePath}'");
    }
}
