using IntentSystem.Cli.Infrastructure;

namespace IntentSystem.Cli.Tests;

/// <summary>
/// G469: release-readiness guards for the next NuGet release after G468.
/// These pin the package metadata that the public install/update docs depend
/// on (package id, command name, license, project URL, README) and assert the
/// version policy records a coherent release-to-be-cut, so an operator can cut
/// the release with confidence and `intent-cli --version` stays trustworthy.
/// </summary>
public sealed class ReleasePackageMetadataTests
{
    [Fact]
    public void Csproj_PinsPublicPackageMetadata()
    {
        var csproj = File.ReadAllText(CsprojPath());

        // Out of scope to change these — guard them so a release never ships
        // with a drifted id / command / license / URL that breaks install docs.
        Assert.Contains("<PackageId>JTechJapan.IntentSystem.Cli</PackageId>", csproj, StringComparison.Ordinal);
        Assert.Contains("<ToolCommandName>intent-cli</ToolCommandName>", csproj, StringComparison.Ordinal);
        Assert.Contains("<PackAsTool>true</PackAsTool>", csproj, StringComparison.Ordinal);
        Assert.Contains("<PackageLicenseExpression>Apache-2.0</PackageLicenseExpression>", csproj, StringComparison.Ordinal);
        Assert.Contains("<PackageReadmeFile>README.md</PackageReadmeFile>", csproj, StringComparison.Ordinal);
        Assert.Contains("https://github.com/J-Tech-Japan/intent-system", csproj, StringComparison.Ordinal);
    }

    [Fact]
    public void Csproj_DescriptionAndTags_NameOrchestrationModel_PackageIdAndLicenseUnchanged_G541()
    {
        var csproj = File.ReadAllText(CsprojPath());

        // G541: the NuGet Description must name the primary four-thread
        // orchestration model so a NuGet visitor learns the primary workflow,
        // and PackageTags must gain orchestration-relevant tags — while
        // package id, tool command, and license stay exactly as they were
        // (diff-verified: same assertions as Csproj_PinsPublicPackageMetadata).
        var descriptionMatch = System.Text.RegularExpressions.Regex.Match(csproj, "<Description>(.*?)</Description>", System.Text.RegularExpressions.RegexOptions.Singleline);
        Assert.True(descriptionMatch.Success, "csproj must declare a <Description>");
        var description = descriptionMatch.Groups[1].Value;
        Assert.Contains("orchestration", description, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("four-thread", description, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("timer-loop", description, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("alternative", description, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("preview", description, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("experimental", description, StringComparison.OrdinalIgnoreCase);

        var tagsMatch = System.Text.RegularExpressions.Regex.Match(csproj, "<PackageTags>(.*?)</PackageTags>");
        Assert.True(tagsMatch.Success, "csproj must declare <PackageTags>");
        var tags = tagsMatch.Groups[1].Value.Split(';', StringSplitOptions.RemoveEmptyEntries);
        Assert.Contains(tags, t => t.Equals("agmsg", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(tags, t => t.Equals("orchestration", StringComparison.OrdinalIgnoreCase));

        // Package id, command, and license unchanged.
        Assert.Contains("<PackageId>JTechJapan.IntentSystem.Cli</PackageId>", csproj, StringComparison.Ordinal);
        Assert.Contains("<ToolCommandName>intent-cli</ToolCommandName>", csproj, StringComparison.Ordinal);
        Assert.Contains("<PackageLicenseExpression>Apache-2.0</PackageLicenseExpression>", csproj, StringComparison.Ordinal);
    }

    [Fact]
    public void VersionPolicy_RecordsReleaseToBeCut_AheadOfPublishedStable()
    {
        var policy = VersionPolicy.TryReadFromRepo(FindRepoRoot());
        Assert.NotNull(policy);

        // The release-to-be-cut is nextVersion; it must be strictly ahead of the
        // currently published stableVersion so the tagged release does not
        // collide with the latest stable line.
        var stable = ParseVersion(policy!.StableVersion);
        var next = ParseVersion(policy.NextVersion);
        Assert.True(
            Compare(next, stable) > 0,
            $"eng/version.json nextVersion ({policy.NextVersion}) must be ahead of stableVersion ({policy.StableVersion}) to be release-ready.");
    }

    [Fact]
    public void DeveloperReference_DocumentsReleaseReadinessForNextVersion()
    {
        var policy = VersionPolicy.TryReadFromRepo(FindRepoRoot());
        Assert.NotNull(policy);

        var devRef = File.ReadAllText(Path.Combine(FindRepoRoot(), "docs", "en", "09-developer-reference.md"));

        // The release-readiness section names the release-to-be-cut and points at
        // this metadata test, so docs and the guard stay in lock-step.
        Assert.Contains("Next release readiness", devRef, StringComparison.Ordinal);
        Assert.Contains(policy!.NextVersion, devRef, StringComparison.Ordinal);
        Assert.Contains("ReleasePackageMetadataTests", devRef, StringComparison.Ordinal);
        // It must surface the version-identity verification (the G468 fix).
        Assert.Contains("intent-cli", devRef, StringComparison.Ordinal);
        Assert.Contains("--version", devRef, StringComparison.Ordinal);
    }

    [Fact]
    public void ReleaseNotes_ForNextVersion_ExistInEnAndJa_WithInstallVersionAndGate()
    {
        // G475: every release-to-be-cut must ship paste-ready release notes
        // (en + ja) so the operator has a release body and a readiness gate.
        // Derived from nextVersion so this guard stays correct across bumps.
        var policy = VersionPolicy.TryReadFromRepo(FindRepoRoot());
        Assert.NotNull(policy);
        var version = policy!.NextVersion;
        var fileName = $"release-notes-v{version}.md";

        foreach (var lang in new[] { "en", "ja" })
        {
            var path = Path.Combine(FindRepoRoot(), "docs", lang, fileName);
            Assert.True(File.Exists(path), $"missing release notes: docs/{lang}/{fileName}");

            var notes = File.ReadAllText(path);
            // The notes must name the package + the exact release version so an
            // install/upgrade copy-paste lands the intended version.
            Assert.Contains($"JTechJapan.IntentSystem.Cli --version {version}", notes, StringComparison.Ordinal);
            // The notes must point at the matching GitHub Release tag (language-neutral).
            Assert.Contains($"releases/tag/v{version}", notes, StringComparison.Ordinal);
        }
    }

    private static (int Major, int Minor, int Patch) ParseVersion(string version)
    {
        var core = version.Split('-', 2)[0];
        var parts = core.Split('.');
        Assert.True(parts.Length >= 3, $"version '{version}' is not semver-shaped");
        return (int.Parse(parts[0]), int.Parse(parts[1]), int.Parse(parts[2]));
    }

    private static int Compare((int Major, int Minor, int Patch) a, (int Major, int Minor, int Patch) b)
    {
        if (a.Major != b.Major) return a.Major.CompareTo(b.Major);
        if (a.Minor != b.Minor) return a.Minor.CompareTo(b.Minor);
        return a.Patch.CompareTo(b.Patch);
    }

    private static string CsprojPath() =>
        Path.Combine(FindRepoRoot(), "src", "IntentSystem.Cli", "IntentSystem.Cli.csproj");

    private static string FindRepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !Directory.Exists(Path.Combine(dir, "src")))
        {
            dir = Path.GetDirectoryName(dir);
        }
        Assert.NotNull(dir);
        return dir!;
    }
}
