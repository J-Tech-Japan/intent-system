namespace IntentSystem.Projection.Tests;

public sealed class ProjectionArtifactWriterTests
{
    [Fact]
    public void Write_GivenGeneratedPacket_WritesAllArtifactsUnderRepoRoot()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        var packet = ProjectionTestData.CreatePacket();

        ProjectionArtifactWriter.Write(packet, repoRoot, overwrite: false);

        Assert.Equal(
            packet.ImplementationMarkdown,
            File.ReadAllText(Path.Combine(repoRoot, ".intent-cli", "issues", "G2", "implementation.md")));
        Assert.Equal(
            packet.ReviewContextMarkdown,
            File.ReadAllText(Path.Combine(repoRoot, ".intent-cli", "issues", "G2", "review-context.md")));
        Assert.Equal(
            packet.PacketYaml,
            File.ReadAllText(Path.Combine(repoRoot, ".intent-cli", "issues", "G2", "packet.yaml")));
    }

    [Fact]
    public void Write_GivenExistingArtifactAndOverwriteFalse_ThrowsInvalidOperationExceptionWithoutPartialWrite()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        var packet = ProjectionTestData.CreatePacket();
        var implementationPath = tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "issues", "G2", "implementation.md"),
            "existing implementation");

        var exception = Assert.Throws<InvalidOperationException>(
            () => ProjectionArtifactWriter.Write(packet, repoRoot, overwrite: false));

        Assert.Contains("already exists", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("existing implementation", File.ReadAllText(implementationPath));
        Assert.False(File.Exists(Path.Combine(repoRoot, ".intent-cli", "issues", "G2", "review-context.md")));
        Assert.False(File.Exists(Path.Combine(repoRoot, ".intent-cli", "issues", "G2", "packet.yaml")));
    }

    [Fact]
    public void Write_GivenExistingArtifactsAndOverwriteTrue_ReplacesAllArtifacts()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        var packet = ProjectionTestData.CreatePacket();
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "issues", "G2", "implementation.md"),
            "stale implementation");
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "issues", "G2", "review-context.md"),
            "stale review context");
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "issues", "G2", "packet.yaml"),
            "stale packet yaml");

        ProjectionArtifactWriter.Write(packet, repoRoot, overwrite: true);

        Assert.Equal(
            packet.ImplementationMarkdown,
            File.ReadAllText(Path.Combine(repoRoot, ".intent-cli", "issues", "G2", "implementation.md")));
        Assert.Equal(
            packet.ReviewContextMarkdown,
            File.ReadAllText(Path.Combine(repoRoot, ".intent-cli", "issues", "G2", "review-context.md")));
        Assert.Equal(
            packet.PacketYaml,
            File.ReadAllText(Path.Combine(repoRoot, ".intent-cli", "issues", "G2", "packet.yaml")));
    }

    [Fact]
    public void Write_GivenExistingPacketYamlAndAllowExistingPacketYaml_OverwritesOnlyPacketYaml()
    {
        using var tempDirectory = new TemporaryDirectory();
        var repoRoot = tempDirectory.CreateDirectory("repo");
        var packet = ProjectionTestData.CreatePacket();
        tempDirectory.CreateFile(
            Path.Combine("repo", ".intent-cli", "issues", "G2", "packet.yaml"),
            "stale packet yaml");

        ProjectionArtifactWriter.Write(
            packet,
            repoRoot,
            overwrite: false,
            allowExistingPacketYaml: true);

        Assert.Equal(
            packet.ImplementationMarkdown,
            File.ReadAllText(Path.Combine(repoRoot, ".intent-cli", "issues", "G2", "implementation.md")));
        Assert.Equal(
            packet.ReviewContextMarkdown,
            File.ReadAllText(Path.Combine(repoRoot, ".intent-cli", "issues", "G2", "review-context.md")));
        Assert.Equal(
            packet.PacketYaml,
            File.ReadAllText(Path.Combine(repoRoot, ".intent-cli", "issues", "G2", "packet.yaml")));
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        private readonly string rootPath = Directory.CreateTempSubdirectory("intent-cli-projection-writer-tests-").FullName;

        public string CreateDirectory(string relativePath)
        {
            var fullPath = Path.Combine(rootPath, relativePath);
            Directory.CreateDirectory(fullPath);
            return fullPath;
        }

        public string CreateFile(string relativePath, string contents)
        {
            var fullPath = Path.Combine(rootPath, relativePath);
            var directoryPath = Path.GetDirectoryName(fullPath)
                ?? throw new InvalidOperationException("Temporary file path did not contain a directory.");

            Directory.CreateDirectory(directoryPath);
            File.WriteAllText(fullPath, contents);
            return fullPath;
        }

        public void Dispose()
        {
            if (Directory.Exists(rootPath))
            {
                Directory.Delete(rootPath, recursive: true);
            }
        }
    }
}
