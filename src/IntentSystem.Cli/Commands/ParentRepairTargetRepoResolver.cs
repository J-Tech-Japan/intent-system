namespace IntentSystem.Cli.Commands;

internal static class ParentRepairTargetRepoResolver
{
    public static string Resolve(CliContext context, IReadOnlyList<string> parentRepairTargets)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(parentRepairTargets);

        if (parentRepairTargets.Count == 0)
        {
            throw new InvalidOperationException("Parent repair targets must contain at least one target.");
        }

        var normalizedTargetPaths = parentRepairTargets
            .Select(NormalizeParentRepairTargetPath)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        var candidates = new List<string>();
        var configuredParentRepoRoot = context.ResolveParentIntentRepoRootPath();
        if (!string.IsNullOrWhiteSpace(configuredParentRepoRoot))
        {
            candidates.Add(configuredParentRepoRoot);
        }

        var repoParentDirectory = Directory.GetParent(context.RepoRoot);
        if (repoParentDirectory is not null)
        {
            candidates.AddRange(
                repoParentDirectory.EnumerateDirectories()
                    .Select(directory => directory.FullName)
                    .Where(path => !string.Equals(path, context.RepoRoot, StringComparison.Ordinal)));
        }

        var matchingRoots = candidates
            .Distinct(StringComparer.Ordinal)
            .Where(Directory.Exists)
            .Where(candidate => normalizedTargetPaths.All(
                target => File.Exists(Path.Combine(candidate, target.Replace('/', Path.DirectorySeparatorChar)))))
            .ToArray();

        return matchingRoots.Length switch
        {
            1 => matchingRoots[0],
            0 => throw new InvalidOperationException("Current parent repo root could not be resolved from parent repair targets."),
            _ => throw new InvalidOperationException(
                $"Parent repair targets resolved to multiple candidate parent repo roots: {string.Join(", ", matchingRoots)}")
        };
    }

    private static string NormalizeParentRepairTargetPath(string target)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(target);

        var separatorIndex = target.IndexOf(':');
        if (separatorIndex < 0 || separatorIndex == target.Length - 1)
        {
            throw new InvalidOperationException($"Parent repair target '{target}' must use the kind:path shape.");
        }

        return target[(separatorIndex + 1)..].Trim();
    }
}
