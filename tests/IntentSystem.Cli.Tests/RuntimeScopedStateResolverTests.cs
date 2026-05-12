using IntentSystem.Cli;

namespace IntentSystem.Cli.Tests;

/// <summary>
/// G327: tests for the per-(domain, owner/repo) runtime state resolver.
/// Verifies the canonical scoped layout, owner/repo normalization
/// (slashes → __, URL prefix stripping, separator collapse), and the
/// preference order (scoped on disk → legacy root → scoped-as-default
/// for write).
/// </summary>
public sealed class RuntimeScopedStateResolverTests
{
    [Fact]
    public void GetScopedRuntimeDirectory_BuildsDomainAndOwnerRepoTree()
    {
        var path = RuntimeScopedStateResolver.GetScopedRuntimeDirectory(
            "/tmp/host",
            "intent-cli",
            "J-Tech-Japan/intent-system");

        Assert.Equal(
            Path.Combine("/tmp/host", ".intent-cli", "runtime", "intent-cli", "J-Tech-Japan__intent-system"),
            path);
    }

    [Fact]
    public void GetScopedQueueStatePath_PointsAtRuntimeQueueStateFile()
    {
        var path = RuntimeScopedStateResolver.GetScopedQueueStatePath(
            "/tmp/host",
            "sekiban-as-a-service",
            "J-Tech-Japan/SekibanAsAService");

        Assert.EndsWith(
            Path.Combine(".intent-cli", "runtime", "sekiban-as-a-service",
                "J-Tech-Japan__SekibanAsAService", "queue-state.json"),
            path);
    }

    [Fact]
    public void GetScopedRunLogPath_PointsAtRuntimeRunsJsonl()
    {
        var path = RuntimeScopedStateResolver.GetScopedRunLogPath(
            "/tmp/host",
            "intent-cli",
            "J-Tech-Japan/intent-system");

        Assert.EndsWith(Path.Combine("J-Tech-Japan__intent-system", "runs.jsonl"), path);
    }

    [Fact]
    public void GetScopedLeasesPath_PointsAtRuntimeLeasesJson()
    {
        var path = RuntimeScopedStateResolver.GetScopedLeasesPath(
            "/tmp/host",
            "intent-cli",
            "J-Tech-Japan/intent-system");

        Assert.EndsWith(Path.Combine("J-Tech-Japan__intent-system", "leases.json"), path);
    }

    [Fact]
    public void GetScopedCloseoutsDirectory_PointsAtRuntimeCloseoutsFolder()
    {
        var path = RuntimeScopedStateResolver.GetScopedCloseoutsDirectory(
            "/tmp/host",
            "intent-cli",
            "J-Tech-Japan/intent-system");

        Assert.EndsWith(Path.Combine("J-Tech-Japan__intent-system", "closeouts"), path);
    }

    [Theory]
    [InlineData("J-Tech-Japan/intent-system", "J-Tech-Japan__intent-system")]
    [InlineData("J-Tech-Japan/SekibanAsAService", "J-Tech-Japan__SekibanAsAService")]
    [InlineData("https://github.com/J-Tech-Japan/intent-system", "J-Tech-Japan__intent-system")]
    [InlineData("https://github.com/J-Tech-Japan/intent-system/", "J-Tech-Japan__intent-system")]
    [InlineData("J-Tech-Japan//intent-system", "J-Tech-Japan__intent-system")]
    [InlineData("J-Tech-Japan\\intent-system", "J-Tech-Japan__intent-system")]
    public void NormalizeOwnerRepo_CollapsesSeparatorsAndStripsGithubUrl(string raw, string expected)
    {
        Assert.Equal(expected, RuntimeScopedStateResolver.NormalizeOwnerRepo(raw));
    }

    [Fact]
    public void ResolveQueueStatePathForRead_PrefersScopedWhenOnDisk()
    {
        using var workspace = new RuntimeScopedWorkspace();
        // Seed BOTH the scoped path and the legacy root. Scoped must win.
        workspace.WriteScopedQueueState("intent-cli", "J-Tech-Japan/intent-system", "{\"scope\":\"scoped\"}");
        workspace.WriteLegacyQueueState("{\"scope\":\"legacy\"}");

        var location = RuntimeScopedStateResolver.ResolveQueueStatePathForRead(
            workspace.RepoRoot,
            "intent-cli",
            "J-Tech-Japan/intent-system");

        Assert.Equal(StateLocationKind.Scoped, location.Kind);
        Assert.EndsWith(
            Path.Combine("J-Tech-Japan__intent-system", "queue-state.json"),
            location.Path);
    }

    [Fact]
    public void ResolveQueueStatePathForRead_FallsBackToLegacyWhenScopedMissing()
    {
        using var workspace = new RuntimeScopedWorkspace();
        workspace.WriteLegacyQueueState("{\"scope\":\"legacy\"}");

        var location = RuntimeScopedStateResolver.ResolveQueueStatePathForRead(
            workspace.RepoRoot,
            "intent-cli",
            "J-Tech-Japan/intent-system");

        Assert.Equal(StateLocationKind.Legacy, location.Kind);
        Assert.EndsWith(Path.Combine(".intent-cli", "queue-state.json"), location.Path);
    }

    [Fact]
    public void ResolveQueueStatePathForRead_WhenNeitherExists_ReturnsScopedAsWriteTarget()
    {
        using var workspace = new RuntimeScopedWorkspace();

        var location = RuntimeScopedStateResolver.ResolveQueueStatePathForRead(
            workspace.RepoRoot,
            "intent-cli",
            "J-Tech-Japan/intent-system");

        Assert.Equal(StateLocationKind.MissingPreferScoped, location.Kind);
        Assert.EndsWith(
            Path.Combine("J-Tech-Japan__intent-system", "queue-state.json"),
            location.Path);
    }

    [Fact]
    public void ResolveQueueStatePathForRead_IntentSystemAndSekibanProduceDifferentScopedFiles()
    {
        // G327 acceptance: intent-system state and Sekiban state must
        // resolve to DIFFERENT scoped paths so they don't share the
        // same active runtime queue file.
        using var workspace = new RuntimeScopedWorkspace();

        var intentSystemLocation = RuntimeScopedStateResolver.ResolveQueueStatePathForRead(
            workspace.RepoRoot,
            "intent-cli",
            "J-Tech-Japan/intent-system");
        var sekibanLocation = RuntimeScopedStateResolver.ResolveQueueStatePathForRead(
            workspace.RepoRoot,
            "sekiban-as-a-service",
            "J-Tech-Japan/SekibanAsAService");

        Assert.NotEqual(intentSystemLocation.Path, sekibanLocation.Path);
        Assert.Contains("intent-cli", intentSystemLocation.Path, StringComparison.Ordinal);
        Assert.Contains("sekiban-as-a-service", sekibanLocation.Path, StringComparison.Ordinal);
    }

    [Fact]
    public void ResolveRunLogPathForRead_PrefersScopedWhenOnDisk()
    {
        using var workspace = new RuntimeScopedWorkspace();
        workspace.WriteScopedRunLog("intent-cli", "J-Tech-Japan/intent-system",
            "{\"ts\":\"2026-05-12T00:00:00Z\",\"execution_unit\":\"G327\",\"event\":\"pr-merged\",\"by\":\"test\"}\n");
        workspace.WriteLegacyRunLog("legacy-runlog-content\n");

        var location = RuntimeScopedStateResolver.ResolveRunLogPathForRead(
            workspace.RepoRoot,
            "intent-cli",
            "J-Tech-Japan/intent-system");

        Assert.Equal(StateLocationKind.Scoped, location.Kind);
    }

    [Fact]
    public void ResolveRunLogPathForRead_FallsBackToLegacyWhenScopedMissing()
    {
        using var workspace = new RuntimeScopedWorkspace();
        workspace.WriteLegacyRunLog("legacy-runlog\n");

        var location = RuntimeScopedStateResolver.ResolveRunLogPathForRead(
            workspace.RepoRoot,
            "intent-cli",
            "J-Tech-Japan/intent-system");

        Assert.Equal(StateLocationKind.Legacy, location.Kind);
    }

    [Fact]
    public void GetLegacyQueueStatePath_PointsAtRootIntentCli()
    {
        var legacy = RuntimeScopedStateResolver.GetLegacyQueueStatePath("/tmp/host");
        Assert.EndsWith(Path.Combine(".intent-cli", "queue-state.json"), legacy);
        Assert.DoesNotContain("runtime", legacy, StringComparison.Ordinal);
    }

    private sealed class RuntimeScopedWorkspace : IDisposable
    {
        public string RepoRoot { get; }

        public RuntimeScopedWorkspace()
        {
            RepoRoot = Directory.CreateTempSubdirectory("g327-runtime-scoped-").FullName;
        }

        public void WriteScopedQueueState(string domain, string ownerRepo, string content)
        {
            var path = RuntimeScopedStateResolver.GetScopedQueueStatePath(RepoRoot, domain, ownerRepo);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content);
        }

        public void WriteLegacyQueueState(string content)
        {
            var path = RuntimeScopedStateResolver.GetLegacyQueueStatePath(RepoRoot);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content);
        }

        public void WriteScopedRunLog(string domain, string ownerRepo, string content)
        {
            var path = RuntimeScopedStateResolver.GetScopedRunLogPath(RepoRoot, domain, ownerRepo);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content);
        }

        public void WriteLegacyRunLog(string content)
        {
            var path = RuntimeScopedStateResolver.GetLegacyRunLogPath(RepoRoot);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content);
        }

        public void Dispose()
        {
            if (Directory.Exists(RepoRoot))
            {
                Directory.Delete(RepoRoot, recursive: true);
            }
        }
    }
}
