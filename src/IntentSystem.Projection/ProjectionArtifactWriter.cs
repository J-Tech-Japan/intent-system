namespace IntentSystem.Projection;

public static class ProjectionArtifactWriter
{
    public static void Write(GeneratedPacket packet, string repoRoot, bool overwrite)
    {
        ArgumentNullException.ThrowIfNull(packet);
        ArgumentException.ThrowIfNullOrWhiteSpace(repoRoot);

        var writeTargets = CreateWriteTargets(packet, repoRoot);

        if (!overwrite)
        {
            ValidateTargetsDoNotExist(writeTargets);
        }

        foreach (var target in writeTargets)
        {
            var directoryPath = Path.GetDirectoryName(target.Path)
                ?? throw new InvalidOperationException(
                    $"Artifact path '{target.Path}' did not contain a directory.");

            Directory.CreateDirectory(directoryPath);
            File.WriteAllText(target.Path, target.Contents);
        }
    }

    private static IReadOnlyList<ArtifactWriteTarget> CreateWriteTargets(GeneratedPacket packet, string repoRoot)
    {
        return
        [
            new ArtifactWriteTarget(
                ResolveAbsolutePath(repoRoot, packet.Paths.Implementation),
                packet.ImplementationMarkdown),
            new ArtifactWriteTarget(
                ResolveAbsolutePath(repoRoot, packet.Paths.ReviewContext),
                packet.ReviewContextMarkdown),
            new ArtifactWriteTarget(
                ResolveAbsolutePath(repoRoot, packet.Paths.Yaml),
                packet.PacketYaml)
        ];
    }

    private static void ValidateTargetsDoNotExist(IReadOnlyList<ArtifactWriteTarget> writeTargets)
    {
        foreach (var target in writeTargets)
        {
            if (File.Exists(target.Path))
            {
                throw new InvalidOperationException($"Artifact already exists at {target.Path}.");
            }
        }
    }

    private static string ResolveAbsolutePath(string repoRoot, string relativePath)
    {
        return Path.GetFullPath(Path.Combine(repoRoot, relativePath.Replace('/', Path.DirectorySeparatorChar)));
    }

    private sealed record ArtifactWriteTarget(string Path, string Contents);
}
