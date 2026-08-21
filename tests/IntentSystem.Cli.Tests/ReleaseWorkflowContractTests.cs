namespace IntentSystem.Cli.Tests;

/// <summary>
/// G386: regression guards for the release-driven distribution workflow
/// (<c>.github/workflows/release.yml</c>). The deliverable is a GitHub
/// Actions workflow, so — like <see cref="IntentCliBuildMetadataTests"/>
/// reads the CLI <c>.csproj</c> directly — these read the workflow source
/// and lock the contract that cannot be exercised by a normal unit test:
/// release-triggered, NuGet publish guarded against forks/missing secrets,
/// self-contained binaries for the three target RIDs, and NO build-time
/// expiry properties on release artifacts.
/// </summary>
public sealed class ReleaseWorkflowContractTests
{
    [Fact]
    public void ReleaseWorkflow_TriggersOnPublishedRelease()
    {
        var workflow = File.ReadAllText(LocateReleaseWorkflow());

        Assert.Contains("on:", workflow, StringComparison.Ordinal);
        Assert.Contains("release:", workflow, StringComparison.Ordinal);
        Assert.Contains("types: [published]", workflow, StringComparison.Ordinal);
    }

    [Fact]
    public void ReleaseWorkflow_GatesExactCommitAgainstRepositoryDefaultBranch()
    {
        var workflow = File.ReadAllText(LocateReleaseWorkflow());

        Assert.Contains("release-reachability:", workflow, StringComparison.Ordinal);
        Assert.Contains("github.event.repository.default_branch", workflow, StringComparison.Ordinal);
        Assert.Contains("git rev-list -n 1 \"refs/tags/${RELEASE_TAG}\"", workflow, StringComparison.Ordinal);
        Assert.Contains("./eng/release-reachability.sh", workflow, StringComparison.Ordinal);
        Assert.Contains("--commit", workflow, StringComparison.Ordinal);
        Assert.Contains("--survey", workflow, StringComparison.Ordinal);
        Assert.Contains("needs: [release-reachability]", workflow, StringComparison.Ordinal);
        Assert.Contains("fetch-depth: 0", workflow, StringComparison.Ordinal);

        // Reachability is an ancestry fact. It must not be inferred from a
        // branch name or from whether a pull request happens to be open.
        Assert.DoesNotContain("github.head_ref", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("pull_request", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("git branch --contains", workflow, StringComparison.Ordinal);
    }

    [Fact]
    public void ReleaseWorkflow_PacksAndPublishesNuGet_GuardedAgainstForksAndMissingSecret()
    {
        var workflow = File.ReadAllText(LocateReleaseWorkflow());

        // Packs the official tool package.
        Assert.Contains("dotnet pack src/IntentSystem.Cli/IntentSystem.Cli.csproj", workflow, StringComparison.Ordinal);
        // Pushes to NuGet.org.
        Assert.Contains("dotnet nuget push", workflow, StringComparison.Ordinal);
        Assert.Contains("https://api.nuget.org/v3/index.json", workflow, StringComparison.Ordinal);
        // The publish must be guarded: only on a real release, and skip when
        // the secret is absent (forks / missing NUGET_API_KEY).
        Assert.Contains("NUGET_API_KEY", workflow, StringComparison.Ordinal);
        Assert.Contains("github.event_name == 'release'", workflow, StringComparison.Ordinal);
        Assert.Contains("skipping NuGet publish", workflow, StringComparison.Ordinal);
    }

    [Fact]
    public void ReleaseWorkflow_BuildsSelfContainedBinariesForAllThreeRids()
    {
        var workflow = File.ReadAllText(LocateReleaseWorkflow());

        Assert.Contains("--self-contained true", workflow, StringComparison.Ordinal);
        Assert.Contains("-p:PublishSingleFile=true", workflow, StringComparison.Ordinal);
        Assert.Contains("osx-arm64", workflow, StringComparison.Ordinal);
        Assert.Contains("win-x64", workflow, StringComparison.Ordinal);
        Assert.Contains("linux-x64", workflow, StringComparison.Ordinal);
        // A --version smoke test proves the binary runs.
        Assert.Contains("--version", workflow, StringComparison.Ordinal);
    }

    [Fact]
    public void ReleaseWorkflow_AttachesAssetsAndChecksumsToRelease()
    {
        var workflow = File.ReadAllText(LocateReleaseWorkflow());

        Assert.Contains("gh release upload", workflow, StringComparison.Ordinal);
        Assert.Contains(".sha256", workflow, StringComparison.Ordinal);
    }

    [Fact]
    public void ReleaseWorkflow_DoesNotEmbedPreviewExpiryProperties()
    {
        var workflow = File.ReadAllText(LocateReleaseWorkflow());

        // Release artifacts must carry no build-time expiry contract — the
        // PrivatePreview* properties (consumed by the csproj G367 block and
        // the PrivatePreviewExpiryGate) must never be passed here.
        Assert.DoesNotContain("PrivatePreviewChannel", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("PrivatePreviewExpiresAt", workflow, StringComparison.Ordinal);
    }

    [Fact]
    public void ReleaseWorkflow_ChecksumSidecarsUseBasenameNotDistPath()
    {
        // G409: sidecar files must record only the archive basename so users can
        // verify from a plain download directory without recreating dist/.
        // The fix is to cd into dist before running sha256sum/shasum, which means
        // the checksum command must NOT pass the full dist/ path to the hasher.
        var workflow = File.ReadAllText(LocateReleaseWorkflow());

        // The fix pattern: cd into dist, then hash the BASENAME variable (not ASSET).
        Assert.Contains("cd dist", workflow, StringComparison.Ordinal);
        Assert.Contains("BASENAME", workflow, StringComparison.Ordinal);

        // Regression guard: the hasher must not receive the dist/-prefixed ASSET path.
        // sha256sum "${ASSET}" or shasum -a 256 "${ASSET}" would reintroduce dist/ prefix.
        Assert.DoesNotContain("sha256sum \"${ASSET}\"", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("shasum -a 256 \"${ASSET}\"", workflow, StringComparison.Ordinal);
    }

    private static string LocateReleaseWorkflow()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, ".github", "workflows", "release.yml");
            if (File.Exists(candidate))
            {
                return candidate;
            }

            dir = dir.Parent;
        }

        throw new FileNotFoundException("Could not locate .github/workflows/release.yml");
    }
}
