using System.Text.Json;
using IntentSystem.Cli.Infrastructure;

namespace IntentSystem.Cli.Tests;

/// <summary>
/// G401: unit coverage for <see cref="VersionPolicy"/> — parsing
/// <c>eng/version.json</c> and deriving preview package versions.
/// </summary>
public sealed class VersionPolicyTests : IDisposable
{
    private readonly string _tempDir;

    public VersionPolicyTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"VersionPolicyTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    [Fact]
    public void TryReadFromFile_ValidPolicy_ReturnsCorrectFields()
    {
        var filePath = Path.Combine(_tempDir, "version.json");
        File.WriteAllText(filePath, """
            {
              "stableVersion": "0.2.0",
              "nextVersion": "0.3.0"
            }
            """);

        var policy = VersionPolicy.TryReadFromFile(filePath);

        Assert.NotNull(policy);
        Assert.Equal("0.2.0", policy.StableVersion);
        Assert.Equal("0.3.0", policy.NextVersion);
    }

    [Fact]
    public void TryReadFromFile_MissingFile_ReturnsNull()
    {
        var policy = VersionPolicy.TryReadFromFile(Path.Combine(_tempDir, "nonexistent.json"));
        Assert.Null(policy);
    }

    [Fact]
    public void TryReadFromFile_MalformedJson_ReturnsNull()
    {
        var filePath = Path.Combine(_tempDir, "version.json");
        File.WriteAllText(filePath, "{ not valid json }");

        var policy = VersionPolicy.TryReadFromFile(filePath);
        Assert.Null(policy);
    }

    [Fact]
    public void TryReadFromFile_MissingNextVersionField_ReturnsNull()
    {
        var filePath = Path.Combine(_tempDir, "version.json");
        File.WriteAllText(filePath, """{"stableVersion": "0.2.0"}""");

        var policy = VersionPolicy.TryReadFromFile(filePath);
        Assert.Null(policy);
    }

    [Fact]
    public void TryReadFromRepo_ValidEngVersionJson_ReturnsPolicy()
    {
        // Write eng/version.json relative to a fake repo root.
        var repoRoot = Path.Combine(_tempDir, "repo");
        var engDir = Path.Combine(repoRoot, "eng");
        Directory.CreateDirectory(engDir);
        File.WriteAllText(Path.Combine(engDir, "version.json"), """
            {
              "stableVersion": "0.2.0",
              "nextVersion": "0.3.0"
            }
            """);

        var policy = VersionPolicy.TryReadFromRepo(repoRoot);

        Assert.NotNull(policy);
        Assert.Equal("0.3.0", policy.NextVersion);
    }

    [Fact]
    public void TryReadFromRepo_MissingEngDir_ReturnsNull()
    {
        var repoRoot = Path.Combine(_tempDir, "repo-no-eng");
        Directory.CreateDirectory(repoRoot);

        var policy = VersionPolicy.TryReadFromRepo(repoRoot);
        Assert.Null(policy);
    }

    [Fact]
    public void DerivePreviewPackageVersion_ProducesCorrectFormat()
    {
        var policy = new VersionPolicy { StableVersion = "0.2.0", NextVersion = "0.3.0" };

        var version = policy.DerivePreviewPackageVersion("42", "1");

        Assert.Equal("0.3.0-preview.42.1", version);
    }

    [Fact]
    public void DerivePreviewPackageVersion_DifferentNextVersion_ProducesCorrectFormat()
    {
        var policy = new VersionPolicy { StableVersion = "0.3.0", NextVersion = "0.4.0" };

        var version = policy.DerivePreviewPackageVersion("100", "2");

        Assert.Equal("0.4.0-preview.100.2", version);
    }

    [Fact]
    public void EngVersionJson_InThisRepo_IsReadableAndHasExpectedNextVersion()
    {
        // G557: this smoke test used to hardcode the stable/next pair. The
        // first live execution of the G554 post-release roll (commit 00936844,
        // nextVersion 0.6.1 -> 0.6.2) broke it — along with two peers — and
        // turned child main red, freezing an unrelated PR that inherited it.
        // A literal pair is the wrong assertion for a field a REQUIRED
        // recurring step is supposed to change. What must hold across every
        // roll is that the policy parses and names a release-to-be-cut
        // strictly ahead of the published stable, so the expectation is
        // derived from eng/version.json itself.
        var policy = RepoVersionPolicySource.Read();

        Assert.False(string.IsNullOrWhiteSpace(policy.StableVersion));
        Assert.False(string.IsNullOrWhiteSpace(policy.NextVersion));
        RepoVersionPolicySource.AssertReleaseToBeCutIsAheadOfPublishedStable(policy);
    }

    [Fact]
    public void EngVersionJson_DerivedExpectations_SurviveASimulatedRoll_G557()
    {
        // Roll simulation: a bumped version.json must keep the derived
        // assertions green. This is the regression the hardcoded literals
        // could not express — it fails only if an expectation is baked in
        // again.
        using var temp = new TemporaryRepoRoot();
        temp.WriteVersionPolicy(stableVersion: "0.6.1", nextVersion: "0.6.2");
        RepoVersionPolicySource.AssertReleaseToBeCutIsAheadOfPublishedStable(
            RepoVersionPolicySource.ReadFrom(temp.RootPath));

        // …and again after the NEXT roll, and across a minor and a major line.
        foreach (var (stable, next) in new[] { ("0.6.2", "0.6.3"), ("0.6.9", "0.7.0"), ("0.9.9", "1.0.0") })
        {
            temp.WriteVersionPolicy(stable, next);
            var rolled = RepoVersionPolicySource.ReadFrom(temp.RootPath);
            Assert.Equal(stable, rolled.StableVersion);
            Assert.Equal(next, rolled.NextVersion);
            RepoVersionPolicySource.AssertReleaseToBeCutIsAheadOfPublishedStable(rolled);
        }
    }

    private sealed class TemporaryRepoRoot : IDisposable
    {
        public TemporaryRepoRoot()
        {
            RootPath = Directory.CreateTempSubdirectory("version-policy-roll-simulation-").FullName;
            Directory.CreateDirectory(Path.Combine(RootPath, "eng"));
        }

        public string RootPath { get; }

        public void WriteVersionPolicy(string stableVersion, string nextVersion) =>
            File.WriteAllText(
                Path.Combine(RootPath, "eng", "version.json"),
                $$"""
                {
                  "stableVersion": "{{stableVersion}}",
                  "nextVersion": "{{nextVersion}}"
                }
                """);

        public void Dispose()
        {
            if (Directory.Exists(RootPath))
            {
                Directory.Delete(RootPath, recursive: true);
            }
        }
    }

    private static string? FindRepoRoot()
    {
        var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "eng", "version.json")))
            {
                return dir.FullName;
            }
            dir = dir.Parent;
        }
        return null;
    }
}
