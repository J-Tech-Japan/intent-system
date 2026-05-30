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
        // Smoke-test: the actual eng/version.json in the repository must
        // be parseable and must point to 0.3.4 as the next development line
        // (post-v0.3.3 release bump, see G442).
        var repoRoot = FindRepoRoot();
        if (repoRoot is null)
        {
            return; // Running outside a checkout — skip.
        }

        var policy = VersionPolicy.TryReadFromRepo(repoRoot);

        Assert.NotNull(policy);
        Assert.Equal("0.3.4", policy.NextVersion);
        Assert.Equal("0.3.3", policy.StableVersion);
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
